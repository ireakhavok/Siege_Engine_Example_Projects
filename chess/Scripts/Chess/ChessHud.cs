using System;
using System.IO;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;

namespace ChessProject
{
    [RegisterHostedContent]
    public sealed class ChessHud : IHostedContent
    {
        private readonly EventBus _eventBus;
        private bool _opened;

        public string DataKey => "ChessHud";

        public ChessHud(EventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Init()
        {
            if (_eventBus == null || _opened) return;
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
                    File.WriteAllText(dest, "<html><body style='background:#1a1612;color:#e2c48a;font-family:Georgia;padding:12px'><h1>CHESS HUD</h1><p>Inventory + board chrome</p></body></html>");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ChessHud] stage HTML: " + ex.Message);
            }
            _eventBus.Publish(new OpenGameHudEvent
            {
                HtmlRelativePath = "ChessHud.html",
                Title = "Chess",
                Chrome = PanelChromeStyle.Game,
                Docking = DockingMode.Dynamic,
                Open = true,
                Width = 280f,
                Height = 320f
            });
            Console.WriteLine("[ChessHud] OpenGameHudEvent ChessHud.html");
        }

        public void Update(float deltaTime) { }

        public void Render() { }

        public void Dispose() { }
    }
}
