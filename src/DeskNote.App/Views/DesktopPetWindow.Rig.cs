using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskNote.App.Services;
using DeskNote.Companion.Core;
using DeskNote.Companion.Rig;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskNote.App.Views;

/// <summary>
/// The window's half of the drawn characters: giving them a surface to be painted on and feeding
/// the director what the motion engine has decided.
/// </summary>
/// <remarks>
/// <para>
/// The animals are sprite sheets played by a strip that slides along an <c>Image</c>. The five
/// characters are painted from parts, one frame at a time, into a bitmap the window then shows.
/// Everything that is about <em>where</em> the pet is - the motion engine, the placement, the
/// transforms that lay it against a wall - is the same for both, because the characters are drawn
/// standing, facing right, in a frame of the same proportions, and the window turns that picture
/// exactly as it turns a sprite.
/// </para>
/// <para>
/// What the characters cannot share is the room they need. A beam crosses the screen, an arm
/// stretches, a great ape is twice as tall; so the window is wider while one of them is out, and
/// the surface is far larger than the frame the pet stands in.
/// </para>
/// </remarks>
public sealed partial class DesktopPetWindow
{
    /// <summary>How wide the window is for a drawn character, in device-independent pixels.</summary>
    private const double RigStageWidth = 520;

    /// <summary>How long a quick succession of throws has to be to count as having had enough.</summary>
    private const double PowerBurstWindowSeconds = 10;

    private const int PowerBurstThrows = 3;
    private const double RigCarePropOffset = 64;
    private const double PowerBurstSeconds = 3.5;

    private RigDirector? _rig;
    private RigSurface? _rigSurface;
    private WriteableBitmap? _rigBitmap;
    private RigCharacter _rigCharacter;
    private CompanionPetKind _rigKind;
    private double _lastMotionX;
    private double _lastMotionY;
    private bool _rigSignatureActive;
    private int _rigThrowCount;
    private double _rigThrowWindowStart;
    private PetMotionState _rigPreviousState = PetMotionState.Walking;

    private bool IsRig => _rig is not null;

    /// <summary>The window's size in physical pixels: the fixed one for the animals, the stage for a drawn character.</summary>
    private int CurrentWindowWidth => IsRig ? (int)Math.Round(RigStageWidth * StageScale) : WindowWidth;

    private int CurrentWindowHeight => IsRig ? (int)Math.Round(WindowHeight * StageScale) : WindowHeight;

    /// <summary>The display's scale, read from the window itself so that it is right before the content has been laid out.</summary>
    private double StageScale
    {
        get
        {
            var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            var dpi = GetDpiForWindow(window);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
    }

    /// <summary>
    /// Switches the window between the sprite strip and a drawn character as the selected pet changes.
    /// </summary>
    private void ApplyRigMode(CompanionPetKind pet)
    {
        var drawn = RigCharacters.TryGet(pet, out var character);
        if (drawn && _rig is not null && _rigKind == pet)
        {
            return;
        }

        _rigKind = pet;
        if (!drawn)
        {
            if (_rig is null)
            {
                return;
            }

            _rig = null;
            _rigSurface?.Dispose();
            _rigSurface = null;
            _rigBitmap = null;
            RigImage.Source = null;
            RigImage.Visibility = Visibility.Collapsed;
            SpriteStrip.Visibility = Visibility.Visible;
            CareProp.HorizontalAlignment = HorizontalAlignment.Right;
            CareProp.Margin = new Thickness(0, 0, 18, 26);
            ResizeStage();
            return;
        }

        _rigCharacter = character;
        _rig = new RigDirector(character);
        _rigSurface?.Dispose();
        _rigSurface = null;
        _rigBitmap = null;
        SpriteStrip.Visibility = Visibility.Collapsed;
        RigImage.Visibility = Visibility.Visible;
        _lastMotionX = _motion.CenterX;
        _lastMotionY = _motion.CenterY;

        // The prop that goes with a care action sat at the right-hand edge of a window the width of
        // the pet; in the wide one that is miles away, so it is set beside the figure instead. A
        // centred child shifts by half its left margin.
        CareProp.HorizontalAlignment = HorizontalAlignment.Center;
        CareProp.Margin = new Thickness(2 * RigCarePropOffset, 0, 0, 26);
        ResizeStage();
    }

    /// <summary>
    /// Sizes the window and everything laid out against it for whichever kind of pet is showing.
    /// </summary>
    private void ResizeStage()
    {
        AppWindow.Resize(new Windows.Graphics.SizeInt32(CurrentWindowWidth, CurrentWindowHeight));
        _lastWindowPosition = new Windows.Graphics.PointInt32(int.MinValue, int.MinValue);
        ApplyPetSize(_settings.PetSize);
        if (_placed)
        {
            PlaceWindow();
        }
    }

    /// <summary>Places the drawn surface inside the frame so that the figure's feet land on the frame's foot line.</summary>
    private void LayOutRigImage()
    {
        if (!IsRig)
        {
            return;
        }

        var width = RigStageWidth;
        var height = WindowHeight;
        RigImage.Width = width;
        RigImage.Height = height;
        Canvas.SetLeft(RigImage, (_frameWidth - width) / 2);
        Canvas.SetTop(RigImage, _frameHeight - height);

        // Nothing the figure does may be cut off at the frame, so the clip is wide open.
        SpriteClip.Rect = new Windows.Foundation.Rect(-width, -height * 2, width * 3, height * 4);
    }

    /// <summary>How tall the character stands on the screen, head to foot, before the growth stage scales it.</summary>
    private double RigCharacterPixels() =>
        RigCharacters.HeightFraction(_rigCharacter) * _frameHeight * StageScale;

    private void EnsureRigSurface()
    {
        var scale = StageScale;
        var width = (int)Math.Round(RigStageWidth * scale);
        var height = (int)Math.Round(WindowHeight * scale);

        if (_rigSurface is null)
        {
            _rigSurface = new RigSurface(_rigCharacter, width, height);
        }
        else
        {
            _rigSurface.Resize(width, height);
        }

        if (_rigBitmap is null || _rigBitmap.PixelWidth != width || _rigBitmap.PixelHeight != height)
        {
            _rigBitmap = new WriteableBitmap(width, height);
            RigImage.Source = _rigBitmap;
        }
    }

    /// <summary>Paints the director's frame and shows it, unless it would look the same as the last one.</summary>
    private void PaintRig(RigFrame frame)
    {
        EnsureRigSurface();

        var scale = StageScale;
        var surface = _rigSurface!;
        var characterPixels = RigCharacterPixels();

        // The feet stand on the frame's foot line, which is a fraction of the frame's height up
        // from the bottom of the window.
        var footY = surface.Height - ((1 - RigCharacters.FootFraction) * _frameHeight * scale);
        if (!surface.Render(frame, new RigPlacement(surface.Width / 2.0, footY, characterPixels)))
        {
            return;
        }

        var pixels = surface.Pixels;
        using var stream = _rigBitmap!.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(pixels);
        _rigBitmap.Invalidate();
    }

    // ------------------------------------------------------------------ each tick

    private RigInput RigInputNow(double elapsed, bool held)
    {
        var moved = Math.Sqrt(
            Math.Pow(_motion.CenterX - _lastMotionX, 2) + Math.Pow(_motion.CenterY - _lastMotionY, 2));
        var velocityX = elapsed > 0 ? (_motion.CenterX - _lastMotionX) / elapsed : 0;
        _lastMotionX = _motion.CenterX;
        _lastMotionY = _motion.CenterY;

        // The picture is drawn facing right and mirrored for the other way, so what the director
        // sees of the pointer is mirrored with it.
        var direction = _motion.Direction;
        var growth = Math.Max(_growthScale, 0.05);
        double grabX = 0;
        double grabY = 0;
        if (held)
        {
            var layout = CurrentLayout();
            var bodies = CurrentBodies(layout, CurrentInk());
            grabX = _grabOffsetX * direction / growth;
            grabY = (_grabOffsetY - (bodies.FloorHeight / 2)) / growth;
        }

        return new RigInput(
            _motion.State,
            _motion.RestPose,
            _motion.RestSerial,
            _motion.StateElapsed,
            _motion.RestElapsed,
            _motion.RestDuration,
            moved / growth,
            velocityX * direction,
            RigCharacterPixels(),
            grabX,
            grabY,
            _motion.Descending,
            _needs.Fullness <= CompanionCareRules.FeedThreshold);
    }

    /// <summary>Advances the drawn character by one tick and shows it.</summary>
    private void AdvanceRig(double elapsed, bool held = false)
    {
        if (_rig is null)
        {
            return;
        }

        WatchForCircling();
        var input = RigInputNow(elapsed, held);
        NoticeThrows(input.State);
        var frame = _rig.Advance(elapsed, input);

        // The tilt in the air is the window's, as it is for the animals: it turns the whole drawing
        // about the feet and is mirrored with it.
        switch (_motion.State)
        {
            case PetMotionState.Falling:
                _poseAngle = 3 * Math.Sin(_motion.StateElapsed * 2.4);
                break;

            case PetMotionState.Thrown:
                _poseAngle = Math.Clamp(_motion.VelocityX / 900 * 30, -ThrownTiltDegrees, ThrownTiltDegrees)
                    + (Math.Sin(_motion.StateElapsed * 13) * 6);
                break;

            default:
                _poseAngle = 0;
                break;
        }

        _poseScale = 1;
        ApplySignatureMovement(frame, elapsed);
        PaintRig(frame);
        ApplySignatureBubble(frame);
        ApplyFacingTransform();
        _rigPreviousState = _motion.State;
    }

    /// <summary>
    /// Three throws in ten seconds is the pet having had enough, and it answers with a burst of ki.
    /// </summary>
    private void NoticeThrows(PetMotionState state)
    {
        if (state != PetMotionState.Thrown || _rigPreviousState == PetMotionState.Thrown)
        {
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (_rigThrowCount == 0 || now - _rigThrowWindowStart > PowerBurstWindowSeconds)
        {
            _rigThrowCount = 0;
            _rigThrowWindowStart = now;
        }

        _rigThrowCount++;
        if (_rigThrowCount >= PowerBurstThrows)
        {
            _rigThrowCount = 0;
            _rig?.TriggerPowerBurst(PowerBurstSeconds * 3);
            Say("Companion_PowerBurst");
        }
    }

    // ------------------------------------------------------------------ signature moves

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

    // ------------------------------------------------------------------ a pointer going round and round

    private const double DizzySeconds = 4;
    private const double DizzyCooldownSeconds = 20;
    private const double CirclingTurns = 1.25;

    private double _orbitAngle;
    private double _orbitTotal;
    private double _orbitSeenAt;
    private double _dizzyReadyAt;
    private bool _orbitTracking;

    /// <summary>
    /// A pointer that goes round the pet's head a turn and a quarter makes it dizzy.
    /// </summary>
    /// <remarks>
    /// Nothing else in the app asks the user to move the pointer in circles, and that is the point:
    /// it is a reaction nobody is told about. The thresholds are set so that wandering across the
    /// pet does not trip it - the pointer has to stay in a ring around the head, keep turning the
    /// same way, and not stop for long.
    /// </remarks>
    private void WatchForCircling()
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (_pointerDown
            || _menuOpen
            || now < _dizzyReadyAt
            || _motion.State is not (PetMotionState.Walking or PetMotionState.Resting)
            || !GetCursorPos(out var cursor))
        {
            _orbitTracking = false;
            return;
        }

        var box = _motion.Box(CurrentBodies(CurrentLayout(), CurrentInk()));
        var headX = box.CenterX;
        var headY = box.Top + (box.Height * 0.3);
        var dx = cursor.X - headX;
        var dy = cursor.Y - headY;
        var radius = Math.Sqrt((dx * dx) + (dy * dy));
        var inner = Math.Max(24, box.Width * 0.45);

        if (radius < inner || radius > inner + (220 * StageScale))
        {
            _orbitTracking = false;
            return;
        }

        var angle = Math.Atan2(dy, dx);
        if (!_orbitTracking || now - _orbitSeenAt > 0.45)
        {
            _orbitTracking = true;
            _orbitAngle = angle;
            _orbitTotal = 0;
            _orbitSeenAt = now;
            return;
        }

        var delta = angle - _orbitAngle;
        while (delta > Math.PI)
        {
            delta -= 2 * Math.PI;
        }

        while (delta < -Math.PI)
        {
            delta += 2 * Math.PI;
        }

        if (Math.Abs(delta) < 0.02)
        {
            return;
        }

        _orbitAngle = angle;
        _orbitSeenAt = now;

        // Turning back the other way starts the count again.
        _orbitTotal = Math.Sign(delta) == Math.Sign(_orbitTotal) || _orbitTotal == 0 ? _orbitTotal + delta : delta;

        if (Math.Abs(_orbitTotal) >= CirclingTurns * 2 * Math.PI)
        {
            _orbitTracking = false;
            _orbitTotal = 0;
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

    // ------------------------------------------------------------------ touch and care

    /// <summary>A click that is not a drag: a drawn character flinches, or glares, as the engine goes on.</summary>
    private void FlinchIfDrawn() => _rig?.Poke();

    /// <summary>Has the drawn character act out what it was just given.</summary>
    private void ActOutCare(CompanionCareAction action) => _rig?.PlayCare(action, CarePropSeconds);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    /// <summary>
    /// How far up the frame the top of the figure's head reaches, as a fraction of the frame.
    /// </summary>
    /// <remarks>
    /// The sprites are measured; the drawn characters are laid out by rule, so this is arithmetic:
    /// the foot line is a fixed fraction up the frame and the figure's height is its own. A pet
    /// that is sitting or asleep is a little lower.
    /// </remarks>
    private double RigHeadTopFraction(bool sitting) =>
        (1 - RigCharacters.FootFraction) + (RigCharacters.HeightFraction(_rigCharacter) * (sitting ? 0.9 : 1.0));

    private double RigSpeedFactor() => IsRig ? RigCharacters.SpeedFactor(_rigCharacter) : 1;

    /// <summary>Paints the character standing still, for a pet that has been told not to move.</summary>
    private void PaintStandingRig()
    {
        if (_rig is null)
        {
            return;
        }

        var input = new RigInput(
            PetMotionState.Walking,
            PetRestPose.Sit,
            0,
            0,
            0,
            0,
            0,
            0,
            RigCharacterPixels(),
            0,
            0,
            false,
            false);
        PaintRig(_rig.Advance(0, input));
    }
}
