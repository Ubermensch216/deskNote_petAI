using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class TextDiffTests
{
    private static string Apply(string current, TextReplacement edit) =>
        current.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Insert);

    [Theory]
    [InlineData("회의 준비", "회의 **준비**")]
    [InlineData("회의 **준비**", "회의 준비")]
    [InlineData("", "새 메모")]
    [InlineData("지울 내용", "")]
    [InlineData("- 첫째\n둘째", "- 첫째\n- 둘째")]
    [InlineData("- [ ] 항목", "- [x] 항목")]
    [InlineData("aaaa", "aaa")]
    [InlineData("aaa", "aaaa")]
    public void The_edit_reproduces_the_updated_text(string current, string updated)
    {
        Assert.Equal(updated, Apply(current, TextDiff.Minimal(current, updated)));
    }

    [Fact]
    public void Identical_text_produces_no_edit()
    {
        Assert.True(TextDiff.Minimal("같은 내용", "같은 내용").IsEmpty);
    }

    /// <summary>The point of the diff: touch as little of the editor's text as possible.</summary>
    [Fact]
    public void Only_the_changed_span_is_replaced()
    {
        var edit = TextDiff.Minimal("앞부분 중간 뒷부분", "앞부분 바뀜 뒷부분");

        Assert.Equal(4, edit.Start);
        Assert.Equal("바뀜", edit.Insert);
        Assert.Equal(2, edit.Length);
    }

    [Fact]
    public void An_insertion_replaces_nothing()
    {
        var edit = TextDiff.Minimal("회의", "회의 준비");

        Assert.Equal(0, edit.Length);
        Assert.Equal(" 준비", edit.Insert);
    }

    /// <summary>
    /// Repeated characters are where a naive prefix/suffix scan double-counts and yields a
    /// negative replacement length.
    /// </summary>
    [Theory]
    [InlineData("****", "**")]
    [InlineData("**", "****")]
    [InlineData("---", "-")]
    [InlineData("ㄱㄱㄱㄱ", "ㄱㄱ")]
    public void Repeated_characters_still_produce_a_valid_edit(string current, string updated)
    {
        var edit = TextDiff.Minimal(current, updated);

        Assert.InRange(edit.Start, 0, current.Length);
        Assert.InRange(edit.Length, 0, current.Length - edit.Start);
        Assert.Equal(updated, Apply(current, edit));
    }
}
