namespace DeskNote.Companion.Core.Tests;

public class CompanionChatterCatalogTests
{
    public static TheoryData<CompanionPetKind, string> EveryPetAndLocale()
    {
        var data = new TheoryData<CompanionPetKind, string>();

        foreach (var pet in Enum.GetValues<CompanionPetKind>())
        {
            data.Add(pet, "ko-KR");
            data.Add(pet, "en-US");
        }

        return data;
    }

    /// <summary>
    /// A missing corpus is silent rather than loud: nothing throws, and the bubble simply never
    /// opens. This is the test that would catch that silence, because nobody else would.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPetAndLocale))]
    public void EverySpeciesShipsACorpusInBothLanguages(CompanionPetKind pet, string locale) =>
        Assert.True(
            CompanionChatterCatalog.Lines(pet, locale).Count >= 300,
            $"{pet} has too few lines in {locale}.");

    /// <summary>
    /// The bubble is two lines of 13px text in a 210px border. A line long enough to be trimmed
    /// reaches the user as an ellipsis, which is the one thing a pet should never say.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPetAndLocale))]
    public void NoLineIsLongEnoughToBeTrimmed(CompanionPetKind pet, string locale)
    {
        // Korean is counted more strictly because its glyphs are close to twice the width.
        var limit = locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? 34 : 62;

        var tooLong = CompanionChatterCatalog.Lines(pet, locale)
            .Where(line => line.Length > limit)
            .ToList();

        Assert.True(tooLong.Count == 0, string.Join(" | ", tooLong));
    }

    /// <summary>
    /// Duplicates would waste the shuffle: the deck guarantees every line before a repeat, which
    /// means nothing if two cards carry the same joke.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPetAndLocale))]
    public void LinesAreDistinct(CompanionPetKind pet, string locale)
    {
        var lines = CompanionChatterCatalog.Lines(pet, locale);
        var duplicates = lines
            .GroupBy(line => line, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, string.Join(" | ", duplicates));
    }

    /// <summary>Section headings are structure for the writer, not something the pet says.</summary>
    [Fact]
    public void HeadingsAreNotPartOfTheCorpus() =>
        Assert.DoesNotContain(
            CompanionChatterCatalog.Lines(CompanionPetKind.Rabbit, "ko-KR"),
            line => line.StartsWith('#'));

    /// <summary>
    /// Species have to differ or the whole corpus is one pet in seven costumes.
    /// </summary>
    [Fact]
    public void SpeciesDoNotShareTheirLines()
    {
        var rabbit = CompanionChatterCatalog.Lines(CompanionPetKind.Rabbit, "ko-KR");
        var dragon = CompanionChatterCatalog.Lines(CompanionPetKind.Dragon, "ko-KR");

        Assert.Empty(rabbit.Intersect(dragon, StringComparer.Ordinal));
    }

    /// <summary>A language tag without a region still has to find its corpus.</summary>
    [Fact]
    public void PlainKoreanResolvesToTheKoreanCorpus() =>
        Assert.Equal(
            CompanionChatterCatalog.Lines(CompanionPetKind.Cat, "ko-KR"),
            CompanionChatterCatalog.Lines(CompanionPetKind.Cat, "ko"));

    /// <summary>An unshipped language falls back rather than leaving the pet mute.</summary>
    [Fact]
    public void AnUnknownLanguageFallsBackToEnglish() =>
        Assert.Equal(
            CompanionChatterCatalog.Lines(CompanionPetKind.Otter, "en-US"),
            CompanionChatterCatalog.Lines(CompanionPetKind.Otter, "fr-FR"));
}

public class CompanionChatterBagTests
{
    /// <summary>
    /// The point of the bag: a full cycle hands out every line exactly once. Drawing at random
    /// would repeat long before this many pulls.
    /// </summary>
    [Fact]
    public void ACycleDealsEveryLineExactlyOnce()
    {
        var lines = Enumerable.Range(0, 300).Select(i => i.ToString()).ToList();
        var bag = new CompanionChatterBag(lines, new Random(7));

        var dealt = Enumerable.Range(0, lines.Count).Select(_ => bag.Next()).ToList();

        Assert.Equal(lines.OrderBy(line => line, StringComparer.Ordinal), dealt.OrderBy(line => line, StringComparer.Ordinal));
    }

    /// <summary>
    /// The seam between two cycles is the one repeat shuffling cannot rule out by itself, and it
    /// is the repeat a user is guaranteed to notice.
    /// </summary>
    [Fact]
    public void ACycleNeverBeginsWithTheLineItEndedOn()
    {
        var lines = Enumerable.Range(0, 12).Select(i => i.ToString()).ToList();

        for (var seed = 0; seed < 200; seed++)
        {
            var bag = new CompanionChatterBag(lines, new Random(seed));
            var dealt = Enumerable.Range(0, lines.Count * 3).Select(_ => bag.Next()).ToList();

            for (var i = 1; i < dealt.Count; i++)
            {
                Assert.NotEqual(dealt[i - 1], dealt[i]);
            }
        }
    }

    /// <summary>Two runs should not open with the same thought.</summary>
    [Fact]
    public void DifferentRunsDealADifferentOrder()
    {
        var lines = Enumerable.Range(0, 300).Select(i => i.ToString()).ToList();

        var first = Enumerable.Range(0, 10)
            .Select(_ => new CompanionChatterBag(lines, new Random(1)).Next())
            .First();
        var second = new CompanionChatterBag(lines, new Random(2)).Next();

        Assert.NotEqual(first, second);
    }

    /// <summary>An empty corpus is quiet, not fatal.</summary>
    [Fact]
    public void AnEmptyBagReturnsEmptyRatherThanThrowing()
    {
        var bag = new CompanionChatterBag([]);

        Assert.True(bag.IsEmpty);
        Assert.Equal(string.Empty, bag.Next());
    }

    /// <summary>A one-line corpus has nowhere to go and must not spin forever.</summary>
    [Fact]
    public void ASingleLineBagKeepsReturningIt()
    {
        var bag = new CompanionChatterBag(["only"]);

        Assert.Equal("only", bag.Next());
        Assert.Equal("only", bag.Next());
    }

    /// <summary>Every shipped corpus should survive several full cycles.</summary>
    [Fact]
    public void TheRealCorpusCyclesWithoutRepeating()
    {
        var lines = CompanionChatterCatalog.Lines(CompanionPetKind.Monkey, "ko-KR");
        var bag = new CompanionChatterBag(lines, new Random(42));

        var dealt = Enumerable.Range(0, lines.Count * 2).Select(_ => bag.Next()).ToList();

        Assert.Equal(lines.Count, dealt.Take(lines.Count).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(lines.Count, dealt.Skip(lines.Count).Distinct(StringComparer.Ordinal).Count());
    }
}
