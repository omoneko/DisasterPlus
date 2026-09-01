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
        public void ItCrossesTheWholeMapInOneLifeSoItCanArriveFromOutside()
        {
            // ★★ **2026-09-02 に意図が反転した。** 以前は「寿命いっぱいでも
            //   渡り切らない」ことを固定していたが、いまは台風が
            //   <b>マップ外から入ってきて、クリック地点を通って、去る</b>。
            //   そのためには一辺ぶんの道のりが要る（NominalPathLength の doc）。
            //
            // ★ 「ゆっくり見える」は速さではなく<b>接近に使う割合</b>で作る。
            //   速すぎると言われたら下げるのは ApproachFraction のほうである。
            float speed = TyphoonTrack.SpeedFor(TyphoonTrack.LifetimeFramesFor(HostDuration));
            float life = TyphoonTrack.LifetimeFramesFor(HostDuration);

            float travelled = speed * life;
            Assert.True(travelled >= TyphoonTrack.MapHalfExtent * 2f,
                        "it must be able to cross the map to arrive from outside");
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
