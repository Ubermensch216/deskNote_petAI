namespace DeskNote.Companion.Core;

/// <summary>
/// Where each species' drawing sits inside its 543 x 724 sprite frame.
/// </summary>
/// <remarks>
/// <para>
/// Measured off the walk sheets: the horizontal extent is the union of the four frames, the head
/// is the highest any frame reaches, and the feet are the usual floor line rather than the lowest
/// stray pixel. Close enough that a pet on the wall touches it; the few pixels a stride moves the
/// body by are not worth a table per frame.
/// </para>
/// <para>
/// A new or redrawn species has to be measured again. <c>assets/companion/measure_art_bounds.py</c>
/// prints this table from the sheets.
/// </para>
/// </remarks>
public static class PetArtBounds
{
    public static PetInk For(CompanionPetKind pet) => pet switch
    {
        CompanionPetKind.Rabbit => new PetInk(0.037, 0.210, 0.965, 0.784),
        CompanionPetKind.Cat => new PetInk(0.076, 0.186, 0.987, 0.782),
        CompanionPetKind.Dog => new PetInk(0.059, 0.195, 0.967, 0.791),
        CompanionPetKind.FennecFox => new PetInk(0.055, 0.113, 0.967, 0.791),
        CompanionPetKind.Otter => new PetInk(0.035, 0.207, 0.969, 0.772),
        CompanionPetKind.Dragon => new PetInk(0.061, 0.200, 1.000, 0.772),
        CompanionPetKind.Monkey => new PetInk(0.064, 0.204, 0.913, 0.778),
        CompanionPetKind.Duck => new PetInk(0.171, 0.198, 0.831, 0.773),

        // The rig-drawn characters, measured off the live drawing rather than a sheet: the figure
        // stands on a fixed foot line (RigCharacters.FootFraction) and its height is its own, so the
        // box is the walking figure's. `tools/PetSheets` prints these.
        CompanionPetKind.Goku => new PetInk(0.239, 0.347, 0.681, 0.906),
        CompanionPetKind.KidGoku => new PetInk(0.289, 0.497, 0.654, 0.905),
        CompanionPetKind.Roshi => new PetInk(0.374, 0.511, 0.683, 0.912),
        CompanionPetKind.Vegeta => new PetInk(0.319, 0.395, 0.661, 0.905),
        CompanionPetKind.Piccolo => new PetInk(0.271, 0.218, 0.731, 0.906),
        _ => new PetInk(0.04, 0.20, 0.96, 0.78),
    };
}
