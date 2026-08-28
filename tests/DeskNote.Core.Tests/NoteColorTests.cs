using DeskNote.Core.Models;

namespace DeskNote.Core.Tests;

public class NoteColorTests
{
    /// <summary>Report p4 calls for six to eight low-saturation colors.</summary>
    [Fact]
    public void The_palette_is_within_the_specified_range()
    {
        Assert.InRange(NoteColors.All.Count, 6, 8);
        Assert.Equal(NoteColors.All.Count, NoteColors.All.Distinct().Count());
    }

    [Fact]
    public void Every_palette_entry_is_recognized()
    {
        Assert.All(NoteColors.All, color => Assert.True(NoteColors.IsKnown(color)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("neon-green")]
    [InlineData("YELLOW")]
    public void Unrecognized_keys_normalize_to_the_default(string? colorKey)
    {
        Assert.Equal(NoteColors.Default, NoteColors.Normalize(colorKey));
    }

    [Fact]
    public void Recognized_keys_pass_through_unchanged()
    {
        Assert.Equal(NoteColors.Teal, NoteColors.Normalize(NoteColors.Teal));
    }
}
