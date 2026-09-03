using System.Text;
using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>A stretch of the shown text carrying one emphasis, in shown-text offsets.</summary>
public readonly record struct RichSpan(int Start, int Length, InlineStyle Style);

/// <summary>What the editor shows, and where the emphasis falls on it.</summary>
public sealed record RichNoteText(string Text, IReadOnlyList<RichSpan> Spans)
{
    public static RichNoteText Empty { get; } = new(string.Empty, []);
}

/// <summary>
/// Converts a note body between its stored Markdown and what the editor draws.
/// </summary>
/// <remarks>
/// <para>
/// Storage does not change: the body is Markdown, and <c>- [ ]</c>, <c>#</c> and the rest are still
/// the only original. What changes is that inline emphasis is now <em>drawn</em> rather than shown
/// as source, so <c>~~취소선~~</c> appears struck through with its tildes hidden.
/// </para>
/// <para>
/// Line-level markers stay visible on purpose. The checklist projection, <c>Ctrl+Enter</c>, list
/// continuation and the derived title all read those characters, and hiding them would mean
/// keeping a second, invisible model of the note's structure in the editor and reconciling the two
/// on every keystroke. Emphasis has no such dependents, which is why it is the part that can safely
/// disappear into formatting.
/// </para>
/// </remarks>
public static class NoteRichText
{
    /// <summary>Reads stored Markdown as text to show plus the emphasis to draw on it.</summary>
    public static RichNoteText FromMarkdown(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return RichNoteText.Empty;
        }

        var text = new StringBuilder();
        var spans = new List<RichSpan>();

        var lines = NoteContent.NormalizeLineEndings(markdown).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                text.Append('\n');
            }

            var line = lines[i];
            var prefix = MarkdownEditing.LineMarkerPrefix(line);
            text.Append(prefix);

            foreach (var run in MarkdownInline.Parse(line[prefix.Length..]))
            {
                if (run.Style != InlineStyle.None)
                {
                    spans.Add(new RichSpan(text.Length, run.Text.Length, run.Style));
                }

                text.Append(run.Text);
            }
        }

        return new RichNoteText(text.ToString(), spans);
    }

    /// <summary>Writes what the editor is showing back out as the Markdown to store.</summary>
    public static string ToMarkdown(string? shown, IReadOnlyList<RichSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (string.IsNullOrEmpty(shown))
        {
            return string.Empty;
        }

        var text = NoteContent.NormalizeLineEndings(shown);
        var styles = StyleMap(text.Length, spans);
        var markdown = new StringBuilder();
        var lineStart = 0;

        while (lineStart <= text.Length)
        {
            var newline = text.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? text.Length : newline;

            markdown.Append(MarkdownInline.Write(RunsIn(text, styles, lineStart, lineEnd)));

            if (newline < 0)
            {
                break;
            }

            markdown.Append('\n');
            lineStart = newline + 1;
        }

        return markdown.ToString();
    }

    /// <summary>
    /// The emphasis on each character, as one array.
    /// </summary>
    /// <remarks>
    /// Spans arrive as whatever the editor reported and may overlap, nest or run past the end of
    /// the text — a rich edit control is under no obligation to hand back tidy, disjoint ranges.
    /// Flattening them per character makes all of that irrelevant, and it is the shape the run
    /// builder wants anyway.
    /// </remarks>
    private static InlineStyle[] StyleMap(int length, IReadOnlyList<RichSpan> spans)
    {
        var styles = new InlineStyle[length];

        foreach (var span in spans)
        {
            var start = Math.Clamp(span.Start, 0, length);
            var end = Math.Clamp(span.Start + span.Length, start, length);

            for (var i = start; i < end; i++)
            {
                styles[i] |= span.Style;
            }
        }

        return styles;
    }

    private static List<InlineRun> RunsIn(string text, InlineStyle[] styles, int start, int end)
    {
        var runs = new List<InlineRun>();
        var runStart = start;

        for (var i = start; i < end; i++)
        {
            if (styles[i] == styles[runStart])
            {
                continue;
            }

            runs.Add(new InlineRun(text[runStart..i], styles[runStart]));
            runStart = i;
        }

        if (runStart < end)
        {
            runs.Add(new InlineRun(text[runStart..end], styles[runStart]));
        }

        return runs;
    }
}
