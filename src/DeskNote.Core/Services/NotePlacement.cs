using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>
/// A display's usable area — the monitor rectangle minus the taskbar.
/// </summary>
/// <param name="DeviceName">Stable display identifier, stored as <see cref="NoteGeometry.MonitorKey"/>.</param>
public readonly record struct WorkArea(string DeviceName, int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// Decides where a note window belongs on the desktop.
/// </summary>
/// <remarks>
/// Kept free of Win32 so the arithmetic can be tested against monitor layouts that are awkward to
/// reproduce on a developer machine — an unplugged second display, a monitor to the left of the
/// primary with negative coordinates, a note larger than the screen it is being restored onto.
/// The caller supplies the real displays.
/// </remarks>
public static class NotePlacement
{
    /// <summary>How much of a note must remain on screen, so there is always something to grab.</summary>
    public const int MinimumVisibleEdge = 48;

    private const int CascadeStep = 28;
    private const int CascadeWrap = 8;

    /// <summary>
    /// Returns geometry the user can actually reach.
    /// </summary>
    /// <remarks>
    /// Notes have no taskbar button, so one restored beyond the desktop bounds — because the
    /// display it lived on is gone — would be unreachable by any means. The recorded display is
    /// preferred, then whichever display contains the saved point, then the first available one.
    /// </remarks>
    public static NoteGeometry ClampToVisibleArea(NoteGeometry geometry, IReadOnlyList<WorkArea> monitors)
    {
        if (monitors.Count == 0)
        {
            // Nothing to clamp against. Returning the geometry unchanged is better than inventing
            // a screen size and moving the note somewhere equally arbitrary.
            return geometry;
        }

        var target =
            Match(monitors, m => string.Equals(m.DeviceName, geometry.MonitorKey, StringComparison.OrdinalIgnoreCase))
            ?? Match(monitors, m => m.Contains(geometry.X, geometry.Y))
            ?? monitors[0];

        var width = Math.Clamp(geometry.Width, NoteGeometry.MinWidth, Math.Max(NoteGeometry.MinWidth, target.Width));
        var height = Math.Clamp(geometry.Height, NoteGeometry.MinHeight, Math.Max(NoteGeometry.MinHeight, target.Height));

        // A note may hang off the right and bottom edges, as long as enough of its top-left corner
        // stays grabbable; that keeps deliberate edge placements intact instead of snapping them in.
        var minX = target.Left - width + MinimumVisibleEdge;
        var maxX = target.Right - MinimumVisibleEdge;
        var minY = target.Top;
        var maxY = target.Bottom - MinimumVisibleEdge;

        return geometry with
        {
            X = Math.Clamp(geometry.X, minX, Math.Max(minX, maxX)),
            Y = Math.Clamp(geometry.Y, minY, Math.Max(minY, maxY)),
            Width = width,
            Height = height,
            MonitorKey = target.DeviceName,
        };
    }

    /// <summary>
    /// Positions a brand-new note near the centre of <paramref name="monitor"/>, stepped by
    /// <paramref name="cascadeIndex"/> so consecutive notes do not land exactly on top of each other.
    /// </summary>
    public static NoteGeometry PlaceNew(
        NoteSizePreset preset,
        WorkArea monitor,
        int cascadeIndex,
        IReadOnlyList<WorkArea>? monitors = null)
    {
        var (width, height) = NoteGeometry.SizeOf(preset);
        var offset = Math.Abs(cascadeIndex % CascadeWrap) * CascadeStep;

        var geometry = new NoteGeometry(
            monitor.Left + ((monitor.Width - width) / 2) + offset,
            monitor.Top + ((monitor.Height - height) / 2) + offset,
            width,
            height,
            monitor.DeviceName,
            preset);

        return ClampToVisibleArea(geometry, monitors ?? [monitor]);
    }

    /// <summary>
    /// Explicit first-match-or-null. <see cref="Enumerable.FirstOrDefault{T}(IEnumerable{T})"/> on a
    /// struct sequence yields <c>default</c> — a <see cref="WorkArea"/> with a null device name —
    /// which reads as a real result and then fails on first use.
    /// </summary>
    private static WorkArea? Match(IReadOnlyList<WorkArea> monitors, Func<WorkArea, bool> predicate)
    {
        foreach (var monitor in monitors)
        {
            if (predicate(monitor))
            {
                return monitor;
            }
        }

        return null;
    }
}
