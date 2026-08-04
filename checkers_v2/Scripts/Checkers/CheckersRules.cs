using System;
using System.Collections.Generic;

namespace CheckersProject
{
    public static class CheckersRules
    {
        // Diagonal directions
        static readonly int[] Df = { 1, 1, -1, -1 };
        static readonly int[] Dr = { 1, -1, 1, -1 };

        public static List<ScoredMove> GetAllLegalMoves(CheckersBoardState state)
        {
            var captures = new List<ScoredMove>(32);
            var quiet = new List<ScoredMove>(32);

            if (state.ContinuationFrom.HasValue)
            {
                CollectJumpsFrom(state, state.ContinuationFrom.Value, captures);
                return captures; // must continue the multi-jump
            }

            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    var p = state.Board[r][f];
                    if (p == null || p.Color != state.SideToMove) continue;
                    var from = new CheckersSquare(f, r);
                    CollectJumpsFrom(state, from, captures);
                    if (captures.Count == 0) // only generate quiet if no captures exist anywhere
                        CollectQuietFrom(state, from, quiet);
                }

            // Forced captures (American rules)
            if (captures.Count > 0) return captures;
            return quiet;
        }

        public static List<ScoredMove> GetLegalMovesFrom(CheckersBoardState state, CheckersSquare from)
        {
            var all = GetAllLegalMoves(state);
            var result = new List<ScoredMove>();
            foreach (var m in all)
                if (m.From == from) result.Add(m);
            return result;
        }

        static void CollectQuietFrom(CheckersBoardState state, CheckersSquare from, List<ScoredMove> outMoves)
        {
            var piece = state.Get(from);
            if (piece == null) return;

            for (int i = 0; i < 4; i++)
            {
                int nf = from.File + Df[i];
                int nr = from.Rank + Dr[i];
                var to = new CheckersSquare(nf, nr);
                if (!to.IsValid || !to.IsDark) continue;
                if (state.Get(to) != null) continue;

                // Men only move forward
                if (piece.Type == CheckersPieceType.Man)
                {
                    if (piece.Color == CheckersColor.White && Dr[i] < 0) continue;
                    if (piece.Color == CheckersColor.Black && Dr[i] > 0) continue;
                }

                outMoves.Add(new ScoredMove(from, to, 0));
            }
        }

        static void CollectJumpsFrom(CheckersBoardState state, CheckersSquare from, List<ScoredMove> outMoves)
        {
            var piece = state.Get(from);
            if (piece == null) return;

            for (int i = 0; i < 4; i++)
            {
                int mf = from.File + Df[i];
                int mr = from.Rank + Dr[i];
                var mid = new CheckersSquare(mf, mr);
                if (!mid.IsValid) continue;

                var victim = state.Get(mid);
                if (victim == null || victim.Color == piece.Color) continue;

                int lf = from.File + 2 * Df[i];
                int lr = from.Rank + 2 * Dr[i];
                var land = new CheckersSquare(lf, lr);
                if (!land.IsValid || !land.IsDark) continue;
                if (state.Get(land) != null) continue;

                // Men can only jump forward
                if (piece.Type == CheckersPieceType.Man)
                {
                    if (piece.Color == CheckersColor.White && Dr[i] < 0) continue;
                    if (piece.Color == CheckersColor.Black && Dr[i] > 0) continue;
                }

                outMoves.Add(new ScoredMove(from, land, 0));
            }
        }

        public static bool TryMove(CheckersBoardState state, CheckersSquare from, CheckersSquare to,
            out CheckersMoveRecord record)
        {
            record = null;
            var legal = GetLegalMovesFrom(state, from);
            bool ok = false;
            foreach (var m in legal)
            {
                if (m.From == from && m.To == to)
                {
                    ok = true;
                    break;
                }
            }
            if (!ok) return false;

            ApplyRaw(state, from, to, out record);

            // Check for multi-jump continuation
            bool wasCapture = record.CapturedSquares.Count > 0;
            if (wasCapture)
            {
                var further = new List<ScoredMove>();
                CollectJumpsFrom(state, to, further);
                if (further.Count > 0)
                {
                    state.ContinuationFrom = to;
                    state.SelectedSquare = to.ToAlgebraic();
                    state.Phase = CheckersPhase.PieceSelected;
                    return true; // same side continues
                }
            }

            state.ContinuationFrom = null;
            state.SelectedSquare = null;
            state.Phase = CheckersPhase.Idle;
            state.SideToMove = state.SideToMove == CheckersColor.White
                ? CheckersColor.Black : CheckersColor.White;

            return true;
        }

        static void ApplyRaw(CheckersBoardState state, CheckersSquare from, CheckersSquare to,
            out CheckersMoveRecord record)
        {
            var piece = state.Get(from);
            record = new CheckersMoveRecord
            {
                From = from,
                To = to,
                FromAlg = from.ToAlgebraic(),
                ToAlg = to.ToAlgebraic()
            };

            // Detect capture (jumped over middle square)
            int df = to.File - from.File;
            int dr = to.Rank - from.Rank;
            if (Math.Abs(df) == 2 && Math.Abs(dr) == 2)
            {
                var mid = new CheckersSquare(from.File + df / 2, from.Rank + dr / 2);
                var victim = state.Get(mid);
                if (victim != null)
                {
                    state.Set(mid, null);
                    record.CapturedSquares.Add(mid);
                }
            }

            state.Set(to, piece);
            state.Set(from, null);

            // Promotion
            if (piece.Type == CheckersPieceType.Man)
            {
                if (piece.Color == CheckersColor.White && to.Rank == 7)
                {
                    piece.Type = CheckersPieceType.King;
                    record.WasPromotion = true;
                }
                else if (piece.Color == CheckersColor.Black && to.Rank == 0)
                {
                    piece.Type = CheckersPieceType.King;
                    record.WasPromotion = true;
                }
            }

            state.LastFrom = record.FromAlg;
            state.LastTo = record.ToAlg;
        }

        public static void EvaluateTerminal(CheckersBoardState state)
        {
            var moves = GetAllLegalMoves(state);
            if (moves.Count > 0) return;

            state.IsGameOver = true;
            state.Phase = CheckersPhase.GameOver;

            // Count remaining pieces of the side that just moved (opponent of SideToMove)
            int white = 0, black = 0;
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    var p = state.Board[r][f];
                    if (p == null) continue;
                    if (p.Color == CheckersColor.White) white++;
                    else black++;
                }

            if (white == 0)
                state.ResultText = "Black wins";
            else if (black == 0)
                state.ResultText = "White wins";
            else
                state.ResultText = $"{(state.SideToMove == CheckersColor.White ? "Black" : "White")} wins — no moves left";
        }
    }
}