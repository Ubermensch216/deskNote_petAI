namespace DeskNote.Companion.Core;

public sealed record CompanionProfile(
    Guid Id,
    string Name,
    string AppearanceKey,
    DateTimeOffset CreatedAt,
    int RuleVersion);

public enum DailyRitualKind
{
    Capture = 1,
    Recall = 2,
    Resolve = 3,
}

public sealed record DailyProgress(
    DateOnly LocalDate,
    int CaptureCount = 0,
    int RecallCount = 0,
    int ResolveCount = 0,
    DailyRitualKind? ChosenRitual = null,
    bool RitualCompleted = false);

public sealed record CompanionSnapshot(
    CompanionProfile Profile,
    GrowthState Growth,
    DailyProgress Today,
    RewardDelta LastReward,
    CompanionActivityType? LastActivityType,
    DateTimeOffset? LastActivityAt);

public sealed record CompanionRecordResult(
    bool Recorded,
    CompanionSnapshot Snapshot);

/// <summary>Persistence boundary used by the activity queue; implementations own idempotency.</summary>
public interface ICompanionRepository
{
    Task<CompanionSnapshot> GetOrCreateAsync(CancellationToken cancellationToken = default);

    Task<CompanionRecordResult> RecordAsync(
        CompanionActivity activity,
        CancellationToken cancellationToken = default);

    Task<CompanionSnapshot> ChooseRitualAsync(
        DateOnly localDate,
        DailyRitualKind ritual,
        CancellationToken cancellationToken = default);
}
