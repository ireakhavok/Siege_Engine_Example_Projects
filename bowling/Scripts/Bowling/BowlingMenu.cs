using System;
using System.Numerics;

namespace BowlingProject
{
    public struct MenuLayout
    {
        public float X, Y, W, H;
        public float BtnX, BtnY, Btn, Gap;
        public float StartX, StartY, StartW, StartH;

        public int Hit(float px, float py)
        {
            const float pad = 8f;
            for (int i = 0; i < 4; i++)
            {
                float x = BtnX + i * (Btn + Gap);
                if (px >= x - pad && px <= x + Btn + pad && py >= BtnY - pad && py <= BtnY + Btn + pad)
                    return i;
            }
            if (px >= StartX - pad && px <= StartX + StartW + pad && py >= StartY - pad && py <= StartY + StartH + pad)
                return 4;
            return -1;
        }
    }

    /// <summary>
    /// Title card over the alley. Replace UI/MenuBackground.png (next to project.json)
    /// to put your own art behind the card. Without that file the lane is the backdrop.
    /// </summary>
    public static class BowlingMenu
    {
        public static MenuLayout Layout(float w, float h)
        {
            if (w < 32f) w = 1280f;
            if (h < 32f) h = 720f;
            var L = new MenuLayout();
            L.W = MathF.Min(620f, w - 48f);
            L.H = 268f;
            if (L.H > h - 24f) L.H = h - 24f;
            L.X = (w - L.W) * 0.5f;
            L.Y = (h - L.H) * 0.42f;
            L.Btn = MathF.Min(70f, (L.W - 80f) / 4.35f);
            L.Gap = 12f;
            float row = L.Btn * 4f + L.Gap * 3f;
            L.BtnX = L.X + (L.W - row) * 0.5f;
            L.BtnY = L.Y + 118f;
            L.StartW = 200f;
            L.StartH = 40f;
            L.StartX = L.X + (L.W - L.StartW) * 0.5f;
            L.StartY = L.BtnY + L.Btn + 16f;
            if (L.StartY + L.StartH > L.Y + L.H - 10f)
                L.StartY = L.Y + L.H - L.StartH - 10f;
            return L;
        }

        public static void Draw(BowlMesh m, float w, float h, int players, int hover, float time)
        {
            MenuPlate.Draw(m, w, h, time);
            var L = Layout(w, h);
            var card = new Vector3(0.07f, 0.055f, 0.04f);
            var edge = new Vector3(0.72f, 0.54f, 0.24f);
            m.Quad(
                new Vector3(L.X - 3f, L.Y - 3f, 0),
                new Vector3(L.X + L.W + 3f, L.Y - 3f, 0),
                new Vector3(L.X + L.W + 3f, L.Y + L.H + 3f, 0),
                new Vector3(L.X - 3f, L.Y + L.H + 3f, 0),
                edge);
            m.Quad(
                new Vector3(L.X, L.Y, 0),
                new Vector3(L.X + L.W, L.Y, 0),
                new Vector3(L.X + L.W, L.Y + L.H, 0),
                new Vector3(L.X, L.Y + L.H, 0),
                card);
            m.Quad(
                new Vector3(L.X + 18f, L.Y + 62f, 0),
                new Vector3(L.X + L.W - 18f, L.Y + 62f, 0),
                new Vector3(L.X + L.W - 18f, L.Y + 63.5f, 0),
                new Vector3(L.X + 18f, L.Y + 63.5f, 0),
                edge);

            var gold = new Vector3(0.93f, 0.78f, 0.42f);
            var ink = new Vector3(0.96f, 0.93f, 0.86f);
            var mute = new Vector3(0.62f, 0.54f, 0.40f);
            float titlePx = MathF.Max(2.4f, MathF.Min(3.6f, w / 380f));
            string title = "BOWLING";
            float tw = BowlingHud.Measure(title, titlePx);
            BowlingHud.Text(m, title, L.X + (L.W - tw) * 0.5f, L.Y + 18f, titlePx, gold);

            float subPx = MathF.Max(1.15f, titlePx * 0.42f);
            string sub = "TEN PIN";
            float swid = BowlingHud.Measure(sub, subPx);
            BowlingHud.Text(m, sub, L.X + (L.W - swid) * 0.5f, L.Y + 46f, subPx, mute);

            float labelPx = MathF.Max(1.25f, titlePx * 0.46f);
            string label = "PLAYERS";
            float lw = BowlingHud.Measure(label, labelPx);
            BowlingHud.Text(m, label, L.X + (L.W - lw) * 0.5f, L.Y + 78f, labelPx, mute);

            for (int i = 0; i < 4; i++)
            {
                float x = L.BtnX + i * (L.Btn + L.Gap);
                bool on = players == i + 1;
                bool hot = hover == i;
                var fill = on ? new Vector3(0.45f, 0.30f, 0.10f) : new Vector3(0.16f, 0.13f, 0.10f);
                if (hot && !on) fill = new Vector3(0.28f, 0.20f, 0.10f);
                m.Quad(
                    new Vector3(x, L.BtnY, 0),
                    new Vector3(x + L.Btn, L.BtnY, 0),
                    new Vector3(x + L.Btn, L.BtnY + L.Btn, 0),
                    new Vector3(x, L.BtnY + L.Btn, 0),
                    on || hot ? gold : new Vector3(0.28f, 0.22f, 0.14f));
                float inset = on || hot ? 2f : 1f;
                m.Quad(
                    new Vector3(x + inset, L.BtnY + inset, 0),
                    new Vector3(x + L.Btn - inset, L.BtnY + inset, 0),
                    new Vector3(x + L.Btn - inset, L.BtnY + L.Btn - inset, 0),
                    new Vector3(x + inset, L.BtnY + L.Btn - inset, 0),
                    fill);
                string n = (i + 1).ToString();
                float np = MathF.Max(2f, L.Btn / 28f);
                float nw = BowlingHud.Measure(n, np);
                BowlingHud.Text(m, n, x + (L.Btn - nw) * 0.5f, L.BtnY + (L.Btn - np * 7f) * 0.5f, np, on ? gold : ink);
            }

            bool startHot = hover == 4;
            var startEdge = startHot ? gold : new Vector3(0.72f, 0.54f, 0.24f);
            m.Quad(
                new Vector3(L.StartX, L.StartY, 0),
                new Vector3(L.StartX + L.StartW, L.StartY, 0),
                new Vector3(L.StartX + L.StartW, L.StartY + L.StartH, 0),
                new Vector3(L.StartX, L.StartY + L.StartH, 0),
                startEdge);
            m.Quad(
                new Vector3(L.StartX + 2f, L.StartY + 2f, 0),
                new Vector3(L.StartX + L.StartW - 2f, L.StartY + 2f, 0),
                new Vector3(L.StartX + L.StartW - 2f, L.StartY + L.StartH - 2f, 0),
                new Vector3(L.StartX + 2f, L.StartY + L.StartH - 2f, 0),
                startHot ? new Vector3(0.40f, 0.26f, 0.08f) : new Vector3(0.22f, 0.14f, 0.06f));
            float sp = 1.7f;
            string start = "START";
            float sw = BowlingHud.Measure(start, sp);
            BowlingHud.Text(m, start, L.StartX + (L.StartW - sw) * 0.5f, L.StartY + 8f, sp, gold);
        }
    }
}
