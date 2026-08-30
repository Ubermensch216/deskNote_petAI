namespace DeskNote.Core.Models;

/// <summary>
/// The eight low-saturation note colors from report p4. Notes store the key, never a hex value,
/// so light and dark themes can each supply their own surface and text brushes while keeping
/// text contrast intact (report p4 accessibility requirement).
/// </summary>
public static class NoteColors
{
    public const string Yellow = "yellow";
    public const string Amber = "amber";
    public const string Green = "green";
    public const string Teal = "teal";
    public const string Blue = "blue";
    public const string Purple = "purple";
    public const string Pink = "pink";
    public const string Gray = "gray";

    public const string Default = Yellow;

    public static IReadOnlyList<string> All { get; } =
    [
        Yellow, Amber, Green, Teal, Blue, Purple, Pink, Gray,
    ];

    public static bool IsKnown(string colorKey) => All.Contains(colorKey);

    /// <summary>Falls back to <see cref="Default"/> so an unknown key from a future version never breaks restore.</summary>
    public static string Normalize(string? colorKey) =>
        colorKey is not null && IsKnown(colorKey) ? colorKey : Default;
}
