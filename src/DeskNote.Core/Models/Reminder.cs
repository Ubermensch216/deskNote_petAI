namespace DeskNote.Core.Models;

/// <summary>
/// A scheduled notification for a note. Stored so a sticky note is an execution tool rather than
/// a passive store (report p5).
/// </summary>
public sealed record Reminder
{
    public required Guid Id { get; init; }

    public required Guid NoteId { get; init; }

    public required DateTimeOffset DueAt { get; init; }

    /// <summary>RFC 5545 RRULE for repeating reminders; null for one-shot.</summary>
    public string? RecurrenceRule { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// When the toast for the current occurrence was raised. Prevents re-firing a reminder that
    /// already fired when the app restarts.
    /// </summary>
    public DateTimeOffset? NotifiedAt { get; init; }

    public bool IsCompleted => CompletedAt is not null;
}
