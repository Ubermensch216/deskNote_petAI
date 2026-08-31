namespace DeskNote.Core.Models;

/// <summary>The product surface that brought an existing note back into view.</summary>
public enum NoteOpenOrigin
{
    Direct = 0,
    Library = 1,
    Search = 2,
    Related = 3,
    AiEvidence = 4,
    Reminder = 5,
    Briefing = 6,
    Companion = 7,
}

/// <summary>Describes why a note was opened without coupling the note layer to analytics or games.</summary>
public sealed record NoteOpenContext
{
    public static NoteOpenContext Direct { get; } = new() { Origin = NoteOpenOrigin.Direct };

    public required NoteOpenOrigin Origin { get; init; }

    /// <summary>The note whose related/evidence surface led to this note, when there was one.</summary>
    public Guid? SourceNoteId { get; init; }

    /// <summary>An optional typed suggestion id; the note layer never interprets it.</summary>
    public Guid? SuggestionId { get; init; }
}

/// <summary>A successfully focused note and the route the user took to it.</summary>
public sealed record NoteOpened(Guid NoteId, NoteOpenContext Context, DateTimeOffset OpenedAt);
