using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 台風の渦を**雲の粒（パフ）の並び**として置くための純データ。
    /// <b>Core なのでエンジンには一切触らない</b>（座標は正規化した比で返し、
    /// ワールド座標へ直すのは <c>Game/Typhoon/TyphoonCloudFx</c> の仕事）。
    ///
    /// ── なぜメッシュ 1 枚ではなく粒の並びなのか ────────────────────────
    ///
    /// 持ち主の指摘は「現在の巨大な渦を雲から構成するように」である。
    /// 従来の <c>SpiralMesh</c> は**自前のメッシュ 1 枚に自前のマテリアルを貼る**形で、
    /// (1) <c>Shader.Find</c> が実機で <c>"Standard"</c> にすら null を返したため何も
    /// 描けず、(2) 描けたとしても④の全体レビューが「半径 900 m の平らな渦巻き＝
    /// 渦の記号であって空を覆う雲ではない」と結論していた。
    ///
    /// バニラの粒子エフェクト（<c>ParticleEffect</c>）は**既に動くマテリアルを持っている**
    /// うえ、そのマテリアルは <c>ParticleSystemRenderer</c> に付くので
    /// 「借りたマテリアルが自前 <c>MeshRenderer</c> で見えない」問題には当たらない
    /// （エフェクト実測文書 §D-3）。だから雲は**バニラの煙／蒸気の粒**で組む。
    ///
    /// ── 置き方 ────────────────────────────────────────────
    ///
    /// <see cref="ArmCount"/> 本の腕（対数ではなく等間隔の線形スパイラル）と、
    /// 眼のすぐ外を囲む <see cref="EyeWallPuffs"/> 個の環。
    /// **腕も環も <see cref="EyeFraction"/> より内側には 1 個も置かない** ——
    /// それが「眼を穴として読める」ことの実装である。テストがこれを固定している。
    ///
    /// ── 揺らぎ ────────────────────────────────────────────
    ///
    /// 完全な等間隔だと機械的に見えるので、<see cref="DeterministicRandom"/> で
    /// 角度と半径に小さな揺らぎを入れる。**添字だけの関数**なので毎フレーム同じ形になり、
    /// 粒がちらつかない（フレームを混ぜると渦が毎フレーム組み替わって沸騰して見える）。
    /// <c>System.Random</c> は使わない（本 MOD 全体の規律）。
    /// </summary>
    public static class VortexPuffLayout
    {
        /// <summary>腕の本数。</summary>
        public const int ArmCount = 3;

        /// <summary>1 本の腕に置く粒の数。</summary>
        public const int PuffsPerArm = 7;

        /// <summary>眼のすぐ外を囲む環の粒の数。</summary>
        public const int EyeWallPuffs = 9;

        /// <summary>1 フレームに出す <c>RenderEffect</c> の本数。**これが毎フレームの上限である。**</summary>
        public const int PuffCount = ArmCount * PuffsPerArm + EyeWallPuffs;

        /// <summary>眼の半径 ÷ 渦の外周半径。**ここより内側には 1 個も置かない。**</summary>
        public const float EyeFraction = 0.16f;

        /// <summary>環を置く半径 ÷ 渦の外周半径（眼のすぐ外）。</summary>
        public const float EyeWallFraction = EyeFraction * 1.25f;

        /// <summary>腕が外周まで伸びるあいだに回る回転数。</summary>
        public const float SpiralTurns = 0.8f;

        /// <summary>角度の揺らぎ（rad）の振幅。</summary>
        public const float AngleJitterRadians = 0.16f;

        /// <summary>半径の揺らぎ（外周半径に対する比）の振幅。</summary>
        public const float RadiusJitterFraction = 0.05f;

        /// <summary>揺らぎの種。**固定値**（添字だけの関数にするため）。</summary>
        private const uint JitterSeed = 0x54595048u;   // "TYPH"

        private const float TwoPi = 6.28318530718f;

        /// <summary>
        /// 粒 <paramref name="index"/> の置き場所。全部**正規化した比**である。
        ///
        /// <paramref name="angleRadians"/> は [0, 2π)、<paramref name="radiusFraction"/> は
        /// [<see cref="EyeFraction"/>, 1] より少しはみ出しうる（揺らぎのぶん）が、
        /// **<see cref="EyeFraction"/> を下回ることはない**（眼は穴のまま）。
        ///
        /// <paramref name="heightFraction"/> は雲の厚みのどこに置くか [0, 1]
        /// （1 が上）。壁雲を高く、外側を低くして、渦が皿ではなく漏斗に見えるようにする。
        ///
        /// <paramref name="sizeFraction"/> は粒の大きさの比 [0, 1]、
        /// <paramref name="densityFraction"/> は粒の濃さの比 [0, 1]。
        ///
        /// **範囲外の添字は例外を投げず、環の 1 個目に丸める。** 毎フレーム回る経路なので、
        /// 呼び出し側の数え違いでレベルロードを壊さない。
        /// </summary>
        public static void Puff(int index,
                                out float angleRadians, out float radiusFraction,
                                out float heightFraction, out float sizeFraction,
                                out float densityFraction)
        {
            if (index < 0 || index >= PuffCount) index = ArmCount * PuffsPerArm;

            float angle;
            float radius;

            if (index < ArmCount * PuffsPerArm)
            {
                int arm = index / PuffsPerArm;
                int step = index - arm * PuffsPerArm;

                // t は腕に沿った位置 (0, 1)。端に粒を寄せないよう半歩ずらす。
                float t = (step + 0.5f) / PuffsPerArm;

                angle = TwoPi * arm / ArmCount + TwoPi * SpiralTurns * t;
                radius = EyeFraction + (1f - EyeFraction) * t;

                // 壁雲側が高く、外へ行くほど低い（漏斗の口）。
                heightFraction = 1f - 0.55f * t;
                // 外へ行くほど粒は大きく、薄くなる。
                sizeFraction = 0.45f + 0.55f * t;
                densityFraction = 1f - 0.45f * t;
            }
            else
            {
                int k = index - ArmCount * PuffsPerArm;

                angle = TwoPi * k / EyeWallPuffs;
                radius = EyeWallFraction;

                // 壁雲は渦でいちばん高く・小さく・濃い。
                heightFraction = 1f;
                sizeFraction = 0.4f;
                densityFraction = 1f;
            }

            // 揺らぎ。**添字だけの関数**なので毎フレーム同じ形になる（クラス doc）。
            float ja = DeterministicRandom.Unit(JitterSeed, (uint)index) * 2f - 1f;
            float jr = DeterministicRandom.Unit(JitterSeed + 1u, (uint)index) * 2f - 1f;

            angle += ja * AngleJitterRadians;
            radius += jr * RadiusJitterFraction;

            // ★ 眼は穴のまま。揺らぎが内側へ食い込んでも戻す（テストが固定している）。
            if (radius < EyeFraction) radius = EyeFraction;

            angleRadians = Normalize(angle);
            radiusFraction = radius;
        }

        /// <summary>
        /// 1 粒ぶんの <c>magnitude</c>（＝粒子の密度）。
        ///
        /// エフェクト実測文書 §B-4 の粒子数の式
        /// <code>count = max(100, π·r²) × (timeDelta × magnitude × 0.01 × rateOverTime)</code>
        /// を **1 秒あたりの粒子数**から逆に解いたもの。<c>timeDelta</c> は両辺で
        /// 打ち消し合うので、返す値はフレームレートにもゲーム速度にも依らない。
        ///
        /// **これが毎フレームの仕事量の上限を決めている 1 本目**である
        /// （2 本目は <c>ParticleSystem.maxParticles</c> に対するバニラ自身の
        /// 絞り込み <c>pps × (1 - fill²)</c>）。
        ///
        /// 壊れた入力（0 以下・NaN）には 0 を返す ——「粒子数 NaN で空が埋まる」を作らない。
        /// </summary>
        /// <param name="discRadius">1 粒の円盤半径（m）。</param>
        /// <param name="rateOverTime">エフェクト側の <c>emission.rateOverTime.constant</c>。</param>
        /// <param name="particlesPerSecond">渦**全体**で 1 秒あたりに出したい粒子数。</param>
        /// <param name="puffCount">粒の数（<see cref="PuffCount"/>）。</param>
        public static float MagnitudeFor(float discRadius, float rateOverTime,
                                         float particlesPerSecond, int puffCount)
        {
            if (!(discRadius > 0f) || !(rateOverTime > 0f)) return 0f;
            if (!(particlesPerSecond > 0f) || puffCount <= 0) return 0f;

            float area = 3.14159265f * discRadius * discRadius;
            if (area < 100f) area = 100f;      // §B-4 の max(100, πr²)

            float perPuff = particlesPerSecond / puffCount;
            float magnitude = perPuff / (area * 0.01f * rateOverTime);

            if (float.IsNaN(magnitude) || magnitude <= 0f) return 0f;
            return magnitude;
        }

        private static float Normalize(float radians)
        {
            if (float.IsNaN(radians)) return 0f;

            float a = radians % TwoPi;
            if (a < 0f) a += TwoPi;
            return a;
        }
    }
}
