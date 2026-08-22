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
                //   プレイヤーが忘れた地点に山が生えはじめる。
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
                          // ★ 火山性地震の行。**揺れていないときも必ず出す** ——
                          //   「機能が死んでいる」と「今は揺れない位相なのだ」が
                          //   ログ上で区別できなくなる（③で実際に起きた形）。
                          + " tremor=" + (VolcanoTremorTrace.Active
                              ? VolcanoTremorTrace.ActivityUnit.ToString("F2")
                              : "off")
                        : "snapshot invalid");
            }

            // ★★ **依頼の受け取りはポーズガードより上**（全体レビュー I1）。
            //    受け取り自体は建物も道路も地形も 1 つも変えない ——
            //    設置の依頼は位相を Clearing にするだけで、実際に壊すのは
            //    下の VolcanoState.Tick である。**答えないと「黙って何もしない」に
            //    なる** —— 山を作る前にポーズしてから地面をクリックしたプレイヤーには、
            //    何も起きなかった。**1 tick に 1 回だけ呼ぶこと。**
            VolcanoState.HandleRequest(snapshot);

            // ★ ここから下は状態を進める。⑤が進めるのは**取り消せない地形**である。
            //    ポーズ中（deltaMinutes == 0）は絶対に通さない。
            //    T4〜T9 が足す処理は必ずこの行より下に置くこと。
            //    このコメントを消すと「ポーズ中に山が育ち、建物が消え、溶岩が流れる」が起きる。
            if (deltaMinutes <= 0f) return;

            // T5〜T9 はこの中の位相分岐から呼ばれる。**ここに直接足さないこと。**
            VolcanoState.Tick(snapshot, frameIndex, deltaMinutes);

            // ★ 位相が進んだ**あと**に、火山性地震の記録側を合わせる
            //   （②の地震計がここから読む。<see cref="VolcanoTremorTrace"/>）。
            //   前に置くと 1 tick 古い位相で記録することになる。
            //   カメラの揺れ（main）とは別経路で、あちらは触らない。
            VolcanoTremorTrace.Update(frameIndex);
        }

        /// <summary>
        /// main スレッド。**ここから sim 側の型を呼ばないこと。**
        /// 読むのは <see cref="VolcanoHub.Latest"/> のスナップショットだけである。
        /// </summary>
        public void OnMainThreadUpdate()
        {
            // ボタンは DisasterPanelBar が 4 個まとめて持つ（FeatureHost が呼ぶ）。
            // ★ ⑤が開く窓はこれ 1 枚だけである。確認の窓は 2026-08-21 に撤去した ——
            //   タイル → スライダー → 地図をクリックで火山が起きる。
            VolcanoPanel.Tick();

            // ★ ⑤を構えているあいだだけ、強度スライダーのラベルを「山の大きさ」に
            //   読み替える（VolcanoSizeReadout のクラス doc）。構えていないフレームは
            //   バニラの表示へ書き戻して何もしない。
            VolcanoSizeReadout.Update();

            // ★ 噴火の描画は main スレッドだけの機能。sim 側からは 1 度も呼ばれない。
            //   設定で切った瞬間に自分で畳む（切ったまま噴煙が残らないこと）。
            //   描いているのは**ゲーム自身の粒子エフェクト**である（VolcanoEruptionFx）。
            bool eruptionFx = ModSettings.VolcanoEruptionFx.value;
            if (eruptionFx)
            {
                VolcanoEruptionFx.Update(VolcanoHub.Latest);
            }
            else
            {
                VolcanoEruptionFx.Destroy();
                // ★ 火口のマグマだまり・光・雷は噴煙の描画の中から呼ばれるので、
                //   噴煙を切ったらこちらも自分で畳む（抱えたままにしない）。
                VolcanoCraterFx.Destroy();
            }

            // ★ 火砕流「もどき」の土煙。**別の設定で独立に切れる** ——
            //   帯 1 本あたり粒子数が大きく、切りたい人が居る見た目である。
            //   これは火砕流の再現ではない（VolcanoPyroclasticFx のクラス doc）。
            bool pyroclasticFx = ModSettings.VolcanoPyroclasticFx.value;
            if (pyroclasticFx) VolcanoPyroclasticFx.Update(VolcanoHub.Latest);
            else VolcanoPyroclasticFx.Destroy();

            // ★ 両方切ってあるあいだは借り物の複製も手放す（切ったまま抱えない）。
            //   次に入れ直したフレームで作り直される。
            if (!eruptionFx && !pyroclasticFx) VolcanoVanillaFx.Destroy();

            // ★ 噴火の音も main スレッドだけの機能。sim 側からは 1 度も呼ばれない。
            //   **1 フレームに 1 回だけ**呼ぶこと（2 回積むとバニラの効果音の枠を
            //   1 つの音で潰す。VolcanoEruptionAudio のクラス doc）。
            //   切った瞬間にクリップを手放す（切ったまま数 MB を抱えないこと）。
            if (ModSettings.VolcanoEruptionSound.value)
            {
                VolcanoEruptionAudio.Update(VolcanoHub.Latest);
            }
            else
            {
                VolcanoEruptionAudio.Destroy();
            }

            // ★ 溶岩の描画も main スレッドだけの機能。sim 側からは 1 度も呼ばれない
            //   （T9 の独立性の実体。VolcanoLavaFx のクラス doc の grep）。
            if (ModSettings.VolcanoLavaRender.value) VolcanoLavaFx.Update(VolcanoHub.Latest);
            else VolcanoLavaFx.Destroy();

            // ★ 火山性地震の揺れ。**②の設定を 1 つも見ない**（VolcanoTremorShake の
            //   クラス doc）。切った瞬間に足すのをやめれば、次のフレームで消える
            //   （CameraController.LateUpdate が毎フレーム 0 に戻す）。
            if (ModSettings.VolcanoQuake.value) VolcanoTremorShake.Update(VolcanoHub.Latest);
            else VolcanoTremorShake.Reset();
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
            //    ボタンの撤去は FeatureHost.LevelUnloading が DisasterPanelBar.Remove で行う。
            VolcanoPanel.Destroy();

            // ★ スライダーのラベルは**触らずに参照だけ手放す**（もう破棄されている）。
            //   ツールを降りたときの書き戻しは VolcanoPlacementTool.Deactivate の側で
            //   既に済んでいる（上の 1 行がそれを呼ぶ）。
            VolcanoSizeReadout.Reset();

            // ★ 噴火の描画側の時計を戻す。
            VolcanoEruptionFx.Destroy();
            // ★ 火口の Mesh / Material / Texture2D も自分で Object.Destroy する
            //   （どれも Component ではないので GameObject の道連れにならない）。
            VolcanoCraterFx.Destroy();
            VolcanoPyroclasticFx.Destroy();

            // ★★ 借り物の複製（GameObject と、その内側に出来る粒子系）は自分で消す。
            //    内側の複製は "Particle Effects" ルート（DontDestroyOnLoad）の下に
            //    ぶら下がっていて**外側を消しても道連れにならない**ので、
            //    ここを飛ばすと都市を出入りするたびに粒子系が 1 組ずつ残る。
            VolcanoVanillaFx.Destroy();
            // ★ 噴火音の AudioClip と AudioInfo も自分で Object.Destroy する
            //   （どちらも Component ではないので GameObject の道連れにならない）。
            //   ここを飛ばすと都市を出入りするたびに数 MB のクリップが 1 個ずつ残る。
            VolcanoEruptionAudio.Destroy();
            // ★ 溶岩の Mesh / Material / Texture2D も自分で Object.Destroy する
            //   （どれも Component ではないので GameObject の道連れにならない）。
            VolcanoLavaFx.Destroy();
            // ★ 火山性地震の時計とカメラの参照も持ち越さない。
            VolcanoTremorShake.Reset();

            VolcanoHub.Clear();
            // ★ 地形の実測（RawHeights の長さ）を都市をまたいで持ち越さない。
            //    持ち越すと 2 つ目の都市で前の都市の事実を名乗ることになる。
            VolcanoReader.Reset();
            // ★ 位相と調査結果も持ち越さない。持ち越すと、次の都市で前の都市の
            //    地点の火山がそのまま育ち続ける。
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
            WriteAudio(b);
            WriteState(b, snapshot);
            WriteNotes(b);
        }

        /// <summary>
        /// **画面から降ろした説明の行き場。**
        ///
        /// 所有者の指示は「あれこれ説明は出さなくていい」「ほかの災害と同じように
        /// クリックしたら起こるようにしてほしい」だった。⑤は確認の窓ごと撤去したので、
        /// 説明はすべてここと火山タブにある。
        /// **落としたのは説明の置き場所であって、情報ではない。**
        /// テスターと不具合報告が読むのはこのファイルであり、
        /// 山を建てようとしている人が読む場所ではない。
        ///
        /// ★ ここは sim スレッドである（<c>DiagnosticDump</c> のクラス doc）。
        ///   ゲームのバッファにも UI にも触らない、定数の行だけにすること。
        /// </summary>
        private static void WriteNotes(DiagnosticBuilder b)
        {
            b.Line(1, "note: counts",
                "the building and road counts on the volcano tab are what the survey "
                + "counted at the moment of the click. The city keeps changing while the "
                + "ground is cleared, so the real number will differ. That is why the row "
                + "says \"approx.\"");
            b.Line(1, "note: no confirmation",
                "clicking the map places the volcano straight away - there is no "
                + "confirmation step. The terrain change is still permanent and is saved; "
                + "the only way out after the click is the Stop button on the volcano tab, "
                + "and that only cancels what has not happened yet");
            b.Line(1, "note: why clear first",
                "raising the ground without destroying the roads and buildings first does "
                + "not work: the game pins the terrain back to the height of every road and "
                + "building on every update, so the mountain would end up full of flat "
                + "trenches and bowls (design section 1.2)");
            b.Line(1, "note: unfinished volcano",
                "Disaster + does not store an unfinished volcano. After loading a save made "
                + "mid-build the mountain stays exactly as far as it got - no crater, no "
                + "eruption, no lava - and there is no way to finish or remove it. Placing a "
                + "new volcano on the same spot piles a second mountain on top of it");
            b.Line(1, "note: buildable ground",
                "the buildable ground and the water level do not follow the visible terrain "
                + "straight away; they catch up at 2 m per 64 simulation frames "
                + "(design section 7.3). This is not a bug");
        }

        /// <summary>
        /// 噴火音の 2〜3 行。
        ///
        /// **音が出ないときの切り分けはここにしか無い。** 「設定で切った」「同梱 wav が
        /// 無い／壊れている」「ゲーム側の経路が解決しない」は、プレイヤーから見ると
        /// どれも同じ無音である。3 つを別々の行にする。
        ///
        /// ★★ <b>ここは sim スレッドである</b>（<c>DiagnosticDump</c> のクラス doc:
        ///   main がホットキーで頼み、sim が組み立てる）。だから
        ///   <c>ScanAudioFacts</c> を**ここから呼ばない** —— あちらは
        ///   <c>File.Exists</c> と <c>PluginManager.GetInstances</c> に触るので、
        ///   sim スレッドへ持ち込んではいけない。読むのは main（<c>Assumptions.Run</c>）が
        ///   レベルロードのたびに走査して置いた <c>LastFacts</c> のキャッシュだけである。
        /// </summary>
        private static void WriteAudio(DiagnosticBuilder b)
        {
            if (!ModSettings.VolcanoEruptionSound.value)
            {
                b.Line(1, "eruption sound", "off (setting)");
                return;
            }

            if (!VolcanoEruptionAudio.FactsScanned)
            {
                // 「まだ走査していない」を「経路が無い」と混ぜない。
                b.Line(1, "eruption sound", "not scanned yet");
                return;
            }

            VolcanoAudioFacts facts = VolcanoEruptionAudio.LastFacts;

            if (!facts.Usable)
            {
                b.Line(1, "eruption sound", "NO USABLE AUDIO PATH (effectGroup="
                    + (facts.EffectGroupResolved ? "ok" : "missing") + ", addEvent="
                    + (facts.AddEventResolved ? "ok" : "missing") + ", clipApi="
                    + (facts.ClipApiResolved ? "ok" : "missing") + ")");
                b.Line(2, "consequence",
                    "the eruption is silent. Nothing else is affected: the mountain, the "
                    + "plume and the lava do not depend on the audio path");
                return;
            }

            b.Line(1, "eruption sound", facts.FileFound
                ? "file present (" + facts.FileBytes + " bytes)"
                : "NO FILE - " + VolcanoEruptionAudio.AudioFolderName + "\\"
                  + VolcanoEruptionAudio.FileName + " is not in the mod folder; "
                  + "the eruption is silent");
            b.Line(2, "clip", VolcanoEruptionAudio.Detail);
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
            // ★ flush 1/1 は「この tick に変わった全域が、同じ tick で画面に出た」
            //   ＝ いちばん滑らかな状態。2 以上なら分割してタイル総当たりに落ちており、
            //   目に見える 1 段はその枚数ぶんの上昇量になる（VolcanoUplift のクラス doc）。
            //   **「断続的なせり上がり」の切り分けはこの 1 行でしかできない。**
            b.Line(2, "cells written", VolcanoUplift.CellsWrittenLastTick
                                       + " last tick; flush " + snapshot.UpliftTileCursor
                                       + "/" + snapshot.UpliftTileCount
                                       + " (1/1 = the whole change reached the screen this tick)"
                                       + "; footprint " + VolcanoUplift.FootprintTileCount
                                       + " tile(s)");
            b.Line(2, "summit crater", snapshot.CraterFormed
                ? "at full depth" : "still shallower than its final depth");
            // 山肌の凹凸。0% なら滑らかな円錐そのもの（設定の意味を診断でも名乗る）。
            b.Line(2, "flank relief", ModSettings.VolcanoReliefStrength.value
                                      + "% (0 = a smooth cone)"
                                      + (VolcanoUplift.Complete
                                         ? ""
                                         : "; rising "
                                           + VolcanoUplift.RiseMetresPerTick.ToString("F2")
                                           + " m per uplift tick ("
                                           + VolcanoUplift.RiseMetresPerFrame.ToString("F3")
                                           + " m per sim frame)"));

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
        /// ★ 借り物の 4 つを 1 行ずつ出すのは、実機で「何も見えない」を切り分ける材料が
        ///   ここにしか無いからである。<c>NOT resolved</c> はその 1 つだけが描かれない
        ///   ということで、噴火も山も溶岩も止まらない。
        ///
        /// ★★ <b>ここは sim スレッドである</b>（<c>DiagnosticDump</c> のクラス doc:
        ///   main がホットキーで頼み、sim が組み立てる）。だから
        ///   <c>VolcanoVanillaFx</c> の解決経路を**ここから呼ばない** ——
        ///   あちらは <c>Object.Instantiate</c> と <c>ParticleSystem</c> に触る。
        ///   読むのは main スレッドが描画のときに書いておいた
        ///   <c>bool</c> / <c>int</c> / <c>string</c> のキャッシュだけである
        ///   （<c>WriteAudio</c> が同じ理由で同じ形をしている）。
        /// </summary>
        private static void WriteEruption(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            if (!snapshot.EruptionActive && VolcanoEruption.BurstsSoFar == 0) return;

            b.Line(1, "eruption", (snapshot.EruptionActive ? "active" : "finished")
                                  + " (intensity "
                                  + snapshot.EruptionIntensityUnit.ToString("F2")
                                  + ", " + VolcanoEruption.BurstsSoFar + " bursts"
                                  + (VolcanoEruption.Building
                                     ? ", still building the mountain" : "") + ")");
            var facts = VolcanoEruptionFx.Facts;

            b.Line(2, "crater effects", VolcanoEruptionFx.Drawing
                ? "drawing (the game's own particle effects)"
                : (ModSettings.VolcanoEruptionFx.value ? "not drawing" : "off (setting)"));

            // ★ 何が引けて何が複製できたかを必ず名乗る。将来のゲーム更新で
            //   黙って何も出なくなったときの唯一の手がかりである。
            b.Line(3, "borrowed effects", VolcanoEruptionFx.Detail);
            b.Line(3, "ash plume", facts.AshResolved
                ? VolcanoVanillaFx.AshName + " (no DLC needed)" : "NOT resolved");
            // ★ 噴煙は 1 回ではなく「柱の段」で出す（Core/Volcano/EruptionColumn）。
            //   0 段なら柱は 1 本も立っていない ——「引けている」と「出ている」は別である。
            // ★ 火口のマグマだまり・噴煙への光・火山雷（2026-08-22）。
            //   **描いていないときも出す** ——「切ってある」「シェーダが引けない」
            //   「今は光っていない」が、出さないとログ上で区別できない。
            b.Line(3, "crater glow", VolcanoCraterFx.MaterialResolved
                ? (VolcanoCraterFx.Drawing ? "drawing" : "idle (not erupting this frame)")
                : "NO MATERIAL - the magma pool, the light and the lightning are not drawn");
            b.Line(3, "volcanic lightning", ModSettings.VolcanoLightningFx.value
                ? VolcanoCraterFx.BoltsDrawn + " bolt(s) lit this frame"
                : "off (setting)");
            b.Line(3, "eruption column", VolcanoEruptionFx.PlumeSegments + " of "
                + EruptionColumn.MaxSegments + " segment(s) this frame, "
                + VolcanoEruptionFx.PlumeHeightMetres.ToString("F0") + " m tall");
            b.Line(3, "flames", facts.FlameResolved
                ? VolcanoVanillaFx.FlameName + " (the game's own building fire, no DLC needed)"
                : "NOT resolved");
            b.Line(3, "ejecta", facts.EjectaResolved
                ? VolcanoVanillaFx.EjectaName + " (no DLC needed)" : "NOT resolved");

            if (!facts.CameraInfoResolved)
            {
                b.Line(3, "camera info", "NOT resolved - nothing is drawn this frame");
            }

            // ★★ 火砕流は**バニラに存在しない**。代用であることを診断でも名乗る。
            b.Line(2, "pyroclastic flow", ModSettings.VolcanoPyroclasticFx.value
                ? (VolcanoPyroclasticFx.DustResolved
                    ? VolcanoPyroclasticFx.BandsDrawn + " of "
                      + PyroclasticSurge.LobeCount + " lobe(s) of "
                      + VolcanoVanillaFx.DustName
                      + " fanning down the flanks, NOT a real pyroclastic flow; the game has "
                      + "no such effect. It damages nothing"
                    : "NOT resolved")
                : "off (setting)");

            // ★ 粒子を描く経路から音は出ない。**IL 実測**（RenderEffect は
            //   m_soundEffect に 1 度も触れず、音は PlayEffect の経路にある）。
            //   ⑤の噴火音は VolcanoEruptionAudio の別経路である。
            b.Line(2, "sound", "the particle path is silent by design; the eruption sound is "
                               + "Disaster +'s own file on a separate audio path");

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
            b.Line(2, "material", VolcanoLavaFx.ShaderDetail);
        }

        /// <summary>
        /// UI の状態。①②③④と同じ形（<see cref="DisasterPanelBar"/> に問い合わせるだけ）。
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b)
        {
            // ボタンは⑤専用ではなく DisasterPanelBar が 4 個まとめて置く。座標は
            // もうこの MOD が決めていないので、出すのは「居るか」と「どこに居るか」だけ。
            b.Line(1, "button", (DisasterPanelBar.IsInstalled(DisasterPanelBar.IdVolcano)
                ? "installed" : "not installed") + "  (" + DisasterPanelBar.Placement + ")");
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

            if (terrain.BurnGroundResolved)
            {
                b.Line(1, "lava scorch", "resolved (DisasterHelpers.BurnGround)");
            }
            else
            {
                b.Line(1, "lava scorch", "NOT RESOLVED");
                b.Line(2, "consequence",
                    "the ground is not scorched along the lava; the mountain, the summit "
                    + "crater and the lava themselves still work");
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
