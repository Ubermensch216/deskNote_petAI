using DeskNote.Core.Models;

namespace DeskNote.Core.Abstractions;

public enum NoteSort
{
    UpdatedDescending = 0,
    CreatedDescending = 1,
    TitleAscending = 2,
}

/// <summary>Filter for listing notes in Notes Explorer.</summary>
public sealed record NoteQuery
{
    public static NoteQuery Default { get; } = new();

    /// <summary>Restrict to a notebook; null means every notebook.</summary>
    public Guid? NotebookId { get; init; }

    /// <summary>Restrict to notes carrying this tag, in <see cref="Tag.NormalizedName"/> form.</summary>
    public string? TagNormalizedName { get; init; }

    /// <summary>
    /// When true, returns only soft-deleted notes (the deleted-notes view). Live notes are
    /// returned otherwise; the two sets are never mixed.
    /// </summary>
    public bool OnlyDeleted { get; init; }

    public NoteSort Sort { get; init; } = NoteSort.UpdatedDescending;

    public int Limit { get; init; } = 200;

    public int Offset { get; init; }
}
