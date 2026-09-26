using System.Xml.Linq;

namespace DeskNote.Companion.Core.Tests;

public class CompanionMemoryTests
{
    [Fact]
    public void Mission_accepts_non_ai_activity_and_care_without_needs_gates()
    {
        var mission = DailyMissionPlanner.For(new DateOnly(2026, 9, 17));
        var progress = new DailyProgress
        {
            LocalDate = new DateOnly(2026, 9, 17),
            AppCounts = new Dictionary<AppScoreCategory, int> { [AppScoreCategory.Capture] = 1 },
            CareCounts = new Dictionary<CompanionCareAction, int>
            {
                [CompanionCareAction.Greeting] = 1,
                [CompanionCareAction.Rest] = 1,
            },
        };
        Assert.True(mission.IsComplete(progress));
        Assert.False(mission.IsComplete(progress with
        {
            CareCounts = new Dictionary<CompanionCareAction, int> { [CompanionCareAction.Feed] = 2 },
        }));
    }

    [Fact]
    public void Activity_classification_preserves_pet_identity()
    {
        var activity = new ActivityClassifier().Classify(new CompanionActivityCandidate
        {
            SourceEventId = "pet-capture",
            OccurredAt = DateTimeOffset.Now,
            Type = CompanionActivityType.MeaningfulCapture,
            CurrentContentLength = 30,
            Pet = CompanionPetKind.Otter,
        });
        Assert.Equal(CompanionPetKind.Otter, activity?.Pet);
    }

    [Fact]
    public void Every_memory_and_unlocked_reaction_has_both_languages()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DeskNote.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        foreach (var locale in new[] { "ko-KR", "en-US" })
        {
            var document = XDocument.Load(Path.Combine(root.FullName, "src", "DeskNote.App", "Strings", locale, "Resources.resw"));
            var strings = document.Root!.Elements("data").ToDictionary(
                element => (string)element.Attribute("name")!, element => (string)element.Element("value")!);
            foreach (var template in CompanionMemoryCatalog.Templates)
            {
                Assert.False(string.IsNullOrWhiteSpace(strings[$"Companion_Memory{template}"]));
            }

            var keys = CompanionUnlockCatalog.All.Where(item => item.Key != "SpecialCare")
                .Select(item => $"Companion_Unlocked{item.Key}")
                .Concat(CompanionPetCatalog.All.Select(pet => $"Companion_UnlockedSpecial{pet}"));
            foreach (var key in keys)
            {
                Assert.InRange(strings[key].Length, 1, locale == "ko-KR" ? 34 : 62);
            }
        }
    }
}
