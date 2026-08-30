namespace DeskNote.Core.Models;

/// <summary>A project or context label attached to notes (Notezilla-style classification, report p5).</summary>
public sealed record Tag
{
    public required Guid Id { get; init; }

    /// <summary>Display form as the user typed it, without a leading '#'.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Case- and width-folded form used for uniqueness and lookup, so "Backend" and "backend"
    /// are the same tag.
    /// </summary>
    public required string NormalizedName { get; init; }

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
