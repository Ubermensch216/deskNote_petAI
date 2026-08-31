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

    /// <summary>A line that opens as a comment, which may quote the report in Korean.</summary>
    [GeneratedRegex(@"^\s*(//|///|\*|/\*|<!--)", RegexOptions.CultureInvariant)]
    private static partial Regex CommentLine { get; }

    /// <summary>
    /// Whether the line leaves an unterminated block comment open behind it.
    /// </summary>
    /// <remarks>
    /// Deliberately crude: it counts openers and closers rather than parsing, because the only
    /// thing that has to be right is "is the next line still inside a comment". A comment marker
    /// inside a string literal would fool it, and a Korean UI string on that same line would then
    /// go unreported — a narrower hole than the one this closes, and one no line in this codebase
    /// has ever had.
    /// </remarks>
    private static bool StillInsideComment(string line, bool alreadyInside)
    {
        var index = 0;
        var inside = alreadyInside;

        while (index < line.Length)
        {
            if (!inside)
            {
                var open = NextOpener(line, index);
                if (open < 0)
                {
                    return false;
                }

                inside = true;
                index = open + 2;
                continue;
            }

            var close = NextCloser(line, index);
            if (close < 0)
            {
                return true;
            }

            inside = false;
            index = close + 2;
        }

        return inside;
    }

    private static int NextOpener(string line, int from) =>
        Earliest(line.IndexOf("<!--", from, StringComparison.Ordinal),
                 line.IndexOf("/*", from, StringComparison.Ordinal));

    private static int NextCloser(string line, int from) =>
        Earliest(line.IndexOf("-->", from, StringComparison.Ordinal),
                 line.IndexOf("*/", from, StringComparison.Ordinal));

    private static int Earliest(int left, int right) =>
        left < 0 ? right : right < 0 ? left : Math.Min(left, right);

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
            var inBlockComment = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var wasInBlockComment = inBlockComment;

                inBlockComment = StillInsideComment(line, inBlockComment);

                // A comment that runs over several lines is a comment on every one of them. The
                // first line opens with a marker and the rest do not, so checking only the start
                // of each line reported the middle of every explanatory block as hardcoded UI —
                // which taught the wrong lesson: write the comment in English, or write less.
                if (wasInBlockComment || inBlockComment || CommentLine.IsMatch(line))
                {
                    continue;
                }

                if (!Hangul.IsMatch(line))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(path)}:{i + 1}  {line.Trim()}");
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
