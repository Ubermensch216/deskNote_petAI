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
