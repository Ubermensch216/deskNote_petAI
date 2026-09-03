using System.Text;
using System.Text.RegularExpressions;

namespace DeskNote.Core.Services;

/// <summary>The line-level markers a note line can carry.</summary>
public enum LineStyle
{
    None = 0,
    Bullet = 1,
    Checklist = 2,
    Heading1 = 3,
    Heading2 = 4,
    Heading3 = 5,
}

/// <summary>
/// The Markdown formatting commands a note editor offers (report p5: 굵게·목록·링크·코드·제목).
/// </summary>
/// <remarks>
/// The note stores Markdown source and the editor shows that source. For a surface whose whole
/// point is capturing a thought in about a second, source text that stays readable beats a
/// rich-text model that has to be round-tripped: what the user sees is what gets stored, searched
/// and later handed to an AI action.
/// </remarks>
public static partial class MarkdownEditing
{
    private const string BoldMarker = "**";
    private const string ItalicMarker = "*";
    private const string CodeMarker = "`";
    private const string StrikethroughMarker = "~~";

    /// <summary>
    /// Underline has no Markdown syntax, so it is written as the HTML tag every Markdown renderer
    /// passes through. The alternative — inventing a marker — would store text that stops meaning
    /// underline the moment it leaves this app.
    /// </summary>
    private const string UnderlineOpen = "<u>";
    private const string UnderlineClose = "</u>";

    /// <summary>Matches the indent and any existing list, checklist or heading marker on a line.</summary>
    [GeneratedRegex(
        @"^(?<indent>[ \t]*)(?<marker>(?:[-*+][ \t]+\[[ xX]\][ \t]+)|(?:[-*+][ \t]+)|(?:\d+[.)][ \t]+)|(?:\#{1,6}[ \t]+))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex LinePrefix { get; }

    /// <summary>Matches a checklist line, capturing its checked state and text.</summary>
    [GeneratedRegex(
        @"^(?<indent>[ \t]*)[-*+][ \t]+\[(?<state>[ xX])\][ \t]*(?<text>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ChecklistLine { get; }

    public static NoteTextState ToggleBold(NoteTextState state) => ToggleWrap(state, BoldMarker);

    public static NoteTextState ToggleInlineCode(NoteTextState state) => ToggleWrap(state, CodeMarker);

    public static NoteTextState ToggleStrikethrough(NoteTextState state) => ToggleWrap(state, StrikethroughMarker);

    public static NoteTextState ToggleUnderline(NoteTextState state) =>
        ToggleWrap(state, UnderlineOpen, UnderlineClose);

    /// <remarks>
    /// Bold is checked first so that italic applied to <c>**text**</c> nests as <c>***text***</c>
    /// rather than silently stripping one of the bold asterisks.
    /// </remarks>
    public static NoteTextState ToggleItalic(NoteTextState state)
    {
        if (IsWrappedWith(state, BoldMarker))
        {
            return Wrap(state, ItalicMarker);
        }

        return ToggleWrap(state, ItalicMarker);
    }

    /// <summary>
    /// Wraps the selection as a Markdown link, leaving the caret where the URL goes.
    /// </summary>
    public static NoteTextState InsertLink(NoteTextState state, string? url = null)
    {
        var clamped = state.Clamped();
        var label = clamped.SelectionLength > 0 ? clamped.SelectedText : string.Empty;
        var target = url ?? string.Empty;
        var replacement = $"[{label}]({target})";

        var text = clamped.Text
            .Remove(clamped.SelectionStart, clamped.SelectionLength)
            .Insert(clamped.SelectionStart, replacement);

        // With no URL supplied the caret goes inside the parentheses, ready to paste one; with a
        // URL already known there is nothing left to type, so the whole link is selected.
        return string.IsNullOrEmpty(target)
            ? new NoteTextState(text, clamped.SelectionStart + label.Length + 3, 0)
            : new NoteTextState(text, clamped.SelectionStart, replacement.Length);
    }

    /// <summary>
    /// Applies or removes a line-level style across every line the selection touches.
    /// </summary>
    /// <remarks>
    /// Toggling is all-or-nothing on purpose: if every touched line already carries the style it is
    /// removed, otherwise the style is applied to all of them. Flipping each line independently
    /// would turn a mixed selection into a differently mixed selection, which reads as the command
    /// not having worked.
    /// </remarks>
    public static NoteTextState ApplyLineStyle(NoteTextState state, LineStyle style)
    {
        var clamped = state.Clamped();
        var (blockStart, blockEnd) = LineBlock(clamped.Text, clamped.SelectionStart, clamped.SelectionEnd);
        var block = clamped.Text[blockStart..blockEnd];
        var lines = block.Split('\n');

        // Starting a list on an empty line is the ordinary way to begin one, so a block that is
        // entirely blank counts as unstyled and gets the marker. Treating blank lines as "already
        // styled" made the command toggle off on an empty note — exactly when a user reaches for it.
        var written = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var alreadyStyled = written.Count > 0 && written.All(line => StyleOf(line) == style);
        var target = alreadyStyled ? LineStyle.None : style;

        // Blank lines inside a larger selection are separators, not items; they only take a marker
        // when a blank line is all there is.
        var blockIsBlank = written.Count == 0;

        var rebuilt = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                rebuilt.Append('\n');
            }

            rebuilt.Append(string.IsNullOrWhiteSpace(lines[i]) && !blockIsBlank
                ? lines[i]
                : SetStyle(lines[i], target));
        }

        var replacement = rebuilt.ToString();
        var text = clamped.Text.Remove(blockStart, block.Length).Insert(blockStart, replacement);

        if (clamped.SelectionLength > 0)
        {
            // A deliberate multi-line selection stays selected, so the command can be repeated.
            return new NoteTextState(text, blockStart, replacement.Length);
        }

        // With only a caret, keep it a caret. Leaving the styled line selected instead means the
        // next keystroke — very often Enter to start the next item — replaces the line the user
        // just formatted.
        var oldLine = lines[0];
        var newLine = SetStyle(oldLine, target);
        var caret = Math.Clamp(
            clamped.SelectionStart + (newLine.Length - oldLine.Length),
            blockStart,
            text.Length);

        return new NoteTextState(text, caret, 0);
    }

    /// <summary>
    /// Continues a list when Enter is pressed inside one, or ends the list on an empty item.
    /// </summary>
    /// <returns>The new state, or null when the caret is not on a list line and Enter should behave normally.</returns>
    /// <remarks>
    /// Typing a three-item checklist should not require retyping "- [ ] " each time; and pressing
    /// Enter on an empty item is the natural way to say "done with the list", so that clears the
    /// marker instead of adding another empty one.
    /// </remarks>
    public static NoteTextState? ContinueListOnEnter(NoteTextState state)
    {
        // Enter with a selection replaces it, exactly as typing any character would. Working out
        // the continuation from the text before the deletion would read the marker off a line that
        // is about to disappear.
        var clamped = state.Clamped();
        if (clamped.SelectionLength > 0)
        {
            clamped = new NoteTextState(
                clamped.Text.Remove(clamped.SelectionStart, clamped.SelectionLength),
                clamped.SelectionStart,
                0);
        }

        var lineStart = LineStartAt(clamped.Text, clamped.SelectionStart);
        var lineEnd = LineEndAt(clamped.Text, clamped.SelectionEnd);
        var line = clamped.Text[lineStart..lineEnd];

        var match = LinePrefix.Match(line);
        var marker = match.Groups["marker"].Value;
        if (string.IsNullOrEmpty(marker) || IsHeadingMarker(marker))
        {
            return null;
        }

        var indent = match.Groups["indent"].Value;
        var body = line[match.Length..];

        if (string.IsNullOrWhiteSpace(body))
        {
            // Empty item: leave the list rather than adding another blank bullet.
            var cleared = clamped.Text.Remove(lineStart, line.Length).Insert(lineStart, indent);
            return new NoteTextState(cleared, lineStart + indent.Length, 0);
        }

        var continuation = "\n" + indent + NextMarker(marker);
        var text = clamped.Text
            .Remove(clamped.SelectionStart, clamped.SelectionLength)
            .Insert(clamped.SelectionStart, continuation);

        return new NoteTextState(text, clamped.SelectionStart + continuation.Length, 0);
    }

    /// <summary>Flips the checked state of the checklist item the caret is on.</summary>
    /// <returns>The new state, or null when the caret is not on a checklist line.</returns>
    public static NoteTextState? ToggleChecklistItemAtCaret(NoteTextState state)
    {
        var clamped = state.Clamped();
        var lineStart = LineStartAt(clamped.Text, clamped.SelectionStart);
        var lineEnd = LineEndAt(clamped.Text, clamped.SelectionStart);
        var line = clamped.Text[lineStart..lineEnd];

        var match = ChecklistLine.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var isDone = !match.Groups["state"].Value.Equals("x", StringComparison.OrdinalIgnoreCase);
        var replacement = $"{match.Groups["indent"].Value}- [{(isDone ? 'x' : ' ')}] {match.Groups["text"].Value}";
        var text = clamped.Text.Remove(lineStart, line.Length).Insert(lineStart, replacement);

        // The caret keeps its offset within the line, so toggling does not move the user's place.
        var offset = Math.Min(clamped.SelectionStart - lineStart, replacement.Length);
        return new NoteTextState(text, lineStart + offset, 0);
    }

    /// <summary>
    /// The indent and list, checklist or heading marker a line opens with.
    /// </summary>
    /// <remarks>
    /// Exposed because the editor's Markdown renderer has to leave this prefix alone. A bullet
    /// written as <c>* item</c> opens with the italic marker, so a renderer that scanned the whole
    /// line for emphasis would read the bullet and the next asterisk on the line as a matched pair
    /// and swallow both.
    /// </remarks>
    public static string LineMarkerPrefix(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line[..LinePrefix.Match(line).Length];
    }

    /// <summary>The line style currently on a line.</summary>
    public static LineStyle StyleOf(string line)
    {
        var match = LinePrefix.Match(line);
        var marker = match.Groups["marker"].Value;

        if (string.IsNullOrEmpty(marker))
        {
            return LineStyle.None;
        }

        if (marker.Contains('[', StringComparison.Ordinal))
        {
            return LineStyle.Checklist;
        }

        if (marker.StartsWith('#'))
        {
            return marker.TakeWhile(c => c == '#').Count() switch
            {
                1 => LineStyle.Heading1,
                2 => LineStyle.Heading2,
                _ => LineStyle.Heading3,
            };
        }

        // Ordered lists count as Bullet for toggling: "remove the list marker" is the same gesture
        // whether the line starts with "-" or "1.".
        return LineStyle.Bullet;
    }

    private static string SetStyle(string line, LineStyle style)
    {
        var match = LinePrefix.Match(line);
        var indent = match.Groups["indent"].Value;
        var body = line[match.Length..];

        var marker = style switch
        {
            LineStyle.Bullet => "- ",
            LineStyle.Checklist => "- [ ] ",
            LineStyle.Heading1 => "# ",
            LineStyle.Heading2 => "## ",
            LineStyle.Heading3 => "### ",
            _ => string.Empty,
        };

        return indent + marker + body;
    }

    private static string NextMarker(string marker)
    {
        var trimmed = marker.TrimEnd();

        // A completed item continues as an empty one — the next thing to do, not another done thing.
        if (trimmed.EndsWith(']'))
        {
            return "- [ ] ";
        }

        if (char.IsDigit(trimmed[0]))
        {
            var digits = new string(trimmed.TakeWhile(char.IsDigit).ToArray());
            var separator = trimmed[digits.Length];
            return int.TryParse(digits, out var number)
                ? $"{number + 1}{separator} "
                : trimmed + " ";
        }

        return "- ";
    }

    private static bool IsHeadingMarker(string marker) => marker.StartsWith('#');

    private static bool IsWrappedWith(NoteTextState state, string marker)
    {
        var (text, start, length) = (state.Text, state.SelectionStart, state.SelectionLength);
        var m = marker.Length;

        if (length >= 2 * m
            && text.AsSpan(start, length).StartsWith(marker)
            && text.AsSpan(start, length).EndsWith(marker))
        {
            return true;
        }

        return start >= m
            && start + length + m <= text.Length
            && text.AsSpan(start - m, m).SequenceEqual(marker)
            && text.AsSpan(start + length, m).SequenceEqual(marker);
    }

    private static NoteTextState ToggleWrap(NoteTextState state, string marker) =>
        ToggleWrap(state, marker, marker);

    /// <remarks>
    /// Open and close are separate strings so the same toggle serves the symmetric Markdown
    /// markers and the HTML tag pair underline needs.
    /// </remarks>
    private static NoteTextState ToggleWrap(NoteTextState state, string open, string close)
    {
        var clamped = state.Clamped();
        var text = clamped.Text;
        var start = clamped.SelectionStart;
        var length = clamped.SelectionLength;

        // Markers inside the selection: "**bold**" selected whole.
        if (length >= open.Length + close.Length
            && text.AsSpan(start, length).StartsWith(open)
            && text.AsSpan(start, length).EndsWith(close))
        {
            var inner = text.Substring(start + open.Length, length - open.Length - close.Length);
            return new NoteTextState(text.Remove(start, length).Insert(start, inner), start, inner.Length);
        }

        // Markers just outside the selection: "bold" selected within "**bold**".
        if (start >= open.Length
            && start + length + close.Length <= text.Length
            && text.AsSpan(start - open.Length, open.Length).SequenceEqual(open)
            && text.AsSpan(start + length, close.Length).SequenceEqual(close))
        {
            var stripped = text.Remove(start + length, close.Length).Remove(start - open.Length, open.Length);
            return new NoteTextState(stripped, start - open.Length, length);
        }

        return Wrap(clamped, open, close);
    }

    private static NoteTextState Wrap(NoteTextState state, string marker) => Wrap(state, marker, marker);

    private static NoteTextState Wrap(NoteTextState state, string open, string close)
    {
        var text = state.Text
            .Insert(state.SelectionEnd, close)
            .Insert(state.SelectionStart, open);

        // With nothing selected the caret lands between the markers, ready to type.
        return new NoteTextState(text, state.SelectionStart + open.Length, state.SelectionLength);
    }

    private static (int Start, int End) LineBlock(string text, int selectionStart, int selectionEnd) =>
        (LineStartAt(text, selectionStart), LineEndAt(text, selectionEnd));

    private static int LineStartAt(string text, int index)
    {
        var previous = text.LastIndexOf('\n', Math.Max(0, Math.Min(index, text.Length) - 1));
        return previous < 0 ? 0 : previous + 1;
    }

    private static int LineEndAt(string text, int index)
    {
        var next = text.IndexOf('\n', Math.Min(index, text.Length));
        return next < 0 ? text.Length : next;
    }
}
