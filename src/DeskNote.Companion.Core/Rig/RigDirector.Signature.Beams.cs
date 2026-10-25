using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>The four moves that fire a beam, which differ in their colours and their timing and not in their shape.</summary>
public sealed partial class RigDirector
{
    private sealed class BeamCfg
    {
        public double ChargeFrom { get; init; } = 0.8;

        public double FireAt { get; init; } = 3.2;

        public double HoldTo { get; init; } = 5.8;

        public double EndAt { get; init; } = 6.6;

        public KiKind Kind { get; init; } = KiKind.Blue;

        public int Aura { get; init; } = 1;

        public Reach ChargeReach { get; init; } = Reach.Cup;

        public Reach FireReach { get; init; } = Reach.Front;

        public HandStyle ChargeHand { get; init; } = HandStyle.Open;

        public HandStyle FireHand { get; init; } = HandStyle.Open;

        public float KiMax { get; init; } = 0.85f;

        public float BeamW { get; init; } = 0.55f;

        public float MaxLen { get; init; } = 1700;

        public float KiPulse { get; init; } = 0.05f;

        /// <summary>How the character recovers: 0 hands on knees, 1 a hand on the back and a cough, 2 arms folded and pleased.</summary>
        public int Recover { get; init; }
    }

    private static readonly BeamCfg CfgKame = new() { Kind = KiKind.Blue, Aura = 1, Recover = 0 };

    private static readonly BeamCfg CfgRoshiKame = new()
    {
        ChargeFrom = 1.0,
        FireAt = 3.6,
        HoldTo = 6.0,
        EndAt = 6.8,
        Kind = KiKind.Blue,
        Aura = 1,
        KiMax = 1f,
        BeamW = 0.85f,
        MaxLen = 1900,
        Recover = 1,
    };

    private static readonly BeamCfg CfgFlash = new()
    {
        ChargeFrom = 0.8,
        FireAt = 3.6,
        HoldTo = 6.2,
        EndAt = 7.0,
        Kind = KiKind.Yellow,
        Aura = 2,
        ChargeReach = Reach.Front,
        FireReach = Reach.Front,
        KiMax = 0.9f,
        BeamW = 1.25f,
        MaxLen = 1900,
        Recover = 0,
    };

    private static readonly BeamCfg CfgBeamCannon = new()
    {
        ChargeFrom = 0.8,
        FireAt = 3.7,
        HoldTo = 6.0,
        EndAt = 6.8,
        Kind = KiKind.Spiral,
        Aura = 5,
        ChargeReach = Reach.Forehead,
        FireReach = Reach.Front,
        ChargeHand = HandStyle.Point,
        FireHand = HandStyle.Point,
        KiMax = 0.8f,
        BeamW = 0.5f,
        MaxLen = 1800,
        Recover = 2,
    };

    private void BeamPose(RigPose pose, RigFrame frame, double t, double duration, BeamCfg c)
    {
        var stance = ESeg(t, 0, 0.8) * (1 - ESeg(t, c.EndAt, c.EndAt + 0.8));
        SigStance(pose, stance);
        pose.KiKind = (int)c.Kind;
        pose.BeamT = F(t);

        if (t < c.FireAt)
        {
            // Gathering: hands cupped at the hip, the orb swelling, the body trembling.
            var ch = ESeg(t, c.ChargeFrom, c.FireAt - 0.35);
            var shake = Math.Sin(t * 52);
            pose.Reach = t < c.ChargeFrom ? Reach.Guard : c.ChargeReach;
            pose.HandL = pose.HandR = t < c.ChargeFrom ? HandStyle.Fist : c.ChargeHand;
            if (c.ChargeHand == HandStyle.Point)
            {
                pose.HandL = HandStyle.Fist;
            }

            pose.Ki = F((c.KiMax * ch) + (c.KiPulse * Math.Sin(t * 9) * ch));
            if (c.ChargeReach == Reach.Forehead)
            {
                pose.KiAt = new PointF(206, 110);
                pose.Ki = F(pose.Ki * 0.55);
            }

            SetAura(pose, 0.1 + (0.65 * ch), c.Aura, t);
            pose.Spark = F(0.35 * ch);
            pose.BodyX = F(shake * 1.4 * ch);
            HairBlow(pose, t, 3 + (9 * ch));
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Brow = F(0.55 * ch);
            pose.Cross = 0;
            pose.Gy = 0;
            pose.Gx = -0.5f;
            pose.Mouth = ch > 0.15 ? MouthStyle.Open : MouthStyle.Neutral;
            pose.MouthOpen = F(0.25 + (0.18 * Math.Abs(Math.Sin(t * 5)) * ch));
            pose.Sweat = ch > 0.5 ? F(t) : -1f;
            if (c.ChargeReach == Reach.Forehead)
            {
                pose.Eyes = EyeStyle.Open;
                pose.Brow = 0.8f;
                pose.Mouth = MouthStyle.Neutral;
                pose.OpenL = pose.OpenR = 0.8f;
            }
        }
        else
        {
            // Firing: both arms thrust forward, the beam racing out, the body bracing against it.
            var f = ESeg(t, c.FireAt, c.FireAt + 0.3);
            var rel = ESeg(t, c.HoldTo, c.EndAt);
            pose.Reach = rel < 0.85 ? c.FireReach : Reach.None;
            pose.HandL = pose.HandR = c.FireHand;
            if (c.FireHand == HandStyle.Point)
            {
                pose.HandL = HandStyle.Open;
            }

            pose.Ki = F(c.KiMax * (1 - rel));
            pose.BeamLen = F(c.MaxLen * f * (1 - rel));
            pose.BeamW = F(c.BeamW * (1 - (0.8 * rel)) * (1 + (0.08 * Math.Sin(t * 30))));
            pose.Flash = F(Math.Max(0, 1 - ((t - c.FireAt) / 0.35)) * 0.85);
            SetAura(pose, 0.95 * (1 - (rel * 0.8)), c.Aura, t);
            pose.Spark = F(0.45 * (1 - rel));
            pose.Lean = F(-6 + (3 * Math.Sin(t * 41) * (1 - rel)) + (10 * rel));
            pose.BodyX = F(Math.Sin(t * 47) * 1.1 * (1 - rel));
            HairBlow(pose, t, 10);
            FaceStrain(pose, 0.7, 0.7);
            Lift(frame, Math.Abs(Math.Sin(t * 40)) * 0.5 * _scale * (1 - rel));
            pose.Sweat = F(t);
        }

        if (t >= c.EndAt)
        {
            RecoverPose(pose, t, duration, c);
        }
    }

    private static void RecoverPose(RigPose pose, double t, double duration, BeamCfg c)
    {
        var q = Seg(t, c.EndAt, duration);
        pose.Ki = 0;
        pose.BeamLen = 0;
        pose.KiKind = 0;
        pose.Shot = -1;
        pose.Flash = 0;
        pose.Spark = 0;
        pose.Reach = Reach.None;
        pose.HandL = pose.HandR = HandStyle.Open;
        pose.Aura = F(Math.Max(0, 0.3 * (1 - (q * 3))));
        pose.AuraKind = c.Aura;
        pose.AuraT = F(t);
        var pant = Math.Sin(t * 8);

        switch (c.Recover)
        {
            case 1:
                // A hand on the back and a cough.
                pose.BodyY = 24;
                pose.Knee = 10;
                pose.Lean = F(-14 + (pant * 1.2));
                pose.Reach = Reach.None;
                pose.ArmLA = 78;
                pose.ArmRA = 70;
                pose.ArmLB = 70;
                pose.ArmRB = 60;
                pose.Eyes = EyeStyle.Sleep;
                pose.OpenL = pose.OpenR = 0;
                pose.Mouth = MouthStyle.Open;
                pose.MouthOpen = F(0.25 + (0.2 * Math.Abs(pant)));
                pose.Sweat = F(t);
                pose.HeadTilt = F(-6 + pant);
                break;

            case 2:
                // Arms folded, pleased with itself.
                pose.Reach = Reach.Cross;
                pose.ArmsFront = true;
                pose.Mouth = MouthStyle.Smile;
                pose.Brow = 0.45f;
                pose.Eyes = EyeStyle.Open;
                pose.OpenL = pose.OpenR = 0.6f;
                pose.Cross = 0.7f;
                pose.Gy = 0.1f;
                pose.BodyY = 0;
                pose.Knee = 0;
                pose.Lean = 0;
                pose.Turn = 0;
                pose.View = 0;
                pose.HeadTilt = 3;
                break;

            default:
                // Hands on the knees, panting.
                pose.Reach = Reach.Lap;
                pose.BodyY = 20;
                pose.Knee = 12;
                pose.Lean = F(-12 + (pant * 1.5));
                pose.Eyes = EyeStyle.Open;
                pose.OpenL = pose.OpenR = 0.6f;
                pose.Mouth = MouthStyle.Open;
                pose.MouthOpen = F(0.25 + (0.2 * Math.Abs(pant)));
                pose.Sweat = F(t);
                pose.HeadTilt = -5;
                pose.Gy = 0.7f;
                pose.Cross = 0.3f;
                break;
        }
    }
}
