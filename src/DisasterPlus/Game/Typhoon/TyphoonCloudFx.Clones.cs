using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="TyphoonCloudFx"/> のうち<b>「何を借りて、どこを変えるか」</b>。
    /// **main スレッド専用。**
    ///
    /// 撒き方は本体側にある。ここに在るのは<b>素材の選び方</b>と
    /// <b>複製 1 個ぶんの数値の書き込み</b>と、複製が実際に使える状態になったかの検査だけ。
    /// 分けてあるのは 800 行の上限のためだけではない ——
    /// 見た目を調整するときに読むのはこのファイルと <c>Core/Typhoon/VortexCloudProfile</c>
    /// だけで済む。
    ///
    /// ── ★ 素材はマテリアルで選ぶ（名前で選ばない）────────────────────────
    ///
    /// 理由と実測値は本体のクラス doc にある。要点だけ:
    /// <c>Smoke</c> のテクスチャは平均 RGB (75, 78, 80) の**煤の絵**で、
    /// <c>Steam</c> は (168, 184, 189) の**淡い青白の綿**である。
    /// <c>startColor</c> は乗算なので、素材が煤なら何色を掛けても煙にしか見えない。
    ///
    /// <see cref="Lookup"/> は在庫を<b>実際に列挙</b>して
    /// <c>ParticleSystemRenderer.sharedMaterial.name</c> を読み、
    /// <see cref="ScoreOf"/> で採点する。名前の一覧
    /// （<see cref="PreferredNames"/>）は**同点の並べ替えにしか使わない**。
    ///
    /// <c>sharedMaterial</c> を読むこと。<c>material</c> はレンダラのマテリアルを
    /// **複製して差し替える**ので、ゲームの共有状態を壊すうえリークする。
    ///
    /// ── ★ 複製する理由（§D-5）─────────────────────────────────
    ///
    /// <c>ParticleSystem</c> の色・粒径・寿命・可視距離は**エフェクトの共有状態**である。
    /// 借り元をそのまま書き換えると**街じゅうのプール・工場の蒸気**が嵐雲色になり、
    /// セーブではなくメモリ上に残る。だから <c>Object.Instantiate</c> してから触る。
    ///
    /// 複製した <c>GameObject</c> は**アクティブなシーンに入る**ので、そのままだと
    /// 自分の <c>ParticleSystem</c> が原点で蒸気を吐く。<see cref="CloneObject"/> は
    /// <c>emission.enabled = false</c> にしてから返す（<c>InitializeEffect()</c> は
    /// **その状態をもう 1 段複製する**ので内側にも同じ設定が渡る。IL 実測）。
    /// <c>playOnAwake</c> は**触らない** —— 内側の複製は <c>ParticleEffect.Update</c> が
    /// <c>isPaused</c> を見て <c>Play()</c> し直す作りなので、止めた状態を配ると
    /// 粒子が動かなくなる。
    /// </summary>
    public static partial class TyphoonCloudFx
    {
        /// <summary>複製側に固定する <c>emission.rateOverTime</c>。
        /// **0 にしてはいけない**（0 だと粒子が 1 個も出ない。§D-2 の罠）。
        /// 借りた素材ごとに違う値（9.67〜200）が入っているので、ここで揃えて
        /// <see cref="VortexPuffLayout.MagnitudeFor"/> の入力を確定させる。</summary>
        private const float RateOverTime = 20f;

        /// <summary>
        /// マテリアル名の採点。**大きいほど雲らしい。** 0 は「使わない」。
        ///
        /// 加算合成（<c>Custom/Particles/Additive (Soft)</c>）の <c>Fire</c> /
        /// <c>Explosion</c> / <c>FireRocket</c> / <c>RocketLaunch</c> /
        /// <c>Fireworks*</c> は**光る**ので雲には使えない。0 を返して除外する。
        /// </summary>
        private static int ScoreOf(string material)
        {
            if (string.IsNullOrEmpty(material)) return 0;

            // ★ 加算合成の光り物は問答無用で除外する（空が燃える）。
            if (material == "Fire" || material == "FireRocket" || material == "Explosion"
                || material == "RocketLaunch" || material == "SmokeRocket"
                || material.StartsWith("Fireworks")) return 0;

            if (material == "Steam") return 100;          // 淡い青白の綿 ＝ 雲
            if (material == "Water") return 60;           // 白青の飛沫。次点
            if (material == "Snow") return 45;
            if (material == "Placement Dust") return 35;  // 砂色の土煙
            if (material == "IndustryDust") return 25;
            if (material == "Smoke") return 15;           // ★ 煤。**最後の手段**
            if (material == "Ship Trail") return 10;
            return 5;                                     // 見たことのないマテリアル
        }

        /// <summary>
        /// 同点のときの並べ替えにだけ使う名前の順。**引く順ではない。**
        /// <c>Large Pool Steam</c> を先頭にしてあるのは、素の初速が 0.1〜0.2 m/s で
        /// いちばん「動かない大きな雲」に近いからである（§A-6）。
        /// </summary>
        private static readonly string[] PreferredNames =
        {
            "Large Pool Steam",
            "Pool Steam",
            "Factory Steam",
            "Fire Copter Water Particles",
            "Collapse Particles",
        };

        // ★ 参照 1 個ずつで持つ（配列にしない）。UnityEngine.Object の == は
        //   破棄済みを null と等価に見せるが、**配列参照の比較にはそれが効かない** ——
        //   static な配列は破棄済みの中身を抱えたまま非 null であり続け、
        //   2 つ目の都市で無言のまま見えなくなる（③火災旋風 §4.8）。
        private static GameObject _deckObject;
        private static ParticleEffect _deckEffect;
        private static ParticleSystem _deckParticles;

        private static GameObject _towerObject;
        private static ParticleEffect _towerEffect;
        private static ParticleSystem _towerParticles;

        private static GameObject _canopyObject;
        private static ParticleEffect _canopyEffect;
        private static ParticleSystem _canopyParticles;

        private static string _sourceName;
        private static string _sourceMaterial;

        /// <summary>借りられないことを 1 度だけ名乗ったか。**<see cref="Destroy"/> で戻さない**
        /// （ゲームのビルドに対する事実であって都市ごとの状態ではない）。</summary>
        private static bool _unavailableLogged;

        /// <summary>
        /// <c>ParticleEffect.m_particleSystem</c>（<c>[NonSerialized]</c> の private）。
        /// **初期化されていない複製を <c>RenderEffect</c> へ渡すと
        /// <c>EmitParticles</c> の中で NRE になる**ので、これで確かめる（⑤と同じ）。
        /// </summary>
        private static readonly System.Reflection.FieldInfo ParticleSystemField =
            typeof(ParticleEffect).GetField("m_particleSystem",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

        internal static string SourceName { get { return _sourceName; } }

        internal static string SourceMaterial { get { return _sourceMaterial; } }

        /// <summary>層に対応する複製。**参照そのものを見る**（fake-null の自己修復）。</summary>
        private static ParticleEffect CloneFor(VortexCloudLayer layer)
        {
            if (layer == VortexCloudLayer.Deck) return _deckEffect != null ? _deckEffect : null;
            if (layer == VortexCloudLayer.Canopy)
            {
                // ★ かなとこが作れなければ塔の複製で代用する（天蓋が塔の色になるだけで、
                //   消えはしない）。⑤の傘と同じ判断。
                if (_canopyEffect != null) return _canopyEffect;
                return _towerEffect != null ? _towerEffect : null;
            }
            return _towerEffect != null ? _towerEffect : null;
        }

        private static bool AnyClone()
        {
            return _deckEffect != null || _towerEffect != null || _canopyEffect != null;
        }

        private static int CloneCount()
        {
            int n = 0;
            if (_deckEffect != null) n++;
            if (_towerEffect != null) n++;
            if (_canopyEffect != null) n++;
            return n;
        }

        private static int TotalParticleCap()
        {
            int cap = 0;
            if (_deckEffect != null) cap += VortexCloudProfile.Deck.MaxParticles;
            if (_towerEffect != null) cap += VortexCloudProfile.Tower.MaxParticles;
            if (_canopyEffect != null) cap += VortexCloudProfile.Canopy.MaxParticles;
            return cap;
        }

        /// <summary>
        /// 借りて、3 つ複製して、初期化する。1 つも作れなければ false（**例外は投げない**）。
        /// </summary>
        private static bool Acquire()
        {
            // ★ 毎フレーム探しに行かない。列挙は辞書 1 周ぶんの費用がある。
            if (_lookupMissCount > 0)
            {
                _lookupMissCount--;
                return false;
            }

            string name;
            string material;
            ParticleEffect source = Lookup(out name, out material);
            if (source == null)
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonCloudFxState.NoEffect;

                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    Log.Warn("typhoon cloud: this build exposes no borrowable particle effect "
                             + "for the vortex (the mod enumerated the game's builtin effects "
                             + "and its effect collection and found none with a usable "
                             + "particle material); falling back to the mod's own spiral mesh. "
                             + "Everything else about the typhoon is unaffected.");
                }
                return false;
            }

            _sourceName = name;
            _sourceMaterial = material;

            _deckObject = BuildClone(source, VortexCloudLayer.Deck, "DisasterPlus_TyphoonDeck");
            _deckEffect = ComponentOf(_deckObject, out _deckParticles);

            _towerObject = BuildClone(source, VortexCloudLayer.Tower, "DisasterPlus_TyphoonTower");
            _towerEffect = ComponentOf(_towerObject, out _towerParticles);

            _canopyObject = BuildClone(source, VortexCloudLayer.Canopy,
                                       "DisasterPlus_TyphoonCanopy");
            _canopyEffect = ComponentOf(_canopyObject, out _canopyParticles);

            if (!AnyClone())
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonCloudFxState.NoEffect;
                return false;
            }

            Log.Info("typhoon cloud: borrowed \"" + name + "\" (particle material \"" + material
                     + "\") for the vortex and cloned it into " + CloneCount()
                     + " cumulonimbus layer(s): " + VortexPuffLayout.PuffCount
                     + " puffs/frame, " + (int)ParticlesPerSecond + " particles/s, cap "
                     + TotalParticleCap());
            return true;
        }

        private static ParticleEffect ComponentOf(GameObject go, out ParticleSystem particles)
        {
            particles = null;
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            if (effect == null) return null;

            particles = go.GetComponent<ParticleSystem>();
            return effect;
        }

        /// <summary>
        /// **在庫を列挙して、いちばん雲らしい粒子エフェクトを 1 つ返す。**
        ///
        /// 2 つの経路を両方なめる —— <c>EffectCollection</c> に登録されているのは
        /// 186 個だけで、<c>Factory Steam</c> のような素材はそこに**入っていない**
        /// （§A-3 の未登録 17 個）。<c>EffectsWrapper.m_BuiltinEffects</c> は
        /// <c>Resources.FindObjectsOfTypeAll&lt;EffectInfo&gt;()</c> の結果なので、
        /// 「このビルドに読み込まれている全部」である。
        ///
        /// 最後の保険として <c>BuildingProperties.m_collapseEffect</c>（崩壊の粉塵）を見る。
        /// **確保はする**（辞書の列挙と <c>EffectCollection.Effects</c> の
        /// <c>IEnumerable</c>）が、**呼ばれるのは都市ごとに 1 回**である。
        /// </summary>
        private static ParticleEffect Lookup(out string name, out string material)
        {
            name = null;
            material = null;

            ParticleEffect best = null;
            int bestScore = 0;
            int bestRank = int.MaxValue;

            Dictionary<string, EffectInfo> builtin = BuiltinEffects();
            if (builtin != null)
            {
                foreach (KeyValuePair<string, EffectInfo> pair in builtin)
                {
                    Consider(pair.Key, pair.Value, ref best, ref bestScore, ref bestRank,
                             ref name, ref material);
                }
            }

            foreach (EffectInfo info in RegisteredEffects())
            {
                if (info == null) continue;
                Consider(info.name, info, ref best, ref bestScore, ref bestRank,
                         ref name, ref material);
            }

            if (best == null)
            {
                // 最後の手段: ゲーム自身が握っている崩壊の粉塵。
                EffectInfo fallback = BuildingCollapseEffect();
                Consider("BuildingProperties.m_collapseEffect", fallback,
                         ref best, ref bestScore, ref bestRank, ref name, ref material);
            }

            return best;
        }

        /// <summary>候補 1 件を採点して、いまの最良と入れ替えるか決める。</summary>
        private static void Consider(string candidateName, EffectInfo info,
                                     ref ParticleEffect best, ref int bestScore, ref int bestRank,
                                     ref string name, ref string material)
        {
            ParticleEffect particle = Extract(info);
            if (particle == null) return;

            string mat = MaterialNameOf(particle);
            int score = ScoreOf(mat);
            if (score <= 0) return;

            int rank = RankOf(candidateName);

            // 点が高いほうを採る。同点なら PreferredNames の順、それでも同点なら
            // 名前の辞書順 —— **どのビルドでも同じものを選ぶ**（実機の報告が読める）。
            if (score < bestScore) return;
            if (score == bestScore)
            {
                if (rank > bestRank) return;
                if (rank == bestRank && best != null
                    && string.CompareOrdinal(candidateName, name) >= 0) return;
            }

            best = particle;
            bestScore = score;
            bestRank = rank;
            name = candidateName;
            material = mat;
        }

        private static int RankOf(string candidateName)
        {
            if (candidateName == null) return int.MaxValue - 1;
            for (int i = 0; i < PreferredNames.Length; i++)
            {
                if (PreferredNames[i] == candidateName) return i;
            }
            return int.MaxValue - 1;
        }

        /// <summary>
        /// その粒子エフェクトが実際に描いているマテリアルの名前。
        /// **<c>sharedMaterial</c> を読むこと**（<c>material</c> は複製して差し替える）。
        /// </summary>
        private static string MaterialNameOf(ParticleEffect effect)
        {
            try
            {
                var go = effect.gameObject;
                if (go == null) return null;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                if (renderer == null) return null;

                var mat = renderer.sharedMaterial;
                if (mat == null) return null;

                // Unity は複製したマテリアルに " (Instance)" を付ける。素の名前で見る。
                string n = mat.name;
                if (n == null) return null;

                int at = n.IndexOf(" (Instance)");
                return at > 0 ? n.Substring(0, at) : n;
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, EffectInfo> BuiltinEffects()
        {
            try
            {
                if (!Singleton<EffectManager>.exists) return null;
                var wrapper = Singleton<EffectManager>.instance.m_EffectsWrapper;
                return wrapper != null ? wrapper.m_BuiltinEffects : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<EffectInfo> RegisteredEffects()
        {
            try
            {
                IEnumerable<EffectInfo> effects = EffectCollection.Effects;
                if (effects != null) return effects;
            }
            catch
            {
                // 列挙できない環境でも、上の m_BuiltinEffects だけで足りる。
            }
            return EmptyEffects;
        }

        private static readonly EffectInfo[] EmptyEffects = new EffectInfo[0];

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
        /// 1 層ぶん複製して入道雲に仕立てる。**共有状態は 1 バイトも触らない**（§D-5）。
        /// 数値表は <see cref="VortexCloudProfile"/>（Core）に在る ——
        /// <c>tools/TyphoonPreview</c> が同じ数字で渦を描くので**ここに直書きしない**。
        /// </summary>
        private static GameObject BuildClone(ParticleEffect source, VortexCloudLayer layer,
                                             string name)
        {
            GameObject go = CloneObject(source, name);
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var ps = go.GetComponent<ParticleSystem>();
            if (effect == null || ps == null) return Reject(go);

            VortexCloudProfile profile = VortexCloudProfile.Of(layer);

            effect.m_maxVisibilityDistance = VisibilityMetres;
            effect.m_minLifeTime = profile.LifeMinSeconds;
            effect.m_maxLifeTime = profile.LifeMaxSeconds;
            effect.m_minStartSpeed = profile.SpeedMin;
            effect.m_maxStartSpeed = profile.SpeedMax;
            effect.m_minSpawnAngle = profile.SpawnAngleMinDegrees;
            effect.m_maxSpawnAngle = profile.SpawnAngleMaxDegrees;
            effect.m_renderDuration = 0f;      // 継続モードで使う（§B-3）
            effect.m_extraRadius = 0f;         // 借り元によっては 2〜9 m 勝手に足す

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(profile.BrightRed, profile.BrightGreen, profile.BrightBlue,
                          profile.Alpha),
                new Color(profile.DarkRed, profile.DarkGreen, profile.DarkBlue, profile.Alpha));
            main.startSize = MinSizeMetres;    // 実際の値は毎フレーム ApplySizes が入れる
            main.gravityModifier = profile.GravityModifier;
            main.maxParticles = profile.MaxParticles;

            var emission = ps.emission;
            emission.rateOverTime = profile.RateOverTime;

            return Initialize(go, effect);
        }

        /// <summary>
        /// プレハブを 1 個複製する。内部フィールドは全て <c>[NonSerialized]</c> なので、
        /// 複製は**未初期化状態**で始まり、元のプレハブと <c>ParticleSystem</c> を
        /// 共有する事故は起きない（IL 実測 §D-1）。
        /// </summary>
        private static GameObject CloneObject(ParticleEffect source, string name)
        {
            try
            {
                var sourceObject = source.gameObject;
                if (sourceObject == null) return null;

                var go = Object.Instantiate(sourceObject) as GameObject;
                if (go == null) return null;

                go.name = name;
                Object.DontDestroyOnLoad(go);

                // ★★ **InitializeEffect の前に**外側の放出を止める。止めないと、この
                //    GameObject 自身がワールド原点で蒸気を吐き続ける（クラス doc）。
                //    playOnAwake は触らない（内側の複製が Play できなくなる）。
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                    ps.Stop();
                    ps.Clear();
                }

                return go;
            }
            catch (System.Exception e)
            {
                Log.Info("typhoon cloud: \"" + name + "\" could not be cloned ("
                         + e.GetType().Name + ")");
                return null;
            }
        }

        /// <summary>
        /// <c>InitializeEffect()</c> を呼び、粒子系が実際に出来たかを確かめる。
        /// **出来ていない複製を描画へ渡すと <c>EmitParticles</c> の中で NRE になる**
        /// （あちらは <c>m_particleSystem</c> を null 検査なしで参照する。IL 実測）。
        /// </summary>
        private static GameObject Initialize(GameObject go, ParticleEffect effect)
        {
            try
            {
                effect.InitializeEffect();
            }
            catch (System.Exception e)
            {
                Log.Info("typhoon cloud: \"" + go.name + "\" could not be initialised ("
                         + e.GetType().Name + ")");
                return Reject(go);
            }

            return Ready(effect) ? go : Reject(go);
        }

        private static bool Ready(ParticleEffect effect)
        {
            if (ParticleSystemField == null) return false;

            try
            {
                // ★ ParticleSystem へ落としてから比較する。object のまま != null で見ると
                //   Unity の破棄済み（fake-null）を「在る」と読んでしまう。
                var particles = ParticleSystemField.GetValue(effect) as ParticleSystem;
                return particles != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>作りかけを捨てる。**中途半端な複製を残さない。**</summary>
        private static GameObject Reject(GameObject go)
        {
            try
            {
                var effect = go.GetComponent<ParticleEffect>();
                if (effect != null) effect.ReleaseEffect();
            }
            catch
            {
                // 解放できなくても GameObject は消す。
            }

            Object.Destroy(go);
            return null;
        }

        /// <summary>
        /// 粒径を渦の大きさに合わせる。**複製側なので毎フレーム書いてよい**
        /// （<c>MainModule</c> は struct、確保は 0 バイト）。
        /// </summary>
        private static void ApplySizes(float radius)
        {
            ApplySize(_deckParticles, radius, VortexCloudProfile.Deck.SizeFraction);
            ApplySize(_towerParticles, radius, VortexCloudProfile.Tower.SizeFraction);
            ApplySize(_canopyParticles, radius, VortexCloudProfile.Canopy.SizeFraction);
        }

        private static void ApplySize(ParticleSystem particles, float radius, float fraction)
        {
            // ★ 参照そのものを見る。破棄済みなら fake-null で null と等価になる。
            if (particles == null) return;

            float size = radius * fraction;
            if (float.IsNaN(size)) return;
            if (size < MinSizeMetres) size = MinSizeMetres;
            if (size > MaxSizeMetres) size = MaxSizeMetres;

            var main = particles.main;
            main.startSize = size;
        }

        private static void DestroyClones()
        {
            Release(ref _deckObject, ref _deckEffect, ref _deckParticles);
            Release(ref _towerObject, ref _towerEffect, ref _towerParticles);
            Release(ref _canopyObject, ref _canopyEffect, ref _canopyParticles);
            _sourceName = null;
            _sourceMaterial = null;
        }

        private static void Release(ref GameObject go, ref ParticleEffect effect,
                                    ref ParticleSystem particles)
        {
            if (effect != null)
            {
                try
                {
                    // 内側の複製（ParticleEffect.CreateEffect が作ったもの）を畳む。
                    effect.ReleaseEffect();
                }
                catch
                {
                    // 畳めなくても外側は必ず消す。
                }
            }
            effect = null;
            particles = null;

            if (go != null) Object.Destroy(go);
            go = null;
        }
    }
}
