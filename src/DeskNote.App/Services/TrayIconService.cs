using System.Runtime.InteropServices;

namespace DeskNote.App.Services;

/// <summary>
/// The notification-area icon.
/// </summary>
/// <remarks>
/// <para>
/// WinUI 3 has no tray icon, so this is Shell_NotifyIcon directly, hosted on its own message-only
/// window for the same reason the hotkeys are: the icon must outlive any individual note window.
/// </para>
/// <para>
/// The tray icon is what makes DeskNote reachable once every note is closed. Without it, closing
/// the last note would leave a running process with no way back to it — report p5 lists
/// tray double-click as a way to create a note precisely because that is the state users end up in.
/// </para>
/// </remarks>
public sealed class TrayIconService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayCallbackMessage = WmApp + 1;
    private const int WmClose = 0x0010;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmCommand = 0x0111;

    private const int NimAdd = 0x0000;
    private const int NimDelete = 0x0002;
    private const uint NifMessage = 0x0001;
    private const uint NifIcon = 0x0002;
    private const uint NifTip = 0x0004;

    private static readonly nint HwndMessage = -3;

    private readonly Action _onNewNote;
    private readonly Action _onOpenLibrary;
    private readonly Action _onBriefing;
    private readonly Action _onExit;
    private readonly Action<bool> _onStartupChanged;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private nint _window;
    private uint _threadId;
    private WndProc? _wndProc;
    private bool _iconAdded;

    public TrayIconService(
        Action onNewNote,
        Action onOpenLibrary,
        Action onExit,
        Action<bool> onStartupChanged,
        Action onBriefing)
    {
        _onNewNote = onNewNote;
        _onOpenLibrary = onOpenLibrary;
        _onBriefing = onBriefing;
        _onExit = onExit;
        _onStartupChanged = onStartupChanged;
    }

    public void Start()
    {
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "DeskNote tray",
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
            lpszClassName = "DeskNoteTrayWindow",
        };

        RegisterClass(ref wndClass);

        _window = CreateWindowEx(
            0, wndClass.lpszClassName, string.Empty, 0, 0, 0, 0, 0,
            HwndMessage, nint.Zero, nint.Zero, nint.Zero);

        _threadId = GetCurrentThreadId();
        AddIcon();
        _ready.Set();

        while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        RemoveIcon();
        DestroyWindow(_window);
        _window = nint.Zero;
    }

    private void AddIcon()
    {
        var data = NewIconData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = TrayCallbackMessage;

        // Falls back to a stock icon rather than showing nothing: a tray icon that failed to load
        // its art is still the only way back to the app once every note is closed.
        var icon = AppIcon.LoadSmall();
        data.hIcon = icon != nint.Zero ? icon : LoadIcon(nint.Zero, 32516 /* IDI_INFORMATION */);
        data.szTip = Strings.Get("Tray_Tooltip");

        _iconAdded = Shell_NotifyIcon(NimAdd, ref data);

        if (!_iconAdded)
        {
            CrashLog.Write($"Could not add the tray icon: win32 error {Marshal.GetLastWin32Error()}");
        }
    }

    private void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = NewIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _iconAdded = false;
    }

    private NotifyIconData NewIconData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window,
        uID = 1,
    };

    private nint HandleMessage(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case TrayCallbackMessage:
                switch ((int)lParam)
                {
                    case WmLButtonDblClk:
                        _onNewNote();
                        return nint.Zero;

                    case WmRButtonUp:
                        ShowMenu();
                        return nint.Zero;
                }

                break;

            case WmCommand:
                switch ((int)wParam & 0xFFFF)
                {
                    case 1: _onNewNote(); return nint.Zero;
                    case 2: _onOpenLibrary(); return nint.Zero;
                    case 3: _onStartupChanged(!StartupRegistration.IsEnabled()); return nint.Zero;
                    case 4: _onExit(); return nint.Zero;
                    case 5: _onBriefing(); return nint.Zero;
                }

                break;

            case WmClose:
                PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, nint.Zero, nint.Zero);
                return nint.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, 1, Strings.Get("Tray_NewNote"));
        AppendMenu(menu, 0, 2, Strings.Get("Tray_Library"));
        AppendMenu(menu, 0, 5, Strings.Get("Tray_Briefing"));
        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, string.Empty);

        // MF_CHECKED reflects the live registry state rather than a cached flag, so the tick is
        // right even if the entry was changed by another tool since the app started.
        var startupFlags = StartupRegistration.IsEnabled() ? 0x8u /* MF_CHECKED */ : 0u;
        AppendMenu(menu, startupFlags, 3, Strings.Get("Tray_StartWithWindows"));

        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, string.Empty);
        AppendMenu(menu, 0, 4, Strings.Get("Tray_Exit"));

        GetCursorPos(out var cursor);

        // Required by TrackPopupMenu: without it the menu does not dismiss when the user clicks
        // away, because the owning window never becomes foreground.
        SetForegroundWindow(_window);
        TrackPopupMenu(menu, 0x0002 /* TPM_RIGHTBUTTON */, cursor.X, cursor.Y, 0, _window, nint.Zero);
        PostMessage(_window, 0, nint.Zero, nint.Zero);
        DestroyMenu(menu);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

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

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint instance, int iconName);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint menu, uint flags, int id, string item);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
}
