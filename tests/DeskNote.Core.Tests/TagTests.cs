using DeskNote.Core.Models;

namespace DeskNote.Core.Tests;

public class TagTests
{
    [Theory]
    [InlineData("Backend", "backend")]
    [InlineData("  backend  ", "backend")]
    [InlineData("BACKEND", "backend")]
    [InlineData("회의록", "회의록")]
    public void Normalization_folds_case_and_surrounding_space(string input, string expected)
    {
        Assert.Equal(expected, Tag.Normalize(input));
    }
}
