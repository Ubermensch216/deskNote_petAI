using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

/// <summary>
/// What a new note starts as.
/// </summary>
/// <remarks>
/// Both keys were declared from the start and read by nothing, so every note arrived yellow and
/// medium-sized however many times its owner had changed it. These tests hold the plumbing that
/// finally connects them.
/// </remarks>
public class NoteDefaultsTests
{
    [Fact]
    public void A_fresh_install_gets_the_built_in_defaults()
    {
        var defaults = new NoteDefaults();

        Assert.Equal(NoteColors.Default, defaults.ColorKey);
        Assert.Equal(NoteSizePreset.Medium, defaults.Size);
    }

    [Fact]
    public async Task Saved_defaults_load_back_unchanged()
    {
        var store = new MemorySettingsStore();

        await new NoteDefaults { ColorKey = NoteColors.Blue, Size = NoteSizePreset.Large }
            .SaveAsync(store, TestContext.Current.CancellationToken);

        var loaded = await NoteDefaults.LoadAsync(store, TestContext.Current.CancellationToken);

        Assert.Equal(NoteColors.Blue, loaded.ColorKey);
        Assert.Equal(NoteSizePreset.Large, loaded.Size);
    }

    /// <summary>
    /// A colour this version does not know about must not stop a note from being created.
    /// </summary>
    /// <remarks>
    /// The value can outlive the colour that produced it — a palette change, or a hand-edited
    /// settings table. Creating notes is the one thing this app must never fail to do, so an
    /// unreadable preference degrades to the default rather than propagating.
    /// </remarks>
    [Theory]
    [InlineData("chartreuse")]
    [InlineData("")]
    [InlineData("YELLOW")]
    public void An_unknown_colour_falls_back_to_the_default(string stored)
    {
        var defaults = NoteDefaults.FromSettings(
            new Dictionary<string, string> { [SettingKeys.DefaultNoteColor] = stored });

        Assert.Equal(NoteColors.Default, defaults.ColorKey);
    }

    [Theory]
    [InlineData("Enormous")]
    [InlineData("")]
    [InlineData("7")]
    public void An_unreadable_size_falls_back_to_medium(string stored)
    {
        var defaults = NoteDefaults.FromSettings(
            new Dictionary<string, string> { [SettingKeys.DefaultNoteSize] = stored });

        Assert.Equal(NoteSizePreset.Medium, defaults.Size);
    }

    /// <summary>
    /// Custom is what a dragged note becomes, not something a new one can start as.
    /// </summary>
    /// <remarks>
    /// It carries no size of its own — the geometry does — so a new note created at "Custom" would
    /// have nothing to be sized by.
    /// </remarks>
    [Fact]
    public void Custom_is_not_a_size_a_new_note_can_start_at()
    {
        Assert.DoesNotContain(NoteSizePreset.Custom, NoteDefaults.SelectableSizes);

        var defaults = NoteDefaults.FromSettings(
            new Dictionary<string, string>
            {
                [SettingKeys.DefaultNoteSize] = NoteSizePreset.Custom.ToString(),
            });

        Assert.Equal(NoteSizePreset.Medium, defaults.Size);
    }

    [Fact]
    public async Task A_custom_size_is_refused_on_the_way_out_as_well()
    {
        var store = new MemorySettingsStore();

        await new NoteDefaults { Size = NoteSizePreset.Custom }
            .SaveAsync(store, TestContext.Current.CancellationToken);

        Assert.Equal(
            NoteSizePreset.Medium,
            (await NoteDefaults.LoadAsync(store, TestContext.Current.CancellationToken)).Size);
    }

    /// <summary>Every selectable size has a real window size behind it.</summary>
    [Fact]
    public void Each_selectable_size_has_usable_dimensions()
    {
        foreach (var size in NoteDefaults.SelectableSizes)
        {
            var (width, height) = NoteGeometry.SizeOf(size);

            Assert.True(width >= NoteGeometry.MinWidth, $"{size} is narrower than the minimum.");
            Assert.True(height >= NoteGeometry.MinHeight, $"{size} is shorter than the minimum.");
        }
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
