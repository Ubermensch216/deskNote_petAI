using Dapper;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Data;

/// <inheritdoc cref="IAttachmentRepository"/>
public sealed class SqliteAttachmentRepository(SqliteConnectionFactory connectionFactory) : IAttachmentRepository
{
    public async Task AddAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO attachments (id, note_id, kind, path, display_name, size_bytes, sha256, created_at)
            VALUES (@id, @noteId, @kind, @path, @displayName, @sizeBytes, @sha256, @createdAt);
            """,
            new
            {
                id = attachment.Id.ToString(),
                noteId = attachment.NoteId.ToString(),
                kind = (int)attachment.Kind,
                path = attachment.Path,
                displayName = attachment.DisplayName,
                sizeBytes = attachment.SizeBytes,
                sha256 = attachment.Sha256,
                createdAt = SqliteTime.ToDb(attachment.CreatedAt),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Attachment>> ListForNoteAsync(
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<AttachmentRow>(new CommandDefinition(
            """
            SELECT id AS Id, note_id AS NoteId, kind AS Kind, path AS Path,
                   display_name AS DisplayName, size_bytes AS SizeBytes,
                   sha256 AS Sha256, created_at AS CreatedAt
            FROM attachments WHERE note_id = @noteId ORDER BY created_at ASC;
            """,
            new { noteId = noteId.ToString() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToAttachment()).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM attachments WHERE id = @id;",
            new { id = id.ToString() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class AttachmentRow
    {
        public string Id { get; set; } = string.Empty;
        public string NoteId { get; set; } = string.Empty;
        public long Kind { get; set; }
        public string Path { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public long SizeBytes { get; set; }
        public string? Sha256 { get; set; }
        public string CreatedAt { get; set; } = string.Empty;

        public Attachment ToAttachment() => new()
        {
            Id = Guid.Parse(Id),
            NoteId = Guid.Parse(NoteId),
            Kind = (AttachmentKind)Kind,
            Path = Path,
            DisplayName = DisplayName,
            SizeBytes = SizeBytes,
            Sha256 = Sha256,
            CreatedAt = SqliteTime.FromDb(CreatedAt),
        };
    }
}
