using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public sealed class StartupNotePolicyTests
{
    /// <summary>Notes that came back on their own are the whole answer.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 20)]
    [InlineData(1, 0)]
    public void Restored_notes_are_left_alone(int openWindowCount, int liveNoteCount)
    {
        Assert.Equal(
            StartupNoteAction.Nothing,
            StartupNotePolicy.Decide(openWindowCount, liveNoteCount));
    }

    /// <summary>
    /// The one that used to open Notes Explorer. A sticky-note app launches with a note on the
    /// desktop, not with a file manager.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public void With_nothing_open_the_last_note_comes_back(int liveNoteCount)
    {
        Assert.Equal(
            StartupNoteAction.OpenMostRecent,
            StartupNotePolicy.Decide(openWindowCount: 0, liveNoteCount));
    }

    [Fact]
    public void An_empty_library_gets_its_one_blank_note()
    {
        Assert.Equal(
            StartupNoteAction.CreateFirstNote,
            StartupNotePolicy.Decide(openWindowCount: 0, liveNoteCount: 0));
    }
}
