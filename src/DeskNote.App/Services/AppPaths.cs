namespace DeskNote.App.Services;

/// <summary>
/// Where DeskNote keeps a user's data.
/// </summary>
/// <remarks>
/// Everything lives under %LOCALAPPDATA% rather than a roaming or synced folder. Report p6 warns
/// specifically against letting an external file sync touch a live WAL-mode database; sync arrives
/// later as note-level replication instead.
/// </remarks>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskNote");

    /// <summary>Holds text that was typed but not yet confirmed saved.</summary>
    public static string JournalDirectory { get; } = Path.Combine(Root, "journal");
}
