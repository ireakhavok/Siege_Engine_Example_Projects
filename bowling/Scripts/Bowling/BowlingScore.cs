using System;

namespace BowlingProject
{
    public sealed class BowlingScore
    {
        readonly int[] _rolls = new int[21];
        int _count;

        public int RollCount => _count;
        public bool IsComplete => FramesUsed() >= 10 && BonusSatisfied();

        public void Add(int pins)
        {
            if (_count >= _rolls.Length) return;
            if (pins < 0) pins = 0;
            if (pins > 10) pins = 10;
            _rolls[_count++] = pins;
        }

        public int RollAt(int index)
        {
            if (index < 0 || index >= _count) return -1;
            return _rolls[index];
        }

        public int Cumulative(int frame)
        {
            if (frame < 0 || frame > 9) return -1;
            int i = 0;
            int total = 0;
            for (int f = 0; f <= frame; f++)
            {
                if (i >= _count) return -1;
                if (f < 9)
                {
                    if (IsStrike(i))
                    {
                        if (i + 2 >= _count) return -1;
                        total += 10 + _rolls[i + 1] + _rolls[i + 2];
                        i += 1;
                    }
                    else
                    {
                        if (i + 1 >= _count) return -1;
                        int sum = _rolls[i] + _rolls[i + 1];
                        if (sum == 10)
                        {
                            if (i + 2 >= _count) return -1;
                            total += 10 + _rolls[i + 2];
                        }
                        else total += sum;
                        i += 2;
                    }
                }
                else
                {
                    if (!BonusSatisfied()) return -1;
                    int n = TenthRolls();
                    for (int k = 0; k < n; k++)
                        total += _rolls[i + k];
                }
            }
            return total;
        }

        public int Total()
        {
            int t = Cumulative(9);
            return t < 0 ? Partial() : t;
        }

        public int FrameStart(int frame)
        {
            int i = 0;
            for (int f = 0; f < frame && f < 10; f++)
            {
                if (i >= _count) return i;
                if (f < 9 && IsStrike(i)) i += 1;
                else i += 2;
            }
            return i;
        }

        int Partial()
        {
            int total = 0;
            int i = 0;
            for (int f = 0; f < 9; f++)
            {
                int c = Cumulative(f);
                if (c < 0) break;
                total = c;
                if (i >= _count) break;
                i = IsStrike(i) ? i + 1 : i + 2;
            }
            return total;
        }

        int FramesUsed()
        {
            int i = 0;
            int f = 0;
            while (f < 10 && i < _count)
            {
                if (f < 9 && IsStrike(i)) i += 1;
                else i += 2;
                f++;
            }
            return f;
        }

        bool BonusSatisfied()
        {
            int start = FrameStart(9);
            if (start >= _count) return false;
            if (_rolls[start] == 10) return _count >= start + 3;
            if (start + 1 >= _count) return false;
            if (_rolls[start] + _rolls[start + 1] == 10) return _count >= start + 3;
            return _count >= start + 2;
        }

        int TenthRolls()
        {
            int start = FrameStart(9);
            if (start >= _count) return 0;
            if (_rolls[start] == 10) return Math.Min(3, _count - start);
            if (start + 1 < _count && _rolls[start] + _rolls[start + 1] == 10)
                return Math.Min(3, _count - start);
            return Math.Min(2, _count - start);
        }

        bool IsStrike(int index) => index < _count && _rolls[index] == 10;
    }
}
