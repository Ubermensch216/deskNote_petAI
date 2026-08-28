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

    /// <summary>Clamps the selection into the text, so a transformation can never return an invalid state.</summary>
    public NoteTextState Clamped()
    {
        var start = Math.Clamp(SelectionStart, 0, Text.Length);
        var length = Math.Clamp(SelectionLength, 0, Text.Length - start);
        return new NoteTextState(Text, start, length);
    }
}
