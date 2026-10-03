using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SiegeEngine.Core.Events;
using SiegeEngine.Scenes;

namespace BowlingProject
{
    /// <summary>
    /// Setup overlay. UI/menu.html is written to UI/menu.live.html and opened by
    /// path so images next to it (UI/MenuBackground.png) resolve. data-hook clicks
    /// only arrive if the HUD overlay has an EventBus; GameHudPanel leaves that
    /// null, so Arm() fills it from the scene bus before the click is published.
    /// </summary>
    public static class MenuBus
    {
        static int _players;
        static int _seen;
        static bool _start;
        static bool _listening;
        static bool _staticHooked;
        static readonly HashSet<object> _seenObjects = new HashSet<object>();

        public static void Listen(EventBus bus)
        {
            if (bus == null || _listening) return;
            _listening = true;
            bus.Subscribe<GenericEvent>(e =>
            {
                if (e != null) Apply(e.Hook);
            });
            ListenStaticHooks();
        }

        public static void Arm(object scene, SceneContext ctx)
        {
            // Do not walk the IDE and stamp its overlays. The menu is its own panel.
        }

        public static void Apply(string hook)
        {
            if (string.IsNullOrEmpty(hook)) return;
            if (hook == "menu.players1") _players = 1;
            else if (hook == "menu.players2") _players = 2;
            else if (hook == "menu.players3") _players = 3;
            else if (hook == "menu.players4") _players = 4;
            else if (hook == "menu.start" || hook == "menu.play") _start = true;
        }

        public static void Drain(ref int players, out bool start)
        {
            if (_players >= 1 && _players <= 4 && _players != _seen)
            {
                players = _players;
                _seen = _players;
            }
            start = _start;
            _start = false;
        }

        static void ListenStaticHooks()
        {
            if (_staticHooked) return;
            _staticHooked = true;
            try
            {
                MethodInfo apply = typeof(MenuBus).GetMethod(nameof(Apply), BindingFlags.Public | BindingFlags.Static);
                foreach (Type t in UiTypes())
                {
                    foreach (EventInfo ev in t.GetEvents(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        try
                        {
                            MethodInfo invoke = ev.EventHandlerType?.GetMethod("Invoke");
                            ParameterInfo[] ps = invoke?.GetParameters();
                            if (ps == null || ps.Length != 1 || ps[0].ParameterType != typeof(string)) continue;
                            Delegate del = Delegate.CreateDelegate(ev.EventHandlerType, apply);
                            ev.AddEventHandler(null, del);
                        }
                        catch
                        {
                            // Signature was not a string hook. Leave it.
                        }
                    }
                }
            }
            catch
            {
                // No static hook event on this build. The bus stamp is the path that matters.
            }
        }

        static void PullHooks()
        {
            foreach (Type t in UiTypes())
            {
                if (t.Name != "DataHookProcessor" && t.Name != "UIOverlay") continue;
                foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType != typeof(string)) continue;
                    string value = null;
                    try { value = f.GetValue(null) as string; } catch { continue; }
                    if (string.IsNullOrEmpty(value) || value.IndexOf("menu.", StringComparison.Ordinal) < 0) continue;
                    Apply(value);
                    try { f.SetValue(null, null); } catch { }
                }
            }
        }

        static FieldInfo[] _staticRoots;

        static void StampStatics(EventBus bus)
        {
            if (_staticRoots == null)
                _staticRoots = FindStaticRoots();
            foreach (FieldInfo f in _staticRoots)
            {
                object value = null;
                try { value = f.GetValue(null); } catch { continue; }
                Consider(value, bus, 0);
            }
        }

        static FieldInfo[] FindStaticRoots()
        {
            var found = new List<FieldInfo>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name ?? "";
                if (an.IndexOf("Siege", StringComparison.OrdinalIgnoreCase) < 0
                    && an.IndexOf("Castle", StringComparison.OrdinalIgnoreCase) < 0
                    && an.IndexOf("Foundation", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                if (types == null) continue;
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    FieldInfo[] fields;
                    try { fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                    catch { continue; }
                    foreach (FieldInfo f in fields)
                    {
                        if (f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum) continue;
                        if (!LooksLikeUiRoot(f.Name) && !LooksLikeUiRoot(f.FieldType.Name)) continue;
                        found.Add(f);
                    }
                }
            }
            return found.ToArray();
        }

        static bool LooksLikeUiRoot(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("Panel", StringComparison.Ordinal) >= 0
                || name.IndexOf("Overlay", StringComparison.Ordinal) >= 0
                || name.IndexOf("Hud", StringComparison.Ordinal) >= 0
                || name.IndexOf("UI", StringComparison.Ordinal) >= 0
                || name.IndexOf("Hook", StringComparison.Ordinal) >= 0
                || name.IndexOf("Manager", StringComparison.Ordinal) >= 0;
        }

        static void Consider(object obj, EventBus bus, int depth)
        {
            if (obj == null || depth > 5) return;
            if (!_seenObjects.Add(obj)) return;
            Type t = obj.GetType();
            if (t.IsPrimitive || t == typeof(string)) return;
            string name = t.FullName ?? "";
            if (depth > 0 && name.IndexOf("SiegeEngine", StringComparison.Ordinal) < 0) return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (IsOverlay(t))
            {
                foreach (FieldInfo f in t.GetFields(flags))
                {
                    FieldInfo field = f;
                    FillBus(field.FieldType, bus, () => field.GetValue(obj), v => field.SetValue(obj, v));
                }
                foreach (PropertyInfo p in t.GetProperties(flags))
                {
                    if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                    PropertyInfo prop = p;
                    FillBus(prop.PropertyType, bus, () => prop.GetValue(obj), v => prop.SetValue(obj, v));
                }
            }

            if (depth >= 4) return;
            foreach (FieldInfo f in t.GetFields(flags))
            {
                if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string)) continue;
                object child = null;
                try { child = f.GetValue(obj); } catch { continue; }
                WalkChild(child, bus, depth + 1);
            }
        }

        static void WalkChild(object child, EventBus bus, int depth)
        {
            if (child == null || child is EventBus || child is string) return;
            Type ct = child.GetType();
            if (ct.IsPrimitive || ct.IsEnum) return;
            string cn = ct.FullName ?? "";
            if (cn.IndexOf("SiegeEngine", StringComparison.Ordinal) >= 0)
                Consider(child, bus, depth);
            if (child is IEnumerable seq && !(child is string))
            {
                int n = 0;
                foreach (object item in seq)
                {
                    if (item == null || ++n > 48) break;
                    string inn = item.GetType().FullName ?? "";
                    if (inn.IndexOf("SiegeEngine", StringComparison.Ordinal) >= 0)
                        Consider(item, bus, depth);
                }
            }
        }

        static void FillBus(Type fieldType, EventBus bus, Func<object> get, Action<object> set)
        {
            if (fieldType != typeof(EventBus)) return;
            try
            {
                if (get() == null) set(bus);
            }
            catch
            {
                // Private setter or a init-only field we cannot reach. Try the next one.
            }
        }

        static bool IsOverlay(Type t)
        {
            string n = t.Name;
            return n.IndexOf("Overlay", StringComparison.Ordinal) >= 0
                || n.IndexOf("Panel", StringComparison.Ordinal) >= 0
                || n.IndexOf("Hook", StringComparison.Ordinal) >= 0;
        }

        static List<Type> _uiTypes;

        static List<Type> UiTypes()
        {
            if (_uiTypes != null) return _uiTypes;
            _uiTypes = new List<Type>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name ?? "";
                if (an.IndexOf("Siege", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                if (types == null) continue;
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    string n = t.Name;
                    if (n.IndexOf("UI", StringComparison.Ordinal) >= 0
                        || n.IndexOf("Panel", StringComparison.Ordinal) >= 0
                        || n.IndexOf("Hook", StringComparison.Ordinal) >= 0
                        || n.IndexOf("Hud", StringComparison.Ordinal) >= 0
                        || n.IndexOf("Overlay", StringComparison.Ordinal) >= 0)
                        _uiTypes.Add(t);
                }
            }
            return _uiTypes;
        }
    }

    public static class BowlingMenuContent
    {
        public const string PanelKey = "BowlingMenu";

        const string Fallback =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>" +
            "html,body{margin:0;padding:0;width:100%;height:100%;overflow:hidden;background-color:#070605;background-image:url(\"MenuBackground.png\");background-repeat:no-repeat;background-position:center center;background-size:cover;color:#f4efe4;font-family:'Segoe UI',sans-serif;}" +
            "#card{width:420px;margin:120px auto 0;background:#12100e;border:1px solid #c6a15b;padding:22px 26px 18px;}" +
            ".kicker{color:#8d7d62;font-size:12px;letter-spacing:3px;}" +
            "h1{margin:2px 0 0;color:#e7c56a;font-size:30px;letter-spacing:6px;font-weight:700;}" +
            ".rule{height:1px;background:#c6a15b;margin:14px 0 12px;}" +
            ".label{color:#b7a88a;font-size:12px;letter-spacing:2px;margin:0 0 8px;}" +
            ".row{display:flex;}" +
            ".choice{width:78px;height:58px;margin:0 10px 0 0;background:#1c1814;border:1px solid #4a3d2a;color:#f4efe4;font-size:22px;font-weight:650;text-align:center;cursor:pointer;}" +
            ".choice:hover{width:78px;height:58px;margin:0 10px 0 0;background:#3a2c16;border:1px solid #e7c56a;color:#ffe7a3;font-size:22px;font-weight:700;text-align:center;cursor:pointer;}" +
            ".choice:active{width:78px;height:58px;margin:0 10px 0 0;background:#5a4018;border:1px solid #ffe7a3;color:#fff6d8;font-size:22px;font-weight:700;text-align:center;cursor:pointer;}" +
            ".picked{width:78px;height:58px;margin:0 10px 0 0;background:#4a3414;border:1px solid #e7c56a;color:#ffe7a3;font-size:22px;font-weight:700;text-align:center;cursor:pointer;}" +
            ".picked:hover{width:78px;height:58px;margin:0 10px 0 0;background:#5a4018;border:1px solid #ffe7a3;color:#fff6d8;font-size:22px;font-weight:700;text-align:center;cursor:pointer;}" +
            ".picked:active{width:78px;height:58px;margin:0 10px 0 0;background:#7a5a22;border:1px solid #fff6d8;color:#fff6d8;font-size:22px;font-weight:700;text-align:center;cursor:pointer;}" +
            ".start{margin:16px 0 0;background:#241c12;border:1px solid #c6a15b;color:#e7c56a;font-size:15px;font-weight:700;letter-spacing:3px;text-align:center;padding:12px 8px;cursor:pointer;}" +
            ".start:hover{margin:16px 0 0;background:#5a4018;border:1px solid #ffe7a3;color:#fff6d8;font-size:15px;font-weight:700;letter-spacing:3px;text-align:center;padding:12px 8px;cursor:pointer;}" +
            ".start:active{margin:16px 0 0;background:#7a5a22;border:1px solid #fff6d8;color:#fff6d8;font-size:15px;font-weight:700;letter-spacing:3px;text-align:center;padding:12px 8px;cursor:pointer;}" +
            "</style></head><body><div id=\"card\">" +
            "<div class=\"kicker\">TEN PIN</div><h1>BOWLING</h1><div class=\"rule\"></div>" +
            "<div class=\"label\">PLAYERS</div><div class=\"row\">" +
            "<div class=\"button {{P1}}\" data-hook=\"menu.players1\">1</div>" +
            "<div class=\"button {{P2}}\" data-hook=\"menu.players2\">2</div>" +
            "<div class=\"button {{P3}}\" data-hook=\"menu.players3\">3</div>" +
            "<div class=\"button {{P4}}\" data-hook=\"menu.players4\">4</div>" +
            "</div><div class=\"button start\" data-hook=\"menu.start\">STEP ONTO THE LANE</div>" +
            "</div></body></html>";

        public static string Build(int players)
        {
            if (players < 1) players = 1;
            if (players > 4) players = 4;
            string html = Load() ?? Fallback;
            for (int i = 1; i <= 4; i++)
                html = html.Replace("{{P" + i + "}}", i == players ? "picked" : "choice");
            return html;
        }

        public static string WriteLive(int players)
        {
            string html = Build(players);
            string dir = DirectoryOfMenu();
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "menu.live.html");
                File.WriteAllText(path, html);
                return path;
            }
            catch
            {
                return null;
            }
        }

        static string Load()
        {
            try
            {
                string path = Find();
                if (path == null) return null;
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text) || text.IndexOf("{{P1}}", StringComparison.Ordinal) < 0)
                    return null;
                return text;
            }
            catch
            {
                return null;
            }
        }

        static string DirectoryOfMenu()
        {
            string path = Find();
            if (!string.IsNullOrEmpty(path)) return Path.GetDirectoryName(path);
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "project.json")))
                    return Path.Combine(dir, "UI");
                string nested = Path.Combine(dir, "bowling", "project.json");
                if (File.Exists(nested))
                    return Path.Combine(dir, "bowling", "UI");
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        static string Find()
        {
            string hit = Walk(Directory.GetCurrentDirectory());
            if (hit != null) return hit;
            hit = Walk(AppContext.BaseDirectory);
            if (hit != null) return hit;
            try
            {
                string loc = typeof(BowlingMenuContent).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                    hit = Walk(Path.GetDirectoryName(loc));
            }
            catch
            {
                hit = null;
            }
            return hit;
        }

        static string Walk(string start)
        {
            var dir = start;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string html = Path.Combine(dir, "UI", "menu.html");
                string proj = Path.Combine(dir, "project.json");
                if (File.Exists(html) && File.Exists(proj)) return html;
                string nested = Path.Combine(dir, "bowling", "UI", "menu.html");
                string nestedProj = Path.Combine(dir, "bowling", "project.json");
                if (File.Exists(nested) && File.Exists(nestedProj)) return nested;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }

    public static class BowlingMenuHost
    {
        static string _openPath = BowlingMenuContent.PanelKey;

        public static void Push(SceneContext context, int players, float width, float height)
        {
            if (context?.EventBus == null) return;
            string file = BowlingMenuContent.WriteLive(players);
            string html = string.IsNullOrEmpty(file) ? BowlingMenuContent.Build(players) : null;
            _openPath = string.IsNullOrEmpty(file) ? BowlingMenuContent.PanelKey : file;
            if (width < 640f) width = 1280f;
            if (height < 360f) height = 720f;
            var evt = new OpenGameHudEvent
            {
                Key = BowlingMenuContent.PanelKey,
                HtmlRelativePath = _openPath,
                Title = "Bowling",
                HtmlContent = html,
                Open = true,
                Width = width,
                Height = height,
                PosX = 0f,
                PosY = 0f,
                AllowMove = false
            };
            SetEnum(evt, "Chrome", "Bare");
            if (!SetEnum(evt, "Docking", "Fill"))
                if (!SetEnum(evt, "Docking", "Overlay"))
                    if (!SetEnum(evt, "Docking", "Fullscreen"))
                        SetEnum(evt, "Docking", "Desktop");
            if (!SetEnum(evt, "Anchor", "TopLeft"))
                if (!SetEnum(evt, "Anchor", "None"))
                    SetEnum(evt, "Anchor", "Center");
            context.EventBus.Publish(evt);
        }

        public static void Close(SceneContext context)
        {
            if (context?.EventBus == null) return;
            var evt = new OpenGameHudEvent
            {
                Key = BowlingMenuContent.PanelKey,
                HtmlRelativePath = _openPath,
                Title = "Bowling",
                Open = false
            };
            context.EventBus.Publish(evt);
        }

        static bool SetEnum(object target, string property, string member)
        {
            try
            {
                PropertyInfo prop = target.GetType().GetProperty(property);
                if (prop == null || !prop.PropertyType.IsEnum) return false;
                if (Enum.TryParse(prop.PropertyType, member, true, out object value))
                {
                    prop.SetValue(target, value);
                    return true;
                }
            }
            catch
            {
                // Older builds omit one of these fields. The panel still opens.
            }
            return false;
        }
    }
}
