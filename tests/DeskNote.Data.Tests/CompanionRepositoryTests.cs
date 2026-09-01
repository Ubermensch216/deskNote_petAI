using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;

namespace DeskNote.Data.Tests;

public class CompanionRepositoryTests
{
    private static readonly DateTimeOffset Morning =
        new(2026, 8, 31, 10, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public async Task Replaying_the_same_event_one_hundred_times_rewards_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);
        var activity = Activity("same-event", CompanionActivityType.MeaningfulCapture);

        for (var index = 0; index < 100; index++)
        {
            await repository.RecordAsync(activity, TestContext.Current.CancellationToken);
        }

        var snapshot = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CompanionBalanceV2.AppPoints(AppScoreCategory.Capture), snapshot.Growth.Experience);
        Assert.Equal(1, snapshot.Growth.Curiosity);
    }

    [Fact]
    public async Task Events_after_the_daily_cap_are_recorded_without_growth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);

        for (var index = 0; index < 7; index++)
        {
            await repository.RecordAsync(
                Activity($"capture-{index}", CompanionActivityType.MeaningfulCapture),
                TestContext.Current.CancellationToken);
        }

        var snapshot = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10, snapshot.Growth.Experience);
        Assert.False(snapshot.LastReward.HasGrowth);
    }

    /// <summary>
    /// Checklists and reminders share one allowance, so the second of the pair must be recorded
    /// and pay nothing rather than quietly doubling the category.
    /// </summary>
    [Fact]
    public async Task A_reminder_and_a_checklist_share_the_same_daily_allowance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);

        await repository.RecordAsync(
            Activity("checklist", CompanionActivityType.ChecklistCompleted),
            TestContext.Current.CancellationToken);
        var second = await repository.RecordAsync(
            Activity("reminder", CompanionActivityType.ReminderHandled),
            TestContext.Current.CancellationToken);

        Assert.True(second.Recorded);
        Assert.False(second.Snapshot.LastReward.HasGrowth);
        Assert.Equal(CompanionBalanceV2.AppPoints(AppScoreCategory.Resolve), second.Snapshot.Growth.Experience);
    }

    [Fact]
    public async Task Projection_survives_a_new_repository_instance()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Repository(database).RecordAsync(
            Activity("reminder", CompanionActivityType.ReminderHandled),
            TestContext.Current.CancellationToken);

        var snapshot = await Repository(database).GetOrCreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, snapshot.Growth.Experience);
        Assert.Equal(1, snapshot.Growth.Reliability);
        Assert.Equal(CompanionActivityType.ReminderHandled, snapshot.LastActivityType);
    }

    [Fact]
    public async Task Care_pays_only_while_the_pet_still_needs_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);
        await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);

        // A pet adopted this instant is content, so the first meal is refused.
        var tooSoon = await repository.PerformCareAsync(
            new CareRequest(CompanionCareAction.Feed),
            DateTimeOffset.Now,
            TestContext.Current.CancellationToken);
        Assert.False(tooSoon.Accepted);
        Assert.Equal(CareRefusal.NotNeededYet, tooSoon.Refusal);

        // Greeting has no gauge, so it is available immediately and exactly once.
        var hello = await repository.PerformCareAsync(
            new CareRequest(CompanionCareAction.Greeting),
            DateTimeOffset.Now,
            TestContext.Current.CancellationToken);
        var again = await repository.PerformCareAsync(
            new CareRequest(CompanionCareAction.Greeting),
            DateTimeOffset.Now,
            TestContext.Current.CancellationToken);

        Assert.True(hello.Accepted);
        Assert.Equal(
            CompanionBalanceV2.CarePoints(CompanionCareAction.Greeting),
            hello.Snapshot.Today.CareScore);
        Assert.Equal(CareRefusal.DailyLimitReached, again.Refusal);
    }

    [Fact]
    public async Task A_day_that_reaches_the_care_threshold_becomes_a_care_day()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);
        await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);

        var before = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, before.Growth.CareDays);

        var last = await FillCareDayAsync(repository);

        Assert.True(last.Snapshot.Today.CareScore >= CompanionBalanceV2.CareDayThreshold);
        Assert.True(last.Snapshot.Today.IsCareDay);
        Assert.Equal(
            1,
            (await repository.GetOrCreateAsync(TestContext.Current.CancellationToken)).Growth.CareDays);
    }

    [Fact]
    public async Task Care_days_are_counted_per_day_not_per_action()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);
        await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        await FillCareDayAsync(repository);
        await FillCareDayAsync(repository);

        // Every action lands on the same local date, so the count must stay at one.
        var snapshot = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, snapshot.Growth.CareDays);
    }

    [Fact]
    public async Task Each_selected_pet_has_independent_growth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);
        await repository.RecordAsync(
            Activity("rabbit-capture", CompanionActivityType.MeaningfulCapture),
            TestContext.Current.CancellationToken);

        await SetSettingAsync(database.Factory, SettingKeys.CompanionSelectedPet, "Cat");
        var newCat = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("cat", newCat.Profile.AppearanceKey);
        Assert.Equal(0, newCat.Growth.Experience);

        await repository.RecordAsync(
            Activity("cat-capture", CompanionActivityType.MeaningfulCapture),
            TestContext.Current.CancellationToken);
        Assert.Equal(5, (await repository.GetOrCreateAsync()).Growth.Experience);

        await SetSettingAsync(database.Factory, SettingKeys.CompanionSelectedPet, "Rabbit");
        Assert.Equal(5, (await repository.GetOrCreateAsync()).Growth.Experience);
    }

    [Fact]
    public async Task Dragon_unlocks_only_when_every_standard_pet_has_the_days_as_well()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = Repository(database);
        await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);

        await SetPetProgressAsync(
            database.Factory,
            CompanionPetCatalog.FullyRaisedExperience,
            careDays: CompanionPetCatalog.FullyRaisedCareDays - 1);
        await repository.RecordAsync(
            Activity("locked-check", CompanionActivityType.MeaningfulCapture),
            TestContext.Current.CancellationToken);
        await SetSettingAsync(database.Factory, SettingKeys.CompanionSelectedPet, "Dragon");
        var stillLocked = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("rabbit", stillLocked.Profile.AppearanceKey);

        await SetPetProgressAsync(
            database.Factory,
            CompanionPetCatalog.FullyRaisedExperience,
            careDays: CompanionPetCatalog.FullyRaisedCareDays);
        await repository.RecordAsync(
            Activity("unlock-check", CompanionActivityType.UsefulRecall),
            TestContext.Current.CancellationToken);

        var dragon = await repository.GetOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("dragon", dragon.Profile.AppearanceKey);
    }

    /// <summary>
    /// Runs enough accepted care to cross the care-day threshold, spread across one local day so
    /// the gauges have time to fall between actions the way they would for a real user.
    /// </summary>
    private static async Task<CompanionCareResult> FillCareDayAsync(SqliteCompanionRepository repository)
    {
        var day = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeZoneInfo.Local.BaseUtcOffset);

        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Greeting), day);
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Rest), day);
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.Feed), day.AddHours(7));
        await repository.PerformCareAsync(new CareRequest(CompanionCareAction.BasicCare), day.AddHours(13));
        return await repository.PerformCareAsync(
            new CareRequest(CompanionCareAction.Play, CompanionPlayKind.Chase),
            day.AddHours(13));
    }

    private static SqliteCompanionRepository Repository(TestDatabase database) =>
        new(database.Factory, new RewardPolicy(), new CarePolicy());

    private static async Task SetPetProgressAsync(
        SqliteConnectionFactory factory,
        int experience,
        int careDays)
    {
        await using var connection = await factory.OpenAsync();
        foreach (var pet in CompanionPetCatalog.RequiredForDragon)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO companion_pet_progress(
                    companion_id, pet_kind, curiosity, insight, reliability, experience, care_days)
                VALUES ($companion, $pet, 0, 0, 0, $experience, $careDays)
                ON CONFLICT(companion_id, pet_kind) DO UPDATE SET
                    experience = excluded.experience,
                    care_days = excluded.care_days;
                """;
            command.Parameters.AddWithValue("$companion", SqliteCompanionRepository.DefaultProfileId.ToString());
            command.Parameters.AddWithValue("$pet", pet.ToString());
            command.Parameters.AddWithValue("$experience", experience);
            command.Parameters.AddWithValue("$careDays", careDays);
            await command.ExecuteNonQueryAsync();
        }
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
        Morning,
        DateOnly.FromDateTime(Morning.LocalDateTime),
        null,
        type is CompanionActivityType.ReminderHandled or CompanionActivityType.ChecklistCompleted
            ? $"entity-{id}"
            : null);
}
