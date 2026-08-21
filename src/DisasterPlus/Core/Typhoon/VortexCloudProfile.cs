namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 渦の雲の粒 1 個ぶんの性質。**バニラの <c>ParticleEffect</c> ＋ <c>ParticleSystem</c> の
    /// 複製へそのまま書き込む数値表**である（<c>Game/Typhoon/TyphoonCloudFx.Clones</c>）。
    ///
    /// ── なぜ Core に在るのか ─────────────────────────────────
    ///
    /// ⑤の <c>EruptionAshProfile</c> とまったく同じ理由である。これらは
    /// 「粒子がどう見えるか」を決める④の演出値であって Unity の API ではない。
    /// Core に置いてあるので <c>tools/TyphoonPreview</c> が**ゲームを起動せずに
    /// 同じ数字で渦を描ける** ——「見た目の変更は自分でオフラインに描画・計測してから
    /// 実機テストを頼む」というこのプロジェクトの決まりは、数字が 2 か所にあると
    /// 成立しない。
    ///
    /// ── 実機側の対応（エフェクト実測文書 §A-6 / §B-4）────────────────────
    ///
    /// <code>
    /// LifeMinSeconds / LifeMaxSeconds  -> ParticleEffect.m_min/maxLifeTime
    /// SpeedMin / SpeedMax              -> ParticleEffect.m_min/maxStartSpeed
    /// SpawnAngleMinDegrees / Max       -> ParticleEffect.m_min/maxSpawnAngle
    /// SizeFraction x 渦の外周半径       -> ParticleSystem.main.startSize
    /// GravityModifier                  -> ParticleSystem.main.gravityModifier
    /// RateOverTime                     -> ParticleSystem.emission.rateOverTime  ★ 0 にしない
    /// MaxParticles                     -> ParticleSystem.main.maxParticles
    /// Red/Green/Blue Bright/Dark       -> ParticleSystem.main.startColor（2 色の階調）
    /// </code>
    ///
    /// <c>rateOverTime</c> を 0 にすると 1 粒も出ない（<c>emission.enabled</c> が false でも
    /// <c>EmitParticles</c> が粒子数の乗数として読み続ける）。いちばん踏みやすい罠である。
    ///
    /// ── ★ 色は「入道雲」そのものである ────────────────────────────
    ///
    /// 積乱雲は<b>上が日に照らされて白く、下は自分の厚みで光を通さず暗い</b>。
    /// 粒子の色は <c>ParticleSystem</c> 側の共有状態で <c>RenderEffect</c> ごとには
    /// 変えられない（§B-4）ので、**明るさを変えたい単位がそのまま複製の数**になる。
    /// ④は 3 つ（雲底・塔・かなとこ）に割った。
    ///
    /// 旧実装は 1 つの複製に <c>(0.60, 0.62, 0.66, 0.62)</c> という均一な灰色を入れていた。
    /// 明暗が無いので、どんな形に置いても**煙の塊**にしか見えない。
    /// </summary>
    public struct VortexCloudProfile
    {
        /// <summary>寿命の下限（秒）。</summary>
        public readonly float LifeMinSeconds;

        /// <summary>寿命の上限（秒）。</summary>
        public readonly float LifeMaxSeconds;

        /// <summary>初速の下限（m/秒）。**渦の形は初速ではなく「どこに湧かせるか」で作る。**</summary>
        public readonly float SpeedMin;

        /// <summary>初速の上限（m/秒）。</summary>
        public readonly float SpeedMax;

        /// <summary>放出角の下限（度）。0 が軸方向（＝上）、90 が真横である。</summary>
        public readonly float SpawnAngleMinDegrees;

        /// <summary>放出角の上限（度）。</summary>
        public readonly float SpawnAngleMaxDegrees;

        /// <summary>粒 1 個の大きさ ÷ 渦の外周半径。</summary>
        public readonly float SizeFraction;

        /// <summary>重力の倍率（負なら浮く）。</summary>
        public readonly float GravityModifier;

        /// <summary>粒子数の乗数。**0 にしない。**</summary>
        public readonly float RateOverTime;

        /// <summary>この粒子系が抱える粒の上限。超えた分は自動で絞られる（§B-4）。</summary>
        public readonly int MaxParticles;

        /// <summary>明るい側の色（0..1）。</summary>
        public readonly float BrightRed;

        public readonly float BrightGreen;

        public readonly float BrightBlue;

        /// <summary>暗い側の色（0..1）。<c>startColor</c> はこの 2 色のあいだで抽選される。</summary>
        public readonly float DarkRed;

        public readonly float DarkGreen;

        public readonly float DarkBlue;

        /// <summary>不透明度（0..1）。**重なって濃くなる**ので 1 にはしない。</summary>
        public readonly float Alpha;

        public VortexCloudProfile(float lifeMinSeconds, float lifeMaxSeconds,
                                  float speedMin, float speedMax,
                                  float spawnAngleMinDegrees, float spawnAngleMaxDegrees,
                                  float sizeFraction, float gravityModifier,
                                  float rateOverTime, int maxParticles,
                                  float brightRed, float brightGreen, float brightBlue,
                                  float darkRed, float darkGreen, float darkBlue,
                                  float alpha)
        {
            LifeMinSeconds = lifeMinSeconds;
            LifeMaxSeconds = lifeMaxSeconds;
            SpeedMin = speedMin;
            SpeedMax = speedMax;
            SpawnAngleMinDegrees = spawnAngleMinDegrees;
            SpawnAngleMaxDegrees = spawnAngleMaxDegrees;
            SizeFraction = sizeFraction;
            GravityModifier = gravityModifier;
            RateOverTime = rateOverTime;
            MaxParticles = maxParticles;
            BrightRed = brightRed;
            BrightGreen = brightGreen;
            BrightBlue = brightBlue;
            DarkRed = darkRed;
            DarkGreen = darkGreen;
            DarkBlue = darkBlue;
            Alpha = alpha;
        }

        /// <summary>
        /// <b>雲底</b>。**暗い青灰色**で、大きく、ほとんど動かず、わずかに沈む。
        /// 入道雲の下面は雨を降らせている側なので、いちばん暗い。
        /// 放出角を広く取って**横に平たく**広がらせる（軸は上向きなので、
        /// 角度が大きいほど横向きの初速になる）。
        /// </summary>
        public static VortexCloudProfile Deck
        {
            get
            {
                return new VortexCloudProfile(
                    14f, 26f, 3f, 9f, 62f, 98f,
                    VortexPuffLayout.DeckSizeFraction, 0.004f, 20f, 2600,
                    0.42f, 0.45f, 0.52f,
                    0.17f, 0.19f, 0.24f,
                    0.55f);
            }
        }

        /// <summary>
        /// <b>塔</b>。**明るい灰白色**で、粒が小さく、上へ盛り上がる。
        /// 放出角を狭く（0〜38 度）取ってあるので粒は上向きに散り、
        /// <see cref="VortexPuffLayout"/> が積んだ 2 段の帯とあわせて
        /// <b>もこもこした縦の塊</b>になる。
        /// </summary>
        public static VortexCloudProfile Tower
        {
            get
            {
                return new VortexCloudProfile(
                    11f, 22f, 5f, 14f, 0f, 38f,
                    VortexPuffLayout.TowerSizeFraction, -0.010f, 20f, 3600,
                    0.96f, 0.97f, 0.99f,
                    0.56f, 0.59f, 0.66f,
                    0.50f);
            }
        }

        /// <summary>
        /// <b>かなとこ</b>。**いちばん白く、いちばん大きく、いちばん長生き**（26〜48 秒）。
        /// 放出角 70〜100 度＝**ほぼ水平に広がる**。滞留して積み上がることで
        /// 平たい天蓋になる（⑤の噴煙の傘と同じ作り）。
        /// </summary>
        public static VortexCloudProfile Canopy
        {
            get
            {
                return new VortexCloudProfile(
                    26f, 48f, 4f, 12f, 70f, 100f,
                    VortexPuffLayout.CanopySizeFraction, 0.002f, 20f, 1800,
                    1f, 1f, 1f,
                    0.74f, 0.77f, 0.84f,
                    0.34f);
            }
        }

        /// <summary>層から数値表を引く。**Game も preview もここを通る。**</summary>
        public static VortexCloudProfile Of(VortexCloudLayer layer)
        {
            if (layer == VortexCloudLayer.Deck) return Deck;
            if (layer == VortexCloudLayer.Canopy) return Canopy;
            return Tower;
        }
    }
}
