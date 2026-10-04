using System;
using System.IO;
using System.Reflection;
using SiegeEngine.Core.Events;
using SiegeEngine.Scenes;

namespace PoolProject
{
    // UI/hud.html is the scoreboard. The file owns the layout. The only substitution
    // is the four tokens already written in that file.
    public static class PoolUi
    {
        public const string Key = "PoolScoreboard";
        static string _template;
        static string _shown;
        static string _path;

        public static void Push(SceneContext context, string who, string game, string detail, int power)
        {
            if (context?.EventBus == null) return;
            string html = Fill(who, game, detail, power);
            if (html == _shown) return;
            _shown = html;
            var evt = new OpenGameHudEvent
            {
                Key = Key,
                HtmlRelativePath = _path ?? Key,
                HtmlContent = html,
                Title = "Pool",
                Open = true,
                Width = 560f,
                Height = 78f,
                AllowMove = false
            };
            SetEnum(evt, "Chrome", "Bare");
            SetEnum(evt, "Docking", "Desktop");
            SetEnum(evt, "Anchor", "Top");
            context.EventBus.Publish(evt);
        }

        public static void Close(SceneContext context)
        {
            if (context?.EventBus == null) return;
            _shown = null;
            context.EventBus.Publish(new OpenGameHudEvent { Key = Key, Open = false, Title = "Pool" });
        }

        static string Fill(string who, string game, string detail, int power)
        {
            if (_template == null)
            {
                _path = Find();
                _template = _path == null
                    ? "<html><body>{{WHO}} {{GAME}} {{DETAIL}}</body></html>"
                    : File.ReadAllText(_path);
            }
            return _template
                .Replace("{{WHO}}", who)
                .Replace("{{GAME}}", game)
                .Replace("{{DETAIL}}", detail)
                .Replace("{{POWER}}", power.ToString());
        }

        public static string Root;

        static string Find()
        {
            if (!string.IsNullOrEmpty(Root))
            {
                string here = Path.Combine(Root, "UI", "hud.html");
                if (File.Exists(here)) return here;
            }
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string html = Path.Combine(dir, "UI", "hud.html");
                if (File.Exists(html) && File.Exists(Path.Combine(dir, "project.json"))) return html;
                string nested = Path.Combine(dir, "pool", "UI", "hud.html");
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
