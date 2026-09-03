namespace DeskNote.Core.Services;

/// <summary>What the desktop should have on it once startup has restored what it can.</summary>
public enum StartupNoteAction
{
    /// <summary>Notes came back on their own. Nothing else is opened.</summary>
    Nothing = 0,

    /// <summary>Nothing was open, but notes exist: bring back the one last worked on.</summary>
    OpenMostRecent = 1,

    /// <summary>There is nothing to bring back, so the user starts on a blank note.</summary>
    CreateFirstNote = 2,
}

/// <summary>
/// Decides what a launch puts on the desktop.
/// </summary>
/// <remarks>
/// <para>
/// A sticky-note app opens with notes on the desktop. It used to open Notes Explorer whenever no
/// note was flagged open, which answered "where is my note?" with a file manager — and since
/// shutting down cleared that flag on every note, that was the ordinary case rather than the edge
/// one. The library stays one click away on the note chrome, the tray and the search hotkey.
/// </para>
/// <para>
/// Reopening the most recent note cannot pile up empty notes: the blank note from a first launch
/// is itself a stored note, so the next launch reopens that one instead of adding another.
/// </para>
/// </remarks>
public static class StartupNotePolicy
{
    /// <param name="openWindowCount">Notes already restored to the desktop.</param>
    /// <param name="liveNoteCount">Stored notes that are not in the deleted view.</param>
    public static StartupNoteAction Decide(int openWindowCount, int liveNoteCount)
    {
        if (openWindowCount > 0)
        {
            return StartupNoteAction.Nothing;
        }

        // Only deleted notes left counts as nothing to bring back: restoring one is a decision
        // the user makes in the library, not something a launch should make for them.
        return liveNoteCount > 0
            ? StartupNoteAction.OpenMostRecent
            : StartupNoteAction.CreateFirstNote;
    }
}
