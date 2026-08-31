using DeskNote.Companion.Core;

namespace DeskNote.Companion.Core.Tests;

public class CompanionDomainTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 31, 10, 0, 0, TimeSpan.FromHours(9));

    [Theory]
    [InlineData(0, 20, true)]
    [InlineData(20, 59, false)]
    [InlineData(20, 60, true)]
    [InlineData(100, 60, true)]
    [InlineData(0, 19, false)]
    public void Capture_requires_a_meaningful_transition(int before, int after, bool expected)
    {
        var result = new ActivityClassifier().Classify(Candidate(
            CompanionActivityType.MeaningfulCapture,
            before: before,
            after: after));

        Assert.Equal(expected, result is not null);
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

    [Theory]
    [InlineData(CompanionActivityType.MeaningfulCapture, 5)]
    [InlineData(CompanionActivityType.UsefulRecall, 4)]
    [InlineData(CompanionActivityType.ChecklistCompleted, 5)]
    [InlineData(CompanionActivityType.ReminderHandled, 3)]
    [InlineData(CompanionActivityType.AiSuggestionAccepted, 2)]
    [InlineData(CompanionActivityType.BriefingEvidenceOpened, 1)]
    public void Daily_cap_rejects_only_the_next_reward(CompanionActivityType type, int cap)
    {
        var activity = new ActivityClassifier().Classify(Candidate(type))!;
        var policy = new RewardPolicy();

        Assert.True(policy.Evaluate(activity, cap - 1).HasGrowth);
        Assert.False(policy.Evaluate(activity, cap).HasGrowth);
    }

    [Fact]
    public void Growth_is_monotonic_capped_and_has_explicit_stages()
    {
        var growth = GrowthProjector.Apply(new GrowthState(99, 44, 89), new RewardDelta(5, 1, 2));

        Assert.Equal(new GrowthState(100, 45, 91), growth);
        Assert.Equal(4, growth.CuriosityStage);
        Assert.Equal(4, growth.AppearanceStage);
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
            ? Guid.NewGuid().ToString("N")
            : null,
        PreviousContentLength = before,
        CurrentContentLength = after,
        DwellTime = type == CompanionActivityType.UsefulRecall ? TimeSpan.FromSeconds(8) : TimeSpan.Zero,
    };
}
