using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskNote.Core.Abstractions;

namespace DeskNote.Ai;

/// <summary>
/// <see cref="IEmbeddingService"/> backed by an embedding model on the local Ollama daemon.
/// </summary>
/// <remarks>
/// Failures are swallowed into an empty result. This runs on the save path's coat-tails, where the
/// only correct behaviour when the daemon is down is that semantic search misses those notes until
/// the next save — never that saving a note reports an error about a model.
/// </remarks>
public sealed class OllamaEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly OllamaOptions _options;

    public OllamaEmbeddingService(OllamaOptions options)
        : this(OllamaAiService.CreateClient(options), options, ownsHttp: true)
    {
    }

    public OllamaEmbeddingService(HttpClient http, OllamaOptions options, bool ownsHttp = false)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options;
        _ownsHttp = ownsHttp;
    }

    public int Dimensions { get; private set; }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (!_options.Enabled || inputs.Count == 0)
        {
            return [];
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);

            using var response = await _http
                .PostAsJsonAsync(
                    "/api/embed",
                    new EmbedRequest
                    {
                        Model = _options.EmbeddingModel,
                        Input = inputs,
                        KeepAlive = _options.KeepAliveValue,
                    },
                    OllamaWire.Json,
                    timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var body = await response.Content
                .ReadFromJsonAsync<EmbedResponse>(OllamaWire.Json, timeout.Token)
                .ConfigureAwait(false);

            if (body?.Embeddings is not { Count: > 0 } embeddings)
            {
                return [];
            }

            Dimensions = embeddings[0].Length;

            // A batch that comes back the wrong length cannot be matched to its inputs, and
            // storing vectors against the wrong chunks is worse than storing none.
            return embeddings.Count == inputs.Count
                ? [.. embeddings.Select(vector => new ReadOnlyMemory<float>(vector))]
                : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private sealed record EmbedRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required IReadOnlyList<string> Input { get; init; }

        /// <summary>
        /// The embedding model unloads on the same schedule as the chat model, and it is called
        /// far more often — once per save, once per search — so it pays for the reload far more
        /// often too.
        /// </summary>
        [JsonPropertyName("keep_alive")]
        public string? KeepAlive { get; init; }
    }

    private sealed record EmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public IReadOnlyList<float[]>? Embeddings { get; init; }
    }
}
