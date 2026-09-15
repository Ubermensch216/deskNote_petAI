namespace DeskNote.Companion.Core;

public enum CompanionPetKind
{
    Rabbit = 1,
    Cat = 2,
    Dog = 3,
    FennecFox = 4,
    Otter = 5,
    Dragon = 6,
    Monkey = 7,
}

public enum CompanionPetSize
{
    Small = 1,
    Medium = 2,
    Large = 3,
}

public static class CompanionPetSizeExtensions
{
    public static double Scale(this CompanionPetSize size) => size switch
    {
        CompanionPetSize.Small => 1d / 3d,
        CompanionPetSize.Medium => 2d / 3d,
        _ => 1d,
    };
}

public static class CompanionPetCatalog
{
    /// <summary>Experience a pet needs before it counts toward unlocking the dragon.</summary>
    public static int FullyRaisedExperience =>
        CompanionGrowthLadder.Rung(CompanionGrowthLadder.FinalStage).Experience;

    /// <summary>Care days that go with <see cref="FullyRaisedExperience"/>.</summary>
    public static int FullyRaisedCareDays =>
        CompanionGrowthLadder.Rung(CompanionGrowthLadder.FinalStage).CareDays;

    public static IReadOnlyList<CompanionPetKind> All { get; } =
    [
        CompanionPetKind.Rabbit,
        CompanionPetKind.Cat,
        CompanionPetKind.Dog,
        CompanionPetKind.FennecFox,
        CompanionPetKind.Otter,
        CompanionPetKind.Dragon,
        CompanionPetKind.Monkey,
    ];

    public static IReadOnlyList<CompanionPetKind> RequiredForDragon { get; } =
    [
        CompanionPetKind.Rabbit,
        CompanionPetKind.Cat,
        CompanionPetKind.Dog,
        CompanionPetKind.FennecFox,
        CompanionPetKind.Otter,
        CompanionPetKind.Monkey,
    ];

    public static string AssetKey(this CompanionPetKind pet) => pet switch
    {
        CompanionPetKind.FennecFox => "fennec",
        _ => pet.ToString().ToLowerInvariant(),
    };

    public static CompanionPetKind FromAssetKey(string? assetKey) => assetKey switch
    {
        "cat" => CompanionPetKind.Cat,
        "dog" => CompanionPetKind.Dog,
        "fennec" => CompanionPetKind.FennecFox,
        "otter" => CompanionPetKind.Otter,
        "dragon" => CompanionPetKind.Dragon,
        "monkey" => CompanionPetKind.Monkey,
        _ => CompanionPetKind.Rabbit,
    };
}

/// <summary>
/// The ornament a pet wears once it has been raised far enough to earn it.
/// </summary>
/// <remarks>
/// The same ladder for every species, on purpose. A mark that differed by pet would be one more
/// thing to learn per animal; the point of the mark is that a glance at any pet on any desktop
/// says how far it has come. It is the same trade the care props already make - a vector ornament
/// rather than five more sprite sheets per species, which is thirty-five strips nobody would draw.
/// </remarks>
public enum CompanionGrowthMark
{
    /// <summary>Stage one. A newborn wears nothing; being tiny is the whole look.</summary>
    None = 0,

    /// <summary>Stage two. A two-leaf sprout, for something that has just started growing.</summary>
    Sprout = 1,

    /// <summary>Stage three. A pair of twinkling stars.</summary>
    Sparkles = 2,

    /// <summary>Stage four. A halo, one step short of the crown.</summary>
    Halo = 3,

    /// <summary>Stage five. The crown, and the only gold on the desktop.</summary>
    Crown = 4,
}

public readonly record struct CompanionGrowthAppearance(
    double WidthScale,
    double HeightScale,
    CompanionGrowthMark Mark);

/// <summary>
/// Keeps the original species artwork while changing its silhouette from a small, round newborn
/// at stage one to the full adult proportions at stage five, and hangs a per-stage ornament over
/// it.
/// </summary>
/// <remarks>
/// The silhouette used to run from about 0.75 of full size up to 1.0, which is under 7% per rung.
/// Nobody sees 7% across the days it takes to earn a rung: five stages that all looked the same
/// meant the growth ladder only existed as a number in the dashboard. The newborn now starts near
/// half size, so each promotion is a visible 15% step, and the ornament makes the stage readable
/// even when there is no earlier pet to compare against.
/// </remarks>
public static class CompanionGrowthAppearanceCatalog
{
    public const int FinalStage = 4;

    public static CompanionGrowthAppearance For(CompanionPetKind pet, int appearanceStage)
    {
        var stage = Math.Clamp(appearanceStage, 0, FinalStage);
        var newborn = pet switch
        {
            // Taller newborn silhouettes preserve the signature ears.
            CompanionPetKind.Rabbit => (Width: 0.50, Height: 0.50),
            CompanionPetKind.FennecFox => (Width: 0.52, Height: 0.52),

            // Broader, shorter silhouettes read as round-faced puppies and kittens.
            CompanionPetKind.Cat => (Width: 0.56, Height: 0.46),
            CompanionPetKind.Dog => (Width: 0.58, Height: 0.48),

            // Otter pups keep their characteristically low, rounded body.
            CompanionPetKind.Otter => (Width: 0.60, Height: 0.44),
            CompanionPetKind.Dragon => (Width: 0.52, Height: 0.46),
            CompanionPetKind.Monkey => (Width: 0.56, Height: 0.48),
            _ => (Width: 0.54, Height: 0.48),
        };

        // Even steps rather than an ease-out. The old curve spent most of its travel on the first
        // two rungs, which left the last three - the ones that cost care days - looking alike.
        var progress = stage switch
        {
            0 => 0d,
            1 => 0.30d,
            2 => 0.55d,
            3 => 0.79d,
            _ => 1d,
        };

        return new CompanionGrowthAppearance(
            newborn.Width + ((1d - newborn.Width) * progress),
            newborn.Height + ((1d - newborn.Height) * progress),
            MarkFor(stage));
    }

    public static CompanionGrowthMark MarkFor(int appearanceStage) =>
        Math.Clamp(appearanceStage, 0, FinalStage) switch
        {
            0 => CompanionGrowthMark.None,
            1 => CompanionGrowthMark.Sprout,
            2 => CompanionGrowthMark.Sparkles,
            3 => CompanionGrowthMark.Halo,
            _ => CompanionGrowthMark.Crown,
        };
}
