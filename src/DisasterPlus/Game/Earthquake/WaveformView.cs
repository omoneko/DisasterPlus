using ColossalFramework.UI;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 波形を 1 枚のテクスチャに描く。**main スレッド専用。**
    ///
    /// ── なぜ文字ではなくテクスチャなのか ─────────────────────────
    ///
    /// ①はハザードのバーを ASCII に固定した（<c>HazardLevel.FilledChar</c>）。理由は
    /// 「CS の UI フォントに罫線素片がある保証が無い」ことで、その規律をここまで
    /// 引き伸ばすと**文字でグラフを組んではいけない**という結論になる ——
    /// UILabel のフォントが等幅である保証も無く、列が揃わないからである。
    /// バー 1 本なら文字数だけの問題だが、80 行 320 列の格子は成立しない。
    ///
    /// ── 使う API は逆アセンブルで確認済み（推測していない）──────────────
    ///
    /// <c>ColossalFramework.UI.UITextureSprite</c>（ColossalManaged.dll。
    /// Assembly-CSharp ではない）:
    ///   - <c>public Texture texture { get; set; }</c> — 実在し、書き込み可能
    ///   - <c>OnRebuildRenderData</c> は <c>m_Texture == null</c> なら何もせず return し、
    ///     そうでなければ <c>EnsureMaterial()</c> → <c>renderMaterial.mainTexture = m_Texture</c>
    ///   - <c>EnsureMaterial</c> は <c>m_Material == null</c> のとき
    ///     <c>GetUIView().defaultAtlas.material</c> から複製を作る
    ///     → **こちらでマテリアルを用意する必要は無い。<c>texture</c> だけ入れればよい。**
    ///
    /// <c>UnityEngine.Texture2D</c>（Unity 5.6）:
    ///   - <c>.ctor(int, int, TextureFormat, bool)</c> — 実在
    ///   - <c>SetPixels32(Color32[])</c> / <c>Apply()</c> — 実在
    ///   - <c>TextureFormat.RGBA32</c> = 4 — 実在
    ///
    /// それでも <see cref="Build"/> は try/catch で囲み、失敗したら
    /// <see cref="Available"/> を false にする。**そのときパネルは黙って空欄に
    /// ならず、最大振幅の数値とバーを出したうえで「このビルドでは描画できない」と
    /// 名乗る**（<c>Strings.EarthquakeWaveformUnavailable</c>）。劣化であって嘘ではない。
    ///
    /// ── 再描画は「データが変わったとき」だけ ────────────────────────
    ///
    /// Task 6 / 7 はカーソルのレイを 4 フレームに 1 回へ絞った（≦521 → ≦131 サンプル
    /// /フレーム）。ここで毎フレーム 25,600 画素を塗り直したら、その努力をそのまま
    /// 返上することになる。<see cref="Render"/> は観測点・件数・最新フレームが
    /// 前回と同じなら**即座に return する**。データ自体も
    /// <c>SeismographRecorder</c> 側で最短 8 フレーム間隔でしか組み直されないので、
    /// 実際の塗り直しは 1 秒あたり数回で頭打ちになる。
    ///
    /// ── <c>Texture2D</c> は自分で捨てる ────────────────────────────
    ///
    /// <c>Texture2D</c> は <c>Component</c> ではないので、親の GameObject を
    /// <c>Object.Destroy</c> しても道連れにならない。<see cref="Destroy"/> で
    /// 明示的に破棄しないと、都市を読み込み直すたびに 1 枚ずつ残る。
    /// またフィールドを null に戻すことで、③で実際に起きた「破棄済み参照を
    /// 掴んだまま 2 つ目の都市で無言で不可視になる」形も避ける。
    /// </summary>
    public static class WaveformView
    {
        /// <summary>プロットの画素サイズ。UI 上のサイズも同じにして 1:1 で表示する。</summary>
        public const int PlotWidth = 320;
        public const int PlotHeight = 80;

        private static readonly Color32 Background = new Color32(16, 18, 24, 255);
        private static readonly Color32 AxisColor = new Color32(70, 76, 90, 255);
        private static readonly Color32 TraceColor = new Color32(255, 255, 255, 255);

        private static UITextureSprite _sprite;
        private static Texture2D _texture;
        private static Color32[] _pixels;
        private static bool _available;

        /// <summary>直近に描いた内容の指紋。同じなら塗り直さない。</summary>
        private static ushort _drawnBuildingId;
        private static int _drawnCount;
        private static uint _drawnNewestFrame;
        private static bool _drawnAnything;

        /// <summary>
        /// テクスチャによる描画が使えるか。<see cref="Build"/> が一度でも呼ばれるまでは false。
        /// false のとき、パネルは波形の代わりに最大振幅の行と理由の 1 行を出す。
        /// </summary>
        public static bool Available { get { return _available; } }

        /// <summary>
        /// プロット用のスプライトを親パネルに作る。**main スレッド、パネル構築時に 1 回。**
        /// 失敗しても例外を投げず、<see cref="Available"/> を false のままにする。
        ///
        /// 計画の型一覧は幅・高さも引数に取る形になっているが、テクスチャの画素数と
        /// UI 上のサイズが食い違うと波形が拡大縮小されて列が潰れる。両者が必ず
        /// 一致するよう、サイズは <see cref="PlotWidth"/> / <see cref="PlotHeight"/> に
        /// 固定して引数から外してある。
        /// </summary>
        public static void Build(UIPanel parent, string suffix, float x, float y)
        {
            Destroy();
            if (parent == null) return;

            try
            {
                _texture = new Texture2D(PlotWidth, PlotHeight, TextureFormat.RGBA32, false);
                _texture.filterMode = FilterMode.Point;
                _texture.wrapMode = TextureWrapMode.Clamp;
                _pixels = new Color32[PlotWidth * PlotHeight];

                _sprite = (UITextureSprite)parent.AddUIComponent(typeof(UITextureSprite));
                _sprite.name = FreeSlotFinder.SelfPrefix + "Earthquake" + suffix;
                _sprite.relativePosition = new Vector3(x, y);
                _sprite.width = PlotWidth;
                _sprite.height = PlotHeight;
                // マテリアルは UITextureSprite.EnsureMaterial が defaultAtlas から
                // 複製してくれる（上の逆アセンブル）。texture だけ入れればよい。
                _sprite.texture = _texture;
                _sprite.isVisible = false;

                Fill(Background);
                DrawAxis();
                _texture.SetPixels32(_pixels);
                _texture.Apply(false);

                _available = true;
            }
            catch (System.Exception e)
            {
                // 構築時に 1 回だけ。以後この経路は通らないのでスロットル不要。
                Log.Warn("earthquake waveform plot unavailable: " + e.GetType().Name
                         + " (" + e.Message + ")");
                Destroy();
            }
        }

        /// <summary>
        /// 波形を描く。**main スレッド。** <paramref name="trace"/> が null か
        /// サンプル 0 件ならプロットを隠す。
        ///
        /// **0 件を「変位 0」として平らな線で描かない。** 空のグラフと平らなグラフは
        /// 別の意味である（前者は「まだ記録が無い」、後者は「記録はあるが揺れていない」）。
        /// </summary>
        public static void Render(SeismographTrace trace)
        {
            if (!_available) return;

            if (trace == null || trace.Count <= 0)
            {
                if (_sprite != null) _sprite.isVisible = false;
                _drawnAnything = false;
                return;
            }

            if (_sprite != null) _sprite.isVisible = true;

            // ★ ここが毎フレームの塗り直しを止めている 1 箇所。
            if (_drawnAnything
                && _drawnBuildingId == trace.BuildingId
                && _drawnCount == trace.Count
                && _drawnNewestFrame == trace.NewestFrame)
            {
                return;
            }

            _drawnBuildingId = trace.BuildingId;
            _drawnCount = trace.Count;
            _drawnNewestFrame = trace.NewestFrame;
            _drawnAnything = true;

            try
            {
                Draw(trace);
            }
            catch (System.Exception e)
            {
                // 例外が出たら二度と描かない（毎フレーム投げ続けるより無害）。
                Log.Warn("earthquake waveform draw failed: " + e.GetType().Name);
                Destroy();
            }
        }

        /// <summary>
        /// レベルアンロード時。**<c>Texture2D</c> は GameObject の道連れにならないので
        /// 明示的に破棄する。** スプライト自体は親パネルと一緒に消える。
        /// </summary>
        public static void Destroy()
        {
            // 実行時の描画失敗でここへ来たときのために、先に隠す。パネルより先に
            // 破棄されている（fake-null）場合は Unity の == null が拾う。
            if (_sprite != null) _sprite.isVisible = false;

            if (_texture != null) Object.Destroy(_texture);

            _texture = null;
            _sprite = null;
            _pixels = null;
            _available = false;
            _drawnAnything = false;
            _drawnBuildingId = 0;
            _drawnCount = 0;
            _drawnNewestFrame = 0u;
        }

        private static void Draw(SeismographTrace trace)
        {
            // 窓は「最新サンプルまでの PlotFrameWindow フレーム」。フレームで割るので
            // ゲーム速度を上げても時間軸は伸び縮みしない（WaveformPlot のクラス doc）。
            uint newest = trace.NewestFrame;
            uint from = newest > SeismographRecorder.PlotFrameWindow
                ? newest - SeismographRecorder.PlotFrameWindow
                : 0u;

            // 縦軸は最大振幅で正規化する。振幅そのものはラベル側で数値として出すので、
            // ここで自動縮尺にしても「大きさ」を偽ることにはならない。
            // 平ら（peak == 0）でも正の縮尺を渡す —— 0 を渡すと WaveformPlot が
            // 全列を「サンプル無し」にしてしまい、平らな波形が空の波形に化ける。
            float peak = trace.PeakAbsolute;
            float scale = peak > 0f ? 1f / peak : 1f;

            int[] columns = WaveformPlot.Columns(trace.Frames, trace.Values, trace.Count,
                                                 from, newest, PlotWidth, PlotHeight - 1, scale);

            Fill(Background);
            DrawAxis();

            int middle = (PlotHeight - 1) / 2;
            for (int x = 0; x < PlotWidth; x++)
            {
                int row = columns[x];
                if (row == WaveformPlot.Empty) continue;

                int lo = row < middle ? row : middle;
                int hi = row < middle ? middle : row;
                for (int r = lo; r <= hi; r++) SetPixel(x, r, TraceColor);
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private static void Fill(Color32 color)
        {
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = color;
        }

        private static void DrawAxis()
        {
            int middle = (PlotHeight - 1) / 2;
            for (int x = 0; x < PlotWidth; x++) SetPixel(x, middle, AxisColor);
        }

        /// <summary>
        /// 行番号（0 = 上端）を <c>Texture2D</c> の画素（0 = 下端）に読み替えて置く。
        /// この上下反転を忘れると波形が鏡像になり、しかも一見それらしく見える。
        /// </summary>
        private static void SetPixel(int x, int row, Color32 color)
        {
            if (x < 0 || x >= PlotWidth) return;
            if (row < 0 || row >= PlotHeight) return;

            int y = PlotHeight - 1 - row;
            _pixels[y * PlotWidth + x] = color;
        }
    }
}
