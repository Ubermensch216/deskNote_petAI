using DeskNote.Companion.Core;

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

    private static CompanionActivity Activity(string id, CompanionActivityType type) => new(
        id,
        type,
        new DateTimeOffset(2026, 8, 31, 2, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 8, 31),
        null,
        type == CompanionActivityType.ReminderHandled ? "reminder-id" : null);
}
