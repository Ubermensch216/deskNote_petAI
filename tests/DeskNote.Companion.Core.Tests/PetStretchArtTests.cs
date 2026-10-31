namespace DeskNote.Companion.Core.Tests;

public sealed class PetStretchArtTests
{
    [Fact]
    public void Only_the_cat_has_a_stretch_drawn_so_far()
    {
        Assert.Equal(
            [CompanionPetKind.Cat],
            CompanionPetCatalog.All.Where(PetStretchArt.Has).ToList());
    }

    [Theory]
    [InlineData(0.00, 0)]
    [InlineData(0.05, 0)]
    [InlineData(0.15, 1)]
    [InlineData(0.30, 2)]
    [InlineData(0.50, 2)]
    [InlineData(0.75, 1)]
    [InlineData(0.90, 0)]
    [InlineData(1.00, 0)]
    public void The_stretch_leans_in_reaches_holds_and_lets_go(double progress, int frame)
    {
        Assert.Equal(frame, PetStretchArt.FrameAt(progress));
    }

    /// <summary>
    /// The pose starts and ends on the same frame, so the pet is where it began when it gets up
    /// - and whatever length the rest was given, because the frame follows progress, not time.
    /// </summary>
    [Fact]
    public void It_ends_where_it_started()
    {
        Assert.Equal(PetStretchArt.FrameAt(0), PetStretchArt.FrameAt(1));
        Assert.Equal(PetStretchArt.FrameAt(-3), PetStretchArt.FrameAt(0));
        Assert.Equal(PetStretchArt.FrameAt(9), PetStretchArt.FrameAt(1));
    }

    [Fact]
    public void Held_it_shivers_between_the_last_two_frames()
    {
        var seen = Enumerable.Range(0, 40)
            .Select(step => PetStretchArt.FrameAt(0.4, step * 0.1))
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal([2, 3], seen);
    }

    [Fact]
    public void Every_frame_it_asks_for_is_in_the_sheet()
    {
        for (var step = 0; step <= 1000; step++)
        {
            var frame = PetStretchArt.FrameAt(step / 1000.0, step * 0.01);

            Assert.InRange(frame, 0, PetStretchArt.Frames - 1);
        }
    }

    [Fact]
    public void Only_the_hold_has_a_tremble()
    {
        Assert.False(PetStretchArt.IsHolding(0.1));
        Assert.True(PetStretchArt.IsHolding(0.4));
        Assert.False(PetStretchArt.IsHolding(0.8));
    }

    /// <summary>
    /// A sheet that is missing leaves the pet with an image that never loads, which is a pet that
    /// vanishes in the middle of a stretch. The files cannot be found by the type system, so this
    /// looks for them.
    /// </summary>
    [Fact]
    public void Every_species_that_stretches_ships_a_sheet_of_four_frames()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DeskNote.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        foreach (var pet in CompanionPetCatalog.All.Where(PetStretchArt.Has))
        {
            var sheet = Path.Combine(
                root.FullName, "src", "DeskNote.App", "Assets", "Companion", $"{pet.AssetKey()}-stretch.png");
            Assert.True(File.Exists(sheet), $"{pet} has no stretch sheet.");

            // The width and height sit at fixed offsets in a PNG's first chunk.
            var header = File.ReadAllBytes(sheet).AsSpan(16, 8);
            var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[..4]);
            var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[4..]);

            var walk = Path.Combine(
                root.FullName, "src", "DeskNote.App", "Assets", "Companion", $"{pet.AssetKey()}-walk.png");
            var walkHeader = File.ReadAllBytes(walk).AsSpan(16, 8);
            var walkWidth = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(walkHeader[..4]);
            var walkHeight = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(walkHeader[4..]);

            Assert.Equal((walkWidth, walkHeight), (width, height));
            Assert.Equal(0, width % PetStretchArt.Frames);
        }
    }
}
