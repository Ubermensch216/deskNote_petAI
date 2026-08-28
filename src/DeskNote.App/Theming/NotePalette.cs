using DeskNote.Core.Models;
using Windows.UI;

namespace DeskNote.App.Theming;

/// <summary>The three colors a note needs to paint itself in one theme.</summary>
public readonly record struct NoteSurface(Color Background, Color Border, Color Ink);

/// <summary>
/// Resolves a stored <see cref="NoteColors"/> key to concrete colors.
/// </summary>
/// <remarks>
/// <para>
/// Report p4 asks for six to eight low-saturation colors and, importantly, that the background
/// opacity be adjustable while text contrast is always preserved. That is why ink is a separate
/// color per theme rather than a fixed black: applying opacity to the surface alone keeps the
/// text fully opaque no matter how translucent the note is.
/// </para>
/// <para>
/// Colors live in code rather than a XAML resource dictionary because the note key is a runtime
/// string from the database; a dictionary lookup would need the same switch anyway, plus a
/// failure mode when a key is missing.
/// </para>
/// </remarks>
public static class NotePalette
{
    /// <summary>Ink for light themes: near-black with a warm cast so it reads as paper, not screen.</summary>
    private static readonly Color LightInk = Color.FromArgb(0xFF, 0x2B, 0x27, 0x1D);

    /// <summary>Ink for dark themes.</summary>
    private static readonly Color DarkInk = Color.FromArgb(0xFF, 0xEC, 0xE8, 0xDC);

    public static NoteSurface Resolve(string colorKey, bool isDarkTheme)
    {
        var key = NoteColors.Normalize(colorKey);

        return isDarkTheme
            ? new NoteSurface(DarkBackground(key), DarkBorder(key), DarkInk)
            : new NoteSurface(LightBackground(key), LightBorder(key), LightInk);
    }

    private static Color LightBackground(string key) => key switch
    {
        NoteColors.Yellow => Rgb(0xFF, 0xF4, 0xCC),
        NoteColors.Amber => Rgb(0xFB, 0xE3, 0xC4),
        NoteColors.Green => Rgb(0xDF, 0xEF, 0xD8),
        NoteColors.Teal => Rgb(0xD5, 0xEC, 0xE8),
        NoteColors.Blue => Rgb(0xDA, 0xE6, 0xF5),
        NoteColors.Purple => Rgb(0xE4, 0xDE, 0xF2),
        NoteColors.Pink => Rgb(0xF6, 0xDE, 0xE6),
        _ => Rgb(0xE9, 0xE8, 0xE4),
    };

    private static Color LightBorder(string key) => key switch
    {
        NoteColors.Yellow => Rgb(0xE6, 0xD8, 0xA4),
        NoteColors.Amber => Rgb(0xE2, 0xC5, 0x9C),
        NoteColors.Green => Rgb(0xC2, 0xD9, 0xB8),
        NoteColors.Teal => Rgb(0xB5, 0xD6, 0xD0),
        NoteColors.Blue => Rgb(0xBB, 0xCD, 0xE4),
        NoteColors.Purple => Rgb(0xC7, 0xBE, 0xDE),
        NoteColors.Pink => Rgb(0xE1, 0xC0, 0xCC),
        _ => Rgb(0xCF, 0xCE, 0xC9),
    };

    private static Color DarkBackground(string key) => key switch
    {
        NoteColors.Yellow => Rgb(0x46, 0x3E, 0x1E),
        NoteColors.Amber => Rgb(0x47, 0x35, 0x1D),
        NoteColors.Green => Rgb(0x25, 0x3C, 0x28),
        NoteColors.Teal => Rgb(0x1E, 0x3B, 0x3A),
        NoteColors.Blue => Rgb(0x21, 0x31, 0x43),
        NoteColors.Purple => Rgb(0x30, 0x28, 0x42),
        NoteColors.Pink => Rgb(0x40, 0x24, 0x37),
        _ => Rgb(0x2C, 0x2C, 0x2A),
    };

    private static Color DarkBorder(string key) => key switch
    {
        NoteColors.Yellow => Rgb(0x62, 0x57, 0x2C),
        NoteColors.Amber => Rgb(0x63, 0x4B, 0x2A),
        NoteColors.Green => Rgb(0x37, 0x54, 0x3A),
        NoteColors.Teal => Rgb(0x2C, 0x53, 0x51),
        NoteColors.Blue => Rgb(0x31, 0x46, 0x5E),
        NoteColors.Purple => Rgb(0x45, 0x3A, 0x5D),
        NoteColors.Pink => Rgb(0x5A, 0x36, 0x4D),
        _ => Rgb(0x41, 0x41, 0x3E),
    };

    /// <summary>
    /// Applies the note's opacity to a surface color as alpha.
    /// </summary>
    /// <remarks>
    /// Opacity is deliberately applied here, to the surface color, rather than to the element that
    /// hosts the text. Setting <c>UIElement.Opacity</c> on the note root would fade the text along
    /// with the paper and destroy contrast, which report p4 rules out.
    /// </remarks>
    public static Color WithOpacity(Color color, double opacity)
    {
        var clamped = Math.Clamp(opacity, Note.MinOpacity, 1.0);
        return Color.FromArgb((byte)Math.Round(clamped * 255), color.R, color.G, color.B);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);
}
