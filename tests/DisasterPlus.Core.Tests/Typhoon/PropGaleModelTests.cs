using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// 所有者の依頼（2026-08-22）「街に暴風および大雨による小さな建物の破壊や
    /// 看板プロップの破壊…を発生させることです」。
    ///
    /// ★★ <c>PropManager.ReleaseProp</c> は<b>取り消せない</b>。だからここが
    ///    固定するのは「壊れること」より<b>壊れすぎないこと</b>である ——
    ///    「台風が来たら看板が全部消える」は直せない壊れ方である。
    /// </summary>
    public class PropGaleModelTests
    {
        private const uint Seed = 777u;

        [Fact]
        public void SignsGoAndBigThingsStay()
        {
            Assert.Equal(1f, PropGaleModel.FragilityOf(1.2f), 4);      // 標識
            Assert.Equal(1f, PropGaleModel.FragilityOf(3f), 4);        // 看板
            Assert.Equal(0f, PropGaleModel.FragilityOf(12f), 4);       // 給水塔
            Assert.InRange(PropGaleModel.FragilityOf(6f), 0.01f, 0.99f); // 街灯
        }

        [Fact]
        public void NothingMovesInAnOrdinaryBreeze()
        {
            // ★ 台風の外側では 1 つも飛ばないこと。
            Assert.Equal(0f, PropGaleModel.TakeChance(10f, 1f), 5);
            Assert.Equal(0f, PropGaleModel.TakeChance(PropGaleModel.MinWindMetresPerSecond, 1f), 5);
        }

        [Fact]
        public void EvenTheWorstStormNeverTakesEverythingAtOnce()
        {
            // ★★ **ここが本丸である。** 1 回の判定で全部持っていかない。
            float worst = PropGaleModel.TakeChance(500f, 1f);
            Assert.True(worst <= PropGaleModel.MaxTakeRatio + 1e-4f,
                        "a single pass can take " + (worst * 100f) + "% of the props");
        }

        [Fact]
        public void AStrongerWindTakesMore()
        {
            float weak = PropGaleModel.TakeChance(30f, 1f);
            float strong = PropGaleModel.TakeChance(55f, 1f);
            Assert.True(strong > weak, weak + " -> " + strong);
        }

        [Fact]
        public void ASturdyPropSurvivesWhatTakesASign()
        {
            float sign = PropGaleModel.TakeChance(45f, PropGaleModel.FragilityOf(2f));
            float pole = PropGaleModel.TakeChance(45f, PropGaleModel.FragilityOf(7f));

            Assert.True(sign > pole, "the sign (" + sign + ") is no more fragile than the pole ("
                                     + pole + ")");
        }

        [Fact]
        public void TheSamePropInTheSameRoundAlwaysGetsTheSameAnswer()
        {
            for (ushort id = 1; id < 60; id++)
            {
                bool a = PropGaleModel.Takes(id, 3u, 50f, 1f, Seed);
                bool b = PropGaleModel.Takes(id, 3u, 50f, 1f, Seed);
                Assert.Equal(a, b);
            }
        }

        [Fact]
        public void ALaterRoundGivesASurvivorAnotherChance()
        {
            // ★ 回を進めないと、1 度助かった看板は二度と飛ばない（風が強くなっても）。
            int changed = 0;
            for (ushort id = 1; id < 200; id++)
            {
                if (PropGaleModel.Takes(id, 0u, 50f, 1f, Seed)
                    != PropGaleModel.Takes(id, 1u, 50f, 1f, Seed))
                {
                    changed++;
                }
            }

            Assert.True(changed > 0, "the round makes no difference; survivors are immortal");
        }

        [Fact]
        public void BrokenInputTakesNothing()
        {
            Assert.Equal(0f, PropGaleModel.FragilityOf(float.NaN), 5);
            Assert.Equal(0f, PropGaleModel.FragilityOf(-3f), 5);
            Assert.Equal(0f, PropGaleModel.TakeChance(float.NaN, 1f), 5);
            Assert.Equal(0f, PropGaleModel.TakeChance(60f, float.NaN), 5);
            Assert.False(PropGaleModel.Takes(1, 0u, float.NaN, 1f, Seed));
        }

        [Fact]
        public void AboutHalfTheSignsSurviveTheWorstPass()
        {
            // 実際に数える。**割合の式が意図どおりか**を、式ではなく結果で見る。
            int taken = 0;
            const int Count = 2000;
            for (ushort id = 1; id <= Count; id++)
            {
                if (PropGaleModel.Takes(id, 0u, PropGaleModel.FullWindMetresPerSecond, 1f, Seed))
                {
                    taken++;
                }
            }

            float ratio = taken / (float)Count;
            Assert.InRange(ratio, PropGaleModel.MaxTakeRatio - 0.08f,
                           PropGaleModel.MaxTakeRatio + 0.08f);
        }
    }
}
