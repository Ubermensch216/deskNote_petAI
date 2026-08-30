namespace DeskNote.Core.Services;

/// <summary>One embeddable slice of a note.</summary>
/// <param name="Ordinal">Position within the note, stable for as long as the text above it is.</param>
/// <param name="Text">The slice itself.</param>
public readonly record struct NoteChunk(int Ordinal, string Text);

/// <summary>
/// Splits a note into the pieces that get embedded for semantic search (report p8).
/// </summary>
/// <remarks>
/// <para>
/// Whole-note embedding is the obvious approach and the wrong one: one vector for a note that
/// covers a meeting, a shopping list and a phone number describes none of them, and long notes
/// dilute until they match nothing. Splitting on blank lines keeps a chunk to one topic, because
/// a blank line is where the writer themselves changed subject.
/// </para>
/// <para>
/// Chunks overlap by a sentence-sized tail so a fact written across a paragraph boundary is not
/// cut in half and lost to both sides.
/// </para>
/// </remarks>
public static class NoteChunker
{
    /// <summary>
    /// Longest chunk in characters. bge-m3 takes far more than this; the limit is about meaning,
    /// not the model — past a few paragraphs a single vector stops standing for anything.
    /// </summary>
    public const int MaxChars = 500;

    /// <summary>Characters of the previous chunk repeated at the start of the next one.</summary>
    public const int OverlapChars = 80;

    /// <summary>Shorter than this and a chunk carries no retrievable meaning of its own.</summary>
    public const int MinChars = 2;

    public static IReadOnlyList<NoteChunk> Split(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var chunks = new List<NoteChunk>();
        var buffer = new System.Text.StringBuilder();

        foreach (var block in Blocks(content))
        {
            if (buffer.Length > 0 && buffer.Length + block.Length + 1 > MaxChars)
            {
                Flush(chunks, buffer);
            }

            // A single block over the limit is split on its own rather than dropped: one very long
            // paragraph is common in a note nobody bothered to format.
            if (block.Length > MaxChars)
            {
                Flush(chunks, buffer);

                foreach (var piece in Hard(block))
                {
                    Add(chunks, piece);
                }

                continue;
            }

            if (buffer.Length > 0)
            {
                buffer.Append('\n');
            }

            buffer.Append(block);
        }

        Flush(chunks, buffer);
        return chunks;
    }

    /// <summary>Paragraphs, with runs of blank lines collapsed into one boundary.</summary>
    private static IEnumerable<string> Blocks(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var block = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (block.Length > 0)
                {
                    yield return block.ToString().Trim();
                    block.Clear();
                }

                continue;
            }

            if (block.Length > 0)
            {
                block.Append('\n');
            }

            block.Append(line.Trim());
        }

        if (block.Length > 0)
        {
            yield return block.ToString().Trim();
        }
    }

    /// <summary>Cuts an over-long block at a space near the limit, so words stay whole.</summary>
    private static IEnumerable<string> Hard(string block)
    {
        var start = 0;

        while (start < block.Length)
        {
            var length = Math.Min(MaxChars, block.Length - start);

            if (start + length < block.Length)
            {
                var lastSpace = block.LastIndexOf(' ', start + length - 1, length);

                if (lastSpace > start)
                {
                    length = lastSpace - start;
                }
            }

            yield return block.Substring(start, length).Trim();

            // Step back by the overlap so the seam is covered by both pieces.
            start += Math.Max(1, length - OverlapChars);
        }
    }

    private static void Flush(List<NoteChunk> chunks, System.Text.StringBuilder buffer)
    {
        if (buffer.Length > 0)
        {
            Add(chunks, buffer.ToString());
            buffer.Clear();
        }
    }

    private static void Add(List<NoteChunk> chunks, string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length >= MinChars)
        {
            chunks.Add(new NoteChunk(chunks.Count, trimmed));
        }
    }
}
