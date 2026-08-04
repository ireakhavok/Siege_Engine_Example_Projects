using System;
using System.Collections.Generic;

namespace CheckersProject
{
    public enum CheckersColor { White, Black }
    public enum CheckersPieceType { Man, King }
    public enum CheckersMode { VsAi, Hotseat }
    public enum CheckersPhase { Idle, PieceSelected, AiThinking, GameOver }
    public enum CheckersAiDifficulty { Easy, Normal, Hard }

    public sealed class CheckersPiece
    {
        public CheckersPieceType Type;
        public CheckersColor Color;

        public CheckersPiece(CheckersPieceType type, CheckersColor color)
        {
            Type = type;
            Color = color;
        }

        public CheckersPiece Clone() => new CheckersPiece(Type, Color);
    }

    public readonly struct CheckersSquare : IEquatable<CheckersSquare>
    {
        public readonly int File; // 0-7
        public readonly int Rank; // 0-7

        public CheckersSquare(int file, int rank)
        {
            File = file;
            Rank = rank;
        }

        public bool IsValid => File >= 0 && File < 8 && Rank >= 0 && Rank < 8;
        public bool IsDark => ((File + Rank) & 1) == 1; // dark squares only

        public string ToAlgebraic() => $"{(char)('a' + File)}{(char)('1' + Rank)}";

        public static bool TryParse(string s, out CheckersSquare sq)
        {
            sq = default;
            if (string.IsNullOrEmpty(s) || s.Length < 2) return false;
            int f = char.ToLowerInvariant(s[0]) - 'a';
            int r = s[1] - '1';
            if (f < 0 || f > 7 || r < 0 || r > 7) return false;
            sq = new CheckersSquare(f, r);
            return true;
        }

        public bool Equals(CheckersSquare other) => File == other.File && Rank == other.Rank;
        public override bool Equals(object obj) => obj is CheckersSquare s && Equals(s);
        public override int GetHashCode() => (File << 3) | Rank;
        public static bool operator ==(CheckersSquare a, CheckersSquare b) => a.Equals(b);
        public static bool operator !=(CheckersSquare a, CheckersSquare b) => !a.Equals(b);
    }

    public sealed class CheckersMoveRecord
    {
        public CheckersSquare From;
        public CheckersSquare To;
        public List<CheckersSquare> CapturedSquares = new();
        public bool WasPromotion;
        public string FromAlg;
        public string ToAlg;
    }

    public sealed class CheckersBoardState
    {
        public CheckersPiece[][] Board = new CheckersPiece[8][];
        public CheckersColor SideToMove = CheckersColor.White;
        public CheckersColor HumanColor = CheckersColor.White;
        public CheckersMode Mode = CheckersMode.VsAi;
        public CheckersPhase Phase = CheckersPhase.Idle;

        public string SelectedSquare;
        public string LastFrom;
        public string LastTo;
        public bool IsGameOver;
        public string ResultText;

        // Multi-jump continuation: when non-null, only this piece may move again
        public CheckersSquare? ContinuationFrom;

        public CheckersBoardState()
        {
            for (int r = 0; r < 8; r++)
                Board[r] = new CheckersPiece[8];
        }

        public CheckersPiece Get(CheckersSquare sq) =>
            sq.IsValid ? Board[sq.Rank][sq.File] : null;

        public void Set(CheckersSquare sq, CheckersPiece p)
        {
            if (sq.IsValid) Board[sq.Rank][sq.File] = p;
        }

        public static CheckersBoardState CreateStartingPosition()
        {
            var b = new CheckersBoardState();
            for (int r = 0; r < 3; r++)
                for (int f = 0; f < 8; f++)
                    if (((f + r) & 1) == 1)
                        b.Board[r][f] = new CheckersPiece(CheckersPieceType.Man, CheckersColor.White);

            for (int r = 5; r < 8; r++)
                for (int f = 0; f < 8; f++)
                    if (((f + r) & 1) == 1)
                        b.Board[r][f] = new CheckersPiece(CheckersPieceType.Man, CheckersColor.Black);

            return b;
        }

        public CheckersBoardState Clone()
        {
            var c = new CheckersBoardState
            {
                SideToMove = SideToMove,
                HumanColor = HumanColor,
                Mode = Mode,
                Phase = Phase,
                SelectedSquare = SelectedSquare,
                LastFrom = LastFrom,
                LastTo = LastTo,
                IsGameOver = IsGameOver,
                ResultText = ResultText,
                ContinuationFrom = ContinuationFrom
            };
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                    c.Board[r][f] = Board[r][f]?.Clone();
            return c;
        }
    }

    public readonly struct ScoredMove
    {
        public readonly CheckersSquare From;
        public readonly CheckersSquare To;
        public readonly int Score;
        // For multi-jump paths the AI stores the full sequence as successive single jumps

        public ScoredMove(CheckersSquare from, CheckersSquare to, int score)
        {
            From = from;
            To = to;
            Score = score;
        }
    }
}