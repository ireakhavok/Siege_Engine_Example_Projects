// Folder: CheckersProject (Scripts)
// File: CheckersScene.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Core.Rendering;
using SiegeEngine.Core.Rendering.ContextManagement;
using SiegeEngine.Core.Rendering.Shaders;
using SiegeEngine.Scenes;

namespace CheckersProject
{
    [CustomSceneEntry]
    public sealed class CheckersScene : Scene
    {
        CheckersBoardState _board;
        ShaderProgram _shader;
        VertexBuffer _boardBuffer;
        VertexBuffer _pieceBuffer;
        VertexBuffer _highlightBuffer;

        bool _aiThinking;
        bool _ready;
        double _cursorX;
        double _cursorY;
        bool _leftWasDown;
        CheckersAiDifficulty _difficulty = CheckersAiDifficulty.Normal;

        // When true the scene is hosted inside the Scene Editor as a view-only preview.
        // Input and AI are suppressed; geometry is still rebuilt and drawn.
        readonly bool _isHostedPreview;

        public CheckersScene(SceneContext context)
            : base(
                context.RenderContext,
                context.ControlContext,
                context.Window,
                context.Server,
                context.EventBus)
        {
            _isHostedPreview = context?.IsHostedPreview ?? false;
            Console.WriteLine($"[CheckersScene] Constructed via SceneContext (hostedPreview={_isHostedPreview})");
        }

        public override void Initialize(int width, int height)
        {
            base.Initialize(width, height);

            _renderContext.ClearColor(0.10f, 0.12f, 0.14f, 1.0f);

            _board = CheckersBoardState.CreateStartingPosition();
            _board.Mode = CheckersMode.VsAi;
            _board.HumanColor = CheckersColor.White;

            _shader = new ShaderProgram(_renderContext, SceneShader.VertexShaderSource, SceneShader.FragmentShaderSource);
            _boardBuffer = new VertexBuffer(_renderContext);
            _pieceBuffer = new VertexBuffer(_renderContext);
            _highlightBuffer = new VertexBuffer(_renderContext);
            RebuildMeshes();

            // No global Set*Callback installation. Host owns the window.
            // Play path polls IControlContext; editor path is muted by IsHostedPreview.

            _ready = true;
            Console.WriteLine(_isHostedPreview
                ? "[CheckersScene] Ready (hosted preview — view only)."
                : "[CheckersScene] Ready — white to move.");
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (!_ready || _board == null) return;

            // Hosted preview: keep geometry current, never run input or AI.
            if (_isHostedPreview)
            {
                RebuildMeshes();
                return;
            }

            // Poll input from the host-owned control context (no global callbacks).
            try
            {
                _controlContext.GetCursorPos(_window, out _cursorX, out _cursorY);
                bool leftDown = _controlContext.GetMouseButton(_window, MouseButton.Left) == InputAction.Press;
                if (leftDown && !_leftWasDown)
                    HandleClick(new Vector2((float)_cursorX, (float)_cursorY));
                _leftWasDown = leftDown;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CheckersScene] input poll: {ex.Message}");
            }

            TickAi();
            RebuildMeshes();
        }

        void TickAi()
        {
            if (_board.IsGameOver || _aiThinking) return;
            if (_board.Mode != CheckersMode.VsAi) return;
            if (_board.SideToMove == _board.HumanColor) return;

            _aiThinking = true;
            _board.Phase = CheckersPhase.AiThinking;
            var snap = _board.Clone();
            var difficulty = _difficulty;

            _ = Task.Run(() =>
            {
                try
                {
                    var best = CheckersAI.FindBestMove(snap, difficulty);
                    if (best.HasValue && _board != null && !_board.IsGameOver)
                    {
                        var m = best.Value;
                        if (CheckersRules.TryMove(_board, m.From, m.To, out var rec))
                        {
                            string cap = rec.CapturedSquares.Count > 0 ? $" x{rec.CapturedSquares.Count}" : "";
                            Console.WriteLine($"[CheckersAI] {rec.FromAlg}-{rec.ToAlg}{cap}  score={m.Score}");
                            // Keep AI moving through multi-jumps
                            while (_board.ContinuationFrom.HasValue && !_board.IsGameOver)
                            {
                                var cont = CheckersAI.FindBestMove(_board, difficulty);
                                if (!cont.HasValue) break;
                                if (!CheckersRules.TryMove(_board, cont.Value.From, cont.Value.To, out var rec2))
                                    break;
                                Console.WriteLine($"[CheckersAI] multi {rec2.FromAlg}-{rec2.ToAlg}");
                            }
                            CheckersRules.EvaluateTerminal(_board);
                        }
                    }
                }
                finally
                {
                    if (_board != null && _board.Phase == CheckersPhase.AiThinking)
                        _board.Phase = CheckersPhase.Idle;
                    _aiThinking = false;
                }
            });
        }

        public void HandleClick(Vector2 windowMouse)
        {
            if (_board == null || _board.IsGameOver || _aiThinking) return;
            if (_board.Mode == CheckersMode.VsAi && _board.SideToMove != _board.HumanColor) return;

            float boardPx = MathF.Min(_width, _height) * 0.82f;
            float originX = (_width - boardPx) * 0.5f;
            float originY = (_height - boardPx) * 0.5f;
            float lx = windowMouse.X - originX;
            float ly = windowMouse.Y - originY;
            if (lx < 0 || ly < 0 || lx > boardPx || ly > boardPx) return;

            float u = lx / boardPx;
            float v = 1f - (ly / boardPx);
            int file = (int)Math.Clamp(u * 8f, 0, 7);
            int rank = (int)Math.Clamp(v * 8f, 0, 7);
            OnSquare(new CheckersSquare(file, rank));
        }

        void OnSquare(CheckersSquare sq)
        {
            string alg = sq.ToAlgebraic();

            if (string.IsNullOrEmpty(_board.SelectedSquare))
            {
                var piece = _board.Get(sq);
                if (piece != null && piece.Color == _board.SideToMove)
                {
                    _board.SelectedSquare = alg;
                    _board.Phase = CheckersPhase.PieceSelected;
                }
                return;
            }

            if (_board.SelectedSquare == alg)
            {
                if (!_board.ContinuationFrom.HasValue)
                {
                    _board.SelectedSquare = null;
                    _board.Phase = CheckersPhase.Idle;
                }
                return;
            }

            var re = _board.Get(sq);
            if (re != null && re.Color == _board.SideToMove && !_board.ContinuationFrom.HasValue)
            {
                _board.SelectedSquare = alg;
                return;
            }

            if (!CheckersSquare.TryParse(_board.SelectedSquare, out var from)) return;

            if (CheckersRules.TryMove(_board, from, sq, out var rec))
            {
                string cap = rec.CapturedSquares.Count > 0 ? $" x{rec.CapturedSquares.Count}" : "";
                Console.WriteLine($"[Checkers] {rec.FromAlg}-{rec.ToAlg}{cap}");
                if (!_board.ContinuationFrom.HasValue)
                    CheckersRules.EvaluateTerminal(_board);
            }
            else if (!_board.ContinuationFrom.HasValue)
            {
                _board.SelectedSquare = null;
                _board.Phase = CheckersPhase.Idle;
            }
        }

        protected override void RenderContent(IReadOnlyList<Entity> entities, Matrix4x4 view, Matrix4x4 projection)
        {
            float half = 5.5f;
            float aspect = AspectRatio > 0 ? AspectRatio : 16f / 9f;
            Matrix4x4 ortho = Matrix4x4.CreateOrthographic(half * 2f * aspect, half * 2f, 0.1f, 100f);
            Matrix4x4 boardView = Matrix4x4.CreateLookAt(
                new Vector3(4f, 4f, 12f),
                new Vector3(4f, 4f, 0f),
                new Vector3(0f, 1f, 0f));

            _renderContext.Disable(_renderContext.Enums.DepthTest);

            if (_shader != null)
            {
                _shader.Use();
                _shader.SetMatrix4("uModel", Matrix4x4.Identity);
                _shader.SetMatrix4("uView", boardView);
                _shader.SetMatrix4("uProjection", ortho);
            }

            DrawBuf(_boardBuffer);
            DrawBuf(_highlightBuffer);
            DrawBuf(_pieceBuffer);

            _renderContext.Enable(_renderContext.Enums.DepthTest);
        }

        void DrawBuf(VertexBuffer buf)
        {
            if (buf == null) return;
            buf.Bind();
            _renderContext.DrawArrays(_renderContext.Enums.Triangles, 0, buf.GetVertexCount());
        }

        void RebuildMeshes()
        {
            if (_board == null || _boardBuffer == null) return;

            var boardVerts = new List<Vertex>(8 * 8 * 6);
            var pieceVerts = new List<Vertex>(24 * 48);
            var hiVerts = new List<Vertex>(64);

            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    bool dark = ((f + r) & 1) == 1;
                    AddQuad(boardVerts, f, r, 1f, 1f, 0f,
                        dark ? 0.32f : 0.78f,
                        dark ? 0.22f : 0.68f,
                        dark ? 0.14f : 0.52f, 1f);
                }

            if (!string.IsNullOrEmpty(_board.LastFrom) && CheckersSquare.TryParse(_board.LastFrom, out var lf))
                AddQuad(hiVerts, lf.File + 0.04f, lf.Rank + 0.04f, 0.92f, 0.92f, 0.01f, 0.85f, 0.75f, 0.15f, 0.40f);
            if (!string.IsNullOrEmpty(_board.LastTo) && CheckersSquare.TryParse(_board.LastTo, out var lt))
                AddQuad(hiVerts, lt.File + 0.04f, lt.Rank + 0.04f, 0.92f, 0.92f, 0.01f, 0.85f, 0.75f, 0.15f, 0.40f);

            if (!string.IsNullOrEmpty(_board.SelectedSquare) && CheckersSquare.TryParse(_board.SelectedSquare, out var sel))
            {
                AddQuad(hiVerts, sel.File + 0.03f, sel.Rank + 0.03f, 0.94f, 0.94f, 0.02f, 0.20f, 0.70f, 1.0f, 0.50f);
                foreach (var m in CheckersRules.GetLegalMovesFrom(_board, sel))
                {
                    bool isCap = Math.Abs(m.To.File - m.From.File) == 2;
                    if (isCap)
                        AddRing(hiVerts, m.To.File + 0.5f, m.To.Rank + 0.5f, 0.38f, 0.28f, 0.03f, 0.95f, 0.25f, 0.20f, 0.85f);
                    else
                        AddQuad(hiVerts, m.To.File + 0.38f, m.To.Rank + 0.38f, 0.24f, 0.24f, 0.03f, 0.15f, 0.85f, 0.35f, 0.75f);
                }
            }

            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    var p = _board.Board[r][f];
                    if (p == null) continue;
                    bool white = p.Color == CheckersColor.White;
                    float fr = white ? 0.95f : 0.12f;
                    float fg = white ? 0.92f : 0.12f;
                    float fb = white ? 0.88f : 0.14f;
                    float or_ = white ? 0.25f : 0.75f;
                    float og = white ? 0.22f : 0.72f;
                    float ob = white ? 0.18f : 0.65f;

                    float cx = f + 0.5f;
                    float cy = r + 0.5f;
                    DrawPiece(pieceVerts, p.Type, cx, cy, fr, fg, fb, or_, og, ob);
                }

            Upload(_boardBuffer, boardVerts);
            Upload(_highlightBuffer, hiVerts);
            Upload(_pieceBuffer, pieceVerts);
        }

        void DrawPiece(List<Vertex> v, CheckersPieceType type, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            // base disc
            AddCircle(v, cx, cy, 0.36f, 0.05f, fr, fg, fb, 1f, 16);
            // rim
            AddRing(v, cx, cy, 0.38f, 0.34f, 0.04f, or_, og, ob, 1f);

            if (type == CheckersPieceType.King)
            {
                // crown indicator
                AddCircle(v, cx, cy + 0.08f, 0.14f, 0.07f, or_, og, ob, 1f, 10);
                AddQuad(v, cx - 0.12f, cy + 0.16f, 0.08f, 0.10f, 0.08f, or_, og, ob, 1f);
                AddQuad(v, cx - 0.02f, cy + 0.18f, 0.08f, 0.12f, 0.08f, or_, og, ob, 1f);
                AddQuad(v, cx + 0.08f, cy + 0.16f, 0.08f, 0.10f, 0.08f, or_, og, ob, 1f);
            }
        }

        static void AddQuad(List<Vertex> verts, float x, float y, float w, float h, float z,
            float r, float g, float b, float a)
        {
            verts.Add(new Vertex(x, y, z, r, g, b, a));
            verts.Add(new Vertex(x + w, y, z, r, g, b, a));
            verts.Add(new Vertex(x + w, y + h, z, r, g, b, a));
            verts.Add(new Vertex(x, y, z, r, g, b, a));
            verts.Add(new Vertex(x + w, y + h, z, r, g, b, a));
            verts.Add(new Vertex(x, y + h, z, r, g, b, a));
        }

        static void AddCircle(List<Vertex> verts, float cx, float cy, float radius, float z,
            float r, float g, float b, float a, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)(i * Math.PI * 2.0 / segments);
                float a1 = (float)((i + 1) * Math.PI * 2.0 / segments);
                verts.Add(new Vertex(cx, cy, z, r, g, b, a));
                verts.Add(new Vertex(cx + MathF.Cos(a0) * radius, cy + MathF.Sin(a0) * radius, z, r, g, b, a));
                verts.Add(new Vertex(cx + MathF.Cos(a1) * radius, cy + MathF.Sin(a1) * radius, z, r, g, b, a));
            }
        }

        static void AddRing(List<Vertex> verts, float cx, float cy, float outer, float inner, float z,
            float r, float g, float b, float a)
        {
            const int seg = 16;
            for (int i = 0; i < seg; i++)
            {
                float a0 = (float)(i * Math.PI * 2.0 / seg);
                float a1 = (float)((i + 1) * Math.PI * 2.0 / seg);
                float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                verts.Add(new Vertex(cx + c0 * outer, cy + s0 * outer, z, r, g, b, a));
                verts.Add(new Vertex(cx + c1 * outer, cy + s1 * outer, z, r, g, b, a));
                verts.Add(new Vertex(cx + c0 * inner, cy + s0 * inner, z, r, g, b, a));
                verts.Add(new Vertex(cx + c1 * outer, cy + s1 * outer, z, r, g, b, a));
                verts.Add(new Vertex(cx + c1 * inner, cy + s1 * inner, z, r, g, b, a));
                verts.Add(new Vertex(cx + c0 * inner, cy + s0 * inner, z, r, g, b, a));
            }
        }

        void Upload(VertexBuffer buffer, List<Vertex> verts)
        {
            var indices = new List<uint>(verts.Count);
            for (uint i = 0; i < verts.Count; i++) indices.Add(i);
            buffer.UpdateCustom(verts, indices);
        }

        public override void Dispose()
        {
            _shader?.Dispose();
            _boardBuffer?.Dispose();
            _pieceBuffer?.Dispose();
            _highlightBuffer?.Dispose();
            base.Dispose();
        }
    }
}