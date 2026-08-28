using Dapper;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Data;

/// <inheritdoc cref="INoteRepository"/>
public sealed class SqliteNoteRepository(SqliteConnectionFactory connectionFactory, IClock clock) : INoteRepository
{
    public async Task<Note?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<NoteRow>(
            new CommandDefinition(
                $"SELECT {NoteRow.Columns} FROM notes WHERE id = @id;",
                new { id = id.ToString() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToNote();
    }

    public async Task<IReadOnlyList<Note>> GetOpenNotesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<NoteRow>(
            new CommandDefinition(
                $"SELECT {NoteRow.Columns} FROM notes " +
                "WHERE is_open = 1 AND deleted_at IS NULL ORDER BY updated_at ASC;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToNote()).ToList();
    }

    public async Task<IReadOnlyList<Note>> ListAsync(NoteQuery query, CancellationToken cancellationToken = default)
    {
        var (where, parameters) = BuildFilter(query);
        var order = query.Sort switch
        {
            NoteSort.CreatedDescending => "created_at DESC",
            NoteSort.TitleAscending => "title COLLATE NOCASE ASC",
            _ => "updated_at DESC",
        };

        parameters.Add("limit", query.Limit);
        parameters.Add("offset", query.Offset);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<NoteRow>(
            new CommandDefinition(
                $"SELECT {NoteRow.Columns} FROM notes n WHERE {where} " +
                $"ORDER BY {order} LIMIT @limit OFFSET @offset;",
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToNote()).ToList();
    }

    public async Task<int> CountAsync(NoteQuery query, CancellationToken cancellationToken = default)
    {
        var (where, parameters) = BuildFilter(query);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                $"SELECT COUNT(*) FROM notes n WHERE {where};",
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task AddAsync(Note note, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO notes (
                id, title, content, format, color_key, opacity, always_on_top, is_open,
                x, y, width, height, monitor_key, size_preset, notebook_id,
                created_at, updated_at, deleted_at, rev)
            VALUES (
                @id, @title, @content, @format, @colorKey, @opacity, @alwaysOnTop, @isOpen,
                @x, @y, @width, @height, @monitorKey, @sizePreset, @notebookId,
                @createdAt, @updatedAt, @deletedAt, @rev);
            """;

        await ExecuteAsync(
            sql,
            new
            {
                id = note.Id.ToString(),
                title = note.Title,
                content = NoteContent.NormalizeLineEndings(note.Content),
                format = (int)note.Format,
                colorKey = NoteColors.Normalize(note.ColorKey),
                opacity = Math.Clamp(note.Opacity, Note.MinOpacity, 1.0),
                alwaysOnTop = note.AlwaysOnTop ? 1 : 0,
                isOpen = note.IsOpen ? 1 : 0,
                x = note.Geometry.X,
                y = note.Geometry.Y,
                width = note.Geometry.Width,
                height = note.Geometry.Height,
                monitorKey = note.Geometry.MonitorKey,
                sizePreset = (int)note.Geometry.Preset,
                notebookId = note.NotebookId?.ToString(),
                createdAt = SqliteTime.ToDb(note.CreatedAt == default ? clock.UtcNow : note.CreatedAt),
                updatedAt = SqliteTime.ToDb(note.UpdatedAt == default ? clock.UtcNow : note.UpdatedAt),
                deletedAt = SqliteTime.ToDb(note.DeletedAt),
                rev = note.Rev == 0 ? 1 : note.Rev,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateContentAsync(Guid id, string title, string content, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE notes SET title = @title, content = @content, updated_at = @updatedAt, rev = rev + 1 " +
            "WHERE id = @id;",
            new
            {
                id = id.ToString(),
                title,
                content = NoteContent.NormalizeLineEndings(content),
                updatedAt = SqliteTime.ToDb(clock.UtcNow),
            },
            cancellationToken);

    /// <remarks>
    /// Deliberately leaves <c>rev</c> and <c>updated_at</c> alone. Dragging a note is not an edit;
    /// bumping the revision here would make tidying the desktop look like a content change to
    /// sync, and would drag every moved note to the top of the "recently updated" list.
    /// </remarks>
    public Task UpdateGeometryAsync(Guid id, NoteGeometry geometry, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE notes SET x = @x, y = @y, width = @width, height = @height, " +
            "monitor_key = @monitorKey, size_preset = @sizePreset WHERE id = @id;",
            new
            {
                id = id.ToString(),
                x = geometry.X,
                y = geometry.Y,
                width = geometry.Width,
                height = geometry.Height,
                monitorKey = geometry.MonitorKey,
                sizePreset = (int)geometry.Preset,
            },
            cancellationToken);

    public Task UpdateAppearanceAsync(
        Guid id,
        string colorKey,
        double opacity,
        bool alwaysOnTop,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE notes SET color_key = @colorKey, opacity = @opacity, always_on_top = @alwaysOnTop " +
            "WHERE id = @id;",
            new
            {
                id = id.ToString(),
                colorKey = NoteColors.Normalize(colorKey),
                opacity = Math.Clamp(opacity, Note.MinOpacity, 1.0),
                alwaysOnTop = alwaysOnTop ? 1 : 0,
            },
            cancellationToken);

    public Task SetOpenAsync(Guid id, bool isOpen, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE notes SET is_open = @isOpen WHERE id = @id;",
            new { id = id.ToString(), isOpen = isOpen ? 1 : 0 },
            cancellationToken);

    public Task SetNotebookAsync(Guid id, Guid? notebookId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE notes SET notebook_id = @notebookId WHERE id = @id;",
            new { id = id.ToString(), notebookId = notebookId?.ToString() },
            cancellationToken);

    /// <remarks>Also closes the window, so a deleted note cannot reappear on the desktop after a restart.</remarks>
    public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE notes SET deleted_at = @deletedAt, is_open = 0 WHERE id = @id AND deleted_at IS NULL;",
            new { id = id.ToString(), deletedAt = SqliteTime.ToDb(clock.UtcNow) },
            cancellationToken);

    public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE notes SET deleted_at = NULL WHERE id = @id;",
            new { id = id.ToString() },
            cancellationToken);

    /// <remarks>
    /// Guarded on <c>deleted_at IS NOT NULL</c> so a live note can never be destroyed by a stray
    /// purge call; a note must pass through the deleted view first.
    /// </remarks>
    public Task PurgeAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "DELETE FROM notes WHERE id = @id AND deleted_at IS NOT NULL;",
            new { id = id.ToString() },
            cancellationToken);

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

    private async Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
