using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **震度分布を地図に描く。** 依頼文の
    /// 「都市内での震源からの距離に応じた震度の分布の概念もありません」に
    /// 地図として答える唯一の部分。**メインスレッド（描画）専用。**
    ///
    /// ── 何を描いているのか（全部バニラが計算している量）───────────────
    ///
    /// | 描くもの | 量 | 典拠 |
    /// |---|---|---|
    /// | 青緑の同心円 | <c>s = 1 - d/R</c>、<c>R = 2000 + 20·intensity</c> | §A-3 の全体円盤 |
    /// | 紅紫の帯 | 断層 4 円盤が落ちうる範囲（<c>1.5·w</c>、沿走 <c>0.4L + w</c>） | §A-3 ＋ 全体レビュー C1 |
    /// | 白の点 | 震央 <c>m_targetPosition</c> | — |
    /// | 白の線 | 断層の走向 <c>m_angle</c>、長さ <c>L</c> | §A-3 / §A-6 |
    ///
    /// <b>s は「揺れ」ではない。</b>倒壊・出火の判定に掛かる局所係数である
    /// （全体レビュー C3）。バニラの揺れには半径の打ち切りが無い（§A-7）ので、
    /// この円盤の外でも地面は揺れている。パネルの凡例がそれを名乗る。
    ///
    /// ── 2 つの円盤を混ぜないための描き分け ──────────────────────
    ///
    /// 全体円盤は <c>probability = 0.02</c> の**ランプ**、断層 4 円盤は
    /// <c>probability = 1</c> の**平ら**である（§A-3）。だから
    /// **濃淡を付けるのは同心円だけ**にして、帯は一様な濃さで塗る。
    /// 帯を「s が高い場所」として濃く描くと、2 つの別モデルが 1 本の尺度に
    /// 見えてしまう（設計書 §3.1 の最終段落が禁じていること）。
    ///
    /// ── バニラのハザードマップとは別物 ────────────────────────
    ///
    /// パネルの「マップに表示」はバニラの情報ビュー（<c>SubInfoMode.EarthquakeHazard</c>）に
    /// 切り替えるだけで、そこに塗られるのは**別の形**である ——
    /// 亀裂**線分**までの距離・2 次減衰・<c>Rmax = R + 400</c>、しかも
    /// <c>Located</c>（＝地震計）が無いと 1 セルも塗られない（§A-6）。
    /// このオーバーレイは地震計が無くても出るし、形も違う。
    /// **同じものだと読ませないこと**が凡例の役目である。
    ///
    /// ── 毎フレームの経路であることの制約 ─────────────────────────
    ///
    ///   - **確保しない。** <c>Color</c> / <c>Vector3</c> / <c>Quad3</c> は全て構造体、
    ///     地震の列挙は添字走査、断層の折れ線は <see cref="_outlines"/> に使い回し。
    ///   - **<c>Log.Warn</c> / <c>Log.Error</c> を置かない**（スロットルされない）。
    ///     例外は 1 回だけ大きく鳴らし、以後は <c>Log.Diag</c> のキー単位スロットルへ
    ///     （<c>CameraShakeBooster</c> が確立した形）。
    ///   - **描画コール数に上限を持つ**（<see cref="MaxDrawCallsPerFrame"/>）。
    ///     地震は同時に 256 個まで存在しうる（§E-1）。
    /// </summary>
    public static class EarthquakeOverlay
    {
        /// <summary>震央のマーカーの直径（m）。</summary>
        private const float EpicentreMarkerSize = 160f;

        /// <summary>走向線の半幅（m）。</summary>
        private const float StrikeLineHalfWidth = 12f;

        /// <summary>
        /// 縦方向の描画帯（ワールド Y）。
        ///
        /// <c>DrawCircle</c> / <c>DrawQuad</c> は <c>minY</c> / <c>maxY</c> の外を
        /// 描かない（<c>ID_LimitsY</c>、IL_0069 / IL_00D2）。CS1 の地形は
        /// <c>RawHeights</c> が <c>ushort/64</c> なので **0〜1024 m** に収まる
        /// （IL 事実文書 §D-1）。その全域を含む帯にしておかないと、
        /// 山の上だけオーバーレイが途切れて「そこは分布の外」に見える。
        /// </summary>
        private const float SlabMinY = -64f;

        private const float SlabMaxY = 1088f;

        /// <summary>
        /// 1 フレームに出してよい描画コールの総数。
        ///
        /// 内訳は 1 地震あたり最大 22 コール
        /// （ランプ 10 ＋ 震央 1 ＋ 断層帯 <see cref="FaultBandOutline.Segments"/>=10 ＋ 走向線 1）で、
        /// **4 地震ぶん**。地震は同時に 256 個まで存在しうるので（§E-1）、
        /// 上限が無いと 5632 コールまで伸びうる。予算は**地震単位で**消費する
        /// （途中まで描いた地震を残さない）。足りなくなったら描くのをやめ、
        /// パネルがその旨を名乗る（<see cref="BudgetExhausted"/>）。
        ///
        /// 地震が進行しているフレームはゲーム中で最も重い。Task 6〜7 が
        /// カーソルのレイを 521 → 131 サンプル/フレームまで削ったのと同じ規律で、
        /// ここも最初から上限を持たせておく。
        /// </summary>
        public const int MaxDrawCallsPerFrame = 4 * DrawCallsPerQuake;

        /// <summary>1 地震あたりの最大コール数（<see cref="MaxDrawCallsPerFrame"/> の内訳）。</summary>
        public const int DrawCallsPerQuake =
            IntensityRamp.Steps + 1 + FaultBandOutline.Segments + 1;

        /// <summary>断層の折れ線のキャッシュ。<see cref="MaxDrawCallsPerFrame"/> と同じ 4 地震ぶん。</summary>
        private static readonly FaultBandOutline[] _outlines = CreateOutlines();

        private static bool _enabled;
        private static bool _registered;
        private static bool _sessionActive;
        private static bool _errorLogged;

        private static int _lastDrawCalls;
        private static int _drawnQuakes;
        private static bool _budgetExhausted;
        private static bool _faultGeometryMissing;

        /// <summary>
        /// 表示中か。**セッション状態**で、セーブにも設定にも残さない
        /// （情報ビューと同じ扱い。都市をロードするたびに OFF から始まる）。
        /// </summary>
        public static bool Enabled { get { return _enabled; } }

        /// <summary>直近のフレームで実際に出した描画コール数。診断ダンプ用。</summary>
        public static int LastDrawCalls { get { return _lastDrawCalls; } }

        /// <summary>直近のフレームで実際に描いた地震の数。</summary>
        public static int DrawnQuakes { get { return _drawnQuakes; } }

        /// <summary>描くべき地震が予算に収まらなかったか（パネルがその旨を出す）。</summary>
        public static bool BudgetExhausted { get { return _budgetExhausted; } }

        /// <summary>
        /// 描いた地震のうち、断層帯の幾何が読めなかったものがあるか。
        /// **読めないときは帯を 1 本も描かない**（推測の大きさで描かない）ので、
        /// 「帯が出ていない」の理由をパネルが名乗るために要る。
        /// </summary>
        public static bool FaultGeometryMissing { get { return _faultGeometryMissing; } }

        /// <summary><c>RenderManager</c> への登録に成功したか。</summary>
        public static bool Registered { get { return _registered; } }

        public static void Toggle()
        {
            _enabled = !_enabled;
            if (!_enabled) ClearStats();
        }

        /// <summary>
        /// 表示を止める。**パネルを閉じたら必ず呼ぶこと。**
        ///
        /// 凡例はパネルの中にしか無い。パネルを閉じたままオーバーレイだけが
        /// 地図に残ると、**何の量を見ているのかを名乗るものが画面から消える** ——
        /// しかもバニラのハザードビューと取り違えやすい状態そのものになる。
        /// 「絵と凡例は必ず同時に出る」を構造で保証する。
        /// </summary>
        public static void Disable()
        {
            if (!_enabled) return;
            _enabled = false;
            ClearStats();
        }

        /// <summary>
        /// レベルロード時（メインスレッド）。**ここで初めて登録する。**
        ///
        /// <c>RenderManager.m_renderables</c> は静的で、外す API が無い
        /// （<see cref="OverlayRenderable"/> のクラス doc）。したがって登録は
        /// プロセスにつき 1 回に絞り、以後は <see cref="_sessionActive"/> で
        /// 描くかどうかを切り替える。
        /// </summary>
        public static void EnsureRegistered()
        {
            _sessionActive = true;
            // 都市をロードするたびに OFF から始まる。Reset() が呼ばれずに
            // 次の都市へ来る経路（クラッシュからの復帰など）でも、前の都市の
            // トグルを引き継がない。
            _enabled = false;
            ClearStats();
            if (_registered) return;

            try
            {
                RenderManager.RegisterRenderableManager(new OverlayRenderable());
                _registered = true;
                Log.Info("earthquake intensity overlay registered with RenderManager");
            }
            catch (System.Exception e)
            {
                // 構築時の 1 回だけなのでスロットル不要。
                Log.Error("failed to register the earthquake intensity overlay", e);
            }
        }

        /// <summary>
        /// レベルアンロード時。**地震が終わった後・都市を出た後に描き続けない。**
        ///
        /// 登録そのものは外せないので、ここで <see cref="_sessionActive"/> を倒して
        /// <see cref="Render"/> を即 return させるのが唯一の止め方になる。
        /// </summary>
        public static void Reset()
        {
            _sessionActive = false;
            _enabled = false;
            ClearStats();
            for (int i = 0; i < _outlines.Length; i++) _outlines[i].Rebuild(default(FaultBand));
        }

        private static void ClearStats()
        {
            _lastDrawCalls = 0;
            _drawnQuakes = 0;
            _budgetExhausted = false;
            _faultGeometryMissing = false;
        }

        private static FaultBandOutline[] CreateOutlines()
        {
            var outlines = new FaultBandOutline[MaxDrawCallsPerFrame / DrawCallsPerQuake];
            for (int i = 0; i < outlines.Length; i++) outlines[i] = new FaultBandOutline();
            return outlines;
        }

        /// <summary>
        /// **描画スレッド（<c>OverlayEffect.OnPostRender</c> の中）から毎フレーム。**
        /// 呼び出し経路の IL は <see cref="OverlayRenderable"/> のクラス doc。
        /// </summary>
        public static void Render(RenderManager.CameraInfo cameraInfo)
        {
            ClearStats();

            if (!_enabled || !_sessionActive) return;
            if (!ModSettings.EarthquakeEnabled.value) return;
            if (cameraInfo == null) return;

            try
            {
                var snapshot = EarthquakeHub.Latest;
                if (snapshot == null || !snapshot.Valid) return;

                var quakes = snapshot.Quakes;
                if (quakes.Count == 0) return;

                if (!Singleton<RenderManager>.exists) return;
                var overlay = Singleton<RenderManager>.instance.OverlayEffect;
                if (overlay == null) return;

                DrawAll(overlay, cameraInfo, quakes);
            }
            catch (System.Exception e)
            {
                // 毎フレームの経路。1 回だけ大きく鳴らし、以後はキー単位スロットルへ。
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("earthquake intensity overlay failed", e);
                }
                else
                {
                    Log.Diag("EqOverlay", "overlay draw failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>
        /// 描く地震を選び、予算の範囲で描く。**確保しない。**
        ///
        /// 対象は <c>QuakeSelection.RunsDamage</c> が true の地震だけ ——
        /// つまり <c>Active</c> と <c>Emerging</c>。<c>Clearing</c> を除くのは、
        /// <c>DestroyBuildings</c> の呼び出しが <c>SimulationStep</c> の
        /// <c>Active</c> 分岐に**しか無い**（§A-3）ためで、パネルのカーソル行が
        /// 同じ理由で自分から降りるのと同じ判定を使う（全体レビュー I2）。
        ///
        /// <c>Emerging</c> を含めるのは、そのときにはもう震央・強度・断層の向きが
        /// 確定していて（§A-1 の <c>StartDisaster</c>）、これから壊れる範囲が
        /// **既に決まっている**からである。バニラのハザードマップも同じく
        /// <c>Emerging|Active</c> で塗る（§A-6）。
        /// </summary>
        private static void DrawAll(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                    IList<EarthquakeReading> quakes)
        {
            int budget = MaxDrawCallsPerFrame;
            int slot = 0;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];
                if (q == null) continue;
                if (!QuakeSelection.RunsDamage(q.Phase)) continue;

                if (budget < DrawCallsPerQuake)
                {
                    // ★ 地震単位で切る。予算の残りで途中まで描くと、
                    //    「内側の濃い段だけが無い」ランプ——つまり実際の分布と
                    //    違う減衰——が画面に出る。
                    _budgetExhausted = true;
                    break;
                }

                var outline = slot < _outlines.Length ? _outlines[slot] : null;
                slot++;

                int used = DrawQuake(overlay, cameraInfo, q, outline);
                budget -= used;
                _lastDrawCalls += used;
                _drawnQuakes++;
            }
        }

        /// <summary>地震 1 個ぶん。戻り値は出した描画コール数。</summary>
        private static int DrawQuake(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                     EarthquakeReading q, FaultBandOutline outline)
        {
            var epicentre = new Vector3(q.Epicentre.X, q.Epicentre.Y, q.Epicentre.Z);
            int calls = DrawRamp(overlay, cameraInfo, epicentre, q.Radius);

            var band = new FaultBand(new Vec2(q.Epicentre.X, q.Epicentre.Z),
                                     q.AngleRadians, q.CrackLength, q.CrackWidth);

            if (!band.Known)
            {
                // 帯の大きさが分からない。**推測して描かない**（プレハブ 4 値は
                // DLL に無く、実機でしか読めない。§A-0）。理由はパネルが出す。
                _faultGeometryMissing = true;
            }
            else if (outline != null)
            {
                // 折れ線は L と W だけの関数なので、地震ごとに 1 回測れば足りる
                // （FaultBandOutline のクラス doc）。**Matches を見てから呼ぶ**のが
                // その 1 回に絞る仕掛けで、ここが描画経路から Rebuild を呼んでよい
                // 唯一の理由である（第 2 層レビュー M1。209 回の Contains ＝
                // 約 3 万回の Gap 評価を、地震が現れた最初の 1 フレームだけ払う）。
                if (!outline.Matches(band.Length, band.Width)) outline.Rebuild(band);
                if (outline.Known) calls += DrawFaultBand(overlay, cameraInfo, band, outline, epicentre.y);
            }

            // 震央は最後に。ランプと帯の上に出す。
            overlay.DrawCircle(cameraInfo, MarkerColour, epicentre, EpicentreMarkerSize,
                               SlabMinY, SlabMaxY, false, true);
            calls++;

            return calls;
        }

        /// <summary>
        /// 全体円盤のランプ。**大きい順**に <see cref="IntensityRamp.Steps"/> 枚。
        /// アルファは <see cref="IntensityRamp.DrawAlpha"/> が決める（濃さが s に比例する）。
        /// </summary>
        private static int DrawRamp(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                    Vector3 epicentre, float radius)
        {
            int calls = 0;
            for (int k = 0; k < IntensityRamp.Steps; k++)
            {
                float r = IntensityRamp.RadiusOf(k, radius);
                if (r <= 0f) continue;

                var colour = new Color(RampR, RampG, RampB, IntensityRamp.DrawAlpha(k));
                // size は直径（IL_007E: 境界は center ± size*0.5、ID_CenterPos.w = size*0.5）。
                overlay.DrawCircle(cameraInfo, colour, epicentre, r * 2f,
                                   SlabMinY, SlabMaxY, false, true);
                calls++;
            }
            return calls;
        }

        /// <summary>
        /// 断層帯を台形の帯として描く。**濃さは一様**（<c>probability = 1</c> のモデルに
        /// ランプは無い）。加えて走向線（長さ <c>L</c>）を 1 本。
        /// </summary>
        private static int DrawFaultBand(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                         FaultBand band, FaultBandOutline outline, float y)
        {
            int calls = 0;
            var colour = new Color(BandR, BandG, BandB, BandAlpha);

            for (int i = 0; i < FaultBandOutline.Segments; i++)
            {
                float u0 = outline.AlongAt(i);
                float u1 = outline.AlongAt(i + 1);
                float h0 = outline.HalfWidthAt(i);
                float h1 = outline.HalfWidthAt(i + 1);
                if (h0 <= 0f && h1 <= 0f) continue;

                var quad = new Quad3(
                    LocalToWorld(band, u0, h0, y),
                    LocalToWorld(band, u1, h1, y),
                    LocalToWorld(band, u1, -h1, y),
                    LocalToWorld(band, u0, -h0, y));

                overlay.DrawQuad(cameraInfo, colour, quad, SlabMinY, SlabMaxY, false, true);
                calls++;
            }

            // 走向（m_angle）。長さは L ちょうど —— バニラがハザードマップの
            // 線分に使うのと同じ量（§A-6 の seg.a/seg.b = c ∓ dir*(L*0.5)）。
            float half = band.Length * 0.5f;
            var strike = new Quad3(
                LocalToWorld(band, -half, StrikeLineHalfWidth, y),
                LocalToWorld(band, half, StrikeLineHalfWidth, y),
                LocalToWorld(band, half, -StrikeLineHalfWidth, y),
                LocalToWorld(band, -half, -StrikeLineHalfWidth, y));
            overlay.DrawQuad(cameraInfo, MarkerColour, strike, SlabMinY, SlabMaxY, false, true);
            calls++;

            return calls;
        }

        /// <summary>
        /// 断層の局所座標（沿走 / 直交）をワールドへ。
        /// 直交の基底は <c>(Direction.Z, -Direction.X)</c> で、
        /// <c>FaultBand.Contains</c> の <c>across</c> と符号まで一致させてある
        /// （<see cref="FaultBandOutline"/> が測ったのと同じ向き）。
        /// </summary>
        private static Vector3 LocalToWorld(FaultBand band, float along, float across, float y)
        {
            float x = band.Centre.X + band.Direction.X * along + band.Direction.Z * across;
            float z = band.Centre.Z + band.Direction.Z * along - band.Direction.X * across;
            return new Vector3(x, y, z);
        }

        // ── 色 ────────────────────────────────────────────
        //
        // **実在の震度階級を思わせる色相の並びを使わない**（設計書 §3.1 / §7-4）。
        // 緑→黄→橙→赤のような並びは、それだけで「気象庁震度階級のような、
        // 実在の意味を持つ尺度」を名乗ってしまう。ここは**単一の色相の濃淡だけ**で
        // 表す —— 段が意味するのは「s がこれだけ大きい」の 1 次元だけである。
        //
        // 色相はバニラの災害ハザード情報ビュー（黄〜赤系）と重ならない側に取る。
        // 2 つを同時に出したときに、同じ絵の続きに見えないようにするため。

        private const float RampR = 0.16f;
        private const float RampG = 0.85f;
        private const float RampB = 1.00f;

        // 断層帯。ランプと**別の色相**にする。同じ色の濃い版にすると
        // 「s が高い場所」に見え、probability = 1 の別モデルであることが消える。
        private const float BandR = 1.00f;
        private const float BandG = 0.30f;
        private const float BandB = 0.80f;

        /// <summary>帯は一様。<c>probability = 1</c> のモデルに減衰は無い（§A-3）。</summary>
        private const float BandAlpha = 0.40f;

        private static readonly Color MarkerColour = new Color(1f, 1f, 1f, 0.85f);
    }
}
