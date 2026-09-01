namespace DeskNote.Companion.Core;

public sealed record CompanionProfile(
    Guid Id,
    string Name,
    string AppearanceKey,
    DateTimeOffset CreatedAt,
    int RuleVersion);

/// <summary>
/// Today's two budgets and what has already been spent from them.
/// </summary>
public sealed record DailyProgress
{
    public required DateOnly LocalDate { get; init; }

    public int AppScore { get; init; }

    public int CareScore { get; init; }

    public IReadOnlyDictionary<AppScoreCategory, int> AppCounts { get; init; } =
        new Dictionary<AppScoreCategory, int>();

    public IReadOnlyDictionary<CompanionCareAction, int> CareCounts { get; init; } =
        new Dictionary<CompanionCareAction, int>();

    public IReadOnlyList<CompanionPlayKind> PlayKinds { get; init; } = [];

    public int TotalScore => AppScore + CareScore;

    /// <summary>Whether today already counts toward the care days a stage requires.</summary>
    public bool IsCareDay => CareScore >= CompanionBalanceV2.CareDayThreshold;

    public int Count(AppScoreCategory category) => AppCounts.GetValueOrDefault(category);

    public int Count(CompanionCareAction action) => CareCounts.GetValueOrDefault(action);

    public bool IsDone(AppScoreCategory category) =>
        Count(category) >= CompanionBalanceV2.AppDailyLimit(category);

    public bool IsDone(CompanionCareAction action) =>
        Count(action) >= CompanionBalanceV2.CareDailyLimit(action);
}

/// <summary>One app task and two care tasks, offered for a single day.</summary>
public sealed record DailyMission(
    AppScoreCategory App,
    CompanionCareAction FirstCare,
    CompanionCareAction SecondCare)
{
    public bool IsComplete(DailyProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return progress.Count(App) > 0
            && progress.Count(FirstCare) > 0
            && progress.Count(SecondCare) > 0;
    }
}

/// <summary>
/// Picks the day's mission from the date alone.
/// </summary>
/// <remarks>
/// Deriving the mission rather than storing it means a mission never disagrees with what the
/// ledger says was done, and a user who changes their clock cannot roll for an easier one.
/// Completing a mission deliberately pays no growth: the 30:70 split is the whole balance, and a
/// bonus large enough to notice would quietly break it.
/// </remarks>
public static class DailyMissionPlanner
{
    private static readonly AppScoreCategory[] AppTasks =
    [
        AppScoreCategory.Capture,
        AppScoreCategory.Reuse,
        AppScoreCategory.Resolve,
        AppScoreCategory.Refine,
        AppScoreCategory.AiApplied,
        AppScoreCategory.Organize,
    ];

    private static readonly CompanionCareAction[] CareTasks =
    [
        CompanionCareAction.Feed,
        CompanionCareAction.Play,
        CompanionCareAction.BasicCare,
        CompanionCareAction.SpecialCare,
        CompanionCareAction.Greeting,
        CompanionCareAction.Rest,
    ];

    public static DailyMission For(DateOnly date)
    {
        var day = date.DayNumber;
        var first = CareTasks[Math.Abs(day) % CareTasks.Length];
        var second = CareTasks[Math.Abs((day / CareTasks.Length) + 1) % CareTasks.Length];
        if (second == first)
        {
            second = CareTasks[(Array.IndexOf(CareTasks, first) + 1) % CareTasks.Length];
        }

        return new DailyMission(AppTasks[Math.Abs(day) % AppTasks.Length], first, second);
    }
}

public sealed record CompanionSnapshot(
    CompanionProfile Profile,
    GrowthState Growth,
    CompanionNeeds Needs,
    DailyProgress Today,
    RewardDelta LastReward,
    CompanionActivityType? LastActivityType,
    DateTimeOffset? LastActivityAt)
{
    public DailyMission Mission => DailyMissionPlanner.For(Today.LocalDate);

    /// <summary>Whether this care action would be paid right now.</summary>
    public bool CanPerform(CompanionCareAction action) =>
        !Today.IsDone(action) && CompanionCareRules.IsNeeded(action, Needs);
}

public sealed record CompanionRecordResult(
    bool Recorded,
    CompanionSnapshot Snapshot);

public sealed record CompanionCareResult(
    bool Accepted,
    CareRefusal Refusal,
    CompanionSnapshot Snapshot);

/// <summary>Persistence boundary used by the activity queue; implementations own idempotency.</summary>
public interface ICompanionRepository
{
    Task<CompanionSnapshot> GetOrCreateAsync(CancellationToken cancellationToken = default);

    Task<CompanionRecordResult> RecordAsync(
        CompanionActivity activity,
        CancellationToken cancellationToken = default);

    Task<CompanionCareResult> PerformCareAsync(
        CareRequest request,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
