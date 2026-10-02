using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Scenes;
using SiegeEngine.Systems;

namespace BowlingProject
{
    [RegisterGameSystem]
    public sealed class BowlingLaunch : GameSystem
    {
        static bool _bound;

        public BowlingLaunch(IGameServer server) : base(server)
        {
            if (_bound) return;
            _bound = true;
            SceneRegistry.Register("RuntimeGameplay", ctx => new BowlingScene(ctx));
        }

        public override void Update(float deltaTime) { }
    }
}
