namespace DeskNote.Companion.Core;

public sealed record ProactiveContext
{
    public required CompanionSettings Settings { get; init; }
    public required DateTimeOffset Now { get; init; }
    public int OfferedThisHour { get; init; }
    public int OfferedToday { get; init; }
    public DateTimeOffset? LastInteractionAt { get; init; }
    public DateTimeOffset? LastSameTriggerAt { get; init; }
    public int ConsecutiveIgnores { get; init; }
    public DateTimeOffset? MutedUntil { get; init; }
    public bool IsFullScreenOrPresenting { get; init; }
}

/// <summary>Pure interruption guard; it never creates UI and never consults AI.</summary>
public sealed class ProactivePolicy
{
    public bool Allows(ProactiveContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Settings.AllowsProactiveAt(context.Now)
            || context.IsFullScreenOrPresenting
            || context.OfferedThisHour >= 2
            || context.OfferedToday >= 5)
        {
            return false;
        }

        if (context.LastInteractionAt is { } interaction
            && context.Now - interaction < TimeSpan.FromMinutes(10))
        {
            return false;
        }

        if (context.LastSameTriggerAt is { } same
            && context.Now - same < TimeSpan.FromHours(24))
        {
            return false;
        }

        if (context.MutedUntil is { } muted && muted > context.Now)
        {
            return false;
        }

        return context.ConsecutiveIgnores < 3;
    }
}
