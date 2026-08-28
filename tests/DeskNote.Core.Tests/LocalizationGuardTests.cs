using System.Text.RegularExpressions;

namespace DeskNote.Core.Tests;

/// <summary>
/// Guards the i18n boundary by reading the source itself.
/// </summary>
/// <remarks>
/// The plan calls for "하드코딩 문자열 0개" as a checked condition. A convention nobody checks is a
/// convention that decays: the cost of adding one more Korean literal to a XAML file is zero, and
/// the damage only shows up when someone runs the app in English. These tests make that cost
/// visible at build time instead.
/// </remarks>
public partial class LocalizationGuardTests
{
    [GeneratedRegex(@"[가-힣]", RegexOptions.CultureInvariant)]
    private static partial Regex Hangul { get; }

    /// <summary>Comment lines, which may quote the report in Korean, and are never shown to a user.</summary>
    [GeneratedRegex(@"^\s*(//|///|\*|/\*|<!--)", RegexOptions.CultureInvariant)]
    private static partial Regex CommentLine { get; }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskNote.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory;
    }

    private static IEnumerable<string> UiSourceFiles()
    {
        var app = Path.Combine(RepositoryRoot().FullName, "src", "DeskNote.App");

        return Directory
            .EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}Strings{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public void No_user_facing_korean_text_is_hardcoded_in_the_app()
    {
        var offenders = new List<string>();

        foreach (var path in UiSourceFiles())
        {
            var lines = File.ReadAllLines(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (CommentLine.IsMatch(lines[i]) || !Hangul.IsMatch(lines[i]))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(path)}:{i + 1}  {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "UI text must come from Strings/*/Resources.resw:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// A key present in one language and missing in the other renders as the raw key at runtime,
    /// which is the failure mode this catches before a user sees it.
    /// </summary>
    [Fact]
    public void Both_languages_define_exactly_the_same_keys()
    {
        var korean = ResourceKeys("ko-KR");
        var english = ResourceKeys("en-US");

        Assert.NotEmpty(korean);
        Assert.Equal(english.OrderBy(k => k, StringComparer.Ordinal), korean.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void No_resource_value_is_empty()
    {
        foreach (var locale in new[] { "ko-KR", "en-US" })
        {
            var document = System.Xml.Linq.XDocument.Load(ResourcePath(locale));

            foreach (var entry in document.Root!.Elements("data"))
            {
                var name = entry.Attribute("name")!.Value;
                Assert.False(
                    string.IsNullOrWhiteSpace(entry.Element("value")?.Value),
                    $"{locale}/{name} has no text.");
            }
        }
    }

    /// <summary>
    /// Format placeholders have to match across languages, or a translated string throws or drops
    /// the value it was meant to show.
    /// </summary>
    [Fact]
    public void Format_placeholders_match_across_languages()
    {
        var korean = ResourceValues("ko-KR");
        var english = ResourceValues("en-US");

        foreach (var (key, koreanValue) in korean)
        {
            Assert.Equal(Placeholders(english[key]), Placeholders(koreanValue));
        }
    }

    private static string ResourcePath(string locale) =>
        Path.Combine(RepositoryRoot().FullName, "src", "DeskNote.App", "Strings", locale, "Resources.resw");

    private static IReadOnlyList<string> ResourceKeys(string locale) =>
        ResourceValues(locale).Keys.ToList();

    private static Dictionary<string, string> ResourceValues(string locale) =>
        System.Xml.Linq.XDocument.Load(ResourcePath(locale))
            .Root!
            .Elements("data")
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => e.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private static IReadOnlyList<string> Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+\}", RegexOptions.CultureInvariant)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}
