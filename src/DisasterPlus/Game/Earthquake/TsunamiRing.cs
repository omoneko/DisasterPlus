using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>震源から同心円状に立つ津波。</b>**sim スレッド専用。**
    ///
    /// ── 何をしているのか ──────────────────────────────────────────
    ///
    /// 震源に <c>WaterSource</c> の <c>TYPE_NATURAL</c> を 1 個置き、その
    /// <c>m_target</c>（＝円を満たす目標水位）を DLC の津波と<b>同じ波形</b>で
    /// 上下させる。円は目標より低ければ<b>水を湧かせ</b>、高ければ<b>抜く</b>ので、
    /// <b>引き波 → 押し波 → 引き波</b>がそのまま海に出る。
    /// 波形の中身は <see cref="TsunamiRingShape"/>（Core 層）。
    ///
    /// ── なぜ <c>TYPE_IMPACT</c> をやめたのか ─────────────────────────
    ///
    /// 前身の <c>TsunamiWave</c> は <c>TYPE_IMPACT</c>、海中に置く<b>仮想の丘</b>だった。
    /// 丘は水を押しのけるだけで<b>作らない</b>ので、出せるのは正味ゼロの双極子である。
    /// オフラインで同じ棚に並べた結果（2026-08-31、水深 174 m、汀線 6.1 km）:
    ///
    /// <list type="bullet">
    /// <item>DLC の津波（外周の境界条件、強度 100）… 汀線 <b>84.8 m</b></item>
    /// <item><c>TYPE_IMPACT</c> の丘（drive 3857）… 汀線 <b>19.3 m</b></item>
    /// </list>
    ///
    /// 差は振幅ではなく<b>水を作るかどうか</b>だった。丘をいくら大きくしても
    /// 湧き出しの真似はできない（海底を掘り抜くだけである）。
    ///
    /// ★★ **だが DLC の津波をそのまま呼ぶのは解析ではない。**（所有者、2026-08-31）
    ///   あれは<b>マップ外周でしか評価されない</b>ので、震源から同心円にはならない。
    ///   <c>WaterSource</c> は同じ「目標水位まで満たす」装置を<b>どこにでも置ける</b>形で
    ///   持っているので、<b>波形は DLC のまま、置き場所だけ震源へ移す</b>。
    ///
    /// ── 危険（<c>TyphoonFlood</c> のクラス doc と同じ、ただし一段重い）─────
    ///
    /// <list type="number">
    /// <item><c>LockWaterSource</c> は <b>Monitor を取ったまま返る</b>。
    ///   <c>UnlockWaterSource</c> が<b>唯一の解放経路</b>なので必ず
    ///   <c>finally</c> に置く。落とすと<b>水スレッドが永久に止まる</b>。</item>
    /// <item><c>LockWaterSource</c> は添字を検査しない。
    ///   <see cref="OwnsSource"/> を通ってからでなければ呼ばない。</item>
    /// <item>★★ <b><c>WaterSource</c> はセーブに焼き付く</b>
    ///   （<c>WaterSimulation+Data.Serialize</c> が書く、IL 実測）。
    ///   置いたまま保存されると、MOD を外してもその都市に<b>永久に水が湧き続ける</b>。
    ///   だから解放は
    ///   <list type="bullet">
    ///   <item>波形が終わったとき</item>
    ///   <item>都市を出るとき（<c>Reset</c>）</item>
    ///   <item><b>保存の直前</b>（<see cref="SuspendForSave"/>）</item>
    ///   </list>
    ///   の三箇所すべてで行う。<c>TyphoonFlood</c> は「<c>CreateWaterSource</c> に
    ///   手を伸ばすな」と書いてあるが、それはこの三箇所を守れないなら、の意味である。</item>
    /// <item><c>CreateWaterSource</c> は <c>m_type == 0</c> の枠を<b>使い回す</b>
    ///   （first-fit、IL_0020-005A）。だから<b>握った番号だけでは足りない</b> ——
    ///   書く前に必ず種別と位置を照合する。</item>
    /// </list>
    /// </summary>
    public static class TsunamiRing
    {
        /// <summary><c>WaterSource.TYPE_NATURAL</c>。</summary>
        private const ushort TypeNatural = 1;

        /// <summary>1 水ステップ ＝ 64 sim フレーム（IL 実測、<c>SetCurrentWaterFrame</c>）。</summary>
        private const int FramesPerWaterStep = 64;

        /// <summary>16 m セルの数。<c>BlockHeights</c> の添字は <c>z*(1080+1)+x</c>。</summary>
        private const int GridCells = 1080;

        /// <summary>マップ半幅（m）。</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// 震源の円の半径（m）。**遠くへ届かせるのは高さではなく体積**なので、
        /// ここが効く（オフライン実測 2026-08-31: 半径 1280 m → 汀線 35 m、
        /// 半径 3840 m → 汀線 83 m、いずれも強度 100・水深 174 m・汀線 6.1 km）。
        /// </summary>
        private const float RadiusMetres = 3840f;

        /// <summary>
        /// <b>押し波の頭打ち（m、絶対値）。</b>強度 255 のときの値。
        ///
        /// ★★ **上げると弱くなる。**（オフライン実測 2026-08-31、1081 格子・
        ///   汀線 13 km・768 水ステップ）
        ///
        /// <list type="bullet">
        /// <item>蓋 40 m … 汀線 <b>66.98 m</b>、浸水 <b>2592 m</b>、震源 189.7 m</item>
        /// <item>蓋 55 m … 汀線 65.38 m、浸水 2384 m、震源 233.2 m</item>
        /// <item>蓋 70 m … 汀線 64.56 m、浸水 2240 m、震源 255.4 m</item>
        /// <item>蓋 20 m … 汀線 25.44 m、浸水 1280 m、震源 100.0 m</item>
        /// </list>
        ///
        /// 高い塔を立てても遠くへは行かない —— 効くのは<b>体積</b>だからである。
        /// 40 m は DLC の津波（強度 100）とほぼ同じ威力になる点でもある
        /// （同条件で汀線 66.81 m、浸水 2592 m）。
        ///
        /// ★ <b>水深にはしない。</b>深さ 60 / 100 / 174 m で汀線 67.1 / 66.4 / 67.0 m と
        ///   ほぼ変わらなかったので、割合にする根拠が無い。
        /// </summary>
        private const float MaxRiseMetres = 40f;

        /// <summary>
        /// 引き波で抜いてよい水深の割合。**海底を露出させない。**
        /// 押しと違いこちらは<b>水深で縛る</b> —— 深さ 60 m で蓋 60 m にすると
        /// 水柱が 100% 抜けて海底が 94 水ステップ露出した（実測 2026-08-31）。
        /// 0.5 にしても深い海の威力は落ちない（汀線 66.98 m のまま）。
        /// </summary>
        private const float MaxDrawFraction = 0.5f;

        /// <summary>
        /// 波形の長さ（水ステップ）。バニラは 256。
        ///
        /// ★★ **長さは効く。** 同じ蓋 40 m で 512 歩なら汀線 52.77 m、
        ///   768 歩なら 66.98 m（実測 2026-08-31）。ただし 1024 歩は 29.80 m と
        ///   かえって落ちる —— 振幅の減衰項 <c>(65536 - t)/65536</c> が
        ///   終盤で 0 に近づき、後半の押しが消えるからである。
        ///   768 水ステップ ＝ 49,152 sim フレーム ≒ 13.6 実分。
        /// </summary>
        private const int DurationSteps = 768;

        private static ushort _source;
        private static Vector3 _centre;
        private static long _rate;
        private static int _deltaUnits;
        private static int _ticks;
        private static int _seaUnits;
        private static int _depthUnits;
        private static int _riseCapUnits;
        private static int _drawCapUnits;
        private static uint _lastFrame;
        private static bool _running;

        /// <summary>いま津波を出しているか。</summary>
        public static bool Running { get { return _running; } }

        /// <summary>直近の理由（パネルと診断に出す）。</summary>
        public static string Detail { get; private set; }

        /// <summary>いまの目標水位の平常からのずれ（m）。診断用。</summary>
        public static float OffsetMetres { get; private set; }

        /// <summary>震源の水深（m）。</summary>
        public static float DepthMetres { get { return _depthUnits / 64f; } }

        /// <summary>これまでに見た最高の押し波（m、目標水位ベース）。</summary>
        public static float PeakRiseMetres { get; private set; }

        /// <summary>波形の何ステップ目か。</summary>
        public static int ElapsedSteps
        {
            get { return _ticks / TsunamiRingShape.TicksPerWaterStep; }
        }

        /// <summary>波形の長さ（水ステップ）。</summary>
        public static int TotalSteps { get { return DurationSteps; } }

        /// <summary>
        /// 都市を出るとき／機能を切るときに呼ぶ。**冪等。例外を投げない。**
        /// ★★ ここを通らないと水源がセーブに残る（クラス doc §3）。
        /// </summary>
        public static void Reset()
        {
            Release();
            _running = false;
            _ticks = 0;
            _lastFrame = 0u;
            _deltaUnits = 0;
            _rate = 0L;
            OffsetMetres = 0f;
            PeakRiseMetres = 0f;
        }

        /// <summary>
        /// 津波を立てる。**sim スレッドから呼ぶこと。**
        /// </summary>
        /// <returns>立てられたか。false のとき <see cref="Detail"/> に理由が入る。</returns>
        public static bool Begin(Vec3 epicentre, byte intensity, uint frame)
        {
            Detail = null;

            if (_running)
            {
                Detail = "a tsunami is already running";
                return false;
            }

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                Detail = "the water simulation is not there";
                return false;
            }

            float depth = TsunamiWave.DepthAt(terrain, epicentre.X, epicentre.Z);
            if (depth <= 0f)
            {
                Detail = "the epicentre is not in the sea";
                return false;
            }

            _seaUnits = (int)(terrain.WaterSimulation.m_currentSeaLevel * 64f);
            _depthUnits = (int)(depth * 64f);
            _centre = new Vector3(epicentre.X, 0f, epicentre.Z);
            _rate = TsunamiRingShape.RateForRadiusMetres(RadiusMetres);
            _deltaUnits = TsunamiRingShape.VanillaDeltaUnits(intensity);

            // ★ 蓋は震度で決まる。255 で 40 m。**これより上げても弱くなる**ので、
            //   強い地震ほど高い塔、にはしない（MaxRiseMetres の doc）。
            _riseCapUnits = (int)(MaxRiseMetres * 64f * intensity / 255f);
            if (_riseCapUnits < 64) _riseCapUnits = 64;

            // ★ 引きは水深に縛る。押しの蓋より深くは引かない。
            _drawCapUnits = (int)(_depthUnits * MaxDrawFraction);
            if (_drawCapUnits > _riseCapUnits) _drawCapUnits = _riseCapUnits;
            if (_drawCapUnits < 0) _drawCapUnits = 0;

            _ticks = 0;
            _lastFrame = frame;
            OffsetMetres = 0f;
            PeakRiseMetres = 0f;

            WaterSource src = new WaterSource();
            src.m_type = TypeNatural;
            src.m_inputPosition = _centre;
            src.m_outputPosition = _centre;
            src.m_target = (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);
            src.m_inputRate = 0u;
            src.m_outputRate = 0u;
            src.m_water = 0u;
            src.m_pollution = 0u;
            src.m_flow = 0u;

            ushort handle;
            if (!terrain.WaterSimulation.CreateWaterSource(out handle, src) || handle == 0)
            {
                Detail = "the game would not give us a water source slot";
                return false;
            }

            _source = handle;
            _running = true;

            // ★ バニラの津波と同じ物差しで自分の波も測る（SeaWatch のクラス doc）。
            SeaWatch.Arm("Disaster+ concentric tsunami from the epicentre", frame);

            Log.Info("tsunami rising at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): a water source of radius "
                     + TsunamiRingShape.RadiusMetresForRate(_rate).ToString("F0")
                     + " m sits on the epicentre and its target sea level is driven with "
                     + "the DLC's own waveform (retreat, crest, retreat) for "
                     + DurationSteps + " water steps = "
                     + (DurationSteps * FramesPerWaterStep / 3600f).ToString("F1")
                     + " real minutes. The raw amplitude for intensity " + intensity
                     + " would be " + (_deltaUnits / 64f).ToString("F0")
                     + " m but it is held to " + (_riseCapUnits / 64f).ToString("F0")
                     + " m up and " + (_drawCapUnits / 64f).ToString("F0")
                     + " m down (the water is " + depth.ToString("F1")
                     + " m deep here). **A taller source does not travel further - what "
                     + "travels is volume - and unlike the old impact wave this MAKES "
                     + "water instead of borrowing it from the hole it digs.**");

            return true;
        }

        /// <summary>**sim スレッド。** 毎 tick 呼んでよい（自分で間引く）。</summary>
        public static void Tick(uint frame)
        {
            if (!_running) return;
            if (frame - _lastFrame < FramesPerWaterStep) return;
            _lastFrame = frame;

            _ticks += TsunamiRingShape.TicksPerWaterStep;

            if (_ticks >= TsunamiRingShape.DurationTicks)
            {
                Release();
                _running = false;
                Log.Info("tsunami source finished after " + DurationSteps
                         + " water steps and is released. The highest the target got was "
                         + PeakRiseMetres.ToString("F1")
                         + " m above the normal sea. From here the wave is carried by the "
                         + "solver alone.");
                return;
            }

            int offset = TsunamiRingShape.LevelOffsetUnits(_ticks, _deltaUnits);

            // ★ 押しは絶対値で、引きは水深で縛る（それぞれの定数の doc）。
            if (offset > _riseCapUnits) offset = _riseCapUnits;
            if (offset < -_drawCapUnits) offset = -_drawCapUnits;

            OffsetMetres = offset / 64f;
            if (OffsetMetres > PeakRiseMetres) PeakRiseMetres = OffsetMetres;

            int target = Clamp(_seaUnits + offset, 0, TsunamiRingShape.MaxLevelUnits);
            Drive(target);
        }

        /// <summary>
        /// 円をいまの目標水位へ押し引きする。**ロックを取るのはここだけ。**
        ///
        /// ★★ 押しと引きを<b>両方いつも入れる</b>。片方ずつにすると、寄せ集まった水を
        ///   抜く力が無く、震源が目標の 2〜3 倍に盛り上がる（オフライン実測 2026-08-31:
        ///   目標 +102 m に対して実測 +330 m）。両方入れると円は目標水位に張り付き、
        ///   DLC が外周セルにやっている Dirichlet 境界とまったく同じ振る舞いになる。
        /// </summary>
        private static void Drive(int target)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null) return;
            if (!OwnsSource(terrain.WaterSimulation)) { _running = false; _source = 0; return; }

            // ★ LockWaterSource は Monitor を取ったまま返る。
            //   UnlockWaterSource が唯一の解放経路なので必ず finally に置く。
            WaterSource src = terrain.WaterSimulation.LockWaterSource(_source);

            try
            {
                src.m_target = (ushort)target;
                src.m_inputRate = (uint)_rate;
                src.m_outputRate = (uint)_rate;
            }
            finally
            {
                terrain.WaterSimulation.UnlockWaterSource(_source, src);
            }
        }

        /// <summary>
        /// その枠が<b>いまも自分のもの</b>か。<c>CreateWaterSource</c> は
        /// <c>m_type == 0</c> の枠を使い回すので、番号だけでは足りない（クラス doc §4）。
        /// </summary>
        private static bool OwnsSource(WaterSimulation sim)
        {
            if (_source == 0) return false;

            FastList<WaterSource> list = sim.m_waterSources;
            if (list == null || list.m_buffer == null) return false;

            int at = _source - 1;
            if (at < 0 || at >= list.m_size) return false;

            WaterSource s = list.m_buffer[at];
            if (s.m_type != TypeNatural) return false;

            // 位置は自分で書いた値そのものなので、ビット一致で照合できる。
            return s.m_outputPosition == _centre && s.m_inputPosition == _centre;
        }

        /// <summary>
        /// 置いた水源を解放する。**冪等。例外を投げない。**
        /// ここが最後の砦である —— 通らないとセーブに水源が残る。
        /// </summary>
        private static void Release()
        {
            if (_source == 0) return;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;

                if (terrain != null && terrain.WaterSimulation != null
                    && OwnsSource(terrain.WaterSimulation))
                {
                    // ★ 解放の前に流量を 0 にする。ReleaseWaterSource は m_type を
                    //   0 にするだけなので、枠を拾い直した誰かが古い流量を見る余地を消す。
                    WaterSource src = terrain.WaterSimulation.LockWaterSource(_source);
                    try
                    {
                        src.m_inputRate = 0u;
                        src.m_outputRate = 0u;
                    }
                    finally
                    {
                        terrain.WaterSimulation.UnlockWaterSource(_source, src);
                    }

                    terrain.WaterSimulation.ReleaseWaterSource(_source);
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water source failed ("
                         + e.GetType().Name + "); it may persist in this save");
            }

            _source = 0;
        }

        /// <summary>
        /// **保存の直前に呼ぶ。** 水源を外して、戻すべきかどうかを返す。
        ///
        /// ★★ <c>WaterSource</c> はセーブに焼き付く。MOD の <c>OnSaveData</c> は
        ///   バニラの配列書き込みより先に走るので、ここで外せば書かれない。
        ///   <b>すぐ戻し直してはいけない</b> —— <c>AddAction</c> で遅らせること
        ///   （<c>DisasterPlusSerialization.RestoreFloodedRiversForSave</c> と同じ理由）。
        /// </summary>
        public static bool SuspendForSave()
        {
            if (!_running || _source == 0) return false;
            Release();
            return true;
        }

        /// <summary>保存が終わってから <c>AddAction</c> 越しに呼ぶ。**sim スレッド。**</summary>
        public static void ReapplyAfterSave()
        {
            if (!_running || _source != 0) return;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain == null || terrain.WaterSimulation == null) { _running = false; return; }

                WaterSource src = new WaterSource();
                src.m_type = TypeNatural;
                src.m_inputPosition = _centre;
                src.m_outputPosition = _centre;
                src.m_target = (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);

                ushort handle;
                if (terrain.WaterSimulation.CreateWaterSource(out handle, src) && handle != 0)
                {
                    _source = handle;
                }
                else
                {
                    _running = false;
                    Log.Warn("tsunami: could not put the water source back after saving; "
                             + "the wave stops here");
                }
            }
            catch (System.Exception e)
            {
                _running = false;
                Log.Error("tsunami: putting the water source back after saving failed", e);
            }
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
