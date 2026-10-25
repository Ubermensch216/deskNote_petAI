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
    private const double SleepHeadTopFraction = 0.62;
    private const double SleepRestEffectFraction = 0.54;
    private const double SleepSpeechFraction = 0.80;
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
    private const double TopmostRecheckSeconds = 1;
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
    [ThreadStatic]
    private static object? _systemDispatcherQueueController;

    private Windows.UI.Composition.Compositor? _backdropCompositor;
    private Windows.UI.Composition.CompositionColorBrush? _transparentBackdrop;
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
    private double _carePropElapsed;
    private PetAnimation _animation = PetAnimation.Walk;
    private CompanionGrowthMark _growthMark = CompanionGrowthMark.None;
    private double _poseScale = 1;
    private double _poseAngle;
    private NativePoint _dragStartCursor;

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

        // The bubble rests on the head of an upright pet; on a wall or the ceiling the head is
        // elsewhere, so the line is dropped there rather than shown floating.
        if (_motion.IsClimbing)
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
                (_frameHeight * _growthScale * SpeechAnchorFraction()) + SpeechBubbleGap,
                WindowHeight - SpeechBubbleReserve));

    /// <summary>How much of the frame the pet currently fills, from the floor up.</summary>
    private double SpeechAnchorFraction() =>
        IsRig ? RigHeadTopFraction(_animation == PetAnimation.Sleep) + 0.02
        : _animation is PetAnimation.Sleep or PetAnimation.Stretch ? SleepSpeechFraction : 1;

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

        // A pet clinging to a wall cannot be fed: it comes down first, and the reaction is acted
        // out when it has landed.
        if (_motion.IsClimbing)
        {
            LetGo();
        }

        // Being put to bed is the one care action with a pose of its own; the rest are acted out
        // by the prop beside the pet, and a pet that curled up and slept through being fed would
        // be answering a different request.
        BeginRest(
            _clock.Elapsed,
            seconds: CarePropSeconds,
            pose: request.Action == CompanionCareAction.Rest
                ? PetRestPose.Sleep
                : PetRestPose.Sit);

        ActOutCare(request.Action);
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
        _growthScale = appearance.Scale;
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
        TiaraMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Tiara);
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
        var drawnHeight = _frameHeight * _growthScale;
        var scale = Math.Clamp(drawnHeight / BaseFrameHeight, OrnamentMinimumScale, 1);

        GrowthMarkScale.ScaleX = scale;
        GrowthMarkScale.ScaleY = scale;
        GrowthMark.Margin = new Thickness(
            0,
            0,
            0,
            drawnHeight * HeadTopFractionNow());

        RestEffectScale.ScaleX = scale;
        RestEffectScale.ScaleY = scale;

        // The rotor stands on the head, above whatever ornament the pet has earned.
        RotorScale.ScaleX = scale;
        RotorScale.ScaleY = scale;
        RotorProp.Margin = new Thickness(0, 0, 0, (drawnHeight * HeadTopFractionNow()) - 2);

        // A centred child is centred in what is left after its own margins, so twice the wanted
        // offset on the left shifts it by the offset itself. The Zs rise beside the head rather
        // than through the ornament already sitting on top of it.
        RestEffect.Margin = new Thickness(
            2 * RestEffectSideOffset * scale,
            0,
            0,
            drawnHeight * RestEffectFractionNow());
    }

    /// <summary>Where the top of the head is, for the crown to sit on, whichever kind of pet this is.</summary>
    private double HeadTopFractionNow() =>
        IsRig ? RigHeadTopFraction(_animation == PetAnimation.Sleep)
        : _animation is PetAnimation.Sleep or PetAnimation.Stretch ? SleepHeadTopFraction : HeadTopFraction;

    /// <summary>Where the Zs and thought dots start: a little under the top of the head, beside it.</summary>
    private double RestEffectFractionNow() =>
        IsRig ? RigHeadTopFraction(_animation == PetAnimation.Sleep) - 0.07
        : _animation is PetAnimation.Sleep or PetAnimation.Stretch ? SleepRestEffectFraction : RestEffectFraction;

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

        // The growth mark sits on the head, so it goes where the head is not free: under a rotor,
        // or on a pet whose head is pointing along a wall.
        var headFree = !_motion.IsClimbing && _motion.State != PetMotionState.Falling;
        GrowthMark.Visibility =
            Collapsed(_growthMark != CompanionGrowthMark.None && !talking && headFree);
        RestEffect.Visibility = Collapsed(
            !talking
            && !_settings.ReduceMotion
            && _motion.State == PetMotionState.Resting
            && _motion.RestPose is PetRestPose.Sleep or PetRestPose.Daydream);
    }

    /// <summary>Gives the ornament a slow bob, the stage-three pair its twinkle, and the tiara its gem shimmer.</summary>
    private void AnimateGrowthMark(TimeSpan now)
    {
        if (_growthMark == CompanionGrowthMark.None)
        {
            return;
        }

        var seconds = now.TotalSeconds;
        GrowthMarkLift.Y = -2.5 * Math.Sin(seconds * 1.9);

        if (_growthMark == CompanionGrowthMark.Sparkles)
        {
            SparkleLeft.Opacity = 0.55 + (0.45 * Math.Sin(seconds * 2.6));
            SparkleRight.Opacity = 0.55 + (0.45 * Math.Sin((seconds * 2.6) + Math.PI));
        }
        else if (_growthMark == CompanionGrowthMark.Tiara)
        {
            TiaraGemShimmer.Opacity = 0.45 + (0.55 * Math.Sin(seconds * 2.4));
        }
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
