// Taken from the desktop pet this project's characters were drawn for, and kept in its style.
#nullable disable
using System;
using DeskNote.Companion.Core;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DeskNote.Companion.Rig
{
    /// <summary>The head: face, ears, hair, eyes, brows, mouth, sunglasses, beard, antennae and turban.</summary>
    internal sealed partial class DbPainter
    {
        private float FX(float x) { return 200 + (x - 200) * (1 - .1f * p.View) - 30 * p.View; }
        private bool Side { get { return p.View == 1; } }
        private float eyeCy { get { return headCy + (who == RigCharacter.Kid ? 30 : 27); } }

        private void DrawHead()
        {
            var st = g.Save();
            g.TranslateTransform(p.HeadX, p.HeadY);
            float pivotY = headCy + headRy - 8;
            g.TranslateTransform(200, pivotY); g.RotateTransform(p.HeadTilt); g.TranslateTransform(-200, -pivotY);
            if (!Side) DrawEars(true);
            DrawFace();
            if (Side) DrawEars(false);
            if (p.Flush > .01f)
            {
                var blush = B(Color.FromArgb((int)(150 * p.Flush), 255, 120, 110));
                g.FillEllipse(blush, FX(200 - 66), headCy + 44, 40, 22); g.FillEllipse(blush, FX(200 + 26), headCy + 44, 40, 22);
            }
            DrawHair();
            DrawEyes();
            DrawNose();
            if (who == RigCharacter.Roshi) DrawBeard();
            DrawMouth();
            DrawHeadTop();
            if (p.Anger > .01f) DrawAnger();
            if (p.Tears >= 0) DrawTears(p.Tears);
            if (p.Sweat >= 0) DrawSweat(p.Sweat);
            g.Restore(st);
        }

        // -------- face shape and ears --------
        private PointF[] FacePoly()
        {
            var pts = new PointF[40];
            for (int i = 0; i < pts.Length; i++)
            {
                double a = i * Math.PI * 2 / pts.Length, c = Math.Cos(a), s = Math.Sin(a);
                float taper = s > 0 ? (float)(1 - jaw * .3 * s * s) : 1f;
                float x = 200 + headRx * (float)c * taper, y = headCy + headRy * (float)s * (s > 0 ? 1 + jaw * .1f : 1);
                pts[i] = Pt(x, y);
            }
            return pts;
        }

        private void DrawFace() { Fill(FacePoly(), skin); }

        private void DrawEars(bool behind)
        {
            float ey = headCy + 12;
            if (who == RigCharacter.Piccolo)
            {
                float sway = (p.EarL + p.EarR) * .4f;
                Color pink = Color.FromArgb(232, 150, 168);
                if (behind)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float bx = 200 + s * (headRx - 8);
                        PointF[] ear = { Pt(bx, ey - 22), Pt(bx + s * 70 + sway, ey - 62), Pt(bx + s * 6, ey + 30) };
                        Fill(ear, skin);
                        PointF[] inner = { Pt(bx + s * 6, ey - 14), Pt(bx + s * 52 + sway, ey - 50), Pt(bx + s * 8, ey + 16) };
                        g.FillPolygon(B(pink), inner);
                        g.DrawLine(Pn(skinSh, ow * .8f), Pt(bx + s * 10, ey - 4), Pt(bx + s * 44, ey - 30));
                    }
                else
                {
                    float bx = 200 + headRx * .42f;
                    PointF[] ear = { Pt(bx, ey - 26), Pt(bx + 66, ey - 70 + sway), Pt(bx + 8, ey + 28) };
                    Fill(ear, skin); g.FillPolygon(B(pink), new[] { Pt(bx + 8, ey - 14), Pt(bx + 48, ey - 50 + sway), Pt(bx + 10, ey + 14) });
                }
                return;
            }
            if (behind)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float ex = 200 + s * (headRx - 1);
                    g.FillEllipse(B(skin), ex - 17, ey - 25, 34, 50); g.DrawEllipse(ink, ex - 17, ey - 25, 34, 50);
                    g.DrawArc(Pn(skinSh, ow * .9f), ex - 8, ey - 12, 16, 24, s < 0 ? 90 : -90, 180);
                }
            }
            else
            {
                float ex = 200 + headRx * .5f;
                g.FillEllipse(B(skin), ex - 17, ey - 25, 34, 50); g.DrawEllipse(ink, ex - 17, ey - 25, 34, 50);
                g.DrawArc(Pn(skinSh, ow * .9f), ex - 8, ey - 12, 16, 24, 90, 180);
            }
        }

        // -------- hair --------
        private struct Spike { public float Bx, By, Bw, Tx, Ty; public Spike(float bx, float by, float bw, float tx, float ty) { Bx = bx; By = by; Bw = bw; Tx = tx; Ty = ty; } }

        private PointF[] SpikePoly(Spike s, float swayDeg, float stretch)
        {
            float tx = s.Bx + (s.Tx - s.Bx) * stretch, ty = s.By + (s.Ty - s.By) * stretch;
            ty = Math.Max(s.Ty < 20 ? 3 : 12, ty);
            double a = swayDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            float vx = tx - s.Bx, vy = ty - s.By;
            float rx = s.Bx + (float)(vx * ca - vy * sa), ry = s.By + (float)(vx * sa + vy * ca);
            PointF bl = Pt(s.Bx - s.Bw / 2, s.By), br = Pt(s.Bx + s.Bw / 2, s.By), tip = Pt(rx, ry);
            PointF ml = Pt((bl.X + tip.X) / 2 + 3, (bl.Y + tip.Y) / 2), mr = Pt((br.X + tip.X) / 2 - 3, (br.Y + tip.Y) / 2);
            return new[] { bl, ml, tip, mr, br };
        }

        private void DrawHair()
        {
            if (who == RigCharacter.Piccolo || who == RigCharacter.Roshi) return;
            float stretch = 1 + .16f * sup + .22f * ape;
            float dy = headCy - 160, sx = headRx / 98f;
            Func<float, float> X = x => 200 + (x - 200) * sx;
            var shapes = new List<PointF[]>();
            float swayL = p.EarL * 1.1f + (Side ? -2 : 0), swayR = p.EarR * 1.1f + (Side ? 9 : 0);
            if (who == RigCharacter.Vegeta)
            {
                PointF[] flame =
                {
                    Pt(104, 160), Pt(104, 112), Pt(120, 74), Pt(142, 52), Pt(146, 16), Pt(174, 42), Pt(198, 4), Pt(224, 42), Pt(252, 16), Pt(258, 52),
                    Pt(280, 74), Pt(296, 112), Pt(296, 160), Pt(284, 132), Pt(262, 112), Pt(232, 100), Pt(200, 128), Pt(168, 100), Pt(138, 112), Pt(116, 132)
                };
                float shiftX = Side ? 6 : 0;
                for (int i = 0; i < flame.Length; i++)
                {
                    float ty = flame[i].Y; if (ty < 56) ty = 56 + (ty - 56) * stretch;
                    flame[i] = Pt(flame[i].X + shiftX * (1 - ty / 160f) + (flame[i].X < 200 ? p.EarL : p.EarR) * (1 - ty / 160f) * .4f, Math.Max(2, ty));
                }
                shapes.Add(flame);
            }
            else
            {
                Spike[] sp =
                {
                    new Spike(118, 126, 46, 56, 70), new Spike(146, 104, 54, 98, 26), new Spike(184, 92, 60, 192, 16),
                    new Spike(224, 94, 58, 262, 22), new Spike(254, 108, 56, 318, 48), new Spike(284, 136, 44, 350, 104)
                };
                for (int i = 0; i < sp.Length; i++)
                {
                    var s = sp[i]; s.Bx = X(s.Bx); s.Tx = X(s.Tx); s.By += dy; s.Ty += dy * .6f;
                    shapes.Add(SpikePoly(s, i < 3 ? swayL : swayR, stretch));
                }
                // crown cap and side hair
                var cap = new List<PointF>();
                float cx = 200, cy = headCy, rx = headRx + 4, ry = headRy + 4;
                for (int i = 0; i <= 16; i++) { double a = Math.PI * (1.04 + .92 * i / 16.0); cap.Add(Pt(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a))); }
                cap.Add(Pt(cx + rx * .97f, cy - 12)); cap.Add(Pt(cx + rx * .62f, cy - 38)); cap.Add(Pt(cx + rx * .2f, cy - 30));
                cap.Add(Pt(cx - rx * .2f, cy - 36)); cap.Add(Pt(cx - rx * .62f, cy - 38)); cap.Add(Pt(cx - rx * .97f, cy - 12));
                shapes.Add(cap.ToArray());
                // fringe (three locks hanging over the forehead)
                float by = cy - 52;
                shapes.Add(new[] { Pt(X(116), by), Pt(X(130), by + 28), Pt(X(130), cy - 12), Pt(X(166), by) });
                shapes.Add(new[] { Pt(X(166), by - 6), Pt(X(176), by + 34), Pt(X(194), cy - 4), Pt(X(214), by - 6) });
                shapes.Add(new[] { Pt(X(210), by - 4), Pt(X(222), by + 30), Pt(X(268), cy - 14), Pt(X(284), by) });
            }
            // outline in one go: draw every thick stroke, then fill the inside
            var thick = Pn(cInk, ow * 2.2f);
            foreach (var s in shapes) g.DrawPolygon(thick, s);
            var fill = B(hair);
            foreach (var s in shapes) g.FillPolygon(fill, s);
            // sheen
            var hi = Pn(Al(hairHi, 190), ow * 1.4f);
            if (who == RigCharacter.Vegeta) { g.DrawLine(hi, Pt(168, 60), Pt(184, 28)); g.DrawLine(hi, Pt(236, 58), Pt(222, 30)); }
            else { g.DrawLine(hi, Pt(X(160), headCy - 88), Pt(X(140), headCy - 112)); g.DrawLine(hi, Pt(X(232), headCy - 94), Pt(X(246), headCy - 112)); g.DrawArc(hi, X(150), headCy - 76, 70, 34, 200, 70); }
        }

        // -------- eyes --------
        private void EyeParams(out float dx, out float rx, out float ry, out float pr, out float baseBrow, out float browW)
        {
            switch (who)
            {
                case RigCharacter.Kid: dx = 53; rx = 30; ry = 36; pr = 18; baseBrow = 0; browW = 8.5f; break;
                case RigCharacter.Vegeta: dx = 47; rx = 27; ry = 25; pr = 12; baseBrow = .6f; browW = 11.5f; break;
                case RigCharacter.Piccolo: dx = 47; rx = 27; ry = 22; pr = 9; baseBrow = .35f; browW = 0; break;
                case RigCharacter.Roshi: dx = 52; rx = 34; ry = 32; pr = 0; baseBrow = 0; browW = 0; break;
                default: dx = 48; rx = 25; ry = 31; pr = 14.5f; baseBrow = 0; browW = 8.5f; break;
            }
        }

        private void DrawEyes()
        {
            if (who == RigCharacter.Roshi) { DrawSunglasses(); return; }
            float dx, rx, ry, pr, baseBrow, browW; EyeParams(out dx, out rx, out ry, out pr, out baseBrow, out browW);
            for (int s = 0; s < 2; s++) DrawEye(s == 1, dx, rx, ry, pr, baseBrow, browW);
        }

        private void DrawEye(bool right, float dx, float rx, float ry, float pr, float baseBrow, float browW)
        {
            float open = right ? p.OpenR : p.OpenL, narrow = (Side && right) ? .74f : 1f;
            float cx = FX(200 + (right ? dx : -dx)), cy = eyeCy, es = p.EyeScale, sgn = right ? 1f : -1f;
            float erx = rx * es * narrow, ery = ry * es * Math.Max(.05f, open);
            float tilt = Math.Max(-1.2f, Math.Min(1.3f, baseBrow + p.Brow));
            float raise = es > 1.04f ? -(es - 1) * 80 : 0;
            // brows
            if (browW > 0)
            {
                PointF outer = Pt(cx + sgn * (erx + 7), cy - ry * es - 15 + raise + tilt * -3), inner = Pt(cx - sgn * (erx - 9), cy - ry * es - 15 + raise + tilt * 17);
                if (p.Eyes != EyeStyle.Open || open < .14f) { outer.Y += 6; inner.Y += 6; }
                g.DrawLine(Pn(hair.GetBrightness() > .6f ? Color.FromArgb(200, 160, 30) : cInk, browW * (Side && right ? .8f : 1)), outer, inner);
            }
            else
            {
                // Piccolo: a bulging brow ridge
                g.DrawArc(Pn(skinSh, ow * 2.2f), cx - erx - 4, cy - ry - 24 + raise, erx * 2 + 8, 34, 200, 140);
            }
            if (p.Eyes == EyeStyle.Spiral)
            {
                g.FillEllipse(B(cWhite), cx - erx * 1.15f, cy - ery * 1.05f - 4, erx * 2.3f, (ery * 2.1f + 8)); g.DrawEllipse(ink, cx - erx * 1.15f, cy - ery * 1.05f - 4, erx * 2.3f, (ery * 2.1f + 8));
                DrawSpiral(new PointF(cx, cy), Math.Min(erx, ery) * 1.1f, p.SpiralT, right);
                return;
            }
            if (p.Eyes != EyeStyle.Open || open < .14f)
            {
                var lid = Pn(cInk, ow * 1.9f);
                float w2 = erx * 1.05f;
                if (p.Eyes == EyeStyle.Happy) g.DrawArc(lid, cx - w2, cy - 12, w2 * 2, 30, 200, 140);        // ∩
                else g.DrawArc(lid, cx - w2, cy - 16, w2 * 2, 28, 25, 130);                                  // ∪
                return;
            }
            PointF[] eye = Geo.Ellipse(cx, cy, erx, ery, 26);
            // an angry slant, lower toward the nose: the top is cut away on a diagonal
            if (Math.Abs(tilt) > .02f)
            {
                float inner = -sgn;
                float slope = tilt * .62f;
                // line: y = y0 + slope*(x-cx)*inner, keep only what lies below it
                float y0 = cy - ery * .9f;
                eye = Geo.ClipHalf(eye, -slope * inner, 1, -y0 + slope * inner * cx);
                if (eye.Length < 3) return;
            }
            g.FillPolygon(B(cWhite), eye);
            var st = g.Save();
            using (var gp = new GraphicsPath()) { gp.AddPolygon(eye); g.SetClip(gp, CombineMode.Intersect); }
            float gx = p.Gx * 10, gy = p.Gy * 9, cross = p.Cross * 6 * (right ? -1 : 1) - (Side ? 7 : 0);
            float px = cx + gx + cross, py = cy + gy - (1 - open) * 3, r = pr * p.PupilR * es;
            bool hasIris = sup > .02f || ape > .02f;
            if (hasIris) { g.FillEllipse(B(eyeIris), px - r * 1.18f, py - r * 1.2f, r * 2.36f, r * 2.4f); g.FillEllipse(B(cInk), px - r * .5f, py - r * .5f, r, r); }
            else g.FillEllipse(B(who == RigCharacter.Piccolo ? Color.FromArgb(24, 18, 28) : cInk), px - r, py - r, r * 2, r * 2);
            g.FillEllipse(B(Color.FromArgb(235, 255, 255, 255)), px - r * .62f, py - r * .72f, r * .56f, r * .56f);
            g.Restore(st);
            if (who == RigCharacter.Piccolo) g.DrawArc(Pn(cInk, ow * 1.6f), cx - erx - 1, cy - ery - 4, erx * 2 + 2, ery * 1.8f, 195, 150);
            g.DrawPolygon(ink, eye);
        }

        private void DrawSpiral(PointF c, float rad, float phase, bool right)
        {
            var pen = Pn(cInk, ow * 1.3f);
            var pts = new List<PointF>();
            for (int i = 0; i <= 40; i++)
            {
                double a = i * .44 + phase * (right ? -1 : 1), r = 2 + i * rad / 40;
                pts.Add(Pt(c.X + (float)(Math.Cos(a) * r), c.Y + (float)(Math.Sin(a) * r)));
            }
            g.DrawLines(pen, pts.ToArray());
        }

        private void DrawSunglasses()
        {
            float dx = 52, r = 35, cy = eyeCy;
            float open = Math.Max(p.OpenL, p.OpenR), es = p.EyeScale;
            // white brows (bushy)
            float tilt = Math.Max(-1f, Math.Min(1f, p.Brow)), raise = es > 1.04f ? -(es - 1) * 90 : 0;
            if (p.Eyes != EyeStyle.Open) raise -= 4;
            for (int s = -1; s <= 1; s += 2)
            {
                bool right = s > 0; float narrow = (Side && right) ? .76f : 1f;
                float cx = FX(200 + s * dx);
                float oy = cy - r - 14 + raise;
                PointF a = Pt(cx + s * (r * narrow + 6), oy - tilt * 3), b = Pt(cx - s * (r * narrow - 6), oy + tilt * 15);
                g.DrawLine(Pn(cInk, 17 + ow * 1.6f), a, b); g.DrawLine(Pn(cWhite, 17), a, b);
                g.DrawLine(Pn(Color.FromArgb(214, 214, 224), 5), Pt(a.X, a.Y + 2), Pt(b.X, b.Y + 2));
            }
            // glasses arms and nose bridge
            var rim = Pn(Color.FromArgb(24, 22, 28), ow * 1.8f);
            g.DrawLine(rim, Pt(FX(200 - dx + r * .9f), cy - 4), Pt(FX(200 + dx - r * .9f), cy - 4));
            g.DrawLine(rim, Pt(FX(200 - dx - r), cy - 6), Pt(200 - headRx + 6, cy - 12));
            if (!Side) g.DrawLine(rim, Pt(FX(200 + dx + r), cy - 6), Pt(200 + headRx - 6, cy - 12));
            for (int s = -1; s <= 1; s += 2)
            {
                bool right = s > 0; float narrow = (Side && right) ? .76f : 1f;
                float cx = FX(200 + s * dx), er = r * es * narrow, ery = r * es * (.86f + .14f * Math.Max(.1f, open));
                if (p.Eyes != EyeStyle.Open || open < .14f) ery = r * .6f;
                g.FillEllipse(B(Color.FromArgb(18, 18, 24)), cx - er, cy - ery, er * 2, ery * 2); g.DrawEllipse(Pn(cInk, ow * 1.5f), cx - er, cy - ery, er * 2, ery * 2);
                if (p.Eyes == EyeStyle.Spiral) { var sp = Pn(cWhite, ow * 1.2f); var pts = new List<PointF>(); for (int i = 0; i <= 36; i++) { double an = i * .46 + p.SpiralT * (right ? -1 : 1), rr = 2 + i * er * .85 / 36; pts.Add(Pt(cx + (float)(Math.Cos(an) * rr), cy + (float)(Math.Sin(an) * rr))); } g.DrawLines(sp, pts.ToArray()); }
                else
                {
                    g.DrawArc(Pn(Color.FromArgb(230, 255, 255, 255), ow * 1.4f), cx - er * .66f, cy - ery * .7f, er * 1.1f, ery * 1.0f, 205, 62);
                    if (p.Eyes == EyeStyle.Happy) g.DrawArc(Pn(Color.FromArgb(200, 255, 255, 255), ow * 1.2f), cx - er * .5f, cy - ery * .1f, er, ery * .7f, 200, 140);
                }
            }
        }

        // -------- nose, mouth, beard --------
        private void DrawNose()
        {
            float nx = FX(200) - (Side ? 6 : 0), ny = headCy + 50;
            switch (who)
            {
                case RigCharacter.Roshi:
                    g.FillEllipse(B(Mix(skin, Color.FromArgb(244, 150, 130), .55f)), nx - 22, ny - 18, 44, 40); g.DrawEllipse(ink, nx - 22, ny - 18, 44, 40);
                    g.DrawArc(Pn(Color.FromArgb(255, 255, 255, 255), ow * 1.2f), nx - 12, ny - 12, 14, 12, 200, 70);
                    break;
                case RigCharacter.Vegeta: g.DrawLine(Pn(skinSh, ow * 1.4f), Pt(nx + 2, ny - 6), Pt(nx - 4, ny + 6)); g.DrawLine(Pn(skinSh, ow * 1.2f), Pt(nx - 4, ny + 6), Pt(nx + 5, ny + 7)); break;
                case RigCharacter.Piccolo:
                    g.FillEllipse(B(skinSh), nx - 11, ny, 6, 8); g.FillEllipse(B(skinSh), nx + 5, ny, 6, 8); break;
                default: g.DrawArc(Pn(Al(cInk, 220), ow * 1.2f), nx - 6, ny - 6, 12, 10, 10, 160); break;
            }
        }

        private void DrawMouth()
        {
            float mx = FX(200) - (Side ? 5 : 0), my = headCy + (who == RigCharacter.Roshi ? 86 : who == RigCharacter.Kid ? 74 : 78);
            float sc = who == RigCharacter.Kid ? 1.05f : 1f;
            var line = Pn(cInk, Math.Max(ow * 1.1f, 3.8f));
            if (who == RigCharacter.Roshi && p.Mouth != MouthStyle.Open) return;
            switch (p.Mouth)
            {
                case MouthStyle.Neutral:
                    if (who == RigCharacter.Vegeta) g.DrawLines(line, new[] { Pt(mx - 18, my + 2), Pt(mx + 8, my + 5), Pt(mx + 22, my - 4) });
                    else g.DrawLine(line, Pt(mx - 14 * sc, my), Pt(mx + 14 * sc, my));
                    break;
                case MouthStyle.Smile:
                    if (who == RigCharacter.Piccolo) g.DrawLines(line, new[] { Pt(mx - 18, my + 2), Pt(mx, my + 5), Pt(mx + 18, my + 2) });
                    else g.DrawArc(line, mx - 24 * sc, my - 14, 48 * sc, 28, 10, 160);
                    break;
                case MouthStyle.Frown: g.DrawArc(line, mx - 17 * sc, my + 2, 34 * sc, 16, 200, 140); break;
                case MouthStyle.Pout: g.DrawArc(line, mx - 12 * sc, my + 2, 24 * sc, 10, 205, 130); break;
                case MouthStyle.Scared:
                    {
                        float w = 14 + p.MouthOpen * 6, h = 9 + p.MouthOpen * 9;
                        g.FillEllipse(B(Color.FromArgb(112, 30, 40)), mx - w, my - 2, w * 2, h); g.DrawEllipse(line, mx - w, my - 2, w * 2, h);
                        break;
                    }
                case MouthStyle.Open:
                    {
                        float w = (who == RigCharacter.Roshi ? 22 : 20) + p.MouthOpen * 14, h = 8 + p.MouthOpen * 38;
                        RectangleF r = new RectangleF(mx - w, my - 6, w * 2, h);
                        g.FillEllipse(B(Color.FromArgb(120, 30, 44)), r);
                        var st = g.Save();
                        using (var gp = new GraphicsPath()) { gp.AddEllipse(r); g.SetClip(gp, CombineMode.Intersect); }
                        float tl = Math.Min(h * .7f, 22 + p.Tongue * 10);
                        g.FillEllipse(B(Color.FromArgb(236, 112, 140)), mx - w * .6f, my - 6 + h - tl * .95f, w * 1.2f, tl * 1.5f);
                        g.Restore(st);
                        g.DrawEllipse(line, r);
                        if (ape > .3f) { g.FillPolygon(B(cWhite), new[] { Pt(mx - w * .8f, my - 4), Pt(mx - w * .62f, my - 4), Pt(mx - w * .7f, my + 10) }); g.FillPolygon(B(cWhite), new[] { Pt(mx + w * .8f, my - 4), Pt(mx + w * .62f, my - 4), Pt(mx + w * .7f, my + 10) }); }
                        break;
                    }
            }
            if (p.Tongue > .02f && p.Mouth != MouthStyle.Open)
            {
                float tl = 8 + p.Tongue * 18, tw = 14;
                g.FillEllipse(B(Color.FromArgb(236, 112, 140)), mx - tw / 2 + 3, my + 3, tw, tl); g.DrawEllipse(line, mx - tw / 2 + 3, my + 3, tw, tl);
            }
        }

        private void DrawBeard()
        {
            float cx = FX(200), jy = headCy;
            Color w = Color.FromArgb(252, 252, 254), sh = Color.FromArgb(216, 218, 228);
            float sway = p.HeadTilt * .5f + p.Breath * 3;
            var pts = new List<PointF>
            {
                Pt(FX(200 - headRx + 6), jy + 36), Pt(FX(200 - headRx + 14), jy + 84), Pt(FX(200 - 80), jy + 126), Pt(FX(200 - 48), jy + 160 + sway),
                Pt(cx, jy + 180 + sway), Pt(FX(200 + 48), jy + 160 - sway), Pt(FX(200 + 80), jy + 126), Pt(FX(200 + headRx - 14), jy + 84), Pt(FX(200 + headRx - 6), jy + 36),
                Pt(FX(200 + 70), jy + 62), Pt(cx, jy + 70), Pt(FX(200 - 70), jy + 62)
            };
            PointF[] beard = Geo.Smooth(pts.ToArray(), true, 6);
            Fill(beard, w);
            // hair strands
            var pen = Pn(sh, ow * .9f);
            for (int i = -2; i <= 2; i++) g.DrawLine(pen, Pt(cx + i * 22, jy + 96), Pt(cx + i * 20 + sway * .4f, jy + 160 - Math.Abs(i) * 10 + sway * .5f));
            // moustache
            float mm = p.Mouth == MouthStyle.Open ? p.MouthOpen * 6 : 0;
            for (int s = -1; s <= 1; s += 2)
            {
                float bx = cx + s * 4, by = jy + 66 - mm;
                PointF[] mu = Geo.Smooth(new[] { Pt(bx, by - 8), Pt(bx + s * 30, by - 10), Pt(bx + s * 56, by + 6), Pt(bx + s * 66, by + 26 - mm), Pt(bx + s * 44, by + 16), Pt(bx + s * 18, by + 10), Pt(bx, by + 10) }, true, 4);
                Fill(mu, w);
            }
        }

        // Piccolo's turban and antennae, Roshi's bald-head shine
        private void DrawHeadTop()
        {
            if (who == RigCharacter.Piccolo)
            {
                float sway = (p.EarL + p.EarR) * .5f;
                for (int s = -1; s <= 1; s += 2)
                {
                    float bx = 200 + s * 28, by = headCy - headRy - 18;
                    float tx = bx + s * 20 + (s < 0 ? p.EarL : p.EarR) * 1.4f, ty = by - 54;
                    PointF[] ant = Geo.Limb(new[] { Pt(bx, by), Pt((bx + tx) / 2 + s * 4, (by + ty) / 2), Pt(tx, ty) }, new[] { 12f, 9f, 6f }, true, 5);
                    Fill(ant, skin);
                }
                // turban (white cloth)
                float cy = headCy - 62;
                PointF[] turban = Geo.Ellipse(200 + (Side ? -4 : 0), cy, headRx + 8, 60, 30);
                Fill(turban, cape);
                var fold = Pn(Color.FromArgb(190, 196, 214), ow * 1.1f);
                g.DrawArc(fold, 112, cy - 40, 176, 80, 200, 120); g.DrawArc(fold, 128, cy - 30, 150, 66, 215, 100);
                g.DrawArc(Pn(Color.FromArgb(150, 156, 180), ow * 1.4f), 108, headCy - 36, 184, 36, 20, 140);
            }
            else if (who == RigCharacter.Roshi)
            {
                // glossy bald-head shine
                g.DrawArc(Pn(Color.FromArgb(220, 255, 255, 255), ow * 1.8f), 150, headCy - headRy + 8, 90, 46, 200, 60);
                g.DrawArc(Pn(Color.FromArgb(150, 255, 255, 255), ow * 1.2f), 246, headCy - headRy + 16, 24, 26, 270, 60);
            }
        }

        // -------- tears, sweat, anger mark --------
        private void DrawTears(float t)
        {
            float dx, rx, ry, pr, bb, bw; EyeParams(out dx, out rx, out ry, out pr, out bb, out bw);
            for (int side = 0; side < 2; side++)
            {
                float dir = side == 0 ? -1 : 1, cx = FX(200 + dir * dx) + dir * 2, top = eyeCy + ry * .8f;
                g.DrawLine(Pn(Color.FromArgb(190, 150, 205, 255), 9), Pt(cx, top), Pt(cx + dir * 3, top + 22 + 8 * (float)Math.Sin(t * 9 + side)));
                for (int i = 0; i < 2; i++)
                {
                    float q = (t * 1.5f + i * .5f + side * .27f) % 1f;
                    float x = cx + dir * (4 + q * 14), y = top + 10 + q * 90, r = 9 - 3.4f * q; int a = (int)(235 * (1 - q * q));
                    g.FillEllipse(B(Color.FromArgb(a, 120, 190, 255)), x - r, y - r * 1.3f, r * 2, r * 2.6f);
                    g.DrawEllipse(Pn(Color.FromArgb(a, 60, 110, 180), 3.2f), x - r, y - r * 1.3f, r * 2, r * 2.6f);
                }
            }
        }

        private void DrawSweat(float t)
        {
            float q = t % 1.2f / 1.2f;
            float x = 200 + headRx + 12 + 6 * q, y = headCy - 70 + q * 44; int a = (int)(235 * (1 - q * q * q));
            var pts = new[] { Pt(x, y - 18), Pt(x + 9, y + 2), Pt(x, y + 11), Pt(x - 9, y + 2) };
            using (var path = new GraphicsPath())
            {
                path.AddCurve(pts, 0f); path.CloseFigure();
                g.FillPath(B(Color.FromArgb(a, 170, 214, 255)), path); g.DrawPath(Pn(Color.FromArgb(a, 70, 120, 190), 3.4f), path);
            }
        }

        private void DrawAnger()
        {
            float[][] spots = { new[] { 200 - headRx - 6, headCy - 78 }, new[] { 200 + headRx + 6, headCy - 84 } };
            float pulse = 1 + .12f * (float)Math.Sin(p.Anger * 40);
            for (int i = 0; i < 2; i++)
            {
                float s = (i == 0 ? 17f : 14f) * pulse * Math.Min(1f, p.Anger * 1.2f);
                var st = g.Save(); g.TranslateTransform(spots[i][0], spots[i][1]);
                var pen = Pn(Color.FromArgb(226, 56, 44), 8f);
                g.DrawArc(pen, -s - 5, -s - 5, s + 2, s + 2, 100, 80); g.DrawArc(pen, 3, -s - 5, s + 2, s + 2, 280, 80);
                g.DrawArc(pen, -s - 5, 3, s + 2, s + 2, 80, -80); g.DrawArc(pen, 3, 3, s + 2, s + 2, 260, -80);
                g.Restore(st);
            }
        }
    }
}
