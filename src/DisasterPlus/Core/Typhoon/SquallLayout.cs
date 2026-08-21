using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 吹き付ける雨（squall）を 1 か所ぶん湧かすための指示。**全部「散らばりの半径に
    /// 対する比」**で、ワールド座標へ直すのは <c>Game/Typhoon/TyphoonSquallFx</c> の仕事。
    /// </summary>
    public struct SquallPatch
    {
        /// <summary>中心からの横のずれ ÷ 散らばりの半径。</summary>
        public readonly float OffsetXFraction;

        /// <summary>同上（奥行き）。</summary>
        public readonly float OffsetZFraction;

        /// <summary>湧かす高さ ÷ 吹き付けの高さ [0, 1]。</summary>
        public readonly float HeightFraction;

        /// <summary>円盤の半径 ÷ 散らばりの半径。</summary>
        public readonly float DiscFraction;

        /// <summary>上への伸び ÷ 吹き付けの高さ。</summary>
        public readonly float BandFraction;

        /// <summary>濃さの比 [0, 1]。</summary>
        public readonly float DensityFraction;

        public SquallPatch(float offsetXFraction, float offsetZFraction, float heightFraction,
                           float discFraction, float bandFraction, float densityFraction)
        {
            OffsetXFraction = offsetXFraction;
            OffsetZFraction = offsetZFraction;
            HeightFraction = heightFraction;
            DiscFraction = discFraction;
            BandFraction = bandFraction;
            DensityFraction = densityFraction;
        }
    }

    /// <summary>
    /// <b>暴風雨</b>を地上で見せるための純データ。**Core なのでエンジンには触れない。**
    ///
    /// ── なぜ要るのか（持ち主の指摘 2026-08-22）─────────────────────────
    ///
    /// &gt; 暴風雨を再現してほしいです。
    ///
    /// ④が今まで「嵐の強さ」として出せていたのは <c>m_targetRain</c> /
    /// <c>m_targetCloud</c> の 2 つだけで、どちらも**空全体の設定**である。
    /// 雨は真下に降り、風の向きにも台風の位置にも従わない。
    /// 地上に居るプレイヤーから見ると、強い台風も弱い雨も同じ絵になる。
    ///
    /// **バニラの雨量はもう上限に張り付いている**（最盛期で 1.0）。しかも
    /// <c>m_currentRain &gt; 0.8</c> はゲーム自身に雷雨を作らせる境界で、
    /// ④の落雷の予算はその手前で釣り合いを取っている（設計書 §4.2）。
    /// **これ以上「雨量」を上げる余地は無い。**
    ///
    /// だから足すのは<b>横殴りの飛沫</b>である。カメラの周りに
    /// <see cref="PatchCount"/> か所、風下へ向かう速度を持った水の粒を湧かす。
    /// バニラの雨と違って**風向きに沿って流れる**ので、暴風雨に見える。
    ///
    /// ── ★ 台風の中心ではなくカメラの周りに置く ────────────────────────
    ///
    /// 渦（<see cref="VortexPuffLayout"/>）は台風の中心に置く。こちらは違う ——
    /// <b>「自分が居るところが吹き荒れている」を見せるもの</b>なので、カメラの下に置く。
    /// 台風が 5 km 先に居るときに 5 km 先で飛沫が舞っても、画面には何も起きない。
    ///
    /// 強さは <see cref="StrengthOf"/> が <c>TyphoonProfile.WindAt</c> の
    /// 風速相当から決める。<see cref="MinWindUnit"/> を下回ると**1 粒も出さない** ——
    /// 台風の外に居るのに飛沫が舞うほうがおかしい。
    ///
    /// ── 風の向き ────────────────────────────────────────
    ///
    /// <see cref="WindDirection"/> が台風の二次循環（接線＋吸い込み）を返す。
    /// <b>渦の回る向きと同じ</b>（<see cref="VortexPuffLayout"/> の粒も
    /// 角度が増える向きへ流れる）。ここがずれると、雲と雨が逆向きに流れる。
    ///
    /// ── 揺らぎ ────────────────────────────────────────
    ///
    /// 置き場所は <see cref="DeterministicRandom"/> の**添字だけの関数**である。
    /// フレームを混ぜると、飛沫の湧く場所が毎フレーム跳んでちらつく。
    /// <c>System.Random</c> は使わない（本 MOD 全体の規律）。
    /// </summary>
    public static class SquallLayout
    {
        /// <summary>1 フレームに撃つ <c>RenderEffect</c> の本数。**これが上限である。**</summary>
        public const int PatchCount = 9;

        /// <summary>
        /// これを下回る風速相当では 1 粒も出さない。
        /// **台風の外（強風域の外）は必ず 0 になる**（<c>WindAt</c> がそこで 0 を返す）。
        /// </summary>
        public const float MinWindUnit = 0.18f;

        /// <summary>吹き付けが最大になる風速相当。ここで <see cref="StrengthOf"/> が 1。</summary>
        public const float FullWindUnit = 0.75f;

        /// <summary>吸い込み成分 ÷ 接線成分。台風は中心へ巻き込みながら回る。</summary>
        public const float InflowFraction = 0.35f;

        // ── 粒子の性質（複製へそのまま書き込む数値表）──────────────────────
        //
        // ★ Core に在る理由は VortexCloudProfile と同じ。tools/TyphoonPreview が
        //   ゲームを起動せずに同じ数字で飛沫を描くので、**2 か所に書かない**。
        //   実機側の対応は Game/Typhoon/TyphoonSquallFx.Clone に 1 対 1 で写してある。

        /// <summary>散らばりの半径（m）。カメラの真下の地面にこの広さで撒く。</summary>
        public const float SpreadMetres = 300f;

        /// <summary>
        /// カメラの高さがこれだけ上がるごとに <see cref="SpreadMetres"/> が 1 倍ぶん増える。
        /// **引くほど広く撒かないと、画面の真ん中に小さな染みが出るだけになる。**
        /// </summary>
        public const float SpreadReferenceHeightMetres = 700f;

        /// <summary>散らばりの半径の上限（m）。上げるほど 1 粒あたりの密度が下がる。</summary>
        public const float MaxSpreadMetres = 1200f;

        /// <summary>
        /// 吹き付けの高さ（m）。**低い** —— <b>地面から</b>この高さまでの帯に撒く。
        /// 建物の高さの帯である（雲ではない）。
        /// </summary>
        public const float HeightMetres = 140f;

        /// <summary>1 粒の大きさ（m）。</summary>
        public const float SizeMetres = 14f;

        /// <summary>寿命（秒）。**短い** —— 飛んで落ちて消える。</summary>
        public const float LifeMinSeconds = 0.9f;

        public const float LifeMaxSeconds = 1.9f;

        /// <summary>初速（m/秒）。<c>velocity</c> 引数の風とは別に、粒自身が散る速さ。</summary>
        public const float SpeedMin = 14f;

        public const float SpeedMax = 34f;

        /// <summary>放出角（度）。0 が軸方向（＝上）、90 が真横。**ほぼ水平に散る。**</summary>
        public const float SpawnAngleMinDegrees = 66f;

        public const float SpawnAngleMaxDegrees = 104f;

        /// <summary>重力の倍率。**正＝落ちる**（雨だから）。</summary>
        public const float GravityModifier = 1.15f;

        /// <summary>粒子数の乗数。**0 にしない**（0 だと 1 粒も出ない。§D-2 の罠）。</summary>
        public const float RateOverTime = 20f;

        /// <summary>生きている粒の上限。渦（8000）とは別枠である。</summary>
        public const int MaxParticles = 3000;

        /// <summary>渦全体で 1 秒あたりに湧かす粒子の数。</summary>
        public const float ParticlesPerSecond = 900f;

        /// <summary>見える距離（m）。借り元は 500〜2000 m しか無いので上げる。</summary>
        public const float VisibilityMetres = 3000f;

        /// <summary>風に乗って流される速さ（m/秒）。強さ 1 でこの値。</summary>
        public const float DriftMetresPerSecond = 46f;

        /// <summary>明るい側の色（0..1）。白い飛沫。</summary>
        public const float BrightRed = 0.94f;

        public const float BrightGreen = 0.96f;

        public const float BrightBlue = 1f;

        /// <summary>暗い側の色（0..1）。雨のすじは空より暗い。</summary>
        public const float DarkRed = 0.62f;

        public const float DarkGreen = 0.68f;

        public const float DarkBlue = 0.78f;

        /// <summary>不透明度。**低い** —— 都市が見えなくなってはいけない。</summary>
        public const float Alpha = 0.30f;

        /// <summary>揺らぎの種。**固定値**（添字だけの関数にするため）。</summary>
        private const uint Seed = 0x53515544u;   // "SQUD"

        private const float TwoPi = 6.28318530718f;

        /// <summary>
        /// カメラの高さ <paramref name="cameraHeightAboveGround"/>（m、地面から）での
        /// 散らばりの半径（m）。引くほど広く撒く（クラス doc の
        /// <see cref="SpreadReferenceHeightMetres"/>）。負・NaN は下限。
        /// </summary>
        public static float SpreadFor(float cameraHeightAboveGround)
        {
            if (!(cameraHeightAboveGround > 0f)) return SpreadMetres;

            float spread = SpreadMetres
                           * (1f + cameraHeightAboveGround / SpreadReferenceHeightMetres);
            if (float.IsNaN(spread) || spread < SpreadMetres) return SpreadMetres;
            if (spread > MaxSpreadMetres) return MaxSpreadMetres;
            return spread;
        }

        /// <summary>
        /// 風速相当 [0, 1] を吹き付けの強さ [0, 1] へ。
        /// <see cref="MinWindUnit"/> 以下で 0、<see cref="FullWindUnit"/> 以上で 1。
        /// NaN は 0。
        /// </summary>
        public static float StrengthOf(float windUnit)
        {
            if (float.IsNaN(windUnit)) return 0f;
            if (windUnit <= MinWindUnit) return 0f;
            if (windUnit >= FullWindUnit) return 1f;
            return (windUnit - MinWindUnit) / (FullWindUnit - MinWindUnit);
        }

        /// <summary>
        /// 台風の中心から見た地点 <c>(dx, dz)</c> での風の向き（単位ベクトル）。
        /// 接線（渦の回る向き）＋ <see cref="InflowFraction"/> ぶんの吸い込み。
        ///
        /// 中心のちょうど真上（距離 0）は向きが決まらないので <c>(1, 0)</c> を返す ——
        /// **例外は投げない**（毎フレーム回る経路である）。
        /// </summary>
        public static void WindDirection(float dx, float dz, out float wx, out float wz)
        {
            float d2 = dx * dx + dz * dz;
            if (!(d2 > 1e-6f) || float.IsNaN(d2))
            {
                wx = 1f;
                wz = 0f;
                return;
            }

            float d = (float)System.Math.Sqrt(d2);
            float ux = dx / d;
            float uz = dz / d;

            // 接線は角度が増える向き（渦の粒と同じ）。吸い込みは中心へ向かう。
            float tx = -uz - InflowFraction * ux;
            float tz = ux - InflowFraction * uz;

            float len = (float)System.Math.Sqrt(tx * tx + tz * tz);
            if (!(len > 0f))
            {
                wx = 1f;
                wz = 0f;
                return;
            }

            wx = tx / len;
            wz = tz / len;
        }

        /// <summary>
        /// 吹き付け <paramref name="index"/> か所目の置き場所。全部**正規化した比**である。
        ///
        /// **範囲外の添字は例外を投げず 0 に丸める。** 毎フレーム回る経路なので、
        /// 呼び出し側の数え違いでレベルロードを壊さない。
        /// </summary>
        public static SquallPatch PatchAt(int index)
        {
            if (index < 0 || index >= PatchCount) index = 0;

            // 1 つ目はカメラの真下、残りは 2 重の環。**等間隔にしない**（機械的に見える）。
            float ring;
            float angle;
            if (index == 0)
            {
                ring = 0f;
                angle = 0f;
            }
            else if (index <= 4)
            {
                ring = 0.5f;
                angle = TwoPi * (index - 1) / 4f;
            }
            else
            {
                ring = 1f;
                angle = TwoPi * (index - 5) / 4f + TwoPi * 0.125f;
            }

            angle += (DeterministicRandom.Unit(Seed, (uint)index) * 2f - 1f) * 0.35f;
            ring += (DeterministicRandom.Unit(Seed + 1u, (uint)index) * 2f - 1f) * 0.12f;
            if (ring < 0f) ring = 0f;
            if (ring > 1.1f) ring = 1.1f;

            float offsetX = (float)System.Math.Cos(angle) * ring;
            float offsetZ = (float)System.Math.Sin(angle) * ring;

            // 高さは低め（地上の飛沫）。外側ほど少し高く、少し薄く。
            float height = 0.06f + 0.30f * ring
                           + (DeterministicRandom.Unit(Seed + 2u, (uint)index) * 2f - 1f) * 0.05f;
            if (height < 0f) height = 0f;
            if (height > 1f) height = 1f;

            // 内側は締まって濃く、外側は広がって薄く（風下へ流れて散る）。
            float disc = 0.28f + 0.22f * ring;
            float band = 0.30f + 0.25f * ring;
            float density = 1f - 0.35f * ring;

            return new SquallPatch(offsetX, offsetZ, height, disc, band, density);
        }
    }
}
