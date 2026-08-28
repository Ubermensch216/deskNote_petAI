namespace DeskNote.Core.Services;

/// <summary>A single contiguous edit: replace <paramref name="Length"/> characters at <paramref name="Start"/>.</summary>
public readonly record struct TextReplacement(int Start, int Length, string Insert)
{
    public bool IsEmpty => Length == 0 && Insert.Length == 0;
}

/// <summary>
/// Reduces a whole-text rewrite to the smallest edit that produces it.
/// </summary>
/// <remarks>
/// Formatting commands are written as pure "old text in, new text out" transformations, but
/// assigning the result straight to a TextBox's Text clears its undo history — so Ctrl+Z after
/// bolding a word would do nothing, and the report lists Undo/Redo as P0. Applying the same change
/// as a selection replacement instead keeps it on the editor's own undo stack, and this is what
/// works out which selection to replace.
/// </remarks>
public static class TextDiff
{
    public static TextReplacement Minimal(string current, string updated)
    {
        if (string.Equals(current, updated, StringComparison.Ordinal))
        {
            return new TextReplacement(0, 0, string.Empty);
        }

        var maxPrefix = Math.Min(current.Length, updated.Length);
        var prefix = 0;
        while (prefix < maxPrefix && current[prefix] == updated[prefix])
        {
            prefix++;
        }

        // Stop the suffix scan before it overlaps the prefix, or a repeated character run would
        // let both ends claim the same text and produce a negative-length replacement.
        var maxSuffix = Math.Min(current.Length - prefix, updated.Length - prefix);
        var suffix = 0;
        while (suffix < maxSuffix
               && current[current.Length - 1 - suffix] == updated[updated.Length - 1 - suffix])
        {
            suffix++;
        }

        return new TextReplacement(
            prefix,
            current.Length - prefix - suffix,
            updated[prefix..(updated.Length - suffix)]);
    }
}
