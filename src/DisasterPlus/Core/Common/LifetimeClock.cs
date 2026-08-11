namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// ゲーム内時間（分）の経過を貯める不変の時計。
    /// 実時間ではない。ポーズ中は Advance(0) が呼ばれるだけで何も進まない。
    /// </summary>
    public struct LifetimeClock
    {
        public readonly float ElapsedMinutes;

        private LifetimeClock(float elapsed)
        {
            ElapsedMinutes = elapsed;
        }

        public static LifetimeClock Start()
        {
            return new LifetimeClock(0f);
        }

        /// <summary>
        /// 経過を足した新しい時計を返す。負の delta は無視する
        /// （セーブロードやポーズ解除でフレーム差が巻き戻ることがあるため）。
        /// </summary>
        public LifetimeClock Advance(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return this;
            return new LifetimeClock(ElapsedMinutes + deltaMinutes);
        }
    }
}
