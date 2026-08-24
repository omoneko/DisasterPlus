using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>アイコンの 1 画素。<c>[0,255]</c> の 4 成分。</summary>
    public struct IconPixel
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public IconPixel(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>透明。</summary>
        public static IconPixel None { get { return new IconPixel(0, 0, 0, 0); } }
    }

    /// <summary>
    /// **災害パネルのタイルに載せる絵を、画素の式として持つ。**
    /// <b>Core なのでエンジンには一切触らない</b>（<c>Texture2D</c> はここには出てこない）。
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; タブアイコンの火山と台風をイラストにしてほしいです。
    ///
    /// 災害パネルに並ぶ⑤と④のタイルは、これまで**文字だけ**だった。
    /// バニラのタイルはどれも絵なので、その列に文字が 2 枚混ざっている。
    ///
    /// ── ★★ なぜ描くのか（バニラのスプライトを使わないのか）────────────────
    ///
    /// <c>DisasterPanelBar</c> のクラス doc が禁じている ——
    /// **前景スプライトの名前を 1 つも指定しない**。スプライト名はアトラスのデータで
    /// あってアセンブリからは読めないので、名前を当てにいくと
    /// 「見えないタイル」になり得る。しかも⑤（火山）と④（台風）は
    /// <b>バニラに存在しない災害</b>で、当てにいく絵がそもそも無い。
    ///
    /// だから<b>自分で描く</b>。サイレン MOD の <c>WarningIcon</c> が同じことを
    /// していて、<c>UITextureSprite</c> に自前の <c>Texture2D</c> を挿すだけで済む。
    ///
    /// ── ★★ 小さくても読めること ────────────────────────────
    ///
    /// タイルは 109×100 で、絵はその 7 割ほど。<b>70 px 前後で意味が分かる</b>
    /// 必要がある。だから:
    ///
    ///   - 形は<b>影絵</b>で決める（細い線は 70 px で消える）
    ///   - 色は<b>2〜3 色</b>だけ。階調は輪郭の内側でしか使わない
    ///   - 縁に 1 px 以上の暗い縁取りを置く —— タイルの背景は明るくも暗くもなりうる
    ///
    /// 実際に 70 px で描いて確かめてある（<c>tools/IconPreview</c>）。
    /// </summary>
    public static class DisasterIconArt
    {
        // ── 色 ────────────────────────────────────────────

        /// <summary>火山の山体。暗い玄武岩。</summary>
        private static readonly IconPixel Rock = new IconPixel(58, 52, 58, 255);

        /// <summary>火山の山体の日向側。</summary>
        private static readonly IconPixel RockLit = new IconPixel(92, 84, 90, 255);

        /// <summary>溶岩。<c>VolcanoLavaFx</c> のティントと同じ向きの橙。</summary>
        private static readonly IconPixel Lava = new IconPixel(255, 122, 32, 255);

        /// <summary>火口の芯。</summary>
        private static readonly IconPixel LavaHot = new IconPixel(255, 214, 130, 255);

        /// <summary>噴煙。</summary>
        private static readonly IconPixel Ash = new IconPixel(146, 142, 148, 255);

        /// <summary>台風の雲。<c>TyphoonVortexPuffFx</c> と同じ日向の白。</summary>
        private static readonly IconPixel Cloud = new IconPixel(242, 244, 248, 255);

        /// <summary>台風の雲の影側。</summary>
        private static readonly IconPixel CloudShade = new IconPixel(176, 186, 204, 255);

        /// <summary>台風の背景（海）。</summary>
        private static readonly IconPixel Sea = new IconPixel(28, 58, 96, 255);

        /// <summary>縁取り。**背景が明るくても暗くても輪郭が立つ。**</summary>
        private static readonly IconPixel Outline = new IconPixel(16, 16, 20, 255);

        // ── 火山 ───────────────────────────────────────────

        /// <summary>山体の頂の高さ（<c>v</c>）。</summary>
        private const float SummitV = 0.50f;

        /// <summary>裾の広がり（<c>x</c> の片側）。</summary>
        private const float BaseHalf = 0.94f;

        /// <summary>頂の平らな部分（<c>x</c> の片側）。</summary>
        private const float SummitHalf = 0.17f;

        /// <summary>
        /// 火山のアイコン。<paramref name="u"/> / <paramref name="v"/> は <c>[0,1]</c> で、
        /// <b><paramref name="v"/> は 0 が下（地面）、1 が上（空）</b>である。
        ///
        /// ── ★★ 噴煙は「台形」ではなく「もくもく」である ────────────────────
        ///
        /// 最初の版は噴煙を<b>上へ広がる台形</b>で描いた。70 px で見ると
        /// **漏斗（じょうご）にしか見えない** —— 直線の縁は煙の縁ではない
        /// （<c>tools/IconPreview</c> の 1 版目）。いまは<b>丸を 4 つ重ねる</b>。
        ///
        /// 山体も同じ理由で直線をやめ、<b>内側へ反った稜線</b>にしてある
        /// （成層火山の形。直線の台形は「山」ではなく「台」に見える）。
        /// </summary>
        public static IconPixel Volcano(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return IconPixel.None;

            float x = u * 2f - 1f;              // -1 .. 1（中心が 0）

            // 噴煙が先。**山体より奥**なので、山体に負ける（下で上書きされる）。
            IconPixel plume = Plume(x, v);

            IconPixel cone = Cone(x, v);
            if (cone.A != 0) return cone;

            return plume;
        }

        /// <summary>
        /// 山体。稜線は内側へ反る（<c>(1-t)^0.72</c>）ので、裾が広く肩が締まる。
        /// 火口は頂の中央を丸く抉り、そこだけ溶岩の色にする。
        /// </summary>
        private static IconPixel Cone(float x, float v)
        {
            if (v > SummitV) return IconPixel.None;

            float ax = x < 0f ? -x : x;
            float t = v / SummitV;                                   // 0 = 裾、1 = 頂

            // 内側へ反った稜線。
            float half = SummitHalf + (BaseHalf - SummitHalf) * (float)Math.Pow(1f - t, 0.88);
            if (ax > half) return IconPixel.None;

            // ── 火口。頂の中央を丸く抉る。
            float craterLip = SummitV - 0.045f;
            if (v > craterLip)
            {
                float bowl = SummitHalf * 0.70f
                             * (float)Math.Sqrt(1f - (v - craterLip) / 0.05f + 0.0001f);
                if (ax < bowl)
                {
                    return v > craterLip + 0.022f ? LavaHot : Lava;
                }
            }

            // 縁取り。稜線に沿って一定の太さで入れる。
            if (half - ax < 0.05f) return Outline;

            // 溶岩の筋 2 本。**火口から下へ**、少し蛇行する。
            if (LavaStreak(x, v, 0.34f) || LavaStreak(x, v, -0.48f)) return Lava;

            // 右から光が当たっている。
            return x > 0.05f ? RockLit : Rock;
        }

        /// <summary>
        /// 噴煙。**丸を 4 つ**、上へ行くほど大きく・少し風下へ寄せて重ねる。
        /// 直線の縁を 1 本も作らないのが要点である（クラス doc）。
        /// </summary>
        private static IconPixel Plume(float x, float v)
        {
            if (v < SummitV - 0.06f) return IconPixel.None;

            // (中心 x, 中心 v, 半径)
            float[] cx = { 0.00f, 0.13f, -0.10f, 0.20f, -0.02f };
            float[] cv = { 0.59f, 0.70f, 0.79f, 0.86f, 0.88f };
            float[] cr = { 0.14f, 0.22f, 0.25f, 0.24f, 0.28f };

            float best = 999f;
            for (int i = 0; i < cx.Length; i++)
            {
                float dx = x - cx[i];
                float dv = v - cv[i];
                float d = (float)Math.Sqrt(dx * dx + dv * dv) - cr[i];
                if (d < best) best = d;
            }

            if (best > 0f) return IconPixel.None;
            if (best > -0.035f) return Outline;
            return Ash;
        }

        /// <summary>
        /// 溶岩の筋 1 本。火口から裾へ、少し蛇行しながら下りる。
        /// <paramref name="lean"/> が正なら右へ流れる。
        /// </summary>
        private static bool LavaStreak(float x, float v, float lean)
        {
            if (v > SummitV - 0.05f) return false;

            float t = 1f - v / SummitV;                              // 0 = 頂、1 = 裾
            float centre = lean * t * t + 0.05f * (float)Math.Sin(7.0 * t);
            float width = 0.030f + 0.040f * t;

            float d = x - centre;
            if (d < 0f) d = -d;
            return d <= width;
        }

        // ── 台風 ───────────────────────────────────────────

        /// <summary>
        /// 台風のアイコン。丸い海の上に、**眼を空けた 3 本の腕**。
        /// 腕は対数螺旋で、内側ほど詰まっている。
        /// </summary>
        public static IconPixel Typhoon(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return IconPixel.None;

            float x = u * 2f - 1f;
            float y = v * 2f - 1f;

            float r = (float)Math.Sqrt(x * x + y * y);
            if (r > 0.97f) return IconPixel.None;
            if (r > 0.90f) return Outline;

            // ── 眼。**中は海のまま**（ここが空いているから台風に見える）。
            const float Eye = 0.17f;
            if (r < Eye) return Sea;
            if (r < Eye + 0.035f) return CloudShade;   // 眼の壁の内側

            float angle = (float)Math.Atan2(y, x);

            // ── 腕。対数螺旋 θ = k ln r を 3 本、120 度ずつずらして置く。
            const int Arms = 3;
            const float Twist = 2.6f;

            float spiral = Twist * (float)Math.Log(r / Eye);
            float phase = angle - spiral;

            // phase を 1 本ぶんの間隔で折り返す。
            float step = 6.2831853f / Arms;
            float local = phase - step * (float)Math.Floor(phase / step + 0.5);

            // 腕の太さ。外へ行くほど細くなって尾を引く。
            float width = 0.62f - 0.30f * r;

            float d = local < 0f ? -local : local;
            if (d <= width)
            {
                // 腕の縁は影、芯は白。
                return d > width - 0.16f ? CloudShade : Cloud;
            }

            return Sea;
        }

        // ── 海溝型地震 ──────────────────────────────────────

        /// <summary>海底。</summary>
        private static readonly IconPixel SeaBed = new IconPixel(64, 58, 52, 255);

        /// <summary>断層の破断面。**ここだけが光る。**</summary>
        private static readonly IconPixel Rupture = new IconPixel(255, 196, 72, 255);

        /// <summary>波の芯。</summary>
        private static readonly IconPixel Foam = new IconPixel(238, 244, 250, 255);

        /// <summary>海面より上の空。</summary>
        private static readonly IconPixel Sky = new IconPixel(96, 124, 156, 255);

        /// <summary>
        /// 海溝型地震のアイコン（2026-08-22、所有者の依頼「アイコンも新規で」）。
        /// <paramref name="v"/> は<b>0 が下（海底）、1 が上（空）</b>。
        ///
        /// ── 70 px で何が読めるか ────────────────────────────────
        ///
        /// タイルは 109×100 px なので、絵は 70 px 角ほどにしか見えない。
        /// **3 つより多い要素は読めない。** 入れるのは
        ///
        ///   1. <b>海</b>（この災害が海のものだと一目で分かる）
        ///   2. <b>津波の波</b>（これが目的である）
        ///   3. <b>海底の V 字の海溝と、そこで光る破断</b>（原因である）
        ///
        /// ★ 火山アイコンで学んだこと（あちらの doc）と同じで、
        ///   <b>直線は自然物に見えない</b>。波の背は正弦、海溝は
        ///   丸めた V 字にしてある。
        /// </summary>
        public static IconPixel TrenchQuake(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return IconPixel.None;

            float x = u * 2f - 1f;

            // ── 丸い枠（ほかの 2 つと同じ作り）──────────────────────
            float y = v * 2f - 1f;
            float r = (float)Math.Sqrt(x * x + y * y);
            if (r > 0.97f) return IconPixel.None;
            if (r > 0.90f) return Outline;

            // ── 海底（下から 0〜0.36）。中央に V 字の海溝 ─────────────
            //   谷は釣鐘状に丸める（尖った V は「割れ目」に見えて海溝に見えない）。
            float trench = 0.36f - 0.21f / (1f + 22f * x * x);
            if (v < trench)
            {
                // ★★ **破断は谷の底から下へ伸びる 1 本の裂け目である。**
                //
                //   はじめ「谷の面から一定の深さの帯」で描いていたが、
                //   それは<b>谷の形をなぞる</b>ので、70 px では
                //   **黄色い角が 2 本生えているようにしか見えなかった**
                //   （tools/IconPreview の 1 版目）。
                //   谷底の 1 点から真下へ、下ほど広がる楔にする。
                float depth = trench - v;
                float halfWidth = 0.055f + 0.55f * depth;
                if (x > -halfWidth && x < halfWidth) return Rupture;

                return SeaBed;
            }

            // ── 海面。津波の背が右上がりに崩れる ────────────────────
            //   正弦 1 本ではなく 2 本重ねて、峰の左右を非対称にする
            //   （対称な波は「波」ではなく「山」に見える）。
            float crest = 0.62f
                          + 0.17f * (float)Math.Sin(2.1f * x + 0.6f)
                          + 0.05f * (float)Math.Sin(5.3f * x + 1.9f);

            if (v > crest + 0.05f) return Sky;

            // 峰の縁を白く。**厚みを持たせないと「線」に見える。**
            if (v > crest - 0.10f) return Foam;

            // 水中。峰の直下だけ明るくして、波が立ち上がって見えるようにする。
            float lift = crest - 0.10f - v;
            if (lift < 0.16f) return CloudShade;
            return Sea;
        }

        private static bool IsBad(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }
    }
}
