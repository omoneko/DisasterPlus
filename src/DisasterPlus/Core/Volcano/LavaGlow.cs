using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 溶岩の<b>発光</b>。**時間の関数ではない。**
    ///
    /// ── なぜ作り直したのか（2026-08-22、実機の指摘④）─────────────────────
    ///
    /// > 熔岩流の光り方が点滅しているのはリアルではありません。（…）
    /// > 噴火が終わっても光り続けているのは修正してください。
    ///
    /// 別々の 2 つの不具合である。
    ///
    /// <b>1. 点滅。</b> 以前は帯のテクスチャに明暗の縞（正弦 3 周期）を焼き、
    /// それを <c>SetTextureOffset</c> で毎フレーム 0.35/秒 で流していた。
    /// 縞は帯の全長にちょうど 3 本しか無いので、**地面のある 1 点は 1 秒弱で
    /// 明 → 暗 → 明を繰り返す**。これが「点滅」の正体である。
    /// 実物の溶岩流は<b>冷えた黒い地殻</b>と、その板と板のあいだの<b>光る割れ目</b>で
    /// できていて、**輝きは時間ではなく場所の関数**である。ゆっくり変わるのは
    /// 「その場所が冷える」からであって、明るさが往復するからではない。
    ///
    /// <b>2. 噴火が終わっても光り続ける。</b> 以前の色は
    /// <c>k = 0.15 + 0.85 × cool</c>、不透明度 <c>0.55 + 0.45 × cool</c> で、
    /// <c>cool = 0</c>（＝冷え切った）でも <b>k = 0.15 / α = 0.55</b> が残っていた。
    /// **どれだけ待っても消えない式だった。** しかも描画側は軌跡の配列が残っている
    /// かぎり描き続けるので、位相が <c>Done</c> になっても帯はそこに在り続ける。
    ///
    /// ── 場所の関数としての明るさ ─────────────────────────────────
    ///
    /// <c>u</c> は帯を横切る向き（0 と 1 が縁）、<c>v</c> は帯に沿う向きで、
    /// **<c>v = 0</c> が火口、<c>v = 1</c> が前進端**である（<c>LavaRibbon</c> が
    /// 折れ線の添字をそのまま <c>v</c> にしている）。明るいのは 2 か所だけ:
    ///
    /// <list type="number">
    /// <item><b>火口</b>（<c>v ≒ 0</c>）—— 供給され続けているので冷えない</item>
    /// <item><b>前進端</b>（<c>v ≒ 1</c>）—— 地殻が割れて中身が出るので、いちばん明るい</item>
    /// </list>
    ///
    /// そのあいだは<b>冷えた地殻</b>で、光っているのは割れ目だけである。
    ///
    /// ★★ <b>これが「年齢とともに冷える」の実装でもある。</b> 溶岩が前へ進むと
    ///   折れ線に点が増え、既に置かれた場所の <c>v</c> は**小さいほうへずれる** ——
    ///   つまり前進端の輝きの帯から外れて地殻の側へ入っていく。
    ///   時間を 1 つも参照せずに、場所が冷えていく。
    ///
    /// ── 全体の減衰 ───────────────────────────────────────
    ///
    /// <see cref="CoolFade"/> は <c>VolcanoLava.CoolUnit</c>（1 = まだ熱い、
    /// 0 = 冷え切った）を受けて **0 でちょうど 0 を返す**。
    /// <see cref="Visible"/> が false になったら描画側は面ごと畳む。
    /// 「薄く光り続ける」余地をどこにも残さない。
    /// </summary>
    public static class LavaGlow
    {
        /// <summary>
        /// 焼くテクスチャの 1 辺。**Game 側と tools/VolcanoPreview がこれを共有する** ——
        /// 割れ目の細かさ（<see cref="CrackAlong"/> / <see cref="CrackAcross"/>）は
        /// この解像度で線に見えるように決めてあるので、片方だけ変えると
        /// 実機とオフラインの絵が食い違う。実費は 128² × 4 B = 64 KB である。
        /// </summary>
        public const int TextureSize = 128;

        /// <summary>火口側で光っている割合（帯の全長に対して）。</summary>
        public const float VentGlowFraction = 0.10f;

        /// <summary>前進端で光っている割合（同上）。**いちばん明るいのはここ。**</summary>
        public const float FrontGlowFraction = 0.13f;

        /// <summary>火口の輝きの強さ（前進端を 1 として）。</summary>
        public const float VentGlowStrength = 0.85f;

        /// <summary>地殻そのものの明るさ。**0 ではない**（余熱で赤黒く見える）。</summary>
        public const float CrustFloor = 0.05f;

        /// <summary>割れ目がどれだけ明るいか（地殻に対して足す量）。</summary>
        public const float CrackStrength = 0.55f;

        /// <summary>割れ目の細かさ（帯に沿う向きの繰り返し数）。</summary>
        public const float CrackAlong = 10f;

        /// <summary>割れ目の細かさ（帯を横切る向きの繰り返し数）。</summary>
        public const float CrackAcross = 2.4f;

        /// <summary>割れ目とみなす幅。細いほど「板と板の隙間」に見える。</summary>
        public const float CrackWidth = 0.25f;

        /// <summary>
        /// 冷え方の指数。**画面の明るさは この値の 2 乗**（色と不透明度の両方に掛かる）
        /// なので、1 未満にして「しばらく赤いまま、終わりへ向かって一気に暗くなる」形にする。
        /// </summary>
        public const float CoolFadePower = 0.75f;

        /// <summary>これ以下の冷え具合では 1 枚も描かない（＝完全に消える）。</summary>
        public const float InvisibleBelow = 0.02f;

        /// <summary>
        /// 帯に沿った位置 <paramref name="v"/>（0 = 火口、1 = 前進端）での輝き <c>[0,1]</c>。
        /// **時間は 1 つも入らない。**
        /// </summary>
        public static float AlongFlowUnit(float v)
        {
            if (IsBad(v)) return 0f;
            float t = v < 0f ? 0f : (v > 1f ? 1f : v);

            // 前進端。地殻が割れて中身が出るので、いちばん明るい。
            float front = 0f;
            float toFront = 1f - t;
            if (FrontGlowFraction > 0f && toFront < FrontGlowFraction)
            {
                front = 1f - toFront / FrontGlowFraction;
            }

            // 火口。供給され続けているので冷えない。
            float vent = 0f;
            if (VentGlowFraction > 0f && t < VentGlowFraction)
            {
                vent = (1f - t / VentGlowFraction) * VentGlowStrength;
            }

            float glow = front > vent ? front : vent;
            // 端の立ち上がりを滑らかに（角のある帯は「塗り」に見える）。
            return glow * glow * (3f - 2f * glow);
        }

        /// <summary>
        /// 割れ目（板と板のあいだ）の明るさ <c>[0,1]</c>。**場所だけの関数**で、
        /// 帯を斜めに横切る細い線が不規則に並ぶ。
        ///
        /// 正弦をそのまま明るさにすると「波板」になるので、
        /// <see cref="VolcanoRelief"/> の放射谷と同じ**零交差**を使う ——
        /// 割れ目は細く、板は広く平らになる。
        /// </summary>
        public static float CrackUnit(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return 0f;

            double a = 2.0 * Math.PI * (v * CrackAlong + u * 0.7);
            double b = 2.0 * Math.PI * (u * CrackAcross - v * 2.3);
            float series = (float)(Math.Sin(a) + 0.6 * Math.Sin(b) + 0.35 * Math.Sin(a * 0.37 + b));

            float abs = series < 0f ? -series : series;
            if (abs >= CrackWidth) return 0f;

            float k = 1f - abs / CrackWidth;
            return k * k;
        }

        /// <summary>
        /// その一点の輝き <c>[0,1]</c>。地殻 ＋ 割れ目 ＋ 両端の熱い帯。
        /// **1 を超えない。**
        /// </summary>
        public static float GlowUnit(float u, float v)
        {
            float ends = AlongFlowUnit(v);
            float crust = CrustFloor + CrackStrength * CrackUnit(u, v);
            float glow = ends > crust ? ends : crust;

            // 端の熱い帯では割れ目の模様も一緒に明るくなる（板ごと溶けている）。
            glow += ends * CrackStrength * CrackUnit(u, v) * 0.5f;

            if (glow < 0f) return 0f;
            return glow > 1f ? 1f : glow;
        }

        /// <summary>
        /// 帯を横切る向きの不透明度 <c>[0,1]</c>。縁で 0 になる山型で、
        /// **溶岩の縁がぼやける**（切り紙のような直線の縁にしない）。
        /// </summary>
        public static float AcrossFalloff(float u)
        {
            if (IsBad(u)) return 0f;
            float t = u < 0f ? 0f : (u > 1f ? 1f : u);
            float k = 1f - Math.Abs(t * 2f - 1f);
            return k * k;
        }

        /// <summary>
        /// 全体の減衰。<paramref name="coolUnit"/> は 1 が「まだ熱い」、0 が「冷え切った」。
        /// **0 でちょうど 0 を返す** —— 「薄く光り続ける」余地を残さない（指摘④）。
        /// </summary>
        public static float CoolFade(float coolUnit)
        {
            if (IsBad(coolUnit) || coolUnit <= 0f) return 0f;
            float c = coolUnit > 1f ? 1f : coolUnit;
            // ★ 画面に出る明るさは**この値の 2 乗**である（色と不透明度の両方に
            //   掛かるため）。したがって指数 0.75 で「しばらく赤いまま、終わりへ
            //   向かって一気に暗くなる」——実物の溶岩の見え方に近い形になる。
            return (float)Math.Pow(c, CoolFadePower);
        }

        /// <summary>まだ描くべきか。false になったら描画側は面ごと畳む。</summary>
        public static bool Visible(float coolUnit)
        {
            return !IsBad(coolUnit) && coolUnit > InvisibleBelow;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
