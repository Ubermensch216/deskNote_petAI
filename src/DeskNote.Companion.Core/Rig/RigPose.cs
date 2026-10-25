using System.Drawing;

namespace DeskNote.Companion.Core;

public enum EyeStyle
{
    Open,
    Happy,
    Sleep,
    Spiral,
}

public enum MouthStyle
{
    Neutral,
    Smile,
    Open,
    Scared,
    Frown,
    Pout,
}

public enum HandStyle
{
    Open,
    Fist,
    Point,
}

/// <summary>
/// Where the hands gather.
/// </summary>
/// <remarks>
/// A pose names the place and the rig turns it into coordinates, because the five bodies are
/// different sizes and "the belly" is not the same point on any two of them.
/// </remarks>
public enum Reach
{
    None,
    Lap,
    Belly,
    Mouth,
    Chest,
    Scratch,
    Chin,
    Cross,
    Overhead,
    Forehead,
    Hips,
    Cup,
    Front,
    Flex,
    Guard,
}

/// <summary>The five drawn characters. Each one has its own build, gait, manners and signature moves.</summary>
public enum RigCharacter
{
    Goku,
    Kid,
    Roshi,
    Vegeta,
    Piccolo,
}

/// <summary>What kind of ki energy is gathered: the colour and shape of the orb and the beam.</summary>
public enum KiKind
{
    None,
    Blue,
    Purple,
    Yellow,
    Spiral,
    Suck,
}

/// <summary>
/// One instant of a drawn character's body.
/// </summary>
/// <remarks>
/// <para>
/// Every length is in units of the reference drawing, which is 571 units tall; rotations are
/// clockwise on the screen, and the figure is drawn facing the way the painter is told to face.
/// </para>
/// <para>
/// The arm angles (<see cref="ArmLA"/> and friends) are on the scale the animals' pose code was
/// written for, where 60 is "hanging at the side"; the rig converts them to the angles of a
/// human arm. Keeping that scale means a pose can be written without knowing which body it lands
/// on.
/// </para>
/// </remarks>
public sealed class RigPose
{
    // Body
    public float BodyX;
    public float BodyY;
    public float Lean;

    /// <summary>Breathing, from -1 to 1.</summary>
    public float Breath;

    /// <summary>Seated: the legs go out in front.</summary>
    public bool Sit;

    /// <summary>Body turn: 0 faces front, 1 is a three-quarter profile (the stance for gathering ki and firing).</summary>
    public float Turn;

    // Head

    /// <summary>0 is a front view, 1 a three-quarter profile.</summary>
    public int View;
    public float HeadX;
    public float HeadY;
    public float HeadTilt;

    /// <summary>Sway of the hair tips and antennae, in degrees.</summary>
    public float EarL;
    public float EarR;

    // Eyes
    public EyeStyle Eyes;
    public float OpenL = 1;
    public float OpenR = 1;

    /// <summary>Where the eyes look (screen x and y) and how far they converge.</summary>
    public float Gx;
    public float Gy;
    public float Cross = .7f;
    public float PupilR = 1;
    public float EyeScale = 1;

    /// <summary>Positive is angry (the inner ends of the brows drop), negative is sad.</summary>
    public float Brow;

    /// <summary>Rotation phase of spiral eyes.</summary>
    public float SpiralT;

    // Mouth
    public MouthStyle Mouth;
    public float MouthOpen;
    public float Tongue;

    // Arms: the shoulder angle of the upper arm and the angle of the forearm beyond the elbow, both
    // measured from straight down, outward being positive.
    public float ArmLA = 60;
    public float ArmLB = 43;
    public float ArmRA = 60;
    public float ArmRB = 43;

    /// <summary>Arm length factor; Piccolo's arms stretch.</summary>
    public float ArmLenL = 1;
    public float ArmLenR = 1;
    public HandStyle HandL;
    public HandStyle HandR;

    /// <summary>When set, the hands go there instead of following the arm angles.</summary>
    public Reach Reach;

    /// <summary>Wrist targets, set directly or filled in from <see cref="Reach"/>.</summary>
    public PointF? HandLTo;
    public PointF? HandRTo;

    /// <summary>Hands clasped in front of the chest.</summary>
    public bool Clasp;

    /// <summary>The arms are drawn in front of the torso.</summary>
    public bool ArmsFront;

    // Legs
    public float FootLX;
    public float FootLY;
    public float FootRX;
    public float FootRY;
    public float FootLRot;
    public float FootRRot;

    /// <summary>How far the knees splay outward.</summary>
    public float Knee;

    // Tail and cape
    public float Tail;
    public float TailCurl;
    public float TailWaveAmp;
    public float TailWavePhase;

    // Marks laid over the figure

    /// <summary>When at least zero, tears are running (the value is the phase).</summary>
    public float Tears = -1;

    /// <summary>The anger mark, from 0 to 1.</summary>
    public float Anger;

    /// <summary>When at least zero, a cold sweat (the value is the phase).</summary>
    public float Sweat = -1;

    /// <summary>When at least zero, a snack is being eaten (how much of it is gone, 0 to 1).</summary>
    public float Cookie = -1;

    /// <summary>Blush on the cheeks, 0 to 1.</summary>
    public float Flush;

    /// <summary>How far the body is laid down, 0 standing to 1 flat on its back.</summary>
    public float Lie;

    /// <summary>When at least zero, the hand rubs back and forth (the value is the phase).</summary>
    public float Wipe = -1;

    // Ki and transformations

    /// <summary>The ki aura, 0 to 1.</summary>
    public float Aura;

    /// <summary>The aura's colour: 1 white, 2 gold, 3 blue, 4 purple, 5 green.</summary>
    public int AuraKind;

    /// <summary>The aura's flicker phase.</summary>
    public float AuraT;

    /// <summary>Super form, 0 to 1: gold hair and teal eyes, and Piccolo shrugs off his cape.</summary>
    public float Super;

    /// <summary>Roshi's MAX power muscles, 0 to 1.</summary>
    public float Muscle;

    /// <summary>The kid's great-ape transformation, 0 to 1.</summary>
    public float Ape;

    /// <summary>Size of the ki orb held between the hands, 0 to 1.</summary>
    public float Ki;

    /// <summary>The orb's kind, as a <see cref="DeskNote.Companion.Core.KiKind"/> value.</summary>
    public int KiKind;

    /// <summary>Where the orb is; between the hands when unset.</summary>
    public PointF? KiAt;

    /// <summary>Beam length (reference units), thickness (0 to 1) and ripple phase.</summary>
    public float BeamLen;
    public float BeamW;
    public float BeamT;

    /// <summary>When at least zero, a ki blast in flight (0 to 1 is its progress, exploding at 1).</summary>
    public float Shot = -1;

    /// <summary>A flash of light, 0 to 1.</summary>
    public float Flash;

    /// <summary>Lightning sparks, 0 to 1.</summary>
    public float Spark;

    /// <summary>0 none, 1 held (Roshi's cane), 2 free (the kid's power pole).</summary>
    public float Staff;
    public float StaffAngle;
    public float StaffLen = 1;

    /// <summary>1 a closed rice cooker, 2 an open one.</summary>
    public int Prop;

    /// <summary>The flying nimbus under the feet, 0 to 1.</summary>
    public float Nimbus;

    public static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    /// <summary>
    /// Folds the values that change the picture into a coarse hash, so a frame that looks the same
    /// as the last one is not painted again.
    /// </summary>
    public long Hash()
    {
        unchecked
        {
            var h = 1469598103934665603L;

            void Add(float value, float quantum) => h = (h ^ (long)Math.Round(value * quantum)) * 1099511628211L;

            Add(BodyX, 2);
            Add(BodyY, 2);
            Add(Lean, 4);
            Add(Breath, 20);
            Add(Sit ? 1 : 0, 1);
            Add(View, 1);
            Add(Turn, 10);
            Add(HeadX, 2);
            Add(HeadY, 2);
            Add(HeadTilt, 4);
            Add(EarL, 3);
            Add(EarR, 3);
            Add((int)Eyes, 1);
            Add(OpenL, 12);
            Add(OpenR, 12);
            Add(Gx, 10);
            Add(Gy, 10);
            Add(Cross, 10);
            Add(PupilR, 10);
            Add(EyeScale, 20);
            Add(Brow, 10);
            Add(SpiralT, 5);
            Add((int)Mouth, 1);
            Add(MouthOpen, 12);
            Add(Tongue, 8);
            Add(ArmLA, 3);
            Add(ArmLB, 3);
            Add(ArmRA, 3);
            Add(ArmRB, 3);
            Add(ArmLenL, 20);
            Add(ArmLenR, 20);
            Add((int)HandL, 1);
            Add((int)HandR, 1);
            Add(HandLTo?.X ?? -999, 2);
            Add(HandLTo?.Y ?? -999, 2);
            Add(HandRTo?.X ?? -999, 2);
            Add(HandRTo?.Y ?? -999, 2);
            Add(Clasp ? 1 : 0, 1);
            Add(ArmsFront ? 1 : 0, 1);
            Add(FootLX, 2);
            Add(FootLY, 2);
            Add(FootRX, 2);
            Add(FootRY, 2);
            Add(FootLRot, 3);
            Add(FootRRot, 3);
            Add(Knee, 3);
            Add(Tail, 3);
            Add(TailCurl, 3);
            Add(TailWaveAmp, 3);
            Add(TailWavePhase, 6);
            Add(Lie, 20);
            Add(Wipe, 12);
            Add((int)Reach, 1);
            Add(Tears, 8);
            Add(Anger, 8);
            Add(Sweat, 8);
            Add(Cookie, 20);
            Add(Flush, 8);
            Add(Aura, 10);
            Add(AuraKind, 1);
            Add(AuraT, 10);
            Add(Super, 12);
            Add(Muscle, 12);
            Add(Ape, 12);
            Add(Ki, 14);
            Add(KiKind, 1);
            Add(KiAt?.X ?? -999, 2);
            Add(KiAt?.Y ?? -999, 2);
            Add(BeamLen, 1);
            Add(BeamW, 12);
            Add(BeamT, 12);
            Add(Shot, 40);
            Add(Flash, 12);
            Add(Spark, 10);
            Add(Staff, 2);
            Add(StaffAngle, 4);
            Add(StaffLen, 20);
            Add(Prop, 1);
            Add(Nimbus, 12);
            return h;
        }
    }
}
