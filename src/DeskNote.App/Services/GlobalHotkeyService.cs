using System.Runtime.InteropServices;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

/// <summary>A hotkey that could not be claimed, and the gesture that was wanted.</summary>
/// <param name="Command">The command left without a hotkey.</param>
/// <param name="Gesture">The combination that was refused.</param>
public readonly record struct HotkeyRegistrationFailure(string Command, HotkeyGesture Gesture);

/// <summary>
/// Registers the application's global hotkeys with Windows.
/// </summary>
/// <remarks>
/// <para>
/// The hotkeys are owned by a message-only window created on its own thread. A global hotkey is
/// delivered to whichever window registered it, and note windows come and go — binding them to a
/// note would lose them the moment that note was closed.
/// </para>
/// <para>
/// Registration failures are reported rather than swallowed. Another application may already own
/// Ctrl+Alt+N, and report p5 requires the user be told instead of being left pressing a key that
/// does nothing.
/// </para>
/// </remarks>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmClose = 0x0010;

    /// <summary>Sentinel HWND that makes CreateWindowEx produce a message-only window.</summary>
    private static readonly nint HwndMessage = -3;

    private readonly Dictionary<int, string> _commandsById = [];
    private readonly List<HotkeyRegistrationFailure> _failures = [];
    private readonly Action<string> _invoke;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private nint _window;
    private uint _threadId;
    private WndProc? _wndProc;
    private HotkeyBindings _bindings = HotkeyBindings.Defaults();

    /// <param name="invoke">
    /// Called with the command name when a hotkey fires. Runs on the hotkey thread, so the handler
    /// is responsible for getting back to the UI queue.
    /// </param>
    public GlobalHotkeyService(Action<string> invoke) => _invoke = invoke;

    /// <summary>Hotkeys Windows refused to give us, for the settings screen to show.</summary>
    public IReadOnlyList<HotkeyRegistrationFailure> Failures
    {
        get
        {
            lock (_failures)
            {
                return _failures.ToList();
            }
        }
    }

    /// <summary>Starts the hotkey thread and claims the given bindings.</summary>
    public void Start(HotkeyBindings bindings)
    {
        _bindings = bindings;

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "DeskNote hotkeys",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Block until the window exists, so Failures is meaningful as soon as Start returns.
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
        // Held in a field so the delegate is not collected while Windows still holds the pointer.
        _wndProc = HandleMessage;

        var wndClass = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "DeskNoteHotkeyWindow",
        };

        RegisterClass(ref wndClass);

        _window = CreateWindowEx(
            0, wndClass.lpszClassName, string.Empty, 0, 0, 0, 0, 0,
            HwndMessage, nint.Zero, nint.Zero, nint.Zero);

        _threadId = GetCurrentThreadId();

        RegisterAll();
        _ready.Set();

        while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        UnregisterAll();
        DestroyWindow(_window);
        _window = nint.Zero;
    }

    private void RegisterAll()
    {
        var id = 1;

        foreach (var command in HotkeyCommands.All)
        {
            if (!_bindings.TryGet(command, out var gesture))
            {
                continue;
            }

            if (!TryGetVirtualKey(gesture.Key, out var virtualKey))
            {
                Record(command, gesture, "unknown key");
                continue;
            }

            if (RegisterHotKey(_window, id, (uint)gesture.Modifiers, virtualKey))
            {
                _commandsById[id] = command;
                id++;
            }
            else
            {
                Record(command, gesture, $"win32 error {Marshal.GetLastWin32Error()}");
            }
        }
    }

    private void Record(string command, HotkeyGesture gesture, string reason)
    {
        lock (_failures)
        {
            _failures.Add(new HotkeyRegistrationFailure(command, gesture));
        }

        CrashLog.Write($"Could not register {gesture} for {command}: {reason}");
    }

    private void UnregisterAll()
    {
        foreach (var id in _commandsById.Keys)
        {
            UnregisterHotKey(_window, id);
        }

        _commandsById.Clear();
    }

    private nint HandleMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmHotkey && _commandsById.TryGetValue((int)wParam, out var command))
        {
            _invoke(command);
            return nint.Zero;
        }

        if (message == WmClose)
        {
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, nint.Zero, nint.Zero);
            return nint.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    /// <summary>
    /// Maps a key name to its virtual-key code.
    /// </summary>
    /// <remarks>
    /// Only the keys a sticky note actually binds are listed. Accepting anything else would let a
    /// user store a gesture that parses but can never be registered.
    /// </remarks>
    private static bool TryGetVirtualKey(string key, out uint virtualKey)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.Length > 1 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "ESCAPE" or "ESC" => 0x1B,
            "TAB" => 0x09,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            _ => 0,
        };

        return virtualKey != 0;
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

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);
}
