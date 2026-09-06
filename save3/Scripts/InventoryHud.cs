// Folder: save3/Scripts
// File: InventoryHud.cs
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.PlayerSystem;
using SiegeEngine.Systems;
using System;

namespace ProjectScripts
{
    [RegisterGameSystem]
    public sealed class InventoryHud : GameSystem
    {
        private readonly EventBus _eventBus;
        private bool _open;

        public InventoryHud(IGameServer server, EventBus eventBus) : this(server, eventBus, null) { }

        public InventoryHud(IGameServer server, EventBus eventBus, InputHandler inputHandler) : base(server)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _eventBus.Subscribe<KeyInputEvent>(OnNetworkKey);
            if (inputHandler != null)
                inputHandler.KeyEvent += OnKey;
            Console.WriteLine("[InventoryHud] I toggles inventory graphic — no file copy");
        }

        public override void Update(float deltaTime) { }

        private void OnNetworkKey(KeyInputEvent e)
        {
            if (e == null) return;
            OnKey(e.Key, e.Action);
        }

        private void OnKey(Key key, InputAction action)
        {
            if (key != Key.I) return;
            if (action != InputAction.Press) return;
            _open = !_open;
            _eventBus.Publish(new OpenHostedContentEvent
            {
                Key = "InventoryHud",
                HtmlRelativePath = "InventoryHud.html",
                HtmlContent = HudHtml,
                Title = "Inventory",
                Chrome = PanelChromeStyle.Bare,
                Docking = DockingMode.Desktop,
                Anchor = HudAnchor.Right,
                AllowMove = true,
                Open = _open,
                Width = 248f,
                Height = 520f
            });
            Console.WriteLine("[InventoryHud] " + (_open ? "open" : "closed"));
        }

        private const string HudHtml = @"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8'>
<style>
html, body { margin:0; padding:0; width:100%; height:100%; background:#14110e; overflow:hidden; }
.inv { width:100%; height:100%; padding:12px; color:#eadcc8; font-family:Georgia, serif; }
.inv h1 { margin:0 0 8px; font-size:14px; letter-spacing:3px; text-align:center; color:#d4b483; }
.gold { text-align:right; font-size:11px; color:#c4a15a; margin:0 0 10px; height:16px; }
.grid { display:grid; grid-template-columns:50px 50px 50px 50px; grid-template-rows:50px 50px 50px 50px 50px 50px; gap:6px; }
.slot { width:50px; height:50px; background:#26201a; border:1px solid #5a4636; }
.slot.worn { border-color:#8a6a3a; background:#2c261e; }
</style>
</head>
<body>
  <div class='inv'>
    <h1>INVENTORY</h1>
    <div class='gold'>0 gold</div>
    <div class='grid'>
      <div class='slot worn'></div><div class='slot worn'></div><div class='slot worn'></div><div class='slot'></div>
      <div class='slot'></div><div class='slot'></div><div class='slot'></div><div class='slot'></div>
      <div class='slot'></div><div class='slot'></div><div class='slot'></div><div class='slot'></div>
      <div class='slot'></div><div class='slot'></div><div class='slot'></div><div class='slot'></div>
      <div class='slot'></div><div class='slot'></div><div class='slot'></div><div class='slot'></div>
      <div class='slot'></div><div class='slot'></div><div class='slot'></div><div class='slot'></div>
    </div>
  </div>
</body>
</html>";
    }
}
