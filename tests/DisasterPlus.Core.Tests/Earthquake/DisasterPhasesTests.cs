using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class DisasterPhasesTests
    {
        [Fact]
        public void FlagBitsMatchTheGame()
        {
            // IL facts document §A-1 / §A-6. If these drift, every check breaks silently.
            Assert.Equal(1, DisasterPhases.Created);
            Assert.Equal(2, DisasterPhases.Deleted);
            Assert.Equal(4, DisasterPhases.Emerging);
            Assert.Equal(8, DisasterPhases.Active);
            Assert.Equal(16, DisasterPhases.Clearing);
            Assert.Equal(32, DisasterPhases.Finished);
            Assert.Equal(64, DisasterPhases.SelfTrigger);
            Assert.Equal(256, DisasterPhases.Significant);
            Assert.Equal(4096, DisasterPhases.Located);
        }

        [Fact]
        public void PhaseIsResolvedMostAdvancedFirst()
        {
            Assert.Equal(EarthquakePhase.Emerging,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Emerging));
            Assert.Equal(EarthquakePhase.Active,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Active));
            Assert.Equal(EarthquakePhase.Clearing,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Clearing));
            Assert.Equal(EarthquakePhase.Finished,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Finished));
            Assert.Equal(EarthquakePhase.Unknown, DisasterPhases.PhaseOf(DisasterPhases.Created));
        }

        [Fact]
        public void AliveMirrorsTheVanillaScanCondition()
        {
            // IL: (m_flags & 3) == 1
            Assert.True(DisasterPhases.IsAlive(DisasterPhases.Created));
            Assert.False(DisasterPhases.IsAlive(DisasterPhases.Created | DisasterPhases.Deleted));
            Assert.False(DisasterPhases.IsAlive(0));
        }

        [Fact]
        public void HazardMapNeedsBothGates()
        {
            // IL facts document §A-6: Located && (Emerging|Active).
            // **Byte-for-byte identical** to the storm.
            int located = DisasterPhases.Created | DisasterPhases.Located;
            Assert.False(DisasterPhases.PaintsHazardMap(located));                              // not in progress
            Assert.True(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Emerging));
            Assert.True(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Active));
            Assert.False(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Clearing));    // not in progress
        }

        [Fact]
        public void HazardMapIsNeverPaintedWithoutLocated()
        {
            // Without a seismometer the earthquake never shows on the hazard map. That is
            // the grounds for not letting 0 be read as "safe".
            int inProgress = DisasterPhases.Created | DisasterPhases.Active;
            Assert.False(DisasterPhases.PaintsHazardMap(inProgress));
            Assert.False(DisasterPhases.IsLocated(inProgress));
        }

        [Fact]
        public void PhaseBasedGateAgreesWithTheRawFlagGate()
        {
            // Pins down that the phase-based gate added by Task 4 agrees with the raw
            // m_flags one. The grounds for that agreement are that the phase bits are
            // mutually exclusive (§A-1: ActivateDisaster is (m_flags & ~4)|8 and
            // DeactivateDisaster is (m_flags & ~12)|16, so every transition drops the
            // previous bit). We therefore scan only mutually exclusive combinations ——
            // demanding agreement even for **unreachable** combinations such as
            // Active|Clearing would mean bending the faithfulness of the raw m_flags
            // version for the sake of a state that does not exist.
            int[] phaseBits =
            {
                0,
                DisasterPhases.Emerging,
                DisasterPhases.Active,
                DisasterPhases.Clearing,
                DisasterPhases.Finished,
            };

            foreach (int phaseBit in phaseBits)
            {
                for (int located = 0; located < 2; located++)
                {
                    int flags = DisasterPhases.Created | phaseBit
                                | (located == 1 ? DisasterPhases.Located : 0);

                    Assert.Equal(
                        DisasterPhases.PaintsHazardMap(flags),
                        DisasterPhases.PaintsHazardMap(
                            DisasterPhases.IsLocated(flags), DisasterPhases.PhaseOf(flags)));
                }
            }
        }

        [Fact]
        public void SelfTriggerIsIndependentOfPhase()
        {
            // Forget to set it and it stalls in Emerging forever (§A-1). The reading side
            // needs to tell the two apart.
            int emerging = DisasterPhases.Created | DisasterPhases.Emerging;
            Assert.Equal(EarthquakePhase.Emerging, DisasterPhases.PhaseOf(emerging));
            Assert.Equal(EarthquakePhase.Emerging,
                DisasterPhases.PhaseOf(emerging | DisasterPhases.SelfTrigger));
        }
    }
}
