using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 「どの地震の話をしているのか」の一本化（全体レビュー I2）。
    ///
    /// 以前この順位付けは 3 箇所に写しで存在し、2 つは 1 バイトも違わない複製、
    /// 残る 1 つだけが <c>Clearing</c> を含んでいた。その 1 つが表示側だったので、
    /// 収束中の地震について「カーソルの倒壊係数」と「断層帯: 内側」が出ていた。
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
            // 同じ強度なら添字の小さい方（先に見た方）を保つ。
            Assert.Equal(2, chosen.DisasterId);
        }

        [Fact]
        public void ClearingIsNeverSelected()
        {
            // 全体円盤の DestroyBuildings は SimulationStep の Active 分岐にしか無い
            // （§A-3）。収束中の地震を選ぶと「もう起きないこと」を出すことになる。
            Assert.Null(QuakeSelection.SelectDamaging(List(
                Quake(1, EarthquakePhase.Clearing, 255),
                Quake(2, EarthquakePhase.Finished, 255),
                Quake(3, EarthquakePhase.Unknown, 255))));
        }

        [Fact]
        public void RunsDamageMatchesTheSelection()
        {
            // 表示側はこの述語で自分から降りるので、選定と食い違ってはいけない。
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
