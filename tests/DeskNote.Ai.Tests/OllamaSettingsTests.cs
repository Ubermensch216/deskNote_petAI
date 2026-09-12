using DeskNote.Core.Abstractions;

namespace DeskNote.Ai.Tests;

/// <summary>
/// The values the settings screen writes, and the rules it has to share with the loader.
/// </summary>
/// <remarks>
/// These settings were unreachable from the app until now — changing a model meant a third-party
/// SQLite editor — so the screen and the loader have never had to agree before. Where they
/// disagree, a value the screen accepted is dropped on the next launch and the AI quietly goes
/// back to a default with nothing said.
/// </remarks>
public class OllamaSettingsTests
{
    private static OllamaOptions Sample => new()
    {
        Enabled = true,
        Endpoint = new Uri("http://127.0.0.1:9999"),
        Model = "llama3.2:3b",
        EmbeddingModel = "nomic-embed-text:latest",
        KeepAlive = TimeSpan.FromMinutes(30),
    };

    [Fact]
    public async Task Saved_settings_load_back_unchanged()
    {
        var store = new MemorySettingsStore();

        await Sample.SaveAsync(store, TestContext.Current.CancellationToken);
        var loaded = await OllamaOptions.LoadAsync(store, TestContext.Current.CancellationToken);

        Assert.True(loaded.Enabled);
        Assert.Equal(new Uri("http://127.0.0.1:9999"), loaded.Endpoint);
        Assert.Equal("llama3.2:3b", loaded.Model);
        Assert.Equal("nomic-embed-text:latest", loaded.EmbeddingModel);
        Assert.Equal(TimeSpan.FromMinutes(30), loaded.KeepAlive);
    }

    /// <summary>Turning AI off has to survive a restart, or the switch does not mean anything.</summary>
    [Fact]
    public async Task Turning_ai_off_survives_a_reload()
    {
        var store = new MemorySettingsStore();

        await (Sample with { Enabled = false }).SaveAsync(store, TestContext.Current.CancellationToken);

        Assert.False((await OllamaOptions.LoadAsync(store, TestContext.Current.CancellationToken)).Enabled);
    }

    /// <summary>
    /// Tuning values are not the user's to pin.
    /// </summary>
    /// <remarks>
    /// Writing the timeouts and the temperature would freeze today's numbers into every install,
    /// so a later change to them would reach nobody who had ever opened the settings screen.
    /// </remarks>
    [Fact]
    public async Task Saving_does_not_persist_the_tuning_values()
    {
        var store = new MemorySettingsStore();

        await Sample.SaveAsync(store, TestContext.Current.CancellationToken);
        var stored = await store.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, stored.Count);
        Assert.All(stored.Keys, key => Assert.StartsWith("ai.", key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_keep_alive_beyond_the_limit_is_clamped_before_it_is_written()
    {
        var store = new MemorySettingsStore();

        await (Sample with { KeepAlive = TimeSpan.FromHours(9) })
            .SaveAsync(store, TestContext.Current.CancellationToken);

        Assert.Equal(
            TimeSpan.FromMinutes(OllamaOptions.MaximumKeepAliveMinutes),
            (await OllamaOptions.LoadAsync(store, TestContext.Current.CancellationToken)).KeepAlive);
    }

    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("https://ollama.example.com")]
    [InlineData("  http://127.0.0.1:11434  ")]
    public void An_http_address_is_accepted(string value) =>
        Assert.True(OllamaOptions.TryParseEndpoint(value, out _));

    /// <summary>
    /// Rejected here means rejected by the loader too.
    /// </summary>
    /// <remarks>
    /// Each of these is dropped by <see cref="OllamaOptions.FromSettings"/>, which falls back to
    /// localhost. A settings screen that stored one would show the user their own value on the
    /// next visit while the app talked to somewhere else entirely.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("localhost:11434")]
    [InlineData("ftp://localhost:11434")]
    [InlineData("file:///C:/ollama")]
    [InlineData("not a url")]
    public void Anything_the_loader_would_drop_is_refused(string? value) =>
        Assert.False(OllamaOptions.TryParseEndpoint(value, out _));

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://192.168.1.40:11434", false)]
    [InlineData("https://ollama.example.com", false)]
    public void An_endpoint_knows_whether_it_keeps_note_text_on_this_machine(string value, bool local) =>
        Assert.Equal(local, (Sample with { Endpoint = new Uri(value) }).IsLocal);

    /// <summary>
    /// Saving a pet name must not cancel a summary that is still generating.
    /// </summary>
    /// <remarks>
    /// One Save writes all three pages, so most saves leave the AI untouched. Rebuilding the
    /// client disposes the one in use, which fails whatever it was in the middle of — so the
    /// rebuild is gated on the settings that actually change how it talks to the daemon.
    /// </remarks>
    [Fact]
    public void Only_the_settings_that_reach_the_daemon_force_a_rebuild()
    {
        var original = Sample;

        Assert.True(original.TalksTheSameWayAs(original with { EmbeddingModel = "other:latest" }));
        Assert.True(original.TalksTheSameWayAs(original with { Temperature = 0.9 }));

        Assert.False(original.TalksTheSameWayAs(original with { Enabled = false }));
        Assert.False(original.TalksTheSameWayAs(original with { Model = "gemma4:e2b" }));
        Assert.False(original.TalksTheSameWayAs(original with { Endpoint = new Uri("http://localhost:1") }));
        Assert.False(original.TalksTheSameWayAs(original with { KeepAlive = TimeSpan.Zero }));
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(_values);
    }
}
