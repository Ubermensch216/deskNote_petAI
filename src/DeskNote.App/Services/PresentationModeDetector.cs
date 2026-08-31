using System.Runtime.InteropServices;

namespace DeskNote.App.Services;

/// <summary>Best-effort Windows signal used to suppress companion cards during presentation.</summary>
internal static class PresentationModeDetector
{
    public static bool AllowsUserNotification()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0
                && state == QueryUserNotificationState.AcceptsNotifications;
        }
        catch (DllNotFoundException)
        {
            return true;
        }
    }

    private enum QueryUserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningDirect3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);
}
