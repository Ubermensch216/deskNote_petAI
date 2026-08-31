using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.App.Services;

/// <summary>One note near another, with how near.</summary>
/// <param name="Note">The neighbour, as a library row.</param>
/// <param name="Similarity">Cosine similarity, 0 to 1.</param>
public sealed record RelatedNote(NoteSummary Note, double Similarity)
{
    /// <summary>
    /// Above this, two notes are saying the same thing rather than merely sharing a subject.
    /// </summary>
    /// <remarks>
    /// Chosen to be quiet rather than eager. bge-m3 puts unrelated Korean prose around 0.3–0.5 and
    /// genuinely related notes around 0.6–0.8, so a "near-duplicate" claim only earns its keep
    /// well above that — a false one sends the user to merge two notes that should stay apart.
    /// </remarks>
    public const double DuplicateThreshold = 0.92;

    public bool IsNearDuplicate => Similarity >= DuplicateThreshold;
}

/// <summary>
/// What the embeddings already know about a note's surroundings.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is a vector read and nothing else — no generation, so no 25-second wait. The
/// index was built for 메모 Q&amp;A and then used by nothing else; these are the questions it can
/// already answer for free.
/// </para>
/// <para>
/// All of it degrades to empty. With no embedding model installed there are no vectors, and a
/// caller gets an empty list rather than an error — the same contract the rest of the semantic
/// layer keeps.
/// </para>
/// </remarks>
public sealed class NoteNeighbourhood(IVectorIndex vectors, INoteLibrary library)
{
    /// <summary>
    /// Notes that read like this one, nearest first.
    /// </summary>
    /// <remarks>
    /// Deleted notes are excluded by the index itself, so a neighbour in this list is always a
    /// note the user can actually open.
    /// </remarks>
    public async Task<IReadOnlyList<RelatedNote>> RelatedAsync(
        Guid noteId,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var hits = await vectors.FindSimilarAsync(noteId, limit, cancellationToken).ConfigureAwait(false);

        if (hits.Count == 0)
        {
            return [];
        }

        var rows = await library
            .ListByIdsAsync([.. hits.Select(hit => hit.NoteId)], NoteQuery.Default, cancellationToken)
            .ConfigureAwait(false);

        var distances = hits.ToDictionary(hit => hit.NoteId, hit => hit.Distance);

        return [.. rows.Select(row => new RelatedNote(row, 1.0 - distances[row.Id]))];
    }

    /// <summary>
    /// The note and the neighbours close enough to belong with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Greedy rather than a clustering pass over the whole library: the question being answered is
    /// "what belongs with <em>this</em> note", which the user asked by opening it. A k-means over
    /// every vector would answer a different question — "how does the library divide" — and would
    /// have to be recomputed on every edit.
    /// </para>
    /// <para>
    /// The floor is well above the threshold for showing a note as related. A list worth reading
    /// and a set worth filing together are different bars: the first can afford a loose match, the
    /// second moves the user's notes into a folder.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<NoteSummary>> GroupAsync(
        Guid noteId,
        double minimumSimilarity = 0.7,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var related = await RelatedAsync(noteId, limit, cancellationToken).ConfigureAwait(false);

        return
        [
            .. related
                .Where(item => item.Similarity >= minimumSimilarity)
                .Select(item => item.Note),
        ];
    }

    /// <summary>
    /// Tags the note's neighbours carry and it does not, most widely shared first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cheapest good answer to "what should this note be tagged". A note about the same thing
    /// as three tagged notes almost always wants one of their tags, and finding that out costs a
    /// vector read instead of the 45 seconds the model takes to invent one.
    /// </para>
    /// <para>
    /// Ranked by how many neighbours share a tag rather than by how near the nearest one is: a tag
    /// three neighbours agree on describes a subject, while one that appears once may just be
    /// something that happened in that note.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> NearbyTagsAsync(
        Guid noteId,
        IReadOnlyCollection<string> alreadyOn,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alreadyOn);

        var related = await RelatedAsync(noteId, limit: 8, cancellationToken).ConfigureAwait(false);

        if (related.Count == 0)
        {
            return [];
        }

        var carried = new HashSet<string>(
            alreadyOn.Select(Tag.Normalize),
            StringComparer.Ordinal);

        return
        [
            .. related
                .SelectMany(neighbour => neighbour.Note.Tags)
                .Where(tag => !carried.Contains(Tag.Normalize(tag)))
                .GroupBy(Tag.Normalize, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.First())
                .Take(limit),
        ];
    }
}
