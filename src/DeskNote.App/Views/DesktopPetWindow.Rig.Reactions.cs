using System.Runtime.InteropServices;
using DeskNote.App.Services;
using DeskNote.Companion.Core;

namespace DeskNote.App.Views;

/// <summary>
/// What a drawn character does about what is done to it: being thrown over and over, a pointer
/// going round its head, a click, being looked after.
/// </summary>
public sealed partial class DesktopPetWindow
{
    private const double PowerBurstSeconds = 3.5 * 3;
    private const double DizzySeconds = 4;
    private const double DizzyCooldownSeconds = 20;

    /// <summary>The first pass of the ring round the head: nearer than this fraction of the body's width is on top of it.</summary>
    private const double CirclingInnerFraction = 0.45;

    private const double CirclingReach = 220;

    private readonly ThrowStreak _throwStreak = new();
    private readonly PointerCircling _circling = new();
    private double _dizzyReadyAt;

    /// <summary>
    /// Three throws in ten seconds is the pet having had enough, and it answers with a burst of ki.
    /// </summary>
    private void NoticeThrows(PetMotionState state)
    {
        if (state != PetMotionState.Thrown || _rigPreviousState == PetMotionState.Thrown)
        {
            return;
        }

        if (_throwStreak.Register(_clock.Elapsed.TotalSeconds))
        {
            _rig?.TriggerPowerBurst(PowerBurstSeconds);
            Say("Companion_PowerBurst");
        }
    }

    /// <summary>
    /// A pointer that goes round the pet's head a turn and a quarter makes it dizzy.
    /// </summary>
    private void WatchForCircling()
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (_pointerDown
            || _menuOpen
            || now < _dizzyReadyAt
            || _motion.State is not (PetMotionState.Walking or PetMotionState.Resting)
            || !GetCursorPos(out var cursor))
        {
            _circling.Reset();
            return;
        }

        var box = _motion.Box(CurrentBodies(CurrentLayout(), CurrentInk()));
        var inner = Math.Max(24, box.Width * CirclingInnerFraction);

        if (_circling.Observe(
                now,
                cursor.X,
                cursor.Y,
                box.CenterX,
                box.Top + (box.Height * 0.3),
                inner,
                inner + (CirclingReach * StageScale)))
        {
            BecomeDizzy(now);
        }
    }

    private void BecomeDizzy(double now)
    {
        if (_rig is null || _motion.IsClimbing || _settings.ReduceMotion)
        {
            return;
        }

        _dizzyReadyAt = now + DizzyCooldownSeconds;
        _rig.PlayDizzy(DizzySeconds);
        BeginRest(_clock.Elapsed, seconds: DizzySeconds, pose: PetRestPose.Sit);
        Say("Companion_Dizzy");
    }

    /// <summary>A click that is not a drag: a drawn character flinches, or glares, as the engine goes on.</summary>
    private void FlinchIfDrawn() => _rig?.Poke();

    /// <summary>Has the drawn character act out what it was just given.</summary>
    private void ActOutCare(CompanionCareAction action) => _rig?.PlayCare(action, CarePropSeconds);
}
