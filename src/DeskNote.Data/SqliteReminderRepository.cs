using Dapper;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Data;

/// <inheritdoc cref="IReminderRepository"/>
public sealed class SqliteReminderRepository(SqliteConnectionFactory connectionFactory) : IReminderRepository
{
    private const string Columns = """
        id AS Id, note_id AS NoteId, due_at AS DueAt, recurrence_rule AS RecurrenceRule,
        completed_at AS CompletedAt, notified_at AS NotifiedAt
        """;

    public async Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO reminders (id, note_id, due_at, recurrence_rule, completed_at, notified_at)
            VALUES (@id, @noteId, @dueAt, @rule, @completedAt, @notifiedAt);
            """,
            new
            {
                id = reminder.Id.ToString(),
                noteId = reminder.NoteId.ToString(),
                dueAt = SqliteTime.ToDb(reminder.DueAt),
                rule = reminder.RecurrenceRule,
                completedAt = SqliteTime.ToDb(reminder.CompletedAt),
                notifiedAt = SqliteTime.ToDb(reminder.NotifiedAt),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Reminder>> ListForNoteAsync(
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ReminderRow>(new CommandDefinition(
            $"SELECT {Columns} FROM reminders WHERE note_id = @noteId ORDER BY due_at ASC;",
            new { noteId = noteId.ToString() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToReminder()).ToList();
    }

    /// <remarks>
    /// Notes that are deleted are excluded here rather than after the fact: a reminder for a note
    /// in the deleted view must not pop a toast pointing at something the user threw away.
    /// </remarks>
    public async Task<IReadOnlyList<Reminder>> ListDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ReminderRow>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM reminders r
             WHERE r.completed_at IS NULL
               AND r.notified_at IS NULL
               AND r.due_at <= @now
               AND EXISTS (SELECT 1 FROM notes n WHERE n.id = r.note_id AND n.deleted_at IS NULL)
             ORDER BY r.due_at ASC;
             """,
            new { now = SqliteTime.ToDb(now) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => r.ToReminder()).ToList();
    }

    public Task MarkNotifiedAsync(Guid id, DateTimeOffset notifiedAt, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE reminders SET notified_at = @notifiedAt WHERE id = @id;",
            new { id = id.ToString(), notifiedAt = SqliteTime.ToDb(notifiedAt) },
            cancellationToken);

    public Task RescheduleAsync(Guid id, DateTimeOffset nextDueAt, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE reminders SET due_at = @dueAt, notified_at = NULL WHERE id = @id;",
            new { id = id.ToString(), dueAt = SqliteTime.ToDb(nextDueAt) },
            cancellationToken);

    public Task CompleteAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "UPDATE reminders SET completed_at = @completedAt WHERE id = @id;",
            new { id = id.ToString(), completedAt = SqliteTime.ToDb(completedAt) },
            cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("DELETE FROM reminders WHERE id = @id;", new { id = id.ToString() }, cancellationToken);

    private async Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private sealed class ReminderRow
    {
        public string Id { get; set; } = string.Empty;
        public string NoteId { get; set; } = string.Empty;
        public string DueAt { get; set; } = string.Empty;
        public string? RecurrenceRule { get; set; }
        public string? CompletedAt { get; set; }
        public string? NotifiedAt { get; set; }

        public Reminder ToReminder() => new()
        {
            Id = Guid.Parse(Id),
            NoteId = Guid.Parse(NoteId),
            DueAt = SqliteTime.FromDb(DueAt),
            RecurrenceRule = RecurrenceRule,
            CompletedAt = SqliteTime.FromDbNullable(CompletedAt),
            NotifiedAt = SqliteTime.FromDbNullable(NotifiedAt),
        };
    }
}
