using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 噴煙柱を 1 段ぶん湧かすための指示。**Unity の型は 1 つも出てこない。**
    /// <c>Game/Volcano/VolcanoEruptionFx</c> がこれを
    /// <c>EffectInfo.SpawnArea(position, Vector3.up, radius, halfHeight)</c> ＋
    /// <c>RenderEffect</c> の <c>velocity</c> 引数へそのまま写す。
    /// </summary>
    public struct EruptionColumnSegment
    {
        /// <summary>噴出口からの水平オフセット（m。風下へずれる量）。</summary>
        public readonly float OffsetX;

        /// <summary>噴出口からの高さ（m）。**この段の下端**である。</summary>
        public readonly float OffsetY;

        /// <summary>噴出口からの水平オフセット（m）。</summary>
        public readonly float OffsetZ;

        /// <summary>湧かす円盤の半径（m）。</summary>
        public readonly float RadiusMetres;

        /// <summary>
        /// 軸（上）方向の伸び（m）。<c>SpawnArea</c> の第 4 引数はこの向きへ
        /// <c>[0, halfHeight]</c> の一様乱数で散らす（**中心対称ではない**。IL 実測 §B-4）ので、
        /// <see cref="OffsetY"/> を下端にすれば段がちょうど積み上がる。
        /// </summary>
        public readonly float HalfHeightMetres;

        /// <summary>粒子へ足す速度（m/秒）。上昇と風下への流されはここで出す。</summary>
        public readonly float DriftX;

        /// <summary>同上（鉛直）。</summary>
        public readonly float DriftY;

        /// <summary>同上。</summary>
        public readonly float DriftZ;

        /// <summary><c>RenderEffect</c> の <c>magnitude</c>（＝粒子密度。大きさではない）。</summary>
        public readonly float Magnitude;

        /// <summary>
        /// 傘（umbrella）の段か。<c>true</c> なら**別の複製**で描く ——
        /// 傘は色が淡く、粒が大きく、寿命が長い（<c>VolcanoVanillaFx.CloneAshUmbrella</c>）。
        /// </summary>
        public readonly bool Umbrella;

        public EruptionColumnSegment(float offsetX, float offsetY, float offsetZ,
                                     float radiusMetres, float halfHeightMetres,
                                     float driftX, float driftY, float driftZ,
                                     float magnitude, bool umbrella)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
            RadiusMetres = radiusMetres;
            HalfHeightMetres = halfHeightMetres;
            DriftX = driftX;
            DriftY = driftY;
            DriftZ = driftZ;
            Magnitude = magnitude;
            Umbrella = umbrella;
        }
    }

    /// <summary>
    /// <b>噴火柱</b>（eruption column）の形と密度。**エンジン非依存の純関数だけ。**
    /// ④の <c>TyphoonSpiral</c> と③の <c>CloudAnimation</c>（ミサイル MOD）と同じ扱いで、
    /// **状態は Core に、粒子は Game に**置く。
    ///
    /// ── なぜ作り直したのか（2026-08-22、実機の指摘③）─────────────────────
    ///
    /// > 噴煙がただの煙だまりになってしまっています。これは MissileMOD のキノコ雲の
    /// > Method を参考にリアルな噴煙（キノコ雲ではない、火山噴火の映像をみて挙動を
    /// > 学習・再現してほしい）を作ってほしいです。
    ///
    /// 以前の噴煙は <c>RenderEffect</c> **1 回**で、噴出口の真上に半径 40〜80 m の円盤を
    /// 置いて煙を湧かせていただけだった。粒子は自分の初速（26〜48 m/s）で 7〜16 秒
    /// 上がって消えるので、出来上がるのは<b>火口の上に浮いた煙の塊</b>である。
    /// 柱にも傘にもならない。**指摘のとおりである。**
    ///
    /// ── ★★ キノコ雲ではない。噴火柱である ────────────────────────────
    ///
    /// ミサイル MOD の <c>CloudAnimation</c> / <c>CloudPuffs</c> から**借りるのは作り方**
    /// （時間を引数にした純粋な状態を Core に置き、Game は毎フレームそれを写すだけ）で、
    /// **形は借りない**。核の雲と噴火柱は物理が違う:
    ///
    /// <list type="bullet">
    /// <item><b>核</b>: 一度の火球が離れて上がる<b>単発の泡</b>。だから細い柄と丸い笠に
    ///   なり、柄は「もう供給が無い」ので痩せて消える</item>
    /// <item><b>火山</b>: 火口から<b>供給され続ける</b>。柱は途切れず、上へ行くほど
    ///   周囲の空気を巻き込んで（entrainment）太くなる</item>
    /// </list>
    ///
    /// この型が組む断面は、火山学の教科書どおりの 3 区間である:
    ///
    /// <code>
    /// 1. ガス推力域 (gas thrust)   0 〜 0.11 H   噴出の運動量で上がる。細くて速い
    /// 2. 対流域     (convective)  0.11 〜 0.78 H 浮力で上がる。減速しながら
    ///                                            dr/dz ≒ 0.14 でほぼ直線的に太る
    /// 3. 傘         (umbrella)    0.78 〜 1.00 H 中立浮力高度。上がるのをやめて
    ///                                            横へ広がる。柱より遥かに広く、平たい
    /// </code>
    ///
    /// そのうえで<b>風下へ倒れる</b>。倒れ方は高いほど強い（<c>(z/H)^1.5</c>）ので、
    /// 柱はまっすぐではなく弓なりになる。傘からは灰が落ちるので、
    /// **風下側だけが長く、低く、薄い** —— 傘を 3 つの段（本体・風下・落下の尾）に
    /// 分けてあるのはそのためである。
    ///
    /// ── 段に分ける（＝バニラの粒子エフェクトで柱を描く方法）──────────────────
    ///
    /// 自前の <c>ParticleSystem</c> は使わない。<c>Shader.Find</c> はこの環境で
    /// <c>"Standard"</c> を含めて全滅する（IL 事実 §D-3 / <c>ShaderPool</c>）ので、
    /// 描けているのは**ゲーム自身の粒子エフェクト**だけである。
    /// そこで柱を <see cref="MaxSegments"/> 段に割り、段ごとに
    /// <c>SpawnArea(位置, 上, 半径, 高さ)</c> の**円柱**を 1 個ずつ湧かす。
    /// 形は「どこに湧かせるか」で作り、粒子自身の初速には頼らない ——
    /// 頼ると寿命ぶん上がり続けて、傘に天井が出来ない。
    ///
    /// ── 密度は面積で正規化する（**ここを外すと粒子が溢れる**）───────────────
    ///
    /// 1 フレームに湧く粒子数は <c>max(100, π r²) × dt × magnitude × 0.01 × rateOverTime</c>
    /// である（IL 実測 §B-4）。半径 700 m の傘に柱と同じ magnitude を渡すと、
    /// 面積比で **70 倍**の粒子を撃つことになる。<see cref="SegmentAt"/> は
    ///
    /// <code>
    /// magnitude_i = (refRadius² × PlumeMagnitude(unit)) × weight_i / radius_i²
    /// </code>
    ///
    /// を返すので、**段ごとの粒子数は半径によらず weight_i に比例する**。
    /// 全段の重みの和は 1 なので、<b>柱ぜんぶで従来の 1 回ぶんと同じ量</b>である。
    /// <c>refRadius</c> と <c>PlumeMagnitude</c> は今までの噴煙と同じ
    /// <see cref="EruptionEffectPlan"/> から取る ——
    /// **噴火の強さの包絡線は 1 本のままにする。**
    /// </summary>
    public struct EruptionColumn
    {
        /// <summary>段の数（柱 <see cref="ColumnSegments"/> ＋ 傘 <see cref="UmbrellaSegments"/>）。</summary>
        public const int MaxSegments = 9;

        /// <summary>柱の段数。**下ほど短く刻む**（変化が速いのは噴出口の近くである）。</summary>
        public const int ColumnSegments = 6;

        /// <summary>傘の段数（本体・風下・落下の尾）。</summary>
        public const int UmbrellaSegments = 3;

        /// <summary>いちばん弱い噴火の柱の高さ（m）。**⑤が決めた演出値。**</summary>
        public const float HeightMinMetres = 420f;

        /// <summary>いちばん強い噴火の柱の高さ（m）。同上。</summary>
        public const float HeightMaxMetres = 1800f;

        /// <summary>高さを火口の大きさで加減する基準（m）。既定の成層火山の火口半径。</summary>
        public const float ReferenceVentRadiusMetres = 144f;

        /// <summary>火口の大きさによる高さの倍率の下限。</summary>
        public const float MinVentScale = 0.6f;

        /// <summary>同上の上限。</summary>
        public const float MaxVentScale = 1.4f;

        /// <summary>ガス推力域の上端（柱の高さに対する比）。</summary>
        public const float GasThrustFraction = 0.11f;

        /// <summary>傘の下端（同上）。ここが中立浮力高度である。</summary>
        public const float UmbrellaBaseFraction = 0.78f;

        /// <summary>噴出口での柱の半径（火口半径に対する比）。火口いっぱいには噴かない。</summary>
        public const float VentRadiusFactor = 0.60f;

        /// <summary>ガス推力域の上端での半径（同上）。まだほとんど太らない。</summary>
        public const float GasTopRadiusFactor = 0.95f;

        /// <summary>
        /// 対流域で 1 m 上がるごとに太る量（無次元）。周囲の空気の巻き込みで、
        /// 噴煙柱の半径は高さにほぼ比例して増える。**演出値だが、桁は教科書の 0.1 前後。**
        /// </summary>
        public const float EntrainmentSlope = 0.14f;

        /// <summary>傘の半径が対流域の上端の何倍か。</summary>
        public const float UmbrellaSpread = 2.8f;

        /// <summary>柱の刻みを下へ寄せる指数（1 なら等間隔）。</summary>
        public const float SegmentBias = 1.35f;

        /// <summary>風下へ倒れる量（柱の高さに対する比。基準風速のとき）。</summary>
        public const float BendFactor = 0.30f;

        /// <summary>倒れ方の指数。**大きいほど「上だけ流される」**（風のシアー）。</summary>
        public const float BendPower = 1.5f;

        /// <summary>倒れ方の基準になる風速（m/秒）。</summary>
        public const float ReferenceWindMetresPerSecond = 12f;

        /// <summary>風で流される速さが高さとともに強くなる指数。</summary>
        public const float WindSharePower = 0.7f;

        /// <summary>噴出口での上昇の速さ（m/秒）。**演出値。**</summary>
        public const float RiseVentMetresPerSecond = 34f;

        /// <summary>上昇が止まるまでの減速の指数（大きいほど下で速く、上で急に止まる）。</summary>
        public const float RiseDecayPower = 1.3f;

        /// <summary>傘のゆっくりした沈み（m/秒）。灰が落ちる側である。</summary>
        public const float FalloutMetresPerSecond = 2.5f;

        /// <summary>柱がゆっくり左右に振れる周期（秒）。**点滅ではなく、ゆらぎである。**</summary>
        public const float SwaySeconds = 37f;

        /// <summary>
        /// 同上の振れ幅（ラジアン）。**風向きそのものを回す**ので、傘のいちばん遠い端は
        /// この角度 × その距離だけ横へ動く（既定で ±230 m ほど）。傘の半径より小さいので
        /// 「輪郭がゆっくりぼやける」に見える。**これ以上大きくしないこと** ——
        /// 大きくすると傘が首を振り、湧かす場所の移動が風速そのものより速くなる。
        /// </summary>
        public const float SwayRadians = 0.10f;

        /// <summary>半径がこれを下回ったら使わない（m）。<see cref="EruptionEffectPlan"/> と同じ下限。</summary>
        public const float MinRadiusMetres = EruptionEffectPlan.MinRadiusMetres;

        /// <summary>
        /// 段ごとの重み（粒子数の配分）。**和は 1。** 前 <see cref="ColumnSegments"/> 個が柱で、
        /// 残りが傘である。下ほど濃いのは、噴出口の近くほど灰が密で暗いからである。
        ///
        /// ★ **柱と傘は別の粒子系（別の複製）なので、両者の配分の比は実機では効かない** ——
        ///   どちらも <c>maxParticles</c> で頭打ちになるからである。効くのは
        ///   <b>同じ系の中での比</b>で、柱 6 段のあいだ／傘 3 段のあいだの濃さを決めている。
        ///   柱の重みを平らに寄せてあるのは、下だけが濃くて中ほどがすかすかに見えたため
        ///   （<c>tools/VolcanoPreview</c> の plume 画像で確認した）。
        /// </summary>
        private static readonly float[] Weights =
        {
            0.16f, 0.14f, 0.13f, 0.12f, 0.11f, 0.10f,   // 柱（下 → 上）
            0.11f, 0.08f, 0.05f,                        // 傘（本体・風下・尾）
        };

        private readonly float _ventRadius;
        private readonly float _height;
        private readonly float _gasTop;
        private readonly float _umbrellaBase;
        private readonly float _gasTopRadius;
        private readonly float _umbrellaRadius;
        private readonly float _bendMetres;
        private readonly float _windX;
        private readonly float _windZ;
        private readonly float _windSpeed;
        private readonly float _budget;

        /// <summary>柱の高さ（m。傘の天面まで）。</summary>
        public float HeightMetres { get { return _height; } }

        /// <summary>傘の半径（m）。</summary>
        public float UmbrellaRadiusMetres { get { return _umbrellaRadius; } }

        /// <summary>傘の下端の高さ（m）＝中立浮力高度。</summary>
        public float UmbrellaBaseMetres { get { return _umbrellaBase; } }

        /// <summary>柱の頂が風下へずれる量（m）。</summary>
        public float BendMetres { get { return _bendMetres; } }

        /// <summary>段の数。**常に <see cref="MaxSegments"/>**（配列を作らないための固定長）。</summary>
        public int SegmentCount { get { return MaxSegments; } }

        /// <summary>
        /// 噴火柱を 1 本組む。**ヒープ確保は 0**（<c>struct</c>）なので毎フレーム作ってよい。
        /// </summary>
        /// <param name="ventRadiusMetres">火口の半径（m）。柱の太さと高さの基準。</param>
        /// <param name="intensityUnit">噴出の強さ <c>[0,1]</c>（<c>VolcanoEruption.IntensityUnit</c>）。</param>
        /// <param name="windX">風向き（単位ベクトル。長さは内部で正規化する）。</param>
        /// <param name="windZ">同上。</param>
        /// <param name="windMetresPerSecond">風速（m/秒）。</param>
        public EruptionColumn(float ventRadiusMetres, float intensityUnit,
                              float windX, float windZ, float windMetresPerSecond)
        {
            float vent = IsBad(ventRadiusMetres) || ventRadiusMetres < MinRadiusMetres
                ? MinRadiusMetres : ventRadiusMetres;
            float unit = Clamp01(intensityUnit);

            _ventRadius = vent;

            float ventScale = Clamp(0.55f + 0.45f * (vent / ReferenceVentRadiusMetres),
                                    MinVentScale, MaxVentScale);
            _height = (HeightMinMetres + (HeightMaxMetres - HeightMinMetres) * unit) * ventScale;

            _gasTop = _height * GasThrustFraction;
            _umbrellaBase = _height * UmbrellaBaseFraction;
            _gasTopRadius = vent * GasTopRadiusFactor;

            float convectiveTop = _gasTopRadius + EntrainmentSlope * (_umbrellaBase - _gasTop);
            _umbrellaRadius = convectiveTop * UmbrellaSpread;

            // 風。長さ 0（無風）は「倒れない」であって NaN ではない。
            float length = (float)Math.Sqrt(windX * windX + windZ * windZ);
            if (IsBad(length) || length <= 0f)
            {
                _windX = 0f;
                _windZ = 0f;
            }
            else
            {
                _windX = windX / length;
                _windZ = windZ / length;
            }

            float speed = IsBad(windMetresPerSecond) || windMetresPerSecond < 0f
                ? 0f : windMetresPerSecond;
            _windSpeed = speed > ReferenceWindMetresPerSecond * 2f
                ? ReferenceWindMetresPerSecond * 2f : speed;

            _bendMetres = _height * BendFactor
                          * (_windSpeed / ReferenceWindMetresPerSecond);

            // ★ 粒子の予算は今までの噴煙 1 回ぶんと同じ（クラス doc の正規化）。
            float refRadius = EruptionEffectPlan.PlumeRadiusMetres(vent, unit);
            _budget = refRadius * refRadius * EruptionEffectPlan.PlumeMagnitude(unit);
        }

        /// <summary>
        /// <paramref name="index"/> 段目の湧かし方。**範囲外は密度 0 の段**を返す
        /// （呼び出し側は <c>Magnitude &gt; 0</c> のときだけ描けばよい）。
        /// </summary>
        public EruptionColumnSegment SegmentAt(int index)
        {
            if (index < 0 || index >= MaxSegments)
            {
                return new EruptionColumnSegment(0f, 0f, 0f, MinRadiusMetres, 0f,
                                                 0f, 0f, 0f, 0f, false);
            }

            return index < ColumnSegments ? ColumnSegment(index) : UmbrellaSegment(index);
        }

        /// <summary>柱の 1 段。下端 <c>z0</c> から上端 <c>z1</c> までの円柱である。</summary>
        private EruptionColumnSegment ColumnSegment(int index)
        {
            float z0 = ColumnBoundary(index);
            float z1 = ColumnBoundary(index + 1);
            float mid = (z0 + z1) * 0.5f;

            float radius = RadiusAt(mid);
            float offset = BendAt(mid);

            float share = WindShareAt(mid);
            float rise = RiseAt(mid);

            return new EruptionColumnSegment(
                _windX * offset, z0, _windZ * offset,
                radius, z1 - z0,
                _windX * _windSpeed * share, rise, _windZ * _windSpeed * share,
                MagnitudeFor(index, radius), false);
        }

        /// <summary>
        /// 傘の 1 段。<b>0 = 本体、1 = 風下、2 = 落下の尾</b>で、風下ほど低く・薄い
        /// （傘の底から灰が落ちるので、風下側だけが長く尾を引く）。
        /// </summary>
        private EruptionColumnSegment UmbrellaSegment(int index)
        {
            int i = index - ColumnSegments;

            float alongFactor = i == 0 ? 0f : (i == 1 ? 1.0f : 2.1f);
            float dropFactor = i == 0 ? 0f : (i == 1 ? 0.04f : 0.13f);
            float radiusFactor = i == 0 ? 0.80f : (i == 1 ? 0.65f : 0.75f);
            float pushFactor = i == 0 ? 0.55f : (i == 1 ? 0.80f : 0.95f);
            float fall = i == 0 ? 0f : (i == 1 ? -FalloutMetresPerSecond * 0.4f
                                               : -FalloutMetresPerSecond);

            float along = BendAt(_height) + _umbrellaRadius * alongFactor;
            float y = _umbrellaBase - _height * dropFactor;
            if (y < 0f) y = 0f;

            float thickness = _height - _umbrellaBase;
            float radius = _umbrellaRadius * radiusFactor;
            if (radius < MinRadiusMetres) radius = MinRadiusMetres;

            return new EruptionColumnSegment(
                _windX * along, y, _windZ * along,
                radius, thickness,
                _windX * _windSpeed * pushFactor, fall, _windZ * _windSpeed * pushFactor,
                MagnitudeFor(index, radius), true);
        }

        /// <summary>柱の <paramref name="index"/> 番目の境目の高さ（m）。下ほど細かい。</summary>
        public float ColumnBoundary(int index)
        {
            if (index <= 0) return 0f;
            if (index >= ColumnSegments) return _umbrellaBase;

            float t = index / (float)ColumnSegments;
            return _umbrellaBase * (float)Math.Pow(t, SegmentBias);
        }

        /// <summary>
        /// 高さ <paramref name="metres"/> での柱の半径（m）。
        /// ガス推力域はほぼ一定、対流域は<b>高さにほぼ比例して太る</b>（巻き込み）。
        /// </summary>
        public float RadiusAt(float metres)
        {
            float r0 = _ventRadius * VentRadiusFactor;

            float r;
            if (IsBad(metres) || metres <= 0f)
            {
                r = r0;
            }
            else if (metres <= _gasTop)
            {
                float t = _gasTop > 0f ? metres / _gasTop : 1f;
                r = r0 + (_gasTopRadius - r0) * t;
            }
            else
            {
                float top = metres > _umbrellaBase ? _umbrellaBase : metres;
                r = _gasTopRadius + EntrainmentSlope * (top - _gasTop);
            }

            // ★ 下限は 1 か所で掛ける。**半径 0 を渡しても粒子は湧く**（面積の床が
            //   max(100, πr²) なので、1 点から噴くことになる。§B-4）。
            return r < MinRadiusMetres ? MinRadiusMetres : r;
        }

        /// <summary>高さ <paramref name="metres"/> での風下へのずれ（m）。</summary>
        public float BendAt(float metres)
        {
            if (IsBad(metres) || metres <= 0f || !(_height > 0f)) return 0f;

            float t = metres / _height;
            if (t > 1f) t = 1f;
            return _bendMetres * (float)Math.Pow(t, BendPower);
        }

        /// <summary>高さ <paramref name="metres"/> での上昇の速さ（m/秒）。傘では 0。</summary>
        public float RiseAt(float metres)
        {
            if (IsBad(metres) || !(_umbrellaBase > 0f)) return 0f;
            if (metres >= _umbrellaBase) return 0f;

            float t = metres <= 0f ? 0f : metres / _umbrellaBase;
            float left = 1f - t;
            return RiseVentMetresPerSecond * (float)Math.Pow(left, RiseDecayPower);
        }

        /// <summary>高さ <paramref name="metres"/> で風にどれだけ流されるか <c>[0,1]</c>。</summary>
        public float WindShareAt(float metres)
        {
            if (IsBad(metres) || metres <= 0f || !(_height > 0f)) return 0f;

            float t = metres / _height;
            if (t > 1f) t = 1f;
            return (float)Math.Pow(t, WindSharePower);
        }

        /// <summary>
        /// 段の密度。**面積で正規化する**ので、粒子数は半径によらず重みに比例する
        /// （クラス doc）。異常な半径でも 0 除算を外へ出さない。
        /// </summary>
        public float MagnitudeFor(int index, float radiusMetres)
        {
            if (index < 0 || index >= Weights.Length) return 0f;

            float r = IsBad(radiusMetres) || radiusMetres < MinRadiusMetres
                ? MinRadiusMetres : radiusMetres;
            float area = r * r;
            if (!(area > 0f)) return 0f;

            float m = _budget * Weights[index] / area;
            if (IsBad(m) || m < 0f) return 0f;
            return m;
        }

        /// <summary>
        /// 柱がゆっくり左右に振れる角度（ラジアン）。**周期 <see cref="SwaySeconds"/> の
        /// 正弦 1 本だけ**である —— 速い成分を足すと「ゆらぎ」ではなく「点滅」になる
        /// （溶岩の発光で同じ失敗をしている。<c>LavaGlow</c> のクラス doc）。
        /// </summary>
        public static float SwayAt(float clockSeconds)
        {
            if (IsBad(clockSeconds)) return 0f;
            return SwayRadians * (float)Math.Sin(2.0 * Math.PI * clockSeconds / SwaySeconds);
        }

        private static float Clamp(float v, float min, float max)
        {
            if (IsBad(v)) return min;
            if (v < min) return min;
            return v > max ? max : v;
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
