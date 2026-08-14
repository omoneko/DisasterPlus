using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// カーソル直下の建物を探した**結果の種類**。
    ///
    /// **「建物が無かった」と「調べられなかった」を同じ顔にしないためだけに存在する。**
    /// 以前は両方が <see cref="BuildingMargin.None"/> になり、表示側が区別できずに
    /// 「カーソルの下に建物がありません」と書いていた —— 読み取り失敗が
    /// 意味のあるゼロの顔をして出てくる、この機能が他の全ての行で禁じている壊れ方
    /// そのものである（<c>EarthquakeReading.CoverageKnown</c> /
    /// <c>EarthquakeSnapshot.CursorCoverageValid</c> と同じ理由）。
    /// </summary>
    public enum BuildingProbeOutcome
    {
        /// <summary>そもそも調べていない（カーソルが無効、対象の地震が無い等）。</summary>
        NotProbed,

        /// <summary>調べて、建物が 1 個見つかった。</summary>
        Found,

        /// <summary>調べて、<c>PickRadius</c> 以内に候補が 1 個も無かった。**これは実測値である。**</summary>
        Empty,

        /// <summary>
        /// 調べられなかった。<c>BuildingManager</c> / バッファ / グリッドが取れないか、
        /// 走査が例外を投げた。**この状態で建物の有無を名乗ってはいけない。**
        /// </summary>
        Failed,
    }

    /// <summary>
    /// カーソル直下の建物を 1 個特定し、その建物の余裕度を出す。**sim スレッド専用。**
    ///
    /// ── なぜカーソルの建物特定を sim スレッドでやるのか ──────────────
    ///
    /// <c>BuildingManager.m_buildings.m_buffer</c> と <c>m_buildingGrid</c> は
    /// **sim スレッドが所有する**。一方カーソル座標は <c>Input.mousePosition</c> と
    /// <c>Camera.main</c> から来るので **main スレッドでしか取れない**。したがって:
    ///
    /// <code>
    /// main（EarthquakePanel.Tick）
    ///   → 地形との交点を出す（既存の TryPickCursorGround）
    ///   → EarthquakeHub.PublishCursor(worldPos, valid)
    /// sim（EarthquakeReader.Read）
    ///   → EarthquakeHub.TakeCursor(out pos)
    ///   → BuildingProbe.ProbeAt(...)  ← ここで初めて建物バッファに触る
    ///   → snapshot.CursorBuilding に載せる
    /// main（EarthquakePanel.Refresh）
    ///   → snapshot.CursorBuilding を描くだけ
    /// </code>
    ///
    /// **1 tick ぶん（最大 1/50 秒相当）の遅延が出る。** カーソルを速く動かすと表示が
    /// 1 フレーム遅れて追従する。これは正しい代償であり、遅延を消すために main から
    /// 建物バッファを読んではいけない。破ると、スタックトレースの無い
    /// <c>IndexOutOfRangeException</c> が後になってバニラのコードの中で出て、
    /// この MOD の try/catch では捕まえられない。
    ///
    /// 走査は <c>FireWhirlDamage.CollectNearby</c> をそのまま手本にしている
    /// （セル 64、オフセット 135、<c>[0,269]</c> クランプ、<c>m_nextGridBuilding</c> 連結、
    /// <c>guard &gt; 32768</c> の保険）。
    /// </summary>
    public static class BuildingProbe
    {
        /// <summary>
        /// カーソル座標からこの距離までの建物を候補にする（建物グリッド 1 セルぶん）。
        /// 建物の当たり判定そのものではないので、これは「だいたいこの辺を指している」の意味。
        /// </summary>
        public const float PickRadius = 64f;

        /// <summary>
        /// 候補にするフラグ条件。**バニラの <c>DestroyBuildings</c> の一次カリングと
        /// 同じマスク・同じ比較にする**（§A-3）:
        ///
        /// <code>if ((m_flags &amp; 524307 /* 0x80013 */) != 1) continue;</code>
        ///
        /// 0x80013 = <c>Created | Deleted | Untouchable | Demolishing</c>（4 つの名前が
        /// 実際にこの値であることは <c>Assembly-CSharp</c> をリフレクションで実測済み）。
        /// つまり「Created が立っていて、残る 3 つがどれも立っていない」建物だけを
        /// バニラは判定する。ここを緩めると、バニラが見向きもしない建物について
        /// 「倒壊します」と断定することになる。
        ///
        /// **<c>Collapsed</c>（0x400000）はこのマスクに入っていない。** バニラも
        /// 弾いていないし、こちらも弾いてはいけない —— 弾くと瓦礫の上で
        /// 「カーソルの下に建物がありません」という**誤った**説明が出る。
        /// 倒壊・炎上は候補から外すのではなく <c>alreadyDown</c> として
        /// <see cref="BuildingMargin.Evaluate"/> へ渡し、結論の側で名乗らせる。
        ///
        /// （<c>FireWhirlDamage.CollectMask</c> が Collapsed を弾くのは、あちらが
        ///  「これから燃やす候補」を選んでいるからで、目的が違う。）
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing;

        /// <summary>走査中の想定外例外を 1 回だけ大きく鳴らしたか。</summary>
        private static bool _probeErrorLogged;

        /// <summary>
        /// カーソル直下の建物 1 個の余裕度。
        /// <paramref name="outcome"/> が <see cref="BuildingProbeOutcome.Found"/> の
        /// ときだけ戻り値に意味がある。
        ///
        /// **3 状態を返すこと自体が仕様である**（<see cref="BuildingProbeOutcome"/> の doc）。
        /// 「調べたが空だった」と「調べられなかった」を混ぜると、読み取り失敗が
        /// 「カーソルの下に建物がありません」という実測値の顔で出てくる。
        ///
        /// **例外を出さない。** sim スレッドの <c>IndexOutOfRangeException</c> は
        /// スタックトレース無しのポップアップになるので、添字は必ず配列長で守る。
        /// </summary>
        /// <param name="heightMetres">
        /// 見つかった建物の高さ（m）。**第 2 層（長周期地震動）専用**で、
        /// 第 1 層の余裕度は高さを一切使わない —— バニラが使っていないからである
        /// （§A-3 は <c>m_position</c> の距離しか見ない）。
        /// **0 は「低い」ではなく「読めなかった」**（<see cref="BuildingHeight.MetresOf"/>）。
        /// </param>
        public static BuildingMargin ProbeAt(Vec3 worldPos, EarthquakeReading quake,
                                             FaultBand band, bool damageModelReplaced,
                                             out BuildingProbeOutcome outcome,
                                             out float heightMetres)
        {
            outcome = BuildingProbeOutcome.NotProbed;
            heightMetres = 0f;
            if (quake == null) return BuildingMargin.None();

            try
            {
                var bm = BuildingManager.instance;
                if (bm == null)
                {
                    outcome = BuildingProbeOutcome.Failed;
                    return BuildingMargin.None();
                }

                var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
                var grid = bm.m_buildingGrid;
                if (buildings == null || grid == null)
                {
                    outcome = BuildingProbeOutcome.Failed;
                    return BuildingMargin.None();
                }

                ushort best = FindNearest(buildings, grid, worldPos);
                if (best == 0)
                {
                    // ★ ここだけが「調べて、無かった」。走査は最後まで走っている。
                    outcome = BuildingProbeOutcome.Empty;
                    return BuildingMargin.None();
                }

                var p = buildings[best].m_position;
                var flags = buildings[best].m_flags;
                bool alreadyDown = (flags & Building.Flags.Collapsed) != Building.Flags.None
                                   || buildings[best].m_fireIntensity != 0;

                // 第 2 層が使う量。第 1 層の結論（BuildingMargin）には入れない。
                heightMetres = BuildingHeight.MetresOf(ref buildings[best]);

                outcome = BuildingProbeOutcome.Found;
                return BuildingMargin.Evaluate(
                    best, quake.DisasterId,
                    new Vec2(p.x, p.z), quake.Epicentre.ToVec2(),
                    quake.Intensity, band, alreadyDown, damageModelReplaced);
            }
            catch (System.Exception e)
            {
                // Log.Warn / Log.Error はスロットルされない。ここは毎 sim tick の経路なので、
                // 1 回だけ大きく鳴らして以後はキー単位スロットルへ落とす
                // （EarthquakeReader._readErrorLogged と同じ形）。
                if (!_probeErrorLogged)
                {
                    _probeErrorLogged = true;
                    Log.Error("earthquake building probe failed", e);
                }
                else
                {
                    Log.Diag("EqProbe", "building probe failed: " + e.GetType().Name);
                }
                outcome = BuildingProbeOutcome.Failed;
                heightMetres = 0f;
                return BuildingMargin.None();
            }
        }

        /// <summary>
        /// <see cref="PickRadius"/> 以内でカーソルにいちばん近い建物。無ければ 0。
        /// </summary>
        private static ushort FindNearest(Building[] buildings, ushort[] grid, Vec3 worldPos)
        {
            // 建物グリッドは 1 セル 64m、270x270。境界をはみ出さないようクランプする。
            int minX = Clamp((int)((worldPos.X - PickRadius) / 64f + 135f));
            int maxX = Clamp((int)((worldPos.X + PickRadius) / 64f + 135f));
            int minZ = Clamp((int)((worldPos.Z - PickRadius) / 64f + 135f));
            int maxZ = Clamp((int)((worldPos.Z + PickRadius) / 64f + 135f));

            var cursor2d = worldPos.ToVec2();
            float bestDistanceSquared = PickRadius * PickRadius;
            ushort best = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = z * 270 + x;
                    if (cell < 0 || cell >= grid.Length) continue;

                    ushort id = grid[cell];
                    int guard = 0;

                    while (id != 0 && id < buildings.Length)
                    {
                        // バニラは `!= 1 -> continue`。読みやすい書き方に直さず、
                        // 比較演算子ごとそのまま写す。
                        if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                        {
                            var p = buildings[id].m_position;
                            float d2 = cursor2d.DistanceSquaredTo(new Vec2(p.x, p.z));
                            if (d2 < bestDistanceSquared)
                            {
                                bestDistanceSquared = d2;
                                best = id;
                            }
                        }

                        id = buildings[id].m_nextGridBuilding;

                        // 連結リストが壊れている保存データで無限ループしないための保険。
                        if (++guard > 32768) break;
                    }
                }
            }

            return best;
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 269) return 269;
            return v;
        }
    }
}
