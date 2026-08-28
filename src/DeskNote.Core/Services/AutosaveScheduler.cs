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
    private readonly TimeSpan _debounce;
    private readonly Func<Guid, string, string, CancellationToken, Task> _save;

    public AutosaveScheduler(
        Func<Guid, string, string, CancellationToken, Task> save,
        TimeSpan? debounce = null)
    {
        _save = save;
        _debounce = debounce ?? DefaultDebounce;
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
    public async Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var noteId in _pending.Keys.ToList())
        {
            await FlushAsync(noteId, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool HasPendingWork => !_pending.IsEmpty;

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

    private async Task SaveSafelyAsync(Guid noteId, string title, string content, CancellationToken cancellationToken)
    {
        try
        {
            await _save(noteId, title, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed autosave must never take the note window down with it; the user's text is
            // still on screen and the next keystroke will schedule another attempt.
            SaveFailed?.Invoke(this, ex);
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
