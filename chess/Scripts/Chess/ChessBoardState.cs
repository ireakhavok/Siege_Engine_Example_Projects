using System;
using System.Collections.Generic;

namespace ChessProject
{
    public enum ChessColor { White, Black }
    public enum ChessPieceType { Pawn, Knight, Bishop, Rook, Queen, King }
    public enum ChessMode { VsAi, Hotseat }
    public enum ChessPhase { Idle, PieceSelected, AiThinking, GameOver }
    public enum ChessAiDifficulty { Easy, Normal, Hard }

    public sealed class ChessPiece
    {
        public ChessPieceType Type;
        public ChessColor Color;
        public bool HasMoved;

        public ChessPiece(ChessPieceType type, ChessColor color)
        {
            Type = type;
            Color = color;
        }

        public ChessPiece Clone() => new ChessPiece(Type, Color) { HasMoved = HasMoved };
    }

    public readonly struct ChessSquare : IEquatable<ChessSquare>
    {
        public readonly int File; // 0-7 = a-h
        public readonly int Rank; // 0-7 = 1-8

        public ChessSquare(int file, int rank)
        {
            File = file;
            Rank = rank;
        }

        public bool IsValid => File >= 0 && File < 8 && Rank >= 0 && Rank < 8;

        public string ToAlgebraic() => $"{(char)('a' + File)}{(char)('1' + Rank)}";

        public static bool TryParse(string s, out ChessSquare sq)
        {
            sq = default;
            if (string.IsNullOrEmpty(s) || s.Length < 2) return false;
            int f = char.ToLowerInvariant(s[0]) - 'a';
            int r = s[1] - '1';
            if (f < 0 || f > 7 || r < 0 || r > 7) return false;
            sq = new ChessSquare(f, r);
            return true;
        }

        public bool Equals(ChessSquare other) => File == other.File && Rank == other.Rank;
        public override bool Equals(object obj) => obj is ChessSquare s && Equals(s);
        public override int GetHashCode() => (File << 3) | Rank;
        public static bool operator ==(ChessSquare a, ChessSquare b) => a.Equals(b);
        public static bool operator !=(ChessSquare a, ChessSquare b) => !a.Equals(b);
    }

    public sealed class ChessMoveRecord
    {
        public ChessSquare From;
        public ChessSquare To;
        public ChessPieceType? Promotion;
        public ChessPiece Captured;
        public bool WasCastle;
        public bool WasEnPassant;
        public string FromAlg;
        public string ToAlg;
    }

    public sealed class ChessBoardState
    {
        // Board[rank][file]
        public ChessPiece[][] Board = new ChessPiece[8][];
        public ChessColor SideToMove = ChessColor.White;
        public ChessColor HumanColor = ChessColor.White;
        public ChessMode Mode = ChessMode.VsAi;
        public ChessPhase Phase = ChessPhase.Idle;

        public string SelectedSquare;
        public string LastFrom;
        public string LastTo;
        public ChessSquare? EnPassantTarget;
        public bool WhiteCastleKing = true;
        public bool WhiteCastleQueen = true;
        public bool BlackCastleKing = true;
        public bool BlackCastleQueen = true;
        public int HalfmoveClock;
        public int FullmoveNumber = 1;
        public bool IsGameOver;
        public string ResultText;

        public ChessBoardState()
        {
            for (int r = 0; r < 8; r++)
                Board[r] = new ChessPiece[8];
        }

        public ChessPiece Get(ChessSquare sq) =>
            sq.IsValid ? Board[sq.Rank][sq.File] : null;

        public void Set(ChessSquare sq, ChessPiece p)
        {
            if (sq.IsValid) Board[sq.Rank][sq.File] = p;
        }

        public static ChessBoardState CreateStartingPosition()
        {
            var b = new ChessBoardState();
            ChessPieceType[] back = {
                ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop,
                ChessPieceType.Queen, ChessPieceType.King,
                ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Rook
            };
            for (int f = 0; f < 8; f++)
            {
                b.Board[0][f] = new ChessPiece(back[f], ChessColor.White);
                b.Board[1][f] = new ChessPiece(ChessPieceType.Pawn, ChessColor.White);
                b.Board[6][f] = new ChessPiece(ChessPieceType.Pawn, ChessColor.Black);
                b.Board[7][f] = new ChessPiece(back[f], ChessColor.Black);
            }
            return b;
        }

        public ChessBoardState Clone()
        {
            var c = new ChessBoardState
            {
                SideToMove = SideToMove,
                HumanColor = HumanColor,
                Mode = Mode,
                Phase = Phase,
                SelectedSquare = SelectedSquare,
                LastFrom = LastFrom,
                LastTo = LastTo,
                EnPassantTarget = EnPassantTarget,
                WhiteCastleKing = WhiteCastleKing,
                WhiteCastleQueen = WhiteCastleQueen,
                BlackCastleKing = BlackCastleKing,
                BlackCastleQueen = BlackCastleQueen,
                HalfmoveClock = HalfmoveClock,
                FullmoveNumber = FullmoveNumber,
                IsGameOver = IsGameOver,
                ResultText = ResultText
            };
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                    c.Board[r][f] = Board[r][f]?.Clone();
            return c;
        }
    }

    public readonly struct ScoredMove
    {
        public readonly ChessSquare From;
        public readonly ChessSquare To;
        public readonly ChessPieceType? Promotion;
        public readonly int Score;

        public ScoredMove(ChessSquare from, ChessSquare to, ChessPieceType? promo, int score)
        {
            From = from;
            To = to;
            Promotion = promo;
            Score = score;
        }
    }
}