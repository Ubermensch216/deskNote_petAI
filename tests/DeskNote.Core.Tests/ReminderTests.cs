using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class ReminderTests
{
    private static readonly DateTimeOffset Nine =
        new(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(9));

    private static Reminder At(DateTimeOffset due, string? rule = null) => new()
    {
        Id = Guid.CreateVersion7(),
        NoteId = Guid.CreateVersion7(),
        DueAt = due,
        RecurrenceRule = rule,
    };

    [Theory]
    [InlineData("FREQ=DAILY", RecurrenceFrequency.Daily, 1)]
    [InlineData("FREQ=WEEKLY;INTERVAL=2", RecurrenceFrequency.Weekly, 2)]
    [InlineData("freq=monthly", RecurrenceFrequency.Monthly, 1)]
    [InlineData("FREQ=YEARLY;INTERVAL=3", RecurrenceFrequency.Yearly, 3)]
    public void Rules_parse(string rule, RecurrenceFrequency frequency, int interval)
    {
        var parsed = Recurrence.Parse(rule);

        Assert.NotNull(parsed);
        Assert.Equal(frequency, parsed.Value.Frequency);
        Assert.Equal(interval, parsed.Value.Interval);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("FREQ=HOURLY")]
    [InlineData("nonsense")]
    public void Absent_or_unsupported_rules_are_not_recurrences(string? rule)
    {
        Assert.Null(Recurrence.Parse(rule));
    }

    [Fact]
    public void A_rule_round_trips()
    {
        var recurrence = new Recurrence(RecurrenceFrequency.Weekly, 2);

        Assert.Equal("FREQ=WEEKLY;INTERVAL=2", recurrence.ToRule());
        Assert.Equal(recurrence, Recurrence.Parse(recurrence.ToRule()));
    }

    [Fact]
    public void An_interval_of_one_is_left_out_of_the_rule()
    {
        Assert.Equal("FREQ=DAILY", new Recurrence(RecurrenceFrequency.Daily).ToRule());
    }

    [Fact]
    public void The_next_occurrence_is_one_step_on()
    {
        var next = new Recurrence(RecurrenceFrequency.Daily).NextAfter(Nine, Nine);

        Assert.Equal(Nine.AddDays(1), next);
    }

    /// <summary>
    /// Occurrences are computed from the previous due time, so a reminder does not drift to
    /// whatever time the machine happened to wake up.
    /// </summary>
    [Fact]
    public void A_daily_reminder_keeps_its_time_of_day_after_a_long_gap()
    {
        var awayForThreeDays = Nine.AddDays(3).AddHours(5);

        var next = new Recurrence(RecurrenceFrequency.Daily).NextAfter(Nine, awayForThreeDays);

        Assert.Equal(9, next.Hour);
        Assert.Equal(Nine.AddDays(4), next);
    }

    /// <summary>Coming back from a week away should produce one reminder, not seven.</summary>
    [Fact]
    public void Missed_occurrences_collapse_into_the_next_one()
    {
        var next = new Recurrence(RecurrenceFrequency.Daily).NextAfter(Nine, Nine.AddDays(7));

        Assert.Equal(Nine.AddDays(8), next);
    }

    /// <summary>
    /// Monthly steps must follow the calendar: from 31 January the sequence is 28 February and
    /// then 31 March, not a run of 28ths.
    /// </summary>
    [Fact]
    public void Monthly_recurrence_follows_calendar_months()
    {
        var lastOfJanuary = new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.FromHours(9));
        var monthly = new Recurrence(RecurrenceFrequency.Monthly);

        var february = monthly.NextAfter(lastOfJanuary, lastOfJanuary);
        var march = monthly.NextAfter(lastOfJanuary, february);

        Assert.Equal(new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.FromHours(9)), february);
        Assert.Equal(new DateTimeOffset(2026, 3, 31, 9, 0, 0, TimeSpan.FromHours(9)), march);
    }

    [Fact]
    public void Only_reminders_that_are_due_and_unshown_fire()
    {
        var due = At(Nine.AddMinutes(-1));
        var future = At(Nine.AddHours(1));
        var alreadyShown = At(Nine.AddMinutes(-5)) with { NotifiedAt = Nine.AddMinutes(-5) };
        var completed = At(Nine.AddMinutes(-5)) with { CompletedAt = Nine.AddMinutes(-4) };

        var selected = ReminderScheduler.SelectDue([due, future, alreadyShown, completed], Nine);

        Assert.Single(selected);
        Assert.Equal(due.Id, selected[0].Id);
    }

    [Fact]
    public void Due_reminders_come_back_oldest_first()
    {
        var later = At(Nine.AddMinutes(-1));
        var earlier = At(Nine.AddHours(-3));

        var selected = ReminderScheduler.SelectDue([later, earlier], Nine);

        Assert.Equal([earlier.Id, later.Id], selected.Select(r => r.Id));
    }

    [Fact]
    public void A_one_off_reminder_is_finished_after_firing()
    {
        var outcome = ReminderScheduler.Resolve(At(Nine), Nine);

        Assert.False(outcome.Repeats);
        Assert.Null(outcome.NextDueAt);
    }

    [Fact]
    public void A_repeating_reminder_moves_to_its_next_occurrence()
    {
        var outcome = ReminderScheduler.Resolve(At(Nine, "FREQ=WEEKLY"), Nine);

        Assert.True(outcome.Repeats);
        Assert.Equal(Nine.AddDays(7), outcome.NextDueAt);
    }

    /// <summary>An unreadable rule must not turn a reminder into one that never stops firing.</summary>
    [Fact]
    public void An_unreadable_rule_is_treated_as_one_off()
    {
        var outcome = ReminderScheduler.Resolve(At(Nine, "FREQ=EVERY-SO-OFTEN"), Nine);

        Assert.False(outcome.Repeats);
    }

    [Fact]
    public void Resolving_a_long_overdue_repeat_lands_in_the_future()
    {
        var now = Nine.AddYears(1);

        var outcome = ReminderScheduler.Resolve(At(Nine, "FREQ=DAILY"), now);

        Assert.NotNull(outcome.NextDueAt);
        Assert.True(outcome.NextDueAt > now);
    }
}
