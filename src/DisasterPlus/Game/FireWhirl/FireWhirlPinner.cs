namespace DisasterPlus.Game
{
    public static class FireWhirlPinner
    {
        public static void BeginEnding(FireWhirlView v)
        {
            // Task 11 で実装する。いまは記録だけして消す。
            FireWhirlRegistry.MarkEnding(v.DisasterId);
        }
    }
}
