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
    /// ここが固定するのは<b>震源に与える外力の形と、その大きさの決め方</b>だけである
    /// —— 波そのものはゲームの浅水ソルバが作るので、こちらには無い。
    /// </summary>
    public class TsunamiSourceTests
    {
        /// <summary>ゲーム速度 1 の目安。**フレームと実秒を混ぜないための注釈。**</summary>
        private const float FramesPerRealSecond = 60f;

        /// <summary>試験に使う外力の大きさ（<c>m_delta</c> の単位）。</summary>
        private const int Drive = 40000;

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
            Assert.Equal(18991, TsunamiSource.VanillaDeltaUnits(255));
        }

        // ── 重ねた波への配分 ─────────────────────────────────────

        [Fact]
        public void EveryWaveGetsAValueTheGameCanStoreInAnInt16()
        {
            // ★★ m_delta は Int16。ここを超えると**符号が折り返して外力が反転する。**
            for (int drive = -TsunamiSource.MaxDriveUnits;
                 drive <= TsunamiSource.MaxDriveUnits; drive += 977)
            {
                for (int i = -2; i < TsunamiSource.MaxStackedWaves + 2; i++)
                {
                    int d = TsunamiSource.DeltaForWave(i, drive);
                    Assert.InRange(d, -TsunamiSource.MaxDeltaUnits,
                                   TsunamiSource.MaxDeltaUnits);
                }
            }
        }

        [Fact]
        public void TheWavesTogetherCarryExactlyTheDrive()
        {
            // ★★ **これが「重ねる」の意味である。** 外力は波ごとに加算されるので、
            //    分けて配ったぶんの合計が、狙った外力そのものでなければならない。
            foreach (int drive in new[] { 0, 1, 2000, 32767, 32768, 70000,
                                          TsunamiSource.MaxDriveUnits,
                                          TsunamiSource.MaxDriveUnits + 50000 })
            {
                int sum = 0;
                for (int i = 0; i < TsunamiSource.MaxStackedWaves; i++)
                {
                    sum += TsunamiSource.DeltaForWave(i, drive);
                }

                int expected = drive > TsunamiSource.MaxDriveUnits
                    ? TsunamiSource.MaxDriveUnits : drive;

                Assert.Equal(expected, sum);
            }
        }

        [Fact]
        public void TheSignIsCarriedByEveryWave()
        {
            for (int i = 0; i < TsunamiSource.MaxStackedWaves; i++)
            {
                int plus = TsunamiSource.DeltaForWave(i, 70000);
                int minus = TsunamiSource.DeltaForWave(i, -70000);

                Assert.Equal(plus, -minus);
            }

            int negSum = 0;
            for (int i = 0; i < TsunamiSource.MaxStackedWaves; i++)
            {
                negSum += TsunamiSource.DeltaForWave(i, -70000);
            }

            Assert.Equal(-70000, negSum);
        }

        [Fact]
        public void OnlyAsManyWavesAsAreNeededAreUsed()
        {
            Assert.Equal(0, TsunamiSource.WavesNeeded(0));
            Assert.Equal(1, TsunamiSource.WavesNeeded(1));
            Assert.Equal(1, TsunamiSource.WavesNeeded(TsunamiSource.MaxDeltaUnits));
            Assert.Equal(2, TsunamiSource.WavesNeeded(TsunamiSource.MaxDeltaUnits + 1));
            Assert.Equal(TsunamiSource.MaxStackedWaves,
                         TsunamiSource.WavesNeeded(TsunamiSource.MaxDriveUnits));
            Assert.Equal(2, TsunamiSource.WavesNeeded(-TsunamiSource.MaxDeltaUnits - 1));

            // 使わない枠は 0（＝その波は何もしない）。
            Assert.Equal(0, TsunamiSource.DeltaForWave(1, TsunamiSource.MaxDeltaUnits));
        }

        // ── ★★ 測って合わせる ────────────────────────────────────

        [Fact]
        public void TheDriveClimbsWhileTheSeaHasNotMoved()
        {
            // ★★ **これが 2026-08-29 の「発生しない」への答えである。**
            //    実機では 7447 units 押して海が 0.7 m しか動かなかった。
            //    増幅率は IL からは決まらないので、届くまで上げる。
            int drive = TsunamiSource.FirstDriveUnits;

            for (int i = 0; i < 40; i++)
            {
                drive = TsunamiSource.NextDrive(drive, 0f, 10f);
            }

            Assert.Equal(TsunamiSource.MaxDriveUnits, drive);
        }

        [Fact]
        public void TheDriveStopsClimbingOnceTheTargetIsReached()
        {
            int drive = 20000;
            Assert.Equal(drive, TsunamiSource.NextDrive(drive, 10f, 10f));
        }

        [Fact]
        public void TheDriveBacksOffWhenTheSeaOvershoots()
        {
            int drive = 20000;
            int backed = TsunamiSource.NextDrive(drive, 40f, 10f);

            Assert.True(backed < drive, drive + " -> " + backed);
            Assert.True(backed >= (int)(drive * TsunamiSource.MaxStepDown),
                        "it backed off further than the step limit allows");
        }

        [Fact]
        public void TheDriveNeverJumpsMoreThanOneStep()
        {
            // ★★ observed が 0 に近いと比が発散する。**挟まないと海が爆発する。**
            for (int drive = TsunamiSource.FirstDriveUnits;
                 drive <= TsunamiSource.MaxDriveUnits; drive += 4099)
            {
                foreach (float observed in new[] { 0f, 0.001f, 0.7f, 5f, 10f, 400f })
                {
                    int next = TsunamiSource.NextDrive(drive, observed, 10f);

                    Assert.True(next <= (int)(drive * TsunamiSource.MaxStepUp) + 1,
                                "jumped from " + drive + " to " + next
                                + " on observed=" + observed);
                    Assert.InRange(next, TsunamiSource.FirstDriveUnits,
                                   TsunamiSource.MaxDriveUnits);
                }
            }
        }

        [Fact]
        public void TheDriveIsAlwaysInsideItsBounds()
        {
            Assert.Equal(TsunamiSource.FirstDriveUnits,
                         TsunamiSource.NextDrive(0, 100f, 10f));
            Assert.Equal(TsunamiSource.MaxDriveUnits,
                         TsunamiSource.NextDrive(TsunamiSource.MaxDriveUnits, 0f, 10f));
            Assert.Equal(0, TsunamiSource.NextDrive(20000, 5f, 0f));
            Assert.Equal(0, TsunamiSource.NextDrive(20000, 5f, float.NaN));
        }

        [Fact]
        public void AStrongerQuakeAsksForABiggerBulge()
        {
            Assert.True(TsunamiSource.TargetBulgeMetres(255)
                        > TsunamiSource.TargetBulgeMetres(100));
            Assert.InRange(TsunamiSource.TargetBulgeMetres(0),
                           TsunamiSource.MinBulgeMetres, TsunamiSource.MaxBulgeMetres);
            Assert.InRange(TsunamiSource.TargetBulgeMetres(255),
                           TsunamiSource.MinBulgeMetres, TsunamiSource.MaxBulgeMetres);
        }

        // ── ① 隆起: 水を中心へ集める（外力は負）───────────────────────

        [Fact]
        public void TheSeaIsDrawnInFirstSoABulgeCanRise()
        {
            // ★★ ここが所有者の「すぐに震源地に海面の巨大な隆起が生成」である。
            //    IMPACT の外力が**負** ＝ 仮想の窪み ＝ 水は中心へ流れ込む。
            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInFrames * 0.5f, Drive) < 0,
                        "the sea is not being drawn in during stage 1");
            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInFrames * 0.9f, Drive) < 0);
        }

        [Fact]
        public void TheDrawInIsBoundedSoTheCentreCannotBeEmptied()
        {
            float deepest = 0f;

            for (float f = 0f; f < TsunamiSource.DrawInFrames; f += 5f)
            {
                float d = TsunamiSource.DriveAt(f);
                if (d < deepest) deepest = d;
            }

            Assert.Equal(-TsunamiSource.DrawInFactor, deepest, 3);
            Assert.True(TsunamiSource.DrawInFactor < 1f,
                        "drawing in harder than pushing out would leave a hole, not a wave");
        }

        [Fact]
        public void TheBulgeRisesRatherThanPoppingIntoExistence()
        {
            float a = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInFrames * 0.1f));
            float b = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInFrames * 0.25f));
            float c = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInFrames * 0.5f));

            Assert.True(a < b && b < c, a + " " + b + " " + c);
        }

        // ── ② ③ 押し出し: 外力は正 ────────────────────────────────

        [Fact]
        public void TheBulgeIsThenPushedOutward()
        {
            float mid = (TsunamiSource.DrawInFrames + TsunamiSource.TurnFrames
                         + TsunamiSource.PushFrames) * 0.5f;

            Assert.Equal(Drive, TsunamiSource.DeltaAt(mid, Drive));
            Assert.Equal(1f, TsunamiSource.DriveAt(TsunamiSource.PushFrames - 1f), 3);
        }

        [Fact]
        public void TheDriveTurnsOverWithoutAStep()
        {
            // ★★ ソルバは<b>傾きの差分</b>を積むので、段差はそのまま衝撃になる。
            float span = TsunamiSource.DrawInFactor + 1f;

            for (float f = 0f; f < TsunamiSource.TotalFrames; f += 1f)
            {
                float a = TsunamiSource.DriveAt(f);
                float b = TsunamiSource.DriveAt(f + 1f);

                Assert.True(System.Math.Abs(b - a) < span * 0.05f,
                            "the drive jumps from " + a + " to " + b + " at frame " + f);
            }
        }

        // ── ④ 外力を切る ────────────────────────────────────────

        [Fact]
        public void TheDriveIsReleasedSoTheSolverCanCarryTheRing()
        {
            // ★★ **これが作り直しの肝である。** 水の壁を自分で描き続けるのではなく、
            //    外力を切って、あとはゲームの浅水ソルバに運ばせる ——
            //    バニラの津波とまったく同じ経路。
            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalFrames, Drive));
            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalFrames + 600f, Drive));
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

        // ── 時計と寸法 ──────────────────────────────────────────

        [Fact]
        public void TheDriveRunsLongEnoughToMoveRealWater()
        {
            // ★ 目盛りはバニラ: DLC の発生源は 256 フレーム（≒4.3 実秒）しかない。
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
            Assert.Equal(0, TsunamiSource.DeltaAt(float.NaN, Drive));
            Assert.Equal(0, TsunamiSource.DeltaAt(float.PositiveInfinity, Drive));
            Assert.Equal(0, TsunamiSource.DeltaAt(-10f, Drive));
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
