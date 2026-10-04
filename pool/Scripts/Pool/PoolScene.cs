using System;
using System.Collections.Generic;
using System.Numerics;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.GPU.ContextManagement;
using SiegeEngine.Core.GPU.Renderers;
using SiegeEngine.Core.Managers;
using SiegeEngine.Core.Physics;
using SiegeEngine.Core.UI;
using SiegeEngine.Scenes;

namespace PoolProject
{
    [CustomSceneEntry]
    public sealed class PoolScene : Scene
    {
        enum Phase { Menu, Place, Aim, Rolling, Over }

        readonly SceneContext _context;
        readonly bool _panel;
        readonly List<Entity> _static = new List<Entity>();
        readonly List<PoolBall> _rack = new List<PoolBall>();
        readonly BallGroup[] _groups = new BallGroup[4];
        PoolBall _cue;
        Entity _stick;
        PhysicsComponent _stickBody;
        Phase _phase = Phase.Menu;
        PoolGame _game = PoolGame.Eight;
        int _players = 2;
        int _turn;
        bool _booted;
        bool _menuOpen;
        bool _charging;
        bool _spaceWas;
        bool _eWas;
        bool _nWas;
        bool _cWas;
        float _aim;
        float _power;
        float _time;
        float _still;
        float _viewW = 1280f;
        float _viewH = 720f;
        int _firstHit;
        int _lowAtShot;
        BallGroup _shotGroup = BallGroup.Open;
        Vector3 _eye = new Vector3(0f, -1.6f, 1.4f);
        Vector3 _look = new Vector3(0f, 0.2f, 0.8f);
        Vector3 _holdEye;
        Vector3 _holdLook;
        string _hud = "";

        public PoolScene(SceneContext context) : base(context)
        {
            _context = context;
            _panel = context != null && context.IsPanelHosted;
        }

        public override void Initialize(int width, int height)
        {
            base.Initialize(width, height);
            _viewW = width;
            _viewH = height;
            string root = PoolAssets.Find(_context);
            PoolMenu.Root = root;
            PoolUi.Root = root;
            PoolAssets.Load(_context, _renderContext);
            PoolMenu.Listen(_context?.EventBus);
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            _viewW = width;
            _viewH = height;
        }

        public override void Update(float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            if (deltaTime > 0.05f) deltaTime = 0.05f;
            base.Update(deltaTime);
            _time += deltaTime;
            if (!_booted && _server != null)
            {
                _booted = true;
                PoolTable.BuildStatic(_server, _static);
                SpawnProp("table", Vector3.Zero, Quaternion.Identity);
                Rack();
            }
            if (_phase == Phase.Menu)
                PollMenu();
            else
                PollPlay(deltaTime);
            PoseStick();
            SyncBodies();
            RefreshHud();
        }

        protected override void GetViewProjection(out Matrix4x4 view, out Matrix4x4 projection)
        {
            float aspect = _viewH > 1f ? _viewW / _viewH : 16f / 9f;
            if (aspect < 0.2f) aspect = 16f / 9f;
            projection = Matrix4x4.CreatePerspectiveFieldOfView(58f * MathF.PI / 180f, aspect, 0.02f, 40f);
            view = Matrix4x4.CreateLookAt(_eye, _look, Vector3.UnitZ);
        }

        protected override void RenderContent(IReadOnlyList<Entity> entities, Matrix4x4 view, Matrix4x4 projection)
        {
            if (_modelRenderer == null) return;
            IReadOnlyList<Entity> list = entities;
            if ((list == null || list.Count == 0) && _server != null)
                list = _server.GetEntities();
            if (list == null || list.Count == 0) return;
            ModelInstanceBatch.RenderAll(_modelRenderer, _renderContext, list, view, projection, _eye);
        }

        void PollMenu()
        {
            AimOverview();
            if (!_menuOpen)
            {
                PoolMenu.Push(_context);
                _menuOpen = true;
            }
            int players = _players;
            if (Edge(ref _eWas, "E")) PoolMenu.Game = PoolGame.Eight;
            if (Edge(ref _nWas, "N")) PoolMenu.Game = PoolGame.Nine;
            if (Edge(ref _cWas, "C")) PoolMenu.Game = PoolGame.Cutthroat;
            if (EdgePlayers(1)) PoolMenu.Players = 1;
            if (EdgePlayers(2)) PoolMenu.Players = 2;
            if (EdgePlayers(3)) PoolMenu.Players = 3;
            if (EdgePlayers(4)) PoolMenu.Players = 4;
            if (PoolMenu.Game != _game || PoolMenu.Players != players)
            {
                _game = PoolMenu.Game;
                _players = PoolMenu.Players;
                if (_game == PoolGame.Cutthroat) _players = 3;
                Rack();
            }
            bool start = PoolMenu.Start || Edge(ref _spaceWas, "Enter", "Return");
            if (!start) return;
            PoolMenu.Start = false;
            _players = PoolMenu.Players;
            _game = PoolMenu.Game;
            if (!PoolRules.Allows(_game, _players))
            {
                _players = 3;
                _game = PoolGame.Cutthroat;
            }
            PoolMenu.Close(_context);
            _menuOpen = false;
            Begin();
        }

        void Begin()
        {
            _turn = 0;
            for (int i = 0; i < 4; i++) _groups[i] = BallGroup.Open;
            Rack();
            _phase = Phase.Aim;
            _aim = 0f;
            _power = 0f;
            _firstHit = 0;
            AimBehindBall();
        }

        void PollPlay(float dt)
        {
            if (Edge(ref _eWas, "R") && _phase != Phase.Rolling)
            {
                Begin();
                return;
            }
            if (_phase == Phase.Over)
            {
                AimOverview();
                if (Edge(ref _spaceWas, "Enter", "Return"))
                {
                    _phase = Phase.Menu;
                    _menuOpen = false;
                    Rack();
                }
                return;
            }
            if (_phase == Phase.Place)
            {
                PlaceCue(dt);
                AimBehindBall();
                if (Pressed("Space") && !_spaceWas)
                    _phase = Phase.Aim;
                _spaceWas = Pressed("Space");
                return;
            }
            if (_phase == Phase.Aim)
            {
                ReadAim(dt);
                AimBehindBall();
                bool down = Pressed("Space") || DownMouse();
                if (down)
                {
                    _charging = true;
                    _power += dt * 0.85f;
                    if (_power > 1f) _power = 0f;
                }
                else if (_charging)
                {
                    Shoot(_power);
                    _charging = false;
                    _power = 0f;
                }
                _spaceWas = down;
                return;
            }
            AimHold();
            NoteFirst();
            PocketFallen();
            if (!Settled())
            {
                _still = 0f;
                return;
            }
            _still += dt;
            if (_still < 0.45f) return;
            Resolve();
        }

        void PlaceCue(float dt)
        {
            if (_cue?.Body == null) return;
            var p = _cue.Body.Position;
            float step = dt * 0.55f;
            if (Down(Key.A) || Down(Key.Left)) p.X -= step;
            if (Down(Key.D) || Down(Key.Right)) p.X += step;
            if (Down(Key.W) || Down(Key.Up)) p.Y += step;
            if (Down(Key.S) || Down(Key.Down)) p.Y -= step;
            float kitchen = -PoolTable.Length * 0.25f;
            if (p.Y > kitchen) p.Y = kitchen;
            p = PoolTable.ClearSpot(p, _rack);
            p.Y = MathF.Min(p.Y, kitchen);
            _cue.Body.Position = p;
            _cue.Body.RenderPosition = p;
            _cue.Body.Velocity = Vector3.Zero;
            _cue.Body.BodyType = BodyType.Kinematic;
        }

        float _mouseAim;

        void ReadAim(float dt)
        {
            if (Down(Key.A) || Down(Key.Left)) _aim -= dt * 1.4f;
            if (Down(Key.D) || Down(Key.Right)) _aim += dt * 1.4f;
            try
            {
                _controlContext.GetCursorPos(_window, out double cx, out double cy);
                float x = (float)cx;
                float w = _viewW;
                if (_panel)
                {
                    Viewport vp = _controlContext.GetCurrentViewport();
                    x -= vp.X;
                    if (vp.Width > 2f) w = vp.Width;
                }
                if (w > 2f)
                {
                    float next = Math.Clamp(x / w * 2f - 1f, -1f, 1f) * MathF.PI;
                    if (MathF.Abs(next - _mouseAim) > 0.004f)
                    {
                        _aim = next;
                        _mouseAim = next;
                    }
                }
            }
            catch
            {
                // The host has not handed over a cursor yet.
            }
        }

        void Shoot(float power)
        {
            if (_cue?.Body == null) return;
            var dir = AimDir();
            foreach (var ball in _rack)
                ball.WasOnTable = ball.OnTable;
            _shotGroup = GroupNow(_turn);
            _lowAtShot = LowestOnTable();
            _firstHit = 0;
            var body = _cue.Body;
            body.BodyType = BodyType.Dynamic;
            body.CollisionEnabled = true;
            body.Wake();
            float speed = 1.15f + Math.Clamp(power, 0.05f, 1f) * 3.35f;
            body.Velocity = dir * speed;
            float roll = 0.62f * speed / PoolTable.BallRadius;
            body.AngularVelocity = new Vector3(dir.Y * roll, -dir.X * roll, 0f);
            foreach (var ball in _rack)
            {
                if (!ball.OnTable || ball.Body == null) continue;
                ball.Body.Wake();
            }
            _holdEye = _eye;
            _holdLook = _look;
            _phase = Phase.Rolling;
            _still = 0f;
        }

        void NoteFirst()
        {
            if (_firstHit != 0 || _cue?.Body == null) return;
            float lim = PoolTable.BallRadius * 2.2f;
            lim *= lim;
            foreach (var ball in _rack)
            {
                if (!ball.OnTable || ball.Body == null) continue;
                var d = ball.Body.Position - _cue.Body.Position;
                if (d.LengthSquared() < lim)
                {
                    _firstHit = ball.Number;
                    return;
                }
            }
        }

        void PocketFallen()
        {
            Pocket(_cue);
            foreach (var ball in _rack)
                Pocket(ball);
        }

        void Pocket(PoolBall ball)
        {
            if (ball == null || !ball.OnTable || ball.Body == null) return;
            if (!PoolTable.Fell(ball.Body.Position)) return;
            var hole = NearestPocket(ball.Body.Position);
            ball.OnTable = false;
            ball.Body.CollisionEnabled = false;
            ball.Body.Velocity = Vector3.Zero;
            ball.Body.AngularVelocity = Vector3.Zero;
            ball.Body.IsSleeping = true;
            var p = new Vector3(hole.X, hole.Y, PoolTable.SlateZ - 0.045f);
            ball.Body.Position = p;
            ball.Body.RenderPosition = p;
        }

        Vector3 NearestPocket(Vector3 p)
        {
            var pockets = PoolTable.Pockets();
            var best = pockets[0];
            float bestD = float.MaxValue;
            foreach (var pocket in pockets)
            {
                float d = (pocket.X - p.X) * (pocket.X - p.X) + (pocket.Y - p.Y) * (pocket.Y - p.Y);
                if (d < bestD)
                {
                    bestD = d;
                    best = pocket;
                }
            }
            return best;
        }

        bool Settled()
        {
            if (Moving(_cue)) return false;
            foreach (var ball in _rack)
                if (Moving(ball)) return false;
            return true;
        }

        static bool Moving(PoolBall ball)
        {
            if (ball == null || !ball.OnTable || ball.Body == null) return false;
            if (ball.Body.IsSleeping) return false;
            return ball.Body.Velocity.LengthSquared() > 0.0008f
                || ball.Body.AngularVelocity.LengthSquared() > 0.02f;
        }

        void Resolve()
        {
            bool scratch = _cue == null || !_cue.OnTable;
            bool legal = PoolRules.IsLegalFirstHit(_game, _shotGroup, _lowAtShot, _firstHit);
            bool scored = false;
            foreach (var ball in _rack)
            {
                if (ball.OnTable || !ball.WasOnTable) continue;
                if (PoolRules.CountsForShooter(_game, _shotGroup, ball.Number, _turn))
                    scored = true;
            }
            if (_game == PoolGame.Eight && Took(8) && _shotGroup == BallGroup.Open)
                Respot(8);
            if (_game == PoolGame.Eight && Took(8))
            {
                _phase = Phase.Over;
                _hud = _shotGroup == BallGroup.Eight && legal && !scratch
                    ? "Player " + (_turn + 1) + " wins"
                    : "Player " + (_turn + 1) + " lost on the 8";
                return;
            }
            if (_game == PoolGame.Nine && Took(9))
            {
                if (legal && !scratch)
                {
                    _phase = Phase.Over;
                    _hud = "Player " + (_turn + 1) + " wins";
                    return;
                }
                Respot(9);
            }
            if (_game == PoolGame.Cutthroat && LastStanding(out int winner))
            {
                _phase = Phase.Over;
                _hud = "Player " + (winner + 1) + " wins";
                return;
            }
            if (_game == PoolGame.Eight && _groups[_turn] == BallGroup.Open && legal && !scratch)
                ClaimGroup();
            bool keep = legal && !scratch && scored;
            if (!keep)
                _turn = (_turn + 1) % _players;
            if (scratch)
                HandBall();
            else
            {
                _phase = Phase.Aim;
                _power = 0f;
            }
            _hud = "";
        }

        void ClaimGroup()
        {
            foreach (var ball in _rack)
            {
                if (ball.OnTable || !ball.WasOnTable) continue;
                var group = PoolRules.GroupOf(ball.Number);
                if (group != BallGroup.Solids && group != BallGroup.Stripes) continue;
                _groups[_turn] = group;
                var other = PoolRules.Opposite(group);
                for (int i = 0; i < _players; i++)
                    if (i != _turn) _groups[i] = other;
                return;
            }
        }

        bool Took(int number)
        {
            foreach (var ball in _rack)
                if (ball.Number == number && ball.WasOnTable && !ball.OnTable) return true;
            return false;
        }

        void Respot(int number)
        {
            foreach (var ball in _rack)
            {
                if (ball.Number != number) continue;
                var spot = PoolTable.ClearSpot(PoolTable.FootSpot, _rack);
                ball.OnTable = true;
                ball.WasOnTable = true;
                ball.Body.CollisionEnabled = true;
                ball.Body.BodyType = BodyType.Kinematic;
                ball.Body.Position = spot;
                ball.Body.RenderPosition = spot;
                ball.Body.Velocity = Vector3.Zero;
                ball.Body.AngularVelocity = Vector3.Zero;
                ball.Body.IsSleeping = true;
                return;
            }
        }

        void HandBall()
        {
            if (_cue?.Body == null) return;
            var spot = PoolTable.ClearSpot(PoolTable.HeadSpot, _rack);
            _cue.OnTable = true;
            _cue.Body.CollisionEnabled = true;
            _cue.Body.BodyType = BodyType.Kinematic;
            _cue.Body.Position = spot;
            _cue.Body.RenderPosition = spot;
            _cue.Body.Velocity = Vector3.Zero;
            _cue.Body.AngularVelocity = Vector3.Zero;
            _cue.Body.IsSleeping = true;
            _phase = Phase.Place;
            _power = 0f;
        }

        bool LastStanding(out int winner)
        {
            winner = -1;
            int alive = 0;
            for (int p = 0; p < _players; p++)
            {
                bool has = false;
                foreach (var ball in _rack)
                {
                    if (!ball.OnTable) continue;
                    if (PoolRules.CutthroatOwner(ball.Number) == p) has = true;
                }
                if (!has) continue;
                alive++;
                winner = p;
            }
            return alive == 1;
        }

        BallGroup GroupNow(int player)
        {
            if (_game != PoolGame.Eight) return _groups[player];
            if (_groups[player] == BallGroup.Open) return BallGroup.Open;
            foreach (var ball in _rack)
            {
                if (!ball.OnTable) continue;
                if (PoolRules.GroupOf(ball.Number) == _groups[player]) return _groups[player];
            }
            return BallGroup.Eight;
        }

        int LowestOnTable()
        {
            int low = 0;
            foreach (var ball in _rack)
            {
                if (!ball.OnTable || ball.Number <= 0) continue;
                if (low == 0 || ball.Number < low) low = ball.Number;
            }
            return low;
        }

        void Rack()
        {
            ClearRack();
            if (_server == null) return;
            foreach (var spot in PoolTable.RackSpots(_game))
            {
                var ball = PoolTable.MakeBall(_server, spot.number, spot.position, false);
                ball.Body.BodyType = BodyType.Dynamic;
                ball.Body.Velocity = Vector3.Zero;
                ball.Body.AngularVelocity = Vector3.Zero;
                ball.Body.IsSleeping = true;
                PoolAssets.Attach(ball.Entity, "ball_" + spot.number.ToString("00"));
                _rack.Add(ball);
            }
            _cue = PoolTable.MakeBall(_server, 0, PoolTable.HeadSpot, true);
            PoolAssets.Attach(_cue.Entity, "ball_00");
            if (_stick != null) _server.RemoveEntity(_stick.Id);
            _stick = SpawnProp("cue", PoolTable.HeadSpot, Quaternion.Identity);
            _stickBody = _stick.GetComponent<PhysicsComponent>();
        }

        void ClearRack()
        {
            if (_server == null) return;
            foreach (var ball in _rack)
                if (ball.Entity != null) _server.RemoveEntity(ball.Entity.Id);
            _rack.Clear();
            if (_cue?.Entity != null) _server.RemoveEntity(_cue.Entity.Id);
            _cue = null;
        }

        Entity SpawnProp(string key, Vector3 position, Quaternion rotation)
        {
            var entity = new Entity();
            var body = new PhysicsComponent();
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            body.Position = position;
            body.RenderPosition = position;
            body.Rotation = rotation;
            body.Mass = 1f;
            body.BodyType = BodyType.Dynamic;
            PoolTable.AssignSphere(body, 0.05f);
            body.BodyType = BodyType.Kinematic;
            body.Position = position;
            body.RenderPosition = position;
            body.Rotation = rotation;
            body.CollisionEnabled = false;
            body.IsSleeping = true;
            entity.AddComponent(body);
            PoolAssets.Attach(entity, key);
            _server.AddEntity(entity);
            return entity;
        }

        void PoseStick()
        {
            if (_stickBody == null) return;
            bool show = _phase == Phase.Aim || _phase == Phase.Place;
            var model = _stick?.GetComponent<ModelComponent>();
            if (model != null) model.CastShadows = show;
            if (!show || _cue?.Body == null)
            {
                var hide = new Vector3(0f, 0f, -2f);
                _stickBody.Position = hide;
                _stickBody.RenderPosition = hide;
                return;
            }
            var dir = AimDir();
            float back = PoolTable.BallRadius + 0.02f + _power * 0.16f;
            var p = _cue.Body.Position - dir * back;
            _stickBody.Position = p;
            _stickBody.RenderPosition = p;
            _stickBody.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -_aim);
        }

        void SyncBodies()
        {
            void Copy(PoolBall ball)
            {
                if (ball?.Body == null) return;
                ball.Body.RenderPosition = ball.Body.Position;
            }
            Copy(_cue);
            foreach (var ball in _rack) Copy(ball);
        }

        void AimBehindBall()
        {
            var ball = _cue?.Body != null ? _cue.Body.Position : PoolTable.HeadSpot;
            var dir = AimDir();
            var side = new Vector3(dir.Y, -dir.X, 0f) * 0.05f;
            _eye = ball - dir * 0.58f + side + new Vector3(0f, 0f, 0.36f);
            _look = ball + dir * 1.35f;
            _look.Z = PoolTable.SlateZ + 0.03f;
        }

        void AimHold()
        {
            _eye = _holdEye;
            _look = _holdLook;
        }

        void AimOverview()
        {
            _eye = new Vector3(0.04f, PoolTable.HeadSpot.Y - 0.95f, PoolTable.SlateZ + 0.72f);
            _look = new Vector3(0f, 0.25f, PoolTable.SlateZ);
        }

        static Vector3 AimDirFrom(float aim)
        {
            return new Vector3(MathF.Sin(aim), MathF.Cos(aim), 0f);
        }

        Vector3 AimDir() => AimDirFrom(_aim);

        void RefreshHud()
        {
            string who;
            string game = _game == PoolGame.Nine ? "9-ball" : _game == PoolGame.Cutthroat ? "Cutthroat" : "8-ball";
            string detail;
            int power = (int)(_power * 100f);
            if (_phase == Phase.Menu)
            {
                who = _players + (_players == 1 ? " player" : " players");
                detail = "1-4 players    E 8-ball    N 9-ball    C cutthroat    Enter";
                power = 0;
            }
            else if (_phase == Phase.Over)
            {
                who = string.IsNullOrEmpty(_hud) ? "Game over" : _hud;
                detail = "Enter menu    R rematch";
                power = 0;
            }
            else if (_phase == Phase.Place)
            {
                who = "Player " + (_turn + 1) + " ball in hand";
                detail = "WASD in the kitchen, then Space";
                power = 0;
            }
            else
            {
                who = "Player " + (_turn + 1);
                detail = GroupLabel(_turn);
                if (_phase == Phase.Rolling) detail = "Rolling";
            }
            PoolUi.Push(_context, who, game, detail, power);
        }

        string GroupLabel(int player)
        {
            if (_game == PoolGame.Nine) return "Hit the " + Math.Max(1, LowestOnTable());
            if (_game == PoolGame.Cutthroat) return "Balls " + CutLabel(player);
            var group = GroupNow(player);
            if (group == BallGroup.Solids) return "Solids";
            if (group == BallGroup.Stripes) return "Stripes";
            if (group == BallGroup.Eight) return "The 8";
            return "Open table";
        }

        static string CutLabel(int player)
        {
            if (player == 0) return "1-5";
            if (player == 1) return "6-10";
            return "11-15";
        }

        bool EdgePlayers(int n)
        {
            string[] names = n switch
            {
                1 => new[] { "D1", "Digit1", "Number1", "Alpha1", "Num1", "Key1", "One" },
                2 => new[] { "D2", "Digit2", "Number2", "Alpha2", "Num2", "Key2", "Two" },
                3 => new[] { "D3", "Digit3", "Number3", "Alpha3", "Num3", "Key3", "Three" },
                _ => new[] { "D4", "Digit4", "Number4", "Alpha4", "Num4", "Key4", "Four" }
            };
            return Edge(ref _playerWas[n - 1], names);
        }

        readonly bool[] _playerWas = new bool[4];

        bool Edge(ref bool was, params string[] names)
        {
            bool down = false;
            foreach (string name in names)
            {
                if (!Enum.TryParse(name, true, out Key key)) continue;
                if (Down(key)) { down = true; break; }
            }
            bool edge = down && !was;
            was = down;
            return edge;
        }

        bool Pressed(params string[] names)
        {
            foreach (string name in names)
            {
                if (!Enum.TryParse(name, true, out Key key)) continue;
                if (Down(key)) return true;
            }
            return false;
        }

        bool Down(Key key)
        {
            try
            {
                var state = _controlContext.GetKey(_window, key);
                return state == InputAction.Press || state == InputAction.Repeat;
            }
            catch
            {
                return false;
            }
        }

        bool DownMouse()
        {
            try
            {
                var state = _controlContext.GetMouseButton(_window, MouseButton.Left);
                return state == InputAction.Press || state == InputAction.Repeat;
            }
            catch
            {
                return false;
            }
        }
    }
}
