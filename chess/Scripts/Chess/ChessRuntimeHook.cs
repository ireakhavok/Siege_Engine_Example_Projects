// Folder: Scripts/Chess
// File: ChessRuntimeHook.cs
using System;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Scenes;
using SiegeEngine.Systems;

namespace ChessProject
{
    /// <summary>
    /// Play always does SceneRegistry.Create("RuntimeGameplay", ctx).
    /// This system is constructed inside ActivateProjectScripts (before Create),
    /// so we rebind "RuntimeGameplay" → ChessScene and never touch the default
    /// RuntimeGameplayScene / man_mesh path.
    /// </summary>
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