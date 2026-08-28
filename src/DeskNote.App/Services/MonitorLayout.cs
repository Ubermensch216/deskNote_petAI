using System.Runtime.InteropServices;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

/// <summary>
/// Enumerates the machine's displays and hands them to <see cref="NotePlacement"/>.
/// </summary>
/// <remarks>
/// This type is the Win32 half only: it knows how to ask Windows what displays exist and which one
/// a window is on. All of the decision-making about where a note should end up lives in
/// <see cref="NotePlacement"/>, where it can be tested against display layouts that are impractical
/// to reproduce on real hardware.
/// </remarks>
public static class MonitorLayout
{
    private const int MonitorDefaultToNearest = 0x00000002;
    private const int MonitorDefaultToPrimary = 0x00000001;

    public static IReadOnlyList<WorkArea> All()
    {
        var monitors = new List<WorkArea>();

        bool Callback(nint monitor, nint _, ref Rect __, nint ___)
        {
            if (TryDescribe(monitor, out var area))
            {
                monitors.Add(area);
            }

            return true;
        }

        // Held in a local so the delegate cannot be collected while the callback is in flight.
        MonitorEnumProc callback = Callback;
        EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);
        GC.KeepAlive(callback);

        return monitors;
    }

    public static WorkArea Primary()
    {
        var monitor = MonitorFromPoint(new Point { X = 0, Y = 0 }, MonitorDefaultToPrimary);
        return TryDescribe(monitor, out var area) ? area : new WorkArea(string.Empty, 0, 0, 1920, 1080);
    }

    public static WorkArea ForPoint(int x, int y)
    {
        var monitor = MonitorFromPoint(new Point { X = x, Y = y }, MonitorDefaultToNearest);
        return TryDescribe(monitor, out var area) ? area : Primary();
    }

    /// <summary>The display device name a window currently sits on, used as <see cref="NoteGeometry.MonitorKey"/>.</summary>
    public static string DeviceNameForWindow(nint windowHandle)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        return TryDescribe(monitor, out var area) ? area.DeviceName : string.Empty;
    }

    /// <summary>Cursor position in physical screen pixels, the same space AppWindow.Move uses.</summary>
    public static bool TryGetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            (x, y) = (point.X, point.Y);
            return true;
        }

        (x, y) = (0, 0);
        return false;
    }

    /// <summary>Clamps saved geometry onto a display that is actually attached right now.</summary>
    public static NoteGeometry ClampToVisibleArea(NoteGeometry geometry) =>
        NotePlacement.ClampToVisibleArea(geometry, All());

    /// <summary>Places a new note on the display under the mouse cursor.</summary>
    public static NoteGeometry PlaceNewNote(NoteSizePreset preset, int cascadeIndex)
    {
        var monitors = All();
        var monitor = GetCursorPos(out var cursor) ? ForPoint(cursor.X, cursor.Y) : Primary();

        return NotePlacement.PlaceNew(preset, monitor, cascadeIndex, monitors);
    }

    private static bool TryDescribe(nint monitor, out WorkArea area)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            area = default;
            return false;
        }

        // rcWork rather than rcMonitor: notes should not be restored underneath the taskbar.
        area = new WorkArea(
            info.szDevice ?? string.Empty,
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Right,
            info.rcWork.Bottom);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint monitor, nint deviceContext, ref Rect clip, nint data);

    // DllImport rather than LibraryImport: the source-generated marshaller supports neither the
    // delegate callback EnumDisplayMonitors takes nor the ByValTStr device-name field in
    // MONITORINFOEX, both of which this file needs.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, int flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}
