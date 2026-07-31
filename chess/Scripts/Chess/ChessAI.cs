using System;
using System.Collections.Generic;

namespace ChessProject
{
    /// <summary>
    /// 1:1 port of the web ai.ts (chess.js minimax).
    /// Same depths, material, full PST, mobility, capture ordering, alphabeta.
    /// </summary>
    public static class ChessAI
    {
        // --- identical to ai.ts MATERIAL ---
        const int PawnValue = 100;
        const int KnightValue = 320;
        const int BishopValue = 330;
        const int RookValue = 500;
        const int QueenValue = 900;
        const int KingValue = 20000;

        // --- identical to ai.ts DEPTH: easy=1, normal=2, hard=3 ---
        static int DepthOf(ChessAiDifficulty d) => d switch
        {
            ChessAiDifficulty.Easy => 1,
            ChessAiDifficulty.Hard => 3,
            _ => 2
        };

        // --- identical to ai.ts PST (rows = rank8 → rank1) ---
        static readonly int[][] PstPawn =
        {
            new[] {  0,  0,  0,  0,  0,  0,  0,  0 },
            new[] { 50, 50, 50, 50, 50, 50, 50, 50 },
            new[] { 10, 10, 20, 30, 30, 20, 10, 10 },
            new[] {  5,  5, 10, 25, 25, 10,  5,  5 },
            new[] {  0,  0,  0, 20, 20,  0,  0,  0 },
            new[] {  5, -5,-10,  0,  0,-10, -5,  5 },
            new[] {  5, 10, 10,-20,-20, 10, 10,  5 },
            new[] {  0,  0,  0,  0,  0,  0,  0,  0 },
        };
        static readonly int[][] PstKnight =
        {
            new[] { -50,-40,-30,-30,-30,-30,-40,-50 },
            new[] { -40,-20,  0,  0,  0,  0,-20,-40 },
            new[] { -30,  0, 10, 15, 15, 10,  0,-30 },
            new[] { -30,  5, 15, 20, 20, 15,  5,-30 },
            new[] { -30,  0, 15, 20, 20, 15,  0,-30 },
            new[] { -30,  5, 10, 15, 15, 10,  5,-30 },
            new[] { -40,-20,  0,  5,  5,  0,-20,-40 },
            new[] { -50,-40,-30,-30,-30,-30,-40,-50 },
        };
        static readonly int[][] PstBishop =
        {
            new[] { -20,-10,-10,-10,-10,-10,-10,-20 },
            new[] { -10,  0,  0,  0,  0,  0,  0,-10 },
            new[] { -10,  0,  5, 10, 10,  5,  0,-10 },
            new[] { -10,  5,  5, 10, 10,  5,  5,-10 },
            new[] { -10,  0, 10, 10, 10, 10,  0,-10 },
            new[] { -10, 10, 10, 10, 10, 10, 10,-10 },
            new[] { -10,  5,  0,  0,  0,  0,  5,-10 },
            new[] { -20,-10,-10,-10,-10,-10,-10,-20 },
        };
        static readonly int[][] PstRook =
        {
            new[] {  0,  0,  0,  0,  0,  0,  0,  0 },
            new[] {  5, 10, 10, 10, 10, 10, 10,  5 },
            new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
            new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
            new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
            new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
            new[] { -5,  0,  0,  0,  0,  0,  0, -5 },
            new[] {  0,  0,  0,  5,  5,  0,  0,  0 },
        };
        static readonly int[][] PstQueen =
        {
            new[] { -20,-10,-10, -5, -5,-10,-10,-20 },
            new[] { -10,  0,  0,  0,  0,  0,  0,-10 },
            new[] { -10,  0,  5,  5,  5,  5,  0,-10 },
            new[] {  -5,  0,  5,  5,  5,  5,  0, -5 },
            new[] {   0,  0,  5,  5,  5,  5,  0, -5 },
            new[] { -10,  5,  5,  5,  5,  5,  0,-10 },
            new[] { -10,  0,  5,  0,  0,  0,  0,-10 },
            new[] { -20,-10,-10, -5, -5,-10,-10,-20 },
        };
        static readonly int[][] PstKing =
        {
            new[] { -30,-40,-40,-50,-50,-40,-40,-30 },
            new[] { -30,-40,-40,-50,-50,-40,-40,-30 },
            new[] { -30,-40,-40,-50,-50,-40,-40,-30 },
            new[] { -30,-40,-40,-50,-50,-40,-40,-30 },
            new[] { -20,-30,-30,-40,-40,-30,-30,-20 },
            new[] { -10,-20,-20,-20,-20,-20,-20,-10 },
            new[] {  20, 20,  0,  0,  0,  0, 20, 20 },
            new[] {  20, 30, 10,  0,  0, 10, 30, 20 },
        };

        public static ScoredMove? FindBestMove(ChessBoardState root, ChessAiDifficulty difficulty)
        {
            var moves = OrderMoves(root, ChessRules.GetAllLegalMoves(root));
            if (moves.Count == 0) return null;

            // easy: 35% random blunder (same as web)
            if (difficulty == ChessAiDifficulty.Easy && Random.Shared.NextDouble() < 0.35)
            {
                var pick = moves[Random.Shared.Next(moves.Count)];
                return new ScoredMove(pick.From, pick.To, pick.Promotion, 0);
            }

            int depth = DepthOf(difficulty);
            ChessColor forColor = root.SideToMove;

            ScoredMove? best = null;
            int bestScore = int.MinValue / 4;

            foreach (var m in moves)
            {
                var child = root.Clone();
                if (!ChessRules.TryMove(child, m.From, m.To, m.Promotion, out _))
                    continue;

                // alphabeta(game, depth-1, -inf, +inf, maximizing=false, forColor)
                int score = AlphaBeta(child, depth - 1, int.MinValue / 4, int.MaxValue / 4,
                    maximizing: false, forColor: forColor);

                if (difficulty != ChessAiDifficulty.Hard)
                {
                    double span = difficulty == ChessAiDifficulty.Easy ? 40.0 : 15.0;
                    score += (int)((Random.Shared.NextDouble() - 0.5) * span);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new ScoredMove(m.From, m.To, m.Promotion, score);
                }
            }

            return best;
        }

        static int AlphaBeta(
            ChessBoardState state,
            int depth,
            int alpha,
            int beta,
            bool maximizing,
            ChessColor forColor)
        {
            if (depth == 0)
                return Evaluate(state, forColor);

            var moves = OrderMoves(state, ChessRules.GetAllLegalMoves(state));
            if (moves.Count == 0)
                return Evaluate(state, forColor);

            if (maximizing)
            {
                int best = int.MinValue / 4;
                foreach (var m in moves)
                {
                    var child = state.Clone();
                    if (!ChessRules.TryMove(child, m.From, m.To, m.Promotion, out _))
                        continue;
                    int val = AlphaBeta(child, depth - 1, alpha, beta, false, forColor);
                    if (val > best) best = val;
                    if (best > alpha) alpha = best;
                    if (beta <= alpha) break;
                }
                return best;
            }
            else
            {
                int best = int.MaxValue / 4;
                foreach (var m in moves)
                {
                    var child = state.Clone();
                    if (!ChessRules.TryMove(child, m.From, m.To, m.Promotion, out _))
                        continue;
                    int val = AlphaBeta(child, depth - 1, alpha, beta, true, forColor);
                    if (val < best) best = val;
                    if (best < beta) beta = best;
                    if (beta <= alpha) break;
                }
                return best;
            }
        }

        public static int Evaluate(ChessBoardState state, ChessColor forColor)
        {
            var legal = ChessRules.GetAllLegalMoves(state);
            if (legal.Count == 0)
            {
                if (ChessRules.IsInCheck(state, state.SideToMove))
                    return state.SideToMove == forColor ? -100000 : 100000;
                return 0;
            }

            int score = 0;
            for (int rank = 0; rank < 8; rank++)
                for (int file = 0; file < 8; file++)
                {
                    var p = state.Board[rank][file];
                    if (p == null) continue;

                    int mat = MaterialOf(p.Type);
                    // Web board[0]=rank8. Our rank0=rank1 → webRow = 7-rank
                    int webRow = 7 - rank;
                    int pr = p.Color == ChessColor.White ? webRow : 7 - webRow;
                    int pst = PstOf(p.Type, pr, file);
                    int val = mat + pst;
                    score += p.Color == forColor ? val : -val;
                }

            int mob = legal.Count;
            score += state.SideToMove == forColor ? mob * 2 : -mob * 2;
            return score;
        }

        static List<ScoredMove> OrderMoves(ChessBoardState state, List<ScoredMove> moves)
        {
            var list = new List<ScoredMove>(moves);
            list.Sort((a, b) => CaptureValue(state, b).CompareTo(CaptureValue(state, a)));
            return list;
        }

        static int CaptureValue(ChessBoardState state, ScoredMove m)
        {
            var victim = state.Get(m.To);
            if (victim != null) return MaterialOf(victim.Type);
            if (state.EnPassantTarget.HasValue && m.To == state.EnPassantTarget.Value)
                return PawnValue;
            return 0;
        }

        static int MaterialOf(ChessPieceType t) => t switch
        {
            ChessPieceType.Pawn => PawnValue,
            ChessPieceType.Knight => KnightValue,
            ChessPieceType.Bishop => BishopValue,
            ChessPieceType.Rook => RookValue,
            ChessPieceType.Queen => QueenValue,
            ChessPieceType.King => KingValue,
            _ => 0
        };

        static int PstOf(ChessPieceType t, int pr, int file) => t switch
        {
            ChessPieceType.Pawn => PstPawn[pr][file],
            ChessPieceType.Knight => PstKnight[pr][file],
            ChessPieceType.Bishop => PstBishop[pr][file],
            ChessPieceType.Rook => PstRook[pr][file],
            ChessPieceType.Queen => PstQueen[pr][file],
            ChessPieceType.King => PstKing[pr][file],
            _ => 0
        };
    }
}