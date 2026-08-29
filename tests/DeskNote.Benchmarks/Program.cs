using System.Diagnostics;
using System.Globalization;
using DeskNote.Benchmarks;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using DeskNote.Data;

// Measures the report's release SLOs (p13-14) against a library the size the report specifies.
// Exits non-zero when a target is missed, so this can gate a release rather than only inform one.

// A second mode used by the hardening check: write notes continuously at the given path until the
// process is killed. Abrupt termination mid-write is the case report p18 lists as a release gate
// (강제 종료, 전원 차단 simulation), and it cannot be exercised without a process to actually kill.
if (args.Length >= 2 && args[0] == "--crash-writer")
{
    var writerConnections = new SqliteConnectionFactory(args[1]);
    await new MigrationRunner(writerConnections).MigrateAsync();
    var writerRepository = new SqliteNoteRepository(writerConnections, SystemClock.Instance);

    var written = 0;
    while (true)
    {
        var note = new Note { Id = Guid.CreateVersion7(), CreatedAt = DateTimeOffset.UtcNow };
        await writerRepository.AddAsync(note);
        await writerRepository.UpdateContentAsync(
            note.Id,
            $"제목 {written}",
            $"본문 {written}\n- [ ] 항목\n#hardening");

        // Flushed so an external observer can tell how far the writer got before it died.
        Console.WriteLine(++written);
        await Console.Out.FlushAsync();
    }
}

var noteCount = args.Length > 0 && int.TryParse(args[0], CultureInfo.InvariantCulture, out var parsed)
    ? parsed
    : 10_000;

var directory = Path.Combine(Path.GetTempPath(), "desknote-bench", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var databasePath = Path.Combine(directory, "notes.db");

Console.WriteLine($"DeskNote SLO benchmark — {noteCount:N0} notes");
Console.WriteLine($"database: {databasePath}");
Console.WriteLine();

var connections = new SqliteConnectionFactory(databasePath);
var migration = await new MigrationRunner(connections).MigrateAsync();
Console.WriteLine($"SQLite {migration.SqliteVersion}"
    + (MigrationRunner.IsSqliteVersionRisky(migration.SqliteVersion) ? "  (predates the WAL-reset fix)" : string.Empty));

IClock clock = SystemClock.Instance;
var notes = new SqliteNoteRepository(connections, clock);
var search = new Fts5SearchIndex(connections);
var library = new SqliteNoteLibrary(connections, clock);

// ---- seed ----------------------------------------------------------------------------------
var seedWatch = Stopwatch.StartNew();
var seeded = new List<Guid>(noteCount);

foreach (var note in NoteCorpus.Generate(noteCount, clock.UtcNow))
{
    await notes.AddAsync(note);
    await notes.UpdateContentAsync(note.Id, note.Title, note.Content);
    seeded.Add(note.Id);
}

seedWatch.Stop();
Console.WriteLine(
    $"seeded in {seedWatch.Elapsed.TotalSeconds:0.0} s "
    + $"({seedWatch.Elapsed.TotalMilliseconds / noteCount:0.00} ms per note, content + checklist + tag projection)");
Console.WriteLine($"database size: {new FileInfo(databasePath).Length / 1024.0 / 1024.0:0.0} MB");
Console.WriteLine();

// ---- measurements --------------------------------------------------------------------------
var koreanQueries = new[] { "수도관", "누수 점검", "응답속도", "회의록", "릴리스" };
var englishQueries = new[] { "deployment", "latency spike", "release notes", "sprint", "vendor" };
var random = new Random(20260829);

var results = new List<Measurement>
{
    await Benchmark.MeasureAsync(
        "note save (content + projections)",
        300,
        async i =>
        {
            var id = seeded[random.Next(seeded.Count)];
            await notes.UpdateContentAsync(id, "벤치 제목", $"수정된 본문 {i}\n- [ ] 확인\n#backend");
        },
        targetMs: 30,
        source: "report p14 — 기본 note save p95"),

    await Benchmark.MeasureAsync(
        "full-text search (Korean)",
        200,
        async i => await search.SearchAsync(koreanQueries[i % koreanQueries.Length]),
        targetMs: 100,
        source: "report p14 — 10,000개 FTS 검색 p95"),

    await Benchmark.MeasureAsync(
        "full-text search (English)",
        200,
        async i => await search.SearchAsync(englishQueries[i % englishQueries.Length]),
        targetMs: 100,
        source: "report p14 — 10,000개 FTS 검색 p95"),

    await Benchmark.MeasureAsync(
        "search under 3 chars (LIKE path)",
        50,
        async i => await search.SearchAsync(i % 2 == 0 ? "회의" : "de"),
        targetMs: null,
        source: "trigram fallback — informational"),

    await Benchmark.MeasureAsync(
        "startup restore query",
        200,
        async _ => await notes.GetOpenNotesAsync(),
        targetMs: 100,
        source: "report p14 — <500 ms restore budget"),

    await Benchmark.MeasureAsync(
        "library list (200 rows + tags)",
        100,
        async _ => await library.ListAsync(NoteQuery.Default),
        targetMs: 100,
        source: "Notes Explorer first paint"),

    await Benchmark.MeasureAsync(
        "library search + summaries",
        100,
        async i => await library.SearchAsync(koreanQueries[i % koreanQueries.Length], NoteQuery.Default),
        targetMs: 100,
        source: "report p14 — find-as-you-type"),

    await Benchmark.MeasureAsync(
        "tag list with counts",
        100,
        async _ => await library.ListTagsAsync(),
        targetMs: null,
        source: "Explorer sidebar — informational"),

    await Benchmark.MeasureAsync(
        "single note read",
        300,
        async i => await notes.GetAsync(seeded[i % seeded.Count]),
        targetMs: null,
        source: "informational"),
};

Console.WriteLine(Measurement.Header());
Console.WriteLine(new string('-', 110));

foreach (var result in results)
{
    Console.WriteLine(result.ToRow());
}

Console.WriteLine();

var failures = results.Where(r => !r.Passes).ToList();

if (failures.Count == 0)
{
    Console.WriteLine("All targets met.");
}
else
{
    Console.WriteLine($"{failures.Count} target(s) missed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"  {failure.Name}: p95 {failure.P95:0.00} ms > {failure.TargetMs} ms ({failure.Source})");
    }
}

Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
try
{
    Directory.Delete(directory, recursive: true);
}
catch (IOException)
{
    Console.WriteLine($"(left {directory} behind)");
}

return failures.Count == 0 ? 0 : 1;
