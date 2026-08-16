using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 噴火で借りられるものが、この環境で実際に使えるか。
    ///
    /// ★ <b>「解決した」ではなく「使える」を持たせる。</b> ④のレビューと②の監査は
    /// 「フィールドが解決したか」を述語にした検査が、値が使えない環境で PASS を
    /// 出す欠陥を見つけている。<see cref="Usable"/> は
    /// <see cref="VolcanoEruption.RenderBorrowed"/> が実際に門にしている式そのもので、
    /// <c>Assumptions</c> もこの同じ式を述語にする。
    ///
    /// bool しか持たないので、<c>VolcanoTerrainFacts</c> と同じくキャッシュしても
    /// Unity の fake-null 問題を持ち込まない。既定値は全て false ＝「読めていない」。
    /// </summary>
    public struct VolcanoBorrowFacts
    {
        /// <summary>
        /// <c>BuildingManager.instance.m_properties.m_fireEffect</c> に届いたか（§B-5）。
        /// **DLC 不要。** バニラの建物火災とまったく同じ炎である。
        /// </summary>
        public readonly bool FireEffectResolved;

        /// <summary>
        /// <c>RenderManager.instance.CurrentCameraInfo</c> が今 null でないか。
        /// <c>EffectInfo.RenderEffect</c> の最後の引数がこれで、
        /// <c>ParticleEffect.RenderEffect</c> は先頭で <c>CheckRenderDistance</c> /
        /// <c>Intersect</c> を呼ぶので、null を渡すと NRE になる。
        /// </summary>
        public readonly bool CameraInfoResolved;

        public VolcanoBorrowFacts(bool fireEffectResolved, bool cameraInfoResolved)
        {
            FireEffectResolved = fireEffectResolved;
            CameraInfoResolved = cameraInfoResolved;
        }

        /// <summary>
        /// 借り物の炎を重ねてよいか。**これが借用の門そのものである。**
        /// false でも⑤自前の噴出物は必ず出る（噴火は成立する）。
        /// </summary>
        public bool Usable { get { return FireEffectResolved && CameraInfoResolved; } }
    }

    /// <summary>
    /// 山頂の噴火。**sim と main で役割が分かれる唯一の⑤の型である。**
    ///
    /// <code>
    /// [sim ] Tick()    噴出の強さと山頂座標を決めるだけ。Unity オブジェクトを 1 つも作らない
    /// [main] Render()  VolcanoHub.Latest を読んで描く。ゲームの状態を 1 つも変えない
    /// </code>
    ///
    /// ── 借りるものと自作するもの（計画 §7.1。設計書 §4.4 から 1 点変更している）──
    ///
    /// 設計書 §4.4 は「プルームは <c>DisasterProperties.m_mediumExplosion</c> を
    /// 借りられる」と書いているが、⑤はそれを**主経路にしない**。
    /// <c>DisasterProperties</c> が ND DLC 非所持の実行環境で実体を持つかは
    /// DLL からは判定できない（§D-11 と同じ性質）。⑤が主経路にするのは
    /// **自前の <c>ParticleSystem</c>** で、③の <c>FireWhirlFlameFx</c> が
    /// 既に出荷され実機で動いている形をそのまま写している。
    ///
    /// その上に重ねるのが <c>BuildingProperties.m_fireEffect</c> の炎である
    /// （§B-5 で CONFIRMED、**DLC 不要**、<c>m_properties</c> は public、
    /// <c>RenderEffect</c> は public virtual、<c>SpawnArea(Vector3,Vector3,float)</c> は public）。
    /// **解決できなければ黙って自前だけに落ちる。** 噴火は何も欠けない。
    ///
    /// ── 音の経路は作らない（計画 §7.1 の判断。ただし理由が違う）───────────
    ///
    /// 計画は「<c>m_fireEffect</c> は <c>FireEffect</c> 合成型で <c>SoundEffect</c> を
    /// 子に持つので <c>RenderEffect</c> を通せば音も出る」と書いているが、
    /// **本タスクの IL 実測はそれを否定した**:
    ///
    /// <code>
    /// FireEffect.RenderEffect : m_particleEffect.EmitParticles → LightSystem.DrawLight
    ///                           **m_soundEffect には 1 度も触れない**
    /// FireEffect.PlayEffect   : 音はこちらの経路にある（AudioManager.ListenerInfo と
    ///                           AudioGroup が要る）
    /// </code>
    ///
    /// したがって⑤の噴火に**音は出ない**。それでも音の経路を新設しないという
    /// 計画の決定は変えていない —— <c>PlayEffect</c> は毎フレーム呼ぶ想定の API では
    /// なく、<c>ListenerInfo</c> / <c>AudioGroup</c> の扱いも本 MOD で前例が無い。
    /// **出ないものを「出るはず」と実機チェックリストに書き続けるほうが害が大きい**ので、
    /// チェックリストの側を実測に合わせて直してある。
    ///
    /// ── <c>DispatchEffect</c> を使わない ───────────────────────────
    ///
    /// <c>magnitude</c> は粒子**密度**であってサイズではない（火災旋風 §4.9）。
    /// 大きさを決めるのは <c>SpawnArea</c> の半径である。⑤は
    /// <c>RenderEffect</c> に <c>SpawnArea(position, direction, radius)</c> を渡す
    /// 経路だけを使う。
    ///
    /// ── 罠: CS のマテリアルを借りない／静的キャッシュを配列にしない ───────────
    ///
    /// CS のマテリアルを借りると自前の <c>Renderer</c> では何も描かれないか真っ黒になる
    /// （火災旋風 §4.9）。借りるのは <c>ShaderPool</c> が取ってくる**シェーダだけ**で、
    /// マテリアルは自作する（2 つの違いはあちらのクラス doc）。
    /// <c>static GameObject[]</c> / <c>Material[]</c> にすると Unity の <c>==</c> による
    /// fake-null の自己修復が効かず、**2 つ目の都市で無言で見えなくなる**
    /// （同 §4.8。③が実際に出荷した不具合）。ここは**参照 1 個ずつ**で持ち、
    /// 毎フレームその参照そのものを <c>== null</c> で見る。
    ///
    /// ── 毎フレームの費用（main）──────────────────────────────
    ///
    /// <c>Transform.position</c> の書き込み 1 回、借り物の炎の <c>RenderEffect</c> 1 回、
    /// そして**強さが 0.02 以上動いたときだけ** <c>ParticleSystem</c> のモジュール
    /// 書き込み 4 本。<c>SpawnArea</c> / <c>InstanceID</c> / <c>Vector3</c> /
    /// <c>MinMaxCurve</c> はいずれも struct なので、**ヒープ確保は 0 バイト**である
    /// （<c>GameObject</c> と <c>Material</c> は都市ごとに 1 回だけ作る）。
    ///
    /// ── 毎 tick の費用（sim）───────────────────────────────
    ///
    /// <c>SampleDetailHeight</c> 1 回（4 読み ＋ 3 <c>Lerp</c>、§B-6）と float 20 本ほど。
    /// **確保は 0 バイト。** 山頂の高さを毎 tick 引き直すのは、火口を彫った
    /// <c>UpdateArea</c> が <c>m_detailHeights</c> に反映されるのが数フレーム遅れるためで、
    /// 1 回だけ読むと**火口を彫る前の高さに噴煙が張り付く**。
    /// </summary>
    public static class VolcanoEruption
    {
        /// <summary>噴火が続くゲーム内時間（分）。**⑤が決めた演出値。**</summary>
        private const float TotalMinutes = 24f;

        /// <summary>強さを引き直す 1 区切り（ゲーム内分）。**フレーム番号は混ぜない。**</summary>
        private const float BurstMinutes = 2f;

        /// <summary>立ち上がりに使う割合（0〜この値で 0 → 1）。</summary>
        private const float RiseFraction = 0.08f;

        /// <summary>衰退が始まる割合（ここから 1 へ向けて 1 → 0）。</summary>
        private const float DecayFraction = 0.65f;

        /// <summary>強さの下限側のゆらぎ（1 区切りごとに <c>[Floor, 1]</c> を引く）。</summary>
        private const float JitterFloor = 0.62f;

        /// <summary>噴出口を火口の底からどれだけ上げるか（m）。</summary>
        private const float VentLiftMetres = 6f;

        /// <summary>借り物の炎を置く高さ（噴出口からの相対、m）。</summary>
        private const float FlameLiftMetres = 14f;

        /// <summary>粒子のモジュールを書き直す強さの刻み（毎フレーム書かないため）。</summary>
        private const float ReconfigureStep = 0.02f;

        /// <summary>噴煙のレイヤー。0 ＝ Default はどのカメラのカリングマスクにも入る。</summary>
        private const int PlumeLayer = 0;

        // ── sim 側の状態 ──────────────────────────────────────

        private static bool _started;
        private static bool _active;
        private static bool _finished;
        private static Vec3 _centre;
        private static Vec3 _summit;
        private static float _elapsedMinutes;
        private static float _intensity;
        private static int _burstIndex;
        private static int _bursts;
        private static uint _seed;
        private static string _lastFailure;
        private static bool _errorLogged;

        // ── main 側の状態（★ 配列にしない。参照 1 個ずつ） ──────────────────

        private static GameObject _plume;
        private static ParticleSystem _particles;
        private static Material _material;
        private static float _configuredUnit = -1f;
        private static VolcanoBorrowFacts _borrowFacts;

        /// <summary>直近に解決したシェーダの事実（**取れなければ <c>Usable</c> が false**）。
        /// <see cref="Reset"/> でも <see cref="Destroy"/> でも戻さない ——
        /// <see cref="_shaderWarned"/> と同じくゲームのビルドに対する事実である。</summary>
        private static ShaderPick _pick;

        private static bool _shaderWarned;
        private static bool _borrowWarned;
        private static bool _renderErrorLogged;

        /// <summary>噴火が進行中か（**sim が決め、スナップショットに載る**）。</summary>
        public static bool Active { get { return _active; } }

        /// <summary>噴火が終わったか。<see cref="VolcanoState"/> が次の位相へ進む合図。</summary>
        public static bool Finished { get { return _finished; } }

        /// <summary>今の噴出の強さ <c>[0,1]</c>。**⑤が決めた量**で、ゲームの値ではない。</summary>
        public static float IntensityUnit { get { return _intensity; } }

        /// <summary>噴出口のワールド座標（<c>Y</c> は <c>SampleDetailHeight</c> ＋ 少しの浮き）。</summary>
        public static Vec3 SummitWorld { get { return _summit; } }

        /// <summary>これまでに強さを引き直した回数（診断用）。</summary>
        public static int BurstsSoFar { get { return _bursts; } }

        /// <summary>借り物の炎がこの環境で使えるか（**直近の <see cref="Render"/> の実測**）。</summary>
        public static bool BorrowedEffectAvailable { get { return _borrowFacts.Usable; } }

        /// <summary><c>RenderManager.CurrentCameraInfo</c> に届いたか（診断の切り分け用）。</summary>
        public static bool CameraInfoAvailable { get { return _borrowFacts.CameraInfoResolved; } }

        /// <summary>⑤自前の噴出物を今描いているか。</summary>
        public static bool Drawing { get { return _plume != null && _particles != null; } }

        /// <summary>
        /// 噴煙のマテリアルが実際に解決したシェーダの名前（**null なら 1 つも取れなかった**）。
        /// 診断に出す —— 将来のゲーム更新で黙って不可視になったときの唯一の手がかり。
        /// </summary>
        public static string ShaderName { get { return _pick.Name; } }

        /// <summary>
        /// 粒子系のシェーダ（加算 / アルファブレンド）が取れたか。**<c>Standard</c> は
        /// ここに数えない**（全体レビュー M10）—— 混ぜた瞬間にこの旗は
        /// **構造上 1 度も false になれない**（と思われていたが、実機では
        /// <c>Shader.Find("Standard")</c> すら null を返した。それでも規律は同じである）。
        /// </summary>
        public static bool ParticleShaderResolved { get { return _pick.Particle; } }

        /// <summary>診断に出す 1 行（**英語**）。</summary>
        public static string ShaderDetail
        {
            get
            {
                return _pick.Usable
                    ? _pick.Describe()
                    : "NONE (no shader resolved; the plume is not drawn)";
            }
        }

        /// <summary>直近の失敗（**英語・診断用**）。無ければ null。</summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// **main スレッド専用。** 借り物の炎がこの環境で使えるかを調べるだけの純粋な走査。
        /// キャッシュを触らないので <c>Assumptions</c>（main）からも呼べる ——
        /// そして <see cref="RenderBorrowed"/> が門にするのも**この同じ式**である。
        ///
        /// <c>Singleton&lt;T&gt;.exists</c> を先に見る（<c>instance</c> は <c>sInstance</c> が
        /// null のとき <c>FindObjectOfType</c> と <c>new GameObject</c> を走らせる）。
        /// </summary>
        public static VolcanoBorrowFacts ScanBorrowFacts()
        {
            bool fireEffect = false;
            bool cameraInfo = false;

            try
            {
                if (Singleton<BuildingManager>.exists)
                {
                    var properties = Singleton<BuildingManager>.instance.m_properties;
                    fireEffect = properties != null && properties.m_fireEffect != null;
                }
            }
            catch
            {
                fireEffect = false;
            }

            try
            {
                if (Singleton<RenderManager>.exists)
                {
                    cameraInfo = Singleton<RenderManager>.instance.CurrentCameraInfo != null;
                }
            }
            catch
            {
                cameraInfo = false;
            }

            return new VolcanoBorrowFacts(fireEffect, cameraInfo);
        }

        /// <summary>
        /// **sim スレッド。** 噴出の予定を決めるだけで、Unity オブジェクトを 1 つも作らない。
        /// <see cref="VolcanoState"/> の位相分岐からのみ呼ぶこと。
        /// </summary>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes)
        {
            try
            {
                Step(footprint, deltaMinutes);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the eruption tick threw " + e.GetType().Name;
                _active = false;
                // ★ 例外で位相を止めない。噴火は演出であって、ここで固まると
                //   プレイヤーは 2 つ目の火山を永久に置けなくなる。
                _finished = true;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano eruption failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcErupt",
                             "volcano eruption failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre)) Start(footprint);
            if (_finished) return;

            if (deltaMinutes > 0f) _elapsedMinutes += deltaMinutes;

            if (_elapsedMinutes >= TotalMinutes)
            {
                _active = false;
                _finished = true;
                _intensity = 0f;
                return;
            }

            // ★ 山頂は毎 tick 引き直す（クラス doc の費用表）。火口を彫った
            //   UpdateArea が m_detailHeights に届くのは数フレーム遅れるので、
            //   1 回しか読まないと噴煙が彫る前の高さに張り付く。
            _summit = SampleSummit(footprint);

            // ★ 区切りは経過ゲーム内時間から出す。**frameIndex % N で組まない**
            //   （DAYTIME_FRAMES = 65536、1 ゲーム内分 ≒ 45.51 フレーム。火災旋風 付録 A-4）。
            int burst = (int)(_elapsedMinutes / BurstMinutes);
            if (burst != _burstIndex)
            {
                _burstIndex = burst;
                _bursts++;
            }

            // ★ 乱数にフレーム番号を混ぜない（計画「2 つの乱数生成器」）。混ぜると
            //   同じ噴火が tick ごとに抽選し直され、強さが毎フレーム跳ねる。
            float jitter = JitterFloor
                           + (1f - JitterFloor)
                             * DeterministicRandom.Unit(_seed, (uint)_burstIndex);

            _intensity = Clamp01(Envelope(_elapsedMinutes / TotalMinutes) * jitter);
            _active = true;
        }

        /// <summary>
        /// 立ち上がり → 持続 → 衰退の包絡線 <c>[0,1]</c>。
        /// **物理量ではない**（設計書 §7.4 / 計画「出してよい断定の範囲」の 5）。
        /// </summary>
        private static float Envelope(float t)
        {
            if (float.IsNaN(t)) return 0f;
            if (t <= 0f) return 0f;
            if (t >= 1f) return 0f;
            if (t < RiseFraction) return t / RiseFraction;
            if (t > DecayFraction) return (1f - t) / (1f - DecayFraction);
            return 1f;
        }

        private static void Start(VolcanoFootprint footprint)
        {
            Reset();
            _started = true;
            _centre = footprint.Centre;
            _summit = SampleSummit(footprint);

            // 地点から決まる種。**都市をまたいでも同じ地点なら同じ噴火**になる
            // （DeterministicRandom は状態を持たないハッシュ）。
            _seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.Z)));
        }

        /// <summary>
        /// 噴出口のワールド座標。**sim スレッド専用**（<c>TerrainManager</c>）。
        /// 読めなければ調査時の地形高さ ＋ 山の高さで代用する ——
        /// **0 を並べた「それらしい」座標を作らない**（地面の中で噴火することになる）。
        /// </summary>
        private static Vec3 SampleSummit(VolcanoFootprint footprint)
        {
            float x = footprint.Centre.X;
            float z = footprint.Centre.Z;
            float fallback = footprint.GroundHeightMetres + footprint.HeightMetres;

            float y = fallback;
            try
            {
                if (Singleton<TerrainManager>.exists)
                {
                    y = Singleton<TerrainManager>.instance
                            .SampleDetailHeight(new Vector3(x, 0f, z));
                }
            }
            catch
            {
                y = fallback;
            }

            if (float.IsNaN(y)) y = fallback;
            return new Vec3(x, y + VentLiftMetres, z);
        }

        /// <summary>
        /// **main スレッド、毎フレーム。** <see cref="VolcanoHub"/> のスナップショットだけを
        /// 読み、ゲームの状態を 1 つも変えない。
        /// </summary>
        public static void Render()
        {
            try
            {
                RenderStep(VolcanoHub.Latest);
            }
            catch (Exception e)
            {
                if (!_renderErrorLogged)
                {
                    _renderErrorLogged = true;
                    Log.Error("volcano eruption render failed", e);
                }
                Destroy();
            }
        }

        private static void RenderStep(VolcanoSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.EruptionActive)
            {
                // 噴火が終わったフレームで**自分で**後始末する。sim 側からは呼ばれない。
                Destroy();
                return;
            }

            float unit = Clamp01(snapshot.EruptionIntensityUnit);
            Vec3 summit = snapshot.SummitWorld;

            // 火口の半径から噴煙の太さを出す（設計書 §4.3 の火口と同じ寸法系）。
            float craterRadius = VolcanoShape.CraterRadiusOf(snapshot.Footprint.RadiusMetres);
            if (!(craterRadius > 0f)) craterRadius = VolcanoShape.MinDomeRadiusMetres * 0.1f;

            // ★ 参照そのものを毎フレーム見る。破棄済みなら Unity の fake-null で
            //   null と等価になり、ここで作り直される（2 つ目の都市の自己修復）。
            if (_plume == null || _particles == null) Build();
            if (_plume == null || _particles == null) return;

            _plume.transform.position = new Vector3(summit.X, summit.Y, summit.Z);

            // ★ 強さがほとんど動いていないフレームではモジュールを書かない
            //   （クラス doc の費用表）。
            if (_configuredUnit < 0f || Mathf.Abs(unit - _configuredUnit) >= ReconfigureStep)
            {
                Configure(unit, craterRadius);
                _configuredUnit = unit;
            }

            RenderBorrowed(summit, unit, craterRadius);
        }

        /// <summary>
        /// 借り物の炎を噴出口に重ねる。**使えなければ黙って戻る**（自前の噴出物だけで
        /// 噴火は成立する）。門にしているのは <see cref="VolcanoBorrowFacts.Usable"/> で、
        /// <c>Assumptions</c> の述語と 1 文字も違わない。
        /// </summary>
        private static void RenderBorrowed(Vec3 summit, float unit, float craterRadius)
        {
            _borrowFacts = ScanBorrowFacts();
            if (!_borrowFacts.Usable)
            {
                if (!_borrowWarned)
                {
                    _borrowWarned = true;
                    // ★ Log.Warn はスロットルされず、ここは毎フレームの経路なので
                    //   1 回だけ鳴らす。**欠けても噴火は出る**ので Info で足りる。
                    Log.Info("volcano eruption: the game's own fire effect could not be "
                             + "borrowed in this environment (fireEffect="
                             + (_borrowFacts.FireEffectResolved ? "ok" : "missing")
                             + ", cameraInfo="
                             + (_borrowFacts.CameraInfoResolved ? "ok" : "missing")
                             + "); Disaster + draws its own plume only");
                }
                return;
            }

            var effect = Singleton<BuildingManager>.instance.m_properties.m_fireEffect;
            var camera = Singleton<RenderManager>.instance.CurrentCameraInfo;

            // ★ 大きさは SpawnArea の半径で決まる。magnitude は粒子密度である
            //   （火災旋風 §4.9）。バニラの建物火災は magnitude を [0,1] で渡し
            //   （m_fireIntensity / 255、§B-5）、FireEffect が ×100 して
            //   「強さの百分率」として使う。⑤も同じ帯に収める。
            float magnitude = 0.25f + 0.75f * unit;
            float radius = craterRadius * (0.45f + 0.35f * unit);

            var area = new EffectInfo.SpawnArea(
                new Vector3(summit.X, summit.Y + FlameLiftMetres, summit.Z),
                Vector3.up, radius);

            // InstanceID は空でよい。ParticleEffect が建物を引くのは
            // m_buildingFlagRequired が立っているときだけで、そのときも
            // 「建物 0 なら旗の検査を飛ばす」形になっている（§B-5 の IL_0037-0076）。
            effect.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                magnitude, -1f, Time.deltaTime, camera);
        }

        /// <summary>
        /// ⑤自前の噴出物を 1 個作る。都市ごとに 1 回だけ。
        /// **<c>FireWhirlFlameFx</c> をそのまま写している**（③で出荷済み）。
        /// </summary>
        private static void Build()
        {
            Destroy();

            Material material = BuildMaterial();
            if (material == null)
            {
                _lastFailure = "no usable shader for the eruption plume";
                return;
            }

            _material = material;

            var go = new GameObject("DisasterPlus_VolcanoPlume");
            go.layer = PlumeLayer;

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 4.5f;
            // 明 → 暗の 2 色勾配（③と同じ考え方）。**色はここでだけ決める。**
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.82f, 0.35f, 1f), new Color(0.55f, 0.12f, 0.04f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = _material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // 噴煙は演出であって遮蔽物ではない（④のレビューが同じ指摘をしている）。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _plume = go;
            _particles = ps;
            _configuredUnit = -1f;
        }

        /// <summary>
        /// 強さと火口の大きさに合わせて形と量を変える。
        /// **毎フレームは呼ばない**（<see cref="ReconfigureStep"/>）。
        /// </summary>
        private static void Configure(float unit, float craterRadius)
        {
            if (_particles == null) return;

            var main = _particles.main;
            main.startSize = craterRadius * (0.18f + 0.12f * unit);
            main.startSpeed = 40f + 90f * unit;

            var shape = _particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = craterRadius * 0.35f;
            // 強いほど大きく開く。
            shape.angle = 8f + 14f * unit;

            var emission = _particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 20f + 120f * unit;
        }

        /// <summary>
        /// **CS のマテリアルは借りない**（火災旋風 §4.9）。借りるのは
        /// <see cref="ShaderPool"/> が取ってくる<b>シェーダだけ</b>で、
        /// <c>Material</c> は必ずここで作る（2 つの違いはあちらのクラス doc）。
        /// 噴煙なので加算合成を狙う。
        ///
        /// ★★ <b><c>Standard</c> は「粒子系が取れた」の外側に置く</b>（全体レビュー M10）。
        ///   かつてここの探索の末尾が <c>Shader.Find("Standard")</c> だった頃、
        ///   下の <c>Usable == false</c> にあたる枝は**構造上 1 度も真になれず**、
        ///   <c>Log.Warn</c> も <see cref="LastFailure"/> も到達しない死んだ枝だった
        ///   （<see cref="VolcanoLavaFx"/> のクラス doc が同じ罠を避けている）。
        ///   しかも <c>Standard</c> を素で使うと**不透明な板**になるので、
        ///   あちらと同じく透過モードへ落とす。
        ///
        /// ★ 探索の間引きは <see cref="ShaderPool"/> の側にある。ここは
        ///   <c>_plume</c> が無いあいだ毎フレーム呼ばれうるが、あちらは解決済みなら
        ///   キャッシュを返すだけで、走査は間引かれている。
        /// </summary>
        private static Material BuildMaterial()
        {
            _pick = ShaderPool.Resolve(ShaderPreference.Additive);
            if (!_pick.Usable)
            {
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    Log.Warn("volcano eruption: no usable shader resolved; the plume is not "
                             + "drawn (Disaster + does not borrow a Cities material - that "
                             + "renders invisible or black in a hand-rolled renderer)");
                }
                return null;
            }

            var m = new Material(_pick.Shader);
            m.name = "DisasterPlus_VolcanoPlume";

            // ★ Standard まで落ちたときの受け皿。透過にしないと噴煙が
            //   **不透明な四角い板の群れ**になる。借りてきた別のシェーダには
            //   掛けない（_Mode / _SrcBlend は Standard の契約である）。
            if (_pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            return m;
        }

        /// <summary>
        /// **main スレッド。** 噴煙を畳む。<c>Material</c> は <c>Component</c> では
        /// ないので <c>GameObject</c> を消しても道連れにならない ——
        /// **自分で <c>Object.Destroy</c> する。** 冪等。
        /// </summary>
        public static void Destroy()
        {
            if (_plume != null) UnityEngine.Object.Destroy(_plume);
            _plume = null;
            _particles = null;

            if (_material != null) UnityEngine.Object.Destroy(_material);
            _material = null;

            _configuredUnit = -1f;
        }

        /// <summary>
        /// sim 側の状態を捨てる。**レベルアンロードと、新しい火山の開始で呼ぶ。**
        /// <c>_errorLogged</c> / <c>_shaderWarned</c> は戻さない（ゲームのビルドに対する
        /// 事実であって都市ごとの状態ではない。④の <c>TyphoonCloud</c> と同じ判断）。
        /// </summary>
        public static void Reset()
        {
            _started = false;
            _active = false;
            _finished = false;
            _centre = new Vec3(0f, 0f, 0f);
            _summit = new Vec3(0f, 0f, 0f);
            _elapsedMinutes = 0f;
            _intensity = 0f;
            _burstIndex = 0;
            _bursts = 0;
            _seed = 0u;
            _lastFailure = null;
        }

        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcErupt",
                     "eruption " + (_active ? "active" : (_finished ? "finished" : "idle"))
                     + " intensity=" + _intensity.ToString("F2")
                     + " bursts=" + _bursts
                     + " elapsed=" + _elapsedMinutes.ToString("F0") + " min"
                     + " frame=" + frame);
        }

        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
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
