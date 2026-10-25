using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>Roshi: MAX Power and the rice cooker. His Kamehameha is a configuration in the beam file.</summary>
public sealed partial class RigDirector
{
    private void RoshiMax(RigPose pose, RigFrame frame, double t)
    {
        var m = ESeg(t, 1.0, 3.0) * (1 - ESeg(t, 5.4, 6.2));
        pose.Muscle = F(m);

        if (t < 1.0 || t >= 6.1)
        {
            // The stooped old man he usually is.
            var end = t >= 6.1;
            var cough = Math.Sin(t * 14);
            pose.BodyY = 14;
            pose.Lean = F(-10 + (end ? cough * 1.4 : 0));
            pose.HeadTilt = -6;
            pose.Knee = 6;
            pose.HeadY = 4;
            pose.Eyes = end ? EyeStyle.Sleep : EyeStyle.Open;
            pose.OpenL = pose.OpenR = end ? 0 : 1;
            pose.Mouth = end ? MouthStyle.Open : MouthStyle.Frown;
            pose.MouthOpen = end ? F(0.3 + (0.25 * Math.Abs(cough))) : 0;
            pose.Sweat = end ? F(t) : -1f;
            if (end)
            {
                pose.ArmRA = 56;
                pose.ArmRB = 40;
                pose.ArmLA = 76;
                pose.ArmLB = 66;
                pose.Reach = Reach.None;
                pose.Breath = F(cough);
            }
        }
        else if (t < 3.0)
        {
            // Drawing breath, swelling.
            pose.Breath = F(1.4 * m);
            pose.BodyX = F(Math.Sin(t * 50) * 2 * m);
            pose.BodyY = F(14 * (1 - m));
            pose.Lean = F(-10 * (1 - m));
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Brow = F(0.5 * m);
            pose.Mouth = MouthStyle.Open;
            pose.MouthOpen = F(0.3 + (0.3 * m));
            pose.ArmLA = pose.ArmRA = F(60 + (30 * m));
            pose.ArmLB = pose.ArmRB = F(43 + (40 * m));
            pose.HandL = pose.HandR = HandStyle.Fist;
            SetAura(pose, 0.5 * m, 1, t);
            pose.Spark = F(0.4 * m);
            pose.Flash = F(Math.Max(0, 1 - (Math.Abs(t - 2.9) / 0.25)) * 0.9);
        }
        else
        {
            // MAX Power: flexing, one pose after another.
            var which = (int)((t - 3.0) / 1.0) % 2;
            var fade = t < 5.4 ? 1 : 1 - Seg(t, 5.4, 6.2);
            pose.Reach = which == 0 ? Reach.Flex : Reach.Hips;
            pose.HandL = pose.HandR = HandStyle.Fist;
            pose.Turn = 0;
            pose.View = 0;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Brow = 0.55f;
            pose.Mouth = MouthStyle.Open;
            pose.MouthOpen = F(0.4 + (0.3 * Math.Abs(Math.Sin(t * 6))));
            pose.BodyY = 4;
            pose.Knee = 2;
            pose.FootLX = -14;
            pose.FootRX = 14;
            pose.Lean = F(Math.Sin(t * 6) * 1.2);
            SetAura(pose, fade * 0.95, 1, t);
            pose.Spark = F(0.5 * fade);
            frame.LiftPixels = Math.Max(0, Math.Sin(t * 6)) * 1.5 * _scale;
        }
    }

    private static void RoshiMafuba(RigPose pose, double t)
    {
        var stance = ESeg(t, 1.0, 1.8) * (1 - ESeg(t, 6.0, 6.8));

        if (t < 1.0)
        {
            // Taking the rice cooker out.
            pose.Reach = Reach.Belly;
            pose.ArmsFront = true;
            pose.Prop = 1;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Brow = F(0.3 * Seg(t, 0.4, 1.0));
            pose.Mouth = MouthStyle.Smile;
        }
        else if (t < 6.8)
        {
            SigStance(pose, stance, 0.9);
            pose.Reach = Reach.Front;
            pose.HandL = pose.HandR = HandStyle.Open;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Brow = 0.6f;
            pose.BodyX = F(Math.Sin(t * 44) * (t is > 2.2 and < 5.2 ? 1.6 : 0.4));

            if (t is >= 2.2 and < 5.4)
            {
                // The lid is off and the evil is pulled in.
                pose.Prop = 2;
                pose.KiKind = (int)KiKind.Suck;
                pose.BeamLen = F(760 * ESeg(t, 2.2, 2.7) * (1 - ESeg(t, 5.0, 5.4)));
                pose.BeamT = F(t);
                pose.KiAt = new PointF(60, 330);
                SetAura(pose, 0.55, 1, t);
                pose.Spark = 0.25f;
                HairBlow(pose, t, 6);
                pose.Mouth = MouthStyle.Open;
                pose.MouthOpen = 0.5f;
                pose.Sweat = F(t);
                pose.Wipe = -1;
            }
            else if (t >= 5.2)
            {
                // The lid goes back on.
                pose.Prop = 1;
                pose.Flash = F(Math.Max(0, 1 - (Math.Abs(t - 5.4) / 0.3)) * 0.9);
                pose.Mouth = t < 5.9 ? MouthStyle.Open : MouthStyle.Frown;
                pose.MouthOpen = 0.4f;
                pose.Sweat = F(t);
            }
            else
            {
                pose.Mouth = MouthStyle.Neutral;
            }
        }
        else
        {
            // Worn out, swaying.
            var w2 = Math.Sin(t * 5);
            pose.Prop = 1;
            pose.Reach = Reach.Belly;
            pose.ArmsFront = true;
            pose.BodyY = 16;
            pose.Lean = F(-8 + (w2 * 3));
            pose.HeadTilt = F(-6 + (w2 * 3));
            pose.Knee = 8;
            pose.Eyes = EyeStyle.Sleep;
            pose.OpenL = pose.OpenR = 0;
            pose.Mouth = MouthStyle.Open;
            pose.MouthOpen = F(0.25 + (0.2 * Math.Abs(Math.Sin(t * 8))));
            pose.Sweat = F(t);
        }
    }
}
