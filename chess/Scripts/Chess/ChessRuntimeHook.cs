using System;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Scenes;
using SiegeEngine.Systems;

namespace ChessProject
{
    [RegisterGameSystem]
    public sealed class ChessRuntimeHook : GameSystem
    {
        static bool _bound;

        public ChessRuntimeHook(IGameServer server) : base(server)
        {
            if (_bound) return;
            _bound = true;

            SceneRegistry.Register("RuntimeGameplay", ctx =>
            {
                Console.WriteLine("[ChessProject] Create('RuntimeGameplay') → ChessScene");
                return new ChessScene(ctx);
            });

            Console.WriteLine("[ChessProject] Bound RuntimeGameplay → ChessScene");
        }

        public override void Update(float deltaTime) { }
    }
}
