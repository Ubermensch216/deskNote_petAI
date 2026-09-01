namespace DeskNote.Companion.Core;

/// <summary>
/// The daily score budget. App work and pet care are deliberately separate budgets so that
/// neither one can be substituted for the other.
/// </summary>
/// <remarks>
/// V1 paid growth only for note activity, which capped a day at 29 points and made the pet a
/// receipt for app usage rather than something raised. V2 splits the day 30:70 between the app
/// and hands-on care, and moves the stage gate onto care days so that the second half of the
/// ladder cannot be reached by note-taking alone.
/// </remarks>
public static class CompanionBalanceV2
{
    public const int RuleVersion = 2;

    public const int DailyAppMaximum = 30;
    public const int DailyCareMaximum = 70;
    public const int DailyMaximum = DailyAppMaximum + DailyCareMaximum;

    /// <summary>Care points in one local day that make it count as a care day.</summary>
    public const int CareDayThreshold = 35;

    /// <summary>Paid once a day for completing two different kinds of play.</summary>
    public const int DifferentPlayBonus = 5;

    public static AppScoreCategory CategoryOf(CompanionActivityType type) => type switch
    {
        CompanionActivityType.MeaningfulCapture => AppScoreCategory.Capture,
        CompanionActivityType.NoteRefined => AppScoreCategory.Refine,
        CompanionActivityType.NoteOrganized => AppScoreCategory.Organize,
        CompanionActivityType.ChecklistCompleted or
        CompanionActivityType.ReminderHandled => AppScoreCategory.Resolve,
        CompanionActivityType.UsefulRecall or
        CompanionActivityType.BriefingEvidenceOpened => AppScoreCategory.Reuse,
        _ => AppScoreCategory.AiApplied,
    };

    /// <summary>
    /// Types that share one daily budget. Checklists and reminders are the same "closed a loop"
    /// action to a user, so they must not each pay a full allowance.
    /// </summary>
    public static IReadOnlyList<CompanionActivityType> TypesIn(AppScoreCategory category) =>
        category switch
        {
            AppScoreCategory.Capture => [CompanionActivityType.MeaningfulCapture],
            AppScoreCategory.Refine => [CompanionActivityType.NoteRefined],
            AppScoreCategory.Organize => [CompanionActivityType.NoteOrganized],
            AppScoreCategory.Resolve =>
            [
                CompanionActivityType.ChecklistCompleted,
                CompanionActivityType.ReminderHandled,
            ],
            AppScoreCategory.Reuse =>
            [
                CompanionActivityType.UsefulRecall,
                CompanionActivityType.BriefingEvidenceOpened,
            ],
            _ => [CompanionActivityType.AiSuggestionAccepted],
        };

    public static int AppPoints(AppScoreCategory category) => category switch
    {
        AppScoreCategory.Capture => 5,
        AppScoreCategory.Refine => 3,
        AppScoreCategory.Organize => 2,
        AppScoreCategory.Resolve => 3,
        AppScoreCategory.Reuse => 4,
        _ => 5,
    };

    public static int AppDailyLimit(AppScoreCategory category) => category switch
    {
        AppScoreCategory.Capture => 2,
        AppScoreCategory.Refine => 2,
        _ => 1,
    };

    public static int CarePoints(CompanionCareAction action) => action switch
    {
        CompanionCareAction.Feed => 10,
        CompanionCareAction.BasicCare => 8,
        CompanionCareAction.SpecialCare => 7,
        CompanionCareAction.Play => 10,
        CompanionCareAction.Greeting => 5,
        _ => 5,
    };

    public static int CareDailyLimit(CompanionCareAction action) => action switch
    {
        CompanionCareAction.Feed => 2,
        CompanionCareAction.Play => 2,
        _ => 1,
    };

    /// <summary>The flavour axis an app activity feeds. Axes no longer gate growth.</summary>
    public static RewardDelta AxisFlavour(CompanionActivityType type) =>
        CategoryOf(type) switch
        {
            AppScoreCategory.Capture or AppScoreCategory.Refine => new RewardDelta(0, 1, 0, 0),
            AppScoreCategory.Reuse or AppScoreCategory.AiApplied => new RewardDelta(0, 0, 1, 0),
            _ => new RewardDelta(0, 0, 0, 1),
        };
}

/// <summary>Note activities that share one daily allowance.</summary>
public enum AppScoreCategory
{
    Capture = 1,
    Refine = 2,
    Organize = 3,
    Resolve = 4,
    Reuse = 5,
    AiApplied = 6,
}

/// <summary>Hands-on care the user performs on the pet itself.</summary>
public enum CompanionCareAction
{
    Feed = 1,
    BasicCare = 2,
    SpecialCare = 3,
    Play = 4,
    Greeting = 5,
    Rest = 6,
}

/// <summary>
/// Play variants. Two different kinds in one day pay the variety bonus, which is what stops a
/// user from tapping the same button twice.
/// </summary>
public enum CompanionPlayKind
{
    Chase = 1,
    Puzzle = 2,
    Toss = 3,
}

/// <summary>
/// The V1 balance, kept because migration 005 converts V1 totals into V2 experience and because
/// the ledger still holds V1 rows. Nothing live scores against it.
/// </summary>
public static class CompanionBalanceV1
{
    public const int RuleVersion = 1;
    public const int AxisMaximum = 100;

    public static int AppearanceStage(int total) => total switch
    {
        >= 150 => 4,
        >= 90 => 3,
        >= 45 => 2,
        >= 15 => 1,
        _ => 0,
    };
}
