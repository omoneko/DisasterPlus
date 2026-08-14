using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class DisasterPhasesTests
    {
        [Fact]
        public void FlagBitsMatchTheGame()
        {
            // IL 事実文書 §A-1 / §A-6。ここがずれると全ての判定が静かに壊れる。
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
            // IL 事実文書 §A-6: Located && (Emerging|Active)。嵐と**バイト単位で同一**。
            int located = DisasterPhases.Created | DisasterPhases.Located;
            Assert.False(DisasterPhases.PaintsHazardMap(located));                              // 進行中でない
            Assert.True(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Emerging));
            Assert.True(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Active));
            Assert.False(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Clearing));    // 進行中でない
        }

        [Fact]
        public void HazardMapIsNeverPaintedWithoutLocated()
        {
            // 地震計が無ければ地震はハザードマップに出ない。0 を「安全」と読ませない根拠。
            int inProgress = DisasterPhases.Created | DisasterPhases.Active;
            Assert.False(DisasterPhases.PaintsHazardMap(inProgress));
            Assert.False(DisasterPhases.IsLocated(inProgress));
        }

        [Fact]
        public void PhaseBasedGateAgreesWithTheRawFlagGate()
        {
            // Task 4 が足した位相版のゲートが、生の m_flags 版と一致することを固定する。
            // 一致の根拠は「位相のビットが互いに排他」であること（§A-1: ActivateDisaster は
            // (m_flags & ~4)|8、DeactivateDisaster は (m_flags & ~12)|16 と、遷移のたびに
            // 前のビットを落とす）。したがって走査するのも排他な組み合わせだけにする——
            // Active|Clearing のような**到達しない**組み合わせまで一致を要求すると、
            // 実在しない状態のために生の m_flags 版の忠実さを曲げることになる。
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
            // 立て忘れると Emerging で永久に止まる（§A-1）。読み側はこの区別が要る。
            int emerging = DisasterPhases.Created | DisasterPhases.Emerging;
            Assert.Equal(EarthquakePhase.Emerging, DisasterPhases.PhaseOf(emerging));
            Assert.Equal(EarthquakePhase.Emerging,
                DisasterPhases.PhaseOf(emerging | DisasterPhases.SelfTrigger));
        }
    }
}
