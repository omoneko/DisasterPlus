using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 前線に沿って撃つ<b>衝撃波（<c>DisasterHelpers.SplashWater</c>）</b>の割り当て。
    /// **エンジン非依存の純関数だけ。**
    ///
    /// ── なぜ水源だけでは足りないのか ─────────────────────────────
    ///
    /// 水源は<b>目標水位へ向かってじわじわ注ぐ</b>ので、「海面が上がった」は
    /// 作れても「波が来た」は作れない。バニラの <c>SplashWater</c> は
    /// <b>セルの水量に直接足す</b>（IL_099A–09C3、IMPACT 波）ので、
    /// これを前線に沿って円周上へ並べると<b>動く壁</b>に見える。
    ///
    /// ── ★★ 深さは「発の間隔」で割らなければならない ──────────────────
    ///
    /// <c>SplashWater</c> は<b>足し算</b>である。同じセルの上を前線が通るあいだに
    /// 何発も撃てば、その回数ぶん<b>そのまま積み上がる</b>。
    ///
    /// <code>
    ///   1 セルが浴びる発の数 ≒ (衝撃波の直径) / (1 発あたり前線が進む距離)
    /// </code>
    ///
    /// 前線が遅いほどこの数は増える。だから<b>1 発の深さをその数で割る</b>。
    /// 割らないと、前線を遅くした瞬間に水位が跳ね上がって海が壊れる。
    ///
    /// ★ 逆に、前線が 1 発で直径ぶん以上進むなら割ってはいけない（重なりが無い）。
    ///   だから比は 1 で頭打ちにする。
    /// </summary>
    public static class TsunamiSplash
    {
        /// <summary>1 発の衝撃波の半径（m）。**壁の厚み**である。</summary>
        public const float RadiusMetres = 700f;

        /// <summary>壁の高さ（そのときの持ち上がりに対する比）。</summary>
        public const float DepthFraction = 0.85f;

        /// <summary>
        /// 円周に並べるときの隣との重なり（直径に対する比）。
        /// 1 以上にすると隣との間に切れ目ができ、<b>輪ではなく点線</b>になる。
        /// </summary>
        public const float RingOverlap = 0.72f;

        /// <summary>1 回に撃つ数の上限。**円周が伸びても際限なく増やさない。**</summary>
        public const int MaxPerPulse = 48;

        /// <summary>同じく下限（前線が小さいうちでも輪に見えるように）。</summary>
        public const int MinPerPulse = 8;

        /// <summary>
        /// 1 発の深さ（m）。<paramref name="advanceMetres"/> は
        /// <b>前回の発からこの発までに前線が進んだ距離</b>。
        /// </summary>
        public static float DepthFor(float riseMetres, float advanceMetres)
        {
            if (IsBad(riseMetres) || riseMetres <= 0f) return 0f;
            if (IsBad(advanceMetres) || advanceMetres <= 0f) return 0f;

            float share = advanceMetres / (RadiusMetres * 2f);
            if (share > 1f) share = 1f;

            return riseMetres * DepthFraction * share;
        }

        /// <summary>半径 <paramref name="frontMetres"/> の円周に並べる発の数。</summary>
        public static int CountFor(float frontMetres)
        {
            if (IsBad(frontMetres) || frontMetres <= 0f) return MinPerPulse;

            float step = RadiusMetres * 2f * RingOverlap;
            int count = (int)Math.Ceiling(6.2831853f * frontMetres / step);

            if (count < MinPerPulse) return MinPerPulse;
            if (count > MaxPerPulse) return MaxPerPulse;
            return count;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
