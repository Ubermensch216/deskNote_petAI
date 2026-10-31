namespace DeskNote.Companion.Core.Tests;

/// <summary>How the pet behaves towards the pointer, and the slides and hops that go with it.</summary>
public partial class PetMotionEngineTests
{
    /// <summary>The pointer at (<paramref name="x"/>, <paramref name="y"/>) and the pet reacting to it in <paramref name="mode"/>.</summary>
    private static PetMotionContext Pointer(PetCursorMode mode, double x, double y, bool pressed = false) =>
        Floor with { CursorMode = mode, Cursor = new PetCursor(x, y, pressed) };

    // ------------------------------------------------------------------ dodging

    [Fact]
    public void A_pointer_that_stays_on_the_pet_makes_it_slide_away_from_it()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: -1);
        // The pointer is on the pet's left shoulder.
        var world = Pointer(PetCursorMode.Dodge, 940, FloorCentre);
        double clock = 0;

        Tick(engine, ref clock, world);
        Assert.NotEqual(PetMotionState.Dashing, engine.State);

        for (var i = 0; i < 6 && engine.State != PetMotionState.Dashing; i++)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(PetMotionState.Dashing, engine.State);
        Assert.Equal(1, engine.Direction);
    }

    [Fact]
    public void A_pointer_just_passing_over_does_not_set_it_off()
    {
        var engine = OnFloor(new Random(1));
        double clock = 0;

        // On the pet for a tick, then gone: well under the delay.
        Tick(engine, ref clock, Pointer(PetCursorMode.Dodge, 960, FloorCentre));
        Tick(engine, ref clock, Pointer(PetCursorMode.Dodge, 300, FloorCentre));
        Tick(engine, ref clock, Pointer(PetCursorMode.Dodge, 960, FloorCentre));

        Assert.NotEqual(PetMotionState.Dashing, engine.State);
    }

    [Fact]
    public void A_pet_that_is_being_clicked_does_not_run_away_from_the_click()
    {
        var engine = OnFloor(new Random(1));
        var world = Pointer(PetCursorMode.Dodge, 960, FloorCentre, pressed: true);
        double clock = 0;

        for (var i = 0; i < 40; i++)
        {
            Tick(engine, ref clock, world);
            Assert.NotEqual(PetMotionState.Dashing, engine.State);
        }
    }

    [Fact]
    public void A_slide_is_much_faster_than_a_walk_and_lasts_under_a_second()
    {
        var engine = OnFloor(new Random(1), x: 700);
        var world = Pointer(PetCursorMode.Dodge, 690, FloorCentre);
        double clock = 0;
        while (engine.State != PetMotionState.Dashing)
        {
            Tick(engine, ref clock, world);
        }

        var start = engine.CenterX;
        var seen = 0.0;
        var away = Pointer(PetCursorMode.None, 0, 0);
        while (engine.State == PetMotionState.Dashing)
        {
            Tick(engine, ref clock, away);
            seen += 0.05;
        }

        Assert.InRange(seen, PetMotionEngine.DashMinimumSeconds - 0.06, PetMotionEngine.DashMinimumSeconds + PetMotionEngine.DashVariationSeconds + 0.06);
        Assert.True(Math.Abs(engine.CenterX - start) > 100 * PetMotionEngine.DashSpeedFactor * 0.5, $"only slid {engine.CenterX - start}");
        Assert.Equal(PetMotionState.Walking, engine.State);
    }

    [Fact]
    public void The_pet_cannot_be_chased_into_a_slide_twice_in_a_row()
    {
        var engine = OnFloor(new Random(1), x: 700);
        var world = Pointer(PetCursorMode.Dodge, 690, FloorCentre);
        double clock = 0;
        while (engine.State != PetMotionState.Dashing)
        {
            Tick(engine, ref clock, world);
        }

        var firstAt = clock;
        while (engine.State == PetMotionState.Dashing)
        {
            Tick(engine, ref clock, Pointer(PetCursorMode.Dodge, 0, 0));
        }

        // The pointer is back on it at once, but the cooldown has not run out.
        var secondAt = double.MaxValue;
        for (var i = 0; i < 200; i++)
        {
            Tick(engine, ref clock, Pointer(PetCursorMode.Dodge, engine.CenterX, FloorCentre));
            if (engine.State == PetMotionState.Dashing)
            {
                secondAt = clock;
                break;
            }
        }

        Assert.True(secondAt - firstAt >= PetMotionEngine.DodgeCooldownSeconds - 0.1, $"slid again after {secondAt - firstAt}");
    }

    [Fact]
    public void A_pet_will_not_be_chased_into_a_corner_it_cannot_get_out_of()
    {
        var engine = OnFloor(new Random(1), x: 1800, direction: 1);
        // The pointer is on the pet's left, so "away" is towards the edge; with the edge that close
        // the pet runs the other way, back under the pointer, instead of into the corner.
        var world = Pointer(PetCursorMode.Dodge, 1790, FloorCentre);
        double clock = 0;

        for (var i = 0; i < 12 && engine.State != PetMotionState.Dashing; i++)
        {
            Tick(engine, ref clock, world);
        }

        Assert.Equal(PetMotionState.Dashing, engine.State);
        Assert.Equal(-1, engine.Direction);
    }

    [Fact]
    public void A_slide_never_leaves_the_floor()
    {
        var engine = OnFloor(new Random(1), x: 1800, direction: 1);
        var world = Pointer(PetCursorMode.Dodge, 1790, FloorCentre);
        double clock = 0;

        for (var i = 0; i < 400; i++)
        {
            Tick(engine, ref clock, world);
            Assert.InRange(engine.CenterX, 50, 1870);
            Near(FloorCentre, engine.CenterY);
        }
    }

    [Fact]
    public void A_pet_without_the_dodge_mode_never_slides()
    {
        foreach (var mode in new[] { PetCursorMode.None, PetCursorMode.Watch, PetCursorMode.Follow })
        {
            var engine = OnFloor(new Random(1));
            var world = Pointer(mode, 960, FloorCentre);
            double clock = 0;

            for (var i = 0; i < 100; i++)
            {
                Tick(engine, ref clock, world);
                Assert.NotEqual(PetMotionState.Dashing, engine.State);
            }
        }
    }

    // ------------------------------------------------------------------ watching

    [Fact]
    public void A_sitting_pet_turns_to_look_at_a_pointer_near_it()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: 1);
        engine.BeginRest(At(0), 5, listless: false, seconds: 30, pose: PetRestPose.Sit);
        var world = Pointer(PetCursorMode.Watch, 700, 700);
        double clock = 0;

        Tick(engine, ref clock, world);

        Assert.Equal(-1, engine.Direction);
        Assert.Equal(PetMotionState.Resting, engine.State);
    }

    [Fact]
    public void A_sleeping_pet_does_not_turn_its_head()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: 1);
        engine.BeginRest(At(0), 5, listless: false, seconds: 30, pose: PetRestPose.Sleep);
        double clock = 0;

        Tick(engine, ref clock, Pointer(PetCursorMode.Watch, 700, 700));

        Assert.Equal(1, engine.Direction);
    }

    [Fact]
    public void A_pointer_far_away_is_not_looked_at()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: 1);
        engine.BeginRest(At(0), 5, listless: false, seconds: 30, pose: PetRestPose.Sit);
        double clock = 0;

        Tick(engine, ref clock, Pointer(PetCursorMode.Watch, 100, 700));
        Tick(engine, ref clock, Pointer(PetCursorMode.Watch, 900, 10));

        Assert.Equal(1, engine.Direction);
    }

    [Fact]
    public void Without_a_pointer_to_watch_nothing_changes()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: 1);
        engine.BeginRest(At(0), 5, listless: false, seconds: 30, pose: PetRestPose.Sit);
        double clock = 0;

        Tick(engine, ref clock, Floor with { CursorMode = PetCursorMode.Watch, Cursor = null });
        Tick(engine, ref clock, Pointer(PetCursorMode.None, 700, 700));

        Assert.Equal(1, engine.Direction);
    }

    // ------------------------------------------------------------------ following

    [Fact]
    public void A_following_pet_heads_for_a_pointer_on_its_level()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: -1);
        double clock = 0;

        Tick(engine, ref clock, Pointer(PetCursorMode.Follow, 1500, FloorCentre - 20));

        Assert.Equal(1, engine.Direction);
    }

    [Fact]
    public void A_following_pet_ignores_a_pointer_high_above_it()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: -1);
        double clock = 0;

        Tick(engine, ref clock, Pointer(PetCursorMode.Follow, 1500, 100));

        Assert.Equal(-1, engine.Direction);
    }

    [Fact]
    public void A_following_pet_that_reaches_the_pointer_sits_beside_it_for_a_moment()
    {
        var engine = OnFloor(new Random(1), x: 960, direction: 1);
        double clock = 0;

        for (var i = 0; i < 400 && engine.State != PetMotionState.Resting; i++)
        {
            // The pointer stays where it is, on the pet's level.
            Tick(engine, ref clock, Pointer(PetCursorMode.Follow, 1200, FloorCentre));
        }

        Assert.Equal(PetMotionState.Resting, engine.State);
        Assert.InRange(engine.CenterX, 1200 - 100, 1200 + 100);
        Near(PetMotionEngine.FollowRestSeconds, engine.RestDuration);
        Assert.Equal(PetRestPose.Sit, engine.RestPose);
    }

    [Fact]
    public void A_pet_that_is_about_to_climb_a_window_is_not_distracted_by_the_pointer()
    {
        var window = new PetSurface(7, 1500, 600, 1900);
        var engine = new PetMotionEngine(new ScriptedRandom(doubles: [0.9], fallback: 0.0));
        var world = Windowed(window) with { CursorMode = PetCursorMode.Dodge, Cursor = new PetCursor(960, FloorCentre, false) };
        engine.Place(At(0), 960, FloorCentre, world);
        engine.Demonstrate(PetDemo.Window, world, At(0));
        double clock = 0;

        for (var i = 0; i < 20; i++)
        {
            Tick(engine, ref clock, world);
        }

        Assert.NotEqual(PetMotionState.Dashing, engine.State);
    }

    // ------------------------------------------------------------------ previews

    [Fact]
    public void Previewing_a_slide_slides_the_pet_the_way_it_faces()
    {
        var engine = OnFloor(new Random(1), x: 700, direction: 1);

        Assert.True(engine.Demonstrate(PetDemo.Dash, Floor, At(0)));

        Assert.Equal(PetMotionState.Dashing, engine.State);
        Assert.Equal(1, engine.Direction);
    }

    [Fact]
    public void A_hop_goes_up_and_comes_back_down_in_under_a_second()
    {
        var engine = OnFloor(new Random(1));
        engine.Demonstrate(PetDemo.Hop, Floor, At(0));
        Assert.Equal(PetMotionState.Hopping, engine.State);
        Near(0, engine.HopHeight);

        double clock = 0;
        var peak = 0.0;
        while (engine.State == PetMotionState.Hopping)
        {
            Tick(engine, ref clock, Floor);
            peak = Math.Max(peak, engine.HopHeight);
            Assert.True(clock < 2, "hop never ended");
        }

        Assert.InRange(peak, Bodies.FloorHeight * PetMotionEngine.HopHeightFraction * 0.8, (Bodies.FloorHeight * PetMotionEngine.HopHeightFraction) + 0.01);
        Assert.Equal(PetMotionState.Walking, engine.State);
        Near(0, engine.HopHeight);
        Near(FloorCentre, engine.CenterY);
    }

    [Fact]
    public void A_rest_asked_for_in_the_middle_of_a_slide_or_hop_takes_over_at_once()
    {
        var engine = OnFloor(new Random(1));
        engine.Demonstrate(PetDemo.Dash, Floor, At(0));

        var started = engine.BeginRest(At(0.1), 5, listless: false, seconds: 2.5, pose: PetRestPose.Sit);

        Assert.True(started);
        Assert.Equal(PetMotionState.Resting, engine.State);
    }
}
