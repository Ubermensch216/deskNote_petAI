using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class NoteTextStateTests
{
    [Fact]
    public void Replacing_a_selection_types_over_it_and_leaves_a_caret()
    {
        var state = new NoteTextState("배포 취소 예정", 3, 2);

        var replaced = state.ReplacingSelection("확정");

        Assert.Equal("배포 확정 예정", replaced.Text);
        Assert.Equal(5, replaced.SelectionStart);
        Assert.Equal(0, replaced.SelectionLength);
    }

    [Fact]
    public void Replacing_nothing_inserts_at_the_caret()
    {
        var replaced = new NoteTextState("배포 예정", 3, 0).ReplacingSelection("즉시 ");

        Assert.Equal("배포 즉시 예정", replaced.Text);
        Assert.Equal(6, replaced.SelectionStart);
    }

    /// <summary>
    /// A paste can arrive while the selection is stale — the AI proposal window and the attachment
    /// pipeline both hand back a state that was captured earlier.
    /// </summary>
    [Fact]
    public void An_out_of_range_selection_is_clamped_rather_than_throwing()
    {
        var replaced = new NoteTextState("짧다", 99, 99).ReplacingSelection("!");

        Assert.Equal("짧다!", replaced.Text);
        Assert.Equal(3, replaced.SelectionStart);
    }
}
