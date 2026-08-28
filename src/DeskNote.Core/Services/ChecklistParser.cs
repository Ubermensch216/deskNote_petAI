using System.Text.RegularExpressions;

namespace DeskNote.Core.Services;

/// <summary>One checklist item found in a note's Markdown.</summary>
/// <param name="LineIndex">Zero-based line the item sits on, used to address it for toggling.</param>
/// <param name="Text">The item's text, without the marker.</param>
/// <param name="IsDone">Whether the box is ticked.</param>
public readonly record struct ChecklistItemLine(int LineIndex, string Text, bool IsDone);

/// <summary>
/// Reads the checklist items out of a note's Markdown.
/// </summary>
/// <remarks>
/// <para>
/// The Markdown text is the single source of truth for checklists. The <c>checklist_items</c>
/// table is a projection of it, rebuilt whenever the note is saved, not a parallel store the
/// editor has to keep in step. Two authoritative copies of the same list is exactly the kind of
/// state that drifts once an AI rewrite or a sync merge touches the note body.
/// </para>
/// <para>
/// The projection earns its place by making questions cheap that scanning note bodies cannot
/// answer quickly: what is still unchecked across every note, which the Notes Explorer and later
/// the 할 일 추출 action both need.
/// </para>
/// </remarks>
public static partial class ChecklistParser
{
    [GeneratedRegex(
        @"^[ \t]*[-*+][ \t]+\[(?<state>[ xX])\][ \t]*(?<text>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ItemLine { get; }

    public static IReadOnlyList<ChecklistItemLine> Parse(string content)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains('[', StringComparison.Ordinal))
        {
            return [];
        }

        var items = new List<ChecklistItemLine>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var match = ItemLine.Match(lines[i].TrimEnd('\r'));
            if (match.Success)
            {
                items.Add(new ChecklistItemLine(
                    i,
                    match.Groups["text"].Value.Trim(),
                    match.Groups["state"].Value.Equals("x", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return items;
    }

    /// <summary>Sets the checked state of the item on <paramref name="lineIndex"/>.</summary>
    /// <returns>The updated content, or the original when that line is not a checklist item.</returns>
    public static string SetItemState(string content, int lineIndex, bool isDone)
    {
        var lines = content.Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            return content;
        }

        var match = ItemLine.Match(lines[lineIndex].TrimEnd('\r'));
        if (!match.Success)
        {
            return content;
        }

        var indent = new string(lines[lineIndex].TakeWhile(c => c is ' ' or '\t').ToArray());
        lines[lineIndex] = $"{indent}- [{(isDone ? 'x' : ' ')}] {match.Groups["text"].Value}";
        return string.Join('\n', lines);
    }

    /// <summary>Counts of done and total items, for the summary a note list row shows.</summary>
    public static (int Done, int Total) Progress(string content)
    {
        var items = Parse(content);
        return (items.Count(i => i.IsDone), items.Count);
    }
}
