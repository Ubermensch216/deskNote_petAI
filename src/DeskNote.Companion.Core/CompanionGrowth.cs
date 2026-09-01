namespace DeskNote.Companion.Core;

/// <summary>
/// What one accepted action is worth. Experience is the only value that moves the ladder; the
/// three axes are lifetime counters kept for flavour and for the upgrade path from V1.
/// </summary>
public readonly record struct RewardDelta(
    int Experience,
    int Curiosity,
    int Insight,
    int Reliability)
{
    public static RewardDelta None => default;

    public bool HasGrowth => Experience > 0;
}

/// <summary>One step of the growth ladder and everything it requires.</summary>
public readonly record struct GrowthRung(int Stage, int Experience, int CareDays);

/// <summary>
/// The five stages, and the care days each one costs.
/// </summary>
/// <remarks>
/// Stage two is deliberately experience-only. Gating the very first evolution on care days would
/// leave a user who never opens the care panel stuck at a newborn forever, which turns the pet
/// into dead weight for anyone using DeskNote purely as a note app. Hands-on care becomes
/// mandatory from stage three, where the promise of raising something actually starts.
/// </remarks>
public static class CompanionGrowthLadder
{
    public const int FirstStage = 1;
    public const int FinalStage = 5;

    public static IReadOnlyList<GrowthRung> Rungs { get; } =
    [
        new(1, 0, 0),
        new(2, 200, 0),
        new(3, 600, 7),
        new(4, 1200, 14),
        new(5, 2200, 28),
    ];

    public static int StageFor(int experience, int careDays)
    {
        var stage = FirstStage;

        foreach (var rung in Rungs)
        {
            if (experience >= rung.Experience && careDays >= rung.CareDays)
            {
                stage = rung.Stage;
            }
        }

        return stage;
    }

    public static GrowthRung Rung(int stage) =>
        Rungs[Math.Clamp(stage, FirstStage, FinalStage) - 1];

    public static GrowthRung? NextRung(int stage) =>
        stage >= FinalStage ? null : Rung(stage + 1);
}

public sealed record GrowthState(
    int Experience = 0,
    int CareDays = 0,
    int Curiosity = 0,
    int Insight = 0,
    int Reliability = 0)
{
    public int Stage => CompanionGrowthLadder.StageFor(Experience, CareDays);

    /// <summary>Zero-based index into the appearance catalogue.</summary>
    public int AppearanceStage => Stage - 1;

    public bool IsFullyRaised => Stage >= CompanionGrowthLadder.FinalStage;

    /// <summary>Progress toward the next rung, 0 to 1, counting experience only.</summary>
    public double ExperienceProgress
    {
        get
        {
            if (CompanionGrowthLadder.NextRung(Stage) is not { } next)
            {
                return 1d;
            }

            var floor = CompanionGrowthLadder.Rung(Stage).Experience;
            var span = next.Experience - floor;
            return span <= 0 ? 1d : Math.Clamp((Experience - floor) / (double)span, 0d, 1d);
        }
    }

    /// <summary>Care days still owed before the next rung, which experience cannot buy off.</summary>
    public int CareDaysRemaining =>
        CompanionGrowthLadder.NextRung(Stage) is { } next
            ? Math.Max(0, next.CareDays - CareDays)
            : 0;
}

public static class GrowthProjector
{
    public static GrowthState Apply(GrowthState current, RewardDelta reward)
    {
        ArgumentNullException.ThrowIfNull(current);

        return current with
        {
            Experience = Math.Max(0, current.Experience + reward.Experience),
            Curiosity = Math.Max(0, current.Curiosity + reward.Curiosity),
            Insight = Math.Max(0, current.Insight + reward.Insight),
            Reliability = Math.Max(0, current.Reliability + reward.Reliability),
        };
    }
}

/// <summary>Decides what an accepted note activity pays, given what the day already paid.</summary>
public sealed class RewardPolicy
{
    public RewardDelta Evaluate(CompanionActivity activity, int acceptedToday)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var category = CompanionBalanceV2.CategoryOf(activity.Type);
        if (acceptedToday >= CompanionBalanceV2.AppDailyLimit(category))
        {
            return RewardDelta.None;
        }

        var flavour = CompanionBalanceV2.AxisFlavour(activity.Type);
        return flavour with { Experience = CompanionBalanceV2.AppPoints(category) };
    }
}

/// <summary>Decides what one care action pays, given the day's care history for this pet.</summary>
public sealed class CarePolicy
{
    /// <param name="request">The action, and the play variant when the action is play.</param>
    /// <param name="needs">Needs projected at the moment the action was requested.</param>
    /// <param name="acceptedToday">Accepted actions of this kind already paid today.</param>
    /// <param name="playKindsToday">Play kinds already paid today, for the variety bonus.</param>
    public CareOutcome Evaluate(
        CareRequest request,
        CompanionNeeds needs,
        int acceptedToday,
        IReadOnlyCollection<CompanionPlayKind> playKindsToday)
    {
        ArgumentNullException.ThrowIfNull(playKindsToday);

        if (acceptedToday >= CompanionBalanceV2.CareDailyLimit(request.Action))
        {
            return new CareOutcome(false, RewardDelta.None, CareRefusal.DailyLimitReached);
        }

        if (!CompanionCareRules.IsNeeded(request.Action, needs))
        {
            return new CareOutcome(false, RewardDelta.None, CareRefusal.NotNeededYet);
        }

        // The bonus pays for variety, not for pressing play twice: repeating the same game the
        // pet already played today earns the base points only.
        var points = CompanionBalanceV2.CarePoints(request.Action);
        if (request.Action == CompanionCareAction.Play
            && request.PlayKind is { } kind
            && playKindsToday.Count > 0
            && !playKindsToday.Contains(kind))
        {
            points += CompanionBalanceV2.DifferentPlayBonus;
        }

        return new CareOutcome(true, new RewardDelta(points, 0, 0, 0), CareRefusal.None);
    }
}

public enum CareRefusal
{
    None = 0,
    DailyLimitReached = 1,
    NotNeededYet = 2,
}

public readonly record struct CareOutcome(bool Accepted, RewardDelta Reward, CareRefusal Refusal);

/// <summary>One requested care action. The play kind is set only for play.</summary>
public readonly record struct CareRequest(CompanionCareAction Action, CompanionPlayKind? PlayKind = null);

/// <summary>
/// Converts a V1 axis total into V2 experience and care days.
/// </summary>
/// <remarks>
/// The stage the user already reached is kept, and their position inside it is rescaled onto the
/// new band; care days are credited only up to what that stage required, because nobody could
/// have known to spend days they were never asked for.
/// <para>
/// Migration 005 performs this same arithmetic in SQL, since SQLite cannot call into here. The
/// tests pin both ends to <see cref="CompanionGrowthLadder"/>, so changing the ladder without
/// changing the migration fails loudly rather than quietly demoting somebody's pet.
/// </para>
/// </remarks>
public static class CompanionGrowthConversionV1
{
    /// <summary>Lowest V1 axis total that reached each stage, from stage one upward.</summary>
    public static IReadOnlyList<int> StageFloors { get; } = [0, 15, 45, 90, 150];

    public static int ExperienceFor(int axisTotal)
    {
        var stage = StageOf(axisTotal);
        var rung = CompanionGrowthLadder.Rung(stage);
        if (CompanionGrowthLadder.NextRung(stage) is not { } next)
        {
            return rung.Experience;
        }

        var floor = StageFloors[stage - 1];
        var span = StageFloors[stage] - floor;
        var progress = span <= 0 ? 0 : (axisTotal - floor) * (next.Experience - rung.Experience) / span;
        return rung.Experience + progress;
    }

    public static int CareDaysFor(int axisTotal) =>
        CompanionGrowthLadder.Rung(StageOf(axisTotal)).CareDays;

    private static int StageOf(int axisTotal)
    {
        var stage = CompanionGrowthLadder.FirstStage;

        for (var index = 0; index < StageFloors.Count; index++)
        {
            if (axisTotal >= StageFloors[index])
            {
                stage = index + 1;
            }
        }

        return stage;
    }
}
