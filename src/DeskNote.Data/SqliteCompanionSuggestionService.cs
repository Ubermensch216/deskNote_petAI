using System.Globalization;
using DeskNote.Companion.Core;
using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

/// <summary>Finds two high-signal local suggestions and persists interruption budgets.</summary>
public sealed class SqliteCompanionSuggestionService(SqliteConnectionFactory connectionFactory)
    : ICompanionSuggestionService
{
    public async Task<CompanionSuggestion?> TryOfferAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (!await IsWithinBudgetAsync(connection, now, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var candidate = await FindUpcomingReminderAsync(connection, now, cancellationToken).ConfigureAwait(false)
            ?? await FindStaleChecklistAsync(connection, now, cancellationToken).ConfigureAwait(false);
        if (candidate is null
            || await WasOfferedRecentlyAsync(connection, candidate, now, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var suggestion = new CompanionSuggestion(
            Guid.CreateVersion7(),
            candidate.Type,
            candidate.NoteId,
            $"{candidate.BaseDedupeKey}:{now:yyyyMMddHH}",
            now,
            now.AddHours(1));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO companion_suggestions(
                id, companion_id, suggestion_type, source_note_id, dedupe_key, status,
                created_at, expires_at, payload_json)
            VALUES ($id, $companion, $type, $note, $dedupe, 0, $created, $expires, '{}');
            """;
        command.Parameters.AddWithValue("$id", suggestion.Id.ToString());
        command.Parameters.AddWithValue("$companion", SqliteCompanionRepository.DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$type", (int)suggestion.Type);
        command.Parameters.AddWithValue("$note", suggestion.NoteId.ToString());
        command.Parameters.AddWithValue("$dedupe", suggestion.DedupeKey);
        command.Parameters.AddWithValue("$created", SqliteTime.ToDb(suggestion.CreatedAt.ToUniversalTime()));
        command.Parameters.AddWithValue("$expires", SqliteTime.ToDb(suggestion.ExpiresAt.ToUniversalTime()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return suggestion;
    }

    public async Task SetStatusAsync(
        Guid suggestionId,
        CompanionSuggestionStatus status,
        DateTimeOffset actedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE companion_suggestions
            SET status = $status, acted_at = $acted
            WHERE id = $id AND status = 0;
            """;
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$acted", SqliteTime.ToDb(actedAt.ToUniversalTime()));
        command.Parameters.AddWithValue("$id", suggestionId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsWithinBudgetAsync(
        SqliteConnection connection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN created_at >= $hour THEN 1 ELSE 0 END),
                SUM(CASE WHEN created_at >= $day THEN 1 ELSE 0 END)
            FROM companion_suggestions
            WHERE companion_id = $companion;
            """;
        command.Parameters.AddWithValue("$hour", SqliteTime.ToDb(now.AddHours(-1).ToUniversalTime()));
        command.Parameters.AddWithValue("$day", SqliteTime.ToDb(now.AddHours(-24).ToUniversalTime()));
        command.Parameters.AddWithValue("$companion", SqliteCompanionRepository.DefaultProfileId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var hour = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        var day = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        return hour < 2 && day < 5;
    }

    private static async Task<Candidate?> FindUpcomingReminderAsync(
        SqliteConnection connection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.note_id
            FROM reminders r
            JOIN notes n ON n.id = r.note_id
            WHERE r.completed_at IS NULL AND n.deleted_at IS NULL
              AND r.due_at >= $now AND r.due_at <= $soon
            ORDER BY r.due_at LIMIT 1;
            """;
        command.Parameters.AddWithValue("$now", SqliteTime.ToDb(now.ToUniversalTime()));
        command.Parameters.AddWithValue("$soon", SqliteTime.ToDb(now.AddMinutes(30).ToUniversalTime()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new Candidate(
                CompanionSuggestionType.UpcomingReminder,
                Guid.Parse(reader.GetString(1)),
                $"reminder:{reader.GetString(0)}")
            : null;
    }

    private static async Task<Candidate?> FindStaleChecklistAsync(
        SqliteConnection connection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.note_id
            FROM checklist_items c
            JOIN notes n ON n.id = c.note_id
            WHERE c.is_done = 0 AND n.deleted_at IS NULL AND n.updated_at <= $stale
            GROUP BY c.note_id
            ORDER BY n.updated_at LIMIT 1;
            """;
        command.Parameters.AddWithValue("$stale", SqliteTime.ToDb(now.AddDays(-7).ToUniversalTime()));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return Guid.TryParse(value, out var noteId)
            ? new Candidate(CompanionSuggestionType.StaleChecklist, noteId, $"stale:{noteId:N}")
            : null;
    }

    private static async Task<bool> WasOfferedRecentlyAsync(
        SqliteConnection connection,
        Candidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM companion_suggestions
                WHERE companion_id = $companion
                  AND suggestion_type = $type
                  AND source_note_id = $note
                  AND created_at >= $since);
            """;
        command.Parameters.AddWithValue("$companion", SqliteCompanionRepository.DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$type", (int)candidate.Type);
        command.Parameters.AddWithValue("$note", candidate.NoteId.ToString());
        command.Parameters.AddWithValue("$since", SqliteTime.ToDb(now.AddHours(-24).ToUniversalTime()));
        var recentlyOffered = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
        if (recentlyOffered)
        {
            return true;
        }

        // Three consecutive dismissals mute this trigger for seven days. Acting on any card
        // breaks the sequence, so one unwanted category never silences the whole companion.
        await using var ignored = connection.CreateCommand();
        ignored.CommandText = """
            SELECT status, acted_at
            FROM companion_suggestions
            WHERE companion_id = $companion
              AND suggestion_type = $type
              AND source_note_id = $note
            ORDER BY created_at DESC LIMIT 3;
            """;
        ignored.Parameters.AddWithValue("$companion", SqliteCompanionRepository.DefaultProfileId.ToString());
        ignored.Parameters.AddWithValue("$type", (int)candidate.Type);
        ignored.Parameters.AddWithValue("$note", candidate.NoteId.ToString());
        await using var reader = await ignored.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var dismissals = 0;
        DateTimeOffset? latestDismissal = null;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetInt32(0) != (int)CompanionSuggestionStatus.Dismissed)
            {
                return false;
            }

            dismissals++;
            if (latestDismissal is null && !reader.IsDBNull(1))
            {
                latestDismissal = SqliteTime.FromDb(reader.GetString(1));
            }
        }

        return dismissals == 3 && latestDismissal > now.AddDays(-7).ToUniversalTime();
    }

    private sealed record Candidate(CompanionSuggestionType Type, Guid NoteId, string BaseDedupeKey);
}
