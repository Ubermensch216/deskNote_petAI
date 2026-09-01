using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;

namespace DeskNote.Data.Tests;

public class CompanionRepositoryTests
{
    [Fact]
    public async Task Replaying_the_same_event_one_hundred_times_rewards_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteCompanionRepository(database.Factory, new RewardPolicy());
        var activity = Activity("same-event", CompanionActivityType.MeaningfulCapture);

        for (var index = 0; index < 100; index++)
        {
            await repository.RecordAsync(activity, TestContext.Current.CancellationToken);
        }

        var snapshot = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, snapshot.Growth.Curiosity);
    }

    [Fact]
    public async Task Events_after_the_daily_cap_are_recorded_without_growth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteCompanionRepository(database.Factory, new RewardPolicy());

        for (var index = 0; index < 7; index++)
        {
            await repository.RecordAsync(
                Activity($"capture-{index}", CompanionActivityType.MeaningfulCapture),
                TestContext.Current.CancellationToken);
        }

        var snapshot = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, snapshot.Growth.Curiosity);
        Assert.False(snapshot.LastReward.HasGrowth);
    }

    [Fact]
    public async Task Projection_survives_a_new_repository_instance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = new SqliteCompanionRepository(database.Factory, new RewardPolicy());
        await first.RecordAsync(
            Activity("reminder", CompanionActivityType.ReminderHandled),
            TestContext.Current.CancellationToken);

        var afterRestart = new SqliteCompanionRepository(database.Factory, new RewardPolicy());
        var snapshot = await afterRestart.GetOrCreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.Growth.Reliability);
        Assert.Equal(CompanionActivityType.ReminderHandled, snapshot.LastActivityType);
    }

    [Fact]
    public async Task Chosen_daily_ritual_completes_when_its_activity_arrives()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteCompanionRepository(database.Factory, new RewardPolicy());
        var date = new DateOnly(2026, 8, 31);

        var chosen = await repository.ChooseRitualAsync(
            date,
            DailyRitualKind.Resolve,
            TestContext.Current.CancellationToken);
        Assert.False(chosen.Today.RitualCompleted);

        var completed = await repository.RecordAsync(
            Activity("resolve", CompanionActivityType.ChecklistCompleted),
            TestContext.Current.CancellationToken);

        Assert.True(completed.Snapshot.Today.RitualCompleted);
        Assert.Equal(1, completed.Snapshot.Today.ResolveCount);
    }

    [Fact]
    public async Task Each_selected_pet_has_independent_growth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteCompanionRepository(database.Factory, new RewardPolicy());
        await repository.RecordAsync(
            Activity("rabbit-capture", CompanionActivityType.MeaningfulCapture),
            TestContext.Current.CancellationToken);

        await SetSettingAsync(database.Factory, SettingKeys.CompanionSelectedPet, "Cat");
        var newCat = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("cat", newCat.Profile.AppearanceKey);
        Assert.Equal(0, newCat.Growth.Total);

        await repository.RecordAsync(
            Activity("cat-capture", CompanionActivityType.MeaningfulCapture),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, (await repository.GetOrCreateAsync()).Growth.Curiosity);

        await SetSettingAsync(database.Factory, SettingKeys.CompanionSelectedPet, "Rabbit");
        Assert.Equal(1, (await repository.GetOrCreateAsync()).Growth.Curiosity);
    }

    [Fact]
    public async Task Dragon_unlocks_after_all_standard_pets_reach_final_stage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteCompanionRepository(database.Factory, new RewardPolicy());
        await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);

        await using (var connection = await database.Factory.OpenAsync())
        {
            foreach (var pet in CompanionPetCatalog.RequiredForDragon)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO companion_pet_progress(
                        companion_id, pet_kind, curiosity, insight, reliability)
                    VALUES ($companion, $pet, 50, 50, 50);
                    """;
                command.Parameters.AddWithValue("$companion", SqliteCompanionRepository.DefaultProfileId.ToString());
                command.Parameters.AddWithValue("$pet", pet.ToString());
                await command.ExecuteNonQueryAsync();
            }
        }

        await repository.RecordAsync(
            Activity("unlock-check", CompanionActivityType.MeaningfulCapture),
            TestContext.Current.CancellationToken);
        await SetSettingAsync(database.Factory, SettingKeys.CompanionSelectedPet, "Dragon");

        var dragon = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("dragon", dragon.Profile.AppearanceKey);
    }

    private static async Task SetSettingAsync(
        SqliteConnectionFactory factory,
        string key,
        string value)
    {
        await using var connection = await factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static CompanionActivity Activity(string id, CompanionActivityType type) => new(
        id,
        type,
        new DateTimeOffset(2026, 8, 31, 2, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 8, 31),
        null,
        type == CompanionActivityType.ReminderHandled ? "reminder-id" : null);
}
