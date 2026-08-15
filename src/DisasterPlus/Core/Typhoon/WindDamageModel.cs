namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 風による倒壊確率。**これは本 MOD が発明した物理であって、バニラにも
    /// 対応する量が無い。**
    ///
    /// バニラには風による破壊機構が 1 つも無く、**風速を上げるフィールドすら存在しない**
    /// （IL 事実文書 §A-5 / §B5）。風速の読み手は風力発電の発電量・木と道路の揺れ・
    /// 霧と雲のスクロール・ハザードマップの補正だけで、そのどれも建物を壊さない。
    /// <c>DisasterHelpers.AddWind</c> ですら市民と車両を押すだけで無傷である。
    /// つまりここにあるのは「可視化」ではなく「発明」である。
    ///
    /// **単位は無い。** wind は [0,1] の係数、返り値は 1 回の走査あたりの確率。
    /// **実在の風速（m/s）には対応しない**（設計書 §7.3。②が気象庁震度階級を
    /// 名乗らなかったのと同じ理由）。呼び出し側はこの値を m/s として表示しないこと。
    ///
    /// 高さの扱いが②の <c>LongPeriodResponse</c> と違う点に注意する。あちらは高さが
    /// 読めなければ何もしなかった（「高層ほど壊れる」が前提だから）が、台風は平屋も
    /// 飛ばす。ここでは高さは**係数**であり、読めなければボーナスを辞退するだけで
    /// 対象からは外さない。高さを推測しているわけではない。
    ///
    /// 乱数はここには無い。抽選は呼び出し側（<c>Game/Typhoon/TyphoonWind</c>）が
    /// <c>DeterministicRandom</c> で行う —— <c>VanillaRandomizer</c> は使わない。
    /// ここで決めるのはバニラが引く値ではなく④が発明した判断である。
    /// </summary>
    public static class WindDamageModel
    {
        /// <summary>
        /// これ未満の風速相当は完全に無視する。**「強風域の縁で家が飛ぶ」を作らない。**
        /// </summary>
        public const float MinWind = 0.25f;

        /// <summary>
        /// 高さボーナスを掛ける前の、1 回の走査あたりの倒壊確率の上限。
        ///
        /// バニラの全体円盤（地震）が 0.02、②の長周期が 0.25 なのに対して中間に置いた。
        /// 走査は 256 フレームに 1 回なので、台風の全期間では十分に積み上がる。
        /// **物理定数ではない。** ④が選んだ数字である。
        /// </summary>
        public const float MaxCollapseChance = 0.05f;

        /// <summary>高さが天井に達したときの上乗せ率（＝係数 1.6 倍）。</summary>
        public const float HeightBonus = 0.6f;

        /// <summary>ここまでは高さボーナス無し（係数 1.0）。</summary>
        public const float HeightFloorMetres = 10f;

        /// <summary>ここから上は伸びない。**超高層 1 棟が確率 1.0 にならないための天井。**</summary>
        public const float HeightCeilingMetres = 70f;

        /// <summary>強さスライダーの最大値（0〜10）。</summary>
        private const float MaxStrength = 10f;

        /// <summary>
        /// 建物の高さ（m）から求める係数。
        ///
        /// <paramref name="heightMetres"/> が 0（＝<c>BuildingHeight.MetresOf</c> が
        /// 「読めなかった」と答えた）なら **1.0（ボーナス無し）** を返す。これは
        /// 高さを推測しているのではなく、**ボーナスの適用を辞退している**（クラス doc）。
        /// </summary>
        public static float HeightFactor(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= HeightFloorMetres) return 1f;
            if (heightMetres >= HeightCeilingMetres) return 1f + HeightBonus;

            float t = (heightMetres - HeightFloorMetres)
                      / (HeightCeilingMetres - HeightFloorMetres);
            return 1f + HeightBonus * t;
        }

        /// <summary>
        /// 1 回の走査あたりの倒壊確率。
        ///
        /// <paramref name="strength"/> は設定の 0〜10。**0 で厳密に 0 を返す**
        /// （スライダーで完全に無効化できることの保証）。<c>.cgs</c> は公開契約なので
        /// 範囲外の値もここでクランプする。
        ///
        /// 壊れた入力（NaN・負）は 0 を返す。**「風速 NaN で全棟倒壊」を作らない。**
        /// </summary>
        public static float CollapseChance(float wind, float heightMetres, int strength)
        {
            if (strength <= 0) return 0f;
            if (strength > (int)MaxStrength) strength = (int)MaxStrength;

            if (float.IsNaN(wind) || wind <= MinWind) return 0f;
            if (wind > 1f) wind = 1f;

            // ★ 高さ 0 は「不明」で、ボーナスを辞退したうえで対象に残す（クラス doc）。
            //   高さ NaN は「不明」ではなく**壊れた読み取り**なので、こちらは弾く。
            //   HeightFactor が NaN に 1.0 を返すのはそれ自体が意味のある既定値だが、
            //   壊れた値を黙って既定値へ丸めて建物を倒すのは別の話である。
            if (float.IsNaN(heightMetres) || heightMetres < 0f) return 0f;

            float excess = (wind - MinWind) / (1f - MinWind);
            float chance = excess * HeightFactor(heightMetres)
                           * (strength / MaxStrength) * MaxCollapseChance;

            // 上限は「高さボーナスを掛け切った値」。ここを MaxCollapseChance に
            // すると高さの差が上限で潰れ、TallerBuildingsCatchMoreWind が意味を失う。
            float ceiling = MaxCollapseChance * (1f + HeightBonus);
            if (float.IsNaN(chance) || chance <= 0f) return 0f;
            return chance > ceiling ? ceiling : chance;
        }
    }
}
