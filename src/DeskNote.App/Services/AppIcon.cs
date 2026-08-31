using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace DeskNote.App.Services;

/// <summary>
/// The DeskNote icon, taken from the executable's own icon resource.
/// </summary>
/// <remarks>
/// <para>
/// The icon is read out of the running module rather than off disk, so it cannot go missing
/// between the build output and the user's machine: whatever <c>ApplicationIcon</c> embedded in
/// DeskNote.exe is exactly what the tray and the windows show.
/// </para>
/// <para>
/// Both sizes are asked for separately because Windows picks different frames for different
/// surfaces — the tray and the title bar take the small icon, Alt-Tab and the taskbar the large
/// one — and LoadImage only picks the best-fitting frame if it is told the size it needs.
/// </para>
/// </remarks>
internal static class AppIcon
{
    // The .NET apphost gives the ApplicationIcon the ordinal IDI_APPLICATION uses.
    private const int IconResourceId = 32512;

    private const uint ImageIcon = 1;

    // LR_SHARED: the system caches and owns the handle, so nothing here has to track a lifetime
    // for an icon that lives as long as the process anyway.
    private const uint LrShared = 0x8000;

    private const int SmCxIcon = 11;
    private const int SmCyIcon = 12;
    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;

    /// <summary>The icon at the notification-area / title-bar size, or zero if it is missing.</summary>
    public static nint LoadSmall() => Load(GetSystemMetrics(SmCxSmIcon), GetSystemMetrics(SmCySmIcon));

    /// <summary>The icon at the taskbar / Alt-Tab size, or zero if it is missing.</summary>
    public static nint LoadLarge() => Load(GetSystemMetrics(SmCxIcon), GetSystemMetrics(SmCyIcon));

    /// <summary>
    /// Gives a window the app icon. Best effort: a window with the shell's default icon is a
    /// cosmetic problem, never a reason to fail opening a note.
    /// </summary>
    public static void Apply(Window window)
    {
        var icon = LoadLarge();

        if (icon == nint.Zero)
        {
            return;
        }

        window.AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(icon));
    }

    private static nint Load(int width, int height) =>
        LoadImage(GetModuleHandle(null), IconResourceId, ImageIcon, width, height, LrShared);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, nint name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
