using System.Drawing;

namespace DeskNote.Companion.Core;

/// <summary>
/// Turns what the motion engine says the pet is doing into the pose of a drawn body.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PetMotionEngine"/> knows where the pet is and what it is up to; it has no opinion on
/// what a leg or an eyebrow does about that. This class has the opinion. The two meet at one
/// small record, <see cref="RigInput"/>, so the motion code is untouched by the fact that some
/// pets are drawn from parts, and so this one can be run against any made-up sequence of states
/// and its poses inspected without a window, a bitmap or a clock.
/// </para>
/// <para>
/// A walk is not a looped picture. The feet are driven by the distance actually covered, so they
/// never slide on the floor however the pet's speed changes with its mood or its age; the eyes
/// blink on their own clock and wander on another; the hair, the antennae and the tail follow the
/// gait; and every pose is laid over the same breathing. That layering is the difference between a
/// body that is drawn and a sprite that is turned.
/// </para>
/// </remarks>
public sealed partial class RigDirector
{
    private readonly Random _random;
    private readonly double _blink;

    private double _anim;
    private double _gait;
    private double _walkAmp;
    private double _scale = 1;
    private double _unit = 1;

    private int _restSerial = -1;
    private RestLook _look = RestLook.Sit;
    private int _pauseAct;

    private double _heldAngle;
    private double _heldSpin;
    private double _heldVelocity;

    private double _poke;
    private double _jitterX;

    public RigDirector(RigCharacter character, Random? random = null)
    {
        Character = character;
        _random = random ?? new Random();
        _blink = _random.NextDouble() * 4.3;
    }

    public RigCharacter Character { get; }

    /// <summary>
    /// The signature move that will play at the start of the next rest, or -1.
    /// </summary>
    public int PendingSignature { get; private set; } = -1;

    /// <summary>What a pet does with the time it is given to sit.</summary>
    private enum RestLook
    {
        Sit,
        Stand,
        Sleep,
        Daydream,
        Stretch,
        Signature,
    }

    private bool Bristles => RigCharacters.BristlesWhenPoked(Character);

    /// <summary>
    /// Arms a signature move for the next rest and says how long the rest has to be to hold it.
    /// </summary>
    public double ArmSignature(int index)
    {
        PendingSignature = Math.Clamp(index, 0, RigSignatures.PerCharacter - 1);
        return RigSignatures.Duration(Character, PendingSignature);
    }

    /// <summary>Picks a signature move at random and arms it.</summary>
    public double ArmRandomSignature() => ArmSignature(_random.Next(RigSignatures.PerCharacter));

    /// <summary>Forgets an armed move that is no longer wanted.</summary>
    public void DisarmSignature() => PendingSignature = -1;

    /// <summary>A finger on the pet: it starts, or it glares, for a moment.</summary>
    public void Poke() => _poke = PokeSeconds;

    /// <summary>
    /// Moves the pet on by <paramref name="dt"/> seconds and returns how it looks.
    /// </summary>
    public RigFrame Advance(double dt, in RigInput input)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        _anim += dt;
        _jitterX = 0;
        _scale = input.CharacterPixels / (RigCharacters.ReferencePixels * RigCharacters.HeightFactor(Character));
        _unit = input.CharacterPixels / 571d;

        UpdateGait(dt, input);
        UpdateHeld(dt, input);
        if (_poke > 0)
        {
            _poke = Math.Max(0, _poke - dt);
        }

        if (input.State == PetMotionState.Resting && input.RestSerial != _restSerial)
        {
            BeginRest(input);
        }

        var frame = new RigFrame();
        Compose(frame, input);
        UpdateCare(dt, frame, input);
        frame.JitterXPixels = _jitterX;
        return frame;
    }

    // ------------------------------------------------------------------ clocks

    private void UpdateGait(double dt, in RigInput input)
    {
        switch (input.State)
        {
            case PetMotionState.Walking:
            case PetMotionState.Wall:
            case PetMotionState.Ceiling:
            case PetMotionState.WindowClimb:
                // The feet follow the ground: one full cycle for every 74 units the body covers,
                // which is what a 44-unit stance needs to keep a foot still on the floor.
                _gait += Math.Min(input.MovedPixels, 130 * _scale * Math.Max(dt, 0.001)) / (74.0 * _unit);
                break;

            case PetMotionState.Dashing:
                _gait += dt * 6.5;
                break;
        }

        var moving = input.State == PetMotionState.Walking;
        var target = moving ? 1d : 0d;
        _walkAmp += (target - _walkAmp) * Math.Min(1, dt * 9);
        if (_walkAmp < 0.004 && target == 0)
        {
            _walkAmp = 0;
        }
    }

    /// <summary>
    /// A pet in the hand swings like something hung from a hook: the pointer's sideways speed
    /// pushes it one way, and a spring pulls it back.
    /// </summary>
    private void UpdateHeld(double dt, in RigInput input)
    {
        if (input.State != PetMotionState.Held)
        {
            _heldAngle = _heldSpin = _heldVelocity = 0;
            return;
        }

        _heldVelocity = (_heldVelocity * 0.7) + (input.VelocityX * 0.3);
        var target = Math.Clamp(-_heldVelocity * 0.05 / Math.Max(_scale, 0.05), -45, 45) + (Math.Sin(_anim * 3.2) * 5);
        _heldSpin += (target - _heldAngle) * 60 * dt;
        _heldSpin -= _heldSpin * 7 * dt;
        _heldAngle += _heldSpin * dt;
    }

    // ------------------------------------------------------------------ choosing a rest

    private void BeginRest(in RigInput input)
    {
        _restSerial = input.RestSerial;
        _pauseAct = 0;

        if (PendingSignature >= 0)
        {
            _look = RestLook.Signature;
            ActiveSignature = PendingSignature;
            PendingSignature = -1;
            _teleported = false;
            return;
        }

        ActiveSignature = -1;
        switch (input.Rest)
        {
            case PetRestPose.Sleep:
                _look = RestLook.Sleep;
                break;
            case PetRestPose.Daydream:
                _look = RestLook.Daydream;
                break;
            case PetRestPose.Stretch:
                _look = RestLook.Stretch;
                break;
            default:
                // Sitting is the pose that was worked up from the animals, where the engine has no
                // standing pause at all. A person stops and stands more often than they sit, so
                // half the sits are the character standing and doing something with its hands.
                _pauseAct = ChoosePauseAct();
                _look = _pauseAct != 0 || _random.NextDouble() < 0.35 ? RestLook.Stand : RestLook.Sit;
                break;
        }
    }

    // ------------------------------------------------------------------ composing

    private void Compose(RigFrame frame, in RigInput input)
    {
        var pose = frame.Pose;
        var t = _anim;
        var hungry = input.Hungry;

        switch (input.State)
        {
            case PetMotionState.Walking:
                IdleBase(pose, t, lookAround: false);
                ApplyWalk(pose, _gait, _walkAmp, 22, 16, 12, 3f);
                if (hungry)
                {
                    SadOverlay(pose, t);
                }

                break;

            case PetMotionState.Landing:
                LandingPose(pose, frame, input);
                break;

            case PetMotionState.Falling:
                FallingPose(pose, input.StateElapsed);
                break;

            case PetMotionState.Thrown:
                ThrownPose(pose, input.StateElapsed);
                break;

            case PetMotionState.Held:
                HeldPose(pose, frame, input);
                break;

            case PetMotionState.Wall:
            case PetMotionState.WindowClimb:
                ClimbBody(pose, input.Descending && input.State == PetMotionState.Wall);
                break;

            case PetMotionState.Ceiling:
                ClimbBody(pose, descending: false);
                break;

            case PetMotionState.Dashing:
                DashPose(pose);
                frame.LiftPixels = Math.Sin(_anim * 20) * 0.5 * _scale;
                frame.Decor |= RigDecor.Speed;
                frame.DecorPhase = (float)_anim;
                break;

            case PetMotionState.Hopping:
                HopPose(pose, frame, input.StateElapsed / PetMotionEngine.HopSeconds);
                break;

            case PetMotionState.Resting:
                ComposeRest(frame, input);
                break;
        }

        // A finger on the pet cuts across whatever it was doing, for as long as a flinch lasts.
        // Sleeping, climbing and flying are left alone: it is a reaction to being touched on the
        // floor, and the engine has already decided those are not that.
        if (_poke > 0 && input.State is PetMotionState.Walking or PetMotionState.Resting
            && _look != RestLook.Signature)
        {
            frame.Pose = new RigPose();
            PokedPose(frame.Pose, PokeSeconds - _poke);
            _jitterX = Math.Sin((PokeSeconds - _poke) * 70) * 0.5 * _scale * Math.Sin(Math.PI * Math.Min(1, (PokeSeconds - _poke) / PokeSeconds));
        }

        ApplyPersona(frame, input);
    }

    private void ComposeRest(RigFrame frame, in RigInput input)
    {
        var pose = frame.Pose;
        var t = input.RestElapsed;

        switch (_look)
        {
            case RestLook.Sleep:
                SleepPose(pose, t);
                break;

            case RestLook.Daydream:
                DaydreamPose(pose, t);
                break;

            case RestLook.Stretch:
                {
                    var u = input.RestDuration <= 0 ? 1 : t / input.RestDuration;
                    double q;
                    if (u < 0.05)
                    {
                        q = 0;
                    }
                    else if (u < 0.35)
                    {
                        q = Ease((u - 0.05) / 0.3);
                    }
                    else if (u < 0.65)
                    {
                        q = 1;
                    }
                    else if (u < 0.9)
                    {
                        q = 1 - Ease((u - 0.65) / 0.25);
                    }
                    else
                    {
                        q = 0;
                    }

                    StretchPose(pose, q);
                    if (u >= 0.9)
                    {
                        var shake = Math.Sin((u - 0.9) * 150) * (1 - ((u - 0.9) / 0.1));
                        pose.Lean += F(shake * 3);
                        pose.HeadTilt += F(shake * 5);
                        pose.EarL += F(shake * 10);
                        pose.EarR -= F(shake * 10);
                    }

                    break;
                }

            case RestLook.Signature:
                frame.Signature = ActiveSignature;
                frame.SignatureTime = t;
                SigPose(pose, frame, t, input.RestDuration);
                frame.Decor |= RigDecor.SigStars;
                frame.DecorPhase = F(t);
                break;

            case RestLook.Stand:
                IdleBase(pose, t, lookAround: _pauseAct == 0);
                ApplyWalk(pose, _gait, _walkAmp, 22, 16, 12, 3f);
                if (input.Hungry)
                {
                    SadOverlay(pose, _anim);
                }

                break;

            default:
                if (input.Hungry)
                {
                    HungryCryPose(pose, _anim);
                    _jitterX = Math.Sin(_anim * 21) * 0.45 * _scale;
                }
                else
                {
                    SitIdlePose(pose, _anim);
                }

                break;
        }
    }

    // ------------------------------------------------------------------ small tools

    private static double Ease(double x)
    {
        x = Math.Max(0, Math.Min(1, x));
        return x * x * (3 - (2 * x));
    }

    private static float F(double value) => (float)value;

    private static double Tau => Math.PI * 2;

    private static double Seg(double t, double a, double b) => t <= a ? 0 : t >= b ? 1 : (t - a) / (b - a);

    private static double ESeg(double t, double a, double b) => Ease(Seg(t, a, b));

    private double Hash01(double seg)
    {
        var v = Math.Sin((seg * 12.9898) + (_blink * 78.233)) * 43758.5453;
        return v - Math.Floor(v);
    }

    /// <summary>How open the eyes are, 1 wide and 0 shut: about every four seconds, now and then twice running.</summary>
    private float BlinkOpen(double t)
    {
        var c = (t + _blink) % 4.3;
        static double Shape(double x) => x < 0 ? 1 : x < 0.05 ? 1 - (x / 0.05) : x < 0.09 ? 0 : x < 0.16 ? (x - 0.09) / 0.07 : 1;
        var o = Shape(c);
        if ((int)((t + _blink) / 4.3) % 3 == 0)
        {
            o = Math.Min(o, Shape(c - 1.1));
        }

        return F(o);
    }

    private float Twitch(double t, double period, double seed)
    {
        var c = (t + seed) % period;
        return c > 0.4 ? 0 : F(Math.Sin(c * 36) * (1 - (c / 0.4)) * 10);
    }

    /// <summary>A glance that moves to somewhere new every 2.4 seconds, the pupils jumping rather than drifting.</summary>
    private void Wander(RigPose pose, double t, double range)
    {
        var u = t / 2.4;
        var seg = Math.Floor(u);
        var e = Ease((u - seg) / 0.1);

        double Pick(int salt, double lo, double hi)
        {
            var a = lo + (Hash01(seg - 1 + salt) * (hi - lo));
            var b = lo + (Hash01(seg + salt) * (hi - lo));
            return a + ((b - a) * e);
        }

        pose.Gx = F(Pick(0, -1, 1) * range);
        pose.Gy = F(Pick(100, -0.2, 0.75) * Math.Min(1, range + 0.3));
        pose.Cross = F(Pick(200, 0.3, 0.85));
    }

    // ------------------------------------------------------------------ the standing body

    /// <summary>The body every other pose is a change to: breathing, blinking, a drifting glance, ears and tail at rest.</summary>
    private void IdleBase(RigPose pose, double t, bool lookAround)
    {
        pose.Breath = F(Math.Sin(_anim * 2.0));
        pose.OpenL = pose.OpenR = BlinkOpen(_anim);
        if (lookAround)
        {
            Wander(pose, t, 1);
        }
        else
        {
            pose.Gy = 0.45f;
            pose.Cross = 0.7f;
        }

        pose.HeadTilt = F((pose.Gx * 2.2) + (Math.Sin(t * 0.8) * 1.2));
        pose.EarL = F(Math.Sin((_anim * 1.7) + _blink) * 2.2);
        pose.EarR = F(Math.Sin((_anim * 1.7) + _blink + 1.1) * 2.2);
        pose.Tail = F(3 + (Math.Sin(t * 0.9) * 4));
        pose.TailWaveAmp = 9;
        pose.TailWavePhase = F(t * 2.1);
        pose.ArmLA = F(60 + (Math.Sin((t * 2.0) + 0.4) * 1.5));
        pose.ArmRA = F(60 + (Math.Sin((t * 2.0) + 0.1) * 1.5));
        pose.ArmLB = F(43 + (Math.Sin(t * 2.0) * 2));
        pose.ArmRB = F(43 + (Math.Sin((t * 2.0) + 0.3) * 2));
    }

    private static void SitDown(RigPose pose)
    {
        pose.Sit = true;
        pose.BodyY = 34;
        pose.Knee = 10;
        pose.FootLX = -8;
        pose.FootRX = 8;
        pose.FootLY = 2;
        pose.FootRY = 2;
        pose.FootLRot = 8;
        pose.FootRRot = 8;
    }

    /// <summary>One foot's step: pushed back along the ground for 60% of the cycle, then lifted and carried forward.</summary>
    private static void Foot(double phase, float stride, float lift, out float dx, out float dy, out float rot)
    {
        var th = phase - Math.Floor(phase);
        double fwd;
        double raise = 0;
        double r;
        const double Stance = 0.6;

        if (th < Stance)
        {
            var u = th / Stance;
            fwd = stride * (1 - (2 * u));
            r = 8 - (20 * u);
        }
        else
        {
            var u = (th - Stance) / (1 - Stance);
            var e = (1 - Math.Cos(Math.PI * u)) / 2;
            fwd = -stride + (2 * stride * e);
            raise = Math.Sin(Math.PI * u);
            r = -12 + (26 * Math.Sin(Math.PI * Math.Min(1, u * 1.15)));
        }

        // The drawing faces toward -x, so forward is negative.
        dx = F(-fwd);
        dy = F(-lift * raise);
        rot = F(r);
    }

    private void ApplyWalk(RigPose pose, double g, double ampD, float stride, float lift, float swing, float lean)
    {
        var amp = F(ampD);
        if (amp <= 0.005f)
        {
            return;
        }

        Foot(g, stride, lift, out var dx, out var dy, out var rot);
        pose.FootLX += dx * amp;
        pose.FootLY += dy * amp;
        pose.FootLRot += rot * amp;

        Foot(g + 0.5, stride, lift, out dx, out dy, out rot);
        pose.FootRX += dx * amp;
        pose.FootRY += dy * amp;
        pose.FootRRot += rot * amp;

        var ph = g * Tau;
        var bob = F(-Math.Abs(Math.Sin(ph)) * 3.4);
        pose.BodyY += bob * amp;
        pose.HeadY += -bob * 0.35f * amp;
        pose.Lean += (lean + F(Math.Sin(ph) * 2.0)) * amp;
        pose.BodyX += F(Math.Sin(ph) * 1.2) * amp;

        // The arms swing against the legs, the forearm a beat behind the upper arm.
        pose.ArmLA += F(Math.Sin(ph) * swing) * amp;
        pose.ArmRA -= F(Math.Sin(ph) * swing) * amp;
        pose.ArmLB += F(Math.Sin(ph - 0.8) * swing * 1.15) * amp;
        pose.ArmRB -= F(Math.Sin(ph - 0.8) * swing * 1.15) * amp;
        pose.Tail += F(Math.Sin(ph - 1.0) * 7) * amp;
        pose.TailCurl += F(10 * amp);
        pose.TailWaveAmp += 8 * amp;
        pose.TailWavePhase = F(ph);
        pose.EarL += F(Math.Sin((2 * ph) - 0.8) * 4) * amp;
        pose.EarR += F(Math.Sin((2 * ph) - 1.1) * 4) * amp;
        pose.HeadTilt += F(Math.Sin(ph + 0.5) * 1.5) * amp;

        if (amp > 0.35f)
        {
            // Walking, the character turns to show its profile.
            pose.View = 1;
            pose.Turn = 0.42f * amp;
            pose.Gx = -0.25f;
            pose.Gy = 0;
            pose.Cross = 0;
        }
    }
}
