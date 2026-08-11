using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 旋風の周囲に火の粉を撒く確率モデル。
    /// 現実の火災旋風は火の粉で延焼を加速させるので、これが現象の核心になる。
    ///
    /// 意図的に DisasterHelpers を経由しない。競合MOD が DisasterHelpers.DestroyBuildings を
    /// 差し替えていても、この層だけは必ず動く（設計書 3.3(b)）。
    /// </summary>
    public static class IgnitionSpread
    {
        /// <summary>SpreadStrength の最大値。ModSettings のスライダー上限と一致させること。</summary>
        public const int MaxStrength = 10;

        /// <summary>強度 10・距離ゼロのときの 1 tick あたり発火確率。</summary>
        private const float BaseProbabilityAtMaxStrength = 0.02f;

        public static void Select(
            Vec2 center,
            float radius,
            int spreadStrength,
            IList<IgnitionCandidate> nearby,
            uint tick,
            List<ushort> into)
        {
            into.Clear();
            if (spreadStrength <= 0 || nearby == null || nearby.Count == 0) return;
            if (radius <= 0f) return;

            int strength = spreadStrength > MaxStrength ? MaxStrength : spreadStrength;
            float strengthScale = strength / (float)MaxStrength;
            float r2 = radius * radius;

            for (int i = 0; i < nearby.Count; i++)
            {
                var c = nearby[i];
                if (c.AlreadyBurning) continue;

                float d2 = center.DistanceSquaredTo(c.Position);
                if (d2 > r2) continue;

                // 距離減衰。中心で 1、外周で 0 になる線形フォールオフ。
                float falloff = 1f - (float)System.Math.Sqrt(d2 / r2);
                float p = BaseProbabilityAtMaxStrength * strengthScale * falloff;
                if (p <= 0f) continue;

                // System.Random は使わない。(tick, buildingId) から決めるので、
                // セーブ・ロードしても、テストを何度回しても同じ結果になる。
                if (DeterministicRandom.Unit(tick, c.Id) < p) into.Add(c.Id);
            }
        }
    }
}
