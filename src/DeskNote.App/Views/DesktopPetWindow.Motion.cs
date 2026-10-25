using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// The window's half of the pet's movement: turning what <see cref="PetMotionEngine"/> decided into
/// a window position, a sprite and a pose.
/// </summary>
/// <remarks>
/// <para>
/// Where the pet goes and what it does lives in the engine, which has no window and no timer and
/// can be run against a made-up desktop. What stays here is everything that needs a window - the
/// displays the pet is moving about on, the sprite strip, the transforms, the rotor, the drifting
/// Zs.
/// </para>
/// <para>
/// The engine thinks in terms of a box: the rectangle the pet's drawing fills in its current pose.
/// The window is much larger than that and padded unevenly, so every tick the box is turned into a
/// window position by <see cref="PetPoseGeometry"/> - which is how a pet lying along the right-hand
/// edge of the screen gets its side exactly on the edge, with the rest of its window hanging
/// transparently off the screen.
/// </para>
/// </remarks>
public sealed partial class DesktopPetWindow
{
    private const int NormalTickMilliseconds = 33;
    private const int FastTickMilliseconds = 15;
    private const double DisplayRefreshSeconds = 1;
    private const double ThrownTiltDegrees = 32;
    private const double LandingSquash = 0.07;

    private readonly List<DisplayRegion> _displays = [];
    private TimeSpan _displaysRefreshedAt = TimeSpan.MinValue;
    private PointInt32 _lastWindowPosition = new(int.MinValue, int.MinValue);
    private bool _fastCadence;
    private double _grabOffsetX;
    private double _grabOffsetY;

    private readonly record struct DisplayRegion(RectInt32 Work, RectInt32 Outer);

    private void OnAnimationTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        if (!_placed || _settings.ReduceMotion || IsPausedForSession)
        {
            return;
        }

        if (_pointerDown)
        {
            // A drawn character in the hand keeps moving - it dangles, kicks and swings with the
            // pointer - where a sprite is simply carried.
            if (IsRig && _dragMoved)
            {
                var held = _clock.Elapsed;
                var heldFor = Math.Min(0.05, (held - _lastTick).TotalSeconds);
                _lastTick = held;
                AnimateGrowthMark(held);
                AdvanceRig(heldFor, held: true);
            }

            return;
        }

        var now = _clock.Elapsed;
        var elapsed = Math.Min(0.05, (now - _lastTick).TotalSeconds);
        _lastTick = now;
        AnimateGrowthMark(now);

        var layout = CurrentLayout();
        var ink = CurrentInk();
        var world = BuildWorld(layout, ink, AreaAt(_motion.CenterX, _motion.CenterY));
        var events = _motion.Advance(now, elapsed, world);

        if (events.HasFlag(PetMotionEvents.StateChanged))
        {
            ApplyStateVisuals();
        }

        if (IsRig)
        {
            // The pet sat down of its own accord: this is the moment it may show off instead.
            if (events.HasFlag(PetMotionEvents.BeganRest))
            {
                MaybeStartSignature();
            }

            AdvanceRig(elapsed);
            PlaceWindow(layout, ink, world.Bodies);
            return;
        }

        if (events.HasFlag(PetMotionEvents.Resting))
        {
            AdvanceRest(elapsed);
        }
        else
        {
            AnimateState(elapsed);
        }

        ApplyFacingTransform();
        PlaceWindow(layout, ink, world.Bodies);
    }

    /// <summary>The per-tick motion of everything that is not resting: legs, tilt, squash, rotor.</summary>
    private void AnimateState(double elapsed)
    {
        switch (_motion.State)
        {
            case PetMotionState.Walking:
            case PetMotionState.Wall:
            case PetMotionState.Ceiling:
                AdvanceFrame(elapsed, WalkFrameSeconds());
                break;

            case PetMotionState.Falling:
                // Legs dangling, body swaying under the rotor.
                AdvanceFrame(elapsed, 0.22);
                _poseAngle = 3 * Math.Sin(_motion.StateElapsed * 2.4);
                RotorBlades.ScaleX = Math.Max(0.12, Math.Abs(Math.Cos(_motion.StateElapsed * 38)));
                break;

            case PetMotionState.Thrown:
                AdvanceFrame(elapsed, 0.07);
                _poseAngle = Math.Clamp(
                    _motion.VelocityX / 900 * 30,
                    -ThrownTiltDegrees,
                    ThrownTiltDegrees) + (Math.Sin(_motion.StateElapsed * 13) * 6);
                break;

            case PetMotionState.Landing:
                // A pulse rather than a squash: the proportions are never touched.
                _poseScale = 1 - (LandingSquash * Math.Sin(Math.PI * _motion.LandingProgress));
                break;
        }
    }

    /// <summary>
    /// Brings everything that depends on what the pet is doing into line with what it is doing.
    /// </summary>
    /// <remarks>
    /// Run when the engine reports a change of state, not every tick: it swaps sprite sheets and
    /// re-lays the overlays, which is more work than the tick should repeat.
    /// </remarks>
    private void ApplyStateVisuals()
    {
        var state = _motion.State;

        if (state == PetMotionState.Resting)
        {
            ApplyRestVisuals();
        }
        else
        {
            ApplyWalkingVisuals();
        }

        if (state is PetMotionState.Held or PetMotionState.Landing)
        {
            SetFrame(0);
        }

        // A drawn character floats down on its own ki, not on a rotor.
        RotorProp.Visibility = Collapsed(state == PetMotionState.Falling && !_settings.ReduceMotion && !IsRig);

        // The bubble rests on the head of an upright pet. On a wall or the ceiling the head is
        // somewhere else, and a bubble left where it was would be words floating nowhere.
        if (_motion.IsClimbing)
        {
            SpeechBubble.Visibility = Visibility.Collapsed;
            _bubbleTimer.Stop();
        }

        UpdateOrnamentVisibility();
        SetFastCadence(state == PetMotionState.Thrown);
    }

    /// <summary>A throw is where a coarse clock shows; hold the finer one only while one is under way.</summary>
    private void SetFastCadence(bool fast)
    {
        if (fast == _fastCadence)
        {
            return;
        }

        _fastCadence = fast;
        _animationTimer.Interval = TimeSpan.FromMilliseconds(fast ? FastTickMilliseconds : NormalTickMilliseconds);
        if (fast)
        {
            HighResolutionTimer.Acquire();
        }
        else
        {
            HighResolutionTimer.Release();
        }
    }

    private void ApplyMotionPreference()
    {
        if (_settings.ReduceMotion)
        {
            _animationTimer.Stop();

            // Stand still where it is, upright, whatever it was doing: the setting means "stay
            // where you were put", and a pet frozen halfway up a wall is not that.
            _motion.BeginWalking(_clock.Elapsed);
            SetAnimation(PetAnimation.Walk);
            SetFrame(0);
            SetPose(1, angle: 0);
            PaintStandingRig();
            GrowthMarkLift.Y = 0;
            RotorProp.Visibility = Visibility.Collapsed;
            SetFastCadence(false);
            ApplyFacingTransform(forceRight: true);
            UpdateOrnamentVisibility();
            if (_placed)
            {
                PlaceWindow();
            }

            return;
        }

        _lastTick = _clock.Elapsed;
        _motion.ScheduleNextWalkLeg(_lastTick);
        if (_placed)
        {
            // A pet that was standing in mid-air while it was told to stand still comes down now.
            var layout = CurrentLayout();
            var ink = CurrentInk();
            _motion.Place(
                _lastTick,
                _motion.CenterX,
                _motion.CenterY,
                BuildWorld(layout, ink, AreaAt(_motion.CenterX, _motion.CenterY)));
            ApplyStateVisuals();
        }

        _animationTimer.Start();
    }

    /// <summary>A low mood tilts the choice of rest pose; see <see cref="PetMotionEngine"/>.</summary>
    private bool IsListless() => _needs.Mood <= CompanionCareRules.PlayThreshold;

    /// <summary>
    /// Sits the pet down for a rest of its own choosing, or for the pose and length it is told.
    /// </summary>
    /// <remarks>
    /// A pet that is in the air or on a wall cannot sit; the engine holds the rest back until it is
    /// down, so a care reaction that arrives mid-fall is acted out on the floor.
    /// </remarks>
    private void BeginRest(TimeSpan now, double? seconds = null, PetRestPose? pose = null)
    {
        if (_motion.BeginRest(now, RestDurationSeconds(), IsListless(), seconds, pose))
        {
            ApplyRestVisuals();
        }
    }

    /// <summary>Makes a pet that is clinging to a wall or the ceiling let go and come down.</summary>
    private void LetGo()
    {
        _motion.Drop(CurrentWorld());
        ApplyStateVisuals();
    }

    /// <summary>Draws the pet as it should look from the moment it sits down.</summary>
    private void ApplyRestVisuals()
    {
        SetAnimation(_motion.RestPose == PetRestPose.Sleep ? PetAnimation.Sleep : PetAnimation.Rest);

        // A sleeping animal is still, so the strip is parked on one frame rather than cycling;
        // the sheet holds four copies of the same pose anyway. What says "asleep" beyond the
        // drawing itself is the breathing and the drifting Zs.
        if (_motion.RestPose == PetRestPose.Sleep)
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
        _poseScale = 1;
        _poseAngle = 0;
        ApplyFacingTransform();
    }

    /// <summary>Draws the pet as it should look from the moment it gets up, or is up to anything but resting.</summary>
    private void ApplyWalkingVisuals()
    {
        // The pose is deliberately left as it was. It is the only memory of what the pet did
        // last, and the engine uses it to avoid dealing the same pose twice running; clearing it
        // here would have compared every roll against a pose the pet had not held for minutes.
        SetAnimation(PetAnimation.Walk);
        _poseScale = 1;
        _poseAngle = 0;
        ApplyFacingTransform();
        ShowRestEffect();
        PositionSpeechBubble();
        PositionOrnaments();
        _lastTick = _clock.Elapsed;
    }

    // ------------------------------------------------------------------ where the pet is

    private double RasterScale => Content?.XamlRoot?.RasterizationScale ?? 1.0;

    private PetLayout CurrentLayout()
    {
        var size = AppWindow.Size;
        var scale = RasterScale;
        return new PetLayout(size.Width, size.Height, _frameWidth * scale, _frameHeight * scale);
    }

    private PetInk CurrentInk() => PetArtBounds.For(CompanionPetCatalog.FromAssetKey(_assetKey));

    /// <summary>
    /// How a pose is drawn.
    /// </summary>
    /// <param name="state">The pose.</param>
    /// <param name="drawing">
    /// True for what goes on the screen, which includes the sway and squash of the moment; false for
    /// the bare pose the window is positioned by, so that a swaying pet does not shift its window.
    /// </param>
    private PetTransform TransformFor(PetMotionState state, bool drawing)
    {
        var growth = _growthScale;

        switch (state)
        {
            case PetMotionState.Wall:
                // Lying on its side with its feet to the wall, head up on the way up and down on
                // the way down: the walking pose turned a quarter turn, mirrored for the left wall.
                return new PetTransform(
                    -90,
                    _motion.WallSide * growth,
                    _motion.Descending ? -growth : growth,
                    PetPivot.Centre);

            case PetMotionState.Ceiling:
                return new PetTransform(0, _motion.Direction * growth, -growth, PetPivot.Centre);

            default:
                var scale = drawing ? growth * _poseScale : growth;
                return new PetTransform(
                    drawing ? _poseAngle : 0,
                    _motion.Direction * scale,
                    scale,
                    PetPivot.Feet);
        }
    }

    /// <summary>How much room the pet takes lying on a wall, hanging from the ceiling and standing.</summary>
    private PetBodies CurrentBodies(PetLayout layout, PetInk ink)
    {
        var growth = _growthScale;
        var floor = PetPoseGeometry.InkBox(layout, ink, new PetTransform(0, growth, growth, PetPivot.Feet));
        var wall = PetPoseGeometry.InkBox(layout, ink, new PetTransform(-90, growth, growth, PetPivot.Centre));
        var ceiling = PetPoseGeometry.InkBox(layout, ink, new PetTransform(0, growth, -growth, PetPivot.Centre));

        return new PetBodies(floor.Width, floor.Height, wall.Width, wall.Height, ceiling.Width, ceiling.Height);
    }

    private PetMotionContext BuildWorld(PetLayout layout, PetInk ink, PetArea area) => new(
        area,
        CurrentBodies(layout, ink),
        WalkSpeed * MoodSpeedFactor() * GrowthSpeedFactor() * RigSpeedFactor(),
        RestDurationSeconds(),
        IsListless(),
        AllowWalls: _settings.RoamScreen,
        AllowCeiling: _settings.RoamScreen);

    private PetMotionContext CurrentWorld() =>
        BuildWorld(CurrentLayout(), CurrentInk(), AreaAt(_motion.CenterX, _motion.CenterY));

    private void PlaceWindow()
    {
        var layout = CurrentLayout();
        var ink = CurrentInk();
        PlaceWindow(layout, ink, CurrentBodies(layout, ink));
    }

    /// <summary>
    /// Moves the window so that the pet's drawing is where the engine says the pet is.
    /// </summary>
    private void PlaceWindow(PetLayout layout, PetInk ink, in PetBodies bodies)
    {
        var inWindow = PetPoseGeometry.InkBox(layout, ink, TransformFor(_motion.State, drawing: false));
        var onScreen = _motion.Box(bodies);

        var x = (int)Math.Round(onScreen.Left - inWindow.Left);
        var y = (int)Math.Round(onScreen.Top - inWindow.Top);
        if (x == _lastWindowPosition.X && y == _lastWindowPosition.Y)
        {
            return;
        }

        _lastWindowPosition = new PointInt32(x, y);
        AppWindow.Move(_lastWindowPosition);
    }

    // ------------------------------------------------------------------ the displays

    /// <summary>
    /// The part of the desktop the pet is on, and which of its edges are the edge of everything.
    /// </summary>
    /// <remarks>
    /// An edge with another display beside it is a doorway, not a wall; only an edge nothing
    /// touches can be climbed. The list of displays is read once a second rather than every tick:
    /// enumerating them is a round trip to the system and they change a few times a year.
    /// </remarks>
    private PetArea AreaAt(double x, double y)
    {
        RefreshDisplays();

        if (_displays.Count == 0)
        {
            var fallback = DisplayArea.Primary.WorkArea;
            return new PetArea(fallback.X, fallback.Y, fallback.X + fallback.Width, fallback.Y + fallback.Height);
        }

        var best = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < _displays.Count; index++)
        {
            var outer = _displays[index].Outer;
            var dx = Math.Max(Math.Max(outer.X - x, 0), x - (outer.X + outer.Width));
            var dy = Math.Max(Math.Max(outer.Y - y, 0), y - (outer.Y + outer.Height));
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        var here = _displays[best].Outer;
        var leftOuter = true;
        var rightOuter = true;
        var topOuter = true;

        for (var index = 0; index < _displays.Count; index++)
        {
            if (index == best)
            {
                continue;
            }

            var other = _displays[index].Outer;
            var overlapsVertically = other.Y < here.Y + here.Height && other.Y + other.Height > here.Y;
            var overlapsHorizontally = other.X < here.X + here.Width && other.X + other.Width > here.X;

            leftOuter &= !(overlapsVertically && Math.Abs(other.X + other.Width - here.X) <= 1);
            rightOuter &= !(overlapsVertically && Math.Abs(other.X - (here.X + here.Width)) <= 1);
            topOuter &= !(overlapsHorizontally && Math.Abs(other.Y + other.Height - here.Y) <= 1);
        }

        var work = _displays[best].Work;
        return new PetArea(
            work.X,
            work.Y,
            work.X + work.Width,
            work.Y + work.Height,
            leftOuter,
            rightOuter,
            topOuter);
    }

    private void RefreshDisplays(bool force = false)
    {
        var now = _clock.Elapsed;
        if (!force && _displays.Count > 0 && (now - _displaysRefreshedAt).TotalSeconds < DisplayRefreshSeconds)
        {
            return;
        }

        _displaysRefreshedAt = now;
        _displays.Clear();
        // Indexed rather than enumerated: this WinRT list cannot be enumerated from managed code
        // here (the projection throws an InvalidCastException building the enumerator), but it
        // answers Count and the indexer perfectly well.
        var all = DisplayArea.FindAll();
        for (var index = 0; index < all.Count; index++)
        {
            _displays.Add(new DisplayRegion(all[index].WorkArea, all[index].OuterBounds));
        }
    }

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
        switch (_motion.RestPose)
        {
            case PetRestPose.Sleep:
                // No frame advance: the sheet is one held pose, and the only motion a sleeping
                // animal has is its breathing.
                SetPose(1 + (0.015 * Math.Sin(_motion.RestElapsed * 1.7)), angle: 0);
                AnimateSleep();
                break;

            case PetRestPose.Daydream:
                AdvanceFrame(elapsed, 1.1);
                SetPose(1, angle: 1.6 * Math.Sin(_motion.RestElapsed * 0.85));
                AnimateDaydream();
                break;

            case PetRestPose.Stretch:
                AdvanceFrame(elapsed, 0.18);
                // One cycle over the pose: up and out of the slouch, then back down into it.
                var pulse = Math.Sin(
                    (_motion.RestDuration <= 0 ? 1 : _motion.RestElapsed / _motion.RestDuration) * Math.PI * 2);
                SetPose(1 + (0.055 * pulse), angle: -2.4 * pulse);
                break;

            default:
                AdvanceFrame(elapsed, 0.32);
                break;
        }
    }

    /// <summary>
    /// Moves the pet without reshaping it.
    /// </summary>
    /// <remarks>
    /// The three moving poses used to squash one axis while stretching the other - the classic
    /// squash and stretch, which on a drawing this soft just looks like the pet is rippling, and
    /// which was reported as exactly that. A uniform scale and a rotation about the feet carry
    /// the same three ideas - breathing, swaying, reaching - and neither one can change the
    /// proportions of the artwork.
    /// </remarks>
    private void SetPose(double scale, double angle)
    {
        if (Math.Abs(_poseScale - scale) < 0.0005 && Math.Abs(_poseAngle - angle) < 0.01)
        {
            return;
        }

        _poseScale = scale;
        _poseAngle = angle;
        ApplyFacingTransform();
    }

    /// <summary>Shows the glyphs that belong to the pose now running, and hides the rest.</summary>
    private void ShowRestEffect()
    {
        SleepMark.Visibility = Collapsed(_motion.RestPose == PetRestPose.Sleep);
        DaydreamMark.Visibility = Collapsed(_motion.RestPose == PetRestPose.Daydream);
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
        Drift(SleepZSmallDrift, SleepZSmall, _motion.RestElapsed, 0);
        Drift(SleepZMediumDrift, SleepZMedium, _motion.RestElapsed, 0.9);
        Drift(SleepZLargeDrift, SleepZLarge, _motion.RestElapsed, 1.8);

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
        DaydreamDotSmall.Opacity = DotOpacity(_motion.RestElapsed, 0);
        DaydreamDotMedium.Opacity = DotOpacity(_motion.RestElapsed, 0.75);
        DaydreamDotLarge.Opacity = DotOpacity(_motion.RestElapsed, 1.5);

        static double DotOpacity(double elapsed, double phase)
        {
            var cycle = ((elapsed - phase) % 3.6 + 3.6) % 3.6;
            return elapsed < phase ? 0 : Math.Clamp(Math.Sin(cycle / 3.6 * Math.PI), 0, 1);
        }
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
        CompanionPetKind.Duck => 5.2,
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
}
