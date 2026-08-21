using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 「火砕流」に見せる<b>土煙の扇</b>の幾何。**エンジン非依存の純関数だけ。**
    ///
    /// ── ★★ これは火砕流ではない。名乗り方を間違えないこと ─────────────
    ///
    /// バニラに火砕流は無い。出荷アセットの <c>EffectInfo</c> を全数（277 個）調べても、
    /// 「地面を這って高速で流れ下る濃密な雲」に当たるものは基本ゲームにも DLC にも
    /// 存在しない。⑤が出すのは <b>建物が崩れるときの粉塵</b>
    /// （<c>Collapse Particles</c>）を斜面のベジェ帯へ湧かせ、引数の <c>velocity</c> で
    /// 下り方向へ押したものである。見た目は「斜面を駆け下りる灰色の土煙」であって、
    /// 火砕流の再現ではない。**この型は建物にも木にも地面にも触らない**
    /// （燃やすのは溶岩の仕事である）。パネルと設計書は<b>そう書く</b>。
    ///
    /// ── ★★ 扇である。溶岩の上のリボンではない（2026-08-22、実機の指摘⑤）───────
    ///
    /// > 火砕流については溶岩流の上だけを今は流れ落ちていますが、実際はもっと裾野に
    /// > 広がっていくはずです。
    ///
    /// 以前はスナップショットの<b>溶岩の軌跡そのもの</b>を経路にしていた。溶岩と同じ
    /// 谷を下るのは正しいが、**火砕流は溶岩の幅では流れない** ——
    /// 火砕流（正しくは火砕サージ／密度流）は重い雲であって流体の筋ではないので、
    /// 下るにつれて<b>横へ広がり、裾野いっぱいに扇を作る</b>。地形に完全には従わず、
    /// **源に近いところでは尾根を越えて乗り越えていく**。
    ///
    /// いまはこの型が扇そのものを組む:
    ///
    /// <code>
    /// 舌(lobe) を <see cref="LobeCount"/> 本、火口のまわりに等間隔＋ゆらぎで配る
    /// 各舌は火口から <see cref="ReachMetres"/> まで放射状に下る
    /// 舌の幅は下るほど広がる（<see cref="HalfWidthMetres"/>。裾で最大 300 m ＝ 直径 600 m）
    /// 谷（＝溶岩が下った向き）へは**裾へ行くほど**引かれる（<see cref="ChannelPullAt"/>）
    ///   → 源の近くでは尾根を越え、裾では谷筋に集まる。これが「部分的に地形に従う」
    /// </code>
    ///
    /// 溶岩の軌跡は<b>「谷がどこにあるか」を知る唯一の手がかり</b>としてだけ使う
    /// （方位を 1 本ずつ取り出す）。経路そのものには使わない。
    ///
    /// ── 密度は面積で正規化する（**ここを外すと粒子が溢れる**）───────────────
    ///
    /// ベジェ帯の粒子数は <c>2 × halfWidth × 経路長 × pps</c> である（IL 実測 §B-5）。
    /// 半幅を 90 m から <see cref="HalfWidthMaxMetres"/> へ広げ、
    /// 本数を 2 から <see cref="LobeCount"/> へ増やすと、
    /// 素朴には**十数倍**の粒子を撃つことになる。<see cref="Magnitude"/> は帯の面積で
    /// 割るので、<b>扇ぜんぶで従来の 2 本ぶんと同じ量</b>に収まる
    /// （<see cref="EruptionColumn"/> の噴煙柱と同じ考え方）。
    ///
    /// ── サージ（突進）の作り方 ───────────────────────────────
    ///
    /// 帯の頭が火口から舌の端まで一定の速さで走り、抜け切ったら火口へ戻ってやり直す。
    /// 1 周の長さは <c>(経路長 + 帯の長さ) / 速さ</c> 秒。帯が経路からはみ出したぶんは
    /// <see cref="Magnitude"/> が薄くするので、端で唐突に消えない。
    /// **舌ごとに位相をずらす**（<see cref="LobePhaseSeconds"/>）ので、舌が
    /// 隊列を組んで同時に走ることはない。
    ///
    /// 時計は呼び出し側が積む。**フレーム番号を混ぜないこと**（この型は時計を持たない）。
    /// </summary>
    public static class PyroclasticSurge
    {
        /// <summary>扇を成す舌の本数。**費用の上限そのもの**（1 本 ＝ <c>RenderEffect</c> 1 回）。</summary>
        public const int LobeCount = 6;

        /// <summary>帯の長さ（m）。頭から尾まで。</summary>
        public const float BandLengthMetres = 260f;

        /// <summary>帯の頭が進む速さ（m/秒）。**⑤が決めた演出値。**</summary>
        public const float HeadSpeedMetresPerSecond = 95f;

        /// <summary>
        /// 粒子そのものを下り方向へ押す速さ（m/秒）。
        /// <b><see cref="HeadSpeedMetresPerSecond"/> とは別物である。</b>
        /// あちらは帯（湧かす場所）が経路を下る速さ、こちらは湧いた 1 粒が飛ぶ速さで、
        /// 同じ値にすると粒子が帯の外へ置き去りになって尾を引く。
        /// </summary>
        public const float PushMetresPerSecond = 30f;

        /// <summary>舌が届く距離（山の半径に対する比）の下限（＝噴出が弱いとき）。</summary>
        public const float ReachBaseFraction = 0.55f;

        /// <summary>強さで足される到達距離（同上）。1 なら裾まで届く。</summary>
        public const float ReachGainFraction = 0.45f;

        /// <summary>帯の半幅の下限（m）。火口のすぐそば。</summary>
        public const float HalfWidthBaseMetres = 45f;

        /// <summary>1 km 下るごとに広がる半幅（m）。**指摘⑤で 40 → 230 にした。**</summary>
        public const float HalfWidthPerKilometre = 230f;

        /// <summary>帯の半幅の上限（m）。**指摘⑤で 90 → 300 にした。**</summary>
        public const float HalfWidthMaxMetres = 300f;

        /// <summary>帯の密度の下限（強さ 0 のとき）。</summary>
        public const float MagnitudeMin = 10f;

        /// <summary>帯の密度の上限（強さ 1 のとき）。</summary>
        public const float MagnitudeMax = 46f;

        /// <summary>
        /// 密度を正規化する基準の面積（m²）。**従来の帯 2 本ぶん**
        /// （<c>2 × 半幅 90 m</c>（＝全幅）× 帯の長さ 260 m × 2 本）である。
        /// 先頭の 2 は「表と裏」ではなく、<see cref="Magnitude"/> の
        /// <c>2f * half</c> と同じ**半幅から全幅への換算**である。
        /// 扇の帯の面積がこれを超えたぶんだけ密度を薄める ——
        /// でないと本数と幅を増やした瞬間に粒子が十数倍になる。
        /// </summary>
        public const float ReferenceAreaSquareMetres = 2f * 90f * 260f * 2f;

        /// <summary>
        /// 舌の方位のゆらぎ（等間隔に対する比）。0 だと**風車の羽根**に見える。
        /// 隣と入れ替わらないよう ±0.5 未満に抑える。
        /// </summary>
        public const float AzimuthJitter = 0.34f;

        /// <summary>
        /// 谷（溶岩が下った向き）へ引かれる最大の割合。**裾での値**である。
        /// 1 にすると「溶岩の上だけを流れる」——それが指摘⑤で直した形そのものなので、
        /// **半分より上げないこと。**
        /// </summary>
        public const float ChannelPullMax = 0.5f;

        /// <summary>
        /// 谷へ引かれる強さが距離とともに増える指数。大きいほど
        /// 「源の近くでは尾根を越え、裾で谷に集まる」がはっきりする。
        /// </summary>
        public const float ChannelPullPower = 1.6f;

        /// <summary>
        /// 舌が横へふくらむ量（半幅に対する比）。**まっすぐな放射線は人工物に見える。**
        /// </summary>
        public const float BowFactor = 0.55f;

        /// <summary>
        /// これより短い舌には帯を出さない（m）。
        /// 火口のすぐそばの点線に帯を巻くと、山頂に灰の球が乗る。
        /// </summary>
        public const float MinPathMetres = 80f;

        /// <summary>
        /// 舌 <paramref name="index"/> 本目の到達距離（m）。
        /// **舌ごとに少し違う**（全部同じだと扇の縁が真円になる）。
        /// </summary>
        public static float ReachMetres(float radiusMetres, float intensityUnit,
                                        uint seed, int index)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 0f;

            float unit = Clamp01(intensityUnit);
            float reach = radiusMetres * (ReachBaseFraction + ReachGainFraction * unit);

            // ±15 % のばらつき。種と番号だけで決まる（フレーム番号は混ぜない）。
            float jitter = 0.85f + 0.3f * DeterministicRandom.Unit(seed, (uint)(0x5A00 + index));
            float value = reach * jitter;
            return IsBad(value) || value < 0f ? 0f : value;
        }

        /// <summary>
        /// 舌 <paramref name="index"/> 本目の**火口を出るときの方位**（ラジアン）。
        /// 等間隔 ＋ <see cref="DeterministicRandom"/> のゆらぎで、地形は 1 つも見ない ——
        /// <b>源の近くでは尾根を越える</b>のがこの型の主張だからである。
        /// </summary>
        public static float LobeAzimuth(uint seed, int index, int count)
        {
            int n = count <= 0 ? 1 : count;
            int i = ((index % n) + n) % n;

            double even = 2.0 * Math.PI * i / n;
            float jitter = DeterministicRandom.Unit(seed, (uint)(0x6B00 + i)) - 0.5f;
            return (float)(even + jitter * AzimuthJitter * 2.0 * Math.PI / n);
        }

        /// <summary>
        /// 火口からの距離の割合 <paramref name="t"/> において、谷へどれだけ引かれるか <c>[0,1]</c>。
        /// **源では 0（尾根を越える）、裾で <see cref="ChannelPullMax"/>**。
        /// </summary>
        public static float ChannelPullAt(float t)
        {
            if (IsBad(t) || t <= 0f) return 0f;
            float u = t > 1f ? 1f : t;
            return ChannelPullMax * (float)Math.Pow(u, ChannelPullPower);
        }

        /// <summary>
        /// <paramref name="bearings"/> のうち <paramref name="azimuth"/> にいちばん近い向き
        /// （ラジアン）。1 本も無ければ <paramref name="found"/> が false になり、
        /// 戻り値は <paramref name="azimuth"/> そのもの ——
        /// **溶岩が 1 本も流れていない山でも扇は出る**（谷に引かれないだけ）。
        /// </summary>
        public static float NearestChannel(float azimuth, float[] bearings, int count,
                                           out bool found)
        {
            found = false;
            if (bearings == null || count <= 0) return azimuth;

            int limit = count > bearings.Length ? bearings.Length : count;
            float best = azimuth;
            float bestDelta = float.MaxValue;

            for (int i = 0; i < limit; i++)
            {
                float b = bearings[i];
                if (IsBad(b)) continue;

                float delta = SignedDelta(azimuth, b);
                float abs = delta < 0f ? -delta : delta;
                if (abs < bestDelta)
                {
                    bestDelta = abs;
                    best = azimuth + delta;
                    found = true;
                }
            }

            return best;
        }

        /// <summary>
        /// 舌 1 本ぶんの帯を 4 点（ベジェの制御点）で返す。
        /// <paramref name="a"/> が尾、<paramref name="d"/> が頭である。
        ///
        /// 帯が舌から完全に外れている・舌が短すぎるときは <c>false</c> を返し、
        /// 出力は全て火口になる（**「それらしい」座標を作らない**）。
        /// </summary>
        public static bool TryLobe(Vec2 vent, float baseAzimuth, float channelAzimuth,
                                   float reachMetres, float headMetres,
                                   out Vec2 a, out Vec2 b, out Vec2 c, out Vec2 d)
        {
            a = b = c = d = vent;

            if (IsBad(vent.X) || IsBad(vent.Z)) return false;
            if (IsBad(reachMetres) || reachMetres < MinPathMetres) return false;
            if (OverlapMetres(headMetres, reachMetres) <= 0f) return false;

            float head = IsBad(headMetres) || headMetres < 0f ? 0f : headMetres;
            if (head > reachMetres) head = reachMetres;

            float tail = head - BandLengthMetres;
            if (tail < 0f) tail = 0f;

            float span = head - tail;
            float delta = SignedDelta(baseAzimuth, channelAzimuth);

            a = PointAt(vent, baseAzimuth, delta, reachMetres, tail);
            b = PointAt(vent, baseAzimuth, delta, reachMetres, tail + span / 3f);
            c = PointAt(vent, baseAzimuth, delta, reachMetres, tail + span * 2f / 3f);
            d = PointAt(vent, baseAzimuth, delta, reachMetres, head);
            return true;
        }

        /// <summary>
        /// 舌の上、火口から <paramref name="distanceMetres"/> の点。
        /// **裾へ行くほど谷の向きへ回り込む**（<see cref="ChannelPullAt"/>）。
        /// </summary>
        public static Vec2 PointAt(Vec2 vent, float baseAzimuth, float channelDeltaRadians,
                                   float reachMetres, float distanceMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return vent;
            if (IsBad(reachMetres) || reachMetres <= 0f) return vent;

            float t = distanceMetres / reachMetres;
            if (t > 1f) t = 1f;

            float delta = IsBad(channelDeltaRadians) ? 0f : channelDeltaRadians;
            double angle = baseAzimuth + delta * ChannelPullAt(t);

            // 横へのふくらみ。**まっすぐな放射線は人工物に見える。**
            float bow = BowFactor * HalfWidthMetres(distanceMetres)
                        * (float)Math.Sin(Math.PI * t);

            double dirX = Math.Cos(angle);
            double dirZ = Math.Sin(angle);

            float x = vent.X + (float)(dirX * distanceMetres - dirZ * bow);
            float z = vent.Z + (float)(dirZ * distanceMetres + dirX * bow);
            if (IsBad(x) || IsBad(z)) return vent;
            return new Vec2(x, z);
        }

        /// <summary>1 周の秒数。経路が短いときも 0 を返さない（0 除算を外へ出さない）。</summary>
        public static float CycleSeconds(float pathLengthMetres)
        {
            float path = IsBad(pathLengthMetres) || pathLengthMetres < 0f ? 0f : pathLengthMetres;
            float seconds = (path + BandLengthMetres) / HeadSpeedMetresPerSecond;
            return seconds < 0.1f ? 0.1f : seconds;
        }

        /// <summary>
        /// 舌ごとの位相のずらし（秒）。**5 本が隊列を組んで走らないため**にある。
        /// </summary>
        public static float LobePhaseSeconds(int index, int count, float pathLengthMetres)
        {
            int n = count <= 0 ? 1 : count;
            int i = ((index % n) + n) % n;
            return CycleSeconds(pathLengthMetres) * i / n;
        }

        /// <summary>
        /// 帯の頭が今どこまで来ているか（火口からの距離 m）。
        /// <paramref name="clockSeconds"/> は呼び出し側が積む時計。
        /// </summary>
        public static float HeadMetres(float clockSeconds, float pathLengthMetres)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;

            float cycle = CycleSeconds(pathLengthMetres);
            float t = clockSeconds - (float)Math.Floor(clockSeconds / cycle) * cycle;
            if (IsBad(t) || t < 0f) t = 0f;

            float head = t * HeadSpeedMetresPerSecond;
            return IsBad(head) || head < 0f ? 0f : head;
        }

        /// <summary>
        /// 帯の密度。**経路に載っている割合で薄める**ので、
        /// 走り始めと抜け際に唐突な出現・消失が起きない。
        /// 経路が <see cref="MinPathMetres"/> に満たなければ 0（＝出さない）。
        ///
        /// ★★ **帯の面積で正規化する。** ベジェ帯の粒子数は
        /// <c>2 × halfWidth × 経路長 × pps</c> なので（IL 実測 §B-5）、
        /// 幅と本数を増やしたぶんをここで割り戻さないと粒子が十数倍になる。
        /// </summary>
        public static float Magnitude(float intensityUnit, float headMetres,
                                      float pathLengthMetres, float halfWidthMetres)
        {
            if (IsBad(pathLengthMetres) || pathLengthMetres < MinPathMetres) return 0f;

            float overlap = OverlapMetres(headMetres, pathLengthMetres);
            if (overlap <= 0f) return 0f;

            float share = overlap / BandLengthMetres;
            if (share > 1f) share = 1f;

            float half = IsBad(halfWidthMetres) || halfWidthMetres <= 0f
                ? HalfWidthBaseMetres : halfWidthMetres;
            float area = 2f * half * BandLengthMetres * LobeCount;
            if (!(area > 0f)) return 0f;

            float scale = ReferenceAreaSquareMetres / area;
            if (scale > 1f) scale = 1f;   // 面積が基準より小さくても濃くはしない

            float u = Clamp01(intensityUnit);
            float m = (MagnitudeMin + (MagnitudeMax - MagnitudeMin) * u) * share * scale;
            return IsBad(m) || m < 0f ? 0f : m;
        }

        /// <summary>帯の半幅（m）。**下るほど広がる**が、必ず頭打ちになる。</summary>
        public static float HalfWidthMetres(float headMetres)
        {
            float head = IsBad(headMetres) || headMetres < 0f ? 0f : headMetres;
            float w = HalfWidthBaseMetres + head / 1000f * HalfWidthPerKilometre;
            if (IsBad(w)) return HalfWidthBaseMetres;
            return w > HalfWidthMaxMetres ? HalfWidthMaxMetres : w;
        }

        /// <summary>
        /// 折れ線（＝溶岩の軌跡）の**全体の向き**（ラジアン）。谷の手がかりとして使う。
        /// 点が足りない・座標が壊れているときは <c>false</c>。
        /// </summary>
        public static bool TryBearing(Vec2[] points, int start, int count, Vec2 vent,
                                      out float bearing)
        {
            bearing = 0f;
            if (points == null || count <= 0 || start < 0 || start >= points.Length) return false;

            int limit = count;
            if (start + limit > points.Length) limit = points.Length - start;
            if (limit < 2) return false;

            // いちばん遠くまで届いた点を向きにする（途中の蛇行に引きずられない）。
            float bestX = 0f, bestZ = 0f, best = 0f;
            for (int i = 0; i < limit; i++)
            {
                Vec2 p = points[start + i];
                if (IsBad(p.X) || IsBad(p.Z)) break;

                float dx = p.X - vent.X;
                float dz = p.Z - vent.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 > best) { best = d2; bestX = dx; bestZ = dz; }
            }

            if (!(best > 1f)) return false;
            bearing = (float)Math.Atan2(bestZ, bestX);
            return true;
        }

        /// <summary>帯のうち経路に載っている長さ（m）。</summary>
        private static float OverlapMetres(float headMetres, float pathLengthMetres)
        {
            float head = IsBad(headMetres) || headMetres < 0f ? 0f : headMetres;
            float path = IsBad(pathLengthMetres) || pathLengthMetres < 0f ? 0f : pathLengthMetres;

            float hi = head < path ? head : path;
            float lo = head - BandLengthMetres;
            if (lo < 0f) lo = 0f;

            float overlap = hi - lo;
            return IsBad(overlap) || overlap < 0f ? 0f : overlap;
        }

        /// <summary>2 つの方位の差を <c>[-π, π]</c> で返す（**巻き戻りを跨いでも近いほう**）。</summary>
        private static float SignedDelta(float from, float to)
        {
            if (IsBad(from) || IsBad(to)) return 0f;

            double d = to - from;
            double twoPi = 2.0 * Math.PI;
            d -= twoPi * Math.Floor((d + Math.PI) / twoPi);
            return (float)d;
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
