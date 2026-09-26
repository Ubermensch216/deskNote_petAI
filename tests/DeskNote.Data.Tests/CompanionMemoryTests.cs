using Dapper;
using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;

namespace DeskNote.Data.Tests;

public class CompanionMemoryTests
{
    private static SqliteCompanionRepository Repository(TestDatabase database) =>
        new(database.Factory, new RewardPolicy(), new CarePolicy());

    private static CompanionActivity Capture(string id, DateTimeOffset now, CompanionPetKind? pet = null) =>
        new(id, CompanionActivityType.MeaningfulCapture, now, DateOnly.FromDateTime(now.LocalDateTime), null, null)
        { Pet = pet };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mission_rewards_once_whichever_kind_of_action_finishes_it(bool appLast)
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        var now = DateTimeOffset.Now;
        var activity = Capture("one-note", now);
        if (!appLast)
        {
            await repository.RecordAsync(activity);
        }

        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Greeting), now);
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Rest), now);
        if (appLast)
        {
            await repository.RecordAsync(activity);
        }

        for (var i = 0; i < 3; i++)
        {
            await Repository(db).RecordAsync(activity);
            var snapshot = await Repository(db).GetOrCreateAsync();
            Assert.True(snapshot.MissionProgress.IsComplete);
            Assert.True(snapshot.MissionRewardGranted);
            Assert.Equal(15, snapshot.Growth.Experience);
        }

        var album = await repository.ReadMemoriesAsync(new());
        Assert.Single(album, card => card.TemplateId == "Mission");
        Assert.Single(album, card => card.TemplateId == "Capture");
    }

    [Fact]
    public async Task Mission_combines_pets_without_resetting_the_shared_app_budget()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        var now = DateTimeOffset.Now;
        await repository.RecordAsync(Capture("rabbit", now));
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Greeting), now);
        await using var connection = await db.Factory.OpenAsync();
        await connection.ExecuteAsync("INSERT OR REPLACE INTO settings(key, value) VALUES (@key, 'Cat');",
            new { key = SettingKeys.CompanionSelectedPet });
        var result = await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Rest), now);
        Assert.True(result.Snapshot.MissionProgress.IsComplete);
        Assert.True(result.Snapshot.MissionRewardGranted);
        Assert.Equal(5, result.Snapshot.Growth.Experience);
        Assert.Equal(0, result.Snapshot.Today.AppScore);
        var mission = Assert.Single(await repository.ReadMemoriesAsync(new()), card => card.TemplateId == "Mission");
        Assert.Equal(CompanionPetKind.Cat, mission.Pet);
        await repository.RecordAsync(Capture("cat", now));
        var capped = await repository.RecordAsync(Capture("cat-extra", now));
        Assert.Equal(10, capped.Snapshot.Growth.Experience);
        Assert.Single(await repository.ReadMemoriesAsync(new()), card => card.TemplateId == "Mission");
    }

    [Fact]
    public async Task Retrying_care_after_a_day_change_does_not_pay_again()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        var now = DateTimeOffset.Now;
        var request = new CareRequest(CompanionCareAction.SpecialCare);
        Assert.True((await repository.PerformCareAsync(request, now)).Accepted);
        var retried = await repository.PerformCareAsync(request, now.AddDays(1));
        Assert.False(retried.Accepted);
        Assert.Equal(CareRefusal.AlreadyProcessed, retried.Refusal);
        Assert.Equal(7, retried.Snapshot.Growth.Experience);
    }

    [Fact]
    public async Task Queued_activity_keeps_its_original_pet_after_selection_changes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        await repository.GetOrCreateAsync();
        await using var connection = await db.Factory.OpenAsync();
        await connection.ExecuteAsync("INSERT OR REPLACE INTO settings(key, value) VALUES (@key, 'Cat');",
            new { key = SettingKeys.CompanionSelectedPet });
        var result = await repository.RecordAsync(Capture("delayed-rabbit", DateTimeOffset.Now, CompanionPetKind.Rabbit));
        Assert.Equal("cat", result.Snapshot.Profile.AppearanceKey);
        Assert.Equal(0, result.Snapshot.Growth.Experience);
        Assert.Equal(CompanionPetKind.Rabbit, Assert.Single(await repository.ReadMemoriesAsync(new())).Pet);
    }

    [Fact]
    public async Task Existing_stage_is_honoured_without_fabricating_past_stage_dates()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var connection = await db.Factory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO companion_profiles(id, name, appearance_key, created_at, enabled, rule_version)
            VALUES (@id, 'Mori', 'rabbit', @now, 1, 2);
            INSERT INTO companion_pet_progress(companion_id, pet_kind, experience, care_days)
            VALUES (@id, 'Rabbit', 2200, 28);
            """, new
        {
            id = SqliteCompanionRepository.DefaultProfileId.ToString(),
            now = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture),
        });
        var repository = Repository(db);
        var snapshot = await repository.GetOrCreateAsync();
        Assert.Equal(5, snapshot.Growth.Stage);
        Assert.Equal(4, snapshot.Unlocks.Count);
        Assert.Equal("WelcomeBack", Assert.Single(await repository.ReadMemoriesAsync(new())).TemplateId);
        await repository.GetOrCreateAsync();
        Assert.Single(await repository.ReadMemoriesAsync(new()));
    }

    [Fact]
    public async Task A_new_stage_grants_a_card_and_reaction_once()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        await repository.GetOrCreateAsync();
        await using var connection = await db.Factory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO companion_pet_progress(companion_id, pet_kind, experience, care_days)
            VALUES (@id, 'Rabbit', 195, 0);
            """, new { id = SqliteCompanionRepository.DefaultProfileId.ToString() });
        var activity = Capture("evolve", DateTimeOffset.Now);
        var result = await repository.RecordAsync(activity);
        Assert.Equal(2, result.Snapshot.Growth.Stage);
        Assert.Contains("Greeting", result.Snapshot.Unlocks);
        await repository.RecordAsync(activity);
        await repository.GetOrCreateAsync();
        Assert.Single(await repository.ReadMemoriesAsync(new()), card => card.TemplateId == "Stage2");
    }

    [Fact]
    public async Task Failed_album_write_rolls_back_growth_and_reward_together()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        await repository.GetOrCreateAsync();
        await using var connection = await db.Factory.OpenAsync();
        await connection.ExecuteAsync("""
            CREATE TRIGGER fail_album BEFORE INSERT ON companion_memory_album
            BEGIN SELECT RAISE(ABORT, 'simulated album failure'); END;
            """);
        var activity = Capture("retry-after-failure", DateTimeOffset.Now);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.RecordAsync(activity));
        Assert.Equal(0, (await repository.GetOrCreateAsync()).Growth.Experience);
        Assert.Empty(await repository.ReadMemoriesAsync(new()));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM companion_meta_reward_ledger;"));
        await connection.ExecuteAsync("DROP TRIGGER fail_album;");
        await repository.RecordAsync(activity);
        Assert.Equal(5, (await repository.GetOrCreateAsync()).Growth.Experience);
        Assert.Single(await repository.ReadMemoriesAsync(new()));
    }

    [Fact]
    public async Task Concurrent_completion_and_refresh_award_one_mission()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        var now = DateTimeOffset.Now;
        await repository.RecordAsync(Capture("capture", now));
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Greeting), now);
        var request = new CareRequest(CompanionCareAction.Rest);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await Repository(db).PerformCareAsync(request, now);
            await Repository(db).GetOrCreateAsync();
        })));
        Assert.Single(await repository.ReadMemoriesAsync(new()), card => card.TemplateId == "Mission");
        Assert.Equal(15, (await repository.GetOrCreateAsync()).Growth.Experience);
    }

    [Fact]
    public async Task Album_filters_and_pages_without_copying_note_content()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        var now = DateTimeOffset.Now;
        for (var day = 0; day < 28; day++)
        {
            await repository.RecordAsync(Capture($"day-{day}", now.AddDays(day), CompanionPetKind.Cat));
        }

        var first = await repository.ReadMemoriesAsync(new());
        var second = await repository.ReadMemoriesAsync(new(CompanionMemoryQuery.PageSize));
        Assert.Equal(25, first.Count);
        Assert.Equal(3, second.Count);
        Assert.Empty(first.Select(card => card.Id).Intersect(second.Select(card => card.Id)));
        Assert.Empty(await repository.ReadMemoriesAsync(new(Pet: CompanionPetKind.Dog)));
        var date = DateOnly.FromDateTime(now.AddDays(3).LocalDateTime);
        Assert.Single(await repository.ReadMemoriesAsync(new(Date: date)));
        await using var connection = await db.Factory.OpenAsync();
        var columns = await connection.QueryAsync<string>("SELECT name FROM pragma_table_info('companion_memory_album');");
        Assert.Equal(new[] { "reward_id", "template_id", "template_version" }, columns);
    }

    [Fact]
    public async Task Days_before_activation_are_not_backfilled_and_day_keys_survive_policy_changes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = Repository(db);
        var now = DateTimeOffset.Now;
        await repository.GetOrCreateAsync();
        var old = now.AddDays(-1);
        await repository.RecordAsync(Capture("yesterday", old));
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Greeting), old);
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Rest), old);
        Assert.Empty(await repository.ReadMemoriesAsync(new()));
        await repository.RecordAsync(Capture("today", now));
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Greeting), now);
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Rest), now);
        await using var connection = await db.Factory.OpenAsync();
        await connection.ExecuteAsync("UPDATE companion_meta_reward_ledger SET policy_version = 0;");
        await repository.GetOrCreateAsync();
        Assert.Single(await repository.ReadMemoriesAsync(new()), card => card.TemplateId == "Mission");
    }
}
