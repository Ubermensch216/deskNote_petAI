namespace DeskNote.Core.Models;

/// <summary>Normalization applied to note text on its way into storage.</summary>
public static class NoteContent
{
    /// <summary>
    /// Rewrites CRLF and lone CR line endings as LF.
    /// </summary>
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
}
