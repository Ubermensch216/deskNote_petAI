using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace DeskNote.App.Views;

/// <summary>
/// The note's body, as a rich edit surface that draws emphasis instead of showing its markers.
/// </summary>
/// <remarks>
/// <para>
/// The note is still stored as Markdown. This type is the only thing that knows the editor is not
/// showing that Markdown: everything above it works in the text the user can see, and asks for
/// <see cref="Markdown"/> when it is time to store. Keeping that translation in one class is what
/// let the rest of the window — line commands, AI actions, attachments, autosave — go on being
/// written against a plain string and a selection.
/// </para>
/// <para>
/// Line markers deliberately remain visible characters of that string. <c>Ctrl+Enter</c>, list
/// continuation, the checklist projection and the derived title all read <c>- [ ]</c> and <c>#</c>
/// directly, and they keep working unchanged because those characters never left.
/// </para>
/// </remarks>
internal sealed class NoteEditor
{
    private readonly RichEditBox _box;

    private string? _shown;
    private string? _markdown;

    public NoteEditor(RichEditBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        _box = box;
    }

    /// <summary>What the user can see, with emphasis markers already removed.</summary>
    public string Text => _shown ??= ReadShown();

    /// <summary>The same body as Markdown, which is what gets stored.</summary>
    public string Markdown => _markdown ??= NoteRichText.ToMarkdown(Text, ReadSpans());

    public int SelectionStart => Math.Max(0, _box.Document.Selection.StartPosition);

    public int SelectionLength => Math.Max(0, _box.Document.Selection.EndPosition - SelectionStart);

    /// <summary>The shown text and selection, as the formatting commands expect them.</summary>
    public NoteTextState State => new NoteTextState(Text, SelectionStart, SelectionLength).Clamped();

    /// <summary>
    /// Notes that the document has changed and the cached reads are stale.
    /// </summary>
    /// <remarks>
    /// Reading the document is a walk over its formatting runs, and the title is re-derived on
    /// every keystroke. Caching until something actually changes turns that into one walk per
    /// edit instead of one per reader.
    /// </remarks>
    public void Invalidate()
    {
        _shown = null;
        _markdown = null;
    }

    /// <summary>Replaces the whole body with stored Markdown, drawing its emphasis.</summary>
    public void Load(string? markdown)
    {
        var rich = NoteRichText.FromMarkdown(markdown);

        _box.Document.SetText(TextSetOptions.None, ForEditor(rich.Text));

        foreach (var span in rich.Spans)
        {
            Format(_box.Document.GetRange(span.Start, span.Start + span.Length).CharacterFormat, span.Style);
        }

        Invalidate();
    }

    /// <summary>
    /// Applies a transformed state as a replacement of only what changed.
    /// </summary>
    /// <remarks>
    /// Setting the whole text would clear the undo stack and repaint every run, so bolding a word
    /// would cost the user their Ctrl+Z and any emphasis elsewhere on the note. Replacing the one
    /// span that differs leaves both alone.
    /// </remarks>
    public void Apply(NoteTextState next)
    {
        var edit = TextDiff.Minimal(Text, next.Text);

        if (!edit.IsEmpty)
        {
            _box.Document
                .GetRange(edit.Start, edit.Start + edit.Length)
                .SetText(TextSetOptions.None, ForEditor(edit.Insert));
        }

        Select(next.SelectionStart, next.SelectionLength);
        Invalidate();
    }

    public void Select(int start, int length) =>
        _box.Document.Selection.SetRange(start, start + length);

    /// <summary>Turns one emphasis on or off across the selection.</summary>
    public void ToggleInline(InlineStyle style)
    {
        var format = _box.Document.Selection.CharacterFormat;

        switch (style)
        {
            case InlineStyle.Bold:
                format.Bold = Flip(format.Bold);
                break;
            case InlineStyle.Italic:
                format.Italic = Flip(format.Italic);
                break;
            case InlineStyle.Strikethrough:
                format.Strikethrough = Flip(format.Strikethrough);
                break;
            default:
                format.Underline = IsUnderlined(format.Underline)
                    ? UnderlineType.None
                    : UnderlineType.Single;
                break;
        }

        Invalidate();
    }

    /// <summary>Paints the note's ink colour over the whole body, and over what is typed next.</summary>
    public void SetInk(Windows.UI.Color color)
    {
        _box.Document.GetRange(0, TextConstants.MaxUnitCount).CharacterFormat.ForegroundColor = color;

        var format = _box.Document.GetDefaultCharacterFormat();
        format.ForegroundColor = color;
        _box.Document.SetDefaultCharacterFormat(format);
    }

    /// <summary>
    /// Text on its way into the document, in the line ending a rich edit story is made of.
    /// </summary>
    /// <remarks>
    /// A rich edit control separates paragraphs with CR. Handing it LF and hoping is how a note
    /// grows a blank line between every pair of lines each time it is opened, so the conversion is
    /// done here and undone in <see cref="ReadShown"/> — one place each way, and neither can drift
    /// from the other.
    /// </remarks>
    private static string ForEditor(string text) => text.Replace('\n', '\r');

    /// <summary>
    /// The body as the user sees it, with the editor's line endings normalized.
    /// </summary>
    /// <remarks>
    /// A rich edit story always ends in a paragraph mark that is structure rather than content, and
    /// it comes back on the end of every read. Left in place it would grow the note by a blank line
    /// on every save.
    /// </remarks>
    private string ReadShown()
    {
        _box.Document.GetText(TextGetOptions.None, out var text);

        if (text.EndsWith('\r'))
        {
            text = text[..^1];
        }

        return NoteContent.NormalizeLineEndings(text);
    }

    /// <summary>
    /// Where the emphasis falls, walked one formatting run at a time.
    /// </summary>
    /// <remarks>
    /// Asking the document to extend a range by one <see cref="TextRangeUnit.CharacterFormat"/> is
    /// what makes this cheap: a note that is uniformly formatted costs a single step, however long
    /// it is. The clamp is the guard against a range that refuses to advance — without it, a
    /// document the control describes in some way this code did not anticipate would hang the UI
    /// thread rather than merely losing a style.
    /// </remarks>
    private List<RichSpan> ReadSpans()
    {
        var spans = new List<RichSpan>();
        var length = Text.Length;
        var position = 0;

        while (position < length)
        {
            var range = _box.Document.GetRange(position, position);
            range.MoveEnd(TextRangeUnit.CharacterFormat, 1);

            var end = Math.Clamp(range.EndPosition, position + 1, length);
            var style = StyleOf(range.CharacterFormat);

            if (style != InlineStyle.None)
            {
                spans.Add(new RichSpan(position, end - position, style));
            }

            position = end;
        }

        return spans;
    }

    private static InlineStyle StyleOf(ITextCharacterFormat format)
    {
        var style = InlineStyle.None;

        if (format.Bold == FormatEffect.On)
        {
            style |= InlineStyle.Bold;
        }

        if (format.Italic == FormatEffect.On)
        {
            style |= InlineStyle.Italic;
        }

        if (format.Strikethrough == FormatEffect.On)
        {
            style |= InlineStyle.Strikethrough;
        }

        if (IsUnderlined(format.Underline))
        {
            style |= InlineStyle.Underline;
        }

        return style;
    }

    private static void Format(ITextCharacterFormat format, InlineStyle style)
    {
        format.Bold = Effect(style.HasFlag(InlineStyle.Bold));
        format.Italic = Effect(style.HasFlag(InlineStyle.Italic));
        format.Strikethrough = Effect(style.HasFlag(InlineStyle.Strikethrough));
        format.Underline = style.HasFlag(InlineStyle.Underline)
            ? UnderlineType.Single
            : UnderlineType.None;
    }

    /// <summary>
    /// True for every underline the control might report.
    /// </summary>
    /// <remarks>
    /// Markdown has one underline, but a rich edit control has a dozen — dotted, wave, double. A
    /// note only ever gets <see cref="UnderlineType.Single"/> from here, but one pasted from
    /// elsewhere can arrive wearing any of them, and all of them mean underlined.
    /// </remarks>
    private static bool IsUnderlined(UnderlineType underline) =>
        underline is not (UnderlineType.None or UnderlineType.Undefined);

    private static FormatEffect Effect(bool on) => on ? FormatEffect.On : FormatEffect.Off;

    /// <summary>
    /// Flips an effect, reading a mixed selection as "not yet formatted".
    /// </summary>
    /// <remarks>
    /// A selection spanning struck and unstruck text reports <see cref="FormatEffect.Undefined"/>.
    /// Handing that straight back as Toggle leaves the two halves swapped, which is nobody's idea
    /// of what the button does; the first press should make the whole selection agree.
    /// </remarks>
    private static FormatEffect Flip(FormatEffect current) =>
        current == FormatEffect.On ? FormatEffect.Off : FormatEffect.On;
}
