using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <c>RenderManager</c> の**オーバーレイ描画の呼び出し口**。
    /// この 1 個のインスタンスがゲームの描画ループに刺さり、
    /// <see cref="EarthquakeOverlay.Render"/> を毎フレーム呼ぶ。
    ///
    /// ── IL で確定させた経路（この機能の土台。憶測は 1 つも無い）─────────────
    ///
    /// <code>
    /// // 登録（public static、追加するだけ。取り外す API は存在しない）
    /// RenderManager::RegisterRenderableManager(IRenderableManager)
    ///   IL_0000 ldarg.0 ; brfalse IL_0011          // null なら何もしない
    ///   IL_0006 ldsfld  RenderManager::m_renderables
    ///   IL_000C callvirt FastList`1::Add
    ///
    /// // 呼び出し（カメラの OnPostRender の中）
    /// OverlayEffect::OnPostRender
    ///   IL_0039 RenderTexture::GetTemporary        // オーバーレイ専用の RGBA バッファ
    ///   IL_0059 Graphics::SetRenderTarget(color: そのバッファ, depth: 画面の深度バッファ)
    ///   IL_0079 GL::Clear(clearDepth: false, clearColor: true, (0,0,0,0))
    ///   IL_007E Application::get_isPlaying          → false なら呼ばない
    ///   IL_008F LoadingManager::m_loadingComplete   → false なら呼ばない  ★
    ///   IL_009E RenderManager::get_CurrentCameraInfo
    ///   IL_00A3 RenderManager::Managers_RenderOverlay(cameraInfo)
    ///
    /// RenderManager::Managers_RenderOverlay(CameraInfo)
    ///   IL_0014 全 renderable の BeginOverlay
    ///   IL_0037 Graphics::ExecuteCommandBuffer(m_overlayBuffer)
    ///   IL_0050 全 renderable の EndOverlay          ★ ここで描く
    /// </code>
    ///
    /// **<see cref="EndOverlay"/> を使う理由**は、バニラのツール類が
    /// （<c>ToolManager.EndOverlayImpl</c> 経由で）オーバーレイを描いているのが
    /// 同じ位置だからである。<c>OverlayEffect.DrawCircle</c> / <c>DrawQuad</c> は
    /// 最終的に <c>Graphics.DrawMeshNow</c> を呼ぶ**即時描画**（<c>DrawEffect</c>
    /// IL_0070 / IL_00AE）なので、カメラの描画コールバックの外——たとえば
    /// <c>ThreadingExtensionBase.OnUpdate</c>——から呼んでも**何も出ない**。
    ///
    /// ★ の <c>m_loadingComplete</c> ゲートのおかげで、ロード中・メインメニューでは
    /// そもそも呼ばれない。それでも <see cref="EarthquakeOverlay"/> 側に
    /// 独自のセッションガードを持つ（都市をアンロードした瞬間から
    /// <c>m_loadingComplete</c> が落ちるまでの間に描かないため）。
    ///
    /// ── 登録は取り消せない ──────────────────────────────────
    ///
    /// <c>m_renderables</c> は <c>RenderManager</c> の**静的**フィールドで、
    /// 全アセンブリを走査しても <c>Clear</c> も <c>Remove</c> も呼ばれていない
    /// （書き手は <c>.cctor</c> の <c>newobj</c> と <c>RegisterRenderableManager</c> の
    /// <c>Add</c> だけ）。つまり一度登録したらプロセスが終わるまで外れない。
    /// したがって:
    ///   - 登録は**プロセスにつき 1 回**（<see cref="EarthquakeOverlay.EnsureRegistered"/>）
    ///   - このクラスは<b>ゲームのオブジェクトを 1 つも掴まない</b>。掴むと
    ///     都市をアンロードしても解放されず、しかも fake-null 化した参照を
    ///     次の都市へ持ち越すことになる（③で実際に起きた壊れ方）
    ///   - 描かない条件は全て <see cref="EarthquakeOverlay"/> 側の状態で判定する
    ///
    /// ── 実装していないメソッドについて ────────────────────────────
    ///
    /// <c>IRenderableManager</c> は 11 個のメソッドを要求する。実際に使うのは
    /// <see cref="EndOverlay"/> だけで、残りは無害な既定値を返す。
    /// 特に <see cref="CalculateGroupData"/> は
    /// <c>RenderGroup.UpdateMeshData</c> から**全描画グループについて**呼ばれる
    /// （<c>RenderManager::Managers_CalculateGroupData</c> は戻り値を OR で畳む）。
    /// <c>false</c> を返す＝「このマネージャはこのグループに何も出さない」で、
    /// バニラの畳み込みに一切影響しない。
    /// </summary>
    public sealed class OverlayRenderable : IRenderableManager
    {
        /// <summary>
        /// 登録済みか。**<c>RenderManager.m_renderables</c> は外す API を持たない**
        /// ので、プロセスにつき 1 個に絞る（クラス doc の IL）。
        /// </summary>
        private static bool _registered;

        /// <summary>
        /// **1 個だけ登録する。** ②（震度）と①（台風の進路・暴風域・風）の
        /// どちらから呼ばれても同じ 1 個で足りる —— 増やしても外せないので、
        /// 機能ごとに 1 個ずつ登録すると<b>都市を出入りするたびに増えていく</b>。
        /// </summary>
        /// <summary>
        /// 本当に登録できたか。**診断はこれを見ること。**
        /// 呼んだ回数ではなく、<c>RegisterRenderableManager</c> が例外を投げずに
        /// 通ったかを表す。
        /// </summary>
        public static bool Registered { get { return _registered; } }

        public static void EnsureRegistered()
        {
            if (_registered) return;

            try
            {
                RenderManager.RegisterRenderableManager(new OverlayRenderable());
                _registered = true;
                Log.Info("map overlay registered with RenderManager "
                         + "(earthquake intensity and typhoon forecast share it)");
            }
            catch (System.Exception e)
            {
                // 構築時の 1 回だけなのでスロットル不要。
                Log.Error("failed to register the map overlay", e);
            }
        }

        public string GetName()
        {
            return "DisasterPlus.MapOverlay";
        }

        public DrawCallData GetDrawCallData()
        {
            // プロファイラ表示用の集計。オーバーレイの実コール数は
            // EarthquakeOverlay.LastDrawCalls が診断ダンプに出す。
            return new DrawCallData();
        }

        public void BeginRendering(RenderManager.CameraInfo cameraInfo) { }

        public void EndRendering(RenderManager.CameraInfo cameraInfo) { }

        public void BeginOverlay(RenderManager.CameraInfo cameraInfo) { }

        /// <summary>
        /// **ここだけが実装。** カメラの <c>OnPostRender</c> の中＝メインスレッド。
        ///
        /// ★ 2 つとも、自分が出るべきでないときは<b>先頭で即座に戻る</b>ので、
        ///   ここで設定や状態を見分けない（見分けると条件が 2 か所に散る）。
        /// </summary>
        public void EndOverlay(RenderManager.CameraInfo cameraInfo)
        {
            EarthquakeOverlay.Render(cameraInfo);
            ForecastOverlay.Render(cameraInfo);
        }

        public void UndergroundOverlay(RenderManager.CameraInfo cameraInfo) { }

        public void CheckReferences() { }

        public void InitRenderData() { }

        public bool CalculateGroupData(int groupX, int groupZ, int layer,
                                       ref int vertexCount, ref int triangleCount,
                                       ref int objectCount, ref RenderGroup.VertexArrays vertexArrays)
        {
            return false;
        }

        public void PopulateGroupData(int groupX, int groupZ, int layer,
                                      ref int vertexIndex, ref int triangleIndex,
                                      Vector3 groupPosition, RenderGroup.MeshData data,
                                      ref Vector3 min, ref Vector3 max,
                                      ref float maxRenderDistance, ref float maxInstanceDistance,
                                      ref bool requireSurfaceMaps)
        {
        }
    }
}
