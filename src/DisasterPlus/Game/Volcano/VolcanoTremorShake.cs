using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **火山性地震の揺れ。** <c>Core/Volcano/VolcanicTremor</c> が出した地動を
    /// カメラへ足す。**main スレッド専用、毎フレーム。**
    ///
    /// ── ②とどう分けたか（2026-08-22、所有者の依頼「火山性の地震の発生」）──────
    ///
    /// | 何を借りたか | どこから |
    /// |---|---|
    /// | <c>CameraController.m_cameraShake</c> へ**足す**という手口 | ②の <c>CameraShakeBooster</c> |
    /// | 揺れの中身（群発 ＋ 微動） | **⑤自身**（<c>VolcanoRelief</c> と同じで Core にある） |
    ///
    /// **②のコードは 1 行も呼ばないし、②の設定も 1 つも見ない。**
    ///
    ///   - <c>eqShakeBoost</c> / <c>eqSeismogram</c> が**両方 OFF でも火山は揺れる。**
    ///     あちらが既定 OFF なのは<b>バニラの地震のカメラ揺れを差し替える</b>からで、
    ///     ⑤の火山はプレイヤーが自分で起こした⑤自身の現象である
    ///   - 逆に②が ON でも、⑤は②の合成記象を 1 度も評価しない。両方同時に起きたら
    ///     **加算される**（<c>m_cameraShake</c> は加算で消費されるフィールドである。
    ///     ②のクラス doc の IL 実測）。それが正しい —— 地震の最中に噴火したら両方揺れる
    ///
    /// ★ <b>バニラの <c>EarthquakeAI</c> は 1 つも起こさない。</b> あれは地図に断層の
    ///   亀裂を刻むので、⑤が同じ地形セルへ山を書いているところへ割り込むことになる。
    ///
    /// ★ <b>建物は 1 棟も壊さない。</b> ⑤は既に<b>火山の足元をまるごと更地にしている</b>
    ///   （準備＝ <c>VolcanoClearing</c>）ので、「小さく、山の近くだけ」の被害を足すと
    ///   **もう何も建っていない場所を壊すことになる**。山の外まで壊すのは
    ///   「山の近くだけ」ではない。したがってここは<b>揺れだけ</b>で、
    ///   <c>BuildingAI.CollapseBuilding</c> も <c>DisasterHelpers</c> も呼ばない
    ///   （<c>Building.m_fireIntensity</c> は当然 1 バイトも書かない）。
    ///
    /// ── 残留しないことの根拠（②が IL で確定させたもの）─────────────────
    ///
    /// <c>CameraController.LateUpdate</c> の**最後の 1 行**が
    /// <c>m_cameraShake = Vector3.zero</c> である。消費とリセットが毎フレーム走るので、
    /// 足すのをやめれば次のフレームで必ず消える。だからこそ**毎フレーム足し続ける**。
    ///
    /// ── 毎フレームの費用 ─────────────────────────────────
    ///
    /// <c>Sin</c> / <c>Exp</c> が十数回と <c>Vector3</c> 1 個。**ヒープ確保は 0 バイト、
    /// ログは 0 行。** 噴火していないフレームは最初の 3 行で抜ける。
    /// </summary>
    public static class VolcanoTremorShake
    {
        /// <summary>
        /// 揺れの最大変位。②のバニラの理論最大（<c>ShakeWaveform.MaxDisplacement</c>
        /// ＝ 0.6）の <b>0.7 倍</b>。火山性地震は近くでは強く感じるが、
        /// <b>本震級の断層地震ではない</b>。
        /// オフラインの実測（<c>docs/images/volcano/tremor-waveform.png</c>）で
        /// 地動の山は活動度 1 で 0.55 なので、火口の真下の実際の最大は
        /// <c>0.55 × 0.42 ≒ 0.23</c> ——**バニラの本震の 4 割弱**である。
        /// </summary>
        private const float MaxDisplacement = 0.7f * ShakeWaveform.MaxDisplacement;

        /// <summary>
        /// 揺れが届く距離（山の半径の何倍か）。**外はきっかり 0** ——
        /// 街の反対側まで揺らすと「地震が起きている」ではなく「画面が壊れている」に見える。
        /// </summary>
        private const float ReachRadiusFactor = 4.5f;

        /// <summary>反対向きの成分に使う位相差（ラジアン）。水平 2 軸を独立に見せる。</summary>
        private const float CrossPhaseSeconds = 0.37f;

        /// <summary>上下動の比。実際の地震と同じく水平より小さい。</summary>
        private const float VerticalRatio = 0.45f;

        /// <summary>時計（秒）。**バニラの効果時計と同じ足し方**（一時停止で止まる）。</summary>
        private static float _clockSeconds;

        private static float _lastActivity;
        private static float _lastAdded;
        private static bool _errorLogged;

        /// <summary>
        /// <c>CameraController</c> の参照。②と同じ規律で、**配列にせず参照 1 個**で持ち、
        /// 毎回 Unity の <c>== null</c>（fake-null も拾う）で確認して駄目なら引き直す。
        /// </summary>
        private static CameraController _controller;

        private static Camera _mainCamera;

        /// <summary>直近の活動度（診断用）。0 は「揺れていない」。</summary>
        public static float ActivityUnit { get { return _lastActivity; } }

        /// <summary>直近のフレームで実際に足した変位の大きさ（診断用）。</summary>
        public static float LastAdded { get { return _lastAdded; } }

        /// <summary>
        /// **main スレッド。** レベルアンロードと、設定で切ったときに呼ぶ。
        /// Unity のオブジェクトには触らず、参照を手放すだけ。冪等。
        /// </summary>
        public static void Reset()
        {
            _clockSeconds = 0f;
            _lastActivity = 0f;
            _lastAdded = 0f;
            _controller = null;
            _mainCamera = null;
            // _errorLogged は戻さない（ゲームのビルドに対する事実である）。
        }

        /// <summary>**main スレッド、毎フレーム。**</summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            _lastAdded = 0f;

            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                // 毎フレームの経路。1 回だけ大きく鳴らし、以後はキー単位スロットルへ。
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano tremor shake failed", e);
                }
                else
                {
                    Log.Diag("volcanoTremor", "tremor shake failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            float activity = ActivityFor(snapshot);
            _lastActivity = activity;

            if (!(activity > 0f))
            {
                _clockSeconds = 0f;
                return;
            }

            // ★ プレイヤーが揺れを切っているなら、この機能は存在しない
            //   （バニラの災害の揺れと同じ扱い。②が同じ条件で止まる）。
            if (!Singleton<DisasterManager>.exists) return;
            if (Singleton<DisasterManager>.instance.m_disableCameraShake) return;

            // ★ Time.deltaTime ではない。一時停止で止まり、ゲーム速度に追随する。
            float dt = VolcanoVanillaFx.EffectTimeDelta();
            if (dt > 0f) _clockSeconds += dt;
            if (dt <= 0f) return;

            Camera cam = ResolveCamera();
            if (cam == null) return;

            CameraController controller = ResolveController();
            if (controller == null) return;

            // ★★ **火口ではなく影響範囲の中心から測る。** <c>VolcanoSnapshot.VentWorld</c> は
            //    噴火（正確には隆起）が始まるまで <c>(0,0,0)</c> のままである
            //    （<c>VolcanoEruption.Tick</c> が呼ばれて初めて埋まる）。
            //    そちらで測ると**準備の段の揺れが原点からの距離で減衰して消える** ——
            //    「噴火の前から揺れている」がまるごと出なくなる。
            Vec3 centre = snapshot.Footprint.Centre;
            Vector3 eye = cam.transform.position;
            float dx = eye.x - centre.X;
            float dz = eye.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            float reach = snapshot.Footprint.RadiusMetres * ReachRadiusFactor;
            float attenuation = VolcanicTremor.AttenuationAt(distance, reach);
            if (!(attenuation > 0f)) return;

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(centre.X)),
                unchecked((uint)Mathf.RoundToInt(centre.Z)));

            float a = VolcanicTremor.DisplacementAt(seed, _clockSeconds, activity);
            float b = VolcanicTremor.DisplacementAt(seed, _clockSeconds + CrossPhaseSeconds,
                                                    activity);

            float gain = MaxDisplacement * attenuation;
            var added = new Vector3(a * gain, b * gain * VerticalRatio, b * gain);

            if (added.x == 0f && added.y == 0f && added.z == 0f) return;

            controller.m_cameraShake += added;
            _lastAdded = added.magnitude;
        }

        /// <summary>
        /// 今の活動度。**位相から決まる**（スナップショットは sim が書いたもので、
        /// ここでは 1 バイトも書き換えない）。
        /// </summary>
        private static float ActivityFor(VolcanoSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return 0f;
            if (!snapshot.Footprint.Valid) return 0f;
            if (!ModSettings.VolcanoEnabled.value) return 0f;
            if (!ModSettings.VolcanoQuake.value) return 0f;

            switch (snapshot.Phase)
            {
                // 準備（破壊）と隆起 —— マグマが上がってきている段。**噴火の前から揺れる。**
                case VolcanoPhase.Clearing:
                case VolcanoPhase.Uplifting:
                    return VolcanicTremor.ActivityUnit(snapshot.ProgressUnit, false, 0f,
                                                       false, 0f);

                case VolcanoPhase.Erupting:
                    return VolcanicTremor.ActivityUnit(1f, true,
                                                       snapshot.EruptionIntensityUnit, false, 0f);

                // 噴火が終わってから溶岩が冷えきるまで、余韻が引いていく。
                case VolcanoPhase.Flowing:
                case VolcanoPhase.Cooling:
                    return VolcanicTremor.ActivityUnit(1f, false, 0f, true,
                                                       snapshot.LavaCoolUnit);

                default:
                    return 0f;
            }
        }

        private static Camera ResolveCamera()
        {
            // Unity 5.6 の Camera.main はタグ検索なので毎フレーム呼ばない
            // （②の CameraShakeBooster と同じ形）。
            if (_mainCamera != null) return _mainCamera;
            _mainCamera = Camera.main;
            return _mainCamera;
        }

        private static CameraController ResolveController()
        {
            if (_controller != null) return _controller;
            _controller = SceneObjects.FindInScene<CameraController>();
            return _controller;
        }
    }
}
