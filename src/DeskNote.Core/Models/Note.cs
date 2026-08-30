namespace DeskNote.Core.Models;

/// <summary>
/// A single sticky note. One note maps to one top-level window at runtime (report p1).
/// </summary>
public sealed record Note
{
    /// <summary>Lowest surface opacity the UI will apply, so note text never becomes unreadable.</summary>
    public const double MinOpacity = 0.35;

    public required Guid Id { get; init; }

    /// <summary>Optional user-supplied heading shown in the note's title strip and in Notes Explorer.</summary>
    public string Title { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public NoteFormat Format { get; init; } = NoteFormat.Markdown;

    public string ColorKey { get; init; } = NoteColors.Default;

    /// <summary>
    /// Opacity of the note's background surface only. Text is always painted fully opaque
    /// (report p4: 배경 표면 opacity는 조절하되 텍스트 대비는 항상 유지).
    /// </summary>
    public double Opacity { get; init; } = 1.0;

    public bool AlwaysOnTop { get; init; }

    /// <summary>
    /// Whether the note is currently shown on the desktop. Closing a note window hides it
    /// (Notes Explorer still lists it) rather than deleting it, matching Sticky Notes behavior.
    /// </summary>
    public bool IsOpen { get; init; } = true;

    public NoteGeometry Geometry { get; init; } = NoteGeometry.Default;

    public Guid? NotebookId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Set when the note is moved to the deleted-notes view; null for live notes.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>
    /// Monotonic per-note revision counter. Incremented on every persisted content change and
    /// used later by sync conflict resolution (report p6: note-level operation 동기화).
    /// </summary>
    public long Rev { get; init; }

    public bool IsDeleted => DeletedAt is not null;
}
