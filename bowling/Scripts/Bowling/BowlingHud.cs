using System.Collections.Generic;
using System.Numerics;

namespace BowlingProject
{
    public static class BowlingHud
    {
        // 5x7 glyphs, bit 4 is the left pixel. Missing glyphs draw as a blank.
        static readonly Dictionary<char, byte[]> Glyphs = Build();

        public static void Panel(BowlMesh m, float x, float y, float w, float h, Vector3 color)
        {
            m.Quad(
                new Vector3(x, y, 0f),
                new Vector3(x + w, y, 0f),
                new Vector3(x + w, y + h, 0f),
                new Vector3(x, y + h, 0f),
                color);
        }

        public static void Text(BowlMesh m, string text, float x, float y, float pixel, Vector3 color)
        {
            if (string.IsNullOrEmpty(text)) return;
            float cursor = x;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ')
                {
                    cursor += pixel * 4f;
                    continue;
                }
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(c), out var rows))
                {
                    cursor += pixel * 6f;
                    continue;
                }
                for (int row = 0; row < 7; row++)
                {
                    byte bits = rows[row];
                    for (int col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) == 0) continue;
                        float px = cursor + col * pixel;
                        float py = y + row * pixel;
                        Panel(m, px, py, pixel, pixel, color);
                    }
                }
                cursor += pixel * 6f;
            }
        }

        public static float Measure(string text, float pixel)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return text.Length * pixel * 6f;
        }

        public static string Marks(BowlingScore score, int frame)
        {
            int i = score.FrameStart(frame);
            int a = score.RollAt(i);
            if (a < 0) return "";
            if (frame < 9)
            {
                if (a == 10) return "X";
                int b = score.RollAt(i + 1);
                if (b < 0) return a.ToString();
                if (a + b == 10) return a + "/";
                return a + " " + b;
            }

            int b2 = score.RollAt(i + 1);
            int c = score.RollAt(i + 2);
            string A = a == 10 ? "X" : a.ToString();
            string B = "";
            if (b2 >= 0)
            {
                if (a != 10 && a + b2 == 10) B = "/";
                else B = b2 == 10 ? "X" : b2.ToString();
            }
            string C = "";
            if (c >= 0)
            {
                if (a == 10 && b2 >= 0 && b2 != 10 && b2 + c == 10) C = "/";
                else C = c == 10 ? "X" : c.ToString();
            }
            if (B.Length == 0) return A;
            if (C.Length == 0) return A + " " + B;
            return A + " " + B + " " + C;
        }

        static Dictionary<char, byte[]> Build()
        {
            var g = new Dictionary<char, byte[]>();
            void Put(char c, params byte[] rows) => g[c] = rows;
            Put('0', 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E);
            Put('1', 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E);
            Put('2', 0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F);
            Put('3', 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E);
            Put('4', 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02);
            Put('5', 0x1F, 0x10, 0x10, 0x1E, 0x01, 0x01, 0x1E);
            Put('6', 0x0E, 0x10, 0x10, 0x1E, 0x11, 0x11, 0x0E);
            Put('7', 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08);
            Put('8', 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E);
            Put('9', 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x01, 0x0E);
            Put('A', 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
            Put('B', 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E);
            Put('C', 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E);
            Put('D', 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C);
            Put('E', 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F);
            Put('F', 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10);
            Put('G', 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F);
            Put('H', 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
            Put('I', 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E);
            Put('J', 0x01, 0x01, 0x01, 0x01, 0x11, 0x11, 0x0E);
            Put('K', 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11);
            Put('L', 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F);
            Put('M', 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11);
            Put('N', 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11);
            Put('O', 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
            Put('P', 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10);
            Put('Q', 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D);
            Put('R', 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11);
            Put('S', 0x0E, 0x11, 0x10, 0x0E, 0x01, 0x11, 0x0E);
            Put('T', 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04);
            Put('U', 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
            Put('V', 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04);
            Put('W', 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11);
            Put('X', 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11);
            Put('Y', 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04);
            Put('Z', 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F);
            Put('/', 0x01, 0x02, 0x02, 0x04, 0x08, 0x08, 0x10);
            Put('-', 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00);
            return g;
        }
    }
}
