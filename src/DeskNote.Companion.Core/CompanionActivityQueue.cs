using System.Threading.Channels;

namespace DeskNote.Companion.Core;

/// <summary>Best-effort background boundary that keeps game writes off the note save path.</summary>
public sealed class CompanionActivityQueue(
    ICompanionRepository repository,
    ActivityClassifier classifier) : IAsyncDisposable
{
    private readonly Channel<CompanionActivityCandidate> _channel = Channel.CreateBounded<CompanionActivityCandidate>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private volatile bool _enabled;

    public event Action<CompanionSnapshot>? SnapshotChanged;

    public event Action<Exception>? Failed;

    public bool IsStarted => _worker is not null;

    public async Task StartAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _enabled = enabled;
        if (!enabled || _worker is not null)
        {
            return;
        }

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = RunAsync(_lifetime.Token);
        SnapshotChanged?.Invoke(await repository.GetOrCreateAsync(cancellationToken).ConfigureAwait(false));
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        SnapshotChanged?.Invoke(await repository.GetOrCreateAsync(cancellationToken).ConfigureAwait(false));
    }

    public bool TryEnqueue(CompanionActivityCandidate candidate) =>
        _enabled && _channel.Writer.TryWrite(candidate);

    public async Task ChooseRitualAsync(
        DailyRitualKind ritual,
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        var snapshot = await repository.ChooseRitualAsync(localDate, ritual, cancellationToken)
            .ConfigureAwait(false);
        SnapshotChanged?.Invoke(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown.
            }
        }

        _enabled = false;
        _lifetime?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var candidate in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_enabled)
            {
                continue;
            }

            try
            {
                if (classifier.Classify(candidate) is { } activity)
                {
                    var result = await repository.RecordAsync(activity, cancellationToken).ConfigureAwait(false);
                    if (result.Recorded)
                    {
                        SnapshotChanged?.Invoke(result.Snapshot);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Failed?.Invoke(ex);
            }
        }
    }
}
