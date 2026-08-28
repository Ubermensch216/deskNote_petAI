using System.Text.RegularExpressions;
using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>A tag as written in a note, with the position it was found at.</summary>
/// <param name="Name">The tag text as typed, without the leading '#'.</param>
/// <param name="NormalizedName">Case-folded form used for lookup and uniqueness.</param>
/// <param name="Index">Character offset of the '#'.</param>
public readonly record struct TagMention(string Name, string NormalizedName, int Index);

/// <summary>
/// Reads the hashtags out of a note's body.
/// </summary>
/// <remarks>
/// <para>
/// Report p6's note mock-up carries its tags inline — <c>#backend #v1</c> sitting in the text —
/// so the body stays the single source of truth, exactly as it is for checklists. The
/// <c>tags</c> and <c>note_tags</c> rows are a projection rebuilt on save, which is what makes
/// "every note tagged #backend" a cheap query without giving the same fact two owners.
/// </para>
/// <para>
/// Markdown headings are the collision to avoid: <c>#</c> followed by a space is a heading, not a
/// tag, and <c>##</c> is a deeper heading. Requiring a non-space, non-hash character immediately
/// after the marker separates the two without needing to parse Markdown.
/// </para>
/// </remarks>
public static partial class TagParser
{
    /// <summary>Longest tag accepted, so a runaway token cannot become a tag.</summary>
    public const int MaxLength = 50;

    [GeneratedRegex(
        @"(?<![^\s(\[])#(?![\s#])(?<tag>[\p{L}\p{N}_\-/]{1,50})",
        RegexOptions.CultureInvariant)]
    private static partial Regex Hashtag { get; }

    /// <summary>Every tag mention in the text, in the order they appear, duplicates included.</summary>
    public static IReadOnlyList<TagMention> FindMentions(string content)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains('#', StringComparison.Ordinal))
        {
            return [];
        }

        var mentions = new List<TagMention>();

        foreach (Match match in Hashtag.Matches(content))
        {
            var name = match.Groups["tag"].Value;

            // A tag made only of digits reads as a number the user wrote, not a label.
            if (name.All(char.IsDigit))
            {
                continue;
            }

            mentions.Add(new TagMention(name, Tag.Normalize(name), match.Groups["tag"].Index - 1));
        }

        return mentions;
    }

    /// <summary>
    /// The distinct tags on a note, keeping the first spelling of each.
    /// </summary>
    /// <remarks>
    /// Writing <c>#Backend</c> once and <c>#backend</c> later is one tag; the first spelling wins
    /// so the note list shows the tag the way the user first wrote it rather than lower-casing it.
    /// </remarks>
    public static IReadOnlyList<TagMention> Parse(string content)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<TagMention>();

        foreach (var mention in FindMentions(content))
        {
            if (seen.Add(mention.NormalizedName))
            {
                distinct.Add(mention);
            }
        }

        return distinct;
    }
}
