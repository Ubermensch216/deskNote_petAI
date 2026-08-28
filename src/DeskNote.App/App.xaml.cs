using DeskNote.App.Services;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Services;
using DeskNote.Data;
using Microsoft.UI.Xaml;

namespace DeskNote.App;

public partial class App : Application
{
    private AutosaveScheduler? _autosave;
    private NoteWindowManager? _windows;
    private ReminderService? _reminders;
    private GlobalHotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    public App()
    {
        InitializeComponent();

        // A failure on a background task must not vanish. Without this, a throw inside startup
        // leaves a running process with no window and no explanation anywhere.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>The note window manager, once startup has built it.</summary>
    public NoteWindowManager Windows =>
        _windows ?? throw new InvalidOperationException("The application has not finished starting.");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = StartAsync();
    }

    /// <summary>
    /// Startup order matters (report p14): open the database, restore the notes that were on
    /// screen, and only then do anything else. Probing for a local model is deliberately not part
    /// of this path — "앱 시작 = 모델 시작" is the failure mode the report calls out, and the note
    /// layer must never wait on AI.
    /// </summary>
    private async Task StartAsync()
    {
        try
        {
            var connections = new SqliteConnectionFactory(SqliteConnectionFactory.DefaultDatabasePath);

            var migration = await new MigrationRunner(connections).MigrateAsync().ConfigureAwait(true);
            if (MigrationRunner.IsSqliteVersionRisky(migration.SqliteVersion))
            {
                CrashLog.Write(
                    $"SQLite {migration.SqliteVersion} predates the WAL-reset fix in " +
                    $"{MigrationRunner.MinimumSafeSqliteVersion}.");
            }

            ISettingsStore settings = new SqliteSettingsStore(connections);

            // Language is chosen before the first window exists, so no text is ever built with the
            // wrong locale and then left stale on screen.
            Strings.UseLocale(await settings.GetAsync(SettingKeys.UiLocale).ConfigureAwait(true));

            IClock clock = SystemClock.Instance;
            INoteRepository notes = new SqliteNoteRepository(connections, clock);
            INoteRevisionStore revisions = new SqliteNoteRevisionStore(connections);
            INoteLibrary library = new SqliteNoteLibrary(connections, clock);
            IReminderRepository reminders = new SqliteReminderRepository(connections);
            var attachmentStore = new AttachmentStore(new SqliteAttachmentRepository(connections), clock);

            var journal = new CrashJournal(AppPaths.JournalDirectory);
            var pipeline = new NoteSavePipeline(notes, revisions, new RevisionPolicy(), clock);

            _autosave = new AutosaveScheduler(
                (id, title, content, token) => pipeline.SaveAsync(id, title, content, cancellationToken: token),
                debounce: null,
                journal: journal);
            _autosave.SaveFailed += (_, ex) => CrashLog.Write("Autosave failed", ex);

            _windows = new NoteWindowManager(
                notes, clock, _autosave, pipeline, library, reminders, attachmentStore);

            // Recovery runs before the notes are shown, so a restored window opens already holding
            // the text that was rescued rather than flashing the stale version first.
            await RecoverUnsavedWorkAsync(journal, notes, pipeline).ConfigureAwait(true);

            await _windows.RestoreAsync().ConfigureAwait(true);

            // A first run has nothing to restore; open one note so the desktop is never empty.
            if (_windows.OpenWindowCount == 0)
            {
                await _windows.CreateAsync().ConfigureAwait(true);
            }

            // Started only after the notes are on screen: a reminder is worth 30 seconds of delay,
            // the notes are not (report p14).
            _reminders = new ReminderService(
                reminders,
                notes,
                clock,
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            _reminders.NoteRequested += async (_, noteId) => await _windows.FocusAsync(noteId);
            _reminders.Start();

            _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var bindings = HotkeyBindings.FromJson(
                await settings.GetAsync(SettingKeys.Hotkeys).ConfigureAwait(true));

            _hotkeys = new GlobalHotkeyService(RunHotkeyCommand);
            _hotkeys.Start(bindings);

            foreach (var failure in _hotkeys.Failures)
            {
                CrashLog.Write(
                    $"Hotkey {failure.Gesture} for {failure.Command} is unavailable — " +
                    "another application already owns it.");
            }

            _tray = new TrayIconService(
                onNewNote: () => RunHotkeyCommand(HotkeyCommands.NewNote),
                onOpenLibrary: () => RunHotkeyCommand(HotkeyCommands.SearchNotes),
                onExit: () => _dispatcher?.TryEnqueue(Exit));
            _tray.Start();


        }
        catch (Exception ex)
        {
            CrashLog.Write("Startup failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Runs a command triggered from outside the UI thread.
    /// </summary>
    /// <remarks>
    /// Hotkeys arrive on their own message-loop thread and the tray on another, so everything hops
    /// onto the dispatcher before touching a window.
    /// </remarks>
    private void RunHotkeyCommand(string command)
    {
        _dispatcher?.TryEnqueue(async () =>
        {
            try
            {
                switch (command)
                {
                    case HotkeyCommands.NewNote:
                        await Windows.CreateAsync();
                        break;

                    case HotkeyCommands.SearchNotes:
                        Windows.ShowLibrary();
                        break;

                    case HotkeyCommands.ToggleAlwaysOnTop:
                        Windows.ToggleActiveNoteAlwaysOnTop();
                        break;

                    case HotkeyCommands.AddReminder:
                        await Windows.RemindActiveNoteAsync(TimeSpan.FromHours(1));
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CrashLog.Write($"Hotkey command {command} failed", ex);
            }
        });
    }

    /// <summary>
    /// Restores note text that was journalled but never made it into the database.
    /// </summary>
    /// <remarks>
    /// The recovered text is applied rather than offered as a prompt. A leftover entry only exists
    /// when a save failed or was interrupted, so it is strictly newer than what is stored, and the
    /// checkpoint taken here means the stored version is still reachable from the note's history —
    /// which is a better answer than a startup dialog asking a question the user cannot evaluate
    /// before seeing either version.
    /// </remarks>
    private static async Task RecoverUnsavedWorkAsync(
        CrashJournal journal,
        INoteRepository notes,
        NoteSavePipeline pipeline)
    {
        var entries = journal.ReadAll();
        if (entries.Count == 0)
        {
            return;
        }

        var stored = new Dictionary<Guid, Core.Models.Note>();
        foreach (var entry in entries)
        {
            if (await notes.GetAsync(entry.NoteId).ConfigureAwait(true) is { } note)
            {
                stored[entry.NoteId] = note;
            }
        }

        foreach (var recoverable in CrashRecovery.FindUnsavedWork(entries, stored))
        {
            await pipeline.SaveAsync(
                recoverable.NoteId,
                recoverable.RecoveredTitle,
                recoverable.RecoveredContent,
                RevisionReason.BulkReplace,
                actionName: "crash-recovery").ConfigureAwait(true);

            CrashLog.Write($"Recovered unsaved text for note {recoverable.NoteId}.");
        }

        journal.ClearAll();
    }
}
