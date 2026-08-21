using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class TyphoonTrackTests
    {
        /// <summary>プレイヤーが指した地点の代わり。マップの中の 1 点。</summary>
        private static readonly Vec2 Pointed = new Vec2(1200f, -800f);

        [Fact]
        public void BearingIsInsideOneTurnAndDependsOnTheSeed()
        {
            int distinct = 0;
            float first = TyphoonTrack.BearingOf(1u);
            for (uint s = 1; s < 200; s++)
            {
                float b = TyphoonTrack.BearingOf(s);
                Assert.InRange(b, 0f, 6.2831855f);
                if (b != first) distinct++;
            }
            Assert.True(distinct >= 190, "too few distinct bearings: " + distinct);
        }

        [Fact]
        public void SameSeedAlwaysGivesTheSameTrack()
        {
            // ここが崩れると「同じセーブで再現できる」（設計書 §4.1）が嘘になる。
            float speed = TyphoonTrack.SpeedFor(20000u);
            for (uint s = 1; s < 50; s++)
            {
                Vec2 a = TyphoonTrack.CentreAt(Pointed, s, 5000u, speed);
                Vec2 b = TyphoonTrack.CentreAt(Pointed, s, 5000u, speed);
                Assert.Equal(a.X, b.X, 4);
                Assert.Equal(a.Z, b.Z, 4);
            }
        }

        [Fact]
        public void TheStormStartsExactlyWhereThePlayerPointed()
        {
            // ★★ バニラの災害ボタンと同じ約束（設計書 §4.1）。指した地点から始まる。
            //    ずれると「押した場所と違うところに台風が出る」になり、
            //    しかも例外は 1 つも出ない。
            float speed = TyphoonTrack.SpeedFor(20000u);
            for (uint s = 1; s < 100; s++)
            {
                Vec2 start = TyphoonTrack.CentreAt(Pointed, s, 0u, speed);
                Assert.Equal(Pointed.X, start.X, 3);
                Assert.Equal(Pointed.Z, start.Z, 3);
                Assert.True(TyphoonTrack.IsInsideMap(start));
            }
        }

        [Fact]
        public void TheTrackStaysOnTheMapForMostOfItsLifetime()
        {
            // ★★ **2026-08-22 に意味が反転した。**
            //   持ち主の指摘「進行をもっとゆっくりに」で NominalPathLength を
            //   マップの一辺から半辺へ半分にしたので、**台風は普通マップの外へ出ない**。
            //   以前ここは「必ず外へ出ること」を固定していた（＝終了条件が
            //   「1 度中に入ってから外へ出た」だけだと思っていた）。
            //   いまの通常の終わり方は「持続時間を使い切った」であり、
            //   その経路も同じ TyphoonController.Stop -> Forget を通る。
            //
            //   ここで固定するのは**都市の上に居続けること**である ——
            //   暴風雨を再現する機能なので、寿命の半分を過ぎても中心がまだ
            //   マップの中に居ること。
            const uint dur = TyphoonPrefabActiveDuration;
            float speed = TyphoonTrack.SpeedFor(dur);

            for (uint s = 1; s < 100; s++)
            {
                Vec2 half = TyphoonTrack.CentreAt(Pointed, s, dur / 2u, speed);
                Assert.True(TyphoonTrack.IsInsideMap(half),
                            "track for seed " + s + " has already left the map at half life");
            }
        }

        [Fact]
        public void TheStormMovesAtHalfTheSpeedItUsedTo()
        {
            // ★ 持ち主の指摘への直接の答え。実測のプレハブ値での速度を固定する。
            //   NominalPathLength を戻すとここが赤くなる。
            float speed = TyphoonTrack.SpeedFor(TyphoonPrefabActiveDuration);
            Assert.Equal(1.0547f, speed, 3);

            // マップの一辺（17280 m）を渡り切るのに掛かるフレーム数 ＝ 寿命の 2 倍。
            float framesToCross = TyphoonTrack.MapHalfExtent * 2f / speed;
            Assert.Equal(2f * TyphoonPrefabActiveDuration, framesToCross, 0);
        }

        /// <summary><c>ThunderStormAI.m_activeDuration</c> の実測値
        /// （<c>sharedassets55.assets</c> の生バイトから復元。IL 事実文書 §A-0b）。</summary>
        private const uint TyphoonPrefabActiveDuration = 8192u;

        [Fact]
        public void ZeroCurvatureIsTheLimitOfSmallCurvature()
        {
            // κ の 0 分岐は「小さいけれど 0 ではない」値でだけ壊れる。
            // 目視では絶対に見つからないので、ここで固定する。
            const float speed = 1.3f;
            float theta = 0.7f;
            var entry = new Vec2(-12000f, 0f);

            Vec2 straight = TyphoonTrack.ArcPosition(entry, theta, 0f, 3000f, speed);
            Vec2 nearlyStraight = TyphoonTrack.ArcPosition(entry, theta, 1e-9f, 3000f, speed);

            Assert.Equal(straight.X, nearlyStraight.X, 1);
            Assert.Equal(straight.Z, nearlyStraight.Z, 1);
        }

        [Fact]
        public void SpeedMatchesTheDistanceTravelledPerFrame()
        {
            float speed = TyphoonTrack.SpeedFor(20000u);
            for (uint s = 1; s < 30; s++)
            {
                Vec2 a = TyphoonTrack.CentreAt(Pointed, s, 4000u, speed);
                Vec2 b = TyphoonTrack.CentreAt(Pointed, s, 4001u, speed);
                float d = (float)System.Math.Sqrt(a.DistanceSquaredTo(b));
                Assert.Equal(speed, d, 2);
            }
        }

        [Fact]
        public void SpeedIsDerivedFromTheMeasuredActiveDuration()
        {
            // 長い嵐ほどゆっくり動く。経路長（マップ 1 辺）は同じなので、持続時間の逆数になる。
            Assert.True(TyphoonTrack.SpeedFor(40000u) < TyphoonTrack.SpeedFor(10000u));
            Assert.InRange(TyphoonTrack.SpeedFor(10u),
                           TyphoonTrack.MinSpeedMetresPerFrame,
                           TyphoonTrack.MaxSpeedMetresPerFrame);
            Assert.InRange(TyphoonTrack.SpeedFor(100000000u),
                           TyphoonTrack.MinSpeedMetresPerFrame,
                           TyphoonTrack.MaxSpeedMetresPerFrame);
        }

        [Fact]
        public void UnknownDurationGivesZeroSpeedNotAGuess()
        {
            // ★ 設計書 §6 の「読めなければ推測せず何もしない」を構造で保証している
            //    唯一の場所。ここを「安全な既定値」に書き換えると、プレハブが
            //    読めない環境で台風が推測値の速度で動き出す。
            Assert.Equal(0f, TyphoonTrack.SpeedFor(0u), 5);
        }

        [Fact]
        public void IntensityRisesPlateausAndFallsLikeTheVanillaLightningRamp()
        {
            const byte peak = 200;
            const uint dur = 20000u;

            byte start = TyphoonTrack.IntensityAt(peak, 0u, dur, 0f);
            byte middle = TyphoonTrack.IntensityAt(peak, dur / 2u, dur, 0f);
            byte end = TyphoonTrack.IntensityAt(peak, dur, dur, 0f);

            Assert.True(start < middle, "the storm must ramp up");
            Assert.Equal(peak, middle);
            Assert.True(end < middle, "the storm must ramp down");
        }

        [Fact]
        public void ALiveTyphoonNeverReportsZeroIntensity()
        {
            // 実機の初回テストで "intensity=0" と出たのはこれである。
            // 台形の端が 0 だと、居るのに何も起きない台風になり、
            // しかも「値が読めなかった」との区別が付かない。
            const uint dur = 8192u;

            foreach (byte peak in new byte[] { 10, 120, 255 })
            {
                Assert.True(TyphoonTrack.IntensityAt(peak, 0u, dur, 0f) > 0,
                            "peak " + peak + " must not start at zero");
                Assert.True(TyphoonTrack.IntensityAt(peak, dur, dur, 0f) > 0,
                            "peak " + peak + " must not end at zero either");
            }
        }

        [Fact]
        public void ZeroIntensityStillMeansUnreadableOrFullyDecayed()
        {
            // 上の床は「丸めで 0 に落ちる」を防ぐだけであって、
            // 0 の意味を奇抜しにしない。
            Assert.Equal(0, TyphoonTrack.IntensityAt(200, 0u, 0u, 0f));      // 持続時間が読めない
            Assert.Equal(0, TyphoonTrack.IntensityAt(200, 4096u, 8192u, 5000f)); // 減衰しきった
        }

        [Fact]
        public void LandfallWeakensTheStormAndTheSeaGivesItBackSlowly()
        {
            float overLand = TyphoonTrack.DecayAfter(0f, true, 10f);
            Assert.True(overLand > 0f, "the storm must weaken over land");

            float recovered = TyphoonTrack.DecayAfter(overLand, false, 10f);
            Assert.True(recovered < overLand, "the sea must give strength back");
            Assert.True(recovered > 0f, "recovery must be slower than decay");

            // 完全に戻り切ることはある。負にはならない。
            Assert.Equal(0f, TyphoonTrack.DecayAfter(0f, false, 1000f), 4);
            // 際限なく積み上がらない。
            Assert.InRange(TyphoonTrack.DecayAfter(0f, true, 100000f), 0f, TyphoonTrack.MaxDecay);
        }

        [Fact]
        public void DecayLowersTheIntensityAndNeverWrapsAround()
        {
            const byte peak = 100;
            const uint dur = 20000u;
            byte weak = TyphoonTrack.IntensityAt(peak, dur / 2u, dur, 90f);
            Assert.True(weak < peak);
            // byte の巻き戻り（255 になる）が最も痛い壊れ方。
            Assert.Equal(0, TyphoonTrack.IntensityAt(peak, dur / 2u, dur, 5000f));
        }

        [Fact]
        public void PhaseWalksForwardsOnlyAndEndsAtGone()
        {
            const uint dur = 20000u;
            Assert.Equal(TyphoonPhase.Approaching, TyphoonTrack.PhaseAt(0u, dur));
            Assert.Equal(TyphoonPhase.Peak, TyphoonTrack.PhaseAt(dur / 2u, dur));
            Assert.Equal(TyphoonPhase.Passing, TyphoonTrack.PhaseAt(dur * 4u / 5u, dur));
            Assert.Equal(TyphoonPhase.Gone, TyphoonTrack.PhaseAt(dur, dur));
            Assert.Equal(TyphoonPhase.Gone, TyphoonTrack.PhaseAt(dur * 10u, dur));

            // 持続時間が読めていないときに位相を名乗らない。
            Assert.Equal(TyphoonPhase.Idle, TyphoonTrack.PhaseAt(0u, 0u));
        }
    }
}
