using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

/// <summary>
/// Chunking decides what semantic search can find: a chunk that mixes three subjects matches none
/// of them, and one cut through the middle of a sentence loses the fact it was carrying.
/// </summary>
public class NoteChunkerTests
{
    [Fact]
    public void An_empty_note_has_nothing_to_embed()
    {
        Assert.Empty(NoteChunker.Split(null));
        Assert.Empty(NoteChunker.Split("   \n\n  "));
    }

    [Fact]
    public void A_short_note_is_one_chunk()
    {
        var chunks = NoteChunker.Split("회의는 화요일 10시");

        Assert.Equal("회의는 화요일 10시", Assert.Single(chunks).Text);
        Assert.Equal(0, chunks[0].Ordinal);
    }

    /// <summary>A blank line is the writer saying "different subject", which is exactly the seam wanted.</summary>
    [Fact]
    public void Paragraphs_are_packed_together_until_the_limit()
    {
        var content = string.Join("\n\n", Enumerable.Range(0, 6).Select(i => new string('가', 120) + i));

        var chunks = NoteChunker.Split(content);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= NoteChunker.MaxChars));
    }

    [Fact]
    public void Ordinals_are_dense_and_start_at_zero()
    {
        var content = string.Join("\n\n", Enumerable.Range(0, 8).Select(i => new string('나', 200) + i));

        var chunks = NoteChunker.Split(content);

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.Ordinal));
    }

    /// <summary>An unformatted wall of text is the common case in a note nobody tidied.</summary>
    [Fact]
    public void A_single_over_long_paragraph_is_split_on_word_boundaries()
    {
        var content = string.Join(" ", Enumerable.Range(0, 400).Select(i => "낱말" + i));

        var chunks = NoteChunker.Split(content);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= NoteChunker.MaxChars));

        // Every word survives somewhere, so nothing is lost at a seam.
        var joined = string.Join(" ", chunks.Select(chunk => chunk.Text));
        Assert.Contains("낱말0 ", joined, StringComparison.Ordinal);
        Assert.Contains("낱말399", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_chunks_overlap_so_a_seam_is_covered_twice()
    {
        var content = string.Join(" ", Enumerable.Range(0, 400).Select(i => "낱말" + i));

        var chunks = NoteChunker.Split(content);
        var total = chunks.Sum(chunk => chunk.Text.Length);

        Assert.True(total > content.Length, "overlapping chunks must repeat some text");
    }

    [Fact]
    public void Blank_line_runs_do_not_produce_empty_chunks()
    {
        var chunks = NoteChunker.Split("첫 문단\n\n\n\n둘째 문단");

        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
    }

    /// <summary>
    /// Summarisation reuses the splitter at a much larger size. 500 characters is the size at
    /// which one chunk still means one thing for an embedding; a summary of 500 characters is
    /// barely shorter than the text it came from.
    /// </summary>
    [Fact]
    public void A_caller_can_ask_for_sections_instead_of_chunks()
    {
        var content = string.Join("\n\n", Enumerable.Range(0, 8).Select(i => new string('가', 400)));

        var chunks = NoteChunker.Split(content);
        var sections = NoteChunker.Split(content, maxChars: 3000, overlapChars: 0);

        Assert.True(sections.Count < chunks.Count);
        Assert.All(sections, section => Assert.True(section.Text.Length <= 3000));
    }

    [Fact]
    public void Sections_can_be_asked_for_without_overlap()
    {
        var content = new string('가', 5000);

        var sections = NoteChunker.Split(content, maxChars: 2000, overlapChars: 0);

        // With no overlap the pieces add up to the original rather than repeating its seams.
        Assert.Equal(content.Length, sections.Sum(section => section.Text.Length));
    }

    [Fact]
    public void Ordinals_stay_sequential_at_any_size()
    {
        var content = string.Join("\n\n", Enumerable.Range(0, 10).Select(i => new string('나', 900)));

        var sections = NoteChunker.Split(content, maxChars: 2500, overlapChars: 0);

        Assert.Equal(Enumerable.Range(0, sections.Count), sections.Select(s => s.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_section_size_too_small_to_hold_anything_is_refused(int maxChars)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoteChunker.Split("본문", maxChars, 0));
    }
}
