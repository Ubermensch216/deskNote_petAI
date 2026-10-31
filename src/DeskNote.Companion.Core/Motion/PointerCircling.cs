namespace DeskNote.Companion.Core;

/// <summary>
/// Notices a pointer that is going round and round a point.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else in the app asks the user to move the pointer in circles, and that is the point: it
/// is a reaction nobody is told about. The thresholds are set so that wandering across the pet does
/// not trip it. The pointer has to stay in a ring around the point, keep turning the same way, and
/// not stop for long; a turn the other way starts the count again.
/// </para>
/// <para>
/// It owns no clock and no pointer: it is handed the time and where the pointer is, which is what
/// lets a test draw a circle with arithmetic and check how many turns it takes to be noticed.
/// </para>
/// </remarks>
public sealed class PointerCircling
{
    /// <summary>How many full turns it takes.</summary>
    public const double DefaultTurns = 1.25;

    /// <summary>A pointer that stops for this long is no longer going round.</summary>
    public const double DefaultPauseSeconds = 0.45;

    /// <summary>Turns smaller than this are the pointer resting, not moving.</summary>
    private const double Jitter = 0.02;

    private readonly double _turns;
    private readonly double _pauseSeconds;

    private bool _tracking;
    private double _angle;
    private double _total;
    private double _seenAt;

    public PointerCircling(double turns = DefaultTurns, double pauseSeconds = DefaultPauseSeconds)
    {
        _turns = turns;
        _pauseSeconds = pauseSeconds;
    }

    /// <summary>How far round it has gone, in turns and signed by the direction. For tests and for curiosity.</summary>
    public double Turns => _total / (2 * Math.PI);

    /// <summary>Forgets what it has seen: the pointer went away, or the pet is busy.</summary>
    public void Reset()
    {
        _tracking = false;
        _total = 0;
    }

    /// <summary>
    /// Takes one look at the pointer.
    /// </summary>
    /// <param name="seconds">When, on any clock that does not go backwards.</param>
    /// <param name="pointerX">Where the pointer is.</param>
    /// <param name="pointerY">Where the pointer is.</param>
    /// <param name="centreX">The point being circled.</param>
    /// <param name="centreY">The point being circled.</param>
    /// <param name="innerRadius">Closer than this is on top of the point, not round it.</param>
    /// <param name="outerRadius">Further than this is somewhere else.</param>
    /// <returns>True the moment the pointer has gone round enough; the count then starts again.</returns>
    public bool Observe(
        double seconds,
        double pointerX,
        double pointerY,
        double centreX,
        double centreY,
        double innerRadius,
        double outerRadius)
    {
        var dx = pointerX - centreX;
        var dy = pointerY - centreY;
        var radius = Math.Sqrt((dx * dx) + (dy * dy));

        if (radius < innerRadius || radius > outerRadius)
        {
            Reset();
            return false;
        }

        var angle = Math.Atan2(dy, dx);
        if (!_tracking || seconds - _seenAt > _pauseSeconds)
        {
            _tracking = true;
            _angle = angle;
            _total = 0;
            _seenAt = seconds;
            return false;
        }

        var delta = angle - _angle;
        while (delta > Math.PI)
        {
            delta -= 2 * Math.PI;
        }

        while (delta < -Math.PI)
        {
            delta += 2 * Math.PI;
        }

        if (Math.Abs(delta) < Jitter)
        {
            return false;
        }

        _angle = angle;
        _seenAt = seconds;

        // Turning back the other way starts the count again.
        _total = _total == 0 || Math.Sign(delta) == Math.Sign(_total) ? _total + delta : delta;

        if (Math.Abs(_total) < _turns * 2 * Math.PI)
        {
            return false;
        }

        Reset();
        return true;
    }
}
