using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DeskNote.Companion.Core;

namespace DeskNote.Companion.Rig;

/// <summary>Where the figure stands on the surface, and how big it is.</summary>
/// <param name="FootX">Horizontal middle of the soles, in surface pixels.</param>
/// <param name="FootY">Height of the soles, in surface pixels.</param>
/// <param name="CharacterPixels">Head to foot, in surface pixels.</param>
public readonly record struct RigPlacement(double FootX, double FootY, double CharacterPixels);

/// <summary>
/// A bitmap that one character is painted onto, frame after frame.
/// </summary>
/// <remarks>
/// <para>
/// The figure is always painted facing right, the way every sprite sheet in the app is drawn, so
/// that the window can mirror it for the other direction with the same transform it already uses
/// for the animals. Turning it round inside the painter would leave the window with two ways to
/// face left.
/// </para>
/// <para>
/// The surface is far larger than the figure. A beam, a stretched arm or a great ape has to have
/// somewhere to go, and everything that is not the figure is left fully transparent.
/// </para>
/// </remarks>
public sealed class RigSurface : IDisposable
{
    private readonly FigureRig _rig;
    private readonly RigCharacter _character;
    private Bitmap _bitmap;
    private Bitmap? _layer;
    private long _lastKey = long.MinValue;
    private byte[] _pixels;

    public RigSurface(RigCharacter character, int width, int height)
    {
        _character = character;
        _rig = new FigureRig(character);
        Width = width;
        Height = height;
        _bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        _pixels = new byte[width * height * 4];
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The last frame painted: premultiplied BGRA, top row first, no padding between rows.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>Resizes the surface; the next paint is always drawn.</summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        _bitmap.Dispose();
        _layer?.Dispose();
        _layer = null;
        Width = width;
        Height = height;
        _bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        _pixels = new byte[width * height * 4];
        _lastKey = long.MinValue;
    }

    /// <summary>
    /// Paints a frame, unless it would look the same as the last one.
    /// </summary>
    /// <returns>True when <see cref="Pixels"/> changed.</returns>
    public bool Render(RigFrame frame, RigPlacement placement)
    {
        var key = KeyOf(frame, placement);
        if (key == _lastKey)
        {
            return false;
        }

        _lastKey = key;
        Paint(_bitmap, frame, placement);
        Copy(_bitmap);
        return true;
    }

    private static long KeyOf(RigFrame frame, RigPlacement placement)
    {
        unchecked
        {
            var h = frame.Pose.Hash();

            void Add(double value, double quantum) => h = (h ^ (long)Math.Round(value * quantum)) * 1099511628211L;

            Add(frame.LiftPixels, 2);
            Add(frame.JitterXPixels, 2);
            Add(frame.Alpha, 50);
            Add(frame.Scale, 40);
            Add(frame.Angle, 4);
            Add(frame.Pivot.X, 1);
            Add(frame.Pivot.Y, 1);
            Add((int)frame.Decor, 1);
            Add(frame.Decor == RigDecor.None ? 0 : frame.DecorPhase, 24);
            Add(frame.Moon, 30);
            Add(placement.FootX, 4);
            Add(placement.FootY, 4);
            Add(placement.CharacterPixels, 4);
            return h;
        }
    }

    /// <summary>Paints a frame onto any bitmap, for callers that want a picture rather than a stream of them.</summary>
    public void Paint(Bitmap target, RigFrame frame, RigPlacement placement)
    {
        var k = (float)(placement.CharacterPixels / FigureRig.ReferenceHeight);
        var scale = placement.CharacterPixels / (RigCharacters.ReferencePixels * RigCharacters.HeightFactor(_character));

        using var g = Graphics.FromImage(target);
        g.Clear(Color.Transparent);
        Quality(g);

        var origin = new PointF(
            (float)(placement.FootX + frame.JitterXPixels),
            (float)(placement.FootY - frame.LiftPixels));

        // The picture is turned about a point (a pet in the hand hangs from where it was taken
        // hold of) and then scaled about the feet, so the great ape grows out of the floor and the
        // tilt is not stretched by it.
        g.TranslateTransform(origin.X, origin.Y);
        g.TranslateTransform(frame.Pivot.X * k, frame.Pivot.Y * k);
        g.RotateTransform(frame.Angle);
        g.TranslateTransform(-frame.Pivot.X * k, -frame.Pivot.Y * k);
        g.ScaleTransform(frame.Scale, frame.Scale);
        using var body = g.Transform;

        if (frame.Alpha < 0.995f)
        {
            DrawFaded(g, frame, k, body);
        }
        else
        {
            _rig.Draw(g, frame.Pose, k, faceRight: true);
        }

        // Things that go with the body but are not of it: they do not fade with a vanishing pet.
        g.Transform = body;
        var width = (float)(RigCharacters.NominalWidthUnits * k);
        var height = (float)placement.CharacterPixels;
        RigEffects.DrawDecor(g, frame, width, height, (float)scale);

        if ((frame.Decor & RigDecor.Moon) != 0)
        {
            g.ResetTransform();
            var radius = height * 0.24f;
            RigEffects.DrawMoon(
                g,
                new PointF(
                    (float)placement.FootX + (width * 0.5f * frame.Scale) + (radius * 1.35f),
                    (float)(placement.FootY - (height * frame.Scale) - (radius * 0.2f))),
                radius,
                frame.Moon);
        }
    }

    private void DrawFaded(Graphics g, RigFrame frame, float k, Matrix body)
    {
        // A vanishing pet: the body is painted onto a layer of its own and laid down with the
        // opacity it has, so that its overlapping parts fade together instead of showing through
        // one another.
        var layer = _layer;
        if (layer is null || layer.Width != Width || layer.Height != Height)
        {
            layer?.Dispose();
            _layer = layer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        }

        using (var lg = Graphics.FromImage(layer))
        {
            lg.Clear(Color.Transparent);
            Quality(lg);
            lg.Transform = body;
            _rig.Draw(lg, frame.Pose, k, faceRight: true);
        }

        var alpha = Math.Max(0, Math.Min(1, frame.Alpha));
        if (alpha <= 0.01f)
        {
            return;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        using var restore = g.Transform;
        g.ResetTransform();
        g.DrawImage(layer, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel, attributes);
        g.Transform = restore;
    }

    private static void Quality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private void Copy(Bitmap source)
    {
        var data = source.LockBits(
            new Rectangle(0, 0, Width, Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            var rowBytes = Width * 4;
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), _pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            source.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        _bitmap.Dispose();
        _layer?.Dispose();
    }
}
