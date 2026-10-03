using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using SiegeEngine.Core.AssetParsing.Model;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.GPU;
using SiegeEngine.Core.GPU.ContextManagement;
using SiegeEngine.Core.GPU.Shaders;
using SiegeEngine.Core.Managers;
using SiegeEngine.Core.Physics;
using SiegeEngine.Core.UI;
using SiegeEngine.Scenes;

namespace BowlingProject
{
    [CustomSceneEntry]
    public sealed class BowlingScene : Scene
    {
        enum Phase { Menu, Aim, Rolling, Watching, Over }

        struct Pin
        {
            public Entity Entity;
            public PhysicsComponent Body;
            public bool Live;
            public bool Loose;
        }

        readonly bool _preview;
        readonly bool _panel;
        readonly SceneContext _context;

        ShaderProgram _shader;
        VertexBuffer _houseBuf;
        VertexBuffer _liveBuf;
        VertexBuffer _uiBuf;
        BowlMesh _house;
        BowlMesh _live;
        BowlMesh _ui;
        bool _houseReady;
        bool _started;
        float _viewW = 1280f;
        float _viewH = 720f;

        readonly List<Entity> _staticBodies = new List<Entity>();
        readonly Pin[] _pins = new Pin[10];
        readonly Pin[] _leftPins = new Pin[10];
        readonly Pin[] _rightPins = new Pin[10];
        Entity _ball;
        PhysicsComponent _ballBody;

        BowlingScore[] _scores = { new BowlingScore(), new BowlingScore(), new BowlingScore(), new BowlingScore() };
        int _players = 1;
        int _turn;
        bool _hudOpened;
        string _hudSig = "";
        bool _menuOpened;
        BowlingMenuPanel _menuPanel;
        int _menuPlayers;
        readonly bool[] _digitWas = new bool[4];
        readonly Key[] _playerKeys = new Key[4];
        readonly bool[] _playerKeyOk = new bool[4];
        bool _playerKeysReady;
        bool _enterWas;
        Phase _phase = Phase.Menu;
        int _frame;
        int _ballInFrame;
        bool _freshRack = true;
        bool _thrownFresh;
        bool _ballLeftLane;
        float _gutterX;
        int _liveAtRelease;
        float _time;
        float _watch;
        float _quiet;
        float _bannerUntil;
        float _lateral = 0.20f;
        float _aim = -0.055f;
        float _hook = 0.42f;
        float _power;
        bool _charging;
        bool _rWas;
        string _banner = "";

        Vector3 _camEye;
        Vector3 _camTarget;

        public BowlingScene(SceneContext context) : base(context)
        {
            _preview = context != null && context.IsHostedPreview;
            _panel = context != null && context.IsPanelHosted;
            _context = context;
        }

        public override void Initialize(int width, int height)
        {
            base.Initialize(width, height);
            _renderContext.ClearColor(0.025f, 0.03f, 0.038f, 1f);
            _shader = ShaderProgram.FromId(_renderContext, ShaderId.Grid);
            _houseBuf = new VertexBuffer(_renderContext);
            _liveBuf = new VertexBuffer(_renderContext);
            _uiBuf = new VertexBuffer(_renderContext);
            _house = new BowlMesh();
            _live = new BowlMesh();
            _ui = new BowlMesh();
            LaneGeometry.BuildHouse(_house, _preview);
            _houseBuf.UpdateCustomWithUV(_house.Vertices, _house.Indices);
            _houseReady = true;
            _viewW = width > 2 ? width : 1280f;
            _viewH = height > 2 ? height : 720f;
            MenuView(out _camEye, out _camTarget);
            if (_preview)
            {
                _camEye = new Vector3(0.85f, -4.35f, 1.72f);
                _camTarget = new Vector3(0f, 11.5f, 0.08f);
            }
        }

        public override void Update(float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            if (deltaTime > 0.05f) deltaTime = 0.05f;
            TrackSize();
            KeepBallSphere();
            base.Update(deltaTime);
            _time += deltaTime;
            _renderContext.ClearColor(0.025f, 0.03f, 0.038f, 1f);
            if (!_houseReady) return;
            RememberView();
            if (_phase == Phase.Menu)
                PollMenu();
            SyncMenu();
            if (_preview) return;
            if (!_started) EnsureWorld();
            SeatWildPins();

            if (_phase != Phase.Menu)
            {
                Poll(deltaTime);
                if (_phase == Phase.Rolling)
                    TickRoll(deltaTime);
                else if (_phase == Phase.Watching)
                    TickWatch(deltaTime);
            }

            AimCamera(deltaTime);
            SyncHud();
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            if (width > 2) _viewW = width;
            if (height > 2) _viewH = height;
        }

        void TrackSize()
        {
            try
            {
                _controlContext.GetWindowSize(_window, out int ww, out int hh);
                if (ww > 2 && hh > 2 && (ww != _width || hh != _height))
                    Resize(ww, hh);
            }
            catch
            {
                // Resize from the engine still updates the frustum.
            }
        }

        protected override void GetViewProjection(out Matrix4x4 view, out Matrix4x4 projection)
        {
            float aspect = _viewH > 2f ? _viewW / _viewH : (AspectRatio > 0.01f ? AspectRatio : 16f / 9f);
            if (_width > 2 && _height > 2)
                aspect = (float)_width / _height;
            if (_preview)
            {
                _camEye = new Vector3(0.85f, -4.35f, 1.72f);
                _camTarget = new Vector3(0f, 11.5f, 0.08f);
            }
            float fov = ActiveFov();
            projection = Matrix4x4.CreatePerspectiveFieldOfView(fov * MathF.PI / 180f, aspect, 0.08f, 95f);
            view = Matrix4x4.CreateLookAt(_camEye, _camTarget, Vector3.UnitZ);
        }

        protected override void RenderContent(IReadOnlyList<Entity> entities, Matrix4x4 view, Matrix4x4 projection)
        {
            if (_shader == null || _houseBuf == null) return;

            _shader.Use();
            _renderContext.SetCull(GpuCullMode.None);
            _renderContext.SetDepthTest(true);
            _renderContext.SetDepthWrite(true);
            _renderContext.SetConstants(ConstantSlot.Frame, new FrameCB
            {
                View = view,
                Projection = projection,
                ViewPos = new Vector4(_camEye, 1f),
                Time = _time,
                HasTexture = 0
            });
            _renderContext.SetConstants(ConstantSlot.Object, new ObjectCB
            {
                Model = Matrix4x4.Identity,
                NormalMatrix = Matrix4x4.Identity
            });
            Draw(_houseBuf);

            if (!_preview)
            {
                BuildLive();
                if (_live.Indices.Count > 0)
                {
                    _liveBuf.UpdateCustomWithUV(_live.Vertices, _live.Indices);
                    Draw(_liveBuf);
                }
            }
        }

        public override void Dispose()
        {
            if (_menuOpened) CloseMenu();
            if (_hudOpened) BowlingHudHost.Close(_context);
            _shader?.Dispose();
            _houseBuf?.Dispose();
            _liveBuf?.Dispose();
            _uiBuf?.Dispose();
            base.Dispose();
        }

        float ActiveFov()
        {
            if (_preview) return 58f;
            if (_phase == Phase.Menu) return 48f;
            return 52f;
        }

        void DrawMenuInWorld()
        {
            try
            {
                RememberView();
                float w = _viewW > 2f ? _viewW : 1280f;
                float h = _viewH > 2f ? _viewH : 720f;
                _ui.Clear();
                BowlingMenu.Draw(_ui, w, h, _players, MenuHover(), _time);
                PlaceMenuInView(_ui, w, h);
                if (_ui.Indices.Count == 0) return;
                _renderContext.SetCull(GpuCullMode.None);
                _renderContext.SetDepthTest(false);
                _renderContext.SetDepthWrite(false);
                _uiBuf.UpdateCustomWithUV(_ui.Vertices, _ui.Indices);
                Draw(_uiBuf);
                _renderContext.SetDepthTest(true);
                _renderContext.SetDepthWrite(true);
            }
            catch
            {
                // The alley still draws if the menu mesh fails.
            }
        }

        void PlaceMenuInView(BowlMesh mesh, float w, float h)
        {
            float fov = ActiveFov() * MathF.PI / 180f;
            float aspect = w / MathF.Max(1f, h);
            const float dist = 2.8f;
            float halfH = dist * MathF.Tan(fov * 0.5f);
            float halfW = halfH * aspect;
            Vector3 fwd = _camTarget - _camEye;
            if (fwd.LengthSquared() < 1e-6f) fwd = Vector3.UnitY;
            fwd = Vector3.Normalize(fwd);
            Vector3 right = Vector3.Cross(fwd, Vector3.UnitZ);
            if (right.LengthSquared() < 1e-8f) right = Vector3.UnitX;
            right = Vector3.Normalize(right);
            Vector3 down = -Vector3.Normalize(Vector3.Cross(right, fwd));
            Vector3 origin = _camEye + fwd * dist - right * halfW - down * halfH;
            float sx = (halfW * 2f) / w;
            float sy = (halfH * 2f) / h;
            var verts = mesh.Vertices;
            for (int i = 0; i + 8 < verts.Count; i += 9)
            {
                float px = verts[i];
                float py = verts[i + 1];
                Vector3 p = origin + right * (px * sx) + down * (py * sy);
                verts[i] = p.X;
                verts[i + 1] = p.Y;
                verts[i + 2] = p.Z;
            }
        }

        void Poll(float dt)
        {
            bool space = Down(Key.Space);
            bool mouse = DownMouse();
            bool r = Down(Key.R);
            float left = (Down(Key.A) || Down(Key.Left)) ? 1f : 0f;
            float right = (Down(Key.D) || Down(Key.Right)) ? 1f : 0f;
            if (Down(Key.Q)) _hook = Math.Clamp(_hook - dt * 0.45f, 0f, 1f);
            if (Down(Key.E)) _hook = Math.Clamp(_hook + dt * 0.45f, 0f, 1f);
            if (_phase == Phase.Aim)
            {
                _lateral = Math.Clamp(_lateral + (right - left) * 0.55f * dt, -0.40f, 0.40f);
                if (!mouse) ReadAim();
                bool held = space || mouse;
                if (held) _power = 0.5f + 0.5f * MathF.Sin(_time * 2.7f);
                if (_charging && !held) Throw(_power);
                _charging = held;
                PlaceAimBall();
            }
            else _charging = false;

            if (r && !_rWas) NewGame();
            _rWas = r;
            if (_phase == Phase.Over && Pressed(StartKey()) && !_enterWas)
                ReturnToMenu();
            _enterWas = Pressed(StartKey());
        }

        void PollMenu()
        {
            EnsurePlayerKeys();
            if (BowlingMenuPanel.Players >= 1 && BowlingMenuPanel.Players <= 4)
                _players = BowlingMenuPanel.Players;
            bool start = BowlingMenuPanel.Start;
            BowlingMenuPanel.Start = false;
            for (int i = 0; i < 4; i++)
            {
                if (!_playerKeyOk[i]) continue;
                bool down = Down(_playerKeys[i]);
                if (down && !_digitWas[i])
                    _players = i + 1;
                _digitWas[i] = down;
            }
            bool enter = Pressed(StartKey());
            if ((enter && !_enterWas) || start)
                NewGame();
            _enterWas = enter;
        }

        int MenuHover()
        {
            if (!TryCursor(out float x, out float y)) return -1;
            return BowlingMenu.Layout(_viewW, _viewH).Hit(x, y);
        }

        bool TryCursor(out float x, out float y)
        {
            x = 0f;
            y = 0f;
            try
            {
                _controlContext.GetCursorPos(_window, out double cx, out double cy);
                x = (float)cx;
                y = (float)cy;
                Viewport vp = _controlContext.GetCurrentViewport();
                if (vp.Width > 2f)
                {
                    _viewW = vp.Width;
                    if (vp.Height > 2f) _viewH = vp.Height;
                    if (_panel || MathF.Abs(vp.X) > 0.5f || MathF.Abs(vp.Y) > 0.5f)
                    {
                        x -= vp.X;
                        y -= vp.Y;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        void RememberView()
        {
            if (_width > 2 && _height > 2)
            {
                _viewW = _width;
                _viewH = _height;
                return;
            }
            try
            {
                Viewport vp = _controlContext.GetCurrentViewport();
                if (vp.Width > 2f && vp.Height > 2f)
                {
                    _viewW = vp.Width;
                    _viewH = vp.Height;
                }
            }
            catch
            {
                // Keep the size from Initialize.
            }
        }

        static Key StartKey()
        {
            if (Enum.TryParse("Enter", false, out Key enter)) return enter;
            if (Enum.TryParse("Return", false, out Key ret)) return ret;
            return Key.Space;
        }

        bool Pressed(Key key)
        {
            try { return _controlContext.GetKey(_window, key) == InputAction.Press; }
            catch { return false; }
        }

        void PollPlayers()
        {
            if (_phase != Phase.Aim && _phase != Phase.Over) return;
            EnsurePlayerKeys();
            for (int i = 0; i < 4; i++)
            {
                if (!_playerKeyOk[i]) continue;
                bool down = Down(_playerKeys[i]);
                if (down && !_digitWas[i] && _players != i + 1)
                {
                    _players = i + 1;
                    NewGame();
                }
                _digitWas[i] = down;
            }
        }

        void EnsurePlayerKeys()
        {
            if (_playerKeysReady) return;
            _playerKeysReady = true;
            string[][] names =
            {
                new[] { "D1", "Digit1", "Number1", "Alpha1", "Num1", "Key1", "K1", "One", "F1" },
                new[] { "D2", "Digit2", "Number2", "Alpha2", "Num2", "Key2", "K2", "Two", "F2" },
                new[] { "D3", "Digit3", "Number3", "Alpha3", "Num3", "Key3", "K3", "Three", "F3" },
                new[] { "D4", "Digit4", "Number4", "Alpha4", "Num4", "Key4", "K4", "Four", "F4" }
            };
            for (int i = 0; i < 4; i++)
            {
                foreach (string name in names[i])
                {
                    if (!Enum.TryParse(name, false, out Key key)) continue;
                    _playerKeys[i] = key;
                    _playerKeyOk[i] = true;
                    break;
                }
            }
        }

        void ReadAim()
        {
            try
            {
                _controlContext.GetCursorPos(_window, out double cx, out double cy);
                float x = (float)cx;
                float w = _width;
                if (_panel)
                {
                    Viewport vp = _controlContext.GetCurrentViewport();
                    x -= vp.X;
                    if (vp.Width > 2f) w = vp.Width;
                }
                if (w > 2f)
                {
                    float n = Math.Clamp(x / w * 2f - 1f, -1f, 1f);
                    _aim = n * 0.30f;
                }
            }
            catch
            {
                // Host has not handed over a cursor yet.
            }
        }

        bool Down(Key key)
        {
            try { return _controlContext.GetKey(_window, key) == InputAction.Press; }
            catch { return false; }
        }

        bool DownMouse()
        {
            try { return _controlContext.GetMouseButton(_window, MouseButton.Left) == InputAction.Press; }
            catch { return false; }
        }

        void NewGame()
        {
            if (_server == null) return;
            ClearDynamics();
            EnsureColliders();
            for (int i = 0; i < _scores.Length; i++)
                _scores[i] = new BowlingScore();
            _players = Math.Clamp(_players, 1, 4);
            _turn = 0;
            _frame = 0;
            _ballInFrame = 0;
            _freshRack = true;
            _banner = "";
            _bannerUntil = 0f;
            _phase = Phase.Aim;
            _started = true;
            SpawnRack();
            SpawnAimBall();
            SyncHud();
        }

        void ReturnToMenu()
        {
            if (_server == null) return;
            _banner = "";
            _bannerUntil = 0f;
            _charging = false;
            _phase = Phase.Menu;
            SpawnRack();
            SpawnAimBall();
            MenuView(out _camEye, out _camTarget);
        }

        void EnsureWorld()
        {
            if (_server == null) return;
            _started = true;
            EnsureColliders();
            if (_pins[0].Body == null)
            {
                SpawnRack();
                SpawnAimBall();
            }
            _phase = Phase.Menu;
        }

        BowlingScore Score => _scores[_turn];

        void CloseMenu()
        {
            if (_menuPanel != null && _eventBus != null)
                _eventBus.Publish(new ClosePanelEvent(_menuPanel));
            _menuPanel = null;
            _menuOpened = false;
            _menuPlayers = 0;
        }

        void SyncMenu()
        {
            if (_preview || _eventBus == null) return;
            if (_phase != Phase.Menu)
            {
                if (_menuOpened) CloseMenu();
                return;
            }
            if (_menuOpened) return;
            BowlingMenuPanel.Players = _players;
            BowlingMenuPanel.Start = false;
            _menuPanel = new BowlingMenuPanel(_renderContext, _controlContext, _window, _eventBus, _players, _viewW, _viewH);
            _eventBus.Publish(new OpenPanelEvent(_menuPanel) { Mode = OpenMode.Overlay });
            _menuOpened = true;
            _menuPlayers = _players;
        }

        void SyncHud()
        {
            if (_context == null) return;
            if (_phase == Phase.Menu)
            {
                if (_hudOpened)
                {
                    BowlingHudHost.Close(_context);
                    _hudOpened = false;
                    _hudSig = "";
                }
                return;
            }
            var label = _phase switch
            {
                Phase.Rolling => PhaseLabel.Rolling,
                Phase.Watching => PhaseLabel.Watching,
                Phase.Over => PhaseLabel.Over,
                _ => PhaseLabel.Aim
            };
            string banner = _time < _bannerUntil ? _banner : "";
            if (_phase == Phase.Over) banner = "FINAL";
            float shownPower = _charging ? (float)Math.Round(_power * 20f) / 20f : 0f;
            string html = BowlingHudContent.Build(
                _scores, _players, _turn, _frame, _ballInFrame, label, shownPower, _charging, _hook, banner);
            if (_hudOpened && html == _hudSig) return;
            _hudSig = html;
            BowlingHudHost.Push(_context, html, BowlingHudContent.PanelHeight(_players));
            _hudOpened = true;
        }

        void TickRoll(float dt)
        {
            if (_ballBody == null) { _phase = Phase.Watching; _watch = 0f; _quiet = 0f; return; }
            KeepBallHonest(dt);

            if (Quiet()) _quiet += dt;
            else _quiet = 0f;
            var p = _ballBody;
            bool pastPins = p.Position.Y > LaneGeometry.HeadPinY + 0.3f;
            bool deep = p.Position.Y > 15.5f;
            bool ballDone = (_ballLeftLane && pastPins)
                || p.Position.Y > LaneGeometry.DeckEndY
                || (deep && p.Velocity.Length() < 0.3f)
                || (deep && p.IsSleeping);
            float needQuiet = _ballLeftLane ? 0.15f : 0.35f;
            if ((ballDone && _quiet > needQuiet && _watch > 0.45f) || _watch > 8f)
            {
                _phase = Phase.Watching;
                _watch = 0f;
                SetBanner(CountStanding());
            }
            else _watch += dt;
        }

        void KeepBallHonest(float dt)
        {
            if (dt < 0f) dt = 0f;
            var p = _ballBody;
            if (_ballLeftLane)
            {
                var pos = p.Position;
                pos.X = _gutterX;
                pos.Z = LaneGeometry.DeckZ - 0.09f + LaneGeometry.BallRadius;
                p.Position = pos;
                p.RenderPosition = pos;
                var v = p.Velocity;
                v.X = 0f;
                v.Z = 0f;
                if (v.Y < 1.5f) v.Y = 1.5f;
                p.Velocity = v;
                p.CollisionEnabled = false;
                if (p.BodyType != BodyType.Kinematic)
                    p.BodyType = BodyType.Kinematic;
                return;
            }

            float edge = LaneGeometry.LaneHalf + 0.01f;
            if (MathF.Abs(p.Position.X) > edge && p.Position.Y < LaneGeometry.HeadPinY - 0.4f)
            {
                _ballLeftLane = true;
                _gutterX = MathF.Sign(p.Position.X) * (LaneGeometry.LaneHalf + LaneGeometry.GutterWidth * 0.55f);
                p.CollisionEnabled = false;
                p.BodyType = BodyType.Kinematic;
                var pos = p.Position;
                pos.X = _gutterX;
                pos.Z = LaneGeometry.DeckZ - 0.09f + LaneGeometry.BallRadius;
                p.Position = pos;
                p.RenderPosition = pos;
                float speed = MathF.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y);
                p.Velocity = new Vector3(0f, MathF.Max(speed, 3.5f), 0f);
                p.AngularVelocity = Vector3.UnitX * -(p.Velocity.Y / LaneGeometry.BallRadius);
                return;
            }

            if (p.Position.Y > LaneGeometry.DeckEndY)
                return;

            float floor = LaneGeometry.DeckZ + LaneGeometry.BallRadius;
            if (p.Position.Z < floor - 0.02f)
            {
                var pos = p.Position;
                pos.Z = floor;
                p.Position = pos;
                p.RenderPosition = pos;
                if (p.Velocity.Z < 0f)
                    p.Velocity = new Vector3(p.Velocity.X, p.Velocity.Y, 0f);
            }

            ApplyHook(p, dt);
        }

        void ApplyHook(PhysicsComponent p, float dt)
        {
            if (_hook <= 0.001f) return;
            if (p.Position.Y < LaneGeometry.BreakStartY || p.Position.Y > LaneGeometry.BreakEndY) return;
            if (MathF.Abs(p.Position.X) > LaneGeometry.LaneHalf - 0.16f) return;
            Vector3 v = p.Velocity;
            v.Z = 0f;
            if (v.LengthSquared() < 1f) return;
            Vector3 fwd = Vector3.Normalize(v);
            Vector3 right = Vector3.Cross(fwd, Vector3.UnitZ);
            float along = Math.Clamp((p.Position.Y - LaneGeometry.BreakStartY) / 8f, 0f, 1f);
            p.Velocity -= right * (LaneGeometry.HookAccel * _hook * along) * dt;
            p.Wake();
        }

        void TickWatch(float dt)
        {
            _watch += dt;
            if (_watch < 1.35f) return;
            Commit();
        }

        bool Quiet()
        {
            if (_ballBody != null && _ballBody.CollisionEnabled)
            {
                if (_ballBody.Velocity.LengthSquared() > 0.20f) return false;
                if (_ballBody.AngularVelocity.LengthSquared() > 1.5f) return false;
            }
            if (!RackQuiet(_pins)) return false;
            return true;
        }

        static bool RackQuiet(Pin[] pins)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                if (!pins[i].Live || pins[i].Body == null) continue;
                var b = pins[i].Body;
                if (!b.CollisionEnabled) continue;
                if (b.Velocity.LengthSquared() > 0.12f) return false;
                if (b.AngularVelocity.LengthSquared() > 0.8f) return false;
            }
            return true;
        }

        void Commit()
        {
            int standing = CountStanding();
            if (_liveAtRelease <= 0)
            {
                _banner = "";
                _bannerUntil = 0f;
                SpawnAimBall();
                _phase = Phase.Aim;
                return;
            }
            int knocked = _liveAtRelease - standing;
            if (knocked < 0) knocked = 0;
            if (knocked > _liveAtRelease) knocked = _liveAtRelease;
            Score.Add(knocked);
            _banner = BannerFor(standing);
            _bannerUntil = _time + 1.8f;

            bool cleared = standing == 0;
            bool secondOfOpen = _frame < 9 && _ballInFrame >= 1;
            bool frameDone = cleared || secondOfOpen || (_frame >= 9 && Score.FrameClosed(9));
            if (frameDone)
                AdvancePlayer();

            if (AllComplete())
            {
                _phase = Phase.Over;
                return;
            }

            if (frameDone)
            {
                _freshRack = true;
                SpawnRack();
            }
            else
            {
                SweepDead();
                _ballInFrame++;
                _freshRack = false;
            }
            SpawnAimBall();
            _phase = Phase.Aim;
        }

        bool AllComplete()
        {
            for (int i = 0; i < _players; i++)
                if (!_scores[i].IsComplete) return false;
            return true;
        }

        void AdvancePlayer()
        {
            if (_players <= 1)
            {
                if (_frame < 9) { _frame++; _ballInFrame = 0; }
                else _ballInFrame++;
                return;
            }
            int next = (_turn + 1) % _players;
            if (next == 0 && _frame < 9)
                _frame++;
            _turn = next;
            _ballInFrame = _frame >= 9 ? Math.Max(0, Score.BallsInFrame(9)) : 0;
        }

        void Throw(float power)
        {
            if (_ballBody == null) return;
            float speed = 6.15f + Math.Clamp(power, 0f, 1f) * 3.55f;
            Vector3 dir = AimDir();
            _thrownFresh = _freshRack;
            _liveAtRelease = CountStanding();
            _ballLeftLane = false;
            _gutterX = 0f;
            _quiet = 0f;
            _watch = 0f;
            float d = LaneGeometry.BallRadius * 2f;
            var body = _ballBody;
            body.Mass = LaneGeometry.BallMass;
            StampBox(body, new Vector3(d, d, d));
            body.BodyType = BodyType.Dynamic;
            Rebuild(body);
            TrySetSphere(body, LaneGeometry.BallRadius);
            body.Position = BallOrigin();
            body.RenderPosition = body.Position;
            body.Rotation = Quaternion.Identity;
            body.Velocity = dir * speed;
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, dir);
            body.AngularVelocity = axis * (speed / LaneGeometry.BallRadius);
            body.Wake();
            body.CollisionEnabled = true;
            _phase = Phase.Rolling;
        }

        Vector3 AimDir()
        {
            float a = _aim;
            return Vector3.Normalize(new Vector3(MathF.Sin(a), MathF.Cos(a), 0f));
        }

        Vector3 BallOrigin()
        {
            return new Vector3(_lateral, LaneGeometry.ReleaseY, LaneGeometry.DeckZ + LaneGeometry.BallRadius + 0.012f);
        }

        void PlaceAimBall()
        {
            if (_ballBody == null || _phase != Phase.Aim) return;
            var o = BallOrigin();
            _ballBody.BodyType = BodyType.Kinematic;
            _ballBody.Position = o;
            _ballBody.RenderPosition = o;
            _ballBody.Rotation = Quaternion.Identity;
            _ballBody.Velocity = Vector3.Zero;
            _ballBody.AngularVelocity = Vector3.Zero;
            _ballBody.CollisionEnabled = true;
        }

        void EnsureColliders()
        {
            if (_staticBodies.Count > 0) return;
            float y0 = LaneGeometry.ApproachY;
            float y1 = LaneGeometry.DeckEndY;
            float length = y1 - y0;
            float midY = (y0 + y1) * 0.5f;
            const float thick = 0.40f;
            for (int lane = 0; lane < LaneGeometry.LaneCount; lane++)
            {
                float ox = LaneGeometry.LaneOrigin(lane);
                // Centre is half a thickness under the wood, so the top face is Z = 0.
                AddBox(
                    new Vector3(ox, midY, -thick * 0.5f),
                    new Vector3(LaneGeometry.LaneHalf * 2f, length, thick));

                // Kickbacks sit outside the pin boxes. No gutter volume: a gutter
                // box that shares the deck edge was swallowing the rack.
                float inner = LaneGeometry.LaneHalf + 0.06f;
                float railW = 0.08f;
                float railY0 = 16.6f;
                float railY1 = LaneGeometry.PitY;
                float railZ0 = -0.10f;
                float railZ1 = 0.55f;
                var rail = new Vector3(railW, railY1 - railY0, railZ1 - railZ0);
                float railY = (railY0 + railY1) * 0.5f;
                float railZ = (railZ0 + railZ1) * 0.5f;
                AddBox(new Vector3(ox - inner - railW * 0.5f, railY, railZ), rail);
                AddBox(new Vector3(ox + inner + railW * 0.5f, railY, railZ), rail);
            }

            float backT = 0.22f;
            float spanL = LaneGeometry.LaneOrigin(0) - 1.2f;
            float spanR = LaneGeometry.LaneOrigin(LaneGeometry.LaneCount - 1) + 1.2f;
            AddBox(
                new Vector3((spanL + spanR) * 0.5f, LaneGeometry.PitY + backT * 0.5f, 0.45f),
                new Vector3(spanR - spanL, backT, 1.4f));
        }

        void AddBox(Vector3 center, Vector3 fullSize)
        {
            var e = new Entity();
            var body = new PhysicsComponent();
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            body.Position = center;
            body.RenderPosition = center;
            body.Rotation = Quaternion.Identity;
            body.Friction = 0.42f;
            body.KineticFriction = 0.28f;
            body.StaticFriction = 0.38f;
            body.Restitution = 0.02f;
            body.RollingResistance = 0.02f;
            StampBox(body, fullSize);
            body.BodyType = BodyType.Static;
            Rebuild(body);
            body.CollisionEnabled = true;
            e.AddComponent(body);
            _server.AddEntity(e);
            _staticBodies.Add(e);
        }

        static Vector3 PinBoxSize => new Vector3(0.10f, 0.10f, LaneGeometry.PinHeight);

        static float PinCenterZ => LaneGeometry.DeckZ + LaneGeometry.PinHeight * 0.5f + 0.008f;

        /// <summary>
        /// Size is the full box in metres. Local bounds are the same box in
        /// centimetres, symmetric about the origin, so RebuildShape's
        /// ObbShape(size * 0.5) and its centimetre path agree and CenterOffset
        /// stays 0. Unset bounds are (0,0,0), which this engine accepts as a
        /// real box — that degenerate shape is what fired the pins off the spots.
        /// </summary>
        static void StampBox(PhysicsComponent body, Vector3 fullSize)
        {
            if (fullSize.X < 0.02f) fullSize.X = 0.02f;
            if (fullSize.Y < 0.02f) fullSize.Y = 0.02f;
            if (fullSize.Z < 0.02f) fullSize.Z = 0.02f;
            body.Size = fullSize;
            Vector3 halfCm = fullSize * 50f;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var t = body.GetType();
                SetVec(t, body, "LocalBoundsMinCm", -halfCm, flags);
                SetVec(t, body, "LocalBoundsMaxCm", halfCm, flags);
            }
            catch
            {
                // Builds without authored bounds still honour Size.
            }
        }

        static void SetVec(Type t, object body, string name, Vector3 value, BindingFlags flags)
        {
            var prop = t.GetProperty(name, flags);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(body, value);
                return;
            }
            var field = t.GetField(name, flags);
            if (field != null) field.SetValue(body, value);
        }

        static void Rebuild(PhysicsComponent body)
        {
            try { body.RebuildShape(null); }
            catch { }
            try { body.RecomputeMassProperties(); }
            catch { }
        }

        void SpawnRack()
        {
            LoadPinPack();
            if (!AdoptScenePins())
            {
                ClearPins();
                SpawnLane(_pins, 0f);
                SpawnLane(_leftPins, -LaneGeometry.LanePitch);
                SpawnLane(_rightPins, LaneGeometry.LanePitch);
            }
            else
            {
                ResetLane(_pins, 0f);
                ResetLane(_leftPins, -LaneGeometry.LanePitch);
                ResetLane(_rightPins, LaneGeometry.LanePitch);
            }
            _freshRack = true;
        }

        void LoadPinPack()
        {
            try
            {
                if (ModelManager.Instance == null) return;
                string path = PinAssets.PackPath("pin_pack");
                if (!string.IsNullOrEmpty(path))
                {
                    var load = ModelManager.Instance.GetType().GetMethod("LoadAnimationPack");
                    if (load != null) load.Invoke(ModelManager.Instance, new object[] { path });
                }
            }
            catch
            {
                // The scene loader already registers packs that are in project.json.
            }
        }

        bool AdoptScenePins()
        {
            var found = new System.Collections.Generic.List<(Entity e, PhysicsComponent b, float x, float y)>();
            var entities = GetEntities();
            if (entities == null) return false;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i] as Entity;
                if (e == null) continue;
                ModelComponent model = e.GetComponent<ModelComponent>();
                if (model == null || model.Key != "pin_pack") continue;
                PhysicsComponent body = e.GetComponent<PhysicsComponent>();
                if (body == null) continue;
                found.Add((e, body, body.Position.X, body.Position.Y));
            }
            if (found.Count < 10) return false;
            found.Sort((a, b) =>
            {
                int c = a.x.CompareTo(b.x);
                return c != 0 ? c : a.y.CompareTo(b.y);
            });
            FillAdopted(_leftPins, found, -LaneGeometry.LanePitch);
            FillAdopted(_pins, found, 0f);
            FillAdopted(_rightPins, found, LaneGeometry.LanePitch);
            return _pins[0].Body != null;
        }

        static void FillAdopted(Pin[] pins, System.Collections.Generic.List<(Entity e, PhysicsComponent b, float x, float y)> found, float laneX)
        {
            var used = new bool[found.Count];
            for (int i = 0; i < pins.Length; i++)
            {
                var spot = LaneGeometry.PinSpot(i, laneX);
                int best = -1;
                float bestD = 0.6f;
                for (int k = 0; k < found.Count; k++)
                {
                    if (used[k]) continue;
                    float dx = found[k].x - spot.X;
                    float dy = found[k].y - spot.Y;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = k; }
                }
                if (best < 0) continue;
                used[best] = true;
                pins[i] = new Pin { Entity = found[best].e, Body = found[best].b, Live = true, Loose = false };
            }
        }

        static void ResetLane(Pin[] pins, float laneX)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                var b = pins[i].Body;
                if (b == null) continue;
                var spot = LaneGeometry.PinSpot(i, laneX);
                spot.Z = PinCenterZ;
                b.Position = spot;
                b.RenderPosition = spot;
                b.Rotation = Quaternion.Identity;
                b.Velocity = Vector3.Zero;
                b.AngularVelocity = Vector3.Zero;
                b.CollisionEnabled = true;
                b.Wake();
            }
        }

        void SpawnLane(Pin[] pins, float laneX)
        {
            var box = PinBoxSize;
            FBXModel mesh = null;
            try
            {
                if (ModelManager.Instance != null && ModelManager.Instance.TryGetModel("pin_pack", out FBXModel loaded))
                    mesh = loaded;
            }
            catch
            {
                mesh = null;
            }
            if (mesh == null) mesh = PinAssets.Pin;
            for (int i = 0; i < pins.Length; i++)
            {
                var spot = LaneGeometry.PinSpot(i, laneX);
                spot.Z = PinCenterZ;
                var e = new Entity { Type = "FBX" };
                e.AddComponent(new ModelComponent
                {
                    Model = mesh,
                    Key = "pin_pack",
                    CastShadows = true,
                    ReceiveShadows = true
                });
                var body = new PhysicsComponent();
                body.UseBoneHitboxes = false;
                body.KeepUpright = false;
                body.Friction = 0.48f;
                body.KineticFriction = 0.36f;
                body.StaticFriction = 0.45f;
                body.Restitution = 0.08f;
                body.RollingResistance = 0.08f;
                body.LinearDamping = 0.12f;
                body.AngularDamping = 0.22f;
                body.SleepThreshold = 0.08f;
                body.Mass = LaneGeometry.PinMass;
                StampBox(body, box);
                body.BodyType = BodyType.Dynamic;
                try { body.RebuildShape(mesh); }
                catch { Rebuild(body); }
                body.Position = spot;
                body.RenderPosition = spot;
                body.Rotation = Quaternion.Identity;
                body.Velocity = Vector3.Zero;
                body.AngularVelocity = Vector3.Zero;
                body.CollisionEnabled = true;
                body.Wake();
                e.AddComponent(body);
                _server.AddEntity(e);
                pins[i] = new Pin { Entity = e, Body = body, Live = true, Loose = false };
            }
        }

        void SeatWildPins()
        {
            bool ballAtDeck = _phase == Phase.Rolling
                && _ballBody != null
                && _ballBody.Position.Y > LaneGeometry.HeadPinY - 2.2f;
            if (ballAtDeck) return;
            Seat(_pins, 0f);
            Seat(_leftPins, -LaneGeometry.LanePitch);
            Seat(_rightPins, LaneGeometry.LanePitch);
        }

        static void Seat(Pin[] pins, float laneX)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                var b = pins[i].Body;
                if (b == null || !pins[i].Live) continue;
                var spot = LaneGeometry.PinSpot(i, laneX);
                spot.Z = PinCenterZ;
                Vector3 p = b.Position;
                Vector3 d = p - spot;
                bool wild = float.IsNaN(p.X) || p.Z < LaneGeometry.DeckZ - 0.02f || p.Z > 1.4f
                    || d.X * d.X + d.Y * d.Y > 0.35f;
                if (!wild) continue;
                b.Position = spot;
                b.RenderPosition = spot;
                b.Rotation = Quaternion.Identity;
                b.Velocity = Vector3.Zero;
                b.AngularVelocity = Vector3.Zero;
                b.CollisionEnabled = true;
                b.Wake();
            }
        }

        void SpawnAimBall()
        {
            if (_ball != null) _server.RemoveEntity(_ball.Id);
            var e = new Entity();
            var body = new PhysicsComponent();
            float d = LaneGeometry.BallRadius * 2f;
            body.Size = new Vector3(d, d, d);
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            var o = BallOrigin();
            body.Position = o;
            body.RenderPosition = o;
            body.Friction = 0.22f;
            body.KineticFriction = 0.16f;
            body.StaticFriction = 0.20f;
            body.Restitution = 0.03f;
            body.RollingResistance = 0.012f;
            body.LinearDamping = 0.025f;
            body.AngularDamping = 0.06f;
            body.SleepThreshold = 0.05f;
            body.Mass = LaneGeometry.BallMass;
            StampBox(body, new Vector3(d, d, d));
            body.BodyType = BodyType.Kinematic;
            Rebuild(body);
            if (!TrySetSphere(body, LaneGeometry.BallRadius))
                Rebuild(body);
            body.Velocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            body.CollisionEnabled = true;
            e.AddComponent(body);
            _server.AddEntity(e);
            _ball = e;
            _ballBody = body;
        }

        void KeepBallSphere()
        {
            if (_ballBody == null || _ballLeftLane) return;
            if (_ballBody.Shape is SphereShape) return;
            TrySetSphere(_ballBody, LaneGeometry.BallRadius);
        }

        static bool TrySetSphere(PhysicsComponent body, float radius)
        {
            try
            {
                PropertyInfo prop = typeof(PhysicsComponent).GetProperty("Shape");
                MethodInfo setter = prop?.GetSetMethod(true);
                if (setter == null) return false;
                setter.Invoke(body, new object[] { new SphereShape(radius) });
                body.RecomputeMassProperties();
                return body.Shape is SphereShape;
            }
            catch
            {
                return false;
            }
        }

        void SweepDead()
        {
            int slot = 0;
            for (int i = 0; i < _pins.Length; i++)
            {
                if (!_pins[i].Live || _pins[i].Body == null) continue;
                if (Standing(_pins[i].Body)) continue;
                Park(_pins[i].Body, slot++);
                _pins[i].Live = false;
            }
        }

        void BuryFallen()
        {
            BuryRack(_pins);
            BuryRack(_leftPins);
            BuryRack(_rightPins);
            if (_ballBody != null && _ballBody.Position.Z < -1.2f)
            {
                _ballBody.CollisionEnabled = false;
                _ballBody.Velocity = Vector3.Zero;
                _ballBody.AngularVelocity = Vector3.Zero;
                _ballBody.IsSleeping = true;
            }
        }

        static void BuryRack(Pin[] pins)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                var b = pins[i].Body;
                if (b == null || !b.CollisionEnabled) continue;
                if (b.Position.Z > -1.2f) continue;
                b.CollisionEnabled = false;
                b.Velocity = Vector3.Zero;
                b.AngularVelocity = Vector3.Zero;
                b.IsSleeping = true;
                var p = b.Position;
                p.Z = -0.55f;
                b.Position = p;
                b.RenderPosition = p;
            }
        }

        static void Park(PhysicsComponent body, int slot)
        {
            body.CollisionEnabled = false;
            body.Velocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            body.IsSleeping = true;
            float x = -0.7f + (slot % 5) * 0.28f;
            float y = 20.15f + (slot / 5) * 0.35f;
            var p = new Vector3(x, y, LaneGeometry.DeckZ - 0.22f);
            body.Position = p;
            body.RenderPosition = p;
        }

        int CountStanding()
        {
            int n = 0;
            for (int i = 0; i < _pins.Length; i++)
            {
                if (!_pins[i].Live || _pins[i].Body == null) continue;
                if (Standing(_pins[i].Body)) n++;
            }
            return n;
        }

        static bool Standing(PhysicsComponent body)
        {
            if (!body.CollisionEnabled) return false;
            if (MathF.Abs(body.Position.X) > LaneGeometry.LaneHalf - 0.02f) return false;
            if (body.Position.Y < 16.6f || body.Position.Y > LaneGeometry.DeckEndY + 0.05f) return false;
            if (body.Position.Z < LaneGeometry.DeckZ + 0.05f) return false;
            Vector3 up = Vector3.Transform(Vector3.UnitZ, body.Rotation);
            return up.Z > 0.68f;
        }

        void ClearPins()
        {
            ClearPinArray(_pins);
            ClearPinArray(_leftPins);
            ClearPinArray(_rightPins);
        }

        void ClearPinArray(Pin[] pins)
        {
            if (_server == null) return;
            for (int i = 0; i < pins.Length; i++)
            {
                if (pins[i].Entity != null)
                    _server.RemoveEntity(pins[i].Entity.Id);
                pins[i] = default;
            }
        }

        void ClearDynamics()
        {
            ClearPins();
            if (_ball != null && _server != null)
                _server.RemoveEntity(_ball.Id);
            _ball = null;
            _ballBody = null;
        }

        void BuildLive()
        {
            _live.Clear();
            DrawRack(_pins, 0f);
            DrawRack(_leftPins, -LaneGeometry.LanePitch);
            DrawRack(_rightPins, LaneGeometry.LanePitch);
            if (_ballBody != null)
            {
                var ball = _ballBody.RenderPosition;
                LaneGeometry.AddCastShadow(_live, ball, 0.13f);
                LaneGeometry.AddBall(_live, ball, _ballBody.Rotation);
            }
            if (_phase == Phase.Aim)
            {
                LaneGeometry.AddStance(_live, _lateral);
                float speed = _charging ? 6.15f + Math.Clamp(_power, 0f, 1f) * 3.55f : 7.8f;
                LaneGeometry.AddHookPath(_live, BallOrigin(), _aim, _hook, speed);
            }
        }

        void DrawRack(Pin[] pins, float laneX)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                var spot = LaneGeometry.PinSpot(i, laneX);
                spot.Z = PinCenterZ;
                var b = pins[i].Body;
                Vector3 pos = spot;
                Quaternion rot = Quaternion.Identity;
                if (b != null)
                {
                    Vector3 p = b.Position;
                    Vector3 d = p - spot;
                    bool sane = !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsNaN(p.Z)
                        && p.Z > -0.05f && p.Z < 2.5f
                        && d.X * d.X + d.Y * d.Y < 4f;
                    if (sane)
                    {
                        pos = b.RenderPosition;
                        if (float.IsNaN(pos.X) || (pos - p).LengthSquared() > 0.25f)
                            pos = p;
                        rot = b.Rotation;
                    }
                }
                LaneGeometry.AddCastShadow(_live, pos, 0.09f);
                LaneGeometry.AddPin(_live, pos, rot);
            }
        }

        void AimCamera(float dt)
        {
            if (_phase == Phase.Menu)
            {
                MenuView(out Vector3 menuEye, out Vector3 menuAt);
                float k0 = 1f - MathF.Exp(-3.2f * MathF.Max(dt, 0.001f));
                _camEye = Vector3.Lerp(_camEye, menuEye, k0);
                _camTarget = Vector3.Lerp(_camTarget, menuAt, k0);
                return;
            }

            Vector3 ball = _ballBody != null ? _ballBody.RenderPosition : BallOrigin();
            // Stay behind the ball, on the lane. The house wall behind the
            // approach is Y -5.8..-5.15. The pit wall is Y 21. Neither is a camera.
            float eyeY = ball.Y - 4.6f;
            if (eyeY < -2.7f) eyeY = -2.7f;
            if (eyeY > 6.4f) eyeY = 6.4f;
            float eyeX = ball.X * 0.2f;
            if (eyeX > 0.32f) eyeX = 0.32f;
            if (eyeX < -0.32f) eyeX = -0.32f;
            float climb = (eyeY + 2.7f) / (6.4f + 2.7f);
            var eye = new Vector3(eyeX, eyeY, 1.48f - 0.22f * climb);

            float lookY = ball.Y + 4.8f;
            if (lookY > LaneGeometry.HeadPinY + 0.85f) lookY = LaneGeometry.HeadPinY + 0.85f;
            if (lookY < eyeY + 4.2f) lookY = eyeY + 4.2f;
            float lookX = ball.X * 0.45f;
            if (lookX > 0.45f) lookX = 0.45f;
            if (lookX < -0.45f) lookX = -0.45f;
            var target = new Vector3(lookX, lookY, 0.03f);

            float k = 1f - MathF.Exp(-4.2f * MathF.Max(dt, 0.001f));
            _camEye = Vector3.Lerp(_camEye, eye, k);
            _camTarget = Vector3.Lerp(_camTarget, target, k);
        }

        static void MenuView(out Vector3 eye, out Vector3 target)
        {
            // The rear wall is a solid box from Y -5.8 to -5.15 and Z -0.55 to 3.5.
            // A camera inside that box looks at nothing but its own dark faces.
            eye = new Vector3(0.38f, -3.45f, 1.28f);
            target = new Vector3(0f, 16.4f, 0.05f);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        void SetBanner(int standing)
        {
            _banner = BannerFor(standing);
        }

        string BannerFor(int standing)
        {
            int knocked = _liveAtRelease - standing;
            if (knocked < 0) knocked = 0;
            if (_thrownFresh && _liveAtRelease >= 10 && standing == 0) return "STRIKE";
            if (!_thrownFresh && _liveAtRelease > 0 && standing == 0) return "SPARE";
            if (knocked == 0 && _ballLeftLane) return "GUTTER";
            return "";
        }

        void Draw(VertexBuffer buf)
        {
            if (buf == null || buf.GetIndexCount() == 0) return;
            buf.Bind();
            _renderContext.DrawIndexed((int)buf.GetIndexCount());
        }
    }
}
