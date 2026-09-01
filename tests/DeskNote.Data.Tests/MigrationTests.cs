using Dapper;
using DeskNote.Companion.Core;

namespace DeskNote.Data.Tests;

public class MigrationTests
{
    /// <summary>Highest migration in <c>Migrations/</c>; bump when one is added.</summary>
    private const int LatestVersion = 5;

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "desknote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public async Task First_run_creates_the_schema_and_records_the_version()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(NewDirectory(), "notes.db"));

        var result = await new MigrationRunner(factory).MigrateAsync();

        Assert.Equal(0, result.FromVersion);
        Assert.Equal(LatestVersion, result.ToVersion);
        Assert.True(result.AppliedAnything);
        Assert.Null(result.BackupPath); // Nothing existed yet, so there was nothing to back up.

        await using var connection = await factory.OpenAsync();
        var tables = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;")).ToList();

        Assert.Contains("notes", tables);
        Assert.Contains("notes_fts", tables);
        Assert.Contains("tags", tables);
        Assert.Contains("reminders", tables);
        Assert.Contains("note_revisions", tables);
        Assert.Contains("note_embeddings", tables);
        Assert.Contains("companion_profiles", tables);
        Assert.Contains("companion_event_ledger", tables);
        Assert.Contains("companion_care_ledger", tables);
        Assert.Contains("companion_suggestions", tables);
        Assert.Contains("companion_preferences", tables);
        Assert.Contains("companion_pet_progress", tables);
        Assert.Contains("schema_version", tables);
    }

    /// <summary>
    /// The V1 to V2 conversion has to keep the stage a user already reached. Demoting a pet on
    /// upgrade is the one outcome that would make the rule change feel like a punishment.
    /// </summary>
    [Fact]
    public async Task Growth_v2_conversion_preserves_the_stage_a_pet_had_reached()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(NewDirectory(), "notes.db"));
        await new MigrationRunner(factory).MigrateAsync();

        foreach (var (total, expectedStage) in new[]
                 {
                     (0, 1), (14, 1), (15, 2), (44, 2), (45, 3),
                     (89, 3), (90, 4), (149, 4), (150, 5), (300, 5),
                 })
        {
            var experience = CompanionGrowthConversionV1.ExperienceFor(total);
            var careDays = CompanionGrowthConversionV1.CareDaysFor(total);

            Assert.Equal(expectedStage, CompanionGrowthLadder.StageFor(experience, careDays));
        }

        // Migration 005 repeats this arithmetic in SQL, so the band floors are pinned here too.
        Assert.Equal(200, CompanionGrowthConversionV1.ExperienceFor(15));
        Assert.Equal(600, CompanionGrowthConversionV1.ExperienceFor(45));
        Assert.Equal(1200, CompanionGrowthConversionV1.ExperienceFor(90));
        Assert.Equal(2200, CompanionGrowthConversionV1.ExperienceFor(150));

        await using var connection = await factory.OpenAsync();
        var columns = (await connection.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('companion_pet_progress');")).ToList();
        Assert.Contains("experience", columns);
        Assert.Contains("care_days", columns);
    }

    [Fact]
    public async Task Running_again_applies_nothing()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(NewDirectory(), "notes.db"));
        var runner = new MigrationRunner(factory);
        await runner.MigrateAsync();

        var second = await runner.MigrateAsync();

        Assert.False(second.AppliedAnything);
        Assert.Equal(LatestVersion, second.FromVersion);
        Assert.Equal(LatestVersion, second.ToVersion);
    }

    [Fact]
    public async Task Write_ahead_logging_and_foreign_keys_are_on()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(NewDirectory(), "notes.db"));
        await new MigrationRunner(factory).MigrateAsync();

        await using var connection = await factory.OpenAsync();

        Assert.Equal("wal", await connection.ExecuteScalarAsync<string>("PRAGMA journal_mode;"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<long>("PRAGMA foreign_keys;"));
    }

    /// <summary>
    /// Report p6: a WAL-reset defect shipped through SQLite 3.51.2. The bundled build is asserted
    /// at runtime rather than assumed from the package graph, so an unexpected downgrade is caught.
    /// </summary>
    [Fact]
    public async Task Bundled_sqlite_is_past_the_wal_reset_fix()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(NewDirectory(), "notes.db"));

        var result = await new MigrationRunner(factory).MigrateAsync();

        Assert.False(
            MigrationRunner.IsSqliteVersionRisky(result.SqliteVersion),
            $"Bundled SQLite {result.SqliteVersion} predates {MigrationRunner.MinimumSafeSqliteVersion}.");
    }

    [Fact]
    public void Version_check_flags_affected_builds_and_clears_fixed_ones()
    {
        Assert.True(MigrationRunner.IsSqliteVersionRisky("3.51.2"));
        Assert.True(MigrationRunner.IsSqliteVersionRisky("3.50.0"));
        Assert.False(MigrationRunner.IsSqliteVersionRisky("3.51.3"));
        Assert.False(MigrationRunner.IsSqliteVersionRisky("3.52.0"));
    }

    [Fact]
    public async Task Deleting_a_note_cascades_to_its_children()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(NewDirectory(), "notes.db"));
        await new MigrationRunner(factory).MigrateAsync();

        await using var connection = await factory.OpenAsync();
        var noteId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

        await connection.ExecuteAsync(
            "INSERT INTO notes(id, created_at, updated_at) VALUES (@id, @now, @now);",
            new { id = noteId, now });
        await connection.ExecuteAsync(
            "INSERT INTO reminders(id, note_id, due_at) VALUES (@id, @noteId, @now);",
            new { id = Guid.NewGuid().ToString(), noteId, now });
        await connection.ExecuteAsync(
            "INSERT INTO checklist_items(id, note_id, text) VALUES (@id, @noteId, 'x');",
            new { id = Guid.NewGuid().ToString(), noteId });

        await connection.ExecuteAsync("DELETE FROM notes WHERE id = @id;", new { id = noteId });

        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM reminders;"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM checklist_items;"));
    }
}
