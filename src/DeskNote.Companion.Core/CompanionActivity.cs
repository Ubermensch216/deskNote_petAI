namespace DeskNote.Companion.Core;

/// <summary>Business actions that may produce companion growth after quality checks.</summary>
public enum CompanionActivityType
{
    MeaningfulCapture = 1,
    UsefulRecall = 2,
    ChecklistCompleted = 3,
    ReminderHandled = 4,
    AiSuggestionAccepted = 5,
    BriefingEvidenceOpened = 6,
    NoteRefined = 7,
    NoteOrganized = 8,
}

/// <summary>A cheap candidate emitted only after the underlying DeskNote action succeeded.</summary>
public sealed record CompanionActivityCandidate
{
    public required string SourceEventId { get; init; }

    public required CompanionActivityType Type { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public Guid? NoteId { get; init; }

    public string? SourceEntityId { get; init; }

    public int PreviousContentLength { get; init; }

    public int CurrentContentLength { get; init; }

    public TimeSpan DwellTime { get; init; }

    public bool HasFollowUpAction { get; init; }

    /// <summary>Checklist, tag or notebook structure changed, not just prose.</summary>
    public bool StructuralChange { get; init; }

    /// <summary>How old the note was when this happened, which tidying has to clear.</summary>
    public TimeSpan SourceAge { get; init; }
}

/// <summary>An activity that passed deterministic quality and identity checks.</summary>
public sealed record CompanionActivity(
    string SourceEventId,
    CompanionActivityType Type,
    DateTimeOffset OccurredAt,
    DateOnly LocalDate,
    Guid? NoteId,
    string? SourceEntityId);

public sealed class ActivityClassifier
{
    private static readonly TimeSpan RecallDwellThreshold = TimeSpan.FromSeconds(8);

    /// <summary>Tidying a note written moments ago is not tidying, so it earns nothing.</summary>
    private static readonly TimeSpan OrganizeAgeThreshold = TimeSpan.FromHours(24);

    private const int MeaningfulLength = 20;
    private const int RefinementLength = 40;

    public CompanionActivity? Classify(CompanionActivityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.IsNullOrWhiteSpace(candidate.SourceEventId))
        {
            return null;
        }

        var eligible = candidate.Type switch
        {
            CompanionActivityType.MeaningfulCapture => IsMeaningfulCapture(candidate),
            CompanionActivityType.NoteRefined => IsRefinement(candidate),
            CompanionActivityType.NoteOrganized =>
                candidate.SourceAge >= OrganizeAgeThreshold
                && !string.IsNullOrWhiteSpace(candidate.SourceEntityId),
            CompanionActivityType.UsefulRecall =>
                candidate.DwellTime >= RecallDwellThreshold || candidate.HasFollowUpAction,
            CompanionActivityType.ChecklistCompleted or
            CompanionActivityType.ReminderHandled or
            CompanionActivityType.AiSuggestionAccepted => !string.IsNullOrWhiteSpace(candidate.SourceEntityId),
            CompanionActivityType.BriefingEvidenceOpened => candidate.NoteId.HasValue,
            _ => false,
        };

        return eligible
            ? new CompanionActivity(
                candidate.SourceEventId,
                candidate.Type,
                candidate.OccurredAt,
                DateOnly.FromDateTime(candidate.OccurredAt.LocalDateTime),
                candidate.NoteId,
                candidate.SourceEntityId)
            : null;
    }

    /// <summary>A note becoming real for the first time. Later edits are refinements instead.</summary>
    private static bool IsMeaningfulCapture(CompanionActivityCandidate candidate) =>
        candidate.CurrentContentLength >= MeaningfulLength
        && candidate.PreviousContentLength < MeaningfulLength;

    private static bool IsRefinement(CompanionActivityCandidate candidate) =>
        candidate.PreviousContentLength >= MeaningfulLength
        && (Math.Abs(candidate.CurrentContentLength - candidate.PreviousContentLength) >= RefinementLength
            || candidate.StructuralChange);
}
