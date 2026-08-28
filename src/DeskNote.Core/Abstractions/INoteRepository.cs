using DeskNote.Core.Models;

namespace DeskNote.Core.Abstractions;

/// <summary>
/// Persistence for notes.
/// </summary>
/// <remarks>
/// Content, geometry and appearance are updated through separate methods on purpose. Geometry is
/// written on every drag- and resize-end, and must not bump <see cref="Note.Rev"/>, rewrite the
/// body, or touch the full-text index — otherwise merely dragging a note would churn the FTS
/// table and the sync revision counter.
/// </remarks>
public interface INoteRepository
{
    Task<Note?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notes to re-open on the desktop at startup. Kept separate from <see cref="ListAsync"/>
    /// because it runs on the critical path of the &lt;500 ms restore budget (report p14).
    /// </summary>
    Task<IReadOnlyList<Note>> GetOpenNotesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Note>> ListAsync(NoteQuery query, CancellationToken cancellationToken = default);

    Task<int> CountAsync(NoteQuery query, CancellationToken cancellationToken = default);

    Task AddAsync(Note note, CancellationToken cancellationToken = default);

    /// <summary>Writes body text and bumps <see cref="Note.Rev"/> and <see cref="Note.UpdatedAt"/>.</summary>
    Task UpdateContentAsync(Guid id, string title, string content, CancellationToken cancellationToken = default);

    /// <summary>Writes window position and size only. Does not bump the revision counter.</summary>
    Task UpdateGeometryAsync(Guid id, NoteGeometry geometry, CancellationToken cancellationToken = default);

    /// <summary>Writes color, surface opacity and always-on-top. Does not bump the revision counter.</summary>
    Task UpdateAppearanceAsync(
        Guid id,
        string colorKey,
        double opacity,
        bool alwaysOnTop,
        CancellationToken cancellationToken = default);

    /// <summary>Shows or hides the note's desktop window without deleting it.</summary>
    Task SetOpenAsync(Guid id, bool isOpen, CancellationToken cancellationToken = default);

    Task SetNotebookAsync(Guid id, Guid? notebookId, CancellationToken cancellationToken = default);

    /// <summary>Moves the note to the deleted-notes view. Recoverable.</summary>
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Irreversibly removes a soft-deleted note and its children. Requires user confirmation upstream.</summary>
    Task PurgeAsync(Guid id, CancellationToken cancellationToken = default);
}
