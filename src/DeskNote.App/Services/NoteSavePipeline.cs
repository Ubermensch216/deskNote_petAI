using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

/// <summary>
/// The one path note text takes on its way to storage.
/// </summary>
/// <remarks>
/// <para>
/// Autosave, an applied crash recovery and — later — an AI rewrite all write note content, and all
/// three need the same guarantees: the previous version is checkpointed when it is worth keeping,
/// and history stays bounded. Putting that here rather than in each caller is what makes report
/// p15's "AI rewrite → 원본 version 보존" a property of the system instead of a rule each new
/// feature has to remember.
/// </para>
/// </remarks>
public sealed class NoteSavePipeline(
    INoteRepository notes,
    INoteRevisionStore revisions,
    RevisionPolicy policy,
    IClock clock)
{
    public event Action<NoteSaveResult>? Saved;

    /// <summary>Writes note text, checkpointing the previous version first when the policy says so.</summary>
    public async Task<NoteSaveResult> SaveAsync(
        Guid noteId,
        string title,
        string content,
        RevisionReason reason = RevisionReason.Edit,
        RevisionSource source = RevisionSource.User,
        string? actionName = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await notes.GetAsync(noteId, cancellationToken).ConfigureAwait(false);

        var contentChanged = existing is not null
            && !string.Equals(existing.Content, content, StringComparison.Ordinal);

        if (existing is not null && contentChanged)
        {
            await CheckpointAsync(noteId, existing.Content, reason, source, actionName, cancellationToken)
                .ConfigureAwait(false);
        }

        await notes.UpdateContentAsync(noteId, title, content, cancellationToken).ConfigureAwait(false);

        var result = new NoteSaveResult(
            noteId,
            existing?.Content,
            content,
            contentChanged,
            clock.UtcNow,
            source,
            actionName,
            existing?.CreatedAt ?? clock.UtcNow);
        Saved?.Invoke(result);
        return result;
    }

    /// <summary>Forgets a note's checkpoint timer, so reopening it checkpoints on the next edit.</summary>
    public void Forget(Guid noteId) => policy.Forget(noteId);

    private async Task CheckpointAsync(
        Guid noteId,
        string previousContent,
        RevisionReason reason,
        RevisionSource source,
        string? actionName,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!policy.ShouldSnapshot(noteId, previousContent, now, reason))
        {
            return;
        }

        await revisions.AddAsync(
            RevisionPolicy.CreateRevision(noteId, previousContent, now, source, actionName),
            cancellationToken).ConfigureAwait(false);

        policy.MarkSnapshotTaken(noteId, now);
        await revisions.PruneAsync(noteId, policy.KeepPerNote, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The observable result of a note save. Consumers may derive optional activity after persistence
/// succeeds without putting that work on the note transaction's critical path.
/// </summary>
public sealed record NoteSaveResult(
    Guid NoteId,
    string? PreviousContent,
    string CurrentContent,
    bool ContentChanged,
    DateTimeOffset SavedAt,
    RevisionSource Source,
    string? ActionName,
    DateTimeOffset NoteCreatedAt);
