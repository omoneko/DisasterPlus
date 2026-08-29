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

        // ── 外力の大きさ（tools/WaterSolverSim で測って決めた）────────────

        [Fact]
        public void AStrongerQuakeDrivesTheSeaHarder()
        {
            Assert.True(TsunamiSource.DriveUnitsFor(255) > TsunamiSource.DriveUnitsFor(100));
            Assert.True(TsunamiSource.DriveUnitsFor(100) > TsunamiSource.DriveUnitsFor(10));
        }

        [Fact]
        public void EvenTheWeakestQuakeDrivesSomething()
        {
            Assert.Equal(TsunamiSource.MinDriveUnits, TsunamiSource.DriveUnitsFor(0));
            Assert.True(TsunamiSource.MinDriveUnits > 0);
        }

        [Fact]
        public void TheStrongestQuakeStillFitsInTheStackedWaves()
        {
            // 頭打ちされると、強度を上げても波が伸びなくなる。
            for (int i = 0; i <= 255; i++)
            {
                Assert.InRange(TsunamiSource.DriveUnitsFor((byte)i),
                               TsunamiSource.MinDriveUnits, TsunamiSource.MaxDriveUnits);
            }
        }

        // ── ① 隆起: 水を中心へ集める（外力は負）───────────────────────

        [Fact]
        public void TheSeaIsDrawnInFirstSoABulgeCanRise()
        {
            // ★★ ここが所有者の「すぐに震源地に海面の巨大な隆起が生成」である。
            //    IMPACT の外力が**負** ＝ 仮想の窪み ＝ 水は中心へ流れ込む。
            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInSteps * 0.5f, Drive) < 0,
                        "the sea is not being drawn in during stage 1");
            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInSteps * 0.9f, Drive) < 0);
        }

        [Fact]
        public void TheDriveEndsByDrawingTheSeaBackIn()
        {
            // ★★ **これが「穴を残さない」の担保である。**
            //    押しっぱなしにすると定常的な流出になり、波ではなく穴ができる
            //    （オフライン再現で確認: 中心が -40 m ＝ 海底まで掘れた）。
            float last = TsunamiSource.DriveAt(TsunamiSource.TotalSteps * 0.85f);
            Assert.True(last < 0f, "the drive does not pull back at the end: " + last);
        }

        [Fact]
        public void TheDriveHasNoNetPush()
        {
            // 時間積分がほぼ 0 —— 正味の押し出しが残ると穴になる。
            double sum = 0.0;
            for (float f = 0f; f < TsunamiSource.TotalSteps; f += 0.25f)
            {
                sum += TsunamiSource.DriveAt(f) * 0.25f;
            }

            // ★ 完全な 0 は要求しない（そこまで縛ると形が選べない）。
            //   縛りたいのは「押しっぱなし」で、それは +0.5*T あたりに出る。
            //   現行の形は -0.085*T（わずかに引き寄りで、穴ではなく僅かな盛り上がり）。
            Assert.True(System.Math.Abs(sum) < TsunamiSource.TotalSteps * 0.20f,
                        "the drive has a net push of " + sum
                        + " (a push-only drive would be about +"
                        + (TsunamiSource.TotalSteps * 0.5f) + ")");
        }

        [Fact]
        public void TheBulgeRisesRatherThanPoppingIntoExistence()
        {
            float a = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInSteps * 0.1f));
            float b = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInSteps * 0.4f));
            float c = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInSteps * 0.7f));

            Assert.True(a < b && b < c, a + " " + b + " " + c);
        }

        // ── ② ③ 押し出し: 外力は正 ────────────────────────────────

        [Fact]
        public void TheBulgeIsThenPushedOutward()
        {
            float mid = (TsunamiSource.DrawInSteps + TsunamiSource.PushSteps) * 0.5f;

            Assert.True(TsunamiSource.DeltaAt(mid, Drive) > 0,
                        "the bulge is not being pushed outward in the middle of the drive");
        }

        [Fact]
        public void TheDriveTurnsOverWithoutAStep()
        {
            // ★★ ソルバは<b>傾きの差分</b>を積むので、段差はそのまま衝撃になる。
            const float span = 2f;   // 外力は [-1, 1]

            for (float f = 0f; f < TsunamiSource.TotalSteps; f += 1f)
            {
                float a = TsunamiSource.DriveAt(f);
                float b = TsunamiSource.DriveAt(f + 1f);

                Assert.True(System.Math.Abs(b - a) < span * 0.05f,
                            "the drive jumps from " + a + " to " + b + " at water step " + f);
            }
        }

        // ── ④ 外力を切る ────────────────────────────────────────

        [Fact]
        public void TheDriveIsReleasedSoTheSolverCanCarryTheRing()
        {
            // ★★ **これが作り直しの肝である。** 水の壁を自分で描き続けるのではなく、
            //    外力を切って、あとはゲームの浅水ソルバに運ばせる ——
            //    バニラの津波とまったく同じ経路。
            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalSteps, Drive));
            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalSteps + 600f, Drive));
            Assert.True(TsunamiSource.IsFinished(TsunamiSource.TotalSteps));
            Assert.False(TsunamiSource.IsFinished(TsunamiSource.TotalSteps - 1f));
        }

        [Fact]
        public void TheDriveFadesRatherThanBeingCutOff()
        {
            // 終わりぎわは 0 へ滑らかに戻る（段差はソルバに衝撃として入る）。
            float c = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.TotalSteps - 1f));
            Assert.True(c < 0.1f, "the drive is cut off at " + c);
        }

        // ── 時計と寸法 ──────────────────────────────────────────

        [Fact]
        public void TheDriveRunsLongEnoughToMoveRealWater()
        {
            // ★ 目盛りはバニラ: DLC の発生源は 256 フレーム（≒4.3 実秒）しかない。
            //   こちらは円形に広げるぶん、それより長く押す。
            // DLC の発生源は m_duration 16384 / 64 = 256 水ステップ。
            Assert.InRange(TsunamiSource.TotalSteps, 60f, 256f);

            // 1 水ステップ = 64 sim フレーム。
            float seconds = TsunamiSource.TotalSteps * 64f / FramesPerRealSecond;
            Assert.InRange(seconds, 60f, 400f);
        }

        [Fact]
        public void TheStagesAreInOrder()
        {
            Assert.True(TsunamiSource.DrawInSteps > 0f);
            Assert.True(TsunamiSource.DrawInSteps < TsunamiSource.PushSteps);
            Assert.True(TsunamiSource.PushSteps < TsunamiSource.TotalSteps);
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
            foreach (float f in new[] { -1f, 0f, TsunamiSource.DrawInSteps * 0.5f,
                                        TsunamiSource.DrawInSteps + 10f,
                                        TsunamiSource.PushSteps - 10f,
                                        TsunamiSource.PushSteps + 10f,
                                        TsunamiSource.TotalSteps + 10f })
            {
                Assert.False(string.IsNullOrEmpty(TsunamiSource.StageAt(f)));
            }

            Assert.NotEqual(TsunamiSource.StageAt(0f),
                            TsunamiSource.StageAt(TsunamiSource.TotalSteps + 1f));
        }
    }
}
