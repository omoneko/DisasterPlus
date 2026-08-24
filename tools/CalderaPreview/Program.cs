using System;
using System.IO;
using DisasterPlus.Core.Volcano;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.CalderaPreview
{
    /// <summary>
    /// 破局噴火の 4 つの段の地形断面を、**ゲームを起動せずに**描いて確かめる
    /// （2026-08-22、所有者の指摘「カルデラ形成時は、山体が大きく落ち込んで
    /// 大爆発するんじゃないでしょうか…？」）。
    ///
    /// このプロジェクトの決まり「見た目の変更は自分でオフラインに描画・計測してから
    /// 実機テストを頼む」のための道具である。<b>Core の実物をそのままコンパイルして
    /// 呼ぶ</b>ので、書き直した近似ではない。
    ///
    ///   dotnet run --project tools/CalderaPreview -- docs/images/volcano
    /// </summary>
    internal static class Program
    {
        // 成層火山・スライダー上端（表示 25.5）の実寸。VolcanoState が
        // 半径をカルデラ側から割り戻すので、円錐はこの大きさになる。
        private const float ConeRadius = 2928f;
        private const float ConeHeight = 1000f;
        private const float Ground = 120f;

        private const int Width = 1100;
        private const int Height = 520;

        /// <summary>横に見る範囲（m、片側）。カルデラの外まで入れる。</summary>
        private const float ViewHalfWidth = 7000f;

        private const uint Seed = 20260822u;

        /// <summary>海面（m）。<c>WaterSimulation.DEFAULT_SEA_LEVEL</c> の実測値。</summary>
        private const float SeaLevel = 40f;

        private static readonly VolcanoRelief Flat =
            VolcanoRelief.For(VolcanoForm.Strato, Seed, 0f);

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(dir);

            float calderaR = SuperEruption.CalderaRadiusMetres(ConeRadius);
            float depth = SuperEruption.CalderaDepthMetres(ConeHeight);
            float bulgeR = SuperEruption.InflationRadiusMetres(ConeRadius);
            float bulgeH = SuperEruption.InflationHeightMetres(ConeHeight);

            Console.WriteLine("cone     r=" + ConeRadius.ToString("F0")
                              + " h=" + ConeHeight.ToString("F0"));
            Console.WriteLine("bulge    r=" + bulgeR.ToString("F0")
                              + " +" + bulgeH.ToString("F0"));
            Console.WriteLine("caldera  r=" + calderaR.ToString("F0")
                              + " floor=" + (Ground - depth).ToString("F0")
                              + " (ground was " + Ground.ToString("F0")
                              + ", sea level " + SeaLevel.ToString("F0") + ")");

            // ★ 床が海面より上か下かを**必ず名乗る**。以前は必ず下だった
            //   （所有者の問い「必ず海抜より低くなる理由は何ですか？」）。
            float floor = Ground - depth;
            Console.WriteLine(floor >= SeaLevel
                ? "         the caldera floor stays ABOVE sea level (dry caldera)"
                : "         the caldera floor is " + (SeaLevel - floor).ToString("F0")
                  + " m below sea level (it will flood, like Santorini)");

            string path = Path.Combine(dir, "caldera-cross-section.png");
            Png.Write(path, Width, Height, Render(calderaR, depth, bulgeR, bulgeH));
            Console.WriteLine("wrote " + path);

            PrintProfile(calderaR, depth, bulgeR, bulgeH);
            return 0;
        }

        /// <summary>その段のあとの地面の高さ（m）。距離は中心から（符号なし）。</summary>
        private static float Stage1Cone(float d)
        {
            return Ground + VolcanoCrater.ProfileAt(Flat, d, 0f, ConeRadius, ConeHeight);
        }

        private static float Stage2Bulge(float d, float bulgeR, float bulgeH)
        {
            return Stage1Cone(d) + SuperEruption.InflationAt(d, bulgeR, bulgeH);
        }

        private static float Stage4Caldera(float d, float calderaR, float depth,
                                           float bulgeR, float bulgeH)
        {
            // ★ 陥没は「膨らんだあとの地面」から落ちる（実機と同じ順序）。
            float baseMetres = Stage2Bulge(d, bulgeR, bulgeH);

            // ★ 床は鉢だけではない —— 崩れた岩塊と中央火口丘が乗る。
            float offset = SuperEruption.CalderaFloorOffsetAt(d, 0f, calderaR, depth, Seed);

            // ★ 山体の外では**そのセルの本物の地面**が基準（元の地形を残す）。
            //   実機の VolcanoUplift.ReferenceGroundFor と同じ規則。
            float reference = ReferenceGround(d, baseMetres);

            return baseMetres + SuperEruption.FounderDropAt(offset, baseMetres, reference);
        }

        /// <summary>実機の <c>VolcanoUplift.ReferenceGroundFor</c> と同じ規則。</summary>
        private static float ReferenceGround(float distance, float baseMetres)
        {
            float band = ConeRadius * 0.25f;
            if (distance <= ConeRadius) return Ground;
            if (distance >= ConeRadius + band) return baseMetres;

            float t = (distance - ConeRadius) / band;
            float k = t * t * (3f - 2f * t);
            return Ground + (baseMetres - Ground) * k;
        }

        private static void PrintProfile(float calderaR, float depth,
                                         float bulgeR, float bulgeH)
        {
            Console.WriteLine();
            Console.WriteLine("   dist |   cone |  bulge | caldera");
            for (int i = 0; i <= 14; i++)
            {
                float d = ViewHalfWidth * i / 14f;
                Console.WriteLine(d.ToString("F0").PadLeft(7)
                                  + " |" + Stage1Cone(d).ToString("F0").PadLeft(7)
                                  + " |" + Stage2Bulge(d, bulgeR, bulgeH).ToString("F0").PadLeft(7)
                                  + " |" + Stage4Caldera(d, calderaR, depth, bulgeR, bulgeH)
                                               .ToString("F0").PadLeft(8));
            }
        }

        private static byte[] Render(float calderaR, float depth, float bulgeR, float bulgeH)
        {
            var rgb = new byte[Width * Height * 3];

            // 縦の見る範囲は「いちばん高いところ」と「床」から決める。
            float top = Ground + ConeHeight + bulgeH + 150f;
            float bottom = Ground - depth - 150f;

            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 16; rgb[i + 1] = 18; rgb[i + 2] = 24;
            }

            // 元の地面の線（基準）。
            PlotLine(rgb, top, bottom, d => Ground, 70, 70, 80);

            // 3 本の断面。
            PlotLine(rgb, top, bottom, Stage1Cone, 210, 140, 90);
            PlotLine(rgb, top, bottom, d => Stage2Bulge(d, bulgeR, bulgeH), 240, 200, 90);
            PlotLine(rgb, top, bottom,
                     d => Stage4Caldera(d, calderaR, depth, bulgeR, bulgeH), 235, 90, 70);

            return rgb;
        }

        private static void PlotLine(byte[] rgb, float top, float bottom,
                                     Func<float, float> heightAt, byte r, byte g, byte b)
        {
            int previous = -1;
            for (int px = 0; px < Width; px++)
            {
                float worldX = (px / (float)(Width - 1) * 2f - 1f) * ViewHalfWidth;
                float d = worldX < 0f ? -worldX : worldX;
                float h = heightAt(d);

                int py = (int)((top - h) / (top - bottom) * (Height - 1));
                if (py < 0) py = 0;
                if (py >= Height) py = Height - 1;

                // 縦に飛ぶところ（崖）も繋いで塗る —— 点が飛ぶと壁が見えない。
                int from = previous < 0 ? py : previous;
                int lo = from < py ? from : py;
                int hi = from < py ? py : from;
                for (int y = lo; y <= hi; y++)
                {
                    int at = (y * Width + px) * 3;
                    rgb[at] = r; rgb[at + 1] = g; rgb[at + 2] = b;
                }
                previous = py;
            }
        }
    }
}
