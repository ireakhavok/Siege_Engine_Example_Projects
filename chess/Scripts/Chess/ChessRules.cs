using System;
using System.Collections.Generic;

namespace ChessProject
{
    public static class ChessRules
    {
        static readonly int[] KnightDf = { 1, 2, 2, 1, -1, -2, -2, -1 };
        static readonly int[] KnightDr = { 2, 1, -1, -2, -2, -1, 1, 2 };
        static readonly int[] KingDf = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly int[] KingDr = { 0, 1, 1, 1, 0, -1, -1, -1 };

        public static List<ScoredMove> GetAllLegalMoves(ChessBoardState state)
        {
            var moves = new List<ScoredMove>(64);
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    var p = state.Board[r][f];
                    if (p == null || p.Color != state.SideToMove) continue;
                    CollectFrom(state, new ChessSquare(f, r), moves);
                }
            return moves;
        }

        public static List<ScoredMove> GetLegalMovesFrom(ChessBoardState state, ChessSquare from)
        {
            var moves = new List<ScoredMove>(16);
            var p = state.Get(from);
            if (p == null || p.Color != state.SideToMove) return moves;
            CollectFrom(state, from, moves);
            return moves;
        }

        static void CollectFrom(ChessBoardState state, ChessSquare from, List<ScoredMove> outMoves)
        {
            var piece = state.Get(from);
            if (piece == null) return;

            switch (piece.Type)
            {
                case ChessPieceType.Pawn: GenPawn(state, from, piece, outMoves); break;
                case ChessPieceType.Knight: GenKnight(state, from, piece, outMoves); break;
                case ChessPieceType.Bishop: GenSlide(state, from, piece, outMoves, true, false); break;
                case ChessPieceType.Rook: GenSlide(state, from, piece, outMoves, false, true); break;
                case ChessPieceType.Queen: GenSlide(state, from, piece, outMoves, true, true); break;
                case ChessPieceType.King: GenKing(state, from, piece, outMoves); break;
            }
        }

        static void TryAdd(ChessBoardState state, ChessSquare from, ChessSquare to,
            ChessPieceType? promo, List<ScoredMove> outMoves)
        {
            if (!to.IsValid) return;
            var snap = state.Clone();
            ApplyRaw(snap, from, to, promo, out _);
            if (IsInCheck(snap, state.SideToMove)) return;
            outMoves.Add(new ScoredMove(from, to, promo, 0));
        }

        static void GenPawn(ChessBoardState state, ChessSquare from, ChessPiece piece, List<ScoredMove> outMoves)
        {
            int dir = piece.Color == ChessColor.White ? 1 : -1;
            int startRank = piece.Color == ChessColor.White ? 1 : 6;
            int promoRank = piece.Color == ChessColor.White ? 7 : 0;

            var one = new ChessSquare(from.File, from.Rank + dir);
            if (one.IsValid && state.Get(one) == null)
            {
                if (one.Rank == promoRank)
                {
                    TryAdd(state, from, one, ChessPieceType.Queen, outMoves);
                    TryAdd(state, from, one, ChessPieceType.Rook, outMoves);
                    TryAdd(state, from, one, ChessPieceType.Bishop, outMoves);
                    TryAdd(state, from, one, ChessPieceType.Knight, outMoves);
                }
                else
                {
                    TryAdd(state, from, one, null, outMoves);
                    if (from.Rank == startRank)
                    {
                        var two = new ChessSquare(from.File, from.Rank + 2 * dir);
                        if (two.IsValid && state.Get(two) == null)
                            TryAdd(state, from, two, null, outMoves);
                    }
                }
            }

            for (int df = -1; df <= 1; df += 2)
            {
                var cap = new ChessSquare(from.File + df, from.Rank + dir);
                if (!cap.IsValid) continue;

                var target = state.Get(cap);
                if (target != null && target.Color != piece.Color)
                {
                    if (cap.Rank == promoRank)
                    {
                        TryAdd(state, from, cap, ChessPieceType.Queen, outMoves);
                        TryAdd(state, from, cap, ChessPieceType.Rook, outMoves);
                        TryAdd(state, from, cap, ChessPieceType.Bishop, outMoves);
                        TryAdd(state, from, cap, ChessPieceType.Knight, outMoves);
                    }
                    else
                        TryAdd(state, from, cap, null, outMoves);
                }
                else if (state.EnPassantTarget.HasValue && state.EnPassantTarget.Value == cap)
                {
                    TryAdd(state, from, cap, null, outMoves);
                }
            }
        }

        static void GenKnight(ChessBoardState state, ChessSquare from, ChessPiece piece, List<ScoredMove> outMoves)
        {
            for (int i = 0; i < 8; i++)
            {
                var to = new ChessSquare(from.File + KnightDf[i], from.Rank + KnightDr[i]);
                if (!to.IsValid) continue;
                var t = state.Get(to);
                if (t == null || t.Color != piece.Color)
                    TryAdd(state, from, to, null, outMoves);
            }
        }

        static void GenSlide(ChessBoardState state, ChessSquare from, ChessPiece piece,
            List<ScoredMove> outMoves, bool diag, bool ortho)
        {
            void Ray(int df, int dr)
            {
                int f = from.File + df;
                int r = from.Rank + dr;
                while (f >= 0 && f < 8 && r >= 0 && r < 8)
                {
                    var to = new ChessSquare(f, r);
                    var t = state.Get(to);
                    if (t == null)
                        TryAdd(state, from, to, null, outMoves);
                    else
                    {
                        if (t.Color != piece.Color)
                            TryAdd(state, from, to, null, outMoves);
                        break;
                    }
                    f += df;
                    r += dr;
                }
            }

            if (ortho) { Ray(1, 0); Ray(-1, 0); Ray(0, 1); Ray(0, -1); }
            if (diag) { Ray(1, 1); Ray(1, -1); Ray(-1, 1); Ray(-1, -1); }
        }

        static void GenKing(ChessBoardState state, ChessSquare from, ChessPiece piece, List<ScoredMove> outMoves)
        {
            for (int i = 0; i < 8; i++)
            {
                var to = new ChessSquare(from.File + KingDf[i], from.Rank + KingDr[i]);
                if (!to.IsValid) continue;
                var t = state.Get(to);
                if (t == null || t.Color != piece.Color)
                    TryAdd(state, from, to, null, outMoves);
            }

            if (IsInCheck(state, piece.Color)) return;
            if (piece.Color == ChessColor.White)
            {
                if (state.WhiteCastleKing && CanCastle(state, from, 1))
                    TryAdd(state, from, new ChessSquare(6, 0), null, outMoves);
                if (state.WhiteCastleQueen && CanCastle(state, from, -1))
                    TryAdd(state, from, new ChessSquare(2, 0), null, outMoves);
            }
            else
            {
                if (state.BlackCastleKing && CanCastle(state, from, 1))
                    TryAdd(state, from, new ChessSquare(6, 7), null, outMoves);
                if (state.BlackCastleQueen && CanCastle(state, from, -1))
                    TryAdd(state, from, new ChessSquare(2, 7), null, outMoves);
            }
        }

        static bool CanCastle(ChessBoardState state, ChessSquare kingFrom, int dir)
        {
            int rank = kingFrom.Rank;
            int rookFile = dir > 0 ? 7 : 0;
            var rook = state.Get(new ChessSquare(rookFile, rank));
            if (rook == null || rook.Type != ChessPieceType.Rook || rook.Color != state.Get(kingFrom).Color)
                return false;

            int start = Math.Min(kingFrom.File, rookFile) + 1;
            int end = Math.Max(kingFrom.File, rookFile);
            for (int f = start; f < end; f++)
                if (state.Get(new ChessSquare(f, rank)) != null) return false;

            var color = state.Get(kingFrom).Color;
            for (int step = 1; step <= 2; step++)
            {
                var sq = new ChessSquare(kingFrom.File + dir * step, rank);
                var trial = state.Clone();
                ApplyRaw(trial, kingFrom, sq, null, out _);
                if (IsInCheck(trial, color)) return false;
            }
            return true;
        }

        public static bool TryMove(ChessBoardState state, ChessSquare from, ChessSquare to,
            ChessPieceType? promo, out ChessMoveRecord record)
        {
            record = null;
            var legal = GetLegalMovesFrom(state, from);
            bool ok = false;
            ChessPieceType? usePromo = promo;
            foreach (var m in legal)
            {
                if (m.From == from && m.To == to)
                {
                    if (promo.HasValue && m.Promotion != promo) continue;
                    usePromo = m.Promotion;
                    ok = true;
                    break;
                }
            }
            if (!ok)
            {
                foreach (var m in legal)
                {
                    if (m.From == from && m.To == to)
                    {
                        usePromo = m.Promotion ?? promo;
                        ok = true;
                        break;
                    }
                }
            }
            if (!ok) return false;

            ApplyRaw(state, from, to, usePromo, out record);
            state.SelectedSquare = null;
            state.LastFrom = from.ToAlgebraic();
            state.LastTo = to.ToAlgebraic();
            state.Phase = ChessPhase.Idle;

            state.SideToMove = state.SideToMove == ChessColor.White ? ChessColor.Black : ChessColor.White;
            if (state.SideToMove == ChessColor.White)
                state.FullmoveNumber++;

            return true;
        }

        static void ApplyRaw(ChessBoardState state, ChessSquare from, ChessSquare to,
            ChessPieceType? promo, out ChessMoveRecord record)
        {
            var piece = state.Get(from);
            var captured = state.Get(to);
            bool enPassant = false;
            bool castle = false;

            if (piece.Type == ChessPieceType.Pawn && state.EnPassantTarget.HasValue
                && to == state.EnPassantTarget.Value && captured == null)
            {
                int capRank = piece.Color == ChessColor.White ? to.Rank - 1 : to.Rank + 1;
                var capSq = new ChessSquare(to.File, capRank);
                captured = state.Get(capSq);
                state.Set(capSq, null);
                enPassant = true;
            }

            if (piece.Type == ChessPieceType.King && Math.Abs(to.File - from.File) == 2)
            {
                castle = true;
                int rank = from.Rank;
                if (to.File == 6)
                {
                    var rook = state.Get(new ChessSquare(7, rank));
                    state.Set(new ChessSquare(7, rank), null);
                    state.Set(new ChessSquare(5, rank), rook);
                    if (rook != null) rook.HasMoved = true;
                }
                else
                {
                    var rook = state.Get(new ChessSquare(0, rank));
                    state.Set(new ChessSquare(0, rank), null);
                    state.Set(new ChessSquare(3, rank), rook);
                    if (rook != null) rook.HasMoved = true;
                }
            }

            state.Set(to, piece);
            state.Set(from, null);
            piece.HasMoved = true;

            if (piece.Type == ChessPieceType.Pawn && (to.Rank == 7 || to.Rank == 0))
                piece.Type = promo ?? ChessPieceType.Queen;

            state.EnPassantTarget = null;
            if (piece.Type == ChessPieceType.Pawn && Math.Abs(to.Rank - from.Rank) == 2)
            {
                int mid = (from.Rank + to.Rank) / 2;
                state.EnPassantTarget = new ChessSquare(from.File, mid);
            }

            if (piece.Type == ChessPieceType.King)
            {
                if (piece.Color == ChessColor.White) { state.WhiteCastleKing = false; state.WhiteCastleQueen = false; }
                else { state.BlackCastleKing = false; state.BlackCastleQueen = false; }
            }
            if (piece.Type == ChessPieceType.Rook)
            {
                if (piece.Color == ChessColor.White)
                {
                    if (from.File == 0 && from.Rank == 0) state.WhiteCastleQueen = false;
                    if (from.File == 7 && from.Rank == 0) state.WhiteCastleKing = false;
                }
                else
                {
                    if (from.File == 0 && from.Rank == 7) state.BlackCastleQueen = false;
                    if (from.File == 7 && from.Rank == 7) state.BlackCastleKing = false;
                }
            }
            if (captured != null && captured.Type == ChessPieceType.Rook)
            {
                if (to.File == 0 && to.Rank == 0) state.WhiteCastleQueen = false;
                if (to.File == 7 && to.Rank == 0) state.WhiteCastleKing = false;
                if (to.File == 0 && to.Rank == 7) state.BlackCastleQueen = false;
                if (to.File == 7 && to.Rank == 7) state.BlackCastleKing = false;
            }

            state.HalfmoveClock = (piece.Type == ChessPieceType.Pawn || captured != null)
                ? 0 : state.HalfmoveClock + 1;

            record = new ChessMoveRecord
            {
                From = from,
                To = to,
                Promotion = promo,
                Captured = captured,
                WasCastle = castle,
                WasEnPassant = enPassant,
                FromAlg = from.ToAlgebraic(),
                ToAlg = to.ToAlgebraic()
            };
        }

        public static bool IsInCheck(ChessBoardState state, ChessColor color)
        {
            ChessSquare kingSq = default;
            bool found = false;
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    var p = state.Board[r][f];
                    if (p != null && p.Type == ChessPieceType.King && p.Color == color)
                    {
                        kingSq = new ChessSquare(f, r);
                        found = true;
                        break;
                    }
                }
            if (!found) return true;
            return IsSquareAttacked(state, kingSq, color == ChessColor.White ? ChessColor.Black : ChessColor.White);
        }

        public static bool IsSquareAttacked(ChessBoardState state, ChessSquare sq, ChessColor byColor)
        {
            int pDir = byColor == ChessColor.White ? 1 : -1;
            for (int df = -1; df <= 1; df += 2)
            {
                var ps = new ChessSquare(sq.File + df, sq.Rank - pDir);
                if (!ps.IsValid) continue;
                var p = state.Get(ps);
                if (p != null && p.Color == byColor && p.Type == ChessPieceType.Pawn) return true;
            }

            for (int i = 0; i < 8; i++)
            {
                var ns = new ChessSquare(sq.File + KnightDf[i], sq.Rank + KnightDr[i]);
                if (!ns.IsValid) continue;
                var p = state.Get(ns);
                if (p != null && p.Color == byColor && p.Type == ChessPieceType.Knight) return true;
            }

            for (int i = 0; i < 8; i++)
            {
                var ks = new ChessSquare(sq.File + KingDf[i], sq.Rank + KingDr[i]);
                if (!ks.IsValid) continue;
                var p = state.Get(ks);
                if (p != null && p.Color == byColor && p.Type == ChessPieceType.King) return true;
            }

            if (AttackedBySlider(state, sq, byColor, true, false)) return true;
            if (AttackedBySlider(state, sq, byColor, false, true)) return true;
            return false;
        }

        static bool AttackedBySlider(ChessBoardState state, ChessSquare sq, ChessColor byColor, bool diag, bool ortho)
        {
            bool Ray(int df, int dr, ChessPieceType single, ChessPieceType queen)
            {
                int f = sq.File + df;
                int r = sq.Rank + dr;
                while (f >= 0 && f < 8 && r >= 0 && r < 8)
                {
                    var p = state.Board[r][f];
                    if (p != null)
                    {
                        if (p.Color == byColor && (p.Type == single || p.Type == queen))
                            return true;
                        return false;
                    }
                    f += df;
                    r += dr;
                }
                return false;
            }

            if (ortho)
            {
                if (Ray(1, 0, ChessPieceType.Rook, ChessPieceType.Queen)) return true;
                if (Ray(-1, 0, ChessPieceType.Rook, ChessPieceType.Queen)) return true;
                if (Ray(0, 1, ChessPieceType.Rook, ChessPieceType.Queen)) return true;
                if (Ray(0, -1, ChessPieceType.Rook, ChessPieceType.Queen)) return true;
            }
            if (diag)
            {
                if (Ray(1, 1, ChessPieceType.Bishop, ChessPieceType.Queen)) return true;
                if (Ray(1, -1, ChessPieceType.Bishop, ChessPieceType.Queen)) return true;
                if (Ray(-1, 1, ChessPieceType.Bishop, ChessPieceType.Queen)) return true;
                if (Ray(-1, -1, ChessPieceType.Bishop, ChessPieceType.Queen)) return true;
            }
            return false;
        }

        public static void EvaluateTerminal(ChessBoardState state)
        {
            var moves = GetAllLegalMoves(state);
            if (moves.Count > 0) return;

            if (IsInCheck(state, state.SideToMove))
            {
                state.IsGameOver = true;
                state.Phase = ChessPhase.GameOver;
                var winner = state.SideToMove == ChessColor.White ? "Black" : "White";
                state.ResultText = $"Checkmate — {winner} wins";
            }
            else
            {
                state.IsGameOver = true;
                state.Phase = ChessPhase.GameOver;
                state.ResultText = "Stalemate — draw";
            }
        }
    }
}