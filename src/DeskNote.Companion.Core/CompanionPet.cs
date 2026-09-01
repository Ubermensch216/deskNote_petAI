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
}
