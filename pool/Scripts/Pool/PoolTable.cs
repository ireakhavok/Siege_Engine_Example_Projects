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
    }

    // A 7-foot table in metres. Z is up, matching the rest of the examples.
    // Cushions and the slate are static boxes. Balls are spheres. The engine resolves contacts.
    // A pocket is not a collider. The engine has no sensor volume, so the scene tests the
    // ball centre against a circle after the step. That is a rule, not a contact solver.
    public static class PoolTable
    {
        public const float Length = 1.98f;
        public const float Width = 0.99f;
        public const float SlateZ = 0.80f;
        public const float BallRadius = 0.0286f;
        public const float PocketRadius = 0.055f;
        public const float Cushion = 0.04f;

        public static Vector3 HeadSpot => new Vector3(0f, -Length * 0.25f, SlateZ + BallRadius);
        public static Vector3 FootSpot => new Vector3(0f, Length * 0.25f, SlateZ + BallRadius);

        public static Vector3[] Pockets()
        {
            float x = Width * 0.5f - 0.02f;
            float y = Length * 0.5f - 0.02f;
            return new[]
            {
                new Vector3(-x, -y, SlateZ),
                new Vector3(x, -y, SlateZ),
                new Vector3(-x, y, SlateZ),
                new Vector3(x, y, SlateZ),
                new Vector3(-x, 0f, SlateZ),
                new Vector3(x, 0f, SlateZ)
            };
        }

        public static bool InPocket(Vector3 p)
        {
            foreach (var pocket in Pockets())
            {
                float dx = p.X - pocket.X;
                float dy = p.Y - pocket.Y;
                if (dx * dx + dy * dy <= PocketRadius * PocketRadius && p.Z < SlateZ + BallRadius)
                    return true;
            }
            return false;
        }

        public static void BuildStatic(IGameServer server, List<Entity> into)
        {
            float hx = Width * 0.5f;
            float hy = Length * 0.5f;
            AddBox(server, into, new Vector3(hx, hy, 0.02f), new Vector3(0f, 0f, SlateZ - 0.02f), 0.18f, 0.22f);
            AddBox(server, into, new Vector3(hx, Cushion * 0.5f, 0.03f), new Vector3(0f, -hy - Cushion * 0.5f, SlateZ + 0.03f), 0.20f, 0.25f);
            AddBox(server, into, new Vector3(hx, Cushion * 0.5f, 0.03f), new Vector3(0f, hy + Cushion * 0.5f, SlateZ + 0.03f), 0.20f, 0.25f);
            AddBox(server, into, new Vector3(Cushion * 0.5f, hy, 0.03f), new Vector3(-hx - Cushion * 0.5f, 0f, SlateZ + 0.03f), 0.20f, 0.25f);
            AddBox(server, into, new Vector3(Cushion * 0.5f, hy, 0.03f), new Vector3(hx + Cushion * 0.5f, 0f, SlateZ + 0.03f), 0.20f, 0.25f);
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
            body.Friction = 0.06f;
            body.KineticFriction = 0.06f;
            body.StaticFriction = 0.08f;
            body.Restitution = cue ? 0.92f : 0.94f;
            body.RollingResistance = 0.015f;
            body.LinearDamping = 0.15f;
            body.AngularDamping = 0.12f;
            body.SleepThreshold = 0.04f;
            body.Mass = cue ? 0.17f : 0.16f;
            body.Size = new Vector3(BallRadius * 2f);
            body.BodyType = BodyType.Dynamic;
            AssignSphere(body, BallRadius);
            body.CollisionEnabled = true;
            e.AddComponent(body);
            server.AddEntity(e);
            return new PoolBall { Number = number, Entity = e, Body = body };
        }

        static void AddBox(IGameServer server, List<Entity> into, Vector3 half, Vector3 position, float kinetic, float stat)
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
            body.StaticFriction = stat;
            body.Restitution = 0.75f;
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

        // Shape's setter is not public on the vendored DLL. RebuildShape builds an OBB or a mesh,
        // not a sphere. This is the same assign the engine uses inside RebuildShape.
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
