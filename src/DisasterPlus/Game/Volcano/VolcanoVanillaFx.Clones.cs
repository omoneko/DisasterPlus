using System;
using DisasterPlus.Core.Volcano;
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
        /// 噴煙<b>柱</b>の複製。**高く・長く・遠くから見えるように**する。
        /// <c>Factory Smoke</c> の素の可視距離は 1000 m しかなく、火山の噴煙は
        /// 遠景から見えてほしい。
        ///
        /// ★★ <b>初速を 26-48 m/s から 3-11 m/s へ落としてある</b>（2026-08-22、指摘③）。
        ///   柱の形はもう「どこに湧かせるか」で作っている（<c>Core/Volcano/EruptionColumn</c>
        ///   の 9 段）ので、粒子自身が上がり続けると**傘に天井が出来ない** ——
        ///   中立浮力高度で止まって横へ広がるのが噴火柱の姿である。
        ///   寿命も 7-16s から 4-9s へ短くした。段に湧いた粒子はその場で消え、
        ///   毎フレーム湧き直すことで<b>供給され続ける柱</b>になる。
        ///
        /// 色は暗い灰褐色。**噴出口の近くほど濃く暗い**のは段ごとの密度が担っていて
        /// （下の段ほど重みが大きい）、色はここで 1 つに決める ——
        /// <c>startColor</c> は <c>ParticleSystem</c> 側の共有状態で、
        /// <c>RenderEffect</c> の呼び出しごとには変えられない（IL 実測 §B-4）。
        /// </summary>
        private static GameObject CloneAsh(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoAsh");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            // ★ 数値表は Core にある（EruptionAshProfile）。tools/VolcanoPreview が
            //   同じ数字で噴煙柱を描くので、**ここに直書きしない**。
            Apply(effect, particles, EruptionAshProfile.Column, 10000f);

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.26f, 0.21f, 0.17f, 1f), new Color(0.08f, 0.07f, 0.065f, 1f));

            return Initialize(go, effect);
        }

        /// <summary>
        /// 噴煙柱の<b>傘</b>の複製。中立浮力高度で横へ広がる、淡くて大きくて長生きの灰。
        ///
        /// 柱と分けてあるのは <c>startColor</c> / <c>startSize</c> / 寿命が
        /// <c>ParticleSystem</c> 側の**共有状態**で、<c>RenderEffect</c> の呼び出しごとには
        /// 変えられないからである（IL 実測 §B-4）。傘は
        ///
        /// <list type="bullet">
        /// <item>柱より<b>淡い</b>（薄く広がった灰は空に対して明るい）</item>
        /// <item>粒が 3 倍以上<b>大きい</b>（半径 500 m 級の面を粒 30 で埋めると数が要る）</item>
        /// <item><b>長生き</b>（18-34s）。滞留して積み上がることで平たい面になる</item>
        /// <item>放出角 55-95°。<b>ほぼ水平に広がる</b> ——
        ///   <c>direction</c> が上なので、この角度がそのまま横向きの初速になる</item>
        /// </list>
        ///
        /// 引けなくても⑤は止まらない（柱の複製で代用する。<c>AshUmbrella</c> の doc）。
        /// </summary>
        private static GameObject CloneAshUmbrella(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoUmbrella");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            Apply(effect, particles, EruptionAshProfile.Umbrella, 12000f);

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.44f, 0.42f, 0.41f, 1f), new Color(0.19f, 0.18f, 0.175f, 1f));

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
        /// Core の数値表（<see cref="EruptionAshProfile"/>）を複製へ書き写す。
        ///
        /// ★ <c>m_renderDuration</c> は必ず 0 にする。継続モード（<c>timeOffset &lt; 0</c>）で
        ///   回すので、0 以外だと <c>m_intensityCurve</c> の位相ゲートが掛かる。
        /// ★★ <c>rateOverTime</c> を 0 にしないこと。<c>emission.enabled</c> が false でも
        ///   <c>EmitParticles</c> は <c>rateOverTime.constant</c> を**粒子数の乗数として
        ///   読み続ける**（IL 実測）。0 にすると 1 粒も出ない。
        /// </summary>
        private static void Apply(ParticleEffect effect, ParticleSystem particles,
                                  EruptionAshProfile profile, float visibilityMetres)
        {
            effect.m_minLifeTime = profile.LifeMinSeconds;
            effect.m_maxLifeTime = profile.LifeMaxSeconds;
            effect.m_minStartSpeed = profile.SpeedMin;
            effect.m_maxStartSpeed = profile.SpeedMax;
            effect.m_minSpawnAngle = profile.SpawnAngleMinDegrees;
            effect.m_maxSpawnAngle = profile.SpawnAngleMaxDegrees;
            effect.m_maxVisibilityDistance = visibilityMetres;
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startSize = profile.SizeMetres;
            main.gravityModifier = profile.GravityModifier;
            main.maxParticles = profile.MaxParticles;

            var emission = particles.emission;
            emission.rateOverTime = profile.RateOverTime;
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
