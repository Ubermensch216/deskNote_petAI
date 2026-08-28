using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class ChecklistParserTests
{
    private const string MeetingNote = """
        # Project Alpha

        API 응답속도 개선

        - [x] p95 기준 확정
        - [ ] GPU fallback 테스트
        - [ ] 릴리스 노트 작성

        #backend #v1
        """;

    [Fact]
    public void Items_are_read_out_of_the_markdown_in_order()
    {
        var items = ChecklistParser.Parse(MeetingNote);

        Assert.Equal(3, items.Count);
        Assert.Equal("p95 기준 확정", items[0].Text);
        Assert.True(items[0].IsDone);
        Assert.Equal("GPU fallback 테스트", items[1].Text);
        Assert.False(items[1].IsDone);
        Assert.Equal("릴리스 노트 작성", items[2].Text);
    }

    /// <summary>Line indexes are how the UI addresses an item, so they must be the real line numbers.</summary>
    [Fact]
    public void Items_carry_the_line_they_sit_on()
    {
        var items = ChecklistParser.Parse(MeetingNote);

        Assert.Equal([4, 5, 6], items.Select(i => i.LineIndex));
    }

    [Theory]
    [InlineData("- [x] 완료")]
    [InlineData("- [X] 완료")]
    [InlineData("* [x] 완료")]
    [InlineData("+ [x] 완료")]
    [InlineData("  - [x] 완료")]
    public void Common_checklist_spellings_are_all_recognized(string line)
    {
        var items = ChecklistParser.Parse(line);

        Assert.Single(items);
        Assert.True(items[0].IsDone);
        Assert.Equal("완료", items[0].Text);
    }

    [Theory]
    [InlineData("일반 문장")]
    [InlineData("- 그냥 목록")]
    [InlineData("# 제목")]
    [InlineData("배열 표기 [0] 은 항목이 아니다")]
    public void Lines_that_are_not_checklist_items_are_ignored(string line)
    {
        Assert.Empty(ChecklistParser.Parse(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("체크박스가 전혀 없는 메모")]
    public void Content_without_items_yields_an_empty_list(string content)
    {
        Assert.Empty(ChecklistParser.Parse(content));
    }

    [Fact]
    public void Setting_an_item_state_rewrites_only_that_line()
    {
        var updated = ChecklistParser.SetItemState(MeetingNote, lineIndex: 5, isDone: true);

        Assert.Contains("- [x] GPU fallback 테스트", updated, StringComparison.Ordinal);
        Assert.Contains("- [x] p95 기준 확정", updated, StringComparison.Ordinal);
        Assert.Contains("- [ ] 릴리스 노트 작성", updated, StringComparison.Ordinal);
        Assert.Contains("#backend #v1", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unchecking_an_item_works_the_same_way()
    {
        var updated = ChecklistParser.SetItemState(MeetingNote, lineIndex: 4, isDone: false);

        Assert.Contains("- [ ] p95 기준 확정", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Indentation_survives_a_state_change()
    {
        var updated = ChecklistParser.SetItemState("    - [ ] 들여쓴 항목", 0, isDone: true);

        Assert.Equal("    - [x] 들여쓴 항목", updated);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    public void Addressing_a_line_that_is_not_an_item_leaves_the_note_untouched(int lineIndex)
    {
        Assert.Equal(MeetingNote, ChecklistParser.SetItemState(MeetingNote, lineIndex, isDone: true));
    }

    [Fact]
    public void Progress_counts_done_against_total()
    {
        Assert.Equal((1, 3), ChecklistParser.Progress(MeetingNote));
        Assert.Equal((0, 0), ChecklistParser.Progress("체크리스트 없음"));
    }

    /// <summary>
    /// The editor produces its markers through MarkdownEditing, so what that writes has to be what
    /// this reads — otherwise the projection quietly misses items the user can plainly see.
    /// </summary>
    [Fact]
    public void What_the_editor_writes_is_what_the_parser_reads()
    {
        var authored = MarkdownEditing.ApplyLineStyle(
            new NoteTextState("회의 준비", 0, 0),
            LineStyle.Checklist);

        var items = ChecklistParser.Parse(authored.Text);

        Assert.Single(items);
        Assert.Equal("회의 준비", items[0].Text);
        Assert.False(items[0].IsDone);
    }

    [Fact]
    public void A_list_continued_with_enter_is_also_readable()
    {
        var first = MarkdownEditing.ApplyLineStyle(new NoteTextState("첫째", 0, 0), LineStyle.Checklist);
        var atEnd = first with { SelectionStart = first.Text.Length, SelectionLength = 0 };
        var second = MarkdownEditing.ContinueListOnEnter(atEnd);

        Assert.NotNull(second);
        var withText = second.Value.Text + "둘째";

        Assert.Equal(2, ChecklistParser.Parse(withText).Count);
    }
}
