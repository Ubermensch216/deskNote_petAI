using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskNote.App.Views;

/// <summary>
/// The signature moves, from the window's side: starting one, moving the pet for it, and the gold
/// bubble that says what it is.
/// </summary>
public sealed partial class DesktopPetWindow
{
    private bool _rigSignatureActive;

    /// <summary>
    /// Carries out what a signature move asks of the pet's place: a teleport somewhere else on the
    /// floor, a ride on a cloud along it.
    /// </summary>
    private void ApplySignatureMovement(RigFrame frame, double elapsed)
    {
        if (frame.Teleport)
        {
            TeleportAlongFloor();
        }

        if (frame.DriftPixelsPerSecond <= 0)
        {
            return;
        }

        var layout = CurrentLayout();
        var bodies = CurrentBodies(layout, CurrentInk());
        var area = AreaAt(_motion.CenterX, _motion.CenterY);
        var half = bodies.FloorWidth / 2;
        var min = area.Left + half;
        var max = Math.Max(min, area.Right - half);

        // The cloud turns round at the ends of the floor instead of carrying the pet off it.
        var next = _motion.CenterX + (_motion.Direction * frame.DriftPixelsPerSecond * StageScale * elapsed);
        if (next <= min || next >= max)
        {
            _motion.Direction = -_motion.Direction;
            next = Math.Clamp(next, min, max);
        }

        _motion.CenterX = next;
        PlaceWindow(layout, CurrentInk(), bodies);
    }

    private void TeleportAlongFloor()
    {
        var layout = CurrentLayout();
        var bodies = CurrentBodies(layout, CurrentInk());
        var area = AreaAt(_motion.CenterX, _motion.CenterY);
        var half = bodies.FloorWidth / 2;
        var min = area.Left + half;
        var max = Math.Max(min, area.Right - half);

        var target = min + (_behaviour.NextDouble() * (max - min));

        // Far enough that it is plainly somewhere else.
        var minimumHop = bodies.FloorWidth * 3;
        if (Math.Abs(target - _motion.CenterX) < minimumHop)
        {
            target = _motion.CenterX < (min + max) / 2
                ? Math.Min(max, _motion.CenterX + (bodies.FloorWidth * 5))
                : Math.Max(min, _motion.CenterX - (bodies.FloorWidth * 5));
        }

        _motion.Direction = target < _motion.CenterX ? -1 : 1;
        _motion.CenterX = target;
        PlaceWindow(layout, CurrentInk(), bodies);
    }

    /// <summary>
    /// Puts the move's name and its shout in a gold bubble for as long as the move lasts, and gives
    /// the ordinary bubble back when it is over.
    /// </summary>
    private void ApplySignatureBubble(RigFrame frame)
    {
        if (frame.ShowsSignature)
        {
            var text = RigSignatures.Text(_rigCharacter, frame.Signature, Strings.ActiveLocale);
            SpeechText.Text = text.Bubble(frame.SignatureTime);
            SpeechBubble.Visibility = Visibility.Visible;
            if (!_rigSignatureActive)
            {
                _rigSignatureActive = true;
                PaintBubble(gold: true);
            }

            // The idle bubble timer must not close it halfway through a Kamehameha.
            _bubbleTimer.Stop();
            ScheduleIdleChatter();
            UpdateOrnamentVisibility();
        }
        else if (_rigSignatureActive)
        {
            _rigSignatureActive = false;
            PaintBubble(gold: false);
            SpeechBubble.Visibility = Visibility.Collapsed;
            UpdateOrnamentVisibility();
        }
    }

    private void PaintBubble(bool gold)
    {
        if (gold)
        {
            SpeechBubble.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xF2, 0xFF, 0xF4, 0xC4));
            SpeechBubble.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xB0, 0x6C, 0x0E));
            SpeechText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x60, 0x34, 0x08));
        }
        else
        {
            SpeechBubble.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xEE, 0x20, 0x21, 0x24));
            SpeechBubble.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x5F, 0xFF, 0xFF, 0xFF));
            SpeechText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        }
    }

    /// <summary>
    /// Starts a signature move, now if the pet is on its feet and as soon as it has landed if not.
    /// </summary>
    private void StartSignature(int index)
    {
        if (_rig is null || !_placed || _settings.ReduceMotion)
        {
            return;
        }

        var duration = _rig.ArmSignature(index);

        if (_motion.IsClimbing)
        {
            // It cannot do a Kamehameha up a wall: it comes down first, and says so.
            LetGo();
            Say("Companion_SignatureComeDown");
        }

        BeginRest(_clock.Elapsed, seconds: duration, pose: PetRestPose.Sit);
    }

    /// <summary>
    /// Now and then a pet that sits down for a rest uses the sitting to show off.
    /// </summary>
    private void MaybeStartSignature()
    {
        if (_rig is null
            || _motion.State != PetMotionState.Resting
            || _rig.PendingSignature >= 0
            || (_carePropElapsed < CarePropSeconds && CareProp.Opacity > 0))
        {
            return;
        }

        if (_settings.SignatureFrequency == SignatureFrequency.Off
            || _behaviour.NextDouble() >= _settings.SignatureFrequency.Chance()
            || !PresentationModeDetector.AllowsUserNotification())
        {
            return;
        }

        StartSignature(_behaviour.Next(RigSignatures.PerCharacter));
    }

    /// <summary>The right-click menu's entries for the moves, for a drawn character.</summary>
    private MenuFlyoutSubItem? SignatureMenu()
    {
        if (_rig is null)
        {
            return null;
        }

        var menu = new MenuFlyoutSubItem { Text = Strings.Get("Companion_MenuSignature") };
        for (var index = 0; index < RigSignatures.PerCharacter; index++)
        {
            var move = index;
            var text = RigSignatures.Text(_rigCharacter, move, Strings.ActiveLocale);
            var item = new MenuFlyoutItem { Text = $"★ {text.Name}" };
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(item, text.Description);
            item.Click += (_, _) => StartSignature(move);
            menu.Items.Add(item);
        }

        menu.IsEnabled = !_settings.ReduceMotion;
        return menu;
    }
}
