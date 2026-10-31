using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>Vegeta: the Big Bang attack and the prince's aura. His Final Flash is a configuration in the beam file.</summary>
public sealed partial class RigDirector
{
    private static void VegetaBigBang(RigPose pose, double t)
    {
        var stance = ESeg(t, 0, 0.8) * (1 - ESeg(t, 4.6, 5.3));
        SigStance(pose, stance, 1);
        pose.Eyes = EyeStyle.Open;
        pose.OpenL = pose.OpenR = 1;
        pose.Brow = 0.8f;
        pose.Cross = 0;
        pose.Gy = 0;
        pose.Gx = -0.5f;

        if (t < 4.5)
        {
            var lv = F(ESeg(t, 0.8, 2.8));
            pose.Reach = Reach.None;
            pose.HandL = HandStyle.Fist;
            pose.HandR = HandStyle.Open;
            pose.HandLTo = new PointF(130 - 6, 400);
            pose.HandRTo = new PointF(64, 322);
            pose.KiKind = (int)KiKind.Blue;
            pose.BeamT = F(t);
            pose.KiAt = new PointF(34, 316);

            if (t < 2.9)
            {
                // The ball grows in the open palm.
                pose.Ki = F((0.85 * lv) + (0.05 * Math.Sin(t * 9) * lv));
                SetAura(pose, 0.1 + (0.6 * lv), 3, t);
                pose.Spark = F(0.4 * lv);
                HairBlow(pose, t, 3 + (6 * lv));
                pose.Mouth = MouthStyle.Open;
                pose.MouthOpen = F(0.2 + (0.2 * Math.Abs(Math.Sin(t * 5)) * lv));
                pose.BodyX = F(Math.Sin(t * 50) * 1.2 * lv);
            }
            else
            {
                // Thrown: the ball leaves the hand and bursts at the end of its flight.
                pose.Ki = F(Math.Max(0, 0.85 * (1 - Seg(t, 2.9, 3.3))));
                pose.Shot = F(Seg(t, 2.9, 4.5));
                if (pose.Shot >= 1)
                {
                    pose.Shot = -1;
                }

                SetAura(pose, 0.8 * (1 - Seg(t, 3.0, 4.6)), 3, t);
                pose.Spark = 0.3f;
                pose.Lean = -7;
                pose.Mouth = MouthStyle.Open;
                pose.MouthOpen = 0.6f;
                HairBlow(pose, t, 8);
                pose.Flash = F(Math.Max(0, 1 - ((t - 2.9) / 0.3)) * 0.6);
            }
        }
        else
        {
            // Arms folded: not worth the trouble.
            pose.Reach = Reach.Cross;
            pose.ArmsFront = true;
            pose.Mouth = MouthStyle.Neutral;
            pose.Brow = 0.6f;
            pose.OpenL = pose.OpenR = 0.7f;
            pose.Cross = 0.6f;
            pose.HeadTilt = -3;
        }
    }

    private void VegetaPrince(RigPose pose, RigFrame frame, double t)
    {
        var rise = ESeg(t, 0.8, 2.0) * (1 - ESeg(t, 5.8, 6.8));
        // Rising with the arms folded, the whole time looking down on everyone.
        Lift(frame, (16 * _scale * rise) + (Math.Sin(t * 2.4) * 1.8 * _scale * rise));
        pose.Turn = 0;
        pose.View = 0;
        pose.Reach = Reach.Cross;
        pose.ArmsFront = true;

        if (t >= 4.8 && t < 6.0)
        {
            pose.Reach = Reach.None;
            pose.ArmsFront = false;
            pose.HandRTo = new PointF(64, 296);
            pose.HandR = HandStyle.Point;
            pose.HandLTo = new PointF(166, 316);
            pose.HandL = HandStyle.Fist;
            pose.View = 1;
            pose.Turn = 0.5f;
        }

        pose.Eyes = t < 1.6 ? EyeStyle.Sleep : EyeStyle.Open;
        pose.OpenL = pose.OpenR = t < 1.6 ? 0 : 1;
        pose.Brow = 0.7f;
        pose.Cross = 0;
        pose.Gy = 0.1f;
        pose.Mouth = t > 2.8 ? MouthStyle.Open : MouthStyle.Smile;
        pose.MouthOpen = F(0.3 + (0.25 * Math.Abs(Math.Sin(t * 5)) * (t > 2.8 ? 1 : 0)));
        pose.FootLX = F(-4 * rise);
        pose.FootRX = F(4 * rise);
        pose.FootLY = pose.FootRY = F(-6 * rise);
        var a = rise * (t < 5.8 ? 1 : 1 - Seg(t, 5.8, 6.8));
        SetAura(pose, Math.Max(0.1 * Math.Min(1, t), 0.95 * a), 3, t);
        pose.Spark = F(0.5 * a);
        HairBlow(pose, t, 3 + (9 * a));
        pose.Breath = F(Math.Sin(t * 3));
        pose.Flash = F(Math.Max(0, 1 - (Math.Abs(t - 2.0) / 0.3)) * 0.9);
        pose.Lean = F(Math.Sin(t * 2) * 1.5);
    }
}
