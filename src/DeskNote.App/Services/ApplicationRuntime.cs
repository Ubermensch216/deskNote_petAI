using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using DeskNote.App.Views;
using DeskNote.Companion.Core;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

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
    private readonly CompanionActivityQueue _companionQueue;
    private readonly ICompanionSuggestionService _companionSuggestions;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _exitApplication;
    private readonly CancellationTokenSource _activityLifetime = new();

    private GlobalHotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private CompanionWindow? _companionWindow;
    private DesktopPetWindow? _desktopPetWindow;
    private CompanionSnapshot? _companionSnapshot;
    private CompanionSettings _companionSettings = new();
    private DispatcherQueueTimer? _suggestionTimer;
    private DateTimeOffset? _lastUserInteractionAt;
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
        CompanionActivityQueue companionQueue,
        ICompanionSuggestionService companionSuggestions,
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
        _companionQueue = companionQueue;
        _companionSuggestions = companionSuggestions;
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

        var companionSettings = await CompanionSettings.LoadAsync(_settings, cancellationToken)
            .ConfigureAwait(true);
        _companionSettings = companionSettings;
        _companionQueue.SnapshotChanged += OnCompanionSnapshotChanged;
        await _companionQueue.StartAsync(companionSettings.Enabled, cancellationToken).ConfigureAwait(true);
        _pipeline.Saved += OnNoteSaved;
        Windows.NoteOpened += OnNoteOpened;

        // Recovery runs before windows, so restored text never flashes an older stored version.
        await RecoverUnsavedWorkAsync(cancellationToken).ConfigureAwait(true);
        await Windows.RestoreAsync(cancellationToken).ConfigureAwait(true);

        if (Windows.OpenWindowCount == 0)
        {
            var liveNoteCount = await _notes.CountAsync(NoteQuery.Default, cancellationToken)
                .ConfigureAwait(true);
            var deletedNoteCount = await _notes.CountAsync(
                    NoteQuery.Default with { OnlyDeleted = true },
                    cancellationToken)
                .ConfigureAwait(true);
            if (StartupNotePolicy.ShouldCreateFirstNote(
                    Windows.OpenWindowCount,
                    liveNoteCount + deletedNoteCount))
            {
                await Windows.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            }
        }

        if (companionSettings.Enabled && _companionSnapshot is { } snapshot)
        {
            ShowDesktopPet(snapshot, companionSettings);
        }
        ConfigureSuggestionTimer(companionSettings);

        // Background integrations begin after the initial desktop state is ready. When the user
        // has closed every existing note, DeskNote stays available from the tray without creating
        // another empty note on every launch.
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
            onSettings: () => _dispatcher.TryEnqueue(ShowSettings),
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
        _suggestionTimer?.Stop();
        _suggestionTimer = null;
        _hotkeys?.Dispose();
        _reminders.NoteRequested -= OnReminderNoteRequested;
        _reminders.Dispose();

        // Persist note text before stopping the indexer. A final embedding may be skipped and
        // recovered by backfill, but a final note save may never be skipped.
        await _autosave.FlushAllAsync(cancellationToken).ConfigureAwait(true);
        await _autosave.DisposeAsync().ConfigureAwait(true);

        _indexer.Dispose();
        _pipeline.Saved -= OnNoteSaved;
        Windows.NoteOpened -= OnNoteOpened;
        _activityLifetime.Cancel();
        _companionQueue.SnapshotChanged -= OnCompanionSnapshotChanged;
        _desktopPetWindow?.Close();
        _desktopPetWindow = null;
        await _companionQueue.DisposeAsync().ConfigureAwait(true);
        Ai.Dispose();
        _activityLifetime.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_settings);
        _settingsWindow = window;
        window.SettingsChanged += OnCompanionSettingsChanged;
        window.Closed += (_, _) => _settingsWindow = null;
        window.Activate();
    }

    private async void OnCompanionSettingsChanged(CompanionSettings settings)
    {
        try
        {
            _companionSettings = settings;
            if (settings.Enabled && !_companionQueue.IsStarted)
            {
                await _companionQueue.StartAsync(enabled: true).ConfigureAwait(true);
            }
            else
            {
                _companionQueue.SetEnabled(settings.Enabled);
            }

            if (!settings.Enabled)
            {
                _desktopPetWindow?.Close();
                _desktopPetWindow = null;
                _companionWindow?.Close();
                _companionWindow = null;
            }
            else if (_companionSnapshot is { } snapshot)
            {
                await _companionQueue.RefreshAsync().ConfigureAwait(true);
                snapshot = _companionSnapshot ?? snapshot;
                ShowDesktopPet(snapshot, settings);
                _companionWindow?.UpdateSettings(settings);
                _companionWindow?.UpdateSnapshot(snapshot);
            }
            ConfigureSuggestionTimer(settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Applying companion settings failed", ex);
        }
    }

    private void OnCompanionSnapshotChanged(CompanionSnapshot snapshot)
    {
        _companionSnapshot = snapshot;
        _dispatcher.TryEnqueue(() =>
        {
            _desktopPetWindow?.UpdateSnapshot(snapshot);
            _companionWindow?.UpdateSnapshot(snapshot);
        });
    }

    private void ShowDesktopPet(CompanionSnapshot snapshot, CompanionSettings settings)
    {
        if (_desktopPetWindow is not null)
        {
            _desktopPetWindow.UpdateSettings(settings);
            _desktopPetWindow.UpdateSnapshot(snapshot);
            _desktopPetWindow.Activate();
            return;
        }

        var window = new DesktopPetWindow(
            snapshot,
            settings,
            SaveDesktopPetPositionAsync,
            () =>
            {
                if (_companionSnapshot is { } current)
                {
                    ShowCompanion(current, _companionSettings);
                }
            });
        _desktopPetWindow = window;
        window.Closed += (_, _) => _desktopPetWindow = null;
        window.Activate();
    }

    private async Task SaveDesktopPetPositionAsync(PointInt32 position)
    {
        _companionSettings = _companionSettings with
        {
            PetPositionX = position.X,
            PetPositionY = position.Y,
        };
        await _settings.SetAsync(
            SettingKeys.CompanionPetPositionX,
            position.X.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(true);
        await _settings.SetAsync(
            SettingKeys.CompanionPetPositionY,
            position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(true);
    }

    private void ShowCompanion(CompanionSnapshot snapshot, CompanionSettings settings)
    {
        if (_companionWindow is not null)
        {
            _companionWindow.UpdateSettings(settings);
            _companionWindow.UpdateSnapshot(snapshot);
            _companionWindow.Activate();
            return;
        }

        var window = new CompanionWindow(
            snapshot,
            settings,
            ShowSettings,
            ritual => _companionQueue.ChooseRitualAsync(
                ritual,
                DateOnly.FromDateTime(DateTime.Now)),
            ActOnSuggestionAsync,
            DismissSuggestionAsync);
        _companionWindow = window;
        window.Closed += (_, _) => _companionWindow = null;
        window.Activate();
    }

    private void ConfigureSuggestionTimer(CompanionSettings settings)
    {
        if (!settings.Enabled || !settings.ProactiveEnabled)
        {
            _suggestionTimer?.Stop();
            _suggestionTimer = null;
            return;
        }

        if (_suggestionTimer is null)
        {
            _suggestionTimer = _dispatcher.CreateTimer();
            _suggestionTimer.Interval = TimeSpan.FromMinutes(5);
            _suggestionTimer.Tick += async (_, _) => await CheckForSuggestionAsync().ConfigureAwait(true);
            _suggestionTimer.Start();
        }

        _ = CheckForSuggestionAsync();
    }

    private async Task CheckForSuggestionAsync()
    {
        var now = DateTimeOffset.Now;
        if (_desktopPetWindow is null
            || !_companionSettings.AllowsProactiveAt(now)
            || !PresentationModeDetector.AllowsUserNotification()
            || (_lastUserInteractionAt is { } interaction
                && now - interaction < TimeSpan.FromMinutes(10)))
        {
            return;
        }

        try
        {
            if (await _companionSuggestions.TryOfferAsync(now).ConfigureAwait(true) is { } suggestion)
            {
                if (_companionSnapshot is { } snapshot)
                {
                    ShowCompanion(snapshot, _companionSettings);
                    _companionWindow?.ShowSuggestion(suggestion);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Finding a companion suggestion failed", ex);
        }
    }

    private async Task ActOnSuggestionAsync(CompanionSuggestion suggestion)
    {
        await _companionSuggestions.SetStatusAsync(
            suggestion.Id,
            CompanionSuggestionStatus.Acted,
            DateTimeOffset.UtcNow).ConfigureAwait(true);
        await Windows.FocusAsync(
            suggestion.NoteId,
            new NoteOpenContext
            {
                Origin = NoteOpenOrigin.Companion,
                SuggestionId = suggestion.Id,
            }).ConfigureAwait(true);
    }

    private async Task DismissSuggestionAsync(CompanionSuggestion suggestion) =>
        await _companionSuggestions.SetStatusAsync(
            suggestion.Id,
            CompanionSuggestionStatus.Dismissed,
            DateTimeOffset.UtcNow).ConfigureAwait(true);

    private void OnNoteSaved(NoteSaveResult result)
    {
        _lastUserInteractionAt = DateTimeOffset.Now;
        if (!result.ContentChanged)
        {
            return;
        }

        _companionQueue.TryEnqueue(new CompanionActivityCandidate
        {
            SourceEventId = $"capture:{result.NoteId:N}:{result.SavedAt.UtcTicks}",
            Type = CompanionActivityType.MeaningfulCapture,
            OccurredAt = result.SavedAt.ToLocalTime(),
            NoteId = result.NoteId,
            PreviousContentLength = result.PreviousContent?.Length ?? 0,
            CurrentContentLength = result.CurrentContent.Length,
        });

        foreach (var key in ChecklistCompletionDetector.FindCompletedKeys(
                     result.NoteId,
                     result.PreviousContent,
                     result.CurrentContent))
        {
            _companionQueue.TryEnqueue(new CompanionActivityCandidate
            {
                SourceEventId = $"checklist:{key}",
                Type = CompanionActivityType.ChecklistCompleted,
                OccurredAt = result.SavedAt.ToLocalTime(),
                NoteId = result.NoteId,
                SourceEntityId = key,
            });
        }

        if (result.Source == RevisionSource.Ai && !string.IsNullOrWhiteSpace(result.ActionName))
        {
            _companionQueue.TryEnqueue(new CompanionActivityCandidate
            {
                SourceEventId = $"ai:{result.NoteId:N}:{result.SavedAt.UtcTicks}",
                Type = CompanionActivityType.AiSuggestionAccepted,
                OccurredAt = result.SavedAt.ToLocalTime(),
                NoteId = result.NoteId,
                SourceEntityId = result.ActionName,
            });
        }
    }

    private async void OnNoteOpened(object? sender, NoteOpened opened)
    {
        _lastUserInteractionAt = DateTimeOffset.Now;
        if (opened.Context.Origin is not (
            NoteOpenOrigin.Search or
            NoteOpenOrigin.Related or
            NoteOpenOrigin.AiEvidence or
            NoteOpenOrigin.Briefing or
            NoteOpenOrigin.Companion))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), _activityLifetime.Token).ConfigureAwait(true);
            if (!Windows.IsOpen(opened.NoteId))
            {
                return;
            }

            var isBriefing = opened.Context.Origin == NoteOpenOrigin.Briefing;
            var local = opened.OpenedAt.ToLocalTime();
            _companionQueue.TryEnqueue(new CompanionActivityCandidate
            {
                SourceEventId = isBriefing
                    ? $"briefing:{local:yyyy-MM-dd}:{opened.NoteId:N}"
                    : $"recall:{opened.Context.Origin}:{opened.NoteId:N}:{opened.OpenedAt.UtcTicks}",
                Type = isBriefing
                    ? CompanionActivityType.BriefingEvidenceOpened
                    : CompanionActivityType.UsefulRecall,
                OccurredAt = local,
                NoteId = opened.NoteId,
                DwellTime = TimeSpan.FromSeconds(8),
            });
        }
        catch (OperationCanceledException)
        {
            // Application shutdown cancels pending dwell checks.
        }
        catch (Exception ex)
        {
            CrashLog.Write("Classifying a recalled note failed", ex);
        }
    }

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
