using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskNote.Companion.Core;

/// <summary>Finds only unchecked-to-checked transitions and gives each text occurrence a stable key.</summary>
public static partial class ChecklistCompletionDetector
{
    [GeneratedRegex(@"^\s*[-*+]\s*\[(?<mark>[ xX])\]\s*(?<text>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex ChecklistLine { get; }

    public static IReadOnlyList<string> FindCompletedKeys(
        Guid noteId,
        string? previousContent,
        string currentContent)
    {
        var before = Parse(previousContent ?? string.Empty);
        var after = Parse(currentContent);
        var completed = new List<string>();

        foreach (var item in after.Where(item => item.Checked))
        {
            if (before.Any(old => old.Key == item.Key && !old.Checked))
            {
                completed.Add(Hash($"{noteId:N}|{item.Key}"));
            }
        }

        return completed;
    }

    private static IReadOnlyList<Item> Parse(string content)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var items = new List<Item>();

        foreach (Match match in ChecklistLine.Matches(content.ReplaceLineEndings("\n")))
        {
            var normalized = string.Join(' ', match.Groups["text"].Value
                .Trim()
                .ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            occurrences.TryGetValue(normalized, out var ordinal);
            ordinal++;
            occurrences[normalized] = ordinal;
            items.Add(new Item($"{normalized}|{ordinal}", !string.IsNullOrWhiteSpace(match.Groups["mark"].Value)));
        }

        return items;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Item(string Key, bool Checked);
}
