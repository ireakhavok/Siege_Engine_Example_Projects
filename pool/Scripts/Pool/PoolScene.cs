using System;
using System.Collections.Generic;
using System.Numerics;
using SiegeEngine.Core.AssetParsing.Model;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.GPU;
using SiegeEngine.Core.GPU.ContextManagement;
using SiegeEngine.Core.GPU.Shaders;
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
        ShaderProgram _shader;
        VertexBuffer _tableBuf;
        VertexBuffer _ballBuf;
        PoolMesh _table;
        PoolMesh _balls;
        readonly List<Entity> _static = new List<Entity>();
        readonly List<PoolBall> _rack = new List<PoolBall>();
        PoolBall _cue;
        Phase _phase = Phase.Menu;
        PoolGame _game = PoolGame.Eight;
        int _players = 2;
        int _turn;
        BallGroup _mine = BallGroup.Open;
        readonly BallGroup[] _groups = new BallGroup[4];
        bool _menuOpen;
        bool _started;
        bool _charging;
        bool _spaceWas;
        float _aim;
        float _power;
        float _time;
        float _still;
        string _status = "Rack";
        int _firstHit;

        public PoolScene(SceneContext context) : base(context)
        {
            _context = context;
        }

        public override void Initialize(int width, int height)
        {
            base.Initialize(width, height);
            _shader = ShaderProgram.FromId(_renderContext, ShaderId.Grid);
            _tableBuf = new VertexBuffer(_renderContext);
            _ballBuf = new VertexBuffer(_renderContext);
            _table = new PoolMesh();
            _balls = new PoolMesh();
            BuildTableMesh();
            _tableBuf.UpdateCustomWithUV(_table.Vertices, _table.Indices);
            PoolMenu.Listen(_context?.EventBus);
        }

        public override void Update(float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            if (deltaTime > 0.05f) deltaTime = 0.05f;
            base.Update(deltaTime);
            _time += deltaTime;
            if (!_started && _server != null)
            {
                _started = true;
                PoolTable.BuildStatic(_server, _static);
                Rack(PoolRules.ObjectBalls(_game));
                _cue = PoolTable.MakeBall(_server, 0, PoolTable.HeadSpot, true);
                _cue.Body.BodyType = BodyType.Kinematic;
                _phase = Phase.Menu;
            }
            if (_phase == Phase.Menu)
            {
                PollMenu();
                return;
            }
            PollShot(deltaTime);
            if (_phase == Phase.Rolling)
                Watch(deltaTime);
        }

        protected override void GetViewProjection(out Matrix4x4 view, out Matrix4x4 projection)
        {
            float aspect = AspectRatio > 0.01f ? AspectRatio : 16f / 9f;
            var eye = new Vector3(0f, -2.4f, 2.1f);
            var at = new Vector3(0f, 0f, PoolTable.SlateZ);
            projection = Matrix4x4.CreatePerspectiveFieldOfView(50f * MathF.PI / 180f, aspect, 0.05f, 40f);
            view = Matrix4x4.CreateLookAt(eye, at, Vector3.UnitZ);
        }

        protected override void RenderContent(IReadOnlyList<Entity> entities, Matrix4x4 view, Matrix4x4 projection)
        {
            if (_shader == null) return;
            _shader.Use();
            _renderContext.SetCull(GpuCullMode.None);
            _renderContext.SetDepthTest(true);
            _renderContext.SetDepthWrite(true);
            _renderContext.SetConstants(ConstantSlot.Frame, new FrameCB
            {
                View = view,
                Projection = projection,
                ViewPos = new Vector4(0f, -2.4f, 2.1f, 1f),
                Time = _time,
                HasTexture = 0
            });
            _renderContext.SetConstants(ConstantSlot.Object, new ObjectCB
            {
                Model = Matrix4x4.Identity,
                NormalMatrix = Matrix4x4.Identity
            });
            Draw(_tableBuf);
            BuildBallMesh();
            if (_balls.Indices.Count > 0)
            {
                _ballBuf.UpdateCustomWithUV(_balls.Vertices, _balls.Indices);
                Draw(_ballBuf);
            }
        }

        void PollMenu()
        {
            if (!_menuOpen)
            {
                PoolMenu.Push(_context);
                _menuOpen = true;
            }
            if (Pressed("D1", "Digit1", "Number1", "Num1")) PoolMenu.Players = 1;
            if (Pressed("D2", "Digit2", "Number2", "Num2")) PoolMenu.Players = 2;
            if (Pressed("D3", "Digit3", "Number3", "Num3")) PoolMenu.Players = 3;
            if (Pressed("D4", "Digit4", "Number4", "Num4")) PoolMenu.Players = 4;
            if (Pressed("E")) PoolMenu.Game = PoolGame.Eight;
            if (Pressed("N")) PoolMenu.Game = PoolGame.Nine;
            if (Pressed("C")) PoolMenu.Game = PoolGame.Cutthroat;
            bool start = PoolMenu.Start || Pressed("Enter", "Return") || Pressed("Space");
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
            NewRack();
        }

        bool Pressed(params string[] names)
        {
            foreach (var name in names)
            {
                if (!Enum.TryParse<Key>(name, true, out var key)) continue;
                if (Down(key)) return true;
            }
            return false;
        }

        void NewRack()
        {
            ClearBalls();
            _turn = 0;
            _mine = BallGroup.Open;
            for (int i = 0; i < 4; i++) _groups[i] = BallGroup.Open;
            int n = PoolRules.ObjectBalls(_game);
            Rack(n);
            _cue = PoolTable.MakeBall(_server, 0, PoolTable.HeadSpot, true);
            _cue.Body.BodyType = BodyType.Kinematic;
            _cue.Body.Velocity = Vector3.Zero;
            _phase = Phase.Aim;
            _status = "Player 1 aim";
            _firstHit = 0;
        }

        void Rack(int count)
        {
            var spot = PoolTable.FootSpot;
            float d = PoolTable.BallRadius * 2.05f;
            int placed = 0;
            for (int row = 0; placed < count; row++)
            {
                for (int col = 0; col <= row && placed < count; col++)
                {
                    int number = placed + 1;
                    float x = spot.X + (col - row * 0.5f) * d;
                    float y = spot.Y + row * d * 0.866f;
                    _rack.Add(PoolTable.MakeBall(_server, number, new Vector3(x, y, spot.Z), false));
                    placed++;
                }
            }
        }

        void PollShot(float dt)
        {
            if (_phase != Phase.Aim && _phase != Phase.Place) return;
            bool left = Down(Key.A) || Down(Key.Left);
            bool right = Down(Key.D) || Down(Key.Right);
            if (left) _aim -= dt;
            if (right) _aim += dt;
            bool space = Down(Key.Space);
            if (_phase == Phase.Place)
            {
                if (left) _cue.Body.Position += new Vector3(-dt * 0.4f, 0f, 0f);
                if (right) _cue.Body.Position += new Vector3(dt * 0.4f, 0f, 0f);
                _cue.Body.RenderPosition = _cue.Body.Position;
                if (space && !_spaceWas) _phase = Phase.Aim;
            }
            else
            {
                if (space) _power = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(_time * 3f));
                if (_charging && !space) Shoot(_power);
                _charging = space;
            }
            _spaceWas = space;
        }

        void Shoot(float power)
        {
            var dir = new Vector3(MathF.Sin(_aim), MathF.Cos(_aim), 0f);
            var body = _cue.Body;
            body.BodyType = BodyType.Dynamic;
            body.Wake();
            body.Velocity = dir * (1.4f + power * 3.2f);
            body.AngularVelocity = Vector3.Zero;
            _firstHit = 0;
            _phase = Phase.Rolling;
            _still = 0f;
            _status = "Player " + (_turn + 1) + " rolling";
        }

        void Watch(float dt)
        {
            NoteFirstHit();
            PocketFallen();
            if (!AllSleeping())
            {
                _still = 0f;
                return;
            }
            _still += dt;
            if (_still < 0.35f && _time < 8f) return;
            Resolve();
        }

        void NoteFirstHit()
        {
            if (_firstHit != 0 || _cue?.Body == null) return;
            foreach (var ball in _rack)
            {
                if (!ball.OnTable || ball.Body == null) continue;
                var d = ball.Body.Position - _cue.Body.Position;
                if (d.LengthSquared() < (PoolTable.BallRadius * 2.1f) * (PoolTable.BallRadius * 2.1f))
                {
                    _firstHit = ball.Number;
                    return;
                }
            }
        }

        void PocketFallen()
        {
            foreach (var ball in _rack)
            {
                if (!ball.OnTable || ball.Body == null) continue;
                if (!PoolTable.InPocket(ball.Body.Position)) continue;
                ball.OnTable = false;
                ball.Body.CollisionEnabled = false;
                ball.Body.Velocity = Vector3.Zero;
                ball.Body.IsSleeping = true;
            }
            if (_cue != null && _cue.OnTable && PoolTable.InPocket(_cue.Body.Position))
            {
                _cue.OnTable = false;
                _cue.Body.CollisionEnabled = false;
                _cue.Body.Velocity = Vector3.Zero;
            }
        }

        bool AllSleeping()
        {
            if (_cue != null && _cue.OnTable && !_cue.Body.IsSleeping && _cue.Body.Velocity.LengthSquared() > 0.002f)
                return false;
            foreach (var ball in _rack)
            {
                if (!ball.OnTable) continue;
                if (!ball.Body.IsSleeping && ball.Body.Velocity.LengthSquared() > 0.002f)
                    return false;
            }
            return true;
        }

        void Resolve()
        {
            bool scratch = _cue == null || !_cue.OnTable;
            bool legal = PoolRules.IsLegalFirstHit(_game, GroupOf(_turn), LowestOnTable(), _firstHit);
            bool scored = false;
            foreach (var ball in _rack)
            {
                if (ball.OnTable || ball.Number == 0) continue;
                if (PoolRules.CountsForShooter(_game, GroupOf(_turn), ball.Number, _turn))
                    scored = true;
            }
            if (_game == PoolGame.Eight && Pocketed(8) && GroupOf(_turn) != BallGroup.Eight)
            {
                _status = "Player " + (_turn + 1) + " lost on the 8";
                _phase = Phase.Over;
                return;
            }
            if (_game == PoolGame.Eight && Pocketed(8) && GroupOf(_turn) == BallGroup.Eight && legal && !scratch)
            {
                _status = "Player " + (_turn + 1) + " wins";
                _phase = Phase.Over;
                return;
            }
            if (_game == PoolGame.Nine && Pocketed(9) && legal && !scratch)
            {
                _status = "Player " + (_turn + 1) + " wins";
                _phase = Phase.Over;
                return;
            }
            if (_game == PoolGame.Cutthroat && RemainingOwner() >= 0 && OnlyOneOwnerLeft())
            {
                _status = "Player " + (RemainingOwner() + 1) + " wins";
                _phase = Phase.Over;
                return;
            }
            if (_game == PoolGame.Eight && _mine == BallGroup.Open)
                AssignGroup();
            bool keep = legal && !scratch && scored;
            if (!keep) _turn = (_turn + 1) % _players;
            RespotCue(scratch);
            _phase = scratch ? Phase.Place : Phase.Aim;
            _status = "Player " + (_turn + 1) + (scratch ? " ball in hand" : " aim");
        }

        void AssignGroup()
        {
            foreach (var ball in _rack)
            {
                if (ball.OnTable || ball.Number == 8) continue;
                var g = PoolRules.GroupOf(ball.Number);
                if (g == BallGroup.Solids || g == BallGroup.Stripes)
                {
                    _groups[_turn] = g;
                    _mine = g;
                    return;
                }
            }
        }

        BallGroup GroupOf(int player)
        {
            if (_game != PoolGame.Eight) return BallGroup.Open;
            if (OnlyEightsLeft(player)) return BallGroup.Eight;
            return _groups[player];
        }

        bool OnlyEightsLeft(int player)
        {
            var mine = _groups[player];
            if (mine != BallGroup.Solids && mine != BallGroup.Stripes) return false;
            foreach (var ball in _rack)
            {
                if (!ball.OnTable) continue;
                if (PoolRules.GroupOf(ball.Number) == mine) return false;
            }
            return true;
        }

        int LowestOnTable()
        {
            int low = 99;
            foreach (var ball in _rack)
            {
                if (ball.OnTable && ball.Number < low) low = ball.Number;
            }
            return low == 99 ? 0 : low;
        }

        bool Pocketed(int number)
        {
            foreach (var ball in _rack)
                if (ball.Number == number && !ball.OnTable) return true;
            return false;
        }

        int RemainingOwner()
        {
            int owner = -1;
            foreach (var ball in _rack)
            {
                if (!ball.OnTable) continue;
                int who = PoolRules.CutthroatOwner(ball.Number);
                if (who < 0) continue;
                if (owner >= 0 && owner != who) return -1;
                owner = who;
            }
            return owner;
        }

        bool OnlyOneOwnerLeft()
        {
            return RemainingOwner() >= 0;
        }

        void RespotCue(bool scratch)
        {
            if (_cue == null) return;
            var spot = PoolTable.HeadSpot;
            _cue.OnTable = true;
            _cue.Body.CollisionEnabled = true;
            _cue.Body.BodyType = BodyType.Kinematic;
            _cue.Body.Velocity = Vector3.Zero;
            _cue.Body.AngularVelocity = Vector3.Zero;
            _cue.Body.Position = scratch ? spot : _cue.Body.Position;
            _cue.Body.RenderPosition = _cue.Body.Position;
            _cue.Body.IsSleeping = true;
        }

        void ClearBalls()
        {
            if (_cue?.Entity != null) _server.RemoveEntity(_cue.Entity.Id);
            foreach (var ball in _rack)
                if (ball.Entity != null) _server.RemoveEntity(ball.Entity.Id);
            _rack.Clear();
            _cue = null;
        }

        void BuildTableMesh()
        {
            float hx = PoolTable.Width * 0.5f;
            float hy = PoolTable.Length * 0.5f;
            float z = PoolTable.SlateZ;
            var felt = new Vector3(0.10f, 0.38f, 0.18f);
            var rail = new Vector3(0.28f, 0.14f, 0.08f);
            _table.Quad(new Vector3(-hx, -hy, z), new Vector3(hx, -hy, z), new Vector3(hx, hy, z), new Vector3(-hx, hy, z), felt);
            _table.Quad(new Vector3(-hx - 0.08f, -hy - 0.08f, z - 0.04f), new Vector3(hx + 0.08f, -hy - 0.08f, z - 0.04f), new Vector3(hx + 0.08f, hy + 0.08f, z - 0.04f), new Vector3(-hx - 0.08f, hy + 0.08f, z - 0.04f), rail);
        }

        void BuildBallMesh()
        {
            _balls.Clear();
            if (_cue != null && _cue.OnTable)
                _balls.Sphere(_cue.Body.RenderPosition, PoolTable.BallRadius, PoolColors.Of(0));
            foreach (var ball in _rack)
            {
                if (!ball.OnTable) continue;
                _balls.Sphere(ball.Body.RenderPosition, PoolTable.BallRadius, PoolColors.Of(ball.Number));
            }
            if (_phase == Phase.Aim && _cue != null)
            {
                var dir = new Vector3(MathF.Sin(_aim), MathF.Cos(_aim), 0f);
                var a = _cue.Body.RenderPosition;
                _balls.Quad(a, a + dir * 0.4f, a + dir * 0.4f + new Vector3(0f, 0f, 0.01f), a + new Vector3(0f, 0f, 0.01f), new Vector3(0.9f, 0.8f, 0.3f));
            }
        }

        bool Down(Key key)
        {
            try { return _controlContext.GetKey(_window, key) == InputAction.Press || _controlContext.GetKey(_window, key) == InputAction.Repeat; }
            catch { return false; }
        }

        void Draw(VertexBuffer buf)
        {
            if (buf == null || buf.GetIndexCount() == 0) return;
            buf.Bind();
            _renderContext.DrawIndexed((int)buf.GetIndexCount());
        }

        public override void Dispose()
        {
            if (_menuOpen) PoolMenu.Close(_context);
            _shader?.Dispose();
            _tableBuf?.Dispose();
            _ballBuf?.Dispose();
            base.Dispose();
        }
    }
}
