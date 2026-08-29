using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 所有者の指示（2026-08-29）:
    ///
    /// &gt; natural DisasterDLC の津波のメカニズムを研究して、
    /// &gt; 私が求めているものを一から作り直してください。
    ///
    /// 研究は <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>。
    /// ここが固定するのは<b>震源に与える外力の形</b>だけである ——
    /// 波そのものはゲームの浅水ソルバが作るので、こちらには無い。
    /// </summary>
    public class TsunamiSourceTests
    {
        /// <summary>ゲーム速度 1 の目安。**フレームと実秒を混ぜないための注釈。**</summary>
        private const float FramesPerRealSecond = 60f;

        // ── バニラの目盛り（IL とプレハブの実測値）─────────────────────

        [Fact]
        public void TheVanillaScaleMatchesTheGamesOwnFormula()
        {
            // IL: m_delta = round(m_height * 65536/1024 * intensity / 55)
            //     プレハブ実測 m_height = 64
            Assert.Equal(64f, TsunamiSource.VanillaHeightMetres);
            Assert.Equal(7447, TsunamiSource.VanillaDeltaUnits(100));
            Assert.Equal(4096, TsunamiSource.VanillaDeltaUnits(55));
            Assert.Equal(0, TsunamiSource.VanillaDeltaUnits(0));
        }

        [Fact]
        public void TheDriveNeverOverflowsTheInt16TheGameStoresItIn()
        {
            // ★★ m_delta は Int16。ここを超えると**符号が折り返して外力が反転する。**
            for (int i = 0; i <= 255; i++)
            {
                int peak = TsunamiSource.PeakDeltaUnits((byte)i);
                Assert.InRange(peak, 0, TsunamiSource.MaxDeltaUnits);

                for (float f = 0f; f < TsunamiSource.TotalFrames + 60f; f += 10f)
                {
                    int d = TsunamiSource.DeltaAt(f, peak);
                    Assert.InRange(d, -TsunamiSource.MaxDeltaUnits,
                                   TsunamiSource.MaxDeltaUnits);
                }
            }
        }

        [Fact]
        public void TheUnlockedIntensityIsClampedRatherThanWrapped()
        {
            // 強度解放（25.5 ＝ raw 255）でも折り返さないこと。
            // 64 * 64 * 255 / 55 = 18991 units = 297 m。Int16 にはまだ収まる。
            Assert.Equal(18991, TsunamiSource.VanillaDeltaUnits(255));
            Assert.InRange(TsunamiSource.VanillaDeltaUnits(255), 0, TsunamiSource.MaxDeltaUnits);
            Assert.True(TsunamiSource.PeakDeltaUnits(255) > TsunamiSource.PeakDeltaUnits(100));
        }

        // ── ① 隆起: 水を中心へ集める（外力は負）───────────────────────

        [Fact]
        public void TheSeaIsDrawnInFirstSoABulgeCanRise()
        {
            // ★★ ここが所有者の「すぐに震源地に海面の巨大な隆起が生成」である。
            //    IMPACT の外力が**負** ＝ 仮想の窪み ＝ 水は中心へ流れ込む。
            int peak = TsunamiSource.PeakDeltaUnits(100);

            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInFrames * 0.5f, peak) < 0,
                        "the sea is not being drawn in during stage 1");
            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInFrames * 0.9f, peak) < 0);
        }

        [Fact]
        public void TheDrawInIsBoundedSoTheCentreCannotBeEmptied()
        {
            int peak = TsunamiSource.PeakDeltaUnits(100);
            float deepest = 0f;

            for (float f = 0f; f < TsunamiSource.DrawInFrames; f += 5f)
            {
                float d = TsunamiSource.DriveAt(f);
                if (d < deepest) deepest = d;
            }

            Assert.Equal(-TsunamiSource.DrawInFactor, deepest, 3);
            Assert.True(TsunamiSource.DrawInFactor < 1f,
                        "drawing in harder than pushing out would leave a hole, not a wave");
            Assert.True(peak > 0);
        }

        // ── ② ③ 押し出し: 外力は正 ────────────────────────────────

        [Fact]
        public void TheBulgeIsThenPushedOutward()
        {
            int peak = TsunamiSource.PeakDeltaUnits(100);

            // 反転が終わったあとは、押し出しの頂点で保たれる。
            float mid = (TsunamiSource.DrawInFrames + TsunamiSource.TurnFrames
                         + TsunamiSource.PushFrames) * 0.5f;

            Assert.Equal(peak, TsunamiSource.DeltaAt(mid, peak));
            Assert.Equal(1f, TsunamiSource.DriveAt(TsunamiSource.PushFrames - 1f), 3);
        }

        [Fact]
        public void TheDriveTurnsOverWithoutAStep()
        {
            // ★★ ソルバは<b>傾きの差分</b>を積むので、段差はそのまま衝撃になる。
            //    隣り合うフレームで外力が飛ばないこと。
            int peak = TsunamiSource.PeakDeltaUnits(255);
            float span = TsunamiSource.DrawInFactor + 1f;

            for (float f = 0f; f < TsunamiSource.TotalFrames; f += 1f)
            {
                float a = TsunamiSource.DriveAt(f);
                float b = TsunamiSource.DriveAt(f + 1f);

                Assert.True(System.Math.Abs(b - a) < span * 0.05f,
                            "the drive jumps from " + a + " to " + b + " at frame " + f);
            }

            Assert.True(peak > 0);
        }

        // ── ④ 外力を切る ────────────────────────────────────────

        [Fact]
        public void TheDriveIsReleasedSoTheSolverCanCarryTheRing()
        {
            // ★★ **これが作り直しの肝である。** 水の壁を自分で描き続けるのではなく、
            //    外力を切って、あとはゲームの浅水ソルバに運ばせる ——
            //    バニラの津波とまったく同じ経路。
            int peak = TsunamiSource.PeakDeltaUnits(100);

            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalFrames, peak));
            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalFrames + 600f, peak));
            Assert.True(TsunamiSource.IsFinished(TsunamiSource.TotalFrames));
            Assert.False(TsunamiSource.IsFinished(TsunamiSource.TotalFrames - 1f));
        }

        [Fact]
        public void TheDriveFadesRatherThanBeingCutOff()
        {
            float a = TsunamiSource.DriveAt(TsunamiSource.PushFrames + 10f);
            float b = TsunamiSource.DriveAt(
                (TsunamiSource.PushFrames + TsunamiSource.TotalFrames) * 0.5f);
            float c = TsunamiSource.DriveAt(TsunamiSource.TotalFrames - 10f);

            Assert.True(a > b && b > c, a + " " + b + " " + c);
            Assert.True(c < 0.1f);
        }

        // ── 時計 ──────────────────────────────────────────────

        [Fact]
        public void TheDriveRunsLongEnoughToMoveRealWater()
        {
            // ★ 目盛りはバニラ: DLC の発生源は 256 フレーム（≒4.3 実秒）しかない
            //   （m_duration 256 << 6 = 16384、m_currentTime は毎水ステップ +64）。
            //   こちらは円形に広げるぶん、それより長く押す。
            Assert.True(TsunamiSource.TotalFrames > 256f,
                        "the drive is shorter than the DLC's own 256-frame source");

            float seconds = TsunamiSource.TotalFrames / FramesPerRealSecond;
            Assert.InRange(seconds, 10f, 40f);
        }

        [Fact]
        public void TheStagesAreInOrder()
        {
            Assert.True(TsunamiSource.DrawInFrames > 0f);
            Assert.True(TsunamiSource.DrawInFrames + TsunamiSource.TurnFrames
                        < TsunamiSource.PushFrames);
            Assert.True(TsunamiSource.PushFrames < TsunamiSource.TotalFrames);
        }

        [Fact]
        public void TheSourceIsBigEnoughToBeATsunamiAndFitsTheMap()
        {
            // 1 セル 16 m、マップ半辺 8640 m。
            Assert.Equal(TsunamiSource.RadiusCells * 16f, TsunamiSource.RadiusMetres, 3);
            Assert.InRange(TsunamiSource.RadiusMetres, 800f, 4000f);
        }

        // ── 壊れた入力 ────────────────────────────────────────

        [Fact]
        public void BrokenInputDrivesNothing()
        {
            Assert.Equal(0, TsunamiSource.DeltaAt(float.NaN, 5000));
            Assert.Equal(0, TsunamiSource.DeltaAt(float.PositiveInfinity, 5000));
            Assert.Equal(0, TsunamiSource.DeltaAt(-10f, 5000));
            Assert.Equal(0, TsunamiSource.DeltaAt(100f, 0));
            Assert.Equal(0, TsunamiSource.DeltaAt(100f, -5000));
            Assert.Equal(0f, TsunamiSource.DriveAt(float.NaN), 4);
            Assert.False(TsunamiSource.IsFinished(float.NaN));
        }

        [Fact]
        public void EveryStageHasAName()
        {
            foreach (float f in new[] { -1f, 0f, TsunamiSource.DrawInFrames * 0.5f,
                                        TsunamiSource.DrawInFrames + 10f,
                                        TsunamiSource.PushFrames - 10f,
                                        TsunamiSource.PushFrames + 10f,
                                        TsunamiSource.TotalFrames + 10f })
            {
                Assert.False(string.IsNullOrEmpty(TsunamiSource.StageAt(f)));
            }

            Assert.NotEqual(TsunamiSource.StageAt(0f),
                            TsunamiSource.StageAt(TsunamiSource.TotalFrames + 1f));
        }
    }
}
