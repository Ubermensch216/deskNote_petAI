using DeskNote.Ai;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;

namespace DeskNote.App.Services;

/// <summary>
/// Owns the app's single AI service and the answer to "can AI run right now".
/// </summary>
/// <remarks>
/// <para>
/// The host exists so the rest of the app can hold one reference that never changes while the
/// thing behind it may be unavailable, become available, and go away again. Windows subscribe to
/// <see cref="CapabilityChanged"/> and enable their AI affordances from it; nothing else probes.
/// </para>
/// <para>
/// Probing is started explicitly, after the notes are on screen — never during startup
/// (report p14: 앱 시작 = 모델 시작 금지).
/// </para>
/// </remarks>
public sealed class LocalAiHost : IDisposable
{
    private readonly ILocalAiService _service;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _stopping = new();

    private LocalAiHost(
        ILocalAiService service,
        AiCapability capability,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _service = service;
        Capability = capability;
        _dispatcher = dispatcher;
    }

    /// <summary>Raised on the UI thread whenever availability changes.</summary>
    public event EventHandler<AiCapability>? CapabilityChanged;

    /// <summary>The AI surface itself. Always present; may report unavailable forever.</summary>
    public ILocalAiService Service => _service;

    public AiCapability Capability { get; private set; }

    public bool IsAvailable => Capability.IsAvailable;

    /// <summary>
    /// Builds the host from stored settings.
    /// </summary>
    /// <remarks>
    /// A settings read that fails must not stop the app: the note layer does not need AI, so a
    /// broken configuration degrades to <see cref="NullAiService"/> and the affordances stay off.
    /// </remarks>
    public static async Task<LocalAiHost> CreateAsync(
        ISettingsStore settings,
        IAiRetriever retriever,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);

        try
        {
            var options = await OllamaOptions.LoadAsync(settings, cancellationToken).ConfigureAwait(true);

            return options.Enabled
                ? new LocalAiHost(
                    new OllamaAiService(options, retriever),
                    AiCapability.Unavailable(AiAvailability.WorkerUnavailable),
                    dispatcher)
                : Disabled(dispatcher);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Could not read AI settings; local AI stays off.", ex);
            return Disabled(dispatcher);
        }
    }

    /// <summary>
    /// Asks the worker to load the model, without waiting for it.
    /// </summary>
    /// <remarks>
    /// Called when an AI menu opens. The user then spends a few seconds reading the tiles, and
    /// those seconds are spent loading weights instead of being spent twice — once reading, once
    /// waiting. Nothing is awaited and nothing is reported: if the daemon is down the tiles are
    /// already disabled, and if it comes up the next probe says so.
    /// </remarks>
    public void Warm() =>
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.WarmAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CrashLog.Write("AI warm-up failed", ex);
            }
        });

    /// <summary>
    /// Probes in the background and keeps <see cref="Capability"/> current.
    /// </summary>
    /// <remarks>
    /// Re-probed on an interval rather than once: Ollama is a separate process a user can start,
    /// stop, or update at any time, and an AI menu that stays greyed out until the app restarts
    /// would be wrong more often than right.
    /// </remarks>
    public void StartProbing(TimeSpan interval)
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);

            do
            {
                try
                {
                    var capability = await _service.ProbeAsync(_stopping.Token).ConfigureAwait(false);
                    Publish(capability);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    CrashLog.Write("AI probe failed", ex);
                    Publish(AiCapability.Unavailable(AiAvailability.WorkerUnavailable));
                }
            }
            while (await SafeWaitAsync(timer).ConfigureAwait(false));
        });
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
        (_service as IDisposable)?.Dispose();
    }

    private static LocalAiHost Disabled(Microsoft.UI.Dispatching.DispatcherQueue dispatcher) =>
        new(
            new NullAiService(AiAvailability.Disabled),
            AiCapability.Unavailable(AiAvailability.Disabled),
            dispatcher);

    private void Publish(AiCapability capability)
    {
        if (capability == Capability)
        {
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            Capability = capability;
            CapabilityChanged?.Invoke(this, capability);
        });
    }

    private async Task<bool> SafeWaitAsync(PeriodicTimer timer)
    {
        try
        {
            return await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
