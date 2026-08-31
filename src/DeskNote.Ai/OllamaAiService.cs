using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;
using DeskNote.Core.Services;

namespace DeskNote.Ai;

/// <summary>
/// <see cref="ILocalAiService"/> backed by a local Ollama daemon.
/// </summary>
/// <remarks>
/// <para>
/// The interface asks for the model to live in a separate process so that exhausted memory or a
/// tripped GPU driver cannot take the note windows down (report p10). Ollama already is that
/// process; this class is the client, and the isolation comes for free — a crashed daemon shows
/// up here as a failed HTTP call and degrades to "unavailable", never as a dead note window.
/// </para>
/// <para>
/// Nothing here loads weights. <see cref="ProbeAsync"/> only lists what is installed, so startup
/// can call it on a background task without turning app start into model start (report p14).
/// </para>
/// </remarks>
public sealed class OllamaAiService : ILocalAiService, IDisposable
{
    /// <summary>
    /// How long a successful probe is trusted. Long enough that a burst of actions costs one
    /// round trip, short enough that stopping Ollama is noticed within a note-taking pause.
    /// </summary>
    private static readonly TimeSpan CapabilityFreshness = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly OllamaOptions _options;
    private readonly IAiRetriever _retriever;
    private readonly IClock _clock;

    private AiCapability _capability = AiCapability.Unavailable(AiAvailability.WorkerUnavailable);
    private long _capabilityStamp = -1;

    public OllamaAiService(OllamaOptions options, IAiRetriever? retriever = null, IClock? clock = null)
        : this(CreateClient(options), options, retriever, clock, ownsHttp: true)
    {
    }

    public OllamaAiService(
        HttpClient http,
        OllamaOptions options,
        IAiRetriever? retriever = null,
        IClock? clock = null,
        bool ownsHttp = false)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options;
        _retriever = retriever ?? NoRetrieval.Instance;
        _clock = clock ?? SystemClock.Instance;
        _ownsHttp = ownsHttp;
    }

    /// <summary>The most recent probe result, without issuing a new one.</summary>
    public AiCapability LastKnownCapability => _capability;

    public async Task<AiCapability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Remember(AiCapability.Unavailable(AiAvailability.Disabled));
        }

        try
        {
            using var timeout = Linked(cancellationToken, _options.ProbeTimeout);

            var tags = await _http
                .GetFromJsonAsync<OllamaWire.TagsResponse>("/api/tags", OllamaWire.Json, timeout.Token)
                .ConfigureAwait(false);

            var installed = tags?.Models.FirstOrDefault(m => Matches(m.Name, _options.Model));
            if (installed is null)
            {
                return Remember(AiCapability.Unavailable(AiAvailability.ModelNotInstalled));
            }

            return Remember(new AiCapability(
                AiAvailability.Available,
                ProviderName: "Ollama",
                ModelId: installed.Name,
                Accelerator: await DetectAcceleratorAsync(timeout.Token).ConfigureAwait(false),
                EstimatedMemoryBytes: installed.Size));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            // A daemon that is not running, not answering, or answering with something else is the
            // same thing to a caller: no AI right now, notes unaffected.
            return Remember(AiCapability.Unavailable(AiAvailability.WorkerUnavailable));
        }
    }

    public async Task<AiTextResult> SummarizeAsync(
        NoteContext context,
        CancellationToken cancellationToken = default) =>
        await RunTextActionAsync(AiAction.Summarize, context, RewriteStyle.Concise, cancellationToken)
            .ConfigureAwait(false);

    public async Task<AiTextResult> OrganizeAsync(
        NoteContext context,
        CancellationToken cancellationToken = default) =>
        await RunTextActionAsync(AiAction.Organize, context, RewriteStyle.Concise, cancellationToken)
            .ConfigureAwait(false);

    public async Task<AiTextResult> RewriteAsync(
        NoteContext context,
        RewriteStyle style,
        CancellationToken cancellationToken = default) =>
        await RunTextActionAsync(AiAction.Rewrite, context, style, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ExtractedTask>> ExtractTasksAsync(
        NoteContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);

        var json = await ChatAsync(
            AiPrompts.SystemFor(AiAction.ExtractTasks),
            AiPrompts.UserFor(AiAction.ExtractTasks, context, now: _clock.Now),
            AiSchemas.ExtractedTasks,
            cancellationToken).ConfigureAwait(false);

        return AiResponseParser.ReadTasks(json);
    }

    public async Task<IReadOnlyList<SuggestedTag>> SuggestTagsAsync(
        NoteContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);

        var json = await ChatAsync(
            AiPrompts.SystemFor(AiAction.SuggestTags),
            AiPrompts.UserFor(AiAction.SuggestTags, context),
            AiSchemas.SuggestedTags,
            cancellationToken).ConfigureAwait(false);

        return AiResponseParser.ReadTags(json);
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AiQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);

        var retrieved = await _retriever.RetrieveAsync(query, cancellationToken).ConfigureAwait(false);

        var request = new OllamaWire.ChatRequest
        {
            Model = _options.Model,
            Stream = true,
            Messages =
            [
                new OllamaWire.Message("system", AiPrompts.SystemFor(AiAction.Answer)),
                new OllamaWire.Message("user", AiPrompts.UserForQuestion(query, retrieved)),
            ],
            Options = new OllamaWire.ChatOptions { Temperature = _options.Temperature },
        };

        using var timeout = Linked(cancellationToken, _options.RequestTimeout);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request, options: OllamaWire.Json),
        };

        using var response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        // Ollama streams NDJSON: one complete JSON object per line, tokens in message.content.
        while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<OllamaWire.ChatResponse>(line, OllamaWire.Json);

            if (!string.IsNullOrEmpty(chunk?.Error))
            {
                throw new AiProviderException(chunk.Error);
            }

            if (chunk?.Message?.Content is { Length: > 0 } text)
            {
                yield return text;
            }

            if (chunk?.Done == true)
            {
                yield break;
            }
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    /// <summary>An <see cref="HttpClient"/> configured for this endpoint, with timeouts left to the caller.</summary>
    public static HttpClient CreateClient(OllamaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new HttpClient
        {
            BaseAddress = options.Endpoint,

            // Every call already carries its own deadline; a second one here would cancel long
            // generations with a less useful exception.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private async Task<AiTextResult> RunTextActionAsync(
        AiAction action,
        NoteContext context,
        RewriteStyle style,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();

        var content = await ChatAsync(
            AiPrompts.SystemFor(action, style),
            AiPrompts.UserFor(action, context, style),
            format: null,
            cancellationToken).ConfigureAwait(false);

        return new AiTextResult
        {
            Original = context.EffectiveText,

            // Cleaned here rather than at the point of applying, so the diff preview shows the
            // same text the note will hold. A proposal the user approved and a proposal the note
            // received being two different strings is the one thing this window exists to prevent.
            Proposed = AiTextCleanup.ToNoteText(content),
            ModelId = _capability.ModelId ?? _options.Model,
            Elapsed = Stopwatch.GetElapsedTime(started),
        };
    }

    private async Task<string> ChatAsync(
        string system,
        string user,
        JsonElement? format,
        CancellationToken cancellationToken)
    {
        var request = new OllamaWire.ChatRequest
        {
            Model = _options.Model,
            Stream = false,
            Format = format,
            Messages =
            [
                new OllamaWire.Message("system", system),
                new OllamaWire.Message("user", user),
            ],
            Options = new OllamaWire.ChatOptions { Temperature = _options.Temperature },
        };

        using var timeout = Linked(cancellationToken, _options.RequestTimeout);

        using var response = await _http
            .PostAsJsonAsync("/api/chat", request, OllamaWire.Json, timeout.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException($"Ollama returned {(int)response.StatusCode} for /api/chat.");
        }

        var body = await response.Content
            .ReadFromJsonAsync<OllamaWire.ChatResponse>(OllamaWire.Json, timeout.Token)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(body?.Error))
        {
            throw new AiProviderException(body.Error);
        }

        return body?.Message?.Content ?? string.Empty;
    }

    /// <summary>
    /// Throws unless a recent probe says the model can run. Cached briefly so a run of actions
    /// does not pay for a probe each time.
    /// </summary>
    private async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (_capabilityStamp >= 0
            && Stopwatch.GetElapsedTime(_capabilityStamp) < CapabilityFreshness
            && _capability.IsAvailable)
        {
            return;
        }

        var capability = await ProbeAsync(cancellationToken).ConfigureAwait(false);

        if (!capability.IsAvailable)
        {
            throw new AiUnavailableException(capability.Availability);
        }
    }

    /// <summary>
    /// Reports where inference actually ran rather than assuming it from the presence of a GPU
    /// (report p9). Only a currently loaded model can answer this, so an idle daemon stays
    /// <see cref="AiAccelerator.Unknown"/> instead of guessing.
    /// </summary>
    private async Task<AiAccelerator> DetectAcceleratorAsync(CancellationToken cancellationToken)
    {
        try
        {
            var running = await _http
                .GetFromJsonAsync<OllamaWire.TagsResponse>("/api/ps", OllamaWire.Json, cancellationToken)
                .ConfigureAwait(false);

            var loaded = running?.Models.FirstOrDefault(m => Matches(m.Name, _options.Model));

            return loaded is null
                ? AiAccelerator.Unknown
                : loaded.SizeVram > 0 ? AiAccelerator.Gpu : AiAccelerator.Cpu;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            // Optional information. Not knowing the accelerator never makes AI unavailable.
            return AiAccelerator.Unknown;
        }
    }

    private AiCapability Remember(AiCapability capability)
    {
        _capability = capability;
        _capabilityStamp = Stopwatch.GetTimestamp();
        return capability;
    }

    private static CancellationTokenSource Linked(CancellationToken cancellationToken, TimeSpan budget)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(budget);
        return source;
    }

    /// <summary>Compares model tags, treating a bare name as the ":latest" tag the way Ollama does.</summary>
    internal static bool Matches(string installed, string wanted) =>
        string.Equals(Normalize(installed), Normalize(wanted), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string tag) =>
        tag.Contains(':', StringComparison.Ordinal)
            ? tag.Trim()
            : string.Create(CultureInfo.InvariantCulture, $"{tag.Trim()}:latest");
}

/// <summary>The local model was reachable but the request failed. Distinct from "no AI installed".</summary>
public sealed class AiProviderException(string message) : InvalidOperationException(message);
