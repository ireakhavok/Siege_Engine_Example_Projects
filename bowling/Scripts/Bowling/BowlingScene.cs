using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using SiegeEngine.Core.AssetParsing.Model;
using SiegeEngine.Core.Definitions;
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
        float _aim;
        float _hook;
        float _power;
        bool _charging;
        bool _rWas;
        bool _qWas;
        bool _eWas;
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
            if (!_panel && _controlContext != null)
            {
                _controlContext.SetWindowSizeCallback(_window, (nint w, int nw, int nh) =>
                {
                    if (nw > 0 && nh > 0)
                        Resize(nw, nh);
                });
            }
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
            MenuBus.Arm(this, _context);
            base.Update(deltaTime);
            _time += deltaTime;
            _renderContext.ClearColor(0.025f, 0.03f, 0.038f, 1f);
            if (!_houseReady) return;
            RememberView();
            MenuBus.Listen(_context?.EventBus);
            MenuBus.Arm(this, _context);
            if (_phase == Phase.Menu)
                PollMenu();
            SyncMenu();
            MenuBus.Arm(this, _context);
            if (_preview) return;
            if (!_started) EnsureWorld();

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

        protected override void GetViewProjection(out Matrix4x4 view, out Matrix4x4 projection)
        {
            float aspect = AspectRatio > 0.01f ? AspectRatio : 16f / 9f;
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
            if (_menuOpened) BowlingMenuHost.Close(_context);
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
            bool q = Down(Key.Q);
            bool e = Down(Key.E);
            if (q && !_qWas) _hook = Math.Clamp(_hook - 0.10f, -1f, 1f);
            if (e && !_eWas) _hook = Math.Clamp(_hook + 0.10f, -1f, 1f);
            if (q) _hook = Math.Clamp(_hook - dt * 0.70f, -1f, 1f);
            if (e) _hook = Math.Clamp(_hook + dt * 0.70f, -1f, 1f);
            _qWas = q;
            _eWas = e;
            if (_phase == Phase.Aim)
            {
                _lateral = Math.Clamp(_lateral + (right - left) * 1.15f * dt, -0.50f, 0.50f);
                ReadAim();
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
            MenuBus.Drain(ref _players, out bool start);
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

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            if (width > 2 && height > 2)
            {
                _viewW = width;
                _viewH = height;
            }
        }

        void RememberView()
        {
            try
            {
                _controlContext.GetWindowSize(_window, out int w, out int h);
                if (w > 2 && h > 2 && (w != _width || h != _height))
                    Resize(w, h);
            }
            catch
            {
            }
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
                if (_viewH < 2f) _viewH = _viewW * 9f / 16f;
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
                    _aim = n * 0.70f;
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

        void SyncMenu()
        {
            if (_context == null) return;
            if (_phase != Phase.Menu)
            {
                if (_menuOpened)
                {
                    BowlingMenuHost.Close(_context);
                    _menuOpened = false;
                    _menuPlayers = 0;
                }
                return;
            }
            if (_menuOpened && _players == _menuPlayers) return;
            if (_menuOpened)
                BowlingMenuHost.Close(_context);
            _menuPlayers = _players;
            BowlingMenuHost.Push(_context, _players);
            _menuOpened = true;
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
            if (_hudOpened)
                BowlingHudHost.Close(_context);
            _hudSig = html;
            string file = BowlingHudContent.WriteLive(
                _scores, _players, _turn, _frame, _ballInFrame, label, shownPower, _charging, _hook, banner);
            BowlingHudHost.Push(_context, html, file, BowlingHudContent.PanelHeight(_players));
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
            var p = _ballBody;
            if (p == null || _ballLeftLane) return;
            if (MathF.Abs(p.Position.X) > LaneGeometry.LaneHalf + 0.01f
                && p.Position.Y < LaneGeometry.HeadPinY - 0.4f)
            {
                _ballLeftLane = true;
                _gutterX = MathF.Sign(p.Position.X) * (LaneGeometry.LaneHalf + LaneGeometry.GutterWidth * 0.55f);
            }
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
            _thrownFresh = _freshRack;
            _liveAtRelease = CountStanding();
            _ballLeftLane = false;
            _gutterX = 0f;
            _quiet = 0f;
            _watch = 0f;
            var body = _ballBody;
            body.Mass = LaneGeometry.BallMass;
            body.BodyType = BodyType.Dynamic;
            SetSphere(body, LaneGeometry.BallRadius);
            body.RecomputeMassProperties();
            body.Position = BallOrigin();
            body.RenderPosition = body.Position;
            body.Rotation = Quaternion.Identity;
            body.Friction = LaneGeometry.BallKinetic;
            body.KineticFriction = LaneGeometry.BallKinetic;
            body.StaticFriction = LaneGeometry.BallStatic;
            LaneGeometry.ReleaseSpin(_aim, _hook, speed, out Vector3 vel, out Vector3 spin);
            body.Velocity = vel;
            body.AngularVelocity = spin;
            body.Wake();
            body.CollisionEnabled = true;
            _phase = Phase.Rolling;
        }

        Vector3 BallOrigin()
        {
            return new Vector3(_lateral, LaneGeometry.ReleaseY, LaneGeometry.DeckZ + LaneGeometry.BallRadius);
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
            for (int lane = 0; lane < LaneGeometry.LaneCount; lane++)
            {
                float ox = LaneGeometry.LaneOrigin(lane);
                float y = y0;
                while (y < y1 - 0.01f)
                {
                    float yb = MathF.Min(y + 2f, y1);
                    float mid = (y + yb) * 0.5f;
                    LaneGeometry.DeckFriction(mid, out float kinetic, out float stat);
                    float x0 = ox - LaneGeometry.LaneHalf;
                    float x1 = ox + LaneGeometry.LaneHalf;
                    AddSurface(LaneGeometry.LaneSlab(x0, x1, y, yb, LaneGeometry.DeckZ), kinetic, stat);
                    y = yb;
                }
                AddGutterMesh(ox, -1);
                AddGutterMesh(ox, 1);
                AddKickback(ox, -1);
                AddKickback(ox, 1);
            }

            float spanL = LaneGeometry.LaneOrigin(0) - 1.2f;
            float spanR = LaneGeometry.LaneOrigin(LaneGeometry.LaneCount - 1) + 1.2f;
            float pit = LaneGeometry.PitY;
            AddSurface(LaneGeometry.Sheet(
                new Vector3(spanL, pit, -0.2f),
                new Vector3(spanR, pit, -0.2f),
                new Vector3(spanR, pit, 1.4f),
                new Vector3(spanL, pit, 1.4f)), 0.28f, 0.38f);
        }

        void AddGutterMesh(float ox, int side)
        {
            float sign = side < 0 ? -1f : 1f;
            float inner = ox + sign * LaneGeometry.LaneHalf;
            float outer = ox + sign * (LaneGeometry.LaneHalf + LaneGeometry.GutterWidth);
            float y0 = LaneGeometry.FoulY;
            float y1 = LaneGeometry.DeckEndY;
            float zFloor = LaneGeometry.DeckZ - LaneGeometry.GutterDepth;
            float x0 = MathF.Min(inner, outer);
            float x1 = MathF.Max(inner, outer);
            AddSurface(LaneGeometry.LaneSlab(x0, x1, y0, y1, zFloor), 0.20f, 0.28f);
            AddSurface(LaneGeometry.Sheet(
                new Vector3(outer, y0, zFloor),
                new Vector3(outer, y1, zFloor),
                new Vector3(outer, y1, LaneGeometry.DeckZ),
                new Vector3(outer, y0, LaneGeometry.DeckZ)), 0.35f, 0.45f);
        }

        void AddKickback(float ox, int side)
        {
            float sign = side < 0 ? -1f : 1f;
            float x = ox + sign * (LaneGeometry.LaneHalf + LaneGeometry.GutterWidth + 0.04f);
            float y0 = 16.6f;
            float y1 = LaneGeometry.PitY;
            AddSurface(LaneGeometry.Sheet(
                new Vector3(x, y0, -0.05f),
                new Vector3(x, y1, -0.05f),
                new Vector3(x, y1, 0.55f),
                new Vector3(x, y0, 0.55f)), 0.35f, 0.45f);
        }

        void AddSurface(FBXModel model, float kinetic, float stat)
        {
            var e = new Entity();
            var body = new PhysicsComponent();
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            body.Position = Vector3.Zero;
            body.RenderPosition = Vector3.Zero;
            body.Rotation = Quaternion.Identity;
            body.Friction = kinetic;
            body.KineticFriction = kinetic;
            body.StaticFriction = stat;
            body.Restitution = LaneGeometry.LaneRestitution;
            body.RollingResistance = 0.02f;
            body.RebuildShape(model);
            body.BodyType = BodyType.Static;
            body.CollisionEnabled = true;
            e.AddComponent(body);
            _server.AddEntity(e);
            _staticBodies.Add(e);
        }

        void SpawnRack()
        {
            ClearPins();
            SpawnLane(_pins, 0f);
            SpawnLane(_leftPins, -LaneGeometry.LanePitch);
            SpawnLane(_rightPins, LaneGeometry.LanePitch);
            _freshRack = true;
        }

        void SpawnLane(Pin[] pins, float laneX)
        {
            var mesh = PinAssets.Pin;
            for (int i = 0; i < pins.Length; i++)
            {
                var spot = LaneGeometry.PinSpot(i, laneX);
                spot.Z = LaneGeometry.DeckZ;
                var e = new Entity();
                var body = new PhysicsComponent();
                body.UseBoneHitboxes = false;
                body.KeepUpright = false;
                body.Friction = 0.48f;
                body.KineticFriction = 0.36f;
                body.StaticFriction = 0.50f;
                body.Restitution = 0.05f;
                body.RollingResistance = 0.06f;
                body.LinearDamping = 0.08f;
                body.AngularDamping = 0.18f;
                body.SleepThreshold = 0.08f;
                body.Mass = LaneGeometry.PinMass;
                body.Size = new Vector3(0.122f, 0.122f, LaneGeometry.PinHeight);
                body.BodyType = BodyType.Dynamic;
                body.RebuildShape(mesh);
                body.Position = spot;
                body.RenderPosition = spot;
                body.Rotation = Quaternion.Identity;
                body.Velocity = Vector3.Zero;
                body.AngularVelocity = Vector3.Zero;
                body.CollisionEnabled = true;
                body.IsSleeping = true;
                e.AddComponent(body);
                _server.AddEntity(e);
                pins[i] = new Pin { Entity = e, Body = body, Live = true, Loose = false };
            }
        }

        void SpawnAimBall()
        {
            if (_ball != null) _server.RemoveEntity(_ball.Id);
            var e = new Entity();
            var body = new PhysicsComponent();
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            var o = BallOrigin();
            body.Position = o;
            body.RenderPosition = o;
            body.Friction = LaneGeometry.BallKinetic;
            body.KineticFriction = LaneGeometry.BallKinetic;
            body.StaticFriction = LaneGeometry.BallStatic;
            body.Restitution = LaneGeometry.BallRestitution;
            body.RollingResistance = LaneGeometry.BallRollingResistance;
            body.LinearDamping = LaneGeometry.BallLinearDamping;
            body.AngularDamping = LaneGeometry.BallAngularDamping;
            body.SleepThreshold = 0.05f;
            body.Mass = LaneGeometry.BallMass;
            body.BodyType = BodyType.Dynamic;
            SetSphere(body, LaneGeometry.BallRadius);
            body.RecomputeMassProperties();
            body.BodyType = BodyType.Kinematic;
            body.Position = o;
            body.RenderPosition = o;
            body.Velocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            body.CollisionEnabled = true;
            e.AddComponent(body);
            _server.AddEntity(e);
            _ball = e;
            _ballBody = body;
        }

        static void SetSphere(PhysicsComponent body, float radius)
        {
            var prop = typeof(PhysicsComponent).GetProperty("Shape");
            var setter = prop?.GetSetMethod(true);
            if (setter == null) return;
            setter.Invoke(body, new object[] { new SphereShape(radius) });
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
            Vector3 center = body.Position + Vector3.Transform(new Vector3(0f, 0f, LaneGeometry.PinHeight * 0.5f), body.Rotation);
            if (MathF.Abs(center.X) > LaneGeometry.LaneHalf - 0.02f) return false;
            if (center.Y < 16.6f || center.Y > LaneGeometry.DeckEndY + 0.05f) return false;
            if (center.Z < LaneGeometry.DeckZ + LaneGeometry.PinHeight * 0.35f) return false;
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
            DrawRack(_pins);
            DrawRack(_leftPins);
            DrawRack(_rightPins);
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

        void DrawRack(Pin[] pins)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                var b = pins[i].Body;
                if (b == null) continue;
                var spot = b.RenderPosition;
                if (float.IsNaN(spot.X) || float.IsNaN(spot.Y) || float.IsNaN(spot.Z))
                    continue;
                if (MathF.Abs(spot.X) > 12f) continue;
                if (spot.Y < -8f || spot.Y > 26f) continue;
                if (spot.Z < -2.2f) continue;
                LaneGeometry.AddCastShadow(_live, spot, 0.09f);
                var up = Vector3.Transform(Vector3.UnitZ, b.Rotation);
                LaneGeometry.AddPin(_live, spot + up * (LaneGeometry.PinHeight * 0.5f), b.Rotation);
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
