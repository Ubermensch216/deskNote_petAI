using Dapper;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

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

    /// <remarks>
    /// The note body and its checklist projection are written in one transaction, so the
    /// <c>checklist_items</c> rows can never describe a version of the note that is not the stored
    /// one.
    /// </remarks>
    public async Task UpdateContentAsync(
        Guid id,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var normalized = NoteContent.NormalizeLineEndings(content);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE notes SET title = @title, content = @content, updated_at = @updatedAt, rev = rev + 1 " +
            "WHERE id = @id;",
            new
            {
                id = id.ToString(),
                title,
                content = normalized,
                updatedAt = SqliteTime.ToDb(clock.UtcNow),
            },
            (Microsoft.Data.Sqlite.SqliteTransaction)transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await SyncChecklistAsync(connection, (Microsoft.Data.Sqlite.SqliteTransaction)transaction, id, normalized, cancellationToken)
            .ConfigureAwait(false);

        await SyncTagsAsync(connection, (Microsoft.Data.Sqlite.SqliteTransaction)transaction, id, normalized, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds a note's rows in <c>checklist_items</c> from its Markdown.
    /// </summary>
    /// <remarks>
    /// The Markdown body is the single source of truth; these rows exist so questions like "what
    /// is still unchecked across every note" do not require scanning note bodies. Rebuilding
    /// wholesale rather than diffing keeps the two in step no matter how the text changed — a
    /// hand edit, a paste, or a future AI rewrite.
    /// </remarks>
    private static async Task SyncChecklistAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        Guid noteId,
        string content,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM checklist_items WHERE note_id = @noteId;",
            new { noteId = noteId.ToString() },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var items = ChecklistParser.Parse(content);
        if (items.Count == 0)
        {
            return;
        }

        // Ordinal is the item's position among the note's checklist items, which is what
        // ChecklistParser returns them in; the line it lives on is recovered from the body.
        var rows = items.Select((item, index) => new
        {
            id = Guid.CreateVersion7().ToString(),
            noteId = noteId.ToString(),
            ordinal = index,
            text = item.Text,
            isDone = item.IsDone ? 1 : 0,
        }).ToList();

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO checklist_items (id, note_id, ordinal, text, is_done) " +
            "VALUES (@id, @noteId, @ordinal, @text, @isDone);",
            rows,
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

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

    /// <summary>
    /// Rebuilds a note's rows in <c>tags</c> and <c>note_tags</c> from the hashtags in its body.
    /// </summary>
    /// <remarks>
    /// Tag rows themselves are never deleted here. A tag the user has stopped using still belongs
    /// in the tag list they pick from, and deleting it would renumber nothing but would lose the
    /// original spelling the first note gave it.
    /// </remarks>
    private static async Task SyncTagsAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        Guid noteId,
        string content,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM note_tags WHERE note_id = @noteId;",
            new { noteId = noteId.ToString() },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var mentions = TagParser.Parse(content);
        if (mentions.Count == 0)
        {
            return;
        }

        foreach (var mention in mentions)
        {
            // The first note to use a spelling defines it; later notes writing it differently
            // still resolve to the same tag through normalized_name.
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO tags (id, name, normalized_name) VALUES (@id, @name, @normalized) " +
                "ON CONFLICT(normalized_name) DO NOTHING;",
                new
                {
                    id = Guid.CreateVersion7().ToString(),
                    name = mention.Name,
                    normalized = mention.NormalizedName,
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO note_tags (note_id, tag_id) " +
            "SELECT @noteId, id FROM tags WHERE normalized_name = @normalized;",
            mentions.Select(m => new { noteId = noteId.ToString(), normalized = m.NormalizedName }).ToList(),
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
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

    private async Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
