using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤火山。プレイヤーが指した地点で、範囲内の道路と建物を段階的に壊しながら
    /// 地面が隆起し、山頂の火口から噴火して溶岩が斜面を流れ下る。
    ///
    /// **⑤はバニラの災害スロットに載らない**（設計書 §2）。ND DLC 無しでは
    /// バニラの災害プレハブが 1 つも存在せず（§D-11）、自前プレハブの
    /// 実行時登録はセーブにプレハブ名を焼き込む。したがって⑤は自前の位相機械
    /// （T4 の <c>VolcanoState</c>）で動く。**災害まわりの型には一切触らない。**
    ///
    /// > **その担保は grep である。** <c>src/DisasterPlus/Game/Volcano/</c> と
    /// > <c>src/DisasterPlus/Core/Volcano/</c> と <c>Game/UI/Volcano*.cs</c> に対して、
    /// > 災害マネージャ・災害プレハブ・災害データ・災害の生成／検索／検出の各 API 名を
    /// > 探して**0 件**であること。**doc コメントにもそれらの名前を書かないこと** ——
    /// > 書くと担保が「0 件」で読めなくなる。名前が要るときは事実文書 §D-11 を指す。
    ///
    /// **①②④と違い、⑤にはバニラの原資がまったく無い。** 火山という現象は
    /// バニラに存在せず、溶岩・マグマ・溶融物のプレハブもマテリアルもシェーダも
    /// DLL の文字列ヒープにすら 1 件も無い（§B-5）。したがって⑤が出す数値は
    /// **原則すべて本 MOD のもの**で、パネルは見出しで一度だけそう名乗る（設計書 §7.4）。
    ///
    /// このタスク（Task 2）の時点では**パネルも火山も無い機能**である。やることは
    /// sim スレッドで読んで <see cref="VolcanoHub"/> へ publish することと、
    /// **地形 API が解決できるかを診断ダンプに出すこと**だけ。それが分からなければ
    /// T3 以降は 1 行も意味を持たない（<see cref="VolcanoTerrainFacts.Usable"/>）。
    ///
    /// <see cref="IPausedTickFeature"/> を実装しているのは①②④と同じ理由
    /// （ロード直後にポーズしたままパネルを開くと全行が「読み取れません」になる）。
    /// **ただし⑤は T4 以降でゲームの状態を進める。しかも⑤が進めるのは地形であり、
    /// 取り消せない。** その契約を守る仕掛けは <see cref="OnSimulationTick"/> の中にある。
    /// </summary>
    public class VolcanoFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Volcano";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            VolcanoHub.Clear();
            VolcanoReader.Reset();
        }

        /// <summary>
        /// sim スレッド。<c>TerrainManager</c> / <c>TerrainModify</c> /
        /// <c>SimulationManager</c> の読み書きは必ずここで行う。
        ///
        /// ポーズ中（deltaMinutes == 0）にも呼ばれる（<see cref="IPausedTickFeature"/>）。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.VolcanoEnabled.value) return;

            // ここまでが「読んで publish するだけ」。ポーズ中もここは通る。
            var snapshot = VolcanoReader.Read();
            VolcanoHub.Publish(snapshot);

            // Volcano チャンネルは既定 OFF。この if が無いと、下の文字列連結が
            // 毎 sim tick（通常速度でおよそ 50 回/秒）実行されてから Log.Diag に
            // 捨てられる —— C# は引数を呼び出し前に評価し切るので、Diag の内側の
            // マスク判定では手遅れになる。
            //
            // ①の ForecastFeature と違い、ここは early-return にしてはいけない。
            // 下のポーズガードと以後の全処理を丸ごと飛ばすことになる
            // （②④が同じ注記を持っている）。
            if (Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano))
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "volcano",
                    snapshot.Valid
                        ? "terrain=" + (snapshot.Terrain.Usable ? "usable" : "UNUSABLE")
                          + " raw=" + snapshot.Terrain.RawArrayLength
                        : "snapshot invalid");
            }

            // ★ ここから下は状態を進める。⑤が進めるのは**取り消せない地形**である。
            //    ポーズ中（deltaMinutes == 0）は絶対に通さない。
            //    T4〜T9 が足す処理は必ずこの行より下に置くこと。
            //    このコメントを消すと「ポーズ中に山が育ち、建物が消え、溶岩が流れる」が起きる。
            if (deltaMinutes <= 0f) return;

            // （T4: VolcanoState.Tick がここに入る。T5〜T9 は VolcanoState の中から呼ばれる）
        }

        /// <summary>
        /// main スレッド。**ここから sim 側の型を呼ばないこと。**
        /// 読むのは <see cref="VolcanoHub.Latest"/> のスナップショットだけである。
        /// このタスクでは何もしない（T3 でパネルとボタンが入る）。
        /// </summary>
        public void OnMainThreadUpdate()
        {
        }

        public void OnLevelUnloading()
        {
            VolcanoHub.Clear();
            // ★ 地形の実測（RawHeights の長さ）を都市をまたいで持ち越さない。
            //    持ち越すと 2 つ目の都市で前の都市の事実を名乗ることになる。
            VolcanoReader.Reset();
        }

        /// <summary>
        /// **このタスクの主目的。** ⑤が地形を書けるかどうかを、行の有無ごと出す。
        ///
        /// **解決できなかった項目は <c>NOT RESOLVED</c> と出し、その下に
        /// 「何ができなくなるか」を 1 行足す。** 推測値を表示しないことを、
        /// 行の有無そのもので示す（設計書 §6）。
        /// </summary>
        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.VolcanoEnabled.value ? "yes" : "no");

            var snapshot = VolcanoHub.Latest;
            b.Line(1, "snapshot", snapshot == null
                ? "none yet"
                : (snapshot.Valid ? "valid" : "INVALID"));

            if (snapshot == null || !snapshot.Valid) return;

            WriteTerrain(b, snapshot.Terrain);
            WriteBudgets(b);
            WriteMode(b, snapshot.GameMode);
            WriteDlc(b, snapshot.Terrain);

            // T4 が位相機械を入れるまでは常に idle。
            b.Line(1, "state", "idle");
        }

        /// <summary>
        /// 地形 API の 4 行。**⑤が動けるかどうかはここだけで決まる。**
        /// </summary>
        private static void WriteTerrain(DiagnosticBuilder b, VolcanoTerrainFacts terrain)
        {
            if (!terrain.HeightsResolved)
            {
                b.Line(1, "terrain", "NOT RESOLVED (TerrainManager.RawHeights is unavailable)");
                b.Line(2, "consequence",
                    "no volcano can be built at all: this array is the ground itself");
            }
            else if (terrain.RawArrayLength != VolcanoTerrainFacts.ExpectedRawArrayLength)
            {
                b.Line(1, "terrain", "UNUSABLE: RawHeights holds " + terrain.RawArrayLength
                                     + " cells, expected "
                                     + VolcanoTerrainFacts.ExpectedRawArrayLength + " (1081^2)");
                b.Line(2, "consequence",
                    "no volcano can be built: every cell index is z*1081+x, so a different "
                    + "length would raise unrelated parts of the map");
            }
            else
            {
                b.Line(1, "terrain", "RawHeights " + terrain.RawArrayLength
                                     + " cells (1081^2), "
                                     + VolcanoShape.RawCellSizeMetres.ToString("F0")
                                     + " m per cell, 1/64 m quantum");
            }

            if (terrain.UpdateAreaResolved)
            {
                b.Line(1, "update path",
                    "resolved (TerrainModify.UpdateArea(int,int,int,int,bool,bool,bool))");
            }
            else
            {
                b.Line(1, "update path", "NOT RESOLVED");
                b.Line(2, "consequence",
                    "no volcano can be built: written heights would never reach the game");
            }

            if (terrain.CraterResolved && terrain.BurnGroundResolved)
            {
                b.Line(1, "crater / scorch",
                    "resolved (DisasterHelpers.MakeCrater, BurnGround)");
            }
            else
            {
                b.Line(1, "crater / scorch", "NOT RESOLVED"
                    + (terrain.CraterResolved ? " (BurnGround)"
                                              : (terrain.BurnGroundResolved ? " (MakeCrater)" : "")));
                b.Line(2, "consequence",
                    "the summit crater is not carved and the ground is not scorched along the "
                    + "lava; the mountain and the lava themselves still work");
            }

            if (terrain.SlopeSampleResolved)
            {
                b.Line(1, "slope sampling",
                    "resolved (TerrainManager.SampleDetailHeight(Vector3, out, out))");
            }
            else
            {
                b.Line(1, "slope sampling", "NOT RESOLVED");
                b.Line(2, "consequence",
                    "the lava cannot find its way downhill, so no lava flows at all. The "
                    + "mountain, the clearing and the eruption are unaffected");
            }
        }

        /// <summary>
        /// ⑤が自分に課している 2 つの上限（罠 3）と、地形高さの天井（§C-10）。
        /// **どちらも本 MOD の数字であって、ゲームが計算した値ではない。**
        /// </summary>
        private static void WriteBudgets(DiagnosticBuilder b)
        {
            b.Line(1, "tile budget",
                TileSplit.CoreTileSide + " core + " + TileSplit.Margin + " margin = "
                + TileSplit.MaxPassedSide + " per side, " + TileSplit.MaxPassedCells
                + " cells (limits: 128 side / 10000 cells)");

            b.Line(1, "ceiling", VolcanoShape.MaxTerrainMetres.ToString("F2") + " m absolute");
        }

        /// <summary>
        /// ゲームモードかエディタか。**定数を 2 つ持たず、実際のモードから選ぶ。**
        /// <c>m_blockHeights</c> の追随速度が変わる（§A-2 の表）ので、
        /// 「建てられる地面」と水位の遅れの見積りがそのまま変わる。
        /// </summary>
        private static void WriteMode(DiagnosticBuilder b, bool gameMode)
        {
            // ゲーム 2 m / エディタ 8 m（§A-2）。UpliftSchedule はゲームモードの
            // 定数だけを持つので、エディタのときは 4 倍速いと名乗る。
            float gameMetres = UpliftSchedule.BlockHeightRiseRawPerCycle
                               / UpliftSchedule.RawUnitsPerMetre;
            float metres = gameMode ? gameMetres : gameMetres * 4f;

            b.Line(1, "mode", (gameMode ? "game" : "editor")
                              + " (block heights rise " + metres.ToString("F0")
                              + " m per " + UpliftSchedule.BlockHeightCycleFrames
                              + " sim frames)");
        }

        /// <summary>
        /// **DLC 非所持は FAIL ではない。** ⑤は Natural Disasters を要らない
        /// （設計書 §1.4）。分岐するのは樹木の着火だけである（§B-7c）。
        /// </summary>
        private static void WriteDlc(DiagnosticBuilder b, VolcanoTerrainFacts terrain)
        {
            b.Line(1, "Natural Disasters DLC", terrain.NaturalDisastersOwned
                ? "owned"
                : "not owned (trees will not burn; everything else works)");
        }
    }
}
