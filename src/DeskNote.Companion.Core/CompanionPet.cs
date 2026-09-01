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
    public const int FullyRaisedTotal = 150;

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

public readonly record struct CompanionGrowthAppearance(double WidthScale, double HeightScale);

/// <summary>
/// Keeps the original species artwork while changing its silhouette from a compact juvenile
/// body at stage one to the full adult proportions at stage five.
/// </summary>
public static class CompanionGrowthAppearanceCatalog
{
    public const int FinalStage = 4;

    public static CompanionGrowthAppearance For(CompanionPetKind pet, int appearanceStage)
    {
        var juvenile = pet switch
        {
            // Taller juvenile silhouettes preserve the signature ears.
            CompanionPetKind.Rabbit => new CompanionGrowthAppearance(0.70, 0.68),
            CompanionPetKind.FennecFox => new CompanionGrowthAppearance(0.72, 0.70),

            // Broader, shorter silhouettes read as round-faced puppies and kittens.
            CompanionPetKind.Cat => new CompanionGrowthAppearance(0.76, 0.64),
            CompanionPetKind.Dog => new CompanionGrowthAppearance(0.78, 0.66),

            // Otter pups keep their characteristically low, rounded body.
            CompanionPetKind.Otter => new CompanionGrowthAppearance(0.80, 0.62),
            CompanionPetKind.Dragon => new CompanionGrowthAppearance(0.72, 0.64),
            CompanionPetKind.Monkey => new CompanionGrowthAppearance(0.76, 0.66),
            _ => new CompanionGrowthAppearance(0.74, 0.66),
        };

        var progress = Math.Clamp(appearanceStage, 0, FinalStage) switch
        {
            0 => 0d,
            1 => 0.34d,
            2 => 0.61d,
            3 => 0.83d,
            _ => 1d,
        };

        return new CompanionGrowthAppearance(
            juvenile.WidthScale + ((1d - juvenile.WidthScale) * progress),
            juvenile.HeightScale + ((1d - juvenile.HeightScale) * progress));
    }
}
