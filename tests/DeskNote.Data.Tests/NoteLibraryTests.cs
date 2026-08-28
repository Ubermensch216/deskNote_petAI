using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Data.Tests;

public class NoteLibraryTests
{
    private static Note NewNote() => new() { Id = Guid.CreateVersion7() };

    private static async Task<Guid> AddAsync(TestDatabase db, string title, string content)
    {
        var note = NewNote();
        await db.Notes.AddAsync(note);
        await db.Notes.UpdateContentAsync(note.Id, title, content);
        return note.Id;
    }

    private static SqliteNoteLibrary Library(TestDatabase db) => new(db.Factory, db.Clock);

    [Fact]
    public async Task Listing_returns_a_preview_rather_than_the_whole_body()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "Project Alpha", new string('가', 500));

        var rows = await Library(db).ListAsync(NoteQuery.Default);

        Assert.Single(rows);
        Assert.Equal("Project Alpha", rows[0].Title);
        Assert.True(rows[0].Preview.Length <= 160);
    }

    [Fact]
    public async Task Rows_carry_the_tags_from_the_body()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "API 개선\n\n#backend #v1");

        var rows = await Library(db).ListAsync(NoteQuery.Default);

        Assert.Equal(["backend", "v1"], rows[0].Tags.Order());
    }

    [Fact]
    public async Task Rows_carry_checklist_progress()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "- [x] 완료\n- [ ] 미완\n- [ ] 미완2");

        var rows = await Library(db).ListAsync(NoteQuery.Default);

        Assert.Equal(1, rows[0].ChecklistDone);
        Assert.Equal(3, rows[0].ChecklistTotal);
    }

    [Fact]
    public async Task Filtering_by_tag_narrows_the_list()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "백엔드 작업 #backend");
        await AddAsync(db, "", "디자인 작업 #design");

        var rows = await Library(db).ListAsync(NoteQuery.Default with { TagNormalizedName = "backend" });

        Assert.Single(rows);
        Assert.Contains("backend", rows[0].Tags);
    }

    /// <summary>Editing a note's tags out of the body must remove it from that tag's list.</summary>
    [Fact]
    public async Task Removing_a_tag_from_the_body_removes_the_note_from_that_tag()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await AddAsync(db, "", "작업 #backend");
        Assert.Single(await Library(db).ListAsync(NoteQuery.Default with { TagNormalizedName = "backend" }));

        await db.Notes.UpdateContentAsync(id, "", "작업 #frontend");

        Assert.Empty(await Library(db).ListAsync(NoteQuery.Default with { TagNormalizedName = "backend" }));
        Assert.Single(await Library(db).ListAsync(NoteQuery.Default with { TagNormalizedName = "frontend" }));
    }

    [Fact]
    public async Task Tag_usage_counts_live_notes_and_orders_by_popularity()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "하나 #backend");
        await AddAsync(db, "", "둘 #backend");
        await AddAsync(db, "", "셋 #design");

        var tags = await Library(db).ListTagsAsync();

        Assert.Equal("backend", tags[0].Tag.NormalizedName);
        Assert.Equal(2, tags[0].NoteCount);
        Assert.Equal(1, tags[1].NoteCount);
    }

    [Fact]
    public async Task A_deleted_note_stops_counting_towards_its_tags()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await AddAsync(db, "", "작업 #backend");

        await db.Notes.SoftDeleteAsync(id);

        Assert.Empty(await Library(db).ListTagsAsync());
    }

    /// <summary>Different spellings are one tag, and the first one wins as the display name.</summary>
    [Fact]
    public async Task Case_variants_collapse_to_a_single_tag()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "하나 #Backend");
        await AddAsync(db, "", "둘 #backend");

        var tags = await Library(db).ListTagsAsync();

        Assert.Single(tags);
        Assert.Equal("Backend", tags[0].Tag.Name);
        Assert.Equal(2, tags[0].NoteCount);
    }

    [Fact]
    public async Task Search_finds_korean_substrings_and_returns_summaries()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "점검 계획", "내일 수도관 누수 점검 예정 #설비");
        await AddAsync(db, "관계없는 메모", "전혀 다른 내용");

        var rows = await Library(db).SearchAsync("수도관", NoteQuery.Default);

        Assert.Single(rows);
        Assert.Equal("점검 계획", rows[0].Title);
        Assert.Contains("설비", rows[0].Tags);
    }

    [Fact]
    public async Task Search_below_three_characters_still_finds_the_note()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "회의 준비");

        Assert.Single(await Library(db).SearchAsync("회의", NoteQuery.Default));
        Assert.Single(await Library(db).SearchAsync("회", NoteQuery.Default));
    }

    [Fact]
    public async Task Search_and_a_tag_filter_apply_together()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "누수 점검 #설비");
        await AddAsync(db, "", "누수 점검 #문서");

        var rows = await Library(db).SearchAsync(
            "누수", NoteQuery.Default with { TagNormalizedName = "설비" });

        Assert.Single(rows);
    }

    [Fact]
    public async Task Search_excludes_deleted_notes_unless_asked_for_them()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await AddAsync(db, "", "누수 점검");
        await db.Notes.SoftDeleteAsync(id);

        Assert.Empty(await Library(db).SearchAsync("누수", NoteQuery.Default));
        Assert.Single(await Library(db).SearchAsync("누수", NoteQuery.Default with { OnlyDeleted = true }));
    }

    /// <summary>Find-as-you-type sends half-typed FTS operator syntax on every keystroke.</summary>
    [Theory]
    [InlineData("\"")]
    [InlineData("NEAR(")]
    [InlineData("foo AND")]
    [InlineData("*")]
    [InlineData("-회의")]
    public async Task Raw_search_input_never_throws(string text)
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "아무 내용");

        Assert.NotNull(await Library(db).SearchAsync(text, NoteQuery.Default));
    }

    [Fact]
    public async Task An_empty_search_falls_back_to_the_plain_list()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "하나");
        await AddAsync(db, "", "둘");

        Assert.Equal(2, (await Library(db).SearchAsync("   ", NoteQuery.Default)).Count);
    }

    [Fact]
    public async Task Notebooks_round_trip_and_filter_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var library = Library(db);
        var notebook = await library.CreateNotebookAsync("업무");
        var id = await AddAsync(db, "", "업무 메모");
        await db.Notes.SetNotebookAsync(id, notebook.Id);

        Assert.Equal("업무", (await library.ListNotebooksAsync()).Single().Name);
        Assert.Single(await library.ListAsync(NoteQuery.Default with { NotebookId = notebook.Id }));
    }

    [Fact]
    public async Task Renaming_a_notebook_keeps_its_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var library = Library(db);
        var notebook = await library.CreateNotebookAsync("업무");
        var id = await AddAsync(db, "", "메모");
        await db.Notes.SetNotebookAsync(id, notebook.Id);

        await library.RenameNotebookAsync(notebook.Id, "개인");

        Assert.Equal("개인", (await library.ListNotebooksAsync()).Single().Name);
        Assert.Single(await library.ListAsync(NoteQuery.Default with { NotebookId = notebook.Id }));
    }

    /// <summary>
    /// Deleting a folder must never destroy the notes inside it — a notebook organizes work, it
    /// does not own it.
    /// </summary>
    [Fact]
    public async Task Deleting_a_notebook_leaves_its_notes_unfiled_rather_than_gone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var library = Library(db);
        var notebook = await library.CreateNotebookAsync("임시");
        var id = await AddAsync(db, "", "지키고 싶은 메모");
        await db.Notes.SetNotebookAsync(id, notebook.Id);

        await library.DeleteNotebookAsync(notebook.Id);

        var note = await db.Notes.GetAsync(id);
        Assert.NotNull(note);
        Assert.Null(note.NotebookId);
        Assert.Single(await library.ListAsync(NoteQuery.Default));
    }

    [Fact]
    public async Task The_deleted_view_shows_only_deleted_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        await AddAsync(db, "", "살아있는 메모");
        var id = await AddAsync(db, "", "지운 메모");
        await db.Notes.SoftDeleteAsync(id);

        var live = await Library(db).ListAsync(NoteQuery.Default);
        var deleted = await Library(db).ListAsync(NoteQuery.Default with { OnlyDeleted = true });

        Assert.Single(live);
        Assert.Single(deleted);
        Assert.True(deleted[0].IsDeleted);
    }
}
