using DeskNote.Companion.Core;
using System.Drawing;

namespace DeskNote.Companion.Rig;

/// <summary>
/// Draws one of the five characters part by part: body, arms on two joints, legs, hair, face, the
/// tail or cape, and the ki around them.
/// </summary>
/// <remarks>
/// <para>
/// The drawing was made for a desktop pet of its own, in the fan project these characters come
/// from, and is kept in that project's compact style. Everything in <see cref="FigurePainter"/> is
/// written against a reference figure that is 571 units tall; <see cref="Draw"/> is told how many
/// pixels one unit is, and nothing else about the size of the figure matters.
/// </para>
/// </remarks>
internal sealed class FigureRig
{
    /// <summary>How tall the reference figure is, in the units every length in a <see cref="RigPose"/> is given in.</summary>
    public const float ReferenceHeight = 571f;

    /// <summary>Where the hands gather in front of the belly, in reference units from the top.</summary>
    public const float BellyY = 430f;

    private readonly RigCharacter _character;

    public FigureRig(RigCharacter character) => _character = character;

    /// <summary>The soles of the feet, in the same units.</summary>
    public static float FootY => ReferenceHeight;

    /// <summary>How wide the body is, for laying it on its back.</summary>
    private float BodyWidth => _character == RigCharacter.Piccolo ? 210f : 190f;

    /// <summary>
    /// Paints <paramref name="pose"/> with the origin of <paramref name="g"/> at the middle of the
    /// feet. <c>Lie</c> lays the whole body down by up to ninety degrees.
    /// </summary>
    /// <param name="k">Pixels in one reference unit.</param>
    public void Draw(Graphics g, RigPose pose, float k, bool faceRight)
    {
        if (Math.Abs(pose.Lie) < .01f)
        {
            Paint(g, pose, k, faceRight);
            return;
        }

        var saved = g.Save();
        var angle = (faceRight ? -90f : 90f) * pose.Lie;
        var amount = Math.Abs(pose.Lie);

        // Lying down puts the body on the floor, moved sideways so its middle is where it stood.
        g.TranslateTransform(-Math.Sign(angle) * ReferenceHeight * k * .5f * amount, -BodyWidth * .5f * k * amount);
        g.RotateTransform(angle);
        Paint(g, pose, k, faceRight);
        g.Restore(saved);
    }

    private void Paint(Graphics g, RigPose pose, float k, bool faceRight)
    {
        using var painter = new FigurePainter(g, pose, _character, k, faceRight);
        painter.Run();
    }
}
