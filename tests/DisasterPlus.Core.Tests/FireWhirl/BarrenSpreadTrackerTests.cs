using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class BarrenSpreadTrackerTests
    {
        [Fact]
        public void FreshTracker_IsNotTripped()
        {
            var t = new BarrenSpreadTracker(3);
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void TripsExactlyOnceAtThreshold()
        {
            var t = new BarrenSpreadTracker(3);
            Assert.False(t.Record(5, 0));
            Assert.False(t.Record(5, 0));
            Assert.True(t.Record(5, 0));    // ここで初めて閾値
            Assert.False(t.Record(5, 0));   // 以後はもう報告しない（ログを 1 回に留める）
            Assert.True(t.Tripped);
        }

        [Fact]
        public void AnyIgnitionResetsEverything()
        {
            var t = new BarrenSpreadTracker(3);
            t.Record(5, 0);
            t.Record(5, 0);
            Assert.Equal(2, t.Streak);

            Assert.False(t.Record(5, 1));
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void IgnitionAfterTrippingClearsTheBadge()
        {
            var t = new BarrenSpreadTracker(2);
            t.Record(1, 0);
            Assert.True(t.Record(1, 0));
            Assert.True(t.Tripped);

            t.Record(1, 3);
            Assert.False(t.Tripped);
            Assert.Equal(0, t.Streak);

            // 一度回復したら、次の閾値到達はまた報告される。
            t.Record(1, 0);
            Assert.True(t.Record(1, 0));
        }

        [Fact]
        public void PassesWithNoAttemptAreNotEvidence()
        {
            // 「燃やす対象がそもそも無かった」回は証拠にならない。
            // 積み増しもしないが、それまでの証拠を消しもしない。
            var t = new BarrenSpreadTracker(3);
            t.Record(2, 0);
            Assert.False(t.Record(0, 0));
            Assert.Equal(1, t.Streak);
            Assert.False(t.Record(0, 0));
            Assert.Equal(1, t.Streak);

            t.Record(2, 0);
            Assert.True(t.Record(2, 0));
        }

        [Fact]
        public void ResetClearsState()
        {
            var t = new BarrenSpreadTracker(2);
            t.Record(1, 0);
            t.Record(1, 0);
            Assert.True(t.Tripped);

            t.Reset();
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void ThresholdIsClampedToAtLeastOne()
        {
            var t = new BarrenSpreadTracker(0);
            Assert.Equal(1, t.Threshold);
            Assert.True(t.Record(1, 0));
        }

        [Fact]
        public void DefaultThresholdIsAWholeWhirlLifetime()
        {
            // 既定値の根拠（1 パス ≒ 0.35 ゲーム内分 × 24 ≒ 8.4 分 ≒ 旋風 1 基の寿命）を固定する。
            // 8 だった頃はゲーム内 2.8 分＝実時間 3 秒足らずで、しかも証拠に
            // 「バニラが設計上断る棟」が混ざっていたため正常な街で必ず踏んだ。
            Assert.Equal(24, BarrenSpreadTracker.DefaultThreshold);
            Assert.Equal(24, new BarrenSpreadTracker().Threshold);
        }

        [Fact]
        public void DefaultTracker_TripsOnlyAtTheDefaultThreshold()
        {
            var t = new BarrenSpreadTracker();
            for (int i = 0; i < BarrenSpreadTracker.DefaultThreshold - 1; i++)
            {
                Assert.False(t.Record(3, 0));
            }
            Assert.True(t.Record(3, 0));
        }

        [Fact]
        public void RefusedByDesignCandidatesAreNotEvidence()
        {
            // この修正の動機。火災旋風が成功した跡地の定常状態は
            // 「まだ燃えている建物（Select が除外）＋ 燃え尽きた瓦礫（必ず拒否される）」で、
            // 瓦礫を attempted に数えていたころは正常な街で無限に証拠が積み上がった。
            // FireWhirlDamage.CanBurn がそれらを attempted から外すので、
            // 呼び出し側から見れば「試行 0 の回」が延々と続くだけになる。
            var t = new BarrenSpreadTracker();
            for (int i = 0; i < BarrenSpreadTracker.DefaultThreshold * 4; i++)
            {
                Assert.False(t.Record(0, 0));
            }
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void OccasionalIgnitionKeepsTheDetectorQuietForever()
        {
            // 正常系。閾値に届く手前で 1 棟でも着火すれば証拠は毎回捨てられる。
            var t = new BarrenSpreadTracker();
            for (int round = 0; round < 10; round++)
            {
                for (int i = 0; i < BarrenSpreadTracker.DefaultThreshold - 1; i++)
                {
                    Assert.False(t.Record(3, 0));
                }
                Assert.False(t.Record(3, 1));
                Assert.Equal(0, t.Streak);
            }
            Assert.False(t.Tripped);
        }
    }
}
