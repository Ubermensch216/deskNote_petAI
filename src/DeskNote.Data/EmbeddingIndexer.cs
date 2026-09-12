using System.Threading.Channels;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Services;

namespace DeskNote.Data;

/// <summary>
/// Keeps note embeddings in step with note text, off the save path.
/// </summary>
/// <remarks>
/// <para>
/// Indexing is a consequence of saving, never a part of it. A save must complete in under 30 ms
/// (report p13) and embedding takes longer than that, so the save enqueues an id and returns; the
/// vectors catch up a moment later. If the embedding model is missing, nothing here reports an
/// error — the note is saved either way and semantic search simply does not cover it yet.
/// </para>
/// <para>
/// Ids are coalesced: a note edited ten times while the queue is busy is embedded once, from
/// whatever the text is when its turn comes.
/// </para>
/// </remarks>
public sealed class EmbeddingIndexer(
    INoteRepository notes,
    SqliteVectorIndex vectors,
    IEmbeddingService embedder) : IDisposable
{
    /// <summary>Notes pulled in per backfill pass. Bounded so a large library is caught up gradually.</summary>
    private const int BackfillBatch = 25;

    /// <summary>
    /// Breathing room between backfill batches.
    /// </summary>
    /// <remarks>
    /// Catching up is background work by definition — the notes are already saved and already
    /// findable by keyword. Pausing between batches keeps a long catch-up from holding the
    /// embedding model busy while the user is trying to use it for something they asked for.
    /// </remarks>
    private static readonly TimeSpan BackfillPause = TimeSpan.FromSeconds(2);

    /// <summary>How often the backfill checks whether the batch it queued has been worked through.</summary>
    private static readonly TimeSpan DrainPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private readonly HashSet<Guid> _pending = [];
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();

    private Task? _worker;
    private Task? _backfill;
    private int _outstanding;

    /// <summary>Raised when a pass fails, so the caller can log it without this class knowing how.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Whether anything is queued or being embedded right now.</summary>
    public bool IsBusy => Volatile.Read(ref _outstanding) > 0;

    public void Start() => _worker ??= Task.Run(RunAsync);

    /// <summary>Queues a note for re-embedding. Returns immediately; the work happens later.</summary>
    public void Enqueue(Guid noteId)
    {
        lock (_gate)
        {
            if (!_pending.Add(noteId))
            {
                return;
            }
        }

        Interlocked.Increment(ref _outstanding);
        _queue.Writer.TryWrite(noteId);
    }

    /// <summary>
    /// Works through every note that has never been embedded, in the background.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns as soon as the catch-up has started. Awaiting it would put the whole library
    /// between the user and their first note, which is the one thing indexing is not allowed to
    /// do.
    /// </para>
    /// <para>
    /// It used to queue a single batch per launch. That is fine for a library that grew alongside
    /// the feature and useless for one that did not: ten thousand notes at twenty-five a launch is
    /// four hundred launches before semantic search covers them, so in practice importing a
    /// library meant never getting it.
    /// </para>
    /// </remarks>
    public void StartBackfill()
    {
        // Starts the worker too: the loop waits for each batch to be worked through, so without
        // one running it would wait for a queue nothing is reading.
        Start();
        _backfill ??= Task.Run(BackfillLoopAsync);
    }

    /// <summary>Queues one batch of unindexed notes. The loop in <see cref="StartBackfill"/> calls this.</summary>
    public async Task<int> BackfillAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ids = await vectors.FindUnindexedAsync(BackfillBatch, cancellationToken).ConfigureAwait(false);

            foreach (var id in ids)
            {
                Enqueue(id);
            }

            return ids.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed?.Invoke(this, ex);
            return 0;
        }
    }

    /// <summary>Embeds one note now. Public so a test can drive it without the queue.</summary>
    public async Task IndexAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var note = await notes.GetAsync(noteId, cancellationToken).ConfigureAwait(false);

        if (note is null)
        {
            await vectors.RemoveNoteAsync(noteId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var chunks = NoteChunker.Split(note.Content);

        if (chunks.Count == 0)
        {
            // An emptied note keeps no vectors: otherwise it would go on matching text it no
            // longer contains.
            await vectors.RemoveNoteAsync(noteId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var embeddings = await embedder
            .EmbedAsync([.. chunks.Select(chunk => chunk.Text)], cancellationToken)
            .ConfigureAwait(false);

        if (embeddings.Count != chunks.Count)
        {
            // No model, or a batch that came back malformed. Leaving the old vectors in place is
            // better than dropping them: stale retrieval beats none until the model returns.
            return;
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            await vectors.UpsertAsync(noteId, chunks[i].Ordinal, embeddings[i], cancellationToken)
                .ConfigureAwait(false);
        }

        await vectors.TrimAsync(noteId, chunks.Count, cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// The token source is cancelled but deliberately not disposed: the worker and the backfill
    /// loop are still inside awaits that hold its token, and disposing it underneath them turns a
    /// clean shutdown into an <see cref="ObjectDisposedException"/> on a background thread. It
    /// owns no unmanaged resource and no timer, so outliving this call costs nothing.
    /// </remarks>
    public void Dispose()
    {
        _stopping.Cancel();
        _queue.Writer.TryComplete();
    }

    /// <summary>
    /// Queues batch after batch until the library is covered.
    /// </summary>
    /// <remarks>
    /// Each batch is waited out before the next is read, because an unindexed note stays unindexed
    /// in the database until its vectors are written — asking again too early returns the same
    /// notes. A round that changes nothing ends the loop: with no embedding model installed every
    /// note fails the same way, and retrying them forever would be a spin rather than a catch-up.
    /// The next launch tries again, by which time the model may be there.
    /// </remarks>
    private async Task BackfillLoopAsync()
    {
        try
        {
            IReadOnlyList<Guid> previous = [];

            while (!_stopping.IsCancellationRequested)
            {
                var ids = await vectors
                    .FindUnindexedAsync(BackfillBatch, _stopping.Token)
                    .ConfigureAwait(false);

                if (ids.Count == 0 || ids.SequenceEqual(previous))
                {
                    return;
                }

                previous = ids;

                foreach (var id in ids)
                {
                    Enqueue(id);
                }

                await WaitForBatchAsync().ConfigureAwait(false);
                await Task.Delay(BackfillPause, _stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex);
        }
    }

    private async Task WaitForBatchAsync()
    {
        while (IsBusy && !_stopping.IsCancellationRequested)
        {
            await Task.Delay(DrainPollInterval, _stopping.Token).ConfigureAwait(false);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var noteId in _queue.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    _pending.Remove(noteId);
                }

                try
                {
                    await IndexAsync(noteId, _stopping.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One note that cannot be embedded must not stop the queue for every other.
                    Failed?.Invoke(this, ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _outstanding);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
