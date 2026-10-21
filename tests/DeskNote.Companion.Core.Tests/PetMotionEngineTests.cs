namespace DeskNote.Companion.Core.Tests;

public partial class PetMotionEngineTests
{
    // A 1920 x 1080 screen, the floor at 1000: a pet standing 100 wide and 120 tall, 130 wide and
    // 100 tall lying on a wall, 120 wide and 90 tall on the ceiling.
    private static readonly PetArea Screen = new(0, 0, 1920, 1000);

    private static readonly PetBodies Bodies = new(
        FloorWidth: 100,
        FloorHeight: 120,
        WallWidth: 110,
        WallHeight: 130,
        CeilingWidth: 130,
        CeilingHeight: 90);

    private static readonly PetMotionContext Floor = new(
        Screen,
        Bodies,
        Speed: 100,
        BaseRestSeconds: 5,
        Listless: false);

    private static readonly PetMotionContext Climber = Floor with { AllowWalls = true, AllowCeiling = true };

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    private static double FloorCentre => 1000 - 60;

    /// <summary>
    /// A random source for a pet that is placed and then left to walk: the first draw is spent on
    /// planning its first walk leg, so the draws given here start at the first decision a test is
    /// about.
    /// </summary>
    private static ScriptedRandom Script(params double[] draws) => new(doubles: [0.9, .. draws]);

    /// <summary>A pet standing at <paramref name="x"/>, already walking.</summary>
    private static PetMotionEngine OnFloor(Random random, double x = 960, int direction = 1)
    {
        var engine = new PetMotionEngine(random) { Direction = direction };
        engine.Place(At(0), x, FloorCentre, Floor);
        return engine;
    }

    private static PetMotionEvents Tick(
        PetMotionEngine engine,
        ref double clock,
        in PetMotionContext world,
        double step = 0.05)
    {
        clock += step;
        return engine.Advance(At(clock), step, world);
    }

    private static void Near(double expected, double actual, int precision = 6) =>
        Assert.Equal(expected, actual, precision);

    // ------------------------------------------------------------------ walking and resting

    [Fact]
    public void Walking_moves_along_the_direction_it_faces_at_the_given_speed()
    {
        var engine = OnFloor(new Random(1));

        var events = engine.Advance(At(0.05), 0.5, Floor);

        Near(1010, engine.CenterX);
        Assert.Equal(PetMotionEvents.Moved, events);
        Assert.Equal(PetMotionState.Walking, engine.State);
    }

    [Fact]
    public void The_floor_is_where_the_bottom_of_the_pets_box_is()
    {
        var engine = OnFloor(new Random(1));

        engine.Advance(At(0.05), 0.05, Floor);

        Near(FloorCentre, engine.CenterY);
        Near(1000, engine.Box(Bodies).Bottom);
    }

    [Fact]
    public void Reaching_the_left_edge_clamps_turns_round_and_sits_down()
    {
        var engine = OnFloor(new Random(1), x: 60, direction: -1);

        var events = engine.Advance(At(0.5), 0.5, Floor);

        Near(50, engine.CenterX);
        Assert.Equal(1, engine.Direction);
        Assert.Equal(PetMotionState.Resting, engine.State);
        Assert.True(events.HasFlag(PetMotionEvents.TurnedAtEdge));
        Assert.True(events.HasFlag(PetMotionEvents.BeganRest));
    }

    [Fact]
    public void Reaching_the_right_edge_clamps_turns_round_and_sits_down()
    {
        var engine = OnFloor(new Random(1), x: 1860, direction: 1);

        var events = engine.Advance(At(0.5), 0.5, Floor);

        Near(1870, engine.CenterX);
        Assert.Equal(-1, engine.Direction);
        Assert.Equal(PetMotionState.Resting, engine.State);
        Assert.True(events.HasFlag(PetMotionEvents.TurnedAtEdge));
    }

    [Fact]
    public void A_walk_leg_ends_with_a_rest_even_in_the_middle_of_the_floor()
    {
        // The pendulum bug: resting only at the edges. With the edges out of reach, the pet must
        // still sit down once its leg is up, and well inside the 3.2 to 9.5 second window.
        var engine = OnFloor(new Random(3));
        var slow = Floor with { Speed = 1 };
        double clock = 0;

        for (var i = 0; i < 200; i++)
        {
            var events = Tick(engine, ref clock, slow);
            if (events.HasFlag(PetMotionEvents.BeganRest))
            {
                Assert.InRange(clock, PetMotionEngine.WalkLegMinimumSeconds, PetMotionEngine.WalkLegMaximumSeconds + 0.1);
                Assert.False(events.HasFlag(PetMotionEvents.TurnedAtEdge));
                Assert.InRange(engine.CenterX, 960, 980);
                return;
            }
        }

        Assert.Fail("the pet never sat down");
    }

    [Fact]
    public void A_rest_ends_and_the_pet_gets_up_and_walks_in_the_same_tick()
    {
        var engine = OnFloor(new Random(1));
        engine.BeginRest(At(0), baseRestSeconds: 5, listless: false, seconds: 2, pose: PetRestPose.Sit);

        var during = engine.Advance(At(1), 1, Floor);
        Assert.Equal(PetMotionEvents.Resting, during);
        Near(960, engine.CenterX);
        Near(1, engine.RestElapsed);

        var after = engine.Advance(At(2), 0.1, Floor);
        Assert.True(after.HasFlag(PetMotionEvents.BeganWalking));
        Assert.True(after.HasFlag(PetMotionEvents.Moved));
        Assert.Equal(PetMotionState.Walking, engine.State);
        Near(970, engine.CenterX);
    }

    [Fact]
    public void Getting_up_leaves_the_last_pose_in_place_for_the_next_roll_to_avoid()
    {
        var engine = OnFloor(new Random(1));
        engine.BeginRest(At(0), 5, listless: false, seconds: 1, pose: PetRestPose.Stretch);

        engine.Advance(At(1), 0.05, Floor);

        Assert.Equal(PetMotionState.Walking, engine.State);
        Assert.Equal(PetRestPose.Stretch, engine.RestPose);
    }

    [Theory]
    [InlineData(PetRestPose.Sit, 5.0)]
    [InlineData(PetRestPose.Daydream, 7.5)]
    [InlineData(PetRestPose.Stretch, 3.5)]
    [InlineData(PetRestPose.Sleep, 11.0)]
    public void Each_pose_holds_for_its_share_of_the_species_rest(PetRestPose pose, double expected)
    {
        var engine = OnFloor(new Random(1));

        engine.BeginRest(At(0), baseRestSeconds: 5, listless: false, pose: pose);

        Near(expected, engine.RestDuration);
        Assert.Equal(pose, engine.RestPose);
        Assert.Equal(0, engine.RestElapsed);
    }

    [Fact]
    public void An_explicit_length_overrides_the_pose_factor()
    {
        var engine = OnFloor(new Random(1));

        engine.BeginRest(At(0), baseRestSeconds: 5, listless: false, seconds: 2.6, pose: PetRestPose.Sleep);

        Near(2.6, engine.RestDuration);
    }

    [Theory]
    [InlineData(10, false, PetRestPose.Sit)]
    [InlineData(10, true, PetRestPose.Sit)]
    [InlineData(30, false, PetRestPose.Sit)]
    [InlineData(30, true, PetRestPose.Daydream)]
    [InlineData(57, false, PetRestPose.Daydream)]
    [InlineData(59, true, PetRestPose.Daydream)]
    [InlineData(60, true, PetRestPose.Sleep)]
    [InlineData(70, false, PetRestPose.Sleep)]
    [InlineData(85, false, PetRestPose.Stretch)]
    [InlineData(85, true, PetRestPose.Sleep)]
    [InlineData(95, true, PetRestPose.Stretch)]
    public void A_low_mood_moves_the_odds_toward_daydreaming_and_away_from_stretching(
        int roll,
        bool listless,
        PetRestPose expected)
    {
        // The engine starts out having last sat, so a Sit roll is re-rolled once. Feeding the same
        // roll twice isolates the table itself from the no-repeat rule below.
        var engine = OnFloor(new ScriptedRandom(doubles: [0.9], rolls: [roll, roll]));

        engine.BeginRest(At(0), 5, listless);

        Assert.Equal(expected, engine.RestPose);
    }

    [Fact]
    public void The_same_pose_twice_running_is_re_rolled_once()
    {
        // Rolls 10 (Sit, the pose it last held) then 70 (Sleep): the repeat is discarded.
        var engine = OnFloor(new ScriptedRandom(doubles: [0.9], rolls: [10, 70]));

        engine.BeginRest(At(0), 5, listless: false);

        Assert.Equal(PetRestPose.Sleep, engine.RestPose);
    }

    [Fact]
    public void The_re_roll_is_a_single_second_chance_not_a_guarantee()
    {
        // Both rolls land on Sit. Re-rolling forever would bend the odds; once is the contract.
        var engine = OnFloor(new ScriptedRandom(doubles: [0.9], rolls: [10, 12]));

        engine.BeginRest(At(0), 5, listless: false);

        Assert.Equal(PetRestPose.Sit, engine.RestPose);
    }

    [Fact]
    public void Every_pose_turns_up_over_a_long_day()
    {
        var engine = OnFloor(new Random(7));
        var seen = new HashSet<PetRestPose>();

        for (var i = 0; i < 400; i++)
        {
            engine.BeginWalking(At(i));
            engine.BeginRest(At(i), 5, listless: true);
            seen.Add(engine.RestPose);
        }

        // Listless pets are nudged, not locked: even the pose they are steered away from stays.
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void The_same_seed_and_ticks_give_the_same_walk()
    {
        var first = OnFloor(new Random(42));
        var second = OnFloor(new Random(42));
        double clock = 0;

        for (var i = 1; i <= 2400; i++)
        {
            var a = first.Advance(At(i * 0.05), 0.05, Climber);
            var b = second.Advance(At(i * 0.05), 0.05, Climber);
            clock = i * 0.05;

            Assert.Equal(a, b);
            Assert.Equal(first.CenterX, second.CenterX);
            Assert.Equal(first.CenterY, second.CenterY);
            Assert.Equal(first.State, second.State);
            Assert.Equal(first.RestPose, second.RestPose);
        }

        Assert.True(clock > 100);
    }

    [Fact]
    public void A_pet_that_cannot_climb_never_leaves_the_floor_however_long_it_walks()
    {
        var engine = OnFloor(new Random(5), direction: -1);
        var edges = 0;

        for (var i = 1; i <= 24000; i++)
        {
            var events = engine.Advance(At(i * 0.05), 0.05, Floor);
            edges += events.HasFlag(PetMotionEvents.TurnedAtEdge) ? 1 : 0;

            Assert.InRange(engine.CenterX, 50, 1870);
            Near(FloorCentre, engine.CenterY);
            Assert.True(engine.IsOnFloor, engine.State.ToString());
        }

        // Twenty minutes at 100 units a second on a 1920-unit floor has to reach an edge.
        Assert.True(edges > 0);
    }

    // ------------------------------------------------------------------ walls and ceiling

    [Fact]
    public void Reaching_a_screen_edge_can_start_a_climb_instead_of_a_turn()
    {
        // A scripted draw of 0 is below WallChance, so the pet takes the wall.
        var engine = OnFloor(Script(0.0), x: 1860);

        var events = engine.Advance(At(0.5), 0.5, Climber);

        Assert.Equal(PetMotionState.Wall, engine.State);
        Assert.Equal(1, engine.WallSide);
        Assert.False(engine.Descending);
        Assert.True(events.HasFlag(PetMotionEvents.StateChanged));
        Assert.False(events.HasFlag(PetMotionEvents.BeganRest));

        // Pressed against the right edge, standing on the floor to start with.
        var box = engine.Box(Bodies);
        Near(1920, box.Right);
        Near(1000, box.Bottom);
    }

    [Fact]
    public void A_roll_above_the_chance_turns_and_rests_as_before()
    {
        var engine = OnFloor(Script(0.99), x: 1860);

        engine.Advance(At(0.5), 0.5, Climber);

        Assert.Equal(PetMotionState.Resting, engine.State);
        Assert.Equal(-1, engine.Direction);
    }

    [Fact]
    public void An_edge_with_another_display_beside_it_is_never_a_wall()
    {
        var shared = Climber with { Area = Screen with { RightIsOuter = false } };
        var engine = OnFloor(Script(0.0), x: 1860);

        engine.Advance(At(0.5), 0.5, shared);

        Assert.Equal(PetMotionState.Resting, engine.State);
    }

    [Fact]
    public void Walls_switched_off_means_no_climbing()
    {
        var engine = OnFloor(Script(0.0), x: 1860);

        engine.Advance(At(0.5), 0.5, Floor);

        Assert.Equal(PetMotionState.Resting, engine.State);
    }

    [Fact]
    public void Climbing_keeps_the_pet_against_the_wall_and_moves_it_up_faster_than_it_walks()
    {
        var engine = OnFloor(Script(0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);
        var before = engine.CenterY;

        engine.Advance(At(1.5), 1, Climber);

        Near(before - (100 * PetMotionEngine.WallSpeedFactor), engine.CenterY);
        Near(1920, engine.Box(Bodies).Right);
    }

    [Fact]
    public void Reaching_the_top_of_a_wall_crawls_onto_the_ceiling_away_from_the_corner()
    {
        var engine = OnFloor(Script(0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);
        double clock = 0.5;

        for (var i = 0; i < 400 && engine.State == PetMotionState.Wall; i++)
        {
            Tick(engine, ref clock, Climber);
        }

        Assert.Equal(PetMotionState.Ceiling, engine.State);
        Assert.Equal(-1, engine.Direction);
        var box = engine.Box(Bodies);
        Near(0, box.Top);
        Near(1920, box.Right);
    }

    [Fact]
    public void Without_a_ceiling_the_top_of_the_wall_is_a_cliff()
    {
        var noCeiling = Climber with { AllowCeiling = false };
        var engine = OnFloor(Script(0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, noCeiling);
        double clock = 0.5;

        for (var i = 0; i < 400 && engine.State == PetMotionState.Wall; i++)
        {
            Tick(engine, ref clock, noCeiling);
        }

        Assert.Equal(PetMotionState.Falling, engine.State);
    }

    [Fact]
    public void The_ceiling_runs_to_the_far_corner_and_comes_down_the_other_wall_head_first()
    {
        var engine = OnFloor(Script(0.0, 0.99), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);
        double clock = 0.5;

        var seenCeiling = false;
        for (var i = 0; i < 4000; i++)
        {
            Tick(engine, ref clock, Climber);
            seenCeiling |= engine.State == PetMotionState.Ceiling;
            if (seenCeiling && engine.State == PetMotionState.Wall)
            {
                break;
            }
        }

        Assert.Equal(PetMotionState.Wall, engine.State);
        Assert.True(engine.Descending);
        Assert.Equal(-1, engine.WallSide);
        var box = engine.Box(Bodies);
        Near(0, box.Left);
        Near(0, box.Top);
    }

    [Fact]
    public void Coming_down_a_wall_lands_on_the_floor_and_walks_away_from_it()
    {
        var engine = OnFloor(Script(0.0, 0.99), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);
        double clock = 0.5;

        var landed = false;
        for (var i = 0; i < 8000 && !landed; i++)
        {
            Tick(engine, ref clock, Climber);
            landed = engine.State == PetMotionState.Landing;
        }

        Assert.True(landed, "the pet never made it back down");
        Assert.Equal(1, engine.Direction);
        var box = engine.Box(Bodies);
        Near(0, box.Left);
        Near(1000, box.Bottom);

        for (var i = 0; i < 20; i++)
        {
            Tick(engine, ref clock, Climber);
        }

        Assert.Equal(PetMotionState.Walking, engine.State);
    }

    [Fact]
    public void A_pet_on_the_ceiling_eventually_lets_go()
    {
        // 0.0 climbs the wall; every later draw is 0.0 too, so the drop chance is always met once
        // the hold time is up, and never before it.
        var engine = OnFloor(new ScriptedRandom(doubles: [0.9], fallback: 0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);
        double clock = 0.5;
        var ceilingSince = -1.0;

        for (var i = 0; i < 4000; i++)
        {
            Tick(engine, ref clock, Climber);
            if (ceilingSince < 0 && engine.State == PetMotionState.Ceiling)
            {
                ceilingSince = clock;
            }

            if (engine.State == PetMotionState.Falling)
            {
                break;
            }
        }

        Assert.Equal(PetMotionState.Falling, engine.State);
        Assert.True(ceilingSince > 0);
        Assert.True(clock - ceilingSince >= PetMotionEngine.CeilingHoldSeconds - 0.1);
    }

    [Fact]
    public void Switching_walls_off_while_climbing_sends_the_pet_down()
    {
        var engine = OnFloor(Script(0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);

        engine.Advance(At(1), 0.5, Floor);

        Assert.Equal(PetMotionState.Falling, engine.State);
    }

    [Fact]
    public void Switching_the_ceiling_off_while_on_it_sends_the_pet_down()
    {
        var engine = OnFloor(Script(0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);
        double clock = 0.5;
        while (engine.State == PetMotionState.Wall)
        {
            Tick(engine, ref clock, Climber);
        }

        Tick(engine, ref clock, Climber with { AllowCeiling = false });

        Assert.Equal(PetMotionState.Falling, engine.State);
    }

    // ------------------------------------------------------------------ falling and landing

    [Fact]
    public void A_pet_dropped_from_a_wall_sinks_on_its_rotor_and_lands_gently()
    {
        var engine = OnFloor(Script(0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);
        double clock = 0.5;
        for (var i = 0; i < 40; i++)
        {
            Tick(engine, ref clock, Climber);
        }

        engine.Drop(Climber);
        Assert.Equal(PetMotionState.Falling, engine.State);

        var previous = engine.CenterY;
        var fastest = 0.0;
        for (var i = 0; i < 4000 && engine.State == PetMotionState.Falling; i++)
        {
            Tick(engine, ref clock, Climber);
            fastest = Math.Max(fastest, (engine.CenterY - previous) / 0.05);
            previous = engine.CenterY;
        }

        Assert.Equal(PetMotionState.Landing, engine.State);
        Near(FloorCentre, engine.CenterY);
        Assert.True(fastest <= PetMotionEngine.FallMaximumSpeed + 0.5, $"fell at {fastest}");
    }

    [Fact]
    public void Landing_settles_for_a_moment_and_then_the_pet_walks_on()
    {
        var engine = new PetMotionEngine(new Random(1));
        engine.Place(At(0), 500, 100, Floor);
        Assert.Equal(PetMotionState.Falling, engine.State);
        double clock = 0;

        while (engine.State == PetMotionState.Falling)
        {
            Tick(engine, ref clock, Floor);
        }

        Assert.Equal(PetMotionState.Landing, engine.State);
        Near(0, engine.LandingProgress);

        Tick(engine, ref clock, Floor, step: 0.15);
        Assert.InRange(engine.LandingProgress, 0.4, 0.6);

        Tick(engine, ref clock, Floor, step: 0.2);
        Assert.Equal(PetMotionState.Walking, engine.State);
    }

    [Fact]
    public void A_rest_asked_for_in_the_air_is_taken_after_landing()
    {
        var engine = new PetMotionEngine(new Random(1));
        engine.Place(At(0), 500, 100, Floor);

        var started = engine.BeginRest(At(0), 5, listless: false, seconds: 2.5, pose: PetRestPose.Sleep);
        Assert.False(started);
        Assert.Equal(PetMotionState.Falling, engine.State);

        double clock = 0;
        while (engine.State != PetMotionState.Resting)
        {
            Tick(engine, ref clock, Floor);
            Assert.True(clock < 120, "never rested");
        }

        Assert.Equal(PetRestPose.Sleep, engine.RestPose);
        Near(2.5, engine.RestDuration);
    }

    [Fact]
    public void Placing_the_pet_on_the_floor_starts_it_walking_and_above_it_starts_a_fall()
    {
        var onFloor = new PetMotionEngine(new Random(1));
        onFloor.Place(At(0), 500, FloorCentre, Floor);
        Assert.Equal(PetMotionState.Walking, onFloor.State);

        var below = new PetMotionEngine(new Random(1));
        below.Place(At(0), 500, 2000, Floor);
        Assert.Equal(PetMotionState.Walking, below.State);
        Near(FloorCentre, below.CenterY);

        var inAir = new PetMotionEngine(new Random(1));
        inAir.Place(At(0), 500, 300, Floor);
        Assert.Equal(PetMotionState.Falling, inAir.State);
    }

    [Fact]
    public void Dropping_does_nothing_to_a_pet_that_is_not_climbing()
    {
        var engine = OnFloor(new Random(1));

        engine.Drop(Floor);

        Assert.Equal(PetMotionState.Walking, engine.State);
    }

    // ------------------------------------------------------------------ being picked up

    [Fact]
    public void A_pet_in_the_hand_goes_where_the_pointer_goes_and_stays_there()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();

        engine.Hold(0.00, 400, 500);
        engine.Hold(0.05, 420, 480);
        var events = engine.Advance(At(0.1), 0.1, Floor);

        Assert.Equal(PetMotionState.Held, engine.State);
        Assert.Equal(PetMotionEvents.None, events);
        Near(420, engine.CenterX);
        Near(480, engine.CenterY);
    }

    [Fact]
    public void Letting_go_gently_drops_the_pet_and_does_not_throw_it()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();
        engine.Hold(0.00, 400, 500);
        engine.Hold(0.05, 402, 501);

        var thrown = engine.Release(Floor);

        Assert.False(thrown);
        Assert.Equal(PetMotionState.Falling, engine.State);
    }

    [Fact]
    public void Letting_go_of_a_fast_swing_throws_the_pet_the_way_the_hand_was_going()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();
        engine.Hold(0.00, 400, 500);
        engine.Hold(0.06, 460, 500);
        engine.Hold(0.12, 520, 500);

        var thrown = engine.Release(Floor);

        Assert.True(thrown);
        Assert.Equal(PetMotionState.Thrown, engine.State);
        Assert.Equal(1, engine.Direction);
        Assert.True(engine.VelocityX > 0);
    }

    [Fact]
    public void A_swing_that_stopped_before_the_pointer_let_go_is_not_a_throw()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();
        engine.Hold(0.00, 400, 500);
        engine.Hold(0.05, 700, 500);

        // Held still for a quarter of a second: the old samples are outside the window.
        engine.Hold(0.30, 701, 500);
        engine.Hold(0.33, 701, 500);
        var thrown = engine.Release(Floor);

        Assert.False(thrown);
    }

    [Fact]
    public void A_throw_is_never_faster_than_the_limit()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();
        engine.Hold(0.000, 100, 500);
        engine.Hold(0.010, 1500, 500);

        engine.Release(Floor);

        Assert.True(Math.Abs(engine.VelocityX) <= PetMotionEngine.ThrowMaximumSpeed);
    }

    [Fact]
    public void A_thrown_pet_flies_then_opens_its_rotor_and_comes_down_inside_the_screen()
    {
        var engine = OnFloor(new Random(1), x: 900);
        engine.Grab();
        engine.Hold(0.00, 900, 700);
        engine.Hold(0.06, 1000, 650);
        engine.Hold(0.12, 1100, 600);
        engine.Release(Floor);
        double clock = 0;
        var sawFalling = false;

        for (var i = 0; i < 2400 && engine.State != PetMotionState.Landing; i++)
        {
            Tick(engine, ref clock, Floor);
            sawFalling |= engine.State == PetMotionState.Falling;

            Assert.InRange(engine.CenterX, 50, 1870);
            Assert.InRange(engine.CenterY, 60, FloorCentre);
        }

        Assert.True(sawFalling);
        Assert.Equal(PetMotionState.Landing, engine.State);
        Near(FloorCentre, engine.CenterY);
    }

    [Fact]
    public void A_toss_from_the_floor_leaves_the_floor_before_the_rotor_can_open()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();
        engine.Hold(0.00, 900, FloorCentre);
        engine.Hold(0.06, 1000, FloorCentre);
        engine.Hold(0.12, 1100, FloorCentre);

        engine.Release(Floor);
        engine.Advance(At(0.05), 0.05, Floor);

        Assert.Equal(PetMotionState.Thrown, engine.State);
        Assert.True(engine.CenterY < FloorCentre);
    }

    [Fact]
    public void A_throw_into_the_wall_bounces_off_it_and_stays_on_the_screen()
    {
        var engine = OnFloor(new Random(1), x: 1700);
        engine.Grab();
        engine.Hold(0.00, 1700, 500);
        engine.Hold(0.06, 1800, 500);
        engine.Hold(0.12, 1900, 500);
        engine.Release(Floor);
        double clock = 0;

        for (var i = 0; i < 2400 && engine.State != PetMotionState.Landing; i++)
        {
            Tick(engine, ref clock, Floor);
            Assert.InRange(engine.CenterX, 50, 1870);
        }

        Assert.Equal(PetMotionState.Landing, engine.State);
    }

    [Fact]
    public void Catching_a_flying_pet_clears_the_old_throw()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();
        engine.Hold(0.00, 400, 500);
        engine.Hold(0.12, 700, 500);
        engine.Release(Floor);

        engine.Grab();

        Assert.Equal(PetMotionState.Held, engine.State);
        Assert.Equal(0, engine.VelocityX);
    }

    [Fact]
    public void Grabbing_a_climbing_pet_takes_it_off_the_wall()
    {
        var engine = OnFloor(Script(0.0), x: 1860);
        engine.Advance(At(0.5), 0.5, Climber);

        engine.Grab();

        Assert.Equal(PetMotionState.Held, engine.State);
        Assert.False(engine.IsClimbing);
    }

    // ------------------------------------------------------------------ preview actions

    [Fact]
    public void Previewing_a_wall_starts_a_climb_on_the_right_hand_edge_when_it_is_free()
    {
        var engine = OnFloor(new Random(1), x: 300);

        var started = engine.Demonstrate(PetDemo.Wall, Climber);

        Assert.True(started);
        Assert.Equal(PetMotionState.Wall, engine.State);
        Assert.Equal(1, engine.WallSide);
        Near(1920, engine.Box(Bodies).Right);
    }

    [Fact]
    public void Previewing_a_wall_falls_back_to_the_left_edge_and_then_to_nothing()
    {
        var onlyLeft = Climber with { Area = Screen with { RightIsOuter = false } };
        var engine = OnFloor(new Random(1), x: 300);
        Assert.True(engine.Demonstrate(PetDemo.Wall, onlyLeft));
        Assert.Equal(-1, engine.WallSide);

        var neither = Climber with { Area = Screen with { RightIsOuter = false, LeftIsOuter = false } };
        var other = OnFloor(new Random(1), x: 300);
        Assert.False(other.Demonstrate(PetDemo.Wall, neither));
        Assert.Equal(PetMotionState.Walking, other.State);
    }

    [Fact]
    public void Previewing_the_ceiling_puts_the_pet_on_it_where_it_stands_heading_for_the_far_end()
    {
        var engine = OnFloor(new Random(1), x: 400);

        Assert.True(engine.Demonstrate(PetDemo.Ceiling, Climber));

        Assert.Equal(PetMotionState.Ceiling, engine.State);
        Near(400, engine.CenterX);
        Assert.Equal(1, engine.Direction);
        Near(0, engine.Box(Bodies).Top);
    }

    [Fact]
    public void Previewing_the_ceiling_needs_a_top_nothing_else_touches()
    {
        var engine = OnFloor(new Random(1), x: 400);

        Assert.False(engine.Demonstrate(PetDemo.Ceiling, Climber with { Area = Screen with { TopIsOuter = false } }));
    }

    [Fact]
    public void Previewing_a_fall_lifts_the_pet_to_the_top_and_lets_it_float_down()
    {
        var engine = OnFloor(new Random(1), x: 400);

        Assert.True(engine.Demonstrate(PetDemo.Fall, Floor));

        Assert.Equal(PetMotionState.Falling, engine.State);
        Near(0, engine.Box(Bodies).Top);
    }

    // ------------------------------------------------------------------ the world changing under it

    [Fact]
    public void Moving_to_a_smaller_display_pulls_the_pet_inside_it()
    {
        var engine = OnFloor(new Random(1), x: 1800);
        var small = Floor with { Area = new PetArea(0, 0, 1000, 700) };

        engine.Advance(At(0.05), 0.05, small);

        Assert.InRange(engine.CenterX, 50, 950);
        Near(700 - 60, engine.CenterY);
    }

    // ------------------------------------------------------------------ test double

    /// <summary>Returns the draws it was given, then high values, in place of real randomness.</summary>
    private sealed class ScriptedRandom(double[]? doubles = null, int[]? rolls = null, double fallback = 0.99) : Random
    {
        private readonly Queue<double> _doubles = new(doubles ?? []);
        private readonly Queue<int> _rolls = new(rolls ?? []);

        public override int Next(int maxValue) => _rolls.Count > 0 ? _rolls.Dequeue() : maxValue - 1;

        // Once the script runs out every draw is the fallback: by default 0.99, which is no wall,
        // no drop and a long walk leg.
        public override double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : fallback;
    }
}
