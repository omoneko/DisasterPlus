using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 山肌の凹凸。<see cref="VolcanoShape.ProfileAt"/> が返す**軸対称の円錐**へ、
    /// 方位（azimuth）に依存する起伏を掛けて「旋盤で挽いた円錐」を崩す型である。
    ///
    /// ── なぜ「ノイズを足す」ではないのか ─────────────────────────
    ///
    /// 無指向のノイズを高さへ足すと、山ではなく**砂嵐**に見える。実際の成層火山の
    /// 肌を決めているのは 3 つで、優先度もこの順である。
    ///
    ///   1. **放射谷（バランコ）** —— 斜面を上から下へ走る溝。方位方向にほぼ等間隔で、
    ///      **山頂側は浅く、裾へ向かって深くなる**。成層火山の見た目そのものである
    ///   2. **円でない裾** —— 低周波の方位変化。footprint が真円でなくなる
    ///   3. **一般の粗さ** —— 波長 100〜200 m 程度のうねり
    ///   4. **非対称** —— 片側の裾が長い / 急である（1 次の方位成分が担う）
    ///
    /// ── 2 つの硬い制約を「掛け算だけ」で構造的に守る ───────────────
    ///
    /// **半径 R を 1 mm も超えない。** 準備（破壊）が届いていない場所を持ち上げると
    /// 道路が <c>NetSegment.TerrainUpdated</c> でセルを道路の y へ引き戻し、
    /// 山の中に平らな溝が残る（設計書 §1.2）。したがって方位変調は
    /// **実効半径を縮める向きにしか働かない**:
    ///
    /// <code>
    /// Reff(θ) = R × (1 − shrink(θ))     shrink(θ) ∈ [0, shrinkMax]   ⇒  Reff ≤ R
    /// </code>
    ///
    /// **最終高 H を 1 mm も超えない。** <c>HeightFor</c> / <c>HeadroomMetres</c> /
    /// <c>HeightWasLimitedByCeiling</c> / <c>CraterRimHeadroomOf</c> と 1024 m の
    /// 生の天井（§C-10）が全部 H を基準に考えている。したがって起伏は
    /// **削る向きにしか働かない**:
    ///
    /// <code>
    /// profile = VolcanoShape.ProfileAt(form, d, Reff(θ), H) × carve(θ, d)
    ///                                                         carve ∈ [0, 1]
    /// </code>
    ///
    /// <c>VolcanoShape.ProfileAt</c> は d=0 で必ず H を返し、それ以外では H 未満なので、
    /// <c>carve ≤ 1</c> であるかぎり結果は必ず H 以下である。**足し算を 1 つも
    /// 持ち込まないこと** —— 持ち込んだ瞬間に上の 2 つが両方とも「たぶん大丈夫」に落ちる。
    /// 削る向きだけというのは物理的にも正しい: 円錐は堆積の包絡線で、谷はそこを侵食して
    /// 削った跡である。
    ///
    /// ── 強さ 0 は「今日と完全に同じ」でなければならない ────────────
    ///
    /// <see cref="StrengthUnit"/> が 0 のとき、この型は <c>VolcanoShape.ProfileAt</c> を
    /// **そのまま返す**（分岐 1 本で早期に抜ける）。設定を 0 にした人が得るのは
    /// 「起伏が小さい山」ではなく**今日の出力そのもの**である。
    ///
    /// ── 乱数 ────────────────────────────────────────────
    ///
    /// <see cref="DeterministicRandom"/> だけを使う。<c>VanillaRandomizer</c> は
    /// **使わない** —— あれはバニラが引く値を先読みするためだけの型で、⑤はバニラの
    /// 災害スロットに載らないので同期すべき引きが 1 つも存在しない。
    /// <c>System.Random</c> も <c>Mathf.PerlinNoise</c> も使わない（Core は engine-free で、
    /// net35 と net8.0 で同じ値を出す必要がある）。
    ///
    /// ── 16 m 格子 ───────────────────────────────────────
    ///
    /// <c>RawHeights</c> のセルは 16 m である。**波長 64 m を下回る成分は起伏ではなく
    /// ノイズに化ける**ので、いちばん細かい成分でも 100 m（6 セル強）より短くしない
    /// （<see cref="MinWavelengthMetres"/>）。方位方向の波長は円周を谷の本数で割った値で、
    /// **山頂に近いほど短くなる**ので、そちらは <see cref="MinAzimuthWavelengthMetres"/> が
    /// 別に見張る —— 見張らないと山頂まわりが市松模様になる（実測で確認した）。
    ///
    /// ── 費用 ────────────────────────────────────────────
    ///
    /// 1 セルあたり三角関数は 0 回である。方位の高調波 cos(kθ) / sin(kθ) は
    /// 単位複素数 (ux + i·uz) の累乗で作る（<see cref="ProfileAt"/> のループ）ので、
    /// 掛け算と足し算しか出てこない。それでも 1 セル 300 flop 程度はあるので、
    /// 呼び出し側（<c>Game/Volcano/VolcanoUplift</c>）は**隆起の開始時に 1 回だけ
    /// 全セルを評価して配列に焼く**。毎 tick 呼ぶ型ではない。
    /// </summary>
    public sealed class VolcanoRelief
    {
        /// <summary>設定から受け取る強さの上限。1 が形態ごとの既定値そのもの。</summary>
        public const float MaxStrengthUnit = 1.5f;

        /// <summary>いちばん細かい成分の波長の下限（m）。16 m 格子で 6 セル強。</summary>
        public const float MinWavelengthMetres = 100f;

        /// <summary>
        /// 方位方向の谷が成立する最小の波長（m）。**山頂に近づくほど方位の波長は
        /// 短くなる**（円周 2πd を谷の本数で割った値）ので、ここを守らないと
        /// 山頂まわりで谷が 16 m 格子に食い込み、起伏ではなく市松模様になる。
        /// 6 セル。
        /// </summary>
        public const float MinAzimuthWavelengthMetres = 96f;

        /// <summary>裾の輪郭に使う方位の高調波の本数（k = 1..4）。1 次が非対称を担う。</summary>
        private const int ShapeHarmonics = 4;

        /// <summary>谷に使う方位の高調波の本数（k = n-2 .. n+2）。側帯が等間隔を崩す。</summary>
        private const int GullyHarmonics = 5;

        /// <summary>谷が出はじめる山頂からの距離（実効半径に対する比）の最小値。</summary>
        private const float GullyStartFraction = 0.16f;

        /// <summary>
        /// 谷ごとに出はじめる距離をずらす幅（同上）。**0 にすると全部の谷が
        /// 山頂の 1 点へ集まり、山ではなく放射状の縞模様に見える。**
        /// </summary>
        private const float GullyStartSpread = 0.24f;

        /// <summary>出はじめてから深さいっぱいになるまでの距離（同上）。裾で深い。</summary>
        private const float GullyRampFraction = 0.30f;

        /// <summary>
        /// 谷の幅。方位の系列 A(θ) の**零交差**を谷の底とし、|A| がこの値を超えたら
        /// 谷の外（尾根）とする。
        ///
        /// 正弦波をそのまま深さに使うと「波板」になる（山肌の半分が谷になる）。
        /// 零交差を使うと、谷は必ず深さいっぱいに達し、**尾根は広く平ら**になる ——
        /// これが成層火山の見え方である。幅は A の局所的な傾きで決まるので、
        /// 谷ごとに自然に広狭が出る。谷の本数は零交差の数、すなわち
        /// **中心の高調波の 2 倍**である（<see cref="GullyCount"/>）。
        /// </summary>
        private const float ChannelWidth = 0.30f;

        /// <summary>
        /// 谷を蛇行させる量（ラジアン）。**まっすぐな放射線は人工物に見える。**
        /// 単位ベクトルを微小角だけ回すだけなので三角関数は要らない。
        /// </summary>
        private const float WarpRadians = 0.13f;

        /// <summary>粗さが出はじめる距離（同上）。山頂は火口が彫られるので触らない。</summary>
        private const float RoughStartFraction = 0.04f;

        /// <summary>粗さがいっぱいになる距離（同上）。</summary>
        private const float RoughFullFraction = 0.18f;

        /// <summary>方位系列を [-1,1] へ均す係数（分散から出した経験値）。</summary>
        private const float NormaliseSpread = 1.45f;

        /// <summary>削り量の上限。これを超えると carve が負に振れうる。</summary>
        private const float MaxCarveAmplitude = 0.6f;

        /// <summary>実効半径を縮める量の上限。</summary>
        private const float MaxShrink = 0.30f;

        /// <summary>いちばん長い粗さの成分の波長（<c>coarse</c> に対する比）。裾のうねり。</summary>
        private const float BroadOctaveRatio = 2.2f;

        /// <summary>谷の深さが方位ごとにばらつく下限（1 なら全部同じ深さ）。</summary>
        private const float GullyDepthFloor = 0.40f;

        private readonly VolcanoForm _form;
        private readonly float _strength;

        private readonly float[] _shapeCos;
        private readonly float[] _shapeSin;
        private readonly float[] _gullyCos;
        private readonly float[] _gullySin;
        private readonly float[] _gullyVarCos;
        private readonly float[] _gullyVarSin;

        /// <summary>谷の系列がはじまる高調波の番号（= 本数 n − 2）。</summary>
        private readonly int _gullyFirst;

        /// <summary>ループを回す最大の高調波（= n + 2）。</summary>
        private readonly int _maxHarmonic;

        private readonly float _shapeNorm;
        private readonly float _gullyNorm;

        private readonly float _shrink;
        private readonly float _gullyAmplitude;
        private readonly float _roughAmplitude;
        private readonly float _wavelengthBroad;
        private readonly float _wavelengthCoarse;
        private readonly float _wavelengthFine;
        private readonly uint _seedBroad;
        private readonly uint _seedCoarse;
        private readonly uint _seedFine;
        private readonly uint _seedWarp;

        /// <summary>この起伏の強さ（0 = 今日の滑らかな円錐そのもの）。</summary>
        public float StrengthUnit { get { return _strength; } }

        /// <summary>放射谷の本数（診断とテスト用）。方位系列の零交差の数である。</summary>
        public int GullyCount { get { return (_gullyFirst + 2) * 2; } }

        /// <summary>いちばん細かい成分の波長（m。診断とテスト用）。</summary>
        public float FineWavelengthMetres { get { return _wavelengthFine; } }

        /// <summary>
        /// 起伏を 1 個作る。<paramref name="seed"/> は火山の地点から出した種
        /// （<c>DeterministicRandom.Hash(round(X), round(Z))</c>）を渡すこと ——
        /// **同じ地点なら何度作り直しても同じ山になる。**
        ///
        /// <paramref name="strengthUnit"/> は 0 で「今日と完全に同じ」、
        /// 1 で形態ごとの既定値、上限は <see cref="MaxStrengthUnit"/>。
        /// NaN と負は 0 に落とす（設定ファイルは手で編集されうる）。
        /// </summary>
        public static VolcanoRelief For(VolcanoForm form, uint seed, float strengthUnit)
        {
            return new VolcanoRelief(form, seed, strengthUnit);
        }

        private VolcanoRelief(VolcanoForm form, uint seed, float strengthUnit)
        {
            _form = form;

            float s = float.IsNaN(strengthUnit) || strengthUnit < 0f ? 0f : strengthUnit;
            if (s > MaxStrengthUnit) s = MaxStrengthUnit;
            _strength = s;

            // ── 形態ごとの性格 ────────────────────────────────
            // 盾状: 玄武岩質の楯状火山は実際になめらかなので、いちばん今日に近い。
            // 成層: **放射谷はここのもの。** 変化がいちばん強く出る。
            // 鐘状: ごつごつして塊状。いちばん短波長で、いちばん粗い。
            int harmonic;
            float shrink, gully, rough, coarse, fine;
            switch (form)
            {
                case VolcanoForm.Shield:
                    harmonic = 4; shrink = 0.06f; gully = 0.08f; rough = 0.07f;
                    coarse = 340f; fine = 170f;
                    break;

                case VolcanoForm.Dome:
                    harmonic = 5; shrink = 0.16f; gully = 0.14f; rough = 0.28f;
                    coarse = 220f; fine = 110f;
                    break;

                default:
                    harmonic = 9; shrink = 0.11f; gully = 0.30f; rough = 0.16f;
                    coarse = 220f; fine = 110f;
                    break;
            }

            _shrink = Clamp(shrink * s, 0f, MaxShrink);
            _gullyAmplitude = Clamp(gully * s, 0f, MaxCarveAmplitude);
            _roughAmplitude = Clamp(rough * s, 0f, MaxCarveAmplitude);

            // ★ 16 m 格子。MinWavelengthMetres を下回る波長はノイズに化ける。
            _wavelengthCoarse = coarse < MinWavelengthMetres ? MinWavelengthMetres : coarse;
            _wavelengthFine = fine < MinWavelengthMetres ? MinWavelengthMetres : fine;
            _wavelengthBroad = _wavelengthCoarse * BroadOctaveRatio;

            _gullyFirst = harmonic - 2;
            _maxHarmonic = harmonic + 2;

            _seedBroad = DeterministicRandom.Hash(seed, 0x5EEDB40Du);
            _seedCoarse = DeterministicRandom.Hash(seed, 0x5EEDC0DEu);
            _seedFine = DeterministicRandom.Hash(seed, 0x5EEDF14Eu);
            _seedWarp = DeterministicRandom.Hash(seed, 0x5EED1A2Bu);

            // 裾の輪郭。1 次を最大にしてあるのが「片側の裾が長い」の実体である。
            float[] shapeWeights = { 1.00f, 0.55f, 0.35f, 0.22f };
            // 谷。中央（k = n）を最大に、側帯 n±1 / n±2 が等間隔と深さを崩す。
            float[] gullyWeights = { 0.55f, 0.80f, 1.00f, 0.80f, 0.55f };

            _shapeCos = new float[ShapeHarmonics];
            _shapeSin = new float[ShapeHarmonics];
            _gullyCos = new float[GullyHarmonics];
            _gullySin = new float[GullyHarmonics];
            _gullyVarCos = new float[GullyHarmonics];
            _gullyVarSin = new float[GullyHarmonics];

            for (int i = 0; i < ShapeHarmonics; i++)
            {
                double phase = 2.0 * Math.PI * DeterministicRandom.Unit(seed, (uint)(0x100 + i));
                _shapeCos[i] = shapeWeights[i] * (float)Math.Cos(phase);
                _shapeSin[i] = shapeWeights[i] * (float)Math.Sin(phase);
            }

            for (int i = 0; i < GullyHarmonics; i++)
            {
                double phase = 2.0 * Math.PI * DeterministicRandom.Unit(seed, (uint)(0x200 + i));
                _gullyCos[i] = gullyWeights[i] * (float)Math.Cos(phase);
                _gullySin[i] = gullyWeights[i] * (float)Math.Sin(phase);

                // 同じ帯・別の位相。**谷ごとに出はじめる高さを変える**ためだけの系列で、
                // これが無いと全部の谷が山頂の 1 点へ集まる。
                double varPhase = 2.0 * Math.PI * DeterministicRandom.Unit(seed, (uint)(0x300 + i));
                _gullyVarCos[i] = gullyWeights[i] * (float)Math.Cos(varPhase);
                _gullyVarSin[i] = gullyWeights[i] * (float)Math.Sin(varPhase);
            }

            _shapeNorm = NormOf(shapeWeights);
            _gullyNorm = NormOf(gullyWeights);
        }

        /// <summary>
        /// 中心から <paramref name="dx"/> / <paramref name="dz"/> だけ離れた地点の
        /// **地形からの盛り上がり**（m）。
        ///
        /// **返り値は必ず [0, <paramref name="heightMetres"/>] で、
        /// <c>√(dx²+dz²) ≥ radiusMetres</c> なら必ずきっかり 0 である。**
        /// この 2 つはクラス doc の 2 つの硬い制約そのもので、掛け算しか使わないことで
        /// 構造的に守られている。異常入力（NaN・R≤0・H≤0）も 0。
        /// </summary>
        public float ProfileAt(float dx, float dz, float radiusMetres, float heightMetres)
        {
            if (float.IsNaN(dx) || float.IsNaN(dz)) return 0f;
            if (float.IsNaN(radiusMetres) || float.IsNaN(heightMetres)) return 0f;
            if (radiusMetres <= 0f || heightMetres <= 0f) return 0f;

            float d2 = dx * dx + dz * dz;
            float d = (float)Math.Sqrt(d2);

            // ★★ 強さ 0 は「起伏の小さい山」ではなく**今日の出力そのもの**である。
            if (!(_strength > 0f)) return VolcanoShape.ProfileAt(_form, d, radiusMetres, heightMetres);

            if (d >= radiusMetres) return 0f;
            if (!(d > 0f)) return VolcanoShape.ProfileAt(_form, 0f, radiusMetres, heightMetres);

            float inv = 1f / d;
            float ux = dx * inv;
            float uz = dz * inv;

            // ── 2. 円でない裾。**方位だけの関数**なので輪郭は閉じた滑らかな曲線になる ──
            //   ★ 三角関数は使わない。cos(kθ) / sin(kθ) は単位複素数の累乗で出す。
            //     (ux + i·uz)^k の実部が cos(kθ)、虚部が sin(kθ) である。
            float shapeAz = 0f;
            float cr = ux;
            float ci = uz;
            for (int k = 1; k <= ShapeHarmonics; k++)
            {
                shapeAz += _shapeCos[k - 1] * cr + _shapeSin[k - 1] * ci;
                float nr0 = cr * ux - ci * uz;
                ci = cr * uz + ci * ux;
                cr = nr0;
            }
            shapeAz = Clamp(shapeAz * _shapeNorm, -1f, 1f);

            // ★★ **縮める向きにしか働かない**（クラス doc の制約 1）。
            float effectiveRadius = radiusMetres * (1f - _shrink * 0.5f * (1f - shapeAz));
            if (!(effectiveRadius > 0f)) return 0f;
            if (d >= effectiveRadius) return 0f;

            float baseMetres = VolcanoShape.ProfileAt(_form, d, effectiveRadius, heightMetres);
            if (!(baseMetres > 0f)) return 0f;

            float t = d / effectiveRadius;

            // ── 1. 放射谷（バランコ）───────────────────────────
            //   まっすぐな放射線は人工物に見えるので、方位を場所ごとに微小角だけ回す。
            //   微小角の回転は (ux − uz·w, uz + ux·w) を正規化するだけで、三角関数は要らない。
            float warp = WarpRadians
                       * ValueNoise(dx / (_wavelengthCoarse * 2f), dz / (_wavelengthCoarse * 2f),
                                    _seedWarp);
            float wx = ux - uz * warp;
            float wz = uz + ux * warp;
            float wlen = (float)Math.Sqrt(wx * wx + wz * wz);
            if (wlen > 0f) { float wi = 1f / wlen; wx *= wi; wz *= wi; }
            else { wx = ux; wz = uz; }

            float gullyAz = 0f;
            float gullyVarAz = 0f;
            cr = wx;
            ci = wz;
            for (int k = 1; k <= _maxHarmonic; k++)
            {
                int g = k - _gullyFirst;
                if (g >= 0 && g < GullyHarmonics)
                {
                    gullyAz += _gullyCos[g] * cr + _gullySin[g] * ci;
                    gullyVarAz += _gullyVarCos[g] * cr + _gullyVarSin[g] * ci;
                }

                float nr = cr * wx - ci * wz;
                ci = cr * wz + ci * wx;
                cr = nr;
            }
            gullyAz = Clamp(gullyAz * _gullyNorm, -1f, 1f);
            gullyVarAz = Clamp(gullyVarAz * _gullyNorm, -1f, 1f);

            //   谷ごとに出はじめる高さが違う。**これが無いと全部の谷が山頂の 1 点へ集まり、
            //   山ではなく放射状の縞模様に見える。**
            float start = GullyStartFraction + GullyStartSpread * 0.5f * (1f + gullyVarAz);
            float depth = SmoothStep(start, start + GullyRampFraction, t);

            //   ★★ **山頂に近いほど方位の波長は短い。** 円周を谷の本数で割った波長が
            //   16 m 格子に対して短くなりすぎるところでは谷を消す ——
            //   消さないと山頂まわりが起伏ではなく市松模様になる（実測で確認した）。
            float azWavelength = 6.2831853f * d / _maxHarmonic;
            depth *= SmoothStep(MinAzimuthWavelengthMetres, MinAzimuthWavelengthMetres * 2f,
                                azWavelength);

            //   谷の底は方位系列の零交差。**尾根は広く平ら**になる（クラス doc）。
            float abs = gullyAz < 0f ? -gullyAz : gullyAz;
            float channel = 1f - SmoothStep(0f, ChannelWidth, abs);

            //   深さは方位と場所でばらつかせる。**全部の谷が同じ深さだと花の模様に見える。**
            float broad = ValueNoise(dx / _wavelengthBroad, dz / _wavelengthBroad, _seedBroad);
            float depthScale = GullyDepthFloor
                             + (1f - GullyDepthFloor) * 0.5f * (1f + broad);

            float carve = 1f - _gullyAmplitude * depth * channel * depthScale;

            // ── 3. 一般の粗さ。3 オクターブ（裾のうねり / 中間 / 細かい肌）──
            float rough = 0.42f * broad
                        + 0.35f * ValueNoise(dx / _wavelengthCoarse, dz / _wavelengthCoarse, _seedCoarse)
                        + 0.23f * ValueNoise(dx / _wavelengthFine, dz / _wavelengthFine, _seedFine);
            carve *= 1f - _roughAmplitude * SmoothStep(RoughStartFraction, RoughFullFraction, t)
                                          * 0.5f * (1f - rough);

            // ★★ **carve は決して 1 を超えない**（クラス doc の制約 2）。
            //    振幅の設計上ここには来ないが、.cgs は手で編集されうる。
            carve = Clamp(carve, 0f, 1f);
            return baseMetres * carve;
        }

        /// <summary>
        /// 方位系列を [-1,1] へ均す係数。素の和は最大 Σw だが実際にはめったにそこまで
        /// 振れないので、RMS から出した幅で割ってから <see cref="ProfileAt"/> が
        /// クランプする。**割った結果が [-1,1] を出ることは織り込み済みである。**
        /// </summary>
        private static float NormOf(float[] weights)
        {
            float sum = 0f;
            for (int i = 0; i < weights.Length; i++) sum += weights[i] * weights[i];
            float rms = (float)Math.Sqrt(sum * 0.5);
            if (!(rms > 0f)) return 0f;
            return 1f / (rms * NormaliseSpread);
        }

        /// <summary>
        /// 2 次元の値ノイズ（[-1,1]）。格子点の値は <see cref="DeterministicRandom"/> の
        /// ハッシュそのもので、補間は 3t²−2t³ である。
        /// **<c>Mathf.PerlinNoise</c> を使わない** —— Core は engine-free で、
        /// net35 と net8.0 で同じ値を出さなければならない。
        /// </summary>
        private static float ValueNoise(float x, float z, uint seed)
        {
            int ix = FloorToInt(x);
            int iz = FloorToInt(z);
            float fx = x - ix;
            float fz = z - iz;

            float u = fx * fx * (3f - 2f * fx);
            float v = fz * fz * (3f - 2f * fz);

            float n00 = Corner(ix, iz, seed);
            float n10 = Corner(ix + 1, iz, seed);
            float n01 = Corner(ix, iz + 1, seed);
            float n11 = Corner(ix + 1, iz + 1, seed);

            float a = n00 + (n10 - n00) * u;
            float b = n01 + (n11 - n01) * u;
            return a + (b - a) * v;
        }

        private static float Corner(int x, int z, uint seed)
        {
            unchecked
            {
                uint key = (uint)(x * 73856093) ^ (uint)(z * 19349663);
                uint h = DeterministicRandom.Hash(key, seed);
                return (h >> 8) * (2f / 16777216f) - 1f;
            }
        }

        /// <summary><c>Math.Floor</c> を通さない整数化（負でも下へ丸める）。</summary>
        private static int FloorToInt(float v)
        {
            int i = (int)v;
            return v < 0f && v != i ? i - 1 : i;
        }

        /// <summary>[<paramref name="from"/>, <paramref name="to"/>] で 0 → 1 へ滑らかに。</summary>
        private static float SmoothStep(float from, float to, float t)
        {
            if (!(to > from)) return t >= to ? 1f : 0f;
            float u = (t - from) / (to - from);
            u = Clamp(u, 0f, 1f);
            return u * u * (3f - 2f * u);
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
