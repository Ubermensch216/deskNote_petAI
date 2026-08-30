namespace DeskNote.Core.Models;

/// <summary>
/// A prior version of a note's content. Written before any destructive content replacement so
/// that an AI rewrite always preserves the original (report p15: AI rewrite → 원본 version 보존).
/// </summary>
public sealed record NoteRevision
{
    public required Guid Id { get; init; }

    public required Guid NoteId { get; init; }

    public required string Content { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public RevisionSource Source { get; init; } = RevisionSource.User;

    /// <summary>Which AI action produced this revision, when <see cref="Source"/> is Ai.</summary>
    public string? ActionName { get; init; }
}
