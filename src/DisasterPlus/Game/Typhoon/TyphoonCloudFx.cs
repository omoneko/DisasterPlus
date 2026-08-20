using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>雲の粒（パフ）で組んだ渦が今どうなっているか。</summary>
    public enum TyphoonCloudFxState
    {
        /// <summary>まだ 1 度も試していない（あるいは都市に入っていない）。</summary>
        Off,

        /// <summary>借りられる粒子エフェクトがこの環境に 1 つも無い。**メッシュへ退避する。**</summary>
        NoEffect,

        /// <summary>台風が居ないので出していない。**不具合ではない。**</summary>
        Idle,

        /// <summary>毎フレーム粒を出している。</summary>
        Emitting,

        /// <summary>例外で落ちた。**メッシュへ退避する。**</summary>
        Failed,
    }

    /// <summary>
    /// 台風の渦を**バニラの雲（粒子エフェクト）で組む**。<b>main スレッド専用。</b>
    ///
    /// ── 持ち主の指摘と、それがなぜ正しいか ──────────────────────────
    ///
    /// > 現在の巨大な渦を雲から構成するようにして
    ///
    /// 旧実装（<see cref="TyphoonCloud"/> のメッシュ経路）は**自前メッシュ 1 枚 ＋
    /// 自前マテリアル**で、2 つの問題があった:
    ///
    /// 1. マテリアルにはシェーダが要るが、**実機の <c>Shader.Find</c> は
    ///    <c>"Standard"</c> を含めて全ての名前に null を返した**（本プロジェクト
    ///    13 回目の誤った「確認済み」）。<c>ShaderPool</c> は読み込み済みマテリアルから
    ///    シェーダだけを借りる回避策だが**まだ 1 度も実機で通っていない**。
    /// 2. 通ったとしても、④の全体レビューが「半径 900 m の平らな渦巻き＝
    ///    渦の記号であって空を覆う雲ではない」と結論していた。
    ///
    /// バニラの粒子エフェクトは<b>既に読み込まれ、既に動くマテリアルを持っている</b>。
    /// しかもそのマテリアルは <c>ParticleSystemRenderer</c> に付くので、
    /// 「CS のマテリアルを借りると自前 <c>MeshRenderer</c> で不可視になる」問題には
    /// **当たらない**（エフェクト実測文書 §D-3。バニラ自身が
    /// <c>EffectsWrapper.CreateParticleEffect</c> で同じことをしている）。
    ///
    /// ── どう組むか ────────────────────────────────────────
    ///
    /// <see cref="VortexPuffLayout"/> が置き場所（腕 3 本 ＋ 壁雲の環）を正規化した比で
    /// 出し、ここがそれを台風の座標・半径・回転へ直して
    /// <c>ParticleEffect.RenderEffect(..., timeOffset: -1f, ...)</c> を
    /// <see cref="VortexPuffLayout.PuffCount"/> 回呼ぶ。<c>timeOffset</c> が負なので
    /// **継続モード**になり、<c>m_renderDuration</c> も <c>m_intensityCurve</c> も
    /// 無視されて「毎フレーム湧かし続ける」になる（§B-3。バニラの
    /// <c>SinkholeAI.RenderInstance</c> と同じ形）。
    ///
    /// **眼は穴のまま。** <see cref="VortexPuffLayout"/> が
    /// <c>EyeFraction</c> より内側に 1 個も置かないことを保証し、テストが固定している。
    ///
    /// ── 毎フレームの仕事量の上限（3 本で決まる）──────────────────────
    ///
    /// 1. <c>RenderEffect</c> は <b><see cref="VortexPuffLayout.PuffCount"/> 回</b>ちょうど。
    /// 2. 新しく湧く粒子は <b><see cref="ParticlesPerSecond"/> 個／秒</b>
    ///    （<see cref="VortexPuffLayout.MagnitudeFor"/> が §B-4 の式を逆に解いて
    ///    <c>magnitude</c> を決める。フレームレートにもゲーム速度にも依らない）。
    /// 3. 生きている粒子の総数は <b><see cref="MaxParticles"/></b> で頭打ち ——
    ///    バニラ自身が <c>pps ×= (1 - fill²)</c> で絞り込む（§B-4）。
    ///
    /// <b>ヒープ確保は 0 バイト。</b><c>SpawnArea</c> / <c>InstanceID</c> /
    /// <c>Vector3</c> / <c>MainModule</c> はすべて struct で、
    /// <c>RenderEffect</c> の内側にも確保は無い（§E）。
    ///
    /// ── クローンする理由と、その副作用 ───────────────────────────
    ///
    /// <c>ParticleSystem</c> の色・粒径・寿命・可視距離は**エフェクトの共有状態**である。
    /// <c>Factory Smoke</c> をそのまま書き換えると**街じゅうの工場の煙**が嵐雲色になり、
    /// セーブではなくメモリ上に残る（§D-5）。だから
    /// <c>Object.Instantiate</c> でクローンしてから触る。
    ///
    /// クローンした <c>GameObject</c> は**アクティブなシーンに入る**ので、そのままだと
    /// 自分の <c>ParticleSystem</c> が原点で煙を吐く。<see cref="BuildClone"/> は
    /// <c>emission.enabled = false</c> にしてからでないと <c>InitializeEffect()</c> を
    /// 呼ばない（<c>ParticleEffect.CreateEffect</c> は**その状態をもう 1 段クローンする**ので、
    /// 内側にも同じ設定が渡る。IL 実測）。<c>playOnAwake</c> は**触らない** ——
    /// 内側のクローンは <c>ParticleEffect.Update</c> が <c>isPaused</c> を見て
    /// <c>Play()</c> し直す作りなので、止めた状態を配ると粒子が動かなくなる。
    ///
    /// ── 取れなければ静かに諦める ──────────────────────────────
    ///
    /// <c>FindEffect</c> / <c>GetBuiltinEffect</c> は**null を返しうる**（未登録・
    /// ゲーム更新・別 MOD）。返ったら <see cref="TyphoonCloudFxState.NoEffect"/> にして
    /// **ログ 1 行**を出し、例外は投げない。呼び出し側（<see cref="TyphoonCloud"/>）は
    /// 旧メッシュ経路へ退避する。台風の他の要素は 1 つも止まらない。
    /// </summary>
    public static class TyphoonCloudFx
    {
        /// <summary>渦全体で 1 秒あたりに湧かす粒子の数。**④が選んだ演出値。**</summary>
        private const float ParticlesPerSecond = 620f;

        /// <summary>クローン側の粒子の上限。生きている粒子の総数はここで頭打ちになる。</summary>
        private const int MaxParticles = 7000;

        /// <summary>クローン側に固定する <c>emission.rateOverTime</c>。
        /// **0 にしてはいけない**（0 だと粒子が 1 個も出ない。§D-2 の罠）。
        /// 借りた素材ごとに違う値（15〜200）が入っているので、ここで揃えて
        /// <see cref="VortexPuffLayout.MagnitudeFor"/> の入力を確定させる。</summary>
        private const float RateOverTime = 20f;

        /// <summary>1 粒の円盤半径 ÷ 渦の外周半径（<see cref="VortexPuffLayout"/> の
        /// size 比で腕の外側ほど広がる）。</summary>
        private const float DiscFraction = 0.11f;

        /// <summary>粒径 ÷ 渦の外周半径。</summary>
        private const float SizeFraction = 0.16f;

        /// <summary>粒径の下限・上限（m）。小さすぎると点、大きすぎると板に見える。</summary>
        private const float MinSizeMetres = 45f;

        private const float MaxSizeMetres = 420f;

        /// <summary>雲の厚み（m）。<c>heightFraction</c> がこの中のどこに置くかを決める。</summary>
        private const float ThicknessMetres = 320f;

        /// <summary>粒が渦に沿って流れる速さ（m/s）。**④が選んだ演出値。**</summary>
        private const float SwirlSpeed = 26f;

        /// <summary>寿命（秒）。長いほど空が埋まるが、<see cref="MaxParticles"/> が上限を握る。</summary>
        private const float MinLifeTime = 7f;

        private const float MaxLifeTime = 17f;

        /// <summary>遠景から見えること（借り元は 500〜1000 m しかない）。</summary>
        private const float VisibilityMetres = 12000f;

        /// <summary>エフェクトを探し直すまでに空けるフレーム数。
        /// <c>FindEffect</c> は失敗すると <c>CODebugBase.Warn</c> を出すので毎フレームは引かない。</summary>
        private const int LookupRetryFrames = 600;

        /// <summary>
        /// 借りる候補。**上から順に試し、最初に取れたものを使う。**
        /// どれも基本ゲーム（DLC 不要）で、灰色〜白の粒子である
        /// （エフェクト実測文書 §A-6）。<c>Factory Smoke</c> は
        /// <c>EffectCollection</c> に**登録されていない**ので
        /// <c>GetBuiltinEffect</c> でしか取れない —— だから
        /// <see cref="Lookup"/> は 2 経路とも試す。
        /// </summary>
        private static readonly string[] CandidateNames =
        {
            "Factory Smoke",
            "Factory Steam",
            "Large Pool Steam",
            "Pool Steam",
            "Collapse Particles",
            "Factory Smoke Small",
        };

        // ★ 参照 1 個ずつで持つ（配列にしない）。UnityEngine.Object の == は
        //   破棄済みを null と等価に見せるが、**配列参照の比較にはそれが効かない** ——
        //   static な配列は破棄済みの中身を抱えたまま非 null であり続け、
        //   2 つ目の都市で無言のまま見えなくなる（③火災旋風 §4.8）。
        private static GameObject _cloneObject;
        private static ParticleEffect _effect;

        private static int _lookupMissCount;
        private static TyphoonCloudFxState _state = TyphoonCloudFxState.Off;
        private static int _lastRenderCalls;
        private static string _sourceName;

        /// <summary>借りられないことを 1 度だけ名乗ったか。**<see cref="Destroy"/> で戻さない**
        /// （ゲームのビルドに対する事実であって都市ごとの状態ではない）。</summary>
        private static bool _unavailableLogged;

        private static bool _errorLogged;

        public static TyphoonCloudFxState State { get { return _state; } }

        /// <summary>直近のフレームで出した <c>RenderEffect</c> の回数。</summary>
        public static int LastRenderCalls { get { return _lastRenderCalls; } }

        /// <summary>診断に出す 1 行（**英語**）。</summary>
        public static string EffectDetail
        {
            get
            {
                if (_effect == null)
                {
                    return "NONE (no vanilla particle effect could be borrowed)";
                }
                return "cloned \"" + (_sourceName ?? "?") + "\" ("
                       + VortexPuffLayout.PuffCount + " puffs/frame, "
                       + (int)ParticlesPerSecond + " particles/s, cap " + MaxParticles + ")";
            }
        }

        /// <summary>
        /// **借りられるかだけを見る、副作用の無い問い合わせ。**
        /// <c>Assumptions</c> がここを呼ぶ —— 検証の述語は
        /// <b>この機能が実際に門にしている式でなければならない</b>ので、
        /// あちらに候補の並びを書き写さず、<see cref="Lookup"/> そのものを共有する。
        /// クローンも作らないし、間引きのカウンタにも触らない。
        /// main スレッド専用（<c>Assumptions.Run</c> 自体が main スレッド専用）。
        /// </summary>
        public static bool CanBorrow(out string name)
        {
            try
            {
                return Lookup(out name) != null;
            }
            catch
            {
                name = null;
                return false;
            }
        }

        /// <summary>
        /// **main スレッド、毎フレーム。** 描けたら true。
        /// false のとき呼び出し側は旧メッシュ経路へ退避する。
        ///
        /// <paramref name="spinDegrees"/> は <see cref="TyphoonCloud"/> が持っている
        /// 回転角（ポーズ中は進まない）。ここで別に数えると、退避経路と本経路で
        /// 渦の向きが食い違う。
        /// </summary>
        public static bool Update(TyphoonSnapshot snapshot, float spinDegrees)
        {
            try
            {
                return Step(snapshot, spinDegrees);
            }
            catch (System.Exception e)
            {
                _state = TyphoonCloudFxState.Failed;
                _lastRenderCalls = 0;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon cloud puffs failed", e);
                }

                // 壊れたクローンを抱えたまま毎フレーム投げ続けない。
                DestroyClone();
                return false;
            }
        }

        private static bool Step(TyphoonSnapshot snapshot, float spinDegrees)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active)
            {
                if (_effect != null) _state = TyphoonCloudFxState.Idle;
                _lastRenderCalls = 0;
                return false;
            }

            float radius = snapshot.GaleRadius;
            if (!(radius > 0f))
            {
                _lastRenderCalls = 0;
                return false;
            }

            // ★ 参照そのものを毎フレーム見る。破棄済みなら fake-null で null と
            //   等価になり、ここで作り直される（2 つ目の都市の自己修復）。
            if (_effect == null && !Acquire()) return false;

            var camera = CurrentCamera();
            if (camera == null)
            {
                // ★ null を渡すと ParticleEffect.RenderEffect の先頭で NRE になる
                //   （CheckRenderDistance / Intersect）。**描かずに黙って待つ。**
                _lastRenderCalls = 0;
                return true;   // エフェクトは持っている。メッシュへ退避させない。
            }

            float timeDelta = SimulationTimeDelta();
            if (!(timeDelta > 0f))
            {
                // ポーズ中・速度 0。粒は湧かないが渦は残る（既存の粒子が漂う）。
                _lastRenderCalls = 0;
                _state = TyphoonCloudFxState.Emitting;
                return true;
            }

            Emit(snapshot, radius, spinDegrees, timeDelta, camera);
            return true;
        }

        private static void Emit(TyphoonSnapshot snapshot, float radius, float spinDegrees,
                                 float timeDelta, RenderManager.CameraInfo camera)
        {
            Vec3 centre = snapshot.Centre;
            float altitude = centre.Y + MinClearanceMetres;
            if (altitude < BaseAltitudeMetres) altitude = BaseAltitudeMetres;

            if (!(radius * DiscFraction > 0f))
            {
                _lastRenderCalls = 0;
                return;
            }

            // 粒径は渦の大きさに合わせる。**共有状態ではなくクローン側**なので
            // 毎フレーム書いてよい（MainModule は struct、確保は 0 バイト）。
            ApplySize(radius);

            // ★ default(InstanceID) の RawData は 0 で、Randomizer の種が 0 に固定される
            //   （§B-6）。ParticleEffect 直呼びなら probability = 100 固定なので実害は
            //   無いが、非 0 を入れておくのが作法である。
            InstanceID id = InstanceID.Empty;
            id.Disaster = snapshot.TyphoonId != 0 ? snapshot.TyphoonId : (ushort)1;

            float spin = spinDegrees * 0.0174532925f;
            int calls = 0;

            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float angle, radiusFraction, heightFraction, sizeFraction, densityFraction;
                VortexPuffLayout.Puff(i, out angle, out radiusFraction,
                                      out heightFraction, out sizeFraction, out densityFraction);

                float a = angle + spin;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                float r = radiusFraction * radius;

                var position = new Vector3(centre.X + cos * r,
                                           altitude + heightFraction * ThicknessMetres,
                                           centre.Z + sin * r);

                // 接線方向へ流す。**渦の回り方と同じ向き**（spin と符号を合わせる）。
                var velocity = new Vector3(-sin * SwirlSpeed, 0f, cos * SwirlSpeed);

                // 腕の外側ほど円盤を広げる（渦の腕が末広がりになる）。
                float discRadius = radius * DiscFraction * (0.55f + 0.9f * sizeFraction);

                // ★ magnitude は円盤ごとに解き直す。§B-4 の式は面積で効くので、
                //   円盤を広げたぶんだけ密度を下げないと外側だけ濃くなる。
                float magnitude = VortexPuffLayout.MagnitudeFor(discRadius, RateOverTime,
                                                                ParticlesPerSecond,
                                                                VortexPuffLayout.PuffCount);
                if (!(magnitude > 0f)) continue;

                // SpawnArea(pos, dir, radius) は必ず「点/円盤」経路に落ちる（§B-2）。
                var area = new EffectInfo.SpawnArea(position, Vector3.up, discRadius);

                // timeOffset = -1f ＝ **継続モード**（§B-3）。
                _effect.RenderEffect(id, area, velocity, 0f,
                                     magnitude * densityFraction,
                                     -1f, timeDelta, camera);
                calls++;
            }

            _lastRenderCalls = calls;
            _state = TyphoonCloudFxState.Emitting;
        }

        /// <summary>雲の基準高度（m）。旧メッシュ経路と同じ値にそろえてある。</summary>
        private const float BaseAltitudeMetres = 900f;

        /// <summary>山岳マップで山に埋まらないための、中心の地形高からの最低クリアランス（m）。</summary>
        private const float MinClearanceMetres = 300f;

        private static void ApplySize(float radius)
        {
            var ps = _cloneObject != null ? _cloneObject.GetComponent<ParticleSystem>() : null;
            if (ps == null) return;

            float size = radius * SizeFraction;
            if (float.IsNaN(size)) return;
            if (size < MinSizeMetres) size = MinSizeMetres;
            if (size > MaxSizeMetres) size = MaxSizeMetres;

            var main = ps.main;
            main.startSize = size;
        }

        // ── 借りる ────────────────────────────────────────────

        /// <summary>
        /// 借りて、クローンして、初期化する。取れなければ false（**例外は投げない**）。
        /// </summary>
        private static bool Acquire()
        {
            // ★ 毎フレーム探しに行かない。FindEffect は失敗すると CODebugBase.Warn を
            //   出すので、実機のログが埋まる。
            if (_lookupMissCount > 0)
            {
                _lookupMissCount--;
                return false;
            }

            string name;
            ParticleEffect source = Lookup(out name);
            if (source == null)
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonCloudFxState.NoEffect;

                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    Log.Warn("typhoon cloud: no vanilla particle effect could be borrowed for "
                             + "the vortex (tried Factory Smoke / Factory Steam / Large Pool "
                             + "Steam / Pool Steam / Collapse Particles); falling back to the "
                             + "mod's own spiral mesh. Everything else about the typhoon is "
                             + "unaffected.");
                }
                return false;
            }

            if (!BuildClone(source, name))
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonCloudFxState.NoEffect;
                return false;
            }

            Log.Info("typhoon cloud: borrowed \"" + name + "\" for the vortex ("
                     + VortexPuffLayout.PuffCount + " puffs/frame, "
                     + (int)ParticlesPerSecond + " particles/s, cap " + MaxParticles + ")");
            return true;
        }

        /// <summary>
        /// 名前で 1 つ引く。**2 経路とも試す** —— <c>EffectCollection</c> に登録されて
        /// いるのは 186 個だけで、<c>Factory Smoke</c> はそこに**入っていない**
        /// （§A-3 の未登録 17 個）。最後にゲーム自身が握っている
        /// <c>BuildingProperties</c> のエフェクトから拾う。
        /// </summary>
        private static ParticleEffect Lookup(out string name)
        {
            name = null;

            for (int i = 0; i < CandidateNames.Length; i++)
            {
                ParticleEffect e = Extract(FromWrapper(CandidateNames[i]))
                                   ?? Extract(FromCollection(CandidateNames[i]));
                if (e != null)
                {
                    name = CandidateNames[i];
                    return e;
                }
            }

            // 最後の手段: ゲーム自身が握っている崩壊の粉塵（灰色）。
            ParticleEffect fallback = Extract(BuildingCollapseEffect());
            if (fallback != null)
            {
                name = "BuildingProperties.m_collapseEffect";
                return fallback;
            }

            return null;
        }

        private static EffectInfo FromWrapper(string name)
        {
            try
            {
                if (!Singleton<EffectManager>.exists) return null;
                var wrapper = Singleton<EffectManager>.instance.m_EffectsWrapper;
                if (wrapper == null) return null;
                return wrapper.GetBuiltinEffect(name) as EffectInfo;
            }
            catch
            {
                return null;
            }
        }

        private static EffectInfo FromCollection(string name)
        {
            try
            {
                return EffectCollection.FindEffect(name);
            }
            catch
            {
                return null;
            }
        }

        private static EffectInfo BuildingCollapseEffect()
        {
            try
            {
                if (!Singleton<BuildingManager>.exists) return null;
                var properties = Singleton<BuildingManager>.instance.m_properties;
                return properties != null ? properties.m_collapseEffect : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// <c>EffectInfo</c> から粒子を取り出す。<c>MultiEffect</c>（＝
        /// <c>Collapse Effect</c> のように粒子と音の束）と <c>FireEffect</c> は
        /// 中に <c>ParticleEffect</c> を抱えている（§B-7）。
        /// **音のほうを掴まないこと** —— <c>SoundEffect.RenderEffect</c> は
        /// override されておらず、呼んでも何も起きない。
        /// </summary>
        private static ParticleEffect Extract(EffectInfo info)
        {
            if (info == null) return null;

            var direct = info as ParticleEffect;
            if (direct != null) return direct;

            var fire = info as FireEffect;
            if (fire != null) return fire.m_particleEffect;

            var multi = info as MultiEffect;
            if (multi != null && multi.m_effects != null)
            {
                for (int i = 0; i < multi.m_effects.Length; i++)
                {
                    var child = multi.m_effects[i].m_effect as ParticleEffect;
                    if (child != null) return child;
                }
            }

            return null;
        }

        /// <summary>
        /// クローンして嵐雲に仕立てる。**共有状態は 1 バイトも触らない**（§D-5）。
        /// </summary>
        private static bool BuildClone(ParticleEffect source, string name)
        {
            var sourceObject = source.gameObject;
            if (sourceObject == null) return false;

            var clone = Object.Instantiate(sourceObject) as GameObject;
            if (clone == null) return false;

            clone.name = "DisasterPlus_TyphoonVortexCloud";
            Object.DontDestroyOnLoad(clone);

            var effect = clone.GetComponent<ParticleEffect>();
            var ps = clone.GetComponent<ParticleSystem>();
            if (effect == null || ps == null)
            {
                Object.Destroy(clone);
                return false;
            }

            // ★★ **InitializeEffect の前に**外側の放出を止める。止めないと、この
            //    GameObject 自身がワールド原点で煙を吐き続ける（クラス doc）。
            //    playOnAwake は触らない（内側のクローンが Play できなくなる）。
            var emission = ps.emission;
            emission.enabled = false;
            emission.rateOverTime = RateOverTime;

            var main = ps.main;
            main.startColor = new Color(0.60f, 0.62f, 0.66f, 0.62f);
            main.startSize = MinSizeMetres;
            main.gravityModifier = -0.02f;
            main.maxParticles = MaxParticles;

            effect.m_maxVisibilityDistance = VisibilityMetres;
            effect.m_minLifeTime = MinLifeTime;
            effect.m_maxLifeTime = MaxLifeTime;
            effect.m_minStartSpeed = 2f;
            effect.m_maxStartSpeed = 9f;
            effect.m_minSpawnAngle = 0f;
            effect.m_maxSpawnAngle = 90f;
            effect.m_renderDuration = 0f;      // 継続モードで使う（§B-3）
            effect.m_extraRadius = 0f;         // 借り元によっては 2〜9 m 勝手に足す

            effect.InitializeEffect();

            _cloneObject = clone;
            _effect = effect;
            _sourceName = name;
            return true;
        }

        private static RenderManager.CameraInfo CurrentCamera()
        {
            try
            {
                if (!Singleton<RenderManager>.exists) return null;
                return Singleton<RenderManager>.instance.CurrentCameraInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 粒子の進み方は <c>Time.deltaTime</c> ではなく
        /// <c>SimulationManager.m_simulationTimeDelta</c> で測る（§B-3）。
        /// ポーズと速度変更に追随し、<c>ParticleEffect.Update</c> が
        /// <c>m_useSimulationTime</c> で <c>Pause()</c> する挙動とも一致する。
        /// </summary>
        private static float SimulationTimeDelta()
        {
            try
            {
                if (!Singleton<SimulationManager>.exists) return 0f;
                return Singleton<SimulationManager>.instance.m_simulationTimeDelta;
            }
            catch
            {
                return 0f;
            }
        }

        // ── 後始末 ────────────────────────────────────────────

        /// <summary>
        /// **レベルアンロードと、設定で雲を切ったときに呼ぶ。** main スレッド専用。冪等。
        /// </summary>
        public static void Destroy()
        {
            DestroyClone();
            _lookupMissCount = 0;
            _lastRenderCalls = 0;
            _state = TyphoonCloudFxState.Off;
            // ★ _unavailableLogged / _errorLogged は戻さない（クラス doc）。
        }

        private static void DestroyClone()
        {
            if (_effect != null)
            {
                try
                {
                    // 内側のクローン（ParticleEffect.CreateEffect が作ったもの）を畳む。
                    _effect.ReleaseEffect();
                }
                catch
                {
                    // 畳めなくても外側は必ず消す。
                }
            }
            _effect = null;

            if (_cloneObject != null) Object.Destroy(_cloneObject);
            _cloneObject = null;
            _sourceName = null;
        }
    }
}
