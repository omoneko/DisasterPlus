using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 火口の見た目 —— <b>噴煙・炎・噴石</b>。**main スレッド専用、毎フレーム。**
    ///
    /// ── ★★ 自前のポリゴンはもう 1 枚も出さない ────────────────────────
    ///
    /// 以前ここは自前の <c>ParticleSystem</c> ＋ 自前 <c>Material</c> で噴煙を出していた。
    /// **実機では 1 粒も描かれていなかった** —— <c>Shader.Find</c> が組み込みの
    /// <c>"Standard"</c> を含めて全ての名前に null を返す環境だったためである。
    /// いま出しているのは**ゲーム自身の粒子エフェクト 3 つ**で、
    /// どれも <b>DLC 不要</b>（Natural Disasters の爆発・隕石は DLC 所持者にしか
    /// 存在しないので、既定経路には決してしない）。
    ///
    /// <code>
    /// 噴煙柱 Factory Smoke              複製して 暗い灰褐色 / 粒 30 / 寿命 4-9s
    /// 傘     Factory Smoke              **もう 1 個**複製して 淡い灰 / 粒 95 / 寿命 18-34s
    /// 炎     Fire Particles             **複製しない**（建物火災と同じ見た目が欲しい）
    /// 噴石   Medium Explosion Particles 複製して 重力を下向き / 粒径 6 に
    /// </code>
    ///
    /// ── ★★ 噴煙は「柱」である（2026-08-22、実機の指摘③）──────────────────
    ///
    /// > 噴煙がただの煙だまりになってしまっています。これは MissileMOD のキノコ雲の
    /// > Method を参考にリアルな噴煙（キノコ雲ではない …）を作ってほしいです。
    ///
    /// 以前は <c>RenderEffect</c> **1 回**で、噴出口の真上の円盤（半径 40〜80 m）から
    /// 煙を湧かせていただけだった。粒子は自分の初速で 7〜16 秒上がって消えるので、
    /// 出来上がるのは<b>火口の上に浮いた煙の塊</b>である。柱にも傘にもならない。
    ///
    /// いまは形を <c>Core/Volcano/EruptionColumn</c>（純粋・テスト付き）が決め、ここは
    /// **その 9 段を <c>SpawnArea(位置, 上, 半径, 高さ)</c> の円柱として湧かすだけ**である
    /// （ミサイル MOD の <c>CloudPuffs</c> と同じ分業。形は借りない ——
    /// あちらは単発の泡、こちらは火口から供給され続ける柱で、物理が違う）。
    /// ガス推力域 → 対流域 → 傘の 3 区間と風下への傾きはあちらのクラス doc にある。
    ///
    /// **粒子の総量は今までと同じ**である。段ごとの密度は面積で正規化してあり
    /// （<c>EruptionColumn.MagnitudeFor</c>）、重みの和が 1 なので、
    /// 柱ぜんぶで従来の 1 回ぶんに等しい。増えるのは <c>RenderEffect</c> の
    /// 回数（1 → 最大 9）だけで、1 回あたりの粒子数はむしろ減る。
    ///
    /// ── ★ 高さの基準は「噴出口 ＝ 火口の底」である（指摘②）───────────────
    ///
    /// 4 つとも <c>snapshot.VentWorld</c>（火口の底 ＋ 少しの浮き）に乗せる。
    /// 山頂（火口の縁）に乗せると**窪みの深さのぶんだけ丸ごと宙に浮く**。
    /// 底は山と一緒に上がるので、sim 側が毎 tick 引き直したものをそのまま使う
    /// （<c>VolcanoEruption.SampleVent</c>）。
    ///
    /// ── 強弱の付け方（magnitude は密度であってサイズではない）───────────────
    ///
    /// <c>ParticleEffect.RenderEffect</c> の <c>magnitude</c> は**粒子の密度**で、
    /// 見た目の大きさを決めるのは <c>SpawnArea</c> の半径である。
    /// どちらを噴火の強さ <c>[0,1]</c> からどう作るかは
    /// <see cref="EruptionEffectPlan"/>（Core、テスト付き）にある。
    ///
    /// ── ★ 直っている不具合 1: magnitude が確率に潰れていた ───────────────
    ///
    /// 以前は <c>BuildingProperties.m_fireEffect</c>（<c>FireEffect</c> 合成型）を
    /// 通していた。<c>FireEffect.RenderEffect</c> は IL で
    ///
    /// <code>
    /// probability = RoundToInt(magnitude * 100)   ← magnitude は「確率」になる
    /// particlesPerSquare = timeDelta * 0.01f      ← magnitude が入っていない
    /// </code>
    ///
    /// であり、しかも <c>EmitParticles</c> は <c>new Randomizer(id.RawData)</c> の
    /// **1 発目**しか使わない。<c>default(InstanceID)</c> は <c>RawData == 0</c> で、
    /// <c>new Randomizer(0).Int32(100)</c> の 1 発目は**必ず 7** である。
    /// つまり ⑤ が渡していた <c>0.25 + 0.75 * unit</c> は
    /// <c>7 &gt; 25…100</c> が常に偽 → **常に「出る」に張り付き、濃さは一定**だった。
    /// 噴火の強弱は 1 度も画面に出ていなかったことになる。
    /// **いまは <c>ParticleEffect</c> を直に呼ぶ**ので <c>magnitude</c> は素直に密度である
    /// （<c>ParticleEffect</c> は <c>probability</c> に定数 100 を渡す＝必ず出す）。
    ///
    /// ── ★ 直っている不具合 2: 時間差が <c>Time.deltaTime</c> だった ────────────
    ///
    /// バニラの <c>EffectManager.EndRenderingImpl</c> は
    /// <c>SimulationManager.m_simulationTimeDelta</c> を渡している（IL 実測）。
    /// <c>Time.deltaTime</c> だと**一時停止しても噴き続け、速度を上げても濃さが変わらない**。
    /// いまは <see cref="VolcanoVanillaFx.EffectTimeDelta"/> を通す。
    ///
    /// ── ★ 引けなかったときに何が起きるか ────────────────────────────
    ///
    /// **その 1 つを出さないだけ。** 例外は出さず、ログは 1 行、噴火は続く。
    /// カメラ情報が取れないフレームは 3 つとも飛ばす
    /// （<c>ParticleEffect.RenderEffect</c> は先頭で <c>cameraInfo</c> を参照するので
    /// null を渡すと NRE になる）。
    ///
    /// ── 毎フレームの費用 ─────────────────────────────────
    ///
    /// <c>RenderEffect</c> を最大 3 回。<c>SpawnArea</c> / <c>InstanceID</c> /
    /// <c>Vector3</c> はすべて struct で、<c>EmitParticles</c> の経路にヒープ確保は無い
    /// （IL 実測）。**ヒープ確保は 0 バイト。**
    /// 粒子数は <c>maxParticles</c> に対する自動絞り込みで頭打ちになる。
    ///
    /// ── この型は sim スレッドから 1 度も呼ばれない ────────────────────
    ///
    /// 読むのは <see cref="VolcanoHub"/> の不変スナップショットだけで、
    /// <c>VolcanoEruption</c> の内部状態には触らない。
    /// </summary>
    public static class VolcanoEruptionFx
    {
        /// <summary>噴煙柱の足元を噴出口からどれだけ上げるか（m）。</summary>
        private const float PlumeLiftMetres = 8f;

        /// <summary>炎を噴出口からどれだけ上げるか（m）。</summary>
        private const float FlameLiftMetres = 3f;

        /// <summary>噴石を噴出口からどれだけ上げるか（m）。</summary>
        private const float EjectaLiftMetres = 4f;

        /// <summary>風向きを引く塩（<see cref="DeterministicRandom"/>）。**地点だけから決める。**</summary>
        private const uint WindDirectionSalt = 0x57494E44u;

        /// <summary>風速を引く塩。</summary>
        private const uint WindSpeedSalt = 0x57535044u;

        /// <summary>噴煙を倒す風速の下限（m/秒）。**⑤が決めた演出値**（気象の実測ではない）。</summary>
        private const float WindSpeedMinMetresPerSecond = 6f;

        /// <summary>同上の上限。</summary>
        private const float WindSpeedMaxMetresPerSecond = 16f;

        /// <summary>
        /// 噴石の窓を刻む時計（秒）。**バニラの効果時計**である ——
        /// <c>EffectManager</c> 自身が描画 1 フレームごとに
        /// <c>m_simulationTimeDelta</c> を足しているので、⑤も同じ足し方をする。
        /// 一時停止で止まり、ゲーム速度に追随する。
        /// </summary>
        private static float _clockSeconds;

        private static VolcanoVanillaFacts _facts;
        private static bool _renderErrorLogged;
        private static bool _cameraWarned;

        private static bool _plumeDrawn;
        private static bool _flameDrawn;
        private static bool _ejectaDrawn;

        /// <summary>今フレームに湧かせた噴煙柱の段数（診断用。0 なら柱は 1 段も出ていない）。</summary>
        private static int _plumeSegments;

        /// <summary>直近に組んだ柱の高さ（m。診断用）。</summary>
        private static float _plumeHeightMetres;

        /// <summary>
        /// 今フレーム、火口に何か 1 つでも出したか。
        /// **診断（sim スレッド）から読まれるので <c>bool</c> のまま持つ** ——
        /// ここで Unity の参照を <c>== null</c> と比べてはいけない。
        /// </summary>
        public static bool Drawing { get { return _plumeDrawn || _flameDrawn || _ejectaDrawn; } }

        /// <summary>今フレームに湧かせた噴煙柱の段数（診断用）。</summary>
        public static int PlumeSegments { get { return _plumeSegments; } }

        /// <summary>直近に組んだ噴煙柱の高さ（m。噴出口からの相対。診断用）。</summary>
        public static float PlumeHeightMetres { get { return _plumeHeightMetres; } }

        /// <summary>
        /// 直近のフレームで実際に門にした借用の可否（診断とパネルの断りに出す）。
        ///
        /// ★ <c>DustResolved</c> は<b>ここでは埋まらない</b>（常に false）。
        ///   火砕流もどきは <see cref="VolcanoPyroclasticFx"/> の持ち物で、
        ///   成否も別に決まる。読むなら <c>VolcanoPyroclasticFx.DustResolved</c> のほう。
        /// </summary>
        public static VolcanoVanillaFacts Facts { get { return _facts; } }

        /// <summary>診断に出す 1 行（**英語**）。</summary>
        public static string Detail { get { return VolcanoVanillaFx.Detail; } }

        /// <summary>**main スレッド、毎フレーム。**</summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (Exception e)
            {
                _plumeDrawn = false;
                _flameDrawn = false;
                _ejectaDrawn = false;

                _plumeSegments = 0;

                if (!_renderErrorLogged)
                {
                    _renderErrorLogged = true;
                    Log.Error("volcano eruption effects failed", e);
                }
            }
        }

        /// <summary>
        /// **main スレッド。** 設定で切ったときとレベルアンロードで呼ぶ。
        /// 借りているエフェクトそのものは <see cref="VolcanoVanillaFx"/> が持っているので、
        /// ここで畳むのは⑤自身の時計だけである。冪等。
        /// </summary>
        public static void Destroy()
        {
            _clockSeconds = 0f;
            _plumeDrawn = false;
            _flameDrawn = false;
            _ejectaDrawn = false;
            _plumeSegments = 0;
            _plumeHeightMetres = 0f;
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            _plumeDrawn = false;
            _flameDrawn = false;
            _ejectaDrawn = false;
            _plumeSegments = 0;

            if (snapshot == null || !snapshot.Valid || !snapshot.EruptionActive)
            {
                // 噴火が終わったフレームで**自分で**時計を戻す。sim 側からは呼ばれない。
                _clockSeconds = 0f;
                return;
            }

            // ★ 在庫は実機で 1 度だけ数える（事実文書の在庫が PARTIAL のままなので）。
            VolcanoVanillaFx.LogInventoryOnce();

            RenderManager.CameraInfo camera = VolcanoVanillaFx.CameraInfo();
            _facts = new VolcanoVanillaFacts(false, false, false, false, camera != null);

            if (camera == null)
            {
                if (!_cameraWarned)
                {
                    _cameraWarned = true;
                    // ★ 毎フレームの経路なので 1 度だけ。Warn は使わない。
                    Log.Info("volcano eruption effects: the game is not reporting a camera "
                             + "this frame, so nothing is drawn at the crater; the eruption "
                             + "itself is unaffected");
                }
                return;
            }

            float dt = VolcanoVanillaFx.EffectTimeDelta();
            if (dt > 0f) _clockSeconds += dt;

            float unit = Clamp01(snapshot.EruptionIntensityUnit);
            Vec3 vent = snapshot.VentWorld;

            float craterRadius = VolcanoShape.CraterRadiusOf(snapshot.Footprint.RadiusMetres);

            ParticleEffect ash = VolcanoVanillaFx.AshPlume();
            ParticleEffect umbrella = VolcanoVanillaFx.AshUmbrella();
            ParticleEffect flames = VolcanoVanillaFx.Flames();
            ParticleEffect ejecta = VolcanoVanillaFx.Ejecta();

            // ★ 第 4 引数（土煙）はここでは埋めない。あれは VolcanoPyroclasticFx の
            //   持ち物で、こちらの門には 1 度も入らない（Facts の doc）。
            _facts = new VolcanoVanillaFacts(ash != null, flames != null, ejecta != null,
                                             false, true);

            // ★ dt == 0（一時停止・読めない）のフレームは 1 粒も出さないのが正しい。
            //   継続モードの粒子数は timeDelta に比例するので、渡しても 0 になる。
            if (dt <= 0f) return;

            _plumeDrawn = RenderColumn(ash, umbrella, camera, vent,
                                       snapshot.Footprint.Centre, craterRadius, unit, dt);
            _flameDrawn = RenderFlames(flames, camera, vent, craterRadius, unit, dt);
            _ejectaDrawn = RenderEjecta(ejecta, camera, vent, craterRadius, unit, dt);
        }

        /// <summary>
        /// <b>噴火柱</b>。<c>Core/Volcano/EruptionColumn</c> が決めた 9 段を、段ごとに
        /// <c>SpawnArea(位置, 上, 半径, 高さ)</c> の**円柱**として湧かす。
        /// **継続モード**（<c>timeOffset &lt; 0</c>）で毎フレーム押し出す。
        ///
        /// ★ 傘の段は別の複製（<c>AshUmbrella</c>）で描く —— 淡くて粒が大きく、寿命が長い。
        ///   引けなければ**柱の複製で代用する**（傘が濃くなるだけで、消えはしない）。
        ///
        /// ★ 風は火山の地点から決まる（<see cref="DeterministicRandom"/>）ので、
        ///   **同じ山なら毎回同じ向きに倒れる**。<c>SwayAt</c> の 1 本の正弦だけが
        ///   ゆっくり左右へ振る（37 秒周期）。フレーム番号は 1 度も混ぜない。
        /// </summary>
        private static bool RenderColumn(ParticleEffect column, ParticleEffect umbrella,
                                         RenderManager.CameraInfo camera, Vec3 vent,
                                         Vec3 centre, float craterRadius, float unit, float dt)
        {
            if (column == null && umbrella == null) return false;

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(centre.X)),
                unchecked((uint)Mathf.RoundToInt(centre.Z)));

            float bearing = 2f * Mathf.PI * DeterministicRandom.Unit(seed, WindDirectionSalt)
                            + EruptionColumn.SwayAt(_clockSeconds);
            float windSpeed = WindSpeedMinMetresPerSecond
                              + (WindSpeedMaxMetresPerSecond - WindSpeedMinMetresPerSecond)
                                * DeterministicRandom.Unit(seed, WindSpeedSalt);

            var plume = new EruptionColumn(craterRadius, unit,
                                           Mathf.Cos(bearing), Mathf.Sin(bearing), windSpeed);
            _plumeHeightMetres = plume.HeightMetres;

            float baseX = vent.X;
            float baseY = vent.Y + PlumeLiftMetres;
            float baseZ = vent.Z;

            int drawn = 0;
            for (int i = 0; i < plume.SegmentCount; i++)
            {
                EruptionColumnSegment segment = plume.SegmentAt(i);
                if (segment.Magnitude <= 0f) continue;

                ParticleEffect effect = segment.Umbrella
                    ? (umbrella != null ? umbrella : column)
                    : column;
                if (effect == null) continue;

                var area = new EffectInfo.SpawnArea(
                    new Vector3(baseX + segment.OffsetX,
                                baseY + segment.OffsetY,
                                baseZ + segment.OffsetZ),
                    Vector3.up,
                    segment.RadiusMetres,
                    segment.HalfHeightMetres);

                // InstanceID は空でよい。ParticleEffect は probability に定数 100 を渡す
                // （＝必ず出す）ので、Randomizer の種が 0 に固定されても影響が無い。
                // 建物の旗の検査も「建物 0 なら飛ばす」形になっている（IL 実測）。
                effect.RenderEffect(default(InstanceID), area,
                                    new Vector3(segment.DriftX, segment.DriftY, segment.DriftZ),
                                    0f, segment.Magnitude,
                                    -1f,   // ★ 継続モード。バニラの陥没穴と同じ形
                                    dt, camera);
                drawn++;
            }

            _plumeSegments = drawn;
            return drawn > 0;
        }

        /// <summary>火口の炎。<b>ゲーム自身の建物火災の炎そのもの。</b></summary>
        private static bool RenderFlames(ParticleEffect effect, RenderManager.CameraInfo camera,
                                         Vec3 vent, float craterRadius, float unit, float dt)
        {
            if (effect == null) return false;

            var area = new EffectInfo.SpawnArea(
                new Vector3(vent.X, vent.Y + FlameLiftMetres, vent.Z),
                Vector3.up,
                EruptionEffectPlan.FlameRadiusMetres(craterRadius, unit));

            effect.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                EruptionEffectPlan.FlameMagnitude(unit), -1f, dt, camera);
            return true;
        }

        /// <summary>
        /// 噴石。**噴いていない間は 1 度も呼ばない**（<c>magnitude</c> が 0 を返す）。
        ///
        /// <c>DispatchEffect</c> を使わないのは、あれが積んだ予定を
        /// <c>EffectManager</c> が**あとで**実行するためである。⑤の複製を
        /// レベルアンロードで破棄したあとに実行されると、
        /// バニラの中で破棄済みオブジェクトを触ることになる。
        /// 継続モードで自分で窓を作れば、生存期間は完全にこちらの手の内にある。
        /// </summary>
        private static bool RenderEjecta(ParticleEffect effect, RenderManager.CameraInfo camera,
                                         Vec3 vent, float craterRadius, float unit, float dt)
        {
            if (effect == null) return false;

            float period = EruptionEffectPlan.EjectaPeriodSeconds(unit);
            float phase = EruptionEffectPlan.BurstPhaseSeconds(_clockSeconds, period);
            float magnitude = EruptionEffectPlan.EjectaMagnitude(unit, phase);
            if (magnitude <= 0f) return false;

            var area = new EffectInfo.SpawnArea(
                new Vector3(vent.X, vent.Y + EjectaLiftMetres, vent.Z),
                Vector3.up,
                EruptionEffectPlan.EjectaRadiusMetres(craterRadius));

            effect.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                magnitude, -1f, dt, camera);
            return true;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
