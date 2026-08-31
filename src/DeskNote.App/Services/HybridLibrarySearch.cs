using DeskNote.Core.Abstractions;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

/// <summary>
/// Library search that reads what the user meant as well as what they typed.
/// </summary>
/// <remarks>
/// <para>
/// The two halves fail differently, which is the whole reason to run both. Keyword search cannot
/// find a note that says "페이먼트 모듈 논의" from the words "결제 관련 회의" — nothing overlaps.
/// Semantic search finds that one and then misses the note whose only relevant content is an
/// order number, because nobody phrases a question around an order number.
/// </para>
/// <para>
/// This is deliberately a second pass rather than a replacement. Keyword results are already on
/// screen 150 ms after the last keystroke; embedding the query costs about 0.3 s, so waiting for
/// it would make every search feel three times slower to serve the minority of queries that need
/// it. Instead the fast answer is shown and then <em>improved</em>, and a search that finds what
/// it needed on the first pass never notices the second one happened.
/// </para>
/// <para>
/// Ranks are fused rather than scores. FTS5 returns negative BM25 and the vector index returns
/// cosine distance; there is no axis on which those two numbers can be compared, but their
/// orderings can be — and a note both halves found should beat one only half found, which is
/// exactly what <see cref="RankFusion"/> encodes.
/// </para>
/// </remarks>
public sealed class HybridLibrarySearch(
    INoteLibrary library,
    IVectorIndex vectors,
    IEmbeddingService embedder)
{
    /// <summary>
    /// Keyword results, exactly as the library has always returned them.
    /// </summary>
    /// <remarks>
    /// Kept as its own call so the caller can render this before asking for the fused pass. The
    /// window needs both halves separately; nothing here decides when they are shown.
    /// </remarks>
    public Task<IReadOnlyList<NoteSummary>> SearchKeywordAsync(
        string text,
        NoteQuery query,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        library.SearchAsync(text, query, limit, cancellationToken);

    /// <summary>
    /// The fused ranking, or null when semantic search had nothing to add.
    /// </summary>
    /// <remarks>
    /// Null rather than a copy of the keyword results, so the caller can tell "no change" from
    /// "changed" and leave the list alone instead of re-rendering it under the user's pointer.
    /// With no embedding model installed this is always null and library search is exactly what
    /// it was before.
    /// </remarks>
    public async Task<IReadOnlyList<NoteSummary>?> FuseAsync(
        string text,
        NoteQuery query,
        IReadOnlyList<NoteSummary> keyword,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyword);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var semantic = await SemanticAsync(text, limit, cancellationToken).ConfigureAwait(false);
        if (semantic.Count == 0)
        {
            return null;
        }

        var keywordIds = keyword.Select(row => row.Id).ToList();
        var fused = RankFusion.Fuse<Guid>([keywordIds, semantic], limit);

        // Nothing moved and nothing was added: re-rendering would only make the list flicker.
        if (fused.SequenceEqual(keywordIds))
        {
            return null;
        }

        return await library.ListByIdsAsync([.. fused], query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Guid>> SemanticAsync(
        string text,
        int limit,
        CancellationToken cancellationToken)
    {
        var embedding = await embedder.EmbedAsync([text], cancellationToken).ConfigureAwait(false);
        if (embedding.Count == 0)
        {
            return [];
        }

        var hits = await vectors
            .SearchAsync(embedding[0], limit, scopedNoteIds: null, cancellationToken)
            .ConfigureAwait(false);

        // Several chunks of one note can match; the ranking is over notes, so the best chunk
        // stands for its note and the rest are dropped rather than counted again.
        return [.. hits.Select(hit => hit.NoteId).Distinct()];
    }
}
