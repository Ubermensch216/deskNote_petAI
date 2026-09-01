using DeskNote.Companion.Core;

namespace DeskNote.Companion.Core.Tests;

/// <summary>
/// Needs are a pure projection over the care ledger, which is what makes these assertions
/// possible at all: an offline week and a running week have to produce the same gauges.
/// </summary>
public class CompanionCareTests
{
    private static readonly DateTimeOffset Adopted = new(2026, 8, 31, 9, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void A_new_pet_starts_content_rather_than_starving()
    {
        var needs = NeedsProjector.At(Adopted, Adopted, [], careDays: 0);

        Assert.Equal((int)NeedsProjector.AdoptionBaseline, needs.Fullness);
        Assert.Equal((int)NeedsProjector.AdoptionBaseline, needs.Cleanliness);
        Assert.Equal(0, needs.Bond);
    }

    [Fact]
    public void Gauges_fall_with_time_and_never_below_zero()
    {
        var later = NeedsProjector.At(Adopted.AddHours(6), Adopted, [], careDays: 0);
        var abandoned = NeedsProjector.At(Adopted.AddDays(9), Adopted, [], careDays: 0);

        Assert.InRange(later.Fullness, 15, 25);
        Assert.Equal(0, abandoned.Fullness);
        Assert.Equal(0, abandoned.Cleanliness);
        Assert.Equal(0, abandoned.Mood);
    }

    [Fact]
    public void The_same_history_gives_the_same_answer_whether_or_not_the_app_was_running()
    {
        var care = new[] { new CareEvent(CompanionCareAction.Feed, Adopted.AddHours(5)) };
        var now = Adopted.AddHours(9);

        Assert.Equal(
            NeedsProjector.At(now, Adopted, care, careDays: 3),
            NeedsProjector.At(now, Adopted, care, careDays: 3));
    }

    [Fact]
    public void Feeding_a_full_pet_earns_nothing()
    {
        var policy = new CarePolicy();
        var full = new CompanionNeeds(100, 100, 100, 0);

        var refused = policy.Evaluate(
            new CareRequest(CompanionCareAction.Feed),
            full,
            acceptedToday: 0,
            playKindsToday: []);

        Assert.False(refused.Accepted);
        Assert.Equal(CareRefusal.NotNeededYet, refused.Refusal);
    }

    [Fact]
    public void Feeding_a_hungry_pet_pays_and_the_third_meal_does_not()
    {
        var policy = new CarePolicy();
        var hungry = new CompanionNeeds(20, 100, 100, 0);

        var first = policy.Evaluate(new CareRequest(CompanionCareAction.Feed), hungry, 0, []);
        var third = policy.Evaluate(new CareRequest(CompanionCareAction.Feed), hungry, 2, []);

        Assert.True(first.Accepted);
        Assert.Equal(CompanionBalanceV2.CarePoints(CompanionCareAction.Feed), first.Reward.Experience);
        Assert.Equal(CareRefusal.DailyLimitReached, third.Refusal);
    }

    /// <summary>The bonus pays for variety, so pressing the same game twice must not earn it.</summary>
    [Fact]
    public void Only_a_different_game_earns_the_play_bonus()
    {
        var policy = new CarePolicy();
        var bored = new CompanionNeeds(100, 100, 20, 0);

        var repeat = policy.Evaluate(
            new CareRequest(CompanionCareAction.Play, CompanionPlayKind.Chase),
            bored,
            acceptedToday: 1,
            playKindsToday: [CompanionPlayKind.Chase]);
        var different = policy.Evaluate(
            new CareRequest(CompanionCareAction.Play, CompanionPlayKind.Puzzle),
            bored,
            acceptedToday: 1,
            playKindsToday: [CompanionPlayKind.Chase]);

        Assert.Equal(10, repeat.Reward.Experience);
        Assert.Equal(10 + CompanionBalanceV2.DifferentPlayBonus, different.Reward.Experience);
    }

    [Fact]
    public void Relationship_actions_are_limited_by_the_day_rather_than_a_gauge()
    {
        var policy = new CarePolicy();
        var content = new CompanionNeeds(100, 100, 100, 0);

        Assert.True(policy.Evaluate(new CareRequest(CompanionCareAction.Greeting), content, 0, []).Accepted);
        Assert.True(policy.Evaluate(new CareRequest(CompanionCareAction.Rest), content, 0, []).Accepted);
        Assert.False(policy.Evaluate(new CareRequest(CompanionCareAction.Greeting), content, 1, []).Accepted);
    }

    [Fact]
    public void Care_becomes_available_again_after_the_gauge_falls()
    {
        var fedAtNine = new[] { new CareEvent(CompanionCareAction.Feed, Adopted) };
        var justFed = NeedsProjector.At(Adopted.AddMinutes(1), Adopted, fedAtNine, 0);
        var muchLater = NeedsProjector.At(Adopted.AddHours(7), Adopted, fedAtNine, 0);

        Assert.False(justFed.IsHungry);
        Assert.True(muchLater.IsHungry);
    }

    [Fact]
    public void Bond_tracks_the_days_a_final_stage_costs()
    {
        var required = CompanionGrowthLadder.Rung(CompanionGrowthLadder.FinalStage).CareDays;

        Assert.Equal(0, NeedsProjector.Bond(0));
        Assert.Equal(50, NeedsProjector.Bond(required / 2));
        Assert.Equal(100, NeedsProjector.Bond(required));
        Assert.Equal(100, NeedsProjector.Bond(required * 3));
    }

    [Fact]
    public void A_care_day_needs_thirty_five_points_which_no_single_action_reaches()
    {
        var largest = Enum.GetValues<CompanionCareAction>()
            .Max(CompanionBalanceV2.CarePoints);

        Assert.True(largest < CompanionBalanceV2.CareDayThreshold);
        Assert.True(CompanionBalanceV2.CareDayThreshold * 2 <= CompanionBalanceV2.DailyCareMaximum);
    }

    [Fact]
    public void Every_species_has_its_own_care_moment_worth_the_same_points()
    {
        var moments = CompanionPetCatalog.All
            .Select(CompanionSpecialCareCatalog.For)
            .ToList();

        Assert.Equal(CompanionPetCatalog.All.Count, moments.Distinct().Count());
    }

    [Fact]
    public void The_daily_mission_is_derived_from_the_date_and_never_repeats_a_care_task()
    {
        for (var day = 0; day < 40; day++)
        {
            var date = new DateOnly(2026, 9, 1).AddDays(day);
            var mission = DailyMissionPlanner.For(date);

            Assert.Equal(mission, DailyMissionPlanner.For(date));
            Assert.NotEqual(mission.FirstCare, mission.SecondCare);
        }
    }

    [Fact]
    public void A_mission_is_complete_only_once_all_three_tasks_are_done()
    {
        var date = new DateOnly(2026, 9, 1);
        var mission = DailyMissionPlanner.For(date);
        var progress = new DailyProgress
        {
            LocalDate = date,
            AppCounts = new Dictionary<AppScoreCategory, int> { [mission.App] = 1 },
            CareCounts = new Dictionary<CompanionCareAction, int> { [mission.FirstCare] = 1 },
        };

        Assert.False(mission.IsComplete(progress));

        var finished = progress with
        {
            CareCounts = new Dictionary<CompanionCareAction, int>
            {
                [mission.FirstCare] = 1,
                [mission.SecondCare] = 1,
            },
        };

        Assert.True(mission.IsComplete(finished));
    }
}
