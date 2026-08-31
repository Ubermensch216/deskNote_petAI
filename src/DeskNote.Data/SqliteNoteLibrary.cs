using Dapper;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

/// <inheritdoc cref="INoteLibrary"/>
public sealed class SqliteNoteLibrary(SqliteConnectionFactory connectionFactory, IClock clock) : INoteLibrary
{
    /// <summary>How much of the body a list row shows.</summary>
    private const int PreviewLength = 160;

    /// <summary>
    /// Summary columns.
    /// </summary>
    /// <remarks>
    /// The checklist counts come from the projection rather than from parsing bodies, and the
    /// preview is truncated in SQL, so listing a large library never pulls whole note bodies
    /// across just to show one line of each.
    /// </remarks>
    private const string SummaryColumns = """
        n.id AS Id,
        n.title AS Title,
        substr(n.content, 1, 160) AS Preview,
        n.color_key AS ColorKey,
        n.notebook_id AS NotebookId,
        n.is_open AS IsOpen,
        n.updated_at AS UpdatedAt,
        n.deleted_at AS DeletedAt,
        (SELECT COUNT(*) FROM checklist_items c WHERE c.note_id = n.id AND c.is_done = 1) AS ChecklistDone,
        (SELECT COUNT(*) FROM checklist_items c WHERE c.note_id = n.id) AS ChecklistTotal,
        (SELECT MIN(r.due_at) FROM reminders r
          WHERE r.note_id = n.id AND r.completed_at IS NULL AND r.due_at >= @now) AS NextReminderAt
        """;

    public async Task<IReadOnlyList<NoteSummary>> ListAsync(
        NoteQuery query,
        CancellationToken cancellationToken = default)
    {
        var (where, parameters) = BuildFilter(query);
        parameters.Add("now", SqliteTime.ToDb(clock.UtcNow));
        parameters.Add("limit", query.Limit);
        parameters.Add("offset", query.Offset);

        var order = query.Sort switch
        {
            NoteSort.CreatedDescending => "n.created_at DESC",
            NoteSort.TitleAscending => "n.title COLLATE NOCASE ASC",
            _ => "n.updated_at DESC",
        };

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM notes n WHERE {where} ORDER BY {order} LIMIT @limit OFFSET @offset;",
            parameters,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return await AttachTagsAsync(connection, rows.ToList(), cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// Search and filtering are combined in one query rather than run separately and intersected,
    /// so "unchecked items in this notebook matching 누수" stays a single indexed pass.
    /// </remarks>
    public async Task<IReadOnlyList<NoteSummary>> SearchAsync(
        string text,
        NoteQuery query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return await ListAsync(query with { Limit = limit }, cancellationToken).ConfigureAwait(false);
        }

        var (where, parameters) = BuildFilter(query);
        parameters.Add("now", SqliteTime.ToDb(clock.UtcNow));
        parameters.Add("limit", limit);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Below three characters the trigram index cannot match, so those keystrokes fall back to
        // LIKE — the same split Fts5SearchIndex makes, kept consistent here.
        string sql;
        if (trimmed.Length < 3)
        {
            parameters.Add("pattern", $"%{EscapeLike(trimmed)}%");
            sql = $"""
                SELECT {SummaryColumns} FROM notes n
                WHERE {where}
                  AND (n.title LIKE @pattern ESCAPE '\' OR n.content LIKE @pattern ESCAPE '\')
                ORDER BY n.updated_at DESC
                LIMIT @limit;
                """;
        }
        else
        {
            parameters.Add("match", ToMatchExpression(trimmed));
            sql = $"""
                SELECT {SummaryColumns} FROM notes n
                JOIN notes_fts f ON f.rowid = n.rowid
                WHERE {where} AND notes_fts MATCH @match
                ORDER BY f.rank
                LIMIT @limit;
                """;
        }

        var rows = await connection.QueryAsync<SummaryRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return await AttachTagsAsync(connection, rows.ToList(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// SQLite has no ordering by position in an <c>IN</c> list, and sorting in SQL by anything
    /// else would throw away the ranking this method exists to preserve. So the filter runs in
    /// the database and the ordering is reapplied here, over a set bounded by the caller's topK.
    /// </remarks>
    public async Task<IReadOnlyList<NoteSummary>> ListByIdsAsync(
        IReadOnlyList<Guid> ids,
        NoteQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        var (where, parameters) = BuildFilter(query);
        parameters.Add("now", SqliteTime.ToDb(clock.UtcNow));
        parameters.Add("ids", ids.Select(id => id.ToString()).ToArray());

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM notes n WHERE {where} AND n.id IN @ids;",
            parameters,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var summaries = await AttachTagsAsync(connection, rows.ToList(), cancellationToken)
            .ConfigureAwait(false);

        var byId = summaries.ToDictionary(summary => summary.Id);

        return [.. ids.Select(id => byId.TryGetValue(id, out var row) ? row : null).OfType<NoteSummary>()];
    }

    public async Task<IReadOnlyList<TagUsage>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<TagUsageRow>(new CommandDefinition(
            """
            SELECT t.id AS Id, t.name AS Name, t.normalized_name AS NormalizedName,
                   COUNT(nt.note_id) AS NoteCount
            FROM tags t
            JOIN note_tags nt ON nt.tag_id = t.id
            JOIN notes n ON n.id = nt.note_id AND n.deleted_at IS NULL
            GROUP BY t.id, t.name, t.normalized_name
            ORDER BY NoteCount DESC, t.normalized_name ASC;
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => new TagUsage(
            new Tag { Id = Guid.Parse(r.Id), Name = r.Name, NormalizedName = r.NormalizedName },
            r.NoteCount)).ToList();
    }

    public async Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<NotebookRow>(new CommandDefinition(
            """
            SELECT id AS Id, name AS Name, parent_id AS ParentId, ordinal AS Ordinal, created_at AS CreatedAt
            FROM notebooks
            ORDER BY ordinal ASC, name COLLATE NOCASE ASC;
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToNotebook()).ToList();
    }

    public async Task<Notebook> CreateNotebookAsync(
        string name,
        Guid? parentId = null,
        CancellationToken cancellationToken = default)
    {
        var notebook = new Notebook
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            ParentId = parentId,
            CreatedAt = clock.UtcNow,
        };

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO notebooks (id, name, parent_id, ordinal, created_at) " +
            "VALUES (@id, @name, @parentId, @ordinal, @createdAt);",
            new
            {
                id = notebook.Id.ToString(),
                name = notebook.Name,
                parentId = notebook.ParentId?.ToString(),
                ordinal = notebook.Ordinal,
                createdAt = SqliteTime.ToDb(notebook.CreatedAt),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return notebook;
    }

    public async Task RenameNotebookAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE notebooks SET name = @name WHERE id = @id;",
            new { id = id.ToString(), name = name.Trim() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <remarks>
    /// Notes survive their notebook. The foreign key nulls their <c>notebook_id</c>, so deleting a
    /// folder never takes its contents with it — a folder is an organizing device, not a container
    /// whose removal should destroy work.
    /// </remarks>
    public async Task DeleteNotebookAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM notebooks WHERE id = @id;",
            new { id = id.ToString() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the tags for a page of summaries in one query.
    /// </summary>
    /// <remarks>
    /// One round trip for the page rather than one per row: at a 200-row page the difference is
    /// between two queries and two hundred.
    /// </remarks>
    private static async Task<IReadOnlyList<NoteSummary>> AttachTagsAsync(
        SqliteConnection connection,
        IReadOnlyList<SummaryRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var byNote = (await connection.QueryAsync<(string NoteId, string Name)>(new CommandDefinition(
                """
                SELECT nt.note_id, t.name
                FROM note_tags nt
                JOIN tags t ON t.id = nt.tag_id
                WHERE nt.note_id IN @ids
                ORDER BY t.normalized_name;
                """,
                new { ids = rows.Select(r => r.Id).ToArray() },
                cancellationToken: cancellationToken)).ConfigureAwait(false))
            .GroupBy(x => x.NoteId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).ToList(), StringComparer.Ordinal);

        return rows
            .Select(r => r.ToSummary(byNote.TryGetValue(r.Id, out var tags) ? tags : []))
            .ToList();
    }

    private static (string Where, DynamicParameters Parameters) BuildFilter(NoteQuery query)
    {
        var parameters = new DynamicParameters();
        var clauses = new List<string>
        {
            query.OnlyDeleted ? "n.deleted_at IS NOT NULL" : "n.deleted_at IS NULL",
        };

        if (query.NotebookId is { } notebookId)
        {
            clauses.Add("n.notebook_id = @notebookId");
            parameters.Add("notebookId", notebookId.ToString());
        }

        if (!string.IsNullOrWhiteSpace(query.TagNormalizedName))
        {
            clauses.Add(
                "EXISTS (SELECT 1 FROM note_tags nt JOIN tags t ON t.id = nt.tag_id " +
                "WHERE nt.note_id = n.id AND t.normalized_name = @tagName)");
            parameters.Add("tagName", query.TagNormalizedName);
        }

        return (string.Join(" AND ", clauses), parameters);
    }

    /// <summary>Quotes raw find-as-you-type input so FTS5 operator characters cannot throw.</summary>
    private static string ToMatchExpression(string query) =>
        "\"" + query.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class SummaryRow
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string ColorKey { get; set; } = string.Empty;
        public string? NotebookId { get; set; }
        public long IsOpen { get; set; }
        public string UpdatedAt { get; set; } = string.Empty;
        public string? DeletedAt { get; set; }
        public int ChecklistDone { get; set; }
        public int ChecklistTotal { get; set; }
        public string? NextReminderAt { get; set; }

        public NoteSummary ToSummary(IReadOnlyList<string> tags) => new()
        {
            Id = Guid.Parse(Id),
            Title = Title,
            Preview = Preview.Length > PreviewLength ? Preview[..PreviewLength] : Preview,
            ColorKey = NoteColors.Normalize(ColorKey),
            NotebookId = NotebookId is null ? null : Guid.Parse(NotebookId),
            IsOpen = IsOpen != 0,
            UpdatedAt = SqliteTime.FromDb(UpdatedAt),
            IsDeleted = DeletedAt is not null,
            ChecklistDone = ChecklistDone,
            ChecklistTotal = ChecklistTotal,
            Tags = tags,
            NextReminderAt = SqliteTime.FromDbNullable(NextReminderAt),
        };
    }

    private sealed class TagUsageRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public int NoteCount { get; set; }
    }

    private sealed class NotebookRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public int Ordinal { get; set; }
        public string CreatedAt { get; set; } = string.Empty;

        public Notebook ToNotebook() => new()
        {
            Id = Guid.Parse(Id),
            Name = Name,
            ParentId = ParentId is null ? null : Guid.Parse(ParentId),
            Ordinal = Ordinal,
            CreatedAt = SqliteTime.FromDb(CreatedAt),
        };
    }
}
