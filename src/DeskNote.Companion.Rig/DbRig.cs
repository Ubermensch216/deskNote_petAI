// Taken from the desktop pet this project's characters were drawn for, and kept in its style.
#nullable disable
using System;
using DeskNote.Companion.Core;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DeskNote.Companion.Rig
{
    /// <summary>Draws the five characters part by part (Goku, kid Goku, Roshi, Vegeta, Piccolo). Coordinates are in units of a 571-unit-tall figure.</summary>
    internal sealed class DbRig : CharRig
    {
        private readonly RigCharacter who;
        public DbRig(RigCharacter who) { this.who = who; }
        public RigCharacter Who { get { return who; } }

        public override float RefH { get { return 571f; } }
        public override float FootX { get { return 200f; } }
        public override float FootY { get { return 571f; } }
        public override float HeadAxisDx { get { return 0; } }
        public override float GripY { get { return 70; } }
        public override float BodyRefW { get { return who == RigCharacter.Piccolo ? 210f : 190f; } }
        public override float NominalW { get { return 270f; } }
        public override float BellyY { get { return 430; } }
        public override float HeadCx { get { return 200; } }
        public override float HeadCy { get { return who == RigCharacter.Kid ? 180 : who == RigCharacter.Roshi ? 185 : 160; } }
        public override float HeadSize { get { return who == RigCharacter.Piccolo ? 250 : 240; } }

        protected override void DrawBody(Graphics g, RigPose p, float k, bool faceRight)
        {
            using (var painter = new DbPainter(g, p, who, k, faceRight)) painter.Run();
        }
    }

    /// <summary>The brushes, dimensions and colours for one paint pass; a new one is made for every frame.</summary>
    internal sealed partial class DbPainter : IDisposable
    {
        private readonly Graphics g; private readonly RigPose p; private readonly RigCharacter who; private readonly float k; private readonly bool faceRight;
        private readonly List<IDisposable> trash = new List<IDisposable>();
        private Pen ink; private float ow;
        private static readonly Color cInk = Color.FromArgb(44, 32, 36);
        private static readonly Color cWhite = Color.FromArgb(250, 250, 252);

        // dimensions
        private float shY, shDx, topW, botW, waistY, hipY, hipDx, footDx, legW, l1, l2, armW, headCy, headRx, headRy, jaw;
        // colours
        private Color skin, skinSh, hair, hairHi, top, topSh, under, belt, pants, pantsSh, shoe, shoeTrim, glove, cape, eyeIris;
        // derived values
        private float turn, muscle, ape, sup;
        private PointF shL, shR;
        private readonly PointF[] wrist = new PointF[2], hdir = new PointF[2];
        private float torsoCx { get { return 200 - 36 * turn; } }

        internal DbPainter(Graphics g, RigPose p, RigCharacter who, float k, bool faceRight)
        { this.g = g; this.p = p; this.who = who; this.k = k; this.faceRight = faceRight; Setup(); }

        public void Dispose() { foreach (var d in trash) d.Dispose(); trash.Clear(); }

        private SolidBrush B(Color c) { var b = new SolidBrush(c); trash.Add(b); return b; }
        private Pen Pn(Color c, float w) { var pn = new Pen(c, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }; trash.Add(pn); return pn; }
        private static Color Mix(Color a, Color b, float t)
        { t = Math.Max(0, Math.Min(1, t)); return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        private static Color Al(Color c, float a) { return Color.FromArgb((int)Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }
        private static PointF Pt(float x, float y) { return new PointF(x, y); }

        private void Fill(PointF[] pts, Color fill, bool edge = true)
        { g.FillPolygon(B(fill), pts); if (edge) g.DrawPolygon(ink, pts); }

        // -------- per-character dimensions and colours --------
        private void Setup()
        {
            turn = Math.Max(0, Math.Min(1, p.Turn)); sup = Math.Max(0, Math.Min(1, p.Super));
            muscle = who == RigCharacter.Roshi ? Math.Max(0, Math.Min(1, p.Muscle)) : 0;
            ape = who == RigCharacter.Kid ? Math.Max(0, Math.Min(1, p.Ape)) : 0;
            jaw = .2f; eyeIris = Color.FromArgb(20, 18, 22);
            hairHi = Color.FromArgb(86, 92, 120);
            switch (who)
            {
                case RigCharacter.Kid:
                    shY = 300; shDx = 70; topW = 140; botW = 124; waistY = 448; hipY = 452; hipDx = 32; footDx = 42; legW = 46; l1 = 52; l2 = 48; armW = 38;
                    headCy = 182; headRx = 110; headRy = 102; jaw = 0;
                    skin = Color.FromArgb(255, 218, 176); skinSh = Color.FromArgb(236, 178, 134);
                    hair = Color.FromArgb(34, 30, 38);
                    top = Color.FromArgb(244, 124, 30); topSh = Color.FromArgb(208, 94, 14); under = Color.FromArgb(40, 100, 178);
                    belt = Color.FromArgb(40, 100, 178); pants = Color.FromArgb(244, 124, 30); pantsSh = topSh;
                    shoe = Color.FromArgb(44, 88, 150); shoeTrim = Color.FromArgb(250, 224, 110); glove = skin;
                    break;
                case RigCharacter.Roshi:
                    shY = 300; shDx = 84; topW = 172; botW = 170; waistY = 446; hipY = 446; hipDx = 38; footDx = 46; legW = 46; l1 = 58; l2 = 52; armW = 38;
                    headCy = 186; headRx = 92; headRy = 88; jaw = .1f;
                    skin = Color.FromArgb(252, 214, 172); skinSh = Color.FromArgb(232, 176, 134);
                    hair = Color.FromArgb(250, 250, 252);
                    top = Color.FromArgb(126, 208, 232); topSh = Color.FromArgb(92, 174, 204); under = skin;
                    belt = Color.FromArgb(70, 130, 160); pants = Color.FromArgb(52, 150, 142); pantsSh = Color.FromArgb(38, 120, 114);
                    shoe = Color.FromArgb(150, 98, 52); shoeTrim = Color.FromArgb(110, 70, 36); glove = skin;
                    break;
                case RigCharacter.Vegeta:
                    shY = 286; shDx = 86; topW = 172; botW = 130; waistY = 440; hipY = 442; hipDx = 36; footDx = 46; legW = 48; l1 = 62; l2 = 58; armW = 40;
                    headCy = 160; headRx = 94; headRy = 90; jaw = .55f;
                    skin = Color.FromArgb(255, 210, 166); skinSh = Color.FromArgb(234, 172, 128);
                    hair = Color.FromArgb(30, 28, 38);
                    top = Color.FromArgb(48, 80, 176); topSh = Color.FromArgb(34, 58, 140); under = top;
                    belt = Color.FromArgb(240, 240, 244); pants = top; pantsSh = topSh;
                    shoe = Color.FromArgb(244, 244, 248); shoeTrim = Color.FromArgb(236, 184, 60); glove = Color.FromArgb(248, 248, 250);
                    break;
                case RigCharacter.Piccolo:
                    shY = 284; shDx = 90; topW = 176; botW = 136; waistY = 438; hipY = 440; hipDx = 38; footDx = 48; legW = 48; l1 = 66; l2 = 62; armW = 40;
                    headCy = 156; headRx = 92; headRy = 92; jaw = .35f;
                    skin = Color.FromArgb(118, 182, 74); skinSh = Color.FromArgb(84, 144, 50);
                    hair = skin;
                    top = Color.FromArgb(104, 56, 138); topSh = Color.FromArgb(76, 38, 104); under = top;
                    belt = Color.FromArgb(188, 44, 70); pants = top; pantsSh = topSh;
                    shoe = Color.FromArgb(188, 122, 66); shoeTrim = Color.FromArgb(240, 214, 150); glove = skin;
                    cape = Color.FromArgb(246, 246, 250);
                    break;
                default:
                    shY = 288; shDx = 80; topW = 160; botW = 136; waistY = 440; hipY = 442; hipDx = 36; footDx = 46; legW = 50; l1 = 60; l2 = 56; armW = 40;
                    headCy = 160; headRx = 98; headRy = 92;
                    skin = Color.FromArgb(255, 214, 170); skinSh = Color.FromArgb(234, 176, 128);
                    hair = Color.FromArgb(32, 28, 36);
                    top = Color.FromArgb(244, 124, 30); topSh = Color.FromArgb(208, 94, 14); under = Color.FromArgb(40, 100, 178);
                    belt = Color.FromArgb(40, 100, 178); pants = Color.FromArgb(244, 124, 30); pantsSh = topSh;
                    shoe = Color.FromArgb(44, 88, 150); shoeTrim = Color.FromArgb(250, 224, 110); glove = skin;
                    break;
            }
            if (who != RigCharacter.Piccolo && who != RigCharacter.Roshi)
            {
                // transformation: golden hair (Super Saiyan) and teal eyes
                hair = Mix(hair, Color.FromArgb(255, 226, 64), sup); hairHi = Mix(hairHi, Color.FromArgb(255, 252, 190), sup);
                eyeIris = Mix(eyeIris, Color.FromArgb(46, 196, 190), sup);
            }
            if (ape > 0)
            {
                // great ape: fur grows in and the eyes turn red
                Color fur = Color.FromArgb(150, 96, 52);
                skin = Mix(skin, fur, ape); skinSh = Mix(skinSh, Color.FromArgb(112, 70, 36), ape); hair = Mix(hair, Color.FromArgb(62, 40, 26), ape);
                top = Mix(top, Color.FromArgb(200, 110, 40), ape * .5f); glove = skin; eyeIris = Mix(eyeIris, Color.FromArgb(236, 40, 40), ape);
                armW *= 1 + .3f * ape; l1 *= 1 + .28f * ape; l2 *= 1 + .28f * ape; legW *= 1 + .12f * ape;
            }
            if (muscle > 0) { armW *= 1 + .62f * muscle; legW *= 1 + .22f * muscle; topW *= 1 + .26f * muscle; shDx *= 1 + .12f * muscle; }
            // body turn: the shoulders draw together and the whole body swings to face front (left)
            shL = Pt(200 - shDx * (1 - .5f * turn) - 18 * turn, shY);
            shR = Pt(200 + shDx * (1 - .5f * turn) - 52 * turn, shY + 4 * turn);
        }

        // -------- the whole figure --------
        public void Run()
        {
            ResolveReach();
            var saved = g.Save();
            g.ScaleTransform(faceRight ? -k : k, k);
            g.TranslateTransform(-200, -571);
            ow = Math.Max(3.4f, .95f / k); ink = Pn(cInk, ow);
            DrawAura();
            var body = g.Save();
            BodyTransform(); DrawBackLayer(); g.Restore(body);

            if (!p.Sit) DrawLegs();
            body = g.Save(); BodyTransform();
            if (!p.ArmsFront) DrawArms();
            DrawTorso();
            if (p.ArmsFront) DrawArms();
            DrawHead();
            DrawSnack(); DrawProp(); DrawEnergy();
            g.Restore(body);
            if (p.Sit) DrawLegs();
            DrawNimbus(); DrawOverlay();
            g.Restore(saved);
        }

        private void BodyTransform()
        {
            g.TranslateTransform(p.BodyX, p.BodyY);
            g.TranslateTransform(200, 520); g.RotateTransform(p.Lean); g.TranslateTransform(-200, -520);
        }

        // -------- hand targets (Reach) --------
        private void ResolveReach()
        {
            if (p.Reach == Reach.None && !p.Clasp && !p.HandLTo.HasValue && !p.HandRTo.HasValue && who == RigCharacter.Roshi && p.Staff >= 0 && p.Staff < 1.5f && p.ArmRA <= 70 && p.Prop == 0 && p.Sit == false)
            {
                // Roshi always leans on his staff with the right hand
                p.Staff = 1; p.HandRTo = Pt(shR.X + 26 + 8 * turn, 418);
            }
            if (p.Reach == Reach.None) { if (p.Wipe >= 0 && p.Reach == Reach.None) { } return; }
            float w = p.Wipe >= 0 ? p.Wipe * 6.28f : 0; bool wob = p.Wipe >= 0;
            float ox = wob ? 9 * (float)Math.Sin(w) : 0, oy = wob ? 7 * (float)Math.Cos(w * 1.3f) : 0;
            float mouthY = headCy + 92;
            PointF? L = null, R = null;
            switch (p.Reach)
            {
                case Reach.Lap: L = Pt(172, 484); R = Pt(228, 484); break;
                case Reach.Belly: L = Pt(178, BellyYy() + 2); R = Pt(222, BellyYy() + 2); break;
                case Reach.Mouth: L = Pt(184, mouthY + 6); R = Pt(216, mouthY + 6); break;
                case Reach.Chest: L = Pt(182, shY + 66); R = Pt(218, shY + 66); break;
                case Reach.Scratch: R = Pt(272 - 6 * turn, headCy - 70); break;
                case Reach.Chin: R = Pt(214, headCy + 110); break;
                case Reach.Cross: L = Pt(248, shY + 42); R = Pt(152, shY + 50); break;
                case Reach.Overhead: L = Pt(166, 26); R = Pt(234, 26); break;
                case Reach.Forehead: L = Pt(shL.X - 4, waistY - 24); R = Pt(228, headCy - 34); break;
                case Reach.Hips: L = Pt(shL.X - 10, waistY - 40); R = Pt(shR.X + 10, waistY - 40); break;
                case Reach.Cup: L = Pt(244, waistY - 34); R = Pt(262, waistY - 44); break;
                case Reach.Front: L = Pt(46, shY + 52); R = Pt(86, shY + 48); break;
                case Reach.Flex: L = Pt(shL.X - 70, shY - 52); R = Pt(shR.X + 70, shY - 52); break;
                case Reach.Guard: L = Pt(shL.X - 24, shY + 46); R = Pt(shL.X + 6, shY + 6); break;
            }
            if (L.HasValue) p.HandLTo = Pt(L.Value.X + ox, L.Value.Y + oy);
            if (R.HasValue) p.HandRTo = Pt(R.Value.X - ox, R.Value.Y - oy);
        }
        private float BellyYy() { return who == RigCharacter.Roshi ? 432 : 424; }

        // -------- back layer (cape, shell, tail, power pole) --------
        private void DrawBackLayer()
        {
            switch (who)
            {
                case RigCharacter.Piccolo:
                    if (sup < .5f) DrawCape();
                    break;
                case RigCharacter.Roshi:
                    DrawShell();
                    break;
                case RigCharacter.Kid:
                    DrawKidBack();
                    break;
            }
        }

        private void DrawCape()
        {
            float sway = p.Tail * 1.1f + (float)Math.Sin(p.TailWavePhase) * p.TailWaveAmp * .7f;
            float hw = topW / 2 + 12;
            var pts = new List<PointF>();
            pts.Add(Pt(torsoCx - hw, shY - 8)); pts.Add(Pt(torsoCx - hw - 10, shY + 60));
            float bot = waistY + 40;
            for (int i = 0; i <= 6; i++)
            {
                float u = i / 6f, x = torsoCx - hw - 22 + (hw * 2 + 44) * u + sway * (.6f + u * .2f);
                float y = bot + (float)Math.Sin(u * Math.PI * 3 + p.TailWavePhase) * 7 * (.5f + Math.Abs(p.TailWaveAmp) / 12f) + (i % 2 == 0 ? 8 : -2);
                pts.Add(Pt(x, y));
            }
            pts.Add(Pt(torsoCx + hw + 10, shY + 60)); pts.Add(Pt(torsoCx + hw, shY - 8));
            PointF[] cp = Geo.Smooth(pts.ToArray(), true, 5);
            Fill(cp, cape);
            using (var gp = new GraphicsPath())
            {
                gp.AddPolygon(cp); var st = g.Save(); g.SetClip(gp, CombineMode.Intersect);
                g.FillPolygon(B(Color.FromArgb(70, 140, 140, 170)), new[] { Pt(torsoCx - 20, shY), Pt(torsoCx + hw + 40, shY), Pt(torsoCx + hw + 60, bot + 30), Pt(torsoCx + 10, bot + 30) });
                g.Restore(st);
            }
            g.DrawPolygon(ink, cp);
        }

        private void DrawShell()
        {
            Color a = Color.FromArgb(104, 140, 76), b = Color.FromArgb(70, 100, 52);
            float cx = torsoCx, cy = shY + 82, rx = topW / 2 + 38, ry = 112;
            PointF[] shell = Geo.Ellipse(cx, cy, rx, ry, 36);
            Fill(shell, a);
            // hexagon pattern
            using (var gp = new GraphicsPath())
            {
                gp.AddPolygon(shell); var st = g.Save(); g.SetClip(gp, CombineMode.Intersect);
                var pen = Pn(b, ow * .9f);
                for (int row = -1; row <= 1; row++)
                    for (int col = -2; col <= 2; col++)
                    {
                        float hx = cx + col * 52 + (row % 2 != 0 ? 26 : 0), hy = cy + row * 44; var hex = new PointF[6];
                        for (int i = 0; i < 6; i++) { double an = Math.PI / 3 * i + Math.PI / 6; hex[i] = Pt(hx + (float)Math.Cos(an) * 30, hy + (float)Math.Sin(an) * 30); }
                        g.DrawPolygon(pen, hex);
                    }
                g.Restore(st);
            }
            g.DrawPolygon(ink, shell);
        }

        private PointF[] Chain(PointF origin, float[] len, float[] baseDeg, float tail, float curl, float wave, float phase)
        {
            var pts = new PointF[len.Length + 1]; pts[0] = origin;
            for (int i = 0; i < len.Length; i++)
            {
                double fr = (i + 1) / (double)len.Length, a = (baseDeg[i] + tail + curl * fr + wave * Math.Sin(phase - i * .9) * fr) * Math.PI / 180;
                pts[i + 1] = Pt(pts[i].X + (float)Math.Cos(a) * len[i], pts[i].Y + (float)Math.Sin(a) * len[i]);
            }
            return pts;
        }

        private void DrawKidBack()
        {
            // the power pole on the back (only its two ends stick out beside the body)
            if (p.Staff < 1.5f)
            {
                PointF a = Pt(torsoCx - topW * .52f, waistY - 6), b = Pt(torsoCx + topW * .6f, shY - 34);
                g.DrawLine(Pn(cInk, 20 + ow * 1.4f), a, b);
                g.DrawLine(Pn(Color.FromArgb(214, 52, 44), 18), a, b);
                g.DrawLine(Pn(Color.FromArgb(246, 200, 70), 18), Pt(a.X - 2, a.Y + 3), Pt(a.X + 10, a.Y - 14));
                g.DrawLine(Pn(Color.FromArgb(246, 200, 70), 18), Pt(b.X + 2, b.Y - 3), Pt(b.X - 10, b.Y + 14));
            }
            // monkey tail
            float[] len = { 36, 36, 34, 32, 28 }; float[] bd = { 25, -4, -34, -62, -84 };
            float sc = 1 + .5f * ape;
            for (int i = 0; i < len.Length; i++) len[i] *= sc;
            PointF[] sp = Chain(Pt(torsoCx + botW * .42f, waistY - 6), len, bd, p.Tail * .8f, p.TailCurl, p.TailWaveAmp, p.TailWavePhase);
            PointF[] tail = Geo.Limb(sp, new[] { 26f * sc, 24f * sc, 22f * sc, 18f * sc, 13f * sc, 9f * sc }, true, 6);
            Fill(tail, Color.FromArgb(158, 100, 48));
        }

        // -------- legs --------
        private void DrawLegs()
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;
                float fx = right ? p.FootRX : p.FootLX, fy = right ? p.FootRY : p.FootLY, rot = right ? p.FootRRot : p.FootLRot;
                float sgn = right ? 1 : -1;
                float hx = torsoCx + sgn * hipDx + p.BodyX * .6f + 14 * turn * 0, hy = hipY + p.BodyY;
                PointF anchor = Pt(200 + sgn * footDx + fx - 10 * turn, 548 + fy);
                float bend = (p.Knee + Math.Max(0, p.BodyY) * .3f) * sgn;
                PointF mid = Pt((hx + anchor.X) / 2 + bend, (hy + anchor.Y) / 2);
                PointF[] leg = Geo.Limb(new[] { Pt(hx, hy), mid, Pt(anchor.X, anchor.Y - 4) }, new[] { legW, legW * .9f, legW * .84f }, false, 6);
                if (who == RigCharacter.Roshi)
                {
                    Fill(leg, skin);
                    PointF[] sh = Geo.Limb(new[] { Pt(hx, hy), Pt((hx + mid.X) / 2 + sgn * 3, (hy + mid.Y) / 2 + 6) }, new[] { legW * 1.12f, legW * 1.08f }, false, 4);
                    Fill(sh, pants);
                }
                else
                {
                    Fill(leg, pants);
                    if (who == RigCharacter.Vegeta)
                    {
                        // white boot cuff
                        PointF bt = Pt(mid.X + (anchor.X - mid.X) * .35f, mid.Y + (anchor.Y - mid.Y) * .35f);
                        PointF[] boot = Geo.Limb(new[] { bt, Pt(anchor.X, anchor.Y - 4) }, new[] { legW * .92f, legW * .86f }, false, 4);
                        Fill(boot, shoe);
                        g.DrawLine(Pn(shoeTrim, ow * 1.6f), Pt(bt.X - legW * .44f, bt.Y), Pt(bt.X + legW * .44f, bt.Y));
                    }
                }
                DrawShoe(anchor, right, rot);
            }
        }

        private void DrawShoe(PointF anchor, bool right, float rot)
        {
            PointF[] baseShape =
            {
                Pt(-22, -26), Pt(22, -26), Pt(27, -2), Pt(25, 22), Pt(-32, 23), Pt(-52, 15), Pt(-48, -1), Pt(-30, -10)
            };
            double a = (right ? -rot : rot) * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            PointF Tx(PointF q) { float x = right ? -q.X : q.X; return Pt(anchor.X + (float)(x * ca - q.Y * sa), anchor.Y + (float)(x * sa + q.Y * ca)); }
            var shoeP = new PointF[baseShape.Length];
            for (int i = 0; i < shoeP.Length; i++) shoeP[i] = Tx(baseShape[i]);
            if (who == RigCharacter.Roshi)
            {
                // sandals: bare foot on a brown sole
                Fill(shoeP, skin);
                PointF[] sole = { Tx(Pt(-34, 14)), Tx(Pt(-52, 15)), Tx(Pt(-50, 22)), Tx(Pt(25, 23)), Tx(Pt(26, 14)) };
                Fill(sole, shoe);
                g.DrawLine(Pn(shoeTrim, ow * 1.5f), Tx(Pt(-10, -10)), Tx(Pt(8, 14)));
                return;
            }
            Fill(shoeP, shoe);
            // sole and trim line
            PointF[] sl = { Tx(Pt(-34, 16)), Tx(Pt(-52, 15)), Tx(Pt(-50, 22)), Tx(Pt(25, 23)), Tx(Pt(26, 16)) };
            g.FillPolygon(B(who == RigCharacter.Vegeta ? Color.FromArgb(236, 184, 60) : who == RigCharacter.Piccolo ? Color.FromArgb(120, 76, 40) : Color.FromArgb(240, 236, 224)), sl);
            g.DrawLine(Pn(shoeTrim, ow * 1.5f), Tx(Pt(-20, -22)), Tx(Pt(24, -22)));
            if (who == RigCharacter.Goku || who == RigCharacter.Kid) g.DrawLine(Pn(shoeTrim, ow * 1.2f), Tx(Pt(-22, -10)), Tx(Pt(25, -10)));
            g.DrawPolygon(ink, shoeP);
        }

        // -------- torso --------
        private PointF[] TorsoShape()
        {
            float tw = topW / 2 * (1 - .42f * turn), bw = botW / 2 * (1 - .36f * turn), cx = torsoCx;
            float belly = who == RigCharacter.Roshi ? 1.06f : 1f;
            var l = new[]
            {
                Pt(cx - tw * .34f, shY - 14), Pt(cx - tw * .9f, shY - 6), Pt(cx - tw, shY + 22), Pt(cx - (tw + bw) / 2 * belly, shY + (waistY - shY) * .52f),
                Pt(cx - bw * belly, waistY - 8), Pt(cx - bw * .92f, waistY + 14)
            };
            var all = new List<PointF>(l); all.Add(Pt(cx, waistY + 22));
            for (int i = l.Length - 1; i >= 0; i--) all.Add(Pt(2 * cx - l[i].X, l[i].Y));
            return Geo.Smooth(all.ToArray(), true, 6);
        }

        private void DrawNeck()
        {
            float nw = 54 * (who == RigCharacter.Kid ? .9f : 1f);
            g.FillRectangle(B(skinSh), 200 - nw / 2, headCy + headRy - 24, nw, shY - (headCy + headRy) + 40);
            g.DrawRectangle(ink, 200 - nw / 2, headCy + headRy - 24, nw, shY - (headCy + headRy) + 40);
        }

        private void DrawTorso()
        {
            DrawNeck();
            PointF[] torso = TorsoShape();
            float br = 1 + p.Breath * .03f;
            var st0 = g.Save();
            g.TranslateTransform(torsoCx, 380); g.ScaleTransform(br, br + p.Breath * .01f); g.TranslateTransform(-torsoCx, -380);
            Fill(torso, who == RigCharacter.Roshi && muscle > .45f ? Mix(top, skin, Math.Min(1, (muscle - .3f) * 2)) : top);
            using (var gp = new GraphicsPath())
            {
                gp.AddPolygon(torso); var st = g.Save(); g.SetClip(gp, CombineMode.Intersect);
                switch (who)
                {
                    case RigCharacter.Goku: case RigCharacter.Kid: GiDetails(); break;
                    case RigCharacter.Vegeta: ArmorDetails(); break;
                    case RigCharacter.Piccolo: PiccoloDetails(); break;
                    case RigCharacter.Roshi: AlohaDetails(); break;
                }
                g.Restore(st);
            }
            g.DrawPolygon(ink, torso);
            g.Restore(st0);
            if (who == RigCharacter.Piccolo) DrawPads(Color.FromArgb(246, 246, 250), 54);
            if (who == RigCharacter.Vegeta) DrawPads(Color.FromArgb(240, 190, 60), 46);
        }

        private void GiDetails()
        {
            float cx = torsoCx, tw = topW / 2 * (1 - .42f * turn);
            // blue undershirt with a V neckline
            PointF[] v = { Pt(cx - tw * .46f, shY - 16), Pt(cx + tw * .46f, shY - 16), Pt(cx + tw * .08f, shY + 62), Pt(cx - tw * .08f, shY + 62) };
            Fill(v, under);
            // gi overlap lines
            g.DrawLine(Pn(cInk, ow), Pt(cx - tw * .46f, shY - 10), Pt(cx + tw * .16f, waistY - 18));
            g.DrawLine(Pn(cInk, ow), Pt(cx + tw * .46f, shY - 10), Pt(cx - tw * .16f, waistY - 18));
            // turtle school mark
            float ex = cx - tw * .46f - 4 * turn, ey = shY + 78, r = who == RigCharacter.Kid ? 21f : 25f;
            g.FillEllipse(B(cWhite), ex - r, ey - r, r * 2, r * 2); g.DrawEllipse(ink, ex - r, ey - r, r * 2, r * 2);
            using (var f = new Font("Malgun Gothic", r * 1.2f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("亀", f, B(Color.FromArgb(200, 40, 30)), new RectangleF(ex - r, ey - r - 1, r * 2, r * 2 + 2), fmt);
            // blue sash
            DrawBelt(under, true);
        }

        private void DrawBelt(Color c, bool knot)
        {
            float cx = torsoCx, bw = botW / 2 * (1 - .36f * turn);
            PointF[] band = { Pt(cx - bw - 6, waistY - 30), Pt(cx + bw + 6, waistY - 30), Pt(cx + bw + 4, waistY + 4), Pt(cx - bw - 4, waistY + 4) };
            Fill(band, c);
            if (knot)
            {
                float kx = cx - bw * .2f;
                PointF[] tail1 = { Pt(kx, waistY - 12), Pt(kx - 20, waistY + 40), Pt(kx - 4, waistY + 44), Pt(kx + 10, waistY - 4) };
                PointF[] tail2 = { Pt(kx, waistY - 12), Pt(kx + 24, waistY + 36), Pt(kx + 38, waistY + 28), Pt(kx + 12, waistY - 8) };
                Fill(tail1, c); Fill(tail2, c);
                g.FillEllipse(B(c), kx - 12, waistY - 26, 26, 26); g.DrawEllipse(ink, kx - 12, waistY - 26, 26, 26);
            }
        }

        private void ArmorDetails()
        {
            float cx = torsoCx, tw = topW / 2 * (1 - .42f * turn), bw = botW / 2 * (1 - .36f * turn);
            // white chest armour
            PointF[] chest = { Pt(cx - tw * 1.0f, shY - 4), Pt(cx + tw * 1.0f, shY - 4), Pt(cx + bw * .98f, waistY - 52), Pt(cx + bw * .6f, waistY - 36), Pt(cx - bw * .6f, waistY - 36), Pt(cx - bw * .98f, waistY - 52) };
            Fill(chest, Color.FromArgb(244, 244, 248));
            g.DrawLine(Pn(Color.FromArgb(200, 200, 210), ow), Pt(cx, shY + 10), Pt(cx, waistY - 40));
            // waist armour (white skirt)
            PointF[] skirtL = { Pt(cx - bw - 4, waistY - 40), Pt(cx - 6, waistY - 40), Pt(cx - 10, waistY + 36), Pt(cx - bw + 6, waistY + 28) };
            PointF[] skirtR = { Pt(cx + bw + 4, waistY - 40), Pt(cx + 6, waistY - 40), Pt(cx + 10, waistY + 36), Pt(cx + bw - 6, waistY + 28) };
            Fill(skirtL, Color.FromArgb(244, 244, 248)); Fill(skirtR, Color.FromArgb(244, 244, 248));
            g.DrawLine(Pn(Color.FromArgb(236, 184, 60), ow * 1.5f), Pt(cx - bw - 2, waistY - 38), Pt(cx + bw + 2, waistY - 38));
        }

        private void PiccoloDetails()
        {
            float cx = torsoCx, bw = botW / 2 * (1 - .36f * turn), tw = topW / 2 * (1 - .42f * turn);
            g.DrawLine(Pn(topSh, ow * 1.2f), Pt(cx - tw * .5f, shY - 8), Pt(cx + tw * .1f, waistY - 40));
            g.DrawLine(Pn(topSh, ow * 1.2f), Pt(cx + tw * .5f, shY - 8), Pt(cx - tw * .1f, waistY - 40));
            DrawBelt(belt, false);
            // sash knot
            g.FillEllipse(B(belt), cx - 18, waistY - 26, 36, 30); g.DrawEllipse(ink, cx - 18, waistY - 26, 36, 30);
        }

        private void AlohaDetails()
        {
            float cx = torsoCx, tw = topW / 2 * (1 - .42f * turn);
            // open shirt: bare skin down the middle
            PointF[] open = { Pt(cx - tw * .3f, shY - 14), Pt(cx + tw * .3f, shY - 14), Pt(cx + tw * .12f, waistY), Pt(cx - tw * .12f, waistY) };
            Fill(open, skin);
            // flower print
            var rnd = new Random(7);
            Color[] fl = { Color.FromArgb(246, 120, 150), Color.FromArgb(250, 220, 90), Color.FromArgb(255, 255, 255) };
            for (int i = 0; i < 18; i++)
            {
                float fx = cx + (float)(rnd.NextDouble() * 2 - 1) * tw * 1.1f, fy = shY + 14 + (float)rnd.NextDouble() * (waistY - shY - 30);
                if (Math.Abs(fx - cx) < tw * .3f) continue;
                for (int j = 0; j < 5; j++) { double a = j * Math.PI * 2 / 5; g.FillEllipse(B(fl[i % 3]), fx + (float)Math.Cos(a) * 8 - 5, fy + (float)Math.Sin(a) * 8 - 5, 10, 10); }
                g.FillEllipse(B(Color.FromArgb(240, 150, 40)), fx - 4, fy - 4, 8, 8);
            }
            g.DrawLine(Pn(cInk, ow), Pt(cx - tw * .3f, shY - 10), Pt(cx - tw * .12f, waistY));
            g.DrawLine(Pn(cInk, ow), Pt(cx + tw * .3f, shY - 10), Pt(cx + tw * .12f, waistY));
            if (muscle > .4f)
            {
                // muscles: bare chest and abs
                float m = Math.Min(1, (muscle - .3f) * 1.6f);
                PointF[] bare = { Pt(cx - tw * 1.05f, shY - 8), Pt(cx + tw * 1.05f, shY - 8), Pt(cx + tw * .86f, waistY), Pt(cx - tw * .86f, waistY) };
                g.FillPolygon(B(Al(skin, 255 * m)), bare);
                var pen = Pn(Al(skinSh, 255 * m), ow * 1.1f);
                g.DrawLine(pen, Pt(cx, shY + 10), Pt(cx, waistY - 10));
                g.DrawArc(pen, cx - tw * .78f, shY + 22, tw * .76f, 56, 20, 140); g.DrawArc(pen, cx + 2, shY + 22, tw * .76f, 56, 20, 140);
                for (int r = 0; r < 3; r++) g.DrawLine(pen, Pt(cx - tw * .36f, shY + 96 + r * 28), Pt(cx + tw * .36f, shY + 96 + r * 28));
            }
            DrawBelt(belt, false);
        }

        private void DrawPads(Color c, float w)
        {
            // shoulder pads (laid over the arms)
            PointF[] sh = { shL, shR };
            for (int i = 0; i < 2; i++)
            {
                float sx = sh[i].X + (i == 0 ? -6 : 6), sy = sh[i].Y - 4;
                var st = g.Save(); g.TranslateTransform(sx, sy); g.RotateTransform(i == 0 ? -14 : 14);
                float ww = w * (who == RigCharacter.Piccolo ? 1.2f : 1f) * (1 - .2f * turn);
                PointF[] pad = Geo.Ellipse(0, 0, ww, who == RigCharacter.Piccolo ? 26 : 24, 20);
                Fill(pad, c);
                g.DrawLine(Pn(Al(cInk, 120), ow * .8f), Pt(-ww * .7f, 4), Pt(ww * .7f, 4));
                g.Restore(st);
            }
        }

        // -------- arms and hands --------
        private static float MapA(float a) { return Math.Max(-24, Math.Min(200, 16 + (a - 60) * 1.2f)); }
        private static float MapB(float b) { return Math.Max(-24, Math.Min(210, 10 + (b - 43) * 1.2f)); }

        private void DrawArms()
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1; int outSign = right ? 1 : -1;
                PointF S = right ? shR : shL;
                float len = right ? p.ArmLenR : p.ArmLenL;
                float L1 = l1 * len, L2 = l2 * len, A, B2;
                PointF? to = p.Clasp ? Pt(torsoCx + (right ? 16 : -16), shY + 70) : (right ? p.HandRTo : p.HandLTo);
                if (to.HasValue) Kit.IK(S, to.Value, L1, L2, outSign, out A, out B2);
                else { A = MapA(right ? p.ArmRA : p.ArmLA); B2 = MapB(right ? p.ArmRB : p.ArmLB); }
                PointF E = Pt(S.X + outSign * (float)Math.Sin(A * Math.PI / 180) * L1, S.Y + (float)Math.Cos(A * Math.PI / 180) * L1);
                PointF d = Pt(outSign * (float)Math.Sin(B2 * Math.PI / 180), (float)Math.Cos(B2 * Math.PI / 180));
                PointF W = Pt(E.X + d.X * L2, E.Y + d.Y * L2);
                wrist[side] = W; hdir[side] = d;
                float aw = armW;
                PointF[] up = Geo.Limb(new[] { S, E }, new[] { aw * 1.05f, aw * .96f }, false, 4);
                PointF[] lo = Geo.Limb(new[] { E, W }, new[] { aw * .94f, aw * .74f }, false, 4);
                Color upC = ArmUpperColor(), loC = ArmLowerColor();
                Fill(lo, loC);
                DrawCuffs(E, W, side);
                Fill(up, upC);
                g.FillEllipse(B(upC), E.X - aw * .46f, E.Y - aw * .46f, aw * .92f, aw * .92f);
                if (muscle > .1f)
                {
                    // biceps
                    var pen = Pn(Al(skinSh, 200 * muscle), ow);
                    PointF mid = Pt((S.X + E.X) / 2, (S.Y + E.Y) / 2);
                    g.DrawArc(pen, mid.X - aw * .4f, mid.Y - aw * .5f, aw * .8f, aw, outSign < 0 ? 200 : -20, 140);
                }
            }
            DrawStaff();
            for (int side = 0; side < 2; side++)
                DrawHand(wrist[side], hdir[side], side == 1 ? p.HandR : p.HandL, side == 1);
        }

        private Color ArmUpperColor()
        {
            switch (who)
            {
                case RigCharacter.Goku: case RigCharacter.Kid: return ape > .5f ? skin : under;
                case RigCharacter.Vegeta: return top;
                case RigCharacter.Piccolo: return top;
                case RigCharacter.Roshi: return muscle > .45f ? skin : top;
                default: return skin;
            }
        }
        private Color ArmLowerColor()
        {
            switch (who)
            {
                case RigCharacter.Vegeta: return top;
                default: return skin;
            }
        }
        private void DrawCuffs(PointF E, PointF W, int side)
        {
            Color c;
            switch (who)
            {
                case RigCharacter.Goku: case RigCharacter.Kid: c = under; break;
                case RigCharacter.Piccolo: c = top; break;
                case RigCharacter.Vegeta: c = glove; break;
                default: return;
            }
            float t0 = who == RigCharacter.Vegeta ? .5f : .72f;
            PointF a = Pt(E.X + (W.X - E.X) * t0, E.Y + (W.Y - E.Y) * t0), b = Pt(E.X + (W.X - E.X) * 1.0f, E.Y + (W.Y - E.Y) * 1.0f);
            PointF[] band = Geo.Limb(new[] { a, b }, new[] { armW * .8f, armW * .74f }, false, 3);
            Fill(band, c);
        }

        private void DrawHand(PointF wr, PointF d, HandStyle style, bool right)
        {
            float ang = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI);
            var st = g.Save();
            g.TranslateTransform(wr.X, wr.Y); g.RotateTransform(ang);
            if (right) g.ScaleTransform(1, -1);
            float s = who == RigCharacter.Kid ? .88f : 1f; if (ape > 0) s *= 1 + .3f * ape;
            g.ScaleTransform(s, s);
            Color hc = glove;
            if (style == HandStyle.Fist)
            {
                g.FillEllipse(B(hc), -4, -19, 40, 38); g.DrawEllipse(ink, -4, -19, 40, 38);
                var kn = Pn(Al(cInk, 200), ow * .7f);
                g.DrawLine(kn, 22, -10, 33, -10); g.DrawLine(kn, 24, 0, 35, 0); g.DrawLine(kn, 22, 10, 33, 10);
            }
            else if (style == HandStyle.Point)
            {
                // hand with the index and middle fingers extended (instant transmission, special beam cannon)
                PointF[] palm = Geo.Smooth(new[] { Pt(-4, -15), Pt(18, -17), Pt(30, -8), Pt(30, 10), Pt(18, 17), Pt(-4, 15) }, true, 5);
                Fill(palm, hc);
                PointF[] f1 = Geo.Limb(new[] { Pt(24, -9), Pt(54, -8) }, new[] { 12f, 9f }, true, 3);
                PointF[] f2 = Geo.Limb(new[] { Pt(24, 1), Pt(56, 3) }, new[] { 12f, 9f }, true, 3);
                Fill(f1, hc); Fill(f2, hc);
                g.FillEllipse(B(hc), 8, 10, 18, 14); g.DrawEllipse(ink, 8, 10, 18, 14);
            }
            else
            {
                PointF[] hand = Geo.Smooth(new[] { Pt(-4, -16), Pt(16, -19), Pt(32, -12), Pt(38, -2), Pt(32, 8), Pt(20, 18), Pt(2, 18), Pt(-4, 12) }, true, 4);
                Fill(hand, hc);
                g.DrawLine(Pn(Al(cInk, 150), ow * .7f), Pt(22, -10), Pt(36, -6)); g.DrawLine(Pn(Al(cInk, 150), ow * .7f), Pt(20, -2), Pt(35, 1)); g.DrawLine(Pn(Al(cInk, 150), ow * .7f), Pt(18, 7), Pt(31, 9));
            }
            g.Restore(st);
        }
    }

}
