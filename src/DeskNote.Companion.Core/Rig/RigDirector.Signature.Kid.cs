using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>The kid: the flying nimbus, the power pole and the great ape.</summary>
public sealed partial class RigDirector
{
    private void KidNimbus(RigPose pose, RigFrame frame, double t)
    {
        var s = ESeg(t, 0.1, 0.9) * (1 - ESeg(t, 6.6, 7.6));
        pose.Nimbus = F(s);
        var lift = (26 * _scale * s) + (Math.Sin(t * 2.6) * 2 * _scale * s);
        Lift(frame, lift);
        pose.Sit = true;
        pose.BodyY = 30;
        pose.Knee = 6;
        pose.FootLX = -10;
        pose.FootRX = 10;
        pose.FootLRot = 6;
        pose.FootRRot = 6;
        pose.Eyes = EyeStyle.Happy;
        pose.OpenL = pose.OpenR = 0;
        pose.Mouth = MouthStyle.Open;
        pose.MouthOpen = F(0.45 + (0.15 * Math.Sin(t * 5)));

        if (t < 0.9)
        {
            // Calling the cloud with both arms up.
            pose.Sit = false;
            pose.BodyY = 0;
            pose.Knee = 0;
            pose.FootLX = pose.FootRX = 0;
            pose.ArmLA = pose.ArmRA = 140;
            pose.ArmLB = pose.ArmRB = 150;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
        }
        else
        {
            var wv = Math.Sin(t * 8);
            pose.ArmRA = 150;
            pose.ArmRB = F(130 + (wv * 26));
            pose.ArmLA = F(70 + (Math.Sin(t * 2.4) * 6));
            pose.ArmLB = 50;
            pose.HeadTilt = F(Math.Sin(t * 2.2) * 4);
            pose.Lean = F(-4 + (Math.Sin(t * 2.6) * 2));
            pose.View = 1;
            pose.Turn = 0.35f;
        }

        pose.Tail = F(14 + (Math.Sin(t * 6) * 18));
        pose.TailWaveAmp = 22;
        pose.TailWavePhase = F(t * 9);
        HairBlow(pose, t, 4 + (4 * s));
        if (t > 6.6)
        {
            pose.Eyes = EyeStyle.Open;
        }

        if (t > 7.4)
        {
            pose.Sit = false;
            pose.BodyY = 0;
            pose.Knee = 0;
            pose.FootLX = pose.FootRX = 0;
        }

        // The cloud carries him along the floor; the window does the carrying.
        if (t is > 1.0 and < 6.6)
        {
            frame.DriftPixelsPerSecond = 46 * _scale * Math.Min(1, (t - 1.0) / 0.6);
        }
    }

    private void KidPole(RigPose pose, RigFrame frame, double t)
    {
        pose.Eyes = EyeStyle.Open;
        pose.OpenL = pose.OpenR = 1;
        pose.Mouth = MouthStyle.Open;
        pose.MouthOpen = 0.4f;
        pose.Brow = 0.2f;
        pose.Cross = 0.3f;
        pose.Gx = -0.3f;
        pose.Gy = 0.1f;
        pose.Tail = F(10 + (Math.Sin(t * 7) * 14));
        pose.TailWaveAmp = 18;
        pose.TailWavePhase = F(t * 10);
        SigStance(pose, ESeg(t, 0.3, 0.8) * (1 - ESeg(t, 6.2, 6.9)), 0.8);

        if (t < 0.6)
        {
            // Drawing the pole from his back.
            pose.Reach = Reach.Scratch;
            pose.Wipe = F(t * 4);
            pose.Staff = 0;
        }
        else if (t < 6.4)
        {
            pose.Staff = 2;
            pose.HandL = pose.HandR = HandStyle.Fist;
            double ang;
            var ext = ESeg(t, 3.0, 4.2) * (1 - ESeg(t, 5.4, 6.2));

            if (t < 3.0)
            {
                ang = (t - 0.6) * 560;
            }
            else if (t < 4.4)
            {
                ang = 20 - (30 * ESeg(t, 3.0, 4.2));
            }
            else if (t < 5.4)
            {
                ang = -10 - (100 * ESeg(t, 4.4, 5.0));
            }
            else
            {
                ang = -110 + (110 * ESeg(t, 5.4, 6.2));
            }

            pose.StaffAngle = F(ang);
            pose.StaffLen = F(1 + (2.2 * ext));
            pose.Reach = Reach.None;
            var pivot = new PointF(150, 338 - (ext > 0.1 ? 10 : 0));
            pose.HandLTo = new PointF(pivot.X - 14, pivot.Y + 4);
            pose.HandRTo = new PointF(pivot.X + 16, pivot.Y - 2);
            pose.Brow = F(0.5 * ext);
            HairBlow(pose, t, 4 + (5 * ext));

            if (t > 4.9 && t < 5.2)
            {
                pose.Flash = F(1 - (Math.Abs(t - 5.0) / 0.25));
                Lift(frame, Math.Sin((t - 4.9) / 0.3 * Math.PI) * 6 * _scale);
            }

            if (t >= 4.9 && t < 5.4)
            {
                pose.BodyY += 6;
            }
        }
        else
        {
            // Pole away, arms up, hopping with delight.
            pose.Staff = 0;
            pose.Reach = Reach.None;
            pose.ArmLA = 150;
            pose.ArmRA = 150;
            pose.ArmLB = 120;
            pose.ArmRB = 120;
            pose.HandL = pose.HandR = HandStyle.Open;
            pose.Eyes = EyeStyle.Happy;
            pose.OpenL = pose.OpenR = 0;
            pose.Mouth = MouthStyle.Smile;
            Lift(frame, Math.Abs(Math.Sin(t * 6)) * 5 * _scale);
        }
    }

    private void KidApe(RigPose pose, RigFrame frame, double t)
    {
        var moon = ESeg(t, 0.2, 1.0) * (1 - ESeg(t, 7.8, 8.8));
        frame.Decor |= RigDecor.Moon;
        frame.Moon = F(moon);
        var grow = ESeg(t, 1.4, 3.2) * (1 - ESeg(t, 7.4, 8.4));
        pose.Ape = F(grow);
        frame.Scale = F(1 + (0.8 * grow));
        pose.Tail = F(8 + (Math.Sin(t * 5) * 10));
        pose.TailWaveAmp = 14;
        pose.TailWavePhase = F(t * 7);

        if (t < 1.4)
        {
            // Looking up at the full moon.
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.EyeScale = F(1 + (0.15 * ESeg(t, 0.4, 1.3)));
            pose.PupilR = 0.6f;
            pose.Gy = -0.9f;
            pose.Cross = 0;
            pose.HeadTilt = -7;
            pose.HeadY = -4;
            pose.Mouth = MouthStyle.Scared;
            pose.MouthOpen = 0.6f;
            pose.Sweat = F(t);
            pose.BodyX = F(Math.Sin(t * 30) * 0.8 * Seg(t, 0.6, 1.4));
        }
        else if (t < 3.3)
        {
            // Shuddering through the change.
            pose.BodyX = F(Math.Sin(t * 55) * 2.6);
            pose.BodyY = 8;
            pose.Knee = 8;
            HairBlow(pose, t, 14);
            FaceStrain(pose, 0.9, 0.8);
            pose.EyeScale = 1.2f;
            pose.Reach = Reach.None;
            pose.ArmLA = pose.ArmRA = 120;
            pose.ArmLB = pose.ArmRB = 140;
            pose.HandL = pose.HandR = HandStyle.Fist;
            pose.Spark = F(0.5 * Seg(t, 1.4, 2.4));
            pose.Flash = F(Math.Max(0, 1 - (Math.Abs(t - 2.6) / 0.3)));
            SetAura(pose, 0.6 * grow, 4, t);
        }
        else if (t < 7.5)
        {
            // Pounding the chest and roaring.
            var beat = Math.Sin(t * 8);
            var a = Math.Max(0, beat);
            FaceStrain(pose, 0.8 + (0.2 * Math.Abs(beat)), 0.9);
            pose.EyeScale = 1.15f;
            pose.HandL = pose.HandR = HandStyle.Fist;
            pose.HandLTo = beat > 0 ? new PointF(176, 350) : new PointF(110, 230 - (10 * F(a)));
            pose.HandRTo = beat > 0 ? new PointF(110, 230) : new PointF(224, 350);
            pose.BodyY = F(8 + (4 * Math.Abs(beat)));
            pose.Knee = 10;
            pose.Lean = F(Math.Sin(t * 4) * 3);
            pose.HeadTilt = F(-3 + (beat * 3));
            pose.FootLX = -20;
            pose.FootRX = 20;
            Lift(frame, Math.Max(0, Math.Sin(t * 4)) * 4 * _scale);
            HairBlow(pose, t, 6);
            if (Math.Abs(Math.Sin(t * 4)) > 0.97)
            {
                pose.Flash = 0.3f;
            }
        }
        else
        {
            pose.Reach = Reach.None;
            pose.ArmLA = pose.ArmRA = 56;
            pose.ArmLB = pose.ArmRB = 40;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Mouth = MouthStyle.Smile;
            pose.Sweat = F(t);
            pose.HeadTilt = 5;
            pose.Flash = F(Math.Max(0, 1 - (Math.Abs(t - 7.7) / 0.3)));
        }
    }
}
