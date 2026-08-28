using DeskNote.Core.Abstractions;
using DeskNote.Data;

namespace DeskNote.Data.Tests;

/// <summary>A clock the tests can move, so revision and timestamp ordering is deterministic.</summary>
internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start.ToUniversalTime();

    public DateTimeOffset Now => UtcNow.ToLocalTime();

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>
/// A migrated, throwaway note database on disk.
/// </summary>
/// <remarks>
/// Deliberately file-backed rather than <c>:memory:</c>: WAL mode, the pragmas in
/// <see cref="SqliteConnectionFactory"/> and the backup step in <see cref="MigrationRunner"/> only
/// behave realistically against a real file, and those are exactly what the persistence tests are
/// meant to cover.
/// </remarks>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private TestDatabase(string directory, SqliteConnectionFactory factory, FakeClock clock)
    {
        _directory = directory;
        Factory = factory;
        Clock = clock;
        Notes = new SqliteNoteRepository(factory, clock);
        Search = new Fts5SearchIndex(factory);
        Settings = new SqliteSettingsStore(factory);
    }

    public SqliteConnectionFactory Factory { get; }

    public FakeClock Clock { get; }

    public SqliteNoteRepository Notes { get; }

    public Fts5SearchIndex Search { get; }

    public SqliteSettingsStore Settings { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "desknote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var factory = new SqliteConnectionFactory(Path.Combine(directory, "notes.db"));
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero));

        await new MigrationRunner(factory).MigrateAsync();

        return new TestDatabase(directory, factory, clock);
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A lingering file handle must not fail an otherwise passing test.
        }

        return ValueTask.CompletedTask;
    }
}
