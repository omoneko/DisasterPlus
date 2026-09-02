using ColossalFramework;
using ColossalFramework.Math;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>台風の進路・暴風域・風の分布を、都市の地図の上に描く。</b>
    /// **main スレッド専用**（カメラの <c>OnPostRender</c> の中）。
    ///
    /// ── 所有者の依頼（2026-09-02）────────────────────────────────
    ///
    /// &gt; 気象予報パネルには、今後の天気予報タブ（気象レーダーを置くことで解禁）、
    /// &gt; 台風の進路、暴風域、風の分布を都市マップ上に示すボタンを配置して、
    /// &gt; 機能するようにしてください
    ///
    /// 3 つは<b>別々に切れる</b>。同時に全部出すと地図が読めなくなるし、
    /// 「進路だけ見たい」が一番多い使い方になるはずだからである。
    ///
    /// ── 描画経路は②で確立済みのものをそのまま使う ────────────────────
    ///
    /// <c>OverlayEffect.DrawCircle</c> / <c>DrawQuad</c> は
    /// <c>Graphics.DrawMeshNow</c> を呼ぶ<b>即時描画</b>なので、
    /// <c>IRenderableManager.EndOverlay</c> の中からしか出ない
    /// （<see cref="OverlayRenderable"/> のクラス doc に IL）。
    /// 登録も②と<b>同じ 1 個</b>を共有する —— <c>RenderManager.m_renderables</c> は
    /// 外す API を持たないので、登録は増やさないほうがよい。
    ///
    /// ── ★★ 予測して描いてよいのは経路だけである ─────────────────────
    ///
    /// 進路は <see cref="TyphoonTrackPlan"/> から<b>厳密に引ける</b> ——
    /// 経路は (原点, 種, 速度, 接近フレーム) だけで決まっていて、乱数も天候も
    /// 入っていないからである。だから「これから通る道」は推測ではない。
    ///
    /// **強度の先読みはしない。** 強度には上陸減衰が入っていて、それは
    /// 「これから陸の上を通るか」に依存する。だから暴風域の円は
    /// <b>今の中心に、今の強度で</b>描くものだけにしてある。
    /// 先の半径を描くと、それは<b>確信を持った誤り</b>を地図に置くことになる。
    ///
    /// ── 1 フレームあたりの費用 ────────────────────────────────
    ///
    /// 進路 <see cref="TrackSamples"/> 本 ＋ 円 2 個 ＋ 風の格子
    /// <see cref="WindGridSide"/>² 個が上限で、**どれも定数**である。
    /// 都市の大きさにも建物の数にも比例しない。
    /// 経路の点は <see cref="_trackBuffer"/> を使い回すので<b>毎フレームの確保は無い</b>。
    /// </summary>
    public static class ForecastOverlay
    {
        /// <summary>進路を何点で引くか。線分はこれ - 1 本になる。</summary>
        private const int TrackSamples = 64;

        /// <summary>進路の線の太さ（m、片側）。</summary>
        private const float TrackHalfWidth = 24f;

        /// <summary>これから通る道に沿って置く印の間隔（点の数）。</summary>
        private const int TrackMarkerEvery = 8;

        /// <summary>印の大きさ（m）。</summary>
        private const float TrackMarkerSize = 220f;

        /// <summary>風の分布を測る格子の 1 辺。**費用の上限そのもの。**</summary>
        private const int WindGridSide = 21;

        /// <summary>風の格子 1 マスの円の大きさ（強風域の直径に対する比）。</summary>
        private const float WindDotFraction = 0.055f;

        /// <summary>
        /// <c>DrawQuad</c> の上下端。②の値と同じ。地形の起伏より広く取らないと、
        /// 丘の上や谷底で線が地面に飲まれる。
        /// </summary>
        private const float SlabMinY = -64f;
        private const float SlabMaxY = 1088f;

        // ── 色 ────────────────────────────────────────────────
        private static readonly Color TrackColour = new Color(1f, 0.85f, 0.25f, 0.75f);
        private static readonly Color TrackPastColour = new Color(1f, 1f, 1f, 0.28f);
        private static readonly Color MarkerColour = new Color(1f, 0.72f, 0.15f, 0.55f);
        private static readonly Color StormColour = new Color(1f, 0.30f, 0.20f, 0.30f);
        private static readonly Color GaleColour = new Color(1f, 0.62f, 0.20f, 0.20f);

        private static bool _showTrack;
        private static bool _showGale;
        private static bool _showWind;
        private static bool _sessionActive;
        private static bool _errorLogged;

        /// <summary>
        /// 経路の点を書き出す先。**毎フレーム使い回す**（描画経路で確保しない）。
        /// </summary>
        private static readonly Vec2[] _trackBuffer = new Vec2[TrackSamples];

        // ── 診断 ──────────────────────────────────────────────
        private static int _lastDrawCalls;

        /// <summary>直近のフレームで出した描画コール数（診断用）。</summary>
        public static int LastDrawCalls { get { return _lastDrawCalls; } }

        public static bool ShowTrack { get { return _showTrack; } }
        public static bool ShowGale { get { return _showGale; } }
        public static bool ShowWind { get { return _showWind; } }

        /// <summary>どれか 1 つでも出ているか（パネルの見た目に使う）。</summary>
        public static bool AnyVisible
        {
            get { return _sessionActive && (_showTrack || _showGale || _showWind); }
        }

        public static void ToggleTrack() { _showTrack = !_showTrack; }
        public static void ToggleGale() { _showGale = !_showGale; }
        public static void ToggleWind() { _showWind = !_showWind; }

        /// <summary>
        /// レベルロード時（main スレッド）。②と<b>同じ登録</b>に相乗りする ——
        /// <c>RenderManager.m_renderables</c> は外せないので増やさない。
        /// </summary>
        public static void EnsureRegistered()
        {
            _sessionActive = true;

            // 都市をロードするたびに全部 OFF から始める（②の EnsureRegistered と同じ）。
            _showTrack = false;
            _showGale = false;
            _showWind = false;
            _lastDrawCalls = 0;

            OverlayRenderable.EnsureRegistered();
        }

        /// <summary>
        /// レベルアンロード時。**登録は外せないので、描かないことをここで保証する。**
        /// </summary>
        public static void Reset()
        {
            _sessionActive = false;
            _showTrack = false;
            _showGale = false;
            _showWind = false;
            _lastDrawCalls = 0;
            _errorLogged = false;
        }

        /// <summary>
        /// **毎フレーム、カメラの <c>OnPostRender</c> の中から。**
        /// 台風が居ないときは即座に戻る。
        /// </summary>
        public static void Render(RenderManager.CameraInfo cameraInfo)
        {
            _lastDrawCalls = 0;

            if (!_sessionActive) return;
            if (!_showTrack && !_showGale && !_showWind) return;
            if (cameraInfo == null) return;
            if (!ModSettings.ForecastEnabled.value) return;

            try
            {
                var snapshot = TyphoonHub.Latest;
                if (snapshot == null || !snapshot.Active) return;

                if (!Singleton<RenderManager>.exists) return;
                var overlay = Singleton<RenderManager>.instance.OverlayEffect;
                if (overlay == null) return;

                if (_showTrack) DrawTrack(overlay, cameraInfo, snapshot);
                if (_showGale) DrawGale(overlay, cameraInfo, snapshot);
                if (_showWind) DrawWind(overlay, cameraInfo, snapshot);
            }
            catch (System.Exception e)
            {
                // 毎フレームの経路。1 回だけ大きく鳴らし、以後はキー単位スロットルへ。
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("forecast map overlay failed", e);
                }
                else
                {
                    Log.Diag("ForecastOverlay", "overlay draw failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>
        /// 進路。**通ってきた道は薄く、これから通る道は濃く。**
        ///
        /// ★ 経路は乱数も天候も含まないので、ここに描く線は推測ではない
        ///   （クラス doc の ★★）。
        /// </summary>
        private static void DrawTrack(OverlayEffect overlay,
                                      RenderManager.CameraInfo cameraInfo,
                                      TyphoonSnapshot s)
        {
            TyphoonTrackPlan plan = s.Track;
            if (!plan.Usable) return;

            int n = plan.Sample(_trackBuffer, TrackSamples, 0u, plan.TotalFrames);
            if (n < 2) return;

            uint now = s.ElapsedFrames;

            for (int i = 1; i < n; i++)
            {
                // この線分がもう通り過ぎたところか。端の点の時刻で決める。
                uint at = (uint)((long)plan.TotalFrames * i / (n - 1));
                Color colour = at <= now ? TrackPastColour : TrackColour;

                DrawSegment(overlay, cameraInfo, _trackBuffer[i - 1], _trackBuffer[i], colour,
                            TrackHalfWidth);

                // ★ これから通るところにだけ印を置く。過ぎた道に置いても
                //   「いつ来るか」を語らないので、地図を汚すだけである。
                if (at > now && i % TrackMarkerEvery == 0)
                {
                    overlay.DrawCircle(cameraInfo, MarkerColour,
                                       Ground(_trackBuffer[i]), TrackMarkerSize,
                                       SlabMinY, SlabMaxY, false, true);
                    _lastDrawCalls++;
                }
            }
        }

        /// <summary>
        /// 暴風域と強風域。**今の中心に、今の半径で**（クラス doc の ★★）。
        /// </summary>
        private static void DrawGale(OverlayEffect overlay,
                                     RenderManager.CameraInfo cameraInfo,
                                     TyphoonSnapshot s)
        {
            Vector3 centre = Ground(new Vec2(s.Centre.X, s.Centre.Z));

            if (s.GaleRadius > 0f)
            {
                overlay.DrawCircle(cameraInfo, GaleColour, centre, s.GaleRadius * 2f,
                                   SlabMinY, SlabMaxY, false, true);
                _lastDrawCalls++;
            }

            if (s.StormRadius > 0f)
            {
                overlay.DrawCircle(cameraInfo, StormColour, centre, s.StormRadius * 2f,
                                   SlabMinY, SlabMaxY, false, true);
                _lastDrawCalls++;
            }
        }

        /// <summary>
        /// 風の分布。強風域を覆う格子の各点で
        /// <c>TyphoonProfile.WindAt</c> を測り、強さで色を変えた点を置く。
        ///
        /// ★ **これは④が持っている風の場そのもの**であって、別に発明した絵ではない ——
        ///   同じ関数が倒木と建物被害の判定にも使われている。
        ///   だから地図の濃いところが、実際に壊れやすいところである。
        ///
        /// ★ 眼の中は静かなので中心付近は薄くなる。それは不具合ではなく、
        ///   <c>TyphoonProfile.WindAt</c> が眼を持っているからである。
        /// </summary>
        private static void DrawWind(OverlayEffect overlay,
                                     RenderManager.CameraInfo cameraInfo,
                                     TyphoonSnapshot s)
        {
            float gale = s.GaleRadius;
            if (!(gale > 0f)) return;

            float prefabRadius = s.Prefab.StormResolved ? s.Prefab.StormRadius : 0f;
            if (!(prefabRadius > 0f)) return;

            float step = gale * 2f / (WindGridSide - 1);
            float dot = gale * 2f * WindDotFraction;

            for (int gz = 0; gz < WindGridSide; gz++)
            {
                float z = s.Centre.Z - gale + gz * step;

                for (int gx = 0; gx < WindGridSide; gx++)
                {
                    float x = s.Centre.X - gale + gx * step;

                    float dx = x - s.Centre.X;
                    float dz = z - s.Centre.Z;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distance > gale) continue;

                    float wind = TyphoonProfile.WindAt(distance, s.Intensity, prefabRadius);
                    if (wind <= 0.02f) continue;

                    overlay.DrawCircle(cameraInfo, WindColourOf(wind),
                                       Ground(new Vec2(x, z)), dot,
                                       SlabMinY, SlabMaxY, false, true);
                    _lastDrawCalls++;
                }
            }
        }

        /// <summary>
        /// 風速相当（[0, 1]）を色へ。弱い＝青、強い＝赤。
        /// **透明度も強さで上げる** —— 弱いところが地図を覆い隠さないように。
        /// </summary>
        private static Color WindColourOf(float wind)
        {
            if (wind > 1f) wind = 1f;

            // 青 (0.25, 0.55, 1) → 黄 (1, 0.85, 0.2) → 赤 (1, 0.25, 0.15)
            float r, g, b;
            if (wind < 0.5f)
            {
                float t = wind * 2f;
                r = 0.25f + (1f - 0.25f) * t;
                g = 0.55f + (0.85f - 0.55f) * t;
                b = 1f + (0.20f - 1f) * t;
            }
            else
            {
                float t = (wind - 0.5f) * 2f;
                r = 1f;
                g = 0.85f + (0.25f - 0.85f) * t;
                b = 0.20f + (0.15f - 0.20f) * t;
            }

            return new Color(r, g, b, 0.20f + 0.45f * wind);
        }

        /// <summary>
        /// 2 点を結ぶ帯を 1 枚の <c>DrawQuad</c> で。②の断層帯と同じ手口。
        /// </summary>
        private static void DrawSegment(OverlayEffect overlay,
                                        RenderManager.CameraInfo cameraInfo,
                                        Vec2 from, Vec2 to, Color colour, float halfWidth)
        {
            float dx = to.X - from.X;
            float dz = to.Z - from.Z;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f) return;

            // 進行方向に直交する単位ベクトル。
            float nx = -dz / length * halfWidth;
            float nz = dx / length * halfWidth;

            var quad = new Quad3(
                Ground(new Vec2(from.X + nx, from.Z + nz)),
                Ground(new Vec2(to.X + nx, to.Z + nz)),
                Ground(new Vec2(to.X - nx, to.Z - nz)),
                Ground(new Vec2(from.X - nx, from.Z - nz)));

            overlay.DrawQuad(cameraInfo, colour, quad, SlabMinY, SlabMaxY, false, true);
            _lastDrawCalls++;
        }

        /// <summary>
        /// XZ をワールド座標へ。**高さは 0 でよい** —— <c>DrawCircle</c> /
        /// <c>DrawQuad</c> は <c>minY</c> / <c>maxY</c> で地形に沿って塗るので、
        /// ここで地面の高さを引く必要は無い（②が確立した使い方）。
        /// </summary>
        private static Vector3 Ground(Vec2 p)
        {
            return new Vector3(p.X, 0f, p.Z);
        }
    }
}
