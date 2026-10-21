using System.Globalization;
using DeskNote.Core.Abstractions;

namespace DeskNote.Companion.Core;

/// <summary>Typed, validated settings that gate every companion subsystem.</summary>
public sealed record CompanionSettings
{
    public const int CurrentRuleVersion = CompanionBalanceV2.RuleVersion;
    public const int MaximumPetNameLength = 20;
    public const string DefaultPetName = "Mori";

    public static readonly TimeOnly DefaultQuietStart = new(20, 0);
    public static readonly TimeOnly DefaultQuietEnd = new(9, 0);

    /// <summary>Opt-in flag. Missing or malformed values are deliberately off.</summary>
    public bool Enabled { get; init; }

    public bool ProactiveEnabled { get; init; } = true;

    public bool AlwaysVisible { get; init; }

    public bool ReduceMotion { get; init; }

    /// <summary>
    /// Whether the pet may climb the sides of the screen and crawl along the top of it, rather than
    /// walking the floor and nothing else. Moving at all is <see cref="ReduceMotion"/>'s say.
    /// </summary>
    public bool RoamScreen { get; init; } = true;

    /// <summary>
    /// Whether the pet climbs the open windows of other applications and walks along their tops.
    /// Off by default: a pet that stands on the window being worked in is the livelier behaviour,
    /// and is for whoever asks for it.
    /// </summary>
    public bool ClimbWindows { get; init; }

    /// <summary>Whether a sitting pet turns to look at the pointer. Costs nothing, so on.</summary>
    public bool WatchCursor { get; init; } = true;

    /// <summary>Whether the pet walks over to the pointer and sits beside it.</summary>
    public bool FollowCursor { get; init; }

    /// <summary>
    /// Whether the pet slides out of the way when the pointer gets close. Off by default, and a
    /// pet that does it cannot also follow: a pet that runs from the pointer and walks to it at
    /// the same time is just in the way. Dodging wins if both are somehow stored.
    /// </summary>
    public bool DodgeCursor { get; init; }

    /// <summary>What the pet does about the pointer: one answer out of the three switches.</summary>
    public PetCursorMode CursorMode =>
        DodgeCursor ? PetCursorMode.Dodge
        : FollowCursor ? PetCursorMode.Follow
        : WatchCursor ? PetCursorMode.Watch
        : PetCursorMode.None;

    public CompanionPetKind SelectedPet { get; init; } = CompanionPetKind.Rabbit;

    public string PetName { get; init; } = DefaultPetName;

    public CompanionPetSize PetSize { get; init; } = CompanionPetSize.Large;

    public int? PetPositionX { get; init; }

    public int? PetPositionY { get; init; }

    public bool DragonUnlocked { get; init; }

    public TimeOnly QuietStart { get; init; } = DefaultQuietStart;

    public TimeOnly QuietEnd { get; init; } = DefaultQuietEnd;

    public int RuleVersion { get; init; } = CurrentRuleVersion;

    public static async Task<CompanionSettings> LoadAsync(
        ISettingsStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var values = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var dragonUnlocked = Boolean(values, SettingKeys.CompanionDragonUnlocked, fallback: false);
        var selectedPet = Pet(values, SettingKeys.CompanionSelectedPet);
        if (selectedPet == CompanionPetKind.Dragon && !dragonUnlocked)
        {
            selectedPet = CompanionPetKind.Rabbit;
        }

        return new CompanionSettings
        {
            Enabled = Boolean(values, SettingKeys.CompanionEnabled, fallback: false),
            ProactiveEnabled = Boolean(values, SettingKeys.CompanionProactiveEnabled, fallback: true),
            AlwaysVisible = Boolean(values, SettingKeys.CompanionAlwaysVisible, fallback: false),
            ReduceMotion = Boolean(values, SettingKeys.CompanionReduceMotion, fallback: false),
            RoamScreen = Boolean(values, SettingKeys.CompanionRoamScreen, fallback: true),
            ClimbWindows = Boolean(values, SettingKeys.CompanionClimbWindows, fallback: false),
            WatchCursor = Boolean(values, SettingKeys.CompanionWatchCursor, fallback: true),
            FollowCursor = Boolean(values, SettingKeys.CompanionFollowCursor, fallback: false),
            DodgeCursor = Boolean(values, SettingKeys.CompanionDodgeCursor, fallback: false),
            SelectedPet = selectedPet,
            PetName = NormalizePetName(values.GetValueOrDefault(SettingKeys.CompanionPetName)),
            PetSize = ParsePetSize(values, SettingKeys.CompanionPetSize),
            PetPositionX = Integer(values, SettingKeys.CompanionPetPositionX),
            PetPositionY = Integer(values, SettingKeys.CompanionPetPositionY),
            DragonUnlocked = dragonUnlocked,
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
        await store.SetAsync(SettingKeys.CompanionRoamScreen, Lower(RoamScreen), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionClimbWindows, Lower(ClimbWindows), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionWatchCursor, Lower(WatchCursor), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionFollowCursor, Lower(FollowCursor), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionDodgeCursor, Lower(DodgeCursor), cancellationToken)
            .ConfigureAwait(false);
        var selectedPet = SelectedPet == CompanionPetKind.Dragon && !DragonUnlocked
            ? CompanionPetKind.Rabbit
            : SelectedPet;
        await store.SetAsync(SettingKeys.CompanionSelectedPet, selectedPet.ToString(), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(
                SettingKeys.CompanionPetName,
                NormalizePetName(PetName),
                cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.CompanionPetSize, PetSize.ToString(), cancellationToken)
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

    public static string NormalizePetName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultPetName;
        }

        return normalized.Length <= MaximumPetNameLength
            ? normalized
            : normalized[..MaximumPetNameLength];
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

    private static CompanionPetKind Pet(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value)
        && Enum.TryParse<CompanionPetKind>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : CompanionPetKind.Rabbit;

    private static CompanionPetSize ParsePetSize(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value)
        && Enum.TryParse<CompanionPetSize>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : CompanionPetSize.Large;

    private static int? Integer(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string Lower(bool value) => value ? "true" : "false";
}
