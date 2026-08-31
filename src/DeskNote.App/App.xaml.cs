using DeskNote.App.Services;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;
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
    private LocalAiHost? _ai;
    private EmbeddingIndexer? _indexer;
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

    /// <summary>
    /// How often availability is re-checked. Ollama can be started or stopped at any time, and a
    /// minute is short enough that the user does not wonder why the AI menu is still greyed out.
    /// </summary>
    private static readonly TimeSpan AiProbeInterval = TimeSpan.FromMinutes(1);

    /// <summary>Local AI, once startup has built it. Always present, often unavailable.</summary>
    public LocalAiHost? Ai => _ai;

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

            // Built before the windows so a note can hold a reference to it, but not probed until
            // everything else is running: creating the host only reads settings (report p14).
            var aiOptions = await DeskNote.Ai.OllamaOptions.LoadAsync(settings).ConfigureAwait(true);
            var embedder = new DeskNote.Ai.OllamaEmbeddingService(aiOptions);
            var vectors = new SqliteVectorIndex(connections, aiOptions.EmbeddingModel);

            _indexer = new EmbeddingIndexer(notes, vectors, embedder);
            _indexer.Failed += (_, ex) => CrashLog.Write("Embedding index update failed", ex);

            // One retriever, two callers: Q&A asks it for note bodies to answer from, and the chat
            // window asks it the same question first so it can show which notes the answer rests on.
            var retriever = new HybridRetriever(new Fts5SearchIndex(connections), vectors, embedder, notes);
            var hybridSearch = new HybridLibrarySearch(library, vectors, embedder);
            var neighbourhood = new NoteNeighbourhood(vectors, library);

            _ai = await LocalAiHost.CreateAsync(
                settings,
                retriever,
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()).ConfigureAwait(true);

            var journal = new CrashJournal(AppPaths.JournalDirectory);
            var pipeline = new NoteSavePipeline(notes, revisions, new RevisionPolicy(), clock);

            // Embedding rides along behind the save rather than inside it: the note is stored on
            // the same schedule as before, and its vectors catch up a moment later.
            _autosave = new AutosaveScheduler(
                async (id, title, content, token) =>
                {
                    await pipeline.SaveAsync(id, title, content, cancellationToken: token)
                        .ConfigureAwait(false);

                    _indexer?.Enqueue(id);
                },
                debounce: null,
                journal: journal);
            _autosave.SaveFailed += (_, ex) => CrashLog.Write("Autosave failed", ex);

            _windows = new NoteWindowManager(
                notes, clock, _autosave, pipeline, library, reminders, attachmentStore, _ai,
                hybridSearch, retriever, revisions, neighbourhood);

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

            // Ctrl+Space is only claimed when there is an AI layer to invoke with it; with AI off
            // the palette stays unbound and the IME keeps the combination (report p5).
            var bindings = HotkeyBindings.FromJson(
                await settings.GetAsync(SettingKeys.Hotkeys).ConfigureAwait(true),
                includeAiPalette: _ai.Capability.Availability != AiAvailability.Disabled);

            _hotkeys = new GlobalHotkeyService(RunHotkeyCommand);
            _hotkeys.Start(bindings);

            foreach (var failure in _hotkeys.Failures)
            {
                CrashLog.Write(
                    $"Hotkey {failure.Gesture} for {failure.Command} is unavailable — " +
                    "another application already owns it.");
            }

            // The stored preference is the source of truth; re-applying it repairs an entry another
            // tool removed and refreshes the path if the app has moved.
            var launchAtStartup =
                await settings.GetAsync(SettingKeys.LaunchAtStartup).ConfigureAwait(true) == "true";
            StartupRegistration.Apply(launchAtStartup);

            _tray = new TrayIconService(
                onNewNote: () => RunHotkeyCommand(HotkeyCommands.NewNote),
                onOpenLibrary: () => RunHotkeyCommand(HotkeyCommands.SearchNotes),
                onExit: () => _dispatcher?.TryEnqueue(Exit),
                onStartupChanged: enabled => _dispatcher?.TryEnqueue(async () =>
                {
                    if (StartupRegistration.SetEnabled(enabled))
                    {
                        await settings.SetAsync(
                            SettingKeys.LaunchAtStartup,
                            enabled ? "true" : "false");
                    }
                }));
            _tray.Start();

            // Last of all, and on its own schedule: local AI is the only part of the app allowed
            // to be missing. Nothing above this line waits for it (report p14).
            _windows.TrackAiCapability();
            _ai.StartProbing(AiProbeInterval);

            // Catches up notes written before semantic search existed, a bounded batch at a time.
            _indexer.Start();
            await _indexer.BackfillAsync().ConfigureAwait(true);
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

                    case HotkeyCommands.AiPalette:
                        Windows.ShowPalette();
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
