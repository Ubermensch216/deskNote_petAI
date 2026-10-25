namespace DeskNote.Companion.Core;

/// <summary>
/// Which species are drawn by the rig rather than shipped as sprite sheets, and how each one is built.
/// </summary>
/// <remarks>
/// <para>
/// The five characters are not animals with a costume: they have arms, so they wave, fold them,
/// flex and throw their hands out to fire a beam, and none of that can be done by turning a
/// picture. That is why the rig exists, and why these species are told apart from the rest in one
/// place.
/// </para>
/// <para>
/// The sprite species keep their sheets and their motion; nothing here applies to them.
/// </para>
/// </remarks>
public static class RigCharacters
{
    /// <summary>How tall the figure stands in a 150 x 200 frame at height factor 1, as a fraction of the frame.</summary>
    public const double BaseHeightFraction = 0.56;

    /// <summary>Where the soles are in the frame, measured down from its top as a fraction. The cloud and the aura have room under and over them.</summary>
    public const double FootFraction = 0.90;

    /// <summary>The figure's height in the original desktop pet, in pixels at its default size. Pose offsets written in pixels are scaled from it.</summary>
    public const double ReferencePixels = 104;

    /// <summary>The width of the reference figure's stride box, in units of a figure 571 tall.</summary>
    public const double NominalWidthUnits = 270;

    public static bool IsRig(CompanionPetKind pet) => TryGet(pet, out _);

    public static bool TryGet(CompanionPetKind pet, out RigCharacter character)
    {
        character = pet switch
        {
            CompanionPetKind.Goku => RigCharacter.Goku,
            CompanionPetKind.KidGoku => RigCharacter.Kid,
            CompanionPetKind.Roshi => RigCharacter.Roshi,
            CompanionPetKind.Vegeta => RigCharacter.Vegeta,
            CompanionPetKind.Piccolo => RigCharacter.Piccolo,
            _ => default,
        };

        return pet is CompanionPetKind.Goku or CompanionPetKind.KidGoku or CompanionPetKind.Roshi
            or CompanionPetKind.Vegeta or CompanionPetKind.Piccolo;
    }

    public static CompanionPetKind PetOf(RigCharacter character) => character switch
    {
        RigCharacter.Kid => CompanionPetKind.KidGoku,
        RigCharacter.Roshi => CompanionPetKind.Roshi,
        RigCharacter.Vegeta => CompanionPetKind.Vegeta,
        RigCharacter.Piccolo => CompanionPetKind.Piccolo,
        _ => CompanionPetKind.Goku,
    };

    /// <summary>How tall the character stands next to the others: the kid is short, Piccolo tall.</summary>
    public static double HeightFactor(RigCharacter character) => character switch
    {
        RigCharacter.Kid => 0.74,
        RigCharacter.Roshi => 0.84,
        RigCharacter.Vegeta => 0.90,
        RigCharacter.Piccolo => 1.18,
        _ => 1.0,
    };

    /// <summary>How briskly the character walks. Roshi shuffles; the kid trots.</summary>
    public static double SpeedFactor(RigCharacter character) => character switch
    {
        RigCharacter.Kid => 1.18,
        RigCharacter.Roshi => 0.78,
        RigCharacter.Vegeta => 0.96,
        RigCharacter.Piccolo => 0.98,
        _ => 1.05,
    };

    /// <summary>The character's height as a fraction of the frame the window draws it in.</summary>
    public static double HeightFraction(RigCharacter character) => BaseHeightFraction * HeightFactor(character);

    /// <summary>
    /// Whether the character gets angry rather than startled when it is poked.
    /// </summary>
    /// <remarks>
    /// Vegeta and Piccolo would never be seen to be frightened of a finger; they glare. It is the
    /// same body and the same pose code, only a different face on the reaction.
    /// </remarks>
    public static bool BristlesWhenPoked(RigCharacter character) =>
        character is RigCharacter.Vegeta or RigCharacter.Piccolo;
}
