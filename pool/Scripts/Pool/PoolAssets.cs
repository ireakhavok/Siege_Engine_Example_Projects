using System.IO;
using SiegeEngine.Core.AssetParsing.Model;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.GPU.ContextManagement;
using SiegeEngine.Core.Managers;
using SiegeEngine.Scenes;

namespace PoolProject
{
    // The table, cue and balls are Assets/*.fbx. LoadModel registers them with
    // ModelManager, which is what the shadow pass and the model renderer both look up.
    public static class PoolAssets
    {
        public static bool Ready;

        public static void Load(SceneContext context, IRenderContext render)
        {
            if (Ready) return;
            if (ModelManager.Instance == null)
                new ModelManager(render, true);
            var mgr = ModelManager.Instance;
            if (mgr == null) return;
            string root = Find(context);
            if (root == null) return;
            foreach (string file in Directory.EnumerateFiles(root, "*.fbx", SearchOption.AllDirectories))
            {
                mgr.LoadModel(file);
                string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (mgr.TryGetModel(key, out FBXModel model) && model != null)
                    model.UnitToMeters = 1f;
            }
            Ready = mgr.TryGetModel("table", out _);
        }

        public static void Attach(Entity entity, string key)
        {
            var model = new ModelComponent
            {
                Key = key,
                CastShadows = true,
                ReceiveShadows = true
            };
            entity.AddComponent(model);
        }

        public static string Find(SceneContext context)
        {
            string direct = context?.PlayProjectPath;
            if (Has(direct)) return direct;
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Has(dir)) return dir;
                string nested = Path.Combine(dir, "pool");
                if (Has(nested)) return nested;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        static bool Has(string dir)
        {
            return !string.IsNullOrEmpty(dir)
                && File.Exists(Path.Combine(dir, "Assets", "table", "table.fbx"))
                && File.Exists(Path.Combine(dir, "project.json"));
        }
    }
}
