using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>
/// The fifteen signature moves, three for each character.
/// </summary>
/// <remarks>
/// Each one is a short play with a beginning, a middle and an end, timed to the second: the
/// character takes a stance, gathers something, lets it go and is left a little worse for wear.
/// The shouts that go with it are timed to the same clock (<see cref="RigSignatures"/>), so a
/// "Ka-me-ha-me-" never arrives after the beam.
/// </remarks>
public sealed partial class RigDirector
{
    private bool _teleported;

    /// <summary>The move that is playing now, or -1.</summary>
    public int ActiveSignature { get; private set; } = -1;

    private void SigPose(RigPose pose, RigFrame frame, double t, double duration)
    {
        IdleBase(pose, _anim, lookAround: false);

        switch ((int)Character * RigSignatures.PerCharacter + ActiveSignature)
        {
            case 0:
                BeamPose(pose, frame, t, duration, CfgKame);
                break;
            case 1:
                GokuTeleport(pose, frame, t);
                break;
            case 2:
                GokuSuper(pose, frame, t);
                break;
            case 3:
                KidNimbus(pose, frame, t);
                break;
            case 4:
                KidPole(pose, frame, t);
                break;
            case 5:
                KidApe(pose, frame, t);
                break;
            case 6:
                BeamPose(pose, frame, t, duration, CfgRoshiKame);
                break;
            case 7:
                RoshiMax(pose, frame, t);
                break;
            case 8:
                RoshiMafuba(pose, t);
                break;
            case 9:
                VegetaBigBang(pose, t);
                break;
            case 10:
                BeamPose(pose, frame, t, duration, CfgFlash);
                break;
            case 11:
                VegetaPrince(pose, frame, t);
                break;
            case 12:
                BeamPose(pose, frame, t, duration, CfgBeamCannon);
                break;
            case 13:
                PiccoloStretch(pose, t);
                break;
            default:
                PiccoloMeditate(pose, frame, t);
                break;
        }
    }

    private static void SetAura(RigPose pose, double amount, int kind, double t)
    {
        pose.Aura = F(Math.Max(0, Math.Min(1, amount)));
        pose.AuraKind = kind;
        pose.AuraT = F(t);
    }

    /// <summary>The stance for gathering ki: the body turns side-on, the knees bend, the feet spread (k runs 0 to 1).</summary>
    private static void SigStance(RigPose pose, double k, double spread = 1)
    {
        pose.Turn = F(k);
        pose.View = k > 0.25 ? 1 : 0;
        pose.BodyY = F(20 * k);
        pose.Knee = F(18 * k);
        pose.FootLX = F(-24 * k * spread);
        pose.FootRX = F(30 * k * spread);
        pose.FootLRot = F(10 * k);
        pose.FootRRot = F(-8 * k);
        pose.Lean = F(-4 * k);
        pose.Breath = 0;
    }

    private static void HairBlow(RigPose pose, double t, double amp)
    {
        pose.EarL = F((Math.Sin(t * 19) * amp) + 4);
        pose.EarR = F((Math.Sin((t * 19) + 1.3) * amp) + 4);
    }

    private static void FaceStrain(RigPose pose, double open, double brow)
    {
        pose.Eyes = EyeStyle.Open;
        pose.OpenL = pose.OpenR = 1;
        pose.Brow = F(brow);
        pose.Mouth = MouthStyle.Open;
        pose.MouthOpen = F(open);
        pose.Cross = 0;
        pose.Gy = 0;
        pose.Gx = -0.4f;
    }

    /// <summary>Lifts the body off the floor by <paramref name="pixels"/> (the original wrote this as a negative "hop").</summary>
    private static void Lift(RigFrame frame, double pixels) => frame.LiftPixels = pixels;
}
