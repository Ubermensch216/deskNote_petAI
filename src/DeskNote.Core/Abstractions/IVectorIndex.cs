namespace DeskNote.Core.Abstractions;

/// <summary>A semantically similar note chunk.</summary>
public sealed record VectorHit(Guid NoteId, int ChunkOrdinal, double Distance);

/// <summary>
/// Semantic retrieval over note embeddings (v1, report p8).
/// </summary>
/// <remarks>
/// <para>
/// The concrete backend is expected to be sqlite-vec, which the report flags as pre-v1 with
/// possible breaking changes. Everything above this interface must stay unaware of that, so the
/// backend can be swapped or version-pinned without touching callers (report p8).
/// </para>
/// <para>
/// Embeddings are as sensitive as the note text they came from and belong inside the same
/// encryption boundary (report p16).
/// </para>
/// </remarks>
public interface IVectorIndex
{
    Task UpsertAsync(
        Guid noteId,
        int chunkOrdinal,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default);

    Task RemoveNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorHit>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK = 10,
        IReadOnlyList<Guid>? scopedNoteIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notes whose text sits near this note's, nearest first, excluding the note itself.
    /// </summary>
    /// <remarks>
    /// Takes a note rather than an embedding because the note already has one. Re-embedding text
    /// that is sitting in the index would cost a model round trip to reproduce a vector that is
    /// already stored — and reproduce it slightly differently, since it would be embedded as one
    /// blob rather than as the chunks the index actually holds.
    /// </remarks>
    Task<IReadOnlyList<VectorHit>> FindSimilarAsync(
        Guid noteId,
        int topK = 10,
        CancellationToken cancellationToken = default);
}
