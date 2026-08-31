namespace DeskNote.Companion.Core;

public readonly record struct RewardDelta(int Curiosity, int Insight, int Reliability)
{
    public static RewardDelta None => default;

    public bool HasGrowth => Curiosity > 0 || Insight > 0 || Reliability > 0;
}

/// <summary>All beta balance assumptions live here so a later version is explicit and testable.</summary>
public static class CompanionBalanceV1
{
    public const int RuleVersion = 1;
    public const int AxisMaximum = 100;

    public static RewardDelta Reward(CompanionActivityType type) => type switch
    {
        CompanionActivityType.MeaningfulCapture => new(1, 0, 0),
        CompanionActivityType.UsefulRecall => new(0, 2, 0),
        CompanionActivityType.ChecklistCompleted => new(0, 0, 1),
        CompanionActivityType.ReminderHandled => new(0, 0, 2),
        CompanionActivityType.AiSuggestionAccepted => new(0, 1, 1),
        CompanionActivityType.BriefingEvidenceOpened => new(0, 1, 0),
        _ => RewardDelta.None,
    };

    public static int DailyCap(CompanionActivityType type) => type switch
    {
        CompanionActivityType.MeaningfulCapture => 5,
        CompanionActivityType.UsefulRecall => 4,
        CompanionActivityType.ChecklistCompleted => 5,
        CompanionActivityType.ReminderHandled => 3,
        CompanionActivityType.AiSuggestionAccepted => 2,
        CompanionActivityType.BriefingEvidenceOpened => 1,
        _ => 0,
    };

    public static int AxisStage(int points) => points switch
    {
        >= 80 => 4,
        >= 50 => 3,
        >= 25 => 2,
        >= 10 => 1,
        _ => 0,
    };

    public static int AppearanceStage(int total) => total switch
    {
        >= 150 => 4,
        >= 90 => 3,
        >= 45 => 2,
        >= 15 => 1,
        _ => 0,
    };
}

public sealed record GrowthState(int Curiosity = 0, int Insight = 0, int Reliability = 0)
{
    public int Total => Curiosity + Insight + Reliability;
    public int CuriosityStage => CompanionBalanceV1.AxisStage(Curiosity);
    public int InsightStage => CompanionBalanceV1.AxisStage(Insight);
    public int ReliabilityStage => CompanionBalanceV1.AxisStage(Reliability);
    public int AppearanceStage => CompanionBalanceV1.AppearanceStage(Total);
}

public static class GrowthProjector
{
    public static GrowthState Apply(GrowthState current, RewardDelta reward)
    {
        ArgumentNullException.ThrowIfNull(current);

        return new GrowthState(
            Clamp(current.Curiosity + reward.Curiosity),
            Clamp(current.Insight + reward.Insight),
            Clamp(current.Reliability + reward.Reliability));
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, CompanionBalanceV1.AxisMaximum);
}

public sealed class RewardPolicy
{
    public RewardDelta Evaluate(CompanionActivity activity, int acceptedToday)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return acceptedToday >= CompanionBalanceV1.DailyCap(activity.Type)
            ? RewardDelta.None
            : CompanionBalanceV1.Reward(activity.Type);
    }
}
