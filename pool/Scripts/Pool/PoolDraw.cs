using System;
using System.Collections.Generic;
using System.Numerics;

namespace PoolProject
{
    // The Grid shader reads 9 floats: position, colour, then three unused. Same layout as bowling.
    public sealed class PoolMesh
    {
        public readonly List<float> Vertices = new List<float>(4096);
        public readonly List<uint> Indices = new List<uint>(8192);

        public void Clear()
        {
            Vertices.Clear();
            Indices.Clear();
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color)
        {
            uint i = (uint)(Vertices.Count / 9);
            Add(a, color);
            Add(b, color);
            Add(c, color);
            Add(d, color);
            Indices.Add(i);
            Indices.Add(i + 1);
            Indices.Add(i + 2);
            Indices.Add(i);
            Indices.Add(i + 2);
            Indices.Add(i + 3);
        }

        public void Sphere(Vector3 center, float radius, Vector3 color)
        {
            const int seg = 8;
            for (int y = 0; y < seg; y++)
            {
                float v0 = y / (float)seg;
                float v1 = (y + 1) / (float)seg;
                float p0 = MathF.PI * v0;
                float p1 = MathF.PI * v1;
                for (int x = 0; x < seg; x++)
                {
                    float u0 = x / (float)seg;
                    float u1 = (x + 1) / (float)seg;
                    var a = center + radius * SpherePoint(u0, p0);
                    var b = center + radius * SpherePoint(u1, p0);
                    var c = center + radius * SpherePoint(u1, p1);
                    var d = center + radius * SpherePoint(u0, p1);
                    Quad(a, b, c, d, color);
                }
            }
        }

        static Vector3 SpherePoint(float u, float phi)
        {
            float s = MathF.Sin(phi);
            return new Vector3(s * MathF.Cos(u * MathF.PI * 2f), s * MathF.Sin(u * MathF.PI * 2f), MathF.Cos(phi));
        }

        void Add(Vector3 p, Vector3 color)
        {
            Vertices.Add(p.X);
            Vertices.Add(p.Y);
            Vertices.Add(p.Z);
            Vertices.Add(color.X);
            Vertices.Add(color.Y);
            Vertices.Add(color.Z);
            Vertices.Add(1f);
            Vertices.Add(0f);
            Vertices.Add(0f);
        }
    }

    public static class PoolColors
    {
        public static Vector3 Of(int number)
        {
            if (number == 0) return new Vector3(0.95f, 0.95f, 0.92f);
            if (number == 8) return new Vector3(0.08f, 0.08f, 0.08f);
            return (number % 7) switch
            {
                1 => new Vector3(0.85f, 0.72f, 0.15f),
                2 => new Vector3(0.15f, 0.35f, 0.75f),
                3 => new Vector3(0.75f, 0.18f, 0.15f),
                4 => new Vector3(0.45f, 0.18f, 0.65f),
                5 => new Vector3(0.85f, 0.40f, 0.12f),
                6 => new Vector3(0.15f, 0.55f, 0.28f),
                _ => new Vector3(0.55f, 0.12f, 0.18f)
            };
        }
    }
}
