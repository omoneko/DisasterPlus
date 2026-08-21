using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>吹き付ける雨が今どうなっているか。</summary>
    public enum TyphoonSquallState
    {
        /// <summary>設定で切っている（あるいはまだ都市に入っていない）。</summary>
        Off,

        /// <summary>借りられる粒子エフェクトがこの環境に無い。**不具合ではない**（雨は降る）。</summary>
        NoEffect,

        /// <summary>台風が居ないので出していない。</summary>
        Idle,

        /// <summary>カメラが強風域の外に居る。**不具合ではない** —— そこは吹いていない。</summary>
        OutsideStorm,

        /// <summary>毎フレーム飛沫を出している。</summary>
        Emitting,

        /// <summary>例外で落ちた。</summary>
        Failed,
    }

    /// <summary>
    /// <b>暴風雨</b>を地上で見せる。<b>main スレッド専用。</b>
    ///
    /// ── 持ち主の指摘（2026-08-22）────────────────────────────────
    ///
    /// &gt; 暴風雨を再現してほしいです。
    ///
    /// ④が今まで「嵐の強さ」として出せていたのは <c>m_targetRain</c> /
    /// <c>m_targetCloud</c> の 2 つだけだった。どちらも**空全体の設定**で、
    /// バニラの雨は真下に降り、風向きにも台風の位置にも従わない。
    /// 地上のプレイヤーから見ると、最盛期の台風もただの雨と同じ絵になる。
    ///
    /// ★★ <b>雨量はもう上げられない。</b> 最盛期の <c>m_targetRain</c> は既に 1.0 で、
    ///   しかも <c>m_currentRain &gt; 0.8</c> はゲーム自身に雷雨災害を作らせる境界である
    ///   （IL 事実文書 §A-3）。④の落雷の予算はその手前で釣り合いを取っている
    ///   （設計書 §4.2）。**この機能は雨量に 1 バイトも触らない。**
    ///
    /// 代わりに足すのが<b>横殴りの飛沫</b>である。バニラの雨と違って
    /// <c>velocity</c> 引数で風下へ流れるので、はじめて「吹いている」ように見える。
    ///
    /// ── ★ カメラの周りに置く（台風の中心ではない）────────────────────────
    ///
    /// 渦（<see cref="TyphoonCloudFx"/>）は台風の中心に置く。こちらは違う ——
    /// <b>「自分が居るところが吹き荒れている」を見せるもの</b>なので、
    /// <c>RenderManager.CurrentCameraInfo.m_position</c> の下に置く。
    /// 5 km 先で飛沫が舞っても画面には何も起きない。
    ///
    /// 強さは <c>TyphoonProfile.WindAt</c>（カメラと台風中心の距離）から
    /// <c>SquallLayout.StrengthOf</c> が決める。強風域の外では**1 粒も出さない**
    /// （<see cref="TyphoonSquallState.OutsideStorm"/>。不具合ではない）。
    ///
    /// ── 素材 ─────────────────────────────────────────
    ///
    /// <see cref="VanillaParticles"/> が在庫を列挙して、粒子マテリアル
    /// <c>Water</c>（出荷アセットの <c>water</c> テクスチャは平均 RGB (201, 222, 254)
    /// の白青の飛沫）を最優先で採る。**名前で引かない**理由はあちらの doc。
    /// 借り元は放出角が 1 度しか無く可視距離も 500〜2000 m なので、
    /// **複製して**ほぼ水平（66〜104 度）に、可視 3000 m に作り替える。
    ///
    /// ── 毎フレームの仕事量 ───────────────────────────────────
    ///
    /// <c>RenderEffect</c> は <c>SquallLayout.PatchCount</c>（9）回ちょうど、
    /// 新しく湧く粒子は <c>SquallLayout.ParticlesPerSecond</c>（900）個／秒、
    /// 生きている粒子は <c>SquallLayout.MaxParticles</c>（2400）で頭打ち。
    /// **ヒープ確保は 0 バイト。**
    ///
    /// ── 止まり方 ─────────────────────────────────────
    ///
    /// この型は**sim スレッドから 1 度も呼ばれない**（<see cref="TyphoonCloud"/> と同じ）。
    /// 台風が終わったフレームに <see cref="TyphoonFeature.OnMainThreadUpdate"/> が
    /// <c>Active == false</c> のスナップショットを渡し、ここが自分で出すのをやめる。
    /// 既に湧いた粒は寿命（最大 1.9 秒）で消える。
    /// <c>TyphoonController.Forget</c> の後始末列にこの型を足さないこと。
    /// </summary>
    public static class TyphoonSquallFx
    {
        private const int LookupRetryFrames = 600;

        private static GameObject _object;
        private static ParticleEffect _effect;
        private static ParticleSystem _particles;

        private static string _sourceName;
        private static string _sourceMaterial;

        private static int _lookupMissCount;
        private static TyphoonSquallState _state = TyphoonSquallState.Off;
        private static int _lastRenderCalls;
        private static float _lastStrength;

        private static bool _unavailableLogged;
        private static bool _errorLogged;

        public static TyphoonSquallState State { get { return _state; } }

        /// <summary>直近のフレームで出した <c>RenderEffect</c> の回数。</summary>
        public static int LastRenderCalls { get { return _lastRenderCalls; } }

        /// <summary>直近に測った吹き付けの強さ [0, 1]（カメラの居る場所の）。</summary>
        public static float LastStrength { get { return _lastStrength; } }

        /// <summary>診断に出す 1 行（**英語**）。</summary>
        public static string Detail
        {
            get
            {
                if (_effect == null)
                {
                    return "NONE (no vanilla water particle effect could be borrowed)";
                }
                return "cloned \"" + (_sourceName ?? "?") + "\" [material \""
                       + (_sourceMaterial ?? "?") + "\"], " + SquallLayout.PatchCount
                       + " patches/frame around the camera, "
                       + (int)SquallLayout.ParticlesPerSecond + " particles/s, cap "
                       + SquallLayout.MaxParticles;
            }
        }

        /// <summary>**main スレッド、毎フレーム。**</summary>
        public static void Update(TyphoonSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                _state = TyphoonSquallState.Failed;
                _lastRenderCalls = 0;
                _lastStrength = 0f;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon driving rain failed", e);
                }

                // 壊れた複製を抱えたまま毎フレーム投げ続けない。
                DestroyClone();
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active)
            {
                if (_effect != null) _state = TyphoonSquallState.Idle;
                _lastRenderCalls = 0;
                _lastStrength = 0f;
                return;
            }

            var camera = VanillaParticles.CameraInfo();
            if (camera == null)
            {
                // ★ null を RenderEffect へ渡すと先頭で NRE になる。**描かずに待つ。**
                _lastRenderCalls = 0;
                return;
            }

            Vector3 eye = camera.m_position;
            Vec3 centre = snapshot.Centre;
            float dx = eye.x - centre.X;
            float dz = eye.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            float wind = TyphoonProfile.WindAt(distance, snapshot.Intensity,
                                               snapshot.Prefab.StormRadius);
            float strength = SquallLayout.StrengthOf(wind);
            _lastStrength = strength;

            if (!(strength > 0f))
            {
                // カメラが強風域の外に居る。**不具合ではない。**
                _lastRenderCalls = 0;
                if (_effect != null) _state = TyphoonSquallState.OutsideStorm;
                return;
            }

            // ★ 参照そのものを毎フレーム見る。破棄済みなら fake-null で null と
            //   等価になり、ここで作り直される（2 つ目の都市の自己修復）。
            if (_effect == null && !Acquire()) return;

            float timeDelta = VanillaParticles.TimeDelta();
            if (!(timeDelta > 0f))
            {
                // ポーズ中・速度 0。粒は湧かないが、既に湧いた粒は漂う。
                _lastRenderCalls = 0;
                _state = TyphoonSquallState.Emitting;
                return;
            }

            Emit(snapshot, eye, dx, dz, strength, timeDelta, camera);
        }

        private static void Emit(TyphoonSnapshot snapshot, Vector3 eye, float dx, float dz,
                                 float strength, float timeDelta,
                                 RenderManager.CameraInfo camera)
        {
            // 風は台風の二次循環（接線＋吸い込み）。**渦の回る向きと同じ。**
            float wx, wz;
            SquallLayout.WindDirection(dx, dz, out wx, out wz);

            float drift = SquallLayout.DriftMetresPerSecond * strength;
            var velocity = new Vector3(wx * drift, 0f, wz * drift);

            InstanceID id = InstanceID.Empty;
            id.Disaster = snapshot.TyphoonId != 0 ? snapshot.TyphoonId : (ushort)1;

            // ★★ **地面から撒く。** 最初はカメラの高さを基準にしていたが、
            //   それだと飛沫が空中の板になって地面に届かなかった
            //   （tools/TyphoonPreview の squall 画像で見つけた）。
            //   <c>SampleDetailHeight</c> は読み取り専用でどちらのスレッドからも安全
            //   （TerrainHeightSampler のクラス doc）。1 フレームに 1 回だけ引く。
            float ground = TerrainHeightSampler.Instance.SampleHeight(eye.x, eye.z);
            if (float.IsNaN(ground))
            {
                // 地形が読めない。**推測した高さで空中に撒かない。**
                _lastRenderCalls = 0;
                return;
            }

            // カメラが高いほど広く撒く（でないと画面の真ん中に小さな染みが出るだけ）。
            float spread = SquallLayout.SpreadFor(eye.y - ground);

            int calls = 0;
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                SquallPatch patch = SquallLayout.PatchAt(i);

                var position = new Vector3(
                    eye.x + patch.OffsetXFraction * spread,
                    ground + patch.HeightFraction * SquallLayout.HeightMetres,
                    eye.z + patch.OffsetZFraction * spread);

                float disc = patch.DiscFraction * spread;
                if (!(disc > 0f)) continue;

                float band = patch.BandFraction * SquallLayout.HeightMetres;

                float magnitude = ParticleBudget.MagnitudeFor(
                    disc, SquallLayout.RateOverTime,
                    SquallLayout.ParticlesPerSecond * strength, SquallLayout.PatchCount);
                if (!(magnitude > 0f)) continue;

                var area = new EffectInfo.SpawnArea(position, Vector3.up, disc, band);

                // timeOffset = -1f ＝ **継続モード**（§B-3）。
                _effect.RenderEffect(id, area, velocity, 0f,
                                     magnitude * patch.DensityFraction,
                                     -1f, timeDelta, camera);
                calls++;
            }

            _lastRenderCalls = calls;
            _state = calls > 0 ? TyphoonSquallState.Emitting : TyphoonSquallState.NoEffect;
        }

        /// <summary>借りて、複製して、初期化する。取れなければ false（**例外は投げない**）。</summary>
        private static bool Acquire()
        {
            if (_lookupMissCount > 0)
            {
                _lookupMissCount--;
                return false;
            }

            string name;
            string material;
            ParticleEffect source = VanillaParticles.Pick(SprayMaterials, SprayNames,
                                                          out name, out material);
            if (source == null)
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonSquallState.NoEffect;

                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    // ★ **Warn ではなく Info。** 飛沫が出なくても雨は降るし、
                    //   台風の他の要素は 1 つも止まらない。
                    Log.Info("typhoon driving rain: this build exposes no borrowable water "
                             + "particle effect, so the storm has no wind-driven spray. "
                             + "The rain, the wind damage and the vortex are unaffected.");
                }
                return false;
            }

            _object = Clone(source);
            if (_object == null)
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonSquallState.NoEffect;
                return false;
            }

            _effect = _object.GetComponent<ParticleEffect>();
            _particles = _object.GetComponent<ParticleSystem>();
            if (_effect == null)
            {
                DestroyClone();
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonSquallState.NoEffect;
                return false;
            }

            _sourceName = name;
            _sourceMaterial = material;

            Log.Info("typhoon driving rain: borrowed \"" + name + "\" (particle material \""
                     + material + "\") for the wind-driven spray: " + SquallLayout.PatchCount
                     + " patches/frame, " + (int)SquallLayout.ParticlesPerSecond
                     + " particles/s, cap " + SquallLayout.MaxParticles);
            return true;
        }

        /// <summary>
        /// 望む粒子マテリアル（良い順）。**<c>Water</c> が本命** ——
        /// 出荷アセットの <c>water</c> テクスチャは平均 RGB (201, 222, 254) の
        /// 白青の飛沫である。<c>Steam</c> は雲に見えてしまうので次点。
        /// </summary>
        private static readonly string[] SprayMaterials =
        {
            "Water", "Snow", "Steam", "Placement Dust",
        };

        /// <summary>同点のときの並べ替えにだけ使う名前の順。**引く順ではない。**</summary>
        private static readonly string[] SprayNames =
        {
            "Fire Copter Water Particles",
            "Fireman Water",
            "Snowplow Particles",
        };

        /// <summary>
        /// 複製して横殴りの飛沫に仕立てる。**共有状態は 1 バイトも触らない**（§D-5）——
        /// 借り元をそのまま書き換えると、街じゅうの消防車の放水が嵐色になる。
        /// 数値表は <see cref="SquallLayout"/>（Core）に在るので**ここに直書きしない**。
        /// </summary>
        private static GameObject Clone(ParticleEffect source)
        {
            GameObject go = VanillaParticles.Clone(source, "DisasterPlus_TyphoonSquall");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var ps = go.GetComponent<ParticleSystem>();
            if (effect == null || ps == null) return VanillaParticles.Reject(go);

            effect.m_maxVisibilityDistance = SquallLayout.VisibilityMetres;
            effect.m_minLifeTime = SquallLayout.LifeMinSeconds;
            effect.m_maxLifeTime = SquallLayout.LifeMaxSeconds;
            effect.m_minStartSpeed = SquallLayout.SpeedMin;
            effect.m_maxStartSpeed = SquallLayout.SpeedMax;
            effect.m_minSpawnAngle = SquallLayout.SpawnAngleMinDegrees;
            effect.m_maxSpawnAngle = SquallLayout.SpawnAngleMaxDegrees;
            effect.m_renderDuration = 0f;      // 継続モードで使う（§B-3）
            effect.m_extraRadius = 0f;

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(SquallLayout.BrightRed, SquallLayout.BrightGreen,
                          SquallLayout.BrightBlue, SquallLayout.Alpha),
                new Color(SquallLayout.DarkRed, SquallLayout.DarkGreen,
                          SquallLayout.DarkBlue, SquallLayout.Alpha));
            main.startSize = SquallLayout.SizeMetres;
            main.gravityModifier = SquallLayout.GravityModifier;
            main.maxParticles = SquallLayout.MaxParticles;

            var emission = ps.emission;
            emission.rateOverTime = SquallLayout.RateOverTime;

            return VanillaParticles.Initialize(go, effect);
        }

        /// <summary>
        /// **レベルアンロードと、設定で切ったときに呼ぶ。** main スレッド専用。冪等。
        /// </summary>
        public static void Destroy()
        {
            DestroyClone();
            _lookupMissCount = 0;
            _lastRenderCalls = 0;
            _lastStrength = 0f;
            _state = TyphoonSquallState.Off;
            // ★ _unavailableLogged / _errorLogged は戻さない（ゲームのビルドに対する事実）。
        }

        private static void DestroyClone()
        {
            VanillaParticles.Release(ref _object, ref _effect, ref _particles);
            _sourceName = null;
            _sourceMaterial = null;
        }
    }
}
