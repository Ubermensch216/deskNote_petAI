namespace DeskNote.Companion.Core;

/// <summary>
/// Which species have a stretch drawn for them, and which frame of it to show when.
/// </summary>
/// <remarks>
/// <para>
/// Every other rest pose is a transform over the sitting or walking artwork: a held frame under a
/// breath, a slow sway, a single scale-and-rotate pulse. A stretch cannot be - a cat stretching
/// puts its front legs out along the floor, drops its chest and raises its rear, and no scale or
/// rotation of a cat sitting up looks like that. So the species that have one carry a sheet of
/// their own, <c>{key}-stretch.png</c>, and the others keep the pulse.
/// </para>
/// <para>
/// The sheet is four frames, from a mild lean to the full pose, and the stretch plays them out
/// and back: lean in, reach, hold, release. Held, the last two frames alternate, which is the
/// small tremble a real stretch has at its deepest.
/// </para>
/// </remarks>
public static class PetStretchArt
{
    /// <summary>The frames in the sheet.</summary>
    public const int Frames = 4;

    /// <summary>The seconds one hold frame is shown before the other takes over.</summary>
    public const double HoldFlipSeconds = 0.45;

    /// <summary>Whether this species has a stretch sheet.</summary>
    public static bool Has(CompanionPetKind pet) => pet == CompanionPetKind.Cat;

    /// <summary>
    /// The frame to show <paramref name="progress"/> of the way through the stretch.
    /// </summary>
    /// <param name="progress">0 at the start of the rest, 1 at its end.</param>
    /// <param name="holdSeconds">How long the rest has been in its hold, for the tremble.</param>
    public static int FrameAt(double progress, double holdSeconds = 0)
    {
        var p = Math.Clamp(progress, 0, 1);

        if (p < 0.10)
        {
            return 0;
        }

        if (p < 0.26)
        {
            return 1;
        }

        if (p < 0.68)
        {
            // Held at the bottom of the stretch, shivering between the last two frames.
            return 2 + ((int)Math.Floor(Math.Max(0, holdSeconds) / HoldFlipSeconds) % 2);
        }

        return p < 0.84 ? 1 : 0;
    }

    /// <summary>Whether a progress falls in the hold, the part of the rest that has the tremble.</summary>
    public static bool IsHolding(double progress) => progress is >= 0.26 and < 0.68;
}
