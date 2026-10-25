// Taken from the desktop pet this project's characters were drawn for, and kept in its style.
#nullable disable
using System;
using DeskNote.Companion.Core;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DeskNote.Companion.Rig
{
    /// <summary>Aura, ki orbs, beams, ki blasts, lightning, flashes, the flying nimbus, the power pole and props.</summary>
    internal sealed partial class DbPainter
    {
        private PointF WristMid { get { return Pt((wrist[0].X + wrist[1].X) / 2, (wrist[0].Y + wrist[1].Y) / 2); } }

        // -------- aura --------
        private void AuraColors(out Color c1, out Color c2)
        {
            switch (p.AuraKind)
            {
                case 2: c1 = Color.FromArgb(255, 200, 40); c2 = Color.FromArgb(255, 248, 176); break;
                case 3: c1 = Color.FromArgb(70, 150, 255); c2 = Color.FromArgb(214, 238, 255); break;
                case 4: c1 = Color.FromArgb(166, 84, 232); c2 = Color.FromArgb(238, 208, 255); break;
                case 5: c1 = Color.FromArgb(110, 226, 150); c2 = Color.FromArgb(232, 255, 238); break;
                default: c1 = Color.FromArgb(150, 204, 255); c2 = Color.FromArgb(252, 253, 255); break;
            }
        }

        private void DrawAura()
        {
            if (p.Aura <= .01f) return;
            float a = Math.Min(1, p.Aura), t = p.AuraT;
            Color c1, c2; AuraColors(out c1, out c2);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(200 - 170, 330 - 290, 340, 580);
                using (var pg = new PathGradientBrush(path) { CenterColor = Al(c2, 120 * a), SurroundColors = new[] { Al(c1, 0) } }) g.FillPath(pg, path);
            }
            const int N = 9;
            for (int i = 0; i < N; i++)
            {
                float u = i / (float)(N - 1), x0 = 50 + 300 * u;
                float s = (float)Math.Sin(t * 6 + i * 1.9);
                float hgt = (210 + 120 * s * s + (i == 4 ? 80 : 0)) * a * (1 - Math.Abs(u - .5f) * .6f);
                float lean = (float)Math.Sin(t * 4 + i) * 14;
                PointF[] fl = { Pt(x0 - 32, 562), Pt(x0 - 20, 562 - hgt * .45f), Pt(x0 + lean - 6, 562 - hgt * .8f), Pt(x0 + lean, 562 - hgt), Pt(x0 + lean + 8, 562 - hgt * .72f), Pt(x0 + 24, 562 - hgt * .4f), Pt(x0 + 32, 562) };
                PointF[] outer = Geo.Smooth(fl, true, 5);
                g.FillPolygon(B(Al(c1, 120 * a)), outer);
                PointF[] inner = Geo.Map(outer, q => Pt(x0 + (q.X - x0) * .56f, 562 + (q.Y - 562) * .66f));
                g.FillPolygon(B(Al(c2, 150 * a)), inner);
            }
        }

        // -------- power pole and staff --------
        private void DrawStaff()
        {
            if (p.Staff >= 1.5f)
            {
                PointF c = WristMid; double ang = p.StaffAngle * Math.PI / 180;
                PointF dir = Pt((float)Math.Sin(ang), -(float)Math.Cos(ang));
                float len = 250 * Math.Max(.2f, p.StaffLen), back = Math.Min(len * .3f, 90);
                PointF a = Pt(c.X - dir.X * back, c.Y - dir.Y * back), b = Pt(c.X + dir.X * (len - back), c.Y + dir.Y * (len - back));
                g.DrawLine(Pn(cInk, 22 + ow * 1.4f), a, b);
                g.DrawLine(Pn(Color.FromArgb(214, 52, 44), 19), a, b);
                g.DrawLine(Pn(Color.FromArgb(244, 120, 100), 5), Pt(a.X + dir.Y * 5, a.Y - dir.X * 5), Pt(b.X + dir.Y * 5, b.Y - dir.X * 5));
                PointF capA = Pt(a.X + dir.X * 20, a.Y + dir.Y * 20), capB = Pt(b.X - dir.X * 20, b.Y - dir.Y * 20);
                g.DrawLine(Pn(Color.FromArgb(246, 200, 70), 21), a, capA); g.DrawLine(Pn(Color.FromArgb(246, 200, 70), 21), b, capB);
            }
            else if (p.Staff >= .5f && who == RigCharacter.Roshi)
            {
                float x = wrist[1].X, top = wrist[1].Y - 150;
                g.DrawLine(Pn(cInk, 17 + ow), Pt(x, top), Pt(x, 574));
                g.DrawLine(Pn(Color.FromArgb(160, 108, 56), 14), Pt(x, top), Pt(x, 574));
                g.DrawLine(Pn(Color.FromArgb(200, 150, 90), 4), Pt(x - 3, top + 6), Pt(x - 3, 560));
                g.FillEllipse(B(Color.FromArgb(120, 80, 40)), x - 14, top - 14, 28, 28); g.DrawEllipse(ink, x - 14, top - 14, 28, 28);
            }
        }

        // -------- snacks (meat, water, tea) --------
        private void DrawSnack()
        {
            if (p.Cookie < 0) return;
            float bite = Math.Max(0, Math.Min(1, p.Cookie));
            PointF m = WristMid; m = Pt(m.X, m.Y - 8);
            var st = g.Save(); g.TranslateTransform(m.X, m.Y);
            if (who == RigCharacter.Piccolo || who == RigCharacter.Roshi)
            {
                bool tea = who == RigCharacter.Roshi;
                PointF[] cup = { Pt(-24, -26), Pt(24, -26), Pt(18, 28), Pt(-18, 28) };
                g.FillPolygon(B(tea ? Color.FromArgb(252, 250, 244) : Color.FromArgb(200, 230, 252, 255)), cup);
                float lv = 1 - bite, top = 26 - 46 * lv;
                if (lv > .02f)
                {
                    var water = new GraphicsPath(); water.AddPolygon(cup);
                    var s2 = g.Save(); g.SetClip(water, CombineMode.Intersect);
                    g.FillRectangle(B(tea ? Color.FromArgb(150, 96, 40) : Color.FromArgb(120, 190, 240)), -30, top, 60, 60);
                    g.Restore(s2); water.Dispose();
                }
                g.DrawPolygon(ink, cup);
                if (tea) { g.DrawArc(ink, 14, -12, 22, 26, 270, 180); }
                if (tea && lv > .1f) for (int i = 0; i < 2; i++) g.DrawArc(Pn(Color.FromArgb(150, 255, 255, 255), 3), -10 + i * 14, -52 + (float)Math.Sin(p.Cookie * 30 + i) * 3, 8, 22, 100, 160);
            }
            else
            {
                float r = who == RigCharacter.Kid ? 40 : 34;
                const int n = 40; var pts = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    double a = i * Math.PI * 2 / n, d = Math.Abs(Math.Atan2(Math.Sin(a + .6), Math.Cos(a + .6)));
                    double cut = Math.Max(0, 1 - d / (.5 + bite * 2.1)) * bite * (.62 + .22 * Math.Abs(Math.Sin(a * 4.5)));
                    float rr = (float)(r * (1 - cut));
                    pts[i] = Pt((float)Math.Cos(a) * rr, (float)Math.Sin(a) * rr * .86f);
                }
                // bone
                g.DrawLine(Pn(cInk, 16 + ow), Pt(r * .3f, r * .3f), Pt(r * 1.1f, r * .96f));
                g.DrawLine(Pn(cWhite, 13), Pt(r * .3f, r * .3f), Pt(r * 1.1f, r * .96f));
                g.FillEllipse(B(cWhite), r * 1.1f - 9, r * .96f - 15, 18, 18); g.FillEllipse(B(cWhite), r * 1.1f + 1, r * .96f - 6, 18, 18);
                g.FillPolygon(B(Color.FromArgb(200, 104, 60)), pts); g.DrawPolygon(ink, pts);
                if (bite < .9f) { g.FillEllipse(B(Color.FromArgb(232, 160, 110)), -r * .5f, -r * .5f, r * .55f, r * .32f); }
            }
            g.Restore(st);
        }

        // -------- prop: the rice cooker (demon-sealing wave) --------
        private void DrawProp()
        {
            if (p.Prop <= 0) return;
            PointF w = wrist[1]; bool open = p.Prop == 2;
            var st = g.Save(); g.TranslateTransform(w.X - 14, w.Y - 30);
            RectangleF body = new RectangleF(-36, -20, 72, 54);
            using (var gp = RoundRectPath(body, 14))
            { g.FillPath(B(Color.FromArgb(250, 250, 252)), gp); g.DrawPath(ink, gp); }
            g.FillRectangle(B(Color.FromArgb(214, 60, 60)), -35, 2, 70, 11);
            g.DrawLine(Pn(cInk, ow), Pt(-35, 2), Pt(35, 2)); g.DrawLine(Pn(cInk, ow), Pt(-35, 13), Pt(35, 13));
            g.FillEllipse(B(Color.FromArgb(60, 60, 70)), -9, 18, 18, 10);
            if (open)
            {
                g.FillEllipse(B(Color.FromArgb(40, 36, 44)), -30, -28, 60, 16);
                PointF[] lid = Geo.Smooth(new[] { Pt(20, -26), Pt(40, -52), Pt(70, -46), Pt(58, -22) }, true, 5);
                g.FillPolygon(B(Color.FromArgb(240, 240, 246)), lid); g.DrawPolygon(ink, lid);
                for (int i = 0; i < 3; i++) g.DrawArc(Pn(Color.FromArgb(160, 255, 255, 255), 4), -24 + i * 22, -72 + (float)Math.Sin(p.BeamT * 6 + i) * 4, 14, 36, 100, 160);
            }
            else
            {
                PointF[] lid = Geo.Smooth(new[] { Pt(-34, -18), Pt(-26, -36), Pt(26, -36), Pt(34, -18) }, true, 5);
                g.FillPolygon(B(Color.FromArgb(240, 240, 246)), lid); g.DrawPolygon(ink, lid);
                g.FillEllipse(B(Color.FromArgb(214, 60, 60)), -7, -46, 14, 14); g.DrawEllipse(ink, -7, -46, 14, 14);
            }
            g.Restore(st);
        }

        private static GraphicsPath RoundRectPath(RectangleF r, float rad)
        {
            var path = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }

        // -------- ki orbs, beams, ki blasts --------
        private static void KiColors(KiKind kind, out Color core, out Color mid, out Color edge)
        {
            switch (kind)
            {
                case KiKind.Purple: core = Color.FromArgb(255, 244, 255); mid = Color.FromArgb(218, 152, 255); edge = Color.FromArgb(128, 40, 214); break;
                case KiKind.Yellow: core = Color.FromArgb(255, 255, 250); mid = Color.FromArgb(255, 244, 150); edge = Color.FromArgb(255, 178, 30); break;
                case KiKind.Spiral: core = Color.FromArgb(255, 252, 210); mid = Color.FromArgb(255, 232, 90); edge = Color.FromArgb(150, 70, 214); break;
                case KiKind.Suck: core = Color.FromArgb(255, 255, 255); mid = Color.FromArgb(214, 236, 255); edge = Color.FromArgb(120, 170, 240); break;
                default: core = Color.FromArgb(255, 255, 255); mid = Color.FromArgb(170, 232, 255); edge = Color.FromArgb(40, 120, 255); break;
            }
        }

        private void DrawEnergy()
        {
            KiKind kind = (KiKind)p.KiKind;
            if (kind == KiKind.None && p.Shot < 0) return;
            PointF c = p.KiAt ?? WristMid;
            if (kind == KiKind.Suck) { if (p.BeamLen > 1) DrawSuck(c); return; }
            if (p.BeamLen > 1) DrawBeam(c, kind);
            if (p.Ki > .01f) DrawBall(c, 16 + 70 * Math.Min(1, p.Ki), kind);
            if (p.Shot >= 0) DrawShot(c, kind);
        }

        private void DrawBall(PointF c, float r, KiKind kind)
        {
            Color core, mid, edge; KiColors(kind, out core, out mid, out edge);
            r *= 1 + .06f * (float)Math.Sin(p.BeamT * 9);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - r * 2.2f, c.Y - r * 2.2f, r * 4.4f, r * 4.4f);
                using (var pg = new PathGradientBrush(path) { CenterColor = Al(mid, 210), SurroundColors = new[] { Al(edge, 0) } }) g.FillPath(pg, path);
            }
            // light streaks
            for (int i = 0; i < 9; i++)
            {
                double a = i * Math.PI * 2 / 9 + p.BeamT * 2.2; float l = r * (1.5f + .4f * (float)Math.Sin(p.BeamT * 7 + i * 2));
                g.DrawLine(Pn(Al(mid, 170), 4), Pt(c.X + (float)Math.Cos(a) * r * 1.05f, c.Y + (float)Math.Sin(a) * r * 1.05f), Pt(c.X + (float)Math.Cos(a) * l, c.Y + (float)Math.Sin(a) * l));
            }
            g.FillEllipse(B(Al(edge, 235)), c.X - r * 1.05f, c.Y - r * 1.05f, r * 2.1f, r * 2.1f);
            g.FillEllipse(B(Al(mid, 250)), c.X - r * .82f, c.Y - r * .82f, r * 1.64f, r * 1.64f);
            g.FillEllipse(B(core), c.X - r * .5f, c.Y - r * .5f, r, r);
            if (kind == KiKind.Spiral) g.DrawArc(Pn(Al(edge, 220), 6), c.X - r * .8f, c.Y - r * .8f, r * 1.6f, r * 1.6f, (float)(p.BeamT * 300 % 360), 220);
        }

        private void DrawBeam(PointF c, KiKind kind)
        {
            Color core, mid, edge; KiColors(kind, out core, out mid, out edge);
            float len = p.BeamLen, hw = 16 + 56 * Math.Max(0, p.BeamW), x1 = c.X - len;
            var blend = new ColorBlend
            {
                Positions = new[] { 0f, .22f, .5f, .78f, 1f },
                Colors = new[] { Al(edge, 0), Al(edge, 190), core, Al(edge, 190), Al(edge, 0) }
            };
            using (var br = new LinearGradientBrush(new PointF(0, c.Y - hw * 1.5f), new PointF(0, c.Y + hw * 1.5f), Color.White, Color.White) { InterpolationColors = blend })
                g.FillRectangle(br, x1, c.Y - hw * 1.5f, len, hw * 3);
            using (var br = new LinearGradientBrush(new PointF(0, c.Y - hw * .62f), new PointF(0, c.Y + hw * .62f), Color.White, Color.White) { InterpolationColors = new ColorBlend { Positions = new[] { 0f, .5f, 1f }, Colors = new[] { Al(mid, 230), Color.White, Al(mid, 230) } } })
                g.FillRectangle(br, x1, c.Y - hw * .62f, len, hw * 1.24f);
            // the round glow at the head of the beam
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x1 - hw * 1.1f, c.Y - hw * 1.3f, hw * 2.2f, hw * 2.6f);
                using (var pg = new PathGradientBrush(path) { CenterColor = Al(core, 255), SurroundColors = new[] { Al(edge, 0) } }) g.FillPath(pg, path);
            }
            // rippling joints
            for (int i = 0; i < 10; i++)
            {
                float x = x1 + (((i * len / 10f) + p.BeamT * 300f) % Math.Max(10, len));
                g.DrawLine(Pn(Al(core, 120), 3.5f), Pt(x, c.Y - hw * .78f), Pt(x - 6, c.Y + hw * .78f));
            }
            if (kind == KiKind.Spiral)
            {
                for (int s = 0; s < 2; s++)
                {
                    var pts = new List<PointF>();
                    for (float x = x1; x <= c.X; x += 8) pts.Add(Pt(x, c.Y + (float)Math.Sin(x * .05 + p.BeamT * 10 + s * Math.PI) * hw * .74f));
                    if (pts.Count > 1) g.DrawLines(Pn(Al(s == 0 ? edge : Color.FromArgb(255, 220, 255), 215), hw * .26f), pts.ToArray());
                }
            }
            DrawBall(c, hw * 1.15f, kind);
        }

        private void DrawShot(PointF c, KiKind kind)
        {
            Color core, mid, edge; KiColors(kind, out core, out mid, out edge);
            const float range = 1000;
            if (p.Shot < .9f)
            {
                float d = p.Shot / .9f * range;
                PointF b = Pt(c.X - d, c.Y);
                PointF[] tail = { Pt(c.X - d * .1f, c.Y - 10), Pt(b.X, b.Y - 30), Pt(b.X, b.Y + 30), Pt(c.X - d * .1f, c.Y + 10) };
                using (var br = new LinearGradientBrush(new PointF(b.X, 0), new PointF(c.X, 0), Al(mid, 200), Al(mid, 0))) g.FillPolygon(br, tail);
                DrawBall(b, 40, kind);
            }
            else
            {
                float q = (p.Shot - .9f) / .1f; PointF b = Pt(c.X - range, c.Y);
                float R = 50 + 190 * q; int a = (int)(255 * (1 - q * .7f));
                PointF[] star = new PointF[24];
                for (int i = 0; i < star.Length; i++) { double an = i * Math.PI * 2 / star.Length; float rr = i % 2 == 0 ? R : R * .55f; star[i] = Pt(b.X + (float)Math.Cos(an) * rr, b.Y + (float)Math.Sin(an) * rr); }
                g.FillPolygon(B(Al(edge, a * .7f)), star);
                PointF[] star2 = Geo.Map(star, s => Pt(b.X + (s.X - b.X) * .62f, b.Y + (s.Y - b.Y) * .62f));
                g.FillPolygon(B(Al(mid, a)), star2);
                g.FillEllipse(B(Al(core, a)), b.X - R * .32f, b.Y - R * .32f, R * .64f, R * .64f);
            }
        }

        private void DrawSuck(PointF c)
        {
            Color core, mid, edge; KiColors(KiKind.Suck, out core, out mid, out edge);
            float R = p.BeamLen;
            for (int i = 0; i < 16; i++)
            {
                double ang = (i / 15.0 - .5) * 1.15;
                float q = (float)((p.BeamT * .8 + i * .37) % 1.0), r = R * (1 - q) + 30;
                PointF d = Pt(-(float)Math.Cos(ang), (float)Math.Sin(ang));
                PointF a = Pt(c.X + d.X * r, c.Y + d.Y * r), b = Pt(c.X + d.X * (r - 34), c.Y + d.Y * (r - 34));
                int al = (int)(220 * Math.Sin(q * Math.PI));
                g.DrawLine(Pn(Al(edge, al), 7), a, b); g.DrawLine(Pn(Al(core, al), 3), a, b);
            }
            for (int i = 0; i < 3; i++) g.DrawEllipse(Pn(Al(mid, 120), 4), c.X - 60 - i * 40, c.Y - 40 - i * 22, 120 + i * 80, 80 + i * 44);
        }

        // -------- flying nimbus --------
        private void DrawNimbus()
        {
            if (p.Nimbus <= .01f) return;
            float s = Math.Min(1, p.Nimbus), bob = (float)Math.Sin(p.AuraT * 3) * 3;
            float[][] puffs = { new[] { 120f, 580f, 36f }, new[] { 158f, 570f, 44f }, new[] { 200f, 566f, 52f }, new[] { 244f, 570f, 44f }, new[] { 282f, 580f, 36f }, new[] { 180f, 590f, 36f }, new[] { 224f, 590f, 36f } };
            var shapes = new List<RectangleF>();
            foreach (var pf in puffs) { float r = pf[2] * s; shapes.Add(new RectangleF(200 + (pf[0] - 200) * s - r, 572 + (pf[1] - 572 + bob) * s - r, r * 2, r * 2)); }
            var thick = Pn(cInk, ow * 2f);
            foreach (var r in shapes) g.DrawEllipse(thick, r);
            foreach (var r in shapes) g.FillEllipse(B(Color.FromArgb(255, 242, 170)), r);
            foreach (var r in shapes) g.DrawArc(Pn(Color.FromArgb(200, 255, 255, 255), ow * 1.5f), r.Left + r.Width * .18f, r.Top + r.Height * .14f, r.Width * .6f, r.Height * .5f, 200, 80);
        }

        // -------- lightning and flashes --------
        private void DrawOverlay()
        {
            if (p.Spark > .01f)
            {
                var rnd = new Random((int)(p.AuraT * 12) + 3);
                for (int b = 0; b < 7; b++)
                {
                    float x = 200 + (float)(rnd.NextDouble() * 2 - 1) * 150, y = 130 + (float)rnd.NextDouble() * 400;
                    var pts = new List<PointF> { Pt(x, y) };
                    for (int s = 0; s < 5; s++) { x += (float)(rnd.NextDouble() * 2 - 1) * 34; y += (float)(rnd.NextDouble() * 2 - .7) * 44; pts.Add(Pt(x, y)); }
                    g.DrawLines(Pn(Al(Color.FromArgb(120, 90, 0), 200 * p.Spark), 9), pts.ToArray());
                    g.DrawLines(Pn(Al(Color.FromArgb(255, 238, 110), 255 * p.Spark), 6), pts.ToArray());
                    g.DrawLines(Pn(Al(Color.White, 255 * p.Spark), 2.4f), pts.ToArray());
                }
            }
            if (p.Flash > .01f)
            {
                float f = Math.Min(1, p.Flash), r = 190 + 160 * f;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(200 - r * .8f, 320 - r * 1.3f, r * 1.6f, r * 2.6f);
                    using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(255 * f), 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } }) g.FillPath(pg, path);
                }
            }
        }
    }
}
