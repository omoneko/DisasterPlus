using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 密集度による火災旋風の発生判定。
    /// 「半径 R 以内に N 棟以上が同時に延焼中」を満たす地点を探す。
    /// 面積だけを見ると郊外の点在火災を足し算してしまうので、密度で判定する。
    /// </summary>
    public static class FireWhirlDetector
    {
        /// <summary>
        /// 判定だけを行う短い形。**診断が要らない呼び出し（テスト）のためだけに在る。**
        /// 本番の呼び出しは <see cref="FireWhirlProspect"/> を受け取る側を使うこと ——
        /// ③は自然発生しか経路を持たないので、「出なかった理由」を捨ててよい経路が
        /// 実質存在しない。
        /// </summary>
        public static List<FireWhirlCandidate> Detect(
            IList<BurningBuilding> burning,
            FireWhirlConfig config,
            IList<Vec2> existingWhirls)
        {
            FireWhirlProspect ignored;
            return Detect(burning, config, existingWhirls, out ignored);
        }

        /// <summary>
        /// 判定と、**同じ 1 パスで**「なぜ出なかったか」を返す
        /// （<see cref="FireWhirlProspect"/> のクラス doc）。
        /// </summary>
        public static List<FireWhirlCandidate> Detect(
            IList<BurningBuilding> burning,
            FireWhirlConfig config,
            IList<Vec2> existingWhirls,
            out FireWhirlProspect prospect)
        {
            var result = new List<FireWhirlCandidate>();

            // いちばん密な塊。**閾値に届かない塊もここでだけは数える** ——
            // 届かないことこそが「出ない理由」なので、判定で捨てる前に控える。
            int densest = 0;
            var densestCentre = new Vec2(0f, 0f);
            int suppressed = 0;

            if (burning == null || burning.Count == 0)
            {
                prospect = new FireWhirlProspect(0, 0, densestCentre,
                    config.DetectRadius, config.DetectCount, 0, 0);
                return result;
            }

            int burningTotal = burning.Count;

            // セルは半径と同じ大きさにする。近傍探索が 3x3 セルで済む。
            var grid = new GridVote(config.DetectRadius);
            for (int i = 0; i < burning.Count; i++) grid.Add(i, burning[i].Position);

            float r2 = config.DetectRadius * config.DetectRadius;
            var near = new List<int>();
            var raw = new List<FireWhirlCandidate>();

            for (int i = 0; i < burning.Count; i++)
            {
                grid.CollectNear(burning[i].Position, config.DetectRadius, near);

                int count = 0;
                float sx = 0f, sz = 0f;
                for (int k = 0; k < near.Count; k++)
                {
                    var other = burning[near[k]];
                    if (burning[i].Position.DistanceSquaredTo(other.Position) > r2) continue;
                    count++;
                    sx += other.Position.X;
                    sz += other.Position.Z;
                }

                var centre = new Vec2(sx / count, sz / count);

                // ★ 判定より先に控える。ここを if の後ろに置くと、閾値に届かない
                //   ——つまり診断がいちばん要る——場合にだけ数え損なう。
                if (count > densest)
                {
                    densest = count;
                    densestCentre = centre;
                }

                if (count < config.DetectCount) continue;
                raw.Add(new FireWhirlCandidate(centre, count));
            }

            if (raw.Count == 0)
            {
                prospect = new FireWhirlProspect(burningTotal, densest, densestCentre,
                    config.DetectRadius, config.DetectCount, 0, 0);
                return result;
            }

            // 燃焼棟数の多い順に確定させ、近すぎる候補を捨てる。
            // 入力順に依存しないよう、同数のときはインデックスで決着させる（決定論のため）。
            var order = new int[raw.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            SortByCountDescending(order, raw);

            float sep2 = config.MinSeparation * config.MinSeparation;

            for (int oi = 0; oi < order.Length; oi++)
            {
                var cand = raw[order[oi]];

                bool blocked = false;

                if (existingWhirls != null)
                {
                    for (int e = 0; e < existingWhirls.Count; e++)
                    {
                        if (cand.Center.DistanceSquaredTo(existingWhirls[e]) < sep2) { blocked = true; break; }
                    }
                }

                // ★ 生存中／クールダウン中の旋風に弾かれた数だけを数える。
                //   同じパスで採用済みの候補に弾かれたぶんは数えない ——
                //   そのときは result が空でないので「出なかった理由」ではない。
                if (blocked) suppressed++;

                if (!blocked)
                {
                    for (int a = 0; a < result.Count; a++)
                    {
                        if (cand.Center.DistanceSquaredTo(result[a].Center) < sep2) { blocked = true; break; }
                    }
                }

                if (!blocked) result.Add(cand);
            }

            prospect = new FireWhirlProspect(burningTotal, densest, densestCentre,
                config.DetectRadius, config.DetectCount, suppressed, result.Count);
            return result;
        }

        /// <summary>
        /// 挿入ソート。件数は同時延焼中の建物数どまりなので O(n^2) で足りる。
        /// List.Sort は比較が等しいとき順序を保証しないため、決定論のために自前で書く。
        /// </summary>
        private static void SortByCountDescending(int[] order, List<FireWhirlCandidate> raw)
        {
            for (int i = 1; i < order.Length; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && IsBefore(key, order[j], raw))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
        }

        private static bool IsBefore(int a, int b, List<FireWhirlCandidate> raw)
        {
            if (raw[a].BurningCount != raw[b].BurningCount)
                return raw[a].BurningCount > raw[b].BurningCount;
            return a < b;   // 同数ならインデックス順。入力が同じなら結果も同じになる。
        }
    }
}
