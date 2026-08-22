using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 稲妻 1 本の 1 点。<see cref="PlumeLightning"/> が組み立てる。
    /// </summary>
    public struct LightningPoint
    {
        /// <summary>火口を原点とした水平方向のずれ（m）。</summary>
        public readonly float X;

        /// <summary>火口からの高さ（m）。</summary>
        public readonly float Y;

        /// <summary>同上、もう 1 軸（m）。</summary>
        public readonly float Z;

        public LightningPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// 「高さの比 <c>[0,1]</c> → その高さでの柱の半径（m）」。
    /// <see cref="PlumeLightning.PathInto"/> が柱の形を知るための唯一の口である
    /// （Core から <c>EruptionColumn</c> の内部にも呼び出し側の状態にも触らせない）。
    /// </summary>
    public delegate float RadiusAtFraction(float heightFraction);

    /// <summary>
    /// **噴煙の中の雷。** <b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// > 噴煙の中で雷（噴石同士が当たって生じるやつ）が発生するのを再現してほしい
    ///
    /// 火山雷である。噴煙の中で火山灰と噴石がぶつかり合って電荷が分かれ、
    /// 柱の中で放電する —— **雲から地面へ落ちる普通の雷とは別の現象**で、
    /// 大半は<b>柱の内側で完結する</b>。だからここは地面まで届く経路を作らない。
    ///
    /// ── ★★ 状態を 1 つも持たない ─────────────────────────────
    ///
    /// <see cref="VolcanicTremor"/> と同じ作りである。時間を <see cref="SlotSeconds"/> の
    /// 枠に切り、**枠ごとに 1 本**、種と枠番号だけから形と時刻を決める。
    /// 「起きるか起きないか」を活動度で決めない —— 決めると<b>活動度が変わった瞬間に、
    /// もう光っている過去の閃光が消える</b>。代わりに<b>明るさ</b>を活動度に比例させ、
    /// 弱いときは<b>ほとんど見えない放電が時々ある</b>という姿にする。
    ///
    /// ── 形 ───────────────────────────────────────
    ///
    /// 折れ線 1 本。**柱の中の 2 点を結ぶ**（火山雷は柱内で完結する）。
    /// 途中の点は <see cref="JitterRatio"/> のぶん柱の半径方向へ振る。
    /// 枝分かれは作らない（頂点が増えるわりに、あの大きさでは見分けが付かない）。
    ///
    /// ── 光り方 ───────────────────────────────────
    ///
    /// **立ち上がりは瞬時、消えるのに <see cref="FlashSeconds"/>。**
    /// 実際の放電と同じで、じわっと明るくなる閃光は無い。
    /// 消え際に 1 度だけ弱く再点灯する（多重放電）。
    /// </summary>
    public static class PlumeLightning
    {
        /// <summary>放電の枠（秒）。**1 枠にちょうど 1 本。**</summary>
        public const float SlotSeconds = 1.15f;

        /// <summary>1 本が光っている長さ（秒）。</summary>
        public const float FlashSeconds = 0.34f;

        /// <summary>同時に見えうる本数（＝さかのぼって見る枠の数）。</summary>
        public const int MaxBolts = 3;

        /// <summary>折れ線の点の数。**2 では真っ直ぐで雷に見えない。**</summary>
        public const int PointCount = 9;

        /// <summary>途中の点を振る量（その高さでの柱の半径に対する比）。</summary>
        public const float JitterRatio = 0.55f;

        /// <summary>放電が起きる高さの下限（柱の高さに対する比）。</summary>
        public const float LowFraction = 0.10f;

        /// <summary>放電が起きる高さの上限（柱の高さに対する比）。</summary>
        public const float HighFraction = 0.72f;

        /// <summary>1 本の長さの下限（柱の高さに対する比）。</summary>
        public const float MinSpanFraction = 0.10f;

        /// <summary>活動度 1 のときのいちばん明るい放電。</summary>
        public const float MaxBrightness = 1f;

        /// <summary>いちばん弱い放電の明るさ（活動度 1 のとき）。</summary>
        public const float MinBrightness = 0.22f;

        private const uint TimeSalt = 0x4C544D45u;
        private const uint ShapeSalt = 0x4C545348u;
        private const uint PickSalt = 0x4C545049u;

        /// <summary>時刻 <paramref name="seconds"/> が入る枠。</summary>
        public static int SlotAt(float seconds)
        {
            if (IsBad(seconds) || seconds < 0f) return 0;
            return (int)(seconds / SlotSeconds);
        }

        /// <summary>枠 <paramref name="slot"/> の放電が始まる時刻（秒）。</summary>
        public static float StartOf(uint seed, int slot)
        {
            if (slot < 0) return 0f;

            // 枠の中のどこで起きるか。**枠の端に寄せない**（拍が見えてしまう）。
            float u = DeterministicRandom.Unit(seed, unchecked((uint)slot ^ TimeSalt));
            return slot * SlotSeconds + u * (SlotSeconds - FlashSeconds);
        }

        /// <summary>
        /// 枠 <paramref name="slot"/> の放電の、時刻 <paramref name="seconds"/> における
        /// 明るさ <c>[0,1]</c>。光っていなければ 0。
        ///
        /// <paramref name="activityUnit"/> は噴出の強さで、**明るさだけを決める**
        /// （起きる／起きないは決めない。クラス doc）。
        /// </summary>
        public static float BrightnessAt(uint seed, int slot, float seconds,
                                         float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (slot < 0) return 0f;
            if (IsBad(seconds)) return 0f;

            float age = seconds - StartOf(seed, slot);
            if (age < 0f || age >= FlashSeconds) return 0f;

            float t = age / FlashSeconds;

            // 立ち上がりは瞬時。あとは落ちるだけ。
            float decay = 1f - t;
            decay *= decay;

            // 多重放電。消え際に 1 度だけ弱く戻る。
            if (t > 0.55f && t < 0.72f) decay += 0.28f * (1f - t);

            float u = DeterministicRandom.Unit(seed, unchecked((uint)slot ^ PickSalt));
            float scale = MinBrightness + (MaxBrightness - MinBrightness) * u * u;

            return Clamp01(a * scale * decay);
        }

        /// <summary>
        /// 枠 <paramref name="slot"/> の放電の折れ線を <paramref name="into"/> へ書く。
        /// 書いた点数を返す（<paramref name="into"/> が足りなければ 0）。
        ///
        /// <paramref name="plumeHeightMetres"/> は柱の全高、
        /// <paramref name="radiusAtFraction"/> は「高さの比 → その高さでの柱の半径（m）」で、
        /// 呼び出し側が <c>EruptionColumn.RadiusAt</c> を包んで渡す。
        /// </summary>
        public static int PathInto(LightningPoint[] into, uint seed, int slot,
                                   float plumeHeightMetres,
                                   RadiusAtFraction radiusAtFraction)
        {
            if (into == null || into.Length < PointCount) return 0;
            if (slot < 0) return 0;
            if (IsBad(plumeHeightMetres) || plumeHeightMetres <= 0f) return 0;
            if (radiusAtFraction == null) return 0;

            uint shape = unchecked((uint)slot ^ ShapeSalt);

            float a = DeterministicRandom.Unit(seed, shape);
            float b = DeterministicRandom.Unit(seed, shape + 977u);

            float lowT = LowFraction + (HighFraction - LowFraction) * (a < b ? a : b);
            float highT = LowFraction + (HighFraction - LowFraction) * (a < b ? b : a);
            if (highT - lowT < MinSpanFraction)
            {
                highT = lowT + MinSpanFraction;
                if (highT > HighFraction) highT = HighFraction;
            }

            // 折れ線がどの向きに走るか（水平の基準方向）。
            float angle = 6.2831853f * DeterministicRandom.Unit(seed, shape + 31u);
            float dirX = (float)System.Math.Cos(angle);
            float dirZ = (float)System.Math.Sin(angle);

            for (int i = 0; i < PointCount; i++)
            {
                float s = i / (float)(PointCount - 1);
                float t = lowT + (highT - lowT) * s;

                float y = plumeHeightMetres * t;
                float radius = radiusAtFraction(t);
                if (IsBad(radius) || radius < 0f) radius = 0f;

                // 端は柱の中心寄り、真ん中ほど大きく振れる（放電路が膨らむ）。
                float bulge = 4f * s * (1f - s);

                float j1 = DeterministicRandom.Unit(seed, shape + (uint)(i * 131 + 7)) - 0.5f;
                float j2 = DeterministicRandom.Unit(seed, shape + (uint)(i * 197 + 53)) - 0.5f;

                float along = radius * JitterRatio * (2f * j1) * bulge;
                float across = radius * JitterRatio * (2f * j2) * bulge;

                into[i] = new LightningPoint(dirX * along - dirZ * across, y,
                                             dirZ * along + dirX * across);
            }

            return PointCount;
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
