namespace DeskNote.Companion.Core.Tests;

public partial class PetMotionEngineTests
{
    /// <summary>
    /// A drawing that plays something different each time the pet sits has to tell one rest from
    /// the next, and two sits of the same pose in a row look identical from outside.
    /// </summary>
    [Fact]
    public void Each_rest_has_a_serial_of_its_own()
    {
        var engine = OnFloor(new Random(1));
        var before = engine.RestSerial;

        Assert.True(engine.BeginRest(At(1), 5, listless: false, seconds: 2, pose: PetRestPose.Sit));
        var first = engine.RestSerial;
        Assert.True(engine.BeginRest(At(5), 5, listless: false, seconds: 2, pose: PetRestPose.Sit));

        Assert.Equal(before + 1, first);
        Assert.Equal(first + 1, engine.RestSerial);
    }

    [Fact]
    public void A_rest_that_is_held_back_does_not_count_until_it_begins()
    {
        var engine = OnFloor(new Random(1));
        engine.Grab();
        var before = engine.RestSerial;

        Assert.False(engine.BeginRest(At(1), 5, listless: false, seconds: 2, pose: PetRestPose.Sit));

        Assert.Equal(before, engine.RestSerial);
    }
}
