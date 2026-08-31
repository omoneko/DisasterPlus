using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// <see cref="SeaCell"/> —— 「そのセルは外洋か」。
    ///
    /// ★★ **この式は 3 回書き直され、そのたびに別の地形を取り違えた。**
    ///   ここに並ぶ 1 件 1 件が、実際に踏んだ穴である
    ///   （<see cref="SeaCell"/> のクラス doc）。減らさないこと。
    /// </summary>
    public class SeaCellTests
    {
        // 海面 40 m ＝ バニラの既定。最小水深 24 m。
        private const int Sea = 40 * 64;
        private const int MinDepth = 24 * 64;

        private static bool Is(float terrainMetres, float columnMetres)
        {
            return SeaCell.IsOpenSea((int)(terrainMetres * 64f), (int)(columnMetres * 64f),
                                     Sea, MinDepth);
        }

        [Fact]
        public void Real_open_sea_is_sea()
        {
            // 海底 5 m、水柱 35 m。
            Assert.True(Is(5f, 35f));
        }

        [Fact]
        public void Water_too_shallow_is_not_sea()
        {
            // 海底 25 m、水柱 15 m。深さが足りない。
            Assert.False(Is(25f, 15f));
        }

        [Fact]
        public void A_dry_basin_below_sea_level_is_not_sea()
        {
            // ★★ 堤防で囲まれた干拓地。**海面より 40 m 低いが乾いている。**
            //    海底の高さだけ見ていた版はここを「海」と答え、
            //    半径 3.8 km の水源を乾いた土地に置こうとした。
            Assert.False(Is(0f, 0f));

            // 底に少しだけ雨水が溜まっていても同じ。
            Assert.False(Is(0f, 1f));
        }

        [Fact]
        public void A_high_river_is_not_sea()
        {
            // ★★ 標高 60 m の谷を流れる、深さ 30 m の川。
            //    水柱だけ見ていた版はここを「海」と答えた。
            Assert.False(Is(60f, 30f));
        }

        [Fact]
        public void A_lake_above_sea_level_is_not_sea()
        {
            // 湖面 70 m、湖底 41 m（海面より 1 m 高い）。
            Assert.False(Is(41f, 29f));
        }

        [Fact]
        public void Flooded_land_is_not_sea()
        {
            // ★★ 前の津波で冠水した街。地面は標高 20 m（海面より下ではない…
            //    ではなく、海面 40 m に対して 20 m なので海面下）。
            //    **ここが肝心**: 陸の標高が「海面 - 最小水深」より高ければ、
            //    どれだけ水を被っていても海ではない。
            Assert.False(Is(20f, 30f));   // 20 > 40 - 24 = 16 なので陸
            Assert.True(Is(16f, 30f));    // ちょうど境目は海
        }

        [Fact]
        public void A_storm_surge_does_not_turn_the_sea_into_land()
        {
            // ★★ **これが 3 回目の書き直しの理由である。**
            //    水面の高さで川を落とす版は、高潮のあいだ外洋を丸ごと
            //    「海ではない」と答え、プレイヤーに
            //    「2,304 m 以内に深い海がありません」と表示していた。
            //    海底は動かないので、水柱が増えても判定は変わらない。
            Assert.True(Is(5f, 35f));    // 平常
            Assert.True(Is(5f, 55f));    // 海面が 20 m 上がっている最中
            Assert.True(Is(5f, 135f));   // 津波の頂点
        }

        [Fact]
        public void The_boundary_is_inclusive_on_the_seabed_and_on_the_column()
        {
            // 海底がちょうど「海面 - 最小水深」なら海。
            Assert.True(Is(16f, 24f));

            // その 1 単位上は陸。
            Assert.False(SeaCell.IsOpenSea(16 * 64 + 1, 24 * 64, Sea, MinDepth));

            // 水柱がちょうど最小水深なら海。1 単位下は違う。
            Assert.True(SeaCell.IsOpenSea(5 * 64, MinDepth, Sea, MinDepth));
            Assert.False(SeaCell.IsOpenSea(5 * 64, MinDepth - 1, Sea, MinDepth));
        }

        [Fact]
        public void A_deep_map_works_the_same_way()
        {
            // 海面 207 m のマップ（プレイヤーが海面を上げたもの）。
            int sea = 207 * 64;
            int min = 24 * 64;

            Assert.True(SeaCell.IsOpenSea(30 * 64, 177 * 64, sea, min));   // 水深 177 m
            Assert.False(SeaCell.IsOpenSea(30 * 64, 0, sea, min));         // 乾いた窪地
            Assert.False(SeaCell.IsOpenSea(220 * 64, 40 * 64, sea, min));  // 高い湖
        }

        [Fact]
        public void Asking_for_no_depth_still_needs_water()
        {
            // 最小水深 0 でも、水が 0 なら海ではない…
            // ではなく、0 >= 0 なので海になる。**その挙動をここで固定する** ——
            // 呼び出し側は必ず正の最小水深を渡すこと。
            Assert.True(SeaCell.IsOpenSea(0, 0, Sea, 0));
        }
    }
}
