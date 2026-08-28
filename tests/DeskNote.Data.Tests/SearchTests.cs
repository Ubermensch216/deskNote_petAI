using DeskNote.Core.Models;

namespace DeskNote.Data.Tests;

public class SearchTests
{
    private static Note NewNote(string content, string title = "") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Content = content,
    };

    /// <summary>
    /// The reason the schema uses the trigram tokenizer instead of the default unicode61.
    /// The report's own example (p8): a past note says "관로 누수 조사" and the user later searches
    /// for part of a compound word. unicode61 tokenizes Korean on whitespace, so "수도" would never
    /// match inside "수도관"; trigram indexes 3-character windows and does.
    /// </summary>
    [Theory]
    [InlineData("수도관")]
    [InlineData("도관 누")]
    [InlineData("누수 점검")]
    [InlineData("점검")]
    public async Task Korean_substrings_match_inside_a_compound_word(string query)
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("내일 수도관 누수 점검 예정");
        await db.Notes.AddAsync(note);

        var hits = await db.Search.SearchAsync(query);

        Assert.Single(hits);
        Assert.Equal(note.Id, hits[0].NoteId);
    }

    [Fact]
    public async Task English_search_is_case_insensitive()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Notes.AddAsync(NewNote("Deploy the API gateway on Friday"));

        Assert.Single(await db.Search.SearchAsync("api gateway"));
        Assert.Single(await db.Search.SearchAsync("API GATEWAY"));
    }

    [Fact]
    public async Task Titles_are_searchable_as_well_as_bodies()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Notes.AddAsync(NewNote("본문에는 없는 단어", title: "프로젝트 알파"));

        var hits = await db.Search.SearchAsync("알파");

        Assert.Single(hits);
        Assert.Equal("프로젝트 알파", hits[0].Title);
    }

    [Fact]
    public async Task Deleted_notes_are_excluded_from_results()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("삭제 예정 회의록");
        await db.Notes.AddAsync(note);
        Assert.Single(await db.Search.SearchAsync("회의록"));

        await db.Notes.SoftDeleteAsync(note.Id);

        Assert.Empty(await db.Search.SearchAsync("회의록"));
    }

    [Fact]
    public async Task Editing_a_note_updates_what_search_finds()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("원래 내용 데이터베이스");
        await db.Notes.AddAsync(note);

        await db.Notes.UpdateContentAsync(note.Id, string.Empty, "바뀐 내용 네트워크");

        Assert.Empty(await db.Search.SearchAsync("데이터베이스"));
        Assert.Single(await db.Search.SearchAsync("네트워크"));
    }

    /// <summary>
    /// Moving a note must not disturb the index. The FTS triggers are scoped to
    /// <c>UPDATE OF title, content</c> precisely so that dragging notes around does not churn it.
    /// </summary>
    [Fact]
    public async Task Moving_a_note_does_not_disturb_the_index()
    {
        await using var db = await TestDatabase.CreateAsync();
        var note = NewNote("자리를 옮겨도 검색되는 메모");
        await db.Notes.AddAsync(note);

        await db.Notes.UpdateGeometryAsync(
            note.Id,
            new NoteGeometry(1500, 40, 240, 180, @"\\.\DISPLAY2", NoteSizePreset.Small));
        await db.Notes.UpdateAppearanceAsync(note.Id, NoteColors.Pink, 0.7, alwaysOnTop: false);

        Assert.Single(await db.Search.SearchAsync("검색되는"));
    }

    /// <summary>
    /// Find-as-you-type sends whatever is in the box on every keystroke, including half-typed
    /// FTS5 operator syntax. None of it may throw.
    /// </summary>
    [Theory]
    [InlineData("\"")]
    [InlineData("a\"b")]
    [InlineData("NEAR(")]
    [InlineData("foo AND")]
    [InlineData("*")]
    [InlineData("-회의")]
    [InlineData("(unbalanced")]
    [InlineData("^start")]
    public async Task Fts_operator_characters_in_raw_input_do_not_throw(string query)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Notes.AddAsync(NewNote("아무 내용"));

        var hits = await db.Search.SearchAsync(query);

        Assert.NotNull(hits);
    }

    /// <summary>
    /// The trigram tokenizer cannot match fewer than three characters, so one- and two-character
    /// queries take the LIKE fallback. They still have to find the note.
    /// </summary>
    [Theory]
    [InlineData("회")]
    [InlineData("회의")]
    [InlineData("AP")]
    public async Task Queries_shorter_than_a_trigram_still_find_the_note(string query)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Notes.AddAsync(NewNote("회의 준비 APIs"));

        var hits = await db.Search.SearchAsync(query);

        Assert.Single(hits);
    }

    [Fact]
    public async Task Empty_or_whitespace_queries_return_nothing_rather_than_everything()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Notes.AddAsync(NewNote("메모 하나"));

        Assert.Empty(await db.Search.SearchAsync(""));
        Assert.Empty(await db.Search.SearchAsync("   "));
    }

    [Fact]
    public async Task Result_limit_is_respected()
    {
        await using var db = await TestDatabase.CreateAsync();
        for (var i = 0; i < 30; i++)
        {
            await db.Notes.AddAsync(NewNote($"공통 키워드 항목 {i}"));
        }

        var hits = await db.Search.SearchAsync("공통 키워드", limit: 5);

        Assert.Equal(5, hits.Count);
    }
}
