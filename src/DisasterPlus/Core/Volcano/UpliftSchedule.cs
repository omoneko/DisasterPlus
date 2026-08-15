using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 隆起の進み方。**increment ではなく、その時刻における絶対目標を返す。**
    ///
    /// 素朴な <c>raw[i] += step</c> は必ず無言で壊れる。RawHeights は ushort の raw/64 m で、
    /// 書き込み側は <c>if (n != raw)</c> で省略する（§C-8）。1 tick の変化がセル高さで
    /// 1/64 m = 0.015625 m を下回ると**丸めで消え、そのセルは永久に動かない**。
    /// 外周ほど遅く上がるので、素朴な実装では**裾だけが最初から完全停止する**。
    /// 例外は 1 つも出ない。
    ///
    /// 絶対目標にすると、まだ 1 raw 単位に届かないセルは「書かれないだけ」で、
    /// 進捗は progress という float に蓄積されている。届いた瞬間に 1 段上がる。
    ///
    /// **それでも山頂だけは毎 tick 動かなければならない。** 動かなければ隆起そのものが
    /// 止まって見えるからで、<see cref="TotalTicksFor"/> が要求 tick 数を H×64 で
    /// 切り詰めることで構造的に保証している。**この切り詰めを外さないこと。**
    ///
    /// <see cref="ActiveRadiusMetres"/> は⑤全体でいちばん重要な 1 行である。
    /// **準備（道路と建物の破壊）が届いていない場所を持ち上げてはいけない。**
    /// 道路は m_flattenTerrain のとき Heights.PrimaryLevel でセルを道路の y に
    /// **強制固定**し、建物は SecondaryLevel で自分の y に固定する。しかもそれは
    /// 毎フラッシュゼロからやり直される（§A-2）ので、**書き続けても勝てない**。
    /// 準備を先にしないと、山の中に平らな溝とすり鉢が残る（設計書 §1.2）。
    /// clearedRadius が 0（＝まだ 1 本も壊していない）なら 0 を返す。
    /// **これを「制限なし」に読み替えてはいけない。**
    ///
    /// <see cref="BlockHeightCatchUpFrames"/> は「建てられる地面」と水位が遅れる量である。
    /// m_blockHeights はゲームモードで上へ 2 m / 64 sim フレームしか動かず（§A-2）、
    /// WaterSimulation.m_heightBuffer はその配列そのもの（§A-4）。**これは不具合ではない。**
    /// 呼び出し側はこの値をプレイヤーに説明すること（設計書 §7.3）。
    /// </summary>
    public static class UpliftSchedule
    {
        /// <summary><c>RawHeights</c> の表現上限。**キャストの前に必ずここへクランプする。**</summary>
        public const int MaxRaw = 65535;

        /// <summary>1 メートルあたりの raw 単位（<see cref="VolcanoShape.RawUnitsPerMetre"/> と同値）。</summary>
        public const float RawUnitsPerMetre = 64f;

        /// <summary>
        /// <c>m_blockHeights</c> が 1 周期で上へ動ける raw 量（§A-2、ゲームモード）。
        /// 128 raw = 2 m。エディタでは 512 raw = 8 m だが、⑤はゲームモードだけを名乗る。
        /// </summary>
        public const int BlockHeightRiseRawPerCycle = 128;

        /// <summary><c>m_blockHeights</c> の 1 周期（sim フレーム）。§A-2 の 1/64 行走査。</summary>
        public const int BlockHeightCycleFrames = 64;

        /// <summary>
        /// 隆起に使う tick 数。**要求値を H×64 で切り詰める**（罠 2）。
        ///
        /// H メートルの山は最大 H×64 raw 単位ぶんしか刻めない。それより細かく刻むと
        /// 山頂の 1 tick あたりの変化が 1 raw 単位を割り、丸めで消えて隆起が
        /// 無言で停止する。**この切り詰めを「余計なお世話」だと思って外さないこと。**
        /// </summary>
        public static int TotalTicksFor(float heightMetres, int requestedTicks)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f)
            {
                return requestedTicks > 1 ? requestedTicks : 1;
            }

            int maxTicks = (int)(heightMetres * RawUnitsPerMetre);
            if (maxTicks < 1) maxTicks = 1;

            if (requestedTicks < 1) return 1;
            return requestedTicks > maxTicks ? maxTicks : requestedTicks;
        }

        /// <summary>tick 番号 → [0,1] の進捗。<paramref name="totalTicks"/> が 0 以下なら 0。</summary>
        public static float ProgressAt(int tick, int totalTicks)
        {
            if (totalTicks <= 0) return 0f;
            if (tick <= 0) return 0f;
            if (tick >= totalTicks) return 1f;
            return tick / (float)totalTicks;
        }

        /// <summary>
        /// このセルの、この時刻における**絶対目標** raw 高さ。
        ///
        /// <paramref name="progress"/> は [0,1] へクランプするが、
        /// <paramref name="profileMetres"/> は**負を許す**（火口を彫る側が使う）。
        /// 返り値は必ず [0, <see cref="MaxRaw"/>]。**ushort へのキャストの前にクランプする**
        /// —— しないと山頂が海面に巻き戻る。
        /// </summary>
        public static ushort RawTargetAt(ushort baseRaw, float profileMetres, float progress)
        {
            if (float.IsNaN(profileMetres) || float.IsNaN(progress)) return baseRaw;

            float p = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            int delta = (int)Math.Round((double)profileMetres * p * RawUnitsPerMetre);

            int v = baseRaw + delta;
            if (v < 0) v = 0;
            if (v > MaxRaw) v = MaxRaw;
            return (ushort)v;
        }

        /// <summary>
        /// **⑤全体でいちばん重要な 1 行**（罠 1）。隆起してよい半径（m）。
        ///
        /// <paramref name="clearedRadiusMetres"/> に渡してよいのは
        /// <c>VolcanoClearing.ClearedRadiusMetres</c> だけである（計画 T5→T6 の型の縛り）。
        /// **まだ 1 本も壊していない（0 以下・NaN）なら 0 を返す。**
        /// これを「制限なし」に読み替えると、隆起がいきなり全域を持ち上げ、
        /// 道路と建物が毎フラッシュ押し戻して山の中に平らな溝とすり鉢が残る。
        /// </summary>
        public static float ActiveRadiusMetres(float shapeRadiusMetres, float clearedRadiusMetres)
        {
            if (float.IsNaN(shapeRadiusMetres) || float.IsNaN(clearedRadiusMetres)) return 0f;
            if (shapeRadiusMetres <= 0f || clearedRadiusMetres <= 0f) return 0f;
            return shapeRadiusMetres < clearedRadiusMetres ? shapeRadiusMetres : clearedRadiusMetres;
        }

        /// <summary>
        /// 準備（破壊）の前線が今どこまで行っているべきか（m）。
        /// 隆起の前線（<c>shapeRadius × progress</c>）より <paramref name="leadMetres"/> だけ先行し、
        /// 山の半径を超えない。**決して後ろへ下がらない。**
        /// </summary>
        public static float ClearingFrontMetres(float shapeRadiusMetres, float progress,
                                                float leadMetres)
        {
            if (float.IsNaN(shapeRadiusMetres) || float.IsNaN(progress)) return 0f;
            if (shapeRadiusMetres <= 0f) return 0f;

            float p = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            float lead = float.IsNaN(leadMetres) || leadMetres < 0f ? 0f : leadMetres;

            float front = shapeRadiusMetres * p + lead;
            return front > shapeRadiusMetres ? shapeRadiusMetres : front;
        }

        /// <summary>
        /// 「建てられる地面」と水位が追いつくまでの sim フレーム数。
        ///
        /// <c>m_blockHeights</c> はゲームモードで上へ 2 m / 64 sim フレームしか動かず（§A-2）、
        /// 水シミュの <c>m_heightBuffer</c> はその配列そのものである（§A-4）。
        /// **これは不具合ではない。** 呼び出し側はこの値をプレイヤーに説明すること。
        /// </summary>
        public static int BlockHeightCatchUpFrames(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f) return 0;

            float metresPerCycle = BlockHeightRiseRawPerCycle / RawUnitsPerMetre;
            int cycles = (int)Math.Ceiling(heightMetres / metresPerCycle);
            return cycles * BlockHeightCycleFrames;
        }
    }
}
