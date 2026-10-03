using System;
using System.IO;
using SiegeEngine.Core.AssetParsing;
using SiegeEngine.Core.AssetParsing.Model;

namespace BowlingProject
{
    /// <summary>
    /// Pin and ball meshes live in Assets/*.fbx. The physics body is built from
    /// that mesh. If a build cannot parse the file, the same shape is built in
    /// memory so the rack still exists.
    /// </summary>
    public static class PinAssets
    {
        static FBXModel _pin;
        static FBXModel _ball;

        public static FBXModel Pin => _pin ??= Load("pin.fbx", LaneGeometry.PinCollider());
        public static FBXModel Ball => _ball ??= Load("ball.fbx", LaneGeometry.BallCollider());

        static FBXModel Load(string file, FBXModel fallback)
        {
            try
            {
                string path = Find(file);
                if (path == null) return fallback;
                var forest = FBXParser.Load(path);
                var model = FBXParser.BuildModelFromForest(forest);
                if (model?.Meshes == null || model.Meshes.Count == 0 || model.Meshes[0].Vertices.Count < 8)
                    return fallback;
                model.UnitToMeters = 1f;
                return model;
            }
            catch
            {
                return fallback;
            }
        }

        public static void Rebuild(PhysicsComponent body, FBXModel model)
        {
            if (body == null) return;
            try
            {
                if (model != null)
                    body.RebuildShape(model);
                else
                    body.RebuildShape(null);
            }
            catch
            {
                try { body.RebuildShape(null); } catch { }
            }
            try { body.RecomputeMassProperties(); } catch { }
        }

        static string Find(string file)
        {
            string hit = Walk(Directory.GetCurrentDirectory(), file);
            if (hit != null) return hit;
            hit = Walk(AppContext.BaseDirectory, file);
            if (hit != null) return hit;
            try
            {
                string loc = typeof(PinAssets).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                    hit = Walk(Path.GetDirectoryName(loc), file);
            }
            catch
            {
                hit = null;
            }
            return hit;
        }

        static string Walk(string start, string file)
        {
            string dir = start;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string direct = Path.Combine(dir, "Assets", file);
                if (File.Exists(direct) && File.Exists(Path.Combine(dir, "project.json")))
                    return direct;
                string nested = Path.Combine(dir, "bowling", "Assets", file);
                if (File.Exists(nested) && File.Exists(Path.Combine(dir, "bowling", "project.json")))
                    return nested;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
