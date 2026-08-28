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
}
