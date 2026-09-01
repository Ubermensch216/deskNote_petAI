using System.Globalization;
using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

/// <summary>SQLite event ledgers and projections for the single local companion.</summary>
/// <remarks>
/// Two ledgers, one per budget: note activity lands in <c>companion_event_ledger</c> and hands-on
/// care in <c>companion_care_ledger</c>. Care days and the current needs are both derived from the
/// care ledger rather than stored, so they cannot drift away from the events that caused them.
/// </remarks>
public sealed class SqliteCompanionRepository(
    SqliteConnectionFactory connectionFactory,
    RewardPolicy rewardPolicy,
    CarePolicy carePolicy) : ICompanionRepository
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
            DateTimeOffset.Now,
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

        if (await EventExistsAsync(connection, transaction, activity, cancellationToken).ConfigureAwait(false))
        {
            var duplicateSnapshot = await ReadSnapshotAsync(
                connection,
                transaction,
                activity.OccurredAt,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CompanionRecordResult(false, duplicateSnapshot);
        }

        var acceptedToday = await CountRewardedTodayAsync(connection, transaction, activity, cancellationToken)
            .ConfigureAwait(false);
        var reward = rewardPolicy.Evaluate(activity, acceptedToday);

        await InsertEventAsync(connection, transaction, activity, selectedPet, reward, cancellationToken)
            .ConfigureAwait(false);
        await AddExperienceAsync(connection, transaction, selectedPet, reward, cancellationToken)
            .ConfigureAwait(false);
        await UpdateDragonUnlockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        var snapshot = await ReadSnapshotAsync(connection, transaction, activity.OccurredAt, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CompanionRecordResult(true, snapshot);
    }

    public async Task<CompanionCareResult> PerformCareAsync(
        CareRequest request,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await EnsureProfileAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var pet = await ReadSelectedPetAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var localDate = DateOnly.FromDateTime(occurredAt.LocalDateTime);

        var needs = await ProjectNeedsAsync(connection, transaction, pet, occurredAt, cancellationToken)
            .ConfigureAwait(false);
        var today = await ReadDailyProgressAsync(connection, transaction, pet, localDate, cancellationToken)
            .ConfigureAwait(false);

        var outcome = carePolicy.Evaluate(
            request,
            needs,
            today.Count(request.Action),
            today.PlayKinds);

        if (!outcome.Accepted)
        {
            var refusedSnapshot = await ReadSnapshotAsync(connection, transaction, occurredAt, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CompanionCareResult(false, outcome.Refusal, refusedSnapshot);
        }

        await InsertCareAsync(connection, transaction, pet, request, outcome, occurredAt, localDate, cancellationToken)
            .ConfigureAwait(false);
        await AddExperienceAsync(connection, transaction, pet, outcome.Reward, cancellationToken)
            .ConfigureAwait(false);
        await RecountCareDaysAsync(connection, transaction, pet, cancellationToken).ConfigureAwait(false);
        await UpdateDragonUnlockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        var snapshot = await ReadSnapshotAsync(connection, transaction, occurredAt, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CompanionCareResult(true, CareRefusal.None, snapshot);
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
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV2.RuleVersion);
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
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV2.RuleVersion);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>
    /// Counts against the shared allowance of the category, not the single event type: closing a
    /// checklist item and handling a reminder are one "closed a loop" budget to the user.
    /// </summary>
    private static async Task<int> CountRewardedTodayAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionActivity activity,
        CancellationToken cancellationToken)
    {
        var category = CompanionBalanceV2.CategoryOf(activity.Type);
        var types = CompanionBalanceV2.TypesIn(category);
        var placeholders = string.Join(", ", types.Select((_, index) => $"$type{index}"));

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT COUNT(*) FROM companion_event_ledger
            WHERE companion_id = $companion
              AND local_date = $date
              AND rule_version = $ruleVersion
              AND event_type IN ({placeholders})
              AND experience_delta > 0;
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$date", Date(activity.LocalDate));
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV2.RuleVersion);
        for (var index = 0; index < types.Count; index++)
        {
            command.Parameters.AddWithValue($"$type{index}", (int)types[index]);
        }

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
                curiosity_delta, insight_delta, reliability_delta, experience_delta,
                occurred_at, local_date, rule_version, payload_json, pet_kind)
            VALUES (
                $id, $companion, $source, $type, $note, $entity,
                $curiosity, $insight, $reliability, $experience,
                $occurred, $date, $ruleVersion, NULL, $pet);
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
        command.Parameters.AddWithValue("$experience", reward.Experience);
        command.Parameters.AddWithValue("$occurred", SqliteTime.ToDb(activity.OccurredAt.ToUniversalTime()));
        command.Parameters.AddWithValue("$date", Date(activity.LocalDate));
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV2.RuleVersion);
        command.Parameters.AddWithValue("$pet", pet.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCareAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionPetKind pet,
        CareRequest request,
        CareOutcome outcome,
        DateTimeOffset occurredAt,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO companion_care_ledger(
                id, companion_id, pet_kind, care_action, play_kind, points,
                occurred_at, local_date, rule_version)
            VALUES ($id, $companion, $pet, $action, $play, $points, $occurred, $date, $ruleVersion);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$pet", pet.ToString());
        command.Parameters.AddWithValue("$action", (int)request.Action);
        command.Parameters.AddWithValue(
            "$play",
            request.PlayKind is { } kind ? (int)kind : (object)DBNull.Value);
        command.Parameters.AddWithValue("$points", outcome.Reward.Experience);
        command.Parameters.AddWithValue("$occurred", SqliteTime.ToDb(occurredAt.ToUniversalTime()));
        command.Parameters.AddWithValue("$date", Date(localDate));
        command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV2.RuleVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddExperienceAsync(
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
                companion_id, pet_kind, curiosity, insight, reliability, experience, care_days)
            VALUES ($companion, $pet, $curiosity, $insight, $reliability, $experience, 0)
            ON CONFLICT(companion_id, pet_kind) DO UPDATE SET
                curiosity = companion_pet_progress.curiosity + excluded.curiosity,
                insight = companion_pet_progress.insight + excluded.insight,
                reliability = companion_pet_progress.reliability + excluded.reliability,
                experience = companion_pet_progress.experience + excluded.experience;
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$pet", pet.ToString());
        command.Parameters.AddWithValue("$curiosity", reward.Curiosity);
        command.Parameters.AddWithValue("$insight", reward.Insight);
        command.Parameters.AddWithValue("$reliability", reward.Reliability);
        command.Parameters.AddWithValue("$experience", reward.Experience);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Recomputes care days from the ledger rather than incrementing a counter, so a day that
    /// crosses the threshold late still counts exactly once.
    /// </summary>
    private static async Task RecountCareDaysAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompanionPetKind pet,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE companion_pet_progress
            SET care_days = (
                SELECT COUNT(*) FROM (
                    SELECT local_date
                    FROM companion_care_ledger
                    WHERE companion_id = $companion AND pet_kind = $pet
                    GROUP BY local_date
                    HAVING SUM(points) >= $threshold))
            WHERE companion_id = $companion AND pet_kind = $pet;
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$pet", pet.ToString());
        command.Parameters.AddWithValue("$threshold", CompanionBalanceV2.CareDayThreshold);
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
              AND experience >= $experience
              AND care_days >= $careDays;
            """;
        count.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        count.Parameters.AddWithValue("$experience", CompanionPetCatalog.FullyRaisedExperience);
        count.Parameters.AddWithValue("$careDays", CompanionPetCatalog.FullyRaisedCareDays);
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

    private static async Task<CompanionSnapshot> ReadSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var selectedPet = await ReadSelectedPetAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        var localDate = DateOnly.FromDateTime(now.LocalDateTime);

        CompanionProfile profile;
        GrowthState growth;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT p.name, p.created_at, p.rule_version,
                       COALESCE(g.experience, 0),
                       COALESCE(g.care_days, 0),
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

            profile = new CompanionProfile(
                DefaultProfileId,
                reader.GetString(0),
                selectedPet.AssetKey(),
                SqliteTime.FromDb(reader.GetString(1)),
                reader.GetInt32(2));
            growth = new GrowthState(
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7));
        }

        var needs = await ProjectNeedsAsync(connection, transaction, selectedPet, now, cancellationToken)
            .ConfigureAwait(false);
        var today = await ReadDailyProgressAsync(connection, transaction, selectedPet, localDate, cancellationToken)
            .ConfigureAwait(false);

        await using var latest = connection.CreateCommand();
        latest.Transaction = transaction;
        latest.CommandText = """
            SELECT event_type, experience_delta, curiosity_delta, insight_delta, reliability_delta,
                   occurred_at
            FROM companion_event_ledger
            WHERE companion_id = $id AND pet_kind = $pet
            ORDER BY rowid DESC LIMIT 1;
            """;
        latest.Parameters.AddWithValue("$id", DefaultProfileId.ToString());
        latest.Parameters.AddWithValue("$pet", selectedPet.ToString());
        await using var lastReader = await latest.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await lastReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new CompanionSnapshot(profile, growth, needs, today, RewardDelta.None, null, null);
        }

        var lastReward = new RewardDelta(
            lastReader.GetInt32(1),
            lastReader.GetInt32(2),
            lastReader.GetInt32(3),
            lastReader.GetInt32(4));
        var lastType = (CompanionActivityType)lastReader.GetInt32(0);
        var lastAt = SqliteTime.FromDb(lastReader.GetString(5));
        return new CompanionSnapshot(profile, growth, needs, today, lastReward, lastType, lastAt);
    }

    private static async Task<CompanionNeeds> ProjectNeedsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CompanionPetKind pet,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var care = new List<CareEvent>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT care_action, occurred_at
                FROM companion_care_ledger
                WHERE companion_id = $companion AND pet_kind = $pet AND occurred_at >= $since
                ORDER BY occurred_at;
                """;
            command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
            command.Parameters.AddWithValue("$pet", pet.ToString());
            command.Parameters.AddWithValue("$since", SqliteTime.ToDb(now - NeedsProjector.ReplayWindow));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                care.Add(new CareEvent(
                    (CompanionCareAction)reader.GetInt32(0),
                    SqliteTime.FromDb(reader.GetString(1))));
            }
        }

        var adoptedAt = await ReadAdoptedAtAsync(connection, transaction, pet, cancellationToken)
            .ConfigureAwait(false);
        var careDays = await ReadCareDaysAsync(connection, transaction, pet, cancellationToken)
            .ConfigureAwait(false);
        return NeedsProjector.At(now, adoptedAt, care, careDays);
    }

    /// <summary>
    /// When this species first earned anything. A pet nobody has met yet starts content instead
    /// of starving, which is what this timestamp anchors.
    /// </summary>
    private static async Task<DateTimeOffset> ReadAdoptedAtAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CompanionPetKind pet,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MIN(occurred_at) FROM (
                SELECT occurred_at FROM companion_care_ledger
                WHERE companion_id = $companion AND pet_kind = $pet
                UNION ALL
                SELECT occurred_at FROM companion_event_ledger
                WHERE companion_id = $companion AND pet_kind = $pet);
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$pet", pet.ToString());
        var raw = (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
        return SqliteTime.FromDbNullable(raw) ?? DateTimeOffset.Now;
    }

    private static async Task<int> ReadCareDaysAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CompanionPetKind pet,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(care_days, 0) FROM companion_pet_progress
            WHERE companion_id = $companion AND pet_kind = $pet;
            """;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        command.Parameters.AddWithValue("$pet", pet.ToString());
        var raw = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return raw is null or DBNull ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
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
        CompanionPetKind pet,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var appScore = 0;
        var appCounts = new Dictionary<AppScoreCategory, int>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT event_type, COUNT(*), COALESCE(SUM(experience_delta), 0)
                FROM companion_event_ledger
                WHERE companion_id = $companion
                  AND pet_kind = $pet
                  AND local_date = $date
                  AND rule_version = $ruleVersion
                  AND experience_delta > 0
                GROUP BY event_type;
                """;
            command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
            command.Parameters.AddWithValue("$pet", pet.ToString());
            command.Parameters.AddWithValue("$date", Date(localDate));
            command.Parameters.AddWithValue("$ruleVersion", CompanionBalanceV2.RuleVersion);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var category = CompanionBalanceV2.CategoryOf((CompanionActivityType)reader.GetInt32(0));
                appCounts[category] = appCounts.GetValueOrDefault(category) + reader.GetInt32(1);
                appScore += reader.GetInt32(2);
            }
        }

        var careScore = 0;
        var careCounts = new Dictionary<CompanionCareAction, int>();
        var playKinds = new List<CompanionPlayKind>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT care_action, play_kind, points
                FROM companion_care_ledger
                WHERE companion_id = $companion AND pet_kind = $pet AND local_date = $date
                ORDER BY occurred_at;
                """;
            command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
            command.Parameters.AddWithValue("$pet", pet.ToString());
            command.Parameters.AddWithValue("$date", Date(localDate));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var action = (CompanionCareAction)reader.GetInt32(0);
                careCounts[action] = careCounts.GetValueOrDefault(action) + 1;
                careScore += reader.GetInt32(2);
                if (reader.IsDBNull(1))
                {
                    continue;
                }

                var kind = (CompanionPlayKind)reader.GetInt32(1);
                if (!playKinds.Contains(kind))
                {
                    playKinds.Add(kind);
                }
            }
        }

        return new DailyProgress
        {
            LocalDate = localDate,
            AppScore = appScore,
            CareScore = careScore,
            AppCounts = appCounts,
            CareCounts = careCounts,
            PlayKinds = playKinds,
        };
    }

    private static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
