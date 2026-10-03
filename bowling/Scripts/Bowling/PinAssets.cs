using System;
using System.IO;
using SiegeEngine.Core.AssetParsing;
using SiegeEngine.Core.AssetParsing.Model;

namespace BowlingProject
{
    /// <summary>
    /// The pin and ball are asset packs, the same way save3 loads a mesh:
    /// Assets/pin_pack/assetpack.json points at pin.fbx. This only reads that
    /// file. The scene entities own the physics bodies.
    /// </summary>
    public static class PinAssets
    {
        static FBXModel _pin;
        static FBXModel _ball;

        public static FBXModel Pin => _pin ??= Load("pin_pack", "pin.fbx", LaneGeometry.PinCollider());
        public static FBXModel Ball => _ball ??= Load("ball_pack", "ball.fbx", LaneGeometry.BallCollider());

        public static string PackPath(string pack)
        {
            return Find(pack, "assetpack.json");
        }

        static FBXModel Load(string pack, string file, FBXModel fallback)
        {
            try
            {
                string path = Find(pack, file);
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

        static string Find(string pack, string file)
        {
            string hit = Walk(Directory.GetCurrentDirectory(), pack, file);
            if (hit != null) return hit;
            hit = Walk(AppContext.BaseDirectory, pack, file);
            if (hit != null) return hit;
            try
            {
                string loc = typeof(PinAssets).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                    hit = Walk(Path.GetDirectoryName(loc), pack, file);
            }
            catch
            {
                hit = null;
            }
            return hit;
        }

        static string Walk(string start, string pack, string file)
        {
            string dir = start;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string direct = Path.Combine(dir, "Assets", pack, file);
                if (File.Exists(direct) && File.Exists(Path.Combine(dir, "project.json")))
                    return direct;
                string nested = Path.Combine(dir, "bowling", "Assets", pack, file);
                if (File.Exists(nested) && File.Exists(Path.Combine(dir, "bowling", "project.json")))
                    return nested;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
