using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace DeskNote.App.Services;

/// <summary>
/// Detects Windows system theme, monitors theme changes at runtime,
/// and applies immersive dark mode to window frames.
/// </summary>
public static class ThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    private static readonly UISettings? _uiSettings;
    private static DispatcherQueue? _dispatcher;
    private static bool _isDarkTheme;
    private static bool _initialized;

    static ThemeService()
    {
        try
        {
            _uiSettings = new UISettings();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not initialize UISettings", ex);
        }

        _isDarkTheme = DetectSystemDarkTheme();
    }

    /// <summary>Whether the Windows system or application theme is currently dark.</summary>
    public static bool IsDarkTheme => _isDarkTheme;

    /// <summary>Raised on the UI thread when the system theme switches.</summary>
    public static event EventHandler<bool>? ThemeChanged;

    /// <summary>
    /// Connects the UI thread dispatcher so system theme events can marshal back to the UI.
    /// </summary>
    public static void Initialize(DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (_initialized)
        {
            return;
        }

        _dispatcher = dispatcher;
        _initialized = true;

        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        }

        _isDarkTheme = DetectSystemDarkTheme();
    }

    /// <summary>
    /// Applies the current theme to a window's content and its native title bar.
    /// </summary>
    public static void ApplyThemeToWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;
        }

        ApplyWindowImmersiveDarkMode(window, _isDarkTheme);
    }

    /// <summary>
    /// Sets the DWM immersive dark mode attribute on the window's top-level HWND.
    /// </summary>
    public static void ApplyWindowImmersiveDarkMode(Window window, bool isDark)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            if (hwnd == nint.Zero)
            {
                return;
            }

            var darkMode = isDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref darkMode, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Applying immersive dark mode to window failed", ex);
        }
    }

    private static void OnColorValuesChanged(UISettings sender, object args)
    {
        var newDark = DetectSystemDarkTheme();
        if (newDark == _isDarkTheme)
        {
            return;
        }

        _isDarkTheme = newDark;

        if (_dispatcher is not null)
        {
            _ = _dispatcher.TryEnqueue(() => ThemeChanged?.Invoke(null, _isDarkTheme));
        }
        else
        {
            ThemeChanged?.Invoke(null, _isDarkTheme);
        }
    }

    private static bool DetectSystemDarkTheme()
    {
        try
        {
            if (_uiSettings is not null)
            {
                var bg = _uiSettings.GetColorValue(UIColorType.Background);
                return (bg.R + bg.G + bg.B) < (128 * 3);
            }
        }
        catch
        {
            // Fall back to registry or Application.Current
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLight)
            {
                return appsUseLight == 0;
            }
        }
        catch
        {
            // Fall back
        }

        try
        {
            if (Application.Current is { } app)
            {
                return app.RequestedTheme == ApplicationTheme.Dark;
            }
        }
        catch
        {
            // Fall back
        }

        return false;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
