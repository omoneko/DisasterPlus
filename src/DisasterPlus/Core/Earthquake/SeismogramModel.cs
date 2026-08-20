using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// **P 波・S 波・コーダを持つ合成地震動。これはこの MOD のモデルであって実測ではない。**
    ///
    /// ── なぜこれが要るのか（依頼②「揺れ方や波形がリアルではない」）────────────
    ///
    /// <see cref="ShakeWaveform"/> はバニラ自身の式（§A-7）をそのまま写している ——
    /// <c>(sin(t·0.63) + sin(t·0.17)) × 振幅</c>、つまり**固定の 2 本の正弦波**である。
    /// 2 本の周期は通約でないので厳密には反復しないが、見た目の肌理は数十フレームで
    /// 一巡し、**到達も、立ち上がりも、減衰も無い**。地震計の記録として見ると、
    /// これは地震動ではなく単なる連続振動である。プレイヤーの指摘は正しい。
    ///
    /// ── 何を作ったのか ──────────────────────────────────────
    ///
    /// 実際の地震記象が持つ、いちばん見分けの付く 3 つの性質だけを作る:
    ///
    ///   1. **P 波の到達** —— 小さく、周波数が高い
    ///   2. **S 波の到達** —— P より遅れて着き、はるかに大きく、周波数が低い
    ///   3. **コーダ** —— S のあと指数的に減衰しつつ、振幅が不規則に揺らぐ
    ///
    /// そして**到達時刻の差が震源距離とともに開く**。これが記象で最も分かりやすい
    /// 性質であり（初期微動継続時間）、②の看板である「距離による震度分布」を
    /// 波形の側から見せる唯一の手掛かりでもある。
    ///
    /// ── 数字の出どころを偽らない ─────────────────────────────
    ///
    ///   - <see cref="VpOverVs"/> = √3 は**実在の物理**である（ポアソン固体の
    ///     P 波速度と S 波速度の比）。比だけは本物を使う。
    ///   - **速度の絶対値は本物ではない。** 実際の地殻は Vp ≒ 6 km/s なので、
    ///     CS の都市（対角 10 km 強）では S-P が 1 秒に満たず、画面上で 1 画素も
    ///     開かない。ここでは「基準距離 <see cref="ReferenceDistanceMetres"/> で
    ///     P が窓の <see cref="PArrivalFractionAtReference"/> の位置に着く」と
    ///     決めている。**見せるために選んだ縮尺であり、実測でも実在の値でもない。**
    ///   - 窓（<paramref name="windowFrames"/>）はバニラの <c>m_activeDuration</c> を
    ///     そのまま使う。読めていないときは呼び出し側が 1 サンプルも取らない
    ///     （<see cref="ShakeWaveform.IsShaking"/> と同じ規律）。
    ///
    /// ── 折り返さないこと（全体レビュー I5 の再発防止）──────────────────
    ///
    /// 記録は 1 sim フレームに 1 点である（<c>SeismographRecorder</c> が tick 内の
    /// 飛んだフレームを埋める）。したがって標本間隔は 1 フレーム、ナイキスト周期は
    /// 2 フレーム。ここで使ういちばん速い成分は P 波の
    /// <see cref="BasePRate"/> = 0.90 rad/frame（周期 ≒7.0 フレーム）で、
    /// ゆらぎの種も <see cref="NoiseSegmentFrames"/> = 11 フレーム刻みである。
    /// **どれも 1 周期あたり 6 点以上とれる。** 速度 3（1 tick = 9 フレーム）でも
    /// 埋めれば足りる。
    /// <b>これ以上速い成分を足さないこと</b> —— 足した瞬間に、第 2 層の
    /// 長周期地震動と見分けの付かない偽の長周期波がグラフに現れる。
    ///
    /// ── 同じ地震は同じ波形（<see cref="DeterministicRandom"/> だけを使う）────────
    ///
    /// 地震ごとのばらつきは全部その地震の種から出す。<c>VanillaRandomizer</c> は
    /// **使わない** —— あれは「バニラが引く値を先読みする」ためだけのもので、
    /// 合成記象はこの MOD が自分で決めることである（<c>DeterministicRandom</c> の doc）。
    /// フレーム番号を種に混ぜないので、同じ地震を同じ時刻で評価すれば必ず同じ値になる。
    ///
    /// <see cref="DisplacementAt"/> は t の**閉じた式**である（状態を持たない）。
    /// 飛んだフレームをあとから埋めても、そのフレームで 1 回評価したのと同じ値になる。
    /// </summary>
    public struct SeismogramModel
    {
        /// <summary>
        /// P 波速度と S 波速度の比。**ここだけは実在の物理**（ポアソン固体で √3）。
        /// 絶対速度は<see cref="ReferenceDistanceMetres"/> の側で決めている。
        /// </summary>
        public const float VpOverVs = 1.7320508f;

        /// <summary>到達時刻の縮尺を決める基準距離（m）。**見せるために選んだ値。**</summary>
        public const float ReferenceDistanceMetres = 4000f;

        /// <summary>基準距離で P が着く位置（窓の長さに対する比）。**見せるために選んだ値。**</summary>
        public const float PArrivalFractionAtReference = 0.12f;

        /// <summary>
        /// S の到達をここで頭打ちにする（窓の長さに対する比）。
        /// これを超えるとコーダを置く場所が無くなり、遠い観測点の記象が
        /// 「S が着いた瞬間に窓が閉じる」だけの絵になる。
        /// **頭打ちに達した先では距離を増やしても S-P は開かない。**
        /// </summary>
        public const float MaxSArrivalFraction = 0.55f;

        /// <summary>P 波の振幅（S 波を 1 とする）。小さい。</summary>
        public const float PAmplitude = 0.22f;

        /// <summary>P 波の搬送波（rad/frame、周期 ≒7.0 フレーム）。**これが最速成分。**</summary>
        public const float BasePRate = 0.90f;

        /// <summary>S 波の搬送波（rad/frame、周期 ≒21 フレーム）。P より低い。</summary>
        public const float BaseSRate = 0.30f;

        /// <summary>ゆらぎの刻み（フレーム）。**2 フレームより十分長いこと**（クラス doc）。</summary>
        public const int NoiseSegmentFrames = 11;

        /// <summary>ゆらぎで振幅が落ちる下限。0 にすると波が完全に途切れて見える。</summary>
        public const float NoiseFloor = 0.55f;

        /// <summary>S 波の立ち上がりに使うフレーム数。**0 にしない**（不連続は耳にも目にも出る）。</summary>
        public const float SRiseFrames = 3f;

        /// <summary>P 波の立ち上がりに使うフレーム数。</summary>
        public const float PRiseFrames = 1.5f;

        /// <summary>コーダの減衰時定数（窓の長さに対する比）。</summary>
        public const float CodaFraction = 0.30f;

        /// <summary>窓の終わりで 0 へ落とす区間（窓の長さに対する比）。</summary>
        public const float TaperFraction = 0.08f;

        /// <summary>
        /// 全体の利得。**S 波の最大振幅をバニラの最大振幅と同じ桁に揃えるためにある。**
        ///
        /// 3 本の搬送波を重みの和で割ってあるので（<see cref="ShapeAt"/>）、
        /// 素の形は理論最大 1 に対して実際には 0.6 前後にしか届かない。そのまま
        /// 差し替えると、**バニラより弱い揺れ**になって「リアルにした結果、
        /// 迫力が落ちた」になる。3 本が同時に揃ったときだけ ±1 で頭打ちになるが、
        /// それは記録計が振り切れた形そのものであり、オフラインで測った
        /// 頭打ちの割合は <c>tools/WaveformPreview</c> の測定に出る。
        /// </summary>
        public const float Gain = 1.4f;

        /// <summary>この型が扱える窓の下限（フレーム）。これ未満なら波形を出さない。</summary>
        public const int MinWindowFrames = 32;

        // ── 地震ごとに種から決まる値 ──────────────────────────────

        private float _window;
        private float _pArrivalScale;
        private float _pRate;
        private float _sRate0;
        private float _sRate1;
        private float _sRate2;
        private float _sPhase0;
        private float _sPhase1;
        private float _sPhase2;
        private float _codaFrames;
        private uint _seed;
        private bool _valid;

        /// <summary>窓の長さが読めていて、波形を出してよいか。</summary>
        public bool Valid { get { return _valid; } }

        /// <summary>この地震の揺れの窓（フレーム）。<c>m_activeDuration</c> そのもの。</summary>
        public float WindowFrames { get { return _window; } }

        /// <summary>
        /// 地震 1 個ぶんの形を種から作る。**毎サンプルではなく、tick / フレームに 1 回作ること**
        /// （<see cref="DisplacementAt"/> は状態を持たないので、作り直しても同じ値になる）。
        ///
        /// <paramref name="windowFrames"/> は <c>m_activeDuration</c>。
        /// 0 や極端に短い値なら <see cref="Valid"/> が false になり、
        /// <see cref="DisplacementAt"/> は 0 を返す —— 窓が分からないまま
        /// 到達時刻を決め打ちすると、地震が終わった後も伸びる波形になる。
        /// </summary>
        public static SeismogramModel For(uint seed, uint windowFrames)
        {
            SeismogramModel m = new SeismogramModel();

            if (windowFrames < MinWindowFrames) return m;

            m._seed = seed;
            m._window = windowFrames;
            m._valid = true;

            // 見かけの速度のばらつき（±15%）。同じ距離でも地震ごとに初期微動継続時間が違う。
            float velocityJitter = 0.85f + 0.30f * DeterministicRandom.Unit(seed, 1u);
            m._pArrivalScale = PArrivalFractionAtReference * m._window * velocityJitter
                               / ReferenceDistanceMetres;

            // 搬送波は ±10% だけ振る。**上限を上げない**（クラス doc の折り返しの話）。
            m._pRate = BasePRate * (0.92f + 0.16f * DeterministicRandom.Unit(seed, 2u));

            // S は 3 本の非通約な成分。1 本だと「同じ波形が連続している」に戻る。
            float sJitter = 0.90f + 0.20f * DeterministicRandom.Unit(seed, 3u);
            m._sRate0 = BaseSRate * sJitter;
            m._sRate1 = BaseSRate * sJitter * 1.618f;   // 黄金比。通約にならない組にする
            m._sRate2 = BaseSRate * sJitter * 0.577f;

            m._sPhase0 = 6.2831853f * DeterministicRandom.Unit(seed, 4u);
            m._sPhase1 = 6.2831853f * DeterministicRandom.Unit(seed, 5u);
            m._sPhase2 = 6.2831853f * DeterministicRandom.Unit(seed, 6u);

            m._codaFrames = CodaFraction * m._window
                            * (0.75f + 0.50f * DeterministicRandom.Unit(seed, 7u));
            if (m._codaFrames < 1f) m._codaFrames = 1f;

            return m;
        }

        /// <summary>
        /// P 波の到達（窓の頭 <c>e = 0</c> からのフレーム数）。距離に比例する。
        /// <see cref="Valid"/> が false なら 0。
        /// </summary>
        public float PArrivalFrames(float distanceMetres)
        {
            if (!_valid) return 0f;
            if (float.IsNaN(distanceMetres) || distanceMetres < 0f) distanceMetres = 0f;

            float p = distanceMetres * _pArrivalScale;
            float maxP = MaxSArrivalFraction * _window / VpOverVs;
            return p > maxP ? maxP : p;
        }

        /// <summary>
        /// S 波の到達（同上）。<c>P × √3</c>。**震源直上では P と同時に着く**
        /// （距離 0 なら S-P も 0）——これは近似ではなく、そういうものである。
        /// </summary>
        public float SArrivalFrames(float distanceMetres)
        {
            return PArrivalFrames(distanceMetres) * VpOverVs;
        }

        /// <summary>
        /// 初期微動継続時間（S-P、フレーム）。**距離とともに開く**のがこのモデルの看板。
        /// <see cref="MaxSArrivalFraction"/> の頭打ちに達した先では開かない。
        /// </summary>
        public float SMinusPFrames(float distanceMetres)
        {
            return PArrivalFrames(distanceMetres) * (VpOverVs - 1f);
        }

        /// <summary>
        /// 符号付きの変位。<paramref name="t"/> は <c>e</c>（フレーム、小数を含む）。
        ///
        /// 振幅の基準はバニラと同じ <see cref="ShakeWaveform.PeakAmplitudeAt"/> の 2 倍
        /// （＝<see cref="ShakeWaveform.MaxDisplacement"/>）なので、**満目盛りは
        /// バニラの波形と共通**である。並べて描いたときに縦の尺度が揃う。
        ///
        /// 窓の外・<see cref="Valid"/> が false・NaN では 0。
        /// </summary>
        public float DisplacementAt(float distanceMetres, float t)
        {
            if (!_valid) return 0f;
            if (float.IsNaN(distanceMetres) || float.IsNaN(t)) return 0f;
            if (t <= 0f || t >= _window) return 0f;

            float shape = ShapeAt(distanceMetres, t);
            if (shape == 0f) return 0f;

            // PeakAmplitudeAt の 2 倍 ＝ 距離 0 でちょうど MaxDisplacement（0.60）。
            return shape * 2f * ShakeWaveform.PeakAmplitudeAt(distanceMetres);
        }

        /// <summary>
        /// 距離を除いた形（絶対値は必ず 1 以下）。テストが上限を直接見る。
        /// </summary>
        public float ShapeAt(float distanceMetres, float t)
        {
            if (!_valid) return 0f;
            if (float.IsNaN(distanceMetres) || float.IsNaN(t)) return 0f;
            if (t <= 0f || t >= _window) return 0f;

            float tP = PArrivalFrames(distanceMetres);
            float tS = tP * VpOverVs;

            float value = 0f;

            // ── P 波: 小さく、速く、S が着くまでに消える ──────────────
            if (t >= tP)
            {
                float dt = t - tP;
                float tauP = 0.35f * (tS - tP);
                if (tauP < 4f) tauP = 4f;

                float envelope = Rise(dt, PRiseFrames) * Decay(dt, tauP);
                value += PAmplitude * envelope
                         * (float)System.Math.Sin(t * _pRate);
            }

            // ── S 波とコーダ: 大きく、遅く、不規則に減衰する ────────────
            if (t >= tS)
            {
                float dt = t - tS;
                float envelope = Rise(dt, SRiseFrames) * Decay(dt, _codaFrames);

                // ゆらぎ。**コーダの振幅を不規則にするのはここ 1 箇所だけ。**
                // 搬送波の周波数を揺らすと折り返しの余裕を食う（クラス doc）。
                envelope *= NoiseFloor + (1f - NoiseFloor) * Wobble(t);

                float carrier =
                    (float)(System.Math.Sin(t * _sRate0 + _sPhase0)
                            + 0.55 * System.Math.Sin(t * _sRate1 + _sPhase1)
                            + 0.40 * System.Math.Sin(t * _sRate2 + _sPhase2))
                    / 1.95f;

                value += envelope * carrier;
            }

            value *= Gain;

            // 窓の終わりで 0 へ落とす。落とさないと窓が閉じた瞬間にカメラが跳ねる。
            float taper = TaperFraction * _window;
            if (taper > 0f)
            {
                float left = _window - t;
                if (left < taper) value *= left / taper;
            }

            if (value > 1f) return 1f;
            if (value < -1f) return -1f;
            return value;
        }

        /// <summary>立ち上がり [0,1]。<paramref name="frames"/> 以下なら線形に上げる。</summary>
        private static float Rise(float dt, float frames)
        {
            if (frames <= 0f) return 1f;
            if (dt >= frames) return 1f;
            return dt <= 0f ? 0f : dt / frames;
        }

        /// <summary>指数減衰 <c>exp(-dt/tau)</c>。<paramref name="tau"/> は 1 未満にしない。</summary>
        private static float Decay(float dt, float tau)
        {
            if (tau < 1f) tau = 1f;
            float x = dt / tau;
            // 8 時定数（振幅 1/3000）より先は 0 でよい。exp を呼ばずに済ませる。
            if (x > 8f) return 0f;
            return (float)System.Math.Exp(-x);
        }

        /// <summary>
        /// [0,1] の不規則な包絡（値ノイズ）。<see cref="NoiseSegmentFrames"/> フレームごとの
        /// 乱数を smoothstep で繋ぐ。**乱数に t の小数部やフレーム番号そのものを混ぜない** ——
        /// 混ぜると同じ時刻を 2 回評価したときに違う値が出て、閉じた式でなくなる。
        /// </summary>
        private float Wobble(float t)
        {
            float scaled = t / NoiseSegmentFrames;
            int cell = (int)scaled;
            if (scaled < 0f) cell = 0;

            float f = scaled - cell;
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;

            float a = DeterministicRandom.Unit(_seed, unchecked((uint)cell) + 0x51ED2701u);
            float b = DeterministicRandom.Unit(_seed, unchecked((uint)(cell + 1)) + 0x51ED2701u);

            float s = f * f * (3f - 2f * f);
            return a + (b - a) * s;
        }
    }
}
