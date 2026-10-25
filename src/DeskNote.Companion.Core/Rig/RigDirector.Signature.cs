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

    // ------------------------------------------------------------------ beams

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

    // ------------------------------------------------------------------ Goku

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

    // ------------------------------------------------------------------ the kid

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

    // ------------------------------------------------------------------ Roshi

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

    // ------------------------------------------------------------------ Vegeta

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

    // ------------------------------------------------------------------ Piccolo

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
