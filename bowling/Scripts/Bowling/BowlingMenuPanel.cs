using System.IO;
using System.Numerics;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.GPU;
using SiegeEngine.Core.GPU.ContextManagement;
using SiegeEngine.Core.UI;

namespace BowlingProject
{
    /// <summary>
    /// Full-screen menu drawn the same way SceneEditorPanel draws its toolbar:
    /// a UIOverlay owned by this panel, with HandleDataHook on that overlay.
    /// It is opened as OpenMode.Overlay. It is not docked into the IDE.
    /// </summary>
    public sealed class BowlingMenuPanel : BasePanel
    {
        public static int Players = 1;
        public static bool Start;

        sealed class MenuOverlay : UIOverlay
        {
            readonly BowlingMenuPanel _parent;

            public MenuOverlay(BowlingMenuPanel parent, IRenderContext renderContext, IControlContext controlContext, nint window)
                : base(renderContext, controlContext, window)
            {
                _parent = parent;
            }

            protected override void HandleDataHook(string hook)
            {
                if (hook == "menu.players1") Players = 1;
                else if (hook == "menu.players2") Players = 2;
                else if (hook == "menu.players3") Players = 3;
                else if (hook == "menu.players4") Players = 4;
                else if (hook == "menu.start" || hook == "menu.play") Start = true;
                if (hook != null && hook.StartsWith("menu.players"))
                    _parent.Reload();
            }
        }

        public BowlingMenuPanel(IRenderContext renderContext, IControlContext controlContext, nint window, EventBus eventBus, int players, float width, float height)
            : base(renderContext, controlContext, window, eventBus)
        {
            if (players < 1) players = 1;
            if (players > 4) players = 4;
            Players = players;
            HasTitleBar = false;
            IsClosable = false;
            IsModal = true;
            RenderOrder = 1100;
            Scaling = ScalingMode.Fill;
            if (width < 640f) width = 1280f;
            if (height < 360f) height = 720f;
            Size = new Vector2(width, height);
            BaseWidth = width;
            BaseHeight = height;
        }

        protected override UIOverlay CreateUIOverlay()
        {
            return new MenuOverlay(this, _renderContext, _controlContext, _window);
        }

        public override void Init()
        {
            base.Init();
            Reload();
        }

        public void Reload()
        {
            if (_uiOverlay == null) return;
            string file = BowlingMenuContent.WriteLive(Players);
            string html = string.IsNullOrEmpty(file) ? BowlingMenuContent.Build(Players) : File.ReadAllText(file);
            string dir = string.IsNullOrEmpty(file) ? "" : Path.GetDirectoryName(file) ?? "";
            _uiOverlay.PanelWidth = Size.X;
            _uiOverlay.PanelHeight = Size.Y;
            _uiOverlay.LoadUI(html, dir);
            _uiOverlay.RefreshUI();
        }
    }
}
