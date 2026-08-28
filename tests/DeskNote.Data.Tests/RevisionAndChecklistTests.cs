using Dapper;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Data.Tests;

public class RevisionAndChecklistTests
{
    private static Note NewNote(string content = "") => new()
    {
        Id = Guid.CreateVersion7(),
        Content = content,
    };

    private const string Checklist = """
        API 응답속도 개선

        - [x] p95 기준 확정
        - [ ] GPU fallback 테스트
        - [ ] 릴리스 노트 작성
        """;

    private static async Task<IReadOnlyList<(int Ordinal, string Text, long IsDone)>> ReadItemsAsync(TestDatabase db, Guid noteId)
    {
        await using var connection = await db.Factory.OpenAsync();
        var rows = await connection.QueryAsync<(int, string, long)>(
            "SELECT ordinal, text, is_done FROM checklist_items WHERE note_id = @id ORDER BY ordinal;",
            new { id = noteId.ToString() });
        return rows.ToList();
    }

    [Fact]
    public async Task Saving_a_note_projects_its_checklist_items()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote();
        await db.Notes.AddAsync(note);

        await db.Notes.UpdateContentAsync(note.Id, "Project Alpha", Checklist);
        var items = await ReadItemsAsync(db, note.Id);

        Assert.Equal(3, items.Count);
        Assert.Equal("p95 기준 확정", items[0].Text);
        Assert.Equal(1, items[0].IsDone);
        Assert.Equal("GPU fallback 테스트", items[1].Text);
        Assert.Equal(0, items[1].IsDone);
    }

    [Fact]
    public async Task Ticking_an_item_in_the_body_updates_the_projection()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote();
        await db.Notes.AddAsync(note);
        await db.Notes.UpdateContentAsync(note.Id, "", Checklist);

        var ticked = ChecklistParser.SetItemState(Checklist, lineIndex: 3, isDone: true);
        await db.Notes.UpdateContentAsync(note.Id, "", ticked);

        var items = await ReadItemsAsync(db, note.Id);
        Assert.Equal(1, items[1].IsDone);
    }

    [Fact]
    public async Task Removing_the_checklist_clears_the_projection()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote();
        await db.Notes.AddAsync(note);
        await db.Notes.UpdateContentAsync(note.Id, "", Checklist);
        Assert.NotEmpty(await ReadItemsAsync(db, note.Id));

        await db.Notes.UpdateContentAsync(note.Id, "", "체크리스트를 모두 지운 메모");

        Assert.Empty(await ReadItemsAsync(db, note.Id));
    }

    /// <summary>
    /// The projection must never describe a version of the note that is not the stored one, so
    /// both are written together.
    /// </summary>
    [Fact]
    public async Task The_projection_matches_the_stored_body()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote();
        await db.Notes.AddAsync(note);
        await db.Notes.UpdateContentAsync(note.Id, "", Checklist);

        var stored = await db.Notes.GetAsync(note.Id);
        var items = await ReadItemsAsync(db, note.Id);

        Assert.Equal(
            ChecklistParser.Parse(stored!.Content).Select(i => (i.Text, i.IsDone)),
            items.Select(i => (i.Text, i.IsDone == 1)));
    }

    /// <summary>Moving or recolouring a note is not a content change and must not touch the projection.</summary>
    [Fact]
    public async Task Moving_a_note_leaves_its_checklist_projection_alone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote();
        await db.Notes.AddAsync(note);
        await db.Notes.UpdateContentAsync(note.Id, "", Checklist);

        await db.Notes.UpdateGeometryAsync(
            note.Id,
            new NoteGeometry(50, 60, 320, 260, @"\\.\DISPLAY1", NoteSizePreset.Medium));

        Assert.Equal(3, (await ReadItemsAsync(db, note.Id)).Count);
    }

    [Fact]
    public async Task Deleting_a_note_removes_its_checklist_rows()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote();
        await db.Notes.AddAsync(note);
        await db.Notes.UpdateContentAsync(note.Id, "", Checklist);

        await db.Notes.SoftDeleteAsync(note.Id);
        await db.Notes.PurgeAsync(note.Id);

        Assert.Empty(await ReadItemsAsync(db, note.Id));
    }

    [Fact]
    public async Task Revisions_come_back_newest_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var store = new SqliteNoteRevisionStore(db.Factory);
        var note = NewNote();
        await db.Notes.AddAsync(note);

        var start = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 3; i++)
        {
            await store.AddAsync(RevisionPolicy.CreateRevision(note.Id, $"버전 {i}", start.AddMinutes(i)));
        }

        var revisions = await store.ListAsync(note.Id);

        Assert.Equal(3, revisions.Count);
        Assert.Equal("버전 2", revisions[0].Content);
        Assert.Equal("버전 0", revisions[2].Content);
        Assert.Equal("버전 2", (await store.GetLatestAsync(note.Id))!.Content);
    }

    [Fact]
    public async Task An_ai_revision_records_which_action_produced_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var store = new SqliteNoteRevisionStore(db.Factory);
        var note = NewNote();
        await db.Notes.AddAsync(note);

        await store.AddAsync(RevisionPolicy.CreateRevision(
            note.Id, "AI가 바꾸기 전 원문", DateTimeOffset.UtcNow, RevisionSource.Ai, "요약"));

        var latest = await store.GetLatestAsync(note.Id);

        Assert.Equal(RevisionSource.Ai, latest!.Source);
        Assert.Equal("요약", latest.ActionName);
        Assert.Equal("AI가 바꾸기 전 원문", latest.Content);
    }

    [Fact]
    public async Task Pruning_keeps_the_newest_revisions()
    {
        await using var db = await TestDatabase.CreateAsync();
        var store = new SqliteNoteRevisionStore(db.Factory);
        var note = NewNote();
        await db.Notes.AddAsync(note);

        var start = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 10; i++)
        {
            await store.AddAsync(RevisionPolicy.CreateRevision(note.Id, $"버전 {i}", start.AddMinutes(i)));
        }

        var removed = await store.PruneAsync(note.Id, keep: 3);
        var remaining = await store.ListAsync(note.Id);

        Assert.Equal(7, removed);
        Assert.Equal(3, remaining.Count);
        Assert.Equal(["버전 9", "버전 8", "버전 7"], remaining.Select(r => r.Content));
    }

    [Fact]
    public async Task Revisions_are_line_ending_normalized_like_note_content()
    {
        await using var db = await TestDatabase.CreateAsync();
        var store = new SqliteNoteRevisionStore(db.Factory);
        var note = NewNote();
        await db.Notes.AddAsync(note);

        await store.AddAsync(RevisionPolicy.CreateRevision(note.Id, "첫 줄\r둘째 줄", DateTimeOffset.UtcNow));

        Assert.Equal("첫 줄\n둘째 줄", (await store.GetLatestAsync(note.Id))!.Content);
    }

    [Fact]
    public async Task Deleting_a_note_removes_its_revisions()
    {
        await using var db = await TestDatabase.CreateAsync();
        var store = new SqliteNoteRevisionStore(db.Factory);
        var note = NewNote();
        await db.Notes.AddAsync(note);
        await store.AddAsync(RevisionPolicy.CreateRevision(note.Id, "본문", DateTimeOffset.UtcNow));

        await db.Notes.SoftDeleteAsync(note.Id);
        await db.Notes.PurgeAsync(note.Id);

        Assert.Empty(await store.ListAsync(note.Id));
    }
}
