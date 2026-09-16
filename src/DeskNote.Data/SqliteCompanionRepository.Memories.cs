using System.Globalization;
using DeskNote.Companion.Core;
using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

public sealed partial class SqliteCompanionRepository : ICompanionMemoryRepository
{
    public async Task<IReadOnlyList<CompanionMemory>> ReadMemoriesAsync(
        CompanionMemoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Offset);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadMemoriesAsync(connection, null, query, CompanionMemoryQuery.PageSize, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<CompanionMemory>> ReadMemoriesAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CompanionMemoryQuery query,
        int limit, CancellationToken cancellationToken)
    {
        await using var command = MetaCommand(connection, transaction, """
            SELECT r.id, a.template_id, a.template_version, r.pet_kind, r.occurred_at, r.local_date
            FROM companion_meta_reward_ledger r
            JOIN companion_memory_album a ON a.reward_id = r.id
            WHERE r.companion_id = $companion
              AND ($pet IS NULL OR r.pet_kind = $pet)
              AND ($date IS NULL OR r.local_date = $date)
            ORDER BY r.local_date DESC, r.id DESC LIMIT $limit OFFSET $offset;
            """);
        command.Parameters.AddWithValue("$pet", query.Pet?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$date", query.Date is { } date ? Date(date) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", query.Offset);
        var result = new List<CompanionMemory>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new CompanionMemory(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                Enum.Parse<CompanionPetKind>(reader.GetString(3)), SqliteTime.FromDb(reader.GetString(4)),
                DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private static SqliteCommand MetaCommand(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$companion", DefaultProfileId.ToString());
        return command;
    }

    private static async Task EnsureMetaAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        await using var command = MetaCommand(connection, transaction, """
            INSERT INTO companion_meta_state(companion_id, activated_date)
            VALUES ($companion, $date) ON CONFLICT DO NOTHING;
            """);
        command.Parameters.AddWithValue("$date", Date(DateOnly.FromDateTime(now.LocalDateTime)));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            return;
        }

        // Existing progress is an entitlement, not evidence of a historical evolution date.
        foreach (var pet in CompanionPetCatalog.All)
        {
            var stage = await ReadStageAsync(connection, transaction, pet, cancellationToken).ConfigureAwait(false);
            if (stage < 2)
            {
                continue;
            }

            await UnlockThroughAsync(connection, transaction, pet, stage, now, false, cancellationToken)
                .ConfigureAwait(false);
            await GrantMemoryAsync(connection, transaction, $"baseline:{pet}", "WelcomeBack", pet,
                now, DateOnly.FromDateTime(now.LocalDateTime), null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> ReadStageAsync(
        SqliteConnection connection, SqliteTransaction transaction, CompanionPetKind pet, CancellationToken cancellationToken)
    {
        await using var command = MetaCommand(connection, transaction, """
            SELECT experience, care_days FROM companion_pet_progress
            WHERE companion_id = $companion AND pet_kind = $pet;
            """);
        command.Parameters.AddWithValue("$pet", pet.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? CompanionGrowthLadder.StageFor(reader.GetInt32(0), reader.GetInt32(1)) : 1;
    }

    private static async Task<CompanionMissionProgress> ReadMissionAsync(
        SqliteConnection connection, SqliteTransaction? transaction, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = MetaCommand(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM companion_event_ledger
                WHERE companion_id = $companion AND local_date = $date
                  AND rule_version = $rule AND experience_delta > 0);
            """);
        command.Parameters.AddWithValue("$date", Date(date));
        command.Parameters.AddWithValue("$rule", CompanionBalanceV2.RuleVersion);
        var appDone = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
        command.CommandText = """
            SELECT DISTINCT care_action FROM companion_care_ledger
            WHERE companion_id = $companion AND local_date = $date AND points > 0 ORDER BY care_action;
            """;
        var care = new List<CompanionCareAction>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            care.Add((CompanionCareAction)reader.GetInt32(0));
        }

        return new CompanionMissionProgress(appDone, care);
    }

    private static async Task ReconcileMetaAsync(
        SqliteConnection connection, SqliteTransaction transaction, CompanionPetKind pet,
        DateTimeOffset now, DateOnly date, CompanionActivity? activity, CancellationToken cancellationToken)
    {
        await using var command = MetaCommand(connection, transaction,
            "SELECT activated_date FROM companion_meta_state WHERE companion_id = $companion;");
        var activated = (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        if (string.CompareOrdinal(Date(date), activated) < 0)
        {
            return;
        }

        if (activity is not null && CompanionMemoryCatalog.ForActivity(activity.Type) is { } template)
        {
            await GrantMemoryAsync(connection, transaction, $"activity-day:{Date(date)}", template,
                pet, now, date, activity.SourceEventId, cancellationToken).ConfigureAwait(false);
        }

        var mission = await ReadMissionAsync(connection, transaction, date, cancellationToken).ConfigureAwait(false);
        if (mission.IsComplete)
        {
            await GrantMemoryAsync(connection, transaction, $"daily-mission:{Date(date)}", "Mission",
                pet, now, date, activity?.SourceEventId, cancellationToken).ConfigureAwait(false);
        }

        var stage = await ReadStageAsync(connection, transaction, pet, cancellationToken).ConfigureAwait(false);
        await UnlockThroughAsync(connection, transaction, pet, stage, now, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UnlockThroughAsync(
        SqliteConnection connection, SqliteTransaction transaction, CompanionPetKind pet, int stage,
        DateTimeOffset now, bool awardCards, CancellationToken cancellationToken)
    {
        foreach (var unlock in CompanionUnlockCatalog.All.Where(item => item.Stage <= stage))
        {
            await using var command = MetaCommand(connection, transaction, """
                INSERT INTO companion_unlocks(companion_id, pet_kind, content_key, unlocked_at)
                VALUES ($companion, $pet, $content, $now) ON CONFLICT DO NOTHING;
                """);
            command.Parameters.AddWithValue("$pet", pet.ToString());
            command.Parameters.AddWithValue("$content", unlock.Key);
            command.Parameters.AddWithValue("$now", SqliteTime.ToDb(now));
            var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (inserted > 0 && awardCards)
            {
                await GrantMemoryAsync(connection, transaction, $"stage:{pet}:{unlock.Stage}", $"Stage{unlock.Stage}",
                    pet, now, DateOnly.FromDateTime(now.LocalDateTime), null, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task GrantMemoryAsync(
        SqliteConnection connection, SqliteTransaction transaction, string key, string template,
        CompanionPetKind pet, DateTimeOffset now, DateOnly date, string? source, CancellationToken cancellationToken)
    {
        await using var command = MetaCommand(connection, transaction, """
            INSERT INTO companion_meta_reward_ledger(
                companion_id, grant_key, policy_version, source_event_id, pet_kind, occurred_at, local_date)
            VALUES ($companion, $key, $version, $source, $pet, $now, $date)
            ON CONFLICT(companion_id, grant_key) DO NOTHING RETURNING id;
            """);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$version", CompanionMemoryCatalog.Version);
        command.Parameters.AddWithValue("$source", source ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$pet", pet.ToString());
        command.Parameters.AddWithValue("$now", SqliteTime.ToDb(now));
        command.Parameters.AddWithValue("$date", Date(date));
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not { } id)
        {
            return;
        }

        command.CommandText = """
            INSERT INTO companion_memory_album(reward_id, template_id, template_version)
            VALUES ($id, $template, $version);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$template", template);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CompanionSnapshot> EnrichSnapshotAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CompanionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var mission = await ReadMissionAsync(connection, transaction, snapshot.Today.LocalDate, cancellationToken)
            .ConfigureAwait(false);
        var memories = await ReadMemoriesAsync(connection, transaction, new CompanionMemoryQuery(), 1, cancellationToken)
            .ConfigureAwait(false);
        await using var command = MetaCommand(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM companion_meta_reward_ledger
                WHERE companion_id = $companion AND grant_key = $key);
            """);
        command.Parameters.AddWithValue("$key", $"daily-mission:{Date(snapshot.Today.LocalDate)}");
        var granted = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
        command.CommandText = """
            SELECT content_key FROM companion_unlocks WHERE companion_id = $companion AND pet_kind = $pet;
            """;
        command.Parameters.AddWithValue("$pet", CompanionPetCatalog.FromAssetKey(snapshot.Profile.AppearanceKey).ToString());
        var unlocks = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            unlocks.Add(reader.GetString(0));
        }

        return snapshot with
        {
            MissionProgress = mission,
            MissionRewardGranted = granted,
            LatestMemory = memories.FirstOrDefault(),
            Unlocks = unlocks,
        };
    }

    private static async Task<bool> CareRequestExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using var command = MetaCommand(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM companion_care_ledger
                WHERE companion_id = $companion AND request_id = $request);
            """);
        command.Parameters.AddWithValue("$request", requestId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }
}
