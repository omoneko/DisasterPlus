namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// ③火災旋風の設定値。Game 層の ModSettings から詰め替えて Core に渡す。
    /// Core は SavedInt を知らないので、ここは素の値だけを持つ。
    /// </summary>
    public class FireWhirlConfig
    {
        /// <summary>発生判定の半径（メートル）。この距離内に DetectCount 棟あれば発生。</summary>
        public float DetectRadius;

        /// <summary>発生判定の棟数。</summary>
        public int DetectCount;

        /// <summary>候補統合と多重発生抑制に使う最小離隔距離（メートル）。</summary>
        public float MinSeparation;

        /// <summary>絶対上限の寿命（ゲーム内分）。これを超えたら必ず打ち切る。</summary>
        public float MaxLifetimeMinutes;

        /// <summary>
        /// 発生条件を割り込んでから消滅するまでの猶予（ゲーム内分）。
        ///
        /// 1 ゲーム内分 = 65536 / 1440 ≒ 45.5 sim フレーム（SimulationManager.DAYTIME_FRAMES）。
        /// 燃焼中建物の走査は 8 tick で 1 周し、ゲーム速度 3 では 1 tick = 9 フレームなので
        /// 1 周に最悪 72 フレーム ≒ 1.6 分かかる。猶予がこれを下回ると、走査 1 周ぶんの
        /// 古い結果だけで旋風が消えてしまう。走査 1 周の 2 倍近い余裕を既定にする。
        /// </summary>
        public float ConditionGraceMinutes;

        /// <summary>延焼拡大の強さ。0 で延焼拡大なし、10 が最大。</summary>
        public int SpreadStrength;

        public static FireWhirlConfig Defaults()
        {
            return new FireWhirlConfig
            {
                DetectRadius = 150f,
                DetectCount = 12,
                MinSeparation = 300f,
                MaxLifetimeMinutes = 10f,
                ConditionGraceMinutes = 3f,
                SpreadStrength = 3,
            };
        }
    }
}
