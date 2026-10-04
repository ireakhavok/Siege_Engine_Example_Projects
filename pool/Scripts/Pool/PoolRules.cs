using System;

namespace PoolProject
{
    public enum PoolGame { Eight, Nine, Cutthroat }

    public enum PlayType { Eight, Nine, Cutthroat }

    public enum BallGroup { Open, Solids, Stripes, Eight }

    // Rules only. No positions, no contacts. The scene asks these questions after the solver sleeps.
    public static class PoolRules
    {
        public static int ObjectBalls(PoolGame game)
        {
            if (game == PoolGame.Nine) return 9;
            return 15;
        }

        public static bool Allows(PoolGame game, int players)
        {
            if (players < 1 || players > 4) return false;
            if (game == PoolGame.Cutthroat) return players == 3;
            return true;
        }

        public static BallGroup GroupOf(int number)
        {
            if (number == 8) return BallGroup.Eight;
            if (number >= 1 && number <= 7) return BallGroup.Solids;
            if (number >= 9 && number <= 15) return BallGroup.Stripes;
            return BallGroup.Open;
        }

        public static BallGroup Opposite(BallGroup group)
        {
            if (group == BallGroup.Solids) return BallGroup.Stripes;
            if (group == BallGroup.Stripes) return BallGroup.Solids;
            return BallGroup.Open;
        }

        public static bool IsStripe(int number)
        {
            return number >= 9 && number <= 15;
        }

        // Cutthroat: each player owns five balls. Pocketing an opponent's ball keeps the turn.
        // The last player with a ball still on the table wins.
        public static int CutthroatOwner(int number)
        {
            if (number >= 1 && number <= 5) return 0;
            if (number >= 6 && number <= 10) return 1;
            if (number >= 11 && number <= 15) return 2;
            return -1;
        }

        public static bool IsLegalFirstHit(PoolGame game, BallGroup mine, int lowestOnTable, int hitNumber)
        {
            if (hitNumber <= 0) return false;
            if (game == PoolGame.Nine) return hitNumber == lowestOnTable;
            if (game == PoolGame.Cutthroat) return true;
            if (mine == BallGroup.Open) return hitNumber != 8;
            if (mine == BallGroup.Eight) return hitNumber == 8;
            return GroupOf(hitNumber) == mine;
        }

        public static bool CountsForShooter(PoolGame game, BallGroup mine, int pocketed, int shooter)
        {
            if (game == PoolGame.Nine) return pocketed != 9;
            if (game == PoolGame.Cutthroat)
            {
                int owner = CutthroatOwner(pocketed);
                return owner >= 0 && owner != shooter;
            }
            if (pocketed == 8) return mine == BallGroup.Eight;
            if (mine == BallGroup.Open) return pocketed != 8;
            return GroupOf(pocketed) == mine;
        }
    }
}
