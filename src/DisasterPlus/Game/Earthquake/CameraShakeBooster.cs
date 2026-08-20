using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// バニラのカメラの揺れに、**強度と距離を入れた分を足す**。
    /// **main スレッド専用、毎フレーム。**
    ///
    /// ── 何を直しているのか（IL 事実文書 §A-7）─────────────────────
    ///
    /// <c>EarthquakeAI.RenderInstance</c> の振幅は <c>0.3 / (1 + dist*0.001)</c> で、
    /// **<c>m_intensity</c> が式に入っていない**。強度 25.5 の地震も 5.5 の地震と
    /// 完全に同じ強さで画面を揺らす。依頼の「震度分布の概念が無い」の、
    /// いちばん体感に近い部分がこれである。
    ///
    /// 直し方は**加算のみ**。バニラの計算を抑制も置換もしない（Harmony も要らない）:
    ///
    /// <code>
    /// 追加分 = バニラと同じ波 × (intensity / 55 - 1)
    /// 合計   = バニラの揺れ × (intensity / 55)
    /// </code>
    ///
    ///   - 強度 55（<c>CreateDisaster</c> の既定値）で追加分は**厳密に 0**。
    ///     そのときこのクラスは <c>m_cameraShake</c> に**一切書き込まない**ので、
    ///     挙動はバニラとビット単位で同一になる。既定 ON にできる根拠がこれ。
    ///   - 55 未満では負。弱い地震はより弱く揺れる。
    ///   - 上限は <see cref="ShakeWaveform.MaxIntensityFactor"/> と
    ///     <see cref="MaxAddedShake"/> の二段。強度 255 で画面が使い物に
    ///     ならなくなるのは迫力ではなく不具合。
    ///   - <c>DisasterManager.m_disableCameraShake</c> が true なら**何も足さない**。
    ///     プレイヤーが「揺らすな」と言っているのだから、この機能より優先する。
    ///
    /// ── バニラと同位相にするための 3 点 ──────────────────────────
    ///
    ///   1. 時刻は <c>m_referenceFrameIndex</c> と <c>m_referenceTimer</c>（描画側の時計）。
    ///      <c>m_currentFrameIndex</c> ではない。どちらも main スレッドが所有する。
    ///   2. 距離も**バニラと同じカメラ空間**で測る
    ///      （<c>InverseTransformPoint</c> して <c>z *= 0.25</c> してから長さ）。
    ///      震央距離に変えると、同じ波に別の振幅が掛かって形が崩れる。
    ///      観測点版（震源からの距離）は波形グラフ（Task 8）の仕事で、別物である
    ///      —— 同じ式の別評価であって近似ではない（設計書 §3.5）。
    ///   3. 窓も同じ（<c>e &lt;= 0</c> / <c>e &gt;= m_activeDuration</c> で何もしない）。
    ///      <c>m_activeDuration</c> は**プレハブ値でまだ誰も実測していない**ので、
    ///      読めていないときは <see cref="ShakeWaveform.IsShaking"/> が false を返し、
    ///      このクラスは何も足さない。窓が分からないまま足すと、地震が終わった後も
    ///      揺れ続ける。**マグニチュードを決め打ちで書かないこと。**
    ///
    /// ── 第 2 層: 合成記象へ差し替える（<c>ModSettings.EarthquakeSeismogram</c>）──
    ///
    /// 依頼②「揺れ方がリアルではない（同じ波形が連続している）」への回答。
    /// **既定 OFF。** ON のとき、このクラスは
    ///
    /// <code>
    /// 足す分 = 合成記象 × (intensity / 55) − バニラの項
    /// </code>
    ///
    /// を書き込む。バニラの項は**このクラスがバニラと同じ式・同じ点・同じ窓で
    /// 計算したもの**（上の 3 点）なので、合計はちょうど合成記象になる。
    /// Harmony もリフレクションも要らない —— <c>m_cameraShake</c> は加算で
    /// 消費されるフィールドなので、打ち消しも加算で書ける。
    ///
    /// **打ち消してよいことの根拠**（§A-7 の適用経路）: バニラは
    /// <c>DisasterManager.EndRenderingImpl</c> で <c>(m_flags &amp; 3) == Created</c> の
    /// 災害**すべて**について <c>RenderInstance</c> を呼ぶ。可視判定も距離打ち切りも
    /// 無いので、こちらが同じ条件（位相 Emerging|Active、<c>e</c> の窓、
    /// <c>m_disableCameraShake</c>）で評価すれば、バニラが足す量と 1 対 1 で対応する。
    /// 残差は <c>Mathf.Sin</c>(float) と <c>System.Math.Sin</c>(double) の最下位ビットだけである。
    ///
    /// ★ **OFF のときの経路は 1 命令も変えていない。** 強度 55 で追加分が厳密に 0 に
    ///   なる既定の道はそのまま残っている（それがこの機能を既定 ON にできる唯一の根拠）。
    ///
    /// ── 残留しないことの根拠 ──────────────────────────────
    ///
    /// <c>CameraController.LateUpdate</c> の**最後の 1 行**が
    /// <c>m_cameraShake = Vector3.zero</c>（IL_0281–0287、本タスクで実測）。
    /// 消費とリセットが毎フレーム走るので、足すのを止めればオフセットは
    /// 次のフレームで必ず消える。だからこそ**毎フレーム足し続ける**必要がある
    /// （sim tick ごとでは 1/n のフレームしか揺れない）。
    ///
    /// ── 毎フレームの経路であることの制約 ─────────────────────────
    ///
    /// 割り当てとログを置かない。<c>Log.Warn</c> / <c>Log.Error</c> は
    /// スロットルされないので、例外時も 1 回だけ鳴らして以後は
    /// <c>Log.Diag</c> のキー単位スロットルへ落とす
    /// （<c>EarthquakeReader._readErrorLogged</c> が確立した形）。
    /// </summary>
    public static class CameraShakeBooster
    {
        /// <summary>
        /// 1 フレームに足せる変位の絶対値の上限。バニラの理論最大は
        /// |sin + sin| = 2 × 0.3 = 0.6 なので、これはその 2 倍にあたる。
        /// </summary>
        private const float MaxAddedShake = 1.2f;

        /// <summary>
        /// 合成記象に差し替えたときに**カメラが受け取る合計**の上限。
        /// バニラの理論最大 0.6 の 3 倍で、<c>ShakeWaveform.MaxIntensityFactor</c>
        /// （＝強度倍率の上限 3.0）と揃えてある。**足す分ではなく合計を押さえる** ——
        /// 足す分を押さえると打ち消しが崩れて、バニラの波が残って混ざる。
        /// </summary>
        private const float MaxTotalShake = 3f * ShakeWaveform.MaxDisplacement;

        /// <summary>同時に覚えておく地震の数（§E-1 で複数同時に起こりうる）。</summary>
        private const int MaxTrackedQuakes = 8;

        /// <summary>
        /// 合成記象の**観測距離を地震ごとに 1 回だけ控える**ための表。
        ///
        /// P 波と S 波の到達時刻は距離で決まるので、毎フレーム測り直すと
        /// **カメラを動かしただけで到達時刻が動く**（揺れの途中で振幅が跳ねる）。
        /// 揺れの窓に入って最初に評価したフレームの距離をその地震の観測距離として
        /// 固定し、地震が消えたら枠を空ける。ID 0 は「空き」。
        ///
        /// ★ **鍵は ID だけではなく (ID, 発動フレーム) の組である。** 災害バッファの
        ///   添字は解放後すぐ再利用されるので（§E-1）、ID だけを鍵にすると
        ///   前の地震の観測距離を次の地震が引き継ぎうる。
        /// </summary>
        private static readonly ushort[] _trackedIds = new ushort[MaxTrackedQuakes];
        private static readonly uint[] _trackedFrames = new uint[MaxTrackedQuakes];
        private static readonly float[] _trackedDistances = new float[MaxTrackedQuakes];

        /// <summary>
        /// <c>CameraController</c> の参照。**静的キャッシュを素の参照比較で
        /// 検査してはいけない**（③で、破棄済みオブジェクトを掴んだまま
        /// 2 つ目の都市で無言で死んだ前例がある）。毎回 Unity の
        /// <c>== null</c>（fake-null も拾う）で確認し、駄目なら引き直す。
        /// </summary>
        private static CameraController _controllerCache;

        /// <summary>
        /// Unity 5.6 の <c>Camera.main</c> はタグ検索なので毎フレーム呼ばない
        /// （<c>ForecastPanel._mainCameraCache</c> / <c>EarthquakePanel</c> と同じ形）。
        /// </summary>
        private static Camera _mainCameraCache;

        private static bool _errorLogged;

        private static float _lastAdded;

        /// <summary>
        /// 直近のフレームで実際に足した変位の大きさ。**診断表示のためだけにある。**
        /// 実機で「効いているのか、それとも 0 を足し続けているのか」を確かめる
        /// 手段がこれ以外に無い（画面の揺れは目で見て区別できない）。
        /// </summary>
        public static float LastAdded { get { return _lastAdded; } }

        /// <summary>レベルアンロード時。都市をまたいで参照も数値も持ち越さない。</summary>
        public static void Reset()
        {
            _controllerCache = null;
            _mainCameraCache = null;
            _lastAdded = 0f;
            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                _trackedIds[i] = 0;
                _trackedFrames[i] = 0u;
                _trackedDistances[i] = 0f;
            }
            // _errorLogged は戻さない。「投げる」はこの DLL が参照しているゲームの
            // ビルドに対する事実であって、都市ごとの状態ではない
            // （EarthquakeReader._readErrorLogged と同じ判断）。
        }

        /// <summary>**main スレッドから毎フレーム。**</summary>
        public static void Update()
        {
            // 効いていないときに古い数値が診断に残らないよう、最初に落とす。
            _lastAdded = 0f;

            if (!ModSettings.EarthquakeEnabled.value) return;
            if (!ModSettings.EarthquakeShakeBoost.value) return;

            try
            {
                var snapshot = EarthquakeHub.Latest;
                if (snapshot == null || !snapshot.Valid) return;

                // m_activeDuration（＝バニラの表示窓）が読めていないなら何もしない。
                // ここを通してしまうと地震が終わっても揺れ続ける。
                if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u) return;

                var quakes = snapshot.Quakes;
                if (quakes.Count == 0) return;

                // ★ プレイヤーが揺れを切っているなら、この機能は存在しない。
                //    バニラの加算そのものが同じ条件で止まる（§A-7 の EndRenderingImpl）。
                if (!Singleton<DisasterManager>.exists) return;
                if (Singleton<DisasterManager>.instance.m_disableCameraShake) return;

                if (!SimulationManager.exists) return;
                var sim = SimulationManager.instance;

                // ここから先で初めて Unity オブジェクトを探す。地震が 1 個も無い
                // 通常のフレームでは、上の早期 return より先には来ない。
                var cam = ResolveMainCamera();
                if (cam == null) return;

                var controller = ResolveController();
                if (controller == null) return;

                Vector3 added = Accumulate(quakes, snapshot.Prefab.ActiveDuration, sim, cam);

                // ★ 追加分が厳密に 0 のときは**書き込まない**。強度 55（バニラ既定）は
                //    必ずここへ来るので、その場合の挙動はバニラとビット単位で同一になる。
                if (added.x == 0f && added.y == 0f && added.z == 0f) return;

                controller.m_cameraShake += added;
                _lastAdded = added.magnitude;
            }
            catch (System.Exception e)
            {
                // 毎フレームの経路。1 回だけ大きく鳴らし、以後はキー単位スロットルへ。
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("earthquake camera shake boost failed", e);
                }
                else
                {
                    Log.Diag("EqShake", "camera shake boost failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>
        /// 進行中の地震ぶんの追加変位を合計し、<see cref="MaxAddedShake"/> で頭を押さえる。
        ///
        /// 割り当てを一切しない（<c>Vector3</c> は構造体、リストは添字走査）。
        /// </summary>
        private static Vector3 Accumulate(IList<EarthquakeReading> quakes,
                                          uint activeDuration, SimulationManager sim, Camera cam)
        {
            bool seismogram = ModSettings.EarthquakeSeismogram.value;

            // ★ 設定の ON / OFF に関わらず毎フレーム掃除する。OFF のあいだ掃除を
            //   止めると、次に ON にしたとき前の地震の枠が残ったままになる。
            ForgetGoneQuakes(quakes);

            Vector3 added = Vector3.zero;

            // 合成記象に差し替えるときだけ使う。target は「カメラが受け取るべき合計」、
            // vanilla は「バニラが自分で足す量」で、その差だけを書き込む。
            Vector3 target = Vector3.zero;
            Vector3 vanilla = Vector3.zero;

            Transform camTransform = cam.transform;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];

                // バニラの (m_flags & 12) != 0 と同じ条件。位相ビットは互いに排他なので
                // （§A-1、DisasterPhases の doc とユニットテスト）、位相 2 種の比較で足りる。
                if (q.Phase != EarthquakePhase.Emerging && q.Phase != EarthquakePhase.Active) continue;

                // m_activationFrame == 0 は「今」ではなく「未定」。引き算に使うと
                // 途方も無い e になる（§A-1 の罠）。
                if (!q.ActivationScheduled) continue;

                float factor = ShakeWaveform.IntensityFactor(q.Intensity);
                // ★ 強度 55 ではここで打ち切られる（合成記象が OFF のときだけ）。
                //   以後の三角関数も実行されない ＝ バニラとビット単位で同一。
                if (!seismogram && factor == 0f) continue;

                long e = (long)sim.m_referenceFrameIndex - q.ActivationFrame + ShakeWaveform.FrameOffset;
                if (!ShakeWaveform.IsShaking(e, activeDuration)) continue;

                float t = e + sim.m_referenceTimer;

                // §A-7 IL_003F と同じカメラ空間の距離。z を 0.25 倍するところまで写す。
                Vector3 v = camTransform.InverseTransformPoint(
                    new Vector3(q.Epicentre.X, q.Epicentre.Y, q.Epicentre.Z));
                v.z *= 0.25f;
                float cameraDistance = v.magnitude;

                // §A-7 IL_00AA / IL_00F2: 揺れは断層に直交する 1 方向。
                float dirX = -Mathf.Sin(q.AngleRadians);
                float dirZ = Mathf.Cos(q.AngleRadians);

                if (!seismogram)
                {
                    float displacement = ShakeWaveform.DisplacementAt(cameraDistance, t) * factor;
                    if (displacement == 0f) continue;

                    added.x += displacement * dirZ;
                    added.z -= displacement * dirX;
                    continue;
                }

                // ── 第 2 層: 合成記象に差し替える ──────────────────────
                // 観測距離は地震ごとに 1 回だけ控える（カメラを動かしても
                // P/S の到達時刻を動かさないため。_trackedDistances の doc）。
                float observed = ObservationDistance(q.DisasterId, q.ActivationFrame, cameraDistance);

                var model = SeismogramModel.For(
                    DeterministicRandom.Hash(q.DisasterId, q.ActivationFrame), activeDuration);

                // 1 + factor ＝ intensity / 55（クランプ済み）。強度 55 でちょうど等倍。
                float wanted = model.DisplacementAt(observed, t) * (1f + factor);
                float replaced = ShakeWaveform.DisplacementAt(cameraDistance, t);

                target.x += wanted * dirZ;
                target.z -= wanted * dirX;
                vanilla.x += replaced * dirZ;
                vanilla.z -= replaced * dirX;
            }

            if (seismogram)
            {
                // ★ 押さえるのは**合計**であって足す分ではない。足す分を押さえると
                //   打ち消しが崩れ、消したはずのバニラの波が残って混ざる。
                float total = target.magnitude;
                if (total > MaxTotalShake) target *= MaxTotalShake / total;
                return target - vanilla;
            }

            // 地震が同時に複数起きうる（§E-1）ので、頭は合計に対して押さえる。
            float magnitude = added.magnitude;
            if (magnitude > MaxAddedShake)
            {
                added *= MaxAddedShake / magnitude;
            }
            return added;
        }

        /// <summary>
        /// この地震の観測距離。**初めて見たフレームの距離をそのまま覚える。**
        /// 表が満杯なら覚えずに今の距離を返す（覚えられないだけで、揺れは出る）。
        /// </summary>
        private static float ObservationDistance(ushort disasterId, uint activationFrame,
                                                 float current)
        {
            if (disasterId == 0) return current;

            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                if (_trackedIds[i] == disasterId && _trackedFrames[i] == activationFrame)
                {
                    return _trackedDistances[i];
                }
            }

            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                if (_trackedIds[i] != 0) continue;
                _trackedIds[i] = disasterId;
                _trackedFrames[i] = activationFrame;
                _trackedDistances[i] = current;
                return current;
            }

            return current;
        }

        /// <summary>
        /// もう揺れていない地震の枠を空ける。**空けないと表が埋まったまま**になり、
        /// 次の地震が観測距離を控えられなくなる（毎フレーム測り直しに落ちる）。
        /// 毎フレームの経路なので割り当ては無い（添字走査だけ）。
        /// </summary>
        private static void ForgetGoneQuakes(IList<EarthquakeReading> quakes)
        {
            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                ushort id = _trackedIds[i];
                if (id == 0) continue;

                bool alive = false;
                for (int j = 0; j < quakes.Count; j++)
                {
                    var q = quakes[j];
                    if (q.DisasterId != id || q.ActivationFrame != _trackedFrames[i]) continue;
                    alive = q.Phase == EarthquakePhase.Emerging || q.Phase == EarthquakePhase.Active;
                    break;
                }

                if (alive) continue;
                _trackedIds[i] = 0;
                _trackedFrames[i] = 0u;
                _trackedDistances[i] = 0f;
            }
        }

        private static Camera ResolveMainCamera()
        {
            // Unity の == null なので、破棄済み（fake-null）なら引き直しになる。
            if (_mainCameraCache == null) _mainCameraCache = Camera.main;
            return _mainCameraCache;
        }

        private static CameraController ResolveController()
        {
            if (_controllerCache == null) _controllerCache = SceneObjects.FindInScene<CameraController>();
            return _controllerCache;
        }
    }
}
