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

    private const double RigCarePropOffset = 64;

    private RigDirector? _rig;
    private RigSurface? _rigSurface;
    private WriteableBitmap? _rigBitmap;
    private RigCharacter _rigCharacter;
    private CompanionPetKind _rigKind;
    private double _lastMotionX;
    private double _lastMotionY;
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
