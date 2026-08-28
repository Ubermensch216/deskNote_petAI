using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>A note whose journal entry does not match what was stored.</summary>
/// <param name="NoteId">The note the unsaved text belongs to.</param>
/// <param name="StoredTitle">Title currently in the database.</param>
/// <param name="StoredContent">Content currently in the database.</param>
/// <param name="RecoveredTitle">Title found in the journal.</param>
/// <param name="RecoveredContent">Content found in the journal.</param>
/// <param name="RecordedAt">When the journal entry was written.</param>
public sealed record RecoverableNote(
    Guid NoteId,
    string StoredTitle,
    string StoredContent,
    string RecoveredTitle,
    string RecoveredContent,
    DateTimeOffset RecordedAt);

/// <summary>
/// Decides which journal entries represent work that was actually lost.
/// </summary>
public static class CrashRecovery
{
    /// <summary>
    /// Pairs leftover journal entries against the stored notes and returns only those that differ.
    /// </summary>
    /// <remarks>
    /// Most leftover entries are not losses. A save that succeeded but whose journal delete did
    /// not land leaves an entry identical to the stored note, and prompting for those would train
    /// the user to dismiss the recovery prompt without reading it. Entries for notes that no
    /// longer exist are dropped outright rather than resurrecting something the user deleted.
    /// </remarks>
    public static IReadOnlyList<RecoverableNote> FindUnsavedWork(
        IReadOnlyList<JournalEntry> entries,
        IReadOnlyDictionary<Guid, Note> storedNotes)
    {
        var recoverable = new List<RecoverableNote>();

        foreach (var entry in entries)
        {
            if (!storedNotes.TryGetValue(entry.NoteId, out var stored) || stored.IsDeleted)
            {
                continue;
            }

            var recoveredContent = NoteContent.NormalizeLineEndings(entry.Content);
            if (string.Equals(stored.Content, recoveredContent, StringComparison.Ordinal)
                && string.Equals(stored.Title, entry.Title, StringComparison.Ordinal))
            {
                continue;
            }

            recoverable.Add(new RecoverableNote(
                entry.NoteId,
                stored.Title,
                stored.Content,
                entry.Title,
                recoveredContent,
                entry.RecordedAt));
        }

        return recoverable
            .OrderByDescending(r => r.RecordedAt)
            .ToList();
    }
}
