using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="VolcanoVanillaFx"/> のうち<b>爆発（一発もの）と噴石（飛ぶ岩）</b>の分。
    /// **main スレッド専用。**
    ///
    /// ── ★★ 爆発だけは「複製しない」（ここが他の 4 つと違う唯一の点）─────────
    ///
    /// 爆発は <c>EffectManager.DispatchEffect</c> で積む一発ものである。あれは
    /// **キューに積むだけで、実際に描くのはバニラがあとのフレームで**行う（IL 事実 §C）。
    /// つまり⑤が複製を <c>DispatchEffect</c> して、その直後にレベルアンロードで
    /// 複製を <c>Destroy</c> すると、**バニラの中で破棄済みオブジェクトを触ることになる。**
    /// だから積むのは <b>ゲーム自身のプレハブ（<c>Medium Explosion Particles</c> そのもの）</b>
    /// だけにする。あれはゲームが持っていて、⑤が消すことは決して無い。
    ///
    /// ★ 1 バイトも書き換えないので、共有しても安全である（§D-5 の規律）。
    ///   <c>m_renderDuration</c> が 1.0 秒あるので、**1 回積むだけで
    ///   <c>m_intensityCurve</c> に沿って減衰して消える** —— 一発ものはこれが正解。
    ///
    /// ── 飛ぶ岩は複製する（尾を引かせたい）───────────────────────────
    ///
    /// 岩そのものの位置は <c>Core/Volcano/EjectaBallistics</c> が決め、こちらは
    /// **その 1 点に小さな粒子の玉を毎フレーム湧かせる**（継続モード）。
    /// 素の <c>Medium Explosion Particles</c> は初速 100–150 m/s で四方へ散るので、
    /// **玉ではなく破裂**に見える。複製して初速を落とし、寿命を短くし、
    /// 重力を弱く効かせて<b>飛跡</b>にする。
    /// </summary>
    internal static partial class VolcanoVanillaFx
    {
        /// <summary>飛ぶ岩の尾。爆発と同じプレハブから作る。**基本ゲーム。**</summary>
        internal const string BlockName = EjectaName;

        private static int _blastMiss;
        private static bool _blastMissLogged;
        private static bool _blastOk;

        // ★ 配列にしない。参照 1 個ずつ。
        private static GameObject _blockObject;
        private static ParticleEffect _blockClone;
        private static int _blockMiss;
        private static bool _blockMissLogged;
        private static bool _blockCloneRefused;
        private static bool _blockOk;
        private static bool _blockCloned;

        /// <summary>
        /// 爆発（<c>DispatchEffect</c> で積む一発もの）。**ゲーム自身のプレハブそのもの**で、
        /// 複製ではない（クラス doc）。引けなければ null ——
        /// **その 1 つを出さないだけで、噴火は続く。**
        /// </summary>
        internal static ParticleEffect BlastOneShot()
        {
            ParticleEffect source = Source(EjectaName, ref _blastMiss, ref _blastMissLogged);

            // ★ 初期化されていないプレハブを描画へ渡さない（Ready の doc）。
            //   DispatchEffect 経由でも最後に走るのは同じ EmitParticles である。
            ParticleEffect resolved = source != null && Ready(source) ? source : null;
            _blastOk = resolved != null;
            return resolved;
        }

        /// <summary>飛んでいる岩の尾。**複製**（引けなければ元のプレハブ、それも駄目なら null）。</summary>
        internal static ParticleEffect FlyingBlock()
        {
            ParticleEffect resolved = ResolveBlock();
            _blockOk = resolved != null;
            _blockCloned = _blockClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveBlock()
        {
            if (_blockClone != null) return _blockClone;

            // ★ 間引きカウンタは爆発とも噴石とも別にする（共有すると同じフレームで
            //   複数回減って RetryFrames が実質割れる）。
            ParticleEffect source = Source(BlockName, ref _blockMiss, ref _blockMissLogged);
            if (source == null) return null;

            if (_blockCloneRefused) return Ready(source) ? source : null;

            ReleaseAndDestroy(ref _blockObject, ref _blockClone);

            _blockObject = CloneFlyingBlock(source);
            _blockClone = _blockObject == null
                ? null : _blockObject.GetComponent<ParticleEffect>();
            if (_blockClone == null)
            {
                ReleaseAndDestroy(ref _blockObject, ref _blockClone);
                _blockCloneRefused = true;
                return Ready(source) ? source : null;
            }

            return _blockClone;
        }

        /// <summary>
        /// 飛ぶ岩の複製。**その場に留まる小さな熱い玉**にする ——
        /// 動かすのは⑤（<c>Core/Volcano/EjectaBallistics</c> が出した位置に
        /// 毎フレーム湧かせ直す）なので、粒子自身は飛ばなくてよい。
        ///
        /// ★ <c>emission.rateOverTime</c> を 0 にしないこと（Clones のクラス doc）。
        /// </summary>
        private static GameObject CloneFlyingBlock(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoFlyingBlock");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            // 尾。1 秒足らずで消えるので、湧いた場所に短く残って線に見える。
            effect.m_minLifeTime = 0.35f;
            effect.m_maxLifeTime = 0.95f;
            effect.m_minStartSpeed = 1.5f;
            effect.m_maxStartSpeed = 7f;
            effect.m_minSpawnAngle = 0f;
            effect.m_maxSpawnAngle = 180f;
            effect.m_maxVisibilityDistance = 10000f;
            // ★ 素は 1.0 秒。継続モードで自分で窓を作るので 0 に落とす。
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.72f, 0.28f, 1f), new Color(0.30f, 0.13f, 0.09f, 1f));
            main.startSize = 9f;
            main.gravityModifier = 0.35f;
            main.maxParticles = 2000;

            var emission = particles.emission;
            emission.rateOverTime = 90f;

            return Initialize(go, effect);
        }

        /// <summary>
        /// 爆発と噴石の後始末。**<see cref="VolcanoVanillaFx.Destroy"/> から呼ぶ。**
        /// 爆発側は借りているだけなので参照すら持たない（数える旗を戻すだけ）。
        /// </summary>
        private static void DestroyBlast()
        {
            ReleaseAndDestroy(ref _blockObject, ref _blockClone);
            _blockCloneRefused = false;
            _blockMiss = 0;
            _blastMiss = 0;
            _blastOk = false;
            _blockOk = false;
            _blockCloned = false;
            // ★ *MissLogged は戻さない（ゲームのビルドに対する事実である）。
        }

        /// <summary>診断に出す 1 行の一部（**英語**）。sim スレッドから読む平の値だけ。</summary>
        internal static string BlastDetail
        {
            get
            {
                return "blast=" + (_blastOk ? "shared" : "MISSING")
                       + ", flying blocks=" + State(_blockOk, _blockCloned, _blockCloneRefused);
            }
        }
    }
}
