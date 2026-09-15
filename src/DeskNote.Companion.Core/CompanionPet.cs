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
    double Scale,
    CompanionGrowthMark Mark);

public static class CompanionGrowthAppearanceCatalog
{
    public const int FinalStage = 4;

    public static CompanionGrowthAppearance For(CompanionPetKind pet, int appearanceStage)
    {
        var stage = Math.Clamp(appearanceStage, 0, FinalStage);

        // How small each species starts. The differences are small and deliberate - a newborn
        // dragon is not a newborn dog - but they are differences in size only.
        var newborn = pet switch
        {
            CompanionPetKind.Rabbit => 0.50,
            CompanionPetKind.Cat => 0.51,
            CompanionPetKind.Dog => 0.53,
            CompanionPetKind.FennecFox => 0.52,
            CompanionPetKind.Otter => 0.51,
            CompanionPetKind.Monkey => 0.52,
            CompanionPetKind.Dragon => 0.49,
            _ => 0.51,
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
            newborn + ((1d - newborn) * progress),
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
