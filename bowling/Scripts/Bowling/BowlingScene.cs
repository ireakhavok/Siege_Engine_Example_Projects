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
using SiegeEngine.Scenes;

namespace BowlingProject
{
    [CustomSceneEntry]
    public sealed class BowlingScene : Scene
    {
        enum Phase { Aim, Rolling, Watching, Over }

        struct Pin
        {
            public Entity Entity;
            public PhysicsComponent Body;
            public bool Live;
        }

        readonly bool _preview;
        readonly bool _panel;

        ShaderProgram _shader;
        VertexBuffer _houseBuf;
        VertexBuffer _liveBuf;
        VertexBuffer _hudBuf;
        BowlMesh _house;
        BowlMesh _live;
        BowlMesh _hud;
        FBXModel _pinModel;
        bool _houseReady;
        bool _started;

        readonly List<Entity> _staticBodies = new List<Entity>();
        readonly Pin[] _pins = new Pin[10];
        Entity _ball;
        PhysicsComponent _ballBody;

        BowlingScore _score = new BowlingScore();
        Phase _phase = Phase.Aim;
        int _frame;
        int _ballInFrame;
        bool _freshRack = true;
        bool _thrownFresh;
        bool _ballLeftLane;
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
        }

        public override void Initialize(int width, int height)
        {
            base.Initialize(width, height);
            _renderContext.ClearColor(0.012f, 0.015f, 0.02f, 1f);
            _shader = ShaderProgram.FromId(_renderContext, ShaderId.Grid);
            _houseBuf = new VertexBuffer(_renderContext);
            _liveBuf = new VertexBuffer(_renderContext);
            _hudBuf = new VertexBuffer(_renderContext);
            _house = new BowlMesh();
            _live = new BowlMesh();
            _hud = new BowlMesh();
            LaneGeometry.BuildHouse(_house, _preview);
            _houseBuf.UpdateCustomWithUV(_house.Vertices, _house.Indices);
            _houseReady = true;
            if (!_preview)
                NewGame();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (deltaTime < 0f) deltaTime = 0f;
            if (deltaTime > 0.05f) deltaTime = 0.05f;
            _time += deltaTime;
            _renderContext.ClearColor(0.012f, 0.015f, 0.02f, 1f);
            if (_preview || !_houseReady) return;
            if (!_started) NewGame();

            Poll(deltaTime);
            if (_phase == Phase.Rolling)
                TickRoll(deltaTime);
            else if (_phase == Phase.Watching)
                TickWatch(deltaTime);

            BuryFallen();
        }

        protected override void GetViewProjection(out Matrix4x4 view, out Matrix4x4 projection)
        {
            float aspect = AspectRatio > 0.01f ? AspectRatio : 16f / 9f;
            projection = Matrix4x4.CreatePerspectiveFieldOfView(50f * MathF.PI / 180f, aspect, 0.05f, 80f);
            if (_preview || _ballBody == null)
            {
                _camEye = new Vector3(1.65f, -3.1f, 2.05f);
                _camTarget = new Vector3(0f, 11.5f, 0.35f);
            }
            else
            {
                Vector3 dir = AimDir();
                Vector3 ball = _ballBody.RenderPosition;
                Vector3 back = new Vector3(-dir.X, -dir.Y, 0f);
                Vector3 right = Vector3.Cross(dir, Vector3.UnitZ);
                Vector3 chaseEye = ball + back * 2.45f + right * 0.42f + new Vector3(0f, 0f, 1.38f);
                Vector3 chaseTarget = ball + dir * 5.5f + new Vector3(0f, 0f, 0.12f);
                float deck = Math.Clamp((ball.Y - 11f) / 6f, 0f, 1f);
                deck = deck * deck * (3f - 2f * deck);
                var holdEye = new Vector3(1.15f, 13.4f, 1.85f);
                var holdTarget = new Vector3(0f, 18.7f, 0.32f);
                _camEye = Vector3.Lerp(chaseEye, holdEye, deck);
                _camTarget = Vector3.Lerp(chaseTarget, holdTarget, deck);
            }
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
                BuildHud();
                if (_hud.Indices.Count > 0 && _width > 0 && _height > 0)
                {
                    _hudBuf.UpdateCustomWithUV(_hud.Vertices, _hud.Indices);
                    _renderContext.SetDepthTest(false);
                    _renderContext.SetDepthWrite(false);
                    var ortho = Matrix4x4.CreateOrthographicOffCenter(0f, _width, _height, 0f, -1f, 1f);
                    _renderContext.SetConstants(ConstantSlot.Frame, new FrameCB
                    {
                        View = Matrix4x4.Identity,
                        Projection = ortho,
                        HasTexture = 0,
                        Time = _time
                    });
                    Draw(_hudBuf);
                    _renderContext.SetDepthTest(true);
                    _renderContext.SetDepthWrite(true);
                }
            }

            _renderContext.SetCull(GpuCullMode.Back);
        }

        public override void Dispose()
        {
            _shader?.Dispose();
            _houseBuf?.Dispose();
            _liveBuf?.Dispose();
            _hudBuf?.Dispose();
            base.Dispose();
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
                _lateral = Math.Clamp(_lateral + (right - left) * 0.42f * dt, -0.38f, 0.38f);
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
                    _aim = n * 0.20f;
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
            _score = new BowlingScore();
            _frame = 0;
            _ballInFrame = 0;
            _freshRack = true;
            _banner = "";
            _bannerUntil = 0f;
            _phase = Phase.Aim;
            _started = true;
            SpawnRack();
            SpawnAimBall();
        }

        BowlingScore Score => _score;

        void TickRoll(float dt)
        {
            if (_ballBody == null) { _phase = Phase.Watching; _watch = 0f; _quiet = 0f; return; }
            var p = _ballBody;
            if (!_ballLeftLane && (MathF.Abs(p.Position.X) > LaneGeometry.LaneHalf - 0.02f || p.Position.Z < LaneGeometry.DeckZ))
                _ballLeftLane = true;

            if (!_ballLeftLane && p.Position.Y < 16.8f && p.Position.Z < LaneGeometry.DeckZ + LaneGeometry.BallRadius + 0.06f)
            {
                Vector3 v = p.Velocity;
                v.Z = 0f;
                if (v.LengthSquared() > 1f)
                {
                    Vector3 fwd = Vector3.Normalize(v);
                    Vector3 right = Vector3.Cross(fwd, Vector3.UnitZ);
                    float along = Math.Clamp((p.Position.Y - 7f) / 8f, 0f, 1f);
                    p.Velocity -= right * (_hook * 0.55f * along) * dt;
                    p.Wake();
                }
            }

            if (Quiet()) _quiet += dt;
            else _quiet = 0f;
            bool ballDone = _ballLeftLane || p.Position.Y > LaneGeometry.DeckEndY || p.Velocity.Length() < 0.25f || p.IsSleeping;
            if ((ballDone && _quiet > 0.35f && _watch > 0.55f) || _watch > 8f)
            {
                _phase = Phase.Watching;
                _watch = 0f;
                SetBanner(CountStanding());
            }
            else _watch += dt;
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
            for (int i = 0; i < _pins.Length; i++)
            {
                if (!_pins[i].Live || _pins[i].Body == null) continue;
                var b = _pins[i].Body;
                if (!b.CollisionEnabled) continue;
                if (b.Velocity.LengthSquared() > 0.12f) return false;
                if (b.AngularVelocity.LengthSquared() > 0.8f) return false;
            }
            return true;
        }

        void Commit()
        {
            int standing = CountStanding();
            int knocked = _liveAtRelease - standing;
            if (knocked < 0) knocked = 0;
            if (knocked > 10) knocked = 10;
            Score.Add(knocked);

            if (standing == 0 && _thrownFresh) _banner = "STRIKE";
            else if (standing == 0) _banner = "SPARE";
            else if (knocked == 0) _banner = _ballLeftLane ? "GUTTER" : "";
            else _banner = "";
            _bannerUntil = _time + 1.8f;

            if (Score.IsComplete)
            {
                _phase = Phase.Over;
                return;
            }

            bool cleared = standing == 0;
            bool secondOfOpen = _frame < 9 && _ballInFrame >= 1;
            if (cleared || secondOfOpen)
            {
                if (_frame < 9)
                {
                    _frame++;
                    _ballInFrame = 0;
                }
                else _ballInFrame++;
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

        void Throw(float power)
        {
            if (_ballBody == null) return;
            float speed = 6.15f + Math.Clamp(power, 0f, 1f) * 3.55f;
            Vector3 dir = AimDir();
            _thrownFresh = _freshRack;
            _liveAtRelease = CountStanding();
            _ballLeftLane = false;
            _quiet = 0f;
            _watch = 0f;
            var body = _ballBody;
            body.BodyType = BodyType.Dynamic;
            body.Mass = LaneGeometry.BallMass;
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
            return new Vector3(_lateral, -2.15f, LaneGeometry.DeckZ + LaneGeometry.BallRadius);
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
            float z = LaneGeometry.DeckZ;
            AddStatic(LaneGeometry.BoxCollider(-LaneGeometry.LaneHalf, LaneGeometry.LaneHalf, LaneGeometry.ApproachY, LaneGeometry.DeckEndY, z - 0.06f, z));
            AddStatic(LaneGeometry.BoxCollider(-0.98f, -LaneGeometry.LaneHalf + 0.02f, 0f, LaneGeometry.DeckEndY, z - 0.12f, z - 0.085f));
            AddStatic(LaneGeometry.BoxCollider(LaneGeometry.LaneHalf - 0.02f, 0.98f, 0f, LaneGeometry.DeckEndY, z - 0.12f, z - 0.085f));
            AddStatic(LaneGeometry.BoxCollider(-1.08f, -0.90f, -1.0f, LaneGeometry.PitY, z - 0.12f, z + 0.50f));
            AddStatic(LaneGeometry.BoxCollider(0.90f, 1.08f, -1.0f, LaneGeometry.PitY, z - 0.12f, z + 0.50f));
            AddStatic(LaneGeometry.BoxCollider(-1.05f, 1.05f, LaneGeometry.DeckEndY + 0.06f, LaneGeometry.PitY, z - 0.42f, z - 0.32f));
            AddStatic(LaneGeometry.BoxCollider(-1.1f, 1.1f, LaneGeometry.PitY, LaneGeometry.PitY + 0.16f, z - 0.42f, z + 1.1f));
        }

        void AddStatic(FBXModel model)
        {
            var e = new Entity();
            var body = new PhysicsComponent();
            body.UseBoneHitboxes = false;
            body.KeepUpright = false;
            body.Position = Vector3.Zero;
            body.RenderPosition = Vector3.Zero;
            body.Friction = 0.45f;
            body.KineticFriction = 0.30f;
            body.StaticFriction = 0.40f;
            body.Restitution = 0.02f;
            body.RollingResistance = 0.02f;
            body.RebuildShape(model);
            body.BodyType = BodyType.Static;
            e.AddComponent(body);
            _server.AddEntity(e);
            _staticBodies.Add(e);
        }

        void SpawnRack()
        {
            ClearPins();
            if (_pinModel == null) _pinModel = LaneGeometry.PinCollider();
            for (int i = 0; i < 10; i++)
            {
                var spot = LaneGeometry.PinSpot(i);
                var e = new Entity();
                var body = new PhysicsComponent();
                body.Size = new Vector3(0.122f, 0.122f, LaneGeometry.PinHeight);
                body.UseBoneHitboxes = false;
                body.KeepUpright = false;
                body.Position = spot;
                body.RenderPosition = spot;
                body.Rotation = Quaternion.Identity;
                body.Friction = 0.62f;
                body.KineticFriction = 0.45f;
                body.StaticFriction = 0.55f;
                body.Restitution = 0.34f;
                body.RollingResistance = 0.05f;
                body.LinearDamping = 0.08f;
                body.AngularDamping = 0.16f;
                body.SleepThreshold = 0.12f;
                body.RebuildShape(_pinModel);
                body.Mass = LaneGeometry.PinMass;
                body.BodyType = BodyType.Dynamic;
                body.Wake();
                e.AddComponent(body);
                _server.AddEntity(e);
                _pins[i] = new Pin { Entity = e, Body = body, Live = true };
            }
            _freshRack = true;
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
            if (!TrySetSphere(body, LaneGeometry.BallRadius))
                body.RebuildShape(BallMeshFallback());
            body.Mass = LaneGeometry.BallMass;
            body.BodyType = BodyType.Kinematic;
            e.AddComponent(body);
            _server.AddEntity(e);
            _ball = e;
            _ballBody = body;
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

        static FBXModel BallMeshFallback()
        {
            const int lat = 6;
            const int lon = 10;
            var model = new FBXModel { UnitToMeters = 1f, Skeleton = null };
            var mesh = new MeshData { Name = "Ball" };
            for (int y = 0; y <= lat; y++)
            {
                float v = y / (float)lat;
                float p = MathF.PI * (v - 0.5f);
                float z = MathF.Sin(p) * LaneGeometry.BallRadius;
                float r = MathF.Cos(p) * LaneGeometry.BallRadius;
                for (int x = 0; x < lon; x++)
                {
                    float u = x * (MathF.PI * 2f) / lon;
                    mesh.Vertices.Add(new FBXVertex(MathF.Cos(u) * r, MathF.Sin(u) * r, z, 0f, 0f, 1f, 0f, 0f, 0f));
                }
            }
            for (int y = 0; y < lat; y++)
            {
                for (int x = 0; x < lon; x++)
                {
                    int x1 = (x + 1) % lon;
                    uint a = (uint)(y * lon + x);
                    uint b = (uint)(y * lon + x1);
                    uint c = (uint)((y + 1) * lon + x);
                    uint d = (uint)((y + 1) * lon + x1);
                    mesh.Indices.Add(a); mesh.Indices.Add(b); mesh.Indices.Add(d);
                    mesh.Indices.Add(a); mesh.Indices.Add(d); mesh.Indices.Add(c);
                }
            }
            model.Meshes.Add(mesh);
            return model;
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
            for (int i = 0; i < _pins.Length; i++)
            {
                var b = _pins[i].Body;
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
            if (_ballBody != null && _ballBody.Position.Z < -1.2f)
            {
                _ballBody.CollisionEnabled = false;
                _ballBody.Velocity = Vector3.Zero;
                _ballBody.AngularVelocity = Vector3.Zero;
                _ballBody.IsSleeping = true;
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
            for (int i = 0; i < _pins.Length; i++)
            {
                if (_pins[i].Entity != null)
                    _server.RemoveEntity(_pins[i].Entity.Id);
                _pins[i] = default;
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
            for (int i = 0; i < _pins.Length; i++)
            {
                var b = _pins[i].Body;
                if (b == null || !b.IsVisible) continue;
                LaneGeometry.AddPin(_live, b.RenderPosition, b.Rotation);
            }
            if (_ballBody != null && _ballBody.IsVisible)
                LaneGeometry.AddBall(_live, _ballBody.RenderPosition, _ballBody.Rotation);
            if (_phase == Phase.Aim && _ballBody != null)
                LaneGeometry.AddAimDots(_live, BallOrigin(), AimDir());
        }

        void SetBanner(int standing)
        {
            int knocked = _liveAtRelease - standing;
            if (knocked < 0) knocked = 0;
            if (standing == 0 && _thrownFresh) _banner = "STRIKE";
            else if (standing == 0) _banner = "SPARE";
            else if (knocked == 0) _banner = _ballLeftLane ? "GUTTER" : "";
            else _banner = "";
        }

        void BuildHud()
        {
            _hud.Clear();
            float w = _width;
            float h = _height;
            if (w < 32f || h < 32f) return;
            float px = MathF.Max(2f, h / 150f);
            var panel = new Vector3(0.015f, 0.02f, 0.03f);
            var gold = new Vector3(0.93f, 0.78f, 0.42f);
            var paper = new Vector3(0.92f, 0.90f, 0.84f);
            var dim = new Vector3(0.55f, 0.58f, 0.62f);
            var amber = new Vector3(1f, 0.72f, 0.28f);

            BowlingHud.Panel(_hud, 12f, 10f, w - 24f, 78f * (px / 2f) + 36f, panel);
            BowlingHud.Panel(_hud, 12f, 10f, w - 24f, 3f, gold);

            float top = 18f;
            BowlingHud.Text(_hud, "FRAME", 22f, top, px, dim);
            float colW = MathF.Min(78f, (w - 280f) / 10f);
            if (colW < 36f) colW = 36f;
            float x0 = 90f;
            for (int f = 0; f < 10; f++)
            {
                float x = x0 + f * colW;
                if (f == _frame && _phase != Phase.Over)
                    BowlingHud.Panel(_hud, x - 4f, top - 4f, colW - 6f, 70f, new Vector3(0.08f, 0.07f, 0.04f));
                string num = (f + 1).ToString();
                BowlingHud.Text(_hud, num, x, top, px * 0.85f, f == _frame ? amber : dim);
                string marks = BowlingHud.Marks(Score, f);
                BowlingHud.Text(_hud, marks, x, top + px * 8f, px, paper);
                int cum = Score.Cumulative(f);
                if (cum >= 0)
                    BowlingHud.Text(_hud, cum.ToString(), x, top + px * 16f, px * 0.9f, gold);
            }

            float tx = w - 168f;
            BowlingHud.Text(_hud, "TOTAL", tx, top, px, dim);
            BowlingHud.Text(_hud, Score.Total().ToString(), tx, top + px * 8f, px * 2.1f, gold);

            float by = h - 64f;
            BowlingHud.Panel(_hud, 16f, by, 280f, 28f, new Vector3(0.02f, 0.02f, 0.025f));
            float fill = (_phase == Phase.Aim && _charging ? _power : 0f) * 268f;
            var bar = new Vector3(0.15f + _power * 0.8f, 0.75f - _power * 0.45f, 0.18f);
            if (fill > 1f) BowlingHud.Panel(_hud, 22f, by + 6f, fill, 16f, bar);
            BowlingHud.Text(_hud, _phase == Phase.Aim ? (_charging ? "POWER" : "HOLD SPACE") : "BALL " + (_ballInFrame + 1), 18f, by - px * 8f, px, paper);

            string hook = "HOOK " + ((int)MathF.Round(_hook * 10f)).ToString();
            BowlingHud.Text(_hud, hook, 310f, by + 6f, px, dim);
            BowlingHud.Text(_hud, "A D LINE    MOUSE AIM    Q E HOOK    R NEW", 16f, h - 22f, px * 0.85f, dim);

            // Ten pin lamps, rack order, lit while that pin is still a live standing pin.
            float lx = w - 150f;
            float ly = h - 118f;
            for (int i = 0; i < 10; i++)
            {
                var spot = LaneGeometry.PinSpot(i);
                float pxn = lx + spot.X * 78f;
                float pyn = ly - (spot.Y - LaneGeometry.HeadPinY) * 70f;
                bool up = _pins[i].Live && _pins[i].Body != null && Standing(_pins[i].Body);
                var c = up ? paper : new Vector3(0.18f, 0.08f, 0.08f);
                BowlingHud.Panel(_hud, pxn, pyn, 8f, 8f, c);
            }

            if (!string.IsNullOrEmpty(_banner) && (_phase != Phase.Aim || _time < _bannerUntil))
            {
                float bw = BowlingHud.Measure(_banner, px * 2.4f);
                float bx = (w - bw) * 0.5f;
                float bby = h * 0.38f;
                BowlingHud.Panel(_hud, bx - 16f, bby - 10f, bw + 32f, px * 2.4f * 7f + 20f, new Vector3(0.02f, 0.02f, 0.025f));
                BowlingHud.Text(_hud, _banner, bx, bby, px * 2.4f, _banner == "STRIKE" || _banner == "SPARE" ? gold : paper);
            }

            if (_phase == Phase.Over)
            {
                string over = "GAME OVER";
                float ow = BowlingHud.Measure(over, px * 2.6f);
                float ox = (w - ow) * 0.5f;
                float oy = h * 0.42f;
                BowlingHud.Panel(_hud, ox - 18f, oy - 12f, ow + 36f, px * 2.6f * 7f + 24f, new Vector3(0.02f, 0.015f, 0.02f));
                BowlingHud.Text(_hud, over, ox, oy, px * 2.6f, gold);
            }
        }

        void Draw(VertexBuffer buf)
        {
            if (buf == null || buf.GetIndexCount() == 0) return;
            buf.Bind();
            _renderContext.DrawIndexed((int)buf.GetIndexCount());
        }
    }
}
