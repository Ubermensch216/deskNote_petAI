namespace DeskNote.Core.Models;

/// <summary>
/// Position and size of a note window, plus the monitor it was last placed on.
/// <see cref="MonitorKey"/> is a stable device identifier so restore can detect that the
/// monitor is gone and clamp the note back onto the primary work area instead of opening
/// it off-screen.
/// </summary>
public readonly record struct NoteGeometry(
    int X,
    int Y,
    int Width,
    int Height,
    string MonitorKey,
    NoteSizePreset Preset)
{
    public const int SmallWidth = 240;
    public const int SmallHeight = 180;
    public const int MediumWidth = 320;
    public const int MediumHeight = 260;
    public const int LargeWidth = 420;
    public const int LargeHeight = 360;

    /// <summary>Smallest window the UI will allow, below which the editor stops being usable.</summary>
    public const int MinWidth = 180;
    public const int MinHeight = 120;

    /// <summary>Sentinel meaning "no monitor recorded yet"; restore treats it as the primary monitor.</summary>
    public const string UnknownMonitor = "";

    public static NoteGeometry Default { get; } = ForPreset(NoteSizePreset.Medium, 0, 0, UnknownMonitor);

    public static (int Width, int Height) SizeOf(NoteSizePreset preset) => preset switch
    {
        NoteSizePreset.Small => (SmallWidth, SmallHeight),
        NoteSizePreset.Medium => (MediumWidth, MediumHeight),
        NoteSizePreset.Large => (LargeWidth, LargeHeight),
        _ => (MediumWidth, MediumHeight),
    };

    public static NoteGeometry ForPreset(NoteSizePreset preset, int x, int y, string monitorKey)
    {
        var (width, height) = SizeOf(preset);
        return new NoteGeometry(x, y, width, height, monitorKey, preset);
    }

    /// <summary>
    /// Returns the geometry with a preset re-derived from the current size, so that a note the
    /// user resized by hand is recorded as <see cref="NoteSizePreset.Custom"/>.
    /// </summary>
    public NoteGeometry WithSize(int width, int height)
    {
        var preset = NoteSizePreset.Custom;
        foreach (var candidate in new[] { NoteSizePreset.Small, NoteSizePreset.Medium, NoteSizePreset.Large })
        {
            var (presetWidth, presetHeight) = SizeOf(candidate);
            if (presetWidth == width && presetHeight == height)
            {
                preset = candidate;
                break;
            }
        }

        return this with { Width = width, Height = height, Preset = preset };
    }
}
