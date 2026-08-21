using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④が<b>バニラの粒子エフェクトを借りる</b>ときの共通部分。**main スレッド専用。**
    ///
    /// ── ★ 名前ではなくマテリアルで選ぶ ────────────────────────────
    ///
    /// エフェクト実測文書の在庫は <b>PARTIAL</b> と印が付いている（アセットに在ることと、
    /// 実行時 API がそれを返すことは別）。名前を書き並べて先頭から試す作りは、
    /// その名前がこのビルドに在るかどうかに賭けている。
    ///
    /// ここは賭けない。<c>EffectsWrapper.m_BuiltinEffects</c>
    /// （＝ <c>Resources.FindObjectsOfTypeAll&lt;EffectInfo&gt;()</c> の結果。「このビルドに
    /// 読み込まれている全部」）と <c>EffectCollection.Effects</c>（登録済み 186 個）を
    /// <b>実際に列挙し</b>、各 <c>ParticleEffect</c> が実際に描いている
    /// <c>ParticleSystemRenderer.sharedMaterial.name</c> を読んで、
    /// <b>呼び出し側が渡したマテリアルの優先順で採点する</b>。
    ///
    /// 出荷アセットから取り出して測ったテクスチャの平均 RGB:
    ///
    /// <code>
    /// マテリアル      テクスチャ        平均 RGB        見た目
    /// Steam          steam           (168, 184, 189)  淡い青白の綿。**雲**
    /// Water          water           (201, 222, 254)  白青の飛沫。**雨・しぶき**
    /// Placement Dust placement-dust  (198, 165, 131)  砂色の土煙
    /// IndustryDust   IndustryDust    ( 99,  94,  79)  茶灰の粉塵
    /// Smoke          smoke           ( 75,  78,  80)  暗い煤色の塊（火の粉の点入り）
    /// </code>
    ///
    /// <c>startColor</c> は乗算で掛かるので、<b>素材が煤なら何色を掛けても煙に見える。</b>
    /// これが持ち主の「雲のエフェクトが煙になっている」の直接の原因だった。
    ///
    /// 名前の一覧（<c>namePreference</c>）は<b>同点の並べ替えにしか使わない</b>。
    /// ゲームが更新されて名前が変わっても、マテリアルが同じなら同じ絵が出る。
    ///
    /// ★ <c>sharedMaterial</c> を読むこと。<c>material</c> はレンダラのマテリアルを
    ///   **複製して差し替える**ので、ゲームの共有状態を壊すうえリークする。
    ///
    /// ── ★ 複製してから触る（§D-5）─────────────────────────────────
    ///
    /// <c>ParticleSystem</c> の色・粒径・寿命・可視距離は**エフェクトの共有状態**である。
    /// 借り元をそのまま書き換えると街じゅうの蒸気や飛沫が道連れになり、
    /// セーブではなくメモリ上に残る。<see cref="Clone"/> が
    /// <c>Object.Instantiate</c> してから返す。
    ///
    /// ★★ 複製した <c>GameObject</c> は**アクティブなシーンに入る**ので、そのままだと
    ///   自分の <c>ParticleSystem</c> がワールド原点で吐き続ける。
    ///   <see cref="Clone"/> は <c>emission.enabled = false</c> にしてから返す
    ///   （<c>InitializeEffect()</c> は**その状態をもう 1 段複製する**ので内側にも
    ///   同じ設定が渡る。IL 実測）。<c>playOnAwake</c> は**触らない** ——
    ///   内側の複製は <c>ParticleEffect.Update</c> が <c>isPaused</c> を見て
    ///   <c>Play()</c> し直す作りなので、止めた状態を配ると粒子が動かなくなる。
    ///
    /// ⑤火山にも同じ形の借り方がある（<c>VolcanoVanillaFx</c>）。**まとめていない** ——
    /// あちらは名前で引く前提の在庫（<c>Fire Particles</c> など「その 1 個でなければ
    /// ならない」もの）を扱っていて、こちらは「いちばん雲らしいもの」を選ぶ。
    /// 選び方が違うものを 1 つの型にすると、どちらの意図も読めなくなる。
    /// </summary>
    internal static class VanillaParticles
    {
        /// <summary>
        /// **加算合成（<c>Custom/Particles/Additive (Soft)</c>）の光り物。**
        /// 雲にも雨にも使えない（空が燃える）ので、採点の前に落とす。
        /// </summary>
        private static readonly string[] Rejected =
        {
            "Fire", "FireRocket", "Explosion", "RocketLaunch", "SmokeRocket",
            "FireworksIngame", "FireworksTrail",
        };

        /// <summary>
        /// <c>ParticleEffect.m_particleSystem</c>（<c>[NonSerialized]</c> の private）。
        /// **初期化されていない複製を <c>RenderEffect</c> へ渡すと
        /// <c>EmitParticles</c> の中で NRE になる**ので、これで確かめる。
        /// </summary>
        private static readonly System.Reflection.FieldInfo ParticleSystemField =
            typeof(ParticleEffect).GetField("m_particleSystem",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

        private static readonly EffectInfo[] EmptyEffects = new EffectInfo[0];

        /// <summary>
        /// 在庫を列挙して、<paramref name="materialPreference"/> にいちばん近い
        /// 粒子エフェクトを 1 つ返す。**例外は投げない**（取れなければ null）。
        ///
        /// **確保はする**（辞書の列挙と <c>EffectCollection.Effects</c> の
        /// <c>IEnumerable</c>）が、呼ぶのは都市ごとに 1 回である。
        /// </summary>
        /// <param name="materialPreference">良い順のマテリアル名。先頭がいちばん良い。</param>
        /// <param name="namePreference">同点のときの並べ替えに使う名前。無ければ null。</param>
        internal static ParticleEffect Pick(string[] materialPreference, string[] namePreference,
                                            out string name, out string material)
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
                    Consider(pair.Key, pair.Value, materialPreference, namePreference,
                             ref best, ref bestScore, ref bestRank, ref name, ref material);
                }
            }

            foreach (EffectInfo info in RegisteredEffects())
            {
                if (info == null) continue;
                Consider(info.name, info, materialPreference, namePreference,
                         ref best, ref bestScore, ref bestRank, ref name, ref material);
            }

            if (best == null)
            {
                // 最後の手段: ゲーム自身が握っている建物崩壊の粉塵。
                Consider("BuildingProperties.m_collapseEffect", BuildingCollapseEffect(),
                         materialPreference, namePreference,
                         ref best, ref bestScore, ref bestRank, ref name, ref material);
            }

            return best;
        }

        /// <summary>候補 1 件を採点して、いまの最良と入れ替えるか決める。</summary>
        private static void Consider(string candidateName, EffectInfo info,
                                     string[] materialPreference, string[] namePreference,
                                     ref ParticleEffect best, ref int bestScore, ref int bestRank,
                                     ref string name, ref string material)
        {
            ParticleEffect particle = Extract(info);
            if (particle == null) return;

            string mat = MaterialNameOf(particle);
            int score = ScoreOf(mat, materialPreference);
            if (score <= 0) return;

            int rank = RankOf(candidateName, namePreference);

            // 点が高いほうを採る。同点なら名前の優先順、それでも同点なら辞書順 ——
            // **どのビルドでも同じものを選ぶ**（実機の報告が読める）。
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

        /// <summary>
        /// マテリアル名の採点。**大きいほど良い。** 0 は「使わない」。
        /// 一覧に無いマテリアルは 1 点 —— **一覧に在るものには必ず負ける**が、
        /// 何も無いよりはましである（ゲーム更新で名前が全部変わった環境の保険）。
        /// </summary>
        private static int ScoreOf(string material, string[] preference)
        {
            if (string.IsNullOrEmpty(material)) return 0;

            for (int i = 0; i < Rejected.Length; i++)
            {
                if (Rejected[i] == material) return 0;
            }

            if (preference != null)
            {
                for (int i = 0; i < preference.Length; i++)
                {
                    if (preference[i] == material) return preference.Length - i + 1;
                }
            }

            return 1;
        }

        private static int RankOf(string candidateName, string[] namePreference)
        {
            if (candidateName == null || namePreference == null) return int.MaxValue - 1;

            for (int i = 0; i < namePreference.Length; i++)
            {
                if (namePreference[i] == candidateName) return i;
            }
            return int.MaxValue - 1;
        }

        /// <summary>
        /// その粒子エフェクトが実際に描いているマテリアルの名前。
        /// **<c>sharedMaterial</c> を読むこと**（クラス doc）。
        /// </summary>
        internal static string MaterialNameOf(ParticleEffect effect)
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
        /// プレハブを 1 個複製する。内部フィールドは全て <c>[NonSerialized]</c> なので、
        /// 複製は**未初期化状態**で始まり、元のプレハブと <c>ParticleSystem</c> を
        /// 共有する事故は起きない（IL 実測 §D-1）。
        /// **まだ <c>InitializeEffect()</c> は呼んでいない**（呼び出し側が数値を書いてから
        /// <see cref="Initialize"/> を呼ぶ）。
        /// </summary>
        internal static GameObject Clone(ParticleEffect source, string name)
        {
            try
            {
                var sourceObject = source.gameObject;
                if (sourceObject == null) return null;

                var go = Object.Instantiate(sourceObject) as GameObject;
                if (go == null) return null;

                go.name = name;
                Object.DontDestroyOnLoad(go);

                // ★★ **InitializeEffect の前に**外側の放出を止める（クラス doc）。
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
                Log.Info("typhoon effects: \"" + name + "\" could not be cloned ("
                         + e.GetType().Name + ")");
                return null;
            }
        }

        /// <summary>
        /// <c>InitializeEffect()</c> を呼び、粒子系が実際に出来たかを確かめる。
        /// 出来ていなければ複製ごと捨てて null を返す。
        /// </summary>
        internal static GameObject Initialize(GameObject go, ParticleEffect effect)
        {
            try
            {
                effect.InitializeEffect();
            }
            catch (System.Exception e)
            {
                Log.Info("typhoon effects: \"" + go.name + "\" could not be initialised ("
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
        internal static GameObject Reject(GameObject go)
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
        /// 複製 1 個を手放す。**冪等。** 内側の複製
        /// （<c>ParticleEffect.CreateEffect</c> が作ったもの）も畳む。
        /// </summary>
        internal static void Release(ref GameObject go, ref ParticleEffect effect,
                                     ref ParticleSystem particles)
        {
            if (effect != null)
            {
                try
                {
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

        /// <summary>
        /// 今この環境で描けるカメラ情報。**null を <c>RenderEffect</c> へ渡さないこと**
        /// （<c>ParticleEffect.RenderEffect</c> の先頭で NRE になる。§H-18）。
        /// </summary>
        internal static RenderManager.CameraInfo CameraInfo()
        {
            try
            {
                return Singleton<RenderManager>.exists
                    ? Singleton<RenderManager>.instance.CurrentCameraInfo : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// バニラが今フレームぶんとして使っている時間差。
        /// <b><c>Time.deltaTime</c> ではない。</b> <c>EffectManager.EndRenderingImpl</c> は
        /// <c>SimulationManager.m_simulationTimeDelta</c> を渡していて（IL 実測 §B-3）、
        /// こちらを使うと**一時停止と速度変更にそのまま追随する**。
        /// 読めなければ 0 を返す ——**0 は「今フレームは 1 粒も出さない」であって、
        /// 例外でも作り話でもない。**
        /// </summary>
        internal static float TimeDelta()
        {
            try
            {
                if (!Singleton<SimulationManager>.exists) return 0f;
                float dt = Singleton<SimulationManager>.instance.m_simulationTimeDelta;
                if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) return 0f;
                return dt;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
