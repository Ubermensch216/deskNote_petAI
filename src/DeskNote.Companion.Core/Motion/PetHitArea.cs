namespace DeskNote.Companion.Core;

/// <summary>
/// Whether the pointer is on the pet.
/// </summary>
/// <remarks>
/// The window is a fixed rectangle much larger than the animal, and only the animal should answer
/// to the pointer. What counts as the animal is the box its drawing fills in the pose it is in -
/// see <see cref="PetPoseGeometry.InkBox"/> - so a pet lying along a wall or hanging from the
/// ceiling is hit where it is, not where it would be if it were standing.
/// </remarks>
public static class PetHitArea
{
    /// <summary>A pointer a few pixels outside the outline is still "on the pet" to whoever holds it.</summary>
    public const double DefaultMargin = 8;

    /// <summary>Whether a point on the screen is over a pet whose drawing fills <paramref name="ink"/>.</summary>
    /// <param name="pointX">Pointer position, in screen pixels.</param>
    /// <param name="pointY">Pointer position, in screen pixels.</param>
    /// <param name="ink">The pet's drawn box, in screen pixels.</param>
    /// <param name="margin">How far outside the outline still counts.</param>
    public static bool Contains(double pointX, double pointY, PetBox ink, double margin = DefaultMargin) =>
        ink.Inflate(margin).Contains(pointX, pointY);
}
