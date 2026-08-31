using System.Collections.Concurrent;
using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>Why a revision is being considered.</summary>
public enum RevisionReason
{
    /// <summary>Ordinary typing.</summary>
    Edit = 0,

    /// <summary>Something replaced the note wholesale — an AI action, a formatting command, a recovery.</summary>
    BulkReplace = 1,
}

/// <summary>
/// Decides when a note's previous content is worth keeping.
/// </summary>
/// <remarks>
/// Snapshotting on every autosave would store a version every few hundred milliseconds of typing,
/// which is noise the user can never navigate. Snapshotting only on demand would leave nothing to
/// go back to. So ordinary typing checkpoints on an interval, while anything that replaces the
/// note wholesale always checkpoints first — those are the edits a user cannot reconstruct from
/// memory, and the ones report p15 requires be reversible.
/// </remarks>
public sealed class RevisionPolicy(TimeSpan? interval = null, int? keepPerNote = null)
{
    public const int DefaultKeepPerNote = 50;

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastSnapshot = new();

    public TimeSpan Interval { get; } = interval ?? DefaultInterval;

    public int KeepPerNote { get; } = keepPerNote ?? DefaultKeepPerNote;

    /// <summary>
    /// Whether <paramref name="previousContent"/> should be stored before the note is overwritten.
    /// </summary>
    public bool ShouldSnapshot(Guid noteId, string previousContent, DateTimeOffset now, RevisionReason reason)
    {
        // Nothing to go back to.
        if (string.IsNullOrEmpty(previousContent))
        {
            return false;
        }

        if (reason == RevisionReason.BulkReplace)
        {
            return true;
        }

        return !_lastSnapshot.TryGetValue(noteId, out var last) || now - last >= Interval;
    }

    /// <summary>Records that a revision was just stored, restarting the interval for that note.</summary>
    public void MarkSnapshotTaken(Guid noteId, DateTimeOffset now) => _lastSnapshot[noteId] = now;

    /// <summary>Forgets a note, so a closed and reopened note checkpoints on its next edit.</summary>
    public void Forget(Guid noteId) => _lastSnapshot.TryRemove(noteId, out _);

    /// <summary>Builds the revision record for content that is about to be replaced.</summary>
    public static NoteRevision CreateRevision(
        Guid noteId,
        string previousContent,
        DateTimeOffset now,
        RevisionSource source = RevisionSource.User,
        string? actionName = null) => new()
        {
            Id = Guid.CreateVersion7(),
            NoteId = noteId,
            Content = previousContent,
            CreatedAt = now,
            Source = source,
            ActionName = actionName,
        };
}
