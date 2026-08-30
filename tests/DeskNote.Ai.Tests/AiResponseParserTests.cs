using DeskNote.Core.Ai;

namespace DeskNote.Ai.Tests;

/// <summary>
/// What the model returns becomes a reminder or a tag, so the validation between them is the
/// last place a hallucinated value can be stopped. Every test here is a value that must not
/// reach the note.
/// </summary>
public class AiResponseParserTests
{
    [Fact]
    public void A_task_keeps_its_title_due_date_priority_and_owner()
    {
        var tasks = AiResponseParser.ReadTasks("""
            {"tasks":[{"title":"보고서 제출","dueAt":"2026-09-01T09:00:00+09:00","priority":"high","assignee":"민수"}]}
            """);

        var task = Assert.Single(tasks);

        Assert.Equal("보고서 제출", task.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(9)), task.DueAt);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal("민수", task.Assignee);
    }

    /// <summary>
    /// A date nobody can parse must not become a reminder at some invented time. The task is still
    /// worth showing, so it survives with no due date rather than being dropped or guessed at.
    /// </summary>
    [Fact]
    public void An_unparsable_due_date_is_discarded_rather_than_guessed()
    {
        var tasks = AiResponseParser.ReadTasks("""
            {"tasks":[{"title":"자료 정리","dueAt":"다음 주쯤","priority":"normal"}]}
            """);

        var task = Assert.Single(tasks);

        Assert.Equal("자료 정리", task.Title);
        Assert.Null(task.DueAt);
    }

    [Fact]
    public void A_task_without_a_title_is_dropped()
    {
        var tasks = AiResponseParser.ReadTasks("""
            {"tasks":[{"title":"   ","priority":"low"},{"title":"진짜 할 일","priority":"low"}]}
            """);

        Assert.Equal("진짜 할 일", Assert.Single(tasks).Title);
    }

    [Fact]
    public void An_unknown_priority_falls_back_to_normal()
    {
        var tasks = AiResponseParser.ReadTasks("""{"tasks":[{"title":"검토","priority":"매우 높음"}]}""");

        Assert.Equal(TaskPriority.Normal, Assert.Single(tasks).Priority);
    }

    [Fact]
    public void An_empty_result_is_an_empty_list_not_a_failure() =>
        Assert.Empty(AiResponseParser.ReadTasks("""{"tasks":[]}"""));

    [Fact]
    public void Output_that_is_not_json_is_reported_as_a_provider_failure() =>
        Assert.Throws<AiProviderException>(() => AiResponseParser.ReadTasks("죄송합니다, 할 일이 없습니다."));

    /// <summary>Constrained decoding should not fence its output, but losing a good answer over it would be silly.</summary>
    [Fact]
    public void A_fenced_json_block_is_still_read()
    {
        var tasks = AiResponseParser.ReadTasks("```json\n{\"tasks\":[{\"title\":\"청소\",\"priority\":\"low\"}]}\n```");

        Assert.Equal("청소", Assert.Single(tasks).Title);
    }

    [Fact]
    public void A_tag_loses_its_hash_and_keeps_its_confidence()
    {
        var tags = AiResponseParser.ReadTags("""{"tags":[{"name":"#backend","confidence":0.8}]}""");

        var tag = Assert.Single(tags);

        Assert.Equal("backend", tag.Name);
        Assert.Equal(0.8, tag.Confidence, 3);
    }

    /// <summary>The note body is the source of truth for tags, and a multi-word tag cannot be typed there.</summary>
    [Fact]
    public void A_tag_containing_whitespace_is_rejected()
    {
        var tags = AiResponseParser.ReadTags("""
            {"tags":[{"name":"백엔드 회의","confidence":0.9},{"name":"백엔드","confidence":0.9}]}
            """);

        Assert.Equal("백엔드", Assert.Single(tags).Name);
    }

    [Fact]
    public void Duplicate_tags_are_offered_once()
    {
        var tags = AiResponseParser.ReadTags("""
            {"tags":[{"name":"api","confidence":0.9},{"name":"API","confidence":0.4}]}
            """);

        Assert.Single(tags);
    }

    [Fact]
    public void At_most_five_tags_are_offered()
    {
        var many = string.Join(",", Enumerable.Range(0, 12).Select(i => $$"""{"name":"t{{i}}","confidence":0.5}"""));

        Assert.Equal(5, AiResponseParser.ReadTags($$"""{"tags":[{{many}}]}""").Count);
    }

    [Fact]
    public void A_confidence_outside_zero_to_one_is_clamped()
    {
        var tags = AiResponseParser.ReadTags("""{"tags":[{"name":"a","confidence":7}]}""");

        Assert.Equal(1, Assert.Single(tags).Confidence);
    }
}
