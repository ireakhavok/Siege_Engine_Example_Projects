using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Physics;
using SiegeEngine.Scenes;

namespace PoolProject
{
    public sealed class PoolBall
    {
        public int Number;
        public Entity Entity;
        public PhysicsComponent Body;
        public bool OnTable = true;
        public bool WasOnTable = true;
    }

    // A 7-foot table in metres. Z is up, matching the rest of the examples.
    // The slate and the cushions are static boxes. Balls are spheres. The solver
    // steps both, the same way the bowling lane steps the ball against the deck.
    // Pocket mouths are gaps in that slate: a ball that leaves the cloth falls.
    public static class PoolTable
    {
        public const float Length = 1.9812f;
        public const float Width = 0.9906f;
        public const float SlateZ = 0.80f;
        public const float BallRadius = 0.028575f;
        public const float RailThick = 0.058f;
        public const float RailRise = 0.064f;
        public const float CornerGap = 0.088f;
        public const float SideGap = 0.114f;
        public const float BallMass = 0.170f;
        public const float CueMass = 0.163f;

        public static float HalfX => Width * 0.5f;
        public static float HalfY => Length * 0.5f;
        public static Vector3 HeadSpot => new Vector3(0f, -Length * 0.25f, RestZ);
        public static Vector3 FootSpot => new Vector3(0f, Length * 0.25f, RestZ);
        public static float RestZ => SlateZ + BallRadius + 0.0015f;

        public static Vector3[] Pockets()
        {
            float x = HalfX - 0.012f;
            float y = HalfY - 0.012f;
            float sx = HalfX + 0.008f;
            return new[]
            {
                new Vector3(-x, -y, SlateZ),
                new Vector3(x, -y, SlateZ),
                new Vector3(-x, y, SlateZ),
                new Vector3(x, y, SlateZ),
                new Vector3(-sx, 0f, SlateZ),
                new Vector3(sx, 0f, SlateZ)
            };
        }

        public static bool Fell(Vector3 p)
        {
            // Resting centre is a radius above the slate. A bounce stays above the cloth.
            // Crossing the slate edge, or dropping through a mouth, is a pocket.
            if (p.Z < SlateZ - 0.008f) return true;
            if (MathF.Abs(p.X) > HalfX + 0.02f) return true;
            if (MathF.Abs(p.Y) > HalfY + 0.02f) return true;
            return false;
        }

        public static List<(int number, Vector3 position)> RackSpots(PoolGame game)
        {
            var list = new List<(int, Vector3)>();
            int count = PoolRules.ObjectBalls(game);
            float d = BallRadius * 2.02f;
            var spot = FootSpot;
            if (game == PoolGame.Nine)
            {
                int[] nums = { 1, 2, 3, 4, 9, 5, 6, 7, 8 };
                int[] rowCount = { 1, 2, 3, 2, 1 };
                int n = 0;
                for (int row = 0; row < rowCount.Length; row++)
                {
                    int c = rowCount[row];
                    for (int col = 0; col < c; col++)
                    {
                        float x = (col - (c - 1) * 0.5f) * d;
                        float y = row * d * 0.8660254f;
                        list.Add((nums[n], new Vector3(spot.X + x, spot.Y + y, spot.Z)));
                        n++;
                    }
                }
                return list;
            }

            // Apex on the foot spot. 8 sits in the middle. Back corners are different groups.
            int[] rack =
            {
                1,
                9, 2,
                10, 8, 3,
                11, 4, 5, 6,
                13, 7, 14, 12, 15
            };
            int placed = 0;
            for (int row = 0; placed < count; row++)
            {
                for (int col = 0; col <= row && placed < count; col++)
                {
                    float x = (col - row * 0.5f) * d;
                    float y = row * d * 0.8660254f;
                    int number = placed < rack.Length ? rack[placed] : placed + 1;
                    list.Add((number, new Vector3(spot.X + x, spot.Y + y, spot.Z)));
                    placed++;
                }
            }
            return list;
        }

        public static void BuildStatic(IGameServer server, List<Entity> into)
        {
            if (server == null || into.Count > 0) return;
            float hx = HalfX;
            float hy = HalfY;
            const float thick = 0.05f;
            // One slate. Cushion gaps are the mouths. A ball that crosses the edge falls.
            AddBox(server, into,
                new Vector3(hx, hy, thick * 0.5f),
                new Vector3(0f, 0f, SlateZ - thick * 0.5f),
                0.20f, 0.03f);

            float cg = CornerGap;
            float sg = SideGap * 0.5f;
            AddCushionX(server, into, hx, 1f, -(hy - cg), -sg);
            AddCushionX(server, into, hx, 1f, sg, hy - cg);
            AddCushionX(server, into, -hx, -1f, -(hy - cg), -sg);
            AddCushionX(server, into, -hx, -1f, sg, hy - cg);
            AddCushionY(server, into, hy, 1f, -(hx - cg), hx - cg);
            AddCushionY(server, into, -hy, -1f, -(hx - cg), hx - cg);

            foreach (var pocket in Pockets())
            {
                AddBox(server, into,
                    new Vector3(0.075f, 0.075f, 0.02f),
                    new Vector3(pocket.X, pocket.Y, SlateZ - 0.11f),
                    0.35f, 0.02f);
            }

            AddBox(server, into,
                new Vector3(hx + RailThick + 0.20f, hy + RailThick + 0.20f, 0.025f),
                new Vector3(0f, 0f, SlateZ - 0.28f),
                0.40f, 0.04f);
        }

        public static PoolBall MakeBall(IGameServer server, int number, Vector3 position, bool cue)
        {
            var e = new Entity();
            var body = new PhysicsComponent();
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            body.Position = position;
            body.RenderPosition = position;
            body.Rotation = Quaternion.Identity;
            body.Friction = 0.10f;
            body.KineticFriction = 0.10f;
            body.StaticFriction = 0.16f;
            body.Restitution = 0.94f;
            body.RollingResistance = 0.045f;
            body.LinearDamping = 0.28f;
            body.AngularDamping = 0.20f;
            body.SleepThreshold = 0.05f;
            body.Mass = cue ? CueMass : BallMass;
            body.Size = new Vector3(BallRadius * 2f);
            body.BodyType = BodyType.Dynamic;
            AssignSphere(body, BallRadius);
            body.BodyType = BodyType.Kinematic;
            body.Position = position;
            body.RenderPosition = position;
            body.Velocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            body.CollisionEnabled = true;
            body.IsSleeping = true;
            e.AddComponent(body);
            server.AddEntity(e);
            return new PoolBall { Number = number, Entity = e, Body = body };
        }

        public static Vector3 ClearSpot(Vector3 desired, IReadOnlyList<PoolBall> rack)
        {
            var p = desired;
            p.Z = RestZ;
            p.X = Math.Clamp(p.X, -HalfX + BallRadius + 0.01f, HalfX - BallRadius - 0.01f);
            p.Y = Math.Clamp(p.Y, -HalfY + BallRadius + 0.01f, HalfY - BallRadius - 0.01f);
            for (int n = 0; n < 8; n++)
            {
                bool hit = false;
                if (rack != null)
                {
                    for (int i = 0; i < rack.Count; i++)
                    {
                        var ball = rack[i];
                        if (ball == null || !ball.OnTable || ball.Body == null) continue;
                        var d = p - ball.Body.Position;
                        d.Z = 0f;
                        float min = BallRadius * 2.08f;
                        float len = d.Length();
                        if (len >= min) continue;
                        hit = true;
                        if (len < 1e-5f) d = new Vector3(0f, -1f, 0f);
                        else d /= len;
                        p += d * (min - len + 0.001f);
                    }
                }
                p.X = Math.Clamp(p.X, -HalfX + BallRadius + 0.01f, HalfX - BallRadius - 0.01f);
                p.Y = Math.Clamp(p.Y, -HalfY + BallRadius + 0.01f, HalfY - BallRadius - 0.01f);
                p.Z = RestZ;
                if (!hit) break;
            }
            return p;
        }

        static void AddCushionX(IGameServer server, List<Entity> into, float railX, float outward, float y0, float y1)
        {
            float a = MathF.Min(y0, y1);
            float b = MathF.Max(y0, y1);
            if (b - a < 0.05f) return;
            float halfY = (b - a) * 0.5f;
            AddBox(server, into,
                new Vector3(RailThick * 0.5f, halfY, RailRise * 0.5f),
                new Vector3(railX + outward * RailThick * 0.5f, (a + b) * 0.5f, SlateZ + RailRise * 0.5f),
                0.14f, 0.80f);
        }

        static void AddCushionY(IGameServer server, List<Entity> into, float railY, float outward, float x0, float x1)
        {
            float a = MathF.Min(x0, x1);
            float b = MathF.Max(x0, x1);
            AddBox(server, into,
                new Vector3((b - a) * 0.5f, RailThick * 0.5f, RailRise * 0.5f),
                new Vector3((a + b) * 0.5f, railY + outward * RailThick * 0.5f, SlateZ + RailRise * 0.5f),
                0.14f, 0.80f);
        }

        static void AddBox(IGameServer server, List<Entity> into, Vector3 half, Vector3 position, float kinetic, float restitution)
        {
            var e = new Entity();
            var body = new PhysicsComponent();
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            body.Position = position;
            body.RenderPosition = position;
            body.Rotation = Quaternion.Identity;
            body.Friction = kinetic;
            body.KineticFriction = kinetic;
            body.StaticFriction = kinetic + 0.08f;
            body.Restitution = restitution;
            body.RollingResistance = 0.02f;
            body.Size = half * 2f;
            body.LocalBoundsMinCm = -half;
            body.LocalBoundsMaxCm = half;
            body.BodyType = BodyType.Static;
            Assign(body, new ObbShape(half));
            body.RecomputeMassProperties();
            body.CollisionEnabled = true;
            e.AddComponent(body);
            server.AddEntity(e);
            into.Add(e);
        }

        // Shape's setter is not public. RebuildShape builds an OBB or a mesh, not a sphere.
        // This is the same assign the bowling lane uses for the ball and the gutter boxes.
        public static void AssignSphere(PhysicsComponent body, float radius)
        {
            Assign(body, new SphereShape(radius));
            body.RecomputeMassProperties();
        }

        static void Assign(PhysicsComponent body, ColliderShape shape)
        {
            var setter = typeof(PhysicsComponent).GetProperty("Shape")?.GetSetMethod(true);
            setter?.Invoke(body, new object[] { shape });
        }
    }
}
