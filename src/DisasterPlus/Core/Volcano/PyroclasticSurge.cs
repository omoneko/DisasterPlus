using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 「火砕流」に見せる<b>土煙の帯</b>の幾何。**エンジン非依存の純関数だけ。**
    ///
    /// ── ★★ これは火砕流ではない。名乗り方を間違えないこと ─────────────
    ///
    /// バニラに火砕流は無い。出荷アセットの <c>EffectInfo</c> を全数（277 個）調べても、
    /// 「地面を這って高速で流れ下る濃密な雲」に当たるものは基本ゲームにも DLC にも
    /// 存在しない。⑤が出すのは <b>建物が崩れるときの粉塵</b>
    /// （<c>Collapse Particles</c>）を、溶岩が通る斜面の経路に沿ったベジェ帯へ湧かせ、
    /// 引数の <c>velocity</c> で下り方向へ押したものである。
    /// 見た目は「斜面を駆け下りる灰色の土煙の帯」であって、火砕流の再現ではない。
    /// パネルと設計書は**そう書く**。
    ///
    /// ── 経路をどこから取るか ────────────────────────────────
    ///
    /// 自分で斜面を辿らない。<c>VolcanoLava</c> が既に下り方向へ辿った軌跡
    /// （スナップショットの不変配列）をそのまま経路にする。理由は 2 つある:
    ///
    /// <list type="number">
    /// <item>地形の読み取りを 1 か所（sim 側）に閉じたままにできる。</item>
    /// <item>火砕流も溶岩も同じ谷を下るので、**経路が一致しているほうが正しい**。</item>
    /// </list>
    ///
    /// ── サージ（突進）の作り方 ───────────────────────────────
    ///
    /// 帯の頭が火口から経路の端まで一定の速さで走り、抜け切ったら火口へ戻ってやり直す。
    /// 1 周の長さは <c>(経路長 + 帯の長さ) / 速さ</c> 秒。帯が経路からはみ出したぶんは
    /// <see cref="Magnitude"/> が薄くするので、端で唐突に消えない。
    ///
    /// 時計は呼び出し側が積む。**フレーム番号を混ぜないこと**（この型は時計を持たない）。
    /// </summary>
    public static class PyroclasticSurge
    {
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

        /// <summary>帯の半幅の下限（m）。</summary>
        public const float HalfWidthBaseMetres = 26f;

        /// <summary>1 km 下るごとに広がる半幅（m）。</summary>
        public const float HalfWidthPerKilometre = 40f;

        /// <summary>帯の半幅の上限（m）。</summary>
        public const float HalfWidthMaxMetres = 90f;

        /// <summary>帯の密度の下限（強さ 0 のとき）。</summary>
        public const float MagnitudeMin = 10f;

        /// <summary>帯の密度の上限（強さ 1 のとき）。</summary>
        public const float MagnitudeMax = 46f;

        /// <summary>
        /// これより短い経路には帯を出さない（m）。
        /// 火口のすぐそばで湧いた 1 本の点線に帯を巻くと、山頂に灰の球が乗る。
        /// </summary>
        public const float MinPathMetres = 80f;

        /// <summary>経路を成すのに要る最小の点数。</summary>
        public const int MinPoints = 3;

        /// <summary>
        /// 折れ線の全長（m）。点が足りない・座標が壊れているときは 0 を返す。
        /// </summary>
        public static float PathLengthMetres(Vec2[] points, int start, int count)
        {
            int n = UsableCount(points, start, count);
            if (n < 2) return 0f;

            float total = 0f;
            for (int i = start + 1; i < start + n; i++)
            {
                total += Distance(points[i - 1], points[i]);
            }
            return IsBad(total) || total < 0f ? 0f : total;
        }

        /// <summary>1 周の秒数。経路が短いときも 0 を返さない（0 除算を外へ出さない）。</summary>
        public static float CycleSeconds(float pathLengthMetres)
        {
            float path = IsBad(pathLengthMetres) || pathLengthMetres < 0f ? 0f : pathLengthMetres;
            float seconds = (path + BandLengthMetres) / HeadSpeedMetresPerSecond;
            return seconds < 0.1f ? 0.1f : seconds;
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
        /// </summary>
        public static float Magnitude(float intensityUnit, float headMetres,
                                      float pathLengthMetres)
        {
            if (IsBad(pathLengthMetres) || pathLengthMetres < MinPathMetres) return 0f;

            float overlap = OverlapMetres(headMetres, pathLengthMetres);
            if (overlap <= 0f) return 0f;

            float share = overlap / BandLengthMetres;
            if (share > 1f) share = 1f;

            float u = Clamp01(intensityUnit);
            return (MagnitudeMin + (MagnitudeMax - MagnitudeMin) * u) * share;
        }

        /// <summary>帯の半幅（m）。下るほど広がるが、必ず頭打ちになる。</summary>
        public static float HalfWidthMetres(float headMetres)
        {
            float head = IsBad(headMetres) || headMetres < 0f ? 0f : headMetres;
            float w = HalfWidthBaseMetres + head / 1000f * HalfWidthPerKilometre;
            if (IsBad(w)) return HalfWidthBaseMetres;
            return w > HalfWidthMaxMetres ? HalfWidthMaxMetres : w;
        }

        /// <summary>
        /// 帯を経路に沿った 4 点（ベジェの制御点）として取り出す。
        /// <paramref name="a"/> が尾、<paramref name="d"/> が頭。
        ///
        /// 帯が経路から完全に外れているとき、点が足りないときは
        /// <c>false</c> を返し、出力は全て経路の先頭になる（**「それらしい」座標を作らない**）。
        /// </summary>
        public static bool TryBand(Vec2[] points, int start, int count, float headMetres,
                                   out Vec2 a, out Vec2 b, out Vec2 c, out Vec2 d)
        {
            a = b = c = d = new Vec2(0f, 0f);

            int n = UsableCount(points, start, count);
            if (n < MinPoints) return false;

            float path = PathLengthMetres(points, start, count);
            if (path < MinPathMetres) return false;

            if (OverlapMetres(headMetres, path) <= 0f) return false;

            float head = headMetres;
            if (IsBad(head) || head < 0f) head = 0f;

            float tail = head - BandLengthMetres;
            if (tail < 0f) tail = 0f;
            if (head > path) head = path;
            if (tail > head) tail = head;

            float span = head - tail;

            return TryPointAt(points, start, count, tail, out a)
                   && TryPointAt(points, start, count, tail + span / 3f, out b)
                   && TryPointAt(points, start, count, tail + span * 2f / 3f, out c)
                   && TryPointAt(points, start, count, head, out d);
        }

        /// <summary>
        /// 折れ線を先頭から <paramref name="distanceMetres"/> だけ辿った点。
        /// 範囲外は端に丸める。点が足りなければ <c>false</c>。
        /// </summary>
        public static bool TryPointAt(Vec2[] points, int start, int count,
                                      float distanceMetres, out Vec2 point)
        {
            point = new Vec2(0f, 0f);

            int n = UsableCount(points, start, count);
            if (n < 1) return false;

            point = points[start];
            if (n < 2) return true;

            float want = IsBad(distanceMetres) || distanceMetres < 0f ? 0f : distanceMetres;

            float walked = 0f;
            for (int i = start + 1; i < start + n; i++)
            {
                Vec2 p0 = points[i - 1];
                Vec2 p1 = points[i];
                float seg = Distance(p0, p1);
                if (seg <= 0f) continue;

                if (walked + seg >= want)
                {
                    float t = (want - walked) / seg;
                    if (t < 0f) t = 0f;
                    if (t > 1f) t = 1f;
                    point = new Vec2(p0.X + (p1.X - p0.X) * t, p0.Z + (p1.Z - p0.Z) * t);
                    return true;
                }

                walked += seg;
                point = p1;
            }

            return true;
        }

        /// <summary>
        /// この配列のこの区間に、座標として使える点が何個並んでいるか。
        /// **NaN が 1 つでも出たらそこで打ち切る**（壊れた点から先を信じない）。
        /// </summary>
        public static int UsableCount(Vec2[] points, int start, int count)
        {
            if (points == null || count <= 0 || start < 0 || start >= points.Length) return 0;

            int limit = count;
            if (start + limit > points.Length) limit = points.Length - start;

            for (int i = 0; i < limit; i++)
            {
                Vec2 p = points[start + i];
                if (IsBad(p.X) || IsBad(p.Z)) return i;
            }
            return limit;
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

        private static float Distance(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            return IsBad(d) ? 0f : d;
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
