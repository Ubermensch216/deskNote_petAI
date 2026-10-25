namespace DeskNote.Companion.Core.Tests;

public class PointerCirclingTests
{
    private const double Inner = 50;
    private const double Outer = 300;

    [Fact]
    public void A_pointer_going_round_a_point_is_noticed_after_a_turn_and_a_quarter()
    {
        var circling = new PointerCircling();

        var turnsAtNotice = Circle(circling, radius: 150, direction: 1, turns: 2);

        Assert.InRange(turnsAtNotice, 1.2, 1.4);
    }

    [Fact]
    public void Turning_the_other_way_is_noticed_too()
    {
        var circling = new PointerCircling();

        var turnsAtNotice = Circle(circling, radius: 150, direction: -1, turns: 2);

        Assert.InRange(turnsAtNotice, 1.2, 1.4);
    }

    [Fact]
    public void Reversing_part_of_the_way_starts_the_count_again()
    {
        var circling = new PointerCircling();
        var clock = 0.0;

        // Three quarters of a turn one way, then back: nothing near a turn and a quarter either way.
        var noticed = Sweep(circling, ref clock, radius: 150, startTurns: 0, endTurns: 0.75);
        noticed |= Sweep(circling, ref clock, radius: 150, startTurns: 0.75, endTurns: 0);
        noticed |= Sweep(circling, ref clock, radius: 150, startTurns: 0, endTurns: 0.75);

        Assert.False(noticed);
    }

    [Fact]
    public void Staying_on_top_of_the_point_does_not_count()
    {
        var circling = new PointerCircling();

        Assert.Equal(double.NaN, Circle(circling, radius: 10, direction: 1, turns: 3));
    }

    [Fact]
    public void Staying_far_from_the_point_does_not_count()
    {
        var circling = new PointerCircling();

        Assert.Equal(double.NaN, Circle(circling, radius: 600, direction: 1, turns: 3));
    }

    [Fact]
    public void A_pause_breaks_the_run()
    {
        var circling = new PointerCircling();
        var clock = 0.0;

        var noticed = Sweep(circling, ref clock, radius: 150, startTurns: 0, endTurns: 0.7);
        clock += PointerCircling.DefaultPauseSeconds * 2;
        noticed |= Sweep(circling, ref clock, radius: 150, startTurns: 0.7, endTurns: 1.4);

        Assert.False(noticed);
    }

    [Fact]
    public void The_count_starts_again_once_it_has_fired()
    {
        var circling = new PointerCircling();

        Circle(circling, radius: 150, direction: 1, turns: 2);

        Assert.Equal(0, circling.Turns);
    }

    [Fact]
    public void Resetting_forgets_the_progress()
    {
        var circling = new PointerCircling();
        var clock = 0.0;
        Sweep(circling, ref clock, radius: 150, startTurns: 0, endTurns: 0.9);
        Assert.NotEqual(0, circling.Turns);

        circling.Reset();

        Assert.Equal(0, circling.Turns);
    }

    /// <summary>Draws the circle at 60 looks a second; returns the turns it took to be noticed, or NaN if it never was.</summary>
    private static double Circle(PointerCircling circling, double radius, int direction, double turns)
    {
        var clock = 0.0;
        var step = 1.0 / 60;
        var total = turns * 120;

        for (var i = 0; i <= total; i++)
        {
            var angle = direction * i / 120.0 * 2 * Math.PI;
            clock += step;
            if (circling.Observe(clock, Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0, 0, Inner, Outer))
            {
                return i / 120.0;
            }
        }

        return double.NaN;
    }

    private static bool Sweep(PointerCircling circling, ref double clock, double radius, double startTurns, double endTurns)
    {
        var noticed = false;
        var count = (int)Math.Ceiling(Math.Abs(endTurns - startTurns) * 120);
        for (var i = 0; i <= count; i++)
        {
            var turns = startTurns + ((endTurns - startTurns) * i / Math.Max(1, count));
            var angle = turns * 2 * Math.PI;
            clock += 1.0 / 60;
            noticed |= circling.Observe(clock, Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0, 0, Inner, Outer);
        }

        return noticed;
    }
}
