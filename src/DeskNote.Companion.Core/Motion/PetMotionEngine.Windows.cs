namespace DeskNote.Companion.Core;

/// <summary>
/// The top edge of an open window, as something to stand on.
/// </summary>
/// <param name="Id">Whatever the caller uses to tell windows apart; it is how the pet recognises "the same window" tick to tick.</param>
/// <param name="Left">Left edge, in screen pixels.</param>
/// <param name="Top">Top edge: the line the pet's feet are on.</param>
/// <param name="Right">Right edge.</param>
public readonly record struct PetSurface(long Id, double Left, double Top, double Right);

public sealed partial class PetMotionEngine
{
    /// <summary>Climbing a window is quicker than walking, a little quicker than climbing the screen.</summary>
    public const double WindowClimbSpeedFactor = 1.35;

    /// <summary>How long between one attempt to go and climb a window and the next.</summary>
    public const double WindowClimbMinimumDelay = 35;
    public const double WindowClimbMaximumDelay = 65;

    /// <summary>The first attempt comes sooner than the later ones, so that a new pet is seen doing it.</summary>
    public const double FirstWindowClimbMinimumDelay = 25;
    public const double FirstWindowClimbMaximumDelay = 45;

    /// <summary>A window must have this much room above it and below it to be worth the trip.</summary>
    public const double WindowMargin = 70;

    /// <summary>How long it sits on top of a window it has just climbed.</summary>
    public const double WindowRestMinimumSeconds = 5;
    public const double WindowRestMaximumSeconds = 10;

    private PetSurface? _support;
    private long _supportId;
    private double _supportLeft;
    private long _targetId;
    private int _targetSide;
    private TimeSpan? _nextWindowClimb;

    /// <summary>The window the pet is standing on, or 0 when it is on the floor.</summary>
    public long SupportId => _supportId;

    /// <summary>The window the pet is heading for or climbing, or 0.</summary>
    public long TargetId => _targetId;

    private static PetSurface? FindSurface(in PetMotionContext world, long id)
    {
        if (world.Surfaces is not { } surfaces)
        {
            return null;
        }

        for (var index = 0; index < surfaces.Count; index++)
        {
            if (surfaces[index].Id == id)
            {
                return surfaces[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Works out whether the window the pet is standing on is still there, and moves the pet with it.
    /// </summary>
    /// <remarks>
    /// A window that has been closed, minimised, covered up or moved off the pet's side of the
    /// screen leaves the list of surfaces, and the pet leaves the window with it - by falling, which
    /// is what it would do. A window that has only moved carries the pet along; the pet does not
    /// have to notice the window moved, only that it is still where its feet are.
    /// </remarks>
    private void ResolveSupport(in PetMotionContext world)
    {
        _support = null;
        if (_supportId == 0)
        {
            return;
        }

        var found = world.AllowWindows ? FindSurface(world, _supportId) : null;
        if (found is not { } surface)
        {
            _supportId = 0;
            if (State is PetMotionState.Walking or PetMotionState.Resting or PetMotionState.Landing
                or PetMotionState.Dashing or PetMotionState.Hopping)
            {
                BeginFalling(0, 0);
            }

            return;
        }

        if (State is PetMotionState.Walking or PetMotionState.Resting or PetMotionState.Landing
            or PetMotionState.Dashing or PetMotionState.Hopping)
        {
            CenterX += surface.Left - _supportLeft;
        }

        _supportLeft = surface.Left;
        _support = surface;
    }

    /// <summary>Stands the pet on a window, remembering where its left edge is so that it can follow it.</summary>
    private void StandOn(PetSurface surface)
    {
        _supportId = surface.Id;
        _supportLeft = surface.Left;
        _support = surface;
        _targetId = 0;
    }

    private void LeaveSupport()
    {
        _supportId = 0;
        _support = null;
    }

    /// <summary>How far left and right the pet's centre can go: the window it is on, or the screen.</summary>
    private void FloorRange(in PetMotionContext world, out double min, out double max)
    {
        var half = world.Bodies.FloorWidth / 2;
        min = world.Area.Left + half;
        max = Math.Max(min, world.Area.Right - half);

        if (_support is { } surface)
        {
            min = Math.Max(min, surface.Left + half);
            max = Math.Max(min, Math.Min(max, surface.Right - half));
        }
    }

    /// <summary>
    /// Whether a falling pet's feet crossed the top of a window this tick, and which.
    /// </summary>
    /// <remarks>
    /// "Crossed" rather than "is at": a pet falling at a hundred pixels a second and one thrown at
    /// a thousand both have to land on a window they pass through between two ticks. The highest
    /// window wins, as it would have been met first.
    /// </remarks>
    private bool TryLandOnWindow(double feetBefore, double feetAfter, in PetMotionContext world, out PetSurface landing)
    {
        landing = default;
        if (!world.AllowWindows || world.Surfaces is not { } surfaces)
        {
            return false;
        }

        var half = world.Bodies.FloorWidth / 2;
        var found = false;
        for (var index = 0; index < surfaces.Count; index++)
        {
            var surface = surfaces[index];
            var crossed = surface.Top >= feetBefore - 0.5 && surface.Top <= feetAfter;
            var under = CenterX >= surface.Left + half && CenterX <= surface.Right - half;
            var room = surface.Top >= world.Area.Top + world.Bodies.FloorHeight;

            if (crossed && under && room && (!found || surface.Top < landing.Top))
            {
                landing = surface;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Picks a window to go and climb, and the side to climb it from.
    /// </summary>
    /// <returns>False when no open window is worth the trip.</returns>
    private bool ChooseWindow(in PetMotionContext world, double min, double max, out PetSurface chosen, out int side)
    {
        chosen = default;
        side = 0;
        if (!world.AllowWindows || world.Surfaces is not { Count: > 0 } surfaces)
        {
            return false;
        }

        var area = world.Area;
        var wallWidth = world.Bodies.WallWidth;
        var candidates = new List<(PetSurface Surface, int Side)>();

        foreach (var surface in surfaces)
        {
            if (surface.Top < area.Top + WindowMargin
                || surface.Top > area.Bottom - WindowMargin
                || surface.Right <= area.Left
                || surface.Left >= area.Right)
            {
                continue;
            }

            // Side +1: the pet stands left of the window with the window to its right.
            var leftTarget = surface.Left - (wallWidth / 2);
            var rightTarget = surface.Right + (wallWidth / 2);
            var fromLeft = surface.Left - wallWidth >= area.Left && leftTarget >= min && leftTarget <= max;
            var fromRight = surface.Right + wallWidth <= area.Right && rightTarget >= min && rightTarget <= max;

            if (fromLeft && fromRight)
            {
                candidates.Add((surface, _random.NextDouble() < 0.5 ? 1 : -1));
            }
            else if (fromLeft)
            {
                candidates.Add((surface, 1));
            }
            else if (fromRight)
            {
                candidates.Add((surface, -1));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var pick = candidates[Math.Min(candidates.Count - 1, (int)(_random.NextDouble() * candidates.Count))];
        chosen = pick.Surface;
        side = pick.Side;
        return true;
    }

    /// <summary>
    /// Decides, now and then, to go and climb a window; and once it has, walks there.
    /// </summary>
    /// <returns>True when the pet reached the foot of the window and began to climb it.</returns>
    private bool SteerTowardWindow(
        TimeSpan now,
        double elapsed,
        in PetMotionContext world,
        double min,
        double max)
    {
        if (!world.AllowWindows)
        {
            _targetId = 0;
            _nextWindowClimb = null;
            return false;
        }

        if (State != PetMotionState.Walking)
        {
            return false;
        }

        _nextWindowClimb ??= now + TimeSpan.FromSeconds(
            FirstWindowClimbMinimumDelay
            + (_random.NextDouble() * (FirstWindowClimbMaximumDelay - FirstWindowClimbMinimumDelay)));

        if (_targetId == 0 && now >= _nextWindowClimb)
        {
            _nextWindowClimb = now + TimeSpan.FromSeconds(
                WindowClimbMinimumDelay
                + (_random.NextDouble() * (WindowClimbMaximumDelay - WindowClimbMinimumDelay)));

            if (ChooseWindow(world, min, max, out var chosen, out var side))
            {
                _targetId = chosen.Id;
                _targetSide = side;
            }
        }

        if (_targetId == 0)
        {
            return false;
        }

        if (FindSurface(world, _targetId) is not { } surface)
        {
            _targetId = 0;
            return false;
        }

        // Walking to the window is the whole of what the pet is doing; it does not sit down on the way.
        _walkUntil = now + TimeSpan.FromSeconds(10);

        var wallWidth = world.Bodies.WallWidth;
        var targetX = _targetSide > 0 ? surface.Left - (wallWidth / 2) : surface.Right + (wallWidth / 2);
        if (Math.Abs(targetX - CenterX) <= (world.Speed * elapsed) + 2)
        {
            BeginWindowClimb(surface, _targetSide, world);
            return true;
        }

        Direction = targetX < CenterX ? -1 : 1;
        return false;
    }

    private void BeginWindowClimb(PetSurface surface, int side, in PetMotionContext world)
    {
        var bodies = world.Bodies;
        SetState(PetMotionState.WindowClimb);
        WallSide = side;
        Descending = false;
        _targetId = surface.Id;
        _targetSide = side;
        Direction = side;
        CenterX = side > 0 ? surface.Left - (bodies.WallWidth / 2) : surface.Right + (bodies.WallWidth / 2);

        // It starts on the floor beside the window and climbs from there.
        CenterY = world.Area.Bottom - (bodies.WallHeight / 2);
    }

    private PetMotionEvents AdvanceWindowClimb(TimeSpan now, double elapsed, in PetMotionContext world)
    {
        var surface = world.AllowWindows ? FindSurface(world, _targetId) : null;
        if (surface is not { } window)
        {
            Drop(world);
            return PetMotionEvents.None;
        }

        var bodies = world.Bodies;
        CenterX = WallSide > 0 ? window.Left - (bodies.WallWidth / 2) : window.Right + (bodies.WallWidth / 2);
        CenterY -= world.Speed * WindowClimbSpeedFactor * elapsed;

        // Over the top once more than half the body is above the edge.
        if (CenterY + (bodies.WallHeight / 2) <= window.Top + (bodies.WallHeight * 0.55))
        {
            StandOn(window);
            Direction = WallSide;
            CenterX = WallSide > 0
                ? window.Left + (bodies.FloorWidth / 2) + 8
                : window.Right - (bodies.FloorWidth / 2) - 8;
            CenterY = window.Top - (bodies.FloorHeight / 2);

            BeginWalking(now);
            BeginRest(
                now,
                world.BaseRestSeconds,
                world.Listless,
                seconds: WindowRestMinimumSeconds
                    + (_random.NextDouble() * (WindowRestMaximumSeconds - WindowRestMinimumSeconds)));
        }

        return PetMotionEvents.Moved;
    }

    private bool DemonstrateWindow(in PetMotionContext world, TimeSpan now)
    {
        StandOnFloor(world, now);
        FloorRange(world, out var min, out var max);
        if (!ChooseWindow(world, min, max, out var chosen, out var side))
        {
            return false;
        }

        _targetId = chosen.Id;
        _targetSide = side;
        return true;
    }

    /// <summary>Puts the pet on the floor of the screen, walking, wherever it was.</summary>
    private void StandOnFloor(in PetMotionContext world, TimeSpan now)
    {
        if (State is PetMotionState.Walking && _support is null)
        {
            return;
        }

        LeaveSupport();
        _targetId = 0;
        CenterY = world.Area.Bottom - (world.Bodies.FloorHeight / 2);
        CenterX = Math.Clamp(
            CenterX,
            world.Area.Left + (world.Bodies.FloorWidth / 2),
            Math.Max(world.Area.Left + (world.Bodies.FloorWidth / 2), world.Area.Right - (world.Bodies.FloorWidth / 2)));
        BeginWalking(now);
    }
}
