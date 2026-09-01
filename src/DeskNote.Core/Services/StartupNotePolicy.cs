namespace DeskNote.Core.Services;

/// <summary>Decides whether a completely new library needs its one initial note.</summary>
public static class StartupNotePolicy
{
    public static bool ShouldCreateFirstNote(int openWindowCount, int storedNoteCount) =>
        openWindowCount == 0 && storedNoteCount == 0;
}
