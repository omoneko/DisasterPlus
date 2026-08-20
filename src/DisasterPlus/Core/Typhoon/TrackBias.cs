namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 台風の**進行方向に対する左右の偏り**（危険半円）。
    /// <b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── 何を模しているか ──────────────────────────────────
    ///
    /// 実在の台風は左右対称ではない。渦の回転速度と台風自身の移動速度が
    /// **足し算になる側**と**引き算になる側**があり、足し算になる側を
    /// 「危険半円」と呼ぶ。北半球（反時計回りの渦）では**進行方向の右側**、
    /// 南半球（時計回りの渦）では左側である。
    ///
    /// 持ち主の指示は「進行方向左側」だったが、**あとから右へ訂正された**。
    /// 訂正が正しい（北半球の危険半円は右）。左は南半球の話なので、
    /// <see cref="RadiusFactor"/> / <see cref="ChanceFactor"/> に
    /// <c>southernHemisphere</c> を渡せば左へ移る（設定 1 本で切り替わる）。
    ///
    /// ── 座標系（ここを取り違えると偏りが左右反対になる）─────────────────
    ///
    /// ④の進行方位 φ は <c>(cos φ, sin φ)</c> を (X, Z) とする数学系である
    /// （<c>TyphoonTrack.ArcPosition</c>）。CS のワールドは X が東、Z が北なので、
    /// 上から見て「進行方向の右」は φ を −90° 回した向き:
    ///
    /// <code>
    /// forward = ( cos φ,  sin φ)
    /// right   = ( sin φ, -cos φ)      // (a, b) を −90° 回すと (b, −a)
    /// </code>
    ///
    /// 検算: 北へ進む（φ = 90°）とき <c>forward = (0, 1) = +Z</c>、
    /// <c>right = (1, 0) = +X = 東</c>。**北を向いて右は東**で合っている。
    /// この検算はテストが固定している。
    ///
    /// ── 経路が曲がっても付いてくる理由 ──────────────────────────
    ///
    /// この型は**方位を引数で受け取るだけ**で、自分では 1 ビットも覚えない。
    /// 呼び出し側は毎 tick <c>TyphoonController.HeadingRadians</c>（＝
    /// <c>TyphoonTrack.HeadingAt</c> が経過フレームから閉じた式で出し直す値）を
    /// 渡すので、**経路が曲がれば偏りもその場で回る**。
    /// 「起点での方位」をどこかにキャッシュしないこと ——
    /// それが「曲がる経路に付いてこない偏り」の作り方である。
    ///
    /// ── 控えめであること ────────────────────────────────
    ///
    /// 上限は半径 +<see cref="MaxRadiusBoost"/>、確率 +<see cref="MaxChanceBoost"/> で、
    /// **どちらも「気付く」程度であって「別の台風」ではない。**
    /// 左側は 1 倍ちょうど（弱めない）。指示は「右側を若干強化」であって
    /// 「左側を弱める」ではない。
    /// </summary>
    public static class TrackBias
    {
        /// <summary>右側で被害半径を何倍まで伸ばすか（1.0 ＝ 変えない）。</summary>
        public const float MaxRadiusBoost = 0.18f;

        /// <summary>右側で倒壊確率を何倍まで上げるか（1.0 ＝ 変えない）。</summary>
        public const float MaxChanceBoost = 0.30f;

        /// <summary>
        /// 進行方向に対する左右の位置 [-1, 1]。**+1 が真右**、−1 が真左、
        /// 真正面と真後ろは 0。
        ///
        /// <paramref name="dx"/> / <paramref name="dz"/> は**眼から見た**オフセット
        /// （ワールド XZ）。中心そのもの（長さ 0）と壊れた入力は 0 を返す。
        /// </summary>
        public static float SideOf(float headingRadians, float dx, float dz)
        {
            if (float.IsNaN(headingRadians) || float.IsNaN(dx) || float.IsNaN(dz)) return 0f;

            float length = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (!(length > 0f)) return 0f;

            float cos = (float)System.Math.Cos(headingRadians);
            float sin = (float)System.Math.Sin(headingRadians);

            // right = (sin φ, -cos φ)。クラス doc の検算どおり。
            float side = (dx * sin - dz * cos) / length;

            if (float.IsNaN(side)) return 0f;
            if (side < -1f) return -1f;
            if (side > 1f) return 1f;
            return side;
        }

        /// <summary>
        /// 危険半円側の強さ [0, 1]。反対側と正面・真後ろは 0。
        /// <paramref name="southernHemisphere"/> が true なら左が危険半円になる。
        /// </summary>
        public static float DangerousSideOf(float headingRadians, float dx, float dz,
                                            bool southernHemisphere)
        {
            float side = SideOf(headingRadians, dx, dz);
            if (southernHemisphere) side = -side;
            return side > 0f ? side : 0f;
        }

        /// <summary>
        /// 被害半径の倍率 [1, 1 + <see cref="MaxRadiusBoost"/>]。
        ///
        /// 使い方は「風速の場を右側へ引き伸ばす」＝
        /// <c>WindAt(distance / RadiusFactor(...), ...)</c> である。
        /// **半径そのものを書き換えない** —— 走査の矩形は別に広げること
        /// （広げないと、伸びた側の外縁の建物がそもそも走査に入らない）。
        /// </summary>
        public static float RadiusFactor(float headingRadians, float dx, float dz,
                                         bool southernHemisphere)
        {
            return 1f + MaxRadiusBoost
                        * DangerousSideOf(headingRadians, dx, dz, southernHemisphere);
        }

        /// <summary>
        /// 倒壊確率の倍率 [1, 1 + <see cref="MaxChanceBoost"/>]。
        /// </summary>
        public static float ChanceFactor(float headingRadians, float dx, float dz,
                                         bool southernHemisphere)
        {
            return 1f + MaxChanceBoost
                        * DangerousSideOf(headingRadians, dx, dz, southernHemisphere);
        }
    }
}
