using DeskNote.Core.Models;

namespace DeskNote.Core.Tests;

public class NoteContentTests
{
    /// <summary>A WinUI TextBox reports its line breaks as bare CR, which is what reaches storage.</summary>
    [Fact]
    public void A_bare_carriage_return_from_the_editor_becomes_a_newline()
    {
        Assert.Equal("수도관 누수 점검\nAPI 응답속도 개선",
            NoteContent.NormalizeLineEndings("수도관 누수 점검\rAPI 응답속도 개선"));
    }

    [Fact]
    public void Windows_line_endings_collapse_to_a_single_newline()
    {
        Assert.Equal("첫 줄\n둘째 줄", NoteContent.NormalizeLineEndings("첫 줄\r\n둘째 줄"));
    }

    [Fact]
    public void Mixed_line_endings_all_normalize()
    {
        var normalized = NoteContent.NormalizeLineEndings("a\rb\r\nc\nd");

        Assert.Equal("a\nb\nc\nd", normalized);
        Assert.Equal(4, normalized.Split('\n').Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("한 줄짜리 메모")]
    [InlineData("already\nnormalized")]
    public void Text_without_carriage_returns_is_returned_unchanged(string text)
    {
        Assert.Same(text, NoteContent.NormalizeLineEndings(text));
    }

    [Theory]
    [InlineData("회의 준비", "회의 준비")]
    [InlineData("# 회의 준비\n본문", "회의 준비")]
    [InlineData("- [ ] 우유 사기", "우유 사기")]
    [InlineData("- **배포** 확인", "배포 확인")]
    [InlineData("> 인용된 줄", "인용된 줄")]
    [InlineData("1. 첫 항목", "첫 항목")]
    [InlineData("`dotnet test` 통과", "dotnet test 통과")]
    [InlineData("<u>마감</u> 금요일", "마감 금요일")]
    [InlineData("[기획 보고서](docs/report.pdf) 검토", "기획 보고서 검토")]
    public void The_title_is_the_first_line_without_its_markup(string content, string expected)
    {
        Assert.Equal(expected, NoteContent.DeriveTitle(content));
    }

    /// <summary>Notes very often start with a blank line or two before the thought arrives.</summary>
    [Theory]
    [InlineData("\n\n  \n실제 첫 줄\n둘째 줄", "실제 첫 줄")]
    [InlineData("![](attachments/shot.png)\n스크린샷 설명", "스크린샷 설명")]
    public void Lines_with_nothing_on_them_are_skipped(string content, string expected)
    {
        Assert.Equal(expected, NoteContent.DeriveTitle(content));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n")]
    [InlineData("###")]
    public void A_note_with_no_text_has_no_title(string? content)
    {
        Assert.Equal(string.Empty, NoteContent.DeriveTitle(content));
    }

    /// <summary>Underscores are left alone: stripping them would rename note_repository.</summary>
    [Fact]
    public void Identifiers_keep_their_underscores()
    {
        Assert.Equal("note_repository 정리", NoteContent.DeriveTitle("note_repository 정리"));
    }

    [Fact]
    public void A_long_first_line_is_cut_to_the_title_limit()
    {
        var title = NoteContent.DeriveTitle(new string('가', 200));

        Assert.Equal(NoteContent.MaxTitleLength, title.Length);
    }

    /// <summary>Checklist parsing in M3 depends on every line being separately addressable.</summary>
    [Fact]
    public void Normalized_content_splits_into_the_lines_the_user_typed()
    {
        var lines = NoteContent.NormalizeLineEndings("☑ p95 기준 확정\r☐ GPU fallback 테스트\r☐ 릴리스 노트 작성")
            .Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Equal("☐ 릴리스 노트 작성", lines[2]);
    }
}
