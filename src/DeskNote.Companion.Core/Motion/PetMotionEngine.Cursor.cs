namespace DeskNote.Companion.Core;

/// <summary>How the pet behaves towards the pointer.</summary>
public enum PetCursorMode
{
    /// <summary>Takes no notice.</summary>
    None,

    /// <summary>Turns to look at it while it sits.</summary>
    Watch,

    /// <summary>Walks towards it and sits beside it.</summary>
    Follow,

    /// <summary>Slides out of the way when it gets close.</summary>
    Dodge,
}

/// <summary>Where the pointer is, in screen pixels, and whether a button is down.</summary>
public readonly record struct PetCursor(double X, double Y, bool Pressed);

public sealed partial class PetMotionEngine
{
    /// <summary>How long the pointer has to stay on the pet before it slides away.</summary>
    public const double DodgeDelaySeconds = 0.12;

    /// <summary>After a slide the pet cannot be made to slide again for this long.</summary>
    public const double DodgeCooldownSeconds = 2.5;

    public const double DashMinimumSeconds = 0.7;
    public const double DashVariationSeconds = 0.25;

    /// <summary>A slide is much faster than a walk: about six times.</summary>
    public const double DashSpeedFactor = 6.2;

    public const double HopSeconds = 0.7;

    /// <summary>How high a hop goes, as a fraction of the pet's height.</summary>
    public const double HopHeightFraction = 0.2;

    /// <summary>How long a pet that has walked up to the pointer sits beside it.</summary>
    public const double FollowRestSeconds = 2.5;

    /// <summary>The pointer counts as being on the same level as the pet within this many pixels.</summary>
    public const double FollowBand = 140;

    /// <summary>The pet only notices a pointer within this many pixels of it sideways, and this far above or below.</summary>
    public const double WatchReach = 500;
    public const double WatchHeight = 450;

    private TimeSpan? _hoverSince;
    private TimeSpan _dodgeAfter;
    private TimeSpan _followRestAfter;
    private double _dashSeconds;
    private double _hopPeak;

    /// <summary>How far off the floor the pet is in the middle of a hop, in pixels.</summary>
    public double HopHeight => State == PetMotionState.Hopping
        ? Math.Sin(Math.PI * Math.Clamp(_stateElapsed / HopSeconds, 0, 1)) * _hopPeak
        : 0;

    /// <summary>
    /// Does whatever the pet does about the pointer on this tick.
    /// </summary>
    /// <returns>True when that changed what the pet is doing: it has started to slide, or to sit.</returns>
    private bool ReactToCursor(TimeSpan now, in PetMotionContext world, double min, double max)
    {
        if (world.CursorMode == PetCursorMode.None
            || world.Cursor is not { } cursor
            || _targetId != 0
            || State is not (PetMotionState.Walking or PetMotionState.Resting))
        {
            _hoverSince = null;
            return false;
        }

        var bodies = world.Bodies;
        var width = bodies.FloorWidth;
        var height = bodies.FloorHeight;
        var sideways = cursor.X - CenterX;

        var near = Math.Abs(sideways) < width * 0.9
            && cursor.Y >= CenterY - (height / 2) - 15
            && cursor.Y <= CenterY + (height / 2) + 8;
        if (near)
        {
            _hoverSince ??= now;
        }
        else
        {
            _hoverSince = null;
        }

        if (world.CursorMode == PetCursorMode.Dodge)
        {
            if (near
                && !cursor.Pressed
                && _hoverSince is { } since
                && (now - since).TotalSeconds > DodgeDelaySeconds
                && now >= _dodgeAfter)
            {
                _dodgeAfter = now + TimeSpan.FromSeconds(DodgeCooldownSeconds);
                _hoverSince = null;

                // Away from the pointer, and never into a corner the pet cannot run out of.
                Direction = sideways < 0 ? 1 : -1;
                if (CenterX > max - (width * 2))
                {
                    Direction = -1;
                }

                if (CenterX < min + (width * 2))
                {
                    Direction = 1;
                }

                BeginDashing(DashMinimumSeconds + (_random.NextDouble() * DashVariationSeconds));
                return true;
            }

            // A pet that dodges still looks at what it is dodging.
            WatchPointer(cursor, width);
            return false;
        }

        if (world.CursorMode == PetCursorMode.Follow
            && State == PetMotionState.Walking
            && Math.Abs(cursor.Y - CenterY) < FollowBand)
        {
            if (Math.Abs(sideways) > width)
            {
                Direction = sideways < 0 ? -1 : 1;
            }
            else if (Math.Abs(sideways) <= width * 0.6 && now >= _followRestAfter)
            {
                // It has got there: sit beside it for a while and look.
                _followRestAfter = now + TimeSpan.FromSeconds(FollowRestSeconds + 4);
                Direction = sideways < 0 ? -1 : 1;
                return BeginRest(now, world.BaseRestSeconds, world.Listless, FollowRestSeconds, PetRestPose.Sit);
            }

            return false;
        }

        WatchPointer(cursor, width);
        return false;
    }

    /// <summary>A pet that is sitting turns to face a pointer that is somewhere near it.</summary>
    private void WatchPointer(PetCursor cursor, double width)
    {
        if (State != PetMotionState.Resting || RestPose == PetRestPose.Sleep)
        {
            return;
        }

        var sideways = cursor.X - CenterX;
        if (Math.Abs(sideways) < width * 0.6
            || Math.Abs(sideways) > WatchReach
            || Math.Abs(cursor.Y - CenterY) > WatchHeight)
        {
            return;
        }

        Direction = sideways < 0 ? -1 : 1;
    }

    private void BeginDashing(double seconds)
    {
        SetState(PetMotionState.Dashing);
        _dashSeconds = seconds;
    }

    private PetMotionEvents AdvanceDashing(TimeSpan now, double elapsed, in PetMotionContext world)
    {
        CenterY = FloorCenter(world);
        FloorRange(world, out var min, out var max);

        CenterX += Direction * world.Speed * DashSpeedFactor * elapsed;
        var atEdge = CenterX <= min || CenterX >= max;
        CenterX = Math.Clamp(CenterX, min, max);

        // Out of room or out of breath: either way it stops where it is and walks on.
        if (atEdge || _stateElapsed >= _dashSeconds)
        {
            BeginWalking(now);
            return PetMotionEvents.Moved | PetMotionEvents.BeganWalking;
        }

        return PetMotionEvents.Moved;
    }

    private void BeginHop(in PetMotionContext world)
    {
        SetState(PetMotionState.Hopping);
        _hopPeak = world.Bodies.FloorHeight * HopHeightFraction;
    }

    private PetMotionEvents AdvanceHopping(TimeSpan now, in PetMotionContext world)
    {
        CenterY = FloorCenter(world);

        if (_stateElapsed >= HopSeconds)
        {
            BeginWalking(now);
            return PetMotionEvents.BeganWalking;
        }

        return PetMotionEvents.None;
    }
}
