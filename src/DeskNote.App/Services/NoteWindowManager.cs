using DeskNote.App.Views;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Services;
using DeskNote.Core.Models;

namespace DeskNote.App.Services;

/// <summary>
/// Owns the set of note windows on the desktop and keeps them in step with the database.
/// </summary>
/// <remarks>
/// All persistence for a note window funnels through here rather than through the window itself,
/// so there is one place that decides what a user gesture means: closing a note hides it, the
/// More menu deletes it, dragging it stores geometry without touching its revision.
/// </remarks>
public sealed class NoteWindowManager(
    INoteRepository notes,
    IClock clock,
    AutosaveScheduler autosave,
    NoteSavePipeline savePipeline,
    INoteLibrary library,
    IReminderRepository reminders,
    AttachmentStore attachmentStore,
    LocalAiHost? ai = null,
    HybridLibrarySearch? hybridSearch = null,
    DeskNote.Ai.IAiRetriever? retriever = null,
    INoteRevisionStore? revisions = null,
    NoteNeighbourhood? neighbourhood = null)
{
    private readonly Dictionary<Guid, NoteWindow> _windows = [];
    private int _cascadeIndex;
    private NotesExplorerWindow? _explorer;
    private AiChatWindow? _chat;
    private NoteHistoryWindow? _history;
    private RelatedNotesWindow? _related;

    public int OpenWindowCount => _windows.Count;

    /// <summary>
    /// Keeps every open note's AI section in step with what the local model can currently do.
    /// </summary>
    /// <remarks>
    /// The host probes on its own schedule, so availability changes while notes are on screen.
    /// Pushing it from here means a window never has to poll, and a note opened later picks up the
    /// current answer from the host directly.
    /// </remarks>
    public void TrackAiCapability()
    {
        if (ai is null)
        {
            return;
        }

        ai.CapabilityChanged += (_, capability) =>
        {
            foreach (var window in _windows.Values)
            {
                window.SetAiCapability(capability);
            }
        };
    }

    /// <summary>
    /// Opens the note library, or brings it forward if it is already open.
    /// </summary>
    /// <remarks>
    /// One instance only. A second copy of the library would show a stale list the moment the
    /// first one was used, and neither would obviously be the real one.
    /// </remarks>
    public void ShowLibrary()
    {
        if (_explorer is not null)
        {
            _explorer.Activate();
            return;
        }

        _explorer = new NotesExplorerWindow(library, notes, noteId => FocusAsync(noteId), hybridSearch);
        _explorer.Closed += (_, _) => _explorer = null;
        _explorer.Activate();
    }

    /// <summary>
    /// Adds a reminder to a note.
    /// </summary>
    /// <remarks>
    /// A zero offset means "tomorrow morning", the one relative time that is a clock time rather
    /// than a duration.
    /// </remarks>
    public async Task AddReminderAsync(Guid noteId, TimeSpan offset, CancellationToken cancellationToken = default)
    {
        var dueAt = offset == TimeSpan.Zero
            ? new DateTimeOffset(clock.Now.Date.AddDays(1).AddHours(9), clock.Now.Offset)
            : clock.Now.Add(offset);

        await reminders.AddAsync(
            new Reminder
            {
                Id = Guid.CreateVersion7(),
                NoteId = noteId,
                DueAt = dueAt.ToUniversalTime(),
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a note's stored versions, or replaces the window if another note's are showing.
    /// </summary>
    /// <remarks>
    /// One window for every note rather than one per note: two histories side by side invite a
    /// restore into the wrong note, and the restore is the one action here that writes.
    /// </remarks>
    public void ShowHistory(Guid noteId)
    {
        if (revisions is null)
        {
            return;
        }

        _history?.Close();

        _history = new NoteHistoryWindow(
            noteId,
            revisions,
            notes,
            savePipeline,
            (id, content) =>
            {
                // The desktop window is still holding the text that was just replaced; it has to
                // show what the database now says the note is.
                if (_windows.TryGetValue(id, out var open))
                {
                    open.ReplaceContent(content);
                }

                return Task.CompletedTask;
            });

        _history.Closed += (_, _) => _history = null;
        _history.Activate();

        if (_windows.TryGetValue(noteId, out var source))
        {
            _history.PlaceNear(source.AppWindow);
        }
    }

    /// <summary>
    /// Opens 메모 Q&amp;A, scoped to one note or to the whole library.
    /// </summary>
    /// <remarks>
    /// One sidecar at a time, re-scoped rather than duplicated: two chat windows answering about
    /// different notes would leave the user guessing which one their next question went to.
    /// </remarks>
    /// <summary>
    /// Opens the notes that read like this one.
    /// </summary>
    /// <remarks>
    /// One window at a time, like the history: this is a view onto one note's surroundings, and
    /// two of them open at once stop saying whose surroundings they are.
    /// </remarks>
    public void ShowRelated(Guid noteId)
    {
        if (neighbourhood is null)
        {
            return;
        }

        _related?.Close();
        _related = new RelatedNotesWindow(noteId, neighbourhood, id => FocusAsync(id));
        _related.Closed += (_, _) => _related = null;
        _related.Activate();

        if (_windows.TryGetValue(noteId, out var source))
        {
            _related.PlaceNear(source.AppWindow);
        }
    }

    public void ShowChat(Guid? noteId)
    {
        if (ai is null)
        {
            return;
        }

        var source = noteId is { } id && _windows.TryGetValue(id, out var window) ? window : null;

        _chat?.Close();
        _chat = new AiChatWindow(
            ai,
            noteId,
            source?.CurrentTitle,
            retriever,
            id => FocusAsync(id));
        _chat.Closed += (_, _) => _chat = null;
        _chat.Activate();

        if (source is not null)
        {
            _chat.PlaceNear(source.AppWindow);
        }
    }

    /// <summary>Opens 메모 Q&amp;A for the note the user was last in, for the global palette hotkey.</summary>
    public void ShowChatForActiveNote() => ShowChat(ActiveNote?.NoteId);

    /// <summary>
    /// Adds a reminder for an absolute moment, as an extracted task's deadline gives it.
    /// </summary>
    /// <remarks>
    /// A deadline already in the past is still stored rather than dropped or shifted: the note
    /// said so, and silently moving someone's date is worse than a reminder that fires at once.
    /// </remarks>
    public async Task AddReminderAtAsync(
        Guid noteId,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken = default) =>
        await reminders.AddAsync(
            new Reminder
            {
                Id = Guid.CreateVersion7(),
                NoteId = noteId,
                DueAt = dueAt.ToUniversalTime(),
            },
            cancellationToken).ConfigureAwait(true);

    /// <summary>
    /// The note the user is working in, for hotkeys that act on "this note".
    /// </summary>
    /// <remarks>
    /// Tracked from activation rather than read from the OS foreground window: a global hotkey can
    /// fire while another application is in front, and Ctrl+Shift+P should still pin the note the
    /// user was last in rather than doing nothing.
    /// </remarks>
    public NoteWindow? ActiveNote { get; private set; }

    /// <summary>Pins or unpins the note the user was last working in.</summary>
    public void ToggleActiveNoteAlwaysOnTop() => ActiveNote?.ToggleAlwaysOnTop();

    /// <summary>Adds a default reminder to the note the user was last working in.</summary>
    public async Task RemindActiveNoteAsync(TimeSpan offset, CancellationToken cancellationToken = default)
    {
        if (ActiveNote is { } note)
        {
            await AddReminderAsync(note.NoteId, offset, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Re-opens the notes that were on screen when the app last ran.
    /// </summary>
    /// <remarks>
    /// On the critical path of the &lt;500 ms restore budget (report p14), so it reads one indexed
    /// query and does no AI or network work.
    /// </remarks>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var open = await notes.GetOpenNotesAsync(cancellationToken).ConfigureAwait(true);

        foreach (var note in open)
        {
            Show(note);
        }
    }

    /// <summary>Creates a note, stores it, and shows it. This is the path the global hotkey takes.</summary>
    public async Task<Note> CreateAsync(
        NoteSizePreset preset = NoteSizePreset.Medium,
        string colorKey = NoteColors.Default,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var note = new Note
        {
            Id = Guid.CreateVersion7(),
            Geometry = MonitorLayout.PlaceNewNote(preset, _cascadeIndex++),
            ColorKey = colorKey,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await notes.AddAsync(note, cancellationToken).ConfigureAwait(true);

        var window = Show(note);
        window.FocusEditor();
        return note;
    }

    /// <summary>Brings an already-open note to the front, or re-opens it if its window was closed.</summary>
    public async Task FocusAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        if (_windows.TryGetValue(noteId, out var existing))
        {
            existing.FocusEditor();
            return;
        }

        var note = await notes.GetAsync(noteId, cancellationToken).ConfigureAwait(true);
        if (note is null || note.IsDeleted)
        {
            return;
        }

        await notes.SetOpenAsync(noteId, isOpen: true, cancellationToken).ConfigureAwait(true);
        Show(note).FocusEditor();
    }

    public async Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        await autosave.FlushAllAsync(cancellationToken).ConfigureAwait(true);

        foreach (var window in _windows.Values.ToList())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private NoteWindow Show(Note note)
    {
        if (_windows.TryGetValue(note.Id, out var existing))
        {
            return existing;
        }

        var window = new NoteWindow(note, ai, neighbourhood);
        _windows[note.Id] = window;

        window.TextChanged += (_, text) => autosave.Schedule(note.Id, text.Title, text.Content);
        window.GeometryChanged += async (_, geometry) => await OnGeometryChangedAsync(note.Id, geometry);
        window.AppearanceChanged += async (_, appearance) => await OnAppearanceChangedAsync(note.Id, appearance);
        window.CloseRequested += async (_, _) => await OnCloseRequestedAsync(note.Id);
        window.DeleteRequested += async (_, _) => await OnDeleteRequestedAsync(note.Id);
        window.LibraryRequested += (_, _) => ShowLibrary();
        window.HistoryRequested += (w, _) => ShowHistory(((NoteWindow)w!).NoteId);
        window.AiApplied += async (w, edit) => await OnAiAppliedAsync(((NoteWindow)w!).NoteId, edit);
        window.RelatedRequested += (w, _) => ShowRelated(((NoteWindow)w!).NoteId);
        window.NewNoteRequested += async (_, seed) => await CreateAsync(seed.Preset, seed.ColorKey);
        window.ReminderRequested += async (_, offset) => await AddReminderAsync(note.Id, offset);
        window.ReminderAtRequested += async (_, dueAt) => await AddReminderAtAsync(note.Id, dueAt);
        window.AskRequested += (w, _) => ShowChat(((NoteWindow)w!).NoteId);
        window.AttachmentRequested += async (w, request) => await OnAttachmentRequestedAsync(w, request);
        window.Activated += (w, args) =>
        {
            if (args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated)
            {
                ActiveNote = w as NoteWindow;
            }
        };

        window.Activate();
        return window;
    }

    /// <summary>
    /// Stores dropped files or a pasted image and writes a reference into the note.
    /// </summary>
    /// <remarks>
    /// The Markdown goes in through the editor rather than straight to the database, so the
    /// insertion lands on the undo stack: attaching the wrong screenshot should be one Ctrl+Z away.
    /// </remarks>
    private async Task OnAttachmentRequestedAsync(object? sender, AttachmentRequest request)
    {
        if (sender is not NoteWindow window)
        {
            return;
        }

        try
        {
            var inserts = new List<string>();

            if (request.ImageBytes is { Length: > 0 } bytes)
            {
                var stored = await attachmentStore
                    .EmbedAsync(request.NoteId, request.ImageName ?? "image.png", bytes)
                    .ConfigureAwait(true);
                inserts.Add(stored.Markdown);
            }

            foreach (var path in request.FilePaths)
            {
                var stored = await attachmentStore.AddFileAsync(request.NoteId, path).ConfigureAwait(true);
                inserts.Add(stored.Markdown);
            }

            if (inserts.Count > 0)
            {
                window.InsertAtCaret(string.Join('\n', inserts) + '\n');
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write($"Attaching to note {request.NoteId} failed", ex);
        }
    }

    /// <summary>
    /// Stores an accepted AI proposal as what it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the debounced path. Autosave checkpoints ordinary typing on an interval, so a second
    /// summary within five minutes of the first would have replaced the note with nothing kept —
    /// the opposite of report p15's requirement that an AI rewrite preserve the original.
    /// <see cref="RevisionReason.BulkReplace"/> always snapshots, and
    /// <see cref="RevisionSource.Ai"/> is what lets the history say which action did it.
    /// </para>
    /// <para>
    /// The pending autosave is flushed first so the checkpoint captures what the user actually
    /// had. Without it, text typed in the seconds before the proposal was accepted would still be
    /// sitting in the debounce, and the version stored as "before the AI" would be missing the
    /// last sentence they wrote.
    /// </para>
    /// </remarks>
    private async Task OnAiAppliedAsync(Guid noteId, AiEdit edit)
    {
        try
        {
            await autosave.FlushAsync(noteId).ConfigureAwait(true);

            await savePipeline.SaveAsync(
                noteId,
                edit.Title,
                edit.Content,
                RevisionReason.BulkReplace,
                RevisionSource.Ai,
                edit.ActionName).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Storing an applied AI edit failed", ex);
        }
    }

    private async Task OnGeometryChangedAsync(Guid noteId, NoteGeometry geometry)
    {
        try
        {
            await notes.UpdateGeometryAsync(noteId, geometry).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Losing a window position is not worth crashing the desktop over.
            System.Diagnostics.Debug.WriteLine($"Geometry save failed for {noteId}: {ex}");
        }
    }

    private async Task OnAppearanceChangedAsync(Guid noteId, NoteAppearance appearance)
    {
        try
        {
            await notes.UpdateAppearanceAsync(
                noteId,
                appearance.ColorKey,
                appearance.Opacity,
                appearance.AlwaysOnTop).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Appearance save failed for {noteId}: {ex}");
        }
    }

    /// <summary>
    /// Closing a note window hides the note; it stays in Notes Explorer. Text is flushed first so
    /// the last keystrokes before the close are never lost to the debounce.
    /// </summary>
    private async Task OnCloseRequestedAsync(Guid noteId)
    {
        if (ActiveNote?.NoteId == noteId)
        {
            ActiveNote = null;
        }

        _windows.Remove(noteId);

        await autosave.FlushAsync(noteId).ConfigureAwait(true);
        await notes.SetOpenAsync(noteId, isOpen: false).ConfigureAwait(true);

        // The next edit after reopening deserves a checkpoint rather than inheriting a timer from
        // a session that may have ended hours ago.
        savePipeline.Forget(noteId);
    }

    private async Task OnDeleteRequestedAsync(Guid noteId)
    {
        if (_windows.Remove(noteId, out var window))
        {
            window.Close();
        }

        await autosave.FlushAsync(noteId).ConfigureAwait(true);
        await notes.SoftDeleteAsync(noteId).ConfigureAwait(true);
    }
}
