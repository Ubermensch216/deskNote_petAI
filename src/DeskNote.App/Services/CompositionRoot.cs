using DeskNote.Core.Abstractions;
using DeskNote.Core.Services;
using DeskNote.Data;
using DeskNote.Companion.Core;
using Microsoft.UI.Dispatching;

namespace DeskNote.App.Services;

/// <summary>
/// Builds the application object graph without starting background work or probing a model.
/// </summary>
/// <remarks>
/// Keeping construction here makes the startup order visible and keeps <c>App</c> responsible
/// only for WinUI lifetime. Database migration and settings reads happen here; note restoration,
/// timers, tray, hotkeys, indexing and AI probes are started later by <see cref="ApplicationRuntime"/>.
/// </remarks>
public static class CompositionRoot
{
    public static async Task<ApplicationRuntime> BuildAsync(
        DispatcherQueue dispatcher,
        Action exitApplication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(exitApplication);

        // AppPaths, not SqliteConnectionFactory.DefaultDatabasePath: the data directory can be
        // overridden, and the database has to move with the attachments, journal and log.
        var connections = new SqliteConnectionFactory(AppPaths.DatabasePath);

        var migration = await new MigrationRunner(connections)
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(true);

        if (MigrationRunner.IsSqliteVersionRisky(migration.SqliteVersion))
        {
            CrashLog.Write(
                $"SQLite {migration.SqliteVersion} predates the WAL-reset fix in " +
                $"{MigrationRunner.MinimumSafeSqliteVersion}.");
        }

        ISettingsStore settings = new SqliteSettingsStore(connections);

        // Language is chosen before the first window exists, so no text is ever built with the
        // wrong locale and then left stale on screen.
        Strings.UseLocale(await settings.GetAsync(SettingKeys.UiLocale, cancellationToken).ConfigureAwait(true));

        IClock clock = SystemClock.Instance;
        INoteRepository notes = new SqliteNoteRepository(connections, clock);
        INoteRevisionStore revisions = new SqliteNoteRevisionStore(connections);
        INoteLibrary library = new SqliteNoteLibrary(connections, clock);
        IReminderRepository reminderRepository = new SqliteReminderRepository(connections);
        var attachmentStore = new AttachmentStore(new SqliteAttachmentRepository(connections), clock);

        // Creating these objects reads configuration but does not probe or load model weights.
        var aiOptions = await DeskNote.Ai.OllamaOptions
            .LoadAsync(settings, cancellationToken)
            .ConfigureAwait(true);
        var embedder = new DeskNote.Ai.OllamaEmbeddingService(aiOptions);
        var vectors = new SqliteVectorIndex(connections, aiOptions.EmbeddingModel);

        var indexer = new EmbeddingIndexer(notes, vectors, embedder);
        indexer.Failed += (_, ex) => CrashLog.Write("Embedding index update failed", ex);

        var retriever = new HybridRetriever(new Fts5SearchIndex(connections), vectors, embedder, notes);
        var hybridSearch = new HybridLibrarySearch(library, vectors, embedder);
        var neighbourhood = new NoteNeighbourhood(vectors, library);

        var ai = await LocalAiHost
            .CreateAsync(settings, retriever, dispatcher, cancellationToken)
            .ConfigureAwait(true);

        var journal = new CrashJournal(AppPaths.JournalDirectory);
        var pipeline = new NoteSavePipeline(notes, revisions, new RevisionPolicy(), clock);
        var companionRepository = new SqliteCompanionRepository(
            connections,
            new RewardPolicy(),
            new CarePolicy());
        var companionQueue = new CompanionActivityQueue(companionRepository, new ActivityClassifier());
        ICompanionSuggestionService companionSuggestions = new SqliteCompanionSuggestionService(connections);
        companionQueue.Failed += ex => CrashLog.Write("Companion activity persistence failed", ex);

        // Embedding follows a successful save and never extends the note transaction.
        var autosave = new AutosaveScheduler(
            async (id, title, content, token) =>
            {
                await pipeline.SaveAsync(id, title, content, cancellationToken: token)
                    .ConfigureAwait(false);
                indexer.Enqueue(id);
            },
            debounce: null,
            journal: journal);
        autosave.SaveFailed += (_, ex) => CrashLog.Write("Autosave failed", ex);

        var windows = new NoteWindowManager(
            notes,
            clock,
            autosave,
            pipeline,
            library,
            reminderRepository,
            attachmentStore,
            ai,
            hybridSearch,
            retriever,
            revisions,
            neighbourhood,
            new DailyBriefing(library, notes, clock, ai));

        var reminders = new ReminderService(reminderRepository, notes, clock, dispatcher);

        return new ApplicationRuntime(
            settings,
            notes,
            journal,
            pipeline,
            autosave,
            windows,
            reminders,
            ai,
            indexer,
            companionQueue,
            companionSuggestions,
            dispatcher,
            exitApplication);
    }
}
