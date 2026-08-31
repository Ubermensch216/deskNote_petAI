using DeskNote.Core.Services;

namespace DeskNote.Ai.Tests;

/// <summary>
/// Reading a moment out of a phrase.
/// </summary>
/// <remarks>
/// The schema stops the model returning prose; these tests cover what it cannot stop — a date that
/// is not a date, a frequency the scheduler does not implement, an interval nobody meant. A
/// reminder that fires at a guessed time is worse than one that was never created, because the
/// user stops trusting the ones that were right (report p11).
/// </remarks>
public class ReminderParsingTests
{
    [Fact]
    public void A_moment_and_a_repeat_are_both_read()
    {
        var parsed = AiResponseParser.ReadReminder(
            """{"dueAt":"2026-09-01T15:00:00+09:00","freq":"weekly","interval":1}""");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.FromHours(9)), parsed.DueAt);
        Assert.Equal("FREQ=WEEKLY", parsed.RecurrenceRule);
    }

    /// <summary>격주 — the one phrasing that needs the interval to survive the round trip.</summary>
    [Fact]
    public void An_interval_greater_than_one_is_kept()
    {
        var parsed = AiResponseParser.ReadReminder(
            """{"dueAt":"2026-09-01T09:00:00+09:00","freq":"weekly","interval":2}""");

        Assert.Equal("FREQ=WEEKLY;INTERVAL=2", parsed!.RecurrenceRule);
    }

    [Fact]
    public void A_one_shot_reminder_carries_no_rule()
    {
        var parsed = AiResponseParser.ReadReminder(
            """{"dueAt":"2026-09-01T09:00:00+09:00","freq":null,"interval":null}""");

        Assert.NotNull(parsed);
        Assert.Null(parsed.RecurrenceRule);
    }

    /// <summary>The case the whole design turns on: an unreadable date creates nothing.</summary>
    [Theory]
    [InlineData("""{"dueAt":null}""")]
    [InlineData("""{"dueAt":""}""")]
    [InlineData("""{"dueAt":"언젠가"}""")]
    [InlineData("""{"dueAt":"2026-13-45T99:00:00"}""")]
    public void A_date_that_cannot_be_read_produces_no_reminder(string json)
    {
        Assert.Null(AiResponseParser.ReadReminder(json));
    }

    /// <summary>The scheduler implements four frequencies; anything else is not a schedule.</summary>
    [Theory]
    [InlineData("hourly")]
    [InlineData("fortnightly")]
    [InlineData("")]
    public void A_frequency_the_scheduler_cannot_run_is_dropped(string freq)
    {
        var parsed = AiResponseParser.ReadReminder(
            $$"""{"dueAt":"2026-09-01T09:00:00+09:00","freq":"{{freq}}"}""");

        Assert.NotNull(parsed);
        Assert.Null(parsed.RecurrenceRule);
    }

    /// <summary>A zero interval is not a schedule, and 9999 is a typo rather than a plan.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(9999)]
    public void An_unusable_interval_falls_back_to_every_time(int interval)
    {
        var parsed = AiResponseParser.ReadReminder(
            $$"""{"dueAt":"2026-09-01T09:00:00+09:00","freq":"daily","interval":{{interval}}}""");

        Assert.Equal("FREQ=DAILY", parsed!.RecurrenceRule);
    }

    /// <summary>Whatever comes back has to be something the scheduler can read again.</summary>
    [Fact]
    public void The_rule_that_is_written_is_a_rule_that_can_be_parsed()
    {
        var parsed = AiResponseParser.ReadReminder(
            """{"dueAt":"2026-09-01T09:00:00+09:00","freq":"monthly","interval":3}""");

        var recurrence = Recurrence.Parse(parsed!.RecurrenceRule);

        Assert.NotNull(recurrence);
        Assert.Equal(RecurrenceFrequency.Monthly, recurrence.Value.Frequency);
        Assert.Equal(3, recurrence.Value.Interval);
    }

    [Fact]
    public void Output_that_is_not_json_is_a_provider_failure()
    {
        Assert.Throws<AiProviderException>(() => AiResponseParser.ReadReminder("죄송합니다, 잘 모르겠습니다."));
    }
}

/// <summary>
/// A proposed title is one line, whatever the model wrapped it in.
/// </summary>
public class TitleParsingTests
{
    [Theory]
    [InlineData("""{"title":"회의 준비"}""", "회의 준비")]
    [InlineData("""{"title":"# 회의 준비"}""", "회의 준비")]
    [InlineData("""{"title":"- 회의 준비"}""", "회의 준비")]
    [InlineData("""{"title":"**회의 준비**"}""", "회의 준비")]
    [InlineData("""{"title":"  회의 준비  "}""", "회의 준비")]
    public void A_title_arrives_as_the_line_the_note_will_hold(string json, string expected)
    {
        Assert.Equal(expected, AiResponseParser.ReadTitle(json));
    }

    /// <summary>A heading is one line; a model that wrapped is folded back rather than trusted.</summary>
    [Fact]
    public void A_wrapped_title_becomes_one_line()
    {
        Assert.Equal("회의 준비 자료", AiResponseParser.ReadTitle("""{"title":"회의 준비\n자료"}"""));
    }

    [Fact]
    public void An_empty_title_stays_empty()
    {
        Assert.Empty(AiResponseParser.ReadTitle("""{"title":"   "}"""));
    }
}
