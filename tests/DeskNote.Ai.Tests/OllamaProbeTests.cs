using System.Net;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;

namespace DeskNote.Ai.Tests;

/// <summary>
/// Probing is the gate every other action passes through, and the note app's promise is that a
/// missing or broken model costs nothing but the AI features. These tests pin each way that can
/// fail to the availability reason the UI needs to explain it.
/// </summary>
public class OllamaProbeTests
{
    private static OllamaOptions Options(bool enabled = true, string model = "gemma4:e2b") =>
        new() { Enabled = enabled, Model = model };

    [Fact]
    public async Task Disabled_ai_never_opens_a_socket()
    {
        var daemon = new FakeOllama().WithModels("gemma4:e2b");
        using var service = new OllamaAiService(daemon.Client(), Options(enabled: false));

        var capability = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AiAvailability.Disabled, capability.Availability);
        Assert.Empty(daemon.RequestPaths);
    }

    [Fact]
    public async Task A_daemon_that_is_not_running_reports_worker_unavailable()
    {
        var daemon = new FakeOllama().Fails("/api/tags", new HttpRequestException("connection refused"));
        using var service = new OllamaAiService(daemon.Client(), Options());

        var capability = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AiAvailability.WorkerUnavailable, capability.Availability);
        Assert.False(capability.IsAvailable);
    }

    [Fact]
    public async Task A_daemon_without_the_model_reports_model_not_installed()
    {
        var daemon = new FakeOllama().WithModels("llama3:8b", "bge-m3:latest");
        using var service = new OllamaAiService(daemon.Client(), Options());

        var capability = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AiAvailability.ModelNotInstalled, capability.Availability);
    }

    [Fact]
    public async Task An_installed_model_reports_available_with_its_identity()
    {
        var daemon = new FakeOllama()
            .WithModels("gemma4:e2b")
            .Route("/api/ps", HttpStatusCode.OK, """{"models":[{"name":"gemma4:e2b","size_vram":6000000000}]}""");

        using var service = new OllamaAiService(daemon.Client(), Options());

        var capability = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.True(capability.IsAvailable);
        Assert.Equal("Ollama", capability.ProviderName);
        Assert.Equal("gemma4:e2b", capability.ModelId);
        Assert.Equal(AiAccelerator.Gpu, capability.Accelerator);
        Assert.Equal(7_200_000_000, capability.EstimatedMemoryBytes);
    }

    [Fact]
    public async Task A_model_that_is_loaded_without_vram_is_reported_as_cpu()
    {
        var daemon = new FakeOllama()
            .WithModels("gemma4:e2b")
            .Route("/api/ps", HttpStatusCode.OK, """{"models":[{"name":"gemma4:e2b","size_vram":0}]}""");

        using var service = new OllamaAiService(daemon.Client(), Options());

        var capability = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AiAccelerator.Cpu, capability.Accelerator);
    }

    /// <summary>An idle daemon cannot say where inference would run, and guessing would be worse.</summary>
    [Fact]
    public async Task An_unloaded_model_leaves_the_accelerator_unknown()
    {
        var daemon = new FakeOllama()
            .WithModels("gemma4:e2b")
            .Route("/api/ps", HttpStatusCode.OK, """{"models":[]}""");

        using var service = new OllamaAiService(daemon.Client(), Options());

        var capability = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.True(capability.IsAvailable);
        Assert.Equal(AiAccelerator.Unknown, capability.Accelerator);
    }

    [Theory]
    [InlineData("bge-m3:latest", "bge-m3")]
    [InlineData("bge-m3", "bge-m3:latest")]
    [InlineData("Gemma4:E2B", "gemma4:e2b")]
    public void A_bare_model_name_means_the_latest_tag(string installed, string wanted) =>
        Assert.True(OllamaAiService.Matches(installed, wanted));

    [Fact]
    public void A_different_tag_of_the_same_model_is_not_a_match() =>
        Assert.False(OllamaAiService.Matches("gemma4:e4b", "gemma4:e2b"));

    [Fact]
    public async Task Actions_throw_rather_than_run_when_the_model_is_missing()
    {
        var daemon = new FakeOllama().WithModels("llama3:8b");
        using var service = new OllamaAiService(daemon.Client(), Options());

        var failure = await Assert.ThrowsAsync<AiUnavailableException>(
            () => service.SummarizeAsync(Note("아무 내용"), TestContext.Current.CancellationToken));

        Assert.Equal(AiAvailability.ModelNotInstalled, failure.Reason);
        Assert.DoesNotContain("/api/chat", daemon.RequestPaths);
    }

    [Fact]
    public void Settings_fall_back_to_the_defaults_when_a_value_is_unusable()
    {
        var options = OllamaOptions.FromSettings(new Dictionary<string, string>
        {
            [SettingKeys.AiEndpoint] = "not a url",
            [SettingKeys.AiModel] = "   ",
        });

        Assert.Equal(new Uri(OllamaOptions.DefaultEndpoint), options.Endpoint);
        Assert.Equal(OllamaOptions.DefaultModel, options.Model);
        Assert.True(options.Enabled);
    }

    [Fact]
    public void Settings_carry_a_chosen_endpoint_model_and_off_switch()
    {
        var options = OllamaOptions.FromSettings(new Dictionary<string, string>
        {
            [SettingKeys.AiEnabled] = "false",
            [SettingKeys.AiEndpoint] = "http://127.0.0.1:9999",
            [SettingKeys.AiModel] = "gemma4:e4b",
            [SettingKeys.AiEmbeddingModel] = "bge-m3",
        });

        Assert.False(options.Enabled);
        Assert.Equal(new Uri("http://127.0.0.1:9999"), options.Endpoint);
        Assert.Equal("gemma4:e4b", options.Model);
        Assert.Equal("bge-m3", options.EmbeddingModel);
    }

    internal static NoteContext Note(string content, string? selected = null) => new()
    {
        NoteId = Guid.NewGuid(),
        Content = content,
        SelectedText = selected,
    };
}
