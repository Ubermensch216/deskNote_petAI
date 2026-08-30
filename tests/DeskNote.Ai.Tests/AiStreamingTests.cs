using System.Net;
using System.Text.Json;
using DeskNote.Core.Ai;

namespace DeskNote.Ai.Tests;

/// <summary>
/// Q&amp;A streams so the sidecar can show tokens as they arrive rather than a spinner. These tests
/// cover the wire format and the retrieval seam that decides which notes the model may read.
/// </summary>
public class AiStreamingTests
{
    private const string Ndjson = """
        {"message":{"role":"assistant","content":"회의는 "},"done":false}
        {"message":{"role":"assistant","content":"화요일"},"done":false}
        {"message":{"role":"assistant","content":"입니다."},"done":true}
        {"message":{"role":"assistant","content":"보내면 안 되는 꼬리"},"done":false}
        """;

    [Fact]
    public async Task Tokens_arrive_in_order_and_stop_at_done()
    {
        var daemon = new FakeOllama()
            .WithModels("gemma4:e2b")
            .Route("/api/chat", HttpStatusCode.OK, Ndjson, "application/x-ndjson");

        using var service = new OllamaAiService(daemon.Client(), new OllamaOptions());

        var chunks = new List<string>();
        await foreach (var chunk in service.StreamAnswerAsync(
            new AiQuery { Question = "회의 언제야?" },
            TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(["회의는 ", "화요일", "입니다."], chunks);
    }

    [Fact]
    public async Task Retrieved_notes_are_wrapped_as_untrusted_context()
    {
        var noteId = Guid.NewGuid();
        var daemon = new FakeOllama()
            .WithModels("gemma4:e2b")
            .Route("/api/chat", HttpStatusCode.OK, Ndjson, "application/x-ndjson");

        var retriever = new StubRetriever([new RetrievedNote(noteId, "회의는 화요일 10시", "팀 회의")]);
        using var service = new OllamaAiService(daemon.Client(), new OllamaOptions(), retriever);

        await foreach (var _ in service.StreamAnswerAsync(
            new AiQuery { Question = "회의 언제야?", ScopedNoteIds = [noteId] },
            TestContext.Current.CancellationToken))
        {
        }

        var user = UserMessage(daemon);

        Assert.Contains(AiPrompts.BlockOpen, user, StringComparison.Ordinal);
        Assert.Contains("회의는 화요일 10시", user, StringComparison.Ordinal);
        Assert.Contains("회의 언제야?", user, StringComparison.Ordinal);
        Assert.Equal(new AiQuery { Question = "회의 언제야?", ScopedNoteIds = [noteId] }.Question, retriever.Seen!.Question);
    }

    /// <summary>Answering from the model's own memory when retrieval found nothing would invent facts.</summary>
    [Fact]
    public async Task An_empty_retrieval_tells_the_model_to_say_it_found_nothing()
    {
        var daemon = new FakeOllama()
            .WithModels("gemma4:e2b")
            .Route("/api/chat", HttpStatusCode.OK, Ndjson, "application/x-ndjson");

        using var service = new OllamaAiService(daemon.Client(), new OllamaOptions());

        await foreach (var _ in service.StreamAnswerAsync(
            new AiQuery { Question = "예산은?" },
            TestContext.Current.CancellationToken))
        {
        }

        Assert.Contains("찾지 못했습니다", UserMessage(daemon), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_requires_an_available_model()
    {
        var daemon = new FakeOllama().WithModels("llama3:8b");
        using var service = new OllamaAiService(daemon.Client(), new OllamaOptions());

        await Assert.ThrowsAsync<AiUnavailableException>(async () =>
        {
            await foreach (var _ in service.StreamAnswerAsync(
                new AiQuery { Question = "뭐라도" },
                TestContext.Current.CancellationToken))
            {
            }
        });
    }

    private static string UserMessage(FakeOllama daemon)
    {
        using var request = JsonDocument.Parse(daemon.RequestBodies[^1]);

        return request.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .Single(m => m.GetProperty("role").GetString() == "user")
            .GetProperty("content")
            .GetString()!;
    }

    private sealed class StubRetriever(IReadOnlyList<RetrievedNote> notes) : IAiRetriever
    {
        internal AiQuery? Seen { get; private set; }

        public Task<IReadOnlyList<RetrievedNote>> RetrieveAsync(
            AiQuery query,
            CancellationToken cancellationToken = default)
        {
            Seen = query;
            return Task.FromResult(notes);
        }
    }
}
