namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 噴煙の粒子 1 個ぶんの性質。**バニラの <c>ParticleEffect</c> ＋ <c>ParticleSystem</c> の
    /// 複製へそのまま書き込む数値表**である（<c>Game/Volcano/VolcanoVanillaFx.Clones</c>）。
    ///
    /// ── なぜ Core に在るのか ─────────────────────────────────
    ///
    /// これらは「粒子がどう見えるか」を決める⑤の演出値であって、Unity の API ではない。
    /// Core に置いてあるので <c>tools/VolcanoPreview</c> が**ゲームを起動せずに同じ数字で
    /// 噴煙柱を描ける** ——「見た目の変更は自分でオフラインに描画・計測してから実機テストを
    /// 頼む」というこのプロジェクトの決まりは、数字が 2 か所にあると成立しない。
    ///
    /// ── 実機側の対応（IL 実測 §A-6 / §B-4）────────────────────────
    ///
    /// <code>
    /// LifeMinSeconds / LifeMaxSeconds  -> ParticleEffect.m_min/maxLifeTime
    /// SpeedMin / SpeedMax              -> ParticleEffect.m_min/maxStartSpeed
    /// SpawnAngleMinDegrees / Max       -> ParticleEffect.m_min/maxSpawnAngle
    /// SizeMetres                       -> ParticleSystem.main.startSize
    /// GravityModifier                  -> ParticleSystem.main.gravityModifier
    /// RateOverTime                     -> ParticleSystem.emission.rateOverTime  ★ 0 にしない
    /// MaxParticles                     -> ParticleSystem.main.maxParticles
    /// </code>
    ///
    /// <c>rateOverTime</c> を 0 にすると 1 粒も出ない（<c>emission.enabled</c> が false でも
    /// <c>EmitParticles</c> が粒子数の乗数として読み続ける）。いちばん踏みやすい罠である。
    /// </summary>
    public struct EruptionAshProfile
    {
        /// <summary>寿命の下限（秒）。</summary>
        public readonly float LifeMinSeconds;

        /// <summary>寿命の上限（秒）。</summary>
        public readonly float LifeMaxSeconds;

        /// <summary>初速の下限（m/秒）。**柱の形は初速ではなく「どこに湧かせるか」で作る。**</summary>
        public readonly float SpeedMin;

        /// <summary>初速の上限（m/秒）。</summary>
        public readonly float SpeedMax;

        /// <summary>放出角の下限（度）。0 が軸方向（＝上）、90 が真横である。</summary>
        public readonly float SpawnAngleMinDegrees;

        /// <summary>放出角の上限（度）。</summary>
        public readonly float SpawnAngleMaxDegrees;

        /// <summary>粒 1 個の大きさ（m）。</summary>
        public readonly float SizeMetres;

        /// <summary>重力の倍率（負なら浮く）。</summary>
        public readonly float GravityModifier;

        /// <summary>粒子数の乗数。**0 にしない。**</summary>
        public readonly float RateOverTime;

        /// <summary>この粒子系が抱える粒の上限。超えた分は自動で絞られる（§B-4）。</summary>
        public readonly int MaxParticles;

        public EruptionAshProfile(float lifeMinSeconds, float lifeMaxSeconds,
                                  float speedMin, float speedMax,
                                  float spawnAngleMinDegrees, float spawnAngleMaxDegrees,
                                  float sizeMetres, float gravityModifier,
                                  float rateOverTime, int maxParticles)
        {
            LifeMinSeconds = lifeMinSeconds;
            LifeMaxSeconds = lifeMaxSeconds;
            SpeedMin = speedMin;
            SpeedMax = speedMax;
            SpawnAngleMinDegrees = spawnAngleMinDegrees;
            SpawnAngleMaxDegrees = spawnAngleMaxDegrees;
            SizeMetres = sizeMetres;
            GravityModifier = gravityModifier;
            RateOverTime = rateOverTime;
            MaxParticles = maxParticles;
        }

        /// <summary>
        /// <b>柱</b>の粒子。暗い灰褐色で、**短命**（4〜9 秒）で、ほとんど自走しない。
        ///
        /// ★★ 初速を落としてあるのが指摘③の直しの中身である。素の
        ///   <c>Factory Smoke</c> は 10〜15 m/s、⑤も以前は 26〜48 m/s を入れていた。
        ///   粒子が自分で上がり続けると**傘に天井が出来ない** ——
        ///   中立浮力高度で止まって横へ広がるのが噴火柱の姿である。
        ///   いまは形を <see cref="EruptionColumn"/> の 9 段が決め、粒子は
        ///   湧いた場所で少し膨らんで消えるだけにする。
        /// </summary>
        public static EruptionAshProfile Column
        {
            get { return new EruptionAshProfile(4f, 9f, 3f, 11f, 0f, 22f, 36f, -0.02f, 42f, 6000); }
        }

        /// <summary>
        /// <b>傘</b>の粒子。淡くて大きくて**長生き**（18〜34 秒）で、
        /// 放出角 55〜95 度＝**ほぼ水平に広がる**（軸が上向きなので、この角度が
        /// そのまま横向きの初速になる）。滞留して積み上がることで平たい面になる。
        /// </summary>
        public static EruptionAshProfile Umbrella
        {
            get { return new EruptionAshProfile(18f, 34f, 5f, 13f, 55f, 95f, 95f, 0.01f, 30f, 6000); }
        }
    }
}
