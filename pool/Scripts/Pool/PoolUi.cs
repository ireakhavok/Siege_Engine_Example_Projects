using SiegeEngine.Scenes;

namespace PoolProject
{
    // HTML open lives in PoolMenu. This file exists so an older PoolUi.cs that named PlayType
    // is replaced by a type the scene already has.
    public static class PoolUi
    {
        public static PlayType Mode
        {
            get
            {
                if (PoolMenu.Game == PoolGame.Nine) return PlayType.Nine;
                if (PoolMenu.Game == PoolGame.Cutthroat) return PlayType.Cutthroat;
                return PlayType.Eight;
            }
            set
            {
                PoolMenu.Game = value == PlayType.Nine ? PoolGame.Nine
                    : value == PlayType.Cutthroat ? PoolGame.Cutthroat
                    : PoolGame.Eight;
            }
        }

        public static void Push(SceneContext context) => PoolMenu.Push(context);

        public static void Close(SceneContext context) => PoolMenu.Close(context);
    }
}
