using System.Text.Json;

namespace DeskNote.Core.Services;

/// <summary>
/// Modifier keys in a hotkey.
/// </summary>
/// <remarks>
/// The values match Win32's <c>MOD_*</c> constants so registration needs no translation table,
/// and a wrong mapping cannot silently register a different combination than the one displayed.
/// </remarks>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>A key combination, as typed by the user and stored in settings.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>
    /// A global hotkey without modifiers would swallow that key everywhere in Windows, so one is
    /// required.
    /// </summary>
    public bool IsValid => Modifiers != HotkeyModifiers.None && !string.IsNullOrWhiteSpace(Key);

    public override string ToString()
    {
        var parts = new List<string>();

        // Fixed order so the same gesture always reads and stores the same way.
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(Key);

        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= HotkeyModifiers.Control; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "WIN" or "WINDOWS": modifiers |= HotkeyModifiers.Windows; break;
                default:
                    // Anything that is not a modifier is the key, and there can only be one.
                    if (key is not null)
                    {
                        return false;
                    }

                    key = NormalizeKey(raw);
                    break;
            }
        }

        if (key is null)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return gesture.IsValid;
    }

    /// <summary>
    /// Puts a key name into one canonical spelling.
    /// </summary>
    /// <remarks>
    /// Without this, "f4" and "F4" are different gestures: they compare unequal, so a stored
    /// binding stops matching the one on screen and two commands on the same physical key are not
    /// reported as conflicting.
    /// </remarks>
    private static string NormalizeKey(string raw)
    {
        if (raw.Length == 1)
        {
            return raw.ToUpperInvariant();
        }

        // Function keys read as F1..F24 rather than as a word.
        if ((raw[0] is 'f' or 'F') && raw.Length <= 3 && raw[1..].All(char.IsAsciiDigit))
        {
            return "F" + raw[1..];
        }

        return string.Concat(char.ToUpperInvariant(raw[0]), raw[1..].ToLowerInvariant());
    }
}

/// <summary>The commands a global hotkey can be bound to.</summary>
public static class HotkeyCommands
{
    public const string NewNote = "note.new";
    public const string SearchNotes = "note.search";
    public const string ToggleAlwaysOnTop = "note.toggleAlwaysOnTop";
    public const string AddReminder = "note.reminder";

    /// <summary>
    /// The AI command palette (report p5).
    /// </summary>
    /// <remarks>
    /// Bound only when local AI is switched on. Ctrl+Space is a combination other applications —
    /// IMEs above all — want too, so taking it away system-wide has to be paid for by the feature
    /// actually working; with AI off the command exists but stays unbound.
    /// </remarks>
    public const string AiPalette = "ai.palette";

    public static IReadOnlyList<string> All { get; } =
    [
        NewNote, SearchNotes, ToggleAlwaysOnTop, AddReminder, AiPalette,
    ];
}

/// <summary>
/// The user's hotkey assignments, with the report's defaults.
/// </summary>
/// <remarks>
/// Report p5 fixes the default gestures and requires that all of them be user-changeable. Stored
/// as one JSON blob under a single setting rather than a key per command, so a partial write can
/// never leave half the bindings from one version and half from another.
/// </remarks>
public sealed class HotkeyBindings
{
    private readonly Dictionary<string, HotkeyGesture> _bindings;

    private HotkeyBindings(Dictionary<string, HotkeyGesture> bindings) => _bindings = bindings;

    /// <param name="includeAiPalette">
    /// True once local AI is switched on. The palette's Ctrl+Space is left unbound otherwise, so an
    /// app with no model never takes that combination away from the IME.
    /// </param>
    public static HotkeyBindings Defaults(bool includeAiPalette = false)
    {
        var bindings = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal)
        {
            [HotkeyCommands.NewNote] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "N"),
            [HotkeyCommands.SearchNotes] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "F"),
            [HotkeyCommands.ToggleAlwaysOnTop] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "P"),
            [HotkeyCommands.AddReminder] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "R"),
        };

        if (includeAiPalette)
        {
            bindings[HotkeyCommands.AiPalette] = new(HotkeyModifiers.Control, "Space");
        }

        return new HotkeyBindings(bindings);
    }

    public IReadOnlyDictionary<string, HotkeyGesture> All => _bindings;

    public HotkeyGesture this[string command] => _bindings[command];

    public bool TryGet(string command, out HotkeyGesture gesture) => _bindings.TryGetValue(command, out gesture);

    /// <summary>Replaces one binding, returning a new set. Unknown commands are ignored.</summary>
    public HotkeyBindings With(string command, HotkeyGesture gesture)
    {
        if (!gesture.IsValid || !_bindings.ContainsKey(command))
        {
            return this;
        }

        var copy = new Dictionary<string, HotkeyGesture>(_bindings, StringComparer.Ordinal)
        {
            [command] = gesture,
        };

        return new HotkeyBindings(copy);
    }

    /// <summary>
    /// Commands sharing a gesture with another command.
    /// </summary>
    /// <remarks>
    /// Two commands on one combination means only the first registers and the second silently
    /// never fires, so this is surfaced rather than resolved automatically.
    /// </remarks>
    public IReadOnlyList<string> FindConflicts() =>
        _bindings
            .GroupBy(pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(pair => pair.Key))
            .OrderBy(command => command, StringComparer.Ordinal)
            .ToList();

    public string ToJson() =>
        JsonSerializer.Serialize(_bindings.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal));

    /// <summary>
    /// Reads stored bindings, falling back to the default for anything missing or unreadable.
    /// </summary>
    /// <remarks>
    /// A settings file written by a newer version, or corrupted, must not leave the app with no
    /// way to create a note. Every command always ends up bound to something.
    /// </remarks>
    public static HotkeyBindings FromJson(string? json, bool includeAiPalette = false)
    {
        var result = Defaults(includeAiPalette);

        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (stored is null)
            {
                return result;
            }

            foreach (var (command, text) in stored)
            {
                if (HotkeyGesture.TryParse(text, out var gesture))
                {
                    result = result.With(command, gesture);
                }
            }
        }
        catch (JsonException)
        {
            return Defaults(includeAiPalette);
        }

        return result;
    }
}
