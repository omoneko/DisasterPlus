using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 噴石の弾道。**固定するのは「落ちる」「大きいほど遠い」「山の大きさに追従する」**の
    /// 3 つと、異常入力で NaN を外へ出さないことである。見た目そのものは
    /// tools/VolcanoPreview が描いて目で確かめる。
    /// </summary>
    public class EjectaBallisticsTests
    {
        private const uint Seed = 0x1A2B3C4Du;
        private const float R = 1200f;
        private const float H = 600f;
        private const float Vent = 540f;   // 火口の底（成層の既定でおよそこの高さ）

        [Fact]
        public void EveryBlockLandsInFiniteTime()
        {
            // ★ 位相機械が止まらないことの担保。飛び続ける岩が 1 個でもあると、
            //   その枠は噴火が終わるまで塞がったままになる。
            for (int blast = 0; blast < 24; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, 1f,
                                                          VolcanoForm.Strato, R, H, Vent);
                    Assert.True(b.Valid, "block " + blast + "/" + i + " has no trajectory");
                    // ★ 帽子（MaxFlightSeconds）に**当たっていない**ことまで見る。
                    //   当たっていたら、その岩は空中で止まったまま消えることになる。
                    Assert.True(b.FlightSeconds > 0f
                                && b.FlightSeconds < 40f,
                        "flight " + b.FlightSeconds + " s is not finite and sane");
                    Assert.False(float.IsNaN(b.RangeMetres));
                    Assert.True(b.RangeMetres > 0f);
                }
            }
        }

        [Fact]
        public void BiggerBlocksTravelFurther()
        {
            // 所有者の依頼そのもの（「larger ones travelling further」）。
            // 同じ噴出の中で、大きさの上位と下位を比べる。
            float smallSum = 0f, bigSum = 0f;
            int smallCount = 0, bigCount = 0;

            for (int blast = 0; blast < 24; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, 1f,
                                                          VolcanoForm.Strato, R, H, Vent);
                    if (!b.Valid) continue;

                    if (b.SizeUnit < 0.25f) { smallSum += b.RangeMetres; smallCount++; }
                    else if (b.SizeUnit > 0.75f) { bigSum += b.RangeMetres; bigCount++; }
                }
            }

            Assert.True(smallCount > 0 && bigCount > 0, "the sample has no small or big blocks");
            Assert.True(bigSum / bigCount > smallSum / smallCount * 1.5f,
                "big blocks do not travel further: " + (bigSum / bigCount)
                + " m vs " + (smallSum / smallCount) + " m");
        }

        [Fact]
        public void TheSpreadFollowsTheSizeOfTheMountain()
        {
            // ★ ⑤はプレイヤーが大きさを選べる機能である。半径 350 m の溶岩ドームから
            //   2 km 岩が飛んだら、それは物理ではなく不具合に見える。
            float small = LongestRange(VolcanoForm.Dome, 350f, 300f, 270f);
            float large = LongestRange(VolcanoForm.Strato, R, H, Vent);

            Assert.True(large > small * 2f,
                "the spread does not scale with the cone: " + large + " vs " + small);
            // ★ 平地に落ちるとした飛距離より実際は伸びる —— 噴出口が火口の底
            //   （ここでは 270 m）に在って、そこより低い裾へ落ちるからである。
            //   その伸びを含めても、山 2 個ぶんより外へは行かない。
            Assert.True(small < 350f * 2.2f,
                "the small cone throws blocks far beyond its own footprint: " + small);
        }

        [Fact]
        public void TheBlockIsAboveTheGroundWhileItFliesAndAtTheGroundWhenItLands()
        {
            EjectaBlock b = EjectaBallistics.Plan(Seed, 3, 2, 1f,
                                                  VolcanoForm.Strato, R, H, Vent);
            Assert.True(b.Valid);

            // 途中は必ず地面より上。
            for (int i = 1; i < 10; i++)
            {
                float t = b.FlightSeconds * i / 10f;
                float dx, dy, dz;
                EjectaBallistics.OffsetAt(b, t, out dx, out dy, out dz);

                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                float ground = EjectaBallistics.GroundAt(VolcanoForm.Strato, d, R, H);
                Assert.True(Vent + dy > ground - 1f,
                    "the block is underground at t=" + t + " (" + (Vent + dy) + " vs " + ground + ")");
            }

            // 着弾では地面と一致する（二分法の刻み 0.15 ms ぶんの誤差だけ）。
            float lx, ly, lz;
            EjectaBallistics.OffsetAt(b, b.FlightSeconds, out lx, out ly, out lz);
            float landDistance = (float)Math.Sqrt(lx * lx + lz * lz);
            float landGround = EjectaBallistics.GroundAt(VolcanoForm.Strato, landDistance, R, H);
            Assert.True(Math.Abs(Vent + ly - landGround) < 2f,
                "the impact is not on the ground: " + (Vent + ly) + " vs " + landGround);
        }

        [Fact]
        public void TheSameBlastAlwaysThrowsTheSameBlocks()
        {
            // フレーム番号を種に混ぜていないことの担保。混ぜると飛行中に行き先が変わる。
            EjectaBlock a = EjectaBallistics.Plan(Seed, 5, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            EjectaBlock b = EjectaBallistics.Plan(Seed, 5, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            Assert.Equal(a.DirX, b.DirX);
            Assert.Equal(a.DirZ, b.DirZ);
            Assert.Equal(a.FlightSeconds, b.FlightSeconds);
            Assert.Equal(a.RangeMetres, b.RangeMetres);
        }

        [Fact]
        public void DifferentBlastsThrowDifferentBlocks()
        {
            EjectaBlock a = EjectaBallistics.Plan(Seed, 5, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            EjectaBlock b = EjectaBallistics.Plan(Seed, 6, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            Assert.NotEqual(a.DirX, b.DirX);
        }

        [Fact]
        public void BadInputIsRefusedAndNeverNaN()
        {
            // .cgs も地形も手で編集されうる。**0 で代用せず「使えない」を返す。**
            foreach (float bad in new float[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.False(EjectaBallistics.Plan(Seed, 0, 0, 1f,
                             VolcanoForm.Strato, bad, H, Vent).Valid);
                Assert.False(EjectaBallistics.Plan(Seed, 0, 0, 1f,
                             VolcanoForm.Strato, R, bad, Vent).Valid);
            }

            Assert.False(EjectaBallistics.Plan(Seed, -1, 0, 1f,
                         VolcanoForm.Strato, R, H, Vent).Valid);

            // 噴出口の高さが壊れていても弾道は成立する（0 に落とす）。
            EjectaBlock ok = EjectaBallistics.Plan(Seed, 0, 0, 1f,
                                                   VolcanoForm.Strato, R, H, float.NaN);
            Assert.True(ok.Valid);
            Assert.False(float.IsNaN(ok.FlightSeconds));

            // 無効な枠に位置を聞いても 0 が返るだけ（NaN を外へ出さない）。
            float dx, dy, dz;
            EjectaBallistics.OffsetAt(default(EjectaBlock), 1f, out dx, out dy, out dz);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
            Assert.Equal(0f, dz);

            EjectaBallistics.OffsetAt(ok, float.NaN, out dx, out dy, out dz);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
            Assert.Equal(0f, dz);
        }

        [Fact]
        public void WeakEruptionsThrowFewerAndShorterBlocks()
        {
            Assert.True(EjectaBallistics.BlocksPerBlast(0f)
                        < EjectaBallistics.BlocksPerBlast(1f));
            Assert.Equal(EjectaBallistics.MinBlocksPerBlast,
                         EjectaBallistics.BlocksPerBlast(0f));
            Assert.Equal(EjectaBallistics.MaxBlocksPerBlast,
                         EjectaBallistics.BlocksPerBlast(1f));

            // NaN は 0 と同じ扱い（いちばん静かな側）。
            Assert.Equal(EjectaBallistics.MinBlocksPerBlast,
                         EjectaBallistics.BlocksPerBlast(float.NaN));

            float weak = LongestRangeAt(0.15f);
            float strong = LongestRangeAt(1f);
            Assert.True(weak < strong, "a weak eruption throws as far as a strong one");
        }

        private static float LongestRange(VolcanoForm form, float r, float h, float vent)
        {
            float longest = 0f;
            for (int blast = 0; blast < 16; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, 1f, form, r, h, vent);
                    if (b.Valid && b.RangeMetres > longest) longest = b.RangeMetres;
                }
            }
            return longest;
        }

        private static float LongestRangeAt(float unit)
        {
            float longest = 0f;
            for (int blast = 0; blast < 16; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, unit,
                                                          VolcanoForm.Strato, R, H, Vent);
                    if (b.Valid && b.RangeMetres > longest) longest = b.RangeMetres;
                }
            }
            return longest;
        }
    }
}
