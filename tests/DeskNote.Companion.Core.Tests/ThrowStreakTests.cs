namespace DeskNote.Companion.Core.Tests;

public class ThrowStreakTests
{
    [Fact]
    public void Three_throws_in_ten_seconds_make_a_streak()
    {
        var streak = new ThrowStreak();

        Assert.False(streak.Register(0));
        Assert.False(streak.Register(3));
        Assert.True(streak.Register(6));
    }

    [Fact]
    public void Throws_spread_over_more_than_the_window_do_not()
    {
        var streak = new ThrowStreak();

        Assert.False(streak.Register(0));
        Assert.False(streak.Register(6));
        Assert.False(streak.Register(12));
        Assert.Equal(1, streak.Count);
    }

    [Fact]
    public void A_streak_starts_the_count_again()
    {
        var streak = new ThrowStreak();
        streak.Register(0);
        streak.Register(1);
        Assert.True(streak.Register(2));

        Assert.Equal(0, streak.Count);
        Assert.False(streak.Register(3));
        Assert.False(streak.Register(4));
        Assert.True(streak.Register(5));
    }

    [Fact]
    public void A_pet_thrown_once_an_hour_never_gets_there()
    {
        var streak = new ThrowStreak();

        for (var hour = 0; hour < 10; hour++)
        {
            Assert.False(streak.Register(hour * 3600.0));
        }
    }

    [Fact]
    public void The_limits_can_be_set()
    {
        var streak = new ThrowStreak(throws: 2, windowSeconds: 1);

        Assert.False(streak.Register(0));
        Assert.True(streak.Register(0.5));
    }

    [Fact]
    public void Resetting_forgets_the_throws()
    {
        var streak = new ThrowStreak();
        streak.Register(0);
        streak.Register(1);

        streak.Reset();

        Assert.Equal(0, streak.Count);
        Assert.False(streak.Register(2));
    }
}
