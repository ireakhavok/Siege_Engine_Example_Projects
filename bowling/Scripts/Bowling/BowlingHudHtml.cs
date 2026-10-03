using System;
using System.IO;
using System.Reflection;
using System.Text;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.GPU.ContextManagement;
using SiegeEngine.Core.UI;
using SiegeEngine.Scenes;

namespace BowlingProject
{
    public enum PhaseLabel { Aim, Rolling, Watching, Over }

    /// <summary>
    /// HTML scoreboard. Game scripts cannot see IHostedContent, so this publishes
    /// OpenGameHudEvent.HtmlContent and reloads the open overlay when the card changes.
    /// </summary>
    public static class BowlingHudContent
    {
        public const string PanelKey = "BowlingScoreboard";

        public static float PanelHeight(int players)
        {
            if (players < 1) players = 1;
            if (players > 4) players = 4;
            return 78f + players * 34f;
        }

        public static string Build(BowlingScore[] scores, int players, int turn, int frame, int ball, PhaseLabel phase, float power, bool charging, float hook, string banner)
        {
            if (players < 1) players = 1;
            if (players > 4) players = 4;
            var sb = new StringBuilder(6144);
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>");
            sb.Append("html,body{margin:0;padding:0;background:transparent;overflow:hidden;");
            sb.Append("font-family:'Segoe UI',Arial,sans-serif;color:#f4efe4;user-select:none;}");
            sb.Append("#board{background:rgba(10,12,16,0.94);border:1px solid #3d382c;");
            sb.Append("border-radius:8px;padding:8px 10px 8px;box-sizing:border-box;}");
            sb.Append(".top{display:flex;align-items:baseline;gap:12px;}");
            sb.Append("#status{font-size:14px;font-weight:650;color:#f3d48a;}");
            sb.Append("#banner{margin-left:auto;font-size:16px;font-weight:750;color:#ffe7a3;}");
            sb.Append(".players{display:flex;gap:4px;}");
            sb.Append(".pl{font-size:12px;line-height:18px;min-width:18px;text-align:center;");
            sb.Append("border-radius:4px;border:1px solid #4a463c;background:#1c1a16;color:#b7b1a4;}");
            sb.Append(".pl.on{background:#3a2e16;border-color:#e2b657;color:#ffe7a3;font-weight:700;}");
            sb.Append(".row{display:flex;gap:3px;align-items:stretch;margin-top:4px;padding:2px 3px;border-radius:5px;}");
            sb.Append(".row.turn{background:rgba(226,182,87,0.10);outline:1px solid #e2b657;}");
            sb.Append(".who{width:64px;flex:0 0 64px;font-size:12px;font-weight:650;display:flex;align-items:center;}");
            sb.Append(".frames{display:flex;gap:2px;flex:1;}");
            sb.Append(".fr{flex:1;min-width:0;background:#141210;border:1px solid #2c291f;border-radius:3px;}");
            sb.Append(".fr.now{border-color:#e2b657;}");
            sb.Append(".fn{font-size:10px;color:#8a8478;text-align:center;line-height:12px;}");
            sb.Append(".mk{font-size:12px;text-align:center;line-height:16px;height:16px;color:#f7f1e6;}");
            sb.Append(".tot{width:36px;flex:0 0 36px;font-size:15px;font-weight:750;color:#f2d48a;");
            sb.Append("display:flex;align-items:center;justify-content:flex-end;}");
            sb.Append(".meter{margin-top:6px;height:8px;background:#100e0c;border:1px solid #3a3428;border-radius:4px;overflow:hidden;}");
            sb.Append("#power{height:100%;background:linear-gradient(90deg,#8a5a18,#f2d48a);}");
            sb.Append("</style></head><body><div id=\"board\">");
            sb.Append("<div class=\"top\"><div id=\"status\">").Append(Esc(Status(players, turn, frame, ball, phase, hook))).Append("</div>");
            sb.Append("<div id=\"banner\">").Append(Esc(banner ?? "")).Append("</div></div>");
            for (int p = 0; p < players; p++)
            {
                sb.Append("<div class=\"row").Append(p == turn && phase != PhaseLabel.Over ? " turn" : "").Append("\">");
                sb.Append("<div class=\"who\">P").Append(p + 1).Append("</div><div class=\"frames\">");
                var score = scores[p];
                for (int f = 0; f < 10; f++)
                {
                    bool now = p == turn && f == frame && phase != PhaseLabel.Over;
                    sb.Append("<div class=\"fr").Append(now ? " now" : "").Append("\">");
                    sb.Append("<div class=\"fn\">").Append(f + 1).Append("</div>");
                    sb.Append("<div class=\"mk\">").Append(Esc(BowlingHud.Marks(score, f))).Append("</div></div>");
                }
                sb.Append("</div><div class=\"tot\">").Append(score.Total()).Append("</div></div>");
            }
            int pct = charging ? (int)Math.Clamp(power * 100f, 0f, 100f) : 0;
            sb.Append("<div class=\"meter\"><div id=\"power\" style=\"width:").Append(pct).Append("%\"></div></div>");
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        public static string WriteLive(BowlingScore[] scores, int players, int turn, int frame, int ball, PhaseLabel phase, float power, bool charging, float hook, string banner)
        {
            string html = Build(scores, players, turn, frame, ball, phase, power, charging, hook, banner);
            string dir = UiDirectory();
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "score.live.html");
                File.WriteAllText(path, html);
                return path;
            }
            catch
            {
                return null;
            }
        }

        static string UiDirectory()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "project.json")))
                    return Path.Combine(dir, "UI");
                if (File.Exists(Path.Combine(dir, "bowling", "project.json")))
                    return Path.Combine(dir, "bowling", "UI");
                dir = Path.GetDirectoryName(dir);
            }
            try
            {
                string loc = typeof(BowlingHudContent).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    dir = Path.GetDirectoryName(loc);
                    for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                    {
                        if (File.Exists(Path.Combine(dir, "project.json")))
                            return Path.Combine(dir, "UI");
                        dir = Path.GetDirectoryName(dir);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        static string Status(int players, int turn, int frame, int ball, PhaseLabel phase, float hook)
        {
            if (phase == PhaseLabel.Over)
                return players == 1 ? "Game over" : "Game over · " + players + " players";
            string ballName = ball <= 0 ? "Ball 1" : "Ball " + (ball + 1);
            int pct = (int)Math.Round(Math.Abs(hook) * 20f) * 5;
            string hookText = pct == 0 ? "Hook straight" : (hook < 0f ? "Hook " + pct + "% left" : "Hook " + pct + "% right");
            return "Player " + (turn + 1) + "  ·  Frame " + (frame + 1) + "  ·  " + ballName + "  ·  " + hookText;
        }

        static string Esc(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '&') sb.Append((char)38).Append("amp;");
                else if (c == '<') sb.Append((char)38).Append("lt;");
                else if (c == '>') sb.Append((char)38).Append("gt;");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public static class BowlingHudHost
    {
        public static void Push(SceneContext context, string html, string file, float height)
        {
            if (context?.EventBus == null) return;
            if (string.IsNullOrEmpty(html) && string.IsNullOrEmpty(file)) return;
            var evt = new OpenGameHudEvent
            {
                Key = BowlingHudContent.PanelKey,
                HtmlRelativePath = string.IsNullOrEmpty(file) ? BowlingHudContent.PanelKey : file,
                Title = "Bowling",
                HtmlContent = string.IsNullOrEmpty(file) ? html : null,
                Open = true,
                Width = 920f,
                Height = height,
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
            var evt = new OpenGameHudEvent
            {
                Key = BowlingHudContent.PanelKey,
                HtmlRelativePath = BowlingHudContent.PanelKey,
                Title = "Bowling",
                Open = false
            };
            context.EventBus.Publish(evt);
        }

        static void SetEnum(object target, string property, string member)
        {
            try
            {
                PropertyInfo prop = target.GetType().GetProperty(property);
                if (prop == null || !prop.PropertyType.IsEnum) return;
                if (Enum.TryParse(prop.PropertyType, member, true, out object value))
                    prop.SetValue(target, value);
            }
            catch
            {
                // Older builds omit one of these fields. The panel still opens.
            }
        }
    }
}
