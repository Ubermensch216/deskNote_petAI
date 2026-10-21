using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace DeskNote.App.Services;

/// <summary>
/// What is on the desktop around the pet, as the pet needs to know it.
/// </summary>
/// <remarks>
/// These questions were first asked by <c>DesktopPetWindow</c> to keep itself on top - is anything
/// drawn over me, is that window a full-screen app, is that one of the shell's own surfaces. They
/// live here so that the movement planned next, which treats other windows as ground to stand on,
/// can ask the same questions the same way instead of growing a second, slightly different copy.
/// </remarks>
internal static class DesktopSurfaces
{
    private const uint GwHwndPrev = 3;
    private const int DwmwaCloaked = 14;

    /// <summary>
    /// The nearest window sitting above the pet and overlapping it, or 0 when the pet is clear.
    /// </summary>
    /// <remarks>
    /// GW_HWNDPREV walks towards the front of the z-order, so the loop only ever visits the handful
    /// of windows that are already above the pet rather than enumerating the desktop.
    ///
    /// Two kinds of window are passed over. The shell's own surfaces — taskbar, Start, Alt+Tab,
    /// Task View, the IME bar — are meant to be above everything, and fighting them would put the
    /// pet over the Start menu. DeskNote's own windows are skipped too: the pet dashboard opens
    /// because the user clicked the pet, and a pet that jumped back over the card it had just
    /// opened would be covering the answer to the question that was asked.
    /// </remarks>
    internal static nint FindCoveringWindow(nint self)
    {
        if (!GetWindowRect(self, out var pet))
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(self, out var ownProcess);

        for (var above = GetWindow(self, GwHwndPrev); above != 0; above = GetWindow(above, GwHwndPrev))
        {
            if (!IsWindowVisible(above) || IsIconic(above) || IsCloaked(above))
            {
                continue;
            }

            _ = GetWindowThreadProcessId(above, out var process);
            if (process == ownProcess || IsShellSurface(above))
            {
                continue;
            }

            if (!GetWindowRect(above, out var other) || !Overlaps(pet, other))
            {
                continue;
            }

            return above;
        }

        return 0;
    }

    private static bool Overlaps(NativeRect left, NativeRect right) =>
        left.Left < right.Right
        && right.Left < left.Right
        && left.Top < right.Bottom
        && right.Top < left.Bottom;

    /// <summary>Whether a window covers its whole display, taskbar included — the full-screen tell.</summary>
    /// <remarks>
    /// Maximised is not full-screen: a maximised window stops at the work area, so it leaves the
    /// taskbar strip uncovered and fails this test, which is what keeps the pet on top of ordinary
    /// maximised documents.
    /// </remarks>
    internal static bool FillsItsDisplay(nint window)
    {
        if (!GetWindowRect(window, out var rect))
        {
            return false;
        }

        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(window);
        var bounds = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).OuterBounds;
        return rect.Left <= bounds.X
            && rect.Top <= bounds.Y
            && rect.Right >= bounds.X + bounds.Width
            && rect.Bottom >= bounds.Y + bounds.Height;
    }

    /// <summary>
    /// Whether the window is one the shell puts above everything on purpose.
    /// </summary>
    internal static bool IsShellSurface(nint window)
    {
        var name = new char[64];
        var length = GetClassName(window, name, name.Length);
        if (length <= 0)
        {
            return false;
        }

        var className = new string(name, 0, length);
        return className is "Shell_TrayWnd"
            or "Shell_SecondaryTrayWnd"
            or "Shell_InputSwitchTopLevelWindow"
            or "TopLevelWindowForOverflowXamlIsland"
            or "Progman"
            or "WorkerW"
            or "ForegroundStaging"
            or "MultitaskingViewFrame"
            or "XamlExplorerHostIslandWindow"
            or "TaskSwitcherWnd"
            or "TaskSwitcherOverlayWnd"
            or "Windows.UI.Core.CoreWindow";
    }

    /// <summary>
    /// Whether DWM is hiding the window although Win32 still calls it visible.
    /// </summary>
    /// <remarks>
    /// Store apps and windows parked on another virtual desktop stay visible to
    /// <c>IsWindowVisible</c> long after they stop being drawn. Without this the pet would find a
    /// phantom over itself and re-promote on every tick.
    /// </remarks>
    internal static bool IsCloaked(nint window) =>
        DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, [Out] char[] className, int capacity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out int value,
        int size);
}
