namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 「終了処理に入ったのに終わらない旋風」の判定。
    ///
    /// なぜ要るか: ③の実装で誤っていた IL 前提のひとつが
    /// 「m_targetPos0 は遠方の目標」で、実際は火災の中心そのものだった。
    /// この取り違えが復活すると、終了処理に入った旋風が
    /// ArriveAtDestination に永遠に到達せず、消えないまま残り続ける。
    /// それでも例外は出ないし、ログも出ない。存在検査（パッチが当たっているか）
    /// では捕まらず、「動いた結果」を見て初めて分かる種類の破れなので、
    /// ここで振る舞いとして検査する。
    ///
    /// エンジン非依存の純粋関数として Core に置き、ユニットテストで固定する。
    /// </summary>
    public static class EndingStall
    {
        /// <summary>
        /// 車両 1 台が VehicleAI.SimulationStep を 1 回受けるまでの sim フレーム数。
        ///
        /// IL 実測 (VehicleManager.SimulationStepImpl):
        ///     int bucket = SimulationManager.m_currentFrameIndex &amp; 15;
        ///     for (int i = bucket * 1024; i &lt;= (bucket + 1) * 1024 - 1; i++)
        ///         info.m_vehicleAI.SimulationStep(i, ref buffer[i], refPos);
        /// 16384 スロットを 16 バケツに割っているので、1 台は 16 sim フレームに
        /// 1 回しかステップしない。ここを「1 ステップ = 1 フレーム」と読み違えると
        /// 解体にかかる時間を 16 倍過小評価する（この定数が要る理由そのもの）。
        /// </summary>
        public const int FramesPerVehicleStep = 16;

        /// <summary>
        /// 正常な解体に必要な車両ステップ数。
        ///
        /// IL 実測 (VortexAI.SimulationStep(6 引数) / VortexAI.ArriveAtDestination):
        ///   - 目標までの距離が 1.0 以下になると
        ///         m_angleVelocity = Mathf.Max(0f, m_angleVelocity - 0.05f)
        ///   - ArriveAtDestination を見にいくのは m_angleVelocity &lt; 0.05f のときだけ
        ///         (IL: ldc.r4 0.05 / bge.un で読み飛ばす)
        ///   - VortexAI.ArriveAtDestination は return ++m_waitCounter &gt; 4
        ///
        /// 固定された旋風は目標が 1000m 先にあるので m_angleVelocity は
        /// Mathf.Min(1f, av + 0.05f) で 1.0f に張り付いており、終了開始時は必ず 1.0f。
        /// そこから 0.05f ずつ引くと float32 の丸めで 19 回目に 0.0499998443f となり、
        /// ここで初めて 0.05f を下回る（20 回ではない。1.0f から 0.05f を 19 回
        /// 引いた値を実際に float で計算して確認した）。
        /// その 19 回目が ArriveAtDestination の 1 回目でもあるので、
        /// m_waitCounter が 5 に達する（&gt; 4）のは 19 + 4 = 23 回目のステップ。
        /// </summary>
        public const int TeardownVehicleSteps = 23;

        /// <summary>正常な解体にかかる sim フレーム数。23 × 16 = 368。</summary>
        public const int TeardownFrames = TeardownVehicleSteps * FramesPerVehicleStep;

        /// <summary>
        /// 実測した解体コストに対して、どれだけ余裕を積むか。
        ///
        /// 4 倍で車両ステップ 92 回ぶん。正味の解体に要るのは 23 回なので 69 回の余白があり、
        /// ここが吸収するのは主に「スピンダウンが始まるまでの助走」。終了開始直後は
        /// 車両がまだ移動速度を持っており、m_velocity は毎ステップおよそ 0.85 倍に
        /// 減衰するが、目標までの距離が 1.0 以下になるまではスピンダウンが始まらない
        /// （渦プレハブの m_maxSpeed は IL には無いので初速は確定できない）。
        /// 0.85^69 ≒ 1/76000 なので、初速が上限判定の 4 桁以上うわを行っていても
        /// なお正常系として通る。加えて、こちらの tick と車両のバケツの位相差
        /// （最大 1 ステップ）も同じ余白が吸収する。
        ///
        /// 誤検知の方が高くつく。検出したい破れ（ArriveAtDestination に永遠に
        /// 到達しない）は「終わらない」であって「遅い」ではないので、
        /// 閾値をいくら伸ばしても取り逃がすことはない。
        /// </summary>
        public const float TeardownSafetyFactor = 4f;

        /// <summary>
        /// 1 ゲーム内分あたりの sim フレーム数のバニラ実測値
        /// （SimulationManager.DAYTIME_FRAMES = 65536、1 日 = 1440 分）。
        ///
        /// Core はゲーム API に触れないのでここに既定値を持つが、ゲーム側は
        /// FeatureHost.FramesPerMinute（実物の DAYTIME_FRAMES から毎回割る）を
        /// 渡すこと。ゲーム更新でこの値が変わっても黙ってずれない。
        /// </summary>
        public const float VanillaFramesPerMinute = 65536f / 1440f;

        /// <summary>
        /// 「最大持続時間の何倍まで待つか」。
        ///
        /// これ単体では床にならない。設定スライダー（1〜60 分）は解体コストとは
        /// 無関係な値なので、小さく設定されると閾値が正常な解体時間を下回る。
        /// 実測由来の下限（<see cref="MinimumMinutes"/>）と併用すること。
        /// </summary>
        public const float LifetimeMultiplier = 2f;

        /// <summary>
        /// 実測した解体コストから導く閾値の下限（ゲーム内分）。
        /// 既定の 45.51 フレーム/分なら 368 / 45.51 × 4 ≒ 32.3 分
        /// （正常な解体そのものは ≒ 8.09 分）。
        /// </summary>
        public static float MinimumMinutes(float framesPerMinute)
        {
            float fpm = framesPerMinute > 0f ? framesPerMinute : VanillaFramesPerMinute;
            return TeardownFrames / fpm * TeardownSafetyFactor;
        }

        /// <summary>実際に使われる閾値（ゲーム内分）。ログに出す文言もこれを使うこと。</summary>
        public static float ThresholdMinutes(int maxLifetimeMinutes, float framesPerMinute)
        {
            // 設定が壊れている（0 以下）ときは寿命側の項を捨て、下限だけで判定する。
            // 下限は正常な解体の 4 倍あるので、これで誤検知にはならない。
            float fromLifetime = maxLifetimeMinutes > 0 ? maxLifetimeMinutes * LifetimeMultiplier : 0f;
            float floor = MinimumMinutes(framesPerMinute);
            return fromLifetime > floor ? fromLifetime : floor;
        }

        /// <param name="endingMinutes">終了処理に入ってからのゲーム内経過（分）。</param>
        /// <param name="maxLifetimeMinutes">設定の最大持続時間（ゲーム内分）。</param>
        /// <param name="framesPerMinute">1 ゲーム内分あたりの sim フレーム数。</param>
        public static bool IsStuck(float endingMinutes, int maxLifetimeMinutes, float framesPerMinute)
        {
            return endingMinutes > ThresholdMinutes(maxLifetimeMinutes, framesPerMinute);
        }
    }
}
