using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 渦の粒がどの層に属するか。<b>層ごとに別の複製（<c>ParticleSystem</c>）で描く。</b>
    ///
    /// 分けるのは好みではなく**制約**である —— <c>startColor</c> と <c>startSize</c> は
    /// <c>ParticleSystem</c> 側の共有状態で、<c>RenderEffect</c> の呼び出しごとには
    /// 変えられない（エフェクト実測文書 §B-4）。色と粒径を変えたい単位が
    /// そのまま複製の数になる。
    /// </summary>
    public enum VortexCloudLayer
    {
        /// <summary>雲底。**暗く・平たく・大きい。** 入道雲の下面である。</summary>
        Deck = 0,

        /// <summary>塔。**明るく・小さめ・もこもこ。** 入道雲の本体（cauliflower）。</summary>
        Tower = 1,

        /// <summary>かなとこ（天蓋）。**いちばん明るく・いちばん大きく・薄い。**</summary>
        Canopy = 2,
    }

    /// <summary>
    /// 渦の粒 1 つぶんの置き場所と流れ方。**全部「渦の外周半径に対する比」**で、
    /// ワールド座標へ直すのは <c>Game/Typhoon/TyphoonCloudFx</c> の仕事である
    /// （この型は Unity にもゲームにも触れない）。
    /// </summary>
    public struct VortexPuff
    {
        /// <summary>中心から見た方位（rad、[0, 2π)）。渦の回転はここに足される。</summary>
        public readonly float AngleRadians;

        /// <summary>中心からの距離 ÷ 渦の外周半径。</summary>
        public readonly float RadiusFraction;

        /// <summary>雲の厚みのどこに置くか [0, 1]（1 が上）。</summary>
        public readonly float HeightFraction;

        /// <summary>粒をばらまく円盤の半径 ÷ 渦の外周半径。</summary>
        public readonly float DiscFraction;

        /// <summary>
        /// 円盤を上へどれだけ引き伸ばすか ÷ 雲の厚み。
        /// <c>SpawnArea</c> の第 4 引数（<c>halfHeight</c>）は
        /// <b>[0, halfHeight] の一様乱数で上へだけ</b>散らす（IL 実測 §B-4）ので、
        /// <see cref="HeightFraction"/> を段の下端にすれば段がちょうど積み上がる。
        /// **雲底は薄く、塔は厚い** —— これが「下は平ら・上はもこもこ」の実体である。
        /// </summary>
        public readonly float BandFraction;

        /// <summary>粒の濃さの比 [0, 1]（<c>magnitude</c> に掛かる）。</summary>
        public readonly float DensityFraction;

        /// <summary>接線方向（渦の回る向き）の速さの比 [0, 1]。</summary>
        public readonly float SwirlFraction;

        /// <summary>半径方向の速さの比。**負が吸い込み、正が吹き出し。**</summary>
        public readonly float RadialFraction;

        /// <summary>鉛直方向の速さの比 [0, 1]（上向き）。</summary>
        public readonly float RiseFraction;

        /// <summary>どの複製で描くか。</summary>
        public readonly VortexCloudLayer Layer;

        public VortexPuff(float angleRadians, float radiusFraction, float heightFraction,
                          float discFraction, float bandFraction, float densityFraction,
                          float swirlFraction, float radialFraction, float riseFraction,
                          VortexCloudLayer layer)
        {
            AngleRadians = angleRadians;
            RadiusFraction = radiusFraction;
            HeightFraction = heightFraction;
            DiscFraction = discFraction;
            BandFraction = bandFraction;
            DensityFraction = densityFraction;
            SwirlFraction = swirlFraction;
            RadialFraction = radialFraction;
            RiseFraction = riseFraction;
            Layer = layer;
        }
    }

    /// <summary>
    /// 台風の渦を**入道雲（積乱雲）の並び**として置くための純データ。
    /// <b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── なぜ作り直したのか（2026-08-22、持ち主の指摘）────────────────────
    ///
    /// &gt; 雲のエフェクトが煙になっているので見た目がとても変です。
    /// &gt; 渦を巻く入道雲をイメージして作り直してください。
    ///
    /// **指摘は正しく、原因も 2 つに割れている。**
    ///
    /// 1. <b>素材</b>。旧実装は <c>Factory Smoke</c> を第 1 候補にしていた。
    ///    出荷アセットを直接読むと、その粒子マテリアル <c>Smoke</c> のテクスチャは
    ///    <b>平均 RGB (75, 78, 80) ＝ 暗い煤色の丸い塊</b>（火の粉の点まで入っている）で、
    ///    どう色を掛けても煙にしか見えない。素材の選び方は
    ///    <c>Game/Typhoon/TyphoonCloudFx</c> の doc に測った数字ごと書いた。
    /// 2. <b>形</b>。旧実装は「腕 3 本 ＋ 環」を**平らに 1 段**置いていた。
    ///    入道雲は<b>鉛直に伸びる塔</b>で、上は日に照らされて白く盛り上がり
    ///    （cauliflower）、下は平たく暗い。1 段では雲にならない。
    ///
    /// ── 積乱雲の構造（これがこの型の形そのものである）─────────────────────
    ///
    /// <code>
    ///        ~~~~~~~~~~~~   Canopy  かなとこ … いちばん明るい。広く薄く、外へ吹き出す
    ///         (  )(  )(  )  Tower   塔     … 明るい。もこもこ盛り上がる（2 段）
    ///        ____________   Deck    雲底   … 暗く平ら。低く、内へ吸い込まれる
    /// </code>
    ///
    /// 台風はこの塔が<b>眼を囲む環（眼の壁雲）</b>として並び、そこから
    /// <b>渦巻きの腕（スパイラルバンド）</b>として外へ流れる。中心へ行くほど背が高い。
    ///
    /// ── 置き方 ────────────────────────────────────────
    ///
    /// <see cref="ColumnCount"/> 本の**柱**（眼の壁雲 <see cref="EyeWallColumns"/> 本 ＋
    /// 腕 <see cref="ArmCount"/> 本 × <see cref="ColumnsPerArm"/> 本）を置き、
    /// 柱 1 本につき <see cref="LevelsPerColumn"/> 個の粒を高さ違いで積む。
    /// 合計は <see cref="PuffCount"/> 個で、**これが毎フレームの
    /// <c>RenderEffect</c> の本数である**（旧実装の 30 本から増えている）。
    ///
    /// ── ★ 眼が穴として読めるための条件（この型が自分で守る）─────────────────
    ///
    /// **旧実装はこの約束を Game 側に預けていて、実際に 1 度埋めた。**
    /// 粒は点ではなく、円盤の半径ぶんばらまかれ、粒径の半分だけ外へ広がる。
    /// いまは<b>円盤の半径（<see cref="VortexPuff.DiscFraction"/>）も粒径
    /// （<see cref="SizeFractionOf"/>）もこの型が宣言している</b>ので、
    /// 「粒がどこまで内側へ届くか」をこの型が計算できる:
    ///
    /// <code>
    /// EyeClearanceOf(index) = RadiusFraction - DiscFraction - SizeFraction/2 ≥ EyeFraction
    /// </code>
    ///
    /// **テストがこれを全粒について固定している。** 数字を動かしても、眼が埋まれば
    /// ビルドが赤くなる ——「テストでも捕まらない」という旧 doc の但し書きは消えた。
    ///
    /// Game 側に残る唯一の逃げ道は粒径の下限クランプ（<c>MinSizeMetres</c>）で、
    /// これは**いちばん小さい台風でしか効かない**（強度 1 でも強風域半径は 2200 m 以上
    /// あり、塔の粒径は 110 m を超える）。あちらの doc に書いてある。
    ///
    /// なお、ここでいう「眼」は<b>雲の穴</b>であって <c>TyphoonProfile.EyeFraction</c> の
    /// <b>風の眼ではない</b>（あちらのほうが小さい）。粒径に対して読める大きさに取ってある。
    ///
    /// ── 揺らぎ ────────────────────────────────────────
    ///
    /// 完全な等間隔だと機械的に見えるので、<see cref="DeterministicRandom"/> で
    /// 角度・半径・高さに小さな揺らぎを入れる。**添字だけの関数**なので毎フレーム
    /// 同じ形になり、粒がちらつかない（フレームを混ぜると渦が毎フレーム組み替わって
    /// 沸騰して見える）。<c>System.Random</c> は使わない（本 MOD 全体の規律）。
    /// 揺らぎは同じ柱の中でも段ごとに違う添字を引くので、**塔はまっすぐ立たず
    /// 段ごとにずれて盛り上がる**＝これが cauliflower の実体である。
    /// </summary>
    public static class VortexPuffLayout
    {
        /// <summary>腕（スパイラルバンド）の本数。</summary>
        public const int ArmCount = 3;

        /// <summary>1 本の腕に置く柱の数。</summary>
        public const int ColumnsPerArm = 5;

        /// <summary>眼を囲む環（眼の壁雲）に置く柱の数。</summary>
        public const int EyeWallColumns = 7;

        /// <summary>柱の総数。</summary>
        public const int ColumnCount = ArmCount * ColumnsPerArm + EyeWallColumns;

        /// <summary>柱 1 本に積む粒の数（雲底・塔下・塔上・かなとこ）。</summary>
        public const int LevelsPerColumn = 4;

        /// <summary>1 フレームに出す <c>RenderEffect</c> の本数。**これが毎フレームの上限である。**</summary>
        public const int PuffCount = ColumnCount * LevelsPerColumn;

        /// <summary>雲の穴（眼）の半径 ÷ 渦の外周半径。**粒はここまで届いてはいけない。**</summary>
        public const float EyeFraction = 0.16f;

        /// <summary>眼の壁雲の環を置く半径 ÷ 渦の外周半径（＝いちばん内側の柱）。</summary>
        public const float EyeWallFraction = 0.26f;

        /// <summary>
        /// 腕が外周まで伸びるあいだに回る回転数。
        ///
        /// ★ **作図して 0.85 から下げた。** 0.85（＝306 度）だと、腕の隣り合う柱の
        ///   あいだの弧が半径 0.5R のところで 3 km を超え、腕が**ちぎれた雲の列**に
        ///   見えた（<c>tools/TyphoonPreview</c> の plan 画像）。
        /// </summary>
        public const float SpiralTurns = 0.45f;

        // ── 段ごとの数値表 ───────────────────────────────────
        //
        // ★ 配列の添字は段（0=雲底, 1=塔下, 2=塔上, 3=かなとこ）である。
        //   readonly な float[] にしてあるのは Core の他の表（EruptionColumn.Weights）と
        //   同じ形で、**要素は書き換えない**（static だが Unity オブジェクトではないので
        //   fake-null の問題は無い）。

        /// <summary>段ごとの層。</summary>
        private static readonly VortexCloudLayer[] Layers =
        {
            VortexCloudLayer.Deck,
            VortexCloudLayer.Tower,
            VortexCloudLayer.Tower,
            VortexCloudLayer.Canopy,
        };

        /// <summary>段ごとの円盤半径 ÷ 外周半径。雲底とかなとこは広く、塔は締まっている。</summary>
        private static readonly float[] LevelDisc = { 0.115f, 0.062f, 0.052f, 0.095f };

        /// <summary>段ごとに柱の半径へ足す量 ÷ 外周半径。
        /// 雲底は外へ広がり、塔は高いほど外へ倒れ（風のシアー）、かなとこは張り出す。</summary>
        private static readonly float[] LevelOutward = { 0.060f, 0.000f, 0.015f, 0.050f };

        /// <summary>段ごとの上への伸び ÷ 雲の厚み。**雲底は薄く平ら、塔は厚い。**</summary>
        private static readonly float[] LevelBand = { 0.06f, 0.34f, 0.34f, 0.14f };

        /// <summary>段ごとの高さの下駄（雲の厚みに対する比）。</summary>
        private static readonly float[] LevelHeightBase = { 0.02f, 0.12f, 0.20f, 0.34f };

        /// <summary>段ごとの高さのうち「柱の背の高さ」に比例する分。</summary>
        private static readonly float[] LevelHeightSpan = { 0.00f, 0.00f, 0.24f, 0.46f };

        /// <summary>段ごとの濃さ。**下が濃く、上ほど薄い**（積乱雲の見え方そのもの）。</summary>
        private static readonly float[] LevelDensity = { 1.00f, 0.85f, 0.65f, 0.45f };

        /// <summary>段ごとの接線方向の速さ。**下層がいちばん速い**（地表付近の暴風）。</summary>
        private static readonly float[] LevelSwirl = { 1.00f, 0.85f, 0.65f, 0.45f };

        /// <summary>
        /// 段ごとの半径方向の速さ。**負が吸い込み、正が吹き出し。**
        /// 下層で吸い込み・上層で吹き出すのが台風の二次循環である。
        /// </summary>
        private static readonly float[] LevelRadial = { -0.35f, -0.10f, 0.05f, 0.45f };

        /// <summary>段ごとの上昇成分。塔の中がいちばん強い。</summary>
        private static readonly float[] LevelRise = { 0.05f, 0.55f, 0.75f, 0.15f };

        /// <summary>層ごとの粒径 ÷ 渦の外周半径。**複製の <c>startSize</c> の比**である。</summary>
        public const float DeckSizeFraction = 0.055f;

        /// <summary>同上（塔）。塔は粒を小さくして「もこもこ」を出す。</summary>
        public const float TowerSizeFraction = 0.040f;

        /// <summary>同上（かなとこ）。いちばん大きく、薄く広げる。</summary>
        public const float CanopySizeFraction = 0.070f;

        /// <summary>眼の壁雲の柱の背の高さ [0, 1]。**いちばん高い。**</summary>
        public const float EyeWallTop = 1f;

        /// <summary>腕の柱が外へ行くにつれて低くなる割合。</summary>
        public const float ArmTopFalloff = 0.5f;

        /// <summary>
        /// 腕の柱が外へ行くにつれて薄くなる割合。
        ///
        /// ★ **作図して 0.45 から下げた。** <see cref="MagnitudeFor"/> は円盤の面積で
        ///   正規化するので、1 粒あたりの粒子数は円盤の大きさに依らない ——
        ///   つまり <see cref="ArmDiscGrowth"/> で円盤を広げた時点で、外側の
        ///   単位面積あたりの濃さは既に下がっている。そこへ更に 0.45 を掛けると
        ///   腕の外側が**点々**になった。実物のスパイラルバンドも外側ほど薄いので
        ///   向きは正しく、効かせ過ぎていただけである。
        /// </summary>
        public const float ArmDensityFalloff = 0.15f;

        /// <summary>いちばん低い柱でも段の厚みがこれだけは残る（背の高さ 0 のときの比）。</summary>
        public const float BandFloor = 0.45f;

        /// <summary>
        /// 腕のいちばん外の柱で円盤が何倍になるか − 1。
        ///
        /// ★ **これも作図して足した。** 腕は外へ行くほど柱の間隔が開く（弧が伸びる）ので、
        ///   円盤の大きさを一定にすると外側だけ雲が途切れる。実物のスパイラルバンドも
        ///   外側ほど広く薄いので、**広げて薄くする**のが正しい向きである
        ///   （薄くするのは <see cref="ArmDensityFalloff"/> が既にやっている）。
        /// </summary>
        public const float ArmDiscGrowth = 1.4f;

        /// <summary>角度の揺らぎ（rad）の振幅。</summary>
        public const float AngleJitterRadians = 0.14f;

        /// <summary>半径の揺らぎ（外周半径に対する比）の振幅。</summary>
        public const float RadiusJitterFraction = 0.035f;

        /// <summary>高さの揺らぎ（厚みに対する比）の振幅。**塔を段ごとにずらす。**</summary>
        public const float HeightJitterFraction = 0.025f;

        /// <summary>揺らぎの種。**固定値**（添字だけの関数にするため）。</summary>
        private const uint JitterSeed = 0x54595048u;   // "TYPH"

        private const float TwoPi = 6.28318530718f;

        /// <summary>
        /// 層ごとの粒径 ÷ 渦の外周半径。<c>Game</c> 側が複製の <c>startSize</c> を
        /// この比で決め、**この型は眼の余白の計算に同じ値を使う**（両者がずれないこと）。
        /// </summary>
        public static float SizeFractionOf(VortexCloudLayer layer)
        {
            if (layer == VortexCloudLayer.Deck) return DeckSizeFraction;
            if (layer == VortexCloudLayer.Canopy) return CanopySizeFraction;
            return TowerSizeFraction;
        }

        /// <summary>
        /// 粒 <paramref name="index"/> の置き場所と流れ方。全部**正規化した比**である。
        ///
        /// **範囲外の添字は例外を投げず、眼の壁雲の 1 個目に丸める。** 毎フレーム回る
        /// 経路なので、呼び出し側の数え違いでレベルロードを壊さない。
        /// </summary>
        public static VortexPuff PuffAt(int index)
        {
            if (index < 0 || index >= PuffCount) index = 0;

            int column = index / LevelsPerColumn;
            int level = index - column * LevelsPerColumn;

            float columnAngle;
            float columnRadius;
            float columnTop;
            float columnDensity;
            float columnDisc;
            Column(column, out columnAngle, out columnRadius, out columnTop, out columnDensity,
                   out columnDisc);

            VortexCloudLayer layer = Layers[level];

            // 揺らぎ。**添字だけの関数**なので毎フレーム同じ形になる（クラス doc）。
            // 段ごとに添字が違う ＝ 塔はまっすぐ立たずに段ごとにずれて盛り上がる。
            float ja = DeterministicRandom.Unit(JitterSeed, (uint)index) * 2f - 1f;
            float jr = DeterministicRandom.Unit(JitterSeed + 1u, (uint)index) * 2f - 1f;
            float jh = DeterministicRandom.Unit(JitterSeed + 2u, (uint)index) * 2f - 1f;

            float angle = columnAngle + ja * AngleJitterRadians;
            float radius = columnRadius + LevelOutward[level] + jr * RadiusJitterFraction;

            // 腕は外へ行くほど柱の間隔が開くので、円盤も広げる（<see cref="ArmDiscGrowth"/>）。
            float disc = LevelDisc[level] * columnDisc;

            // ★★ 眼は穴のまま。**この型が自分で守る**（クラス doc）。
            //    戻す先は「その段の粒がどこまで広がるか」を引いた最小半径である。
            float minimum = EyeFraction + disc + SizeFractionOf(layer) * 0.5f;
            if (radius < minimum) radius = minimum;

            float height = LevelHeightBase[level] + LevelHeightSpan[level] * columnTop
                           + jh * HeightJitterFraction;
            if (height < 0f) height = 0f;
            if (height > 1f) height = 1f;

            // ★ 段の厚みも柱の背の高さに従う。従わせないと、外側の低い柱まで
            //   眼の壁雲と同じ高さの塔になり、「中心ほど高い」が消える。
            float band = LevelBand[level] * (BandFloor + (1f - BandFloor) * columnTop);

            return new VortexPuff(Normalize(angle), radius, height,
                                  disc, band,
                                  LevelDensity[level] * columnDensity,
                                  LevelSwirl[level], LevelRadial[level], LevelRise[level],
                                  layer);
        }

        /// <summary>
        /// 粒 <paramref name="index"/> の内側の縁が眼の縁からどれだけ離れているか
        /// （外周半径に対する比）。**0 以上でなければ眼が埋まる。**
        /// テストが全粒についてこれを固定している。
        /// </summary>
        public static float EyeClearanceOf(int index)
        {
            VortexPuff puff = PuffAt(index);
            float reach = puff.DiscFraction + SizeFractionOf(puff.Layer) * 0.5f;
            return puff.RadiusFraction - reach - EyeFraction;
        }

        /// <summary>
        /// 柱 <paramref name="column"/> の素の置き場所（段の下駄も揺らぎも入っていない）。
        /// 前半が眼の壁雲の環、後半が腕である。
        /// </summary>
        private static void Column(int column, out float angleRadians, out float radiusFraction,
                                   out float topFraction, out float densityFraction,
                                   out float discScale)
        {
            if (column < EyeWallColumns)
            {
                // 眼の壁雲: 眼のすぐ外を等間隔に囲む環。いちばん高く、いちばん濃い。
                angleRadians = TwoPi * column / EyeWallColumns;
                radiusFraction = EyeWallFraction;
                topFraction = EyeWallTop;
                densityFraction = 1f;
                discScale = 1f;
                return;
            }

            int k = column - EyeWallColumns;
            int arm = k / ColumnsPerArm;
            int step = k - arm * ColumnsPerArm;

            // t は腕に沿った位置 (0, 1)。端に柱を寄せないよう半歩ずらす。
            float t = (step + 0.5f) / ColumnsPerArm;

            angleRadians = TwoPi * arm / ArmCount + TwoPi * SpiralTurns * t;
            radiusFraction = EyeWallFraction + (1f - EyeWallFraction) * t;
            topFraction = EyeWallTop - ArmTopFalloff * t;
            densityFraction = 1f - ArmDensityFalloff * t;
            discScale = 1f + ArmDiscGrowth * t;
        }

        /// <summary>
        /// 1 粒ぶんの <c>magnitude</c>（＝粒子の密度）。中身は
        /// <see cref="ParticleBudget.MagnitudeFor"/> そのもの ——
        /// **④の渦と暴風雨で同じ式を使う**ためにあちらへ移した。
        /// ここに残してあるのは、渦の側の呼び出しと doc がこの名前で書かれているからである。
        /// </summary>
        /// <param name="discRadius">1 粒の円盤半径（m）。</param>
        /// <param name="rateOverTime">エフェクト側の <c>emission.rateOverTime.constant</c>。</param>
        /// <param name="particlesPerSecond">渦**全体**で 1 秒あたりに出したい粒子数。</param>
        /// <param name="puffCount">粒の数（<see cref="PuffCount"/>）。</param>
        public static float MagnitudeFor(float discRadius, float rateOverTime,
                                         float particlesPerSecond, int puffCount)
        {
            return ParticleBudget.MagnitudeFor(discRadius, rateOverTime,
                                               particlesPerSecond, puffCount);
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
