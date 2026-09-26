using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// The owner's instruction (2026-08-22): "for the trigger, please make it happen in
    /// the sea nearest to the place that was left-clicked after clicking the icon".
    ///
    /// ★★ <b>"Nearest first" cannot be confirmed in the running game.</b> Even with the
    ///    order broken the sea is still found somewhere, so <b>a slightly more distant
    ///    sea is simply picked</b> and nobody can notice. That is why the order itself
    ///    is pinned down here.
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
            // ★★ This is what "the nearest sea" actually amounts to. The rings are square,
            //    so it is not a strict nearest neighbour, but **an outer ring always comes
            //    after an inner one**.
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
            // The map half-extent is 8640 m. Wherever you point, if there is a sea, the
            // search must reach it.
            float reach = SeaSearch.StepMetres * SeaSearch.MaxRing;
            Assert.True(reach > 8640f,
                        "the search only reaches " + reach + " m; the map half-extent is 8640");
        }

        [Fact]
        public void TheStepIsFineEnoughNotToJumpOverAnInlet()
        {
            // Too coarse a step strides over a narrow inlet and picks the distant open sea.
            Assert.True(SeaSearch.StepMetres <= 128f,
                        "the step is " + SeaSearch.StepMetres + " m; narrow inlets fall through");
        }

        [Fact]
        public void AnOutOfRangeOrdinalIsRefusedRatherThanFaked()
        {
            // **Do not return a plausible-looking 0.**
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
