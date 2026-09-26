using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The entry point for <c>RenderManager</c>'s overlay drawing.**
    /// This single instance hooks into the game's render loop and calls
    /// <see cref="EarthquakeOverlay.Render"/> every frame.
    ///
    /// ── The path, pinned down in the IL (the foundation of this feature; not one
    ///    guess in it) ──────────────────────────────────────────────
    ///
    /// <code>
    /// // Registration (public static, add-only. There is no API to remove one.)
    /// RenderManager::RegisterRenderableManager(IRenderableManager)
    ///   IL_0000 ldarg.0 ; brfalse IL_0011          // null does nothing
    ///   IL_0006 ldsfld  RenderManager::m_renderables
    ///   IL_000C callvirt FastList`1::Add
    ///
    /// // The call (inside the camera's OnPostRender)
    /// OverlayEffect::OnPostRender
    ///   IL_0039 RenderTexture::GetTemporary        // an RGBA buffer just for overlays
    ///   IL_0059 Graphics::SetRenderTarget(color: that buffer, depth: the screen's depth buffer)
    ///   IL_0079 GL::Clear(clearDepth: false, clearColor: true, (0,0,0,0))
    ///   IL_007E Application::get_isPlaying          → false means it is not called
    ///   IL_008F LoadingManager::m_loadingComplete   → false means it is not called  ★
    ///   IL_009E RenderManager::get_CurrentCameraInfo
    ///   IL_00A3 RenderManager::Managers_RenderOverlay(cameraInfo)
    ///
    /// RenderManager::Managers_RenderOverlay(CameraInfo)
    ///   IL_0014 BeginOverlay on every renderable
    ///   IL_0037 Graphics::ExecuteCommandBuffer(m_overlayBuffer)
    ///   IL_0050 EndOverlay on every renderable      ★ this is where we draw
    /// </code>
    ///
    /// **The reason for using <see cref="EndOverlay"/>** is that it is exactly where
    /// vanilla's own tools draw their overlays (via <c>ToolManager.EndOverlayImpl</c>).
    /// <c>OverlayEffect.DrawCircle</c> / <c>DrawQuad</c> end up calling
    /// <c>Graphics.DrawMeshNow</c>, i.e. **immediate-mode drawing** (<c>DrawEffect</c>
    /// IL_0070 / IL_00AE), so calling them outside a camera render callback — from
    /// <c>ThreadingExtensionBase.OnUpdate</c>, say — **draws nothing at all**.
    ///
    /// Thanks to the <c>m_loadingComplete</c> gate marked ★, this is never called while
    /// loading or in the main menu. Even so, <see cref="EarthquakeOverlay"/> keeps its
    /// own session guard (so that nothing is drawn between the moment a city is unloaded
    /// and the moment <c>m_loadingComplete</c> drops).
    ///
    /// ── Registration cannot be undone ────────────────────────────────
    ///
    /// <c>m_renderables</c> is a **static** field of <c>RenderManager</c>, and scanning
    /// every assembly turns up no call to <c>Clear</c> or <c>Remove</c> (the only writers
    /// are the <c>newobj</c> in the <c>.cctor</c> and the <c>Add</c> in
    /// <c>RegisterRenderableManager</c>). So once registered, it stays until the process
    /// exits. Therefore:
    ///   - register **once per process** (<see cref="EarthquakeOverlay.EnsureRegistered"/>)
    ///   - this class <b>holds not one game object</b>. Hold one and it is never released
    ///     when the city unloads, and on top of that a reference that has gone fake-null
    ///     is carried over into the next city (the exact breakage feature ③ actually hit)
    ///   - every condition for not drawing is decided from state on the
    ///     <see cref="EarthquakeOverlay"/> side
    ///
    /// ── About the methods that are not implemented ───────────────────
    ///
    /// <c>IRenderableManager</c> demands 11 methods. The only one actually used is
    /// <see cref="EndOverlay"/>; the rest return harmless defaults.
    /// <see cref="CalculateGroupData"/> in particular is called from
    /// <c>RenderGroup.UpdateMeshData</c> **for every render group**
    /// (<c>RenderManager::Managers_CalculateGroupData</c> folds the return values with
    /// OR). Returning <c>false</c> means "this manager puts nothing in this group", which
    /// has no effect whatsoever on vanilla's fold.
    /// </summary>
    public sealed class OverlayRenderable : IRenderableManager
    {
        /// <summary>
        /// Whether we are registered. **<c>RenderManager.m_renderables</c> has no API to
        /// remove one**, so this is kept to one per process (see the IL in the class doc).
        /// </summary>
        private static bool _registered;

        /// <summary>
        /// **Register exactly one.** The same single instance serves whether the call
        /// comes from ② (seismic intensity) or ① (typhoon track, storm area, wind) —
        /// since extras cannot be removed, registering one per feature would mean
        /// <b>the count growing every time you enter and leave a city</b>.
        /// </summary>
        /// <summary>
        /// Whether registration really succeeded. **This is what to look at when
        /// diagnosing.** It is not a count of calls: it says whether
        /// <c>RegisterRenderableManager</c> got through without throwing.
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
                // This happens once at construction, so no throttling is needed.
                Log.Error("failed to register the map overlay", e);
            }
        }

        public string GetName()
        {
            return "DisasterPlus.MapOverlay";
        }

        public DrawCallData GetDrawCallData()
        {
            // Totals for the profiler display. The overlay's real draw-call count is
            // reported in the diagnostic dump by EarthquakeOverlay.LastDrawCalls.
            return new DrawCallData();
        }

        public void BeginRendering(RenderManager.CameraInfo cameraInfo) { }

        public void EndRendering(RenderManager.CameraInfo cameraInfo) { }

        public void BeginOverlay(RenderManager.CameraInfo cameraInfo) { }

        /// <summary>
        /// **This is the only one implemented.** Inside the camera's <c>OnPostRender</c>,
        /// i.e. the main thread.
        ///
        /// ★ Both of them <b>return immediately at the top</b> when they should not be
        ///   drawing, so do not sort out settings or state here (doing so would scatter
        ///   the conditions across two places).
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
