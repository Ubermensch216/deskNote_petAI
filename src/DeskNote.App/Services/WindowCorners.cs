using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace DeskNote.App.Services;

/// <summary>
/// Asks DWM to round a window's corners.
/// </summary>
/// <remarks>
/// Notes remove their border and title bar, and a borderless top-level window keeps hard corners
/// unless DWM is told otherwise. Rounding at the window level rather than only on the inner
/// surface means the backdrop is clipped too, so no square edge peeks out behind the note.
/// </remarks>
internal static class WindowCorners
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    public static void Round(Window window)
    {
        var handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        var preference = DwmwcpRound;

        // Best effort: on Windows 10 the attribute is unknown and DWM returns a failure HRESULT,
        // which simply leaves the corners square rather than breaking the window.
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
