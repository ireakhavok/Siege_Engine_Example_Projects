// Folder: chess/Scripts/Chess
// File: ChessHud.cs
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Systems;
using System;
using System.IO;

namespace ChessProject
{
    [RegisterGameSystem]
    public sealed class ChessHud : GameSystem
    {
        private readonly EventBus _eventBus;
        private bool _opened;

        public ChessHud(IGameServer server, EventBus eventBus) : base(server)
        {
            _eventBus = eventBus;
        }

        public override void Update(float deltaTime)
        {
            if (_opened || _eventBus == null) return;
            _opened = true;
            try
            {
                string dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChessHud.html");
                string[] guesses =
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "ChessHud.html"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Chess", "ChessHud.html")
                };
                bool copied = false;
                for (int i = 0; i < guesses.Length; i++)
                {
                    if (File.Exists(guesses[i]))
                    {
                        File.Copy(guesses[i], dest, true);
                        copied = true;
                        break;
                    }
                }
                if (!copied && !File.Exists(dest))
                    File.WriteAllText(dest, FallbackHtml);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ChessHud] stage HTML: " + ex.Message);
            }

            _eventBus.Publish(new OpenGameHudEvent
            {
                HtmlRelativePath = "ChessHud.html",
                Title = "Chess HUD",
                Chrome = PanelChromeStyle.Game,
                Docking = DockingMode.Dynamic,
                Open = true,
                Width = 280f,
                Height = 320f
            });
            Console.WriteLine("[ChessHud] opened ChessHud.html");
        }

        private const string FallbackHtml = "<html><body style='background:#1a1612;color:#e2c48a;font-family:Georgia;padding:12px'><h1>CHESS HUD</h1><p>Inventory</p></body></html>";
    }
}
