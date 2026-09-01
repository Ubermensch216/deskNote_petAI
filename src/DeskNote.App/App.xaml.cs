using DeskNote.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DeskNote.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\DeskNote.Desktop.SingleInstance";
    private ApplicationRuntime? _runtime;
    private Mutex? _singleInstanceMutex;

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
            CrashLog.Write("Startup failed", ex);
            throw;
        }
    }
}
