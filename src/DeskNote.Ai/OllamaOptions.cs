using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DeskNote.Core.Abstractions;

namespace DeskNote.Ai;

/// <summary>
/// Everything the Ollama provider needs to reach a model, resolved from <see cref="ISettingsStore"/>.
/// </summary>
/// <remarks>
/// Defaults point at a stock local Ollama install. No value here is a secret — the endpoint is
/// loopback and there is no key — so the settings table is the right home for it (report p15
/// keeps credentials out of settings, and there are none to keep out).
/// </remarks>
public sealed record OllamaOptions
{
    public const string DefaultEndpoint = "http://localhost:11434";

    public const string DefaultModel = "gemma4:e2b";

    public const string DefaultEmbeddingModel = "bge-m3:latest";

    /// <summary>When false the provider reports <see cref="Core.Ai.AiAvailability.Disabled"/> and never opens a socket.</summary>
    public bool Enabled { get; init; } = true;

    public Uri Endpoint { get; init; } = new(DefaultEndpoint);

    public string Model { get; init; } = DefaultModel;

    public string EmbeddingModel { get; init; } = DefaultEmbeddingModel;

    /// <summary>
    /// Budget for a probe. Kept short because startup fires it on a background task and a hung
    /// daemon must degrade to "unavailable" quickly rather than leave AI affordances in limbo.
    /// </summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Budget for one generation. Generous: a 7GB model on CPU is slow, and cancelling a summary
    /// the user is watching stream in is worse than waiting.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Low but not zero: deterministic enough to be predictable, not so rigid it loops.</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>
    /// How long the daemon should keep the model resident after a call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ollama's own default is five minutes, and it does not survive the way this app is used:
    /// summarise a note, read the result, tag it — the second action pays the 60-second load
    /// again because the pause in between was longer than the daemon's patience.
    /// </para>
    /// <para>
    /// Fifteen minutes covers a working session without pretending memory is free. The value is a
    /// setting because it is a property of the machine rather than of the app.
    /// </para>
    /// </remarks>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>The <c>keep_alive</c> value to send, in the seconds form the API accepts.</summary>
    internal string KeepAliveValue =>
        ((int)KeepAlive.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";

    public static async Task<OllamaOptions> LoadAsync(
        ISettingsStore settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return FromSettings(stored);
    }

    /// <summary>
    /// Builds options from raw settings values, falling back to a default whenever a value is
    /// absent or unusable. A malformed endpoint must not stop the app from starting: AI is the
    /// optional layer, so a bad value costs the AI features and nothing else.
    /// </summary>
    public static OllamaOptions FromSettings(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new OllamaOptions();

        if (settings.TryGetValue(SettingKeys.AiEnabled, out var enabled))
        {
            options = options with
            {
                Enabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase),
            };
        }

        if (settings.TryGetValue(SettingKeys.AiEndpoint, out var endpoint)
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            options = options with { Endpoint = parsed };
        }

        if (settings.TryGetValue(SettingKeys.AiModel, out var model) && !string.IsNullOrWhiteSpace(model))
        {
            options = options with { Model = model.Trim() };
        }

        if (settings.TryGetValue(SettingKeys.AiEmbeddingModel, out var embedding)
            && !string.IsNullOrWhiteSpace(embedding))
        {
            options = options with { EmbeddingModel = embedding.Trim() };
        }

        // Clamped rather than trusted. A negative value is meaningless to the daemon, and an hour
        // of a 7GB model resident is a decision a typo should not be able to make.
        if (settings.TryGetValue(SettingKeys.AiKeepAliveMinutes, out var keepAlive)
            && double.TryParse(keepAlive, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
        {
            options = options with { KeepAlive = TimeSpan.FromMinutes(ClampKeepAliveMinutes(minutes)) };
        }

        return options;
    }

    /// <summary>Longest the model may be asked to stay resident, in minutes.</summary>
    public const double MaximumKeepAliveMinutes = 60;

    /// <summary>Brings a typed-in keep-alive into the range the daemon is asked for.</summary>
    public static double ClampKeepAliveMinutes(double minutes) =>
        Math.Clamp(minutes, 0, MaximumKeepAliveMinutes);

    /// <summary>
    /// Reads an endpoint the way <see cref="FromSettings"/> does, for a caller that has to refuse
    /// a bad one rather than silently fall back to the default.
    /// </summary>
    /// <remarks>
    /// A settings screen and the loader have to agree on what counts as an address, or a value the
    /// screen accepted would be dropped on the next launch and the AI would quietly go back to
    /// localhost with nothing said.
    /// </remarks>
    public static bool TryParseEndpoint(string? value, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }

    /// <summary>
    /// Whether this endpoint keeps note text on this machine.
    /// </summary>
    /// <remarks>
    /// The product's claim is that inference is local, and the endpoint is the one setting that
    /// can quietly make it untrue: an address pointing somewhere else sends note bodies over the
    /// network with nothing on screen to say so. Nothing here forbids it — running Ollama on a
    /// machine of your own is a reasonable thing to want — but a caller can say it out loud.
    /// </remarks>
    public bool IsLocal => Endpoint.IsLoopback;

    /// <summary>
    /// Writes the values <see cref="FromSettings"/> reads, and only those.
    /// </summary>
    /// <remarks>
    /// The timeouts and the temperature are deliberately not persisted: they are properties of how
    /// this client talks to the daemon rather than choices a user makes, and writing them would
    /// turn a tuning change in a future version into a value already pinned in every install.
    /// </remarks>
    public async Task SaveAsync(ISettingsStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        await store.SetAsync(SettingKeys.AiEnabled, Enabled ? "true" : "false", cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.AiEndpoint, Endpoint.ToString(), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.AiModel, Model.Trim(), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.AiEmbeddingModel, EmbeddingModel.Trim(), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(
                SettingKeys.AiKeepAliveMinutes,
                ClampKeepAliveMinutes(KeepAlive.TotalMinutes)
                    .ToString("0.##", CultureInfo.InvariantCulture),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether two configurations would talk to the same daemon in the same way.
    /// </summary>
    /// <remarks>
    /// Used to decide whether saving has to rebuild the AI client. Rebuilding it cancels whatever
    /// it is doing, so saving a new pet name should not take a summary down with it.
    /// </remarks>
    public bool TalksTheSameWayAs(OllamaOptions other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Enabled == other.Enabled
            && Endpoint == other.Endpoint
            && string.Equals(Model, other.Model, StringComparison.Ordinal)
            && KeepAlive == other.KeepAlive;
    }
}
