using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class UpliftScheduleTests
    {
        private const ushort BaseRaw = 2560;   // 40 m（海面）。§D-4 と同じ換算。

        [Fact]
        public void ProgressZeroIsTheOriginalGroundAndProgressOneIsTheFinalHeight()
        {
            Assert.Equal(BaseRaw, UpliftSchedule.RawTargetAt(BaseRaw, 400f, 0f));
            Assert.Equal((ushort)(BaseRaw + 400 * 64), UpliftSchedule.RawTargetAt(BaseRaw, 400f, 1f));
        }

        [Fact]
        public void TheSummitAdvancesByAtLeastOneRawUnitEveryTick()
        {
            // ★ 罠 2。1 tick の変化が 1/64 m 未満だと丸めで消え、隆起が無言で完全停止する
            //   （§C-8 の `if (n != raw)`）。TotalTicksFor がそれを構造で防いでいる。
            //   この検査を通すために「増分」を大きくするのではなく、
            //   **tick 数のほうを切り詰める**のが正しい直し方である。
            const float h = 300f;
            int total = UpliftSchedule.TotalTicksFor(h, 100000);
            ushort previous = UpliftSchedule.RawTargetAt(BaseRaw, h, UpliftSchedule.ProgressAt(0, total));
            for (int t = 1; t <= total; t++)
            {
                ushort now = UpliftSchedule.RawTargetAt(BaseRaw, h, UpliftSchedule.ProgressAt(t, total));
                Assert.True(now > previous,
                    "the summit did not move at tick " + t + " (raw stayed at " + now + ")");
                previous = now;
            }
        }

        [Fact]
        public void AskingForMoreTicksThanTheQuantumAllowsIsTruncated()
        {
            // 20 m の山は最大 20*64 = 1280 tick でしか刻めない。
            Assert.True(UpliftSchedule.TotalTicksFor(20f, 100000) <= 1280);
            // 十分に短い要求はそのまま通る。
            Assert.Equal(600, UpliftSchedule.TotalTicksFor(600f, 600));
            // 0 や負の要求でも 1 tick は返す（0 除算を作らない）。
            Assert.True(UpliftSchedule.TotalTicksFor(600f, 0) >= 1);
            Assert.True(UpliftSchedule.TotalTicksFor(600f, -5) >= 1);
            Assert.True(UpliftSchedule.TotalTicksFor(0f, 500) >= 1);
        }

        [Fact]
        public void WalkingEveryTickReachesExactlyTheFinalRawValue()
        {
            // 絶対目標なので、途中で何 tick 動かなかったセルがあっても最後は必ず一致する。
            const float h = 173.4f;
            int total = UpliftSchedule.TotalTicksFor(h, 900);
            ushort last = 0;
            for (int t = 0; t <= total; t++)
            {
                last = UpliftSchedule.RawTargetAt(BaseRaw, h * 0.13f, UpliftSchedule.ProgressAt(t, total));
            }
            Assert.Equal(UpliftSchedule.RawTargetAt(BaseRaw, h * 0.13f, 1f), last);
        }

        [Fact]
        public void TheRawTargetSaturatesAtTheCeilingInsteadOfWrapping()
        {
            // ushort の巻き戻り（山頂が海面に戻る）がいちばん痛い壊れ方。
            Assert.Equal((ushort)UpliftSchedule.MaxRaw,
                         UpliftSchedule.RawTargetAt(60000, 400f, 1f));
            Assert.Equal((ushort)0, UpliftSchedule.RawTargetAt(0, -400f, 1f));
        }

        [Fact]
        public void NothingIsRaisedOutsideTheClearedRadius()
        {
            // ★ 罠 1。準備が届いていない場所を持ち上げると、道路と建物が毎フラッシュ
            //   地形を押し戻し（§A-2）、山の中に平らな溝とすり鉢が残る。
            Assert.Equal(300f, UpliftSchedule.ActiveRadiusMetres(1200f, 300f), 3);
            Assert.Equal(1200f, UpliftSchedule.ActiveRadiusMetres(1200f, 5000f), 3);
        }

        [Fact]
        public void WithNothingClearedYetTheActiveRadiusIsZero()
        {
            // ★ 罠 1 のいちばん危ない入口。「まだ 1 本も壊していない」を
            //   「制限なし」と取り違えると、隆起がいきなり全域を持ち上げる。
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(1200f, 0f), 4);
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(1200f, -1f), 4);
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(1200f, float.NaN), 4);
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(float.NaN, 300f), 4);
        }

        [Fact]
        public void TheClearingFrontStaysAheadOfTheUpliftFront()
        {
            const float r = 1200f;
            const float lead = 96f;
            for (float p = 0f; p <= 1f; p += 0.05f)
            {
                float front = UpliftSchedule.ClearingFrontMetres(r, p, lead);
                Assert.True(front >= r * p, "the clearing front fell behind at p=" + p);
                Assert.InRange(front, 0f, r);
            }
            Assert.Equal(r, UpliftSchedule.ClearingFrontMetres(r, 1f, lead), 3);
            // 先行量が 0 でも後ろへ下がらない。
            Assert.True(UpliftSchedule.ClearingFrontMetres(r, 0.5f, 0f) >= r * 0.5f);
        }

        [Fact]
        public void TheBlockHeightCatchUpIsTwoMetresPerSixtyFourFrames()
        {
            // §A-2: ゲームモードでは上へ 2 m / 64 sim フレーム。300 m なら 9600 フレーム。
            Assert.Equal(9600, UpliftSchedule.BlockHeightCatchUpFrames(300f));
            Assert.Equal(64, UpliftSchedule.BlockHeightCatchUpFrames(2f));
            Assert.Equal(128, UpliftSchedule.BlockHeightCatchUpFrames(2.5f));
            Assert.Equal(0, UpliftSchedule.BlockHeightCatchUpFrames(0f));
            Assert.Equal(0, UpliftSchedule.BlockHeightCatchUpFrames(float.NaN));
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            Assert.Equal(BaseRaw, UpliftSchedule.RawTargetAt(BaseRaw, float.NaN, 0.5f));
            Assert.Equal(BaseRaw, UpliftSchedule.RawTargetAt(BaseRaw, 400f, float.NaN));
            Assert.Equal(0f, UpliftSchedule.ClearingFrontMetres(float.NaN, 0.5f, 96f), 4);
            Assert.Equal(0f, UpliftSchedule.ClearingFrontMetres(1200f, float.NaN, 96f), 4);
        }

        [Fact]
        public void ProgressIsClampedAtBothEnds()
        {
            Assert.Equal(0f, UpliftSchedule.ProgressAt(-5, 100), 4);
            Assert.Equal(1f, UpliftSchedule.ProgressAt(500, 100), 4);
            Assert.Equal(0f, UpliftSchedule.ProgressAt(5, 0), 4);
            Assert.Equal(0f, UpliftSchedule.ProgressAt(5, -3), 4);
            Assert.Equal(0.5f, UpliftSchedule.ProgressAt(50, 100), 4);
        }
    }
}
