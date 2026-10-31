namespace DeskNote.Companion.Core;

/// <summary>
/// Counts how often a pet has been thrown in a short while.
/// </summary>
/// <remarks>
/// The same pet thrown three times in ten seconds has had enough. It is a reaction that costs the
/// user something to find, so it is a reward for fiddling and not a menu item - and it has to be
/// a count over a window, not a running total, or a pet thrown once an hour would get there on
/// the third day.
/// </remarks>
public sealed class ThrowStreak
{
    public const int DefaultThrows = 3;
    public const double DefaultWindowSeconds = 10;

    private readonly int _throws;
    private readonly double _windowSeconds;

    private int _count;
    private double _windowStart;

    public ThrowStreak(int throws = DefaultThrows, double windowSeconds = DefaultWindowSeconds)
    {
        _throws = throws;
        _windowSeconds = windowSeconds;
    }

    /// <summary>How many throws are in the window now.</summary>
    public int Count => _count;

    /// <summary>Records a throw at <paramref name="seconds"/>.</summary>
    /// <returns>True on the throw that makes the streak; the count then starts again.</returns>
    public bool Register(double seconds)
    {
        if (_count == 0 || seconds - _windowStart > _windowSeconds)
        {
            _count = 0;
            _windowStart = seconds;
        }

        _count++;
        if (_count < _throws)
        {
            return false;
        }

        _count = 0;
        return true;
    }

    public void Reset() => _count = 0;
}
