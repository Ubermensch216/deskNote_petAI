namespace DeskNote.Companion.Core;

/// <summary>An axis-aligned rectangle, in whatever pixel space the caller is working in.</summary>
public readonly record struct PetBox(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterX => Left + (Width / 2);

    public double CenterY => Top + (Height / 2);

    /// <summary>Grows the box by <paramref name="margin"/> on every side.</summary>
    public PetBox Inflate(double margin) =>
        new(Left - margin, Top - margin, Width + (2 * margin), Height + (2 * margin));

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <summary>
/// Where the drawing sits inside its frame, as fractions of the frame's width and height.
/// </summary>
/// <remarks>
/// The sprite sheets pad every frame, and the padding differs by species: a duck fills a little
/// over half the frame's width, a dragon all of it. The window has to be placed so that what is
/// <em>drawn</em> touches the edge of the screen, not what is padded, and that needs the drawing's
/// own bounds.
/// </remarks>
public readonly record struct PetInk(double Left, double Top, double Right, double Bottom);

/// <summary>Which point of the frame a pose is turned about.</summary>
public enum PetPivot
{
    /// <summary>Bottom centre of the frame: a pet that sways does so about its feet.</summary>
    Feet,

    /// <summary>Centre of the frame: a pet lying on a wall or hanging from the ceiling stays inside its window.</summary>
    Centre,
}

/// <summary>
/// How a pose is drawn: turned, then scaled, about a pivot - the order the window's
/// <c>TransformGroup</c> applies them in.
/// </summary>
/// <param name="Rotation">Degrees, clockwise, as in XAML.</param>
/// <param name="ScaleX">Negative mirrors the pet left to right.</param>
/// <param name="ScaleY">Negative turns the pet upside down.</param>
public readonly record struct PetTransform(
    double Rotation,
    double ScaleX,
    double ScaleY,
    PetPivot Pivot);

/// <summary>
/// The pet's window and the sprite frame inside it, in pixels.
/// </summary>
/// <remarks>
/// The frame is as wide and tall as the size setting makes it, centred in the window, and stands
/// on the window's bottom edge; the growth stage scales the drawing on top of that, which is part
/// of the <see cref="PetTransform"/> and not of the frame.
/// </remarks>
public readonly record struct PetLayout(
    double WindowWidth,
    double WindowHeight,
    double FrameWidth,
    double FrameHeight);

/// <summary>
/// Works out where the visible pet is inside its window for any pose.
/// </summary>
/// <remarks>
/// This is pure arithmetic and the whole of the placement problem. To make a pet stand on the
/// floor, cling to the right wall or hang from the ceiling, the window is moved so that the
/// pose's ink box lands where it should; the window itself overhangs the screen by whatever
/// transparent padding is left over. Getting it wrong shows as a pet floating a hand's breadth
/// off the wall it is meant to be climbing.
/// </remarks>
public static class PetPoseGeometry
{
    /// <summary>The drawn pet's bounding box, relative to the window's top-left corner.</summary>
    public static PetBox InkBox(PetLayout layout, PetInk ink, PetTransform transform)
    {
        var frameLeft = (layout.WindowWidth - layout.FrameWidth) / 2;
        var frameTop = layout.WindowHeight - layout.FrameHeight;

        var pivotX = frameLeft + (layout.FrameWidth / 2);
        var pivotY = transform.Pivot == PetPivot.Feet
            ? layout.WindowHeight
            : frameTop + (layout.FrameHeight / 2);

        var radians = transform.Rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var (fx, fy) in new[]
        {
            (ink.Left, ink.Top),
            (ink.Right, ink.Top),
            (ink.Right, ink.Bottom),
            (ink.Left, ink.Bottom),
        })
        {
            // Relative to the pivot, y down: a positive angle turns clockwise on the screen.
            var x = frameLeft + (fx * layout.FrameWidth) - pivotX;
            var y = frameTop + (fy * layout.FrameHeight) - pivotY;

            var turnedX = (x * cos) - (y * sin);
            var turnedY = (x * sin) + (y * cos);

            var finalX = pivotX + (turnedX * transform.ScaleX);
            var finalY = pivotY + (turnedY * transform.ScaleY);

            minX = Math.Min(minX, finalX);
            maxX = Math.Max(maxX, finalX);
            minY = Math.Min(minY, finalY);
            maxY = Math.Max(maxY, finalY);
        }

        return new PetBox(minX, minY, maxX - minX, maxY - minY);
    }
}
