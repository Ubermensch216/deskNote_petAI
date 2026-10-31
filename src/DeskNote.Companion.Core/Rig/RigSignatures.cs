using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DeskNote.Companion.Core;

/// <summary>How often a pet breaks into one of its signature moves when it sits down to rest.</summary>
public enum SignatureFrequency
{
    Off = 0,
    Rare = 1,
    Normal = 2,
    Often = 3,
}

public static class SignatureFrequencyExtensions
{
    /// <summary>The chance that a rest turns into a signature move.</summary>
    public static double Chance(this SignatureFrequency frequency) => frequency switch
    {
        SignatureFrequency.Off => 0,
        SignatureFrequency.Rare => 0.08,
        SignatureFrequency.Often => 0.38,
        _ => 0.20,
    };
}

/// <summary>One line the pet shouts at a moment in a signature move.</summary>
public readonly record struct SignatureShout(double At, string Text);

/// <summary>What the user is told about a signature move, in one language.</summary>
public sealed record SignatureText(string Name, string Description, IReadOnlyList<SignatureShout> Shouts)
{
    /// <summary>The shout in force <paramref name="seconds"/> into the move: the last one whose time has come.</summary>
    public string ShoutAt(double seconds)
    {
        var current = string.Empty;

        foreach (var shout in Shouts)
        {
            if (seconds >= shout.At)
            {
                current = shout.Text;
            }
        }

        return current;
    }

    /// <summary>The bubble: the move's name between stars, then what is being shouted right now.</summary>
    public string Bubble(double seconds)
    {
        var shout = ShoutAt(seconds);
        return shout.Length == 0
            ? $"★ {Name} ★"
            : $"★ {Name} ★\n{shout}";
    }
}

/// <summary>
/// The signature moves: three for each of the five characters.
/// </summary>
/// <remarks>
/// <para>
/// A signature move is not an animation the pet picks like a stretch. It has a name, a shout that
/// is timed to the pose, and a gold bubble and gold stars so that nobody mistakes it for ordinary
/// fidgeting; it is the one thing a pet does that the user can ask to see by name.
/// </para>
/// <para>
/// The words live in embedded text files, like the pet's small talk, and for the same reason:
/// the timed shouts do not fit a resource table, and a pet that went mute because a file was
/// left out of a publish is a bug nobody reports.
/// </para>
/// </remarks>
public static class RigSignatures
{
    public const int PerCharacter = 3;

    /// <summary>The language used when the requested one ships nothing.</summary>
    public const string FallbackLanguage = "en";

    private static readonly double[][] Durations =
    [
        [7.8, 5.6, 8.0],
        [8.0, 7.5, 9.0],
        [8.0, 7.0, 8.5],
        [6.5, 8.0, 7.0],
        [7.8, 6.5, 9.0],
    ];

    private static readonly string[] Keys = ["goku", "kid", "roshi", "vegeta", "piccolo"];

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, SignatureText>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Assembly Source = typeof(RigSignatures).Assembly;

    /// <summary>How long the move runs, in seconds.</summary>
    public static double Duration(RigCharacter character, int index) =>
        Durations[(int)character][Math.Clamp(index, 0, PerCharacter - 1)];

    /// <summary>The name, description and shouts of one move in one language.</summary>
    public static SignatureText Text(RigCharacter character, int index, string? locale)
    {
        var language = Normalize(locale);
        var key = $"{Keys[(int)character]}.{Math.Clamp(index, 0, PerCharacter - 1)}";

        if (Read(language).TryGetValue(key, out var text))
        {
            return text;
        }

        return Read(FallbackLanguage).TryGetValue(key, out var fallback)
            ? fallback
            : new SignatureText(key, string.Empty, []);
    }

    private static string Normalize(string? locale) =>
        !string.IsNullOrWhiteSpace(locale) && locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
            ? "ko"
            : FallbackLanguage;

    private static IReadOnlyDictionary<string, SignatureText> Read(string language) =>
        Cache.GetOrAdd(language, static code =>
        {
            using var stream = Source.GetManifestResourceStream($"DeskNote.Companion.Core.Rig.Signatures.{code}.txt");
            if (stream is null)
            {
                return new Dictionary<string, SignatureText>();
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var names = new Dictionary<string, string>();
            var descriptions = new Dictionary<string, string>();
            var shouts = new Dictionary<string, List<SignatureShout>>();

            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var parts = line.Split('|', 4);
                if (parts.Length < 3)
                {
                    continue;
                }

                var id = parts[0].Trim();
                switch (parts[1].Trim())
                {
                    case "name":
                        names[id] = parts[2].Trim();
                        break;

                    case "desc":
                        descriptions[id] = parts[2].Trim();
                        break;

                    case "shout" when parts.Length == 4
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at):
                        if (!shouts.TryGetValue(id, out var list))
                        {
                            shouts[id] = list = [];
                        }

                        list.Add(new SignatureShout(at, parts[3].Trim()));
                        break;
                }
            }

            var result = new Dictionary<string, SignatureText>();
            foreach (var (id, name) in names)
            {
                result[id] = new SignatureText(
                    name,
                    descriptions.GetValueOrDefault(id, string.Empty),
                    shouts.TryGetValue(id, out var lines) ? lines.OrderBy(s => s.At).ToList() : []);
            }

            return result;
        });
}
