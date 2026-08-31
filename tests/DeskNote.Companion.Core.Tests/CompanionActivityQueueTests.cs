using DeskNote.Companion.Core;

namespace DeskNote.Companion.Core.Tests;

public class CompanionActivityQueueTests
{
    [Fact]
    public async Task Disabled_queue_does_not_start_or_write()
    {
        var repository = new RecordingRepository();
        await using var queue = new CompanionActivityQueue(repository, new ActivityClassifier());

        await queue.StartAsync(enabled: false, TestContext.Current.CancellationToken);
        var accepted = queue.TryEnqueue(Candidate("disabled"));

        Assert.False(queue.IsStarted);
        Assert.False(accepted);
        Assert.Equal(0, repository.RecordCount);
    }

    [Fact]
    public async Task Shutdown_drains_accepted_activity()
    {
        var repository = new RecordingRepository();
        var queue = new CompanionActivityQueue(repository, new ActivityClassifier());
        await queue.StartAsync(enabled: true, TestContext.Current.CancellationToken);

        Assert.True(queue.TryEnqueue(Candidate("drain")));
        await queue.DisposeAsync();

        Assert.Equal(1, repository.RecordCount);
    }

    private static CompanionActivityCandidate Candidate(string id) => new()
    {
        SourceEventId = id,
        Type = CompanionActivityType.MeaningfulCapture,
        OccurredAt = new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.FromHours(9)),
        CurrentContentLength = 20,
    };

    private sealed class RecordingRepository : ICompanionRepository
    {
        private static readonly CompanionSnapshot Empty = new(
            new CompanionProfile(Guid.NewGuid(), "Mori", "seed-v1", DateTimeOffset.UtcNow, 1),
            new GrowthState(),
            new DailyProgress(new DateOnly(2026, 8, 31)),
            RewardDelta.None,
            null,
            null);

        public int RecordCount { get; private set; }

        public Task<CompanionSnapshot> GetOrCreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Empty);

        public Task<CompanionRecordResult> RecordAsync(
            CompanionActivity activity,
            CancellationToken cancellationToken = default)
        {
            RecordCount++;
            return Task.FromResult(new CompanionRecordResult(true, Empty));
        }

        public Task<CompanionSnapshot> ChooseRitualAsync(
            DateOnly localDate,
            DailyRitualKind ritual,
            CancellationToken cancellationToken = default) => Task.FromResult(Empty);
    }
}
