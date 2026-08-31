using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

/// <summary>
/// Guards the line between markup the note draws and markup it only spells out.
/// </summary>
/// <remarks>
/// Two failures are worth separating here. Leaving a heading's hashes in is cosmetic — the user
/// reads past it. Dropping a <c>- [ ]</c> or a <c>#태그</c> is not: the first stops toggling and
/// the second stops filing the note. So the "kept" tests below are the load-bearing ones.
/// </remarks>
public class AiTextCleanupTests
{
    [Theory]
    [InlineData("**중요한 회의**", "중요한 회의")]
    [InlineData("*기울임*", "기울임")]
    [InlineData("~~취소된 항목~~", "취소된 항목")]
    [InlineData("`NoteRepository`", "NoteRepository")]
    [InlineData("<u>밑줄</u>", "밑줄")]
    [InlineData("**굵게** 그리고 *기울임*", "굵게 그리고 기울임")]
    public void Inline_markers_the_note_cannot_draw_are_removed(string proposed, string expected)
    {
        Assert.Equal(expected, AiTextCleanup.ToNoteText(proposed));
    }

    [Theory]
    [InlineData("# 회의 준비", "회의 준비")]
    [InlineData("## 회의 준비", "회의 준비")]
    [InlineData("###### 회의 준비", "회의 준비")]
    [InlineData("> 인용된 줄", "인용된 줄")]
    public void Heading_and_quote_markers_are_removed(string proposed, string expected)
    {
        Assert.Equal(expected, AiTextCleanup.ToNoteText(proposed));
    }

    /// <summary>
    /// The three notations the note is built on. A bullet continues on Enter, a checkbox toggles,
    /// and a hashtag becomes a row in the tags table — none of them is decoration.
    /// </summary>
    [Theory]
    [InlineData("- 자료 정리")]
    [InlineData("- [ ] 초대장 보내기")]
    [InlineData("- [x] 회의실 예약")]
    [InlineData("1. 첫 번째 단계")]
    [InlineData("#backend 태그가 붙은 메모")]
    public void The_notation_the_note_actually_uses_survives(string proposed)
    {
        Assert.Equal(proposed, AiTextCleanup.ToNoteText(proposed));
    }

    /// <summary>A heading needs a space after the hashes, which is what keeps a hashtag a hashtag.</summary>
    [Fact]
    public void A_hashtag_is_not_mistaken_for_a_heading()
    {
        Assert.Equal("#backend", AiTextCleanup.ToNoteText("#backend"));
    }

    [Theory]
    [InlineData("* 별표 목록", "- 별표 목록")]
    [InlineData("+ 더하기 목록", "- 더하기 목록")]
    [InlineData("* [ ] 별표 체크박스", "- [ ] 별표 체크박스")]
    [InlineData("- [X] 대문자 체크", "- [x] 대문자 체크")]
    public void List_markers_are_rewritten_in_the_notes_own_spelling(string proposed, string expected)
    {
        Assert.Equal(expected, AiTextCleanup.ToNoteText(proposed));
    }

    [Fact]
    public void Nested_list_indentation_is_kept()
    {
        Assert.Equal("- 상위\n  - 하위", AiTextCleanup.ToNoteText("- 상위\n  * 하위"));
    }

    /// <summary>
    /// The fence goes; the blank line that separated it from the heading was in the note's own
    /// shape and stays.
    /// </summary>
    [Fact]
    public void A_fenced_block_loses_its_fence_and_keeps_its_lines()
    {
        Assert.Equal(
            "회의 준비\n\ndotnet build",
            AiTextCleanup.ToNoteText("## 회의 준비\n\n```bash\ndotnet build\n```"));
    }

    [Fact]
    public void A_horizontal_rule_is_dropped()
    {
        Assert.Equal("위\n\n아래", AiTextCleanup.ToNoteText("위\n\n---\n\n아래"));
    }

    /// <summary>
    /// A table is column rules a single-column note has nowhere to put, so the row is flattened
    /// into the cells it was holding rather than left as a wall of pipes.
    /// </summary>
    [Fact]
    public void A_table_becomes_one_line_per_row()
    {
        var cleaned = AiTextCleanup.ToNoteText(
            "| 이름 | 담당 |\n|------|------|\n| 로그인 | 김 |\n| 결제 | 이 |");

        Assert.Equal("이름 · 담당\n로그인 · 김\n결제 · 이", cleaned);
    }

    [Fact]
    public void A_link_keeps_both_its_label_and_its_address()
    {
        Assert.Equal(
            "자료는 회의록 (https://example.com/notes) 에 있음",
            AiTextCleanup.ToNoteText("자료는 [회의록](https://example.com/notes) 에 있음"));
    }

    [Fact]
    public void A_link_whose_label_is_its_address_is_written_once()
    {
        Assert.Equal(
            "https://example.com",
            AiTextCleanup.ToNoteText("[https://example.com](https://example.com)"));
    }

    /// <summary>An image reference draws nothing in a text box, so it contributes nothing.</summary>
    [Fact]
    public void An_image_reference_is_dropped()
    {
        Assert.Equal("도면 참고", AiTextCleanup.ToNoteText("![도면](diagram.png)도면 참고"));
    }

    /// <summary>
    /// Where this parts company with title stripping, which removes every asterisk it finds.
    /// </summary>
    [Theory]
    [InlineData("2 * 3 = 6")]
    [InlineData("note_repository 를 확인")]
    [InlineData("별표 하나만 * 남은 줄")]
    public void Unpaired_markers_are_left_as_the_characters_they_are(string proposed)
    {
        Assert.Equal(proposed, AiTextCleanup.ToNoteText(proposed));
    }

    [Fact]
    public void Blank_runs_left_behind_by_dropped_lines_collapse_to_one()
    {
        Assert.Equal("첫 문단\n\n둘째 문단", AiTextCleanup.ToNoteText("첫 문단\n\n\n\n둘째 문단"));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        Assert.Equal("본문", AiTextCleanup.ToNoteText("\n\n  본문   \n\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void Nothing_in_gives_nothing_out(string? proposed)
    {
        Assert.Equal(string.Empty, AiTextCleanup.ToNoteText(proposed));
    }

    /// <summary>Prose the model wrote plainly has to come back the way it went in.</summary>
    [Fact]
    public void Plain_prose_is_untouched()
    {
        const string proposed = "금요일 회의는 오후 3시로 옮겼고, 자료는 목요일까지 모으기로 했다.";

        Assert.Equal(proposed, AiTextCleanup.ToNoteText(proposed));
    }

    /// <summary>
    /// The shape a summary actually arrives in, run end to end: a heading, emphasis, a rewritten
    /// bullet and a checkbox that has to still toggle afterwards.
    /// </summary>
    [Fact]
    public void A_whole_proposal_reads_as_plain_note_text()
    {
        var cleaned = AiTextCleanup.ToNoteText(
            """
            ## 회의 요약

            * **일정**: 금요일 오후 3시
            * 장소는 `3층 회의실`

            ---

            ### 할 일
            - [ ] 자료 모으기
            - [X] 회의실 예약 #회의
            """);

        var expected = NoteContent.NormalizeLineEndings(
            """
            회의 요약

            - 일정: 금요일 오후 3시
            - 장소는 3층 회의실

            할 일
            - [ ] 자료 모으기
            - [x] 회의실 예약 #회의
            """);

        Assert.Equal(expected, cleaned);
    }
}
