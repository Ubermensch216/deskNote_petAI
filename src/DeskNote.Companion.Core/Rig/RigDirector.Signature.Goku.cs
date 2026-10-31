namespace DeskNote.Companion.Core;

/// <summary>Goku: the instant transmission and the Super Saiyan transformation. His beam is a configuration in the beam file.</summary>
public sealed partial class RigDirector
{
    private void GokuTeleport(RigPose pose, RigFrame frame, double t)
    {
        pose.Eyes = EyeStyle.Open;
        pose.OpenL = pose.OpenR = 1;

        if (t < 2.0)
        {
            // Two fingers to the forehead, and concentrate.
            var c = ESeg(t, 0, 0.6);
            pose.Reach = Reach.Forehead;
            pose.HandR = HandStyle.Point;
            pose.HandL = HandStyle.Fist;
            pose.Eyes = EyeStyle.Sleep;
            pose.OpenL = pose.OpenR = 0;
            pose.Brow = F(0.6 * c);
            pose.Mouth = MouthStyle.Neutral;
            SetAura(pose, 0.1 + (0.4 * ESeg(t, 0.6, 1.8)), 1, t);
            pose.Spark = F(0.35 * ESeg(t, 0.8, 1.8));
            HairBlow(pose, t, 2 + (6 * c));
            pose.BodyX = F(Math.Sin(t * 40) * 0.6 * c);
            frame.Alpha = F(1 - ESeg(t, 1.5, 2.0));
            pose.Flash = F(ESeg(t, 1.6, 1.95) * 0.9);
        }
        else if (t < 3.4)
        {
            // Gone: the window moves the pet while nothing is drawn.
            frame.Alpha = 0;
            pose.Reach = Reach.Forehead;
            pose.HandR = HandStyle.Point;
            pose.Eyes = EyeStyle.Sleep;
            pose.OpenL = pose.OpenR = 0;
            if (!_teleported)
            {
                _teleported = true;
                frame.Teleport = true;
            }
        }
        else
        {
            var q = ESeg(t, 3.4, 3.8);
            frame.Alpha = F(q);
            pose.Flash = F(Math.Max(0, 1 - ((t - 3.4) / 0.5)) * 0.9);

            if (t < 4.4)
            {
                // Back with a flourish, both arms thrown up.
                pose.ArmLA = pose.ArmRA = 150;
                pose.ArmLB = pose.ArmRB = 150;
                pose.Eyes = EyeStyle.Happy;
                pose.OpenL = pose.OpenR = 0;
                pose.Mouth = MouthStyle.Open;
                pose.MouthOpen = 0.5f;
                Lift(frame, Math.Abs(Math.Sin((t - 3.4) * 5)) * 4 * _scale);
                SetAura(pose, 0.3 * (1 - Seg(t, 3.4, 4.4)), 1, t);
            }
            else
            {
                // And a casual wave.
                var wv = Math.Sin(t * 9);
                pose.ArmRA = 150;
                pose.ArmRB = F(130 + (wv * 24));
                pose.Eyes = EyeStyle.Happy;
                pose.OpenL = pose.OpenR = 0;
                pose.Mouth = MouthStyle.Smile;
                pose.HeadTilt = F(Math.Sin(t * 3) * 3);
            }
        }
    }

    private void GokuSuper(RigPose pose, RigFrame frame, double t)
    {
        var up = ESeg(t, 0.8, 3.2);
        var down = ESeg(t, 7.0, 7.6);
        pose.Super = F(ESeg(t, 3.0, 3.5) * (1 - down));
        pose.HandL = pose.HandR = HandStyle.Fist;

        if (t < 3.3)
        {
            // Crouched, fists clenched, roaring.
            SigStance(pose, 0);
            pose.BodyY = F(10 + (18 * up));
            pose.Knee = F(16 * up);
            pose.FootLX = F(-14 * up);
            pose.FootRX = F(14 * up);
            pose.ArmLA = F(40 - (8 * up));
            pose.ArmRA = F(40 - (8 * up));
            pose.ArmLB = 18;
            pose.ArmRB = 18;
            FaceStrain(pose, 0.5 + (0.4 * Math.Abs(Math.Sin(t * 14))), 0.5 + (0.6 * up));
            pose.BodyX = F(Math.Sin(t * 60) * (1 + (2.4 * up)));
            HairBlow(pose, t, 4 + (14 * up));
            SetAura(pose, 0.15 + (0.85 * up), 1, t);
            pose.Spark = F(0.2 + (0.7 * up));
            Lift(frame, Math.Sin(t * 70) * 0.9 * _scale * up);
        }
        else if (t < 7.2)
        {
            // Golden and upright, fists on the hips.
            pose.Reach = Reach.Hips;
            pose.BodyY = 4;
            pose.Knee = 0;
            pose.FootLX = -16;
            pose.FootRX = 16;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Brow = 0.5f;
            pose.Mouth = MouthStyle.Smile;
            pose.Cross = 0;
            pose.Gy = 0;
            pose.Gx = 0;
            SetAura(pose, 1, 2, t);
            pose.Spark = F(0.55 + (0.2 * Math.Sin(t * 9)));
            HairBlow(pose, t, 7);
            pose.Breath = F(Math.Sin(t * 3));
            Lift(frame, Math.Sin(t * 4) * 0.6 * _scale);
        }
        else
        {
            pose.Reach = Reach.None;
            pose.ArmLA = pose.ArmRA = 56;
            pose.ArmLB = pose.ArmRB = 40;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Mouth = MouthStyle.Smile;
            pose.HandL = pose.HandR = HandStyle.Open;
            SetAura(pose, 1 - down, 2, t);
            HairBlow(pose, t, 4);
        }

        pose.Flash = F(Math.Max(
            pose.Flash,
            (Math.Max(0, 1 - (Math.Abs(t - 3.3) / 0.3)) * 0.95) + (Math.Max(0, 1 - (Math.Abs(t - 7.3) / 0.25)) * 0.7)));
    }
}
