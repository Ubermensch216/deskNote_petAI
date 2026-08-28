using System.Globalization;
using System.Text.Json;

namespace DeskNote.Core.Services;

/// <summary>Text that was being saved when the app last stopped.</summary>
public sealed record JournalEntry(Guid NoteId, string Title, string Content, DateTimeOffset RecordedAt);

/// <summary>
/// An on-disk record of note text that has been typed but not yet confirmed saved.
/// </summary>
/// <remarks>
/// <para>
/// Each note gets its own small file, written immediately before the database write is attempted
/// and deleted once that write succeeds. A leftover file therefore means exactly one thing: the
/// app went away, or the database write failed, between those two points. That is the case a
/// note-taking app cannot afford to lose (report p5 lists crash recovery as 메모 앱 신뢰의 핵심).
/// </para>
/// <para>
/// This does not make the debounce lossless. Text typed in the last few hundred milliseconds
/// before a hard power cut was never handed to the scheduler and is not here either; the journal
/// covers failed and interrupted saves, not the debounce window itself.
/// </para>
/// <para>
/// Files are written to a temporary name and then moved into place, so a crash during the write
/// leaves either the previous entry or none — never a half-written one that fails to parse during
/// recovery.
/// </para>
/// </remarks>
public sealed class CrashJournal(string directory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly Lock _gate = new();

    public string Directory { get; } = directory;

    /// <summary>Records the text about to be written for a note, replacing any earlier entry.</summary>
    public void Record(Guid noteId, string title, string content)
    {
        var entry = new JournalEntry(noteId, title, content, DateTimeOffset.UtcNow);

        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var final = PathFor(noteId);
                var temporary = final + ".tmp";

                File.WriteAllText(temporary, JsonSerializer.Serialize(entry, SerializerOptions));
                File.Move(temporary, final, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The journal is a safety net, not the save path. Failing to write it must not
                // stop the database write that follows.
            }
        }
    }

    /// <summary>Drops a note's entry once its text is safely in the database.</summary>
    public void Clear(Guid noteId)
    {
        lock (_gate)
        {
            try
            {
                File.Delete(PathFor(noteId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stale entry is harmless: recovery compares it against the stored note and
                // discards it when they already agree.
            }
        }
    }

    /// <summary>Reads every entry left behind by a previous run.</summary>
    public IReadOnlyList<JournalEntry> ReadAll()
    {
        lock (_gate)
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return [];
            }

            var entries = new List<JournalEntry>();

            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<JournalEntry>(File.ReadAllText(path));
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    // An unreadable entry cannot be recovered from, and must not stop the app from
                    // starting or block recovery of the other notes.
                }
            }

            return entries;
        }
    }

    /// <summary>Removes every entry. Used once recovery has been resolved one way or the other.</summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            foreach (var entry in ReadAllPaths())
            {
                try
                {
                    File.Delete(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private IEnumerable<string> ReadAllPaths() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json").ToList()
            : [];

    private string PathFor(Guid noteId) =>
        Path.Combine(Directory, noteId.ToString("N", CultureInfo.InvariantCulture) + ".json");
}
