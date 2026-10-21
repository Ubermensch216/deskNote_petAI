using System.Runtime.InteropServices;

namespace DeskNote.App.Services;

/// <summary>
/// Reports when nobody can be looking at the screen: the session is locked, or the machine is
/// asleep.
/// </summary>
/// <remarks>
/// <para>
/// Hosted on its own message-only window, the same way the tray icon and the hotkeys are, because
/// both notifications are delivered to a window handle and a WinUI window is a poor place to
/// receive them - its message loop belongs to XAML. The callback runs on this watcher's thread;
/// whoever passes it in is responsible for hopping to the thread it needs.
/// </para>
/// <para>
/// <c>Microsoft.Win32.SystemEvents</c> would do the same job in a dozen lines, but it is not part
/// of the reference set this app builds against, and a package for two window messages is more
/// dependency than they are worth.
/// </para>
/// </remarks>
internal sealed class SessionWatcher : IDisposable
{
    private const int WmClose = 0x0010;
    private const int WmPowerBroadcast = 0x0218;
    private const int WmWtsSessionChange = 0x02B1;

    private const int WtsConsoleDisconnect = 0x2;
    private const int WtsRemoteDisconnect = 0x4;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int WtsConsoleConnect = 0x1;
    private const int WtsRemoteConnect = 0x3;

    private const int PbtApmSuspend = 0x4;
    private const int PbtApmResumeSuspend = 0x7;
    private const int PbtApmResumeAutomatic = 0x12;

    private const int NotifyForThisSession = 0;
    private const int DeviceNotifyWindowHandle = 0;

    private static readonly nint HwndMessage = -3;

    private readonly Action<bool> _onAwayChanged;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private nint _window;
    private uint _threadId;
    private WndProc? _wndProc;
    private nint _suspendNotification;
    private bool _locked;
    private bool _suspended;
    private bool _away;

    /// <param name="onAwayChanged">Called with true when the screen goes unwatched, false when it returns.</param>
    public SessionWatcher(Action<bool> onAwayChanged)
    {
        ArgumentNullException.ThrowIfNull(onAwayChanged);
        _onAwayChanged = onAwayChanged;
    }

    public void Start()
    {
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "DeskNote session watcher",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_window != nint.Zero)
        {
            PostMessage(_window, WmClose, nint.Zero, nint.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        _wndProc = HandleMessage;

        var wndClass = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "DeskNoteSessionWatcher",
        };

        RegisterClass(ref wndClass);

        _window = CreateWindowEx(
            0, wndClass.lpszClassName, string.Empty, 0, 0, 0, 0, 0,
            HwndMessage, nint.Zero, nint.Zero, nint.Zero);

        _threadId = GetCurrentThreadId();

        // Either registration can fail on a locked-down machine. The pet then simply keeps walking
        // behind a lock screen, which is how it behaved before this existed.
        WTSRegisterSessionNotification(_window, NotifyForThisSession);
        _suspendNotification = RegisterSuspendResumeNotification(_window, DeviceNotifyWindowHandle);
        _ready.Set();

        while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        if (_suspendNotification != nint.Zero)
        {
            UnregisterSuspendResumeNotification(_suspendNotification);
        }

        WTSUnRegisterSessionNotification(_window);
        DestroyWindow(_window);
        _window = nint.Zero;
    }

    private nint HandleMessage(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmWtsSessionChange:
                switch ((int)wParam)
                {
                    case WtsSessionLock:
                    case WtsConsoleDisconnect:
                    case WtsRemoteDisconnect:
                        _locked = true;
                        Publish();
                        break;
                    case WtsSessionUnlock:
                    case WtsConsoleConnect:
                    case WtsRemoteConnect:
                        _locked = false;
                        Publish();
                        break;
                }

                return nint.Zero;

            case WmPowerBroadcast:
                switch ((int)wParam)
                {
                    case PbtApmSuspend:
                        _suspended = true;
                        Publish();
                        break;
                    case PbtApmResumeSuspend:
                    case PbtApmResumeAutomatic:
                        _suspended = false;
                        Publish();
                        break;
                }

                return nint.Zero;

            case WmClose:
                PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, nint.Zero, nint.Zero);
                return nint.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    /// <summary>Tells the owner only when "away" actually changes, not on every message.</summary>
    private void Publish()
    {
        var away = _locked || _suspended;
        if (away == _away)
        {
            return;
        }

        _away = away;
        _onAwayChanged(away);
    }

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WndClass wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Msg message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint RegisterSuspendResumeNotification(nint recipient, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterSuspendResumeNotification(nint handle);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSRegisterSessionNotification(nint window, int flags);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSUnRegisterSessionNotification(nint window);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
