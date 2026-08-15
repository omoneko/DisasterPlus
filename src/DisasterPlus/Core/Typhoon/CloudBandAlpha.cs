using System;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 渦巻き雲のリボンに貼る**アルファの型**。エンジン非依存の純データで、
    /// <c>Texture2D</c> の組み立ては <c>Game/Typhoon/TyphoonCloud</c> が行う
    /// （<see cref="SpiralMesh"/> と同じ分担）。
    ///
    /// ── なぜ要るか（全体レビュー）──────────────────────────────
    ///
    /// <see cref="SpiralMesh"/> は頂点ごとに UV を出している（u ＝ 渦に沿った進み、
    /// v ＝ 内側 0 / 外側 1）のに、雲のマテリアルは <c>_MainTex</c> を 1 度も
    /// 割り当てていなかった。つまり **4608 個の UV は誰にも読まれない死んだデータ**で、
    /// 雲はリボンの縁が硬いべた塗りとして出ていた。捨てるか使うかのどちらかしか
    /// 正しくないので、**使う**ほうを選んだ —— UV は既に意味のある値が入っており、
    /// 縁を落とすだけで「切り抜いたリボン」が「雲」に近づく。
    ///
    /// ── 何を保証するか ──────────────────────────────────────
    ///
    /// **リボンの両縁（v = 0 と v = 1）で必ず 0 になる。** そこが 0 でないと、
    /// メッシュの縁がそのまま見えて硬い帯に戻る。中央（v = 0.5）が最大で、
    /// そこの値は<b>これまでと同じ濃さ</b>である —— つまりこの変更は
    /// 「縁だけを柔らかくする」ものであり、雲全体を濃くも薄くもしない
    /// （実機を見る前に見た目を強くしない、というこのプロジェクトの規律）。
    ///
    /// u 方向は腕の先端と根元をわずかに落とすだけ。メッシュ側が既に幅を
    /// 細らせている（<c>SpiralMesh.TaperFloor</c>）ので、ここで強く落とすと
    /// 腕が短く見える。
    ///
    /// RGB は書かない。呼び出し側が**白**を入れること —— 色はマテリアルの
    /// ティントが持っており、テクスチャに色を入れると 2 箇所で色を決めることになる。
    /// （<c>Particles/Alpha Blended</c> は <c>tex * _TintColor</c> なので、
    ///  白 × ティント ＝ ティントそのもの。黒が混ざる経路は無い。）
    /// </summary>
    public static class CloudBandAlpha
    {
        /// <summary>1 辺のテクセル数。64×64 ＝ 4 KB（RGBA32 で 16 KB）。
        /// 縁のグラデーションにこれ以上の解像度は要らない。</summary>
        public const int Size = 64;

        /// <summary>腕の先端・根元で残すアルファの割合（u 方向の落ち込みの下限）。</summary>
        private const float LengthFloor = 0.55f;

        /// <summary>u 方向の細かなむら。**規則正しい帯は雲に見えない**（<see cref="SpiralMesh"/>
        /// の WobbleAmplitude と同じ理由）。振幅は小さく保つ。</summary>
        private const float RippleAmplitude = 0.12f;

        private const float RippleFrequency = 13f;

        /// <summary>
        /// アルファを 1 枚ぶん埋める。**呼び出し側が確保する**（<c>new</c> しない）。
        /// 配列が短ければ何もしない —— 途中まで書くと「縁の片側だけ硬い」という
        /// いちばん調べにくい形になる（<see cref="SpiralMesh.Build"/> と同じ判断）。
        ///
        /// 並びは行優先で、行 <c>y</c> が v ＝ <c>y / (Size - 1)</c>、
        /// 列 <c>x</c> が u ＝ <c>x / (Size - 1)</c>。
        /// </summary>
        public static void Build(byte[] alpha)
        {
            if (alpha == null || alpha.Length < Size * Size) return;

            for (int y = 0; y < Size; y++)
            {
                float v = (float)y / (Size - 1);
                // 両縁で 0、中央で 1。sin は端が 0 になり、微分も 0 に近いので
                // 縁が「切れた」ように見えない。
                float across = (float)Math.Sin(Math.PI * v);
                across = across * across;   // 縁をもう一段柔らかく（中央は 1 のまま）

                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / (Size - 1);

                    float along = LengthFloor
                                  + (1f - LengthFloor) * (float)Math.Sin(Math.PI * u);
                    float ripple = 1f - RippleAmplitude
                                        * (0.5f - 0.5f * (float)Math.Cos(u * RippleFrequency));

                    float a = across * along * ripple;
                    if (a < 0f) a = 0f;
                    if (a > 1f) a = 1f;

                    alpha[y * Size + x] = (byte)(a * 255f + 0.5f);
                }
            }
        }

        /// <summary>
        /// このテクスチャの最大アルファ（0〜1）。**1.0 である**ことをテストが固定する。
        /// マテリアルのティントの α がそのまま雲の濃さの上限になり、
        /// テクスチャは縁を落とすだけ、という約束そのもの。
        /// </summary>
        public static float PeakAlpha
        {
            get { return 1f; }
        }
    }
}
