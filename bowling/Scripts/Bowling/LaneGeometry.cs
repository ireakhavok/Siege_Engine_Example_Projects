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

        public void QuadUnlit(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 albedo)
        {
            TriUnlit(a, b, c, albedo);
            TriUnlit(a, c, d, albedo);
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
            Ellipse(c, radius, radius, 0f, albedo, seg);
        }

        public void Ellipse(Vector3 c, float rx, float ry, float angle, Vector3 albedo, int seg)
        {
            float ca = MathF.Cos(angle);
            float sa = MathF.Sin(angle);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * (MathF.PI * 2f) / seg;
                float a1 = (i + 1) * (MathF.PI * 2f) / seg;
                var p0 = c + new Vector3(
                    ca * MathF.Cos(a0) * rx - sa * MathF.Sin(a0) * ry,
                    sa * MathF.Cos(a0) * rx + ca * MathF.Sin(a0) * ry,
                    0f);
                var p1 = c + new Vector3(
                    ca * MathF.Cos(a1) * rx - sa * MathF.Sin(a1) * ry,
                    sa * MathF.Cos(a1) * rx + ca * MathF.Sin(a1) * ry,
                    0f);
                Tri(c, p0, p1, albedo);
            }
        }

        void TriUnlit(Vector3 a, Vector3 b, Vector3 c, Vector3 albedo)
        {
            uint i = (uint)(Vertices.Count / 9);
            AddRaw(a, albedo);
            AddRaw(b, albedo);
            AddRaw(c, albedo);
            Indices.Add(i);
            Indices.Add(i + 1);
            Indices.Add(i + 2);
        }

        void AddRaw(Vector3 p, Vector3 albedo)
        {
            Vertices.Add(p.X);
            Vertices.Add(p.Y);
            Vertices.Add(p.Z);
            Vertices.Add(Clamp01(albedo.X));
            Vertices.Add(Clamp01(albedo.Y));
            Vertices.Add(Clamp01(albedo.Z));
            Vertices.Add(1f);
            Vertices.Add(0f);
            Vertices.Add(0f);
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
        public const int LaneCount = 5;
        public const float GutterWidth = 0.24f;
        public const float LanePitch = 1.66f;
        public const float ReleaseY = -2.35f;
        public const float OilEndY = 6f;
        public const float DryStartY = 10f;
        public const float OilKinetic = 0.040f;
        public const float DryKinetic = 0.30f;
        public const float RollFraction = 0.58f;
        public const float SideRev = 20f;
        public const float BallKinetic = 0.90f;
        public const float BallStatic = 1.05f;
        public const float BallLinearDamping = 0.01f;
        public const float BallAngularDamping = 0.02f;
        public const float BallRollingResistance = 0.012f;
        public const float BallRestitution = 0.03f;
        public const float LaneRestitution = 0.02f;

        public static float LaneOrigin(int lane) => (lane - 2) * LanePitch;

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
            return new Vector3(x, HeadPinY + y, DeckZ + PinHeight * 0.5f + 0.004f);
        }

        public static Vector3 PinSpot(int index, float laneX)
        {
            var p = PinSpot(index);
            p.X += laneX;
            return p;
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
            DrawRoom(m);
            for (int lane = 0; lane < LaneCount; lane++)
            {
                float ox = LaneOrigin(lane);
                bool player = lane == 2;
                bool outer = lane == 0 || lane == LaneCount - 1;
                DrawLane(m, ox, player);
                if (restRack || outer)
                    DrawRestRack(m, ox, restRack && player);
            }
        }

        static void DrawRoom(BowlMesh m)
        {
            float left = LaneOrigin(0) - 1.15f;
            float right = LaneOrigin(LaneCount - 1) + 1.15f;
            float wallIn = right + 0.55f;
            var carpet = new Vector3(0.11f, 0.075f, 0.055f);
            var apron = new Vector3(0.14f, 0.15f, 0.17f);
            // Subfloor under every lane and the walkways between them.
            m.Box(new Vector3(-wallIn, ApproachY - 0.05f, DeckZ - 0.08f), new Vector3(wallIn, PitY + 0.2f, DeckZ - 0.012f), carpet);
            m.Box(new Vector3(-wallIn, ApproachY, DeckZ - 0.012f), new Vector3(wallIn, FoulY, DeckZ + 0.001f), apron);

            var wall = new Vector3(0.12f, 0.125f, 0.14f);
            m.Box(new Vector3(-wallIn - 1.6f, ApproachY - 0.2f, DeckZ - 0.3f), new Vector3(-wallIn, PitY + 0.4f, DeckZ + 3.5f), wall);
            m.Box(new Vector3(wallIn, ApproachY - 0.2f, DeckZ - 0.3f), new Vector3(wallIn + 1.6f, PitY + 0.4f, DeckZ + 3.5f), wall);
            m.Box(new Vector3(-wallIn - 1.6f, ApproachY - 0.3f, DeckZ + 3.5f), new Vector3(wallIn + 1.6f, PitY + 0.4f, DeckZ + 3.68f), new Vector3(0.055f, 0.058f, 0.065f));
            m.Box(new Vector3(-wallIn - 1.6f, ApproachY - 1.2f, DeckZ - 0.55f), new Vector3(wallIn + 1.6f, ApproachY - 0.55f, DeckZ + 3.5f), new Vector3(0.07f, 0.075f, 0.085f));

            var trim = new Vector3(0.22f, 0.17f, 0.12f);
            for (int i = 0; i < 6; i++)
            {
                float y = ApproachY + 0.2f + i * 4.2f;
                m.Box(new Vector3(-wallIn, y, DeckZ + 0.9f), new Vector3(-wallIn + 0.07f, y + 0.05f, DeckZ + 2.5f), trim);
                m.Box(new Vector3(wallIn - 0.07f, y, DeckZ + 0.9f), new Vector3(wallIn, y + 0.05f, DeckZ + 2.5f), trim);
            }

            // Seating and returns sit outside the lanes, not on them.
            var bench = new Vector3(0.30f, 0.15f, 0.09f);
            m.Box(new Vector3(-wallIn + 0.08f, ApproachY + 0.35f, DeckZ), new Vector3(-wallIn + 0.72f, -0.7f, DeckZ + 0.42f), bench);
            m.Box(new Vector3(-wallIn + 0.12f, ApproachY + 0.4f, DeckZ + 0.42f), new Vector3(-wallIn + 0.68f, -0.75f, DeckZ + 0.78f), bench * 1.15f);
            m.Box(new Vector3(wallIn - 0.72f, ApproachY + 0.15f, DeckZ), new Vector3(wallIn - 0.08f, -0.2f, DeckZ + 0.32f), new Vector3(0.16f, 0.17f, 0.19f));
            m.Box(new Vector3(wallIn - 0.66f, ApproachY + 0.3f, DeckZ + 0.32f), new Vector3(wallIn - 0.14f, -0.4f, DeckZ + 0.38f), new Vector3(0.03f, 0.03f, 0.035f));

            // One masking wall behind every rack.
            float spanL = left - 0.2f;
            float spanR = right + 0.2f;
            m.Box(new Vector3(spanL, PitY, DeckZ - 0.45f), new Vector3(spanR, PitY + 0.2f, DeckZ + 1.45f), new Vector3(0.02f, 0.02f, 0.025f));
            for (int s = 0; s < 8; s++)
            {
                float z0 = DeckZ + 0.06f + s * 0.15f;
                var band = (s % 2 == 0) ? new Vector3(0.12f, 0.02f, 0.03f) : new Vector3(0.82f, 0.78f, 0.70f);
                m.Quad(
                    new Vector3(spanL, PitY - 0.01f, z0),
                    new Vector3(spanR, PitY - 0.01f, z0),
                    new Vector3(spanR, PitY - 0.01f, z0 + 0.075f),
                    new Vector3(spanL, PitY - 0.01f, z0 + 0.075f),
                    band);
            }
            m.Box(new Vector3(spanL, PitY + 0.12f, DeckZ + 1.3f), new Vector3(spanR, PitY + 0.32f, DeckZ + 3.15f), new Vector3(0.10f, 0.105f, 0.12f));
        }

        static void DrawLane(BowlMesh m, float ox, bool player)
        {
            const int boards = 39;
            float width = LaneHalf * 2f;
            float boardW = width / boards;
            float x0 = ox - LaneHalf;
            for (int i = 0; i < boards; i++)
            {
                float xa = x0 + i * boardW;
                float xb = xa + boardW;
                float stripe = 0.90f + 0.10f * ((i * 7) % 5) / 4f;
                if ((i % 5) == 0) stripe *= 0.92f;
                float across = MathF.Abs((i + 0.5f) / boards - 0.5f) * 2f;
                float oil = 0.58f + 0.42f * across;
                var wood = Wood * stripe;
                var maple = Maple * (0.94f + 0.06f * ((i * 3) % 4) / 3f);
                var approach = Approach * (player ? 1.05f : 0.85f) * (0.9f + 0.1f * stripe);
                QuadY(m, xa, xb, ApproachY, FoulY, DeckZ, approach);
                QuadY(m, xa, xb, FoulY, 12.5f, DeckZ, wood * oil);
                QuadY(m, xa, xb, 12.5f, 17.55f, DeckZ, wood * (0.9f + 0.1f * across));
                QuadY(m, xa, xb, 17.55f, DeckEndY, DeckZ + 0.001f, maple);
            }

            var foul = player ? new Vector3(0.96f, 0.96f, 0.93f) : new Vector3(0.55f, 0.55f, 0.52f);
            m.Quad(
                new Vector3(ox - LaneHalf, -0.02f, DeckZ + 0.008f),
                new Vector3(ox + LaneHalf, -0.02f, DeckZ + 0.008f),
                new Vector3(ox + LaneHalf, 0.02f, DeckZ + 0.008f),
                new Vector3(ox - LaneHalf, 0.02f, DeckZ + 0.008f),
                foul);

            AddArrows(m, ox, player ? 1f : 0.65f);
            AddDots(m, ox, 0.22f, 0.016f);
            AddDots(m, ox, -1.15f, 0.02f);
            AddDots(m, ox, -2.55f, 0.016f);
            AddGutter(m, -1, ox);
            AddGutter(m, 1, ox);

            float outer = LaneHalf + GutterWidth;
            var curb = new Vector3(0.05f, 0.05f, 0.055f);
            m.Box(new Vector3(ox - outer - 0.04f, FoulY, DeckZ - 0.16f), new Vector3(ox - outer, DeckEndY, DeckZ + 0.04f), curb);
            m.Box(new Vector3(ox + outer, FoulY, DeckZ - 0.16f), new Vector3(ox + outer + 0.04f, DeckEndY, DeckZ + 0.04f), curb);

            var kick = new Vector3(0.045f, 0.045f, 0.05f);
            var rail = new Vector3(0.55f, 0.07f, 0.08f);
            m.Box(new Vector3(ox - outer - 0.06f, 16.3f, DeckZ - 0.12f), new Vector3(ox - outer + 0.02f, PitY, DeckZ + 0.48f), kick);
            m.Box(new Vector3(ox + outer - 0.02f, 16.3f, DeckZ - 0.12f), new Vector3(ox + outer + 0.06f, PitY, DeckZ + 0.48f), kick);
            m.Box(new Vector3(ox - outer - 0.06f, 16.3f, DeckZ + 0.42f), new Vector3(ox - outer + 0.02f, PitY, DeckZ + 0.50f), rail);
            m.Box(new Vector3(ox + outer - 0.02f, 16.3f, DeckZ + 0.42f), new Vector3(ox + outer + 0.06f, PitY, DeckZ + 0.50f), rail);

            m.Box(new Vector3(ox - LaneHalf - 0.15f, DeckEndY + 0.02f, DeckZ - 0.42f), new Vector3(ox + LaneHalf + 0.15f, PitY, DeckZ - 0.28f), new Vector3(0.025f, 0.025f, 0.03f));

            for (int i = 0; i < 10; i++)
            {
                var spot = PinSpot(i, ox);
                m.Disc(new Vector3(spot.X, spot.Y, DeckZ + 0.006f), 0.045f, new Vector3(0.25f, 0.18f, 0.10f), 8);
            }

            var lamp = new Vector3(1.75f, 1.6f, 1.3f);
            if (player)
            {
                for (int i = 0; i < 4; i++)
                {
                    float y = 2.2f + i * 4.0f;
                    m.Box(new Vector3(ox - 0.34f, y, DeckZ + 3.15f), new Vector3(ox + 0.34f, y + 0.36f, DeckZ + 3.3f), lamp);
                }
            }
            m.Box(new Vector3(ox - 0.55f, HeadPinY - 0.2f, DeckZ + 2.95f), new Vector3(ox + 0.55f, HeadPinY + 1.2f, DeckZ + 3.1f), lamp);
            m.Box(new Vector3(ox - 0.62f, 17.7f, DeckZ + 0.85f), new Vector3(ox + 0.62f, 20.05f, DeckZ + 1.55f), new Vector3(0.04f, 0.042f, 0.05f));
        }

        static void DrawRestRack(BowlMesh m, float ox, bool withBall)
        {
            for (int i = 0; i < 10; i++)
            {
                var spot = PinSpot(i, ox);
                AddCastShadow(m, spot, 0.09f);
                AddPin(m, spot, Quaternion.Identity);
            }
            if (!withBall) return;
            var ball = new Vector3(0.20f, ReleaseY, DeckZ + BallRadius + 0.012f);
            AddCastShadow(m, ball, 0.14f);
            AddBall(m, ball, Quaternion.Identity);
            AddStance(m, 0.20f);
        }

        static void QuadY(BowlMesh m, float x0, float x1, float y0, float y1, float z, Vector3 albedo)
        {
            const int cuts = 4;
            for (int c = 0; c < cuts; c++)
            {
                float t0 = c / (float)cuts;
                float t1 = (c + 1) / (float)cuts;
                float ya = y0 + (y1 - y0) * t0;
                float yb = y0 + (y1 - y0) * t1;
                float grain = 0.94f + 0.06f * MathF.Abs(MathF.Sin(ya * 1.7f + x0 * 9f));
                m.Quad(
                    new Vector3(x0, ya, z),
                    new Vector3(x1, ya, z),
                    new Vector3(x1, yb, z),
                    new Vector3(x0, yb, z),
                    albedo * grain);
            }
        }

        static void AddGutter(BowlMesh m, int side, float ox)
        {
            float inner = ox + (side < 0 ? -LaneHalf - 0.012f : LaneHalf + 0.012f);
            float outer = ox + (side < 0 ? -(LaneHalf + GutterWidth) : LaneHalf + GutterWidth);
            float xL = MathF.Min(inner, outer);
            float xR = MathF.Max(inner, outer);
            float zTop = DeckZ - 0.008f;
            float zBot = DeckZ - 0.14f;
            float far = side < 0 ? xL : xR;
            m.Quad(
                new Vector3(inner, FoulY, zTop),
                new Vector3(inner, DeckEndY, zTop),
                new Vector3(far, DeckEndY, zBot),
                new Vector3(far, FoulY, zBot),
                Gutter);
            m.Quad(
                new Vector3(xL, FoulY, zBot),
                new Vector3(xR, FoulY, zBot),
                new Vector3(xR, DeckEndY, zBot),
                new Vector3(xL, DeckEndY, zBot),
                Gutter * 0.75f);
            m.Quad(
                new Vector3(far, FoulY, zBot),
                new Vector3(far, DeckEndY, zBot),
                new Vector3(far, DeckEndY, DeckZ + 0.02f),
                new Vector3(far, FoulY, DeckZ + 0.02f),
                Gutter * 1.35f);
        }

        static void AddArrows(BowlMesh m, float ox, float shade)
        {
            float y = 4.572f;
            float[] xs = { 0f, -0.135f, 0.135f, -0.270f, 0.270f, -0.405f, 0.405f };
            var ink = new Vector3(0.18f, 0.09f, 0.04f) * shade;
            for (int i = 0; i < xs.Length; i++)
            {
                float x = ox + xs[i];
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

        static void AddDots(BowlMesh m, float ox, float y, float radius)
        {
            float[] xs = { 0f, -0.135f, 0.135f, -0.270f, 0.270f, -0.405f, 0.405f };
            var ink = new Vector3(0.20f, 0.10f, 0.05f);
            for (int i = 0; i < xs.Length; i++)
                m.Disc(new Vector3(ox + xs[i], y, DeckZ + 0.009f), radius, ink, 8);
        }

        public static void AddCastShadow(BowlMesh m, Vector3 pos, float radius)
        {
            float h = pos.Z - DeckZ;
            if (h < 0.01f) h = 0.01f;
            if (h > 2.2f) return;
            var c = new Vector3(pos.X + h * 0.22f, pos.Y + h * 0.48f, DeckZ + 0.008f);
            const float ang = 1.16f;
            m.Ellipse(c, radius * 0.95f, radius * 2.05f, ang, new Vector3(0.012f, 0.008f, 0.006f), 14);
            c.Z = DeckZ + 0.011f;
            m.Ellipse(c, radius * 0.42f, radius * 0.85f, ang, new Vector3(0.0f, 0.0f, 0.0f), 10);
        }

        public static void AddStance(BowlMesh m, float lateral)
        {
            var shoe = new Vector3(0.72f, 0.68f, 0.48f);
            float y = ReleaseY - 0.22f;
            float z = DeckZ + 0.01f;
            m.Disc(new Vector3(lateral - 0.10f, y, z), 0.05f, shoe, 8);
            m.Disc(new Vector3(lateral + 0.08f, y + 0.06f, z), 0.05f, shoe * 0.85f, 8);
        }

        public static void DeckFriction(float y, out float kinetic, out float stat)
        {
            float span = 2f;
            float slab = MathF.Floor((y - ApproachY) / span);
            float center = ApproachY + (slab + 0.5f) * span;
            float t = (center - OilEndY) / (DryStartY - OilEndY);
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            t = t * t * (3f - 2f * t);
            kinetic = OilKinetic + (DryKinetic - OilKinetic) * t;
            stat = kinetic * 1.25f;
        }

        public static void ReleaseSpin(float aim, float hook, float speed, out Vector3 velocity, out Vector3 omega)
        {
            if (hook < -1f) hook = -1f;
            else if (hook > 1f) hook = 1f;
            float dirX = MathF.Sin(aim);
            float dirY = MathF.Cos(aim);
            velocity = new Vector3(dirX * speed, dirY * speed, 0f);
            float roll = speed / BallRadius * RollFraction;
            float side = -hook * SideRev;
            float rightX = dirY;
            float rightY = -dirX;
            omega = new Vector3(
                -rightX * roll - dirX * side,
                -rightY * roll - dirY * side,
                0f);
        }

        public static void ApplySkid(ref Vector3 vel, ref Vector3 omega, float y, float dt)
        {
            float sx = vel.X - omega.Y * BallRadius;
            float sy = vel.Y + omega.X * BallRadius;
            float sp = MathF.Sqrt(sx * sx + sy * sy);
            if (sp < 1e-5f) return;
            DeckFriction(y, out float kinetic, out float stat);
            float mu = sp < 0.06f ? MathF.Min(stat, BallStatic) : MathF.Min(kinetic, BallKinetic);
            float jtMax = mu * BallMass * 9.81f * dt * (1f + MathF.Min(BallRestitution, LaneRestitution));
            float invI = 1f / (0.4f * BallMass * BallRadius * BallRadius);
            float invEff = 1f / BallMass + BallRadius * BallRadius * invI;
            float impulse = MathF.Min(sp / invEff, jtMax);
            float ix = -sx / sp * impulse;
            float iy = -sy / sp * impulse;
            vel.X += ix / BallMass;
            vel.Y += iy / BallMass;
            omega.X += BallRadius * iy * invI;
            omega.Y += -BallRadius * ix * invI;
        }

        public static void StepSkid(ref Vector3 pos, ref Vector3 vel, ref Vector3 omega, float dt)
        {
            float damp = MathF.Max(0f, 1f - BallLinearDamping * dt);
            vel.X *= damp;
            vel.Y *= damp;
            float ad = MathF.Max(0f, 1f - BallAngularDamping * dt);
            omega.X *= ad;
            omega.Y *= ad;
            pos.X += vel.X * dt;
            pos.Y += vel.Y * dt;
            if (MathF.Abs(pos.X) > LaneHalf) return;
            ApplySkid(ref vel, ref omega, pos.Y, dt);
        }

        public static void AddHookPath(BowlMesh m, Vector3 origin, float aim, float hook, float speed)
        {
            ReleaseSpin(aim, hook, MathF.Max(4.5f, speed), out Vector3 vel, out Vector3 omega);
            var pos = origin;
            bool gutter = false;
            float gutterX = 0f;
            const float dt = 1f / 60f;
            for (int i = 1; i <= 240; i++)
            {
                if (!gutter && MathF.Abs(pos.X) > LaneHalf - 0.02f)
                {
                    gutter = true;
                    gutterX = MathF.Sign(pos.X) * (LaneHalf + GutterWidth * 0.55f);
                    vel.X = 0f;
                }
                if (gutter)
                {
                    pos.X = gutterX;
                    pos.Y += vel.Y * dt;
                }
                else
                    StepSkid(ref pos, ref vel, ref omega, dt);
                if (pos.Y > HeadPinY + 0.4f) break;
                if ((i % 6) != 0) continue;
                var ink = gutter
                    ? new Vector3(0.75f, 0.12f, 0.10f)
                    : new Vector3(0.97f, 0.84f, 0.36f);
                m.Disc(new Vector3(pos.X, pos.Y, DeckZ + 0.016f), gutter ? 0.028f : 0.02f, ink * (1f - i / 280f), 6);
            }
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

        public static FBXModel BallCollider()
        {
            const int slices = 12;
            const int rings = 8;
            var model = new FBXModel { UnitToMeters = 1f, Skeleton = null };
            var mesh = new MeshData { Name = "Ball" };
            mesh.Vertices.Add(new FBXVertex(0f, 0f, BallRadius, 0f, 0f, 1f, 0f, 0f, 0f));
            for (int r = 1; r < rings; r++)
            {
                float v = r * MathF.PI / rings;
                float z = MathF.Cos(v) * BallRadius;
                float ring = MathF.Sin(v) * BallRadius;
                for (int s = 0; s < slices; s++)
                {
                    float a = s * (MathF.PI * 2f) / slices;
                    mesh.Vertices.Add(new FBXVertex(MathF.Cos(a) * ring, MathF.Sin(a) * ring, z, 0f, 0f, 1f, 0f, 0f, 0f));
                }
            }
            mesh.Vertices.Add(new FBXVertex(0f, 0f, -BallRadius, 0f, 0f, 1f, 0f, 0f, 0f));
            int south = 1 + (rings - 1) * slices;
            for (int s = 0; s < slices; s++)
            {
                int s1 = (s + 1) % slices;
                mesh.Indices.Add(0);
                mesh.Indices.Add((uint)(1 + s1));
                mesh.Indices.Add((uint)(1 + s));
                uint b0 = (uint)(1 + (rings - 2) * slices + s);
                uint b1 = (uint)(1 + (rings - 2) * slices + s1);
                mesh.Indices.Add(b0);
                mesh.Indices.Add(b1);
                mesh.Indices.Add((uint)south);
            }
            for (int r = 0; r < rings - 2; r++)
            {
                for (int s = 0; s < slices; s++)
                {
                    int s1 = (s + 1) % slices;
                    uint a = (uint)(1 + r * slices + s);
                    uint b = (uint)(1 + r * slices + s1);
                    uint c = (uint)(1 + (r + 1) * slices + s);
                    uint d = (uint)(1 + (r + 1) * slices + s1);
                    mesh.Indices.Add(a); mesh.Indices.Add(b); mesh.Indices.Add(c);
                    mesh.Indices.Add(b); mesh.Indices.Add(d); mesh.Indices.Add(c);
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
