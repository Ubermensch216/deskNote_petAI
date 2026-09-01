using DeskNote.Companion.Core;

namespace DeskNote.Companion.Core.Tests;

public class CompanionDomainTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 31, 10, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Every_pet_grows_from_a_distinct_juvenile_silhouette_to_full_size()
    {
        foreach (var pet in CompanionPetCatalog.All)
        {
            var previous = CompanionGrowthAppearanceCatalog.For(pet, 0);
            Assert.True(previous.WidthScale < 1);
            Assert.True(previous.HeightScale < 1);

            for (var stage = 1; stage <= CompanionGrowthAppearanceCatalog.FinalStage; stage++)
            {
                var current = CompanionGrowthAppearanceCatalog.For(pet, stage);
                Assert.True(current.WidthScale > previous.WidthScale);
                Assert.True(current.HeightScale > previous.HeightScale);
                previous = current;
            }

            Assert.Equal(new CompanionGrowthAppearance(1, 1), previous);
        }
    }

    /// <summary>
    /// Creating a note and refining it later are separate budgets in V2, so a long edit to an
    /// established note must not be paid as a fresh capture.
    /// </summary>
    [Theory]
    [InlineData(0, 20, true)]
    [InlineData(0, 19, false)]
    [InlineData(20, 60, false)]
    [InlineData(100, 200, false)]
    public void Capture_pays_only_the_first_time_a_note_becomes_real(int before, int after, bool expected)
    {
        var result = new ActivityClassifier().Classify(Candidate(
            CompanionActivityType.MeaningfulCapture,
            before: before,
            after: after));

        Assert.Equal(expected, result is not null);
    }

    [Theory]
    [InlineData(100, 141, false, true)]
    [InlineData(100, 139, false, false)]
    [InlineData(100, 101, true, true)]
    [InlineData(10, 200, false, false)]
    public void Refinement_needs_a_real_edit_or_a_structural_change(
        int before,
        int after,
        bool structural,
        bool expected)
    {
        var result = new ActivityClassifier().Classify(
            Candidate(CompanionActivityType.NoteRefined, before, after) with
            {
                StructuralChange = structural,
            });

        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public void Tidying_only_counts_on_a_note_that_is_more_than_a_day_old()
    {
        var classifier = new ActivityClassifier();
        var fresh = Candidate(CompanionActivityType.NoteOrganized) with
        {
            SourceEntityId = "backlog",
            SourceAge = TimeSpan.FromHours(23),
        };

        Assert.Null(classifier.Classify(fresh));
        Assert.NotNull(classifier.Classify(fresh with { SourceAge = TimeSpan.FromHours(25) }));
    }

    [Fact]
    public void Recall_requires_dwell_or_a_follow_up_action()
    {
        var classifier = new ActivityClassifier();

        Assert.Null(classifier.Classify(Candidate(CompanionActivityType.UsefulRecall) with
        {
            DwellTime = TimeSpan.FromSeconds(7.9),
        }));
        Assert.NotNull(classifier.Classify(Candidate(CompanionActivityType.UsefulRecall) with
        {
            HasFollowUpAction = true,
        }));
    }

    [Fact]
    public void Checklist_transition_has_a_stable_key_and_retoggle_is_the_same_event()
    {
        var noteId = Guid.NewGuid();
        var first = ChecklistCompletionDetector.FindCompletedKeys(
            noteId,
            "- [ ] Ship beta",
            "- [x] Ship beta");
        var retoggle = ChecklistCompletionDetector.FindCompletedKeys(
            noteId,
            "- [ ]   SHIP   beta ",
            "- [X] ship beta");

        Assert.Single(first);
        Assert.Equal(first, retoggle);
    }

    [Fact]
    public void Adding_a_tag_is_structural_but_ticking_a_box_is_not()
    {
        var tagged = NoteStructureInspector.Compare("Ship the beta", "Ship the beta #release");
        var ticked = NoteStructureInspector.Compare("- [ ] Ship beta", "- [x] Ship beta");
        var itemAdded = NoteStructureInspector.Compare("- [ ] Ship beta", "- [ ] Ship beta\n- [ ] Tag it");

        Assert.Equal(new[] { "release" }, tagged.AddedTags);
        Assert.True(tagged.HasStructuralChange);
        Assert.False(ticked.HasStructuralChange);
        Assert.True(itemAdded.ChecklistChanged);
    }

    [Theory]
    [InlineData(AppScoreCategory.Capture, 5, 2)]
    [InlineData(AppScoreCategory.Refine, 3, 2)]
    [InlineData(AppScoreCategory.Organize, 2, 1)]
    [InlineData(AppScoreCategory.Resolve, 3, 1)]
    [InlineData(AppScoreCategory.Reuse, 4, 1)]
    [InlineData(AppScoreCategory.AiApplied, 5, 1)]
    public void Daily_cap_rejects_only_the_next_reward(AppScoreCategory category, int points, int limit)
    {
        var type = CompanionBalanceV2.TypesIn(category)[0];
        var candidate = type == CompanionActivityType.NoteRefined
            ? Candidate(type, before: 100, after: 200)
            : Candidate(type);
        var activity = new ActivityClassifier().Classify(candidate)!;
        var policy = new RewardPolicy();

        Assert.Equal(points, policy.Evaluate(activity, limit - 1).Experience);
        Assert.False(policy.Evaluate(activity, limit).HasGrowth);
    }

    /// <summary>
    /// The whole point of the split: a day of note work is worth 30 and a day of care is worth 70.
    /// If either total drifts, the ladder silently changes length.
    /// </summary>
    [Fact]
    public void The_day_is_worth_thirty_from_the_app_and_seventy_from_care()
    {
        var app = Enum.GetValues<AppScoreCategory>()
            .Sum(category => CompanionBalanceV2.AppPoints(category) * CompanionBalanceV2.AppDailyLimit(category));
        var care = Enum.GetValues<CompanionCareAction>()
            .Sum(action => CompanionBalanceV2.CarePoints(action) * CompanionBalanceV2.CareDailyLimit(action))
            + CompanionBalanceV2.DifferentPlayBonus;

        Assert.Equal(CompanionBalanceV2.DailyAppMaximum, app);
        Assert.Equal(CompanionBalanceV2.DailyCareMaximum, care);
        Assert.Equal(100, CompanionBalanceV2.DailyMaximum);
    }

    [Fact]
    public void Checklists_and_reminders_share_one_allowance()
    {
        Assert.Equal(
            CompanionBalanceV2.CategoryOf(CompanionActivityType.ChecklistCompleted),
            CompanionBalanceV2.CategoryOf(CompanionActivityType.ReminderHandled));
        Assert.Equal(
            CompanionBalanceV2.CategoryOf(CompanionActivityType.UsefulRecall),
            CompanionBalanceV2.CategoryOf(CompanionActivityType.BriefingEvidenceOpened));
    }

    [Fact]
    public void Experience_accumulates_without_a_ceiling_and_axes_stay_as_counters()
    {
        var growth = GrowthProjector.Apply(
            new GrowthState(Experience: 195, CareDays: 0, Curiosity: 40),
            new RewardDelta(5, 1, 0, 0));

        Assert.Equal(200, growth.Experience);
        Assert.Equal(41, growth.Curiosity);
        Assert.Equal(2, growth.Stage);
    }

    /// <summary>
    /// Stage two is reachable on experience alone; every stage above it is not. This is the rule
    /// that keeps a note-only user from being walled out while still making care mandatory for
    /// real growth.
    /// </summary>
    [Fact]
    public void Care_days_gate_every_stage_above_the_second()
    {
        Assert.Equal(2, CompanionGrowthLadder.StageFor(experience: 5000, careDays: 0));
        Assert.Equal(3, CompanionGrowthLadder.StageFor(experience: 5000, careDays: 7));
        Assert.Equal(4, CompanionGrowthLadder.StageFor(experience: 5000, careDays: 14));
        Assert.Equal(5, CompanionGrowthLadder.StageFor(experience: 5000, careDays: 28));

        // Care days alone buy nothing either.
        Assert.Equal(1, CompanionGrowthLadder.StageFor(experience: 199, careDays: 28));
    }

    [Fact]
    public void A_fully_committed_day_still_takes_four_weeks_to_finish_a_pet()
    {
        var final = CompanionGrowthLadder.Rung(CompanionGrowthLadder.FinalStage);
        var fastestByExperience = Math.Ceiling(final.Experience / (double)CompanionBalanceV2.DailyMaximum);

        Assert.Equal(22, fastestByExperience);
        Assert.Equal(28, final.CareDays);
    }

    [Fact]
    public void Proactive_policy_enforces_every_interruption_guard()
    {
        var settings = new CompanionSettings { Enabled = true, ProactiveEnabled = true };
        var policy = new ProactivePolicy();
        var allowed = new ProactiveContext { Settings = settings, Now = Morning };

        Assert.True(policy.Allows(allowed));
        Assert.False(policy.Allows(allowed with { OfferedThisHour = 2 }));
        Assert.False(policy.Allows(allowed with { OfferedToday = 5 }));
        Assert.False(policy.Allows(allowed with { LastInteractionAt = Morning.AddMinutes(-9) }));
        Assert.False(policy.Allows(allowed with { LastSameTriggerAt = Morning.AddHours(-23) }));
        Assert.False(policy.Allows(allowed with { IsFullScreenOrPresenting = true }));
        Assert.False(policy.Allows(allowed with { ConsecutiveIgnores = 3 }));
        Assert.False(policy.Allows(allowed with { MutedUntil = Morning.AddDays(1) }));
    }

    private static CompanionActivityCandidate Candidate(
        CompanionActivityType type,
        int before = 0,
        int after = 20) => new()
        {
            SourceEventId = Guid.NewGuid().ToString("N"),
            Type = type,
            OccurredAt = Morning,
            NoteId = Guid.NewGuid(),
            SourceEntityId = type is CompanionActivityType.ChecklistCompleted
            or CompanionActivityType.ReminderHandled
            or CompanionActivityType.AiSuggestionAccepted
            or CompanionActivityType.NoteOrganized
            ? Guid.NewGuid().ToString("N")
            : null,
            PreviousContentLength = before,
            CurrentContentLength = after,
            SourceAge = TimeSpan.FromDays(3),
            DwellTime = type == CompanionActivityType.UsefulRecall ? TimeSpan.FromSeconds(8) : TimeSpan.Zero,
        };
}
