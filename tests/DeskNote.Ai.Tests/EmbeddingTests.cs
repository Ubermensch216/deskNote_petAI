using System.Net;
using System.Text.Json;

namespace DeskNote.Ai.Tests;

/// <summary>
/// Embedding runs behind every save, which is exactly why none of its failures may surface as an
/// exception: the note is already stored, and a missing model can only be allowed to cost search
/// coverage.
/// </summary>
public class EmbeddingTests
{
    private const string Body = """{"embeddings":[[0.1,0.2,0.3],[0.4,0.5,0.6]]}""";

    [Fact]
    public async Task Vectors_come_back_in_input_order()
    {
        var daemon = new FakeOllama().Route("/api/embed", HttpStatusCode.OK, Body);
        using var service = new OllamaEmbeddingService(daemon.Client(), new OllamaOptions());

        var vectors = await service.EmbedAsync(["첫째", "둘째"], TestContext.Current.CancellationToken);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(0.1f, vectors[0].Span[0], 5);
        Assert.Equal(0.4f, vectors[1].Span[0], 5);
        Assert.Equal(3, service.Dimensions);
    }

    [Fact]
    public async Task The_configured_embedding_model_is_the_one_asked_for()
    {
        var daemon = new FakeOllama().Route("/api/embed", HttpStatusCode.OK, Body);

        using var service = new OllamaEmbeddingService(
            daemon.Client(),
            new OllamaOptions { EmbeddingModel = "bge-m3:latest" });

        await service.EmbedAsync(["하나", "둘"], TestContext.Current.CancellationToken);

        using var request = JsonDocument.Parse(daemon.RequestBodies[^1]);

        Assert.Equal("bge-m3:latest", request.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, request.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task A_daemon_that_is_not_running_yields_no_vectors_and_no_exception()
    {
        var daemon = new FakeOllama().Fails("/api/embed", new HttpRequestException("connection refused"));
        using var service = new OllamaEmbeddingService(daemon.Client(), new OllamaOptions());

        Assert.Empty(await service.EmbedAsync(["무엇이든"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_error_status_yields_no_vectors()
    {
        var daemon = new FakeOllama().Route("/api/embed", HttpStatusCode.NotFound, """{"error":"model not found"}""");
        using var service = new OllamaEmbeddingService(daemon.Client(), new OllamaOptions());

        Assert.Empty(await service.EmbedAsync(["무엇이든"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A short batch cannot be matched to its inputs, and storing vectors against the wrong chunks
    /// would corrupt retrieval silently — far worse than indexing nothing.
    /// </summary>
    [Fact]
    public async Task A_batch_of_the_wrong_length_is_refused_whole()
    {
        var daemon = new FakeOllama().Route("/api/embed", HttpStatusCode.OK, """{"embeddings":[[0.1,0.2]]}""");
        using var service = new OllamaEmbeddingService(daemon.Client(), new OllamaOptions());

        Assert.Empty(await service.EmbedAsync(["하나", "둘"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disabled_ai_never_opens_a_socket()
    {
        var daemon = new FakeOllama().Route("/api/embed", HttpStatusCode.OK, Body);
        using var service = new OllamaEmbeddingService(daemon.Client(), new OllamaOptions { Enabled = false });

        Assert.Empty(await service.EmbedAsync(["무엇이든"], TestContext.Current.CancellationToken));
        Assert.Empty(daemon.RequestPaths);
    }

    [Fact]
    public async Task An_empty_batch_asks_nothing()
    {
        var daemon = new FakeOllama().Route("/api/embed", HttpStatusCode.OK, Body);
        using var service = new OllamaEmbeddingService(daemon.Client(), new OllamaOptions());

        Assert.Empty(await service.EmbedAsync([], TestContext.Current.CancellationToken));
        Assert.Empty(daemon.RequestPaths);
    }
}
