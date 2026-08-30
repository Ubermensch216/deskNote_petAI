using System.Runtime.InteropServices;
using Dapper;
using DeskNote.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

/// <summary>
/// <see cref="IVectorIndex"/> over an ordinary SQLite table, scanned in process.
/// </summary>
/// <remarks>
/// <para>
/// The report names sqlite-vec and in the same breath calls it pre-v1 with breaking changes
/// possible (p8). A native extension that has to be loaded, shipped per architecture, and
/// re-validated on every upgrade is a large bill for a personal note app: a brute-force cosine
/// over a few thousand vectors costs single-digit milliseconds, and the whole point of putting
/// this behind an interface is that the day the bill is worth paying, only this file changes.
/// </para>
/// <para>
/// Vectors are stored per model. Comparing embeddings produced by two different models is
/// meaningless, so rows from another model are ignored by search rather than mixed into it.
/// </para>
/// </remarks>
public sealed class SqliteVectorIndex(SqliteConnectionFactory connectionFactory, string modelId) : IVectorIndex
{
    public async Task UpsertAsync(
        Guid noteId,
        int chunkOrdinal,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        if (embedding.Length == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO note_embeddings (note_id, chunk_ordinal, model, dim, embedding, updated_at)
            VALUES (@noteId, @ordinal, @model, @dim, @embedding, @updatedAt)
            ON CONFLICT (note_id, chunk_ordinal) DO UPDATE SET
                model      = excluded.model,
                dim        = excluded.dim,
                embedding  = excluded.embedding,
                updated_at = excluded.updated_at;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                noteId = noteId.ToString(),
                ordinal = chunkOrdinal,
                model = modelId,
                dim = embedding.Length,
                embedding = ToBlob(embedding.Span),
                updatedAt = SqliteTime.ToDb(DateTimeOffset.UtcNow),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task RemoveNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM note_embeddings WHERE note_id = @noteId;",
            new { noteId = noteId.ToString() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the chunks of a note beyond <paramref name="keepBelow"/>.
    /// </summary>
    /// <remarks>
    /// A note that shrinks leaves orphan chunks behind, and an orphan is worse than a missing one:
    /// it goes on matching text the note no longer contains.
    /// </remarks>
    public async Task TrimAsync(Guid noteId, int keepBelow, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM note_embeddings WHERE note_id = @noteId AND chunk_ordinal >= @keepBelow;",
            new { noteId = noteId.ToString(), keepBelow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Notes that have no vectors for this model yet, oldest edits last.
    /// </summary>
    /// <remarks>
    /// Semantic search would otherwise only ever cover notes written after the feature shipped, or
    /// after a model change. Backfilling a bounded batch at a time keeps that from becoming a
    /// startup that embeds ten thousand notes before the user can type.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> FindUnindexedAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT n.id
            FROM notes n
            LEFT JOIN note_embeddings e ON e.note_id = n.id AND e.model = @model
            WHERE n.deleted_at IS NULL AND e.note_id IS NULL AND trim(n.content) <> ''
            ORDER BY n.updated_at DESC
            LIMIT @limit;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var ids = await connection.QueryAsync<string>(new CommandDefinition(
            sql,
            new { model = modelId, limit },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return ids.Select(Guid.Parse).ToList();
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK = 10,
        IReadOnlyList<Guid>? scopedNoteIds = null,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding.Length == 0 || topK <= 0)
        {
            return [];
        }

        var sql = """
            SELECT e.note_id, e.chunk_ordinal, e.embedding
            FROM note_embeddings e
            JOIN notes n ON n.id = e.note_id
            WHERE e.model = @model AND e.dim = @dim AND n.deleted_at IS NULL
            """;

        var scope = scopedNoteIds?.Select(id => id.ToString()).ToList();

        if (scope is { Count: > 0 })
        {
            sql += " AND e.note_id IN @scope";
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var reader = await connection.ExecuteReaderAsync(new CommandDefinition(
            sql + ";",
            new { model = modelId, dim = queryEmbedding.Length, scope },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var query = queryEmbedding.Span;
        var queryNorm = Norm(query);

        if (queryNorm == 0)
        {
            return [];
        }

        var hits = new List<VectorHit>();

        while (reader.Read())
        {
            var blob = (byte[])reader.GetValue(2);
            var vector = MemoryMarshal.Cast<byte, float>(blob);

            if (vector.Length != query.Length)
            {
                continue;
            }

            var norm = Norm(vector);
            if (norm == 0)
            {
                continue;
            }

            var similarity = Dot(query, vector) / (queryNorm * norm);

            hits.Add(new VectorHit(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                1.0 - similarity));
        }

        // One note can match on several chunks; the note is the result, so it appears once, at the
        // distance of its best chunk.
        return hits
            .GroupBy(hit => hit.NoteId)
            .Select(group => group.MinBy(hit => hit.Distance)!)
            .OrderBy(hit => hit.Distance)
            .Take(topK)
            .ToList();
    }

    private static byte[] ToBlob(ReadOnlySpan<float> vector) =>
        MemoryMarshal.AsBytes(vector).ToArray();

    private static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double sum = 0;

        for (var i = 0; i < left.Length; i++)
        {
            sum += (double)left[i] * right[i];
        }

        return sum;
    }

    private static double Norm(ReadOnlySpan<float> vector) => Math.Sqrt(Dot(vector, vector));
}
