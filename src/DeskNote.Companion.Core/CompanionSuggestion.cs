namespace DeskNote.Companion.Core;

public enum CompanionSuggestionType
{
    UpcomingReminder = 1,
    StaleChecklist = 2,
}

public enum CompanionSuggestionStatus
{
    Pending = 0,
    Acted = 1,
    Dismissed = 2,
}

public sealed record CompanionSuggestion(
    Guid Id,
    CompanionSuggestionType Type,
    Guid NoteId,
    string DedupeKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public interface ICompanionSuggestionService
{
    Task<CompanionSuggestion?> TryOfferAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task SetStatusAsync(
        Guid suggestionId,
        CompanionSuggestionStatus status,
        DateTimeOffset actedAt,
        CancellationToken cancellationToken = default);
}
