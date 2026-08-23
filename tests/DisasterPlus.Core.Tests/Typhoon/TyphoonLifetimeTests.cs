using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// 実機報告（2026-08-22）「台風についてはエフェクトがすぐに消えてしまいます。
    /// 台風がゆっくりと移動する様子を再現してください」。
    ///
    /// ★★ 寿命を延ばすと**速度は自動的に落ちる**（経路長を寿命で割るため）。
    ///    ここが固定するのはその関係と、宿主の持続時間が読めないときの振る舞いである。
    /// </summary>
    public class TyphoonLifetimeTests
    {
        /// <summary>ThunderStormAI の実測値（sharedassets55）。</summary>
        private const uint HostDuration = 8192u;

        [Fact]
        public void TheTyphoonOutlivesItsHostStorm()
        {
            uint life = TyphoonTrack.LifetimeFramesFor(HostDuration);

            Assert.True(life > HostDuration, life + " is not longer than the host's " + HostDuration);
            Assert.Equal(HostDuration * TyphoonTrack.LifetimeMultiplier, life);
        }

        [Fact]
        public void ALongerLifeMeansASlowerTyphoon()
        {
            float before = TyphoonTrack.SpeedFor(HostDuration);
            float after = TyphoonTrack.SpeedFor(TyphoonTrack.LifetimeFramesFor(HostDuration));

            Assert.True(after < before, after + " should be slower than " + before);

            // 経路長は変えていないので、速度はちょうど倍率ぶん落ちる
            // （帯でクランプされない限り）。
            Assert.Equal(before / TyphoonTrack.LifetimeMultiplier, after, 4);
        }

        [Fact]
        public void ItStillTakesLongerThanItsLifeToCrossTheWholeMap()
        {
            // 経路長はマップの半辺なので、寿命いっぱいでも渡り切らない ——
            // 「ゆっくり」を寿命の延長だけで作っていないことの確認。
            float speed = TyphoonTrack.SpeedFor(TyphoonTrack.LifetimeFramesFor(HostDuration));
            float life = TyphoonTrack.LifetimeFramesFor(HostDuration);

            float travelled = speed * life;
            Assert.True(travelled < TyphoonTrack.MapHalfExtent * 2f,
                        "it crosses the whole map within its life");
        }

        [Fact]
        public void AnUnreadableHostDurationYieldsNothingToStartWith()
        {
            // 読めなければ 0。呼び出し側は台風を 1 個も起こさない（設計書 §6）。
            Assert.Equal(0u, TyphoonTrack.LifetimeFramesFor(0u));
            Assert.Equal(0f, TyphoonTrack.SpeedFor(0u), 5);
        }

        [Fact]
        public void AnAbsurdHostDurationDoesNotOverflow()
        {
            // .cgs もプレハブも手で変えられうる。掛け算で巻き戻らないこと。
            uint life = TyphoonTrack.LifetimeFramesFor(uint.MaxValue);
            Assert.True(life >= uint.MaxValue / TyphoonTrack.LifetimeMultiplier,
                        "the lifetime wrapped around");
        }
    }
}
