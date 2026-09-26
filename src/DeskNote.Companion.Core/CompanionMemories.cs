namespace DeskNote.Companion.Core;

/// <summary>Profile-wide mission progress, independent of the selected pet's XP budget.</summary>
public sealed record CompanionMissionProgress(bool AppDone, IReadOnlyList<CompanionCareAction> CareKinds)
{
    public static CompanionMissionProgress Empty { get; } = new(false, []);
    public int CompletedTasks => (AppDone ? 1 : 0) + Math.Min(2, CareKinds.Count);
    public bool IsComplete => CompletedTasks == 3;
}

public sealed record CompanionMemory(
    long Id,
    string TemplateId,
    int TemplateVersion,
    CompanionPetKind Pet,
    DateTimeOffset OccurredAt,
    DateOnly LocalDate);

public sealed record CompanionMemoryQuery(
    int Offset = 0,
    CompanionPetKind? Pet = null,
    DateOnly? Date = null)
{
    public const int PageSize = 25;
}

public interface ICompanionMemoryRepository
{
    Task<IReadOnlyList<CompanionMemory>> ReadMemoriesAsync(
        CompanionMemoryQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable content IDs. Changing a policy version never changes a grant's identity.</summary>
public static class CompanionMemoryCatalog
{
    public const int Version = 1;
    public static IReadOnlyList<string> Templates { get; } =
    [
        "Mission", "Recall", "Checklist", "Capture", "Refine", "Organize", "AiApplied", "Reminder",
        "Stage2", "Stage3", "Stage4", "Stage5", "WelcomeBack",
    ];

    public static string? ForActivity(CompanionActivityType type) => type switch
    {
        CompanionActivityType.MeaningfulCapture => "Capture",
        CompanionActivityType.UsefulRecall or CompanionActivityType.BriefingEvidenceOpened => "Recall",
        CompanionActivityType.ChecklistCompleted => "Checklist",
        CompanionActivityType.NoteRefined => "Refine",
        CompanionActivityType.NoteOrganized => "Organize",
        CompanionActivityType.AiSuggestionAccepted => "AiApplied",
        CompanionActivityType.ReminderHandled => "Reminder",
        _ => null,
    };
}

public sealed record CompanionUnlock(int Stage, string Key);

public static class CompanionUnlockCatalog
{
    public static IReadOnlyList<CompanionUnlock> All { get; } =
    [
        new(2, "Greeting"), new(3, "SpecialCare"), new(4, "Rest"), new(5, "Celebration"),
    ];
}
