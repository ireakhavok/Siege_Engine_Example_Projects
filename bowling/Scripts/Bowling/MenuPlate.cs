using System;
using System.IO;
using System.IO.Compression;

namespace BowlingProject
{
    /// <summary>
    /// Optional menu backdrop. Drop a PNG at UI/MenuBackground.png in the project
    /// folder (next to project.json). 8-bit RGB, RGBA, grey, or indexed. The alley
    /// stays visible when the file is missing.
    /// </summary>
    public static class MenuPlate
    {
        const int Cols = 72;

        struct Plate
        {
            public int W, H;
            public float[] Rgb;
            public long Stamp;
        }

        static Plate _plate;
        static bool _ready;
        static float _nextLook;

        public static bool Draw(BowlMesh mesh, float w, float h, float time)
        {
            if (w < 2f || h < 2f) return false;
            if (time >= _nextLook)
            {
                _nextLook = time + 1.5f;
                TryLoad();
            }
            if (!_ready || _plate.Rgb == null) return false;

            float cw = w / _plate.W;
            float ch = h / _plate.H;
            var rgb = _plate.Rgb;
            for (int y = 0; y < _plate.H; y++)
            {
                float py = y * ch;
                int row = y * _plate.W;
                for (int x = 0; x < _plate.W; x++)
                {
                    int i = (row + x) * 3;
                    var color = new System.Numerics.Vector3(rgb[i], rgb[i + 1], rgb[i + 2]);
                    float px = x * cw;
                    mesh.QuadUnlit(
                        new System.Numerics.Vector3(px, py, 0f),
                        new System.Numerics.Vector3(px + cw + 0.6f, py, 0f),
                        new System.Numerics.Vector3(px + cw + 0.6f, py + ch + 0.6f, 0f),
                        new System.Numerics.Vector3(px, py + ch + 0.6f, 0f),
                        color);
                }
            }
            return true;
        }

        static void TryLoad()
        {
            try
            {
                string path = Find();
                if (path == null)
                {
                    _ready = false;
                    _plate = default;
                    return;
                }
                long stamp = File.GetLastWriteTimeUtc(path).Ticks;
                if (_ready && stamp == _plate.Stamp) return;
                byte[] file = File.ReadAllBytes(path);
                if (!Decode(file, out int iw, out int ih, out byte[] rgba))
                {
                    _ready = false;
                    return;
                }
                int cols = Cols;
                int rows = Math.Clamp((int)MathF.Round(cols * (ih / (float)Math.Max(1, iw))), 8, 96);
                var rgb = new float[cols * rows * 3];
                for (int y = 0; y < rows; y++)
                {
                    int y0 = y * ih / rows;
                    int y1 = Math.Max(y0 + 1, (y + 1) * ih / rows);
                    if (y1 > ih) y1 = ih;
                    for (int x = 0; x < cols; x++)
                    {
                        int x0 = x * iw / cols;
                        int x1 = Math.Max(x0 + 1, (x + 1) * iw / cols);
                        if (x1 > iw) x1 = iw;
                        long r = 0, g = 0, b = 0, n = 0;
                        for (int py = y0; py < y1; py++)
                        {
                            int row = py * iw;
                            for (int px = x0; px < x1; px++)
                            {
                                int p = (row + px) * 4;
                                r += rgba[p];
                                g += rgba[p + 1];
                                b += rgba[p + 2];
                                n++;
                            }
                        }
                        if (n < 1) n = 1;
                        int o = (y * cols + x) * 3;
                        rgb[o] = (r / (float)n) / 255f;
                        rgb[o + 1] = (g / (float)n) / 255f;
                        rgb[o + 2] = (b / (float)n) / 255f;
                    }
                }
                _plate = new Plate { W = cols, H = rows, Rgb = rgb, Stamp = stamp };
                _ready = true;
            }
            catch
            {
                _ready = false;
            }
        }

        static string Find()
        {
            string hit = Walk(Directory.GetCurrentDirectory());
            if (hit != null) return hit;
            hit = Walk(AppContext.BaseDirectory);
            if (hit != null) return hit;
            try
            {
                string loc = typeof(MenuPlate).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                    hit = Walk(Path.GetDirectoryName(loc));
            }
            catch
            {
                hit = null;
            }
            return hit;
        }

        static string Walk(string start)
        {
            var dir = start;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string png = Path.Combine(dir, "UI", "MenuBackground.png");
                string proj = Path.Combine(dir, "project.json");
                if (File.Exists(png) && File.Exists(proj)) return png;
                string nested = Path.Combine(dir, "bowling", "UI", "MenuBackground.png");
                string nestedProj = Path.Combine(dir, "bowling", "project.json");
                if (File.Exists(nested) && File.Exists(nestedProj)) return nested;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        static bool Decode(byte[] file, out int width, out int height, out byte[] rgba)
        {
            width = 0;
            height = 0;
            rgba = null;
            if (file == null || file.Length < 8) return false;
            byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < 8; i++)
                if (file[i] != sig[i]) return false;

            int bitDepth = 0, colorType = 0, interlace = 0;
            byte[] idat = null;
            byte[] plte = null;
            int o = 8;
            while (o + 8 <= file.Length)
            {
                int len = ReadInt(file, o);
                if (len < 0 || o + 12 + len > file.Length) return false;
                string type = System.Text.Encoding.ASCII.GetString(file, o + 4, 4);
                int data = o + 8;
                if (type == "IHDR")
                {
                    width = ReadInt(file, data);
                    height = ReadInt(file, data + 4);
                    bitDepth = file[data + 8];
                    colorType = file[data + 9];
                    interlace = file[data + 12];
                }
                else if (type == "PLTE")
                {
                    plte = new byte[len];
                    Buffer.BlockCopy(file, data, plte, 0, len);
                }
                else if (type == "IDAT")
                {
                    if (idat == null)
                    {
                        idat = new byte[len];
                        Buffer.BlockCopy(file, data, idat, 0, len);
                    }
                    else
                    {
                        var grown = new byte[idat.Length + len];
                        Buffer.BlockCopy(idat, 0, grown, 0, idat.Length);
                        Buffer.BlockCopy(file, data, grown, idat.Length, len);
                        idat = grown;
                    }
                }
                else if (type == "IEND")
                    break;
                o += 12 + len;
            }

            if (width < 1 || height < 1 || width > 8192 || height > 8192) return false;
            if (bitDepth != 8 || interlace != 0 || idat == null) return false;
            int bpp = colorType switch
            {
                0 => 1,
                2 => 3,
                3 => 1,
                6 => 4,
                _ => 0
            };
            if (bpp == 0) return false;
            if (colorType == 3 && (plte == null || plte.Length < 3)) return false;

            byte[] raw;
            using (var src = new MemoryStream(idat))
            using (var zlib = new ZLibStream(src, CompressionMode.Decompress))
            using (var dst = new MemoryStream())
            {
                zlib.CopyTo(dst);
                raw = dst.ToArray();
            }

            int stride = width * bpp;
            int need = height * (stride + 1);
            if (raw.Length < need) return false;
            var cur = new byte[stride];
            var prev = new byte[stride];
            rgba = new byte[width * height * 4];
            int srcRow = 0;
            for (int y = 0; y < height; y++)
            {
                int filter = raw[srcRow++];
                for (int i = 0; i < stride; i++)
                {
                    byte v = raw[srcRow++];
                    byte left = i >= bpp ? cur[i - bpp] : (byte)0;
                    byte up = prev[i];
                    byte ul = i >= bpp ? prev[i - bpp] : (byte)0;
                    cur[i] = (byte)(v + Recon(filter, left, up, ul));
                }
                int dstRow = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int s = x * bpp;
                    int d = dstRow + x * 4;
                    if (colorType == 6)
                    {
                        rgba[d] = cur[s];
                        rgba[d + 1] = cur[s + 1];
                        rgba[d + 2] = cur[s + 2];
                        rgba[d + 3] = cur[s + 3];
                    }
                    else if (colorType == 2)
                    {
                        rgba[d] = cur[s];
                        rgba[d + 1] = cur[s + 1];
                        rgba[d + 2] = cur[s + 2];
                        rgba[d + 3] = 255;
                    }
                    else if (colorType == 0)
                    {
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = cur[s];
                        rgba[d + 3] = 255;
                    }
                    else
                    {
                        int idx = cur[s] * 3;
                        if (idx + 2 >= plte.Length) idx = 0;
                        rgba[d] = plte[idx];
                        rgba[d + 1] = plte[idx + 1];
                        rgba[d + 2] = plte[idx + 2];
                        rgba[d + 3] = 255;
                    }
                }
                Buffer.BlockCopy(cur, 0, prev, 0, stride);
            }
            return true;
        }

        static int Recon(int filter, byte left, byte up, byte ul)
        {
            switch (filter)
            {
                case 0: return 0;
                case 1: return left;
                case 2: return up;
                case 3: return (left + up) / 2;
                case 4:
                    int p = left + up - ul;
                    int pa = Math.Abs(p - left);
                    int pb = Math.Abs(p - up);
                    int pc = Math.Abs(p - ul);
                    if (pa <= pb && pa <= pc) return left;
                    if (pb <= pc) return up;
                    return ul;
                default: return 0;
            }
        }

        static int ReadInt(byte[] b, int o)
        {
            return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        }
    }
}
