using DeskNote.Companion.Core;
using DeskNote.Companion.Rig;

namespace DeskNote.Companion.Rig.Tests;

public sealed class RigSurfaceTests
{
    private const int Width = 520;
    private const int Height = 250;
    private const double Tick = 1.0 / 30;
    private const double CharacterPixels = 112;

    private static readonly RigPlacement Stand = new(Width / 2.0, 225, CharacterPixels);

    public static TheoryData<RigCharacter> EveryCharacter()
    {
        var data = new TheoryData<RigCharacter>();

        foreach (var character in Enum.GetValues<RigCharacter>())
        {
            data.Add(character);
        }

        return data;
    }

    private static RigFrame Standing(RigCharacter character)
    {
        var director = new RigDirector(character, new Random(5));
        RigFrame frame = null!;

        for (var i = 0; i < 60; i++)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Walking, moved: 0.7));
        }

        return frame;
    }

    private static RigInput Input(PetMotionState state, double moved = 0, double t = 0, double duration = 5) => new(
        state,
        PetRestPose.Sit,
        1,
        t,
        t,
        duration,
        moved,
        0,
        CharacterPixels,
        0,
        0,
        false,
        false);

    private static (int Left, int Top, int Right, int Bottom, int Count) Bounds(RigSurface surface)
    {
        int left = Width, top = Height, right = -1, bottom = -1, count = 0;
        var pixels = surface.Pixels;

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (pixels[(((y * Width) + x) * 4) + 3] > 8)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                    count++;
                }
            }
        }

        return (left, top, right, bottom, count);
    }

    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void A_standing_character_is_painted_on_its_foot_line(RigCharacter character)
    {
        using var surface = new RigSurface(character, Width, Height);

        Assert.True(surface.Render(Standing(character), Stand));

        var (left, top, right, bottom, count) = Bounds(surface);
        Assert.True(count > 2000, $"Only {count} pixels were painted.");

        // Centred on the middle of the surface, standing on the line it was told, and about as tall
        // as it was asked to be (hair and antennae stand above the nominal height; a bald, stooped
        // old man stands a good deal below it).
        Assert.InRange((left + right) / 2.0, (Width / 2.0) - 25, (Width / 2.0) + 25);
        Assert.InRange(bottom, 220, 235);
        Assert.InRange(bottom - top, CharacterPixels * 0.7, CharacterPixels * 1.35);
    }

    /// <summary>
    /// The window mirrors the picture for the other direction, so the picture is always the one
    /// facing right: anything the pose reaches toward is to the right of the body.
    /// </summary>
    [Fact]
    public void A_beam_goes_out_the_way_the_character_faces_and_a_long_way()
    {
        var director = new RigDirector(RigCharacter.Goku, new Random(5));
        var duration = director.ArmSignature(0);
        RigFrame frame = null!;
        for (var t = 0.0; t < 4.6; t += Tick)
        {
            frame = director.Advance(Tick, Input(PetMotionState.Resting, t: t, duration: duration));
        }

        using var surface = new RigSurface(RigCharacter.Goku, Width, Height);
        surface.Render(frame, Stand);

        var (left, _, right, _, _) = Bounds(surface);
        Assert.True(right > (Width / 2.0) + 150, $"The beam only reached {right}.");
        Assert.True((Width / 2.0) - left < right - (Width / 2.0), "The picture reaches further behind the character than in front of it.");
    }

    [Fact]
    public void A_fully_transparent_body_leaves_nothing_behind()
    {
        var frame = Standing(RigCharacter.Vegeta);
        frame.Alpha = 0;
        using var surface = new RigSurface(RigCharacter.Vegeta, Width, Height);

        surface.Render(frame, Stand);

        Assert.Equal(0, Bounds(surface).Count);
    }

    [Fact]
    public void A_half_transparent_body_is_half_as_opaque()
    {
        var frame = Standing(RigCharacter.Goku);
        using var solid = new RigSurface(RigCharacter.Goku, Width, Height);
        using var faded = new RigSurface(RigCharacter.Goku, Width, Height);

        solid.Render(frame, Stand);
        frame.Alpha = 0.5f;
        faded.Render(frame, Stand);

        Assert.Equal(255, MaxAlpha(solid));
        Assert.InRange(MaxAlpha(faded), 120, 135);
    }

    private static byte MaxAlpha(RigSurface surface)
    {
        byte max = 0;
        var pixels = surface.Pixels;

        for (var i = 3; i < pixels.Length; i += 4)
        {
            max = Math.Max(max, pixels[i]);
        }

        return max;
    }

    /// <summary>
    /// What the window hands to WinUI is premultiplied; a pixel whose colour is brighter than its
    /// alpha would show as a fringe round every edge.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void Pixels_are_premultiplied(RigCharacter character)
    {
        using var surface = new RigSurface(character, Width, Height);
        surface.Render(Standing(character), Stand);

        var pixels = surface.Pixels;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.True(pixels[i] <= pixels[i + 3] && pixels[i + 1] <= pixels[i + 3] && pixels[i + 2] <= pixels[i + 3]);
        }
    }

    [Fact]
    public void A_frame_that_looks_the_same_is_not_painted_again()
    {
        // Two frames from two directors that were told the same things: painting a pose settles
        // things on it (Roshi picks up his staff), so the same object cannot be offered twice.
        using var surface = new RigSurface(RigCharacter.Roshi, Width, Height);

        Assert.True(surface.Render(Standing(RigCharacter.Roshi), Stand));
        Assert.False(surface.Render(Standing(RigCharacter.Roshi), Stand));

        var moved = Standing(RigCharacter.Roshi);
        moved.Pose.BodyY += 3;
        Assert.True(surface.Render(moved, Stand));
    }

    [Fact]
    public void Moving_the_figure_or_resizing_it_repaints()
    {
        using var surface = new RigSurface(RigCharacter.Piccolo, Width, Height);
        surface.Render(Standing(RigCharacter.Piccolo), Stand);

        Assert.True(surface.Render(Standing(RigCharacter.Piccolo), Stand with { CharacterPixels = 80 }));
        Assert.True(surface.Render(Standing(RigCharacter.Piccolo), Stand with { FootX = 100 }));

        surface.Resize(Width / 2, Height / 2);
        Assert.Equal(Width / 2 * (Height / 2) * 4, surface.Pixels.Length);
        Assert.True(surface.Render(Standing(RigCharacter.Piccolo), new RigPlacement(Width / 4.0, 110, 50)));
    }

    [Fact]
    public void Two_surfaces_paint_the_same_picture_from_the_same_frame()
    {
        var frame = Standing(RigCharacter.Kid);
        using var a = new RigSurface(RigCharacter.Kid, Width, Height);
        using var b = new RigSurface(RigCharacter.Kid, Width, Height);

        a.Render(frame, Stand);
        b.Render(frame, Stand);

        Assert.True(a.Pixels.SequenceEqual(b.Pixels));
    }

    /// <summary>
    /// A painter that throws takes the pet window's timer down with it. Every moment of every
    /// signature move is painted, at a few sizes, to find a pose that GDI+ will not draw.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void Every_signature_paints_at_every_moment_and_several_sizes(RigCharacter character)
    {
        foreach (var pixels in new[] { 24.0, 56.0, 112.0 })
        {
            using var surface = new RigSurface(character, Width, Height);
            var placement = new RigPlacement(Width / 2.0, 225, pixels);

            for (var index = 0; index < RigSignatures.PerCharacter; index++)
            {
                var director = new RigDirector(character, new Random(index));
                var duration = director.ArmSignature(index);
                for (var t = 0.0; t < duration; t += 0.25)
                {
                    var frame = director.Advance(0.25, new RigInput(
                        PetMotionState.Resting, PetRestPose.Sit, 1, t, t, duration, 0, 0, pixels, 0, 0, false, false));

                    surface.Render(frame, placement);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryCharacter))]
    public void Every_state_paints(RigCharacter character)
    {
        using var surface = new RigSurface(character, Width, Height);

        foreach (var state in Enum.GetValues<PetMotionState>())
        {
            foreach (var rest in Enum.GetValues<PetRestPose>())
            {
                var director = new RigDirector(character, new Random(2));
                director.PlayCare(CompanionCareAction.Greeting, 3);
                RigFrame frame = null!;
                for (var i = 0; i < 20; i++)
                {
                    frame = director.Advance(Tick, new RigInput(
                        state, rest, 1, i * Tick, i * Tick, 5, 0.7, 120, CharacterPixels, 12, -50, i % 2 == 0, i % 3 == 0));
                }

                surface.Render(frame, Stand);
            }
        }
    }
}
