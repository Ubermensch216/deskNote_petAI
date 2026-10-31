using System.Drawing;
using System.Drawing.Drawing2D;
using DeskNote.Companion.Core;

namespace DeskNote.Companion.Rig;

/// <summary>Polygon helpers.</summary>
internal static class Shapes
{
    public static PointF P(float x, float y) { return new PointF(x, y); }

    private static PointF CR(PointF p0, PointF p1, PointF p2, PointF p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return new PointF(
            .5f * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
            .5f * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3));
    }

    /// <summary>Turns a Catmull-Rom curve through the control points into a fine polyline.</summary>
    public static PointF[] Smooth(PointF[] c, bool closed, int sub = 7)
    {
        int n = c.Length; var res = new List<PointF>();
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            PointF p0 = c[closed ? (i - 1 + n) % n : Math.Max(i - 1, 0)], p1 = c[i];
            PointF p2 = c[closed ? (i + 1) % n : i + 1], p3 = c[closed ? (i + 2) % n : Math.Min(i + 2, n - 1)];
            for (int s = 0; s < sub; s++) res.Add(CR(p0, p1, p2, p3, s / (float)sub));
        }
        if (!closed) res.Add(c[n - 1]);
        return res.ToArray();
    }

    public static PointF[] Mirror(PointF[] p, float axis)
    {
        var r = new PointF[p.Length];
        for (int i = 0; i < p.Length; i++) r[i] = new PointF(2 * axis - p[p.Length - 1 - i].X, p[p.Length - 1 - i].Y);
        return r;
    }

    public static PointF[] Ellipse(float cx, float cy, float rx, float ry, int n = 24)
    {
        var r = new PointF[n];
        for (int i = 0; i < n; i++) { double a = i * Math.PI * 2 / n; r[i] = new PointF(cx + (float)Math.Cos(a) * rx, cy + (float)Math.Sin(a) * ry); }
        return r;
    }

    public static PointF[] Map(PointF[] p, Func<PointF, PointF> f)
    {
        var r = new PointF[p.Length];
        for (int i = 0; i < p.Length; i++) r[i] = f(p[i]);
        return r;
    }

    /// <summary>Keeps the side where a*x + b*y + c is at least zero (Sutherland-Hodgman).</summary>
    public static PointF[] ClipHalf(PointF[] poly, float a, float b, float c)
    {
        var res = new List<PointF>();
        for (int i = 0; i < poly.Length; i++)
        {
            PointF cur = poly[i], nxt = poly[(i + 1) % poly.Length];
            float dc = a * cur.X + b * cur.Y + c, dn = a * nxt.X + b * nxt.Y + c;
            if (dc >= 0) res.Add(cur);
            if ((dc >= 0) != (dn >= 0))
            {
                float t = dc / (dc - dn);
                res.Add(new PointF(cur.X + (nxt.X - cur.X) * t, cur.Y + (nxt.Y - cur.Y) * t));
            }
        }
        return res.ToArray();
    }

    /// <summary>The outline of a limb of width <c>w</c> swept along a spine; the end can be closed round.</summary>
    public static PointF[] Limb(PointF[] spine, float[] w, bool roundEnd, int sub = 6)
    {
        PointF[] s = Smooth(spine, false, sub);
        int m = s.Length;
        var left = new PointF[m]; var right = new PointF[m];
        for (int i = 0; i < m; i++)
        {
            PointF a = s[Math.Max(i - 1, 0)], b = s[Math.Min(i + 1, m - 1)];
            float tx = b.X - a.X, ty = b.Y - a.Y, len = (float)Math.Sqrt(tx * tx + ty * ty);
            if (len < 1e-4f) { tx = 1; ty = 0; len = 1; }
            tx /= len; ty /= len;
            float u = i / (float)(m - 1) * (w.Length - 1); int i0 = Math.Min((int)u, w.Length - 2); float f = u - i0;
            float wd = (w.Length == 1 ? w[0] : w[i0] + (w[i0 + 1] - w[i0]) * f) / 2;
            left[i] = new PointF(s[i].X - ty * wd, s[i].Y + tx * wd);
            right[i] = new PointF(s[i].X + ty * wd, s[i].Y - tx * wd);
        }
        var res = new List<PointF>(left);
        if (roundEnd)
        {
            PointF a = s[m - 2], b = s[m - 1];
            double ang = Math.Atan2(b.Y - a.Y, b.X - a.X); float wd = (w[w.Length - 1]) / 2;
            for (int i = 1; i < 8; i++)
            {
                double q = ang + Math.PI / 2 - Math.PI * i / 8.0;
                res.Add(new PointF(b.X + (float)Math.Cos(q) * wd, b.Y + (float)Math.Sin(q) * wd));
            }
        }
        for (int i = m - 1; i >= 0; i--) res.Add(right[i]);
        return res.ToArray();
    }
}

/// <summary>Arm maths shared by the characters.</summary>
internal static class TwoJointArm
{
    /// <summary>Two-joint arm: finds the upper-arm (A) and forearm (B) angles that put the wrist at T from shoulder S (straight down is 0, outward is positive).</summary>
    public static void IK(PointF S, PointF T, float L1, float L2, int outSign, out float A, out float B)
    {
        float dx = T.X - S.X, dy = T.Y - S.Y, raw = (float)Math.Sqrt(dx * dx + dy * dy), dist = raw;
        dist = Math.Max(Math.Abs(L1 - L2) + .5f, Math.Min(L1 + L2 - .5f, dist));
        double th = Math.Atan2(dy, dx), alpha = Math.Acos(Math.Max(-1, Math.Min(1, (L1 * L1 + dist * dist - L2 * L2) / (2 * L1 * dist))));
        PointF best = PointF.Empty; double bestScore = double.MinValue;
        for (int sg = -1; sg <= 1; sg += 2)
        {
            double a = th + sg * alpha;
            PointF e = new PointF(S.X + (float)Math.Cos(a) * L1, S.Y + (float)Math.Sin(a) * L1);
            double score = (e.X - S.X) * outSign + (e.Y - S.Y) * .6;
            if (score > bestScore) { bestScore = score; best = e; }
        }
        PointF v1 = new PointF(best.X - S.X, best.Y - S.Y);
        float k = raw < 1e-3f ? 0 : dist / raw;
        PointF wr = new PointF(S.X + dx * k, S.Y + dy * k);
        PointF v2 = new PointF(wr.X - best.X, wr.Y - best.Y);
        A = (float)(Math.Atan2(outSign * v1.X, v1.Y) * 180 / Math.PI);
        B = (float)(Math.Atan2(outSign * v2.X, v2.Y) * 180 / Math.PI);
    }
}
