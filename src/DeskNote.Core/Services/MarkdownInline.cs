using System.Text;

namespace DeskNote.Core.Services;

/// <summary>
/// The character-level emphasis a stretch of note text can carry.
/// </summary>
/// <remarks>
/// Exactly the four styles a rich edit control formats natively, which is what makes the round trip
/// safe: each one is a property on ITextCharacterFormat, so a style survives being written into the
/// editor and read back out. Inline code is deliberately absent — a backtick span would have to be
/// carried as a font name, and a font is not a reliable place to keep meaning, so backticks stay
/// visible in the note as the source characters they are.
/// </remarks>
[Flags]
public enum InlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
}

/// <summary>One stretch of a line whose every character is formatted the same way.</summary>
public readonly record struct InlineRun(string Text, InlineStyle Style);

/// <summary>
/// Turns the inline Markdown on one line into formatting runs, and back again.
/// </summary>
/// <remarks>
/// <para>
/// The note is stored as Markdown source and always will be — the checklist projection, tag
/// parsing, search snippets, revisions and every AI action read that string. What changes is that
/// the editor no longer <em>shows</em> the source: <c>~~취소선~~</c> is drawn struck through, with
/// the tildes hidden. This type is the boundary between the two, and it is the only place where
/// note text can be silently damaged, so it is written to make that impossible rather than
/// unlikely.
/// </para>
/// <para>
/// The guarantee is enforced at runtime, not argued for: <see cref="Parse"/> serializes the runs it
/// just produced and compares them to the line it was given. Anything that does not come back
/// character for character — an unmatched marker, an ordering this writer would not have chosen, a
/// construct nobody anticipated — is returned as one unformatted run holding the raw line. The
/// worst case is therefore a line that shows its markers, which is exactly what the editor did
/// before. The one thing that cannot happen is a character going missing.
/// </para>
/// </remarks>
public static class MarkdownInline
{
    private const string BoldMarker = "**";
    private const string ItalicMarker = "*";
    private const string StrikethroughMarker = "~~";
    private const string UnderlineOpen = "<u>";
    private const string UnderlineClose = "</u>";

    /// <summary>
    /// Nesting order used when writing, outermost first.
    /// </summary>
    /// <remarks>
    /// A fixed order is what makes the round trip decidable. Without one, <c>**~~a~~**</c> and
    /// <c>~~**a**~~</c> would both be legitimate spellings of the same pair of styles, and the
    /// writer would have no way to reproduce the one the user actually typed.
    /// </remarks>
    private static readonly InlineStyle[] NestingOrder =
    [
        InlineStyle.Bold,
        InlineStyle.Italic,
        InlineStyle.Underline,
        InlineStyle.Strikethrough,
    ];

    /// <summary>
    /// Reads one line of Markdown as formatting runs, or as itself when it cannot be read exactly.
    /// </summary>
    public static IReadOnlyList<InlineRun> Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Length == 0)
        {
            return [];
        }

        var runs = new List<InlineRun>();
        Scan(line, InlineStyle.None, runs);
        var coalesced = Coalesce(runs);

        return string.Equals(Write(coalesced), line, StringComparison.Ordinal)
            ? coalesced
            : [new InlineRun(line, InlineStyle.None)];
    }

    /// <summary>Writes formatting runs back out as Markdown.</summary>
    public static string Write(IReadOnlyList<InlineRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var text = new StringBuilder();
        var open = new List<InlineStyle>();

        foreach (var run in runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }

            // Markers close in the reverse of the order they opened, so a style that has to go
            // takes everything nested inside it with it.
            while (open.Exists(style => !run.Style.HasFlag(style)))
            {
                text.Append(Close(open[^1]));
                open.RemoveAt(open.Count - 1);
            }

            foreach (var style in NestingOrder)
            {
                if (run.Style.HasFlag(style) && !open.Contains(style))
                {
                    text.Append(Open(style));
                    open.Add(style);
                }
            }

            text.Append(run.Text);
        }

        for (var i = open.Count - 1; i >= 0; i--)
        {
            text.Append(Close(open[i]));
        }

        return text.ToString();
    }

    /// <summary>The text a reader sees, with every marker removed.</summary>
    public static string Flatten(IReadOnlyList<InlineRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var text = new StringBuilder();
        foreach (var run in runs)
        {
            text.Append(run.Text);
        }

        return text.ToString();
    }

    private static void Scan(string text, InlineStyle inherited, List<InlineRun> runs)
    {
        var plain = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            if (MatchAt(text, index) is not { } span)
            {
                plain.Append(text[index]);
                index++;
                continue;
            }

            Flush(plain, inherited, runs);

            Scan(text[span.InnerStart..span.InnerEnd], inherited | span.Style, runs);
            index = span.End;
        }

        Flush(plain, inherited, runs);
    }

    private static void Flush(StringBuilder plain, InlineStyle style, List<InlineRun> runs)
    {
        if (plain.Length == 0)
        {
            return;
        }

        runs.Add(new InlineRun(plain.ToString(), style));
        plain.Clear();
    }

    /// <summary>
    /// The delimited span opening at <paramref name="index"/>, if there is one.
    /// </summary>
    /// <remarks>
    /// Bold is tried before italic because <c>**</c> starts with the italic marker, and reading it
    /// as italic would quietly eat one asterisk of every bold span in the note.
    /// </remarks>
    private static Span? MatchAt(string text, int index) =>
        Delimited(text, index, StrikethroughMarker, StrikethroughMarker, InlineStyle.Strikethrough)
        ?? Delimited(text, index, BoldMarker, BoldMarker, InlineStyle.Bold)
        ?? Delimited(text, index, UnderlineOpen, UnderlineClose, InlineStyle.Underline)
        ?? Delimited(text, index, ItalicMarker, ItalicMarker, InlineStyle.Italic);

    private static Span? Delimited(string text, int index, string open, string close, InlineStyle style)
    {
        if (string.CompareOrdinal(text, index, open, 0, open.Length) != 0)
        {
            return null;
        }

        var innerStart = index + open.Length;
        if (innerStart > text.Length)
        {
            return null;
        }

        var closeAt = text.IndexOf(close, innerStart, StringComparison.Ordinal);

        // An empty span has no text to format, and reading "****" as a bold nothing would delete
        // four characters the user can see.
        return closeAt <= innerStart
            ? null
            : new Span(style, innerStart, closeAt, closeAt + close.Length);
    }

    /// <summary>
    /// Merges neighbouring runs that share a style.
    /// </summary>
    /// <remarks>
    /// Scanning produces a run per plain stretch, so <c>a*b*c</c> arrives as three. Left as they
    /// are, the writer would close and reopen markers between two identically formatted runs and
    /// spell <c>**a****b**</c> where the user wrote <c>**ab**</c>.
    /// </remarks>
    private static List<InlineRun> Coalesce(List<InlineRun> runs)
    {
        var merged = new List<InlineRun>(runs.Count);

        foreach (var run in runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }

            if (merged.Count > 0 && merged[^1].Style == run.Style)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };
                continue;
            }

            merged.Add(run);
        }

        return merged;
    }

    private static string Open(InlineStyle style) => style switch
    {
        InlineStyle.Bold => BoldMarker,
        InlineStyle.Italic => ItalicMarker,
        InlineStyle.Underline => UnderlineOpen,
        _ => StrikethroughMarker,
    };

    private static string Close(InlineStyle style) => style switch
    {
        InlineStyle.Bold => BoldMarker,
        InlineStyle.Italic => ItalicMarker,
        InlineStyle.Underline => UnderlineClose,
        _ => StrikethroughMarker,
    };

    private readonly record struct Span(InlineStyle Style, int InnerStart, int InnerEnd, int End);
}
