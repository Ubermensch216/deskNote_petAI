using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>Piccolo: stretching his arms and meditating in the air. His beam is a configuration in the beam file.</summary>
public sealed partial class RigDirector
{
    private static void PiccoloStretch(RigPose pose, double t)
    {
        pose.Eyes = EyeStyle.Open;
        pose.OpenL = pose.OpenR = 1;
        pose.Brow = 0.7f;
        pose.Cross = 0;
        pose.Gy = 0;
        pose.Gx = -0.4f;
        pose.Mouth = MouthStyle.Neutral;
        pose.Tail = F(-10 + (Math.Sin(t * 6) * 6));
        pose.TailWaveAmp = 12;
        pose.TailWavePhase = F(t * 8);
        var stance = ESeg(t, 0, 0.7) * (1 - ESeg(t, 5.4, 6.2));
        SigStance(pose, stance, 1.1);
        pose.HandL = pose.HandR = HandStyle.Fist;

        if (t < 0.9)
        {
            pose.Reach = Reach.Guard;
        }
        else if (t < 1.7)
        {
            // Drawing the right arm back.
            pose.Reach = Reach.None;
            pose.HandLTo = new PointF(98, 330);
            pose.HandRTo = new PointF(284, 330 + (24 * F(Seg(t, 0.9, 1.7))));
            pose.Lean = F(3 + (6 * Seg(t, 0.9, 1.7)));
        }
        else if (t < 3.6)
        {
            // Out it goes: the arm stretches and the fist lands far away.
            var f = ESeg(t, 1.7, 2.1) * (1 - ESeg(t, 2.8, 3.5));
            pose.ArmLenR = F(1 + (2.9 * f));
            pose.HandLTo = new PointF(98, 330);
            pose.HandRTo = new PointF(F(200 - 90 - (380 * f)), 322);
            pose.Lean = F((-8 * f) + (3 * (1 - f)));
            pose.Mouth = MouthStyle.Open;
            pose.MouthOpen = F(0.5 * f);
            pose.Flash = F(Math.Max(0, 1 - (Math.Abs(t - 2.15) / 0.15)) * 0.55);
            SetAura(pose, 0.4 * f, 5, t);
        }
        else if (t < 5.4)
        {
            // And the left one, straight up.
            var f = ESeg(t, 3.8, 4.2) * (1 - ESeg(t, 4.8, 5.3));
            pose.ArmLenL = F(1 + (2.3 * f));
            pose.HandRTo = new PointF(284, 340);
            pose.HandLTo = new PointF(F(110 - (130 * f)), F(300 - (250 * f)));
            pose.Lean = F(-4 * f);
            pose.Mouth = MouthStyle.Open;
            pose.MouthOpen = F(0.5 * f);
            SetAura(pose, 0.4 * f, 5, t);
        }
        else
        {
            pose.Reach = Reach.Cross;
            pose.ArmsFront = true;
            pose.Mouth = MouthStyle.Smile;
            pose.OpenL = pose.OpenR = 0.65f;
            pose.Brow = 0.4f;
        }
    }

    private void PiccoloMeditate(RigPose pose, RigFrame frame, double t)
    {
        var rise = ESeg(t, 0.2, 1.4) * (1 - ESeg(t, 7.2, 8.6));
        Lift(frame, (30 * _scale * rise) + (Math.Sin(t * 2.3) * 2.4 * _scale * rise));
        pose.Sit = true;
        pose.BodyY = F(34 * rise);
        pose.Knee = F(38 * rise);
        pose.FootLX = F(32 * rise);
        pose.FootRX = F(-32 * rise);
        pose.FootLY = pose.FootRY = F(-2 * rise);
        pose.FootLRot = F(-20 * rise);
        pose.FootRRot = F(-20 * rise);
        if (rise < 0.05)
        {
            pose.Sit = false;
            pose.BodyY = 0;
            pose.Knee = 0;
        }

        pose.Tail = F((Math.Sin(t * 3) * 10) + (10 * rise));
        pose.TailWaveAmp = F(10 + (10 * rise));
        pose.TailWavePhase = F(t * 5);

        if (t < 6.5 || t > 7.2)
        {
            var calm = t < 6.5 || t > 7.6;
            pose.Eyes = t > 7.4 ? EyeStyle.Open : EyeStyle.Sleep;
            pose.OpenL = pose.OpenR = t > 7.4 ? 0.75f : 0;
            pose.Mouth = MouthStyle.Neutral;
            pose.Reach = t > 7.6 ? Reach.Cross : Reach.Lap;
            pose.ArmsFront = true;
            pose.HandL = pose.HandR = HandStyle.Open;
            pose.Breath = F(Math.Sin(t * 1.4) * 0.7);
            pose.Brow = 0.2f;
            pose.HeadTilt = 0;
            SetAura(
                pose,
                (t < 6.5
                    ? 0.25 + (0.35 * ESeg(t, 0.6, 3.0)) + (0.08 * Math.Sin(t * 2))
                    : 0.5 * (1 - ESeg(t, 7.2, 8.4))) * (calm ? 1 : 0),
                5,
                t);
            pose.Spark = 0;
        }
        else
        {
            // Eyes snap open.
            var q = Seg(t, 6.5, 7.2);
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.EyeScale = 1.12f;
            pose.Brow = 0.9f;
            pose.Reach = Reach.Lap;
            pose.ArmsFront = true;
            pose.Mouth = MouthStyle.Neutral;
            pose.Cross = 0;
            pose.Gy = 0;
            pose.Gx = -0.2f;
            SetAura(pose, 0.75 + (0.25 * (1 - q)), 5, t);
            pose.Spark = F(0.4 * (1 - q));
            pose.Flash = F(Math.Max(0, 1 - (q * 2.2)) * 0.9);
        }
    }
}
