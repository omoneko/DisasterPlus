using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// **竜巻を出さずに竜巻並みの被害だけを起こす**ための、局所被害域（パッチ）の
    /// 撒き方と寿命。<b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── 持ち主の指示 ──────────────────────────────────────
    ///
    /// > 竜巻を発生させずに竜巻の被害だけを複数発生させてください
    ///
    /// つまり<b>災害の実体（<c>TornadoAI</c>）も漏斗のメッシュも作らない</b>。
    /// 作るのは「台風の下のあちこちで、短いあいだ、狭い範囲だけが竜巻並みに壊れる」
    /// という現象だけである。バニラの竜巻を借りていた旧実装（随伴竜巻）は退役した。
    ///
    /// **やめた側の利点も書いておく。** バニラ竜巻の破壊は
    /// <c>DisasterHelpers.DestroyStuff</c> を通るので Natural Disasters Renewal が
    /// 丸ごと置き換えていた。パッチは <c>BuildingAI.CollapseBuilding</c> を直接呼ぶ
    /// （＝④の風害と同じ経路）ので、**NDR と完全に無衝突**になった。
    ///
    /// ── 一団（世代）で撒く ────────────────────────────────
    ///
    /// パッチは <see cref="SpawnIntervalFrames"/> フレームごとに 1 個生まれ、
    /// <see cref="LifetimeFrames"/> フレームで消える。したがって同時に生きているのは
    /// <c>ceil(Lifetime / Interval)</c> 個で、<see cref="MaxActivePatches"/> が
    /// その上限を宣言している（テストが両者の整合を固定している）。
    /// **これが 1 tick あたりの仕事量の上限を決める 1 本目である。**
    ///
    /// 「何番目のパッチか」（序数）だけが状態で、位置も大きさも寿命も**序数の関数**である。
    /// ゲーム側は序数の範囲を訊いて、その場で位置を組み立て直す ——
    /// 台帳を持ち回さないので、**台風が消えたらパッチも 1 個残らず消える**
    /// （寿命の管理を忘れて残る、という壊れ方が構造的に起きない）。
    ///
    /// ── 危険半円へ寄せる ────────────────────────────────
    ///
    /// パッチは進行方向の危険半円側（北半球なら右。<see cref="TrackBias"/>）に寄る。
    /// 反対側にも出るが、**多数派は危険半円**である（テストが分布で固定している）。
    ///
    /// 乱数は <see cref="DeterministicRandom"/> だけ。<c>VanillaRandomizer</c> は
    /// 使わない —— ここで決めるのはバニラが引く値ではなく④が発明した判断である。
    /// **フレームを混ぜない**（混ぜるとパッチが毎 tick 場所を変えて瞬間移動する）。
    /// </summary>
    public static class GustPatchPlan
    {
        /// <summary>新しいパッチが生まれる間隔（台風の経過フレーム）。</summary>
        public const uint SpawnIntervalFrames = 128u;

        /// <summary>1 個のパッチが生きているフレーム数。**短命**であること。</summary>
        public const uint LifetimeFrames = 320u;

        /// <summary>同時に生きうるパッチ数の上限。<see cref="AliveRange"/> はこれを超えない。</summary>
        public const int MaxActivePatches = 4;

        /// <summary>パッチの半径（m）の下限・上限。**狭いこと** —— 竜巻の被害幅である。</summary>
        public const float MinRadiusMetres = 45f;

        public const float MaxRadiusMetres = 95f;

        /// <summary>パッチが置かれる半径 ÷ 暴風域半径の下限・上限。
        /// 眼のすぐ外から暴風域の縁までのあいだに撒く。</summary>
        public const float MinOrbitFraction = 0.25f;

        public const float MaxOrbitFraction = 0.95f;

        /// <summary>危険半円へ寄せる強さ。1 で一様、2 以上で寄る。**2 乗**を使う。</summary>
        private const float BiasExponent = 2f;

        private const float Pi = 3.14159265f;
        private const float HalfPi = 1.57079633f;

        /// <summary>序数から乱数の種を作るときの混ぜ物。**固定値**。</summary>
        private const uint AngleSalt = 0x47555331u;    // "GUS1"
        private const uint SpreadSalt = 0x47555332u;
        private const uint OrbitSalt = 0x47555333u;
        private const uint RadiusSalt = 0x47555334u;
        private const uint StrengthSalt = 0x47555335u;

        /// <summary>序数 <paramref name="ordinal"/> のパッチが生まれる経過フレーム。</summary>
        public static uint BirthFrameOf(uint ordinal)
        {
            return ordinal * SpawnIntervalFrames;
        }

        /// <summary>
        /// <paramref name="elapsedFrames"/> の時点で生きている序数の範囲
        /// <c>[first, last]</c>（両端を含む）。生きているものが 1 つも無ければ false。
        ///
        /// **返る個数は必ず <see cref="MaxActivePatches"/> 以下である**（テストが固定）。
        /// </summary>
        public static bool AliveRange(uint elapsedFrames, out uint first, out uint last)
        {
            first = 0u;
            last = 0u;

            // 今までに生まれた最後の序数。
            last = elapsedFrames / SpawnIntervalFrames;

            // 生まれてから LifetimeFrames 未満のものだけが生きている。
            uint oldestBirth = elapsedFrames >= LifetimeFrames
                ? elapsedFrames - LifetimeFrames + 1u
                : 0u;

            // 切り上げ除算（そのフレーム以降に生まれた序数の最小値）。
            first = (oldestBirth + SpawnIntervalFrames - 1u) / SpawnIntervalFrames;

            if (first > last) return false;

            // 上限で頭を落とす。**新しいほうを残す**（古いほうはもう消えかけている）。
            if (last - first + 1u > (uint)MaxActivePatches)
            {
                first = last - (uint)MaxActivePatches + 1u;
            }
            return true;
        }

        /// <summary>
        /// パッチの生涯のどこか [0, 1]。生まれた瞬間 0、消える瞬間 1。
        /// 生きていない序数には 1 を返す（＝もう終わっている）。
        /// </summary>
        public static float LifePhase(uint ordinal, uint elapsedFrames)
        {
            uint birth = BirthFrameOf(ordinal);
            if (elapsedFrames <= birth) return 0f;

            uint age = elapsedFrames - birth;
            if (age >= LifetimeFrames) return 1f;
            return (float)age / LifetimeFrames;
        }

        /// <summary>
        /// パッチ 1 個ぶんの置き場所と大きさ。**序数だけの関数**なので、
        /// 呼び出し側が台帳を持たなくても毎 tick 同じ答えが返る。
        ///
        /// <paramref name="relativeAngleRadians"/> は<b>進行方位からの相対角</b>
        /// （ワールド角ではない）。呼び出し側が
        /// <c>heading + relativeAngle</c> でワールド角にする ——
        /// こうしておくと**経路が曲がればパッチの散らばりも一緒に回る**。
        ///
        /// <paramref name="orbitFraction"/> は暴風域半径に対する比、
        /// <paramref name="radiusMetres"/> はパッチ自身の半径（m）、
        /// <paramref name="strengthFraction"/> は破壊力の比 [0, 1]。
        /// </summary>
        public static void Patch(ushort typhoonId, uint ordinal, bool southernHemisphere,
                                 out float relativeAngleRadians, out float orbitFraction,
                                 out float radiusMetres, out float strengthFraction)
        {
            uint id = typhoonId;

            // 危険半円の中心方向（進行方位からの相対角）。
            // TrackBias の right = (sin φ, -cos φ) は角 φ - 90° の向きなので、
            // 北半球の危険半円は相対角 -90°、南半球は +90° である。
            float centre = southernHemisphere ? HalfPi : -HalfPi;

            // 危険半円からのずれ。2 乗で 0 側（＝危険半円）へ寄せる。
            float u = DeterministicRandom.Unit(id ^ SpreadSalt, ordinal);
            float spread = Pi * Power(u, BiasExponent);

            float side = DeterministicRandom.Unit(id ^ AngleSalt, ordinal) < 0.5f ? -1f : 1f;
            relativeAngleRadians = centre + side * spread;

            orbitFraction = MinOrbitFraction
                + (MaxOrbitFraction - MinOrbitFraction)
                  * DeterministicRandom.Unit(id ^ OrbitSalt, ordinal);

            radiusMetres = MinRadiusMetres
                + (MaxRadiusMetres - MinRadiusMetres)
                  * DeterministicRandom.Unit(id ^ RadiusSalt, ordinal);

            // 0.6〜1.0。**0 にはしない** —— 何も壊さないパッチは「出ていない」と
            // 見分けが付かず、診断が読めなくなる。
            strengthFraction = 0.6f
                + 0.4f * DeterministicRandom.Unit(id ^ StrengthSalt, ordinal);
        }

        /// <summary>
        /// <c>System.Math.Pow</c> を避けた 2 乗（<see cref="BiasExponent"/> は 2 固定）。
        /// 指数を変えるときはここも直すこと。
        /// </summary>
        private static float Power(float value, float exponent)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            if (value >= 1f) return 1f;
            return exponent == 2f ? value * value : value;
        }
    }
}
