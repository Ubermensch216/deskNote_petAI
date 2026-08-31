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

    private static bool IsMeaningfulCapture(CompanionActivityCandidate candidate) =>
        candidate.CurrentContentLength >= 20
        && (candidate.PreviousContentLength < 20
            || Math.Abs(candidate.CurrentContentLength - candidate.PreviousContentLength) >= 40);
}
