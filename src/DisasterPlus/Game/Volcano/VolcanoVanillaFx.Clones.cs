using System;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="VolcanoVanillaFx"/> のうち<b>「借りたプレハブの、どこを変えるか」</b>。
    /// **main スレッド専用。**
    ///
    /// 引き方・寿命・後始末は本体側にある。ここに在るのは
    /// <b>複製 1 個ぶんの数値表</b>と、複製が実際に使える状態になったかの検査だけである。
    /// 分けてあるのは 800 行の上限のためだけではない ——
    /// 見た目を調整するときに読むのはこのファイルだけで済む。
    ///
    /// ── ★ ここで変えてよいのは複製だけである（§D-5）─────────────────────
    ///
    /// <c>ParticleSystem.main.startColor</c> / <c>startSize</c> や
    /// <c>ParticleEffect.m_minLifeTime</c> は**そのプレハブの共有状態**である。
    /// 元のプレハブを書き換えると<b>街じゅうの建物火災や工場の煙が道連れになり</b>、
    /// しかもセーブではなくメモリ上に残る。この 3 つは全部**複製に対して**書いている。
    /// 炎（<c>Fire Particles</c>）はここに出てこない ——
    /// あれは複製せず、1 バイトも書き換えないから共有して安全である。
    ///
    /// ── ★★ <c>emission.rateOverTime</c> を 0 にしないこと ─────────────────
    ///
    /// <c>ParticleEffect.CreateEffect</c> は内側の複製の <c>emission.enabled</c> を
    /// <c>false</c> にするが、<c>EmitParticles</c> は
    /// <c>emission.rateOverTime.constant</c> を<b>粒子数の乗数として読み続ける</b>
    /// （IL 実測）。0 にすると 1 粒も出ない。いちばん踏みやすい罠である。
    /// </summary>
    internal static partial class VolcanoVanillaFx
    {
        /// <summary>
        /// 噴煙の複製。**高く・長く・遠くから見えるように**する。
        /// <c>Factory Smoke</c> の素の可視距離は 1000 m しかなく、火山の噴煙は
        /// 遠景から見えてほしい。
        /// </summary>
        private static GameObject CloneAsh(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoAsh");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            effect.m_minLifeTime = 7f;
            effect.m_maxLifeTime = 16f;
            effect.m_minStartSpeed = 26f;
            effect.m_maxStartSpeed = 48f;
            effect.m_minSpawnAngle = 0f;
            effect.m_maxSpawnAngle = 13f;
            effect.m_maxVisibilityDistance = 10000f;
            // ★ 0 にしておくこと。継続モード（timeOffset < 0）で回すので、
            //   0 以外だと m_intensityCurve の位相ゲートが掛かる。
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.24f, 0.22f, 0.21f, 1f), new Color(0.09f, 0.08f, 0.08f, 1f));
            main.startSize = 34f;
            main.gravityModifier = -0.04f;   // わずかに浮く
            main.maxParticles = 5000;

            // ★★ rateOverTime を 0 にしないこと。emission.enabled が false でも
            //    EmitParticles は rateOverTime.constant を**粒子数の乗数として読む**。
            //    0 にすると 1 粒も出ない（いちばん踏みやすい罠）。
            var emission = particles.emission;
            emission.rateOverTime = 42f;

            return Initialize(go, effect);
        }

        /// <summary>
        /// 噴石の複製。素の <c>Medium Explosion Particles</c> は
        /// <c>gravityModifier = -1</c>（＝上向き）で粒径 60 の**爆炎**なので、
        /// 重力を下向きに戻し粒を小さくして「弧を描いて落ちる破片」にする。
        /// </summary>
        private static GameObject CloneEjecta(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoEjecta");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            effect.m_minLifeTime = 2.4f;
            effect.m_maxLifeTime = 4.2f;
            effect.m_minStartSpeed = 55f;
            effect.m_maxStartSpeed = 120f;
            effect.m_minSpawnAngle = 0f;
            effect.m_maxSpawnAngle = 42f;
            effect.m_maxVisibilityDistance = 10000f;
            // ★ 素は 1.0 秒。継続モードで自分で窓を作るので 0 に落とす。
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.86f, 0.42f, 1f), new Color(0.62f, 0.16f, 0.05f, 1f));
            main.startSize = 6f;
            main.gravityModifier = 1.6f;     // ★ 落ちる。素は -1（上向き）
            main.maxParticles = 3000;

            var emission = particles.emission;
            emission.rateOverTime = 150f;

            return Initialize(go, effect);
        }

        /// <summary>
        /// 火砕流もどきの複製。**ほぼ水平に広がり、ゆっくり沈む灰**にする。
        /// 素の <c>Collapse Particles</c> は放出角 50–80°（ほぼ横）で既に近いが、
        /// 寿命が短く、可視距離が 1000 m しかない。
        /// </summary>
        private static GameObject CloneDust(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoPyroclast");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            effect.m_minLifeTime = 4.5f;
            effect.m_maxLifeTime = 9f;
            // ★ ベジェ帯では速さが**上向き成分にしか入らない**（IL 実測）。
            //   横へ流すのは RenderEffect の velocity 引数のほうなので、ここは小さく。
            effect.m_minStartSpeed = 2f;
            effect.m_maxStartSpeed = 7f;
            effect.m_minSpawnAngle = 70f;
            effect.m_maxSpawnAngle = 95f;
            effect.m_maxVisibilityDistance = 10000f;
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.30f, 0.27f, 0.25f, 1f), new Color(0.13f, 0.12f, 0.11f, 1f));
            main.startSize = 28f;
            main.gravityModifier = 0.12f;    // ゆっくり沈む
            main.maxParticles = 6000;

            var emission = particles.emission;
            emission.rateOverTime = 26f;

            return Initialize(go, effect);
        }

        /// <summary>
        /// プレハブを 1 個複製する。内部フィールドは全て <c>[NonSerialized]</c> なので、
        /// 複製は**未初期化状態**で始まり、元のプレハブと <c>ParticleSystem</c> を
        /// 共有する事故は起きない（IL 実測）。
        /// </summary>
        private static GameObject CloneObject(ParticleEffect source, string name)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(source.gameObject);
                if (go == null) return null;

                go.name = name;
                // レベルアンロードでゲームに消させない（自分で消す。Destroy() を見よ）。
                UnityEngine.Object.DontDestroyOnLoad(go);

                // ★ 複製そのものは「型紙」であって、これ自身は 1 粒も出さない
                //   （粒子が湧くのは InitializeEffect が作る内側の複製である）。
                //   放っておくと型紙が地図の原点で煙を吐く。
                var particles = go.GetComponent<ParticleSystem>();
                if (particles != null)
                {
                    var emission = particles.emission;
                    emission.enabled = false;
                    particles.Stop();
                    particles.Clear();
                }

                return go;
            }
            catch (Exception e)
            {
                Log.Info("volcano effects: \"" + name + "\" could not be cloned ("
                         + e.GetType().Name + "); Disaster + uses the game's own effect "
                         + "unchanged instead");
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
            catch (Exception e)
            {
                Log.Info("volcano effects: \"" + go.name + "\" could not be initialised ("
                         + e.GetType().Name + ")");
                return Reject(go);
            }

            return Ready(effect) ? go : Reject(go);
        }

        /// <summary>
        /// その <c>ParticleEffect</c> の <c>m_particleSystem</c> が実際に出来ているか。
        ///
        /// ★★ 複製にも**共有プレハブにも**掛ける。<c>EffectCollection</c> は名前を
        ///   辞書へ入れるだけで <c>InitializeEffect()</c> を呼ばない（IL 実測）ので、
        ///   「引けた」と「粒子系が在る」は別である。初期化されていないものを
        ///   <c>RenderEffect</c> へ渡すと <c>EmitParticles</c> の中で NRE になる。
        /// </summary>
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

            UnityEngine.Object.Destroy(go);
            return null;
        }
    }
}
