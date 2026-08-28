using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class MarkdownEditingTests
{
    /// <summary>
    /// Builds a state from a string where "|" marks the caret and "[...]" marks a selection.
    /// </summary>
    /// <remarks>
    /// The caret marker is checked first. Checklist syntax contains real square brackets, so
    /// looking for "[" first reads "- [ ] item|" as a selection over the empty checkbox.
    /// </remarks>
    private static NoteTextState State(string annotated)
    {
        var caret = annotated.IndexOf('|', StringComparison.Ordinal);
        if (caret >= 0)
        {
            return new NoteTextState(annotated.Remove(caret, 1), caret, 0);
        }

        var open = annotated.IndexOf('[', StringComparison.Ordinal);
        var close = annotated.IndexOf(']', StringComparison.Ordinal);
        var text = annotated.Remove(close, 1).Remove(open, 1);
        return new NoteTextState(text, open, close - open - 1);
    }

    private static string Annotate(NoteTextState state) =>
        state.SelectionLength == 0
            ? state.Text.Insert(state.SelectionStart, "|")
            : state.Text.Insert(state.SelectionEnd, "]").Insert(state.SelectionStart, "[");

    /// <summary>
    /// The word stays selected and the markers land outside it, so pressing bold again removes it.
    /// </summary>
    [Fact]
    public void Bold_wraps_the_selection_and_keeps_the_word_itself_selected()
    {
        Assert.Equal("회의 **[준비]**", Annotate(MarkdownEditing.ToggleBold(State("회의 [준비]"))));
    }

    [Fact]
    public void Bold_on_an_empty_selection_leaves_the_caret_between_the_markers()
    {
        Assert.Equal("****", MarkdownEditing.ToggleBold(State("|")).Text);
        Assert.Equal("**|**", Annotate(MarkdownEditing.ToggleBold(State("|"))));
    }

    [Fact]
    public void Bold_applied_twice_returns_the_original_text()
    {
        var once = MarkdownEditing.ToggleBold(State("[중요]"));

        Assert.Equal("중요", MarkdownEditing.ToggleBold(once).Text);
    }

    /// <summary>The markers usually sit just outside the selection after the user re-selects the word.</summary>
    [Fact]
    public void Bold_unwraps_when_the_markers_are_outside_the_selection()
    {
        Assert.Equal("중요", MarkdownEditing.ToggleBold(State("**[중요]**")).Text);
    }

    [Fact]
    public void Inline_code_wraps_with_backticks()
    {
        Assert.Equal("`p95`", MarkdownEditing.ToggleInlineCode(State("[p95]")).Text);
    }

    /// <summary>
    /// Italic uses a single asterisk, so naive toggling on bold text would eat one of the bold
    /// markers and silently corrupt the formatting.
    /// </summary>
    [Fact]
    public void Italic_on_bold_text_nests_instead_of_breaking_the_bold()
    {
        Assert.Equal("***중요***", MarkdownEditing.ToggleItalic(State("**[중요]**")).Text);
    }

    [Fact]
    public void A_link_with_a_selection_puts_the_caret_where_the_url_goes()
    {
        var result = MarkdownEditing.InsertLink(State("[보고서]"));

        Assert.Equal("[보고서]()", result.Text);
        Assert.Equal("[보고서](|)", Annotate(result));
    }

    [Fact]
    public void A_link_with_a_known_url_is_inserted_whole()
    {
        var result = MarkdownEditing.InsertLink(State("[문서]"), "https://example.com/a");

        Assert.Equal("[문서](https://example.com/a)", result.Text);
        Assert.Equal(result.Text.Length, result.SelectionLength);
    }

    [Theory]
    [InlineData(LineStyle.Bullet, "- 회의 준비")]
    [InlineData(LineStyle.Checklist, "- [ ] 회의 준비")]
    [InlineData(LineStyle.Heading1, "# 회의 준비")]
    [InlineData(LineStyle.Heading2, "## 회의 준비")]
    [InlineData(LineStyle.Heading3, "### 회의 준비")]
    public void A_line_style_is_applied_to_the_caret_line(LineStyle style, string expected)
    {
        Assert.Equal(expected, MarkdownEditing.ApplyLineStyle(State("회의 |준비"), style).Text);
    }

    [Fact]
    public void Applying_the_same_line_style_twice_removes_it()
    {
        var once = MarkdownEditing.ApplyLineStyle(State("회의 |준비"), LineStyle.Checklist);

        Assert.Equal("회의 준비", MarkdownEditing.ApplyLineStyle(once, LineStyle.Checklist).Text);
    }

    /// <summary>Switching between markers must replace, not stack: "- - [ ] item" is nonsense.</summary>
    [Fact]
    public void One_line_style_replaces_another()
    {
        var bulleted = MarkdownEditing.ApplyLineStyle(State("회의 |준비"), LineStyle.Bullet);
        var checked_ = MarkdownEditing.ApplyLineStyle(bulleted, LineStyle.Checklist);

        Assert.Equal("- [ ] 회의 준비", checked_.Text);
    }

    [Fact]
    public void A_line_style_covers_every_line_the_selection_touches()
    {
        var state = new NoteTextState("첫째\n둘째\n셋째", 1, 8);

        var result = MarkdownEditing.ApplyLineStyle(state, LineStyle.Bullet);

        Assert.Equal("- 첫째\n- 둘째\n- 셋째", result.Text);
    }

    /// <summary>
    /// A mixed selection gets the style applied everywhere rather than flipped per line — flipping
    /// would just produce a differently mixed block, which reads as the command failing.
    /// </summary>
    [Fact]
    public void A_partly_styled_selection_becomes_fully_styled()
    {
        var state = new NoteTextState("- 첫째\n둘째", 0, 8);

        var result = MarkdownEditing.ApplyLineStyle(state, LineStyle.Bullet);

        Assert.Equal("- 첫째\n- 둘째", result.Text);
    }

    [Fact]
    public void Enter_continues_a_bullet_list()
    {
        var result = MarkdownEditing.ContinueListOnEnter(State("- 첫째|"));

        Assert.NotNull(result);
        Assert.Equal("- 첫째\n- ", result.Value.Text);
    }

    [Fact]
    public void Enter_continues_a_checklist_with_an_unchecked_box()
    {
        var result = MarkdownEditing.ContinueListOnEnter(State("- [x] 완료한 일|"));

        Assert.NotNull(result);
        Assert.Equal("- [x] 완료한 일\n- [ ] ", result.Value.Text);
    }

    [Fact]
    public void Enter_increments_a_numbered_list()
    {
        var result = MarkdownEditing.ContinueListOnEnter(State("1. 첫째|"));

        Assert.NotNull(result);
        Assert.Equal("1. 첫째\n2. ", result.Value.Text);
    }

    [Fact]
    public void Enter_preserves_indentation()
    {
        var result = MarkdownEditing.ContinueListOnEnter(State("    - 들여쓴 항목|"));

        Assert.NotNull(result);
        Assert.Equal("    - 들여쓴 항목\n    - ", result.Value.Text);
    }

    /// <summary>Pressing Enter on an empty item is how a user says "done with this list".</summary>
    [Fact]
    public void Enter_on_an_empty_item_ends_the_list()
    {
        var result = MarkdownEditing.ContinueListOnEnter(State("- 첫째\n- |"));

        Assert.NotNull(result);
        Assert.Equal("- 첫째\n", result.Value.Text);
    }

    [Fact]
    public void Enter_outside_a_list_is_left_to_the_editor()
    {
        Assert.Null(MarkdownEditing.ContinueListOnEnter(State("그냥 문장|")));
    }

    [Fact]
    public void Enter_after_a_heading_does_not_repeat_the_heading()
    {
        Assert.Null(MarkdownEditing.ContinueListOnEnter(State("# 제목|")));
    }

    [Fact]
    public void Toggling_a_checklist_item_flips_its_box()
    {
        var unchecked_ = State("- [ ] p95 기준 확정|");
        var ticked = MarkdownEditing.ToggleChecklistItemAtCaret(unchecked_);

        Assert.NotNull(ticked);
        Assert.Equal("- [x] p95 기준 확정", ticked.Value.Text);

        var back = MarkdownEditing.ToggleChecklistItemAtCaret(ticked.Value);
        Assert.NotNull(back);
        Assert.Equal("- [ ] p95 기준 확정", back.Value.Text);
    }

    [Fact]
    public void Toggling_works_on_the_line_the_caret_is_on_in_a_longer_note()
    {
        var state = new NoteTextState("- [ ] 첫째\n- [ ] 둘째\n- [ ] 셋째", 14, 0);

        var result = MarkdownEditing.ToggleChecklistItemAtCaret(state);

        Assert.NotNull(result);
        Assert.Equal("- [ ] 첫째\n- [x] 둘째\n- [ ] 셋째", result.Value.Text);
    }

    [Fact]
    public void Toggling_off_a_checklist_line_does_nothing()
    {
        Assert.Null(MarkdownEditing.ToggleChecklistItemAtCaret(State("그냥 문장|")));
    }

    /// <summary>Transformations must never hand the editor a selection outside the text.</summary>
    [Theory]
    [InlineData(LineStyle.Bullet)]
    [InlineData(LineStyle.Checklist)]
    [InlineData(LineStyle.Heading1)]
    public void A_transformation_always_returns_a_valid_selection(LineStyle style)
    {
        var state = new NoteTextState("한 줄\n두 줄", 0, 7);

        var result = MarkdownEditing.ApplyLineStyle(state, style);

        Assert.InRange(result.SelectionStart, 0, result.Text.Length);
        Assert.InRange(result.SelectionEnd, 0, result.Text.Length);
    }

    /// <summary>
    /// Regression: styling a line used to leave the whole line selected, so the very next Enter —
    /// the natural way to start the second checklist item — replaced the item just created.
    /// </summary>
    [Fact]
    public void Styling_with_only_a_caret_leaves_a_caret_not_a_selection()
    {
        var result = MarkdownEditing.ApplyLineStyle(State("p95 기준 확정|"), LineStyle.Checklist);

        Assert.Equal(0, result.SelectionLength);
        Assert.Equal("- [ ] p95 기준 확정", result.Text);
        Assert.Equal(result.Text.Length, result.SelectionStart);
    }

    [Fact]
    public void The_caret_keeps_its_place_in_the_line_after_styling()
    {
        var result = MarkdownEditing.ApplyLineStyle(State("회의 |준비"), LineStyle.Bullet);

        Assert.Equal("- 회의 준비", result.Text);
        Assert.Equal(5, result.SelectionStart);
        Assert.Equal(0, result.SelectionLength);
    }

    [Fact]
    public void Styling_a_real_selection_still_keeps_it_selected()
    {
        var state = new NoteTextState("첫째\n둘째", 0, 5);

        var result = MarkdownEditing.ApplyLineStyle(state, LineStyle.Bullet);

        Assert.True(result.SelectionLength > 0);
    }

    /// <summary>Styling then pressing Enter is the sequence that builds a checklist.</summary>
    [Fact]
    public void Styling_then_enter_produces_a_second_item()
    {
        var first = MarkdownEditing.ApplyLineStyle(State("p95 기준 확정|"), LineStyle.Checklist);
        var second = MarkdownEditing.ContinueListOnEnter(first);

        Assert.NotNull(second);
        Assert.Equal("- [ ] p95 기준 확정\n- [ ] ", second.Value.Text);
    }

    /// <summary>
    /// Enter with text selected deletes it first, the way typing any character would, and only
    /// then continues the list from what is left.
    /// </summary>
    [Fact]
    public void Enter_with_a_selection_replaces_it_before_continuing()
    {
        // "항목" selected at the end of the item.
        var state = new NoteTextState("- [ ] 첫째 항목", 9, 2);

        var result = MarkdownEditing.ContinueListOnEnter(state);

        Assert.NotNull(result);
        Assert.Equal("- [ ] 첫째 \n- [ ] ", result.Value.Text);
    }

    /// <summary>Enter mid-line splits the item, which is what an editor is expected to do.</summary>
    [Fact]
    public void Enter_in_the_middle_of_an_item_splits_it()
    {
        var result = MarkdownEditing.ContinueListOnEnter(State("- [ ] 첫째|둘째"));

        Assert.NotNull(result);
        Assert.Equal("- [ ] 첫째\n- [ ] 둘째", result.Value.Text);
    }

    /// <summary>
    /// Regression: an empty line counted as "already styled", so the very first Ctrl+Shift+C on a
    /// new note toggled the style off and produced nothing.
    /// </summary>
    [Theory]
    [InlineData(LineStyle.Checklist, "- [ ] ")]
    [InlineData(LineStyle.Bullet, "- ")]
    [InlineData(LineStyle.Heading1, "# ")]
    public void Starting_a_list_on_an_empty_note_adds_the_marker(LineStyle style, string expected)
    {
        var result = MarkdownEditing.ApplyLineStyle(NoteTextState.Empty, style);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expected.Length, result.SelectionStart);
    }

    [Fact]
    public void An_empty_line_can_be_unstyled_again()
    {
        var started = MarkdownEditing.ApplyLineStyle(NoteTextState.Empty, LineStyle.Checklist);

        Assert.Equal(string.Empty, MarkdownEditing.ApplyLineStyle(started, LineStyle.Checklist).Text);
    }

    /// <summary>Blank lines between items are separators; marking them would create empty entries.</summary>
    [Fact]
    public void Blank_lines_inside_a_selection_do_not_get_markers()
    {
        var state = new NoteTextState("첫째\n\n둘째", 0, 8);

        var result = MarkdownEditing.ApplyLineStyle(state, LineStyle.Bullet);

        Assert.Equal("- 첫째\n\n- 둘째", result.Text);
    }
}
