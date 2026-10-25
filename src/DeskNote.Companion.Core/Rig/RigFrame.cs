using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>Things drawn around the body rather than on it.</summary>
[Flags]
public enum RigDecor
{
    None = 0,

    /// <summary>Stars circling the head: dizzy.</summary>
    Stars = 1,

    /// <summary>Gold stars circling the whole body: a signature move is under way.</summary>
    SigStars = 2,

    /// <summary>A full moon beside the pet.</summary>
    Moon = 4,

    /// <summary>Dust and speed lines behind a pet that is running.</summary>
    Speed = 8,

    /// <summary>A bouquet held in both arms.</summary>
    Flower = 16,

    /// <summary>Music notes either side: dancing.</summary>
    Notes = 32,
}

/// <summary>What the motion engine and the window know about the pet this tick.</summary>
/// <param name="State">What the body is doing, as decided by <see cref="PetMotionEngine"/>.</param>
/// <param name="Rest">The rest pose, when <paramref name="State"/> is <see cref="PetMotionState.Resting"/>.</param>
/// <param name="RestSerial">Counts rests, so the director can tell a new one from the same one still running.</param>
/// <param name="StateElapsed">Seconds in the current state.</param>
/// <param name="RestElapsed">Seconds in the current rest.</param>
/// <param name="RestDuration">How long the current rest is meant to last.</param>
/// <param name="MovedPixels">How far the body moved on screen since the last tick, in screen pixels.</param>
/// <param name="VelocityX">Sideways speed in screen pixels a second: the pointer's, while the pet is held.</param>
/// <param name="CharacterPixels">How tall the drawn character stands on screen, head to foot, in pixels.</param>
/// <param name="GrabXPixels">Where the pointer holds the pet, sideways from the middle of its feet, in screen pixels.</param>
/// <param name="GrabYPixels">Where the pointer holds the pet, above its feet (negative is up), in screen pixels.</param>
/// <param name="Descending">On a wall: heading down, head first.</param>
/// <param name="Hungry">Whether the pet's fullness is low enough that it wants feeding.</param>
public readonly record struct RigInput(
    PetMotionState State,
    PetRestPose Rest,
    int RestSerial,
    double StateElapsed,
    double RestElapsed,
    double RestDuration,
    double MovedPixels,
    double VelocityX,
    double CharacterPixels,
    double GrabXPixels,
    double GrabYPixels,
    bool Descending,
    bool Hungry);

/// <summary>What the director wants drawn, and the few things it wants the window to do about it.</summary>
public sealed class RigFrame
{
    /// <summary>The body.</summary>
    public RigPose Pose { get; set; } = new();

    /// <summary>A sideways shake of the whole figure, in screen pixels: shivering, sobbing.</summary>
    public double JitterXPixels { get; set; }

    /// <summary>How far the body is lifted off whatever it stands on, in screen pixels. Hops, rising on a cloud.</summary>
    public double LiftPixels { get; set; }

    /// <summary>Opacity of the body alone, so a vanishing pet leaves its effects behind.</summary>
    public float Alpha { get; set; } = 1;

    /// <summary>Uniform size of the body about its feet. Only the great ape is not 1.</summary>
    public float Scale { get; set; } = 1;

    /// <summary>A tilt applied inside the picture, in degrees, about <see cref="Pivot"/>.</summary>
    public float Angle { get; set; }

    /// <summary>
    /// The point the tilt turns about, in reference units from the middle of the feet (up is
    /// negative). A pet in the hand hangs from where it was taken hold of.
    /// </summary>
    public PointF Pivot { get; set; }

    public RigDecor Decor { get; set; }

    public float DecorPhase { get; set; }

    /// <summary>How bright the moon is, 0 to 1.</summary>
    public float Moon { get; set; }

    /// <summary>Which signature move is playing, or -1.</summary>
    public int Signature { get; set; } = -1;

    /// <summary>Seconds into the signature move.</summary>
    public double SignatureTime { get; set; }

    /// <summary>Sideways travel the move asks for, in screen pixels a second, along the way the pet faces.</summary>
    public double DriftPixelsPerSecond { get; set; }

    /// <summary>Set for one tick when the pet should be moved elsewhere on the floor.</summary>
    public bool Teleport { get; set; }

    /// <summary>Whether a signature move is playing, which is what earns the gold bubble and the stars.</summary>
    public bool ShowsSignature => Signature >= 0;
}
