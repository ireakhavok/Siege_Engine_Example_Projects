using System;
using System.Collections.Generic;
using System.Numerics;
using SiegeEngine.Core.AssetParsing.Model;

namespace BowlingProject
{
    public sealed class BowlMesh
    {
        public readonly List<float> Vertices = new List<float>(8192);
        public readonly List<uint> Indices = new List<uint>(12288);

        public void Clear()
        {
            Vertices.Clear();
            Indices.Clear();
        }

        public void Add(Vector3 p, Vector3 n, Vector3 albedo)
        {
            float lit = Shade(n);
            Vertices.Add(p.X);
            Vertices.Add(p.Y);
            Vertices.Add(p.Z);
            Vertices.Add(Clamp01(albedo.X * lit));
            Vertices.Add(Clamp01(albedo.Y * lit));
            Vertices.Add(Clamp01(albedo.Z * lit));
            Vertices.Add(1f);
            Vertices.Add(0f);
            Vertices.Add(0f);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 albedo)
        {
            Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            if (float.IsNaN(n.X)) n = Vector3.UnitZ;
            uint i = (uint)(Vertices.Count / 9);
            Add(a, n, albedo);
            Add(b, n, albedo);
            Add(c, n, albedo);
            Indices.Add(i);
            Indices.Add(i + 1);
            Indices.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 albedo)
        {
            Tri(a, b, c, albedo);
            Tri(a, c, d, albedo);
        }

        public void Box(Vector3 min, Vector3 max, Vector3 albedo)
        {
            var p000 = new Vector3(min.X, min.Y, min.Z);
            var p100 = new Vector3(max.X, min.Y, min.Z);
            var p110 = new Vector3(max.X, max.Y, min.Z);
            var p010 = new Vector3(min.X, max.Y, min.Z);
            var p001 = new Vector3(min.X, min.Y, max.Z);
            var p101 = new Vector3(max.X, min.Y, max.Z);
            var p111 = new Vector3(max.X, max.Y, max.Z);
            var p011 = new Vector3(min.X, max.Y, max.Z);
            Quad(p001, p101, p111, p011, albedo);
            Quad(p100, p000, p010, p110, albedo * 0.55f);
            Quad(p000, p001, p011, p010, albedo * 0.72f);
            Quad(p101, p100, p110, p111, albedo * 0.72f);
            Quad(p000, p100, p101, p001, albedo * 0.62f);
            Quad(p010, p011, p111, p110, albedo * 0.8f);
        }

        public void Disc(Vector3 c, float radius, Vector3 albedo, int seg)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * (MathF.PI * 2f) / seg;
                float a1 = (i + 1) * (MathF.PI * 2f) / seg;
                var p0 = c + new Vector3(MathF.Cos(a0) * radius, MathF.Sin(a0) * radius, 0f);
                var p1 = c + new Vector3(MathF.Cos(a1) * radius, MathF.Sin(a1) * radius, 0f);
                Tri(c, p0, p1, albedo);
            }
        }

        static float Shade(Vector3 n)
        {
            if (n.LengthSquared() < 1e-8f) return 1f;
            n = Vector3.Normalize(n);
            var key = Vector3.Normalize(new Vector3(-0.22f, -0.48f, 0.85f));
            var fill = Vector3.Normalize(new Vector3(0.62f, 0.18f, 0.42f));
            float d = MathF.Max(0f, Vector3.Dot(n, key));
            float f = MathF.Max(0f, Vector3.Dot(n, fill));
            float up = MathF.Max(0f, n.Z);
            float spec = MathF.Pow(d, 18f);
            return 0.18f + 0.70f * d + 0.16f * f + 0.10f * up + 0.22f * spec;
        }

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public static class LaneGeometry
    {
        public const float BallRadius = 0.1085f;
        public const float BallMass = 6.35f;
        public const float PinHeight = 0.381f;
        public const float PinMass = 1.53f;
        public const float PinSpacing = 0.3048f;
        public const float HeadPinY = 18.288f;
        public const float LaneHalf = 0.527f;
        public const float DeckZ = 0.0f;
        public const float ApproachY = -4.6f;
        public const float FoulY = 0f;
        public const float DeckEndY = 19.42f;
        public const float PitY = 21.15f;

        public static readonly Vector3 Wood = new Vector3(0.66f, 0.46f, 0.24f);
        public static readonly Vector3 Maple = new Vector3(0.86f, 0.74f, 0.46f);
        public static readonly Vector3 Approach = new Vector3(0.16f, 0.18f, 0.22f);
        public static readonly Vector3 Gutter = new Vector3(0.07f, 0.075f, 0.08f);
        public static readonly Vector3 BallAlbedo = new Vector3(0.62f, 0.07f, 0.10f);
        public static readonly Vector3 PinWhite = new Vector3(0.93f, 0.92f, 0.88f);
        public static readonly Vector3 PinRed = new Vector3(0.72f, 0.08f, 0.09f);

        static readonly float[] ProfileT =
        {
            0.00f, 0.05f, 0.14f, 0.30f, 0.44f, 0.56f, 0.64f, 0.72f, 0.84f, 0.93f, 1.00f
        };
        static readonly float[] ProfileR =
        {
            0.016f, 0.038f, 0.056f, 0.061f, 0.054f, 0.038f, 0.025f, 0.034f, 0.041f, 0.024f, 0.007f
        };

        public static Vector3 PinSpot(int index)
        {
            float s = PinSpacing;
            float row = s * 0.8660254f;
            float x, y;
            switch (index)
            {
                case 0: x = 0f; y = 0f; break;
                case 1: x = -s * 0.5f; y = row; break;
                case 2: x = s * 0.5f; y = row; break;
                case 3: x = -s; y = row * 2f; break;
                case 4: x = 0f; y = row * 2f; break;
                case 5: x = s; y = row * 2f; break;
                case 6: x = -1.5f * s; y = row * 3f; break;
                case 7: x = -0.5f * s; y = row * 3f; break;
                case 8: x = 0.5f * s; y = row * 3f; break;
                default: x = 1.5f * s; y = row * 3f; break;
            }
            return new Vector3(x, HeadPinY + y, DeckZ + PinHeight * 0.5f);
        }

        public static float PinRadiusAt(float t)
        {
            if (t <= 0f) return ProfileR[0];
            if (t >= 1f) return ProfileR[ProfileR.Length - 1];
            for (int i = 0; i < ProfileT.Length - 1; i++)
            {
                if (t > ProfileT[i + 1]) continue;
                float u = (t - ProfileT[i]) / (ProfileT[i + 1] - ProfileT[i]);
                return ProfileR[i] + (ProfileR[i + 1] - ProfileR[i]) * u;
            }
            return ProfileR[ProfileR.Length - 1];
        }

        public static void BuildHouse(BowlMesh m, bool restRack)
        {
            const int boards = 39;
            float width = LaneHalf * 2f;
            float boardW = width / boards;
            float x0 = -LaneHalf;

            for (int i = 0; i < boards; i++)
            {
                float xa = x0 + i * boardW;
                float xb = xa + boardW;
                float stripe = 0.90f + 0.10f * ((i * 7) % 5) / 4f;
                if ((i % 5) == 0) stripe *= 0.92f;
                var wood = Wood * stripe;
                var maple = Maple * (0.94f + 0.06f * ((i * 3) % 4) / 3f);
                float z = DeckZ;
                QuadY(m, xa, xb, ApproachY, FoulY, z, Approach * (0.85f + 0.15f * stripe));
                QuadY(m, xa, xb, FoulY, 17.55f, z, wood);
                QuadY(m, xa, xb, 17.55f, DeckEndY, z + 0.001f, maple);
            }

            // Foul line and lane edges.
            m.Quad(
                new Vector3(-LaneHalf, -0.018f, DeckZ + 0.008f),
                new Vector3(LaneHalf, -0.018f, DeckZ + 0.008f),
                new Vector3(LaneHalf, 0.018f, DeckZ + 0.008f),
                new Vector3(-LaneHalf, 0.018f, DeckZ + 0.008f),
                new Vector3(0.95f, 0.95f, 0.92f));
            m.Quad(
                new Vector3(-LaneHalf - 0.012f, FoulY, DeckZ + 0.006f),
                new Vector3(-LaneHalf, FoulY, DeckZ + 0.006f),
                new Vector3(-LaneHalf, DeckEndY, DeckZ + 0.006f),
                new Vector3(-LaneHalf - 0.012f, DeckEndY, DeckZ + 0.006f),
                new Vector3(0.05f, 0.05f, 0.05f));
            m.Quad(
                new Vector3(LaneHalf, FoulY, DeckZ + 0.006f),
                new Vector3(LaneHalf + 0.012f, FoulY, DeckZ + 0.006f),
                new Vector3(LaneHalf + 0.012f, DeckEndY, DeckZ + 0.006f),
                new Vector3(LaneHalf, DeckEndY, DeckZ + 0.006f),
                new Vector3(0.05f, 0.05f, 0.05f));

            AddArrows(m);
            AddDots(m, 0.22f, 0.018f);
            AddDots(m, 4.57f, 0.012f);

            // Gutters.
            AddGutter(m, -1);
            AddGutter(m, 1);

            // Kickbacks.
            var kick = new Vector3(0.04f, 0.04f, 0.045f);
            var rail = new Vector3(0.55f, 0.06f, 0.07f);
            m.Box(new Vector3(-1.08f, -1.2f, DeckZ - 0.10f), new Vector3(-0.90f, PitY, DeckZ + 0.52f), kick);
            m.Box(new Vector3(0.90f, -1.2f, DeckZ - 0.10f), new Vector3(1.08f, PitY, DeckZ + 0.52f), kick);
            m.Box(new Vector3(-1.09f, -1.2f, DeckZ + 0.48f), new Vector3(-0.89f, PitY, DeckZ + 0.56f), rail);
            m.Box(new Vector3(0.89f, -1.2f, DeckZ + 0.48f), new Vector3(1.09f, PitY, DeckZ + 0.56f), rail);

            // Pit and curtain.
            m.Box(new Vector3(-1.08f, DeckEndY + 0.02f, DeckZ - 0.42f), new Vector3(1.08f, PitY, DeckZ - 0.30f), new Vector3(0.03f, 0.03f, 0.035f));
            m.Box(new Vector3(-1.15f, PitY, DeckZ - 0.42f), new Vector3(1.15f, PitY + 0.18f, DeckZ + 1.35f), new Vector3(0.02f, 0.02f, 0.025f));
            for (int s = 0; s < 8; s++)
            {
                float z0 = DeckZ + 0.05f + s * 0.14f;
                var band = (s % 2 == 0) ? new Vector3(0.12f, 0.02f, 0.03f) : new Vector3(0.82f, 0.78f, 0.70f);
                m.Quad(
                    new Vector3(-1.05f, PitY - 0.01f, z0),
                    new Vector3(1.05f, PitY - 0.01f, z0),
                    new Vector3(1.05f, PitY - 0.01f, z0 + 0.07f),
                    new Vector3(-1.05f, PitY - 0.01f, z0 + 0.07f),
                    band);
            }

            // House shell: side walls, ceiling, light bars, ball return.
            var wall = new Vector3(0.035f, 0.04f, 0.05f);
            m.Box(new Vector3(-3.4f, ApproachY - 0.4f, DeckZ - 0.2f), new Vector3(-1.08f, PitY + 0.4f, DeckZ + 2.8f), wall);
            m.Box(new Vector3(1.08f, ApproachY - 0.4f, DeckZ - 0.2f), new Vector3(3.4f, PitY + 0.4f, DeckZ + 2.8f), wall);
            m.Box(new Vector3(-3.4f, ApproachY - 0.5f, DeckZ + 2.75f), new Vector3(3.4f, PitY + 0.5f, DeckZ + 2.92f), new Vector3(0.025f, 0.026f, 0.03f));
            m.Box(new Vector3(-3.4f, ApproachY - 0.8f, DeckZ - 0.55f), new Vector3(3.4f, ApproachY - 0.2f, DeckZ + 2.8f), new Vector3(0.03f, 0.032f, 0.04f));

            var lamp = new Vector3(1.6f, 1.45f, 1.15f);
            for (int i = 0; i < 5; i++)
            {
                float y = 1.2f + i * 4.0f;
                m.Box(new Vector3(-0.55f, y, DeckZ + 2.55f), new Vector3(0.55f, y + 0.55f, DeckZ + 2.68f), lamp);
            }
            m.Box(new Vector3(-0.7f, HeadPinY - 0.4f, DeckZ + 2.35f), new Vector3(0.7f, HeadPinY + 1.3f, DeckZ + 2.48f), lamp * 1.1f);

            // Ball return on the right of the approach.
            m.Box(new Vector3(0.62f, ApproachY, DeckZ), new Vector3(0.98f, -0.15f, DeckZ + 0.28f), new Vector3(0.10f, 0.11f, 0.13f));
            m.Box(new Vector3(0.66f, ApproachY + 0.15f, DeckZ + 0.28f), new Vector3(0.94f, -0.35f, DeckZ + 0.34f), new Vector3(0.02f, 0.02f, 0.025f));

            // Masking fascia above the curtain.
            m.Box(new Vector3(-1.2f, PitY + 0.12f, DeckZ + 1.25f), new Vector3(1.2f, PitY + 0.28f, DeckZ + 2.7f), new Vector3(0.08f, 0.09f, 0.11f));

            if (restRack)
            {
                for (int i = 0; i < 10; i++)
                    AddPin(m, PinSpot(i), Quaternion.Identity);
                AddBall(m, new Vector3(0.18f, -2.15f, DeckZ + BallRadius), Quaternion.Identity);
            }
        }

        static void QuadY(BowlMesh m, float x0, float x1, float y0, float y1, float z, Vector3 albedo)
        {
            // Long boards get a couple of grain breaks so the lane is not one flat fill.
            const int cuts = 6;
            for (int c = 0; c < cuts; c++)
            {
                float t0 = c / (float)cuts;
                float t1 = (c + 1) / (float)cuts;
                float ya = y0 + (y1 - y0) * t0;
                float yb = y0 + (y1 - y0) * t1;
                float grain = 0.93f + 0.07f * MathF.Abs(MathF.Sin(ya * 1.7f + x0 * 9f));
                m.Quad(
                    new Vector3(x0, ya, z),
                    new Vector3(x1, ya, z),
                    new Vector3(x1, yb, z),
                    new Vector3(x0, yb, z),
                    albedo * grain);
            }
        }

        static void AddGutter(BowlMesh m, int side)
        {
            float inner = side < 0 ? -LaneHalf - 0.02f : LaneHalf + 0.02f;
            float outer = side < 0 ? -0.98f : 0.98f;
            float xL = MathF.Min(inner, outer);
            float xR = MathF.Max(inner, outer);
            float zTop = DeckZ - 0.01f;
            float zBot = DeckZ - 0.095f;
            m.Quad(
                new Vector3(inner, FoulY, zTop),
                new Vector3(inner, DeckEndY, zTop),
                new Vector3(side < 0 ? xL : xR, DeckEndY, zBot),
                new Vector3(side < 0 ? xL : xR, FoulY, zBot),
                Gutter);
            m.Quad(
                new Vector3(xL, FoulY, zBot),
                new Vector3(xR, FoulY, zBot),
                new Vector3(xR, DeckEndY, zBot),
                new Vector3(xL, DeckEndY, zBot),
                Gutter * 0.8f);
            float wall = side < 0 ? xL : xR;
            m.Quad(
                new Vector3(wall, FoulY, zBot),
                new Vector3(wall, DeckEndY, zBot),
                new Vector3(wall, DeckEndY, DeckZ + 0.02f),
                new Vector3(wall, FoulY, DeckZ + 0.02f),
                Gutter * 1.3f);
        }

        static void AddArrows(BowlMesh m)
        {
            float y = 4.572f;
            float[] xs = { 0f, -0.135f, 0.135f, -0.270f, 0.270f, -0.405f, 0.405f };
            var ink = new Vector3(0.18f, 0.09f, 0.04f);
            for (int i = 0; i < xs.Length; i++)
            {
                float x = xs[i];
                float z = DeckZ + 0.009f;
                var tip = new Vector3(x, y + 0.11f, z);
                var l0 = new Vector3(x - 0.055f, y - 0.02f, z);
                var l1 = new Vector3(x - 0.018f, y - 0.02f, z);
                var r0 = new Vector3(x + 0.018f, y - 0.02f, z);
                var r1 = new Vector3(x + 0.055f, y - 0.02f, z);
                var tailL = new Vector3(x - 0.018f, y - 0.16f, z);
                var tailR = new Vector3(x + 0.018f, y - 0.16f, z);
                m.Tri(tip, l1, l0, ink);
                m.Tri(tip, r1, r0, ink);
                m.Quad(l1, r0, tailR, tailL, ink);
            }
        }

        static void AddDots(BowlMesh m, float y, float radius)
        {
            float[] xs = { 0f, -0.135f, 0.135f, -0.270f, 0.270f, -0.405f, 0.405f };
            var ink = new Vector3(0.20f, 0.10f, 0.05f);
            for (int i = 0; i < xs.Length; i++)
                m.Disc(new Vector3(xs[i], y, DeckZ + 0.009f), radius, ink, 10);
        }

        public static void AddPin(BowlMesh m, Vector3 center, Quaternion rotation)
        {
            const int slices = 24;
            const int rings = 14;
            var locals = new Vector3[(rings + 1) * slices];
            var normals = new Vector3[(rings + 1) * slices];
            var colors = new Vector3[(rings + 1) * slices];
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float z = -PinHeight * 0.5f + PinHeight * t;
                float rad = PinRadiusAt(t);
                float t0 = MathF.Max(0f, t - 0.02f);
                float t1 = MathF.Min(1f, t + 0.02f);
                float z0 = -PinHeight * 0.5f + PinHeight * t0;
                float z1 = -PinHeight * 0.5f + PinHeight * t1;
                float dr = PinRadiusAt(t1) - PinRadiusAt(t0);
                float dz = z1 - z0;
                float nz = -dr;
                float nr = dz;
                float len = MathF.Sqrt(nz * nz + nr * nr);
                if (len < 1e-6f) { nz = 0f; nr = 1f; len = 1f; }
                nz /= len;
                nr /= len;
                var albedo = PinAlbedo(z);
                for (int s = 0; s < slices; s++)
                {
                    float a = s * (MathF.PI * 2f) / slices;
                    int idx = r * slices + s;
                    locals[idx] = new Vector3(MathF.Cos(a) * rad, MathF.Sin(a) * rad, z);
                    normals[idx] = new Vector3(MathF.Cos(a) * nr, MathF.Sin(a) * nr, nz);
                    colors[idx] = albedo;
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < slices; s++)
                {
                    int s1 = (s + 1) % slices;
                    int a = r * slices + s;
                    int b = r * slices + s1;
                    int c = (r + 1) * slices + s;
                    int d = (r + 1) * slices + s1;
                    PushLit(m, center, rotation, locals[a], normals[a], colors[a]);
                    PushLit(m, center, rotation, locals[c], normals[c], colors[c]);
                    PushLit(m, center, rotation, locals[b], normals[b], colors[b]);
                    PushLit(m, center, rotation, locals[b], normals[b], colors[b]);
                    PushLit(m, center, rotation, locals[c], normals[c], colors[c]);
                    PushLit(m, center, rotation, locals[d], normals[d], colors[d]);
                }
            }

            // Caps.
            var bottom = center + Vector3.Transform(new Vector3(0f, 0f, -PinHeight * 0.5f), rotation);
            var top = center + Vector3.Transform(new Vector3(0f, 0f, PinHeight * 0.5f), rotation);
            var down = Vector3.Transform(-Vector3.UnitZ, rotation);
            var up = Vector3.Transform(Vector3.UnitZ, rotation);
            for (int s = 0; s < slices; s++)
            {
                int s1 = (s + 1) % slices;
                var p0 = center + Vector3.Transform(locals[s], rotation);
                var p1 = center + Vector3.Transform(locals[s1], rotation);
                var q0 = center + Vector3.Transform(locals[rings * slices + s], rotation);
                var q1 = center + Vector3.Transform(locals[rings * slices + s1], rotation);
                uint i0 = (uint)(m.Vertices.Count / 9);
                m.Add(bottom, down, PinWhite * 0.7f);
                m.Add(p1, down, PinWhite * 0.7f);
                m.Add(p0, down, PinWhite * 0.7f);
                m.Indices.Add(i0);
                m.Indices.Add(i0 + 1);
                m.Indices.Add(i0 + 2);
                uint i1 = (uint)(m.Vertices.Count / 9);
                m.Add(top, up, PinWhite);
                m.Add(q0, up, PinWhite);
                m.Add(q1, up, PinWhite);
                m.Indices.Add(i1);
                m.Indices.Add(i1 + 1);
                m.Indices.Add(i1 + 2);
            }
        }

        static void PushLit(BowlMesh m, Vector3 center, Quaternion rotation, Vector3 local, Vector3 localN, Vector3 albedo)
        {
            uint i = (uint)(m.Vertices.Count / 9);
            m.Add(center + Vector3.Transform(local, rotation), Vector3.Normalize(Vector3.Transform(localN, rotation)), albedo);
            m.Indices.Add(i);
        }

        static Vector3 PinAlbedo(float localZ)
        {
            // Two crimson neck bands. localZ is centered, neck sits just above the middle.
            if (localZ > 0.012f && localZ < 0.042f) return PinRed;
            if (localZ > 0.058f && localZ < 0.082f) return PinRed;
            return PinWhite;
        }

        public static void AddBall(BowlMesh m, Vector3 center, Quaternion rotation)
        {
            const int lat = 16;
            const int lon = 24;
            var holes = new[]
            {
                Vector3.Normalize(new Vector3(0.22f, -0.78f, 0.42f)),
                Vector3.Normalize(new Vector3(-0.22f, -0.78f, 0.42f)),
                Vector3.Normalize(new Vector3(0f, -0.88f, 0.12f))
            };
            for (int y = 0; y < lat; y++)
            {
                float v0 = y / (float)lat;
                float v1 = (y + 1) / (float)lat;
                float p0 = MathF.PI * (v0 - 0.5f);
                float p1 = MathF.PI * (v1 - 0.5f);
                float z0 = MathF.Sin(p0);
                float z1 = MathF.Sin(p1);
                float r0 = MathF.Cos(p0);
                float r1 = MathF.Cos(p1);
                for (int x = 0; x < lon; x++)
                {
                    float u0 = x * (MathF.PI * 2f) / lon;
                    float u1 = (x + 1) * (MathF.PI * 2f) / lon;
                    var a = BallPoint(r0, z0, u0);
                    var b = BallPoint(r0, z0, u1);
                    var c = BallPoint(r1, z1, u1);
                    var d = BallPoint(r1, z1, u0);
                    PushBall(m, center, rotation, a, holes);
                    PushBall(m, center, rotation, b, holes);
                    PushBall(m, center, rotation, c, holes);
                    PushBall(m, center, rotation, a, holes);
                    PushBall(m, center, rotation, c, holes);
                    PushBall(m, center, rotation, d, holes);
                }
            }
        }

        static Vector3 BallPoint(float ring, float z, float u)
        {
            return new Vector3(MathF.Cos(u) * ring, MathF.Sin(u) * ring, z);
        }

        static void PushBall(BowlMesh m, Vector3 center, Quaternion rotation, Vector3 localUnit, Vector3[] holes)
        {
            var albedo = BallAlbedo;
            for (int i = 0; i < holes.Length; i++)
            {
                if (Vector3.Dot(localUnit, holes[i]) > 0.965f)
                {
                    albedo = new Vector3(0.02f, 0.015f, 0.015f);
                    break;
                }
            }
            // A pale crescent so the ball reads as finished, not a flat maroon sphere.
            float sheen = MathF.Max(0f, Vector3.Dot(localUnit, Vector3.Normalize(new Vector3(-0.2f, -0.4f, 0.9f))));
            albedo += new Vector3(0.18f, 0.08f, 0.06f) * MathF.Pow(sheen, 3f);
            uint idx = (uint)(m.Vertices.Count / 9);
            var n = Vector3.Normalize(Vector3.Transform(localUnit, rotation));
            m.Add(center + n * BallRadius, n, albedo);
            m.Indices.Add(idx);
        }

        public static void AddAimDots(BowlMesh m, Vector3 origin, Vector3 dir)
        {
            var ink = new Vector3(0.95f, 0.85f, 0.45f);
            for (int i = 1; i <= 16; i++)
            {
                var p = origin + dir * (i * 0.85f);
                p.Z = DeckZ + 0.012f;
                if (p.Y > 16.5f) break;
                if (MathF.Abs(p.X) > LaneHalf - 0.04f) break;
                m.Disc(p, 0.018f, ink * (1f - i / 20f), 8);
            }
        }

        public static FBXModel PinCollider()
        {
            const int slices = 12;
            const int rings = 8;
            var model = new FBXModel { UnitToMeters = 1f, Skeleton = null };
            var mesh = new MeshData { Name = "Pin" };
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float z = -PinHeight * 0.5f + PinHeight * t;
                float rad = PinRadiusAt(t);
                for (int s = 0; s < slices; s++)
                {
                    float a = s * (MathF.PI * 2f) / slices;
                    mesh.Vertices.Add(new FBXVertex(MathF.Cos(a) * rad, MathF.Sin(a) * rad, z, 0f, 0f, 1f, 0f, 0f, 0f));
                }
            }
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < slices; s++)
                {
                    int s1 = (s + 1) % slices;
                    uint a = (uint)(r * slices + s);
                    uint b = (uint)(r * slices + s1);
                    uint c = (uint)((r + 1) * slices + s);
                    uint d = (uint)((r + 1) * slices + s1);
                    mesh.Indices.Add(a); mesh.Indices.Add(c); mesh.Indices.Add(b);
                    mesh.Indices.Add(b); mesh.Indices.Add(c); mesh.Indices.Add(d);
                }
            }
            model.Meshes.Add(mesh);
            return model;
        }

        public static FBXModel BoxCollider(float x0, float x1, float y0, float y1, float z0, float z1)
        {
            var model = new FBXModel { UnitToMeters = 1f, Skeleton = null };
            var mesh = new MeshData { Name = "Box" };
            void V(float x, float y, float z) =>
                mesh.Vertices.Add(new FBXVertex(x, y, z, 0f, 0f, 1f, 0f, 0f, 0f));
            V(x0, y0, z0); V(x1, y0, z0); V(x1, y1, z0); V(x0, y1, z0);
            V(x0, y0, z1); V(x1, y0, z1); V(x1, y1, z1); V(x0, y1, z1);
            void Face(uint a, uint b, uint c, uint d)
            {
                mesh.Indices.Add(a); mesh.Indices.Add(b); mesh.Indices.Add(c);
                mesh.Indices.Add(a); mesh.Indices.Add(c); mesh.Indices.Add(d);
            }
            Face(4, 5, 6, 7);
            Face(1, 0, 3, 2);
            Face(0, 4, 7, 3);
            Face(5, 1, 2, 6);
            Face(0, 1, 5, 4);
            Face(3, 7, 6, 2);
            model.Meshes.Add(mesh);
            return model;
        }
    }
}
