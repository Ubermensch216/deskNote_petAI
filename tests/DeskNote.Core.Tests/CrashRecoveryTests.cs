using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class CrashRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "desknote-journal", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private CrashJournal NewJournal() => new(_directory);

    private static Note StoredNote(Guid id, string title, string content) => new()
    {
        Id = id,
        Title = title,
        Content = content,
    };

    [Fact]
    public void An_entry_round_trips_through_the_journal()
    {
        var journal = NewJournal();
        var id = Guid.NewGuid();

        journal.Record(id, "Project Alpha", "수도관 누수 점검\nAPI 응답속도 개선");
        var entries = journal.ReadAll();

        Assert.Single(entries);
        Assert.Equal(id, entries[0].NoteId);
        Assert.Equal("Project Alpha", entries[0].Title);
        Assert.Equal("수도관 누수 점검\nAPI 응답속도 개선", entries[0].Content);
    }

    [Fact]
    public void Recording_again_replaces_the_previous_entry()
    {
        var journal = NewJournal();
        var id = Guid.NewGuid();

        journal.Record(id, "", "첫 번째");
        journal.Record(id, "", "두 번째");

        Assert.Single(journal.ReadAll());
        Assert.Equal("두 번째", journal.ReadAll()[0].Content);
    }

    /// <summary>A completed save clears its entry, which is what makes a leftover entry meaningful.</summary>
    [Fact]
    public void Clearing_removes_only_that_note_s_entry()
    {
        var journal = NewJournal();
        var kept = Guid.NewGuid();
        var cleared = Guid.NewGuid();

        journal.Record(kept, "", "남는 메모");
        journal.Record(cleared, "", "저장된 메모");
        journal.Clear(cleared);

        Assert.Single(journal.ReadAll());
        Assert.Equal(kept, journal.ReadAll()[0].NoteId);
    }

    [Fact]
    public void Reading_an_empty_or_missing_directory_is_not_an_error()
    {
        Assert.Empty(NewJournal().ReadAll());
    }

    [Fact]
    public void Clearing_a_note_that_was_never_journalled_is_not_an_error()
    {
        NewJournal().Clear(Guid.NewGuid());
    }

    /// <summary>
    /// A file truncated by a crash must not stop the app starting, nor block recovery of the
    /// notes whose entries are intact.
    /// </summary>
    [Fact]
    public void A_corrupt_entry_is_skipped_without_losing_the_others()
    {
        var journal = NewJournal();
        var good = Guid.NewGuid();
        journal.Record(good, "", "읽을 수 있는 메모");
        File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ this is not json");

        var entries = journal.ReadAll();

        Assert.Single(entries);
        Assert.Equal(good, entries[0].NoteId);
    }

    [Fact]
    public void An_entry_matching_the_stored_note_is_not_offered_for_recovery()
    {
        var id = Guid.NewGuid();
        var entries = new List<JournalEntry> { new(id, "제목", "본문", DateTimeOffset.UtcNow) };
        var stored = new Dictionary<Guid, Note> { [id] = StoredNote(id, "제목", "본문") };

        Assert.Empty(CrashRecovery.FindUnsavedWork(entries, stored));
    }

    [Fact]
    public void An_entry_newer_than_the_stored_note_is_offered_for_recovery()
    {
        var id = Guid.NewGuid();
        var entries = new List<JournalEntry> { new(id, "제목", "저장되지 못한 본문", DateTimeOffset.UtcNow) };
        var stored = new Dictionary<Guid, Note> { [id] = StoredNote(id, "제목", "예전 본문") };

        var recoverable = CrashRecovery.FindUnsavedWork(entries, stored);

        Assert.Single(recoverable);
        Assert.Equal("예전 본문", recoverable[0].StoredContent);
        Assert.Equal("저장되지 못한 본문", recoverable[0].RecoveredContent);
    }

    [Fact]
    public void A_title_that_differs_is_enough_to_offer_recovery()
    {
        var id = Guid.NewGuid();
        var entries = new List<JournalEntry> { new(id, "새 제목", "본문", DateTimeOffset.UtcNow) };
        var stored = new Dictionary<Guid, Note> { [id] = StoredNote(id, "옛 제목", "본문") };

        Assert.Single(CrashRecovery.FindUnsavedWork(entries, stored));
    }

    /// <summary>Recovery must not bring back something the user deliberately got rid of.</summary>
    [Fact]
    public void Entries_for_deleted_notes_are_discarded()
    {
        var id = Guid.NewGuid();
        var entries = new List<JournalEntry> { new(id, "", "지운 메모의 잔재", DateTimeOffset.UtcNow) };
        var stored = new Dictionary<Guid, Note>
        {
            [id] = StoredNote(id, "", "지운 메모") with { DeletedAt = DateTimeOffset.UtcNow },
        };

        Assert.Empty(CrashRecovery.FindUnsavedWork(entries, stored));
    }

    [Fact]
    public void Entries_for_notes_that_no_longer_exist_are_discarded()
    {
        var entries = new List<JournalEntry> { new(Guid.NewGuid(), "", "고아 항목", DateTimeOffset.UtcNow) };

        Assert.Empty(CrashRecovery.FindUnsavedWork(entries, new Dictionary<Guid, Note>()));
    }

    /// <summary>
    /// The journal stores whatever the editor handed over, which uses CR line breaks. Comparing
    /// that against normalized stored content without normalizing first would report every note
    /// with more than one line as needing recovery.
    /// </summary>
    [Fact]
    public void Editor_line_endings_do_not_produce_a_false_recovery_prompt()
    {
        var id = Guid.NewGuid();
        var entries = new List<JournalEntry> { new(id, "", "첫 줄\r둘째 줄", DateTimeOffset.UtcNow) };
        var stored = new Dictionary<Guid, Note> { [id] = StoredNote(id, "", "첫 줄\n둘째 줄") };

        Assert.Empty(CrashRecovery.FindUnsavedWork(entries, stored));
    }

    [Fact]
    public void The_most_recent_loss_is_listed_first()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var entries = new List<JournalEntry>
        {
            new(older, "", "먼저", now.AddMinutes(-10)),
            new(newer, "", "나중", now),
        };
        var stored = new Dictionary<Guid, Note>
        {
            [older] = StoredNote(older, "", ""),
            [newer] = StoredNote(newer, "", ""),
        };

        var recoverable = CrashRecovery.FindUnsavedWork(entries, stored);

        Assert.Equal(newer, recoverable[0].NoteId);
    }

    /// <summary>End to end: a save that throws leaves exactly the entry recovery needs.</summary>
    [Fact]
    public async Task A_failed_save_leaves_recoverable_work_behind()
    {
        var journal = NewJournal();
        var id = Guid.NewGuid();

        await using var scheduler = new AutosaveScheduler(
            (_, _, _, _) => throw new IOException("database is locked"),
            TimeSpan.FromMilliseconds(30),
            journal);

        scheduler.Schedule(id, "제목", "잃으면 안 되는 본문");
        await Task.Delay(400);

        var stored = new Dictionary<Guid, Note> { [id] = StoredNote(id, "", "") };
        var recoverable = CrashRecovery.FindUnsavedWork(journal.ReadAll(), stored);

        Assert.Single(recoverable);
        Assert.Equal("잃으면 안 되는 본문", recoverable[0].RecoveredContent);
    }

    [Fact]
    public async Task A_successful_save_leaves_nothing_behind()
    {
        var journal = NewJournal();
        var id = Guid.NewGuid();

        await using var scheduler = new AutosaveScheduler(
            (_, _, _, _) => Task.CompletedTask,
            TimeSpan.FromMilliseconds(30),
            journal);

        scheduler.Schedule(id, "제목", "정상 저장되는 본문");
        await Task.Delay(400);

        Assert.Empty(journal.ReadAll());
    }
}
