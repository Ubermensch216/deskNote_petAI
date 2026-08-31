using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>A transparent, animated desktop projection of the companion.</summary>
public sealed partial class DesktopPetWindow : Window
{
    private const int WindowWidth = 240;
    private const int WindowHeight = 250;
    private const double FrameWidth = 140;
    private const double WalkSpeed = 58;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x0008_0000;
    private const long WsExToolWindow = 0x0000_0080;
    private const uint LwaColorKey = 0x0000_0001;

    private readonly Action _showDetails;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _animationTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _bubbleTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CompanionSettings _settings;
    private TimeSpan _lastTick;
    private double _x;
    private double _frameTime;
    private int _frame;
    private int _direction = -1;
    private bool _placed;

    public DesktopPetWindow(
        CompanionSnapshot snapshot,
        CompanionSettings settings,
        Action showDetails)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showDetails);

        InitializeComponent();
        _settings = settings;
        _showDetails = showDetails;

        AppWindow.Title = Strings.Get("Companion_Title");
        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
        }

        MakeBackgroundTransparent();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            SpriteViewport,
            Strings.Get("Companion_AccessibleName"));

        _animationTimer = DispatcherQueue.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(33);
        _animationTimer.Tick += OnAnimationTick;

        _bubbleTimer = DispatcherQueue.CreateTimer();
        _bubbleTimer.Interval = TimeSpan.FromSeconds(4);
        _bubbleTimer.IsRepeating = false;
        _bubbleTimer.Tick += (_, _) => SpeechBubble.Visibility = Visibility.Collapsed;

        UpdateSnapshot(snapshot);
        ApplyMotionPreference();
        Activated += OnFirstActivated;
        Closed += OnClosed;
    }

    public void UpdateSettings(CompanionSettings settings)
    {
        _settings = settings;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
        }

        ApplyMotionPreference();
    }

    public void UpdateSnapshot(CompanionSnapshot snapshot)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateSnapshot(snapshot));
            return;
        }

        if (!snapshot.LastReward.HasGrowth)
        {
            return;
        }

        SpeechText.Text = Strings.Get("Companion_MoodGrowth");
        SpeechBubble.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();
        _bubbleTimer.Start();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_placed)
        {
            return;
        }

        _placed = true;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        _x = area.X + area.Width - WindowWidth - 24;
        AppWindow.Move(new PointInt32((int)_x, area.Y + area.Height - WindowHeight));
        _lastTick = _clock.Elapsed;
    }

    private void OnAnimationTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        if (!_placed || _settings.ReduceMotion)
        {
            return;
        }

        var now = _clock.Elapsed;
        var elapsed = Math.Min(0.05, (now - _lastTick).TotalSeconds);
        _lastTick = now;

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var left = area.X;
        var right = area.X + area.Width - WindowWidth;
        _x += _direction * WalkSpeed * elapsed;

        if (_x <= left)
        {
            _x = left;
            _direction = 1;
        }
        else if (_x >= right)
        {
            _x = right;
            _direction = -1;
        }

        _frameTime += elapsed;
        if (_frameTime >= 0.13)
        {
            _frameTime %= 0.13;
            _frame = (_frame + 1) % 4;
            Canvas.SetLeft(SpriteStrip, -_frame * FrameWidth);
        }

        FacingTransform.ScaleX = _direction;
        AppWindow.Move(new PointInt32((int)Math.Round(_x), area.Y + area.Height - WindowHeight));
    }

    private void ApplyMotionPreference()
    {
        if (_settings.ReduceMotion)
        {
            _animationTimer.Stop();
            _frame = 0;
            Canvas.SetLeft(SpriteStrip, 0);
            FacingTransform.ScaleX = 1;
            return;
        }

        _lastTick = _clock.Elapsed;
        _animationTimer.Start();
    }

    private void OnPetTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        _showDetails();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _animationTimer.Stop();
        _bubbleTimer.Stop();
    }

    private void MakeBackgroundTransparent()
    {
        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(window, GwlExStyle, new nint(style | WsExLayered | WsExToolWindow));
        _ = SetLayeredWindowAttributes(window, 0, 255, LwaColorKey);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(
        nint window,
        uint colorKey,
        byte alpha,
        uint flags);
}
