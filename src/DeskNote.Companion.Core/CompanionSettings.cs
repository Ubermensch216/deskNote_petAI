using System.Globalization;
using DeskNote.Core.Abstractions;

namespace DeskNote.Companion.Core;

/// <summary>Typed, validated settings that gate every companion subsystem.</summary>
public sealed record CompanionSettings
{
    public const int CurrentRuleVersion = 1;

    public static readonly TimeOnly DefaultQuietStart = new(20, 0);
    public static readonly TimeOnly DefaultQuietEnd = new(9, 0);

    /// <summary>Opt-in flag. Missing or malformed values are deliberately off.</summary>
    public bool Enabled { get; init; }

    public bool ProactiveEnabled { get; init; } = true;

    public bool AlwaysVisible { get; init; }

    public bool ReduceMotion { get; init; }

    public TimeOnly QuietStart { get; init; } = DefaultQuietStart;

    public TimeOnly QuietEnd { get; init; } = DefaultQuietEnd;

    public int RuleVersion { get; init; } = CurrentRuleVersion;

    public static async Task<CompanionSettings> LoadAsync(
        ISettingsStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var values = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return new CompanionSettings
        {
            Enabled = Boolean(values, SettingKeys.CompanionEnabled, fallback: false),
            ProactiveEnabled = Boolean(values, SettingKeys.CompanionProactiveEnabled, fallback: true),
            AlwaysVisible = Boolean(values, SettingKeys.CompanionAlwaysVisible, fallback: false),
            ReduceMotion = Boolean(values, SettingKeys.CompanionReduceMotion, fallback: false),
            QuietStart = Time(values, SettingKeys.CompanionQuietHoursStart, DefaultQuietStart),
            QuietEnd = Time(values, SettingKeys.CompanionQuietHoursEnd, DefaultQuietEnd),
            RuleVersion = PositiveInteger(
                values,
                SettingKeys.CompanionRuleVersion,
                CurrentRuleVersion),
        };
    }

    public async Task SaveAsync(
        ISettingsStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        await store.SetAsync(SettingKeys.CompanionEnabled, Lower(Enabled), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionProactiveEnabled, Lower(ProactiveEnabled), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionAlwaysVisible, Lower(AlwaysVisible), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionReduceMotion, Lower(ReduceMotion), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(
                SettingKeys.CompanionQuietHoursStart,
                QuietStart.ToString("HH:mm", CultureInfo.InvariantCulture),
                cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(
                SettingKeys.CompanionQuietHoursEnd,
                QuietEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
                cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(
                SettingKeys.CompanionRuleVersion,
                Math.Max(1, RuleVersion).ToString(CultureInfo.InvariantCulture),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>True only when enabled proactive work is permitted at this local time.</summary>
    public bool AllowsProactiveAt(DateTimeOffset localNow) =>
        Enabled && ProactiveEnabled && !IsQuietAt(TimeOnly.FromDateTime(localNow.DateTime));

    public bool IsQuietAt(TimeOnly localTime)
    {
        if (QuietStart == QuietEnd)
        {
            return false;
        }

        return QuietStart < QuietEnd
            ? localTime >= QuietStart && localTime < QuietEnd
            : localTime >= QuietStart || localTime < QuietEnd;
    }

    private static bool Boolean(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool fallback) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;

    private static TimeOnly Time(
        IReadOnlyDictionary<string, string> values,
        string key,
        TimeOnly fallback) =>
        values.TryGetValue(key, out var value)
        && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;

    private static int PositiveInteger(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback) =>
        values.TryGetValue(key, out var value)
        && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
            ? parsed
            : fallback;

    private static string Lower(bool value) => value ? "true" : "false";
}
