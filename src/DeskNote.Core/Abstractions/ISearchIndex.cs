namespace DeskNote.Core.Abstractions;

/// <summary>A note matching a full-text query.</summary>
/// <param name="NoteId">The matched note.</param>
/// <param name="Title">Note title at match time, for rendering the result row without a second read.</param>
/// <param name="Snippet">Highlighted excerpt around the match.</param>
/// <param name="Rank">FTS5 rank; lower is a better match.</param>
public sealed record NoteSearchHit(Guid NoteId, string Title, string Snippet, double Rank);

/// <summary>
/// Full-text search over note titles and bodies, backed by SQLite FTS5 (report p6).
/// </summary>
/// <remarks>
/// The index is maintained by database triggers rather than by explicit calls, so writers never
/// have to remember to reindex. Semantic search arrives later behind <see cref="IVectorIndex"/>
/// and is combined with these results by a hybrid retriever (report p8).
/// </remarks>
public interface ISearchIndex
{
    /// <summary>
    /// Runs a find-as-you-type query. Implementations must tolerate arbitrary user input —
    /// FTS5 operator characters in the raw string must not throw.
    /// </summary>
    Task<IReadOnlyList<NoteSearchHit>> SearchAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
