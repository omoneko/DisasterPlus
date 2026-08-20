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
        // ── 山頂から外へ広がる隆起（GrowthMetresAt）──────────────────

        [Fact]
        public void GrowthStartsAtNothingAndEndsAtTheFinalProfile()
        {
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(300f, 600f, 0f), 4);
            Assert.Equal(300f, UpliftSchedule.GrowthMetresAt(300f, 600f, 1f), 4);
            Assert.Equal(600f, UpliftSchedule.GrowthMetresAt(600f, 600f, 1f), 4);
        }

        [Fact]
        public void TheSummitStillRisesInProportionToProgress()
        {
            // 山頂のプロファイルは H なので、山頂の盛り上がりは H×progress のままである
            // （パネルと診断が出している「今の山頂」の意味を変えない）。
            for (float p = 0f; p <= 1f; p += 0.05f)
            {
                Assert.Equal(600f * p, UpliftSchedule.GrowthMetresAt(600f, 600f, p), 3);
            }
        }

        [Fact]
        public void TheFrontExpandsOutwardAsProgressAdvances()
        {
            // ★★ 本タスクそのもの。山が一様に膨らむのではなく、山頂から外へ広がること。
            //    直線の円錐（成層）なら、progress p で地表に出ているのは d < R·p である。
            const float r = 1200f, h = 600f;
            float previousFront = -1f;

            for (float p = 0.1f; p <= 1.0f; p += 0.1f)
            {
                float front = 0f;
                for (float d = 0f; d <= r; d += 1f)
                {
                    float profile = VolcanoShape.ProfileAt(VolcanoForm.Strato, d, r, h);
                    if (UpliftSchedule.GrowthMetresAt(profile, h, p) > 0f) front = d;
                }

                Assert.True(front > previousFront, "the front went backwards at p=" + p);
                Assert.True(front <= r, "the front left the radius at p=" + p);
                // 直線の円錐なら前線はちょうど R·p（走査の刻み 1 m ぶんだけ内側で見つかる）。
                Assert.InRange(front, r * p - 2f, r * p);
                previousFront = front;
            }
        }

        [Fact]
        public void EveryGrowingCellRisesByTheSameAmountEachTick()
        {
            // ★★ 罠 2 に対してむしろ強い。profile × progress では外周ほど 1 tick の
            //    変化が小さく、合計の盛り上がりが小さいセルは丸めで消えていた。
            //    この式では育っているセルはどれも H/totalTicks だけ上がる。
            const float h = 600f;
            int ticks = UpliftSchedule.TotalTicksFor(h, 85);
            float expected = h / ticks;

            Assert.True(expected >= 1f / UpliftSchedule.RawUnitsPerMetre,
                "a tick moves less than one raw unit: " + expected);

            foreach (float profile in new float[] { 600f, 300f, 120f, 20f, 0.5f })
            {
                for (int t = 1; t < ticks; t++)
                {
                    float a = UpliftSchedule.GrowthMetresAt(profile, h,
                                  UpliftSchedule.ProgressAt(t - 1, ticks));
                    float b = UpliftSchedule.GrowthMetresAt(profile, h,
                                  UpliftSchedule.ProgressAt(t, ticks));

                    Assert.True(b >= a, "a cell went down between ticks");
                    // 育っている最中（0 でも頭打ちでもない）なら、上がる量は H/ticks である。
                    if (a > 0f && b < profile) Assert.Equal(expected, b - a, 2);
                }
            }
        }

        [Fact]
        public void GrowthIsNeverNegativeAndNeverPassesTheProfile()
        {
            for (float p = -1f; p <= 2f; p += 0.05f)
            {
                float v = UpliftSchedule.GrowthMetresAt(250f, 600f, p);
                Assert.InRange(v, 0f, 250f);
            }
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(float.NaN, 600f, 0.5f), 4);
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(250f, float.NaN, 0.5f), 4);
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(250f, 600f, float.NaN), 4);
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(-5f, 600f, 0.5f), 4);
        }
    }
}
