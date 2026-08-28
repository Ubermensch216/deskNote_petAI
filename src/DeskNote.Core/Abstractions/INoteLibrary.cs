using DeskNote.Core.Models;

namespace DeskNote.Core.Abstractions;

/// <summary>A tag with how many live notes carry it.</summary>
public sealed record TagUsage(Tag Tag, int NoteCount);

/// <summary>
/// One row in Notes Explorer.
/// </summary>
/// <remarks>
/// Deliberately not the whole <see cref="Note"/>. The library lists every note the user has ever
/// written, and loading full bodies to render a list of one-line previews is the shape of query
/// that stops being fast at exactly the size report p14 sets a target for (10,000 notes, p95
/// under 100 ms).
/// </remarks>
public sealed record NoteSummary
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    /// <summary>First line or so of the body, for the list row.</summary>
    public required string Preview { get; init; }

    public string ColorKey { get; init; } = NoteColors.Default;

    public Guid? NotebookId { get; init; }

    public bool IsOpen { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }

    /// <summary>Checked and total checklist items, so a row can show "1/3" without parsing the body.</summary>
    public int ChecklistDone { get; init; }

    public int ChecklistTotal { get; init; }

    /// <summary>Tags on the note, in the order they appear in the body.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The next reminder that has not fired yet, if any.</summary>
    public DateTimeOffset? NextReminderAt { get; init; }
}

/// <summary>
/// Read side of the note library: the queries Notes Explorer runs.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="INoteRepository"/>, which is about one note at a time. These are
/// list-shaped queries over the whole collection, and they are the ones that have to stay fast as
/// the library grows.
/// </remarks>
public interface INoteLibrary
{
    Task<IReadOnlyList<NoteSummary>> ListAsync(NoteQuery query, CancellationToken cancellationToken = default);

    /// <summary>Full-text search restricted by the same filters the list uses.</summary>
    Task<IReadOnlyList<NoteSummary>> SearchAsync(
        string text,
        NoteQuery query,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Tags in use, ordered by how many notes carry them.</summary>
    Task<IReadOnlyList<TagUsage>> ListTagsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken cancellationToken = default);

    Task<Notebook> CreateNotebookAsync(string name, Guid? parentId = null, CancellationToken cancellationToken = default);

    Task RenameNotebookAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Removes a notebook; its notes are kept and become unfiled.</summary>
    Task DeleteNotebookAsync(Guid id, CancellationToken cancellationToken = default);
}
