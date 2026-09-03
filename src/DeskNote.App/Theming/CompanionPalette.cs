using Windows.UI;

namespace DeskNote.App.Theming;

/// <summary>Which band a 0–100 gauge sits in, which is what decides its colour.</summary>
public enum CompanionGaugeLevel
{
    /// <summary>Empty enough that the pet is asking for something.</summary>
    Low = 0,

    /// <summary>Falling, but not yet a request.</summary>
    Warning = 1,

    /// <summary>Nothing needed here.</summary>
    Good = 2,
}

/// <summary>What a care tile looks like, which is decided by whether it can be done now.</summary>
public enum CompanionTileState
{
    /// <summary>Doable, and worth points.</summary>
    Ready = 0,

    /// <summary>Doable, and one of today's mission tasks.</summary>
    Mission = 1,

    /// <summary>Already done today. The allowance is spent.</summary>
    Spent = 2,

    /// <summary>The pet does not need it yet, so it would pay nothing.</summary>
    NotNeeded = 3,
}

/// <summary>The four colours one care tile is painted with.</summary>
public readonly record struct CompanionTileSurface(Color Fill, Color Border, Color Ink, Color Subtle);

/// <summary>
/// The colours of the pet dashboard.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard is a game screen, and a game screen that borrows the system's form colours reads
/// as a settings page with animals in it. The palette here is what says "this is the pet", and it
/// carries meaning rather than decoration: a gauge is green, amber or red because of what it is
/// worth doing about it, a tile is gold because it is today's mission, and the hero band is the
/// one saturated surface on the window so the eye starts at the pet.
/// </para>
/// <para>
/// Colours live in code, like <see cref="NotePalette"/>, because the dashboard picks them from
/// runtime state — a gauge value, whether an action is spent — and a resource dictionary would
/// need the same switch plus a missing-key failure mode.
/// </para>
/// </remarks>
public static class CompanionPalette
{
    /// <summary>The window behind the cards. Never white: the cards need something to sit on.</summary>
    public static Color Board(bool dark) => dark ? Rgb(0x14, 0x16, 0x1C) : Rgb(0xF3, 0xF5, 0xFA);

    public static Color Card(bool dark) => dark ? Rgb(0x1D, 0x20, 0x28) : Rgb(0xFF, 0xFF, 0xFF);

    public static Color CardBorder(bool dark) => dark ? Rgb(0x2C, 0x30, 0x3B) : Rgb(0xE3, 0xE7, 0xF0);

    public static Color Ink(bool dark) => dark ? Rgb(0xEC, 0xEE, 0xF4) : Rgb(0x1B, 0x1E, 0x27);

    /// <summary>Secondary text: units, captions, the reason a tile is unavailable.</summary>
    public static Color Subtle(bool dark) => dark ? Rgb(0xA2, 0xA9, 0xBC) : Rgb(0x5B, 0x61, 0x72);

    /// <summary>The hero band's gradient, top-left to bottom-right.</summary>
    public static (Color From, Color To) Hero(bool dark) => dark
        ? (Rgb(0x3C, 0x4E, 0x9E), Rgb(0x6B, 0x3F, 0x9E))
        : (Rgb(0x6A, 0x8B, 0xF7), Rgb(0xA4, 0x72, 0xF0));

    /// <summary>Text on the hero band, which is dark in neither theme.</summary>
    public static Color HeroInk => Rgb(0xFF, 0xFF, 0xFF);

    public static Color HeroSubtle => Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);

    /// <summary>The pet's own plate inside the hero band.</summary>
    public static Color HeroWell => Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);

    /// <summary>The unfilled part of any bar.</summary>
    public static Color Track(bool dark) => dark ? Rgb(0x2A, 0x2E, 0x39) : Rgb(0xE6, 0xE9, 0xF2);

    /// <summary>The unfilled part of a bar drawn on the hero band.</summary>
    public static Color HeroTrack => Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF);

    public static Color Gauge(CompanionGaugeLevel level, bool dark) => level switch
    {
        CompanionGaugeLevel.Low => dark ? Rgb(0xE2, 0x69, 0x5A) : Rgb(0xE0, 0x5B, 0x4A),
        CompanionGaugeLevel.Warning => dark ? Rgb(0xE0, 0xA9, 0x4C) : Rgb(0xE3, 0x9B, 0x2E),
        _ => dark ? Rgb(0x3F, 0xBE, 0x7E) : Rgb(0x2F, 0xAE, 0x6E),
    };

    /// <summary>Bond has no thresholds — it only ever rises — so it gets a colour of its own.</summary>
    public static Color Bond(bool dark) => dark ? Rgb(0xB9, 0x8B, 0xF0) : Rgb(0xB0, 0x6B, 0xE8);

    /// <summary>Today's mission: the one gold on the window, so it cannot be missed.</summary>
    public static Color Mission(bool dark) => dark ? Rgb(0xE8, 0xB6, 0x5C) : Rgb(0xD9, 0x92, 0x2A);

    /// <summary>The app half of the day's budget.</summary>
    public static Color AppBudget(bool dark) => dark ? Rgb(0x7B, 0x95, 0xF8) : Rgb(0x5B, 0x7C, 0xF7);

    /// <summary>The care half of the day's budget.</summary>
    public static Color CareBudget(bool dark) => dark ? Rgb(0x35, 0xBD, 0xB4) : Rgb(0x17, 0xA7, 0xA0);

    public static CompanionTileSurface Tile(CompanionTileState state, bool dark) => (state, dark) switch
    {
        (CompanionTileState.Mission, false) =>
            new(Rgb(0xFF, 0xF6, 0xE4), Rgb(0xE7, 0xBE, 0x72), Rgb(0x1B, 0x1E, 0x27), Rgb(0xA9, 0x7A, 0x2A)),
        (CompanionTileState.Mission, true) =>
            new(Rgb(0x32, 0x2B, 0x1C), Rgb(0x7A, 0x63, 0x30), Rgb(0xF4, 0xEC, 0xDC), Rgb(0xD9, 0xB1, 0x6A)),

        (CompanionTileState.Ready, false) =>
            new(Rgb(0xFF, 0xFF, 0xFF), Rgb(0xD9, 0xDE, 0xEC), Rgb(0x1B, 0x1E, 0x27), Rgb(0x5B, 0x61, 0x72)),
        (CompanionTileState.Ready, true) =>
            new(Rgb(0x26, 0x2A, 0x34), Rgb(0x34, 0x3A, 0x47), Rgb(0xEC, 0xEE, 0xF4), Rgb(0xA2, 0xA9, 0xBC)),

        // Spent and not-needed are both "not now", and telling them apart is the subtitle's job,
        // not the colour's. A third grey would only make the row harder to scan.
        (_, false) => new(Rgb(0xF1, 0xF3, 0xF8), Rgb(0xE3, 0xE7, 0xF0), Rgb(0x8A, 0x91, 0xA4), Rgb(0x9A, 0xA1, 0xB4)),
        (_, true) => new(Rgb(0x1B, 0x1E, 0x25), Rgb(0x26, 0x2A, 0x33), Rgb(0x80, 0x87, 0x98), Rgb(0x6E, 0x74, 0x84)),
    };

    /// <summary>The band a gauge value falls in.</summary>
    public static CompanionGaugeLevel LevelOf(int value, int threshold) => value switch
    {
        _ when value <= threshold => CompanionGaugeLevel.Low,
        _ when value <= threshold + 20 => CompanionGaugeLevel.Warning,
        _ => CompanionGaugeLevel.Good,
    };

    /// <summary>Same colour, lighter, for a hovered tile.</summary>
    public static Color Lift(Color color, bool dark) => dark
        ? Blend(color, Rgb(0xFF, 0xFF, 0xFF), 0.08)
        : Blend(color, Rgb(0x00, 0x00, 0x00), 0.05);

    /// <summary>Same colour, firmer, for a pressed tile.</summary>
    public static Color Press(Color color, bool dark) => dark
        ? Blend(color, Rgb(0xFF, 0xFF, 0xFF), 0.16)
        : Blend(color, Rgb(0x00, 0x00, 0x00), 0.11);

    private static Color Blend(Color color, Color towards, double amount) => Color.FromArgb(
        color.A,
        (byte)Math.Round(color.R + ((towards.R - color.R) * amount)),
        (byte)Math.Round(color.G + ((towards.G - color.G) * amount)),
        (byte)Math.Round(color.B + ((towards.B - color.B) * amount)));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);
}
