using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;

namespace DeskNote.Companion.Core.Tests;

public sealed class CompanionSettingsTests
{
    [Fact]
    public async Task Missing_values_keep_the_feature_opted_out()
    {
        var settings = await CompanionSettings.LoadAsync(
            new MemorySettingsStore(),
            TestContext.Current.CancellationToken);

        Assert.False(settings.Enabled);
        Assert.True(settings.ProactiveEnabled);
        Assert.False(settings.AlwaysVisible);
        Assert.False(settings.ReduceMotion);
        Assert.Equal(CompanionPetKind.Rabbit, settings.SelectedPet);
        Assert.Equal(CompanionSettings.DefaultPetName, settings.PetName);
        Assert.Equal(CompanionPetSize.Large, settings.PetSize);
        Assert.Null(settings.PetPositionX);
        Assert.Null(settings.PetPositionY);
        Assert.False(settings.DragonUnlocked);
        Assert.Equal(new TimeOnly(20, 0), settings.QuietStart);
        Assert.Equal(new TimeOnly(9, 0), settings.QuietEnd);
        Assert.Equal(CompanionSettings.CurrentRuleVersion, settings.RuleVersion);
    }

    [Fact]
    public async Task Malformed_values_fall_back_without_blocking_startup()
    {
        var store = new MemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.CompanionEnabled] = "yes",
            [SettingKeys.CompanionProactiveEnabled] = "sometimes",
            [SettingKeys.CompanionQuietHoursStart] = "25:90",
            [SettingKeys.CompanionQuietHoursEnd] = "tomorrow",
            [SettingKeys.CompanionRuleVersion] = "0",
            [SettingKeys.CompanionPetSize] = "huge",
            [SettingKeys.CompanionPetPositionX] = "left",
        });

        var settings = await CompanionSettings.LoadAsync(store, TestContext.Current.CancellationToken);

        Assert.False(settings.Enabled);
        Assert.True(settings.ProactiveEnabled);
        Assert.Equal(CompanionSettings.DefaultQuietStart, settings.QuietStart);
        Assert.Equal(CompanionSettings.DefaultQuietEnd, settings.QuietEnd);
        Assert.Equal(CompanionSettings.CurrentRuleVersion, settings.RuleVersion);
        Assert.Equal(CompanionPetSize.Large, settings.PetSize);
        Assert.Null(settings.PetPositionX);
    }

    [Theory]
    [InlineData(19, 59, false)]
    [InlineData(20, 0, true)]
    [InlineData(23, 30, true)]
    [InlineData(0, 0, true)]
    [InlineData(8, 59, true)]
    [InlineData(9, 0, false)]
    public void Quiet_hours_span_midnight(int hour, int minute, bool expected)
    {
        var settings = new CompanionSettings();

        Assert.Equal(expected, settings.IsQuietAt(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void Equal_quiet_boundaries_mean_quiet_hours_are_disabled()
    {
        var settings = new CompanionSettings
        {
            QuietStart = new TimeOnly(9, 0),
            QuietEnd = new TimeOnly(9, 0),
        };

        Assert.False(settings.IsQuietAt(new TimeOnly(9, 0)));
        Assert.False(settings.IsQuietAt(new TimeOnly(23, 0)));
    }

    [Fact]
    public void Proactive_requires_both_opt_ins_and_a_non_quiet_time()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.FromHours(9));

        Assert.False(new CompanionSettings().AllowsProactiveAt(now));
        Assert.False(new CompanionSettings { Enabled = true, ProactiveEnabled = false }.AllowsProactiveAt(now));
        Assert.True(new CompanionSettings { Enabled = true }.AllowsProactiveAt(now));
    }

    [Fact]
    public async Task Save_round_trips_every_value_in_invariant_form()
    {
        var store = new MemorySettingsStore();
        var expected = new CompanionSettings
        {
            Enabled = true,
            ProactiveEnabled = false,
            AlwaysVisible = true,
            ReduceMotion = true,
            SelectedPet = CompanionPetKind.Otter,
            PetName = "  Dori  ",
            PetSize = CompanionPetSize.Small,
            QuietStart = new TimeOnly(21, 30),
            QuietEnd = new TimeOnly(7, 15),
            RuleVersion = 3,
        };

        await expected.SaveAsync(store, TestContext.Current.CancellationToken);
        var actual = await CompanionSettings.LoadAsync(store, TestContext.Current.CancellationToken);

        Assert.Equal(expected with { PetName = "Dori" }, actual);
    }

    [Theory]
    [InlineData(null, "Mori")]
    [InlineData("   ", "Mori")]
    [InlineData("  보리  ", "보리")]
    [InlineData("1234567890123456789012345", "12345678901234567890")]
    public void Pet_name_is_normalized(string? value, string expected)
    {
        Assert.Equal(expected, CompanionSettings.NormalizePetName(value));
    }

    [Fact]
    public async Task Locked_dragon_selection_falls_back_to_rabbit()
    {
        var store = new MemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.CompanionSelectedPet] = CompanionPetKind.Dragon.ToString(),
            [SettingKeys.CompanionDragonUnlocked] = "false",
        });

        var actual = await CompanionSettings.LoadAsync(store, TestContext.Current.CancellationToken);

        Assert.Equal(CompanionPetKind.Rabbit, actual.SelectedPet);
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values;

        public MemorySettingsStore(Dictionary<string, string>? values = null)
        {
            _values = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

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
