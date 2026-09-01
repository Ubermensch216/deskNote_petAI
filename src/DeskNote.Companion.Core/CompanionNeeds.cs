namespace DeskNote.Companion.Core;

/// <summary>One care action that actually happened, as stored in the care ledger.</summary>
public readonly record struct CareEvent(CompanionCareAction Action, DateTimeOffset OccurredAt);

/// <summary>
/// What the pet currently wants. Fullness, cleanliness and mood fall with time; bond only rises.
/// </summary>
public readonly record struct CompanionNeeds(
    int Fullness,
    int Cleanliness,
    int Mood,
    int Bond)
{
    public bool IsHungry => Fullness <= CompanionCareRules.FeedThreshold;

    public bool IsUnkempt => Cleanliness <= CompanionCareRules.BasicCareThreshold;

    public bool WantsToPlay => Mood <= CompanionCareRules.PlayThreshold;
}

/// <summary>
/// Projects needs from the care ledger instead of storing them.
/// </summary>
/// <remarks>
/// A stored gauge decremented by a background timer is wrong every time the app was closed, and
/// it cannot be reasoned about in a test without waiting or faking a clock into the database.
/// Replaying the ledger forward from a baseline makes the same numbers a pure function of
/// (now, care history), so an offline week and a running week produce the same answer.
/// </remarks>
public static class NeedsProjector
{
    /// <summary>Care older than this cannot matter: every gauge has already bottomed out.</summary>
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromHours(48);

    /// <summary>An adopted pet starts content rather than starving.</summary>
    public const double AdoptionBaseline = 70;

    public const double FullnessDecayPerHour = 100d / 12;
    public const double CleanlinessDecayPerHour = 100d / 24;
    public const double MoodDecayPerHour = 100d / 10;

    public static CompanionNeeds At(
        DateTimeOffset now,
        DateTimeOffset adoptedAt,
        IReadOnlyList<CareEvent> care,
        int careDays)
    {
        ArgumentNullException.ThrowIfNull(care);

        var windowStart = now - ReplayWindow;
        var startedInsideWindow = adoptedAt >= windowStart;
        var cursor = startedInsideWindow ? adoptedAt : windowStart;
        var fullness = startedInsideWindow ? AdoptionBaseline : 0d;
        var cleanliness = fullness;
        var mood = fullness;

        var replayed = care
            .Where(e => e.OccurredAt >= cursor && e.OccurredAt <= now)
            .OrderBy(e => e.OccurredAt);

        foreach (var entry in replayed)
        {
            var hours = (entry.OccurredAt - cursor).TotalHours;
            fullness = Decay(fullness, hours, FullnessDecayPerHour);
            cleanliness = Decay(cleanliness, hours, CleanlinessDecayPerHour);
            mood = Decay(mood, hours, MoodDecayPerHour);

            var restore = Restore(entry.Action);
            fullness = Math.Min(100, fullness + restore.Fullness);
            cleanliness = Math.Min(100, cleanliness + restore.Cleanliness);
            mood = Math.Min(100, mood + restore.Mood);
            cursor = entry.OccurredAt;
        }

        var tail = Math.Max(0, (now - cursor).TotalHours);
        fullness = Decay(fullness, tail, FullnessDecayPerHour);
        cleanliness = Decay(cleanliness, tail, CleanlinessDecayPerHour);
        mood = Decay(mood, tail, MoodDecayPerHour);

        // A hungry or grubby pet is not in a good mood, so the displayed mood blends the other
        // two gauges in. That is also what keeps play from being available while the pet is fed,
        // clean and freshly entertained.
        var blendedMood = ((mood * 2) + fullness + cleanliness) / 4;

        return new CompanionNeeds(
            Round(fullness),
            Round(cleanliness),
            Round(blendedMood),
            Bond(careDays));
    }

    public static int Bond(int careDays)
    {
        var required = CompanionGrowthLadder.Rung(CompanionGrowthLadder.FinalStage).CareDays;
        return required <= 0
            ? 100
            : Math.Clamp((int)Math.Round(careDays * 100d / required), 0, 100);
    }

    private static NeedRestore Restore(CompanionCareAction action) => action switch
    {
        CompanionCareAction.Feed => new NeedRestore(Fullness: 50),
        CompanionCareAction.BasicCare => new NeedRestore(Cleanliness: 45),
        CompanionCareAction.SpecialCare => new NeedRestore(Cleanliness: 35, Mood: 10),
        CompanionCareAction.Play => new NeedRestore(Mood: 35),
        CompanionCareAction.Greeting => new NeedRestore(Mood: 15),
        _ => new NeedRestore(Mood: 20),
    };

    private static double Decay(double value, double hours, double perHour) =>
        Math.Max(0, value - (Math.Max(0, hours) * perHour));

    private static int Round(double value) => (int)Math.Round(Math.Clamp(value, 0, 100));

    private readonly record struct NeedRestore(
        double Fullness = 0,
        double Cleanliness = 0,
        double Mood = 0);
}

/// <summary>
/// When a care action is worth points.
/// </summary>
/// <remarks>
/// The three actions that map onto a falling gauge are gated on that gauge, so a full pet cannot
/// be fed for points. The three relationship actions have no gauge to read, so a once-a-day
/// allowance is the whole limit; adding an invented gauge for them would only be a cooldown
/// wearing a costume.
/// </remarks>
public static class CompanionCareRules
{
    public const int FeedThreshold = 50;
    public const int BasicCareThreshold = 55;
    public const int PlayThreshold = 65;
    public const int SpecialCareThreshold = 80;

    public static bool IsNeeded(CompanionCareAction action, CompanionNeeds needs) => action switch
    {
        CompanionCareAction.Feed => needs.Fullness <= FeedThreshold,
        CompanionCareAction.BasicCare => needs.Cleanliness <= BasicCareThreshold,
        CompanionCareAction.Play => needs.Mood <= PlayThreshold,
        CompanionCareAction.SpecialCare => needs.Mood <= SpecialCareThreshold,
        _ => true,
    };
}

/// <summary>The species-specific care moment. Every one of them is worth the same points.</summary>
public enum CompanionSpecialCare
{
    Brushing = 1,
    Grooming = 2,
    Walk = 3,
    SandBath = 4,
    WaterPlay = 5,
    Preening = 6,
    ScalePolish = 7,
}

public static class CompanionSpecialCareCatalog
{
    public static CompanionSpecialCare For(CompanionPetKind pet) => pet switch
    {
        CompanionPetKind.Cat => CompanionSpecialCare.Grooming,
        CompanionPetKind.Dog => CompanionSpecialCare.Walk,
        CompanionPetKind.FennecFox => CompanionSpecialCare.SandBath,
        CompanionPetKind.Otter => CompanionSpecialCare.WaterPlay,
        CompanionPetKind.Monkey => CompanionSpecialCare.Preening,
        CompanionPetKind.Dragon => CompanionSpecialCare.ScalePolish,
        _ => CompanionSpecialCare.Brushing,
    };

    /// <summary>Every pet is offered every game, so choosing a species costs no points.</summary>
    public static IReadOnlyList<CompanionPlayKind> PlayKinds { get; } =
    [
        CompanionPlayKind.Chase,
        CompanionPlayKind.Puzzle,
        CompanionPlayKind.Toss,
    ];
}
