using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class TagParserTests
{
    [Fact]
    public void Tags_are_read_from_the_body_in_order()
    {
        var tags = TagParser.Parse("API 응답속도 개선\n\n#backend #v1");

        Assert.Equal(["backend", "v1"], tags.Select(t => t.Name));
    }

    [Fact]
    public void Korean_tags_are_recognized()
    {
        var tags = TagParser.Parse("금요일 회의 준비 #회의록 #프로젝트-알파");

        Assert.Equal(["회의록", "프로젝트-알파"], tags.Select(t => t.Name));
    }

    /// <summary>
    /// The collision that matters: a note is Markdown, so "# 제목" is a heading and must never
    /// become a tag named 제목.
    /// </summary>
    [Theory]
    [InlineData("# 제목")]
    [InlineData("## 부제목")]
    [InlineData("### 소제목")]
    [InlineData("#")]
    [InlineData("# ")]
    public void Markdown_headings_are_not_tags(string content)
    {
        Assert.Empty(TagParser.Parse(content));
    }

    [Fact]
    public void A_heading_and_a_tag_can_coexist_in_one_note()
    {
        var tags = TagParser.Parse("# Project Alpha\n\n본문입니다\n\n#backend");

        Assert.Single(tags);
        Assert.Equal("backend", tags[0].Name);
    }

    [Theory]
    [InlineData("a#b")]
    [InlineData("이슈 #123")]
    [InlineData("PR #4821 리뷰")]
    public void Things_that_only_look_like_tags_are_ignored(string content)
    {
        Assert.Empty(TagParser.Parse(content));
    }

    /// <summary>
    /// An all-digit token is an issue or PR number far more often than a label, so it is not a
    /// tag. Anything with a letter in it is taken at face value: a word like "ffffff" may be a hex
    /// colour, but no rule can separate that from a legitimate tag without also rejecting real
    /// ones like #abcdef.
    /// </summary>
    [Theory]
    [InlineData("#123456", 0)]
    [InlineData("#abc123", 1)]
    [InlineData("#ffffff", 1)]
    [InlineData("#v1", 1)]
    public void Digits_alone_are_not_a_tag_but_anything_with_a_letter_is(string content, int expected)
    {
        Assert.Equal(expected, TagParser.Parse(content).Count);
    }

    [Fact]
    public void A_tag_at_the_very_start_of_a_note_counts()
    {
        Assert.Single(TagParser.Parse("#backend 로 시작하는 메모"));
    }

    [Fact]
    public void Tags_in_brackets_or_parentheses_count()
    {
        Assert.Equal(["backend", "v1"], TagParser.Parse("(#backend) [#v1]").Select(t => t.Name));
    }

    [Fact]
    public void Repeating_a_tag_yields_one_entry_keeping_the_first_spelling()
    {
        var tags = TagParser.Parse("#Backend 작업\n다시 #backend 언급\n또 #BACKEND");

        Assert.Single(tags);
        Assert.Equal("Backend", tags[0].Name);
        Assert.Equal("backend", tags[0].NormalizedName);
    }

    [Fact]
    public void Every_mention_is_available_separately_from_the_distinct_set()
    {
        var content = "#backend 와 #backend 두 번";

        Assert.Equal(2, TagParser.FindMentions(content).Count);
        Assert.Single(TagParser.Parse(content));
    }

    [Fact]
    public void The_index_points_at_the_hash()
    {
        var mention = TagParser.Parse("메모 #backend").Single();

        Assert.Equal("메모 ".Length, mention.Index);
    }

    [Fact]
    public void An_over_long_token_is_not_taken_whole()
    {
        var tags = TagParser.Parse("#" + new string('a', TagParser.MaxLength + 20));

        Assert.Single(tags);
        Assert.Equal(TagParser.MaxLength, tags[0].Name.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("태그가 전혀 없는 메모")]
    public void Content_without_tags_yields_nothing(string content)
    {
        Assert.Empty(TagParser.Parse(content));
    }

    /// <summary>Tags and checklists share a note body and must not confuse each other.</summary>
    [Fact]
    public void Tags_and_checklists_coexist()
    {
        const string note = """
            # Project Alpha

            - [x] p95 기준 확정
            - [ ] GPU fallback 테스트

            #backend #v1
            """;

        Assert.Equal(["backend", "v1"], TagParser.Parse(note).Select(t => t.Name));
        Assert.Equal(2, ChecklistParser.Parse(note).Count);
    }
}
