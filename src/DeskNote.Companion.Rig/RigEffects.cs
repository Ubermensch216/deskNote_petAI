using System.Drawing;
using System.Drawing.Drawing2D;
using DeskNote.Companion.Core;

namespace DeskNote.Companion.Rig;

/// <summary>
/// The things drawn around a figure rather than on it: gold stars for a signature move, dust and
/// speed lines behind a runner, a full moon, a bouquet, music notes.
/// </summary>
internal static class RigEffects
{
    /// <param name="g">Origin at the middle of the feet.</param>
    /// <param name="frame">What to draw.</param>
    /// <param name="rig">The body the effects belong to.</param>
    /// <param name="w">Width of the body's walking box, in pixels.</param>
    /// <param name="h">Height of the body, in pixels.</param>
    /// <param name="s">Size of the figure against the one the effects were drawn for.</param>
    public static void DrawDecor(Graphics g, RigFrame frame, DbRig rig, float w, float h, float s)
    {
        var decor = frame.Decor;
        if (decor == RigDecor.None)
        {
            return;
        }

        var t = frame.DecorPhase;
        using var ink = new Pen(Color.FromArgb(43, 36, 42), 2.2f * s)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        if ((decor & RigDecor.Speed) != 0)
        {
            // Behind the runner: dust off the floor and three streaks. Figures face right here and
            // the window mirrors them, so "behind" is always to the left.
            const float Side = -1;
            for (var k = 0; k < 4; k++)
            {
                var p = (float)(((t * 3.2) + (k * 0.25)) % 1);
                var px = Side * ((w * 0.42f) + ((2 + (p * 14) + (k * 2)) * s));
                var py = (-1.5f * s) - (p * 5 * s);
                using var dust = new SolidBrush(Color.FromArgb((int)(150 * (1 - p)), 232, 224, 212));
                g.FillEllipse(dust, px - ((1.8f + (p * 2.4f)) * s), py - ((1.5f + (p * 2f)) * s), (3.6f + (p * 4.8f)) * s, (3f + (p * 4f)) * s);
            }

            using var line = new Pen(Color.FromArgb(190, 120, 120, 135), 1.8f * s)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            for (var i = 0; i < 3; i++)
            {
                var y = -h * (0.22f + (i * 0.26f));
                var off = (float)(((t * 70) + (i * 9)) % 12) * s;
                var x0 = Side * ((w * 0.5f) + (3 * s) + off);
                g.DrawLine(line, x0, y, x0 + (Side * (8 + (i * 3)) * s), y);
            }
        }

        if ((decor & RigDecor.SigStars) != 0)
        {
            DrawSigStars(g, w, h, s, t);
        }

        if ((decor & RigDecor.Stars) != 0)
        {
            using var stars = new Pen(Color.FromArgb(222, 172, 71), 2 * s)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            for (var i = 0; i < 3; i++)
            {
                var a = (t * 3) + (i * Math.PI * 2 / 3);
                var x = (float)Math.Cos(a) * w * 0.35f;
                var y = -h - (6 * s) + ((float)Math.Sin(a) * 6 * s);
                g.DrawLine(stars, x - (3 * s), y, x + (3 * s), y);
                g.DrawLine(stars, x, y - (3 * s), x, y + (3 * s));
            }
        }

        if ((decor & RigDecor.Flower) != 0)
        {
            DrawBouquet(g, rig, h / rig.RefH, s, ink);
        }

        if ((decor & RigDecor.Notes) != 0)
        {
            using var note = new SolidBrush(Color.FromArgb(176, 111, 160));
            using var stem = new Pen(Color.FromArgb(176, 111, 160), 2 * s);
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * ((w * 0.47f) + (8 * s));
                var y = (-h * 0.8f) + ((float)Math.Sin((t * 5) + side) * 4 * s);
                g.FillEllipse(note, x - (3 * s), y, 6 * s, 4 * s);
                g.DrawLine(stem, x + (2 * s), y + (2 * s), x + (2 * s), y - (10 * s));
                g.DrawLine(stem, x + (2 * s), y - (10 * s), x + (7 * s), y - (8 * s));
            }
        }
    }

    /// <summary>Gold stars that circle the body and twinkle: the mark of a signature move.</summary>
    private static void DrawSigStars(Graphics g, float w, float h, float s, float t)
    {
        for (var i = 0; i < 5; i++)
        {
            var a = (t * 1.9) + (i * Math.PI * 2 / 5);
            var x = (float)Math.Cos(a) * w * 0.6f;
            var y = (-h * 0.52f) + ((float)Math.Sin(a) * h * 0.52f);
            var twinkle = 0.75f + (0.25f * (float)Math.Sin((t * 8) + (i * 1.7)));
            var r = (4.2f + ((i % 2) * 1.6f)) * s * twinkle * 1.35f;
            var points = new PointF[10];
            for (var k = 0; k < 10; k++)
            {
                var an = (-Math.PI / 2) + (k * Math.PI / 5);
                var rr = k % 2 == 0 ? r : r * 0.42f;
                points[k] = new PointF(x + ((float)Math.Cos(an) * rr), y + ((float)Math.Sin(an) * rr));
            }

            using var glow = new SolidBrush(Color.FromArgb(70, 255, 220, 90));
            g.FillEllipse(glow, x - (r * 1.5f), y - (r * 1.5f), r * 3, r * 3);
            using var fill = new SolidBrush(Color.FromArgb(255, 255, 214, 48));
            using var pen = new Pen(Color.FromArgb(255, 176, 108, 14), 1.2f * s) { LineJoin = LineJoin.Round };
            g.FillPolygon(fill, points);
            g.DrawPolygon(pen, points);
        }
    }

    /// <summary>The full moon the kid sees before he turns into the great ape: a soft glow and craters.</summary>
    public static void DrawMoon(Graphics g, PointF c, float r, float a)
    {
        if (a <= 0.01f)
        {
            return;
        }

        var al = (int)(255 * Math.Min(1, a));
        r *= 0.6f + (0.4f * Math.Min(1, a));

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(c.X - (r * 1.7f), c.Y - (r * 1.7f), r * 3.4f, r * 3.4f);
            using var glow = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(al * 150 / 255, 255, 248, 190),
                SurroundColors = [Color.FromArgb(0, 255, 248, 190)],
            };
            g.FillPath(glow, path);
        }

        using var body = new SolidBrush(Color.FromArgb(al, 255, 247, 205));
        using var rim = new Pen(Color.FromArgb(al, 214, 190, 110), Math.Max(1.5f, r * 0.06f));
        using var crater = new SolidBrush(Color.FromArgb(al * 150 / 255, 226, 208, 150));
        g.FillEllipse(body, c.X - r, c.Y - r, r * 2, r * 2);
        g.DrawEllipse(rim, c.X - r, c.Y - r, r * 2, r * 2);
        g.FillEllipse(crater, c.X - (r * 0.5f), c.Y - (r * 0.45f), r * 0.46f, r * 0.4f);
        g.FillEllipse(crater, c.X + (r * 0.15f), c.Y + (r * 0.1f), r * 0.56f, r * 0.5f);
        g.FillEllipse(crater, c.X - (r * 0.3f), c.Y + (r * 0.4f), r * 0.3f, r * 0.26f);
        g.FillEllipse(crater, c.X + (r * 0.3f), c.Y - (r * 0.55f), r * 0.24f, r * 0.22f);
    }

    /// <summary>A bouquet in both arms. The hands are the rig's, already folded at the belly.</summary>
    private static void DrawBouquet(Graphics g, DbRig rig, float k, float s, Pen ink)
    {
        var handY = (rig.BellyY - rig.FootY) * k;
        var x = 3 * s;
        var y = handY - (20 * s);

        using (var stem = new Pen(Color.FromArgb(90, 141, 97), 2.4f * s))
        {
            for (var j = -1; j <= 1; j++)
            {
                g.DrawLine(stem, x - (3 * s), y + (25 * s), x + (j * 9 * s), y - ((j == 0 ? 7 : 0) * s));
            }
        }

        using (var wrap = new SolidBrush(Color.FromArgb(255, 224, 231)))
        {
            var points = new[]
            {
                new PointF(x - (13 * s), y + (8 * s)),
                new PointF(x + (10 * s), y + (8 * s)),
                new PointF(x - (2 * s), y + (28 * s)),
            };
            g.FillPolygon(wrap, points);
            g.DrawPolygon(ink, points);
        }

        using (var paw = new SolidBrush(Color.FromArgb(255, 224, 190)))
        {
            for (var side = -1; side <= 1; side += 2)
            {
                var px = x - (2 * s) + (side * 7 * s);
                g.FillEllipse(paw, px - (4.5f * s), y + (18 * s), 9 * s, 7 * s);
                g.DrawEllipse(ink, px - (4.5f * s), y + (18 * s), 9 * s, 7 * s);
            }
        }

        for (var j = -1; j <= 1; j++)
        {
            var fx = x + (j * 9 * s);
            var fy = y - ((j == 0 ? 9 : 0) * s);
            for (var i = 0; i < 5; i++)
            {
                var a = (i * Math.PI * 2 / 5) - (Math.PI / 2);
                var px = fx + ((float)Math.Cos(a) * 4 * s);
                var py = fy + ((float)Math.Sin(a) * 4 * s);
                using var petal = new SolidBrush(j == 0 ? Color.FromArgb(208, 183, 238) : Color.FromArgb(245, 164, 185));
                g.FillEllipse(petal, px - (3.5f * s), py - (3.5f * s), 7 * s, 7 * s);
                g.DrawEllipse(ink, px - (3.5f * s), py - (3.5f * s), 7 * s, 7 * s);
            }

            using var center = new SolidBrush(Color.FromArgb(255, 218, 117));
            g.FillEllipse(center, fx - (3 * s), fy - (3 * s), 6 * s, 6 * s);
            g.DrawEllipse(ink, fx - (3 * s), fy - (3 * s), 6 * s, 6 * s);
        }
    }
}
