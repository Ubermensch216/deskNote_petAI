using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Data.Tests;

public class NoteRepositoryTests
{
    private static Note NewNote(string content = "", string title = "") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Content = content,
        ColorKey = NoteColors.Teal,
        Opacity = 0.9,
        AlwaysOnTop = true,
        Geometry = new NoteGeometry(120, 240, 420, 360, @"\\.\DISPLAY1", NoteSizePreset.Large),
    };

    [Fact]
    public async Task Add_then_get_round_trips_every_field()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("회의 메모", "Project Alpha");

        await db.Notes.AddAsync(note);
        var loaded = await db.Notes.GetAsync(note.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Project Alpha", loaded.Title);
        Assert.Equal("회의 메모", loaded.Content);
        Assert.Equal(NoteColors.Teal, loaded.ColorKey);
        Assert.Equal(0.9, loaded.Opacity, precision: 6);
        Assert.True(loaded.AlwaysOnTop);
        Assert.True(loaded.IsOpen);
        Assert.Equal(note.Geometry, loaded.Geometry);
        Assert.Equal(1, loaded.Rev);
    }

    [Fact]
    public async Task Updating_content_bumps_the_revision()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("초안");
        await db.Notes.AddAsync(note);

        await db.Notes.UpdateContentAsync(note.Id, "제목", "수정된 내용");
        var loaded = await db.Notes.GetAsync(note.Id);

        Assert.Equal("수정된 내용", loaded!.Content);
        Assert.Equal(2, loaded.Rev);
    }

    /// <summary>
    /// Guards the split in <see cref="INoteRepository"/>: moving a note happens on every drag and
    /// must not look like a content edit to sync or to the "recently updated" ordering.
    /// </summary>
    [Fact]
    public async Task Updating_geometry_leaves_the_revision_and_timestamp_untouched()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("고정 내용");
        await db.Notes.AddAsync(note);
        var before = await db.Notes.GetAsync(note.Id);

        await db.Notes.UpdateGeometryAsync(
            note.Id,
            new NoteGeometry(900, 100, 240, 180, @"\\.\DISPLAY2", NoteSizePreset.Small));
        var after = await db.Notes.GetAsync(note.Id);

        Assert.Equal(before!.Rev, after!.Rev);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(900, after.Geometry.X);
        Assert.Equal(NoteSizePreset.Small, after.Geometry.Preset);
        Assert.Equal(@"\\.\DISPLAY2", after.Geometry.MonitorKey);
    }

    [Fact]
    public async Task Opacity_is_clamped_so_note_text_stays_readable()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("대비 유지");
        await db.Notes.AddAsync(note);

        await db.Notes.UpdateAppearanceAsync(note.Id, NoteColors.Blue, opacity: 0.0, alwaysOnTop: false);
        var loaded = await db.Notes.GetAsync(note.Id);

        Assert.Equal(Note.MinOpacity, loaded!.Opacity, precision: 6);
    }

    [Fact]
    public async Task Unknown_color_from_a_newer_version_falls_back_instead_of_throwing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("색상") with { ColorKey = "chartreuse-from-the-future" };

        await db.Notes.AddAsync(note);
        var loaded = await db.Notes.GetAsync(note.Id);

        Assert.Equal(NoteColors.Default, loaded!.ColorKey);
    }

    [Fact]
    public async Task Startup_restore_returns_only_open_undeleted_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var open = NewNote("열린 메모");
        var closed = NewNote("닫힌 메모");
        var deleted = NewNote("삭제된 메모");

        await db.Notes.AddAsync(open);
        await db.Notes.AddAsync(closed);
        await db.Notes.AddAsync(deleted);
        await db.Notes.SetOpenAsync(closed.Id, isOpen: false);
        await db.Notes.SoftDeleteAsync(deleted.Id);

        var restored = await db.Notes.GetOpenNotesAsync();

        Assert.Single(restored);
        Assert.Equal(open.Id, restored[0].Id);
    }

    [Fact]
    public async Task Soft_delete_hides_the_note_and_closes_its_window()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("지울 메모");
        await db.Notes.AddAsync(note);

        await db.Notes.SoftDeleteAsync(note.Id);

        Assert.Empty(await db.Notes.ListAsync(NoteQuery.Default));
        var deleted = await db.Notes.ListAsync(NoteQuery.Default with { OnlyDeleted = true });
        Assert.Single(deleted);
        Assert.False(deleted[0].IsOpen);
        Assert.True(deleted[0].IsDeleted);
    }

    [Fact]
    public async Task Restore_brings_a_deleted_note_back_to_the_live_list()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("복구할 메모");
        await db.Notes.AddAsync(note);
        await db.Notes.SoftDeleteAsync(note.Id);

        await db.Notes.RestoreAsync(note.Id);

        Assert.Single(await db.Notes.ListAsync(NoteQuery.Default));
    }

    /// <summary>
    /// Deleting is immediate: the note goes, whether or not it ever passed through a deleted view.
    /// </summary>
    /// <remarks>
    /// It used to refuse a live note, because deletion was two-stage and the guard was what stopped
    /// a stray call from destroying work. The app now deletes on the spot at the user's request, so
    /// the guard would refuse every real deletion; the confirmation dialog is what protects the
    /// note instead.
    /// </remarks>
    [Fact]
    public async Task Purge_removes_a_live_note_outright()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("영구 삭제");
        await db.Notes.AddAsync(note);

        await db.Notes.PurgeAsync(note.Id);
        Assert.Null(await db.Notes.GetAsync(note.Id));

        var soft = NewNote("거쳐 간 메모");
        await db.Notes.AddAsync(soft);
        await db.Notes.SoftDeleteAsync(soft.Id);
        await db.Notes.PurgeAsync(soft.Id);
        Assert.Null(await db.Notes.GetAsync(soft.Id));
    }

    [Fact]
    public async Task Counting_and_paging_agree_with_the_same_filter()
    {
        await using var db = await TestDatabase.CreateAsync();
        for (var i = 0; i < 25; i++)
        {
            await db.Notes.AddAsync(NewNote($"메모 {i}"));
        }

        var total = await db.Notes.CountAsync(NoteQuery.Default);
        var firstPage = await db.Notes.ListAsync(NoteQuery.Default with { Limit = 10 });
        var secondPage = await db.Notes.ListAsync(NoteQuery.Default with { Limit = 10, Offset = 10 });

        Assert.Equal(25, total);
        Assert.Equal(10, firstPage.Count);
        Assert.Equal(10, secondPage.Count);
        Assert.Empty(firstPage.Select(n => n.Id).Intersect(secondPage.Select(n => n.Id)));
    }

    [Fact]
    public async Task Settings_round_trip_and_overwrite()
    {
        await using var db = await TestDatabase.CreateAsync();

        await db.Settings.SetAsync(SettingKeys.UiLocale, "ko-KR");
        Assert.Equal("ko-KR", await db.Settings.GetAsync(SettingKeys.UiLocale));

        await db.Settings.SetAsync(SettingKeys.UiLocale, "en-US");
        Assert.Equal("en-US", await db.Settings.GetAsync(SettingKeys.UiLocale));

        await db.Settings.RemoveAsync(SettingKeys.UiLocale);
        Assert.Null(await db.Settings.GetAsync(SettingKeys.UiLocale));
    }
}
