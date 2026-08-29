using Microsoft.Win32;

namespace DeskNote.App.Services;

/// <summary>
/// Whether DeskNote starts with Windows.
/// </summary>
/// <remarks>
/// <para>
/// Written to the per-user Run key rather than a machine-wide one or a scheduled task: DeskNote is
/// a personal desktop layer, and "start with Windows" should mean "when I sign in", never "for
/// everyone on this PC". A per-user value also needs no elevation, so the toggle can live in a
/// tray menu instead of behind a prompt.
/// </para>
/// <para>
/// Every operation is best effort. A locked-down or policy-managed machine may refuse the write,
/// and being unable to set an optional convenience must not stop the app from running.
/// </para>
/// </remarks>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeskNote";

    /// <summary>The command Windows would run, quoted so a path with spaces still launches.</summary>
    private static string LaunchCommand => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string existing && existing.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            CrashLog.Write("Could not read the startup registration", ex);
            return false;
        }
    }

    /// <summary>Adds or removes the Run entry. Returns whether the change took effect.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                // Rewritten every time rather than only when absent, so moving or reinstalling the
                // app does not leave Windows launching a path that no longer exists.
                key.SetValue(ValueName, LaunchCommand, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            CrashLog.Write($"Could not {(enabled ? "enable" : "disable")} startup registration", ex);
            return false;
        }
    }

    /// <summary>
    /// Brings the Run key in line with the stored preference at startup.
    /// </summary>
    /// <remarks>
    /// The setting is the source of truth. Re-applying it on every launch repairs an entry another
    /// tool removed, and refreshes the path after the app has been moved.
    /// </remarks>
    public static void Apply(bool enabled)
    {
        if (enabled != IsEnabled() || enabled)
        {
            SetEnabled(enabled);
        }
    }
}
