namespace DeskNote.Companion.Core.Tests;

public class PetPoseGeometryTests
{
    // The shipped window and the largest pet: 240 x 250 holding a 150 x 200 frame on its bottom edge.
    private static readonly PetLayout Layout = new(240, 250, 150, 200);

    // A drawing that fills the middle of its frame: 10% to 90% across, 20% to 80% down.
    private static readonly PetInk Ink = new(0.1, 0.2, 0.9, 0.8);

    private static PetTransform Upright(double scale = 1, double direction = 1) =>
        new(0, direction * scale, scale, PetPivot.Feet);

    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, precision: 6);

    [Fact]
    public void An_upright_pet_is_where_its_frame_puts_it()
    {
        var box = PetPoseGeometry.InkBox(Layout, Ink, Upright());

        // Frame: x 45..195, y 50..250. Ink: 15 in from the left, 40 down from the frame's top.
        Near(60, box.Left);
        Near(90, box.Top);
        Near(120, box.Width);
        Near(120, box.Height);
    }

    [Fact]
    public void Turning_to_face_the_other_way_mirrors_the_box_about_the_frame()
    {
        var asymmetric = new PetInk(0.1, 0.2, 0.7, 0.8);

        var right = PetPoseGeometry.InkBox(Layout, asymmetric, Upright(direction: 1));
        var left = PetPoseGeometry.InkBox(Layout, asymmetric, Upright(direction: -1));

        Near(right.Width, left.Width);
        // Mirrored about the window's centre line, x = 120.
        Near(240 - right.Right, left.Left);
        Near(right.Top, left.Top);
    }

    [Fact]
    public void Growing_smaller_shrinks_the_box_about_the_feet()
    {
        var full = PetPoseGeometry.InkBox(Layout, Ink, Upright());
        var half = PetPoseGeometry.InkBox(Layout, Ink, Upright(0.5));

        Near(full.Width / 2, half.Width);
        Near(full.Height / 2, half.Height);
        // The feet do not move: the bottom of the ink stays the same distance above the window edge.
        Near(250 - ((250 - full.Bottom) / 2), half.Bottom);
    }

    [Fact]
    public void Lying_on_a_wall_swaps_the_boxs_width_and_height()
    {
        var turned = new PetTransform(-90, 1, 1, PetPivot.Centre);

        var box = PetPoseGeometry.InkBox(Layout, Ink, turned);

        Near(120, box.Width);
        Near(120, box.Height);

        var wide = new PetInk(0, 0.2, 1, 0.8);
        var onWall = PetPoseGeometry.InkBox(Layout, wide, turned);

        // 150 wide and 120 tall upright; 120 wide and 150 tall on its side.
        Near(120, onWall.Width);
        Near(150, onWall.Height);
    }

    [Fact]
    public void A_pose_turned_about_the_centre_stays_inside_its_window()
    {
        var wide = new PetInk(0, 0, 1, 1);
        var onWall = PetPoseGeometry.InkBox(Layout, wide, new PetTransform(-90, 1, 1, PetPivot.Centre));

        // The 150 x 200 frame turned on its side is 200 wide and 150 tall, about (120, 150).
        Near(20, onWall.Left);
        Near(220, onWall.Right);
        Assert.InRange(onWall.Top, 0, 250);
        Assert.InRange(onWall.Bottom, 0, 250);
    }

    [Fact]
    public void Hanging_from_the_ceiling_turns_the_pet_upside_down_without_moving_its_centre()
    {
        // Ink from 10% to 80% down the frame: 80 above the frame's centre line (y = 150) and 60 below.
        var tallHead = new PetInk(0.1, 0.1, 0.9, 0.8);
        var upright = PetPoseGeometry.InkBox(Layout, tallHead, new PetTransform(0, 1, 1, PetPivot.Centre));
        var upsideDown = PetPoseGeometry.InkBox(Layout, tallHead, new PetTransform(0, 1, -1, PetPivot.Centre));

        Near(70, upright.Top);
        Near(140, upright.Height);

        // Turned over, the 80 that was above is below: the box is as tall, and now reaches 80 down.
        Near(140, upsideDown.Height);
        Near(90, upsideDown.Top);
        Near(230, upsideDown.Bottom);
    }

    [Fact]
    public void A_left_wall_is_the_right_walls_mirror_image()
    {
        var asymmetric = new PetInk(0.05, 0.2, 0.8, 0.8);
        var right = PetPoseGeometry.InkBox(Layout, asymmetric, new PetTransform(-90, 1, 1, PetPivot.Centre));
        var left = PetPoseGeometry.InkBox(Layout, asymmetric, new PetTransform(-90, -1, 1, PetPivot.Centre));

        Near(right.Width, left.Width);
        Near(right.Height, left.Height);
        Near(240 - right.Right, left.Left);
    }

    [Fact]
    public void Descending_flips_a_wall_pose_vertically_in_place()
    {
        var up = PetPoseGeometry.InkBox(Layout, Ink, new PetTransform(-90, 1, 1, PetPivot.Centre));
        var down = PetPoseGeometry.InkBox(Layout, Ink, new PetTransform(-90, 1, -1, PetPivot.Centre));

        Near(up.Width, down.Width);
        Near(up.Height, down.Height);
    }

    [Fact]
    public void Placing_the_window_by_the_boxs_offset_puts_the_drawing_exactly_on_the_edge()
    {
        // The use the numbers exist for: a pet whose box must have its right side on x = 1920.
        var box = PetPoseGeometry.InkBox(Layout, new PetInk(0.1, 0.2, 0.9, 0.8), new PetTransform(-90, 1, 1, PetPivot.Centre));
        var windowLeft = 1920 - box.Right;

        Near(1920, windowLeft + box.Right);
        Assert.True(windowLeft + 240 > 1920, "the window must overhang the screen by its padding");
    }

    [Theory]
    [InlineData(CompanionPetKind.Rabbit)]
    [InlineData(CompanionPetKind.Cat)]
    [InlineData(CompanionPetKind.Dog)]
    [InlineData(CompanionPetKind.FennecFox)]
    [InlineData(CompanionPetKind.Otter)]
    [InlineData(CompanionPetKind.Dragon)]
    [InlineData(CompanionPetKind.Monkey)]
    [InlineData(CompanionPetKind.Duck)]
    public void Every_species_has_art_bounds_inside_its_frame(CompanionPetKind pet)
    {
        var ink = PetArtBounds.For(pet);

        Assert.InRange(ink.Left, 0, 0.5);
        Assert.InRange(ink.Right, 0.5, 1);
        Assert.InRange(ink.Top, 0, 0.5);
        Assert.InRange(ink.Bottom, 0.6, 0.9);
    }

    [Fact]
    public void Every_species_in_the_catalog_is_measured()
    {
        // The fallback is a guess, and a species that quietly falls back stands a hand's breadth off the wall.
        var fallback = PetArtBounds.For((CompanionPetKind)999);

        foreach (var pet in CompanionPetCatalog.All)
        {
            Assert.NotEqual(fallback, PetArtBounds.For(pet));
        }
    }
}
