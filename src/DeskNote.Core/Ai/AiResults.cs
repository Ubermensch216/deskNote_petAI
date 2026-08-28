namespace DeskNote.Core.Ai;

/// <summary>
/// Text produced by an AI action, kept separate from the stored note until the user applies it.
/// The UI shows <see cref="Original"/> against <see cref="Proposed"/> as a diff preview and only
/// then writes (report p7: Diff Preview → 적용).
/// </summary>
public sealed record AiTextResult
{
    public required string Original { get; init; }

    public required string Proposed { get; init; }

    /// <summary>Model that produced the text, shown in the UI so AI output is never mistaken for the user's own note.</summary>
    public string? ModelId { get; init; }

    public TimeSpan Elapsed { get; init; }
}

public enum TaskPriority
{
    Normal = 0,
    Low = 1,
    High = 2,
}

/// <summary>
/// One extracted to-do. Produced through an enforced JSON schema rather than parsed out of prose,
/// so a misread date fails validation instead of silently creating a wrong reminder (report p11).
/// </summary>
public sealed record ExtractedTask
{
    public required string Title { get; init; }

    public DateTimeOffset? DueAt { get; init; }

    public TaskPriority Priority { get; init; } = TaskPriority.Normal;

    /// <summary>Free-text owner as written in the note; not resolved to a real identity.</summary>
    public string? Assignee { get; init; }
}

/// <summary>A tag the model suggests for a note. Never applied without the user accepting it.</summary>
public sealed record SuggestedTag(string Name, double Confidence);

/// <summary>Rewrite tone presets offered by the 재작성 action (report p7).</summary>
public enum RewriteStyle
{
    Concise = 0,
    Formal = 1,
    Friendly = 2,
    Report = 3,
}
