using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤の位相。**バニラの災害スロットに載らない**（設計書 §2）ので、⑤は自前の
    /// 位相機械で動く。
    ///
    /// <see cref="Idle"/> / <see cref="Done"/> / <see cref="Refused"/> の 3 つが
    /// 「今は何も進んでいない」であり、そこからしか新しい火山は始まらない。
    ///
    /// ★★ <b>1 番（Surveying）と 2 番（AwaitingConfirmation）は 2026-08-21 に退役した。</b>
    /// 確認の窓を撤去したので、クリック 1 回で
    /// <c>Idle</c> → <c>Clearing</c>（または <c>Refused</c>）まで**同じ tick の中で**進む。
    /// 調査そのものは残っているが、**外から観測できる位相ではなくなった**ので
    /// 位相にもしていない（誰も到達できない状態を残さない）。
    /// **番号は詰めていない** —— 退役した番号を別の意味で使い回さないためである。
    /// </summary>
    public enum VolcanoPhase
    {
        /// <summary>何も無い。</summary>
        Idle = 0,

        // 1 = Surveying（退役）／2 = AwaitingConfirmation（退役）。再利用しないこと。

        /// <summary>準備（道路と建物の段階的破壊）。T5。</summary>
        Clearing = 3,

        /// <summary>隆起。T6。</summary>
        Uplifting = 4,

        /// <summary>噴火。T7。</summary>
        Erupting = 5,

        /// <summary>溶岩の前進。T8。</summary>
        Flowing = 6,

        /// <summary>溶岩が冷える。T8。</summary>
        Cooling = 7,

        /// <summary>終わった。**地形はそのまま残る（不可逆）。**</summary>
        Done = 8,

        /// <summary>断った。理由は <see cref="VolcanoState.LastRefusal"/>。</summary>
        Refused = 9,

        /// <summary>
        /// **破局噴火だけ。** 巨大なマグマだまりが育って、山より広い地面が
        /// ドーム状に膨らむ（<c>SuperEruption.InflationAt</c>）。
        /// スライダーが上端（表示 25.5）のときにだけ通る。
        /// </summary>
        Inflating = 10,

        /// <summary>
        /// **破局噴火だけ。** 空になったマグマだまりの屋根が自重で抜け、
        /// 平底のカルデラが落ちる（<c>SuperEruption.BowlProfileAt</c>）。
        /// </summary>
        Collapsing = 11,
    }

    /// <summary>
    /// ⑤の位相機械。**sim スレッド専用。**
    ///
    /// ── クリック 1 回で着手まで行く（2026-08-21 変更）───────────────
    ///
    /// 所有者の指示:
    ///
    /// > ほかの災害と同じようにタブから選択してスケール選択して発生個所押したら
    /// > その災害が起こるようにしてください。
    ///
    /// **確認の窓は撤去した。** クリックが「作る」であり、そこから先に人の入る隙は無い。
    ///
    /// <code>
    /// [main] クリック → RayGeometry.IntersectTerrain で地点を取る
    ///                 → VolcanoHub.Request(Place, point, sizeScale)
    ///                 → ツールを解除する（パネルは開かない）
    ///         v
    /// [sim ] VolcanoState.HandleRequest が TakeRequest() で拾う
    ///                 → VolcanoSurvey.Run(...) が建物と道路を数え、Footprint を作る
    ///                 → Phase = Clearing（T5 が動き出す）
    /// </code>
    ///
    /// ★ **調査は無くなっていない。** <c>BuildingManager.m_buildingGrid</c> と
    /// <c>NetManager.m_segmentGrid</c> は sim スレッドが所有しているので、
    /// **main スレッド（ツールのクリックハンドラ）から数えることはできない**。
    /// 変わったのは「調査の結果を人に見せて待つ」段が消えたことだけで、
    /// main → sim の 1 往復は今も要る。
    ///
    /// ★ 調べた影響範囲の数（建物・道路）と、実際に届いた山頂の高さと、断った理由は
    ///   **火山タブ（<c>VolcanoEffectRows</c> / <c>VolcanoStatusRows</c>）と
    ///   診断ダンプ（<c>VolcanoFeature</c>）**にある。消えたのは
    ///   「先に読ませて止める」段であって、情報そのものではない。
    ///
    /// ── ポーズ中に指しても捨てない ─────────────────────────
    ///
    /// 依頼の受け取り（<see cref="HandleRequest"/>）は
    /// <c>VolcanoFeature.OnSimulationTick</c> の**ポーズガードより上**にあり、
    /// 位相の前進（<see cref="Tick"/>）だけがガードの下にある。したがって
    /// ポーズ中のクリックは<b>位相を <c>Clearing</c> にするところまで</b>進み、
    /// **建物も道路も地形も 1 つも変わらないまま**、解除した瞬間から動き出す ——
    /// バニラの災害をポーズ中に起こしたときと同じ挙動である。
    /// **黙って捨てない**（このクラス doc がいちばん禁じている形）。
    ///
    /// ── 同時に 1 つだけ ─────────────────────────────────
    ///
    /// 進行中（<see cref="VolcanoPhase.Clearing"/> 以降）なら <c>Place</c> は無視し、
    /// 理由を <see cref="LastRefusal"/> に残す。止めたいときは <c>Stop</c> である。
    ///
    /// **黙って何もしないをやらない。** 断ったときは必ず <see cref="LastRefusal"/> に
    /// 英語 1 文を残す（④の <c>TyphoonSnapshot.Refusal</c> と同じ扱い）。
    /// </summary>
    public static class VolcanoState
    {
        /// <summary>
        /// 溶岩が流れはじめる隆起の進捗。**この MOD が決めた演出値である。**
        /// 0 にすると平らな地面から溶岩を出すことになり、勾配が無いのでその場で溜まる
        /// （<c>LavaPath.MinSlope</c>）。0.6 なら円錐は最終形の 6 割まで立っている。
        /// </summary>
        private const float LavaDuringUpliftFrom = 0.6f;

        private static VolcanoPhase _phase = VolcanoPhase.Idle;
        private static VolcanoFootprint _footprint = VolcanoFootprint.None;
        private static string _lastRefusal;

        /// <summary>
        /// この火山が破局噴火か（スライダーが上端だったか）。
        /// **置いた瞬間に 1 度だけ決まる** —— 途中でスライダーを動かされても
        /// 進行中の火山の筋書きは変わらない。
        /// </summary>
        private static bool _super;

        /// <summary>今の位相。</summary>
        public static VolcanoPhase Phase { get { return _phase; } }

        /// <summary>
        /// 進行中の火山が破局噴火か。表示と診断が「なぜ地面がこんなに動くのか」を
        /// 名乗るのに使う。
        /// </summary>
        public static bool IsSupereruption { get { return _super; } }

        /// <summary>
        /// 膨らみの段の影響範囲（山より広い）。破局噴火でなければ
        /// <see cref="Footprint"/> と同じ。
        /// </summary>
        private static VolcanoFootprint InflationFootprint
        {
            get
            {
                return _footprint.Resized(
                    SuperEruption.InflationRadiusMetres(_footprint.RadiusMetres),
                    SuperEruption.InflationHeightMetres(_footprint.HeightMetres));
            }
        }

        /// <summary>カルデラの段の影響範囲（山より広く、**深さは正の値**で入る）。</summary>
        private static VolcanoFootprint CalderaFootprint
        {
            get
            {
                return _footprint.Resized(
                    SuperEruption.CalderaRadiusMetres(_footprint.RadiusMetres),
                    SuperEruption.CalderaDepthMetres(_footprint.HeightMetres));
            }
        }

        /// <summary>直近の調査結果。<c>Valid == false</c> なら「まだ調べていない」。</summary>
        public static VolcanoFootprint Footprint { get { return _footprint; } }

        /// <summary>
        /// 隆起の進捗 [0,1]。T6 以降は <see cref="VolcanoUplift.ProgressUnit"/> そのもの。
        /// <b>0 のうちは行にしないこと</b> —— 「進捗 0%」は「進んでいない」ではなく
        /// 「まだ隆起という段に入っていない」だからである。
        /// </summary>
        public static float ProgressUnit { get { return VolcanoUplift.ProgressUnit; } }

        /// <summary>
        /// 直近に断った理由（**英語・診断用**）。断っていなければ null。
        /// 翻訳は無いが、出さないほうが悪い —— これが「起こせなかった」の
        /// 唯一の手がかりである（④の <c>TyphoonSnapshot.Refusal</c> と同じ判断）。
        /// </summary>
        public static string LastRefusal { get { return _lastRefusal; } }

        /// <summary>
        /// レベルのロード／アンロードで呼ぶ。**全状態を捨てる。**
        /// 持ち越すと、次の都市で前の都市の地点の火山が動き続ける。
        /// </summary>
        public static void Reset()
        {
            _phase = VolcanoPhase.Idle;
            _footprint = VolcanoFootprint.None;
            _lastRefusal = null;
            _super = false;
            // 準備の実績も持ち越さない。**進行中の火山は保存しない**ので、
            // 都市を出入りすると準備は 0 からになる（地形はそのままの形で残る）。
            VolcanoClearing.Reset();
            // ★ 隆起の退避配列も返す（半径 3 km で 279 KB）。**地形は戻らない。**
            VolcanoUplift.Reset();
            // ★ 噴火の予定も畳む。**描画側（main）の後始末はここではしない** ——
            //   Unity オブジェクトの破棄は main スレッドの仕事で、
            //   VolcanoEruptionFx がスナップショットを見て自分で畳む
            //   （レベルアンロードでは VolcanoFeature が Destroy を呼ぶ）。
            VolcanoEruption.Reset();
            // ★ 溶岩の軌跡も返す（8 本 × 128 点で 8 KB）。**焦げた地面と燃えた建物は
            //   戻らない** —— 捨てるのは「これからの予定」だけである。
            VolcanoLava.Reset();
            // ★ 地震計へ記録している火山性地震も畳む。**次の都市／次の火山へ
            //   前の揺れを持ち越さない。**
            VolcanoTremorTrace.Reset();
        }

        /// <summary>
        /// 積まれている依頼を 1 件だけ拾って答える。**sim スレッド。**
        ///
        /// ★★ <b>これは <c>VolcanoFeature.OnSimulationTick</c> のポーズガードより
        /// <u>上</u>から呼ぶ</b>（全体レビュー I1）。ガードの下に置いていた頃、
        /// **ポーズ中に地面をクリックしたプレイヤーには何も起きなかった** ——
        /// 状態の行は止まったまま、ログにも診断にも何も残らなかった。
        /// **山を作る前にポーズするのは最も自然な操作**であり、そこが
        /// 「黙って何もしない」になっていた（クラス doc がまさに禁じている形）。
        ///
        /// ポーズ中でも <c>Place</c> を受けるが、**位相を <c>Clearing</c> にするだけ**で
        /// 建物も道路も地形も 1 つも変わらない —— 実際に壊し始めるのは
        /// <see cref="Tick"/> であり、あちらはポーズガードの下に在る。
        /// バニラの災害をポーズ中に起こしたときと同じ挙動である。
        ///
        /// <see cref="VolcanoHub.TakeRequest"/> は<b>1 tick にちょうど 1 回</b>
        /// しか呼ばない（2 回呼ぶと 2 回目が必ず None になり、呼び出し順に依存した
        /// 取りこぼしを作る。あちらの doc）。**その 1 回はここである。**
        /// </summary>
        public static void HandleRequest(VolcanoSnapshot snapshot)
        {
            VolcanoRequestData request = VolcanoHub.TakeRequest();
            if (request.Kind == VolcanoRequest.None) return;

            // ★ 述語は⑤の門そのもの（VolcanoTerrainFacts.Usable）。
            //   「フィールドが解決した」で通すと、値が使えない環境で着手できてしまう。
            bool terrainUsable = snapshot != null && snapshot.Valid && snapshot.Terrain.Usable;
            if (!terrainUsable)
            {
                Refuse("the terrain write path is not usable in this build of the game; "
                       + "no volcano can be placed");
                return;
            }

            switch (request.Kind)
            {
                case VolcanoRequest.Place:
                    HandlePlace(request.Point, request.SizeScale, request.SizeRaw);
                    break;

                case VolcanoRequest.Stop:
                    HandleStop();
                    break;
            }
        }

        /// <summary>
        /// 位相を前へ進める。**必ず <c>VolcanoFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に山が育ち、建物が消える）。
        ///
        /// 依頼の受け取りはここには無い（<see cref="HandleRequest"/> がガードの上で
        /// 済ませている）。
        /// </summary>
        public static void Tick(VolcanoSnapshot snapshot, uint frame, float deltaMinutes)
        {
            // ★ 述語は⑤の門そのもの（VolcanoTerrainFacts.Usable）。
            bool terrainUsable = snapshot != null && snapshot.Valid && snapshot.Terrain.Usable;
            if (!terrainUsable) return;

            StepPhase(frame, deltaMinutes);
        }

        /// <summary>
        /// 位相ごとの前進。**実処理はこのファイルに書かない** ——
        /// <see cref="VolcanoClearing"/> / <c>VolcanoUplift</c> / <c>VolcanoLava</c> に置く
        /// （800 行の規則）。ここに書いてよいのは「どれをどの順で呼ぶか」だけである。
        ///
        /// ★★ <b>準備 → 隆起の順序は、ここでしか壊れない。</b> 順序を入れ替えると
        /// 道路と建物が毎フラッシュ地形を押し戻して、山の中に平らな溝とすり鉢が残る
        /// （設計書 §1.2 / §A-2）。型の側の担保は
        /// <c>UpliftSchedule.ActiveRadiusMetres(R, VolcanoClearing.ClearedRadiusMetres)</c> で、
        /// 準備が届いていなければ 0 が返る。
        /// </summary>
        private static void StepPhase(uint frame, float deltaMinutes)
        {
            if (!_footprint.Valid) return;

            if (_phase == VolcanoPhase.Clearing)
            {
                // 隆起はまだ 1 度も動いていないので進捗は 0。前線は
                // ModSettings.VolcanoClearingLeadMetres のぶんだけ先へ出る。
                VolcanoClearing.Tick(_footprint, 0f, deltaMinutes);

                if (!VolcanoClearing.FrontReached) return;

                // 最初の前線まで届いた。ここから先は隆起が進捗を持ち、
                // 準備はその前を走る（ring lockstep）。
                _phase = VolcanoPhase.Uplifting;
                Log.Info("volcano clearing reached its first front ("
                         + VolcanoClearing.ClearedRadiusMetres.ToString("F0")
                         + " m); the uplift starts now");
                return;
            }

            if (_phase == VolcanoPhase.Inflating)
            {
                // ★★ **数万年かけたマグマだまりの成長**（所有者の依頼）。
                //    山より広い地面が、山よりずっと低くドーム状に膨らむ。
                //    ここではまだ噴火していない —— 噴煙も溶岩も出さない。
                VolcanoFootprint bulge = InflationFootprint;
                VolcanoClearing.Tick(bulge, VolcanoUplift.GrowthFrontUnit, deltaMinutes);
                VolcanoUplift.Tick(bulge, frame, deltaMinutes);

                if (!VolcanoUplift.Complete) return;

                _phase = VolcanoPhase.Erupting;
                Log.Info("supereruption: the magma chamber finished inflating (+"
                         + bulge.HeightMetres.ToString("F0") + " m over "
                         + bulge.RadiusMetres.ToString("F0")
                         + " m); the chamber is now at its pressure limit and erupts");
                return;
            }

            if (_phase == VolcanoPhase.Collapsing)
            {
                // ★★ **地面の自重による大陥没**（所有者の依頼）。空になった
                //    マグマだまりの屋根が落ちる。<b>唯一、地面を下げる段である。</b>
                VolcanoFootprint caldera = CalderaFootprint;
                VolcanoClearing.Tick(caldera, VolcanoUplift.GrowthFrontUnit, deltaMinutes);
                VolcanoUplift.Tick(caldera, frame, deltaMinutes);

                // 溶岩は陥没のあいだも流れ続ける（止めるとここだけ絵が凍る）。
                VolcanoLava.Tick(_footprint, frame, deltaMinutes,
                                 VolcanoUplift.RiseMetresPerFrame);

                if (!VolcanoUplift.Complete) return;

                _phase = VolcanoPhase.Flowing;
                Log.Info("supereruption: the caldera finished collapsing (-"
                         + caldera.HeightMetres.ToString("F0") + " m over "
                         + caldera.RadiusMetres.ToString("F0")
                         + " m); the terrain stays as it is (this is irreversible)");
                return;
            }

            if (_phase == VolcanoPhase.Erupting)
            {
                // T7。**噴出の予定を決めるだけ**で、地形も建物も 1 つも変えない。
                //   山はもうできあがっているので進捗に 1 を渡す（＝包絡線が持続から
                //   衰退へ進む）。噴火そのものは隆起の最初から続いている。
                VolcanoEruption.Tick(_footprint, frame, deltaMinutes, 1f);

                // ★ 溶岩は隆起の途中から出ているので、ここでも進め続ける ——
                //   止めると噴火のあいだだけ流れが凍りつく。地形はもう動かないので
                //   許容差は 0（VolcanoUplift.RiseMetresPerFrame が完了後 0 を返す）。
                VolcanoLava.Tick(_footprint, frame, deltaMinutes,
                                 VolcanoUplift.RiseMetresPerFrame);

                if (!VolcanoEruption.Finished) return;

                // ★★ **破局噴火だけ、噴火のあとに陥没がある**（所有者の依頼の 4 段目）。
                //    ここで StartStage を明示的に呼ぶ —— VolcanoUplift が自分から
                //    始めるのは円錐だけで、あれは今 Complete のまま止まっている。
                if (_super)
                {
                    VolcanoFootprint caldera = CalderaFootprint;
                    if (VolcanoUplift.StartStage(caldera, UpliftStage.Collapse))
                    {
                        _phase = VolcanoPhase.Collapsing;
                        Log.Info("supereruption: the eruption emptied the chamber after "
                                 + VolcanoEruption.BurstsSoFar
                                 + " bursts; the roof now collapses into a caldera of r="
                                 + caldera.RadiusMetres.ToString("F0") + " m, depth "
                                 + caldera.HeightMetres.ToString("F0") + " m");
                        return;
                    }

                    // ★ **黙って飛ばさない。** 掘れなかった理由を残して、
                    //   ふつうの噴火と同じ終わり方へ落とす。
                    Refuse("the caldera collapse could not start ("
                           + (VolcanoUplift.LastFailure ?? "unknown reason")
                           + "); the volcano finishes without one");
                }

                // ★ T8 がここを <c>Done</c> から <c>Flowing</c> に差し替えた。
                //   位相が進行中のまま止まらないことは、溶岩の側の 2 つの有限性が
                //   担保する —— 1 本の流れは <c>LavaPath.MaxSteps</c> で必ず止まり、
                //   全部止まったあとは <c>CoolMinutes</c> で必ず冷え切る。
                _phase = VolcanoPhase.Flowing;
                Log.Info("volcano eruption finished after "
                         + VolcanoEruption.BurstsSoFar + " bursts; the lava starts now");
                return;
            }

            if (_phase == VolcanoPhase.Flowing)
            {
                // T8。**地形は変えない**（RawHeights を書くのは T6 だけ）。
                VolcanoLava.Tick(_footprint, frame, deltaMinutes, 0f);

                // 本数 0（設定で無効）のときは 1 度も流れずにここを抜ける。
                if (!VolcanoLava.AllStopped && !VolcanoLava.Finished) return;

                _phase = VolcanoPhase.Cooling;
                Log.Info("volcano lava stopped: " + VolcanoLava.FlowCount + " flows, longest "
                         + VolcanoLava.LongestMetres.ToString("F0") + " m, ignited "
                         + VolcanoLava.BuildingsIgnited + " buildings and "
                         + VolcanoLava.TreesIgnited + " trees");
                return;
            }

            if (_phase == VolcanoPhase.Cooling)
            {
                // 冷えるのを待つだけ。**新しい流れは出さない。**
                VolcanoLava.Tick(_footprint, frame, deltaMinutes, 0f);

                if (!VolcanoLava.Finished) return;

                _phase = VolcanoPhase.Done;
                Log.Info("volcano finished; the terrain stays as it is (this is irreversible)");
                return;
            }

            if (_phase != VolcanoPhase.Uplifting) return;

            // ★★ **順序がこの 2 行そのものである**（設計書 §1.2 / 罠 1）。
            //    準備を先に、隆起の進捗を渡して前へ走らせ、そのあとで隆起が
            //    「準備が届いた半径」の内側だけを上げる。入れ替えてはいけない。
            //   ★ 渡すのは進捗ではなく**隆起の前線**である（火口のぶん円錐を立て直して
            //     いるので、前線は進捗より先に出る。VolcanoClearing.Tick の doc）。
            VolcanoClearing.Tick(_footprint, VolcanoUplift.GrowthFrontUnit, deltaMinutes);
            VolcanoUplift.Tick(_footprint, frame, deltaMinutes);

            // ★★ **SimCity 4 の順序。噴火が先で、山はそれに積み上げられる。**
            //    以前はここが「隆起が終わってから噴火」で、できあがった山が音もなく
            //    地面から膨らんだあとに煙が出ていた。噴煙と発光は隆起の 1 tick 目から出す。
            //    T7 は**予定を決めるだけ**で地形も建物も 1 つも変えないので、
            //    準備 → 隆起の順序（罠 1）には触れていない。
            VolcanoEruption.Tick(_footprint, frame, deltaMinutes, VolcanoUplift.ProgressUnit);

            // ★ 溶岩も山ができきる前から流れはじめる。**円錐がある程度立ってから**に
            //   してあるのは、平らな地面から出しても勾配が無くてその場で溜まるだけだからで、
            //   閾値そのものは演出値である（LavaDuringUpliftFrom）。
            //   地形はまだ上がっているので、その量を許容差として渡す
            //   （渡さないと「溶岩が登った」と誤って観測して流れが止まる）。
            if (VolcanoUplift.ProgressUnit >= LavaDuringUpliftFrom)
            {
                VolcanoLava.Tick(_footprint, frame, deltaMinutes,
                                 VolcanoUplift.RiseMetresPerFrame);
            }

            if (!VolcanoUplift.Complete) return;

            // ★ T7 がここを <c>Done</c> から <c>Erupting</c> に差し替えた。位相が
            //   進行中のまま止まらないことは <c>VolcanoEruption.Finished</c> が担保する
            //   （噴火は必ず有限のゲーム内時間で終わり、例外が出た場合も終わる）。
            _lastRefusal = null;

            // ★★ **破局噴火だけ、山ができたあとにマグマだまりが育つ**
            //    （所有者の依頼の 2 段目）。ふつうの噴火はそのまま Erupting へ。
            if (_super && VolcanoUplift.Stage == UpliftStage.Cone)
            {
                VolcanoFootprint bulge = InflationFootprint;
                if (VolcanoUplift.StartStage(bulge, UpliftStage.Inflation))
                {
                    _phase = VolcanoPhase.Inflating;
                    Log.Info("supereruption: the cone is up; a magma chamber now inflates the "
                             + "ground over r=" + bulge.RadiusMetres.ToString("F0") + " m by "
                             + bulge.HeightMetres.ToString("F0") + " m");
                    return;
                }

                Refuse("the magma chamber inflation could not start ("
                       + (VolcanoUplift.LastFailure ?? "unknown reason")
                       + "); the volcano erupts without one");
            }

            _phase = VolcanoPhase.Erupting;
            Log.Info("volcano uplift complete (the eruption has been running since it started): summit +"
                     + VolcanoUplift.SummitMetres.ToString("F0")
                     + " m, crater " + (VolcanoUplift.CraterFormed ? "at full depth" : "SHALLOW")
                     + "; the eruption starts now");

            // ★★ **山頂がゲームの高さの天井で削られたなら、そう言う。**
            //    黙って平らな山頂を出すと、プレイヤーからは
            //    「高さの設定が効いていない」にしか見えない。
            //    天井は MOD からは上げられない（UpliftSchedule.CeilingClipped の doc）。
            if (VolcanoUplift.CeilingClippedCells > 0)
            {
                Log.Info("volcano summit was clipped by the game's terrain ceiling ("
                         + UpliftSchedule.CeilingMetres.ToString("F0")
                         + " m) on " + VolcanoUplift.CeilingClippedCells
                         + " cells; the top is flat there. The ceiling cannot be raised by a "
                         + "mod - place the volcano on lower ground or reduce its height");
            }
        }

        /// <summary>
        /// **地図をクリックされた。ここが「作る」である。**
        ///
        /// 調査（<c>VolcanoSurvey.Run</c>）と着手を 1 つの呼び出しで済ませる ——
        /// 確認の窓が無くなったので、この 2 つの間に人の判断は入らない
        /// （クラス doc）。**この 2 つを別の位相に割らないこと**:
        /// 間の状態は誰も観測できず、到達不能な位相が 1 つ増えるだけである。
        ///
        /// 断る順序は「進行中 → 破壊経路が無い → 調査が失敗」で、
        /// **どれも 1 つも壊す前に返る**。理由は必ず <see cref="LastRefusal"/> に残す。
        /// </summary>
        private static void HandlePlace(Vec3 point, float sizeScale, int sizeRaw)
        {
            if (InProgress())
            {
                Refuse("a volcano is already in progress (phase=" + _phase
                       + "); the placement request was ignored. Stop it first");
                return;
            }

            // ★★ **準備の破壊経路が無い環境では、1 つも壊さずにここで断る**
            //    （設計書 §1.2 / T5 Step 1）。「道路だけ諦めて隆起する」は選ばない ——
            //    それは §1.2 が発見した失敗（山の中の平らな溝）を、分かったうえで
            //    出荷することになる。**判定は着手の直前に、破壊より先に置く。**
            //
            //    ★ 述語は <c>VolcanoClearing.Sweep</c> が実際に門にしている式と同じ
            //      （全体レビュー M9）。道路側だけを見ていた頃は、建物側が解決できない
            //      環境で **火山が確定して <c>Clearing</c> のまま永久に止まった。**
            if (!VolcanoClearing.ClearingPathAvailable)
            {
                RefuseAndForget("no usable destruction path for the roads and buildings in "
                                + "this build of the game; raising the ground would leave flat "
                                + "trenches and bowls where they stand");
                return;
            }

            // ★ 倍率は**依頼が運んできたもの**を使う。ここでスライダーを読み直す
            //   ことはできない（sim スレッドから UI に触らない）し、読み直せたと
            //   しても「クリックした時の値」ではなくなる。
            VolcanoForm form = CurrentForm();
            VolcanoFootprint footprint;

            // ★★ **スライダーが上端のときだけ破局噴火**（所有者の依頼）。
            //    生値で判定する —— 倍率は帯でクランプされたあとの値なので、
            //    上端かどうかがもう分からない。
            //    **調査より先に決める**（下で半径の読み方が変わる）。
            _super = SuperEruption.IsSuper(sizeRaw);

            // ★★ **破局噴火では、頼まれた大きさは「カルデラ」の大きさである。**
            //
            //    そうしないと絵にならない。カルデラは円錐の 1.9 倍なので、
            //    円錐を頼まれた半径いっぱい（成層火山なら 5564 m）で立てると
            //    カルデラは 10.6 km を要求し、実費の上限（6 km）で切られて
            //    **山とほぼ同じ大きさの穴**になる —— 陥没が見えない。
            //
            //    実際の超巨大火山（Yellowstone・Toba）にも**大きな円錐は無い**。
            //    在るのはカルデラである。だから 25.5 では、頼まれた半径を
            //    カルデラの半径として読み、円錐はそこから割り戻す:
            //
            //      円錐 = R / 1.9 → 膨らみ = 円錐 × 2.4 → カルデラ = 円錐 × 1.9 = R
            //
            //    ★ 高さは割り戻さない。低い山が落ちても陥没に見えないので、
            //      円錐は頼まれた高さのまま立てる。
            float requestedRadius =
                VolcanoSizeScale.Apply(VolcanoShape.DefaultRadiusOf(form), sizeScale);
            //
            //    ★ 上限も割り戻す。<c>SuperEruption.MaxRadiusMetres</c> で頭を
            //      押さえているのは**カルデラ**なので、円錐をそれより大きく立てると
            //      カルデラだけが天井に当たって、また「山と同じ大きさの穴」に戻る
            //      （楯状火山は推奨半径が 2 km あるので、ここが無いと必ずそうなる）。
            if (_super)
            {
                float coneCeiling =
                    SuperEruption.MaxRadiusMetres / SuperEruption.CalderaRadiusFactor;
                requestedRadius /= SuperEruption.CalderaRadiusFactor;
                if (requestedRadius > coneCeiling) requestedRadius = coneCeiling;
            }

            if (!VolcanoSurvey.Run(
                    point, form,
                    // ★★ 基準は**形態ごとの推奨値**である（2026-08-22）。
                    //    設定画面の半径・最終高のスライダーは撤去した ——
                    //    同じ量を 2 つのつまみで決めさせていた（<c>VolcanoSizeScale</c>）。
                    requestedRadius,
                    VolcanoSizeScale.Apply(VolcanoShape.DefaultHeightOf(form), sizeScale),
                    out footprint))
            {
                RefuseAndForget(VolcanoSurvey.LastFailure
                                ?? "the survey failed for an unknown reason");
                return;
            }

            _footprint = footprint;
            _lastRefusal = null;
            // ★ ここから先が「壊す」である。T5 の VolcanoClearing がこの位相を動かす。
            //   ポーズ中なら位相がここまで進むだけで、実際の破壊は解除まで始まらない
            //   （VolcanoFeature のポーズガード）。
            _phase = VolcanoPhase.Clearing;
            Log.Info("volcano placed at (" + _footprint.Centre.X.ToString("F0") + ","
                     + _footprint.Centre.Z.ToString("F0") + "): " + _footprint.Form
                     + " r=" + _footprint.RadiusMetres.ToString("F0")
                     + " m h=" + _footprint.HeightMetres.ToString("F0")
                     + " m; clearing starts now"
                     + (_super
                        ? ". THIS IS A SUPERERUPTION (the slider is at its top). The cone is "
                          + "deliberately SMALLER than at lower settings - at 25.5 the size you "
                          + "picked is the size of the CALDERA, not of the mountain (real "
                          + "supervolcanoes have no big cone). It will grow, then a magma "
                          + "chamber will inflate the ground over r="
                          + SuperEruption.InflationRadiusMetres(_footprint.RadiusMetres)
                                .ToString("F0")
                          + " m, then it erupts and collapses into a caldera of r="
                          + SuperEruption.CalderaRadiusMetres(_footprint.RadiusMetres)
                                .ToString("F0")
                          + " m"
                        : ""));
        }

        /// <summary>
        /// 進行中の火山を止める。**既に変わった地形は戻らない**（設計書 §7.1 / §E-13）。
        /// 止まるのは「これからの破壊と隆起」だけである。
        ///
        /// ★ 依頼を積むのは <see cref="VolcanoEffectRows"/> の [止める] ボタン 1 箇所だけで、
        ///   そのボタンは進行中の位相のときしか出ない（全体レビュー I5）。
        ///   それでも位相を見るのは、押した直後の 1 tick に位相が変わりうるからである。
        /// </summary>
        private static void HandleStop()
        {
            if (!InProgress())
            {
                Refuse("no volcano is in progress (phase=" + _phase
                       + "); the stop request was ignored");
                return;
            }

            _phase = VolcanoPhase.Idle;
            _footprint = VolcanoFootprint.None;
            _super = false;
            // ★ 準備の実績も畳む。**既に壊した建物と道路は戻らない**（不可逆）。
            //   畳まないと、次に開いたパネルが前の火山の破壊数を名乗る。
            VolcanoClearing.Reset();
            // ★ 隆起の退避配列も返す（半径 3 km で 279 KB）。**地形は戻らない。**
            VolcanoUplift.Reset();
            // ★ 噴火の予定も畳む。**描画側（main）の後始末はここではしない** ——
            //   Unity オブジェクトの破棄は main スレッドの仕事で、
            //   VolcanoEruptionFx がスナップショットを見て自分で畳む
            //   （レベルアンロードでは VolcanoFeature が Destroy を呼ぶ）。
            VolcanoEruption.Reset();
            // ★ 溶岩の軌跡も返す（8 本 × 128 点で 8 KB）。**焦げた地面と燃えた建物は
            //   戻らない** —— 捨てるのは「これからの予定」だけである。
            VolcanoLava.Reset();
            VolcanoTremorTrace.Reset();
            _lastRefusal = "stopped by the player; the terrain that already changed stays changed";
        }

        /// <summary>
        /// 「もう新しい火山は始められない」位相か。**壊し始めてからの 5 つ**である。
        /// </summary>
        private static bool InProgress()
        {
            return _phase == VolcanoPhase.Clearing
                   || _phase == VolcanoPhase.Uplifting
                   || _phase == VolcanoPhase.Inflating
                   || _phase == VolcanoPhase.Collapsing
                   || _phase == VolcanoPhase.Erupting
                   || _phase == VolcanoPhase.Flowing
                   || _phase == VolcanoPhase.Cooling;
        }

        private static void Refuse(string reason)
        {
            _lastRefusal = reason;
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcState", reason);
        }

        /// <summary>
        /// 置こうとした地点そのものを断る。**位相を <see cref="VolcanoPhase.Refused"/> へ
        /// 落として調査結果も捨てる** —— 断ったのに前の火山の影響範囲が残っていると、
        /// 火山タブが「作られなかった山」の数を名乗り続ける。
        /// </summary>
        private static void RefuseAndForget(string reason)
        {
            _footprint = VolcanoFootprint.None;
            // ★ 作らなかった火山の筋書きを持ち越さない。
            _super = false;
            _phase = VolcanoPhase.Refused;
            Refuse(reason);
        }

        /// <summary>設定の形態。範囲外の値は <c>VolcanoShape.FormOf</c> が既定へ落とす。</summary>
        private static VolcanoForm CurrentForm()
        {
            return VolcanoShape.FormOf(ModSettings.VolcanoShapeSetting.value);
        }

    }
}
