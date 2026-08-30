using DeskNote.Ai;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

/// <summary>
/// Picks the notes 메모 Q&amp;A may read, from keyword and semantic search together (report p8).
/// </summary>
/// <remarks>
/// <para>
/// Each retriever fails in a way the other does not. FTS cannot find "결제 관련 회의" in a note that
/// says "페이먼트 모듈 논의" — no word matches. Embeddings find that, and then miss the note whose
/// only relevant content is an order number nobody would phrase a question around. Running both
/// and fusing their rankings is what makes the answer depend on the note rather than on which
/// words the user happened to reuse.
/// </para>
/// <para>
/// Semantic search is best-effort. With no embedding model installed the vector side returns
/// nothing and this degrades to exactly the keyword retrieval it replaced.
/// </para>
/// </remarks>
public sealed class HybridRetriever(
    ISearchIndex search,
    IVectorIndex vectors,
    IEmbeddingService embedder,
    INoteRepository notes,
    int maxNotes = 5) : IAiRetriever
{
    public async Task<IReadOnlyList<RetrievedNote>> RetrieveAsync(
        AiQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A scoped question is never widened. If the caller named the notes, those are the notes,
        // however much better a search result might look.
        var ids = query.ScopedNoteIds.Count > 0
            ? query.ScopedNoteIds.Take(maxNotes).ToList()
            : await FindAsync(query.Question, cancellationToken).ConfigureAwait(false);

        var retrieved = new List<RetrievedNote>(ids.Count);

        foreach (var id in ids)
        {
            // A note deleted between the search and the read is simply not part of the answer.
            if (await notes.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } note)
            {
                retrieved.Add(new RetrievedNote(note.Id, note.Content, note.Title));
            }
        }

        return retrieved;
    }

    private async Task<List<Guid>> FindAsync(string question, CancellationToken cancellationToken)
    {
        // Each retriever is asked for more than the final count: fusion needs room to disagree.
        var depth = maxNotes * 2;

        var keyword = (await search.SearchAsync(question, depth, cancellationToken).ConfigureAwait(false))
            .Select(hit => hit.NoteId)
            .ToList();

        var semantic = await SemanticAsync(question, depth, cancellationToken).ConfigureAwait(false);

        return semantic.Count == 0
            ? keyword.Take(maxNotes).ToList()
            : [.. RankFusion.Fuse<Guid>([keyword, semantic], maxNotes)];
    }

    private async Task<List<Guid>> SemanticAsync(
        string question,
        int depth,
        CancellationToken cancellationToken)
    {
        var embedding = await embedder.EmbedAsync([question], cancellationToken).ConfigureAwait(false);

        if (embedding.Count == 0)
        {
            return [];
        }

        var hits = await vectors
            .SearchAsync(embedding[0], depth, scopedNoteIds: null, cancellationToken)
            .ConfigureAwait(false);

        return hits.Select(hit => hit.NoteId).ToList();
    }
}
