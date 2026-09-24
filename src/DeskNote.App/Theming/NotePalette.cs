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
        NoteColors.Yellow => Rgb(0x52, 0x45, 0x1C),
        NoteColors.Amber => Rgb(0x54, 0x38, 0x1C),
        NoteColors.Green => Rgb(0x23, 0x48, 0x2A),
        NoteColors.Teal => Rgb(0x1B, 0x47, 0x45),
        NoteColors.Blue => Rgb(0x20, 0x3C, 0x60),
        NoteColors.Purple => Rgb(0x3C, 0x2C, 0x54),
        NoteColors.Pink => Rgb(0x52, 0x28, 0x42),
        _ => Rgb(0x2E, 0x30, 0x35),
    };

    private static Color DarkBorder(string key) => key switch
    {
        NoteColors.Yellow => Rgb(0x73, 0x5F, 0x28),
        NoteColors.Amber => Rgb(0x75, 0x4E, 0x26),
        NoteColors.Green => Rgb(0x33, 0x63, 0x3A),
        NoteColors.Teal => Rgb(0x27, 0x63, 0x60),
        NoteColors.Blue => Rgb(0x2E, 0x54, 0x85),
        NoteColors.Purple => Rgb(0x55, 0x3F, 0x77),
        NoteColors.Pink => Rgb(0x73, 0x38, 0x5C),
        _ => Rgb(0x45, 0x48, 0x50),
    };

    /// <summary>
    /// Distinct indicator color for small badges, swatches, and list chips.
    /// In dark mode, provides vibrant chromatic identity that stands out against dark backgrounds.
    /// </summary>
    public static Color Swatch(string colorKey, bool isDarkTheme)
    {
        var key = NoteColors.Normalize(colorKey);

        if (!isDarkTheme)
        {
            return LightBackground(key);
        }

        return key switch
        {
            NoteColors.Yellow => Rgb(0xF2, 0xC9, 0x4C),
            NoteColors.Amber => Rgb(0xF2, 0x99, 0x4A),
            NoteColors.Green => Rgb(0x6F, 0xCF, 0x97),
            NoteColors.Teal => Rgb(0x4E, 0xCD, 0xC4),
            NoteColors.Blue => Rgb(0x5D, 0xAD, 0xE2),
            NoteColors.Purple => Rgb(0xBB, 0x6B, 0xD9),
            NoteColors.Pink => Rgb(0xF0, 0x62, 0x92),
            _ => Rgb(0xA0, 0xA4, 0xB0),
        };
    }

    /// <summary>
    /// The color a destructive command is written in.
    /// </summary>
    /// <remarks>
    /// Deliberately not the system's error red: delete sits in a menu on coloured paper, and the
    /// system red is tuned for a white dialog. These two are dimmed enough to stay readable on the
    /// flyout in either theme while still reading as "this one is different from the others".
    /// </remarks>
    public static Color Danger(bool isDarkTheme) =>
        isDarkTheme ? Rgb(0xF2, 0x8B, 0x82) : Rgb(0xC0, 0x39, 0x2B);

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
