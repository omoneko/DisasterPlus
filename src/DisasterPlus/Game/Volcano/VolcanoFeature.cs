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
            VolcanoState.Reset();

            // ★★ **毎レベルロードで登録し直すこと。** ToolController.m_tools は
            //    Awake で一度だけ構築され、ToolsModifierControl.SetTool<T> は静的辞書を
            //    引くだけなので、登録しないと SetTool<T>() が**黙って空振りする**
            //    （火災旋風 付録 A。VolcanoPlacementTool のクラス doc）。
            //    ToolController は都市ごとに作り直されるので、前の都市の登録は使えない。
            ToolRegistration.Register<VolcanoPlacementTool>();
        }

        /// <summary>
        /// sim スレッド。<c>TerrainManager</c> / <c>TerrainModify</c> /
        /// <c>SimulationManager</c> の読み書きは必ずここで行う。
        ///
        /// ポーズ中（deltaMinutes == 0）にも呼ばれる（<see cref="IPausedTickFeature"/>）。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.VolcanoEnabled.value)
            {
                // ★ 機能を切ったら、積まれている依頼と位相を捨てる（計画 §4.1）。
                //   捨てないと、切っている間に積まれた依頼が**入れ直した瞬間に発火**して、
                //   プレイヤーが忘れた地点に確認が出る。
                //   **既に変わった地形は戻らない。** 捨てるのは「これからの予定」だけである。
                //   条件を付けているのは、切っている間ずっとロックを取り続けないため。
                if (VolcanoState.Phase != VolcanoPhase.Idle
                    || VolcanoHub.PendingRequest.Kind != VolcanoRequest.None)
                {
                    VolcanoHub.TakeRequest();
                    VolcanoState.Reset();
                }
                return;
            }

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

            // ★★ **依頼の受け取りはポーズガードより上**（全体レビュー I1）。
            //    調べる・取りやめる・止めるは建物も道路も地形も 1 つも変えないので、
            //    ポーズ中でも答える。**答えないと「黙って何もしない」になる** ——
            //    山を作る前にポーズしてから地面をクリックしたプレイヤーには、
            //    確認が永久に出てこなかった。
            //    着手（Start）だけはここでは通らず、断って理由を残す
            //    （VolcanoState.HandleRequest）。**1 tick に 1 回だけ呼ぶこと。**
            VolcanoState.HandleRequest(snapshot, deltaMinutes > 0f);

            // ★ ここから下は状態を進める。⑤が進めるのは**取り消せない地形**である。
            //    ポーズ中（deltaMinutes == 0）は絶対に通さない。
            //    T4〜T9 が足す処理は必ずこの行より下に置くこと。
            //    このコメントを消すと「ポーズ中に山が育ち、建物が消え、溶岩が流れる」が起きる。
            if (deltaMinutes <= 0f) return;

            // T5〜T9 はこの中の位相分岐から呼ばれる。**ここに直接足さないこと。**
            VolcanoState.Tick(snapshot, frameIndex, deltaMinutes);
        }

        /// <summary>
        /// main スレッド。**ここから sim 側の型を呼ばないこと。**
        /// 読むのは <see cref="VolcanoHub.Latest"/> のスナップショットだけである。
        /// </summary>
        public void OnMainThreadUpdate()
        {
            VolcanoPanelButton.Tick();
            VolcanoPanel.Tick();

            // ★ 噴火の描画は main スレッドだけの機能。sim 側からは 1 度も呼ばれない。
            //   設定で切った瞬間に自分で畳む（切ったまま噴煙が残らないこと）。
            if (ModSettings.VolcanoEruptionFx.value) VolcanoEruption.Render();
            else VolcanoEruption.Destroy();

            // ★ 溶岩の描画も main スレッドだけの機能。sim 側からは 1 度も呼ばれない
            //   （T9 の独立性の実体。VolcanoLavaFx のクラス doc の grep）。
            if (ModSettings.VolcanoLavaRender.value) VolcanoLavaFx.Update(VolcanoHub.Latest);
            else VolcanoLavaFx.Destroy();
        }

        public void OnLevelUnloading()
        {
            // ★★ 配置ツールが選ばれたまま都市を出させない。次の都市でカーソルが
            //    「火山を置く」のまま始まると、プレイヤーが意図せず地点を指しうる
            //    （地形は取り消せない）。**アクティブでないときは何もしない**ので、
            //    他 MOD が選んでいたツールを横から戻すことはない。
            VolcanoPlacementTool.Deactivate();

            // ★ UI から先に畳む。2 つ目の都市が**ボタン 1 個・パネル 1 枚**で
            //    始まること（残すと都市を読み込むたびに 1 枚ずつ積み上がる）。
            VolcanoPanelButton.Remove();
            VolcanoPanel.Destroy();

            // ★ 噴煙の GameObject と Material は自分で Object.Destroy する
            //   （Material は Component ではないので GameObject の道連れにならない）。
            //   ここを飛ばすと都市を出入りするたびに 1 個ずつ残る。
            VolcanoEruption.Destroy();
            // ★ 溶岩の Mesh / Material / Texture2D も自分で Object.Destroy する
            //   （どれも Component ではないので GameObject の道連れにならない）。
            VolcanoLavaFx.Destroy();

            VolcanoHub.Clear();
            // ★ 地形の実測（RawHeights の長さ）を都市をまたいで持ち越さない。
            //    持ち越すと 2 つ目の都市で前の都市の事実を名乗ることになる。
            VolcanoReader.Reset();
            // ★ 位相と調査結果も持ち越さない。持ち越すと、次の都市で前の都市の
            //    地点に確認が出る（そして押せてしまう）。
            VolcanoState.Reset();
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
            WriteUiState(b);
            WriteState(b, snapshot);
        }

        /// <summary>
        /// 位相と、直近の調査結果。
        ///
        /// **<c>refusal</c> は必ず出す。** 「断られた」を「何も起きていない」と
        /// 見分ける手段がここにしか無い（④の <c>TyphoonSnapshot.Refusal</c> と同じ扱い）。
        ///
        /// **数えられなかった道路は <c>not counted</c> と出す。0 と混ぜない**
        /// （<see cref="VolcanoFootprint.SegmentCount"/> の doc）。
        /// </summary>
        private static void WriteState(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            b.Line(1, "phase", snapshot.Phase.ToString());
            b.Line(1, "placement tool", VolcanoPlacementTool.IsActive ? "active" : "idle");

            if (!string.IsNullOrEmpty(snapshot.Refusal))
            {
                b.Line(1, "refusal", snapshot.Refusal);
            }

            VolcanoFootprint f = snapshot.Footprint;
            if (!f.Valid)
            {
                b.Line(1, "survey", "none yet");
                return;
            }

            b.Line(1, "survey", "(" + f.Centre.X.ToString("F0") + ","
                                + f.Centre.Z.ToString("F0") + ")  ground "
                                + f.GroundHeightMetres.ToString("F1") + " m");
            b.Line(2, "shape", f.Form + "  r=" + f.RadiusMetres.ToString("F0")
                               + " m  h=" + f.HeightMetres.ToString("F0") + " m"
                               + (f.HeightLimitedByCeiling ? "  (LIMITED by the 1024 m ceiling)" : ""));
            b.Line(2, "counts", "buildings " + f.BuildingCount + ", segments "
                                + (f.SegmentCount < 0 ? "not counted" : f.SegmentCount.ToString())
                                + (f.Capped ? "  (CAPPED: these are a lower bound)" : ""));
            b.Line(2, "uplift tiles", f.TileCount.ToString());
            b.Line(2, "block height catch-up", f.BlockHeightCatchUpFrames
                                               + " sim frames (this is not a bug)");

            WriteClearing(b, snapshot);
        }

        /// <summary>
        /// 準備（T5）の実績。**壊した数が 0 のときも出す** —— 「機能が死んでいる」と
        /// 「範囲内に何も無い」を診断で区別できるようにするため（③で実際に起きた形）。
        ///
        /// <c>refused</c> は必ず出す。0 でないなら、その足元のセルは元の高さに固定された
        /// まま隆起に取り残される（<see cref="VolcanoClearing"/> のクラス doc の 4）。
        /// </summary>
        private static void WriteClearing(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            if (!snapshot.ClearingPathAvailable)
            {
                b.Line(1, "clearing", "NO USABLE DESTRUCTION PATH (roads and/or buildings)");
                b.Line(2, "consequence",
                    "no volcano is built at all: raising the ground without removing the roads "
                    + "and buildings first leaves flat trenches and bowls where they stood");
                return;
            }

            b.Line(1, "clearing", "swept " + snapshot.ClearedRadiusMetres.ToString("F0")
                                  + " m of " + snapshot.Footprint.RadiusMetres.ToString("F0")
                                  + " m" + (snapshot.ClearingComplete ? " (complete)" : "")
                                  + (snapshot.ClearingCapped
                                        ? "  (CAPPED: the front was not reached this pass)"
                                        : ""));
            b.Line(2, "removed", "buildings " + snapshot.BuildingsDestroyed
                                 + ", roads " + snapshot.SegmentsDestroyed
                                 + ", refused " + snapshot.BuildingsRefused);

            if (!string.IsNullOrEmpty(VolcanoClearing.LastFailure))
            {
                b.Line(2, "clearing failure", VolcanoClearing.LastFailure);
            }

            WriteUplift(b, snapshot);
        }

        /// <summary>
        /// 隆起（T6）の実績。
        ///
        /// ★ <c>cells written</c> と <c>active radius</c> の 2 つが、罠 1 と罠 2 を
        /// 実機で切り分ける唯一の材料である ——
        /// <c>cells written</c> が 0 なら 1 tick の増分が丸めで消えているか、
        /// もう目標に届いている。<c>active radius</c> が伸びないなら準備が止まっている。
        ///
        /// ★ <c>refused buildings</c> をここにも出すのは、§A-3 のフィードバックループの
        /// 規模がそれそのものだからである（<c>m_flattenTerrain == false</c> の建物が
        /// 動くたびに追加の <c>UpdateArea</c> が 1 回増える）。
        /// </summary>
        private static void WriteUplift(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            b.Line(1, "uplift", "tick " + VolcanoUplift.Ticks + "/" + VolcanoUplift.TotalTicks
                                + "  progress " + (snapshot.ProgressUnit * 100f).ToString("F0")
                                + "%  summit +" + snapshot.SummitMetres.ToString("F1") + " m"
                                + (snapshot.UpliftComplete ? " (complete)" : ""));
            b.Line(2, "active radius", snapshot.ActiveRadiusMetres.ToString("F0")
                                       + " m (as far as the clearing has reached)");
            b.Line(2, "cells written", VolcanoUplift.CellsWrittenLastTick
                                       + " last tick; tile " + snapshot.UpliftTileCursor
                                       + "/" + snapshot.UpliftTileCount);
            b.Line(2, "summit crater", snapshot.CraterCarved ? "carved" : "not carved yet");

            // 「建てられる地面」と水位の遅れ。**これは不具合ではない**（設計書 §7.3）。
            // 換算は FeatureHost.FramesPerMinute から出す（定数を直書きしない）。
            int frames = snapshot.Footprint.BlockHeightCatchUpFrames;
            float framesPerMinute = FeatureHost.FramesPerMinute;
            string catchUp = frames + " frames";
            if (framesPerMinute > 0f)
            {
                catchUp += " (about " + (frames / framesPerMinute / 60f).ToString("F1")
                           + " in-game hours)";
            }
            b.Line(2, "block heights catch-up", catchUp + " - this is not a bug");

            b.Line(2, "refused buildings still pinning",
                snapshot.BuildingsRefused
                + " (each one keeps its cell at the original height and adds a terrain "
                + "update of its own)");

            if (!string.IsNullOrEmpty(VolcanoUplift.LastFailure))
            {
                b.Line(2, "uplift failure", VolcanoUplift.LastFailure);
            }

            WriteEruption(b, snapshot);
        }

        /// <summary>
        /// 噴火（T7）。**借り物のエフェクトが使えないのは不具合ではない** ——
        /// ⑤自前の噴出物だけで噴火は成立する。だから
        /// <c>not available in this environment</c> にはその旨を添える。
        ///
        /// ★ <c>own particles</c> と <c>borrowed fire effect</c> を別の行にするのは、
        ///   実機で「何も見えない」を切り分ける材料がここにしか無いからである ——
        ///   前者が <c>not drawing</c> ならシェーダかマテリアルの問題（火災旋風 §4.9 / §4.8）、
        ///   後者だけが欠けているならこの環境で借りられないだけである。
        /// </summary>
        private static void WriteEruption(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            if (!snapshot.EruptionActive && VolcanoEruption.BurstsSoFar == 0) return;

            b.Line(1, "eruption", (snapshot.EruptionActive ? "active" : "finished")
                                  + " (intensity "
                                  + snapshot.EruptionIntensityUnit.ToString("F2")
                                  + ", " + VolcanoEruption.BurstsSoFar + " bursts)");
            b.Line(2, "own particles", VolcanoEruption.Drawing ? "drawing" : "not drawing");

            // ★ どのシェーダで解決したかを必ず名乗る（溶岩の描画と同じ扱い）。
            //   将来のゲーム更新で黙って不可視になったときの唯一の手がかりであり、
            //   Standard へ落ちた（＝光らない）ことも、ここでしか分からない。
            b.Line(3, "plume material", string.IsNullOrEmpty(VolcanoEruption.ShaderName)
                ? "NONE (no shader resolved; the plume is not drawn)"
                : VolcanoEruption.ShaderName
                  + (VolcanoEruption.ParticleShaderResolved
                        ? "" : "  (fallback: no particle shader in this build; "
                               + "forced to transparent so it does not draw opaque quads)"));
            b.Line(2, "borrowed fire effect", VolcanoEruption.BorrowedEffectAvailable
                ? "applied (the game's own building fire effect, no DLC needed)"
                : "not available in this environment (this is normal; the eruption still "
                  + "shows Disaster +'s own plume)");

            if (!VolcanoEruption.BorrowedEffectAvailable)
            {
                b.Line(3, "camera info", VolcanoEruption.CameraInfoAvailable
                    ? "resolved" : "NOT resolved");
            }

            // ★ 音は出ない。**IL 実測**（FireEffect.RenderEffect は m_soundEffect に
            //   1 度も触れず、音は PlayEffect の経路にある）。仕様であることを名乗る。
            b.Line(2, "sound", "none - the borrowed effect's sound lives on PlayEffect, "
                               + "not RenderEffect; Disaster + does not open an audio path");

            if (!string.IsNullOrEmpty(VolcanoEruption.LastFailure))
            {
                b.Line(2, "eruption failure", VolcanoEruption.LastFailure);
            }

            WriteLava(b, snapshot);
        }

        /// <summary>
        /// 溶岩（T8）。**本数 0（設定で無効）のときは行ごと出さない** ——
        /// 「0 本流れた」と「切ってある」を混ぜない。
        ///
        /// ★ <c>slope sign</c> はこの機能でいちばん重要な 1 行である。
        ///   勾配の符号を取り違えると溶岩が山を登るが、例外は 1 つも出ない（§8.1）。
        ///   IL では確定させてあるので、ここが <c>NOT VERIFIED</c> のまま進まないなら
        ///   ゲームの更新で挙動が変わっている。
        ///
        /// ★ 樹木・道路の 2 行は**できないことの説明**である。
        ///   どちらも⑤の手抜きではなくゲーム側の制約なので、診断でもそう名乗る。
        /// </summary>
        private static void WriteLava(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            if (snapshot.LavaFlowCount <= 0)
            {
                if (snapshot.Phase == VolcanoPhase.Flowing
                    || snapshot.Phase == VolcanoPhase.Cooling)
                {
                    b.Line(1, "lava", "off (the number of flows is set to 0)");
                }
                return;
            }

            b.Line(1, "lava", snapshot.LavaAliveCount + "/" + snapshot.LavaFlowCount
                              + " flows alive, longest "
                              + snapshot.LavaLongestMetres.ToString("F0") + " m, cooling "
                              + (snapshot.LavaCoolUnit * 100f).ToString("F0") + "% left");

            b.Line(2, "slope sign", VolcanoLava.SlopeSignVerified
                ? "verified at runtime (the first steps of a flow lost altitude)"
                : "NOT VERIFIED YET (a flow has not finished its observation window)");

            // ★ refused は**呼び出しの回数**であって建物の数ではない（VolcanoLava の doc）。
            //   同じ建物を何度も叩くので、燃えた数より遥かに大きくなるのが正常である。
            b.Line(2, "ignited", "buildings " + snapshot.LavaBuildingsIgnited
                                 + " (refused calls " + VolcanoLava.BuildingsRefused
                                 + "; mostly re-hits on buildings that are already burning)"
                                 + ", trees " + snapshot.LavaTreesIgnited);

            b.Line(2, "trees", snapshot.LavaTreesAvailable
                ? "burnable (Natural Disasters DLC is owned)"
                : "not burnable without the Natural Disasters DLC - the game itself refuses, "
                  + "so Disaster + leaves them standing (this is normal)");

            b.Line(2, "roads", "never burn - the game has no API for it at all; only the roads "
                               + "inside the footprint are removed, during the clearing phase");

            b.Line(2, "trail points", snapshot.LavaTrailPoints == null
                ? "0" : snapshot.LavaTrailPoints.Length.ToString());

            if (VolcanoLava.OutsidePurchasedArea)
            {
                b.Line(2, "outside the purchased area",
                    "the lava left the tiles you own; terrain sampling drops from 4 m detail to "
                    + "16 m interpolation there (this is normal, not a bug)");
            }

            if (!string.IsNullOrEmpty(VolcanoLava.LastFailure))
            {
                b.Line(2, "lava failure", VolcanoLava.LastFailure);
            }

            // ★ 溶岩の描画（T9）。**この 1 行が T9 の唯一の診断出力**である
            //   （この型を参照するファイルは 4 つだけ。あちらのクラス doc の grep）。
            //   どのシェーダで解決したかを必ず名乗る —— 将来のゲーム更新で
            //   黙って不可視になったときの、唯一の手がかりだからである。
            b.Line(1, "lava surface", VolcanoLavaFx.Drawing
                ? VolcanoLavaFx.DrawCalls + " draw call/frame, "
                  + VolcanoLavaFx.PointsDrawn + " points"
                : (ModSettings.VolcanoLavaRender.value ? "not drawing" : "off (setting)"));
            b.Line(2, "material", string.IsNullOrEmpty(VolcanoLavaFx.ShaderName)
                ? "NONE (no shader resolved; the lava is invisible but still flows and burns)"
                : VolcanoLavaFx.ShaderName
                  + (VolcanoLavaFx.ParticleShaderResolved
                        ? "" : "  (fallback: no particle shader in this build)"));
        }

        /// <summary>
        /// UI の状態。②④と同じ 3 状態（未設置／保存位置の再利用／新規探索の成否）。
        ///
        /// **ボタンが①②④のボタンと重なっているかどうかは、ここでしか分からない。**
        /// 重なったボタンは画面上で「1 個しか無い」ように見えるので、
        /// <c>fresh free-slot search FAILED</c> が出ているかを診断で確かめる。
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b)
        {
            string placement;
            if (!VolcanoPanelButton.Installed)
            {
                placement = "button not installed yet";
            }
            else if (VolcanoPanelButton.UsedSavedPosition)
            {
                placement = "saved position reused";
            }
            else
            {
                placement = VolcanoPanelButton.FoundFreeSlot
                    ? "fresh free-slot search succeeded"
                    : "fresh free-slot search FAILED (fell back to preferred position)";
            }

            b.Line(1, "button position", ModSettings.VolcanoButtonX.value + ","
                                         + ModSettings.VolcanoButtonY.value
                                         + "  (" + placement + ")");
            b.Line(1, "panel body", VolcanoPanel.IsVisible ? "shown" : "hidden");
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
            if (!terrain.NaturalDisastersOwned)
            {
                b.Line(1, "Natural Disasters DLC",
                    "not owned (trees will not burn; everything else works)");
                return;
            }

            // ★ 「測って所持」と「判定に失敗したので所持に倒した」を混ぜない
            //   （全体レビュー M11）。後者で「owned」と出すと、木が燃えない理由を
            //   探す人に嘘の手がかりを渡す。
            b.Line(1, "Natural Disasters DLC", ModCompat.NaturalDisastersOwnedKnown
                ? "owned"
                : "ASSUMED owned - the DLC check itself failed. If the trees do not burn, "
                  + "this is why: the game refuses BurnTree without the DLC and says nothing");
        }
    }
}
