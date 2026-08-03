// Folder: Scripts/Chess
// File: ChessScene.cs
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

namespace ChessProject
{
    [CustomSceneEntry]
    public sealed class ChessScene : Scene
    {
        ChessBoardState _board;
        ShaderProgram _shader;
        VertexBuffer _boardBuffer;
        VertexBuffer _pieceBuffer;
        VertexBuffer _highlightBuffer;

        bool _aiThinking;
        bool _pendingClick;
        bool _ready;
        double _cursorX;
        double _cursorY;
        ChessAiDifficulty _difficulty = ChessAiDifficulty.Normal;

        readonly bool _isHostedPreview;

        public ChessScene(SceneContext context)
            : base(
                context.RenderContext,
                context.ControlContext,
                context.Window,
                context.Server,
                context.EventBus)
        {
            _isHostedPreview = context != null && context.IsHostedPreview;
            Console.WriteLine($"[ChessScene] Constructed via SceneContext (hostedPreview={_isHostedPreview})");
        }

        public override void Initialize(int width, int height)
        {
            base.Initialize(width, height);

            _renderContext.ClearColor(0.10f, 0.12f, 0.14f, 1.0f);

            _board = ChessBoardState.CreateStartingPosition();
            _board.Mode = ChessMode.VsAi;
            _board.HumanColor = ChessColor.White;

            _shader = new ShaderProgram(_renderContext, SceneShader.VertexShaderSource, SceneShader.FragmentShaderSource);
            _boardBuffer = new VertexBuffer(_renderContext);
            _pieceBuffer = new VertexBuffer(_renderContext);
            _highlightBuffer = new VertexBuffer(_renderContext);
            RebuildMeshes();

            // Hosted preview: editor owns the window and input. Never install callbacks.
            if (!_isHostedPreview)
            {
                try
                {
                    _controlContext.SetWindowSizeCallback(_window, (w, nw, nh) =>
                    {
                        if (nw > 0 && nh > 0)
                        {
                            _width = nw;
                            _height = nh;
                            _renderContext.Viewport(0, 0, (uint)nw, (uint)nh);
                        }
                    });
                    _controlContext.SetCursorPosCallback(_window, (w, x, y) =>
                    {
                        _cursorX = x;
                        _cursorY = y;
                    });
                    _controlContext.SetMouseButtonCallback(_window, (w, button, action, mods) =>
                    {
                        string a = action.ToString();
                        string b = button.ToString();
                        if (a.Equals("Press", StringComparison.OrdinalIgnoreCase)
                            && (b.Equals("Left", StringComparison.OrdinalIgnoreCase)
                                || b.Equals("Button1", StringComparison.OrdinalIgnoreCase)
                                || b == "0"))
                            _pendingClick = true;
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ChessScene] input setup: {ex.Message}");
                }
            }

            _ready = true;
            Console.WriteLine("[ChessScene] Ready — white to move. AI is black with full material eval.");
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (!_ready || _board == null) return;

            // Hosted preview: no interactive logic, but keep meshes current so the board is visible.
            if (_isHostedPreview)
            {
                RebuildMeshes();
                return;
            }

            if (_pendingClick)
            {
                _pendingClick = false;
                HandleClick(new Vector2((float)_cursorX, (float)_cursorY));
            }

            TickAi();
            RebuildMeshes();
        }

        void TickAi()
        {
            if (_board.IsGameOver || _aiThinking) return;
            if (_board.Mode != ChessMode.VsAi) return;
            if (_board.SideToMove == _board.HumanColor) return;

            _aiThinking = true;
            _board.Phase = ChessPhase.AiThinking;
            var snap = _board.Clone();
            var difficulty = _difficulty;

            _ = Task.Run(() =>
            {
                try
                {
                    var best = ChessAI.FindBestMove(snap, difficulty);
                    if (best.HasValue && _board != null && !_board.IsGameOver)
                    {
                        var m = best.Value;
                        if (ChessRules.TryMove(_board, m.From, m.To, m.Promotion, out var rec))
                        {
                            string cap = rec.Captured != null ? $" x{rec.Captured.Type}" : "";
                            Console.WriteLine($"[ChessAI] {rec.FromAlg}-{rec.ToAlg}{cap}  score={m.Score}");
                            ChessRules.EvaluateTerminal(_board);
                        }
                    }
                }
                finally
                {
                    if (_board != null && _board.Phase == ChessPhase.AiThinking)
                        _board.Phase = ChessPhase.Idle;
                    _aiThinking = false;
                }
            });
        }

        public void HandleClick(Vector2 windowMouse)
        {
            if (_isHostedPreview) return;
            if (_board == null || _board.IsGameOver || _aiThinking) return;
            if (_board.Mode == ChessMode.VsAi && _board.SideToMove != _board.HumanColor) return;

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
            OnSquare(new ChessSquare(file, rank));
        }

        void OnSquare(ChessSquare sq)
        {
            string alg = sq.ToAlgebraic();

            if (string.IsNullOrEmpty(_board.SelectedSquare))
            {
                var piece = _board.Get(sq);
                if (piece != null && piece.Color == _board.SideToMove)
                {
                    _board.SelectedSquare = alg;
                    _board.Phase = ChessPhase.PieceSelected;
                }
                return;
            }

            if (_board.SelectedSquare == alg)
            {
                _board.SelectedSquare = null;
                _board.Phase = ChessPhase.Idle;
                return;
            }

            var re = _board.Get(sq);
            if (re != null && re.Color == _board.SideToMove)
            {
                _board.SelectedSquare = alg;
                return;
            }

            if (!ChessSquare.TryParse(_board.SelectedSquare, out var from)) return;

            ChessPieceType? promo = null;
            var moving = _board.Get(from);
            if (moving?.Type == ChessPieceType.Pawn &&
                ((moving.Color == ChessColor.White && sq.Rank == 7) ||
                 (moving.Color == ChessColor.Black && sq.Rank == 0)))
                promo = ChessPieceType.Queen;

            if (ChessRules.TryMove(_board, from, sq, promo, out var rec))
            {
                string cap = rec.Captured != null ? $" x{rec.Captured.Type}" : "";
                Console.WriteLine($"[Chess] {rec.FromAlg}-{rec.ToAlg}{cap}");
                ChessRules.EvaluateTerminal(_board);
            }
            else
            {
                _board.SelectedSquare = null;
                _board.Phase = ChessPhase.Idle;
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
            var pieceVerts = new List<Vertex>(32 * 48);
            var hiVerts = new List<Vertex>(64);

            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    bool light = ((f + r) & 1) == 0;
                    AddQuad(boardVerts, f, r, 1f, 1f, 0f,
                        light ? 0.82f : 0.28f,
                        light ? 0.72f : 0.22f,
                        light ? 0.55f : 0.16f, 1f);
                }

            if (!string.IsNullOrEmpty(_board.LastFrom) && ChessSquare.TryParse(_board.LastFrom, out var lf))
                AddQuad(hiVerts, lf.File + 0.04f, lf.Rank + 0.04f, 0.92f, 0.92f, 0.01f, 0.85f, 0.75f, 0.15f, 0.40f);
            if (!string.IsNullOrEmpty(_board.LastTo) && ChessSquare.TryParse(_board.LastTo, out var lt))
                AddQuad(hiVerts, lt.File + 0.04f, lt.Rank + 0.04f, 0.92f, 0.92f, 0.01f, 0.85f, 0.75f, 0.15f, 0.40f);

            if (!string.IsNullOrEmpty(_board.SelectedSquare) && ChessSquare.TryParse(_board.SelectedSquare, out var sel))
            {
                AddQuad(hiVerts, sel.File + 0.03f, sel.Rank + 0.03f, 0.94f, 0.94f, 0.02f, 0.20f, 0.70f, 1.0f, 0.50f);
                foreach (var m in ChessRules.GetLegalMovesFrom(_board, sel))
                {
                    bool isCap = _board.Get(m.To) != null;
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
                    bool white = p.Color == ChessColor.White;
                    float fr = white ? 0.96f : 0.12f;
                    float fg = white ? 0.94f : 0.12f;
                    float fb = white ? 0.90f : 0.14f;
                    float or_ = white ? 0.15f : 0.85f;
                    float og = white ? 0.15f : 0.85f;
                    float ob = white ? 0.18f : 0.80f;

                    float cx = f + 0.5f;
                    float cy = r + 0.5f;
                    DrawPiece(pieceVerts, p.Type, cx, cy, fr, fg, fb, or_, og, ob);
                }

            Upload(_boardBuffer, boardVerts);
            Upload(_highlightBuffer, hiVerts);
            Upload(_pieceBuffer, pieceVerts);
        }

        void DrawPiece(List<Vertex> v, ChessPieceType type, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            switch (type)
            {
                case ChessPieceType.Pawn: DrawPawn(v, cx, cy, fr, fg, fb, or_, og, ob); break;
                case ChessPieceType.Rook: DrawRook(v, cx, cy, fr, fg, fb, or_, og, ob); break;
                case ChessPieceType.Knight: DrawKnight(v, cx, cy, fr, fg, fb, or_, og, ob); break;
                case ChessPieceType.Bishop: DrawBishop(v, cx, cy, fr, fg, fb, or_, og, ob); break;
                case ChessPieceType.Queen: DrawQueen(v, cx, cy, fr, fg, fb, or_, og, ob); break;
                case ChessPieceType.King: DrawKing(v, cx, cy, fr, fg, fb, or_, og, ob); break;
            }
        }

        void DrawPawn(List<Vertex> v, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            AddQuad(v, cx - 0.28f, cy - 0.38f, 0.56f, 0.12f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.10f, cy - 0.28f, 0.20f, 0.22f, 0.05f, fr, fg, fb, 1f);
            AddCircle(v, cx, cy + 0.08f, 0.16f, 0.06f, fr, fg, fb, 1f, 12);
            AddQuad(v, cx - 0.30f, cy - 0.40f, 0.60f, 0.04f, 0.04f, or_, og, ob, 1f);
        }

        void DrawRook(List<Vertex> v, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            AddQuad(v, cx - 0.30f, cy - 0.38f, 0.60f, 0.12f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.22f, cy - 0.26f, 0.44f, 0.40f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.28f, cy + 0.14f, 0.14f, 0.18f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.07f, cy + 0.14f, 0.14f, 0.18f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx + 0.14f, cy + 0.14f, 0.14f, 0.18f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.30f, cy - 0.40f, 0.60f, 0.04f, 0.04f, or_, og, ob, 1f);
        }

        void DrawKnight(List<Vertex> v, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            AddQuad(v, cx - 0.28f, cy - 0.38f, 0.56f, 0.12f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.18f, cy - 0.26f, 0.36f, 0.30f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.30f, cy + 0.00f, 0.28f, 0.22f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.34f, cy + 0.18f, 0.22f, 0.14f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.38f, cy + 0.10f, 0.12f, 0.10f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.22f, cy + 0.28f, 0.08f, 0.12f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.30f, cy - 0.40f, 0.60f, 0.04f, 0.04f, or_, og, ob, 1f);
        }

        void DrawBishop(List<Vertex> v, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            AddQuad(v, cx - 0.28f, cy - 0.38f, 0.56f, 0.12f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.16f, cy - 0.26f, 0.32f, 0.20f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.12f, cy - 0.06f, 0.24f, 0.18f, 0.05f, fr, fg, fb, 1f);
            AddCircle(v, cx, cy + 0.18f, 0.14f, 0.06f, fr, fg, fb, 1f, 10);
            AddQuad(v, cx - 0.03f, cy + 0.22f, 0.06f, 0.16f, 0.07f, or_, og, ob, 1f);
            AddCircle(v, cx, cy + 0.36f, 0.05f, 0.07f, fr, fg, fb, 1f, 8);
            AddQuad(v, cx - 0.30f, cy - 0.40f, 0.60f, 0.04f, 0.04f, or_, og, ob, 1f);
        }

        void DrawQueen(List<Vertex> v, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            AddQuad(v, cx - 0.32f, cy - 0.38f, 0.64f, 0.12f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.20f, cy - 0.26f, 0.40f, 0.28f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.26f, cy + 0.02f, 0.52f, 0.10f, 0.05f, fr, fg, fb, 1f);
            for (int i = -2; i <= 2; i++)
            {
                float px = cx + i * 0.11f;
                float h = (i == 0) ? 0.22f : 0.16f;
                AddQuad(v, px - 0.04f, cy + 0.10f, 0.08f, h, 0.05f, fr, fg, fb, 1f);
                AddCircle(v, px, cy + 0.10f + h, 0.045f, 0.06f, fr, fg, fb, 1f, 8);
            }
            AddQuad(v, cx - 0.32f, cy - 0.40f, 0.64f, 0.04f, 0.04f, or_, og, ob, 1f);
        }

        void DrawKing(List<Vertex> v, float cx, float cy,
            float fr, float fg, float fb, float or_, float og, float ob)
        {
            AddQuad(v, cx - 0.30f, cy - 0.38f, 0.60f, 0.12f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.18f, cy - 0.26f, 0.36f, 0.30f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.24f, cy + 0.04f, 0.48f, 0.10f, 0.05f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.05f, cy + 0.12f, 0.10f, 0.32f, 0.06f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.16f, cy + 0.26f, 0.32f, 0.09f, 0.06f, fr, fg, fb, 1f);
            AddQuad(v, cx - 0.30f, cy - 0.40f, 0.60f, 0.04f, 0.04f, or_, og, ob, 1f);
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