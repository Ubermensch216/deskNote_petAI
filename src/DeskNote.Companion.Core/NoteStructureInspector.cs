using DeskNote.Core.Services;

namespace DeskNote.Companion.Core;

/// <summary>What changed about a note's shape, as opposed to its prose.</summary>
public readonly record struct NoteStructureDelta(
    IReadOnlyList<string> AddedTags,
    bool ChecklistChanged)
{
    public bool HasStructuralChange => AddedTags.Count > 0 || ChecklistChanged;
}

/// <summary>
/// Separates organising a note from writing in it.
/// </summary>
/// <remarks>
/// Length alone cannot tell the two apart: adding a tag to a year-old note is a handful of
/// characters and is exactly the tidying the rules mean to pay for, while padding a paragraph is
/// forty characters and is not. Ticking a box is deliberately not structural — that is already
/// paid as a completed checklist item, and counting it twice would double the allowance.
/// </remarks>
public static class NoteStructureInspector
{
    public static NoteStructureDelta Compare(string? previousContent, string currentContent)
    {
        var previous = previousContent ?? string.Empty;

        var before = TagParser.Parse(previous).Select(tag => tag.NormalizedName).ToHashSet(StringComparer.Ordinal);
        var addedTags = TagParser.Parse(currentContent)
            .Select(tag => tag.NormalizedName)
            .Where(name => !before.Contains(name))
            .ToList();

        var beforeItems = ChecklistParser.Parse(previous)
            .Select(item => item.Text)
            .ToHashSet(StringComparer.Ordinal);
        var afterItems = ChecklistParser.Parse(currentContent)
            .Select(item => item.Text)
            .ToHashSet(StringComparer.Ordinal);

        return new NoteStructureDelta(addedTags, !beforeItems.SetEquals(afterItems));
    }
}
