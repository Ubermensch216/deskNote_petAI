using System.Reflection;

namespace DeskNote.Companion.Core.Tests;

public sealed class RigDirectorTests
{
    private const double Tick = 1.0 / 30;
    private const double CharacterPixels = 112;

    public static TheoryData<RigCharacter, int> EverySignature()
    {
        var data = new TheoryData<RigCharacter, int>();

        foreach (var character in Enum.GetValues<RigCharacter>())
        {
            for (var index = 0; index < RigSignatures.PerCharacter; index++)
            {
                data.Add(character, index);
            }
        }

        return data;
    }

    public static TheoryData<RigCharacter> EveryCharacter()
    {
        var data = new TheoryData<RigCharacter>();

        foreach (var character in Enum.GetValues<RigCharacter>())
        {
            data.Add(character);
        }

        return data;
    }

    private static RigInput Input(
        PetMotionState state,
        double stateElapsed = 0,
        PetRestPose rest = PetRestPose.Sit,
        int serial = 1,
        double restElapsed = 0,
        double restDuration = 5,
        double moved = 0,
        double velocityX = 0,
        bool hungry = false,
        bool descending = false) => new(
        state,
        rest,
        serial,
        stateElapsed,
        restElapsed,
        restDuration,
        moved,
        velocityX,
        CharacterPixels,
        GrabXPixels: 10,
        GrabYPixels: -40,
        descending,
        hungry);

    private static RigDirector Director(RigCharacter character, int seed = 3) => new(character, new Random(seed));

    /// <summary>Plays a signature move from start to finish the way the window would, one tick at a time.</summary>
    private static List<RigFrame> Play(RigCharacter character, int index, out double duration)
    {
        var director = Director(character);
        duration = director.ArmSignature(index);
        var frames = new List<RigFrame>();

        for (var t = 0.0; t < duration; t += Tick)
        {
            frames.Add(director.Advance(
                Tick,
                Input(PetMotionState.Resting, stateElapsed: t, restElapsed: t, restDuration: duration)));
        }

        return frames;
    }

    private static IEnumerable<(string Name, float Value)> Numbers(RigPose pose) =>
        typeof(RigPose)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(float))
            .Select(field => (field.Name, (float)field.GetValue(pose)!));

    // ------------------------------------------------------------------ signature moves

    /// <summary>
    /// A pose is a few dozen floats written by hand against a clock; one division by a value that
    /// happens to be zero at one instant would put a NaN in the middle of a Kamehameha and the
    /// painter would throw on the next frame. Playing all fifteen end to end is how that is caught.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySignature))]
    public void Every_signature_plays_through_with_finite_numbers(RigCharacter character, int index)
    {
        var frames = Play(character, index, out _);

        Assert.NotEmpty(frames);
        foreach (var frame in frames)
        {
            foreach (var (name, value) in Numbers(frame.Pose))
            {
                Assert.True(float.IsFinite(value), $"{character} #{index}: {name} is {value}.");
            }

            Assert.True(double.IsFinite(frame.LiftPixels));
            Assert.InRange(frame.Alpha, 0f, 1f);
        }
    }

    [Theory]
    [MemberData(nameof(EverySignature))]
    public void A_signature_reports_which_move_is_playing(RigCharacter character, int index)
    {
        var frames = Play(character, index, out _);

        Assert.All(frames, frame => Assert.Equal(index, frame.Signature));
        Assert.All(frames, frame => Assert.True(frame.ShowsSignature));
        Assert.All(frames, frame => Assert.True((frame.Decor & RigDecor.SigStars) != 0));
    }

    /// <summary>The stars and the gold bubble are what tell a signature move from a fidget, so an ordinary rest must not have them.</summary>
    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void An_ordinary_rest_is_not_a_signature(RigCharacter character)
    {
        var director = Director(character);

        for (var t = 0.0; t < 5; t += Tick)
        {
            var frame = director.Advance(Tick, Input(PetMotionState.Resting, restElapsed: t, stateElapsed: t));

            Assert.False(frame.ShowsSignature);
            Assert.Equal(RigDecor.None, frame.Decor & RigDecor.SigStars);
        }
    }

    /// <summary>A move that was asked for belongs to the next rest only: asked once, shown once.</summary>
    [Fact]
    public void An_armed_signature_is_used_up_by_the_rest_that_plays_it()
    {
        var director = Director(RigCharacter.Goku);
        director.ArmSignature(0);

        var first = director.Advance(Tick, Input(PetMotionState.Resting, serial: 1));
        Assert.Equal(0, first.Signature);
        Assert.Equal(-1, director.PendingSignature);

        var next = director.Advance(Tick, Input(PetMotionState.Resting, serial: 2));
        Assert.Equal(-1, next.Signature);
    }

    [Fact]
    public void An_armed_signature_can_be_withdrawn()
    {
        var director = Director(RigCharacter.Piccolo);
        director.ArmSignature(2);
        director.DisarmSignature();

        var frame = director.Advance(Tick, Input(PetMotionState.Resting, serial: 1));

        Assert.False(frame.ShowsSignature);
    }

    [Theory]
    [MemberData(nameof(EverySignature))]
    public void Arming_a_move_returns_how_long_it_runs(RigCharacter character, int index)
    {
        var director = Director(character);

        Assert.Equal(RigSignatures.Duration(character, index), director.ArmSignature(index));
    }

    [Theory]
    [InlineData(RigCharacter.Goku, 0)]
    [InlineData(RigCharacter.Roshi, 0)]
    [InlineData(RigCharacter.Vegeta, 1)]
    [InlineData(RigCharacter.Piccolo, 0)]
    public void A_beam_is_fired_and_has_gone_by_the_end(RigCharacter character, int index)
    {
        var frames = Play(character, index, out _);

        Assert.Contains(frames, frame => frame.Pose.BeamLen > 100);
        Assert.Equal(0, frames[^1].Pose.BeamLen);
        Assert.Equal(0, frames[^1].Pose.Flash);
    }

    [Fact]
    public void Gokus_instant_transmission_asks_to_be_moved_exactly_once()
    {
        var frames = Play(RigCharacter.Goku, 1, out _);

        Assert.Equal(1, frames.Count(frame => frame.Teleport));

        // While it is gone nothing is drawn of the body, and it is whole again at the end.
        Assert.Contains(frames, frame => frame.Alpha == 0);
        Assert.Equal(1f, frames[^1].Alpha);
    }

    [Fact]
    public void Only_the_instant_transmission_teleports()
    {
        foreach (var character in Enum.GetValues<RigCharacter>())
        {
            for (var index = 0; index < RigSignatures.PerCharacter; index++)
            {
                if (character == RigCharacter.Goku && index == 1)
                {
                    continue;
                }

                Assert.DoesNotContain(Play(character, index, out _), frame => frame.Teleport);
            }
        }
    }

    [Fact]
    public void The_kid_rides_his_cloud_along_the_floor()
    {
        var frames = Play(RigCharacter.Kid, 0, out _);

        Assert.Contains(frames, frame => frame.DriftPixelsPerSecond > 10);
        Assert.Contains(frames, frame => frame.Pose.Nimbus > 0.9f);
        Assert.Contains(frames, frame => frame.LiftPixels > 10);
        Assert.Equal(0, frames[^1].DriftPixelsPerSecond);
    }

    [Fact]
    public void The_great_ape_grows_and_goes_back()
    {
        var frames = Play(RigCharacter.Kid, 2, out _);

        Assert.Contains(frames, frame => frame.Scale > 1.7f && frame.Pose.Ape > 0.9f);
        Assert.Contains(frames, frame => (frame.Decor & RigDecor.Moon) != 0 && frame.Moon > 0.9f);
        Assert.Equal(1f, frames[^1].Scale, 2);
        Assert.Equal(0f, frames[^1].Pose.Ape, 2);
    }

    [Fact]
    public void Goku_goes_gold_and_comes_back()
    {
        var frames = Play(RigCharacter.Goku, 2, out _);

        Assert.Contains(frames, frame => frame.Pose.Super > 0.99f);
        Assert.True(frames[^1].Pose.Super < 0.05f);
    }

    [Fact]
    public void Roshis_muscles_swell_and_shrink()
    {
        var frames = Play(RigCharacter.Roshi, 1, out _);

        Assert.Contains(frames, frame => frame.Pose.Muscle > 0.95f);
        Assert.True(frames[^1].Pose.Muscle < 0.05f);
    }

    [Fact]
    public void Piccolo_stretches_each_arm_in_turn()
    {
        var frames = Play(RigCharacter.Piccolo, 1, out _);

        Assert.Contains(frames, frame => frame.Pose.ArmLenR > 2.5f);
        Assert.Contains(frames, frame => frame.Pose.ArmLenL > 2.0f);
        Assert.Equal(1f, frames[^1].Pose.ArmLenR);
        Assert.Equal(1f, frames[^1].Pose.ArmLenL);
    }

    // ------------------------------------------------------------------ walking

    /// <summary>
    /// The legs follow the ground, not a clock. A pet that stands still has still feet however
    /// long it stands, and one that walks puts a foot back where it was after one stride's worth
    /// of ground: that is what keeps a foot from skating.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void A_pet_that_is_not_moving_keeps_its_feet_still(RigCharacter character)
    {
        var director = Director(character);
        RigFrame frame = null!;

        for (var i = 0; i < 60; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Walking, moved: 0));
        }

        var before = (frame.Pose.FootLX, frame.Pose.FootLY, frame.Pose.FootRX, frame.Pose.FootRY);
        for (var i = 0; i < 30; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Walking, moved: 0));
        }

        Assert.Equal(before, (frame.Pose.FootLX, frame.Pose.FootLY, frame.Pose.FootRX, frame.Pose.FootRY));
    }

    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void The_feet_come_round_after_one_stride_of_ground(RigCharacter character)
    {
        var director = Director(character);
        var unit = CharacterPixels / 571.0;
        const int StepsPerStride = 20;
        var step = 74.0 * unit / StepsPerStride;
        RigFrame frame = null!;

        for (var i = 0; i < 90; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Walking, moved: step));
        }

        var start = frame.Pose.FootLX;
        var moved = false;
        for (var i = 0; i < StepsPerStride; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Walking, moved: step));
            moved |= Math.Abs(frame.Pose.FootLX - start) > 1;
        }

        Assert.True(moved, "The foot never moved during the stride.");
        Assert.Equal(start, frame.Pose.FootLX, 0.5f);
    }

    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void Walking_turns_the_body_to_show_its_profile(RigCharacter character)
    {
        var director = Director(character);
        RigFrame frame = null!;

        for (var i = 0; i < 60; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Walking, moved: 1));
        }

        Assert.Equal(1, frame.Pose.View);
        Assert.True(frame.Pose.Turn > 0.3f);
    }

    // ------------------------------------------------------------------ states

    [Fact]
    public void Vegeta_glares_when_poked_and_Goku_is_startled()
    {
        var vegeta = Director(RigCharacter.Vegeta);
        var goku = Director(RigCharacter.Goku);
        vegeta.Poke();
        goku.Poke();

        RigFrame angry = null!;
        RigFrame startled = null!;
        for (var i = 0; i < 6; i++)
        {
            angry = vegeta.Advance(Tick, Input(PetMotionState.Walking));
            startled = goku.Advance(Tick, Input(PetMotionState.Walking));
        }

        Assert.True(angry.Pose.Anger > 0.5f);
        Assert.Equal(MouthStyle.Frown, angry.Pose.Mouth);
        Assert.Equal(0f, startled.Pose.Anger);
        Assert.Equal(MouthStyle.Scared, startled.Pose.Mouth);
    }

    [Fact]
    public void A_poke_wears_off()
    {
        var director = Director(RigCharacter.Vegeta);
        director.Poke();

        RigFrame frame = null!;
        for (var i = 0; i < 30; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Walking));
        }

        Assert.Equal(0f, frame.Pose.Anger);
    }

    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void A_hungry_pet_is_in_tears_whether_it_walks_or_sits(RigCharacter character)
    {
        var walking = Director(character).Advance(Tick, Input(PetMotionState.Walking, hungry: true));
        Assert.True(walking.Pose.Tears >= 0, "Not crying on its feet.");

        // Sat down it cries on the spot, unless the sit came out as a stand with something to do.
        var cried = false;
        for (var serial = 1; serial <= 12 && !cried; serial++)
        {
            var director = Director(character, seed: serial);
            var sitting = director.Advance(Tick, Input(PetMotionState.Resting, serial: 1, hungry: true));
            cried = sitting.Pose.Tears >= 0;
        }

        Assert.True(cried);
    }

    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void Falling_floats_on_ki_and_the_kid_rides_his_cloud(RigCharacter character)
    {
        var frame = Director(character).Advance(Tick, Input(PetMotionState.Falling, stateElapsed: 1));

        if (character == RigCharacter.Kid)
        {
            Assert.True(frame.Pose.Nimbus > 0.5f);
        }
        else
        {
            Assert.True(frame.Pose.Aura > 0.3f);
        }
    }

    [Fact]
    public void Three_throws_become_a_burst_of_ki_on_the_way_down()
    {
        var director = Director(RigCharacter.Goku);
        director.TriggerPowerBurst(3);

        var frame = director.Advance(Tick, Input(PetMotionState.Falling, stateElapsed: 1));

        Assert.Equal(1f, frame.Pose.Super);
        Assert.Equal(2, frame.Pose.AuraKind);

        // It does not outlast its time.
        for (var i = 0; i < 120; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Falling, stateElapsed: 1));
        }

        Assert.Equal(0f, frame.Pose.Super);
    }

    [Fact]
    public void A_pet_in_the_hand_hangs_from_where_it_was_taken_hold_of()
    {
        var director = Director(RigCharacter.Roshi);
        RigFrame frame = null!;

        for (var i = 0; i < 30; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Held, velocityX: 600));
        }

        var unit = CharacterPixels / 571.0;
        Assert.Equal(10 / unit, frame.Pivot.X, 0.5);
        Assert.Equal(-40 / unit, frame.Pivot.Y, 0.5);
        Assert.NotEqual(0f, frame.Angle);
    }

    [Fact]
    public void A_pet_in_the_hand_swings_against_the_pointer()
    {
        var right = Director(RigCharacter.Goku);
        var left = Director(RigCharacter.Goku);
        RigFrame a = null!;
        RigFrame b = null!;

        for (var i = 0; i < 20; i++)
        {
            a = right.Advance(Tick, Input(PetMotionState.Held, velocityX: 800));
            b = left.Advance(Tick, Input(PetMotionState.Held, velocityX: -800));
        }

        Assert.True(a.Angle * b.Angle < 0, $"Swing was {a.Angle} and {b.Angle}.");
    }

    [Fact]
    public void Dashing_leaves_dust_and_a_hop_leaves_the_ground()
    {
        var dash = Director(RigCharacter.Piccolo).Advance(Tick, Input(PetMotionState.Dashing, stateElapsed: 0.2));
        Assert.True((dash.Decor & RigDecor.Speed) != 0);

        var director = Director(RigCharacter.Piccolo);
        var peak = 0.0;
        for (var t = 0.0; t < PetMotionEngine.HopSeconds; t += Tick)
        {
            peak = Math.Max(peak, director.Advance(Tick, Input(PetMotionState.Hopping, stateElapsed: t)).LiftPixels);
        }

        Assert.True(peak > 5, $"Peak was {peak}.");
    }

    [Theory]
    [InlineData(PetRestPose.Sleep)]
    [InlineData(PetRestPose.Daydream)]
    [InlineData(PetRestPose.Stretch)]
    [InlineData(PetRestPose.Sit)]
    public void Every_rest_pose_is_a_valid_pose(PetRestPose rest)
    {
        foreach (var character in Enum.GetValues<RigCharacter>())
        {
            var director = Director(character);
            for (var t = 0.0; t < 6; t += Tick)
            {
                var frame = director.Advance(
                    Tick,
                    Input(PetMotionState.Resting, rest: rest, restElapsed: t, stateElapsed: t, restDuration: 6));

                Assert.All(Numbers(frame.Pose), n => Assert.True(float.IsFinite(n.Value), $"{character} {rest}: {n.Name}"));
            }
        }
    }

    [Fact]
    public void A_sleeping_pet_sits_with_its_eyes_shut()
    {
        var frame = Director(RigCharacter.Goku).Advance(
            Tick,
            Input(PetMotionState.Resting, rest: PetRestPose.Sleep, restElapsed: 1, stateElapsed: 1));

        Assert.True(frame.Pose.Sit);
        Assert.Equal(EyeStyle.Sleep, frame.Pose.Eyes);
    }

    // ------------------------------------------------------------------ care

    [Fact]
    public void Being_fed_shows_a_snack_that_is_eaten_down()
    {
        var director = Director(RigCharacter.Goku);
        director.PlayCare(CompanionCareAction.Feed, 2.6);

        var early = director.Advance(Tick, Input(PetMotionState.Resting));
        for (var i = 0; i < 90; i++)
        {
            director.Advance(Tick, Input(PetMotionState.Resting));
        }

        var late = director.Advance(Tick, Input(PetMotionState.Resting));

        Assert.InRange(early.Pose.Cookie, 0f, 0.2f);
        Assert.True(late.Pose.Cookie < 0, "The snack should be gone when the care is over.");
    }

    [Theory]
    [InlineData(CompanionCareAction.Play, RigDecor.Notes)]
    [InlineData(CompanionCareAction.Greeting, RigDecor.Flower)]
    public void Games_and_greetings_have_their_own_decorations(CompanionCareAction action, RigDecor expected)
    {
        var director = Director(RigCharacter.Vegeta);
        director.PlayCare(action, 2.6);

        var frame = director.Advance(Tick, Input(PetMotionState.Resting));

        Assert.Equal(expected, frame.Decor & expected);
    }

    [Fact]
    public void A_dizzy_pet_spirals_its_eyes_with_stars_round_its_head_and_then_recovers()
    {
        var director = Director(RigCharacter.Piccolo);
        director.PlayDizzy(1);

        var reeling = director.Advance(Tick, Input(PetMotionState.Resting));
        Assert.Equal(EyeStyle.Spiral, reeling.Pose.Eyes);
        Assert.True((reeling.Decor & RigDecor.Stars) != 0);

        RigFrame frame = null!;
        for (var i = 0; i < 45; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Resting));
        }

        Assert.NotEqual(EyeStyle.Spiral, frame.Pose.Eyes);
        Assert.Equal(RigDecor.None, frame.Decor & RigDecor.Stars);
    }

    [Fact]
    public void A_pet_that_is_flying_is_not_fed()
    {
        var director = Director(RigCharacter.Goku);
        director.PlayCare(CompanionCareAction.Feed, 2.6);

        var frame = director.Advance(Tick, Input(PetMotionState.Falling, stateElapsed: 1));

        Assert.True(frame.Pose.Cookie < 0);
    }

    // ------------------------------------------------------------------ repeatability

    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void The_same_seed_gives_the_same_performance(RigCharacter character)
    {
        long Run()
        {
            var director = Director(character, seed: 99);
            long hash = 17;
            for (var i = 0; i < 300; i++)
            {
                var state = i < 150 ? PetMotionState.Walking : PetMotionState.Resting;
                var frame = director.Advance(
                    Tick,
                    Input(state, moved: 0.7, restElapsed: (i - 150) * Tick, serial: i < 150 ? 0 : 1));
                hash = unchecked((hash * 31) + frame.Pose.Hash());
            }

            return hash;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Standing_rests_vary_so_that_a_sit_is_not_always_the_same_sit()
    {
        var hashes = new HashSet<long>();

        for (var seed = 0; seed < 40; seed++)
        {
            var director = Director(RigCharacter.Goku, seed);
            RigFrame frame = null!;
            for (var i = 0; i < 40; i++)
            {
                frame = director.Advance(
                    Tick,
                    Input(PetMotionState.Resting, restElapsed: i * Tick, stateElapsed: i * Tick));
            }

            hashes.Add(frame.Pose.Hash());
        }

        Assert.True(hashes.Count > 5, $"Only {hashes.Count} distinct sits in forty tries.");
    }
}
