using System.Diagnostics;
using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private const double WalkLegMinimumSeconds = 3.2;
    private const double WalkLegMaximumSeconds = 9.5;
    private const long WsExTopmost = 0x0000_0008;
    private const nint HwndTopmost = -1;
    private const uint SwpKeepTopmost = 0x0001 | 0x0002 | 0x0010;
    private const double TopmostRecheckSeconds = 1;

    private readonly Action _showDetails;
    private readonly Func<Task> _createNote;
    private readonly Func<PointInt32, Task> _savePosition;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _animationTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _bubbleTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _clickTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _carePropTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _idleChatterTimer;

    /// <summary>
    /// Keeps the pet in the topmost band, on its own clock.
    /// </summary>
    /// <remarks>
    /// The check used to ride on the animation tick, which meant it stopped entirely whenever the
    /// animation did: with 모션 줄이기 on, <see cref="ApplyMotionPreference"/> stops that timer, and
    /// the pet was then left under whatever had covered it for the rest of the session. Staying on
    /// top is not an animation, so it no longer depends on one.
    /// </remarks>
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _topmostTimer;
    private readonly Random _chatter = new();

    /// <summary>
    /// Kept apart from <see cref="_chatter"/> so a change to how often the pet speaks cannot
    /// silently reshuffle how it moves.
    /// </summary>
    private readonly Random _behaviour = new();
    private readonly PetMotionEngine _motion;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private CompanionSettings _settings;
    private TimeSpan _lastTick;
    private double _frameWidth = BaseFrameWidth;
    private double _frameHeight = BaseFrameHeight;
    private double _growthScale = 1;
    private double _frameTime;
    private int _frame;
    private bool _placed;
    private bool _pointerDown;
    private bool _dragMoved;
    private string? _assetKey;
    private int _appearanceStage = -1;
    private CompanionPetKind? _appearancePet;
    private CompanionNeeds _needs = new(100, 100, 100, 0);
    private PetAnimation _animation = PetAnimation.Walk;
    private CompanionGrowthMark _growthMark = CompanionGrowthMark.None;
    private double _poseScale = 1;
    private double _poseAngle;
    private NativePoint _dragStartCursor;

    public DesktopPetWindow(
        CompanionSnapshot snapshot,
        CompanionSettings settings,
        Func<PointInt32, Task> savePosition,
        Action showDetails,
        Func<Task> createNote)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(savePosition);
        ArgumentNullException.ThrowIfNull(showDetails);
        ArgumentNullException.ThrowIfNull(createNote);

        InitializeComponent();
        _settings = settings;
        _motion = new PetMotionEngine(_behaviour);
        _savePosition = savePosition;
        ApplyPetSize(settings.PetSize);
        SetPetAsset(settings.SelectedPet.AssetKey());
        _showDetails = showDetails;
        _createNote = createNote;

        AppWindow.Title = Strings.Get("Companion_Title");
        AppWindow.Resize(new SizeInt32(CurrentWindowWidth, CurrentWindowHeight));
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        try
        {
            AppWindow.IsShownInSwitchers = false;
        }
        catch (Exception)
        {
            // Unpackaged desktop apps throw NotImplementedException on SetShownInSwitchers.
            // WsExToolWindow in MakeBackgroundTransparent handles hiding from switchers.
        }
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        // Always-on-top is deliberately not set here: assigned before the window is first shown it
        // does not stick, and the pet ends up in the ordinary z-order band under whatever the user
        // is working in. AssertAlwaysOnTop runs from OnFirstActivated instead, once there is a live
        // HWND for the window manager to promote.

        MakeBackgroundTransparent();
        ApplyAccessibleName();

        _animationTimer = DispatcherQueue.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(33);
        _animationTimer.Tick += OnAnimationTick;

        _bubbleTimer = DispatcherQueue.CreateTimer();
        _bubbleTimer.Interval = TimeSpan.FromSeconds(4);
        _bubbleTimer.IsRepeating = false;
        _bubbleTimer.Tick += (_, _) =>
        {
            SpeechBubble.Visibility = Visibility.Collapsed;
            UpdateOrnamentVisibility();
        };

        _clickTimer = DispatcherQueue.CreateTimer();
        _clickTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime());
        _clickTimer.IsRepeating = false;
        _clickTimer.Tick += OnSingleClickElapsed;

        _carePropTimer = DispatcherQueue.CreateTimer();
        _carePropTimer.Interval = TimeSpan.FromMilliseconds(33);
        _carePropTimer.Tick += OnCarePropTick;

        _idleChatterTimer = DispatcherQueue.CreateTimer();
        _idleChatterTimer.IsRepeating = false;
        _idleChatterTimer.Tick += OnIdleChatterTick;

        _topmostTimer = DispatcherQueue.CreateTimer();
        _topmostTimer.Interval = TimeSpan.FromSeconds(TopmostRecheckSeconds);
        _topmostTimer.Tick += (_, _) =>
        {
            EnsureStillTopmostSafely();

            // Same once-a-second housekeeping: a display that was unplugged or resized leaves the
            // pet standing where there is no longer any screen.
            if (!_pointerDown)
            {
                KeepOnScreen();
            }
        };

        WatchSession();
        ScheduleIdleChatter();
        UpdateSnapshot(snapshot);
        ApplyMotionPreference();
        Activated += OnFirstActivated;
        Closed += OnClosed;
    }

    public void UpdateSettings(CompanionSettings settings)
    {
        var sizeChanged = _settings.PetSize != settings.PetSize;
        _settings = settings;
        AssertAlwaysOnTop();

        if (sizeChanged)
        {
            ApplyPetSize(settings.PetSize);
        }
        ApplyAccessibleName();
        ApplyMotionPreference();
    }

    /// <summary>
    /// Names the pet the way its owner named it. A screen reader announcing a stock name while
    /// the window shows another one leaves the two users of the same app describing different
    /// pets.
    /// </summary>
    private void ApplyAccessibleName() =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            SpriteViewport,
            Strings.Format("Companion_AccessibleNameFormat", _settings.PetName));

    public void UpdateSnapshot(CompanionSnapshot snapshot)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateSnapshot(snapshot));
            return;
        }

        _unlocks = snapshot.Unlocks;
        SetPetAsset(snapshot.Profile.AppearanceKey);
        ApplyGrowthAppearance(
            CompanionPetCatalog.FromAssetKey(snapshot.Profile.AppearanceKey),
            snapshot.Growth.AppearanceStage);
        _needs = snapshot.Needs;

        if (CelebrateNewStage(snapshot))
        {
            return;
        }

        // Growth is the news worth interrupting for; an unmet need only speaks up when there is
        // nothing better to say, so the pet never nags over its own good news.
        if (snapshot.LastReward.HasGrowth)
        {
            Say("Companion_MoodGrowth");
            return;
        }

        if (NeedKey(snapshot.Needs) is { } key)
        {
            Say(key);
        }
    }

    private static string? NeedKey(CompanionNeeds needs)
    {
        if (needs.Fullness <= CompanionCareRules.FeedThreshold)
        {
            return "Companion_MoodHungry";
        }

        if (needs.Cleanliness <= CompanionCareRules.BasicCareThreshold)
        {
            return "Companion_MoodUnkempt";
        }

        return needs.Mood <= CompanionCareRules.PlayThreshold ? "Companion_MoodBored" : null;
    }

    private static Visibility Collapsed(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_placed)
        {
            return;
        }

        _placed = true;
        RefreshDisplays(force: true);

        var layout = CurrentLayout();
        var ink = CurrentInk();
        var inWindow = PetPoseGeometry.InkBox(layout, ink, TransformFor(PetMotionState.Walking, drawing: false));

        double centerX;
        double? centerY;
        if (_settings.PetPositionX is int savedX && _settings.PetPositionY is int savedY)
        {
            // A saved position is where the window was; the pet is wherever its drawing sits in it.
            centerX = savedX + inWindow.CenterX;
            centerY = savedY + inWindow.CenterY;
        }
        else
        {
            var primary = DisplayArea.Primary.WorkArea;
            centerX = primary.X + primary.Width - 24 - (WindowWidth / 2.0);
            centerY = null;
        }

        var world = BuildWorld(layout, ink, AreaAt(centerX, centerY ?? DisplayArea.Primary.WorkArea.Y));
        var floor = world.Area.Bottom - (world.Bodies.FloorHeight / 2);

        // On the floor it starts walking; anywhere above it, it floats down on its rotor, which is
        // what sorts out a saved position that is no longer where the floor is.
        _lastTick = _clock.Elapsed;
        _motion.Place(_lastTick, centerX, centerY ?? floor, world);
        ApplyStateVisuals();
        ApplyFacingTransform();
        PlaceWindow(layout, ink, world.Bodies);

        AssertAlwaysOnTop();
        _topmostTimer.Start();
        StartHoverWatch();
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
        LayOutRigImage();
        PositionSpeechBubble();
        PositionOrnaments();
    }

    /// <summary>
    /// Puts the pet at the size, facing and pose it should currently have.
    /// </summary>
    /// <remarks>
    /// Both axes always carry the same factor. Anything that scales one differently from the
    /// other is redrawing somebody's artwork to the wrong proportions, and on a pose that
    /// animates it reads as the pet rippling.
    /// </remarks>
    private void ApplyFacingTransform(bool forceRight = false)
    {
        var transform = TransformFor(forceRight ? PetMotionState.Walking : _motion.State, drawing: true);
        FacingTransform.ScaleX = forceRight ? Math.Abs(transform.ScaleX) : transform.ScaleX;
        FacingTransform.ScaleY = transform.ScaleY;
        PoseRotation.Angle = transform.Rotation;

        // Swaying turns about the feet; lying on a wall or hanging from the ceiling turns about the
        // middle of the frame, which keeps the turned pet inside its window.
        SpriteViewport.RenderTransformOrigin = new Windows.Foundation.Point(
            0.5,
            transform.Pivot == PetPivot.Feet ? 1 : 0.5);
    }

    private void OnPetPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(SpriteViewport).Properties.IsLeftButtonPressed
            || !GetCursorPos(out _dragStartCursor))
        {
            return;
        }

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

        // A press that has not moved is a click; the pet is not picked up until the pointer has
        // really gone somewhere, so that clicking a pet on the ceiling does not first pull it down
        // into the hand and set it going from there.
        if (!_dragMoved
            && (Math.Abs(cursor.X - _dragStartCursor.X) > 3 || Math.Abs(cursor.Y - _dragStartCursor.Y) > 3))
        {
            _dragMoved = true;
            BeginHold(cursor);
        }

        if (_dragMoved)
        {
            MoveHeld(cursor);
        }

        e.Handled = true;
    }

    /// <summary>Takes the pet into the hand, remembering where on it the pointer took hold.</summary>
    private void BeginHold(NativePoint cursor)
    {
        if (!_settings.ReduceMotion)
        {
            _motion.Grab();
            ApplyStateVisuals();
            ApplyFacingTransform();
        }

        _grabOffsetX = cursor.X - _motion.CenterX;
        _grabOffsetY = cursor.Y - _motion.CenterY;
    }

    private void MoveHeld(NativePoint cursor)
    {
        var layout = CurrentLayout();
        var ink = CurrentInk();
        var area = AreaAt(cursor.X, cursor.Y);
        var bodies = CurrentBodies(layout, ink);

        // The pet stays on the screen it is carried to, however far the pointer goes.
        var halfWidth = bodies.FloorWidth / 2;
        var halfHeight = bodies.FloorHeight / 2;
        var x = Math.Clamp(cursor.X - _grabOffsetX, area.Left + halfWidth, Math.Max(area.Left + halfWidth, area.Right - halfWidth));
        var y = Math.Clamp(cursor.Y - _grabOffsetY, area.Top + halfHeight, Math.Max(area.Top + halfHeight, area.Bottom - halfHeight));

        if (_settings.ReduceMotion)
        {
            // Told to stand still, the pet stays wherever it is put down: nothing makes it fall.
            _motion.CenterX = x;
            _motion.CenterY = y;
        }
        else
        {
            _motion.Hold(_clock.Elapsed.TotalSeconds, x, y);
        }

        PlaceWindow(layout, ink, bodies);
    }

    private void OnPetPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var clicked = _pointerDown && !_dragMoved;
        SpriteViewport.ReleasePointerCapture(e.Pointer);
        FinishDrag();
        if (clicked)
        {
            FlinchIfDrawn();

            // A click on a pet that is clinging to something lets it go, as well as doing what a
            // click does: it is the quickest way to bring one down.
            if (_motion.IsClimbing)
            {
                LetGo();
            }

            RegisterClick();
        }
        e.Handled = true;
    }

    /// <summary>
    /// One click opens the dashboard; two make a note.
    /// </summary>
    /// <remarks>
    /// The single click still has to wait out the system double-click interval before it acts,
    /// because the first release of a double click looks exactly like a single one. What it costs
    /// is a fraction of a second before the dashboard appears; what it buys is that a double click
    /// never opens the dashboard on its way to the new note.
    /// </remarks>
    private void RegisterClick()
    {
        if (_clickTimer.IsRunning)
        {
            _clickTimer.Stop();
            _ = CreateNoteSafelyAsync();
            return;
        }

        _clickTimer.Start();
    }

    private void OnSingleClickElapsed(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        _showDetails();
    }

    private async Task CreateNoteSafelyAsync()
    {
        try
        {
            await _createNote().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Creating a note from the desktop pet failed", ex);
        }
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

        // A drag that started while a click was still pending reads as neither request, so the
        // pending click is dropped rather than opening a dashboard the user never asked for.
        _clickTimer.Stop();

        if (!_settings.ReduceMotion)
        {
            // Let go gently and the pet floats down; let go at speed and it flies first. Either
            // way it rests a moment once it is down.
            _motion.Release(CurrentWorld());
            BeginRest(_lastTick, seconds: 2.5);
            ApplyStateVisuals();
        }

        _ = SavePositionSafelyAsync(AppWindow.Position);
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
        ApplyRigMode(CompanionPetCatalog.FromAssetKey(assetKey));
        SetAnimation(_animation, force: true);
    }

    private void SetAnimation(PetAnimation animation, bool force = false)
    {
        if (!force && _animation == animation)
        {
            return;
        }

        _animation = animation;
        _frameTime = 0;
        SetFrame(0);
        if (IsRig)
        {
            // A drawn character has no strips to load; the animation is only remembered, because
            // where the head is, and so where the crown and the Zs go, depends on it.
            return;
        }

        var suffix = animation switch
        {
            PetAnimation.Sleep => "sleep",
            PetAnimation.Stretch => "stretch",
            PetAnimation.Rest => "rest",
            _ => "walk",
        };
        SpriteStrip.Source = new BitmapImage(
            new Uri($"ms-appx:///Assets/Companion/{_assetKey}-{suffix}.png"));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        StopWatchingSession();
        StopHoverWatch();
        SetFastCadence(false);
        _animationTimer.Stop();
        _bubbleTimer.Stop();
        _clickTimer.Stop();
        _carePropTimer.Stop();
        _idleChatterTimer.Stop();
        _topmostTimer.Stop();
        _rigSurface?.Dispose();
        _rigSurface = null;
        _transparentBackdrop?.Dispose();
        _transparentBackdrop = null;
        _backdropCompositor?.Dispose();
        _backdropCompositor = null;
    }

    /// <summary>Which sprite sheet is loaded.</summary>
    /// <remarks>
    /// Sleeping earns a sheet of its own because it cannot be faked from the sitting one: a
    /// uniform squash flattens the face, and an eye drawn open cannot be shut from the outside.
    /// The sheets are derived from the rest artwork rather than drawn - see
    /// <c>assets/companion/build_sleep_sprites.py</c>.
    /// </remarks>
    private enum PetAnimation
    {
        Walk,
        Rest,
        Sleep,

        /// <summary>
        /// A stretch drawn for the species that has one; see <see cref="PetStretchArt"/>. The head
        /// is as low as a sleeping pet's, so the crown and the bubble sit where they do for sleep.
        /// </summary>
        Stretch,
    }

}
