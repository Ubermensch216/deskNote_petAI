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
public sealed partial class NoteWindowManager(
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
    NoteNeighbourhood? neighbourhood = null,
    DailyBriefing? briefing = null)
{
    private readonly Dictionary<Guid, NoteWindow> _windows = [];
    private int _cascadeIndex;
    private NotesExplorerWindow? _explorer;
    private AiChatWindow? _chat;
    private NoteHistoryWindow? _history;
    private RelatedNotesWindow? _related;
    private ReminderWindow? _reminderWindow;
    private CommandPaletteWindow? _palette;
    private bool _composingBriefing;

    public int OpenWindowCount => _windows.Count;

    public bool IsOpen(Guid noteId) => _windows.ContainsKey(noteId);

    /// <summary>Raised after a note has successfully been brought into view.</summary>
    public event EventHandler<NoteOpened>? NoteOpened;

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
    /// Opens the composer for a reminder described in the user's own words.
    /// </summary>
    /// <remarks>
    /// The window creates nothing itself; it hands back a resolved moment and this method stores
    /// it, so the one place that decides what a user gesture means stays the one place.
    /// </remarks>
    public void ShowReminderComposer(Guid noteId)
    {
        if (ai is null)
        {
            return;
        }

        _reminderWindow?.Close();
        _reminderWindow = new ReminderWindow(
            ai,
            clock,
            (dueAt, rule) => AddReminderAtAsync(noteId, dueAt, rule));

        _reminderWindow.Closed += (_, _) => _reminderWindow = null;
        _reminderWindow.Activate();

        if (_windows.TryGetValue(noteId, out var source))
        {
            _reminderWindow.PlaceNear(source.AppWindow);
        }
    }


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
        string? recurrenceRule = null,
        CancellationToken cancellationToken = default) =>
        await reminders.AddAsync(
            new Reminder
            {
                Id = Guid.CreateVersion7(),
                NoteId = noteId,
                DueAt = dueAt.ToUniversalTime(),
                RecurrenceRule = recurrenceRule,
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
    public Task FocusAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        FocusAsync(noteId, NoteOpenContext.Direct, cancellationToken);

    /// <summary>Brings a note into view and preserves the route that led the user to it.</summary>
    public async Task FocusAsync(
        Guid noteId,
        NoteOpenContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_windows.TryGetValue(noteId, out var existing))
        {
            existing.FocusEditor();
            NoteOpened?.Invoke(this, new NoteOpened(noteId, context, clock.UtcNow));
            return;
        }

        var note = await notes.GetAsync(noteId, cancellationToken).ConfigureAwait(true);
        if (note is null || note.IsDeleted)
        {
            return;
        }

        await notes.SetOpenAsync(noteId, isOpen: true, cancellationToken).ConfigureAwait(true);
        Show(note).FocusEditor();
        NoteOpened?.Invoke(this, new NoteOpened(noteId, context, clock.UtcNow));
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
        window.CustomReminderRequested += (w, _) => ShowReminderComposer(((NoteWindow)w!).NoteId);
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

    private Task OnDeleteRequestedAsync(Guid noteId) => DeleteNotesAsync([noteId]);

    private async Task DeleteNotesAsync(IReadOnlyList<Guid> noteIds)
    {
        foreach (var noteId in noteIds.Distinct())
        {
            if (ActiveNote?.NoteId == noteId)
            {
                ActiveNote = null;
            }

            if (_windows.Remove(noteId, out var window))
            {
                window.Close();
            }

            await autosave.FlushAsync(noteId).ConfigureAwait(true);
            await notes.SoftDeleteAsync(noteId).ConfigureAwait(true);
            savePipeline.Forget(noteId);
        }
    }
}
