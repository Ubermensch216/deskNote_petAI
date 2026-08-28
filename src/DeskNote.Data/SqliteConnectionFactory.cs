using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

/// <summary>
/// Opens connections to the note database with the pragmas the app depends on.
/// </summary>
/// <remarks>
/// <para>
/// The database lives under <c>%LOCALAPPDATA%</c> and must not be placed in a file-sync folder
/// such as OneDrive: with WAL enabled, syncing the database file behind the app's back risks
/// corruption. Sync is done later at the note-operation level instead (report p6).
/// </para>
/// <para>
/// <c>foreign_keys</c> is per-connection in SQLite and off by default, so it is applied on every
/// open rather than once at creation.
/// </para>
/// </remarks>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>Default database location for the current user.</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskNote",
        "notes.db");

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        ApplyPragmas(connection);
        return connection;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA temp_store = MEMORY;
            """;
        command.ExecuteNonQuery();
    }
}
