using System.Collections.Concurrent;

namespace DeskNote.Core.Services;

/// <summary>
/// Write-behind autosave with a per-note debounce.
/// </summary>
/// <remarks>
/// <para>
/// Report p5 asks for a save to begin 250–500 ms after typing stops, rather than on every
/// keystroke. Each note gets its own timer, so typing in one note never delays the save of
/// another.
/// </para>
/// <para>
/// <see cref="FlushAsync"/> exists for the moments a debounce must not be honoured — the window
/// losing focus, being closed, or the session ending. Those paths have to complete the write
/// before the app can be allowed to go away.
/// </para>
/// </remarks>
public sealed class AutosaveScheduler : IAsyncDisposable
{
    /// <summary>Middle of the 250–500 ms band the report specifies.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ConcurrentDictionary<Guid, PendingSave> _pending = new();

    /// <summary>The write currently in flight for a note, or the last one queued behind it.</summary>
    /// <remarks>
    /// A note leaves <see cref="_pending"/> the moment its write begins, so without this a save
    /// that had already started was invisible: shutdown walked an empty queue and let the process
    /// end underneath it, and a second write for the same note could overtake the first and leave
    /// the note holding the older text. Writes are chained per note, so they land in order, and
    /// <see cref="FlushAllAsync"/> can wait for the ones already running.
    /// </remarks>
    private readonly Dictionary<Guid, Task> _writing = [];

    private readonly Lock _writingGate = new();
    private readonly TimeSpan _debounce;
    private readonly Func<Guid, string, string, CancellationToken, Task> _save;
    private readonly CrashJournal? _journal;

    public AutosaveScheduler(
        Func<Guid, string, string, CancellationToken, Task> save,
        TimeSpan? debounce = null,
        CrashJournal? journal = null)
    {
        _save = save;
        _debounce = debounce ?? DefaultDebounce;
        _journal = journal;
    }

    /// <summary>Raised when a scheduled write fails, so the UI can warn instead of losing text silently.</summary>
    public event EventHandler<Exception>? SaveFailed;

    /// <summary>Records the latest text for a note and restarts its debounce timer.</summary>
    public void Schedule(Guid noteId, string title, string content)
    {
        var entry = _pending.AddOrUpdate(
            noteId,
            _ => new PendingSave(title, content),
            (_, existing) =>
            {
                existing.Update(title, content);
                return existing;
            });

        entry.Restart(_debounce, () => RunSaveAsync(noteId));
    }

    /// <summary>Writes a note's pending text immediately, if it has any. Safe to call when nothing is pending.</summary>
    public async Task FlushAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        if (!_pending.TryRemove(noteId, out var entry))
        {
            return;
        }

        entry.Cancel();
        var (title, content) = entry.Snapshot();
        await SaveSafelyAsync(noteId, title, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes every pending note. Used on session end and before shutdown.</summary>
    /// <remarks>
    /// Draining afterwards is the point: a note whose write had already begun is no longer in the
    /// pending queue, and returning without waiting for it is what let shutdown race the last save
    /// of the session.
    /// </remarks>
    public async Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var noteId in _pending.Keys.ToList())
        {
            await FlushAsync(noteId, cancellationToken).ConfigureAwait(false);
        }

        await DrainAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until no write is in flight.
    /// </summary>
    /// <remarks>
    /// Loops because a write chained behind the one being awaited only becomes visible once the
    /// first completes. It terminates as soon as nothing new is scheduled, which on the shutdown
    /// path is guaranteed: external input is disconnected before the flush.
    /// </remarks>
    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_writingGate)
            {
                running = [.. _writing.Values];
            }

            if (running.Length == 0)
            {
                return;
            }

            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    public bool HasPendingWork
    {
        get
        {
            if (!_pending.IsEmpty)
            {
                return true;
            }

            lock (_writingGate)
            {
                return _writing.Count > 0;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAllAsync().ConfigureAwait(false);
    }

    private async Task RunSaveAsync(Guid noteId)
    {
        if (!_pending.TryRemove(noteId, out var entry))
        {
            return;
        }

        var (title, content) = entry.Snapshot();
        await SaveSafelyAsync(noteId, title, content, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues a write for a note behind whatever is already writing it.
    /// </summary>
    /// <remarks>
    /// Two writes for one note are never in flight at once. Both carry the whole note rather than
    /// a delta, so if the earlier one finished last the note would be left holding text the user
    /// had already replaced — a save silently undoing the edit that followed it.
    /// </remarks>
    private Task SaveSafelyAsync(Guid noteId, string title, string content, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;

        lock (_writingGate)
        {
            previous = _writing.TryGetValue(noteId, out var running) ? running : Task.CompletedTask;
            _writing[noteId] = completion.Task;
        }

        _ = WriteAfterAsync(previous, completion, noteId, title, content, cancellationToken);
        return completion.Task;
    }

    private async Task WriteAfterAsync(
        Task previous,
        TaskCompletionSource completion,
        Guid noteId,
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            await previous.ConfigureAwait(false);

            // Journalled here rather than when the write was queued, so the order the entries are
            // written in is the order the database sees. Journal first, clear after: a leftover
            // entry then means the write below never completed, which is precisely the case
            // recovery exists for.
            _journal?.Record(noteId, title, content);

            await _save(noteId, title, content, cancellationToken).ConfigureAwait(false);
            _journal?.Clear(noteId);
        }
        catch (OperationCanceledException)
        {
            // Shutting down. The journal entry stays, and recovery picks it up next launch.
        }
        catch (Exception ex)
        {
            // A failed autosave must never take the note window down with it; the user's text is
            // still on screen, the journal entry survives, and the next keystroke schedules
            // another attempt.
            SaveFailed?.Invoke(this, ex);
        }
        finally
        {
            lock (_writingGate)
            {
                // Only clear the slot if nothing newer has already claimed it, or a write queued
                // behind this one would stop being visible to the drain.
                if (_writing.TryGetValue(noteId, out var current) && ReferenceEquals(current, completion.Task))
                {
                    _writing.Remove(noteId);
                }
            }

            completion.SetResult();
        }
    }

    private sealed class PendingSave(string title, string content)
    {
        private readonly Lock _gate = new();
        private CancellationTokenSource? _timer;
        private string _title = title;
        private string _content = content;

        public void Update(string newTitle, string newContent)
        {
            lock (_gate)
            {
                _title = newTitle;
                _content = newContent;
            }
        }

        public (string Title, string Content) Snapshot()
        {
            lock (_gate)
            {
                return (_title, _content);
            }
        }

        public void Restart(TimeSpan delay, Func<Task> action)
        {
            CancellationTokenSource source;
            lock (_gate)
            {
                _timer?.Cancel();
                _timer?.Dispose();
                _timer = source = new CancellationTokenSource();
            }

            _ = DelayThenRunAsync(delay, action, source.Token);
        }

        public void Cancel()
        {
            lock (_gate)
            {
                _timer?.Cancel();
                _timer?.Dispose();
                _timer = null;
            }
        }

        private static async Task DelayThenRunAsync(TimeSpan delay, Func<Task> action, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer keystroke, or flushed explicitly.
                return;
            }

            await action().ConfigureAwait(false);
        }
    }
}
