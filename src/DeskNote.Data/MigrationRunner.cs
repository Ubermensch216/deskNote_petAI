using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DeskNote.Data;

/// <summary>Outcome of a migration run, surfaced so startup can log or warn.</summary>
/// <param name="FromVersion">Schema version before the run.</param>
/// <param name="ToVersion">Schema version after the run.</param>
/// <param name="BackupPath">Backup taken before upgrading an existing database; null on a first run or a no-op run.</param>
/// <param name="SqliteVersion">The SQLite build actually loaded at runtime.</param>
public sealed record MigrationResult(int FromVersion, int ToVersion, string? BackupPath, string SqliteVersion)
{
    public bool AppliedAnything => ToVersion > FromVersion;
}

/// <summary>
/// Applies embedded <c>NNN_name.sql</c> migrations in order, recording each in
/// <c>schema_version</c>.
/// </summary>
/// <remarks>
/// A copy of the database is taken before the first migration of a run so a failed upgrade can be
/// rolled back to N-1, which the report lists as a release gate alongside migration testing (p6).
/// </remarks>
public sealed partial class MigrationRunner(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// Report p6: a WAL-reset defect existed through SQLite 3.51.2 and was fixed in 3.51.3.
    /// The bundled build is checked at runtime instead of being inferred from the package graph.
    /// </summary>
    public static readonly Version MinimumSafeSqliteVersion = new(3, 51, 3);

    [GeneratedRegex(@"^(?<version>\d{3})_(?<name>.+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationFileName { get; }

    public async Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sqliteVersion = await ScalarAsync(connection, "SELECT sqlite_version();", cancellationToken)
            .ConfigureAwait(false) ?? "unknown";

        await EnsureVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);
        var current = await GetCurrentVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        var pending = DiscoverMigrations().Where(m => m.Version > current).OrderBy(m => m.Version).ToList();
        if (pending.Count == 0)
        {
            return new MigrationResult(current, current, null, sqliteVersion);
        }

        // Only worth backing up when there is prior data to roll back to. On a first run the
        // connection above has already created an empty file, so backing it up would drop a
        // pointless .bak next to every new installation.
        var backupPath = current > 0 ? BackupDatabase() : null;

        foreach (var migration in pending)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var record = connection.CreateCommand())
            {
                record.Transaction = (SqliteTransaction)transaction;
                record.CommandText =
                    "INSERT INTO schema_version(version, name, applied_at) VALUES ($v, $n, $t);";
                record.Parameters.AddWithValue("$v", migration.Version);
                record.Parameters.AddWithValue("$n", migration.Name);
                record.Parameters.AddWithValue("$t", SqliteTime.ToDb(DateTimeOffset.UtcNow));
                await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new MigrationResult(current, pending[^1].Version, backupPath, sqliteVersion);
    }

    /// <summary>
    /// True when the loaded SQLite predates the WAL-reset fix. Callers surface this as a warning
    /// rather than a hard failure so an old build never blocks access to the user's notes.
    /// </summary>
    public static bool IsSqliteVersionRisky(string sqliteVersion) =>
        Version.TryParse(sqliteVersion, out var parsed) && parsed < MinimumSafeSqliteVersion;

    private string? BackupDatabase()
    {
        var path = connectionFactory.DatabasePath;
        if (!File.Exists(path))
        {
            return null;
        }

        var backupPath = $"{path}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak";
        File.Copy(path, backupPath, overwrite: true);
        return backupPath;
    }

    private static async Task EnsureVersionTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                version    INTEGER NOT NULL PRIMARY KEY,
                name       TEXT    NOT NULL,
                applied_at TEXT    NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var value = await ScalarAsync(connection, "SELECT COALESCE(MAX(version), 0) FROM schema_version;", cancellationToken)
            .ConfigureAwait(false);
        return value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result?.ToString();
    }

    private static List<(int Version, string Name, string Sql)> DiscoverMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = new List<(int, string, string)>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            var fileName = resourceName.Split('.') is { Length: >= 2 } parts
                ? $"{parts[^2]}.{parts[^1]}"
                : resourceName;

            var match = MigrationFileName.Match(fileName);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            migrations.Add((
                int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["name"].Value,
                reader.ReadToEnd()));
        }

        return migrations;
    }
}
