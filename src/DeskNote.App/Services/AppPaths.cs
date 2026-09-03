namespace DeskNote.App.Services;

/// <summary>
/// Where DeskNote keeps a user's data.
/// </summary>
/// <remarks>
/// <para>
/// Everything lives under %LOCALAPPDATA% rather than a roaming or synced folder. Report p6 warns
/// specifically against letting an external file sync touch a live WAL-mode database; sync arrives
/// later as note-level replication instead.
/// </para>
/// <para>
/// This is the one place that decides the location. The database, the attachments, the journal and
/// the log all hang off <see cref="Root"/>, so a second instance pointed elsewhere takes all four
/// with it rather than writing its notes to a demo folder and its log to the real one.
/// </para>
/// </remarks>
internal static class AppPaths
{
    public static string Root { get; } = Resolve();

    /// <summary>The notes database.</summary>
    public static string DatabasePath { get; } = Path.Combine(Root, "notes.db");

    /// <summary>Holds text that was typed but not yet confirmed saved.</summary>
    public static string JournalDirectory { get; } = Path.Combine(Root, "journal");

    public static string LogDirectory { get; } = Path.Combine(Root, "logs");

    /// <summary>Whether the data directory came from the command line or the environment.</summary>
    public static bool IsOverridden { get; private set; }

    /// <summary>
    /// Resolves the data directory, honouring an explicit override.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--data &lt;path&gt;</c> on the command line, or the <c>DESKNOTE_DATA</c> environment
    /// variable. Without one of these there is no way to run an instance beside a real
    /// installation: <see cref="Environment.GetFolderPath"/> reads the shell's known folder and
    /// ignores <c>LOCALAPPDATA</c>, so the documented trick of setting that variable does nothing,
    /// and the only remaining option was moving the user's live database aside.
    /// </para>
    /// <para>
    /// That matters for the screen photographs in <c>docs/images/</c>, which are taken from a demo
    /// database precisely so that nobody's real notes end up in the README.
    /// </para>
    /// </remarks>
    private static string Resolve()
    {
        var arguments = Environment.GetCommandLineArgs();

        for (var index = 1; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], "--data", StringComparison.OrdinalIgnoreCase))
            {
                return Accept(arguments[index + 1]);
            }
        }

        if (Environment.GetEnvironmentVariable("DESKNOTE_DATA") is { Length: > 0 } fromEnvironment)
        {
            return Accept(fromEnvironment);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskNote");
    }

    private static string Accept(string path)
    {
        IsOverridden = true;
        return Path.GetFullPath(path);
    }
}
