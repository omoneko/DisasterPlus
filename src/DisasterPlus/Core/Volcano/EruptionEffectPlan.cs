namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 噴火の見た目に渡す量（<b>粒子の密度と、湧かす円の半径</b>）を強さから決める。
    /// **エンジン非依存の純関数だけ。** Unity の型も、ゲームの型も、乱数も出てこない。
    ///
    /// ── なぜ Core に置くのか ─────────────────────────────────
    ///
    /// バニラの <c>ParticleEffect.RenderEffect</c> に渡す <c>magnitude</c> は
    /// **粒子の密度**であって大きさではない。1 フレームに湧く粒子数は
    ///
    /// <code>
    /// count = max(100, PI * r^2) * (timeDelta * magnitude * 0.01) * rateOverTime
    /// </code>
    ///
    /// で決まる（IL 実測）。つまり「見た目の強さ」を作るつまみは <c>magnitude</c> と
    /// <c>r</c> の 2 本で、どちらも**単なる数の写像**である。写像を Game 側に散らすと
    /// 実機を起動しないと 1 行も確かめられなくなるので、ここに集めてテストで固定する。
    ///
    /// ── 帯の根拠 ────────────────────────────────────────
    ///
    /// <c>magnitude &lt;= 1</c> の帯は<b>建物火災の慣習</b>（<c>m_fireIntensity / 255</c>）で
    /// あって、<c>ParticleEffect</c> を直に呼ぶときには当てはまらない。
    /// バニラ自身、陥没穴は 2.0 まで上げているし、<c>rateOverTime</c> が 15 しかない
    /// <c>Factory Smoke</c> を柱に見せるには 2 桁が要る（半径 30 m・60 fps で
    /// <c>magnitude = 1</c> だと毎フレーム 5 粒子にしかならない）。
    /// **したがってここの数はすべて⑤が決めた演出値**であり、物理量ではない。
    /// </summary>
    public static class EruptionEffectPlan
    {
        // ── 噴煙（灰の柱）──────────────────────────────────

        /// <summary>噴煙の密度の下限（強さ 0 のとき）。</summary>
        public const float PlumeMagnitudeMin = 14f;

        /// <summary>噴煙の密度の上限（強さ 1 のとき）。</summary>
        public const float PlumeMagnitudeMax = 78f;

        /// <summary>噴煙を湧かす円の半径（火口半径に対する比）の下限。</summary>
        public const float PlumeRadiusFloorRatio = 0.30f;

        /// <summary>強さで足される半径（火口半径に対する比）。</summary>
        public const float PlumeRadiusGainRatio = 0.28f;

        // ── 炎（火口とラバの縁）─────────────────────────────

        /// <summary>炎の密度の下限。<c>Fire Particles</c> は <c>rateOverTime</c> が 200 と
        /// 高いので、噴煙よりずっと小さい数で足りる。</summary>
        public const float FlameMagnitudeMin = 1.6f;

        /// <summary>炎の密度の上限。</summary>
        public const float FlameMagnitudeMax = 9f;

        /// <summary>炎を湧かす円の半径（火口半径に対する比）の下限。</summary>
        public const float FlameRadiusFloorRatio = 0.18f;

        /// <summary>強さで足される炎の半径（火口半径に対する比）。</summary>
        public const float FlameRadiusGainRatio = 0.22f;

        // ── 噴石 ───────────────────────────────────────

        /// <summary>噴石が 1 回噴き上がる長さ（秒）。</summary>
        public const float EjectaBurstSeconds = 0.55f;

        /// <summary>噴石の間隔の上限（＝いちばん弱いとき。秒）。</summary>
        public const float EjectaPeriodMaxSeconds = 7.5f;

        /// <summary>噴石の間隔の下限（＝いちばん強いとき。秒）。</summary>
        public const float EjectaPeriodMinSeconds = 1.6f;

        /// <summary>噴石 1 回の密度の下限。</summary>
        public const float EjectaMagnitudeMin = 6f;

        /// <summary>噴石 1 回の密度の上限。</summary>
        public const float EjectaMagnitudeMax = 34f;

        /// <summary>噴石を湧かす円の半径（火口半径に対する比）。</summary>
        public const float EjectaRadiusRatio = 0.22f;

        /// <summary>
        /// 半径がこれ未満なら「火口が読めていない」とみなして使わない（m）。
        /// 0 を渡されても <c>max(100, PI r^2)</c> のおかげで粒子は湧くので、
        /// **0 のまま素通りさせると 1 点から噴くことになる。**
        /// </summary>
        public const float MinRadiusMetres = 4f;

        /// <summary>噴煙の密度。<paramref name="unit"/> は強さ <c>[0,1]</c>。</summary>
        public static float PlumeMagnitude(float unit)
        {
            return Lerp(PlumeMagnitudeMin, PlumeMagnitudeMax, Clamp01(unit));
        }

        /// <summary>噴煙を湧かす円の半径（m）。</summary>
        public static float PlumeRadiusMetres(float craterRadiusMetres, float unit)
        {
            return RadiusFrom(craterRadiusMetres, PlumeRadiusFloorRatio,
                              PlumeRadiusGainRatio, unit);
        }

        /// <summary>炎の密度。</summary>
        public static float FlameMagnitude(float unit)
        {
            return Lerp(FlameMagnitudeMin, FlameMagnitudeMax, Clamp01(unit));
        }

        /// <summary>炎を湧かす円の半径（m）。</summary>
        public static float FlameRadiusMetres(float craterRadiusMetres, float unit)
        {
            return RadiusFrom(craterRadiusMetres, FlameRadiusFloorRatio,
                              FlameRadiusGainRatio, unit);
        }

        /// <summary>噴石を湧かす円の半径（m）。**強さでは変えない**（火口の口の広さである）。</summary>
        public static float EjectaRadiusMetres(float craterRadiusMetres)
        {
            return RadiusFrom(craterRadiusMetres, EjectaRadiusRatio, 0f, 0f);
        }

        /// <summary>
        /// 噴石の間隔（秒）。強いほど短い。
        /// </summary>
        public static float EjectaPeriodSeconds(float unit)
        {
            return Lerp(EjectaPeriodMaxSeconds, EjectaPeriodMinSeconds, Clamp01(unit));
        }

        /// <summary>
        /// 時計を間隔で畳んだ位相（秒、<c>[0, period)</c>）。
        /// **負の時計と 0 以下の間隔でも NaN を外へ出さない。**
        /// </summary>
        public static float BurstPhaseSeconds(float clockSeconds, float periodSeconds)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;
            if (IsBad(periodSeconds) || periodSeconds <= 0f) return 0f;

            float t = clockSeconds - (float)System.Math.Floor(clockSeconds / periodSeconds)
                                     * periodSeconds;
            if (IsBad(t) || t < 0f) return 0f;
            if (t >= periodSeconds) return 0f;
            return t;
        }

        /// <summary>
        /// 噴石の密度。<b>噴いていないあいだは 0 を返す</b>ので、
        /// 呼び出し側は <c>&gt; 0</c> のときだけ <c>RenderEffect</c> を呼べばよい。
        ///
        /// 山なりの窓（立ち上がって落ちる）にしてあるのは、切り替えを矩形にすると
        /// 1 フレームだけ濃い粒子が出て**点滅して見える**ためである。
        /// </summary>
        public static float EjectaMagnitude(float unit, float phaseSeconds)
        {
            float u = Clamp01(unit);
            if (IsBad(phaseSeconds) || phaseSeconds < 0f) return 0f;
            if (phaseSeconds >= EjectaBurstSeconds) return 0f;

            // 0 → 1 → 0 の山。頂点は窓の真ん中。
            float w = phaseSeconds / EjectaBurstSeconds;
            float shape = 1f - System.Math.Abs(w * 2f - 1f);
            if (shape < 0f) shape = 0f;

            return Lerp(EjectaMagnitudeMin, EjectaMagnitudeMax, u) * shape;
        }

        private static float RadiusFrom(float craterRadiusMetres, float floorRatio,
                                        float gainRatio, float unit)
        {
            if (IsBad(craterRadiusMetres) || craterRadiusMetres <= 0f) return MinRadiusMetres;

            float r = craterRadiusMetres * (floorRatio + gainRatio * Clamp01(unit));
            if (IsBad(r) || r < MinRadiusMetres) return MinRadiusMetres;
            return r;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
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
