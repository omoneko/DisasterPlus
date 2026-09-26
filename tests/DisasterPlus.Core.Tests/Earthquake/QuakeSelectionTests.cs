using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// Unifying "which earthquake are we talking about" (overall review I2).
    ///
    /// This ranking used to exist as copies in three places: two were duplicates identical
    /// to the byte, and only the remaining one included <c>Clearing</c>. That one was the
    /// display side, so for an earthquake that was already dying down it still reported
    /// "cursor collapse factor" and "fault zone: inside".
    /// </summary>
    public class QuakeSelectionTests
    {
        private static EarthquakeReading Quake(ushort id, EarthquakePhase phase, byte intensity)
        {
            return new EarthquakeReading(id, new Vec3(0f, 0f, 0f), 0f, intensity, phase,
                                         located: false, startFrame: 0u, activationFrame: 1u,
                                         activationScheduled: true, coverageAtEpicentre: 0,
                                         coverageKnown: true, crackLength: 0f, crackWidth: 0f);
        }

        private static IList<EarthquakeReading> List(params EarthquakeReading[] quakes)
        {
            return new List<EarthquakeReading>(quakes);
        }

        [Fact]
        public void ActiveBeatsEmerging()
        {
            var chosen = QuakeSelection.SelectDamaging(List(
                Quake(1, EarthquakePhase.Emerging, 255),
                Quake(2, EarthquakePhase.Active, 10)));
            Assert.Equal(2, chosen.DisasterId);
        }

        [Fact]
        public void WithinTheSamePhaseTheStrongerWins()
        {
            var chosen = QuakeSelection.SelectDamaging(List(
                Quake(1, EarthquakePhase.Active, 40),
                Quake(2, EarthquakePhase.Active, 90),
                Quake(3, EarthquakePhase.Active, 90)));
            // At equal intensity, keep the lower index (the one seen first).
            Assert.Equal(2, chosen.DisasterId);
        }

        [Fact]
        public void ClearingIsNeverSelected()
        {
            // The overall disc's DestroyBuildings only exists in the Active branch of
            // SimulationStep (§A-3). Choosing an earthquake that is dying down means
            // reporting something that will no longer happen.
            Assert.Null(QuakeSelection.SelectDamaging(List(
                Quake(1, EarthquakePhase.Clearing, 255),
                Quake(2, EarthquakePhase.Finished, 255),
                Quake(3, EarthquakePhase.Unknown, 255))));
        }

        [Fact]
        public void RunsDamageMatchesTheSelection()
        {
            // The display side bows out on this predicate, so it must not disagree with the
            // selection.
            Assert.True(QuakeSelection.RunsDamage(EarthquakePhase.Active));
            Assert.True(QuakeSelection.RunsDamage(EarthquakePhase.Emerging));
            Assert.False(QuakeSelection.RunsDamage(EarthquakePhase.Clearing));
            Assert.False(QuakeSelection.RunsDamage(EarthquakePhase.Finished));
            Assert.False(QuakeSelection.RunsDamage(EarthquakePhase.Unknown));
        }

        [Fact]
        public void EmptyAndNullInputsAreSafe()
        {
            Assert.Null(QuakeSelection.SelectDamaging(null));
            Assert.Null(QuakeSelection.SelectDamaging(new List<EarthquakeReading>()));
        }
    }
}
