using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 所有者の指示（2026-08-25）「震源地を中心に２-3 個の連続する山状：
    /// 実際の津波メカニズムで」。
    ///
    /// ★★ いちばん大事なのは<b>終わりに 0 へ戻ること</b>である。
    ///    戻らないと、呼び出し側が水位を下げる合図を受け取れず、
    ///    <b>持ち上げた海がセーブに残る</b>。
    /// </summary>
    public class TsunamiWaveTrainTests
    {
        private const float Amplitude = 6f;

        [Fact]
        public void TheWaveTravelsOutwardFromTheEpicentre()
        {
            // ★★ **いちばん高い点を追ってはいけない。**（テストを 1 度書き直した）
            //    第 2 波のほうが高いので、それが生まれた瞬間に「最大の山」は
            //    <b>内側へ跳ぶ</b>。それはモデルが正しい証拠であって、
            //    波が戻っているわけではない。
            //
            //    外へ広がることを見るなら<b>いちばん外の波がどこまで届いたか</b>で
            //    見る。こちらは単調でなければならない。
            float early = OuterEdgeAt(TsunamiWaveTrain.TotalSeconds * 0.05f);
            float late = OuterEdgeAt(TsunamiWaveTrain.TotalSeconds * 0.15f);
            float later = OuterEdgeAt(TsunamiWaveTrain.TotalSeconds * 0.30f);

            Assert.True(late > early, "the front did not move outward: " + early + " -> " + late);
            Assert.True(later > late, "the front stalled: " + late + " -> " + later);
        }

        [Fact]
        public void TheBiggestCrestIsNotTheFirstOneToArrive()
        {
            // 第 1 波が最大とは限らない（実際の津波でも、避難を解いた人が
            // 第 2 波にさらわれるのが典型的な被害である）。
            //
            // ★★ **「山のある場所」で比べてはいけない。**（テストを 2 度書き直した）
            //    k 本目がこの地点に届くのは <c>k×gap + d/speed</c> なので、
            //    サンプル時刻を gap の倍数で取ると<b>どの波の前線も同じ距離</b>に来る。
            //    比べるべきは位置ではなく<b>その地点が実際に何 m 上がるか</b>である。
            const float Distance = 2400f;
            float travel = Distance / TsunamiWaveTrain.SpeedMetresPerSecond;

            float first = TsunamiWaveTrain.RiseAt(Distance, travel, Amplitude);
            float second = TsunamiWaveTrain.RiseAt(
                Distance, TsunamiWaveTrain.CrestGapSeconds + travel, Amplitude);

            Assert.True(second > first,
                        "the second crest is not the bigger one; that is not how a "
                        + "tsunami behaves (" + first + " m -> " + second + " m)");
        }

        [Fact]
        public void ThereAreSeveralCrestsNotOne()
        {
            // ★★ 「2-3 個の連続する山状」。ある地点で、時間とともに山が複数回来ること。
            const float Distance = 2500f;

            int peaks = 0;
            float previous = 0f;
            bool rising = false;

            for (int i = 0; i <= 800; i++)
            {
                float t = TsunamiWaveTrain.TotalSeconds * i / 800f;
                float r = TsunamiWaveTrain.RiseAt(Distance, t, Amplitude);

                if (r > previous + 1e-4f) rising = true;
                else if (rising && r < previous - 1e-4f) { peaks++; rising = false; }

                previous = r;
            }

            Assert.InRange(peaks, 2, TsunamiWaveTrain.CrestCount);
        }

        [Fact]
        public void TheSecondCrestIsTheBiggest()
        {
            // 実際の津波でも第 1 波が最大とは限らない。
            Assert.True(TsunamiWaveTrain.CrestWeights[1] > TsunamiWaveTrain.CrestWeights[0],
                        "the first crest is the biggest; that is not how a tsunami behaves");
            Assert.True(TsunamiWaveTrain.CrestWeights[1] > TsunamiWaveTrain.CrestWeights[2]);
        }

        [Fact]
        public void TheSeaAlwaysGoesBackDown()
        {
            // ★★ **ここが破れると海が戻らない。**
            for (float d = 0f; d <= 6000f; d += 250f)
            {
                Assert.Equal(0f, TsunamiWaveTrain.RiseAt(d, TsunamiWaveTrain.TotalSeconds,
                                                         Amplitude), 4);
                Assert.Equal(0f, TsunamiWaveTrain.RiseAt(d, TsunamiWaveTrain.TotalSeconds + 60f,
                                                         Amplitude), 4);
            }

            Assert.Equal(0f, TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds), 4);
        }

        [Fact]
        public void TheEnvelopeFadesRatherThanStopping()
        {
            // 急に 0 になると水面が段差で落ちる。終わりに向かってなだらかに下がること。
            float a = TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.80f);
            float b = TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.90f);
            float c = TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.99f);

            Assert.True(a > b && b > c, a + " " + b + " " + c);
            Assert.True(c < 0.1f);
        }

        [Fact]
        public void TheRiseNeverExceedsTheAmplitudeAndTheWakeTogether()
        {
            // 山と wake が重なった高さより上には行かないこと。
            //
            // ★★ **上限は定数から出す。**（テストを 2 度直した）1.35 と直書きして
            //    いたので、土台を足した瞬間に嘘になった。上限は
            //    「いちばん高い山（1.0）＋ wake」そのものである。
            float ceiling = Amplitude * (1f + TsunamiWaveTrain.WakeFraction) * 1.02f;

            float worst = 0f;
            for (float t = 0f; t < TsunamiWaveTrain.TotalSeconds; t += 3f)
            {
                for (float d = 0f; d <= 10000f; d += 100f)
                {
                    float r = TsunamiWaveTrain.RiseAt(d, t, Amplitude);
                    if (r > worst) worst = r;
                }
            }

            Assert.True(worst <= ceiling,
                        "the crests stacked to " + worst + " m for a " + Amplitude
                        + " m wave (ceiling " + ceiling + ")");
            Assert.True(worst > Amplitude * 0.9f,
                        "the wave never even reached its own amplitude (" + worst + ")");
        }

        [Fact]
        public void NothingIsRaisedAheadOfTheAdvancingWall()
        {
            // ★★ **これが「同心円状の水の壁」の要である。**（2026-08-29、所有者の指摘
            //    「求めているのは、震源地から水の壁が同心円状に生成される挙動です」）
            //
            //    前線より外がわずかでも上がっていると、波が着く前から海が高く、
            //    <b>輪ではなく「一様に持ち上がった海」に見える</b>。
            //    1 つ前の版（台地）はまさにそれで、指摘のとおり別物だった。
            for (float t = 20f; t < TsunamiWaveTrain.TotalSeconds * 0.6f; t += 20f)
            {
                float front = TsunamiWaveTrain.LeadingFrontAt(t);

                // 前線より先（山の幅ぶんは余裕を見る）。
                float ahead = front + TsunamiWaveTrain.CrestWidthMetres + 200f;
                if (ahead >= TsunamiWaveTrain.ReachEdgeMetres) continue;

                Assert.Equal(0f, TsunamiWaveTrain.RiseAt(ahead, t, Amplitude), 4);
            }
        }

        [Fact]
        public void TheWallIsTallerThanTheWaterItLeavesBehind()
        {
            // 壁が背後の海より低かったら、それは壁ではない。
            for (float t = 60f; t < TsunamiWaveTrain.TotalSeconds * 0.5f; t += 30f)
            {
                float front = TsunamiWaveTrain.LeadingFrontAt(t);
                if (front < 1500f || front > TsunamiWaveTrain.ReachEdgeMetres - 1500f) continue;

                float atWall = TsunamiWaveTrain.RiseAt(front, t, Amplitude);

                // 背後の、どの山からも離れたところ。
                float behind = front - TsunamiWaveTrain.CrestWidthMetres * 1.6f;
                if (behind < 200f) continue;
                float atWake = TsunamiWaveTrain.RiseAt(behind, t, Amplitude);

                Assert.True(atWall > atWake,
                            "at t=" + t + " the wall (" + atWall
                            + " m) is not above its wake (" + atWake + " m)");
            }
        }

        [Fact]
        public void TheWakeFollowsTheWallRatherThanCoveringEverythingFromTheStart()
        {
            // 内側だけが上がっていること。**はじめから全部上がっていたら台地である。**
            float early = 30f;
            float front = TsunamiWaveTrain.LeadingFrontAt(early);

            Assert.True(TsunamiWaveTrain.WakeShapeAt(front * 0.3f, early) > 0f,
                        "the water behind the wall is not raised at all");
            Assert.Equal(0f, TsunamiWaveTrain.WakeShapeAt(front + 100f, early), 4);
            Assert.Equal(0f, TsunamiWaveTrain.WakeShapeAt(
                TsunamiWaveTrain.ReachEdgeMetres, early), 4);
        }

        [Fact]
        public void TheWallKeepsMovingOutward()
        {
            float a = TsunamiWaveTrain.LeadingFrontAt(30f);
            float b = TsunamiWaveTrain.LeadingFrontAt(90f);
            float c = TsunamiWaveTrain.LeadingFrontAt(150f);

            Assert.True(b > a && c > b, a + " " + b + " " + c);
        }

        /// <summary>その時刻に波が届いているいちばん外の距離（m）。</summary>
        private static float OuterEdgeAt(float seconds)
        {
            float edge = 0f;
            for (float d = 0f; d <= 20000f; d += 25f)
            {
                if (TsunamiWaveTrain.RiseAt(d, seconds, Amplitude) > 0.05f) edge = d;
            }
            return edge;
        }

    }
}
