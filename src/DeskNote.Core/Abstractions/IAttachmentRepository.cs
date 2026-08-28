using DeskNote.Core.Models;

namespace DeskNote.Core.Abstractions;

/// <summary>Persistence for note attachments.</summary>
/// <remarks>
/// Only the record is stored here. The bytes of an embedded attachment live on disk under the
/// app's data directory, and a linked one stays wherever the user put it.
/// </remarks>
public interface IAttachmentRepository
{
    Task AddAsync(Attachment attachment, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attachment>> ListForNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
