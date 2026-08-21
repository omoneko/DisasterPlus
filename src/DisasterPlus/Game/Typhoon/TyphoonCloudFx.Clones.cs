using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="TyphoonCloudFx"/> のうち<b>「何を借りて、どこを変えるか」</b>。
    /// **main スレッド専用。**
    ///
    /// 撒き方は本体側にある。ここに在るのは<b>素材の望み</b>と
    /// <b>複製 1 個ぶんの数値の書き込み</b>だけ。分けてあるのは 800 行の上限のためだけ
    /// ではない —— 見た目を調整するときに読むのはこのファイルと
    /// <c>Core/Typhoon/VortexCloudProfile</c> だけで済む。
    ///
    /// 列挙・採点・複製・初期化・後始末は <see cref="VanillaParticles"/> が持っている
    /// （<c>TyphoonSquallFx</c> と共有する）。**名前ではなくマテリアルで選ぶ**理由と、
    /// 出荷アセットから測ったテクスチャの平均 RGB もあちらの doc にある。
    /// </summary>
    public static partial class TyphoonCloudFx
    {
        /// <summary>複製側に固定する <c>emission.rateOverTime</c>。
        /// **0 にしてはいけない**（0 だと粒子が 1 個も出ない。§D-2 の罠）。
        /// 借りた素材ごとに違う値（9.67〜200）が入っているので、ここで揃えて
        /// <see cref="VortexPuffLayout.MagnitudeFor"/> の入力を確定させる。</summary>
        private const float RateOverTime = 20f;

        /// <summary>
        /// 望む粒子マテリアル（良い順）。**<c>Steam</c> が本命である** ——
        /// 出荷アセットの <c>steam</c> テクスチャは平均 RGB (168, 184, 189) の
        /// 淡い青白の綿で、雲そのものである。<c>Smoke</c> は (75, 78, 80) の
        /// 煤の絵なので**最後**に置く（旧実装はこれを第 1 候補にしていた）。
        /// </summary>
        private static readonly string[] CloudMaterials =
        {
            "Steam", "Water", "Snow", "Placement Dust", "IndustryDust", "Smoke",
        };

        /// <summary>
        /// 同点のときの並べ替えにだけ使う名前の順。**引く順ではない。**
        /// <c>Large Pool Steam</c> を先頭にしてあるのは、素の初速が 0.1〜0.2 m/s で
        /// いちばん「動かない大きな雲」に近いからである（§A-6）。
        /// </summary>
        private static readonly string[] CloudNames =
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
        /// 借りられるかだけを見る（<c>Assumptions</c> と共有する式）。副作用は無い。
        /// </summary>
        private static ParticleEffect Lookup(out string name, out string material)
        {
            return VanillaParticles.Pick(CloudMaterials, CloudNames, out name, out material);
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
        /// 1 層ぶん複製して入道雲に仕立てる。**共有状態は 1 バイトも触らない**（§D-5）。
        /// 数値表は <see cref="VortexCloudProfile"/>（Core）に在る ——
        /// <c>tools/TyphoonPreview</c> が同じ数字で渦を描くので**ここに直書きしない**。
        /// </summary>
        private static GameObject BuildClone(ParticleEffect source, VortexCloudLayer layer,
                                             string name)
        {
            GameObject go = VanillaParticles.Clone(source, name);
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var ps = go.GetComponent<ParticleSystem>();
            if (effect == null || ps == null) return VanillaParticles.Reject(go);

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

            return VanillaParticles.Initialize(go, effect);
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
            VanillaParticles.Release(ref _deckObject, ref _deckEffect, ref _deckParticles);
            VanillaParticles.Release(ref _towerObject, ref _towerEffect, ref _towerParticles);
            VanillaParticles.Release(ref _canopyObject, ref _canopyEffect, ref _canopyParticles);
            _sourceName = null;
            _sourceMaterial = null;
        }
    }
}
