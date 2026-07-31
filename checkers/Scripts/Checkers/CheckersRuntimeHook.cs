using System;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Scenes;
using SiegeEngine.Systems;

namespace CheckersProject
{
    [RegisterGameSystem]
    public sealed class CheckersRuntimeHook : GameSystem
    {
        static bool _bound;

        public CheckersRuntimeHook(IGameServer server) : base(server)
        {
            if (_bound) return;
            _bound = true;

            SceneRegistry.Register("RuntimeGameplay", ctx =>
            {
                Console.WriteLine("[CheckersProject] Create('RuntimeGameplay') → CheckersScene");
                return new CheckersScene(ctx);
            });

            Console.WriteLine("[CheckersProject] Bound RuntimeGameplay → CheckersScene");
        }

        public override void Update(float deltaTime) { }
    }
}