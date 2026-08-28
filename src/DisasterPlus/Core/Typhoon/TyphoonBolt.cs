using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>折れ線の 1 点（台風の目を原点とした相対座標、m）。</summary>
    public struct TyphoonBoltPoint
    {
        public readonly float X;

        /// <summary>雲底からの高さ（m）。</summary>
        public readonly float Y;

        public readonly float Z;

        public TyphoonBoltPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// <b>台風の雲の中で光る稲妻。</b>**エンジン非依存の純関数だけ。**
    ///
    /// ── 所有者の指示（2026-08-25）─────────────────────────────────
    ///
    /// &gt; まだ雷の発生場所が台風の雲より上です。いっそのこと「雷雨」にせずに
    /// &gt; 「雨」だけにして、時々台風の雲の中から稲妻を発生させる方がうまく
    /// &gt; いくかもしれません
    ///
    /// ── ★★ なぜバニラの雷は雲より上に出るのか（IL 実測）────────────────
    ///
    /// バニラが空から雷を落とすのは <c>WeatherManager.SimulationStepImpl</c> の
    /// <b>1 箇所だけ</b>で、条件は <c>m_currentRain &gt; 0.8</c> である（IL_09D3）。
    /// 落ちる高さはゲームの雷レンダラが決めており、**MOD から動かせない。**
    /// ④の雲を 1200 m まで上げてもまだ上から降ってきた。
    ///
    /// だから<b>ゲームに雷を落とさせない</b>（雨を 0.8 で止める。
    /// <c>TyphoonWeather.MaxRainWithoutLightning</c>）。代わりに
    /// <b>雲の中に自分で描く</b> —— ⑤の噴煙の中の雷
    /// （<c>Core.Volcano.PlumeLightning</c>）で通した道と同じである。
    ///
    /// ── 形は噴煙の雷とは違う ───────────────────────────────────
    ///
    /// <code>
    /// 噴煙: 柱の中を**ほぼ真上へ**走る（細い柱の中の放電）
    /// 台風: 雲は**平たい円盤**なので、稲妻は主に**横へ**走る（雲間放電）
    /// </code>
    ///
    /// だからこちらは<b>水平に長く、上下に薄い</b>折れ線を作る。
    /// 発生場所は壁雲と雨雲帯に偏らせる —— そこが対流のいちばん強いところである。
    ///
    /// ★ 枠（<see cref="SlotSeconds"/>）ごとにちょうど 1 本を決める。
    ///   種と枠から決まるので、**同じ台風は何度でも同じように光る。**
    /// </summary>
    public static class TyphoonBolt
    {
        /// <summary>放電の枠（秒）。**1 枠にちょうど 1 本。**</summary>
        public const float SlotSeconds = 2.4f;

        /// <summary>1 本が光っている長さ（秒）。**短い。**</summary>
        public const float FlashSeconds = 0.22f;

        /// <summary>同時に見えうる本数（＝さかのぼって見る枠の数）。</summary>
        public const int MaxBolts = 3;

        /// <summary>折れ線の点の数。**2 では真っ直ぐで雷に見えない。**</summary>
        public const int PointCount = 7;

        /// <summary>1 本の水平の長さ（渦の半径に対する比）の下限。</summary>
        public const float LengthMinFraction = 0.16f;

        /// <summary>同上の上限。</summary>
        public const float LengthMaxFraction = 0.42f;

        /// <summary>途中の点を横へ振る量（その 1 本の長さに対する比）。</summary>
        public const float JitterFraction = 0.14f;

        /// <summary>上下に振れる量（雲の厚みに対する比）。**雲から出さない。**</summary>
        public const float VerticalFraction = 0.30f;

        /// <summary>いちばん明るいときの値。</summary>
        public const float PeakBrightness = 1f;

        /// <summary>
        /// この枠で光るか（＝1 本出るか）。全部の枠で光ると忙しなく、
        /// 静かな時間が無いと「時々」に見えない。
        /// </summary>
        public const float StrikeChance = 0.55f;

        /// <summary>今どの枠か。</summary>
        public static int SlotAt(float clockSeconds)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0;
            return (int)(clockSeconds / SlotSeconds);
        }

        /// <summary>
        /// <paramref name="slot"/> の 1 本の今の明るさ <c>[0,1]</c>。
        /// 光っていない（枠が外れた・時間が過ぎた）なら 0。
        ///
        /// <paramref name="intensityUnit"/> は台風の強さ <c>[0,1]</c> ——
        /// 弱い台風では雷そのものが減る。
        /// </summary>
        public static float BrightnessAt(uint seed, int slot, float clockSeconds,
                                         float intensityUnit)
        {
            if (slot < 0) return 0f;
            if (IsBad(clockSeconds)) return 0f;

            float unit = Clamp01(intensityUnit);
            if (unit <= 0f) return 0f;

            // この枠は光る枠か。
            if (DeterministicRandom.Unit(seed, (uint)slot * 5u + 1u) > StrikeChance * unit)
            {
                return 0f;
            }

            float start = slot * SlotSeconds
                          + DeterministicRandom.Unit(seed, (uint)slot * 5u + 2u)
                            * (SlotSeconds - FlashSeconds);

            float age = clockSeconds - start;
            if (age < 0f || age > FlashSeconds) return 0f;

            // ★ 立ち上がりは一瞬、消えるのは少し尾を引く（放電の見え方）。
            float w = age / FlashSeconds;
            float shape = w < 0.12f ? w / 0.12f : (1f - w) / 0.88f;
            if (shape < 0f) shape = 0f;

            return PeakBrightness * shape * (0.45f + 0.55f * unit);
        }

        /// <summary>
        /// <paramref name="slot"/> の 1 本の折れ線を <paramref name="into"/> へ書く。
        /// 書いた点数を返す（<see cref="PointCount"/>、失敗なら 0）。
        ///
        /// <paramref name="radiusMetres"/> は渦の外周半径、
        /// <paramref name="thicknessMetres"/> は雲の厚み。
        /// </summary>
        public static int PathInto(TyphoonBoltPoint[] into, uint seed, int slot,
                                   float radiusMetres, float thicknessMetres)
        {
            if (into == null || into.Length < PointCount) return 0;
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 0;

            float thickness = IsBad(thicknessMetres) || thicknessMetres <= 0f
                ? radiusMetres * 0.16f
                : thicknessMetres;

            uint draw = (uint)slot * 17u + 3u;

            // ── どこで光るか。壁雲と雨雲帯に偏らせる ─────────────────
            //   目の中では光らせない（あそこは晴れている）。
            float band = TyphoonCloudParcels.EyewallFraction
                         + (1f - TyphoonCloudParcels.EyewallFraction)
                           * DeterministicRandom.Unit(seed, draw) * 0.85f;

            // ★★ **1 本ぶんの長さを見込んで内側へ寄せる。**（テストが拾った）
            //    band を 0.89 まで許したまま長さ 0.42R の稲妻を置くと、
            //    端が 1.10R ——<b>渦の外</b>で光る。中心の帯を
            //    「半分の長さ ＋ 横振れ」ぶんだけ内へ引く。
            float reach = (LengthMaxFraction * 0.5f)
                          * (1f + JitterFraction);
            float maxBand = 1f - reach;
            if (maxBand < TyphoonCloudParcels.EyewallFraction)
            {
                maxBand = TyphoonCloudParcels.EyewallFraction;
            }
            if (band > maxBand) band = maxBand;

            float angle = DeterministicRandom.Unit(seed, draw + 1u) * 6.2831853f;
            float cx = (float)Math.Cos(angle) * band * radiusMetres;
            float cz = (float)Math.Sin(angle) * band * radiusMetres;

            // 雲の厚みの中ほど。**上端や下端に置かない**（雲から出て見える）。
            float cy = thickness * (0.30f + 0.40f * DeterministicRandom.Unit(seed, draw + 2u));

            // ── 走る向き。**水平である**（雲間放電）。────────────────
            float heading = DeterministicRandom.Unit(seed, draw + 3u) * 6.2831853f;
            float length = radiusMetres
                           * (LengthMinFraction
                              + (LengthMaxFraction - LengthMinFraction)
                                * DeterministicRandom.Unit(seed, draw + 4u));

            float dx = (float)Math.Cos(heading);
            float dz = (float)Math.Sin(heading);

            // 直角の向き（横へ振るため）。
            float px = -dz;
            float pz = dx;

            for (int i = 0; i < PointCount; i++)
            {
                float t = i / (float)(PointCount - 1);

                // 端は振らない（振ると両端がほつれて見える）。
                float taper = 1f - Math.Abs(t * 2f - 1f);

                uint js = draw + 10u + (uint)i * 3u;
                float side = (DeterministicRandom.Unit(seed, js) * 2f - 1f)
                             * JitterFraction * length * taper;
                float lift = (DeterministicRandom.Unit(seed, js + 1u) * 2f - 1f)
                             * VerticalFraction * thickness * taper;

                float along = (t - 0.5f) * length;

                into[i] = new TyphoonBoltPoint(
                    cx + dx * along + px * side,
                    cy + lift,
                    cz + dz * along + pz * side);
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
