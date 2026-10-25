namespace DeskNote.Companion.Core;

/// <summary>
/// What makes the five characters different people rather than one body in five colours.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Goku always has a grin, and scratches his head, waves, or flexes when he stops.</item>
/// <item>The kid trots with a bounce and a wagging tail, and is easily startled.</item>
/// <item>Roshi stoops and shuffles, strokes his beard, and folds his arms.</item>
/// <item>Vegeta never stops frowning, stands with his arms folded, and bristles when poked.</item>
/// <item>Piccolo keeps his arms folded, his antennae twitching and his cape moving.</item>
/// </list>
/// The common poses are built first and these are laid over them.
/// </remarks>
public sealed partial class RigDirector
{
    /// <summary>
    /// The small thing a character does with its hands while standing: 0 nothing, 1 scratch the
    /// head, 2 fold the arms, 3 wave, 4 flex or put the fists on the hips, 5 stroke the beard.
    /// </summary>
    private int ChoosePauseAct()
    {
        if (_random.NextDouble() < 0.52)
        {
            return 0;
        }

        int[] pool = Character switch
        {
            RigCharacter.Goku => [1, 3, 4],
            RigCharacter.Kid => [1, 3, 4, 3],
            RigCharacter.Roshi => [5, 5, 3, 2],
            RigCharacter.Vegeta => [2, 2, 4],
            _ => [2, 2, 2, 4],
        };

        return pool[_random.Next(pool.Length)];
    }

    private void PauseActPose(RigPose pose, double t, double duration)
    {
        var e = Math.Min(1, t / 0.35) * (1 - Seg(t, Math.Max(0.5, duration - 0.35), duration));
        if (e < 0.05)
        {
            return;
        }

        switch (_pauseAct)
        {
            case 1:
                // Scratching the head, sheepish.
                pose.Reach = Reach.Scratch;
                pose.Wipe = F(_anim * 2.6);
                pose.Eyes = EyeStyle.Happy;
                pose.OpenL = pose.OpenR = 0;
                pose.Mouth = MouthStyle.Smile;
                pose.HeadTilt = F((-6 * e) + pose.HeadTilt);
                pose.Sweat = F(_anim);
                break;

            case 2:
                // Arms folded, a foot tapping now and then.
                pose.Reach = Reach.Cross;
                pose.ArmsFront = true;
                pose.Mouth = MouthStyle.Neutral;
                pose.Brow = F(0.2 + pose.Brow);
                pose.OpenL = pose.OpenR = Math.Min(pose.OpenL, 0.75f);
                pose.Gy = 0.1f;
                pose.Cross = 0.5f;
                pose.FootRY = F(-4 * Math.Max(0, Math.Sin(_anim * 6.5)) * (Math.Sin(_anim * 0.9) > 0 ? 1 : 0));
                break;

            case 3:
                // A wave.
                pose.ArmRA = 150;
                pose.ArmRB = F(130 + (Math.Sin(_anim * 9) * 24));
                pose.Mouth = MouthStyle.Smile;
                pose.Eyes = EyeStyle.Open;
                pose.HeadTilt = F(Math.Sin(_anim * 3) * 3);
                break;

            case 4:
                // Fists on the hips for the proud ones, a flexed bicep for the others.
                if (Character == RigCharacter.Vegeta)
                {
                    pose.Reach = Reach.Hips;
                    pose.HandL = pose.HandR = HandStyle.Fist;
                    pose.Mouth = MouthStyle.Neutral;
                    pose.Brow = 0.3f;
                    pose.HeadTilt = 4;
                    break;
                }

                pose.Reach = Reach.Flex;
                pose.HandL = pose.HandR = HandStyle.Fist;
                pose.Mouth = MouthStyle.Smile;
                pose.Eyes = EyeStyle.Happy;
                pose.OpenL = pose.OpenR = 0;
                pose.BodyY = F(3 + (Math.Sin(_anim * 8) * 2));
                break;

            case 5:
                // Stroking the beard.
                pose.Reach = Reach.Chin;
                pose.Wipe = F(_anim * 1.3);
                pose.Eyes = EyeStyle.Happy;
                pose.OpenL = pose.OpenR = 0;
                pose.HeadTilt = F(pose.HeadTilt + 3);
                break;
        }
    }

    /// <summary>Lays the character's manners over the pose built for the state, unless a signature move is on.</summary>
    private void ApplyPersona(RigFrame frame, in RigInput input)
    {
        // A signature move is its own performance, and a flinch is a reaction: neither wants the
        // character's everyday grin or stoop laid over it.
        if (frame.ShowsSignature || _poke > 0)
        {
            return;
        }

        var pose = frame.Pose;
        var t = _anim;
        var floor = input.State is PetMotionState.Walking
            or PetMotionState.Resting;
        var walking = _walkAmp > 0.02 && floor && (input.State == PetMotionState.Walking || _look == RestLook.Stand);
        var idleLike = input.State == PetMotionState.Resting
            && _look is RestLook.Sit or RestLook.Stand or RestLook.Daydream;
        var a = F(_walkAmp);
        var ph = F(_gait * Tau);

        switch (Character)
        {
            case RigCharacter.Goku:
                if (idleLike && pose.Tears < 0 && pose.Mouth == MouthStyle.Neutral && pose.Eyes == EyeStyle.Open)
                {
                    pose.Mouth = MouthStyle.Smile;
                }

                if (walking)
                {
                    pose.EarL += F(Math.Sin(ph * 2) * 3 * a);
                    pose.EarR += F(Math.Sin((ph * 2) + 1) * 3 * a);
                    pose.Mouth = MouthStyle.Smile;
                }

                break;

            case RigCharacter.Kid:
                if (walking)
                {
                    pose.BodyY += F(-Math.Abs(Math.Sin(ph)) * 3.5 * a);
                    pose.Tail += F(Math.Sin(ph * 2) * 14 * a);
                    pose.TailWaveAmp += 10 * a;
                    pose.Mouth = MouthStyle.Smile;
                }

                if (idleLike && pose.Tears < 0)
                {
                    pose.Tail += F(Math.Sin(t * 5) * 8);
                    pose.TailWaveAmp += 8;
                    if (pose.Mouth == MouthStyle.Neutral)
                    {
                        pose.Mouth = MouthStyle.Smile;
                    }
                }

                break;

            case RigCharacter.Roshi:
                if (walking)
                {
                    pose.Lean += F(-6 * a);
                    pose.BodyY += F(5 * a);
                    pose.HeadTilt += F(-4 * a);
                }

                break;

            case RigCharacter.Vegeta:
                if (walking)
                {
                    pose.Lean += F(1.5 * a);
                    pose.Brow = Math.Max(pose.Brow, 0.1f);
                }

                break;

            case RigCharacter.Piccolo:
                pose.EarL += Twitch(t, 4.7, _blink) * 0.4f;
                pose.EarR += Twitch(t, 6.1, (_blink * 2) + 1) * 0.4f;
                if (walking)
                {
                    pose.Tail += F(Math.Sin(ph - 0.4) * 10 * a);
                    pose.TailWaveAmp += 8 * a;
                }

                break;
        }

        // Standing still with something to do: only once the legs have stopped, and not while
        // the face is wet with tears, which an act of dignity would contradict.
        if (input.State == PetMotionState.Resting && _look == RestLook.Stand && _pauseAct != 0
            && _walkAmp < 0.05 && pose.Tears < 0 && _care == CareLook.None)
        {
            PauseActPose(pose, input.RestElapsed, input.RestDuration);
        }
    }
}
