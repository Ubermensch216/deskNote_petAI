namespace DeskNote.Core.Abstractions;

/// <summary>
/// Key/value application settings: hotkey bindings, UI locale override, theme, window defaults.
/// Secrets never go here — cloud credentials belong in the Windows Credential Locker (report p15).
/// </summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Well-known <see cref="ISettingsStore"/> keys.</summary>
public static class SettingKeys
{
    /// <summary>"ko-KR", "en-US", or absent to follow the system locale.</summary>
    public const string UiLocale = "ui.locale";

    /// <summary>"light", "dark", or "system".</summary>
    public const string Theme = "ui.theme";

    /// <summary>JSON map of command name to hotkey gesture.</summary>
    public const string Hotkeys = "input.hotkeys";

    /// <summary>Default color key applied to newly created notes.</summary>
    public const string DefaultNoteColor = "note.defaultColor";

    /// <summary>Default size preset applied to newly created notes.</summary>
    public const string DefaultNoteSize = "note.defaultSize";

    /// <summary>Whether DeskNote registers itself to start with Windows.</summary>
    public const string LaunchAtStartup = "app.launchAtStartup";

    /// <summary>"false" turns local AI off entirely; absent means enabled-if-available.</summary>
    public const string AiEnabled = "ai.enabled";

    /// <summary>Base address of the local inference server, e.g. "http://localhost:11434".</summary>
    public const string AiEndpoint = "ai.endpoint";

    /// <summary>Model tag used for the text actions, e.g. "gemma4:e2b".</summary>
    public const string AiModel = "ai.model";

    /// <summary>Model tag used for retrieval embeddings, e.g. "bge-m3:latest".</summary>
    public const string AiEmbeddingModel = "ai.embeddingModel";

    /// <summary>
    /// How long the local server should keep the model resident between calls, as minutes.
    /// </summary>
    /// <remarks>
    /// Exposed because the right value is a property of the machine, not of the app: a 7GB model
    /// held for twenty minutes is free on a workstation and ruinous on a laptop with 8GB. "0"
    /// unloads immediately, which is the setting for a machine that cannot spare the memory.
    /// </remarks>
    public const string AiKeepAliveMinutes = "ai.keepAliveMinutes";
}
