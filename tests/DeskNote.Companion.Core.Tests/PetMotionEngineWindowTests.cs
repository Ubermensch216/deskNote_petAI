namespace DeskNote.Companion.Core.Tests;

/// <summary>Standing on, climbing and falling off other applications' windows.</summary>
public partial class PetMotionEngineTests
{
    private static PetMotionContext Windowed(params PetSurface[] surfaces) =>
        Floor with { AllowWindows = true, Surfaces = surfaces };

    /// <summary>A window in the middle of the screen, a good way up from the floor.</summary>
    private static readonly PetSurface Middle = new(1, 600, 600, 1200);

    /// <summary>A pet that has fallen from a height onto <paramref name="window"/> and landed on it.</summary>
    private static PetMotionEngine OnWindow(PetSurface window, double x, double seed = 1)
    {
        var engine = new PetMotionEngine(new Random((int)seed));
        var world = Windowed(window);
        engine.Place(At(0), x, window.Top - 300, world);
        double clock = 0;
        for (var i = 0; i < 4000 && engine.State != PetMotionState.Landing; i++)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(PetMotionState.Landing, engine.State);
        for (var i = 0; i < 12; i++)
        {
            Tick(engine, ref clock, world);
        }

        return engine;
    }

    // ------------------------------------------------------------------ landing on a window

    [Fact]
    public void A_pet_floating_down_lands_on_the_top_of_a_window_it_passes_over()
    {
        var engine = new PetMotionEngine(new Random(1));
        var world = Windowed(Middle);
        engine.Place(At(0), 900, 300, world);
        double clock = 0;

        while (engine.State == PetMotionState.Falling)
        {
            Tick(engine, ref clock, world);
            Assert.True(clock < 120, "never landed");
        }

        Assert.Equal(PetMotionState.Landing, engine.State);
        Assert.Equal(Middle.Id, engine.SupportId);
        Near(Middle.Top, engine.Box(Bodies).Bottom);
    }

    [Fact]
    public void A_pet_beside_the_window_falls_past_it_to_the_floor()
    {
        var engine = new PetMotionEngine(new Random(1));
        var world = Windowed(Middle);
        engine.Place(At(0), 300, 300, world);
        double clock = 0;

        while (engine.State == PetMotionState.Falling)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(0, engine.SupportId);
        Near(1000, engine.Box(Bodies).Bottom);
    }

    [Fact]
    public void Windows_do_nothing_unless_they_are_allowed()
    {
        var engine = new PetMotionEngine(new Random(1));
        var world = Windowed(Middle) with { AllowWindows = false };
        engine.Place(At(0), 900, 300, world);
        double clock = 0;

        while (engine.State == PetMotionState.Falling)
        {
            Tick(engine, ref clock, world);
        }

        Near(1000, engine.Box(Bodies).Bottom);
    }

    [Fact]
    public void A_window_too_close_to_the_top_of_the_screen_is_not_stood_on()
    {
        // Less than a pet's height of headroom: it would stand with its head off the screen.
        var low = new PetSurface(2, 600, 60, 1200);
        var engine = new PetMotionEngine(new Random(1));
        var world = Windowed(low);
        engine.Place(At(0), 900, 20, world);
        double clock = 0;

        while (engine.State == PetMotionState.Falling)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(0, engine.SupportId);
    }

    [Fact]
    public void A_throw_that_flies_over_a_window_comes_down_on_it()
    {
        var engine = OnFloor(new Random(1), x: 700);
        engine.Grab();
        var world = Windowed(Middle);
        engine.Hold(0.00, 700, 560);
        engine.Hold(0.06, 760, 520);
        engine.Hold(0.12, 820, 480);
        engine.Release(world);
        double clock = 0;

        for (var i = 0; i < 2400 && engine.State != PetMotionState.Landing; i++)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(PetMotionState.Landing, engine.State);
        Assert.Equal(Middle.Id, engine.SupportId);
        Near(Middle.Top, engine.Box(Bodies).Bottom);
    }

    // ------------------------------------------------------------------ living on a window

    [Fact]
    public void A_pet_on_a_window_walks_its_length_and_turns_at_both_ends()
    {
        var engine = OnWindow(Middle, x: 900);
        var world = Windowed(Middle);
        double clock = 100;
        var turns = 0;

        for (var i = 0; i < 20000; i++)
        {
            clock += 0.05;
            var events = engine.Advance(At(clock), 0.05, world);
            turns += events.HasFlag(PetMotionEvents.TurnedAtEdge) ? 1 : 0;

            Assert.InRange(engine.CenterX, Middle.Left + 50, Middle.Right - 50);
            Near(Middle.Top, engine.Box(Bodies).Bottom);
            Assert.Equal(Middle.Id, engine.SupportId);
        }

        Assert.True(turns >= 2, $"only {turns} turns");
    }

    [Fact]
    public void A_window_that_is_dragged_carries_the_pet_with_it()
    {
        var engine = OnWindow(Middle, x: 900);
        var before = engine.CenterX;
        var moved = new PetSurface(Middle.Id, Middle.Left + 120, Middle.Top + 25, Middle.Right + 120);

        engine.Advance(At(50), 0.05, Windowed(moved));

        // The window's own shift, plus at most a step of the pet's own walking.
        Assert.InRange(engine.CenterX, before + 120 - 6, before + 120 + 6);
        Near(moved.Top, engine.Box(Bodies).Bottom);
    }

    [Fact]
    public void A_window_that_is_closed_leaves_the_pet_to_fall()
    {
        var engine = OnWindow(Middle, x: 900);

        engine.Advance(At(50), 0.05, Windowed());

        Assert.Equal(PetMotionState.Falling, engine.State);
        Assert.Equal(0, engine.SupportId);

        double clock = 50;
        for (var i = 0; i < 4000 && engine.State == PetMotionState.Falling; i++)
        {
            Tick(engine, ref clock, Windowed());
        }

        Near(1000, engine.Box(Bodies).Bottom);
    }

    [Fact]
    public void Switching_windows_off_while_standing_on_one_brings_the_pet_down()
    {
        var engine = OnWindow(Middle, x: 900);

        engine.Advance(At(50), 0.05, Windowed(Middle) with { AllowWindows = false });

        Assert.Equal(PetMotionState.Falling, engine.State);
    }

    [Fact]
    public void A_window_that_shrinks_under_the_pet_keeps_it_on_the_window()
    {
        var engine = OnWindow(Middle, x: 1150);
        var shrunk = new PetSurface(Middle.Id, 600, 600, 1000);

        engine.Advance(At(50), 0.05, Windowed(shrunk));

        Assert.InRange(engine.CenterX, 650, 950);
        Assert.Equal(PetMotionState.Walking, engine.State is PetMotionState.Resting ? PetMotionState.Walking : engine.State);
    }

    // ------------------------------------------------------------------ climbing one

    /// <summary>
    /// A pet that has decided to climb a window on the far right of the screen, with the draws spent
    /// so that every choice goes the way the test wants.
    /// </summary>
    private static (PetMotionEngine Engine, PetSurface Window) EagerClimber()
    {
        // From the right-hand edge there is room on the left of this window only, so the side needs no draw.
        var window = new PetSurface(7, 1500, 600, 1900);
        var engine = new PetMotionEngine(new ScriptedRandom(doubles: [0.9], fallback: 0.0));
        engine.Place(At(0), 960, FloorCentre, Windowed(window));
        return (engine, window);
    }

    private static PetMotionContext Eager(PetSurface window) => Windowed(window);

    [Fact]
    public void Previewing_a_window_sends_the_pet_to_climb_it()
    {
        var (engine, window) = EagerClimber();
        var world = Eager(window);

        Assert.True(engine.Demonstrate(PetDemo.Window, world, At(0)));
        double clock = 0;
        for (var i = 0; i < 2000 && engine.State != PetMotionState.WindowClimb; i++)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(PetMotionState.WindowClimb, engine.State);
        Assert.Equal(window.Id, engine.TargetId);
        Assert.Equal(1, engine.WallSide);
        var box = engine.Box(Bodies);
        Near(window.Left, box.Right);
        Near(1000, box.Bottom);
    }

    [Fact]
    public void Climbing_a_window_keeps_the_pets_side_on_the_windows_edge_and_ends_standing_on_it()
    {
        var (engine, window) = EagerClimber();
        var world = Eager(window);
        engine.Demonstrate(PetDemo.Window, world, At(0));
        double clock = 0;
        while (engine.State != PetMotionState.WindowClimb)
        {
            Tick(engine, ref clock, world);
            Assert.True(clock < 200, "never began the climb");
        }

        var start = engine.CenterY;
        Tick(engine, ref clock, world);
        Assert.True(engine.CenterY < start);

        while (engine.State == PetMotionState.WindowClimb)
        {
            Tick(engine, ref clock, world);
            if (engine.State == PetMotionState.WindowClimb)
            {
                Near(window.Left, engine.Box(Bodies).Right);
            }

            Assert.True(clock < 400, "never reached the top");
        }

        Assert.Equal(window.Id, engine.SupportId);
        Assert.Equal(0, engine.TargetId);
        Assert.Equal(PetMotionState.Resting, engine.State);
        Near(window.Top, engine.Box(Bodies).Bottom);
        Assert.InRange(engine.RestDuration, PetMotionEngine.WindowRestMinimumSeconds, PetMotionEngine.WindowRestMaximumSeconds);
        Assert.Equal(1, engine.Direction);
    }

    [Fact]
    public void A_window_that_moves_while_it_is_being_climbed_is_followed()
    {
        var (engine, window) = EagerClimber();
        var world = Eager(window);
        engine.Demonstrate(PetDemo.Window, world, At(0));
        double clock = 0;
        while (engine.State != PetMotionState.WindowClimb)
        {
            Tick(engine, ref clock, world);
        }

        var shifted = new PetSurface(window.Id, window.Left - 150, window.Top, window.Right - 150);
        Tick(engine, ref clock, Eager(shifted));

        Near(shifted.Left, engine.Box(Bodies).Right);
    }

    [Fact]
    public void A_window_that_closes_while_it_is_being_climbed_lets_the_pet_go()
    {
        var (engine, window) = EagerClimber();
        var world = Eager(window);
        engine.Demonstrate(PetDemo.Window, world, At(0));
        double clock = 0;
        while (engine.State != PetMotionState.WindowClimb)
        {
            Tick(engine, ref clock, world);
        }

        Tick(engine, ref clock, Windowed());

        Assert.Equal(PetMotionState.Falling, engine.State);
        Assert.Equal(0, engine.TargetId);
    }

    [Fact]
    public void A_click_on_a_pet_climbing_a_window_lets_it_go()
    {
        var (engine, window) = EagerClimber();
        var world = Eager(window);
        engine.Demonstrate(PetDemo.Window, world, At(0));
        double clock = 0;
        while (engine.State != PetMotionState.WindowClimb)
        {
            Tick(engine, ref clock, world);
        }

        Assert.True(engine.IsClimbing);
        engine.Drop(world);

        Assert.Equal(PetMotionState.Falling, engine.State);
    }

    [Fact]
    public void Without_a_window_worth_climbing_there_is_nothing_to_preview()
    {
        var engine = OnFloor(new Random(1));

        Assert.False(engine.Demonstrate(PetDemo.Window, Windowed(), At(0)));
        Assert.False(engine.Demonstrate(PetDemo.Window, Windowed(new PetSurface(3, 600, 30, 1200)), At(0)));
        Assert.False(engine.Demonstrate(PetDemo.Window, Windowed(new PetSurface(4, 600, 980, 1200)), At(0)));
        Assert.False(engine.Demonstrate(PetDemo.Window, Windowed(new PetSurface(5, 50, 600, 1900)), At(0)));
        Assert.False(engine.Demonstrate(PetDemo.Window, Floor with { Surfaces = [Middle] }, At(0)));
    }

    [Fact]
    public void Left_alone_the_pet_gets_round_to_climbing_a_window_in_the_end()
    {
        var window = new PetSurface(9, 1500, 600, 1900);
        var engine = new PetMotionEngine(new ScriptedRandom(doubles: [0.9], fallback: 0.0));
        var world = Windowed(window);
        engine.Place(At(0), 960, FloorCentre, world);
        double clock = 0;

        for (var i = 0; i < 4000 && engine.State != PetMotionState.WindowClimb; i++)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(PetMotionState.WindowClimb, engine.State);
        Assert.InRange(clock, PetMotionEngine.FirstWindowClimbMinimumDelay, 120);
    }

    [Fact]
    public void A_pet_on_a_window_does_not_start_looking_for_another()
    {
        var engine = OnWindow(Middle, x: 900);
        var world = Windowed(Middle, new PetSurface(8, 100, 400, 500));
        double clock = 100;

        for (var i = 0; i < 4000; i++)
        {
            Tick(engine, ref clock, world);
            Assert.NotEqual(PetMotionState.WindowClimb, engine.State);
            Assert.Equal(0, engine.TargetId);
        }
    }

    [Fact]
    public void Walls_and_windows_do_not_fight_over_the_pet()
    {
        // With a window to go to, an edge of the screen is not a wall to climb on the way there.
        var window = new PetSurface(11, 700, 600, 1100);
        var engine = new PetMotionEngine(new ScriptedRandom(doubles: [0.9], fallback: 0.0));
        var world = Windowed(window) with { AllowWalls = true, AllowCeiling = true };
        engine.Place(At(0), 960, FloorCentre, world);
        engine.Demonstrate(PetDemo.Window, world, At(0));
        Assert.NotEqual(0, engine.TargetId);
        double clock = 0;

        for (var i = 0; i < 2000 && engine.State != PetMotionState.WindowClimb; i++)
        {
            Tick(engine, ref clock, world);
            Assert.NotEqual(PetMotionState.Wall, engine.State);
        }

        Assert.Equal(PetMotionState.WindowClimb, engine.State);
    }
}
