using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// The owner's request (2026-08-22): "to make the gale and the heavy rain destroy small
    /// buildings and sign props… in the city".
    ///
    /// ★★ <c>PropManager.ReleaseProp</c> <b>cannot be undone</b>. So what is pinned down
    ///    here is less "that things break" than <b>that they do not break too much</b> ——
    ///    "every sign in the city disappears when a typhoon comes" is a breakage that cannot
    ///    be repaired.
    /// </summary>
    public class PropGaleModelTests
    {
        private const uint Seed = 777u;

        [Fact]
        public void SignsGoAndBigThingsStay()
        {
            Assert.Equal(1f, PropGaleModel.FragilityOf(1.2f), 4);      // a road sign
            Assert.Equal(1f, PropGaleModel.FragilityOf(3f), 4);        // a billboard
            Assert.Equal(0f, PropGaleModel.FragilityOf(12f), 4);       // a water tower
            Assert.InRange(PropGaleModel.FragilityOf(6f), 0.01f, 0.99f); // a street light
        }

        [Fact]
        public void NothingMovesInAnOrdinaryBreeze()
        {
            // ★ Outside the typhoon not a single prop may be carried off.
            Assert.Equal(0f, PropGaleModel.TakeChance(10f, 1f), 5);
            Assert.Equal(0f, PropGaleModel.TakeChance(PropGaleModel.MinWindMetresPerSecond, 1f), 5);
        }

        [Fact]
        public void EvenTheWorstStormNeverTakesEverythingAtOnce()
        {
            // ★★ **This is the crux.** A single pass must never take everything.
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
            // ★ Without advancing the round, a sign that survived once is never carried off
            //   again (however much the wind picks up).
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
            // Count them for real. We check **whether the ratio formula does what we meant**
            // by the result rather than by the formula.
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
