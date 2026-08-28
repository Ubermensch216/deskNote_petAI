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

            IClock clock = SystemClock.Instance;
            INoteRepository notes = new SqliteNoteRepository(connections, clock);

            _autosave = new AutosaveScheduler(
                (id, title, content, token) => notes.UpdateContentAsync(id, title, content, token));
            _autosave.SaveFailed += (_, ex) => CrashLog.Write("Autosave failed", ex);

            _windows = new NoteWindowManager(notes, clock, _autosave);
            await _windows.RestoreAsync().ConfigureAwait(true);

            // A first run has nothing to restore; open one note so the desktop is never empty.
            if (_windows.OpenWindowCount == 0)
            {
                await _windows.CreateAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Startup failed", ex);
            throw;
        }
    }
}
