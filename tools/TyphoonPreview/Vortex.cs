using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Tools.TyphoonPreview
{
    /// <summary>1 粒子ぶん（描画用）。</summary>
    internal struct Speck
    {
        public float X;
        public float Y;
        public float Z;
        public float SizeMetres;
        public float R;
        public float G;
        public float B;
        public float Alpha;
    }

    /// <summary>
    /// <see cref="VortexPuffLayout"/> と <see cref="VortexCloudProfile"/> が決めた渦を、
    /// **バニラの <c>ParticleEffect.EmitParticles</c> の IL 実測（エフェクト実測文書 §B-4）を
    /// そのまま写して**粒子の雲に展開する。ゲームは起動しない。
    ///
    /// <code>
    /// 位置 = 段の中心 + 円盤(半径 disc) + 上 × [0, band)
    /// 速度 = 段の velocity + (上·cos a + 横·sin a) × speed        a = 放出角
    /// 年齢 = [0, 寿命) の一様分布（＝定常状態の分布）
    /// 数   ∝ max(100, π disc²) × magnitude × rateOverTime × 寿命
    /// </code>
    ///
    /// ★ 実機では <c>maxParticles</c> の自動絞り込み（<c>pps ×= 1 − fill²</c>）が効いて
    ///   **必ず頭打ちになる**ので、ここでも層ごとの粒子総数を
    ///   <c>VortexCloudProfile.MaxParticles</c> に固定し、段ごとの配分だけを
    ///   <c>magnitude × disc²</c>（＝ 実機の 1 秒あたりの湧き数）× 寿命の比で決める。
    ///   これが実機の定常状態である。
    ///
    /// ★ 乱数は <see cref="DeterministicRandom"/> だけ（Core と同じ規律）。
    ///   同じ絵が何度でも出る。
    /// </summary>
    internal static class Vortex
    {
        /// <summary>実機の <c>TyphoonCloudFx.VortexRadiusMetres</c> と同じ規則。</summary>
        internal const float VortexRadiusFactor = 1.35f;

        internal const float MaxVortexRadiusMetres = 6000f;

        internal const float MinVortexRadiusMetres = 900f;

        /// <summary>暴風域半径（m）から渦の外周半径（m）を出す。</summary>
        internal static float RadiusOf(float stormRadiusMetres)
        {
            float r = stormRadiusMetres * VortexRadiusFactor;
            if (r > MaxVortexRadiusMetres) r = MaxVortexRadiusMetres;
            if (r < MinVortexRadiusMetres) r = MinVortexRadiusMetres;
            return r;
        }

        /// <summary>この重力（m/s²）に <c>GravityModifier</c> が掛かる（Unity と同じ）。</summary>
        private const float Gravity = 9.81f;

        /// <summary>実機の <c>TyphoonCloudFx</c> と同じ値。**ずらさないこと。**</summary>
        internal const float ThicknessMetres = 2200f;

        internal const float SwirlMetresPerSecond = 34f;

        internal const float RadialMetresPerSecond = 20f;

        internal const float RiseMetresPerSecond = 16f;

        internal const float RateOverTime = 20f;

        internal const float ParticlesPerSecond = 700f;

        internal const float MinSizeMetres = 40f;

        internal const float MaxSizeMetres = 900f;

        /// <summary>
        /// 渦ぜんぶを粒子へ展開する。<paramref name="radius"/> は強風域半径（m）。
        /// <paramref name="spinDegrees"/> は渦の回転角（実機の <c>TyphoonCloud</c> と同じ）。
        /// </summary>
        internal static Speck[] Build(float radius, float spinDegrees, uint seed)
        {
            var list = new System.Collections.Generic.List<Speck>();

            for (int layerIndex = 0; layerIndex < 3; layerIndex++)
            {
                var layer = (VortexCloudLayer)layerIndex;
                VortexCloudProfile profile = VortexCloudProfile.Of(layer);

                // 段ごとの配分（1 秒あたりの湧き数 × 寿命 ＝ 定常状態の在庫の比）。
                var share = new float[VortexPuffLayout.PuffCount];
                float total = 0f;
                for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
                {
                    VortexPuff puff = VortexPuffLayout.PuffAt(i);
                    if (puff.Layer != layer) continue;

                    float disc = puff.DiscFraction * radius;
                    float magnitude = VortexPuffLayout.MagnitudeFor(
                        disc, RateOverTime, ParticlesPerSecond, VortexPuffLayout.PuffCount);
                    float area = 3.14159265f * disc * disc;
                    if (area < 100f) area = 100f;

                    share[i] = area * magnitude * 0.01f * RateOverTime * puff.DensityFraction;
                    total += share[i];
                }
                if (!(total > 0f)) continue;

                float size = radius * profile.SizeFraction;
                if (size < MinSizeMetres) size = MinSizeMetres;
                if (size > MaxSizeMetres) size = MaxSizeMetres;

                float spin = spinDegrees * 0.0174532925f;
                uint draw = 0u;

                for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
                {
                    if (!(share[i] > 0f)) continue;
                    VortexPuff puff = VortexPuffLayout.PuffAt(i);

                    int count = (int)(profile.MaxParticles * (share[i] / total));
                    if (count <= 0) continue;

                    float a = puff.AngleRadians + spin;
                    float cos = (float)Math.Cos(a);
                    float sin = (float)Math.Sin(a);
                    float r = puff.RadiusFraction * radius;

                    float centreX = cos * r;
                    float centreY = puff.HeightFraction * ThicknessMetres;
                    float centreZ = sin * r;

                    float disc = puff.DiscFraction * radius;
                    float band = puff.BandFraction * ThicknessMetres;

                    float swirl = SwirlMetresPerSecond * puff.SwirlFraction;
                    float radial = RadialMetresPerSecond * puff.RadialFraction;
                    float driftX = -sin * swirl + cos * radial;
                    float driftY = RiseMetresPerSecond * puff.RiseFraction;
                    float driftZ = cos * swirl + sin * radial;

                    for (int n = 0; n < count; n++)
                    {
                        draw++;
                        float u1 = DeterministicRandom.Unit(seed, draw * 9u + 1u);
                        float u2 = DeterministicRandom.Unit(seed, draw * 9u + 2u);
                        float u3 = DeterministicRandom.Unit(seed, draw * 9u + 3u);
                        float u4 = DeterministicRandom.Unit(seed, draw * 9u + 4u);
                        float u5 = DeterministicRandom.Unit(seed, draw * 9u + 5u);
                        float u6 = DeterministicRandom.Unit(seed, draw * 9u + 6u);
                        float u7 = DeterministicRandom.Unit(seed, draw * 9u + 7u);
                        float u8 = DeterministicRandom.Unit(seed, draw * 9u + 8u);

                        // 湧く場所: 円盤 × [0, band)（IL 実測の EmitParticles と同じ）。
                        double theta = 2.0 * Math.PI * u1;
                        float rr = (float)Math.Sqrt(u2) * disc;
                        float px = centreX + (float)Math.Cos(theta) * rr;
                        float py = centreY + u3 * band;
                        float pz = centreZ + (float)Math.Sin(theta) * rr;

                        // 初速: 軸（上）を放出角ぶん傾けた向き。
                        float ang = (profile.SpawnAngleMinDegrees
                                     + (profile.SpawnAngleMaxDegrees
                                        - profile.SpawnAngleMinDegrees) * u4)
                                    * (float)(Math.PI / 180.0);
                        float speed = profile.SpeedMin
                                      + (profile.SpeedMax - profile.SpeedMin) * u5;
                        double side = 2.0 * Math.PI * u6;

                        float vy = (float)Math.Cos(ang) * speed + driftY;
                        float vx = (float)(Math.Sin(ang) * Math.Cos(side)) * speed + driftX;
                        float vz = (float)(Math.Sin(ang) * Math.Sin(side)) * speed + driftZ;

                        float life = profile.LifeMinSeconds
                                     + (profile.LifeMaxSeconds - profile.LifeMinSeconds) * u7;
                        float age = u8 * life;

                        px += vx * age;
                        py += vy * age - 0.5f * Gravity * profile.GravityModifier * age * age;
                        pz += vz * age;

                        // 色は 2 色の階調から 1 つ（ParticleSystem.MinMaxGradient と同じ）。
                        float k = DeterministicRandom.Unit(seed + 1u, draw);
                        var speck = new Speck();
                        speck.X = px;
                        speck.Y = py;
                        speck.Z = pz;
                        speck.SizeMetres = size;
                        speck.R = profile.DarkRed + (profile.BrightRed - profile.DarkRed) * k;
                        speck.G = profile.DarkGreen
                                  + (profile.BrightGreen - profile.DarkGreen) * k;
                        speck.B = profile.DarkBlue + (profile.BrightBlue - profile.DarkBlue) * k;
                        speck.Alpha = profile.Alpha;
                        list.Add(speck);
                    }
                }
            }

            return list.ToArray();
        }
    }
}
