namespace DeskNote.Core.Services;

/// <summary>
/// An editor's text together with the current selection.
/// </summary>
/// <remarks>
/// Formatting commands are expressed as pure transformations of this value rather than as
/// operations on a TextBox. That keeps every rule about where the caret lands after applying bold,
/// or what happens when a list marker is toggled across three selected lines, testable without a
/// UI — which matters because those are exactly the details that feel broken when they are wrong.
/// </remarks>
public readonly record struct NoteTextState(string Text, int SelectionStart, int SelectionLength)
{
    public static NoteTextState Empty { get; } = new(string.Empty, 0, 0);

    public int SelectionEnd => SelectionStart + SelectionLength;

    public string SelectedText => Text.Substring(SelectionStart, SelectionLength);

    /// <summary>
    /// The state after typing <paramref name="text"/> over the selection.
    /// </summary>
    /// <remarks>
    /// What an ordinary keystroke does, expressed as a transformation so that the paths which have
    /// to do it deliberately — pasting, inserting an attachment link — go through the editor as one
    /// undoable edit rather than assigning the whole body.
    /// </remarks>
    public NoteTextState ReplacingSelection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var clamped = Clamped();
        var replaced = clamped.Text
            .Remove(clamped.SelectionStart, clamped.SelectionLength)
            .Insert(clamped.SelectionStart, text);

        return new NoteTextState(replaced, clamped.SelectionStart + text.Length, 0);
    }

    /// <summary>Clamps the selection into the text, so a transformation can never return an invalid state.</summary>
    public NoteTextState Clamped()
    {
        var start = Math.Clamp(SelectionStart, 0, Text.Length);
        var length = Math.Clamp(SelectionLength, 0, Text.Length - start);
        return new NoteTextState(Text, start, length);
    }
}
