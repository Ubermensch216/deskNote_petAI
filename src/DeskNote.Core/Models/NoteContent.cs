using System.Text.RegularExpressions;

namespace DeskNote.Core.Models;

/// <summary>Normalization applied to note text on its way into storage.</summary>
public static partial class NoteContent
{
    /// <summary>Longest derived title kept; past this a list row would truncate it anyway.</summary>
    public const int MaxTitleLength = 60;

    /// <summary>Rewrites CRLF and lone CR line endings as LF.</summary>
    /// <remarks>
    /// A WinUI TextBox reports its line breaks as bare CR. Storing that verbatim leaves the
    /// database holding a line ending that Markdown rendering, checklist parsing and FTS snippets
    /// all fail to split on, and that differs from text arriving by any other route. Normalizing
    /// at the storage boundary means every writer — the editor today, AI actions and import
    /// later — produces the same thing.
    /// </remarks>
    public static string NormalizeLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\r', StringComparison.Ordinal))
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    /// <summary>
    /// The note's title, taken from the first line that has anything on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sticky note is a thought typed into an empty window; almost nobody stops to fill in a
    /// separate title field, so a note window that offers one spends its scarcest row on something
    /// that stays empty. Deriving the title from the first line instead means the library, search
    /// results and reminder toasts all have a real name to show without the user doing anything.
    /// </para>
    /// <para>
    /// Markup is stripped rather than kept: <c>"# 회의 준비"</c> is titled <c>회의 준비</c>, because
    /// the hash is how the line is formatted, not part of what it says.
    /// </para>
    /// </remarks>
    public static string DeriveTitle(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        foreach (var line in NormalizeLineEndings(content).Split('\n'))
        {
            var stripped = StripMarkup(line);
            if (stripped.Length == 0)
            {
                continue;
            }

            return stripped.Length <= MaxTitleLength
                ? stripped
                : stripped[..MaxTitleLength].TrimEnd();
        }

        return string.Empty;
    }

    /// <summary>Matches the indent plus any run of heading, quote, bullet or checklist markers.</summary>
    [GeneratedRegex(
        @"^[ \t]*(?:(?:\#{1,6}|>)[ \t]*|[-*+][ \t]+(?:\[[ xX]\][ \t]*)?|\d+[.)][ \t]+)*",
        RegexOptions.CultureInvariant)]
    private static partial Regex LineMarkers { get; }

    /// <summary>Matches an image reference, which contributes nothing to a title.</summary>
    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImageReference { get; }

    /// <summary>Matches a link, capturing the label that should survive.</summary>
    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkReference { get; }

    /// <summary>Matches an inline HTML tag, such as the one underline is written with.</summary>
    [GeneratedRegex(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag { get; }

    /// <summary>
    /// Matches inline emphasis markers. Underscores are deliberately absent: stripping them would
    /// rename <c>note_repository</c> to <c>noterepository</c>.
    /// </summary>
    [GeneratedRegex(@"\*\*|~~|[*`]", RegexOptions.CultureInvariant)]
    private static partial Regex EmphasisMarker { get; }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace { get; }

    private static string StripMarkup(string line)
    {
        var text = LineMarkers.Replace(line, string.Empty, 1);
        text = ImageReference.Replace(text, string.Empty);
        text = LinkReference.Replace(text, "$1");
        text = HtmlTag.Replace(text, string.Empty);
        text = EmphasisMarker.Replace(text, string.Empty);

        return Whitespace.Replace(text, " ").Trim();
    }
}
