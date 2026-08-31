using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.UI.Dispatching;

namespace DeskNote.App.Services;

/// <summary>Owns the running application's services and their ordered startup and shutdown.</summary>
public sealed class ApplicationRuntime : IAsyncDisposable
{
    private static readonly TimeSpan AiProbeInterval = TimeSpan.FromMinutes(1);

    private readonly ISettingsStore _settings;
    private readonly INoteRepository _notes;
    private readonly CrashJournal _journal;
    private readonly NoteSavePipeline _pipeline;
    private readonly AutosaveScheduler _autosave;
    private readonly ReminderService _reminders;
    private readonly EmbeddingIndexer _indexer;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _exitApplication;

    private GlobalHotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private bool _started;
    private int _stopping;

    internal ApplicationRuntime(
        ISettingsStore settings,
        INoteRepository notes,
        CrashJournal journal,
        NoteSavePipeline pipeline,
        AutosaveScheduler autosave,
        NoteWindowManager windows,
        ReminderService reminders,
        LocalAiHost ai,
        EmbeddingIndexer indexer,
        DispatcherQueue dispatcher,
        Action exitApplication)
    {
        _settings = settings;
        _notes = notes;
        _journal = journal;
        _pipeline = pipeline;
        _autosave = autosave;
        Windows = windows;
        _reminders = reminders;
        Ai = ai;
        _indexer = indexer;
        _dispatcher = dispatcher;
        _exitApplication = exitApplication;
    }

    public LocalAiHost Ai { get; }

    public NoteWindowManager Windows { get; }

    /// <summary>
    /// Restores notes first, then starts optional and background integrations in dependency order.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        // Recovery runs before windows, so restored text never flashes an older stored version.
        await RecoverUnsavedWorkAsync(cancellationToken).ConfigureAwait(true);
        await Windows.RestoreAsync(cancellationToken).ConfigureAwait(true);

        if (Windows.OpenWindowCount == 0)
        {
            await Windows.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        }

        // Background integrations begin only after a note is visible.
        _reminders.NoteRequested += OnReminderNoteRequested;
        _reminders.Start();

        var bindings = HotkeyBindings.FromJson(
            await _settings.GetAsync(SettingKeys.Hotkeys, cancellationToken).ConfigureAwait(true),
            includeAiPalette: Ai.Capability.Availability != AiAvailability.Disabled);

        _hotkeys = new GlobalHotkeyService(RunHotkeyCommand);
        _hotkeys.Start(bindings);

        foreach (var failure in _hotkeys.Failures)
        {
            CrashLog.Write(
                $"Hotkey {failure.Gesture} for {failure.Command} is unavailable — " +
                "another application already owns it.");
        }

        var launchAtStartup =
            await _settings.GetAsync(SettingKeys.LaunchAtStartup, cancellationToken).ConfigureAwait(true) == "true";
        StartupRegistration.Apply(launchAtStartup);

        _tray = new TrayIconService(
            onNewNote: () => RunHotkeyCommand(HotkeyCommands.NewNote),
            onOpenLibrary: () => RunHotkeyCommand(HotkeyCommands.SearchNotes),
            onExit: RequestExit,
            onBriefing: () => _dispatcher.TryEnqueue(async () => await Windows.ShowBriefingAsync()),
            onStartupChanged: enabled => _dispatcher.TryEnqueue(async () =>
            {
                if (StartupRegistration.SetEnabled(enabled))
                {
                    await _settings.SetAsync(
                        SettingKeys.LaunchAtStartup,
                        enabled ? "true" : "false");
                }
            }));
        _tray.Start();

        // AI and semantic indexing remain last. Neither is allowed to delay the first note.
        Windows.TrackAiCapability();
        Ai.StartProbing(AiProbeInterval);
        _indexer.Start();
        await _indexer.BackfillAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        _tray?.Dispose();
        _hotkeys?.Dispose();
        _reminders.NoteRequested -= OnReminderNoteRequested;
        _reminders.Dispose();

        // Persist note text before stopping the indexer. A final embedding may be skipped and
        // recovered by backfill, but a final note save may never be skipped.
        await _autosave.FlushAllAsync(cancellationToken).ConfigureAwait(true);
        await _autosave.DisposeAsync().ConfigureAwait(true);

        _indexer.Dispose();
        Ai.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async void OnReminderNoteRequested(object? sender, Guid noteId)
    {
        try
        {
            await Windows.FocusAsync(
                noteId,
                new NoteOpenContext { Origin = NoteOpenOrigin.Reminder }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write($"Opening reminder note {noteId} failed", ex);
        }
    }

    private void RequestExit()
    {
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await StopAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CrashLog.Write("Shutdown failed", ex);
            }
            finally
            {
                _exitApplication();
            }
        });
    }

    /// <summary>Runs a command that originated on a hotkey or tray message-loop thread.</summary>
    private void RunHotkeyCommand(string command)
    {
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                switch (command)
                {
                    case HotkeyCommands.NewNote:
                        await Windows.CreateAsync().ConfigureAwait(true);
                        break;

                    case HotkeyCommands.SearchNotes:
                        Windows.ShowLibrary();
                        break;

                    case HotkeyCommands.ToggleAlwaysOnTop:
                        Windows.ToggleActiveNoteAlwaysOnTop();
                        break;

                    case HotkeyCommands.AddReminder:
                        await Windows.RemindActiveNoteAsync(TimeSpan.FromHours(1)).ConfigureAwait(true);
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

    private async Task RecoverUnsavedWorkAsync(CancellationToken cancellationToken)
    {
        var entries = _journal.ReadAll();
        if (entries.Count == 0)
        {
            return;
        }

        var stored = new Dictionary<Guid, Note>();
        foreach (var entry in entries)
        {
            if (await _notes.GetAsync(entry.NoteId, cancellationToken).ConfigureAwait(true) is { } note)
            {
                stored[entry.NoteId] = note;
            }
        }

        foreach (var recoverable in CrashRecovery.FindUnsavedWork(entries, stored))
        {
            await _pipeline.SaveAsync(
                recoverable.NoteId,
                recoverable.RecoveredTitle,
                recoverable.RecoveredContent,
                RevisionReason.BulkReplace,
                actionName: "crash-recovery",
                cancellationToken: cancellationToken).ConfigureAwait(true);

            CrashLog.Write($"Recovered unsaved text for note {recoverable.NoteId}.");
        }

        _journal.ClearAll();
    }
}
