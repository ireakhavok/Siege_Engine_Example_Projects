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
            public bool Loose;
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
        readonly Pin[] _leftPins = new Pin[10];
        readonly Pin[] _rightPins = new Pin[10];
        Entity _ball;
        PhysicsComponent _ballBody;

        BowlingScore _score = new BowlingScore();
        Phase _phase = Phase.Aim;
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
        }

        public override void Initialize(int width, int height)
        {
            base.Initialize(width, height);
            _renderContext.ClearColor(0.025f, 0.03f, 0.038f, 1f);
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
            _camEye = new Vector3(0.10f, -4.15f, 1.02f);
            _camTarget = new Vector3(0.12f, 2.85f, 0.02f);
            if (!_preview)
                NewGame();
        }

        public override void Update(float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            if (deltaTime > 0.05f) deltaTime = 0.05f;
            KeepBallSphere();
            base.Update(deltaTime);
            _time += deltaTime;
            _renderContext.ClearColor(0.025f, 0.03f, 0.038f, 1f);
            if (_preview || !_houseReady) return;
            if (!_started) NewGame();

            Poll(deltaTime);
            if (_phase == Phase.Rolling)
                TickRoll(deltaTime);
            else if (_phase == Phase.Watching)
                TickWatch(deltaTime);

            BuryFallen();
            AimCamera(deltaTime);
        }

        protected override void GetViewProjection(out Matrix4x4 view, out Matrix4x4 projection)
        {
            float aspect = AspectRatio > 0.01f ? AspectRatio : 16f / 9f;
            if (_preview)
            {
                _camEye = new Vector3(0.85f, -4.35f, 1.72f);
                _camTarget = new Vector3(0f, 11.5f, 0.08f);
            }
            float fov = _preview ? 58f : 52f;
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
                _lateral = Math.Clamp(_lateral + (right - left) * 0.55f * dt, -0.40f, 0.40f);
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
            _gutterX = 0f;
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
            float z = LaneGeometry.DeckZ;
            float gutter = LaneGeometry.GutterWidth;
            for (int lane = 0; lane < LaneGeometry.LaneCount; lane++)
            {
                float ox = LaneGeometry.LaneOrigin(lane);
                AddStatic(LaneGeometry.BoxCollider(
                    ox - LaneGeometry.LaneHalf, ox + LaneGeometry.LaneHalf,
                    LaneGeometry.ApproachY, LaneGeometry.DeckEndY,
                    z - 0.06f, z));
                AddStatic(LaneGeometry.BoxCollider(
                    ox - LaneGeometry.LaneHalf - gutter, ox - LaneGeometry.LaneHalf + 0.01f,
                    0f, LaneGeometry.DeckEndY,
                    z - 0.20f, z - 0.14f));
                AddStatic(LaneGeometry.BoxCollider(
                    ox + LaneGeometry.LaneHalf - 0.01f, ox + LaneGeometry.LaneHalf + gutter,
                    0f, LaneGeometry.DeckEndY,
                    z - 0.20f, z - 0.14f));
                float curb = LaneGeometry.LaneHalf + gutter;
                AddStatic(LaneGeometry.BoxCollider(ox - curb - 0.05f, ox - curb, -0.2f, LaneGeometry.PitY, z - 0.20f, z + 0.06f));
                AddStatic(LaneGeometry.BoxCollider(ox + curb, ox + curb + 0.05f, -0.2f, LaneGeometry.PitY, z - 0.20f, z + 0.06f));
                AddStatic(LaneGeometry.BoxCollider(
                    ox - LaneGeometry.LaneHalf - 0.1f, ox + LaneGeometry.LaneHalf + 0.1f,
                    LaneGeometry.DeckEndY + 0.04f, LaneGeometry.PitY,
                    z - 0.42f, z - 0.30f));
            }
            float span = LaneGeometry.LaneOrigin(0) - 1.3f;
            float spanR = -span;
            AddStatic(LaneGeometry.BoxCollider(span, spanR, LaneGeometry.PitY, LaneGeometry.PitY + 0.18f, z - 0.42f, z + 1.2f));
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
            SpawnLane(_pins, 0f);
            SpawnLane(_leftPins, -LaneGeometry.LanePitch);
            SpawnLane(_rightPins, LaneGeometry.LanePitch);
            _freshRack = true;
        }

        void SpawnLane(Pin[] pins, float laneX)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                var spot = LaneGeometry.PinSpot(i, laneX);
                var e = new Entity();
                var body = new PhysicsComponent();
                body.Size = new Vector3(0.122f, 0.122f, LaneGeometry.PinHeight);
                body.UseBoneHitboxes = false;
                body.KeepUpright = false;
                body.Position = spot;
                body.RenderPosition = spot;
                body.Rotation = Quaternion.Identity;
                body.Friction = 0.55f;
                body.KineticFriction = 0.42f;
                body.StaticFriction = 0.50f;
                body.Restitution = 0.18f;
                body.RollingResistance = 0.04f;
                body.LinearDamping = 0.06f;
                body.AngularDamping = 0.12f;
                body.SleepThreshold = 0.08f;
                body.RebuildShape(_pinModel);
                body.Mass = LaneGeometry.PinMass;
                body.BodyType = BodyType.Dynamic;
                body.Velocity = Vector3.Zero;
                body.AngularVelocity = Vector3.Zero;
                body.IsSleeping = false;
                body.CollisionEnabled = true;
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
                if (b == null || b.Position.Z < -0.4f) continue;
                var spot = b.RenderPosition;
                LaneGeometry.AddCastShadow(_live, spot, 0.09f);
                LaneGeometry.AddPin(_live, spot, b.Rotation);
            }
        }

        void AimCamera(float dt)
        {
            Vector3 ball = _ballBody != null ? _ballBody.RenderPosition : BallOrigin();
            float span = LaneGeometry.HeadPinY - LaneGeometry.ReleaseY;
            float along = 0f;
            if (_phase == Phase.Rolling || _phase == Phase.Watching)
                along = Math.Clamp((ball.Y - LaneGeometry.ReleaseY) / span, 0f, 1f);
            float s = along * along * (3f - 2f * along);

            float eyeY = MathF.Min(
                Lerp(-4.15f, ball.Y - 2.8f, s),
                ball.Y - 2.2f);
            float eyeZ = Lerp(1.02f, 0.78f, s);
            var eye = new Vector3(ball.X * 0.45f, eyeY, eyeZ);
            float lookAhead = Lerp(5.2f, 3.2f, s);
            var target = new Vector3(
                Lerp(ball.X, 0f, 0.25f + 0.45f * s),
                MathF.Min(ball.Y + lookAhead, LaneGeometry.HeadPinY + 0.55f),
                0.02f);
            float k = 1f - MathF.Exp(-5.5f * MathF.Max(dt, 0.001f));
            _camEye = Vector3.Lerp(_camEye, eye, k);
            _camTarget = Vector3.Lerp(_camTarget, target, k);
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

        void BuildHud()
        {
            _hud.Clear();
            float w = _width;
            float h = _height;
            if (w < 80f || h < 64f) return;

            float px = Math.Clamp(h / 460f, 0.9f, 1.45f);
            var panel = new Vector3(0.015f, 0.018f, 0.022f);
            var gold = new Vector3(0.93f, 0.78f, 0.42f);
            var paper = new Vector3(0.90f, 0.88f, 0.82f);
            var dim = new Vector3(0.62f, 0.64f, 0.68f);
            var amber = new Vector3(1f, 0.74f, 0.32f);

            float margin = 8f;
            float totalW = MathF.Max(52f, px * 36f);
            float colW = (w - margin * 2f - totalW - 8f) / 10f;
            float markPx = px * 0.85f;
            if (4f * markPx * 6f > colW - 2f)
                markPx = MathF.Max(0.7f, (colW - 2f) / 24f);

            float top = 6f;
            float rowH = markPx * 8f + 6f;
            BowlingHud.Panel(_hud, margin, top, w - margin * 2f, rowH, panel);
            BowlingHud.Panel(_hud, margin, top, 2f, rowH, gold);

            float x0 = margin + 6f;
            for (int f = 0; f < 10; f++)
            {
                float x = x0 + f * colW;
                if (f == _frame && _phase != Phase.Over)
                    BowlingHud.Panel(_hud, x - 1f, top + 2f, MathF.Max(4f, colW - 4f), rowH - 4f, new Vector3(0.06f, 0.05f, 0.035f));
                string marks = BowlingHud.Marks(Score, f);
                if (string.IsNullOrEmpty(marks))
                    BowlingHud.Text(_hud, (f + 1).ToString(), x, top + 3f, markPx, f == _frame ? amber : dim);
                else
                    BowlingHud.Text(_hud, marks, x, top + 3f, markPx, paper);
            }
            float tx = x0 + 10f * colW;
            BowlingHud.Text(_hud, Score.Total().ToString(), tx, top + 2f, markPx * 1.05f, gold);

            float helpPx = MathF.Min(px, 1.15f);
            string help = "A D BOARDS    MOUSE AIM    Q E CURVE    SPACE    R NEW";
            float helpW = BowlingHud.Measure(help, helpPx);
            if (helpW > w - margin * 2f)
            {
                help = "A D    MOUSE    Q E    SPACE    R";
                helpW = BowlingHud.Measure(help, helpPx);
            }
            float helpY = h - 4f - helpPx * 8f;
            if (helpY > top + rowH + 4f && helpW < w - margin)
                BowlingHud.Text(_hud, help, margin, helpY, helpPx, dim);

            float widgetH = Math.Clamp(h * 0.16f, 36f, 58f);
            float widgetW = Math.Clamp(w * 0.18f, 108f, 150f);
            float widgetY = helpY - 8f - widgetH;
            bool widgets = widgetY > top + rowH + 6f;
            if (widgets)
            {
                DrawPathWidget(margin, widgetY, widgetW, widgetH, px);
                float px0 = margin + widgetW + 14f;
                float barW = Math.Clamp(w * 0.16f, 80f, 140f);
                float barH = MathF.Max(5f, px * 3.2f);
                float barY = widgetY + widgetH - barH;
                if (px0 + barW < w - 80f)
                {
                    string powerLabel = _phase == Phase.Aim
                        ? (_charging ? "POWER" : "HOLD SPACE")
                        : "BALL " + (_ballInFrame + 1);
                    BowlingHud.Text(_hud, powerLabel, px0, widgetY + 2f, px * 0.8f, paper);
                    BowlingHud.Panel(_hud, px0, barY, barW, barH, new Vector3(0.04f, 0.045f, 0.05f));
                    float pf = (_phase == Phase.Aim && _charging ? _power : 0f) * barW;
                    if (pf > 1f)
                    {
                        var bar = new Vector3(0.25f + _power * 0.7f, 0.72f - _power * 0.4f, 0.18f);
                        BowlingHud.Panel(_hud, px0, barY, pf, barH, bar);
                    }
                }
            }

            float lamp = MathF.Max(3f, px * 1.7f);
            float lx = w - margin - 58f;
            float ly = widgets ? widgetY + widgetH * 0.45f : helpY - 36f;
            if (ly > top + rowH + 16f && lx > margin + 220f)
            {
                for (int i = 0; i < 10; i++)
                {
                    var spot = LaneGeometry.PinSpot(i);
                    float pxn = lx + spot.X * 46f;
                    float pyn = ly - (spot.Y - LaneGeometry.HeadPinY) * 34f;
                    bool up = _pins[i].Live && _pins[i].Body != null && Standing(_pins[i].Body);
                    BowlingHud.Panel(_hud, pxn, pyn, lamp, lamp, up ? paper : new Vector3(0.28f, 0.10f, 0.10f));
                }
            }

            if (!string.IsNullOrEmpty(_banner) && (_phase != Phase.Aim || _time < _bannerUntil))
            {
                float bannerPx = Math.Clamp(px * 1.35f, 1.1f, 2.1f);
                float bw = BowlingHud.Measure(_banner, bannerPx);
                float bx = (w - bw) * 0.5f;
                float bby = h * 0.28f;
                float bh = bannerPx * 7f + 8f;
                float limit = widgets ? widgetY - 6f : helpY - 8f;
                if (bby > top + rowH + 8f && bby + bh < limit)
                {
                    BowlingHud.Panel(_hud, bx - 6f, bby - 3f, bw + 12f, bh, new Vector3(0.02f, 0.02f, 0.024f));
                    var bc = _banner == "STRIKE" || _banner == "SPARE" ? gold : paper;
                    BowlingHud.Text(_hud, _banner, bx, bby, bannerPx, bc);
                }
            }

            if (_phase == Phase.Over)
            {
                float bannerPx = Math.Clamp(px * 1.5f, 1.2f, 2.2f);
                string over = "GAME OVER";
                float ow = BowlingHud.Measure(over, bannerPx);
                float ox = (w - ow) * 0.5f;
                float oy = h * 0.40f;
                BowlingHud.Panel(_hud, ox - 6f, oy - 3f, ow + 12f, bannerPx * 7f + 8f, new Vector3(0.02f, 0.016f, 0.02f));
                BowlingHud.Text(_hud, over, ox, oy, bannerPx, gold);
            }
        }

        void DrawPathWidget(float x, float y, float w, float h, float px)
        {
            var bg = new Vector3(0.02f, 0.022f, 0.028f);
            var wood = new Vector3(0.42f, 0.30f, 0.16f);
            var channel = new Vector3(0.07f, 0.075f, 0.08f);
            var gold = new Vector3(0.95f, 0.82f, 0.38f);
            var paper = new Vector3(0.86f, 0.84f, 0.78f);
            var dim = new Vector3(0.70f, 0.72f, 0.75f);
            BowlingHud.Panel(_hud, x, y, w, h, bg);
            float pad = 4f;
            float laneL = x + w * 0.28f;
            float laneR = x + w * 0.72f;
            BowlingHud.Panel(_hud, x + pad, y + pad, w - pad * 2f, h - pad * 2f, channel);
            BowlingHud.Panel(_hud, laneL, y + pad, laneR - laneL, h - pad * 2f, wood);

            float speed = _charging ? 6.15f + Math.Clamp(_power, 0f, 1f) * 3.55f : 7.8f;
            float dt = 0.05f;
            var pos = BallOrigin();
            var vel = new Vector3(MathF.Sin(_aim), MathF.Cos(_aim), 0f) * speed;
            bool guttered = false;
            float gutterX = 0f;
            float y0 = LaneGeometry.ReleaseY;
            float y1 = LaneGeometry.HeadPinY;
            float half = LaneGeometry.LaneHalf + LaneGeometry.GutterWidth;
            for (int i = 0; i < 36; i++)
            {
                if (!guttered && MathF.Abs(pos.X) > LaneGeometry.LaneHalf - 0.02f)
                {
                    guttered = true;
                    gutterX = MathF.Sign(pos.X) * (LaneGeometry.LaneHalf + LaneGeometry.GutterWidth * 0.55f);
                    vel.X = 0f;
                }
                if (!guttered && pos.Y > LaneGeometry.BreakStartY && pos.Y < LaneGeometry.BreakEndY)
                {
                    float along = Math.Clamp((pos.Y - LaneGeometry.BreakStartY) / 8f, 0f, 1f);
                    Vector3 fwd = Vector3.Normalize(vel);
                    Vector3 right = Vector3.Cross(fwd, Vector3.UnitZ);
                    vel -= right * (LaneGeometry.HookAccel * _hook * along) * dt;
                }
                if (guttered) pos.X = gutterX;
                pos += new Vector3(vel.X, vel.Y, 0f) * dt;
                if (pos.Y > y1) break;
                if ((i & 2) != 0) continue;
                float u = Math.Clamp((pos.Y - y0) / (y1 - y0), 0f, 1f);
                float nx = Math.Clamp(pos.X / half, -1f, 1f);
                float sx = x + w * 0.5f + nx * (w * 0.5f - pad - 2f);
                float sy = y + h - pad - 2f - u * (h - pad * 2f - 4f);
                var c = guttered ? new Vector3(0.75f, 0.14f, 0.12f) : gold;
                BowlingHud.Panel(_hud, sx, sy, MathF.Max(2f, px), MathF.Max(2f, px), c);
            }

            float feet = Math.Clamp(_lateral / half, -1f, 1f);
            float fx = x + w * 0.5f + feet * (w * 0.5f - pad - 2f);
            BowlingHud.Panel(_hud, fx - 1.5f, y + h - pad - 5f, 3f, 3f, paper);
            BowlingHud.Text(_hud, "Q", x + 1f, y + h * 0.42f, px * 0.7f, dim);
            BowlingHud.Text(_hud, "E", x + w - px * 5f, y + h * 0.42f, px * 0.7f, dim);
        }

        void Draw(VertexBuffer buf)
        {
            if (buf == null || buf.GetIndexCount() == 0) return;
            buf.Bind();
            _renderContext.DrawIndexed((int)buf.GetIndexCount());
        }
    }
}
