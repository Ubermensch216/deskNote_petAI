using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>
/// What a new note looks like before the user touches it.
/// </summary>
/// <remarks>
/// <para>
/// The settings keys for these existed from the start and nothing ever read them, so every note
/// arrived yellow and medium-sized however many times the user had changed it afterwards. Colour
/// and size are the two things a note is adjusted for immediately and constantly, which makes them
/// the two worth remembering.
/// </para>
/// <para>
/// A stored value that no longer means anything falls back to the built-in default rather than
/// failing: a colour removed in a future version, or a size preset written by hand, must not stop
/// a note from being created.
/// </para>
/// </remarks>
public sealed record NoteDefaults
{
    public string ColorKey { get; init; } = NoteColors.Default;

    /// <summary>
    /// Size of a new note.
    /// </summary>
    /// <remarks>
    /// <see cref="NoteSizePreset.Custom"/> is what a dragged note becomes, not something a new one
    /// can start as, so it is rejected on the way in and out.
    /// </remarks>
    public NoteSizePreset Size { get; init; } = NoteSizePreset.Medium;

    /// <summary>The presets a new note may be created at, in the order a settings screen lists them.</summary>
    public static IReadOnlyList<NoteSizePreset> SelectableSizes { get; } =
        [NoteSizePreset.Small, NoteSizePreset.Medium, NoteSizePreset.Large];

    public static async Task<NoteDefaults> LoadAsync(
        ISettingsStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var stored = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return FromSettings(stored);
    }

    public static NoteDefaults FromSettings(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var defaults = new NoteDefaults();

        if (settings.TryGetValue(SettingKeys.DefaultNoteColor, out var color))
        {
            defaults = defaults with { ColorKey = NoteColors.Normalize(color) };
        }

        if (settings.TryGetValue(SettingKeys.DefaultNoteSize, out var size))
        {
            defaults = defaults with { Size = NormalizeSize(size) };
        }

        return defaults;
    }

    public async Task SaveAsync(ISettingsStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        await store.SetAsync(SettingKeys.DefaultNoteColor, NoteColors.Normalize(ColorKey), cancellationToken)
            .ConfigureAwait(false);
        await store.SetAsync(SettingKeys.DefaultNoteSize, NormalizeSize(Size).ToString(), cancellationToken)
            .ConfigureAwait(false);
    }

    private static NoteSizePreset NormalizeSize(NoteSizePreset size) =>
        SelectableSizes.Contains(size) ? size : NoteSizePreset.Medium;

    private static NoteSizePreset NormalizeSize(string? value) =>
        Enum.TryParse<NoteSizePreset>(value, ignoreCase: true, out var parsed)
            ? NormalizeSize(parsed)
            : NoteSizePreset.Medium;
}
