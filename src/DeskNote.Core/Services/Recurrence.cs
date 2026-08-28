using System.Globalization;

namespace DeskNote.Core.Services;

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

/// <summary>
/// A repeating reminder schedule.
/// </summary>
/// <remarks>
/// <para>
/// Stored as an RFC 5545 <c>RRULE</c> fragment — <c>FREQ=WEEKLY;INTERVAL=2</c> — so the column can
/// hold a full rule later without a migration, while only the subset a sticky note actually needs
/// is interpreted today: every N days, weeks, months or years.
/// </para>
/// <para>
/// Occurrences are computed from the previous due time rather than from "now", so a reminder the
/// user was away for does not drift: a daily 9am reminder missed for three days is still due at
/// 9am, not at whatever time the app happened to notice.
/// </para>
/// </remarks>
public readonly record struct Recurrence(RecurrenceFrequency Frequency, int Interval = 1)
{
    public string ToRule() =>
        Interval == 1
            ? $"FREQ={Frequency.ToString().ToUpperInvariant()}"
            : $"FREQ={Frequency.ToString().ToUpperInvariant()};INTERVAL={Interval.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Reads a stored rule, returning null for absent or unsupported ones.</summary>
    public static Recurrence? Parse(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return null;
        }

        RecurrenceFrequency? frequency = null;
        var interval = 1;

        foreach (var part in rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            switch (pair[0].Trim().ToUpperInvariant())
            {
                case "FREQ":
                    frequency = pair[1].Trim().ToUpperInvariant() switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        "YEARLY" => RecurrenceFrequency.Yearly,
                        _ => null,
                    };
                    break;

                case "INTERVAL":
                    if (int.TryParse(pair[1].Trim(), CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                    {
                        interval = parsed;
                    }

                    break;
            }
        }

        return frequency is null ? null : new Recurrence(frequency.Value, interval);
    }

    /// <summary>The first occurrence strictly after <paramref name="after"/>, counting from <paramref name="from"/>.</summary>
    /// <remarks>
    /// Every occurrence is measured from the original <paramref name="from"/> rather than by
    /// stepping off the previous result, which is what keeps a monthly reminder anchored to the
    /// day of the month it was set on. Stepping forward one month at a time from 31 January gives
    /// 28 February and then 28 March, quietly moving a month-end reminder to the 28th forever;
    /// counting whole months from the original gives 28 February and then 31 March.
    /// </remarks>
    public DateTimeOffset NextAfter(DateTimeOffset from, DateTimeOffset after)
    {
        // A reminder left alone for years still terminates: the cap covers any real gap and stops
        // a corrupt rule from spinning.
        const int MaxSteps = 10_000;

        var step = 0;
        DateTimeOffset next;

        do
        {
            next = Advance(from, ++step);
        }
        while (next <= after && step < MaxSteps);

        return next;
    }

    private DateTimeOffset Advance(DateTimeOffset origin, int steps) => Frequency switch
    {
        RecurrenceFrequency.Daily => origin.AddDays((double)Interval * steps),
        RecurrenceFrequency.Weekly => origin.AddDays(7d * Interval * steps),
        RecurrenceFrequency.Monthly => origin.AddMonths(Interval * steps),
        _ => origin.AddYears(Interval * steps),
    };
}
