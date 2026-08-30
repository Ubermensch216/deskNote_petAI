namespace DeskNote.Core.Models;

/// <summary>
/// A file or image attached to a note. Large files are referenced rather than copied
/// (report p5: 대용량은 복사보다 링크 우선).
/// </summary>
public sealed record Attachment
{
    public required Guid Id { get; init; }

    public required Guid NoteId { get; init; }

    public AttachmentKind Kind { get; init; } = AttachmentKind.Embedded;

    /// <summary>Absolute path for links, or a path relative to the app data directory for embedded files.</summary>
    public required string Path { get; init; }

    public string? DisplayName { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>Content hash, used to detect a link whose target changed underneath the note.</summary>
    public string? Sha256 { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
