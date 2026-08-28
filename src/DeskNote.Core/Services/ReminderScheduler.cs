using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>What should happen to a reminder once its toast has been raised.</summary>
/// <param name="Reminder">The reminder that fired.</param>
/// <param name="NextDueAt">When it should next fire, or null if it is finished.</param>
public readonly record struct ReminderOutcome(Reminder Reminder, DateTimeOffset? NextDueAt)
{
    public bool Repeats => NextDueAt is not null;
}

/// <summary>
/// Decides which reminders are due and what happens to them afterwards.
/// </summary>
/// <remarks>
/// Kept free of timers and storage so the awkward cases — a machine asleep across three
/// occurrences, a reminder whose rule is unreadable, an app restarted after a toast was already
/// shown — can be tested directly rather than by waiting.
/// </remarks>
public static class ReminderScheduler
{
    /// <summary>The reminders in <paramref name="reminders"/> that should fire now.</summary>
    public static IReadOnlyList<Reminder> SelectDue(IEnumerable<Reminder> reminders, DateTimeOffset now) =>
        reminders
            .Where(r => !r.IsCompleted && r.NotifiedAt is null && r.DueAt <= now)
            .OrderBy(r => r.DueAt)
            .ToList();

    /// <summary>
    /// Works out where a reminder goes after firing.
    /// </summary>
    /// <remarks>
    /// A repeating reminder skips straight to the next occurrence after now rather than firing
    /// once per missed period: coming back from a week away should produce one reminder, not seven.
    /// </remarks>
    public static ReminderOutcome Resolve(Reminder reminder, DateTimeOffset now)
    {
        var recurrence = Recurrence.Parse(reminder.RecurrenceRule);
        if (recurrence is null)
        {
            return new ReminderOutcome(reminder, null);
        }

        return new ReminderOutcome(reminder, recurrence.Value.NextAfter(reminder.DueAt, now));
    }
}
