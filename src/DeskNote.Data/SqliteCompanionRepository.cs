using System.Globalization;
using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

/// <summary>SQLite event ledger and projection for the single local companion.</summary>
public sealed class SqliteCompanionRepository(
    SqliteConnectionFactory connectionFactory,
    RewardPolicy rewardPolicy) : ICompanionRepository
{
    // A stable local profile keeps one growth history through restarts and rule upgrades.
    public static readonly Guid DefaultProfileId = new("9e8ac9ce-2bf3-4e19-93a5-d154fa03b7b9");

    public async Task<CompanionSnapshot> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureProfileAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false);
        return await ReadSnapshotAsync(
            connection,
            transaction: null,
            DateOnly.FromDateTime(DateTime.Now),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompanionRecordResult> RecordAsync(
        CompanionActivity activity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // This harmless write serializes concurrent reward decisions before the daily count read.
        await EnsureProfileAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var selectedPet = await ReadSelectedPetAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        var existing = await EventExistsAsync(connection, transaction, activity, cancellationToken)
            .ConfigureAwait(false);
        if (existing)
        {
            var duplicateSnapshot = await ReadSnapshotAsync(connection, transaction, activity.LocalDate, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CompanionRecordResult(false, duplicateSnapshot);
        }

        var acceptedToday = await CountRewardedTodayAsync(connection, transaction, activity, cancellationToken)
            .ConfigureAwait(false);
        var reward = rewardPolicy.Evaluate(activity, acceptedToday);

        await InsertEventAsync(connection, transaction, activity, selectedPet, reward, cancellationToken)
            .ConfigureAwait(false);
        await ProjectPetProgressAsync(connection, transaction, selectedPet, reward, cancellationToken)
            .ConfigureAwait(false);
        await UpdateDragonUnlockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await ProjectDailyProgressAsync(connection, transaction, activity, cancellationToken)
            .ConfigureAwait(false);

        var snapshot = await ReadSnapshotAsync(connection, transaction, activity.LocalDate, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CompanionRecordResult(true, snapshot);
    }

    public async Task<CompanionSnapshot> ChooseRitualAsync(
        DateOnly localDate,
        DailyRitualKind ritual,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnsureProfileAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO companion_daily_progress(companion_id, local_date, chosen_ritual)
                VALUES ($companion, $date, $ritual)
                ON CONFLICT(companion_id, local_date) DO UPDATE SET
                    chosen_ritual = excluded.chosen_ritual,
                    ritual_completed = CASE excluded.chosen_ritual
                        WHEN 1 THEN companion_daily_progress.capture_count > 0
                        WHEN 2 THEN companion_daily_progress.recall_count > 0
                        WHEN 3 THEN companion_daily_progress.resolve_count > 0
                        ELSE 0 END;
                """;
            command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
            command.Parameters.AddWithValue("$date", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$ritual", (int)ritual);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await ReadSnapshotAsync(connection, transaction, localDate, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private static async Task EnsureProfileAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO companion_profiles(
                id, name, appearance_key, created_at, enabled, rule_version)
            VALUES ($id, 'Mori', 'seed-v1', $createdAt, 1, $ruleVersion);
            """;
        command.Parameters.AddWithValue("$id", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$createdAt", SqliteTime.ToDb(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV1.RuleVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> EventExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionActivity activity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM companion_event_ledger
                WHERE source_event_id = $source AND rule_version = $ruleVersion);
            """;
        command.Parameters.AddWithValue("$source", activity.SourceEventId);
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV1.RuleVersion);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<int> CountRewardedTodayAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionActivity activity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM companion_event_ledger
            WHERE companion_id = $companion
              AND local_date = $date
              AND event_type = $type
              AND curiosity_delta + insight_delta + reliability_delta > 0;
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$date", activity.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$type", (int)activity.Type);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionActivity activity,
        CompanionPetKind pet,
        RewardDelta reward,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO companion_event_ledger(
                id, companion_id, source_event_id, event_type, note_id, source_entity_id,
                curiosity_delta, insight_delta, reliability_delta, occurred_at, local_date,
                rule_version, payload_json, pet_kind)
            VALUES (
                $id, $companion, $source, $type, $note, $entity,
                $curiosity, $insight, $reliability, $occurred, $date, $ruleVersion, NULL, $pet);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$source", activity.SourceEventId);
        command.Parameters.AddWithValue("$type", (int)activity.Type);
        command.Parameters.AddWithValue("$note", activity.NoteId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$entity", activity.SourceEntityId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$curiosity", reward.Curiosity);
        command.Parameters.AddWithValue("$insight", reward.Insight);
        command.Parameters.AddWithValue("$reliability", reward.Reliability);
        command.Parameters.AddWithValue("$occurred", SqliteTime.ToDb(activity.OccurredAt.ToUniversalTime()));
        command.Parameters.AddWithValue("$date", activity.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV1.RuleVersion);
        command.Parameters.AddWithValue("$pet", pet.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ProjectPetProgressAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionPetKind pet,
        RewardDelta reward,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO companion_pet_progress(
                companion_id, pet_kind, curiosity, insight, reliability)
            VALUES ($companion, $pet, $curiosity, $insight, $reliability)
            ON CONFLICT(companion_id, pet_kind) DO UPDATE SET
                curiosity = MIN(100, companion_pet_progress.curiosity + excluded.curiosity),
                insight = MIN(100, companion_pet_progress.insight + excluded.insight),
                reliability = MIN(100, companion_pet_progress.reliability + excluded.reliability);
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$pet", pet.ToString());
        command.Parameters.AddWithValue("$curiosity", reward.Curiosity);
        command.Parameters.AddWithValue("$insight", reward.Insight);
        command.Parameters.AddWithValue("$reliability", reward.Reliability);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateDragonUnlockAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = """
            SELECT COUNT(*)
            FROM companion_pet_progress
            WHERE companion_id = $companion
              AND pet_kind IN ('Rabbit', 'Cat', 'Dog', 'FennecFox', 'Otter', 'Monkey')
              AND curiosity + insight + reliability >= $required;
            """;
        count.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        count.Parameters.AddWithValue("$required", CompanionPetCatalog.FullyRaisedTotal);
        var raised = Convert.ToInt32(
            await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (raised < CompanionPetCatalog.RequiredForDragon.Count)
        {
            return;
        }

        await using var unlock = connection.CreateCommand();
        unlock.Transaction = transaction;
        unlock.CommandText = """
            INSERT INTO settings(key, value) VALUES ($key, 'true')
            ON CONFLICT(key) DO UPDATE SET value = 'true';
            """;
        unlock.Parameters.AddWithValue("$key", SettingKeys.CompanionDragonUnlocked);
        await unlock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ProjectDailyProgressAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionActivity activity,
        CancellationToken cancellationToken)
    {
        var capture = activity.Type == CompanionActivityType.MeaningfulCapture ? 1 : 0;
        var recall = activity.Type is CompanionActivityType.UsefulRecall
            or CompanionActivityType.BriefingEvidenceOpened ? 1 : 0;
        var resolve = activity.Type is CompanionActivityType.ChecklistCompleted
            or CompanionActivityType.ReminderHandled
            or CompanionActivityType.AiSuggestionAccepted ? 1 : 0;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO companion_daily_progress(
                companion_id, local_date, capture_count, recall_count, resolve_count)
            VALUES ($companion, $date, $capture, $recall, $resolve)
            ON CONFLICT(companion_id, local_date) DO UPDATE SET
                capture_count = companion_daily_progress.capture_count + excluded.capture_count,
                recall_count = companion_daily_progress.recall_count + excluded.recall_count,
                resolve_count = companion_daily_progress.resolve_count + excluded.resolve_count,
                ritual_completed = CASE companion_daily_progress.chosen_ritual
                    WHEN 1 THEN companion_daily_progress.capture_count + excluded.capture_count > 0
                    WHEN 2 THEN companion_daily_progress.recall_count + excluded.recall_count > 0
                    WHEN 3 THEN companion_daily_progress.resolve_count + excluded.resolve_count > 0
                    ELSE 0 END;
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$date", activity.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$capture", capture);
        command.Parameters.AddWithValue("$recall", recall);
        command.Parameters.AddWithValue("$resolve", resolve);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CompanionSnapshot> ReadSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var selectedPet = await ReadSelectedPetAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.name, p.created_at, p.rule_version,
                   COALESCE(g.curiosity, 0),
                   COALESCE(g.insight, 0),
                   COALESCE(g.reliability, 0)
            FROM companion_profiles p
            LEFT JOIN companion_pet_progress g
              ON g.companion_id = p.id AND g.pet_kind = $pet
            WHERE p.id = $id
            """;
        command.Parameters.AddWithValue("$id", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$pet", selectedPet.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The companion profile could not be read after creation.");
        }

        var profile = new CompanionProfile(
            DefaultProfileId,
            reader.GetString(0),
            selectedPet.AssetKey(),
            SqliteTime.FromDb(reader.GetString(1)),
            reader.GetInt32(2));
        var growth = new GrowthState(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
        await reader.DisposeAsync().ConfigureAwait(false);

        await using var latest = connection.CreateCommand();
        latest.Transaction = transaction;
        latest.CommandText = """
            SELECT event_type, curiosity_delta, insight_delta, reliability_delta, occurred_at
            FROM companion_event_ledger
            WHERE companion_id = $id AND pet_kind = $pet
            ORDER BY rowid DESC LIMIT 1;
            """;
        latest.Parameters.AddWithValue("$id", DefaultProfileId.ToString());
        latest.Parameters.AddWithValue("$pet", selectedPet.ToString());
        await using var lastReader = await latest.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await lastReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            await lastReader.DisposeAsync().ConfigureAwait(false);
            return new CompanionSnapshot(
                profile,
                growth,
                await ReadDailyProgressAsync(connection, transaction, localDate, cancellationToken).ConfigureAwait(false),
                RewardDelta.None,
                null,
                null);
        }

        var lastReward = new RewardDelta(lastReader.GetInt32(1), lastReader.GetInt32(2), lastReader.GetInt32(3));
        var lastType = (CompanionActivityType)lastReader.GetInt32(0);
        var lastAt = SqliteTime.FromDb(lastReader.GetString(4));
        await lastReader.DisposeAsync().ConfigureAwait(false);
        return new CompanionSnapshot(
            profile,
            growth,
            await ReadDailyProgressAsync(connection, transaction, localDate, cancellationToken).ConfigureAwait(false),
            lastReward,
            lastType,
            lastAt);
    }

    private static async Task<CompanionPetKind> ReadSelectedPetAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", SettingKeys.CompanionSelectedPet);
        var raw = (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
        var selected = Enum.TryParse<CompanionPetKind>(raw, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : CompanionPetKind.Rabbit;

        if (selected != CompanionPetKind.Dragon)
        {
            return selected;
        }

        command.Parameters.Clear();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", SettingKeys.CompanionDragonUnlocked);
        var unlocked = string.Equals(
            (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString(),
            "true",
            StringComparison.OrdinalIgnoreCase);
        return unlocked ? selected : CompanionPetKind.Rabbit;
    }

    private static async Task<DailyProgress> ReadDailyProgressAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT capture_count, recall_count, resolve_count, chosen_ritual, ritual_completed
            FROM companion_daily_progress
            WHERE companion_id = $companion AND local_date = $date;
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$date", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new DailyProgress(localDate);
        }

        return new DailyProgress(
            localDate,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : (DailyRitualKind)reader.GetInt32(3),
            reader.GetInt32(4) == 1);
    }
}
