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
    /// 噴煙  Factory Smoke              複製して 灰黒 / 寿命 7-16s / 可視 10 km
    /// 炎    Fire Particles             **複製しない**（建物火災と同じ見た目が欲しい）
    /// 噴石  Medium Explosion Particles 複製して 重力を下向き / 粒径 6 に
    /// </code>
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
        /// ── ★ 高さの基準は「噴出口 ＝ 火口の底」である（2026-08-22、指摘②）────────
        ///
        /// 所有者の指摘は「噴火口の炎が浮いて見えるので、この窪みと一致させてください」。
        /// 3 つとも <c>snapshot.VentWorld</c>（火口の底 ＋ 少しの浮き）に乗せる。
        /// 山頂（火口の縁）に乗せると、**窪みの深さのぶんだけ丸ごと宙に浮く**。
        /// 底は山と一緒に上がるので、sim 側が毎 tick 引き直したものをそのまま使う
        /// （<c>VolcanoEruption.SampleVent</c>）。
        ///
        /// <summary>噴煙を噴出口からどれだけ上げるか（m）。</summary>
        private const float PlumeLiftMetres = 8f;

        /// <summary>炎を噴出口からどれだけ上げるか（m）。</summary>
        private const float FlameLiftMetres = 3f;

        /// <summary>噴石を噴出口からどれだけ上げるか（m）。</summary>
        private const float EjectaLiftMetres = 4f;

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

        /// <summary>
        /// 今フレーム、火口に何か 1 つでも出したか。
        /// **診断（sim スレッド）から読まれるので <c>bool</c> のまま持つ** ——
        /// ここで Unity の参照を <c>== null</c> と比べてはいけない。
        /// </summary>
        public static bool Drawing { get { return _plumeDrawn || _flameDrawn || _ejectaDrawn; } }

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
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            _plumeDrawn = false;
            _flameDrawn = false;
            _ejectaDrawn = false;

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
            ParticleEffect flames = VolcanoVanillaFx.Flames();
            ParticleEffect ejecta = VolcanoVanillaFx.Ejecta();

            // ★ 第 4 引数（土煙）はここでは埋めない。あれは VolcanoPyroclasticFx の
            //   持ち物で、こちらの門には 1 度も入らない（Facts の doc）。
            _facts = new VolcanoVanillaFacts(ash != null, flames != null, ejecta != null,
                                             false, true);

            // ★ dt == 0（一時停止・読めない）のフレームは 1 粒も出さないのが正しい。
            //   継続モードの粒子数は timeDelta に比例するので、渡しても 0 になる。
            if (dt <= 0f) return;

            _plumeDrawn = RenderAsh(ash, camera, vent, craterRadius, unit, dt);
            _flameDrawn = RenderFlames(flames, camera, vent, craterRadius, unit, dt);
            _ejectaDrawn = RenderEjecta(ejecta, camera, vent, craterRadius, unit, dt);
        }

        /// <summary>灰の柱。**継続モード**（<c>timeOffset &lt; 0</c>）で毎フレーム押し出す。</summary>
        private static bool RenderAsh(ParticleEffect effect, RenderManager.CameraInfo camera,
                                      Vec3 vent, float craterRadius, float unit, float dt)
        {
            if (effect == null) return false;

            var area = new EffectInfo.SpawnArea(
                new Vector3(vent.X, vent.Y + PlumeLiftMetres, vent.Z),
                Vector3.up,
                EruptionEffectPlan.PlumeRadiusMetres(craterRadius, unit));

            // InstanceID は空でよい。ParticleEffect は probability に定数 100 を渡す
            // （＝必ず出す）ので、Randomizer の種が 0 に固定されても影響が無い。
            // 建物の旗の検査も「建物 0 なら飛ばす」形になっている（IL 実測）。
            effect.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                EruptionEffectPlan.PlumeMagnitude(unit),
                                -1f,   // ★ 継続モード。バニラの陥没穴と同じ形
                                dt, camera);
            return true;
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
