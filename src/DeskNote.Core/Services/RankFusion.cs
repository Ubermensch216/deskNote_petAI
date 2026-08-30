namespace DeskNote.Core.Services;

/// <summary>
/// Merges the keyword and semantic result lists into one ranking (report p8: hybrid retrieval).
/// </summary>
/// <remarks>
/// <para>
/// Reciprocal rank fusion, because the two retrievers produce numbers that cannot be compared:
/// FTS5 rank is a negative BM25 score and a vector distance is a cosine, and any attempt to
/// normalise them into a shared scale is a constant that has to be re-tuned whenever either side
/// changes. Positions are comparable, so only positions are used.
/// </para>
/// <para>
/// A note found by both retrievers outranks one found by either alone, which is the property that
/// makes the hybrid worth having: the keyword hit proves the words are there, and the vector hit
/// proves the meaning is.
/// </para>
/// </remarks>
public static class RankFusion
{
    /// <summary>
    /// Damping constant from the original RRF paper. Large enough that the top of a list does not
    /// dominate the fusion outright, which is what lets agreement between lists win.
    /// </summary>
    public const int K = 60;

    /// <summary>
    /// Fuses ranked lists, best first. Duplicates within one list are ignored after their first
    /// appearance, so a note cannot buy rank by being listed twice.
    /// </summary>
    public static IReadOnlyList<T> Fuse<T>(IEnumerable<IReadOnlyList<T>> rankings, int limit = 10)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(rankings);

        var scores = new Dictionary<T, double>();
        var firstSeen = new Dictionary<T, int>();
        var order = 0;

        foreach (var ranking in rankings)
        {
            var seen = new HashSet<T>();

            for (var position = 0; position < ranking.Count; position++)
            {
                var item = ranking[position];

                if (!seen.Add(item))
                {
                    continue;
                }

                scores[item] = scores.GetValueOrDefault(item) + (1.0 / (K + position + 1));
                firstSeen.TryAdd(item, order++);
            }
        }

        return scores
            .OrderByDescending(pair => pair.Value)

            // Ties break by which retriever saw the item first, so the order is deterministic
            // rather than dependent on dictionary iteration.
            .ThenBy(pair => firstSeen[pair.Key])
            .Take(limit)
            .Select(pair => pair.Key)
            .ToList();
    }
}
