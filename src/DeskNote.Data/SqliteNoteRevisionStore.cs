using Dapper;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Data;

/// <inheritdoc cref="INoteRevisionStore"/>
public sealed class SqliteNoteRevisionStore(SqliteConnectionFactory connectionFactory) : INoteRevisionStore
{
    private const string Columns = """
        id AS Id, note_id AS NoteId, content AS Content,
        created_at AS CreatedAt, source AS Source, action_name AS ActionName
        """;

    public async Task AddAsync(NoteRevision revision, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO note_revisions (id, note_id, content, created_at, source, action_name)
            VALUES (@id, @noteId, @content, @createdAt, @source, @actionName);
            """,
            new
            {
                id = revision.Id.ToString(),
                noteId = revision.NoteId.ToString(),
                content = NoteContent.NormalizeLineEndings(revision.Content),
                createdAt = SqliteTime.ToDb(revision.CreatedAt),
                source = (int)revision.Source,
                actionName = revision.ActionName,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NoteRevision>> ListAsync(
        Guid noteId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<RevisionRow>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM note_revisions
             WHERE note_id = @noteId
             ORDER BY created_at DESC, id DESC
             LIMIT @limit;
             """,
            new { noteId = noteId.ToString(), limit },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToRevision()).ToList();
    }

    public async Task<NoteRevision?> GetLatestAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var latest = await ListAsync(noteId, limit: 1, cancellationToken).ConfigureAwait(false);
        return latest.Count == 0 ? null : latest[0];
    }

    /// <remarks>
    /// Revisions accumulate for as long as a note is edited, so history is capped per note rather
    /// than left to grow without bound inside a database the user never sees.
    /// </remarks>
    public async Task<int> PruneAsync(Guid noteId, int keep, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM note_revisions
            WHERE note_id = @noteId
              AND id NOT IN (
                  SELECT id FROM note_revisions
                  WHERE note_id = @noteId
                  ORDER BY created_at DESC, id DESC
                  LIMIT @keep
              );
            """,
            new { noteId = noteId.ToString(), keep = Math.Max(0, keep) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class RevisionRow
    {
        public string Id { get; set; } = string.Empty;
        public string NoteId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public long Source { get; set; }
        public string? ActionName { get; set; }

        public NoteRevision ToRevision() => new()
        {
            Id = Guid.Parse(Id),
            NoteId = Guid.Parse(NoteId),
            Content = Content,
            CreatedAt = SqliteTime.FromDb(CreatedAt),
            Source = (RevisionSource)Source,
            ActionName = ActionName,
        };
    }
}
