using DeskNote.Core.Models;

namespace DeskNote.Core.Abstractions;

/// <summary>
/// Prior versions of note content.
/// </summary>
/// <remarks>
/// Report p15 requires that an AI rewrite preserve the original, and p5 lists revisions alongside
/// undo/redo. Undo inside the editor only survives while the window is open; a stored revision is
/// what lets a user get yesterday's wording back, and what makes it safe to let a model rewrite a
/// note at all.
/// </remarks>
public interface INoteRevisionStore
{
    Task AddAsync(NoteRevision revision, CancellationToken cancellationToken = default);

    /// <summary>Revisions for a note, newest first.</summary>
    Task<IReadOnlyList<NoteRevision>> ListAsync(
        Guid noteId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<NoteRevision?> GetLatestAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Drops all but the newest <paramref name="keep"/> revisions, returning how many were removed.</summary>
    Task<int> PruneAsync(Guid noteId, int keep, CancellationToken cancellationToken = default);
}
