using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.ForecastMapPreview
{
    /// <summary>
    /// <b>予報パネルが地図に描くものを、ゲームを起動せずに描く。</b>
    ///
    /// ── なぜ要るのか ─────────────────────────────────────────
    ///
    /// 所有者の規律:「視覚変更はオフラインで描画・計測して自分で確認してから
    /// 実機テストを頼む」。<c>ForecastOverlay</c> は
    /// <c>OverlayEffect.DrawCircle</c> / <c>DrawQuad</c> を呼ぶだけなので、
    /// <b>座標を決めているのは全部 Core の式</b>である。その式をここで同じように
    /// 走らせて絵にすれば、<b>実機を起動する前に幾何が正しいか分かる</b>。
    ///
    /// 確かめたいのは 3 つ:
    ///
    /// <list type="number">
    /// <item><b>進路がマップの外から入り、クリック地点を通り、外へ抜ける</b>こと</item>
    /// <item><b>暴風域の円が中心に乗っている</b>こと</item>
    /// <item><b>風の分布に眼がある</b>こと（中心が薄く、壁雲が濃い）</item>
    /// </list>
    ///
    /// ★ ここで描けないのは「実際に画面へ出るか」だけである。それは
    ///   <c>OverlayEffect</c> の仕事で、②の震度オーバーレイが同じ経路で
    ///   既に出ていることが分かっている。
    /// </summary>
    internal static class Program
    {
        private const int Size = 900;

        /// <summary>マップの半辺（m）。<c>TyphoonTrack.MapHalfExtent</c> と同じ。</summary>
        private const float Half = 8640f;

        /// <summary>25 タイル全域が入るよう、マップより少し広く描く。</summary>
        private const float ViewHalf = Half * 1.25f;

        private static void Main(string[] args)
        {
            uint lifetime = 8192u;
            byte intensity = 150;
            float prefabRadius = 1000f;

            var origin = new Vec2(2000f, -1500f);
            uint seed = 12345u;

            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                if (args[i] == "--seed") seed = uint.Parse(args[i + 1]);
                else if (args[i] == "--intensity") intensity = byte.Parse(args[i + 1]);
                else if (args[i] == "--prefab") prefabRadius = float.Parse(args[i + 1]);
            }

            float speed = TyphoonTrack.SpeedFor(lifetime);
            uint approach = TyphoonTrack.ApproachFramesFor(origin, seed, speed, lifetime);
            var plan = new TyphoonTrackPlan(origin, seed, speed, approach, lifetime);

            // 「今」はクリック地点に着いた瞬間 —— いちばん見たい絵になる。
            uint now = approach;
            Vec2 centre = plan.CentreAt(now);

            float storm = TyphoonProfile.StormRadiusOf(intensity, prefabRadius);
            float gale = TyphoonProfile.GaleRadiusOf(intensity, prefabRadius);

            var rgb = new byte[Size * Size * 3];
            Background(rgb);

            DrawWindField(rgb, centre, gale, intensity, prefabRadius);
            DrawCircle(rgb, centre, gale, 250, 158, 51);
            DrawCircle(rgb, centre, storm, 255, 77, 51);
            DrawTrack(rgb, plan, now);
            DrawMapEdge(rgb);

            Png.Write("forecast-map.png", Size, Size, rgb);

            Console.WriteLine("seed=" + seed + " intensity=" + intensity
                              + " prefabRadius=" + prefabRadius.ToString("F0"));
            Console.WriteLine("speed=" + speed.ToString("F3")
                              + " m/frame  approach=" + approach + " frames of " + lifetime);
            Console.WriteLine("storm=" + storm.ToString("F0")
                              + " m  gale=" + gale.ToString("F0") + " m");

            var start = plan.CentreAt(0u);
            var end = plan.CentreAt(lifetime);
            Console.WriteLine("start (" + start.X.ToString("F0") + "," + start.Z.ToString("F0")
                              + ") inside=" + TyphoonTrack.IsInsideMap(start));
            Console.WriteLine("at click (" + centre.X.ToString("F0") + ","
                              + centre.Z.ToString("F0") + ")  wanted ("
                              + origin.X.ToString("F0") + "," + origin.Z.ToString("F0") + ")");
            Console.WriteLine("end (" + end.X.ToString("F0") + "," + end.Z.ToString("F0")
                              + ") inside=" + TyphoonTrack.IsInsideMap(end));

            // 眼が本当にあるか、数字でも言う（絵だけだと薄い色を見落とす）。
            Console.WriteLine("wind at centre=" + TyphoonProfile.WindAt(0f, intensity, prefabRadius).ToString("F2")
                              + "  at eyewall=" + TyphoonProfile.WindAt(storm * 0.6f, intensity, prefabRadius).ToString("F2")
                              + "  at gale edge=" + TyphoonProfile.WindAt(gale * 0.99f, intensity, prefabRadius).ToString("F2"));
        }

        private static void Background(byte[] rgb)
        {
            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 18; rgb[i + 1] = 22; rgb[i + 2] = 28;
            }
        }

        /// <summary>マップの範囲を薄い枠で。進路が外から入ることを目で確かめるため。</summary>
        private static void DrawMapEdge(byte[] rgb)
        {
            int a = ToPixel(-Half), b = ToPixel(Half);
            for (int p = a; p <= b; p++)
            {
                Plot(rgb, p, a, 90, 100, 115);
                Plot(rgb, p, b, 90, 100, 115);
                Plot(rgb, a, p, 90, 100, 115);
                Plot(rgb, b, p, 90, 100, 115);
            }
        }

        /// <summary>
        /// 風の分布。<c>ForecastOverlay.DrawWind</c> と<b>同じ格子・同じ関数</b>。
        /// </summary>
        private static void DrawWindField(byte[] rgb, Vec2 centre, float gale,
                                          byte intensity, float prefabRadius)
        {
            const int side = 21;
            if (!(gale > 0f)) return;

            float step = gale * 2f / (side - 1);
            float dot = gale * 2f * 0.055f;

            for (int gz = 0; gz < side; gz++)
            {
                float z = centre.Z - gale + gz * step;
                for (int gx = 0; gx < side; gx++)
                {
                    float x = centre.X - gale + gx * step;
                    float dx = x - centre.X, dz = z - centre.Z;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d > gale) continue;

                    float wind = TyphoonProfile.WindAt(d, intensity, prefabRadius);
                    if (wind <= 0.02f) continue;

                    byte r, g, b;
                    WindColour(wind, out r, out g, out b);
                    FillDisc(rgb, new Vec2(x, z), dot * 0.5f, r, g, b);
                }
            }
        }

        /// <summary><c>ForecastOverlay.WindColourOf</c> と同じ配色。</summary>
        private static void WindColour(float wind, out byte r, out byte g, out byte b)
        {
            if (wind > 1f) wind = 1f;
            float fr, fg, fb;
            if (wind < 0.5f)
            {
                float t = wind * 2f;
                fr = 0.25f + 0.75f * t;
                fg = 0.55f + 0.30f * t;
                fb = 1f - 0.80f * t;
            }
            else
            {
                float t = (wind - 0.5f) * 2f;
                fr = 1f;
                fg = 0.85f - 0.60f * t;
                fb = 0.20f - 0.05f * t;
            }
            r = (byte)(fr * 255f); g = (byte)(fg * 255f); b = (byte)(fb * 255f);
        }

        private static void DrawTrack(byte[] rgb, TyphoonTrackPlan plan, uint now)
        {
            var points = new Vec2[64];
            int n = plan.Sample(points, points.Length, 0u, plan.TotalFrames);

            for (int i = 1; i < n; i++)
            {
                uint at = (uint)((long)plan.TotalFrames * i / (n - 1));
                bool past = at <= now;

                byte r = past ? (byte)150 : (byte)255;
                byte g = past ? (byte)150 : (byte)217;
                byte b = past ? (byte)150 : (byte)64;

                Line(rgb, points[i - 1], points[i], r, g, b);

                if (!past && i % 8 == 0) FillDisc(rgb, points[i], 60f, 255, 184, 38);
            }
        }

        private static void DrawCircle(byte[] rgb, Vec2 centre, float radius,
                                       byte r, byte g, byte b)
        {
            if (!(radius > 0f)) return;
            const int steps = 360;
            for (int i = 0; i < steps; i++)
            {
                double a0 = 2.0 * Math.PI * i / steps;
                double a1 = 2.0 * Math.PI * (i + 1) / steps;
                Line(rgb,
                     new Vec2(centre.X + (float)Math.Cos(a0) * radius,
                              centre.Z + (float)Math.Sin(a0) * radius),
                     new Vec2(centre.X + (float)Math.Cos(a1) * radius,
                              centre.Z + (float)Math.Sin(a1) * radius),
                     r, g, b);
            }
        }

        private static void FillDisc(byte[] rgb, Vec2 at, float radius,
                                     byte r, byte g, byte b)
        {
            int cx = ToPixel(at.X), cz = ToPixel(at.Z);
            int rp = (int)(radius / (ViewHalf * 2f) * Size);
            if (rp < 1) rp = 1;

            for (int dz = -rp; dz <= rp; dz++)
            {
                for (int dx = -rp; dx <= rp; dx++)
                {
                    if (dx * dx + dz * dz > rp * rp) continue;
                    Plot(rgb, cx + dx, cz + dz, r, g, b);
                }
            }
        }

        private static void Line(byte[] rgb, Vec2 a, Vec2 b, byte r, byte g, byte bl)
        {
            int x0 = ToPixel(a.X), z0 = ToPixel(a.Z);
            int x1 = ToPixel(b.X), z1 = ToPixel(b.Z);

            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(z1 - z0));
            if (steps == 0) { Plot(rgb, x0, z0, r, g, bl); return; }

            for (int i = 0; i <= steps; i++)
            {
                int x = x0 + (x1 - x0) * i / steps;
                int z = z0 + (z1 - z0) * i / steps;
                // 少し太らせる（1 px だと縮小して見えない）。
                for (int oz = -1; oz <= 1; oz++)
                    for (int ox = -1; ox <= 1; ox++)
                        Plot(rgb, x + ox, z + oz, r, g, bl);
            }
        }

        private static int ToPixel(float world)
        {
            return (int)((world + ViewHalf) / (ViewHalf * 2f) * Size);
        }

        private static void Plot(byte[] rgb, int x, int z, byte r, byte g, byte b)
        {
            if (x < 0 || x >= Size || z < 0 || z >= Size) return;
            // 北が上になるよう z を反転する。
            int i = ((Size - 1 - z) * Size + x) * 3;
            rgb[i] = r; rgb[i + 1] = g; rgb[i + 2] = b;
        }
    }
}
