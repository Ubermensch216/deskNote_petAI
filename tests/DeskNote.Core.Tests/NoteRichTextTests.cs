using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

/// <summary>
/// Guards the boundary between the stored Markdown and what the editor draws.
/// </summary>
/// <remarks>
/// The editor now hides emphasis markers, which means every save runs the note back through this
/// conversion. A bug here does not show up as a wrong-looking note — it shows up as a note that
/// lost a sentence, and the user finds out later. So the round trip is tested as a property over a
/// corpus rather than only on the cases that were easy to think of.
/// </remarks>
public class NoteRichTextTests
{
    [Theory]
    [InlineData("~~배포~~ 취소", "배포 취소", 0, 2, InlineStyle.Strikethrough)]
    [InlineData("**중요** 확인", "중요 확인", 0, 2, InlineStyle.Bold)]
    [InlineData("*기울임*", "기울임", 0, 3, InlineStyle.Italic)]
    [InlineData("<u>밑줄</u>", "밑줄", 0, 2, InlineStyle.Underline)]
    [InlineData("앞 ~~가운데~~ 뒤", "앞 가운데 뒤", 2, 3, InlineStyle.Strikethrough)]
    public void Markers_are_hidden_and_become_emphasis(
        string markdown,
        string shown,
        int start,
        int length,
        InlineStyle style)
    {
        var rich = NoteRichText.FromMarkdown(markdown);

        Assert.Equal(shown, rich.Text);
        Assert.Equal(new RichSpan(start, length, style), Assert.Single(rich.Spans));
    }

    [Fact]
    public void Nested_emphasis_carries_both_styles()
    {
        var rich = NoteRichText.FromMarkdown("**굵고 ~~그은~~ 글**");

        Assert.Equal("굵고 그은 글", rich.Text);
        Assert.Equal(
            [
                new RichSpan(0, 3, InlineStyle.Bold),
                new RichSpan(3, 2, InlineStyle.Bold | InlineStyle.Strikethrough),
                new RichSpan(5, 2, InlineStyle.Bold),
            ],
            rich.Spans);
    }

    /// <summary>
    /// A bullet written with an asterisk opens with the italic marker. Reading it as emphasis would
    /// pair it with the next asterisk on the line and eat both, along with the bullet itself.
    /// </summary>
    [Fact]
    public void A_star_bullet_is_not_read_as_emphasis()
    {
        var rich = NoteRichText.FromMarkdown("* 항목 *하나* 더");

        Assert.Equal("* 항목 하나 더", rich.Text);
        Assert.Equal(new RichSpan(5, 2, InlineStyle.Italic), Assert.Single(rich.Spans));
    }

    [Theory]
    [InlineData("- [ ] 할 일")]
    [InlineData("- [x] 끝난 일")]
    [InlineData("# 제목")]
    [InlineData("### 작은 제목")]
    [InlineData("1. 첫째")]
    public void Line_markers_stay_visible(string markdown)
    {
        var rich = NoteRichText.FromMarkdown(markdown);

        Assert.Equal(markdown, rich.Text);
        Assert.Empty(rich.Spans);
    }

    [Fact]
    public void A_checklist_line_still_carries_emphasis_after_its_marker()
    {
        var rich = NoteRichText.FromMarkdown("- [x] ~~보낸 메일~~");

        Assert.Equal("- [x] 보낸 메일", rich.Text);
        Assert.Equal(new RichSpan(6, 5, InlineStyle.Strikethrough), Assert.Single(rich.Spans));
    }

    /// <summary>
    /// The fallback that makes the conversion safe. Anything the writer would not spell the same
    /// way comes back as itself, markers and all, rather than being rewritten into something else.
    /// </summary>
    [Theory]
    [InlineData("닫히지 않은 ~~마커")]
    [InlineData("빈 강조 ****")]
    [InlineData("별 하나 * 그리고 곱하기")]
    [InlineData("~~**뒤집힌 중첩**~~")]
    public void Anything_that_would_not_round_trip_is_left_as_source(string markdown)
    {
        var rich = NoteRichText.FromMarkdown(markdown);

        Assert.Equal(markdown, rich.Text);
        Assert.Empty(rich.Spans);
    }

    [Fact]
    public void Blank_lines_between_paragraphs_survive()
    {
        const string markdown = "첫 줄\n\n셋째 줄";

        var rich = NoteRichText.FromMarkdown(markdown);

        Assert.Equal(markdown, rich.Text);
        Assert.Equal(markdown, NoteRichText.ToMarkdown(rich.Text, rich.Spans));
    }

    [Fact]
    public void An_empty_note_stays_empty()
    {
        Assert.Equal(RichNoteText.Empty, NoteRichText.FromMarkdown(null));
        Assert.Equal(RichNoteText.Empty, NoteRichText.FromMarkdown(string.Empty));
        Assert.Equal(string.Empty, NoteRichText.ToMarkdown(string.Empty, []));
    }

    /// <summary>
    /// The editor reports its paragraph breaks as bare CR, so the text coming back has to be
    /// normalized before it is stored — exactly as the plain TextBox's did.
    /// </summary>
    [Fact]
    public void Carriage_returns_from_the_editor_are_normalized()
    {
        Assert.Equal("첫 줄\n둘째 줄", NoteRichText.ToMarkdown("첫 줄\r둘째 줄", []));
    }

    [Fact]
    public void Emphasis_applied_in_the_editor_becomes_markers()
    {
        var markdown = NoteRichText.ToMarkdown(
            "배포 취소",
            [new RichSpan(0, 2, InlineStyle.Strikethrough)]);

        Assert.Equal("~~배포~~ 취소", markdown);
    }

    /// <summary>
    /// A rich edit control is free to report overlapping or oversized ranges. None of that may
    /// reach the stored text.
    /// </summary>
    /// <remarks>
    /// Bold covers "ab" and strikethrough "bc", which no single nesting can spell. The writer
    /// closes bold before the run that is struck through but not bold and opens a fresh pair, so
    /// the styles land on exactly the characters they were reported on — and, read back, produce
    /// the same three runs.
    /// </remarks>
    [Fact]
    public void Overlapping_and_oversized_spans_are_tolerated()
    {
        var markdown = NoteRichText.ToMarkdown(
            "abc",
            [
                new RichSpan(0, 2, InlineStyle.Bold),
                new RichSpan(1, 99, InlineStyle.Strikethrough),
                new RichSpan(-5, 3, InlineStyle.Bold),
            ]);

        Assert.Equal("**a~~b~~**~~c~~", markdown);

        var rich = NoteRichText.FromMarkdown(markdown);
        Assert.Equal("abc", rich.Text);
        Assert.Equal(
            [
                new RichSpan(0, 1, InlineStyle.Bold),
                new RichSpan(1, 1, InlineStyle.Bold | InlineStyle.Strikethrough),
                new RichSpan(2, 1, InlineStyle.Strikethrough),
            ],
            rich.Spans);
    }

    public static TheoryData<string> Corpus() =>
    [
        string.Empty,
        "그냥 한 줄",
        "~~배포~~ 취소",
        "**굵게** 그리고 *기울임*",
        "<u>밑줄</u>과 ~~취소선~~",
        "**굵고 ~~그은~~ 글**",
        "- [ ] 할 일\n- [x] ~~끝난 일~~\n- [ ] 남은 일",
        "# 제목\n\n본문 *한* 줄\n\n## 둘째\n- 목록",
        "1. 첫째\n2. 둘째 **강조**\n3. 셋째",
        "* 별 목록 *기울임* 포함",
        "닫히지 않은 ~~마커",
        "빈 강조 ****",
        "곱하기 3 * 4 = 12",
        "코드 `**리터럴**` 유지",
        "여러\n줄\n\n\n빈 줄 셋",
        "  들여쓴 줄\n\t탭으로 들여쓴 줄",
        "이모지 🐰 와 한자 漢字 그리고 ~~취소~~",
        "URL https://example.com/a_b_c 안의 밑줄",
        "끝에 공백 있는 줄   \n다음 줄",
        "\n앞이 빈 줄",
        "뒤가 빈 줄\n",
    ];

    /// <summary>
    /// The property that has to hold for every note in the database: showing it and storing it
    /// again gives back exactly what was stored.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void Every_note_survives_the_round_trip(string markdown)
    {
        var rich = NoteRichText.FromMarkdown(markdown);

        Assert.Equal(markdown, NoteRichText.ToMarkdown(rich.Text, rich.Spans));
    }

    /// <summary>
    /// The weaker property that still has to hold when the stronger one does not: whatever the
    /// conversion does to the markers, not one character of what the user can read may go missing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void No_readable_character_is_ever_lost(string markdown)
    {
        var shown = NoteRichText.FromMarkdown(markdown).Text;

        Assert.Equal(
            Readable(markdown),
            Readable(shown));
    }

    private static string Readable(string text) =>
        new(NoteContent.NormalizeLineEndings(text)
            .Where(c => c is not ('*' or '~' or '<' or '>' or 'u' or '/'))
            .ToArray());
}
