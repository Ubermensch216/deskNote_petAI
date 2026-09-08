using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace DeskNote.Companion.Core;

/// <summary>
/// The lines a pet says to itself, one corpus per species per language.
/// </summary>
/// <remarks>
/// <para>
/// These live in embedded text files rather than in the UI resource files. There are hundreds of
/// them per species, and dropping several thousand entries into <c>Resources.resw</c> would bury
/// the couple of hundred strings that actually label buttons — the file people open to fix a typo
/// on a menu would become a joke book with a settings screen hidden inside it.
/// </para>
/// <para>
/// Each language is written rather than translated. A rabbit's joke about carrots being a
/// vegetable it is legally required to like does not survive being carried word for word into
/// Korean, so the two corpora are allowed to differ in content and in length; only the species
/// they belong to has to match.
/// </para>
/// </remarks>
public static class CompanionChatterCatalog
{
    /// <summary>The language used when the requested one ships no corpus for a species.</summary>
    public const string FallbackLanguage = "en";

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> Cache = new(
        StringComparer.OrdinalIgnoreCase);

    private static readonly Assembly Source = typeof(CompanionChatterCatalog).Assembly;

    /// <summary>
    /// Every line <paramref name="pet"/> can say in <paramref name="locale"/>.
    /// </summary>
    /// <remarks>
    /// An empty list is returned rather than an exception thrown when nothing can be read. A pet
    /// that has run out of things to say is a quiet pet; a pet that throws while thinking takes
    /// the desktop window down with it.
    /// </remarks>
    public static IReadOnlyList<string> Lines(CompanionPetKind pet, string? locale)
    {
        var requested = Normalize(locale);
        var lines = Read(pet, requested);

        return lines.Count > 0 || requested == FallbackLanguage
            ? lines
            : Read(pet, FallbackLanguage);
    }

    /// <summary>
    /// Maps anything Windows might report onto a language the corpus ships.
    /// </summary>
    /// <remarks>
    /// The match is on the language, not the region: a user set to <c>ko-KP</c> or plain <c>ko</c>
    /// wants the Korean pet, and falling those through to English because the tag is not spelled
    /// <c>ko-KR</c> would be a strange way to greet them. The corpus folders are named for the
    /// language alone for the same reason, and because a hyphen in a folder name reaches the
    /// manifest as an underscore - a detail no reader of this file should have to know.
    /// </remarks>
    private static string Normalize(string? locale) =>
        !string.IsNullOrWhiteSpace(locale)
        && locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
            ? "ko"
            : FallbackLanguage;

    private static IReadOnlyList<string> Read(CompanionPetKind pet, string locale) =>
        Cache.GetOrAdd($"{locale}/{pet.AssetKey()}", static key =>
        {
            var separator = key.IndexOf('/', StringComparison.Ordinal);
            var name = string.Concat(
                "DeskNote.Companion.Core.Chatter.",
                key[..separator],
                ".",
                key[(separator + 1)..],
                ".txt");

            using var stream = Source.GetManifestResourceStream(name);

            if (stream is null)
            {
                return [];
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();

            while (reader.ReadLine() is { } line)
            {
                var trimmed = line.Trim();

                // '#' opens a section heading. The corpus is written in themed runs — food, naps,
                // weather, the user's own work — and the headings are what stop a species drifting
                // into three hundred variations on being sleepy.
                if (trimmed.Length > 0 && trimmed[0] != '#')
                {
                    lines.Add(trimmed);
                }
            }

            return lines;
        });
}

/// <summary>
/// Hands out lines in a shuffled cycle, so every one is heard before any is heard twice.
/// </summary>
/// <remarks>
/// <para>
/// Picking uniformly at random is what makes a large corpus feel small. With three hundred lines
/// and an independent draw each time, the birthday problem says a repeat is more likely than not
/// after about twenty of them — the user meets the same joke twice in an afternoon and concludes
/// the pet has a handful of things to say, however many it actually has.
/// </para>
/// <para>
/// Dealing from a shuffled deck instead guarantees the full corpus before any repeat, and
/// reshuffling at the end keeps the order from being the same on every run. The seam between two
/// cycles is patched as well: without it, the last line of one deck can be dealt again as the
/// first of the next, which is the one repeat a user is certain to notice.
/// </para>
/// </remarks>
public sealed class CompanionChatterBag
{
    private readonly IReadOnlyList<string> _lines;
    private readonly Random _random;
    private readonly int[] _order;

    private int _position;

    public CompanionChatterBag(IReadOnlyList<string> lines, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _lines = lines;
        _random = random ?? Random.Shared;
        _order = new int[lines.Count];

        for (var i = 0; i < _order.Length; i++)
        {
            _order[i] = i;
        }

        Shuffle(previous: -1);
    }

    /// <summary>True when there is nothing to say.</summary>
    public bool IsEmpty => _lines.Count == 0;

    /// <summary>How many lines the pet knows.</summary>
    public int Count => _lines.Count;

    /// <summary>The next line in the current cycle, reshuffling once the deck runs out.</summary>
    public string Next()
    {
        if (_lines.Count == 0)
        {
            return string.Empty;
        }

        if (_position >= _order.Length)
        {
            Shuffle(previous: _order[^1]);
            _position = 0;
        }

        return _lines[_order[_position++]];
    }

    private void Shuffle(int previous)
    {
        for (var i = _order.Length - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }

        // Two decks in a row can otherwise end and start on the same line. Swapping the head with
        // some other card costs nothing and removes the only repeat the cycle cannot rule out.
        if (_order.Length > 1 && _order[0] == previous)
        {
            var swap = 1 + _random.Next(_order.Length - 1);
            (_order[0], _order[swap]) = (_order[swap], _order[0]);
        }
    }
}
