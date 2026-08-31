using System.Net;
using System.Text.Json;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;

namespace DeskNote.Ai.Tests;

/// <summary>
/// Guards the residency contract with the daemon.
/// </summary>
/// <remarks>
/// Ollama applies the <c>keep_alive</c> of the request it last received, so one call that omits
/// it silently returns the model to the five-minute default and the next action pays the full
/// load again. That makes "every request carries it" the property worth testing, rather than
/// "some request does".
/// </remarks>
public class KeepAliveTests
{
    private static readonly NoteContext Note = new() { NoteId = Guid.NewGuid(), Content = "회의 준비" };

    [Fact]
    public async Task A_text_action_asks_the_daemon_to_keep_the_model_resident()
    {
        var daemon = new FakeOllama().WithModels("gemma4:e2b").WithChatReply("요약된 본문");
        var service = new OllamaAiService(daemon.Client(), new OllamaOptions(), ownsHttp: true);

        await service.SummarizeAsync(Note);

        Assert.Equal("900s", KeepAliveOf(daemon.RequestBodies[^1]));
    }

    [Fact]
    public async Task A_structured_action_carries_it_too()
    {
        var daemon = new FakeOllama()
            .WithModels("gemma4:e2b")
            .WithChatReply("""{"tags":[{"name":"회의","confidence":0.9}]}""");

        var service = new OllamaAiService(daemon.Client(), new OllamaOptions(), ownsHttp: true);

        await service.SuggestTagsAsync(Note);

        Assert.Equal("900s", KeepAliveOf(daemon.RequestBodies[^1]));
    }

    [Fact]
    public async Task The_configured_value_is_what_is_sent()
    {
        var options = OllamaOptions.FromSettings(
            new Dictionary<string, string> { [SettingKeys.AiKeepAliveMinutes] = "2" });

        var daemon = new FakeOllama().WithModels("gemma4:e2b").WithChatReply("요약");
        var service = new OllamaAiService(daemon.Client(), options, ownsHttp: true);

        await service.SummarizeAsync(Note);

        Assert.Equal("120s", KeepAliveOf(daemon.RequestBodies[^1]));
    }

    /// <summary>An hour of a 7GB model resident is not a decision a stray keystroke should make.</summary>
    [Theory]
    [InlineData("-5", 0)]
    [InlineData("9999", 60)]
    [InlineData("30", 30)]
    [InlineData("스물", 15)]
    public void A_configured_duration_is_clamped_to_something_a_machine_can_afford(string stored, int expected)
    {
        var options = OllamaOptions.FromSettings(
            new Dictionary<string, string> { [SettingKeys.AiKeepAliveMinutes] = stored });

        Assert.Equal(TimeSpan.FromMinutes(expected), options.KeepAlive);
    }

    [Fact]
    public async Task Warming_loads_the_model_without_asking_it_for_anything()
    {
        var daemon = new FakeOllama().Route("/api/chat", HttpStatusCode.OK, """{"done":true}""");
        var service = new OllamaAiService(daemon.Client(), new OllamaOptions(), ownsHttp: true);

        await service.WarmAsync();

        var sent = JsonDocument.Parse(Assert.Single(daemon.RequestBodies)).RootElement;

        Assert.Equal("/api/chat", Assert.Single(daemon.RequestPaths));
        Assert.Empty(sent.GetProperty("messages").EnumerateArray());
        Assert.Equal("900s", sent.GetProperty("keep_alive").GetString());
    }

    /// <summary>
    /// Turning residency off has to turn warming off with it: loading a model the daemon is about
    /// to drop is the worst of both — the memory spike with none of the benefit.
    /// </summary>
    [Fact]
    public async Task Warming_does_nothing_when_the_model_is_not_kept_resident()
    {
        var options = OllamaOptions.FromSettings(
            new Dictionary<string, string> { [SettingKeys.AiKeepAliveMinutes] = "0" });

        var daemon = new FakeOllama();
        var service = new OllamaAiService(daemon.Client(), options, ownsHttp: true);

        await service.WarmAsync();

        Assert.Empty(daemon.RequestPaths);
    }

    /// <summary>Warming is an optimisation; a daemon that is down must not surface as an error.</summary>
    [Fact]
    public async Task Warming_a_daemon_that_is_not_running_is_silent()
    {
        var daemon = new FakeOllama().Fails("/api/chat", new HttpRequestException("connection refused"));
        var service = new OllamaAiService(daemon.Client(), new OllamaOptions(), ownsHttp: true);

        await service.WarmAsync();
    }

    private static string? KeepAliveOf(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("keep_alive").GetString();
}
