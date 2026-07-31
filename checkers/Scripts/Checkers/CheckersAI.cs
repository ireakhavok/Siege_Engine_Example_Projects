using System;
using System.Collections.Generic;

namespace CheckersProject
{
    public static class CheckersAI
    {
        const int ManValue = 100;
        const int KingValue = 250;

        static int DepthOf(CheckersAiDifficulty d) => d switch
        {
            CheckersAiDifficulty.Easy => 2,
            CheckersAiDifficulty.Hard => 6,
            _ => 4
        };

        public static ScoredMove? FindBestMove(CheckersBoardState root, CheckersAiDifficulty difficulty)
        {
            var moves = CheckersRules.GetAllLegalMoves(root);
            if (moves.Count == 0) return null;

            if (difficulty == CheckersAiDifficulty.Easy && Random.Shared.NextDouble() < 0.30)
            {
                var pick = moves[Random.Shared.Next(moves.Count)];
                return new ScoredMove(pick.From, pick.To, 0);
            }

            int depth = DepthOf(difficulty);
            CheckersColor forColor = root.SideToMove;

            ScoredMove? best = null;
            int bestScore = int.MinValue / 4;

            foreach (var m in moves)
            {
                var child = root.Clone();
                if (!CheckersRules.TryMove(child, m.From, m.To, out _))
                    continue;

                // If multi-jump continuation, keep searching same side
                bool stillSameSide = child.ContinuationFrom.HasValue;
                int score = AlphaBeta(child, depth - 1, int.MinValue / 4, int.MaxValue / 4,
                    maximizing: stillSameSide, forColor: forColor);

                if (difficulty != CheckersAiDifficulty.Hard)
                {
                    double span = difficulty == CheckersAiDifficulty.Easy ? 35.0 : 12.0;
                    score += (int)((Random.Shared.NextDouble() - 0.5) * span);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new ScoredMove(m.From, m.To, score);
                }
            }

            return best;
        }

        static int AlphaBeta(CheckersBoardState state, int depth, int alpha, int beta,
            bool maximizing, CheckersColor forColor)
        {
            if (depth == 0 || state.IsGameOver)
                return Evaluate(state, forColor);

            var moves = CheckersRules.GetAllLegalMoves(state);
            if (moves.Count == 0)
                return Evaluate(state, forColor);

            if (maximizing)
            {
                int best = int.MinValue / 4;
                foreach (var m in moves)
                {
                    var child = state.Clone();
                    if (!CheckersRules.TryMove(child, m.From, m.To, out _)) continue;
                    bool cont = child.ContinuationFrom.HasValue;
                    int val = AlphaBeta(child, depth - 1, alpha, beta, cont ? true : false, forColor);
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
                    if (!CheckersRules.TryMove(child, m.From, m.To, out _)) continue;
                    bool cont = child.ContinuationFrom.HasValue;
                    int val = AlphaBeta(child, depth - 1, alpha, beta, cont ? false : true, forColor);
                    if (val < best) best = val;
                    if (best < beta) beta = best;
                    if (beta <= alpha) break;
                }
                return best;
            }
        }

        public static int Evaluate(CheckersBoardState state, CheckersColor forColor)
        {
            var legal = CheckersRules.GetAllLegalMoves(state);
            if (legal.Count == 0)
            {
                // Side to move has no moves → they lose
                return state.SideToMove == forColor ? -100000 : 100000;
            }

            int score = 0;
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    var p = state.Board[r][f];
                    if (p == null) continue;
                    int val = p.Type == CheckersPieceType.King ? KingValue : ManValue;
                    // slight advancement bonus for men
                    if (p.Type == CheckersPieceType.Man)
                        val += p.Color == CheckersColor.White ? r * 3 : (7 - r) * 3;
                    score += p.Color == forColor ? val : -val;
                }

            score += state.SideToMove == forColor ? legal.Count * 2 : -legal.Count * 2;
            return score;
        }
    }
}