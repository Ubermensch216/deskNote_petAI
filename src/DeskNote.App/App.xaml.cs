using DeskNote.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DeskNote.App;

public partial class App : Application
{
    private ApplicationRuntime? _runtime;
    private Mutex? _singleInstanceMutex;

    /// <summary>
    /// One instance per data directory, not one per machine.
    /// </summary>
    /// <remarks>
    /// Two copies sharing a database would fight over the same notes, so the guard stays. It is
    /// keyed by the data directory because an instance pointed at a demo folder shares nothing
    /// with the real one, and refusing to start it would make <c>--data</c> useless — which is the
    /// whole point of being able to take screen photographs without touching real notes.
    /// </remarks>
    private static string SingleInstanceMutexName =>
        @"Local\DeskNote.Desktop.SingleInstance." +
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(AppPaths.Root.ToUpperInvariant())))[..16];

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

        // The last line before the process dies.
        //
        // Every `async void` handler in a WinUI app — which is every UI event handler — routes its
        // exceptions here, and the default is to terminate. For a sticky note app that default is
        // the worst possible one: the text on screen is the copy the user can still see, and
        // killing the window to report a failed database write throws away the very thing the
        // failure was about. Handling it keeps the notes on the desktop; the log says what broke.
        //
        // This is a net, not a substitute for handling failures where they happen. Callers that
        // can say something useful to the user still should, and the wrappers in
        // NoteWindowManager.Show exist so this is never the first place a note error is noticed.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("Unhandled exception on the UI thread", e.Exception);
            e.Handled = true;
        };
    }

    /// <summary>Local AI, once startup has built it. Always present, often unavailable.</summary>
    public LocalAiHost? Ai => _runtime?.Ai;

    /// <summary>The note window manager, once startup has built it.</summary>
    public NoteWindowManager Windows =>
        _runtime?.Windows ?? throw new InvalidOperationException("The application has not finished starting.");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            mutex.Dispose();
            Exit();
            return;
        }

        _singleInstanceMutex = mutex;
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("DeskNote must start on a dispatcher thread.");

            _runtime = await CompositionRoot
                .BuildAsync(dispatcher, Exit)
                .ConfigureAwait(true);

            await _runtime.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Rethrowing here went nowhere: OnLaunched discards this task, so the failure only
            // surfaced whenever the finalizer got round to it. A start that did not finish leaves
            // a process with no window and no tray icon, which the user cannot even quit — so it
            // is written down and then ended.
            CrashLog.Write("Startup failed", ex);
            Exit();
        }
    }
}
