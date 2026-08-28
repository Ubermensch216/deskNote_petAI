using Dapper;
using DeskNote.Core.Abstractions;

namespace DeskNote.Data;

/// <summary>
/// Full-text search over notes, backed by the <c>notes_fts</c> trigram FTS5 table.
/// </summary>
/// <remarks>
/// <para>
/// The index is maintained entirely by triggers, so this type only reads.
/// </para>
/// <para>
/// The trigram tokenizer indexes 3-character windows, which is what makes substring search work
/// for Korean, where whitespace does not separate meaningful units. Its cost is that
/// <c>MATCH</c> cannot serve queries shorter than three characters, so those fall back to
/// <c>LIKE</c> over the base table — slower, but correct, and only ever hit while the user is
/// typing the first two characters.
/// </para>
/// </remarks>
public sealed class Fts5SearchIndex(SqliteConnectionFactory connectionFactory) : ISearchIndex
{
    /// <summary>Shortest query the trigram tokenizer can match.</summary>
    private const int MinimumTrigramLength = 3;

    public async Task<IReadOnlyList<NoteSearchHit>> SearchAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        return trimmed.Length < MinimumTrigramLength
            ? await SearchWithLikeAsync(connection, trimmed, limit, cancellationToken).ConfigureAwait(false)
            : await SearchWithFtsAsync(connection, trimmed, limit, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<NoteSearchHit>> SearchWithFtsAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT n.id AS NoteId,
                   n.title AS Title,
                   snippet(notes_fts, 1, '[', ']', '…', 12) AS Snippet,
                   f.rank AS Rank
            FROM notes_fts f
            JOIN notes n ON n.rowid = f.rowid
            WHERE notes_fts MATCH @match AND n.deleted_at IS NULL
            ORDER BY f.rank
            LIMIT @limit;
            """;

        var rows = await connection.QueryAsync<SearchRow>(
            new CommandDefinition(
                sql,
                new { match = ToMatchExpression(query), limit },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToHit()).ToList();
    }

    private static async Task<IReadOnlyList<NoteSearchHit>> SearchWithLikeAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT n.id AS NoteId,
                   n.title AS Title,
                   substr(n.content, 1, 120) AS Snippet,
                   0.0 AS Rank
            FROM notes n
            WHERE n.deleted_at IS NULL
              AND (n.title LIKE @pattern ESCAPE '\' OR n.content LIKE @pattern ESCAPE '\')
            ORDER BY n.updated_at DESC
            LIMIT @limit;
            """;

        var rows = await connection.QueryAsync<SearchRow>(
            new CommandDefinition(
                sql,
                new { pattern = $"%{EscapeLike(query)}%", limit },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToHit()).ToList();
    }

    /// <summary>
    /// Wraps the user's text as a single quoted FTS5 phrase.
    /// </summary>
    /// <remarks>
    /// Raw find-as-you-type input regularly contains characters FTS5 reads as operators — a stray
    /// <c>"</c>, <c>*</c>, <c>-</c> or <c>(</c> would otherwise throw a syntax error mid-keystroke.
    /// Quoting turns the whole query into a literal phrase; embedded quotes are doubled, which is
    /// how FTS5 escapes them inside a quoted string.
    /// </remarks>
    private static string ToMatchExpression(string query) =>
        "\"" + query.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class SearchRow
    {
        public string NoteId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public double Rank { get; set; }

        public NoteSearchHit ToHit() => new(Guid.Parse(NoteId), Title, Snippet, Rank);
    }
}
