using System;
using System.IO;
using System.Reflection;
using SiegeEngine.Core.Events;
using SiegeEngine.Scenes;

namespace PoolProject
{
    // The menu is UI/menu.html. Push writes the picked classes into UI/menu.live.html and opens
    // that path with OpenGameHudEvent, the same host the scoreboard uses. The document is 400x308
    // because that is the size this event opens. data-hook clicks come back as GenericEvent.
    public static class PoolMenu
    {
        public const string Key = "PoolMenu";
        public static int Players = 2;
        public static PoolGame Game = PoolGame.Eight;
        public static bool Start;

        public static void Listen(EventBus bus)
        {
            if (bus == null) return;
            bus.Subscribe<GenericEvent>(e => Apply(e?.Hook));
        }

        public static void Apply(string hook)
        {
            if (hook == "menu.players1") Players = 1;
            else if (hook == "menu.players2") Players = 2;
            else if (hook == "menu.players3") Players = 3;
            else if (hook == "menu.players4") Players = 4;
            else if (hook == "menu.eight") Game = PoolGame.Eight;
            else if (hook == "menu.nine") Game = PoolGame.Nine;
            else if (hook == "menu.cut") Game = PoolGame.Cutthroat;
            else if (hook == "menu.start") Start = true;
        }

        public static void Push(SceneContext context)
        {
            if (context?.EventBus == null) return;
            string path = Write();
            var evt = new OpenGameHudEvent
            {
                Key = Key,
                HtmlRelativePath = string.IsNullOrEmpty(path) ? Key : path,
                HtmlContent = string.IsNullOrEmpty(path) ? Html() : null,
                Title = "Pool",
                Open = true,
                Width = 400f,
                Height = 308f,
                AllowMove = false
            };
            SetEnum(evt, "Chrome", "Bare");
            SetEnum(evt, "Docking", "Desktop");
            SetEnum(evt, "Anchor", "Center");
            context.EventBus.Publish(evt);
        }

        public static void Close(SceneContext context)
        {
            if (context?.EventBus == null) return;
            context.EventBus.Publish(new OpenGameHudEvent { Key = Key, Open = false, Title = "Pool" });
        }

        static string Write()
        {
            string dir = FindDir();
            if (dir == null) return null;
            string path = Path.Combine(dir, "menu.live.html");
            File.WriteAllText(path, Html());
            return path;
        }

        static string Html()
        {
            string text = Load() ?? Fallback();
            text = text.Replace("{{P1}}", Players == 1 ? "picked" : "choice");
            text = text.Replace("{{P2}}", Players == 2 ? "picked" : "choice");
            text = text.Replace("{{P3}}", Players == 3 ? "picked" : "choice");
            text = text.Replace("{{P4}}", Players == 4 ? "picked" : "choice");
            text = text.Replace("{{Eight}}", Game == PoolGame.Eight ? "picked" : "choice");
            text = text.Replace("{{Nine}}", Game == PoolGame.Nine ? "picked" : "choice");
            text = text.Replace("{{Cut}}", Game == PoolGame.Cutthroat ? "picked" : "choice");
            return text;
        }

        static string Load()
        {
            string path = Find();
            if (path == null) return null;
            return File.ReadAllText(path);
        }

        static string Find()
        {
            var dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string html = Path.Combine(dir, "UI", "menu.html");
                if (File.Exists(html) && File.Exists(Path.Combine(dir, "project.json"))) return html;
                string nested = Path.Combine(dir, "pool", "UI", "menu.html");
                if (File.Exists(nested)) return nested;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        static string FindDir()
        {
            string path = Find();
            return path == null ? null : Path.GetDirectoryName(path);
        }

        static string Fallback()
        {
            return "<html><body><div class=\"{{P2}}\" data-hook=\"menu.players2\">2</div><div class=\"start\" data-hook=\"menu.start\">RACK</div></body></html>";
        }

        static void SetEnum(object target, string property, string member)
        {
            var prop = target.GetType().GetProperty(property);
            if (prop == null || !prop.PropertyType.IsEnum) return;
            if (Enum.TryParse(prop.PropertyType, member, true, out object value))
                prop.SetValue(target, value);
        }
    }
}
