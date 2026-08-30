namespace DeskNote.Core.Models;

/// <summary>A folder grouping notes. Nestable via <see cref="ParentId"/>.</summary>
public sealed record Notebook
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public Guid? ParentId { get; init; }

    public int Ordinal { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
