using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 所有者の指示（2026-08-22）「発生は、アイコンクリック→左クリックした場所に
    /// 一番近い海で発生にしてください」。
    ///
    /// ★★ <b>「近い順」は実機では確かめられない。</b> 順序が壊れていても海は
    ///    どこかで見つかるので、<b>少し遠い海が選ばれるだけ</b>で誰も気づけない。
    ///    だから順序そのものをここで固定する。
    /// </summary>
    public class SeaSearchTests
    {
        [Fact]
        public void TheFirstPlaceCheckedIsThePointItself()
        {
            float dx, dz;
            Assert.True(SeaSearch.At(0, out dx, out dz));
            Assert.Equal(0f, dx, 4);
            Assert.Equal(0f, dz, 4);
        }

        [Fact]
        public void EveryPlaceIsCheckedBeforeAnyPlaceFurtherOut()
        {
            // ★★ これが「一番近い海」の中身である。リングは正方形なので
            //    厳密な最近傍ではないが、**外側のリングは必ず内側より後**である。
            int previousRing = -1;

            for (int i = 0; i < SeaSearch.Count; i++)
            {
                float dx, dz;
                Assert.True(SeaSearch.At(i, out dx, out dz), "ordinal " + i + " was refused");

                int cx = (int)System.Math.Round(dx / SeaSearch.StepMetres);
                int cz = (int)System.Math.Round(dz / SeaSearch.StepMetres);
                int ring = System.Math.Max(System.Math.Abs(cx), System.Math.Abs(cz));

                Assert.True(ring >= previousRing,
                            "ordinal " + i + " stepped back from ring " + previousRing
                            + " to " + ring);
                previousRing = ring;
            }
        }

        [Fact]
        public void NoPlaceIsVisitedTwice()
        {
            var seen = new System.Collections.Generic.HashSet<long>();

            for (int i = 0; i < SeaSearch.Count; i++)
            {
                float dx, dz;
                Assert.True(SeaSearch.At(i, out dx, out dz));

                long key = ((long)System.Math.Round(dx) << 20)
                           ^ (long)System.Math.Round(dz);
                Assert.True(seen.Add(key), "ordinal " + i + " repeats a place");
            }
        }

        [Fact]
        public void TheSearchReachesRightAcrossTheMap()
        {
            // マップ半辺は 8640 m。どこを指しても、海があるなら届くこと。
            float reach = SeaSearch.StepMetres * SeaSearch.MaxRing;
            Assert.True(reach > 8640f,
                        "the search only reaches " + reach + " m; the map half-extent is 8640");
        }

        [Fact]
        public void TheStepIsFineEnoughNotToJumpOverAnInlet()
        {
            // 刻みが粗すぎると、狭い入り江を跨いで遠い外海が選ばれる。
            Assert.True(SeaSearch.StepMetres <= 128f,
                        "the step is " + SeaSearch.StepMetres + " m; narrow inlets fall through");
        }

        [Fact]
        public void AnOutOfRangeOrdinalIsRefusedRatherThanFaked()
        {
            // **「それらしい 0」を返さない。**
            float dx, dz;
            Assert.False(SeaSearch.At(-1, out dx, out dz));
            Assert.False(SeaSearch.At(SeaSearch.Count, out dx, out dz));
        }

        [Fact]
        public void TheDistanceIsTheRealDistance()
        {
            Assert.Equal(0f, SeaSearch.DistanceMetres(0f, 0f), 4);
            Assert.Equal(5f, SeaSearch.DistanceMetres(3f, 4f), 4);
        }

        [Fact]
        public void TheCountMatchesTheSquareOfTheReach()
        {
            int side = SeaSearch.MaxRing * 2 + 1;
            Assert.Equal(side * side, SeaSearch.Count);
        }
    }
}
