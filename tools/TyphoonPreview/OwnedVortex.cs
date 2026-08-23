using System;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Tools.TyphoonPreview
{
    /// <summary>
    /// **自前の白い雲の粒で組んだ渦**（2026-08-22 の作り直し）を描く。
    ///
    /// 所有者の指摘「まだ煙のようなものが見えるんですが、MissileDisaster のキノコ雲の
    /// エフェクトに使っている白い雲を上空の方で渦上に表示させられますか？」への答えを、
    /// **実機に出す前に**確かめるためのものである。
    ///
    /// ── ここが <see cref="Vortex"/>（旧）と違うところ ─────────────────────
    ///
    /// | | 旧（借り物を撒く） | 新（自前を置く） |
    /// |---|---|---|
    /// | 粒の数 | 段ごとに数千（バニラが湧かせる） | **88 個ちょうど**（<c>PuffCount</c>） |
    /// | 位置 | 円盤の中へ散らして漂わせる | **表のとおりに置く**（漂わない） |
    /// | 絵 | <c>steam</c>（平均アルファ 0.43） | **芯が不透明**（0.42 まで 1.0） |
    ///
    /// 粒の絵は <c>Game/Common/CloudParticleAssets.BuildTexture</c> と<b>同じ式</b>で
    /// 評価する（芯 <see cref="CoreEnd"/> まで 1、<see cref="EdgeEnd"/> で 0、
    /// 縁を 3 回ぶん揺らす）。**近似ではない。**
    /// </summary>
    internal static class OwnedVortex
    {
        // Game/Common/CloudParticleAssets と同じ値（あちらは Game なのでここから参照できない）。
        private const float CoreEnd = 0.42f;
        private const float EdgeEnd = 0.95f;
        private const float RimWobble = 0.10f;

        // Game/Typhoon/TyphoonVortexPuffFx と同じ値。
        private const float PuffSizeGain = 1.0f;
        private const float MaxAlpha = 0.95f;
        private const float MinAlpha = 0.42f;
        private const float ThicknessMetres = 2200f;

        /// <summary>置いた粒 1 個。</summary>
        internal struct Puff
        {
            public float X;
            public float Y;
            public float Z;
            public float Radius;
            public float Alpha;
            public float Shade;   // 0 = 日向の白、1 = 底面の灰
        }

        /// <summary>
        /// 渦ぜんぶ。<paramref name="radius"/> は強風域半径（m）。
        ///
        /// ★ 群れは <see cref="VortexPuffCrowd"/>（Core の実物）が組む。
        ///   ゲーム側（<c>TyphoonVortexPuffFx</c>）とまったく同じ表である。
        /// </summary>
        internal static Puff[] Build(float radius, float spinDegrees)
        {
            var crowd = new CrowdPuff[VortexPuffCrowd.TotalCount];
            int n = VortexPuffCrowd.Build(crowd);

            var puffs = new Puff[n];
            float spin = spinDegrees * 0.0174532925f;

            for (int i = 0; i < n; i++)
            {
                CrowdPuff p = crowd[i];

                float a = p.AngleRadians + spin;
                float r = p.RadiusFraction * radius;

                puffs[i].X = (float)Math.Cos(a) * r;
                puffs[i].Y = p.HeightFraction * ThicknessMetres;
                puffs[i].Z = (float)Math.Sin(a) * r;

                puffs[i].Radius = p.SizeFraction * radius * PuffSizeGain;

                puffs[i].Alpha = MinAlpha + (MaxAlpha - MinAlpha) * Clamp01(p.DensityFraction);
                puffs[i].Shade = 1f - Clamp01(p.HeightFraction);
            }

            return puffs;
        }

        /// <summary>
        /// テクスチャの不透明度。中心からの比 <paramref name="d"/>（0-1 以上）と
        /// 角度から、<c>CloudParticleAssets.BuildTexture</c> と同じ式で出す。
        /// </summary>
        internal static float TextureAlpha(float d, float angle)
        {
            float wobble = 1f + RimWobble * (float)Math.Sin(3.0 * angle);
            if (wobble < 0.0001f) wobble = 0.0001f;

            float t = (d / wobble - CoreEnd) / (EdgeEnd - CoreEnd);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>雲の本体（中心付近）で測った不透明度。**0.99 前後であること。**</summary>
        internal static float CoreOpacity()
        {
            return TextureAlpha(0.2f, 0f);
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
