using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>A transparent, animated desktop projection of the companion.</summary>
public sealed partial class DesktopPetWindow : Window
{
    private const int WindowWidth = 240;
    private const int WindowHeight = 250;
    private const double BaseFrameWidth = 150;
    private const double BaseFrameHeight = 200;
    private const int FrameCount = 4;
    private const double WalkSpeed = 58;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x0008_0000;
    private const long WsExToolWindow = 0x0000_0080;
    private const uint DwmBbEnable = 0x0000_0001;
    private const uint DwmBbBlurRegion = 0x0000_0002;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;
    private const uint SwpRefreshFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;

    private readonly Action _showDetails;
    private readonly Func<PointInt32, Task> _savePosition;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _animationTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _bubbleTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Windows.UI.Composition.Compositor? _backdropCompositor;
    private Windows.UI.Composition.CompositionColorBrush? _transparentBackdrop;
    private CompanionSettings _settings;
    private TimeSpan _lastTick;
    private double _x;
    private double _y;
    private double _frameWidth = BaseFrameWidth;
    private double _frameHeight = BaseFrameHeight;
    private double _frameTime;
    private int _frame;
    private int _direction = -1;
    private bool _placed;
    private bool _pointerDown;
    private bool _dragMoved;
    private string? _assetKey;
    private PetAnimation _animation = PetAnimation.Walk;
    private PetMotionState _motionState = PetMotionState.Walking;
    private TimeSpan _restUntil;
    private NativePoint _dragStartCursor;
    private PointInt32 _dragStartWindow;

    public DesktopPetWindow(
        CompanionSnapshot snapshot,
        CompanionSettings settings,
        Func<PointInt32, Task> savePosition,
        Action showDetails)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(savePosition);
        ArgumentNullException.ThrowIfNull(showDetails);

        InitializeComponent();
        _settings = settings;
        _savePosition = savePosition;
        ApplyPetSize(settings.PetSize);
        SetPetAsset(settings.SelectedPet.AssetKey());
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
            presenter.IsAlwaysOnTop = true;
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
        var sizeChanged = _settings.PetSize != settings.PetSize;
        _settings = settings;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }

        if (sizeChanged)
        {
            ApplyPetSize(settings.PetSize);
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

        SetPetAsset(snapshot.Profile.AppearanceKey);
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
        var fallbackArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var requested = _settings.PetPositionX is int savedX
                        && _settings.PetPositionY is int savedY
            ? new PointInt32(savedX, savedY)
            : new PointInt32(
                fallbackArea.WorkArea.X + fallbackArea.WorkArea.Width - WindowWidth - 24,
                fallbackArea.WorkArea.Y + fallbackArea.WorkArea.Height - WindowHeight);
        var area = DisplayArea.GetFromPoint(requested, DisplayAreaFallback.Nearest).WorkArea;
        _x = Math.Clamp(requested.X, area.X, Math.Max(area.X, area.X + area.Width - WindowWidth));
        _y = Math.Clamp(requested.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - WindowHeight));
        AppWindow.Move(new PointInt32((int)_x, (int)_y));
        _lastTick = _clock.Elapsed;
    }

    private void OnAnimationTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        if (!_placed || _settings.ReduceMotion || _pointerDown)
        {
            return;
        }

        var now = _clock.Elapsed;
        var elapsed = Math.Min(0.05, (now - _lastTick).TotalSeconds);
        _lastTick = now;

        if (_motionState == PetMotionState.Resting)
        {
            if (now >= _restUntil)
            {
                BeginWalking();
            }
            else
            {
                AdvanceFrame(elapsed, 0.32);
                return;
            }
        }

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var horizontalInset = (WindowWidth - _frameWidth) / 2;
        var left = area.X - horizontalInset;
        var right = area.X + area.Width - WindowWidth + horizontalInset;
        _x += _direction * WalkSpeed * elapsed;

        if (_x <= left)
        {
            _x = left;
            _direction = 1;
            BeginRest(now);
        }
        else if (_x >= right)
        {
            _x = right;
            _direction = -1;
            BeginRest(now);
        }

        AdvanceFrame(elapsed, 0.13);
        FacingTransform.ScaleX = _direction;
        AppWindow.Move(new PointInt32((int)Math.Round(_x), (int)Math.Round(_y)));
    }

    private void ApplyMotionPreference()
    {
        if (_settings.ReduceMotion)
        {
            _animationTimer.Stop();
            _motionState = PetMotionState.Walking;
            SetAnimation(PetAnimation.Walk);
            _frame = 0;
            Canvas.SetLeft(SpriteStrip, 0);
            FacingTransform.ScaleX = 1;
            return;
        }

        _lastTick = _clock.Elapsed;
        _animationTimer.Start();
    }

    private void ApplyPetSize(CompanionPetSize size)
    {
        var scale = size.Scale();
        _frameWidth = BaseFrameWidth * scale;
        _frameHeight = BaseFrameHeight * scale;
        SpriteViewport.Width = _frameWidth;
        SpriteViewport.Height = _frameHeight;
        SpriteClip.Rect = new Windows.Foundation.Rect(0, 0, _frameWidth, _frameHeight);
        SpriteCanvas.Width = _frameWidth;
        SpriteCanvas.Height = _frameHeight;
        SpriteStrip.Width = _frameWidth * FrameCount;
        SpriteStrip.Height = _frameHeight;
        Canvas.SetLeft(SpriteStrip, -_frame * _frameWidth);
    }

    private void BeginRest(TimeSpan now, double? seconds = null)
    {
        _motionState = PetMotionState.Resting;
        _restUntil = now + TimeSpan.FromSeconds(seconds ?? RestDurationSeconds());
        SetAnimation(PetAnimation.Rest);
        FacingTransform.ScaleX = _direction;
    }

    private void BeginWalking()
    {
        _motionState = PetMotionState.Walking;
        SetAnimation(PetAnimation.Walk);
        _lastTick = _clock.Elapsed;
    }

    private double RestDurationSeconds() => _settings.SelectedPet switch
    {
        CompanionPetKind.Cat => 5.8,
        CompanionPetKind.Dog => 4.4,
        CompanionPetKind.FennecFox => 5.0,
        CompanionPetKind.Otter => 5.4,
        CompanionPetKind.Monkey => 4.8,
        CompanionPetKind.Dragon => 6.0,
        _ => 4.6,
    };

    private void AdvanceFrame(double elapsed, double frameDuration)
    {
        _frameTime += elapsed;
        if (_frameTime < frameDuration)
        {
            return;
        }

        _frameTime %= frameDuration;
        _frame = (_frame + 1) % FrameCount;
        Canvas.SetLeft(SpriteStrip, -_frame * _frameWidth);
    }

    private void OnPetPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(SpriteViewport).Properties.IsLeftButtonPressed
            || !GetCursorPos(out _dragStartCursor))
        {
            return;
        }

        _dragStartWindow = AppWindow.Position;
        _pointerDown = true;
        _dragMoved = false;
        _lastTick = _clock.Elapsed;
        SpriteViewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPetPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerDown
            || !e.GetCurrentPoint(SpriteViewport).Properties.IsLeftButtonPressed
            || !GetCursorPos(out var cursor))
        {
            return;
        }

        var deltaX = cursor.X - _dragStartCursor.X;
        var deltaY = cursor.Y - _dragStartCursor.Y;
        _dragMoved |= Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3;

        var area = DisplayArea.GetFromPoint(
            new PointInt32(cursor.X, cursor.Y),
            DisplayAreaFallback.Nearest).WorkArea;
        var x = Math.Clamp(
            _dragStartWindow.X + deltaX,
            area.X,
            Math.Max(area.X, area.X + area.Width - WindowWidth));
        var y = Math.Clamp(
            _dragStartWindow.Y + deltaY,
            area.Y,
            Math.Max(area.Y, area.Y + area.Height - WindowHeight));
        _x = x;
        _y = y;
        AppWindow.Move(new PointInt32(x, y));
        e.Handled = true;
    }

    private void OnPetPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var shouldOpenDetails = _pointerDown && !_dragMoved;
        SpriteViewport.ReleasePointerCapture(e.Pointer);
        FinishDrag();
        if (shouldOpenDetails)
        {
            _showDetails();
        }
        e.Handled = true;
    }

    private void OnPetPointerCaptureLost(object sender, PointerRoutedEventArgs e) => FinishDrag();

    private void FinishDrag()
    {
        if (!_pointerDown)
        {
            return;
        }

        _pointerDown = false;
        _lastTick = _clock.Elapsed;
        if (!_dragMoved)
        {
            return;
        }

        BeginRest(_lastTick, seconds: 2.5);
        _ = SavePositionSafelyAsync(new PointInt32(
            (int)Math.Round(_x),
            (int)Math.Round(_y)));
    }

    private async Task SavePositionSafelyAsync(PointInt32 position)
    {
        try
        {
            await _savePosition(position).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Saving desktop pet position failed", ex);
        }
    }

    private void SetPetAsset(string assetKey)
    {
        if (string.Equals(_assetKey, assetKey, StringComparison.Ordinal))
        {
            return;
        }

        _assetKey = assetKey;
        SetAnimation(_motionState == PetMotionState.Resting
            ? PetAnimation.Rest
            : PetAnimation.Walk, force: true);
    }

    private void SetAnimation(PetAnimation animation, bool force = false)
    {
        if (!force && _animation == animation)
        {
            return;
        }

        _animation = animation;
        _frame = 0;
        _frameTime = 0;
        Canvas.SetLeft(SpriteStrip, 0);
        var suffix = animation == PetAnimation.Rest ? "rest" : "walk";
        SpriteStrip.Source = new BitmapImage(
            new Uri($"ms-appx:///Assets/Companion/{_assetKey}-{suffix}.png"));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _animationTimer.Stop();
        _bubbleTimer.Stop();
        _transparentBackdrop?.Dispose();
        _transparentBackdrop = null;
        _backdropCompositor?.Dispose();
        _backdropCompositor = null;
    }

    private void MakeBackgroundTransparent()
    {
        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(window, GwlExStyle, new nint(style | WsExLayered | WsExToolWindow));

        var cornerPreference = DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(
            window,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        _ = SetWindowPos(window, 0, 0, 0, 0, 0, SwpRefreshFrame);

        var margins = new Margins();
        _ = DwmExtendFrameIntoClientArea(window, ref margins);

        // WinUI owns a DirectComposition surface, so a layered-window colour key cannot remove
        // its background. Give DWM a transparent system backdrop instead; XAML alpha then reaches
        // the desktop while the PNG and speech bubble keep their own per-pixel opacity.
        var blurRegion = CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new DwmBlurBehind
            {
                Flags = DwmBbEnable | DwmBbBlurRegion,
                Enable = true,
                BlurRegion = blurRegion,
            };
            _ = DwmEnableBlurBehindWindow(window, ref blur);
        }
        finally
        {
            _ = DeleteObject(blurRegion);
        }

        _backdropCompositor = new Windows.UI.Composition.Compositor();
        _transparentBackdrop = _backdropCompositor.CreateColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));
        this.As<ICompositionSupportsSystemBackdrop>().SystemBackdrop = _transparentBackdrop;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Enable;

        public nint BlurRegion;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum PetMotionState
    {
        Walking,
        Resting,
    }

    private enum PetAnimation
    {
        Walk,
        Rest,
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint window, ref DwmBlurBehind blur);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
