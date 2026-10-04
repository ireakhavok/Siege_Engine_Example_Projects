using System;
using System.IO;
using System.Reflection;
using SiegeEngine.Core.Events;
using SiegeEngine.Scenes;

namespace PoolProject
{
    // UI/menu.html is the menu. It is opened by path. data-hook clicks come back as GenericEvent.
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
            string path = Find();
            var evt = new OpenGameHudEvent
            {
                Key = Key,
                HtmlRelativePath = string.IsNullOrEmpty(path) ? Key : path,
                Title = "Pool",
                Open = true,
                Width = 420f,
                Height = 390f,
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
            context.EventBus.Publish(new OpenGameHudEvent
            {
                Key = Key,
                HtmlRelativePath = Find() ?? Key,
                Title = "Pool",
                Open = false
            });
        }

        public static string Root;

        public static string Find()
        {
            if (!string.IsNullOrEmpty(Root))
            {
                string here = Path.Combine(Root, "UI", "menu.html");
                if (File.Exists(here)) return here;
            }
            string dir = Directory.GetCurrentDirectory();
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

        static void SetEnum(object target, string property, string member)
        {
            var prop = target.GetType().GetProperty(property);
            if (prop == null || !prop.PropertyType.IsEnum) return;
            if (Enum.TryParse(prop.PropertyType, member, true, out object value))
                prop.SetValue(target, value);
        }
    }
}
