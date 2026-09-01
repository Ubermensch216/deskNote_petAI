using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public sealed class StartupNotePolicyTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(0, 25, false)]
    [InlineData(1, 1, false)]
    public void Only_a_completely_new_library_gets_an_initial_blank_note(
        int openWindowCount,
        int storedNoteCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            StartupNotePolicy.ShouldCreateFirstNote(openWindowCount, storedNoteCount));
    }
}
