using System.Threading.Channels;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Services;
using DeskNote.Data;

namespace DeskNote.App.Services;

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

    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private readonly HashSet<Guid> _pending = [];
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();

    private Task? _worker;

    /// <summary>Raised when a pass fails, so the caller can log it without this class knowing how.</summary>
    public event EventHandler<Exception>? Failed;

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

        _queue.Writer.TryWrite(noteId);
    }

    /// <summary>
    /// Queues notes that have never been embedded, so search covers the library rather than only
    /// what has been edited since this feature arrived.
    /// </summary>
    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var id in await vectors.FindUnindexedAsync(BackfillBatch, cancellationToken)
                         .ConfigureAwait(false))
            {
                Enqueue(id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed?.Invoke(this, ex);
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

    public void Dispose()
    {
        _stopping.Cancel();
        _queue.Writer.TryComplete();
        _stopping.Dispose();
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
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
