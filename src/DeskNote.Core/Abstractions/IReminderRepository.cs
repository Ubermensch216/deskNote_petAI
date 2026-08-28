using DeskNote.Core.Models;

namespace DeskNote.Core.Abstractions;

/// <summary>
/// Persistence for reminders.
/// </summary>
/// <remarks>
/// Report p5 lists reminders as P0 so that a sticky note is an execution tool rather than a
/// passive store. The scheduling decisions live in <see cref="Services.ReminderScheduler"/>; this
/// is only storage.
/// </remarks>
public interface IReminderRepository
{
    Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reminder>> ListForNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reminders that are due and have not been shown yet.
    /// </summary>
    /// <remarks>
    /// Filtering on <c>notified_at</c> in the query rather than in memory is what stops a reminder
    /// firing again every time the app restarts.
    /// </remarks>
    Task<IReadOnlyList<Reminder>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Records that a reminder's toast was raised.</summary>
    Task MarkNotifiedAsync(Guid id, DateTimeOffset notifiedAt, CancellationToken cancellationToken = default);

    /// <summary>Moves a repeating reminder to its next occurrence and clears the notified mark.</summary>
    Task RescheduleAsync(Guid id, DateTimeOffset nextDueAt, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
