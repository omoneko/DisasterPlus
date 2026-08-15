using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 台風の渦巻き雲の**頂点だけ**を生成する。エンジン非依存の純データで、
    /// <c>Mesh</c> の組み立ては <c>Game/Typhoon/TyphoonCloud</c> が行う（設計書 §5）。
    ///
    /// ── なぜ自分で組むのか ──────────────────────────────────
    ///
    /// バニラに流用できる雲は**存在しない**。<c>DisasterInfo.m_effect</c> は
    /// フィールドごと無く、災害まわりの <c>EffectInfo</c> は
    /// <c>DisasterProperties.m_mediumExplosion</c> と <c>MeteorAI.m_impactEffect</c> の
    /// 2 つだけで、雲も渦もプレハブ化されていない（IL 事実文書 §C-1）。しかも
    /// バニラの雲は半径 6400 km のスカイドームに貼られたノイズシェーダで
    /// **ワールド座標を持たない**ので、「台風の位置に雲の渦を置く」合成は原理的に
    /// できない（§C-2）。したがって④は自分でメッシュを組んで自分で描く。
    ///
    /// 手本は <c>VortexAI.GenerateMesh()</c>（16250 頂点・高さ 2000 m の漏斗を
    /// <c>Randomizer(2975689)</c> の固定シードで手続き生成する。§C-1）。④の雲は
    /// 上空に 1 枚あればよく漏斗ほどの密度は要らないので、**竜巻より 1 桁小さい**
    /// 2304 頂点に収めてある。
    ///
    /// ── 形 ────────────────────────────────────────────
    ///
    /// 中心に眼の穴（<c>innerRadius</c>）を空けた円環に、<see cref="Arms"/> 本の
    /// スパイラルのリボンを <see cref="Rings"/> 段重ねる。1 本のリボンは
    /// <see cref="SegmentsPerArm"/> 個の断面を持ち、断面ごとに内側／外側の 2 頂点を出す。
    ///
    /// **リボンの幅は「中心からの距離」で作り、法線方向のオフセットでは作らない。**
    /// 法線オフセットにすると、渦の内端で頂点が眼の穴へ食い込み、外端では外周を
    /// はみ出す。距離で作れば両端を <c>[inner, outer]</c> にクランプするだけで
    /// 「眼が必ず空いていて、外周を必ず越えない」ことが保証できる（テストがこの 2 つを固定する）。
    ///
    /// ── 三角形は表裏どちらも張る ──────────────────────────────
    ///
    /// 同じ四角形に巻き方向の違う三角形を 2 組張って**両面**にする。頂点を増やさずに
    /// 索引だけ倍にする形なので安い。これは保険である ——「雲が真上から見えない」は
    /// 巻き方向 1 つで起きるうえ、実機に出るまで気付けない壊れ方で、しかも
    /// <c>Shader.Find("Standard")</c> のマテリアルは裏面を描かない。
    ///
    /// 乱数は <see cref="DeterministicRandom"/> だけを使う（<c>VanillaRandomizer</c> は
    /// 使わない —— ここで決めるのはバニラが引く値ではなく④が発明した形である）。
    /// **同じ引数なら常に同じ形になる**（都市を読み直して雲の形が変わらない）。
    /// </summary>
    public static class SpiralMesh
    {
        /// <summary>渦巻きの腕の本数。</summary>
        public const int Arms = 4;

        /// <summary>腕 1 本あたりの断面の数。</summary>
        public const int SegmentsPerArm = 96;

        /// <summary>重ねる段数（高さ方向の層）。</summary>
        public const int Rings = 3;

        /// <summary>腕 1 本が中心のまわりを回る角度（rad）。1.6 周ぶん。</summary>
        private const float TurnRadians = 10.053096f;

        /// <summary>
        /// リボンの半幅 ÷（外半径 − 内半径）。
        ///
        /// **腕どうしの隙間が閉じない値であること。** 隣り合う腕の通過の半径間隔は
        /// <c>span /（回転数 × <see cref="Arms"/>）</c> ＝ span の約 1/6.4 になる。
        /// リボンの全幅（＝この値の 2 倍）がそれに追いつくと、渦巻きは隙間の無い
        /// 円盤に見えてしまう（オフライン描画で実測して 0.07 から下げた）。
        /// </summary>
        private const float BandHalfWidth = 0.04f;

        /// <summary>
        /// リボンの端の細り。両端で細くしないと、内端と外端が
        /// <c>[inner, outer]</c> のクランプで**平らに切り落とされ**、渦の先端が
        /// 四角い出っ張りになる（オフライン描画で確認）。
        /// </summary>
        private const float TaperFloor = 0.15f;

        /// <summary>
        /// 腕のうねりの振幅（span に対する割合）と周波数。**完全な等間隔の渦は
        /// 台風ではなく催眠術の円盤に見える**（オフライン描画で確認）。腕ごとに
        /// 位相の違う低周波のうねりを 1 本入れて、規則性を崩す。
        /// </summary>
        private const float WobbleAmplitude = 0.035f;

        private const float WobbleFrequency = 9f;

        /// <summary>段ごとの高さのばらつき（層の厚みに対する割合）。</summary>
        private const float HeightJitter = 0.12f;

        /// <summary>
        /// 段ごとの位相のずれの最大値（rad）。腕の角度間隔（2π/<see cref="Arms"/> ≒ 1.57）に
        /// 対して十分小さくすること —— 大きいと 3 段が互いの隙間を埋め合って
        /// 渦巻きが消える。
        /// </summary>
        private const float RingPhaseSpread = 0.12f;

        private const float TwoPi = 6.2831855f;

        /// <summary>
        /// 頂点数。腕 × 段 × 断面 × 2（内側・外側）。
        /// **竜巻の 16250 より 1 桁小さい**（クラス doc）。
        /// </summary>
        public static int VertexCount
        {
            get { return Arms * Rings * SegmentsPerArm * 2; }
        }

        /// <summary>
        /// 三角形索引の数。四角形 1 枚につき表裏 2 組 ＝ 12 索引（クラス doc）。
        /// </summary>
        public static int TriangleIndexCount
        {
            get { return Arms * Rings * (SegmentsPerArm - 1) * 12; }
        }

        /// <summary>
        /// 配列を埋める。**呼び出し側が確保する**（このメソッドは <c>new</c> しない）。
        /// 配列が短ければ何もしない —— 途中まで書くと「頂点はあるのに面が壊れている」
        /// という最も調べにくい形になる。
        /// </summary>
        /// <param name="innerRadius">眼の穴の半径。ここより内側には頂点を置かない。</param>
        /// <param name="outerRadius">外周。ここより外側には頂点を置かない。</param>
        /// <param name="height">層の総厚み。Y は <c>[0, height]</c> に収まる。</param>
        /// <param name="vertices">長さ <see cref="VertexCount"/> 以上。</param>
        /// <param name="uvs">長さ <see cref="VertexCount"/> × 2 以上（u, v の交互）。</param>
        /// <param name="triangles">長さ <see cref="TriangleIndexCount"/> 以上。</param>
        public static void Build(float innerRadius, float outerRadius, float height,
                                 Vec3[] vertices, float[] uvs, int[] triangles)
        {
            if (vertices == null || uvs == null || triangles == null) return;
            if (vertices.Length < VertexCount) return;
            if (uvs.Length < VertexCount * 2) return;
            if (triangles.Length < TriangleIndexCount) return;

            float inner = Sane(innerRadius, 0f);
            float outer = Sane(outerRadius, 0f);
            if (!(outer > inner)) outer = inner + 1f;
            float tall = Sane(height, 0f);

            float span = outer - inner;
            float halfWidth = span * BandHalfWidth;
            float layer = Rings > 0 ? 1f / Rings : 1f;

            int v = 0;
            int uv = 0;
            int tri = 0;

            for (int a = 0; a < Arms; a++)
            {
                float armBase = TwoPi * a / Arms;
                // 腕ごとのうねりの位相。決定論的なので都市を読み直しても形は変わらない。
                float wobblePhase = DeterministicRandom.Unit((uint)(a + 1), 0x574F4243u) * TwoPi;

                for (int r = 0; r < Rings; r++)
                {
                    // 段ごとに少しだけ位相をずらす。真上から見たときに 3 段が
                    // ぴったり重なると、層があることが見えない。
                    float ringPhase = DeterministicRandom.Unit((uint)a, (uint)(r + 1))
                                      * RingPhaseSpread;
                    float ringHeight = (r + 0.5f) * layer;

                    for (int s = 0; s < SegmentsPerArm; s++)
                    {
                        float t = SegmentsPerArm > 1 ? (float)s / (SegmentsPerArm - 1) : 0f;
                        float angle = armBase + ringPhase + t * TurnRadians;
                        float cos = (float)Math.Cos(angle);
                        float sin = (float)Math.Sin(angle);

                        // ★ 幅は「中心からの距離」で作り、両端をクランプする（クラス doc）。
                        //   端は細らせる（TaperFloor）。細らせないとクランプが渦の先端を
                        //   平らに切り落とし、四角い出っ張りになる。
                        float taper = TaperFloor
                                      + (1f - TaperFloor) * (float)Math.Sin(Math.PI * t);
                        // うねりも端では消す（taper と同じ理由。外周に押し付けない）。
                        float wobble = span * WobbleAmplitude * taper
                                       * (float)Math.Sin(t * WobbleFrequency + wobblePhase);
                        float centreRadius = inner + span * t + wobble;
                        float lo = centreRadius - halfWidth * taper;
                        float hi = centreRadius + halfWidth * taper;
                        if (lo < inner) lo = inner;
                        if (hi > outer) hi = outer;

                        float jitter = (DeterministicRandom.Unit((uint)(s + 1),
                                            (uint)(a * Rings + r + 1)) - 0.5f)
                                       * HeightJitter * layer;
                        float unitY = ringHeight + jitter;
                        if (unitY < 0f) unitY = 0f;
                        if (unitY > 1f) unitY = 1f;
                        float y = unitY * tall;

                        vertices[v] = new Vec3(lo * cos, y, lo * sin);
                        vertices[v + 1] = new Vec3(hi * cos, y, hi * sin);

                        // u は渦に沿った進み、v は内側 0 / 外側 1。どちらも [0,1]。
                        uvs[uv] = t;
                        uvs[uv + 1] = 0f;
                        uvs[uv + 2] = t;
                        uvs[uv + 3] = 1f;

                        v += 2;
                        uv += 4;
                    }

                    // この腕・この段の四角形を張る。頂点はもう置き終わっているので、
                    // 索引の基点は「今書いた 2*SegmentsPerArm 個の先頭」になる。
                    int start = v - SegmentsPerArm * 2;
                    for (int s = 0; s < SegmentsPerArm - 1; s++)
                    {
                        int i0 = start + s * 2;          // 内側 s
                        int i1 = i0 + 1;                 // 外側 s
                        int i2 = i0 + 2;                 // 内側 s+1
                        int i3 = i0 + 3;                 // 外側 s+1

                        triangles[tri] = i0;
                        triangles[tri + 1] = i2;
                        triangles[tri + 2] = i1;
                        triangles[tri + 3] = i1;
                        triangles[tri + 4] = i2;
                        triangles[tri + 5] = i3;

                        // ★ 裏面（クラス doc の保険）。巻き方向だけを逆にする。
                        triangles[tri + 6] = i0;
                        triangles[tri + 7] = i1;
                        triangles[tri + 8] = i2;
                        triangles[tri + 9] = i1;
                        triangles[tri + 10] = i3;
                        triangles[tri + 11] = i2;

                        tri += 12;
                    }
                }
            }
        }

        /// <summary>NaN と無限大と負の値を落とす。「雲の頂点が NaN」を作らない。</summary>
        private static float Sane(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return fallback;
            return value;
        }
    }
}
