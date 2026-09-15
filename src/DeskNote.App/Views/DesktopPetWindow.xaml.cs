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
    private const double CarePropSeconds = 2.6;
    private const double WalkLegMinimumSeconds = 3.2;
    private const double WalkLegMaximumSeconds = 9.5;
    private const double OrnamentMinimumScale = 0.5;
    private const double HeadTopFraction = 0.78;
    private const double RestEffectFraction = 0.68;

    // The sleeping artwork is the same frame with the animal lying down in it, so everything
    // that hangs off the pet's head has to come down with it. Measured off the generated sheets
    // by assets/companion/build_sleep_sprites.py, which prints the content bounds it produced.
    private const double SleepHeadTopFraction = 0.58;
    private const double SleepRestEffectFraction = 0.50;
    private const double SleepSpeechFraction = 0.66;
    private const double RestEffectSideOffset = 18;
    private const double SpeechBubbleGap = 2;
    private const double SpeechBubbleReserve = 58;
    private const int IdleChatterMinimumSeconds = 45;
    private const int IdleChatterMaximumSeconds = 105;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x0008_0000;
    private const long WsExToolWindow = 0x0000_0080;
    private const long WsExTopmost = 0x0000_0008;
    private const nint HwndTopmost = -1;
    private const uint SwpKeepTopmost = 0x0001 | 0x0002 | 0x0010;
    private const double TopmostRecheckSeconds = 2;
    private const uint DwmBbEnable = 0x0000_0001;
    private const uint DwmBbBlurRegion = 0x0000_0002;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;
    private const uint SwpRefreshFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;

    private readonly Action _showDetails;
    private readonly Func<Task> _createNote;
    private readonly Func<PointInt32, Task> _savePosition;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _animationTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _bubbleTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _clickTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _carePropTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _idleChatterTimer;
    private readonly Random _chatter = new();

    /// <summary>
    /// Kept apart from <see cref="_chatter"/> so a change to how often the pet speaks cannot
    /// silently reshuffle how it moves.
    /// </summary>
    private readonly Random _behaviour = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    [ThreadStatic]
    private static object? _systemDispatcherQueueController;

    private Windows.UI.Composition.Compositor? _backdropCompositor;
    private Windows.UI.Composition.CompositionColorBrush? _transparentBackdrop;
    private CompanionSettings _settings;
    private TimeSpan _lastTick;
    private double _x;
    private double _y;
    private double _frameWidth = BaseFrameWidth;
    private double _frameHeight = BaseFrameHeight;
    private double _growthScaleX = 1;
    private double _growthScaleY = 1;
    private double _frameTime;
    private int _frame;
    private int _direction = -1;
    private bool _placed;
    private bool _pointerDown;
    private bool _dragMoved;
    private string? _assetKey;
    private int _appearanceStage = -1;
    private CompanionPetKind? _appearancePet;
    private CompanionNeeds _needs = new(100, 100, 100, 0);
    private double _carePropElapsed;
    private PetAnimation _animation = PetAnimation.Walk;
    private PetMotionState _motionState = PetMotionState.Walking;
    private PetRestPose _restPose = PetRestPose.Sit;
    private CompanionGrowthMark _growthMark = CompanionGrowthMark.None;
    private double _restElapsed;
    private double _restDuration;
    private double _poseScaleX = 1;
    private double _poseScaleY = 1;
    private TimeSpan _restUntil;
    private TimeSpan _walkUntil;
    private TimeSpan _lastTopmostCheck;
    private NativePoint _dragStartCursor;
    private PointInt32 _dragStartWindow;

    /// <summary>
    /// What the pet says to itself when nothing has happened.
    /// </summary>
    /// <remarks>
    /// Small, self-absorbed thoughts rather than prompts: the pet is not a reminder that has
    /// learned to talk, and a line that asks the user for something every time it opens its mouth
    /// stops being company and becomes another notification. The corpus is per species and per
    /// language, and it is dealt from a shuffled deck rather than drawn at random - three hundred
    /// lines picked independently repeat inside an afternoon, and a repeat is what makes a large
    /// corpus feel like a small one.
    /// </remarks>
    private CompanionChatterBag? _chatterBag;

    /// <summary>The species and language the loaded corpus belongs to.</summary>
    private (CompanionPetKind Pet, string Locale) _chatterSource;

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
        _savePosition = savePosition;
        ApplyPetSize(settings.PetSize);
        SetPetAsset(settings.SelectedPet.AssetKey());
        _showDetails = showDetails;
        _createNote = createNote;

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

        SetPetAsset(snapshot.Profile.AppearanceKey);
        ApplyGrowthAppearance(
            CompanionPetCatalog.FromAssetKey(snapshot.Profile.AppearanceKey),
            snapshot.Growth.AppearanceStage);
        _needs = snapshot.Needs;

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

    private void Say(string key) => SayLine(Strings.Get(key));

    /// <summary>
    /// Puts one already-written line in the bubble.
    /// </summary>
    /// <remarks>
    /// The care and mood lines come from the UI resources by key, while the idle musings come from
    /// the chatter corpus as finished text. Both end here so the bubble timing and the idle reset
    /// are written once - the pet should not have two ways of opening its mouth.
    /// </remarks>
    private void SayLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            ScheduleIdleChatter();
            return;
        }

        SpeechText.Text = line;
        SpeechBubble.Visibility = Visibility.Visible;
        UpdateOrnamentVisibility();
        _bubbleTimer.Stop();
        _bubbleTimer.Start();

        // Anything the pet was told to say resets the idle clock, so a musing never lands on top
        // of news the user actually asked for.
        ScheduleIdleChatter();
    }

    /// <summary>
    /// Rests the bubble on the pet's head.
    /// </summary>
    /// <remarks>
    /// The window is a fixed 250px tall; the pet inside it is anywhere from about 40px to the full
    /// 200, once the size setting and the growth silhouette are applied. A bubble pinned to the top
    /// of the window therefore floated further above the animal the smaller it was — most of a
    /// hand's width away from a small, young pet, which reads as words belonging to nothing.
    /// Measuring from the drawn height instead keeps the two together at every size; the cap stops
    /// a full-grown pet from pushing the bubble out through the top of the window.
    /// </remarks>
    private void PositionSpeechBubble() =>
        SpeechBubble.Margin = new Thickness(
            0,
            0,
            0,
            Math.Min(
                (_frameHeight * _growthScaleY * SpeechAnchorFraction()) + SpeechBubbleGap,
                WindowHeight - SpeechBubbleReserve));

    /// <summary>How much of the frame the pet currently fills, from the floor up.</summary>
    private double SpeechAnchorFraction() =>
        _animation == PetAnimation.Sleep ? SleepSpeechFraction : 1;

    /// <summary>
    /// Waits a while, then lets the pet think out loud.
    /// </summary>
    /// <remarks>
    /// The gap is randomised because a fixed one turns into a metronome: the second time a bubble
    /// appears exactly a minute after the first, it stops being a pet having a thought and becomes
    /// a timer firing. Quiet hours and the proactive switch apply — this is the pet speaking
    /// first, which is exactly what those settings are about.
    /// </remarks>
    private void ScheduleIdleChatter()
    {
        _idleChatterTimer.Stop();
        _idleChatterTimer.Interval = TimeSpan.FromSeconds(
            _chatter.Next(IdleChatterMinimumSeconds, IdleChatterMaximumSeconds + 1));
        _idleChatterTimer.Start();
    }

    private void OnIdleChatterTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();

        if (!_settings.AllowsProactiveAt(DateTimeOffset.Now)
            || SpeechBubble.Visibility == Visibility.Visible
            || _pointerDown)
        {
            ScheduleIdleChatter();
            return;
        }

        SayLine(Chatter().Next());
    }

    /// <summary>
    /// Loads the corpus for the species and language now in force, if either has changed.
    /// </summary>
    /// <remarks>
    /// Both can change while the window is alive - the user picks another species in settings, or
    /// switches the UI language - and a pet that went on musing in the language it was born in
    /// would be the most visible part of the app to miss the switch.
    /// </remarks>
    private CompanionChatterBag Chatter()
    {
        var source = (Pet: _settings.SelectedPet, Locale: Strings.ActiveLocale);

        if (_chatterBag is null || _chatterSource != source)
        {
            _chatterSource = source;
            _chatterBag = new CompanionChatterBag(
                CompanionChatterCatalog.Lines(source.Pet, source.Locale));
        }

        return _chatterBag;
    }

    /// <summary>Acts out one paid care action beside the pet.</summary>
    public void PlayCareReaction(CareRequest request)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => PlayCareReaction(request));
            return;
        }

        FeedProp.Visibility = Collapsed(request.Action == CompanionCareAction.Feed);
        CleanProp.Visibility = Collapsed(request.Action == CompanionCareAction.BasicCare);
        SpecialProp.Visibility = Collapsed(request.Action == CompanionCareAction.SpecialCare);
        PlayProp.Visibility = Collapsed(request.Action == CompanionCareAction.Play);
        GreetProp.Visibility = Collapsed(request.Action == CompanionCareAction.Greeting);
        RestProp.Visibility = Collapsed(request.Action == CompanionCareAction.Rest);

        Say($"Companion_CareReaction{request.Action}");

        // Being put to bed is the one care action with a pose of its own; the rest are acted out
        // by the prop beside the pet, and a pet that curled up and slept through being fed would
        // be answering a different request.
        BeginRest(
            _clock.Elapsed,
            seconds: CarePropSeconds,
            pose: request.Action == CompanionCareAction.Rest
                ? PetRestPose.Sleep
                : PetRestPose.Sit);

        _carePropElapsed = 0;
        CareProp.Opacity = 1;
        if (_settings.ReduceMotion)
        {
            CarePropLift.Y = 0;
            _carePropTimer.Stop();
            _bubbleTimer.Stop();
            _bubbleTimer.Start();
            return;
        }

        _carePropTimer.Start();
    }

    private void OnCarePropTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        _carePropElapsed += sender.Interval.TotalSeconds;
        if (_carePropElapsed >= CarePropSeconds)
        {
            sender.Stop();
            CareProp.Opacity = 0;
            CarePropLift.Y = 0;
            return;
        }

        CarePropLift.Y = -6 * Math.Abs(Math.Sin(_carePropElapsed * 4));
        var fadeFrom = CarePropSeconds - 0.5;
        CareProp.Opacity = _carePropElapsed <= fadeFrom
            ? 1
            : 1 - ((_carePropElapsed - fadeFrom) / 0.5);
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
        AssertAlwaysOnTop();
        _lastTick = _clock.Elapsed;
        _lastTopmostCheck = _lastTick;
        ScheduleNextRest(_lastTick);
    }

    /// <summary>
    /// Puts the pet window into the topmost band and keeps it there.
    /// </summary>
    /// <remarks>
    /// The presenter flag alone is not enough on two counts. It is ignored while the window has not
    /// been shown yet, and re-assigning the value the presenter already holds is not guaranteed to
    /// reach the window manager, so the explicit HWND_TOPMOST call is what actually does the work.
    /// It is sent without activating: the pet must never steal focus from the document underneath
    /// it, which is the whole reason the window is a tool window in the first place.
    /// </remarks>
    private void AssertAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }

        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        _ = SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpKeepTopmost);
    }

    /// <summary>
    /// Re-promotes the pet if something demoted it out of the topmost band.
    /// </summary>
    /// <remarks>
    /// Full-screen apps and shell transitions strip WS_EX_TOPMOST from windows they cover, and the
    /// pet has no way of hearing about it. The style word is read rather than the promotion simply
    /// being repeated on a timer, so a pet that is still on top is left exactly where the user's
    /// own pinned windows put it.
    /// </remarks>
    private void EnsureStillTopmost(TimeSpan now)
    {
        if ((now - _lastTopmostCheck).TotalSeconds < TopmostRecheckSeconds)
        {
            return;
        }

        _lastTopmostCheck = now;
        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if ((GetWindowLongPtr(window, GwlExStyle).ToInt64() & WsExTopmost) == 0)
        {
            AssertAlwaysOnTop();
        }
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
        EnsureStillTopmost(now);
        AnimateGrowthMark(now);

        if (_motionState == PetMotionState.Resting)
        {
            if (now >= _restUntil)
            {
                BeginWalking();
            }
            else
            {
                AdvanceRest(elapsed);
                return;
            }
        }

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var visibleFrameWidth = _frameWidth * _growthScaleX;
        var horizontalInset = (WindowWidth - visibleFrameWidth) / 2;
        var left = area.X - horizontalInset;
        var right = area.X + area.Width - WindowWidth + horizontalInset;
        _x += _direction * WalkSpeed * MoodSpeedFactor() * GrowthSpeedFactor() * elapsed;

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
        else if (now >= _walkUntil)
        {
            // Resting only at the two edges turned the pet into a pendulum: the same two stops,
            // in the same two places, for as long as the machine was on. A leg of random length
            // means it also sits down in the middle of the screen, which is where the user
            // actually is.
            BeginRest(now);
        }

        AdvanceFrame(elapsed, WalkFrameSeconds());
        ApplyFacingTransform();
        AppWindow.Move(new PointInt32((int)Math.Round(_x), (int)Math.Round(_y)));
    }

    private void ApplyMotionPreference()
    {
        if (_settings.ReduceMotion)
        {
            _animationTimer.Stop();
            _motionState = PetMotionState.Walking;
            SetAnimation(PetAnimation.Walk);
            SetFrame(0);
            SetPoseScale(1, 1);
            GrowthMarkLift.Y = 0;
            ApplyFacingTransform(forceRight: true);
            UpdateOrnamentVisibility();
            return;
        }

        _lastTick = _clock.Elapsed;
        ScheduleNextRest(_lastTick);
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
        PositionSpeechBubble();
        PositionOrnaments();
    }

    private void BeginRest(TimeSpan now, double? seconds = null, PetRestPose? pose = null)
    {
        _motionState = PetMotionState.Resting;
        _restPose = pose ?? ChooseRestPose();
        _restElapsed = 0;
        _restDuration = seconds ?? (RestDurationSeconds() * RestPoseFactor(_restPose));
        _restUntil = now + TimeSpan.FromSeconds(_restDuration);
        SetAnimation(_restPose == PetRestPose.Sleep ? PetAnimation.Sleep : PetAnimation.Rest);

        // A sleeping animal is still, so the strip is parked on one frame rather than cycling;
        // the sheet holds four copies of the same pose anyway. What says "asleep" beyond the
        // drawing itself is the breathing and the drifting Zs.
        if (_restPose == PetRestPose.Sleep)
        {
            SetFrame(0);
        }

        ShowRestEffect();
        PositionSpeechBubble();
        PositionOrnaments();

        // Set rather than eased: the pose the pet is leaving may have left the sprite mid-squash,
        // and the facing is re-applied here because an edge turn flips the direction one line
        // before this call - a pet that sat down still facing the wall it just reached read as a
        // missed frame.
        _poseScaleX = 1;
        _poseScaleY = 1;
        ApplyFacingTransform();
    }

    private void BeginWalking()
    {
        _motionState = PetMotionState.Walking;

        // The pose is deliberately left as it was. It is the only memory of what the pet did
        // last, and ChooseRestPose uses it to avoid dealing the same pose twice running; clearing
        // it here would have compared every roll against a pose the pet had not held for minutes.
        SetAnimation(PetAnimation.Walk);
        _poseScaleX = 1;
        _poseScaleY = 1;
        ApplyFacingTransform();
        ShowRestEffect();
        PositionSpeechBubble();
        PositionOrnaments();
        _lastTick = _clock.Elapsed;
        ScheduleNextRest(_lastTick);
    }

    /// <summary>Decides how far the pet gets before it next sits down.</summary>
    private void ScheduleNextRest(TimeSpan now) =>
        _walkUntil = now + TimeSpan.FromSeconds(
            WalkLegMinimumSeconds
            + (_behaviour.NextDouble() * (WalkLegMaximumSeconds - WalkLegMinimumSeconds)));

    /// <summary>
    /// Picks what the pet does while it is sitting still.
    /// </summary>
    /// <remarks>
    /// Four poses rather than one, because a single rest loop seen a hundred times a day is the
    /// part of the pet that most obviously repeats. A bored pet is nudged toward staring into
    /// space and away from stretching; the nudge moves the odds rather than replacing them, so no
    /// pose ever vanishes. The same pose twice running is re-rolled once - true randomness reads
    /// as a stuck animation when it repeats, and one re-roll costs nothing.
    /// </remarks>
    private PetRestPose ChooseRestPose()
    {
        var pose = RollRestPose();
        return pose == _restPose ? RollRestPose() : pose;
    }

    private PetRestPose RollRestPose()
    {
        var listless = _needs.Mood <= CompanionCareRules.PlayThreshold;
        var roll = _behaviour.Next(100);

        if (roll < (listless ? 24 : 34))
        {
            return PetRestPose.Sit;
        }

        if (roll < (listless ? 60 : 58))
        {
            return PetRestPose.Daydream;
        }

        return roll < (listless ? 88 : 82) ? PetRestPose.Sleep : PetRestPose.Stretch;
    }

    /// <summary>How long each pose holds, against the species' own base rest.</summary>
    private static double RestPoseFactor(PetRestPose pose) => pose switch
    {
        PetRestPose.Sleep => 2.2,
        PetRestPose.Daydream => 1.5,
        PetRestPose.Stretch => 0.7,
        _ => 1,
    };

    /// <summary>
    /// Moves whichever rest pose is running along by one frame's worth of time.
    /// </summary>
    /// <remarks>
    /// The poses are separated by how they move, not by new artwork. Seven species times four
    /// poses is twenty-eight strips nobody is going to draw; a held frame under a breathing
    /// squash, a near-frozen frame under a slow sway, and a squash-and-stretch pulse are read as
    /// three different things by anyone watching, and they cost three lines each.
    /// </remarks>
    private void AdvanceRest(double elapsed)
    {
        _restElapsed += elapsed;

        switch (_restPose)
        {
            case PetRestPose.Sleep:
                // No frame advance: the sheet is one held pose, and the only motion a sleeping
                // animal has is its breathing.
                var breath = Math.Sin(_restElapsed * 1.7);
                SetPoseScale(1 - (0.018 * breath), 1 + (0.032 * breath));
                AnimateSleep();
                break;

            case PetRestPose.Daydream:
                AdvanceFrame(elapsed, 1.1);
                var sway = Math.Sin(_restElapsed * 0.85);
                SetPoseScale(1 + (0.014 * sway), 1 - (0.014 * sway));
                AnimateDaydream();
                break;

            case PetRestPose.Stretch:
                AdvanceFrame(elapsed, 0.18);
                // One full cycle over the pose: up onto the toes, down into a slump, back to rest.
                var pulse = Math.Sin(
                    (_restDuration <= 0 ? 1 : _restElapsed / _restDuration) * Math.PI * 2);
                SetPoseScale(1 - (0.10 * pulse), 1 + (0.17 * pulse));
                break;

            default:
                AdvanceFrame(elapsed, 0.32);
                break;
        }
    }

    private void SetPoseScale(double x, double y)
    {
        if (Math.Abs(_poseScaleX - x) < 0.0005 && Math.Abs(_poseScaleY - y) < 0.0005)
        {
            return;
        }

        _poseScaleX = x;
        _poseScaleY = y;
        ApplyFacingTransform();
    }

    /// <summary>Shows the glyphs that belong to the pose now running, and hides the rest.</summary>
    private void ShowRestEffect()
    {
        SleepMark.Visibility = Collapsed(_restPose == PetRestPose.Sleep);
        DaydreamMark.Visibility = Collapsed(_restPose == PetRestPose.Daydream);
        UpdateOrnamentVisibility();
    }

    /// <summary>
    /// Sends three Zs up and away on staggered phases.
    /// </summary>
    /// <remarks>
    /// One shared phase would make the three move as a single object, which reads as a decal
    /// sliding rather than as breath leaving an animal.
    /// </remarks>
    private void AnimateSleep()
    {
        Drift(SleepZSmallDrift, SleepZSmall, _restElapsed, 0);
        Drift(SleepZMediumDrift, SleepZMedium, _restElapsed, 0.9);
        Drift(SleepZLargeDrift, SleepZLarge, _restElapsed, 1.8);

        static void Drift(TranslateTransform transform, UIElement glyph, double elapsed, double phase)
        {
            var cycle = ((elapsed - phase) % 2.7 + 2.7) % 2.7;
            transform.Y = -10 * cycle;
            transform.X = 4 * cycle;
            glyph.Opacity = elapsed < phase ? 0 : Math.Clamp(1 - (cycle / 2.7), 0, 1);
        }
    }

    /// <summary>Fades the three thought dots in one after another, then starts over.</summary>
    private void AnimateDaydream()
    {
        DaydreamDotSmall.Opacity = DotOpacity(_restElapsed, 0);
        DaydreamDotMedium.Opacity = DotOpacity(_restElapsed, 0.75);
        DaydreamDotLarge.Opacity = DotOpacity(_restElapsed, 1.5);

        static double DotOpacity(double elapsed, double phase)
        {
            var cycle = ((elapsed - phase) % 3.6 + 3.6) % 3.6;
            return elapsed < phase ? 0 : Math.Clamp(Math.Sin(cycle / 3.6 * Math.PI), 0, 1);
        }
    }

    private void ApplyGrowthAppearance(CompanionPetKind pet, int appearanceStage)
    {
        var normalizedStage = Math.Clamp(
            appearanceStage,
            0,
            CompanionGrowthAppearanceCatalog.FinalStage);
        if (_appearancePet == pet && _appearanceStage == normalizedStage)
        {
            return;
        }

        _appearancePet = pet;
        _appearanceStage = normalizedStage;
        var appearance = CompanionGrowthAppearanceCatalog.For(pet, normalizedStage);
        _growthScaleX = appearance.WidthScale;
        _growthScaleY = appearance.HeightScale;
        _growthMark = appearance.Mark;
        ApplyGrowthMark();
        ApplyFacingTransform(_settings.ReduceMotion);
        PositionSpeechBubble();
        PositionOrnaments();
    }

    /// <summary>Hangs the ornament this stage has earned over the pet.</summary>
    private void ApplyGrowthMark()
    {
        SproutMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Sprout);
        SparklesMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Sparkles);
        HaloMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Halo);
        CrownMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Crown);
        UpdateOrnamentVisibility();
    }

    /// <summary>
    /// Puts both overlays where the pet's head actually is.
    /// </summary>
    /// <remarks>
    /// The sprite sits between roughly a fifth and four fifths of its frame, so the head top is a
    /// fraction of the drawn height rather than the window's. Both layers shrink with the pet but
    /// stop at half scale: a newborn at the small size setting is about thirty pixels tall, and an
    /// ornament that shrank with it in full would be a smudge.
    /// </remarks>
    private void PositionOrnaments()
    {
        var drawnHeight = _frameHeight * _growthScaleY;
        var scale = Math.Clamp(drawnHeight / BaseFrameHeight, OrnamentMinimumScale, 1);

        GrowthMarkScale.ScaleX = scale;
        GrowthMarkScale.ScaleY = scale;
        GrowthMark.Margin = new Thickness(
            0,
            0,
            0,
            drawnHeight * (_animation == PetAnimation.Sleep
                ? SleepHeadTopFraction
                : HeadTopFraction));

        RestEffectScale.ScaleX = scale;
        RestEffectScale.ScaleY = scale;

        // A centred child is centred in what is left after its own margins, so twice the wanted
        // offset on the left shifts it by the offset itself. The Zs rise beside the head rather
        // than through the ornament already sitting on top of it.
        RestEffect.Margin = new Thickness(
            2 * RestEffectSideOffset * scale,
            0,
            0,
            drawnHeight * (_animation == PetAnimation.Sleep
                ? SleepRestEffectFraction
                : RestEffectFraction));
    }

    /// <summary>
    /// Keeps the overlays out of the way of the pet's own words.
    /// </summary>
    /// <remarks>
    /// A small pet's bubble lands in the same band as its ornament, and an opaque bubble with a
    /// crown poking through it looks like a drawing error rather than a pet. The bubble lasts four
    /// seconds; the ornament comes back after it.
    /// </remarks>
    private void UpdateOrnamentVisibility()
    {
        var talking = SpeechBubble.Visibility == Visibility.Visible;
        GrowthMark.Visibility =
            Collapsed(_growthMark != CompanionGrowthMark.None && !talking);
        RestEffect.Visibility = Collapsed(
            !talking
            && !_settings.ReduceMotion
            && _motionState == PetMotionState.Resting
            && _restPose is PetRestPose.Sleep or PetRestPose.Daydream);
    }

    /// <summary>Gives the ornament a slow bob, and the stage-three pair its twinkle.</summary>
    private void AnimateGrowthMark(TimeSpan now)
    {
        if (_growthMark == CompanionGrowthMark.None)
        {
            return;
        }

        var seconds = now.TotalSeconds;
        GrowthMarkLift.Y = -2.5 * Math.Sin(seconds * 1.9);

        if (_growthMark != CompanionGrowthMark.Sparkles)
        {
            return;
        }

        SparkleLeft.Opacity = 0.55 + (0.45 * Math.Sin(seconds * 2.6));
        SparkleRight.Opacity = 0.55 + (0.45 * Math.Sin((seconds * 2.6) + Math.PI));
    }

    private void ApplyFacingTransform(bool forceRight = false)
    {
        FacingTransform.ScaleX = (forceRight ? 1 : _direction) * _growthScaleX * _poseScaleX;
        FacingTransform.ScaleY = _growthScaleY * _poseScaleY;
    }

    /// <summary>A hungry, bored pet plods; a happy one trots. Never fully stops, which would
    /// read as the app having frozen.</summary>
    private double MoodSpeedFactor() => 0.6 + (0.4 * (_needs.Mood / 100d));

    /// <summary>
    /// A newborn toddles and a full-grown pet strides.
    /// </summary>
    /// <remarks>
    /// Size alone is only legible next to another pet. Gait is legible on its own: short quick
    /// steps that cover little ground are what "young" looks like from across the desk, and it
    /// costs two numbers.
    /// </remarks>
    private double GrowthSpeedFactor() =>
        0.72 + (0.07 * Math.Max(0, _appearanceStage));

    private double WalkFrameSeconds() =>
        0.13 - (0.0075 * (CompanionGrowthAppearanceCatalog.FinalStage
                          - Math.Clamp(_appearanceStage, 0, CompanionGrowthAppearanceCatalog.FinalStage)));

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
        SetFrame((_frame + 1) % FrameCount);
    }

    private void SetFrame(int frame)
    {
        _frame = frame;
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
        var clicked = _pointerDown && !_dragMoved;
        SpriteViewport.ReleasePointerCapture(e.Pointer);
        FinishDrag();
        if (clicked)
        {
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
        var suffix = animation switch
        {
            PetAnimation.Sleep => "sleep",
            PetAnimation.Rest => "rest",
            _ => "walk",
        };
        SpriteStrip.Source = new BitmapImage(
            new Uri($"ms-appx:///Assets/Companion/{_assetKey}-{suffix}.png"));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _animationTimer.Stop();
        _bubbleTimer.Stop();
        _clickTimer.Stop();
        _carePropTimer.Stop();
        _idleChatterTimer.Stop();
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

        EnsureSystemDispatcherQueue();
        _backdropCompositor = new Windows.UI.Composition.Compositor();
        _transparentBackdrop = _backdropCompositor.CreateColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));
        this.As<ICompositionSupportsSystemBackdrop>().SystemBackdrop = _transparentBackdrop;
    }

    private static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = 2, // DQTYPE_THREAD_CURRENT
            ApartmentType = 2, // DQTAT_COM_STA
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(
            options,
            out _systemDispatcherQueueController));
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    private enum PetMotionState
    {
        Walking,
        Resting,
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
    }

    /// <summary>What the pet is doing while it is not walking.</summary>
    private enum PetRestPose
    {
        /// <summary>The original: the rest strip looping at its own pace.</summary>
        Sit,

        /// <summary>One held frame, breathing, with Zs drifting off the head.</summary>
        Sleep,

        /// <summary>Barely moving, swaying, with a trail of thought dots.</summary>
        Daydream,

        /// <summary>A squash-and-stretch pulse, over quickly.</summary>
        Stretch,
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        [MarshalAs(UnmanagedType.IUnknown)] out object? dispatcherQueueController);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

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
