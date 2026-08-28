using System.Globalization;

namespace DeskNote.App.Services;

/// <summary>
/// Appends diagnostic lines to a per-user log file.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately never records note text. Report p15 rules out note-content telemetry and requires
/// crash reports to be scrubbed of note text and prompts, so callers pass exceptions and short
/// descriptions — never the user's content.
/// </para>
/// <para>
/// Writing is best effort: a logger that throws while reporting a failure is worse than one that
/// stays quiet.
/// </para>
/// </remarks>
internal static class CrashLog
{
    private static readonly Lock Gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskNote",
        "logs",
        "desknote.log");

    public static void Write(string message, Exception? exception = null)
    {
        var line = exception is null
            ? $"{Timestamp()} {message}"
            : $"{Timestamp()} {message}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}";

        System.Diagnostics.Debug.WriteLine(line);

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            // Nothing useful left to do; the Debug line above is the fallback.
        }
    }

    private static string Timestamp() =>
        DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
