namespace DeskNote.Companion.Core;

public sealed partial class RigDirector
{
    /// <summary>How long a flinch lasts, in seconds.</summary>
    private const double PokeSeconds = 0.42;

    private double _burst;

    /// <summary>
    /// Makes the next descent a burst of ki: gold aura, sparks, the transformation, for a while.
    /// </summary>
    /// <remarks>
    /// The same pet thrown three times in a few seconds has had enough. It is the one reaction here
    /// that costs the user something to find, so it is a reward for fiddling and not a menu item.
    /// </remarks>
    public void TriggerPowerBurst(double seconds) => _burst = seconds;

    // ------------------------------------------------------------------ touch

    private void PokedPose(RigPose pose, double elapsed)
    {
        var k = Math.Sin(Math.PI * Math.Min(1, elapsed / PokeSeconds));
        IdleBase(pose, _anim, lookAround: false);
        pose.BodyY = F(20 * k);
        pose.Knee = F(12 * k);
        pose.Lean = F(5 * k);

        if (Bristles)
        {
            // Stung into a glare: crouch, fists, and the anger mark.
            pose.HandL = pose.HandR = HandStyle.Fist;
            pose.ArmLA = F(60 - (22 * k));
            pose.ArmRA = F(60 - (22 * k));
            pose.ArmLB = F(30 - (20 * k));
            pose.ArmRB = F(30 - (20 * k));
            pose.Brow = F(0.95 * Math.Min(1, k * 2.2));
            pose.Mouth = MouthStyle.Frown;
            pose.EyeScale = 1f;
            pose.PupilR = F(0.85 - (0.15 * k));
            pose.Cross = 0;
            pose.Gy = 0;
            pose.Anger = F(Math.Min(1, (elapsed * 3) + 0.15)) * (k > 0.05 ? 1f : 0f);
        }
        else
        {
            // Startled: the eyes go round and the arms fly up.
            pose.ArmLA = F(60 + (40 * k));
            pose.ArmRA = F(60 + (40 * k));
            pose.ArmLB = F(43 + (46 * k));
            pose.ArmRB = F(43 + (46 * k));
            pose.Brow = F(-0.4 * k);
            pose.Mouth = MouthStyle.Scared;
            pose.MouthOpen = F(0.6 * k);
            pose.EyeScale = F(1 + (0.16 * k));
            pose.PupilR = 0.7f;
            pose.Cross = 0;
            pose.Gy = F(-0.2 * k);
            pose.Sweat = F(elapsed);
        }

        pose.EarL = F(-10 * k);
        pose.EarR = F(10 * k);
        pose.Tail = F(-14 * k);
        pose.TailWaveAmp = 16;
        pose.TailWavePhase = F(_anim * 26);
        pose.HeadY = F(4 * k);
    }

    // ------------------------------------------------------------------ landing, falling, flying, being held

    private void LandingPose(RigPose pose, RigFrame frame, in RigInput input)
    {
        // Knees bend and the arms spread for balance.
        var k = Math.Sin(Math.PI * Math.Min(1, input.StateElapsed / PetMotionEngine.LandingSeconds));
        IdleBase(pose, _anim, lookAround: false);
        pose.BodyY = F(24 * k);
        pose.Knee = F(14 * k);
        pose.FootLRot = pose.FootRRot = F(4 * k);
        pose.ArmLA = F(60 + (28 * k));
        pose.ArmRA = F(60 + (28 * k));
        pose.ArmLB = F(43 + (30 * k));
        pose.ArmRB = F(43 + (30 * k));
        pose.OpenL = pose.OpenR = F(1 - (0.7 * k * Math.Min(1, input.StateElapsed * 8)));
        pose.EarL = F(10 * k);
        pose.EarR = F(-10 * k);
        pose.Tail = F(-10 * k);
        pose.Mouth = k > 0.4 ? MouthStyle.Pout : MouthStyle.Neutral;
        frame.LiftPixels = 0;
    }

    private void FallingPose(RigPose pose, double t)
    {
        // Floating down on a gathered breath of ki, swaying; the kid rides his cloud instead.
        var sway = Math.Sin(t * 2.4);
        pose.Breath = F(Math.Sin(t * 3));
        pose.OpenL = pose.OpenR = BlinkOpen(t);
        pose.Gy = -0.95f;
        pose.Gx = F(Math.Sin(t * 1.3) * 0.2);
        pose.Cross = 0.15f;
        pose.PupilR = 0.85f;
        pose.ArmLA = F(78 + (sway * 6));
        pose.ArmRA = F(78 - (sway * 6));
        pose.ArmLB = F(70 + (sway * 8));
        pose.ArmRB = F(70 - (sway * 8));
        pose.FootLX = F(-3 - (sway * 3));
        pose.FootRX = F(3 - (sway * 3));
        pose.FootLY = F(-9 + (Math.Sin(t * 3.1) * 3));
        pose.FootRY = F(-9 + (Math.Sin((t * 3.1) + 1.3) * 3));
        pose.FootLRot = F(-14 + (sway * 5));
        pose.FootRRot = F(-14 - (sway * 5));
        pose.Knee = 3;

        var flutter = Math.Sin(t * 30);
        pose.EarL = F(-4 + (flutter * 5));
        pose.EarR = F(4 - (flutter * 5));
        pose.Tail = F(-10 + (sway * 8));
        pose.TailWaveAmp = 20;
        pose.TailWavePhase = F(t * 9);
        pose.TailCurl = 12;
        pose.Mouth = MouthStyle.Neutral;
        pose.MouthOpen = 0.3f;

        var aura = Character == RigCharacter.Vegeta ? 3 : Character == RigCharacter.Piccolo ? 5 : 1;
        if (Character == RigCharacter.Kid)
        {
            pose.Nimbus = F(Math.Min(1, t * 4));
        }
        else
        {
            pose.Aura = F(0.55 + (0.1 * Math.Sin(t * 5)));
            pose.AuraKind = aura;
            pose.AuraT = F(t);
        }

        if (_burst > 0)
        {
            // Ki bursts: the transformation, a gold aura and sparks all at once.
            pose.Super = 1;
            pose.Aura = 1;
            pose.AuraKind = 2;
            pose.AuraT = F(t);
            pose.Spark = 0.6f;
            pose.Muscle = 1;
            pose.Nimbus = 0;
            pose.Mouth = MouthStyle.Smile;
            pose.Gy = -0.2f;
            pose.Gx = 0;
            pose.Cross = 0;
            pose.Brow = 0.5f;
            pose.ArmLA = 40;
            pose.ArmRA = 40;
            pose.ArmLB = 20;
            pose.ArmRB = 20;
            pose.HandL = pose.HandR = HandStyle.Fist;
        }
    }

    private static void ThrownPose(RigPose pose, double t)
    {
        // Arms and legs flung wide.
        var sh = Math.Sin(t * 22);
        pose.Breath = 0;
        pose.OpenL = pose.OpenR = 1;
        pose.EyeScale = 1.08f;
        pose.PupilR = 0.7f;
        pose.Gy = -0.3f;
        pose.Cross = 0;
        pose.Gx = 0;
        pose.ArmLA = F(128 + (sh * 8));
        pose.ArmRA = F(128 - (sh * 8));
        pose.ArmLB = F(150 + (sh * 10));
        pose.ArmRB = F(150 - (sh * 10));
        pose.FootLX = -16;
        pose.FootRX = 16;
        pose.FootLY = -12;
        pose.FootRY = -10;
        pose.FootLRot = -8;
        pose.FootRRot = -8;
        pose.Knee = 6;
        pose.Mouth = MouthStyle.Open;
        pose.MouthOpen = F(0.55 + (sh * 0.1));
        pose.EarL = F(-26 + (sh * 6));
        pose.EarR = F(26 - (sh * 6));
        pose.Tail = F(-20 + (sh * 6));
        pose.TailCurl = -16;
        pose.TailWaveAmp = 18;
        pose.TailWavePhase = F(t * 24);
        pose.Brow = -0.3f;
        pose.Sweat = F(t);
    }

    private void HeldPose(RigPose pose, RigFrame frame, in RigInput input)
    {
        // Lifted by the scruff: limp, legs kicking, swinging against the pointer's motion.
        var sw = Math.Max(-1, Math.Min(1, _heldAngle / 30));
        var t = _anim;
        var kick = Math.Sin(t * 7.5);
        pose.Breath = F(Math.Sin(t * 5));
        pose.OpenL = pose.OpenR = BlinkOpen(t);
        pose.EyeScale = 1.06f;
        pose.PupilR = 0.75f;
        pose.Gy = -0.35f;
        pose.Cross = 0.1f;
        pose.Gx = F(-sw * 0.6);
        pose.ArmLA = F(105 + (kick * 14) + (sw * 14));
        pose.ArmRA = F(105 - (kick * 14) - (sw * 14));
        pose.ArmLB = F(130 + (kick * 18));
        pose.ArmRB = F(130 - (kick * 18));
        pose.FootLX = F(-5 + (kick * 7) - (sw * 3));
        pose.FootRX = F(5 - (kick * 7) - (sw * 3));
        pose.FootLY = F(-14 - (kick * 3));
        pose.FootRY = F(-14 + (kick * 3));
        pose.FootLRot = F(-16 + (kick * 6));
        pose.FootRRot = F(-16 - (kick * 6));
        pose.Knee = 4;
        pose.Mouth = MouthStyle.Scared;
        pose.MouthOpen = F(0.6 + (0.3 * Math.Sin(t * 9)));
        pose.EarL = F(-14 - (sw * 10) + (Math.Sin(t * 11) * 5));
        pose.EarR = F(14 - (sw * 10) - (Math.Sin(t * 11) * 5));
        pose.Tail = F(sw * 28);
        pose.TailCurl = F(-sw * 26);
        pose.TailWaveAmp = 14;
        pose.TailWavePhase = F(t * 8);
        pose.Sweat = F(t);

        frame.Angle = F(_heldAngle);
        var unit = Math.Max(_unit, 1e-6);
        frame.Pivot = new System.Drawing.PointF(F(input.GrabXPixels / unit), F(input.GrabYPixels / unit));
    }

    // ------------------------------------------------------------------ resting

    private static void SleepPose(RigPose pose, double t)
    {
        SitDown(pose);
        pose.Eyes = EyeStyle.Sleep;
        pose.OpenL = pose.OpenR = 0;
        var nod = Math.Sin(t * 1.15);
        pose.Breath = F(Math.Sin(t * 1.7) * 1.4);
        pose.HeadTilt = F(7 + (nod * 3.5));
        pose.HeadY = F(9 + (nod * 2));
        pose.HeadX = -3;
        pose.Mouth = MouthStyle.Neutral;
        pose.Gy = 0;
        pose.EarL = F(-6 + (Math.Sin(t * 1.15) * 1.5));
        pose.EarR = F(6 + (Math.Sin(t * 1.15) * 1.5));
        pose.ArmsFront = true;
        pose.ArmLA = 24;
        pose.ArmRA = 24;
        pose.Reach = Reach.Lap;
        pose.Tail = F(24 + (Math.Sin(t * 1.7) * 2));
        pose.TailCurl = 36;
        pose.TailWaveAmp = 3;
        pose.TailWavePhase = F(t * 1.7);
        pose.Flush = 0.35f;
    }

    private void StretchPose(RigPose pose, double q)
    {
        // q runs 0, 1, 0 as the arms go up and come down: a yawn with the body bowed back.
        IdleBase(pose, _anim, lookAround: false);
        pose.Eyes = q > 0.25 ? EyeStyle.Sleep : EyeStyle.Open;
        pose.OpenL = pose.OpenR = q > 0.25 ? 0 : 1;
        pose.ArmLA = F(60 + (118 * q));
        pose.ArmRA = F(60 + (118 * q));
        pose.ArmLB = F(43 + (134 * q));
        pose.ArmRB = F(43 + (134 * q));
        pose.Lean = F(7 * q);
        pose.HeadTilt = F(9 * q);
        pose.BodyY = F(-4 * q);
        pose.HeadY = F(-2 * q);
        pose.FootLY = pose.FootRY = F(-5 * q);
        pose.FootLRot = pose.FootRRot = F(-18 * q);
        pose.Mouth = q < 0.2 ? MouthStyle.Neutral : MouthStyle.Open;
        pose.MouthOpen = F(Math.Max(0, q - 0.15) * 1.1);
        pose.Tail = F(-24 * q);
        pose.TailCurl = F(-20 * q);
        pose.EarL = F(14 * q);
        pose.EarR = F(-14 * q);
        if (q > 0.6)
        {
            pose.ArmLA += F(Math.Sin(_anim * 30) * 1.5);
            pose.ArmRA -= F(Math.Sin(_anim * 30) * 1.5);
        }
    }

    private void DaydreamPose(RigPose pose, double t)
    {
        SitDown(pose);
        pose.Breath = F(Math.Sin(t * 1.1) * 0.6);
        pose.OpenL = pose.OpenR = BlinkOpen(t * 0.6);
        pose.Gx = F(Math.Sin(t * 0.45) * 0.55);
        pose.Gy = F(-0.55 + (Math.Sin(t * 0.31) * 0.2));
        pose.Cross = 0.2f;
        pose.HeadTilt = F(-4 + (Math.Sin(t * 0.7) * 3));
        pose.HeadY = -2;
        pose.Mouth = MouthStyle.Pout;
        pose.MouthOpen = 0.3f;
        pose.EarL = Twitch(t, 6.2, 1.3) * 0.6f;
        pose.EarR = (Twitch(t, 8.4, 4.1) * 0.6f) + F(Math.Sin(t * 0.9) * 2);
        pose.Tail = F(14 + (Math.Sin(t * 0.8) * 8));
        pose.TailCurl = 22;
        pose.TailWaveAmp = 7;
        pose.TailWavePhase = F(t * 1.6);
        pose.ArmLA = F(48 + (Math.Sin(t * 0.6) * 3));
        pose.ArmRA = F(48 - (Math.Sin(t * 0.6) * 3));
        pose.ArmLB = 36;
        pose.ArmRB = 36;
    }

    private void SitIdlePose(RigPose pose, double t)
    {
        SitDown(pose);
        IdleBase(pose, t, lookAround: true);
        pose.Sit = true;
        pose.BodyY = 34;
        pose.ArmLA = F(46 + (Math.Sin(t * 2.0) * 1.5));
        pose.ArmRA = F(46 + (Math.Sin((t * 2.0) + 0.3) * 1.5));
        pose.ArmLB = 30;
        pose.ArmRB = 30;
        pose.Tail = F(16 + (Math.Sin(t * 1.1) * 6));
        pose.TailCurl = 26;
        pose.TailWaveAmp = 6;
        pose.TailWavePhase = F(t * 1.9);

        // Now and then, a big yawn.
        var yc = (_anim + (_blink * 2)) % 11;
        if (yc < 1.7)
        {
            var q = Math.Sin(Math.PI * yc / 1.7);
            pose.Mouth = MouthStyle.Open;
            pose.MouthOpen = F(q * 0.9);
            pose.Eyes = q > 0.5 ? EyeStyle.Sleep : EyeStyle.Open;
            pose.OpenL = pose.OpenR = q > 0.5 ? 0 : 1;
            pose.HeadTilt = F(pose.HeadTilt - (5 * q));
            pose.ArmLA += F(30 * q);
            pose.ArmRA += F(30 * q);
        }
    }

    private void HungryCryPose(RigPose pose, double t)
    {
        SitDown(pose);
        var sob = Math.Sin(t * 9);
        pose.Breath = F(Math.Sin(t * 5) * 0.8);
        pose.OpenL = pose.OpenR = 1;
        pose.EyeScale = 1.04f;
        pose.PupilR = 0.9f;
        pose.Gy = 0.5f;
        pose.Cross = 0.5f;
        pose.Gx = 0;
        pose.Brow = -0.85f;
        pose.Mouth = MouthStyle.Frown;
        pose.MouthOpen = 0;
        pose.HeadTilt = F(-4 + (sob * 1.2));
        pose.HeadY = F(5 + (Math.Abs(sob) * 1.5));
        pose.ArmsFront = true;
        pose.ArmLA = 30;
        pose.ArmRA = 30;
        pose.Reach = Reach.Belly;
        pose.Tears = F(t);
        pose.EarL = F(-16 + (sob * 2));
        pose.EarR = F(16 - (sob * 2));
        pose.Tail = 30;
        pose.TailCurl = 12;
        pose.TailWaveAmp = 3;
        pose.TailWavePhase = F(t * 4);
    }

    /// <summary>A hungry pet on its feet: the same walk, with the brow low and the tears running.</summary>
    private static void SadOverlay(RigPose pose, double t)
    {
        var sob = Math.Sin(t * 9);
        pose.Brow = -0.85f;
        pose.Mouth = MouthStyle.Frown;
        pose.Tears = F(t);
        pose.EarL = F(-16 + (sob * 2));
        pose.EarR = F(16 - (sob * 2));
        pose.Tail = Math.Max(pose.Tail, 22);
        pose.TailWaveAmp *= 0.4f;
        pose.PupilR = 0.9f;
        pose.EyeScale = 1.04f;
        pose.HeadY += F(3 + Math.Abs(sob));
        pose.Gx = 0;
        pose.Gy = Math.Max(pose.Gy, 0.4f);
    }

    // ------------------------------------------------------------------ hopping, dashing, climbing

    private void HopPose(RigPose pose, RigFrame frame, double t)
    {
        t = Math.Clamp(t, 0, 1);
        IdleBase(pose, _anim, lookAround: false);
        pose.Eyes = EyeStyle.Happy;
        pose.OpenL = pose.OpenR = 0;
        pose.Mouth = MouthStyle.Smile;

        if (t < 0.16)
        {
            // Crouch.
            var q = Math.Sin(t / 0.16 * Math.PI);
            pose.BodyY = F(24 * q);
            pose.Knee = F(14 * q);
            pose.ArmLA = F(60 - (24 * q));
            pose.ArmRA = F(60 - (24 * q));
            pose.ArmLB = 30;
            pose.ArmRB = 30;
            pose.Eyes = EyeStyle.Open;
            pose.OpenL = pose.OpenR = 1;
            pose.Mouth = MouthStyle.Neutral;
            pose.Brow = F(0.4 * q);
        }
        else if (t < 0.84)
        {
            // Airborne, arms thrown up.
            var q = (t - 0.16) / 0.68;
            var up = Math.Sin(q * Math.PI);
            pose.ArmLA = F(60 + (100 * up));
            pose.ArmRA = F(60 + (100 * up));
            pose.ArmLB = F(43 + (120 * up));
            pose.ArmRB = F(43 + (120 * up));
            pose.FootLY = F(-12 * up);
            pose.FootRY = F(-12 * up);
            pose.FootLRot = pose.FootRRot = F(-14 * up);
            pose.FootLX = F(-5 * up);
            pose.FootRX = F(5 * up);
            pose.EarL = F(10 * up);
            pose.EarR = F(-10 * up);
            pose.Tail = F(-18 * up);
            pose.Mouth = MouthStyle.Open;
            pose.MouthOpen = F(0.55 * up);
            pose.Lean = F(-3 * Math.Sin(q * Math.PI * 2));
            frame.LiftPixels = up * 22 * _scale;
        }
        else
        {
            var q = Math.Sin((t - 0.84) / 0.16 * Math.PI);
            pose.BodyY = F(22 * q);
            pose.Knee = F(12 * q);
            pose.ArmLA = F(60 + (20 * q));
            pose.ArmRA = F(60 + (20 * q));
        }
    }

    private void DashPose(RigPose pose)
    {
        // Running from the pointer: a profile, leaning in, legs spinning, tongue out.
        var g = _gait;
        var ph = g * Tau;
        var t = _anim;
        pose.View = 1;
        pose.Breath = 0;
        pose.Lean = F(-12 + (Math.Sin(ph) * 1.5));
        pose.BodyY = F((-Math.Abs(Math.Sin(ph)) * 3) - 3);

        Foot(g, 30, 22, out var dx, out var dy, out var rot);
        pose.FootLX = dx;
        pose.FootLY = dy;
        pose.FootLRot = rot;
        Foot(g + 0.5, 30, 22, out dx, out dy, out rot);
        pose.FootRX = dx;
        pose.FootRY = dy;
        pose.FootRRot = rot;

        pose.ArmLA = F(120 + (Math.Sin(ph) * 22));
        pose.ArmRA = F(120 - (Math.Sin(ph) * 22));
        pose.ArmLB = F(142 + (Math.Sin(ph - 0.6) * 24));
        pose.ArmRB = F(142 - (Math.Sin(ph - 0.6) * 24));
        pose.Eyes = EyeStyle.Open;
        pose.OpenL = pose.OpenR = 1;
        pose.EyeScale = 1.04f;
        pose.Gx = -0.9f;
        pose.Gy = 0;
        pose.Cross = 0;
        pose.PupilR = 0.85f;
        pose.Mouth = MouthStyle.Open;
        pose.MouthOpen = F(0.35 + (Math.Sin(ph * 2) * 0.06));
        pose.Turn = 0.85f;
        pose.Aura = 0.35f;
        pose.AuraKind = Character == RigCharacter.Vegeta ? 3 : Character == RigCharacter.Piccolo ? 5 : 1;
        pose.AuraT = F(t);
        pose.EarL = F(26 + (Math.Sin(ph * 2) * 5));
        pose.EarR = F(26 + (Math.Sin((ph * 2) + 0.5) * 5));
        pose.Tail = F(-8 + (Math.Sin(ph - 1) * 6));
        pose.TailCurl = -6;
        pose.TailWaveAmp = 10;
        pose.TailWavePhase = F(ph * 2);
        pose.HeadTilt = -4;
        pose.HeadX = -4;
        pose.Sweat = F(t);
    }

    /// <summary>
    /// The pose on a wall, the ceiling or a window: in profile, hands and feet reaching in turn.
    /// The turn that lays the body against the wall is the window's, not this pose's.
    /// </summary>
    private void ClimbBody(RigPose pose, bool descending)
    {
        IdleBase(pose, _anim, lookAround: false);
        pose.View = 1;
        pose.Gx = -0.3f;
        pose.Gy = 0;
        pose.Cross = 0;
        pose.PupilR = 0.9f;
        ApplyWalk(pose, _gait, 1d, 24, 20, 30, 0);
        pose.ArmLA += 30;
        pose.ArmRA += 30;
        pose.Mouth = MouthStyle.Neutral;
        pose.Tail = F(pose.Tail + 6);
        pose.Brow = descending ? 0 : F(0.25);
    }

    // ------------------------------------------------------------------ being looked after

    private enum CareLook
    {
        None,
        Eat,
        Cheer,
        Dance,
        Flower,
        Scratch,
        Dizzy,
    }

    private CareLook _care;
    private double _careElapsed;
    private double _careFor;

    /// <summary>
    /// Has the pet act out a care action: eating what it was given, dancing at a game, holding out
    /// a bouquet for a greeting.
    /// </summary>
    public void PlayCare(CompanionCareAction action, double seconds)
    {
        _care = action switch
        {
            CompanionCareAction.Feed => CareLook.Eat,
            CompanionCareAction.Play => CareLook.Dance,
            CompanionCareAction.Greeting => CareLook.Flower,
            CompanionCareAction.BasicCare => CareLook.Scratch,
            CompanionCareAction.SpecialCare => CareLook.Cheer,
            _ => CareLook.None,
        };
        _careElapsed = 0;
        _careFor = seconds;
    }

    /// <summary>
    /// Has the pet reel: eyes spiralling, stars circling its head. The answer to a pointer that has
    /// been going round and round over it.
    /// </summary>
    public void PlayDizzy(double seconds)
    {
        _care = CareLook.Dizzy;
        _careElapsed = 0;
        _careFor = seconds;
    }

    private void UpdateCare(double dt, RigFrame frame, in RigInput input)
    {
        if (_burst > 0)
        {
            _burst = Math.Max(0, _burst - dt);
        }

        if (_care == CareLook.None)
        {
            return;
        }

        _careElapsed += dt;
        if (_careElapsed >= _careFor)
        {
            _care = CareLook.None;
            return;
        }

        // A pet that is in the air or on a wall is not being fed; only the floor has a use for these.
        if (input.State is not (PetMotionState.Walking or PetMotionState.Resting or PetMotionState.Landing)
            || _look == RestLook.Signature && input.State == PetMotionState.Resting)
        {
            return;
        }

        frame.Pose = new RigPose();
        var pose = frame.Pose;
        var t = _careElapsed;
        switch (_care)
        {
            case CareLook.Eat:
                EatPose(pose, Math.Min(1, t / Math.Max(1, _careFor)));
                break;

            case CareLook.Cheer:
                CheerPose(pose, hopping: true, t);
                frame.LiftPixels = Math.Sin(Math.PI * ((t % 0.42) / 0.42)) * 11 * _scale;
                break;

            case CareLook.Dance:
                DancePose(pose, t);
                frame.LiftPixels = Math.Abs(Math.Sin(t * Math.PI * 2.5)) * 12 * _scale;
                frame.Decor |= RigDecor.Notes;
                frame.DecorPhase = F(t);
                break;

            case CareLook.Flower:
                FlowerPose(pose, t);
                frame.Decor |= RigDecor.Flower;
                frame.DecorPhase = F(t);
                break;

            case CareLook.Dizzy:
                DizzyPose(pose, t);
                frame.Decor |= RigDecor.Stars;
                frame.DecorPhase = F(t);
                break;

            case CareLook.Scratch:
                IdleBase(pose, _anim, lookAround: false);
                pose.Reach = Reach.Scratch;
                pose.Wipe = F(_anim * 2.6);
                pose.Eyes = EyeStyle.Happy;
                pose.OpenL = pose.OpenR = 0;
                pose.Mouth = MouthStyle.Smile;
                pose.Flush = 0.6f;
                break;
        }
    }

    private void DizzyPose(RigPose pose, double t)
    {
        var sw = Math.Sin(t * 5.2);
        pose.Eyes = EyeStyle.Spiral;
        pose.SpiralT = F(t * 11);
        pose.Breath = F(Math.Sin(t * 4));
        pose.BodyX = F(sw * 7);
        pose.Lean = F(sw * 9);
        pose.HeadTilt = F((-sw * 7) + (Math.Sin(t * 3) * 3));
        pose.HeadX = F(sw * 3);
        pose.ArmLA = F(95 + (Math.Sin((t * 5.2) + 1) * 28));
        pose.ArmRA = F(95 + (Math.Sin((t * 5.2) + 3.1) * 28));
        pose.ArmLB = F(110 + (Math.Sin(t * 5.2) * 20));
        pose.ArmRB = F(110 - (Math.Sin(t * 5.2) * 20));

        // Staggering on the spot.
        Foot(t * 1.3, 10, 7, out var dx, out var dy, out var rot);
        pose.FootLX = dx;
        pose.FootLY = dy;
        pose.FootLRot = rot * 0.4f;
        Foot((t * 1.3) + 0.5, 10, 7, out dx, out dy, out rot);
        pose.FootRX = dx;
        pose.FootRY = dy;
        pose.FootRRot = rot * 0.4f;

        pose.Mouth = MouthStyle.Pout;
        pose.MouthOpen = F(0.5 + (0.4 * Math.Sin(t * 6)));
        pose.EarL = F(Math.Sin((t * 5.2) + 0.6) * 9);
        pose.EarR = F(-Math.Sin((t * 5.2) + 0.6) * 9);
        pose.Tail = F(-sw * 20);
        pose.TailWaveAmp = 14;
        pose.TailWavePhase = F(t * 8);
    }

    private void EatPose(RigPose pose, double progress)
    {
        SitDown(pose);
        var chew = Math.Sin(_anim * 2.4 * Tau);
        pose.Breath = F(chew * 0.3);
        pose.Eyes = EyeStyle.Happy;
        pose.OpenL = pose.OpenR = 0;
        pose.Mouth = MouthStyle.Neutral;
        pose.HeadY = F(1.5 + (chew * 1.6));
        pose.HeadTilt = F(chew * 1.4);
        pose.ArmsFront = true;
        pose.Reach = Reach.Mouth;
        pose.Cookie = F(progress);
        pose.Flush = 0.7f;
        pose.EarL = F(-3 + (chew * 3));
        pose.EarR = F(3 - (chew * 3));
        pose.Tail = F(14 + (Math.Sin(_anim * 3.4) * 9));
        pose.TailCurl = 24;
        pose.TailWaveAmp = 9;
        pose.TailWavePhase = F(_anim * 6);
    }

    private void CheerPose(RigPose pose, bool hopping, double t)
    {
        IdleBase(pose, _anim, lookAround: false);
        pose.Eyes = EyeStyle.Happy;
        pose.OpenL = pose.OpenR = 0;
        pose.Mouth = MouthStyle.Open;
        pose.MouthOpen = 0.5f;
        pose.Flush = 0.5f;

        if (hopping)
        {
            var q = (t % 0.42) / 0.42;
            var up = Math.Sin(Math.PI * q);
            pose.ArmLA = F(60 + (105 * up));
            pose.ArmRA = F(60 + (105 * up));
            pose.ArmLB = F(43 + (125 * up));
            pose.ArmRB = F(43 + (125 * up));
            pose.FootLY = F(-9 * up);
            pose.FootRY = F(-9 * up);
            pose.FootLRot = pose.FootRRot = F(-12 * up);
            pose.BodyY = F(q < 0.12 ? 12 * (1 - (q / 0.12)) : 0);
            pose.EarL = F(8 * up);
            pose.EarR = F(-8 * up);
            pose.Tail = F(-14 * up);
            pose.Lean = F(Math.Sin(q * Tau) * 2.5);
        }
        else
        {
            pose.ArmLA = 120;
            pose.ArmRA = 120;
            pose.ArmLB = 150;
            pose.ArmRB = 150;
        }
    }

    private void DancePose(RigPose pose, double phase)
    {
        var hop = Math.Abs(Math.Sin(phase * Math.PI * 2.5));
        var sw = Math.Sin(phase * Math.PI * 2.5);
        IdleBase(pose, _anim, lookAround: false);
        pose.Eyes = EyeStyle.Happy;
        pose.OpenL = pose.OpenR = 0;
        pose.Mouth = MouthStyle.Open;
        pose.MouthOpen = F(0.45 + (0.2 * hop));
        pose.Flush = 0.6f;

        var leftUp = (int)(phase / 0.3) % 2 == 0;
        pose.ArmLA = leftUp ? 150f : 38f;
        pose.ArmRA = leftUp ? 38f : 150f;
        pose.ArmLB = leftUp ? 168f : 25f;
        pose.ArmRB = leftUp ? 25f : 168f;
        pose.Lean = F(sw * 6);
        pose.HeadTilt = F(sw * 8);
        pose.BodyY = F(-hop * 2);
        pose.FootLY = F(-hop * 10 * (leftUp ? 1 : 0.3));
        pose.FootRY = F(-hop * 10 * (leftUp ? 0.3 : 1));
        pose.FootLRot = pose.FootRRot = F(-10 * hop);
        pose.EarL = F(hop * 10);
        pose.EarR = F(-hop * 10);
        pose.Tail = F(sw * 22);
        pose.TailWaveAmp = 14;
        pose.TailWavePhase = F(phase * 14);
    }

    private void FlowerPose(RigPose pose, double t)
    {
        IdleBase(pose, _anim, lookAround: false);
        pose.Eyes = EyeStyle.Happy;
        pose.OpenL = pose.OpenR = 0;
        pose.Mouth = MouthStyle.Smile;
        pose.Flush = 0.8f;
        pose.ArmsFront = true;
        pose.ArmLA = 30;
        pose.ArmRA = 30;
        pose.Reach = Reach.Belly;
        pose.HeadTilt = F(Math.Sin(t * 2.2) * 3.5);
        pose.BodyY = F(-Math.Abs(Math.Sin(t * 3.2)) * 2.5);
        pose.EarL = F(Math.Sin(t * 3.2) * 4);
        pose.EarR = F(-Math.Sin(t * 3.2) * 4);
        pose.Tail = F(Math.Sin(t * 4) * 10);
        pose.TailWaveAmp = 16;
        pose.TailWavePhase = F(t * 8);
    }
}
