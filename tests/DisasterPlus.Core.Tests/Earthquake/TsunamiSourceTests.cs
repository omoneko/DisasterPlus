using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// Owner's instruction (2026-08-29):
    ///
    /// &gt; Study the tsunami mechanism of the Natural Disasters DLC,
    /// &gt; and rebuild what I am asking for from scratch.
    ///
    /// The research is in <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>.
    /// What is pinned down here is only <b>the shape of the forcing applied at the
    /// epicentre, and how its magnitude is decided</b> — the wave itself is produced by
    /// the game's shallow-water solver, so it is not ours.
    /// </summary>
    public class TsunamiSourceTests
    {
        /// <summary>A rough figure for game speed 1.
        /// **A note so that frames and real seconds are not mixed up.**</summary>
        private const float FramesPerRealSecond = 60f;

        /// <summary>The magnitude of the forcing used in the tests
        /// (in <c>m_delta</c> units).</summary>
        private const int Drive = 40000;

        // ── The vanilla scale (values measured from IL and the prefab) ─────────────

        [Fact]
        public void TheVanillaScaleMatchesTheGamesOwnFormula()
        {
            // IL: m_delta = round(m_height * 65536/1024 * intensity / 55)
            //     measured on the prefab: m_height = 64
            Assert.Equal(64f, TsunamiSource.VanillaHeightMetres);
            Assert.Equal(7447, TsunamiSource.VanillaDeltaUnits(100));
            Assert.Equal(4096, TsunamiSource.VanillaDeltaUnits(55));
            Assert.Equal(0, TsunamiSource.VanillaDeltaUnits(0));
            Assert.Equal(18991, TsunamiSource.VanillaDeltaUnits(255));
        }

        // ── Sharing the forcing out over the stacked waves ─────────────────────────

        [Fact]
        public void EveryWaveGetsAValueTheGameCanStoreInAnInt16()
        {
            // ★★ m_delta is an Int16. Go beyond it and **the sign wraps round, so the
            //    forcing reverses.**
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
            // ★★ **This is what "stacking" means.** The forcing is added up wave by wave,
            //    so the total of the shares handed out must be exactly the forcing we aimed
            //    for.
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

            // Unused slots are 0 (that wave does nothing).
            Assert.Equal(0, TsunamiSource.DeltaForWave(1, TsunamiSource.MaxDeltaUnits));
        }

        // ── The magnitude of the forcing (measured and settled with tools/WaterSolverSim) ──

        private const float Deep = TsunamiSource.ReferenceDepthMetres;

        [Fact]
        public void AStrongerQuakeDrivesTheSeaHarder()
        {
            Assert.True(TsunamiSource.DriveUnitsFor(255, Deep)
                        > TsunamiSource.DriveUnitsFor(100, Deep));
            Assert.True(TsunamiSource.DriveUnitsFor(100, Deep)
                        > TsunamiSource.DriveUnitsFor(10, Deep));
        }

        [Fact]
        public void EvenTheWeakestQuakeDrivesSomething()
        {
            Assert.Equal(TsunamiSource.MinDriveUnits, TsunamiSource.DriveUnitsFor(0, Deep));
            Assert.True(TsunamiSource.MinDriveUnits > 0);
        }

        [Fact]
        public void TheStrongestQuakeStaysInsideTheBandWeMeasured()
        {
            // ★★ Beyond the ceiling it is not "strong" but **broken** ——
            //    at a depth of 40 m, a drive of 1500 dug the epicentre right through to the
            //    seabed (tools/WaterSolverSim, grid 1081).
            for (int i = 0; i <= 255; i++)
            {
                Assert.InRange(TsunamiSource.DriveUnitsFor((byte)i, Deep),
                               TsunamiSource.MinDriveUnits,
                               TsunamiSource.MaxIntensityDriveUnits);
            }

            Assert.True(TsunamiSource.MaxIntensityDriveUnits <= 1200,
                        "above 1200 the epicentre is dug down to bare seabed");
        }

        // ── ★★ Divide by the water depth ──────────────────────────────────────────

        [Fact]
        public void ShallowSeasGetAProportionallySmallerSource()
        {
            // ★★ The solver's flow is capped at the water depth by v = min(v, m_height).
            //    Apply a deep-sea forcing to a shallow sea and **the seabed is laid bare at
            //    the epicentre**.
            int deep = TsunamiSource.DriveUnitsFor(100, Deep);
            int shallow = TsunamiSource.DriveUnitsFor(100, Deep * 0.25f);

            Assert.True(shallow < deep, deep + " -> " + shallow);
            Assert.Equal(deep * 0.25f, shallow, 0);
        }

        [Fact]
        public void TheDepthFactorIsBounded()
        {
            Assert.Equal(1f, TsunamiSource.DepthFactor(Deep), 4);
            Assert.Equal(TsunamiSource.MinDepthFactor, TsunamiSource.DepthFactor(0.01f), 4);
            Assert.Equal(TsunamiSource.MinDepthFactor, TsunamiSource.DepthFactor(0f), 4);
            Assert.Equal(TsunamiSource.MinDepthFactor, TsunamiSource.DepthFactor(-5f), 4);
            Assert.Equal(TsunamiSource.MinDepthFactor, TsunamiSource.DepthFactor(float.NaN), 4);
            Assert.Equal(TsunamiSource.MaxDepthFactor, TsunamiSource.DepthFactor(9999f), 4);
        }

        [Fact]
        public void TheDriveScalesWithDepthSoTheDugFractionStaysTheSame()
        {
            // ★★ **This is what stops the 2026-08-30 in-game report "no tsunami appears"
            //    from happening again.** The ceiling had been set to 1.0, so a sea 174 m
            //    deep was only given the forcing meant for 40 m of water; <b>only 15% of the
            //    water column was dug out</b>, the ring reached only 2-3 m, and it was
            //    invisible on the open sea.
            //
            //    The response is independent of depth (in tools/WaterSolverSim, 40 m and
            //    174 m agreed in every column), so the dug fraction is 0.0329 * drive /
            //    depth. The line that never exceeds 70% is drive ≒ 21 * depth.
            for (float depth = 24f; depth <= 500f; depth += 8f)
            {
                for (int i = 0; i <= 255; i += 17)
                {
                    int drive = TsunamiSource.DriveUnitsFor((byte)i, depth);

                    Assert.True(drive <= 22.5f * depth + 1f,
                                "drive " + drive + " at depth " + depth
                                + " digs more than 74% of the water column");
                    Assert.True(drive >= 19f * depth - 1f,
                                "drive " + drive + " at depth " + depth
                                + " leaves most of the column unused - the wave will be "
                                + "invisible on the open sea");
                }
            }
        }

        [Fact]
        public void TheDeepestPossibleSeaStillFitsTheStackedWaves()
        {
            // WaterSimulation.MAX_SEA_LEVEL is 500 (measured from IL).
            int deepest = TsunamiSource.DriveUnitsFor(255, 500f);

            Assert.InRange(deepest, 1, TsunamiSource.MaxDriveUnits);
            Assert.Equal(12.5f, TsunamiSource.MaxDepthFactor, 3);
        }

        [Fact]
        public void AShallowSeaStillGetsSomething()
        {
            // **It must not be 0.** Tsunamis happen in shallow water too
            // (indeed that is where the damage is done).
            Assert.True(TsunamiSource.DriveUnitsFor(255, 1f) > 0);
            Assert.True(TsunamiSource.MinDepthFactor > 0f);
        }

        // ── ① Uplift: gather the water towards the centre (forcing is negative) ────

        [Fact]
        public void TheSeaIsDrawnInFirstSoABulgeCanRise()
        {
            // ★★ This is the owner's "a huge bulge of sea surface is created at the
            //    epicentre straight away". A **negative** IMPACT forcing = a virtual hollow
            //    = the water flows in towards the centre.
            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInSteps * 0.5f, Drive) < 0,
                        "the sea is not being drawn in during stage 1");
            Assert.True(TsunamiSource.DeltaAt(TsunamiSource.DrawInSteps * 0.9f, Drive) < 0);
        }

        [Fact]
        public void TheDrawInComesFirstAndThePushSecond()
        {
            // ① a long draw-in (negative) -> ② a short, hard push (positive).
            // The shape that won the sweep.
            Assert.True(TsunamiSource.DriveAt(TsunamiSource.TotalSteps * 0.3f) < 0f);
            Assert.True(TsunamiSource.DriveAt(TsunamiSource.TotalSteps * 0.8f) > 0f);
        }

        [Fact]
        public void ThePushIsShorterAndHarderThanTheDrawIn()
        {
            float draw = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.TotalSteps * 0.3f));
            float push = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.TotalSteps * 0.8f));

            Assert.True(push > draw, draw + " -> " + push);
            Assert.Equal(TsunamiSource.PushOvershoot, push / draw, 2);
        }

        [Fact]
        public void TheDriveHasNoNetPush()
        {
            // The time integral is nearly 0 —— if a net push is left over it becomes a hole.
            double sum = 0.0;
            for (float f = 0f; f < TsunamiSource.TotalSteps; f += 0.25f)
            {
                sum += TsunamiSource.DriveAt(f) * 0.25f;
            }

            // ★ Exactly 0 is not demanded (tying it that tightly would leave no choice of
            //   shape). What we want to rule out is "pushing the whole time", and that shows
            //   up around +0.5*T.
            //   The current shape gives -0.085*T (slightly draw-heavy, so a small rise rather
            //   than a hole).
            // ★★ **A shape that comes out at exactly 0 has been chosen** ——
            //    0.6 * (2/pi) == 0.4 * 1.5 * (2/pi). Pushing the whole time would give +0.5*T.
            Assert.True(System.Math.Abs(sum) < TsunamiSource.TotalSteps * 0.01f,
                        "the drive has a net push of " + sum
                        + " (a push-only drive would be about +"
                        + (TsunamiSource.TotalSteps * 0.5f) + ")");
        }

        [Fact]
        public void TheBulgeRisesRatherThanPoppingIntoExistence()
        {
            float a = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInSteps * 0.1f));
            float b = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInSteps * 0.3f));
            float c = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.DrawInSteps * 0.5f));

            Assert.True(a < b && b < c, a + " " + b + " " + c);
        }

        // ── ② ③ Pushing outward: the forcing is positive ───────────────────────────

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
            // ★★ The solver accumulates <b>differences of slope</b>, so a step goes straight
            //    in as a shock.
            const float span = 2f;   // the forcing lies in [-1, 1]

            for (float f = 0f; f < TsunamiSource.TotalSteps; f += 1f)
            {
                float a = TsunamiSource.DriveAt(f);
                float b = TsunamiSource.DriveAt(f + 1f);

                Assert.True(System.Math.Abs(b - a) < span * 0.05f,
                            "the drive jumps from " + a + " to " + b + " at water step " + f);
            }
        }

        // ── ④ Release the forcing ─────────────────────────────────────────────────

        [Fact]
        public void TheDriveIsReleasedSoTheSolverCanCarryTheRing()
        {
            // ★★ **This is the heart of the rebuild.** Instead of carrying on drawing the
            //    wall of water ourselves, we release the forcing and let the game's
            //    shallow-water solver carry it from there ——
            //    exactly the same path as the vanilla tsunami.
            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalSteps, Drive));
            Assert.Equal(0, TsunamiSource.DeltaAt(TsunamiSource.TotalSteps + 600f, Drive));
            Assert.True(TsunamiSource.IsFinished(TsunamiSource.TotalSteps));
            Assert.False(TsunamiSource.IsFinished(TsunamiSource.TotalSteps - 1f));
        }

        [Fact]
        public void TheDriveFadesRatherThanBeingCutOff()
        {
            // Near the end it returns smoothly to 0 (a step enters the solver as a shock).
            float c = System.Math.Abs(TsunamiSource.DriveAt(TsunamiSource.TotalSteps - 1f));
            Assert.True(c < 0.1f, "the drive is cut off at " + c);
        }

        // ── Clock and dimensions ──────────────────────────────────────────────────

        [Fact]
        public void TheDriveRunsLongEnoughToMoveRealWater()
        {
            // ★ The scale is vanilla: the DLC source lasts only 256 frames (≒4.3 real
            //   seconds). Ours pushes for longer than that, because it spreads in a circle.
            // The DLC source is m_duration 16384 / 64 = 256 water steps.
            // The DLC source is m_duration 16384/64 = 256 water steps. Same order of size.
            Assert.InRange(TsunamiSource.TotalSteps, 60f, 400f);

            // 1 water step = 64 sim frames.
            float seconds = TsunamiSource.TotalSteps * 64f / FramesPerRealSecond;
            Assert.InRange(seconds, 60f, 500f);
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
            // 1 cell is 16 m, the map half-extent is 8640 m.
            Assert.Equal(TsunamiSource.RadiusCells * 16f, TsunamiSource.RadiusMetres, 3);
            Assert.InRange(TsunamiSource.RadiusMetres, 800f, 4000f);
        }

        // ── Broken input ──────────────────────────────────────────────────────────

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
