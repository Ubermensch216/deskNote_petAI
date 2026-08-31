using System.Text;
using System.Text.RegularExpressions;
using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>
/// Turns a model's Markdown-flavoured answer into the text a note can actually show.
/// </summary>
/// <remarks>
/// <para>
/// A note is a plain <c>TextBox</c>: one font, one size, no renderer. Markdown a note cannot draw
/// is not formatting there, it is punctuation the reader has to look past — <c>**중요**</c> is
/// four characters of noise where the editor was asked for emphasis it has no way to paint. So an
/// AI proposal is reduced to what the surface can honour before the user ever sees it.
/// </para>
/// <para>
/// What survives is the notation the note itself is built on: <c>- </c> bullets, <c>- [ ]</c>
/// checklists that toggle, numbered lists that continue on Enter, and <c>#태그</c> hashtags that
/// the save pipeline projects into the tags table. Dropping those would not tidy the note, it
/// would break it. Everything else — headings, emphasis, code spans, rules, fences, tables — is
/// carried by nothing at all once it lands, and goes.
/// </para>
/// <para>
/// The prompt already asks the model for this shape. This runs anyway: formatting instructions are
/// the first thing a small local model drops, and a note is not the place to find that out.
/// </para>
/// </remarks>
public static partial class AiTextCleanup
{
    /// <summary>Separator between the cells of a flattened table row.</summary>
    private const string CellSeparator = " · ";

    /// <summary>Matches a fence line, opening or closing, with or without a language tag.</summary>
    [GeneratedRegex(@"^[ \t]*(?:```|~~~)", RegexOptions.CultureInvariant)]
    private static partial Regex CodeFence { get; }

    /// <summary>Matches a thematic break: three or more of the same marker and nothing else.</summary>
    [GeneratedRegex(@"^[ \t]*([-*_])[ \t]*(?:\1[ \t]*){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalRule { get; }

    /// <summary>Matches a heading line with no text on it, which is a marker and nothing else.</summary>
    [GeneratedRegex(@"^[ \t]*\#{1,6}[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EmptyHeading { get; }

    /// <summary>Matches the rule under a table header, which draws a line the note cannot draw.</summary>
    [GeneratedRegex(@"^[ \t]*\|[\s:|-]*\|[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparator { get; }

    /// <summary>Matches a table row, whose pipes are column rules rather than characters.</summary>
    [GeneratedRegex(@"^[ \t]*\|.*\|[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableRow { get; }

    /// <summary>Matches leading blockquote markers, however many are stacked.</summary>
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:>[ \t]?)+", RegexOptions.CultureInvariant)]
    private static partial Regex QuoteMarker { get; }

    /// <summary>
    /// Matches the line's indent and its leading marker.
    /// </summary>
    /// <remarks>
    /// A heading needs whitespace after the hashes, which is what keeps <c>#backend</c> a hashtag
    /// rather than a heading whose marker is about to be removed.
    /// </remarks>
    [GeneratedRegex(
        @"^(?<indent>[ \t]*)(?:(?<check>[-*+][ \t]+\[(?<state>[ xX])\][ \t]*)|(?<bullet>[-*+][ \t]+)|(?<ordered>\d+[.)][ \t]+)|(?<heading>\#{1,6}[ \t]+))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex LineMarker { get; }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImageReference { get; }

    [GeneratedRegex(@"\[(?<label>[^\]]*)\]\((?<url>[^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkReference { get; }

    /// <summary>Matches an inline HTML tag, such as the one underline is written with.</summary>
    [GeneratedRegex(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag { get; }

    [GeneratedRegex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex Bold { get; }

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.CultureInvariant)]
    private static partial Regex Strikethrough { get; }

    [GeneratedRegex(@"\*(?=\S)([^*]+?)(?<=\S)\*", RegexOptions.CultureInvariant)]
    private static partial Regex Italic { get; }

    [GeneratedRegex(@"`(?=\S)([^`]+?)(?<=\S)`", RegexOptions.CultureInvariant)]
    private static partial Regex InlineCode { get; }

    /// <summary>
    /// The note-ready form of a proposal.
    /// </summary>
    /// <remarks>
    /// Safe to run on text that carries no Markdown at all: every rule matches a marker, so prose
    /// the model wrote plainly comes back as it went in, apart from line endings and blank runs.
    /// </remarks>
    public static string ToNoteText(string? proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return string.Empty;
        }

        var lines = NoteContent.NormalizeLineEndings(proposed).Split('\n');
        var kept = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (CodeFence.IsMatch(line)
                || HorizontalRule.IsMatch(line)
                || EmptyHeading.IsMatch(line)
                || TableSeparator.IsMatch(line))
            {
                continue;
            }

            kept.Add(CleanLine(line));
        }

        return Join(kept);
    }

    /// <summary>
    /// The note-ready form of a value that has to stay on one line, such as a task title.
    /// </summary>
    /// <remarks>
    /// A task title becomes one <c>- [ ]</c> line in the note, so a model that answered with a
    /// wrapped sentence has to be folded back to a single line before the marker is put on it —
    /// two lines would leave the second one outside the checklist.
    /// <para>
    /// The marker itself goes, which is where this differs from <see cref="ToNoteText"/>. There a
    /// bullet is the shape of the text; here the caller writes its own, and a title that arrived
    /// as <c>- [ ] 초대장 보내기</c> would otherwise be filed as <c>- [ ] - [ ] 초대장 보내기</c>.
    /// </para>
    /// </remarks>
    public static string ToNoteLine(string? proposed)
    {
        var line = string.Join(' ', ToNoteText(proposed).Split('\n', StringSplitOptions.RemoveEmptyEntries));

        return line[LineMarker.Match(line).Length..].Trim();
    }

    private static string CleanLine(string line)
    {
        // A quoted line is still one of the note's own lines once the bar in front of it is gone.
        var text = QuoteMarker.Replace(line, "${indent}", 1);

        if (TableRow.IsMatch(text))
        {
            return FlattenTableRow(text);
        }

        var marker = LineMarker.Match(text);
        var written = MarkerFor(marker);
        var body = CleanInline(text[marker.Length..]);

        // Indentation is kept under a list marker, where it is what says "this item belongs to
        // the one above", and dropped everywhere else, where it is only the space a marker used
        // to occupy. A heading pushed in by two columns is a heading nobody asked to be indented.
        var indent = written.Length > 0 ? marker.Groups["indent"].Value : string.Empty;

        return (indent + written + body).TrimEnd();
    }

    /// <summary>
    /// The marker to write back, which is the note's own spelling of the one that was there.
    /// </summary>
    /// <remarks>
    /// A heading returns nothing. Its text keeps its own line and the blank line around it, which
    /// is all the hierarchy a single-size surface can show; the hashes in front would only say
    /// that a size change was intended and did not happen.
    /// </remarks>
    private static string MarkerFor(Match marker)
    {
        if (marker.Groups["check"].Success)
        {
            var done = marker.Groups["state"].Value is "x" or "X";
            return done ? "- [x] " : "- [ ] ";
        }

        if (marker.Groups["bullet"].Success)
        {
            return "- ";
        }

        // Numbering is content: "3." says which step this is, and renumbering it as a dash loses that.
        return marker.Groups["ordered"].Success
            ? marker.Groups["ordered"].Value.TrimEnd() + " "
            : string.Empty;
    }

    private static string FlattenTableRow(string line)
    {
        var cells = line.Trim().Trim('|').Split('|');

        return string.Join(
            CellSeparator,
            cells.Select(cell => CleanInline(cell).Trim()).Where(cell => cell.Length > 0));
    }

    /// <summary>
    /// Removes the inline markers, keeping the words they wrapped.
    /// </summary>
    /// <remarks>
    /// Only matched pairs are removed, which is where this parts company with
    /// <see cref="NoteContent.DeriveTitle"/>. A title is one line squeezed into a list row, so
    /// stripping every asterisk there costs nothing; a note body is text the user is about to
    /// keep, and <c>2 * 3</c> has to survive being summarised. Underscores are left alone for the
    /// same reason they are in a title: <c>note_repository</c> is a word, not emphasis.
    /// </remarks>
    private static string CleanInline(string text)
    {
        var cleaned = ImageReference.Replace(text, string.Empty);
        cleaned = LinkReference.Replace(cleaned, Flatten);
        cleaned = HtmlTag.Replace(cleaned, string.Empty);

        // Bold before italic: "**x**" would otherwise be read as an empty italic around "x".
        cleaned = Bold.Replace(cleaned, "$1");
        cleaned = Strikethrough.Replace(cleaned, "$1");
        cleaned = Italic.Replace(cleaned, "$1");
        cleaned = InlineCode.Replace(cleaned, "$1");

        return cleaned;
    }

    /// <summary>
    /// Writes a link as text a note can show.
    /// </summary>
    /// <remarks>
    /// The address is kept rather than dropped. Nothing in a note is clickable either way, so the
    /// only question is whether the user can still get to the page — and a label alone cannot take
    /// them there.
    /// </remarks>
    private static string Flatten(Match link)
    {
        var label = link.Groups["label"].Value.Trim();
        var url = link.Groups["url"].Value.Trim();

        if (url.Length == 0)
        {
            return label;
        }

        return label.Length == 0 || string.Equals(label, url, StringComparison.Ordinal)
            ? url
            : $"{label} ({url})";
    }

    /// <summary>
    /// Joins the kept lines, leaving at most one blank line between blocks.
    /// </summary>
    /// <remarks>
    /// Dropping a fence or a table rule leaves the blank lines that surrounded it behind, and a
    /// note is too short to spend three rows on a gap that used to hold something.
    /// </remarks>
    private static string Join(List<string> lines)
    {
        var builder = new StringBuilder();
        var blanks = 0;
        var written = false;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blanks++;
                continue;
            }

            if (written)
            {
                builder.Append(blanks > 0 ? "\n\n" : "\n");
            }

            builder.Append(line);
            blanks = 0;
            written = true;
        }

        return builder.ToString();
    }
}
