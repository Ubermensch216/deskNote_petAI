using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Data.Tests;

/// <summary>
/// The catch-up that decides whether semantic search covers a library or only the part of it
/// written after the feature arrived.
/// </summary>
public class EmbeddingIndexerTests
{
    private const int Dimensions = 8;

    /// <summary>An embedder that answers instantly, and can be told to stop answering.</summary>
    private sealed class StubEmbedder : IEmbeddingService
    {
        public int Dimensions => EmbeddingIndexerTests.Dimensions;

        /// <summary>When false every batch comes back empty, which is how a missing model behaves.</summary>
        public bool IsAvailable { get; set; } = true;

        public int Calls { get; private set; }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            if (!IsAvailable)
            {
                return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([]);
            }

            var vectors = inputs
                .Select(text => new ReadOnlyMemory<float>(
                    [.. Enumerable.Range(0, Dimensions).Select(i => (float)((text.Length + i) % 7) + 0.5f)]))
                .ToList();

            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
        }
    }

    private static async Task<List<Guid>> SeedNotesAsync(TestDatabase database, int count)
    {
        var ids = new List<Guid>(count);

        for (var i = 0; i < count; i++)
        {
            var note = new Note { Id = Guid.NewGuid(), Content = $"메모 {i} 본문입니다." };
            await database.Notes.AddAsync(note);
            ids.Add(note.Id);
        }

        return ids;
    }

    private static async Task<int> UnindexedCountAsync(TestDatabase database, SqliteVectorIndex vectors) =>
        (await vectors.FindUnindexedAsync(1000)).Count;

    private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    /// <summary>
    /// A library bigger than one batch still ends up fully covered.
    /// </summary>
    /// <remarks>
    /// The backfill used to queue twenty-five notes once per launch, so a ten-thousand note
    /// library needed four hundred launches before semantic search reached the end of it — which
    /// in practice meant importing a library and never getting search over it. The loop has to
    /// keep going until nothing is left.
    /// </remarks>
    [Fact]
    public async Task Backfill_covers_a_library_larger_than_one_batch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var vectors = new SqliteVectorIndex(database.Factory, "test-embed");
        var embedder = new StubEmbedder();

        // Three batches' worth: one pass would leave two thirds of it unreachable.
        await SeedNotesAsync(database, 60);
        Assert.Equal(60, await UnindexedCountAsync(database, vectors));

        using var indexer = new EmbeddingIndexer(database.Notes, vectors, embedder);
        indexer.StartBackfill();

        await WaitForAsync(async () => await UnindexedCountAsync(database, vectors) == 0);

        Assert.Equal(0, await UnindexedCountAsync(database, vectors));
    }

    /// <summary>
    /// With no embedding model the loop has to stop, not spin.
    /// </summary>
    /// <remarks>
    /// An unindexed note stays unindexed when embedding fails, so a loop that simply re-read the
    /// pending set would queue the same notes forever — a background thread burning the disk for
    /// as long as the daemon is down. Ending the round instead leaves the work for the next
    /// launch, by which time the model may be installed.
    /// </remarks>
    [Fact]
    public async Task Backfill_stops_when_a_round_indexes_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var vectors = new SqliteVectorIndex(database.Factory, "test-embed");
        var embedder = new StubEmbedder { IsAvailable = false };

        await SeedNotesAsync(database, 5);

        using var indexer = new EmbeddingIndexer(database.Notes, vectors, embedder);
        indexer.StartBackfill();

        await WaitForAsync(() => Task.FromResult(embedder.Calls >= 5), timeoutMs: 5000);
        var afterFirstRound = embedder.Calls;

        // Long enough for several more rounds, had the loop kept queueing them.
        await Task.Delay(3000);

        Assert.Equal(afterFirstRound, embedder.Calls);
        Assert.Equal(5, await UnindexedCountAsync(database, vectors));
    }

    /// <summary>
    /// One batch is not a catch-up, which is why the loop above has to exist.
    /// </summary>
    /// <remarks>
    /// Stated as its own test so the batch size stays a pacing decision rather than the limit of
    /// what gets indexed: this is exactly what a launch used to do, and all it used to do.
    /// </remarks>
    [Fact]
    public async Task One_batch_leaves_a_large_library_partly_unindexed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var vectors = new SqliteVectorIndex(database.Factory, "test-embed");

        await SeedNotesAsync(database, 60);

        using var indexer = new EmbeddingIndexer(database.Notes, vectors, new StubEmbedder());
        indexer.Start();

        var queued = await indexer.BackfillAsync();
        await WaitForAsync(() => Task.FromResult(!indexer.IsBusy));

        Assert.Equal(25, queued);
        Assert.Equal(35, await UnindexedCountAsync(database, vectors));
    }

    /// <summary>Startup must not wait for the library; the catch-up is background work.</summary>
    [Fact]
    public async Task Starting_the_backfill_returns_before_the_work_is_done()
    {
        await using var database = await TestDatabase.CreateAsync();
        var vectors = new SqliteVectorIndex(database.Factory, "test-embed");

        await SeedNotesAsync(database, 60);

        using var indexer = new EmbeddingIndexer(database.Notes, vectors, new StubEmbedder());

        var started = System.Diagnostics.Stopwatch.StartNew();
        indexer.StartBackfill();
        started.Stop();

        Assert.True(
            started.ElapsedMilliseconds < 500,
            $"StartBackfill blocked for {started.ElapsedMilliseconds} ms.");
    }

    /// <summary>A note saved while the catch-up is running is embedded like any other.</summary>
    [Fact]
    public async Task An_enqueued_note_is_embedded()
    {
        await using var database = await TestDatabase.CreateAsync();
        var vectors = new SqliteVectorIndex(database.Factory, "test-embed");

        var ids = await SeedNotesAsync(database, 1);

        using var indexer = new EmbeddingIndexer(database.Notes, vectors, new StubEmbedder());
        indexer.Start();
        indexer.Enqueue(ids[0]);

        await WaitForAsync(async () => await UnindexedCountAsync(database, vectors) == 0);

        Assert.Equal(0, await UnindexedCountAsync(database, vectors));

        // The queue is only reported idle once the worker has left the note, which is a moment
        // after its vectors are readable — asserting it the instant the rows appear is a race.
        await WaitForAsync(() => Task.FromResult(!indexer.IsBusy));
        Assert.False(indexer.IsBusy);
    }
}
