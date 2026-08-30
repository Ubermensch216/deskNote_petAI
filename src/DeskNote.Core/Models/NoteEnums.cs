namespace DeskNote.Core.Models;

/// <summary>How a note's body text is stored and rendered.</summary>
public enum NoteFormat
{
    Markdown = 0,
    PlainText = 1,
}

/// <summary>
/// Report p4 note size presets. Custom means the user freely resized the window.
/// </summary>
public enum NoteSizePreset
{
    Small = 0,
    Medium = 1,
    Large = 2,
    Custom = 3,
}

/// <summary>Who produced a stored revision of a note's content.</summary>
public enum RevisionSource
{
    User = 0,
    Ai = 1,
}

/// <summary>How an attachment's bytes are reached.</summary>
public enum AttachmentKind
{
    /// <summary>Copied into the app's per-user data directory.</summary>
    Embedded = 0,

    /// <summary>Referenced in place. Preferred for large files.</summary>
    Link = 1,
}
