namespace DeskNote.Companion.Core;

/// <summary>What the pet is doing with its body, apart from how it is drawn.</summary>
public enum PetMotionState
{
    Walking,
    Resting,

    /// <summary>The short settle after coming down.</summary>
    Landing,

    /// <summary>Drifting down on a rotor.</summary>
    Falling,

    /// <summary>Flying after being let go of at speed.</summary>
    Thrown,

    /// <summary>In the pointer's hand.</summary>
    Held,

    /// <summary>Climbing a side of the screen.</summary>
    Wall,

    /// <summary>Crawling along the top of the screen.</summary>
    Ceiling,

    /// <summary>Climbing up the side of another application's window.</summary>
    WindowClimb,

    /// <summary>Sliding quickly along the floor, away from the pointer.</summary>
    Dashing,

    /// <summary>A little jump on the spot.</summary>
    Hopping,
}

/// <summary>What the pet is doing while it is not walking.</summary>
public enum PetRestPose
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

/// <summary>The part of the desktop the pet is moving about on: one display's work area.</summary>
/// <param name="Left">Left edge, in screen pixels.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Right">Right edge.</param>
/// <param name="Bottom">Bottom edge; the floor.</param>
/// <param name="LeftIsOuter">No other display touches the left edge, so it is a wall.</param>
/// <param name="RightIsOuter">No other display touches the right edge.</param>
/// <param name="TopIsOuter">No other display touches the top edge, so it is a ceiling.</param>
public readonly record struct PetArea(
    double Left,
    double Top,
    double Right,
    double Bottom,
    bool LeftIsOuter = true,
    bool RightIsOuter = true,
    bool TopIsOuter = true);

/// <summary>How much room the pet's drawing takes in each of the three ways it can be oriented.</summary>
/// <param name="FloorWidth">Standing, walking, falling, flying.</param>
/// <param name="FloorHeight">Standing, walking, falling, flying.</param>
/// <param name="WallWidth">Lying against a wall: the floor pose turned on its side.</param>
/// <param name="WallHeight">Lying against a wall.</param>
/// <param name="CeilingWidth">Hanging from the ceiling.</param>
/// <param name="CeilingHeight">Hanging from the ceiling.</param>
public readonly record struct PetBodies(
    double FloorWidth,
    double FloorHeight,
    double WallWidth,
    double WallHeight,
    double CeilingWidth,
    double CeilingHeight);

/// <summary>Movements the pet can be asked to show on the spot, for "preview actions".</summary>
public enum PetDemo
{
    /// <summary>Start up the nearest wall that can be climbed.</summary>
    Wall,

    /// <summary>Start along the ceiling.</summary>
    Ceiling,

    /// <summary>Be lifted to the top of the screen and float down.</summary>
    Fall,

    /// <summary>Walk to an open window, climb it and stand on top.</summary>
    Window,

    /// <summary>Slide a short way along the floor.</summary>
    Dash,

    /// <summary>Jump on the spot.</summary>
    Hop,
}

/// <summary>What the world looks like to the pet on this tick.</summary>
/// <param name="Area">The display the pet is on.</param>
/// <param name="Bodies">How much room the pet takes right now.</param>
/// <param name="Speed">Walking speed this tick, already scaled by mood and growth.</param>
/// <param name="BaseRestSeconds">How long this species sits before the pose's own factor.</param>
/// <param name="Listless">Whether a low mood should tilt the choice of rest pose.</param>
/// <param name="AllowWalls">Whether the pet may climb the sides of the screen.</param>
/// <param name="AllowCeiling">Whether the pet may crawl along the top of the screen.</param>
/// <param name="AllowWindows">Whether the pet may climb open windows and walk along their tops.</param>
/// <param name="CursorMode">How the pet reacts to the pointer.</param>
/// <param name="Cursor">Where the pointer is, or null when that cannot be known.</param>
/// <param name="Surfaces">The open windows the pet could stand on, in screen pixels.</param>
public readonly record struct PetMotionContext(
    PetArea Area,
    PetBodies Bodies,
    double Speed,
    double BaseRestSeconds,
    bool Listless,
    bool AllowWalls = false,
    bool AllowCeiling = false,
    bool AllowWindows = false,
    PetCursorMode CursorMode = PetCursorMode.None,
    PetCursor? Cursor = null,
    IReadOnlyList<PetSurface>? Surfaces = null);

/// <summary>What changed during one call to <see cref="PetMotionEngine.Advance"/>.</summary>
[Flags]
public enum PetMotionEvents
{
    None = 0,

    /// <summary>The pet moved.</summary>
    Moved = 1,

    /// <summary>The pet sat down on this tick.</summary>
    BeganRest = 2,

    /// <summary>The pet got up on this tick.</summary>
    BeganWalking = 4,

    /// <summary>The pet reached an edge and turned round.</summary>
    TurnedAtEdge = 8,

    /// <summary>The pet is part-way through a rest; the pose animation should move on.</summary>
    Resting = 16,

    /// <summary>The state is not what it was at the start of the tick; the drawing has to follow.</summary>
    StateChanged = 32,
}

/// <summary>
/// Where the pet is, which way it faces, and what it is up to - with no window, timer or drawing
/// attached, so that the same seed and the same sequence of ticks always give the same walk.
/// </summary>
/// <remarks>
/// <para>
/// The pet is a box: the rectangle its drawing fills in the pose it is in. Everything here is about
/// where that box goes. Walking keeps the box's bottom on the floor; the wall states keep one of
/// its sides on the edge of the screen; the ceiling keeps its top on the top. The window is then
/// placed so the drawing lands in the box, which is the window's business and none of this
/// class's.
/// </para>
/// <para>
/// It owns no clock. Every method takes the time it is meant to act at, which is what lets a test
/// run an hour of walking in a millisecond.
/// </para>
/// </remarks>
public sealed partial class PetMotionEngine
{
    public const double WalkLegMinimumSeconds = 3.2;
    public const double WalkLegMaximumSeconds = 9.5;

    /// <summary>How often reaching an edge of the screen starts a climb rather than a turn and a rest.</summary>
    public const double WallChance = 0.65;

    /// <summary>Climbing is a little quicker than walking, and the ceiling a little slower than that.</summary>
    public const double WallSpeedFactor = 1.24;
    public const double CeilingSpeedFactor = 1.06;

    /// <summary>After this long on the ceiling the pet may let go.</summary>
    public const double CeilingHoldSeconds = 14;
    public const double CeilingDropPerSecond = 0.08;

    public const double LandingSeconds = 0.3;

    /// <summary>The rotor's pace: it accelerates to a gentle sink rather than dropping.</summary>
    public const double FallAcceleration = 66;
    public const double FallMinimumSpeed = 42;
    public const double FallMaximumSpeed = 114;

    public const double ThrowGravity = 900;
    public const double ThrowMinimumSpeed = 300;
    public const double ThrowMaximumSpeed = 1600;
    public const double ThrowSampleSeconds = 0.12;

    private const double FloorTossRise = -240;
    private const double ThrowCarry = 0.7;
    private const double ThrowLift = -80;
    private const double ThrowMaximumRise = 700;
    private const double ThrowDrag = 1.6;
    private const double FallDrift = 0.45;
    private const double DriftDecay = 4;

    private readonly Random _random;
    private readonly List<(double Time, double X, double Y)> _samples = [];

    private TimeSpan _restUntil;
    private TimeSpan _walkUntil;
    private double _stateElapsed;
    private double _velocityX;
    private double _velocityY;
    private double _throwSeconds;
    private double _drift;
    private bool _changed;
    private QueuedRest? _queued;

    /// <param name="random">
    /// Kept apart from the chatter's generator on purpose, so a change to how often the pet speaks
    /// cannot silently reshuffle how it moves.
    /// </param>
    public PetMotionEngine(Random? random = null) => _random = random ?? new Random();

    /// <summary>Horizontal centre of the pet's box, in screen pixels.</summary>
    public double CenterX { get; set; }

    /// <summary>Vertical centre of the pet's box, in screen pixels.</summary>
    public double CenterY { get; set; }

    /// <summary>-1 for left, 1 for right: which way the pet faces and, on the floor and ceiling, goes.</summary>
    public int Direction { get; set; } = -1;

    public PetMotionState State { get; private set; } = PetMotionState.Walking;

    /// <summary>-1 for the left wall, 1 for the right: the side the pet is climbing.</summary>
    public int WallSide { get; private set; } = 1;

    /// <summary>True on the way down a wall, head first.</summary>
    public bool Descending { get; private set; }

    /// <summary>
    /// The pose of the current or most recent rest. It is deliberately left alone when the pet gets
    /// up: it is the only memory of what the pet did last, and the next roll avoids repeating it.
    /// </summary>
    public PetRestPose RestPose { get; private set; } = PetRestPose.Sit;

    public double RestElapsed { get; private set; }

    public double RestDuration { get; private set; }

    /// <summary>Horizontal velocity in a throw, in pixels a second.</summary>
    public double VelocityX => _velocityX;

    /// <summary>How long the pet has been in its current state, in seconds.</summary>
    public double StateElapsed => _stateElapsed;

    /// <summary>How far through the landing settle, from 0 to 1.</summary>
    public double LandingProgress => State == PetMotionState.Landing
        ? Math.Clamp(_stateElapsed / LandingSeconds, 0, 1)
        : 0;

    /// <summary>
    /// Whether the pet is on its feet, so anything that wants it to sit may have it now. That is
    /// the floor or the top of a window, and a slide or a hop in progress gives way to a rest.
    /// </summary>
    public bool IsOnFloor => State is PetMotionState.Walking or PetMotionState.Resting
        or PetMotionState.Dashing or PetMotionState.Hopping;

    /// <summary>Whether the pet is on a wall, the ceiling or the side of a window.</summary>
    public bool IsClimbing => State is PetMotionState.Wall or PetMotionState.Ceiling or PetMotionState.WindowClimb;

    /// <summary>Whether something is carrying the pet through the air: falling, flying or in a hand.</summary>
    public bool IsAirborne => State is PetMotionState.Falling or PetMotionState.Thrown or PetMotionState.Held;

    /// <summary>How long a pose holds, against the species' own base rest.</summary>
    public static double RestPoseFactor(PetRestPose pose) => pose switch
    {
        PetRestPose.Sleep => 2.2,
        PetRestPose.Daydream => 1.5,
        PetRestPose.Stretch => 0.7,
        _ => 1,
    };

    /// <summary>The box the pet fills in its current pose.</summary>
    public PetBox Box(in PetBodies bodies)
    {
        var (width, height) = Size(State, bodies);
        return new PetBox(CenterX - (width / 2), CenterY - (height / 2), width, height);
    }

    /// <summary>Decides how far the pet gets before it next sits down.</summary>
    public void ScheduleNextWalkLeg(TimeSpan now) =>
        _walkUntil = now + TimeSpan.FromSeconds(
            WalkLegMinimumSeconds
            + (_random.NextDouble() * (WalkLegMaximumSeconds - WalkLegMinimumSeconds)));

    /// <summary>
    /// Puts the pet down at a point and lets it find the floor from there.
    /// </summary>
    /// <remarks>
    /// A pet placed on the floor starts walking; a pet placed above it comes down on its rotor,
    /// which is how a saved position that is no longer where the floor is gets sorted out.
    /// </remarks>
    public void Place(TimeSpan now, double centerX, double centerY, in PetMotionContext world)
    {
        CenterX = centerX;
        CenterY = centerY;
        _queued = null;
        LeaveSupport();

        var floorBox = FloorCenter(world);
        if (centerY >= floorBox - 1)
        {
            CenterY = floorBox;
            BeginWalking(now);
        }
        else
        {
            BeginFalling(0, 0);
        }
    }

    /// <summary>Sits the pet down, choosing a pose and a length unless it is told both.</summary>
    /// <returns>
    /// False when the pet is not on its feet and the rest has been held back until it is: a pet
    /// that is climbing, flying or being carried cannot sit, and one that has just been put down
    /// sits when it has finished landing.
    /// </returns>
    public bool BeginRest(
        TimeSpan now,
        double baseRestSeconds,
        bool listless,
        double? seconds = null,
        PetRestPose? pose = null)
    {
        if (!IsOnFloor)
        {
            _queued = new QueuedRest(baseRestSeconds, listless, seconds, pose);
            return false;
        }

        SetState(PetMotionState.Resting);
        RestPose = pose ?? ChooseRestPose(listless);
        RestElapsed = 0;
        RestDuration = seconds ?? (baseRestSeconds * RestPoseFactor(RestPose));
        _restUntil = now + TimeSpan.FromSeconds(RestDuration);
        return true;
    }

    /// <summary>Gets the pet back on its feet and plans where it stops next.</summary>
    public void BeginWalking(TimeSpan now)
    {
        SetState(PetMotionState.Walking);
        ScheduleNextWalkLeg(now);
    }

    /// <summary>Takes the pet into the pointer's hand.</summary>
    public void Grab()
    {
        _queued = null;
        LeaveSupport();
        _samples.Clear();
        _velocityX = 0;
        _velocityY = 0;
        _drift = 0;
        SetState(PetMotionState.Held);
    }

    /// <summary>Moves the pet with the pointer while it is being held, and remembers how fast.</summary>
    public void Hold(double time, double centerX, double centerY)
    {
        if (State != PetMotionState.Held)
        {
            return;
        }

        CenterX = centerX;
        CenterY = centerY;

        // Only the last tenth of a second says how hard the pet was thrown: a pointer that swung
        // fast and then rested before letting go is a pet being put down, not thrown.
        var last = _samples.Count - 1;
        if (last >= 0 && time - _samples[last].Time < 0.0001)
        {
            _samples[last] = (time, centerX, centerY);
        }
        else
        {
            _samples.Add((time, centerX, centerY));
        }

        while (_samples.Count > 1 && time - _samples[0].Time > ThrowSampleSeconds)
        {
            _samples.RemoveAt(0);
        }
    }

    /// <summary>
    /// Lets go of the pet.
    /// </summary>
    /// <returns>True when it was thrown; otherwise it is put down and falls, or lands, from where it is.</returns>
    public bool Release(in PetMotionContext world)
    {
        if (State != PetMotionState.Held)
        {
            return false;
        }

        var thrown = TryThrow(world);
        _samples.Clear();
        if (thrown)
        {
            return true;
        }

        var floor = FloorCenter(world);
        if (CenterY >= floor - 1)
        {
            CenterY = floor;
            BeginLanding();
        }
        else
        {
            BeginFalling(0, 0);
        }

        return false;
    }

    /// <summary>
    /// Starts a movement on the spot, so that someone can see it without waiting for it to happen.
    /// </summary>
    /// <returns>
    /// False when the screen offers nothing to do it on: a wall needs an edge that no other display
    /// touches, and the ceiling needs a top that none does.
    /// </returns>
    public bool Demonstrate(PetDemo demo, in PetMotionContext world, TimeSpan now = default)
    {
        var area = world.Area;
        _queued = null;

        switch (demo)
        {
            case PetDemo.Window:
                return DemonstrateWindow(world, now);

            case PetDemo.Dash:
                StandOnFloor(world, now);
                BeginDashing(DashMinimumSeconds + 0.1);
                return true;

            case PetDemo.Hop:
                StandOnFloor(world, now);
                BeginHop(world);
                return true;

            case PetDemo.Wall:
                var side = area.RightIsOuter ? 1 : area.LeftIsOuter ? -1 : 0;
                if (side == 0)
                {
                    return false;
                }

                BeginWall(side, descending: false, world);
                return true;

            case PetDemo.Ceiling:
                if (!area.TopIsOuter)
                {
                    return false;
                }

                // Straight up from where the pet is, heading for whichever end is further away.
                var from = CenterX;
                BeginCeiling(from < (area.Left + area.Right) / 2 ? -1 : 1, world);
                var half = world.Bodies.CeilingWidth / 2;
                CenterX = Math.Clamp(from, area.Left + half, Math.Max(area.Left + half, area.Right - half));
                return true;

            default:
                CenterY = area.Top + (world.Bodies.FloorHeight / 2);
                BeginFalling(0, 0);
                return true;
        }
    }

    /// <summary>Makes the pet let go of a wall, the ceiling or a window and come down.</summary>
    public void Drop(in PetMotionContext world)
    {
        if (!IsClimbing)
        {
            return;
        }

        _targetId = 0;
        BeginFalling(0, 0);
        ClampToArea(world);
    }

    /// <summary>
    /// Moves the pet along by <paramref name="elapsed"/> seconds.
    /// </summary>
    /// <returns>
    /// What happened, so the caller can change the artwork without having to compare states itself.
    /// </returns>
    public PetMotionEvents Advance(TimeSpan now, double elapsed, in PetMotionContext world)
    {
        _changed = false;
        _stateElapsed += elapsed;
        ResolveSupport(world);

        var events = State switch
        {
            PetMotionState.Walking or PetMotionState.Resting => AdvanceFloor(now, elapsed, world),
            PetMotionState.Landing => AdvanceLanding(now, world),
            PetMotionState.Falling => AdvanceFalling(elapsed, world),
            PetMotionState.Thrown => AdvanceThrown(elapsed, world),
            PetMotionState.Wall => AdvanceWall(elapsed, world),
            PetMotionState.Ceiling => AdvanceCeiling(elapsed, world),
            PetMotionState.WindowClimb => AdvanceWindowClimb(now, elapsed, world),
            PetMotionState.Dashing => AdvanceDashing(now, elapsed, world),
            PetMotionState.Hopping => AdvanceHopping(now, world),
            _ => PetMotionEvents.None,
        };

        return _changed ? events | PetMotionEvents.StateChanged : events;
    }

    private static (double Width, double Height) Size(PetMotionState state, in PetBodies bodies) => state switch
    {
        PetMotionState.Wall or PetMotionState.WindowClimb => (bodies.WallWidth, bodies.WallHeight),
        PetMotionState.Ceiling => (bodies.CeilingWidth, bodies.CeilingHeight),
        _ => (bodies.FloorWidth, bodies.FloorHeight),
    };

    /// <summary>
    /// Where the centre of a standing pet's box is when its feet are on whatever it stands on: the
    /// floor, or the top of the window it has climbed.
    /// </summary>
    private double FloorCenter(in PetMotionContext world) =>
        (_support?.Top ?? world.Area.Bottom) - (world.Bodies.FloorHeight / 2);

    private PetMotionEvents AdvanceFloor(TimeSpan now, double elapsed, in PetMotionContext world)
    {
        var events = PetMotionEvents.None;
        CenterY = FloorCenter(world);

        FloorRange(world, out var min, out var max);
        CenterX = Math.Clamp(CenterX, min, max);

        // The pointer comes first: a pet that is about to be caught slides away from it whether it
        // was walking or sitting, and one that is watching it turns to look wherever it was.
        if (ReactToCursor(now, world, min, max))
        {
            return PetMotionEvents.Moved;
        }

        if (State == PetMotionState.Resting)
        {
            if (now < _restUntil)
            {
                RestElapsed += elapsed;
                return PetMotionEvents.Resting;
            }

            BeginWalking(now);
            events |= PetMotionEvents.BeganWalking;
        }

        if (_support is null && SteerTowardWindow(now, elapsed, world, min, max))
        {
            return events | PetMotionEvents.Moved;
        }

        CenterX += Direction * world.Speed * elapsed;
        events |= PetMotionEvents.Moved;

        if (CenterX <= min || CenterX >= max)
        {
            var side = CenterX <= min ? -1 : 1;
            CenterX = side < 0 ? min : max;

            if (_support is null)
            {
                var outer = side < 0 ? world.Area.LeftIsOuter : world.Area.RightIsOuter;
                if (world.AllowWalls && outer && _targetId == 0 && _random.NextDouble() < WallChance)
                {
                    BeginWall(side, descending: false, world);
                    return events;
                }
            }

            Direction = -side;
            events |= PetMotionEvents.TurnedAtEdge;
            BeginRest(now, world.BaseRestSeconds, world.Listless);
            events |= PetMotionEvents.BeganRest;
        }
        else if (now >= _walkUntil)
        {
            // Resting only at the two edges turned the pet into a pendulum: the same two stops,
            // in the same two places, for as long as the machine was on. A leg of random length
            // means it also sits down in the middle of the screen, which is where the user
            // actually is.
            BeginRest(now, world.BaseRestSeconds, world.Listless);
            events |= PetMotionEvents.BeganRest;
        }

        return events;
    }

    private PetMotionEvents AdvanceLanding(TimeSpan now, in PetMotionContext world)
    {
        CenterY = FloorCenter(world);

        if (_stateElapsed < LandingSeconds)
        {
            return PetMotionEvents.None;
        }

        var events = PetMotionEvents.BeganWalking;
        if (_queued is { } queued)
        {
            _queued = null;
            BeginWalking(now);
            BeginRest(now, queued.BaseRestSeconds, queued.Listless, queued.Seconds, queued.Pose);
            events = PetMotionEvents.BeganRest;
        }
        else
        {
            BeginWalking(now);
        }

        return events;
    }

    private PetMotionEvents AdvanceFalling(double elapsed, in PetMotionContext world)
    {
        // A rotor sinks, it does not drop: speed builds up to a steady descent and stays there.
        _velocityY = Math.Min(FallMaximumSpeed, Math.Max(FallMinimumSpeed, _velocityY + (FallAcceleration * elapsed)));
        var feetBefore = CenterY + (world.Bodies.FloorHeight / 2);
        CenterY += _velocityY * elapsed;

        if (Math.Abs(_drift) > 0.1)
        {
            CenterX += _drift * elapsed;
            _drift *= Math.Exp(-DriftDecay * elapsed);
        }

        ClampToArea(world);

        if (TryLandOnWindow(feetBefore, CenterY + (world.Bodies.FloorHeight / 2), world, out var window))
        {
            StandOn(window);
            CenterY = window.Top - (world.Bodies.FloorHeight / 2);
            BeginLanding();
            return PetMotionEvents.Moved;
        }

        if (CenterY >= FloorCenter(world))
        {
            CenterY = FloorCenter(world);
            BeginLanding();
        }

        return PetMotionEvents.Moved;
    }

    private PetMotionEvents AdvanceThrown(double elapsed, in PetMotionContext world)
    {
        var bodies = world.Bodies;
        var halfWidth = bodies.FloorWidth / 2;
        var halfHeight = bodies.FloorHeight / 2;

        _velocityY += ThrowGravity * elapsed;
        var nextX = CenterX + (_velocityX * elapsed);
        var nextY = CenterY + (_velocityY * elapsed);
        _velocityX *= Math.Exp(-ThrowDrag * elapsed);

        var leftLimit = world.Area.Left + halfWidth;
        var rightLimit = Math.Max(leftLimit, world.Area.Right - halfWidth);
        var topLimit = world.Area.Top + halfHeight;

        var hitSide = nextX < leftLimit || nextX > rightLimit;
        var hitTop = nextY < topLimit;
        var nearFloor = _velocityY > 0 && nextY >= FloorCenter(world) - 10;

        var feetBefore = CenterY + halfHeight;
        CenterX = Math.Clamp(nextX, leftLimit, rightLimit);
        CenterY = Math.Max(topLimit, Math.Min(FloorCenter(world), nextY));

        // Flying over the top of a window: open the rotor just above it, and let the fall do the
        // landing, so a throw never passes through ground the pet could have stood on.
        if (_velocityY > 0 && TryLandOnWindow(feetBefore, CenterY + halfHeight + 18, world, out var over))
        {
            CenterY = Math.Min(CenterY, over.Top - halfHeight - 1);
            nearFloor = true;
        }

        if (hitSide)
        {
            _velocityX = -_velocityX * 0.2;
        }

        if (hitTop)
        {
            _velocityY = Math.Max(0, -_velocityY * 0.2);
        }

        if (_stateElapsed >= _throwSeconds || hitSide || hitTop || nearFloor)
        {
            // The rotor opens: what is left of the throw becomes a drift and a slow sink.
            BeginFalling(Math.Max(0, _velocityY * 0.25), _velocityX * FallDrift);
        }

        return PetMotionEvents.Moved;
    }

    private PetMotionEvents AdvanceWall(double elapsed, in PetMotionContext world)
    {
        if (!world.AllowWalls)
        {
            Drop(world);
            return PetMotionEvents.None;
        }

        var bodies = world.Bodies;
        var speed = world.Speed * WallSpeedFactor;
        CenterX = WallSide < 0
            ? world.Area.Left + (bodies.WallWidth / 2)
            : world.Area.Right - (bodies.WallWidth / 2);

        if (!Descending)
        {
            CenterY -= speed * elapsed;
            if (CenterY - (bodies.WallHeight / 2) <= world.Area.Top)
            {
                if (world.AllowCeiling && world.Area.TopIsOuter)
                {
                    BeginCeiling(WallSide, world);
                }
                else
                {
                    CenterY = world.Area.Top + (bodies.WallHeight / 2);
                    Drop(world);
                }
            }
        }
        else
        {
            CenterY += speed * elapsed;
            if (CenterY + (bodies.WallHeight / 2) >= world.Area.Bottom)
            {
                // Down at last, with the wall behind it: walk away from it.
                var side = WallSide;
                Direction = -side;
                CenterX = side < 0
                    ? world.Area.Left + (bodies.FloorWidth / 2)
                    : world.Area.Right - (bodies.FloorWidth / 2);
                CenterY = FloorCenter(world);
                BeginLanding();
            }
        }

        return PetMotionEvents.Moved;
    }

    private PetMotionEvents AdvanceCeiling(double elapsed, in PetMotionContext world)
    {
        if (!world.AllowCeiling)
        {
            Drop(world);
            return PetMotionEvents.None;
        }

        var bodies = world.Bodies;
        CenterY = world.Area.Top + (bodies.CeilingHeight / 2);
        CenterX += Direction * world.Speed * CeilingSpeedFactor * elapsed;

        if (_stateElapsed > CeilingHoldSeconds && _random.NextDouble() < elapsed * CeilingDropPerSecond)
        {
            Drop(world);
            return PetMotionEvents.Moved;
        }

        var min = world.Area.Left + (bodies.CeilingWidth / 2);
        var max = Math.Max(min, world.Area.Right - (bodies.CeilingWidth / 2));
        if (CenterX <= min || CenterX >= max)
        {
            var side = CenterX <= min ? -1 : 1;
            CenterX = side < 0 ? min : max;

            var outer = side < 0 ? world.Area.LeftIsOuter : world.Area.RightIsOuter;
            if (world.AllowWalls && outer)
            {
                BeginWall(side, descending: true, world);
            }
            else
            {
                Direction = -side;
            }
        }

        return PetMotionEvents.Moved;
    }

    private void BeginWall(int side, bool descending, in PetMotionContext world)
    {
        var bodies = world.Bodies;
        SetState(PetMotionState.Wall);
        WallSide = side;
        Descending = descending;
        CenterX = side < 0
            ? world.Area.Left + (bodies.WallWidth / 2)
            : world.Area.Right - (bodies.WallWidth / 2);
        CenterY = descending
            ? world.Area.Top + (bodies.WallHeight / 2)
            : world.Area.Bottom - (bodies.WallHeight / 2);
        Direction = side;
    }

    private void BeginCeiling(int fromSide, in PetMotionContext world)
    {
        var bodies = world.Bodies;
        SetState(PetMotionState.Ceiling);
        Direction = -fromSide;
        CenterY = world.Area.Top + (bodies.CeilingHeight / 2);
        CenterX = fromSide < 0
            ? world.Area.Left + (bodies.CeilingWidth / 2)
            : world.Area.Right - (bodies.CeilingWidth / 2);
    }

    private void BeginFalling(double velocityY, double drift)
    {
        LeaveSupport();
        SetState(PetMotionState.Falling);
        _velocityY = velocityY;
        _drift = drift;
        _velocityX = 0;
    }

    private void BeginLanding()
    {
        SetState(PetMotionState.Landing);
        _velocityX = 0;
        _velocityY = 0;
        _drift = 0;
    }

    private bool TryThrow(in PetMotionContext world)
    {
        if (_samples.Count < 2)
        {
            return false;
        }

        var first = _samples[0];
        var last = _samples[^1];
        var seconds = last.Time - first.Time;
        if (seconds < 0.008)
        {
            return false;
        }

        var vx = (last.X - first.X) / seconds;
        var vy = (last.Y - first.Y) / seconds;
        var speed = Math.Sqrt((vx * vx) + (vy * vy));
        if (speed < ThrowMinimumSpeed)
        {
            return false;
        }

        var limit = Math.Min(1, ThrowMaximumSpeed / speed);
        _velocityX = vx * limit * ThrowCarry;
        _velocityY = Math.Clamp((vy * limit * ThrowCarry) + ThrowLift, -ThrowMaximumRise, ThrowMaximumRise);

        // A toss from the floor has to leave it before the rotor can open, or the pet would be
        // "thrown" straight into the ground.
        var floor = FloorCenter(world);
        if (CenterY > floor - 24)
        {
            _velocityY = Math.Min(_velocityY, FloorTossRise);
        }


        _throwSeconds = 0.45 + (0.15 * Math.Clamp(-_velocityY / 600, 0, 1));
        if (Math.Abs(_velocityX) > 25)
        {
            Direction = _velocityX < 0 ? -1 : 1;
        }

        SetState(PetMotionState.Thrown);
        return true;
    }

    private void ClampToArea(in PetMotionContext world)
    {
        var bodies = world.Bodies;
        var left = world.Area.Left + (bodies.FloorWidth / 2);
        var right = Math.Max(left, world.Area.Right - (bodies.FloorWidth / 2));
        var top = world.Area.Top + (bodies.FloorHeight / 2);
        CenterX = Math.Clamp(CenterX, left, right);
        CenterY = Math.Clamp(CenterY, top, Math.Max(top, FloorCenter(world)));
    }

    private void SetState(PetMotionState state)
    {
        if (State != state)
        {
            _changed = true;
        }

        State = state;
        _stateElapsed = 0;
    }

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
    private PetRestPose ChooseRestPose(bool listless)
    {
        var pose = RollRestPose(listless);
        return pose == RestPose ? RollRestPose(listless) : pose;
    }

    private PetRestPose RollRestPose(bool listless)
    {
        var roll = _random.Next(100);

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

    private sealed record QueuedRest(double BaseRestSeconds, bool Listless, double? Seconds, PetRestPose? Pose);
}
