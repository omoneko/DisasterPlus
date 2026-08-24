using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 実機報告（2026-08-22）「カルデラ内部が平地になるのはおかしい（元の地形や
    /// 火山の山体の残骸も加味してリアルに寄せてください）」。
    ///
    /// ★★ <b>「まっ平らでないこと」はテストできる。</b>
    ///    落ちた屋根は 1 枚の板のまま着地するのではなく、割れて岩塊の山になる。
    /// </summary>
    public class CalderaFloorTests
    {
        private const float ConeRadius = 2928f;
        private const float ConeHeight = 1000f;
        private const uint Seed = 20260822u;

        private static float R { get { return SuperEruption.CalderaRadiusMetres(ConeRadius); } }
        private static float D { get { return SuperEruption.CalderaDepthMetres(ConeHeight); } }

        private static float F(float dx, float dz)
        {
            return SuperEruption.CalderaFloorOffsetAt(dx, dz, R, D, Seed);
        }

        [Fact]
        public void TheFloorIsNotFlat()
        {
            // ★★ これが所有者の指摘そのもの。床の高さが 1 つの値に潰れていないこと。
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 60; i++)
            {
                float d = R * SuperEruption.FloorFraction * i / 60f;
                seen.Add((int)(F(d, 0f) / 5f));   // 5 m 刻み
            }

            Assert.True(seen.Count > 12,
                        "the caldera floor only has " + seen.Count + " distinct heights");
        }

        [Fact]
        public void TheFloorHasARaisedCentre()
        {
            // 実在の大カルデラには中央火口丘がある。無いと「まっさらな鉢」に見える。
            float centre = F(0f, 0f);
            float outer = F(R * 0.6f, 0f);

            Assert.True(centre > outer,
                        "the centre (" + centre + ") is not raised over the floor ("
                        + outer + ")");
        }

        [Fact]
        public void NothingInTheCalderaEverRisesAboveTheOriginalGround()
        {
            // でこぼこも中央火口丘も、元の地面より上には出ない ——
            // 出ると、陥没したはずのカルデラの中に元の高さの島が残る。
            for (int i = 0; i <= 240; i++)
            {
                float d = R * 1.3f * i / 240f;
                Assert.True(F(d, 0f) <= 0f, "the caldera floor rose above ground at " + d);
                Assert.True(F(0f, d) <= 0f, "the caldera floor rose above ground at z=" + d);
            }
        }

        [Fact]
        public void TheBumpinessDiesOutAtTheRim()
        {
            // 縁の外に岩塊がぽつぽつ残らないこと。
            Assert.Equal(0f, F(R, 0f), 3);
            Assert.Equal(0f, F(R * 1.5f, 0f), 3);
            Assert.Equal(0f, F(R * 4f, 0f), 3);
        }

        [Fact]
        public void TheFloorNeverGoesUnreasonablyDeeperThanTheCaldera()
        {
            float worst = 0f;
            for (int i = 0; i <= 300; i++)
            {
                float d = R * i / 300f;
                float v = F(d, 0f);
                if (v < worst) worst = v;
            }

            // でこぼこの幅ぶんは超えてよいが、それ以上は超えないこと。
            Assert.True(worst >= -D * (1f + SuperEruption.FloorRoughFraction) - 1f,
                        "the floor reached " + worst + " for a " + D + " m caldera");
        }

        [Fact]
        public void TheSameVolcanoAlwaysGetsTheSameFloor()
        {
            for (int i = 0; i < 40; i++)
            {
                float d = R * i / 40f;
                Assert.Equal(F(d, 0f), F(d, 0f), 5);
            }
        }

        [Fact]
        public void ADifferentVolcanoGetsADifferentFloor()
        {
            bool different = false;
            for (int i = 0; i < 60; i++)
            {
                float d = R * i / 60f;
                if (SuperEruption.CalderaFloorOffsetAt(d, 0f, R, D, Seed)
                    != SuperEruption.CalderaFloorOffsetAt(d, 0f, R, D, Seed + 1u))
                {
                    different = true;
                    break;
                }
            }

            Assert.True(different, "every volcano gets the same caldera floor");
        }

        [Fact]
        public void BrokenInputMovesNoGround()
        {
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(float.NaN, 0f, R, D, Seed), 4);
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(0f, float.NaN, R, D, Seed), 4);
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(0f, 0f, 0f, D, Seed), 4);
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(0f, 0f, R, 0f, Seed), 4);
        }

        [Fact]
        public void ACalderaOnHighGroundStaysAboveSeaLevel()
        {
            // ★★ 所有者の問い「カルデラ内部の標高が必ず海抜より低くなる理由は
            //    何ですか？」への答えの検査。**「必ず」ではなくなったこと。**
            //    ゲームの海面は 40 m（WaterSimulation.DEFAULT_SEA_LEVEL、IL 実測）。
            const float seaLevel = 40f;

            float highGround = 600f;
            Assert.True(highGround - D > seaLevel,
                        "even on 600 m ground the caldera floor (" + (highGround - D)
                        + " m) is below sea level");

            // 海に近い土地では水没してよい（サントリーニ・クラカタウ）。
            float lowGround = 90f;
            Assert.True(lowGround - D < seaLevel);
        }
    }
}
