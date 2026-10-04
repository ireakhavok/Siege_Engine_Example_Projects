using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Scenes;
using SiegeEngine.Systems;

namespace PoolProject
{
    // Play looks up "RuntimeGameplay" and builds the scene from the registry.
    // project.json customSceneClass is the editor name. This registration is what Play runs.
    [RegisterGameSystem]
    public sealed class PoolLaunch : GameSystem
    {
        static bool _bound;

        public PoolLaunch(IGameServer server) : base(server)
        {
            if (_bound) return;
            _bound = true;
            SceneRegistry.Register("RuntimeGameplay", ctx => new PoolScene(ctx));
        }

        public override void Update(float deltaTime) { }
    }
}
