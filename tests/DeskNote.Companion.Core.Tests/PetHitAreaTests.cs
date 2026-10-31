namespace DeskNote.Companion.Core.Tests;

public class PetHitAreaTests
{
    private static readonly PetBox Pet = new(100, 800, 140, 110);

    [Fact]
    public void The_pet_itself_is_hit() => Assert.True(PetHitArea.Contains(170, 855, Pet));

    [Fact]
    public void Empty_air_around_it_is_not()
    {
        Assert.False(PetHitArea.Contains(60, 855, Pet));
        Assert.False(PetHitArea.Contains(170, 700, Pet));
        Assert.False(PetHitArea.Contains(300, 855, Pet));
        Assert.False(PetHitArea.Contains(170, 960, Pet));
    }

    [Fact]
    public void A_pointer_just_outside_the_outline_still_counts()
    {
        Assert.True(PetHitArea.Contains(100 - 6, 855, Pet));
        Assert.False(PetHitArea.Contains(100 - PetHitArea.DefaultMargin - 2, 855, Pet));
    }

    [Fact]
    public void A_pet_lying_on_a_wall_is_hit_where_it_lies()
    {
        // 113 wide, 139 tall against the right edge of a 1920-wide screen, a third of the way up.
        var onWall = new PetBox(1920 - 113, 300, 113, 139);

        Assert.True(PetHitArea.Contains(1900, 370, onWall));
        Assert.False(PetHitArea.Contains(1900, 700, onWall));
    }
}
