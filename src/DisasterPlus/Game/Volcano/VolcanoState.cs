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
    /// </summary>
    public enum VolcanoPhase
    {
        /// <summary>何も無い。</summary>
        Idle = 0,

        /// <summary>調査中（実際には 1 tick で終わるので、外から見えるのは一瞬）。</summary>
        Surveying = 1,

        /// <summary>**プレイヤーの確認待ち。まだ何も壊していない。**</summary>
        AwaitingConfirmation = 2,

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
    }

    /// <summary>
    /// ⑤の位相機械。**sim スレッド専用。**
    ///
    /// ── クリックから着手までは 2 往復する（計画 §4.1）─────────────────
    ///
    /// <c>BuildingManager.m_buildingGrid</c> と <c>NetManager.m_segmentGrid</c> は
    /// sim スレッドが所有しているので、**main スレッド（ツールのクリックハンドラ）から
    /// 数えることはできない**。したがって⑤は必ず次の 2 往復を通る。
    /// **1 往復で済ませようとしてはいけない。**
    ///
    /// <code>
    /// [main] クリック → RayGeometry.IntersectTerrain で地点を取る
    ///                 → VolcanoHub.Request(Survey, point)
    ///                 → ツールを解除し、パネルを開く（「調査しています」）
    ///         v
    /// [sim ] VolcanoState.Tick が TakeRequest() で拾う
    ///                 → VolcanoSurvey.Run(...) が建物と道路を**数えるだけ**
    ///                 → Phase = AwaitingConfirmation、Footprint をスナップショットへ
    ///         v
    /// [main] パネルが確認の行を出す（VolcanoConfirmRows）
    ///                 → プレイヤーが [この場所に火山を作る] を押す
    ///                 → VolcanoHub.Request(Start, point)
    ///         v
    /// [sim ] VolcanoState.Tick が拾い、Phase = Clearing へ（T5）
    /// </code>
    ///
    /// ── 確認は迂回できない ────────────────────────────────
    ///
    /// <see cref="VolcanoRequest.Start"/> を受け付けるのは
    /// <see cref="VolcanoPhase.AwaitingConfirmation"/> のときだけである。
    /// 配置ツールは <see cref="VolcanoRequest.Survey"/> しか積まない（あちらのクラス doc）。
    ///
    /// **レビューの grep（実際に走らせて件数を合わせてある）**:
    /// <code>
    /// grep -rn --include=*.cs "VolcanoRequest.Start" src/DisasterPlus | grep -v '///'
    /// # -> 2 件だけ:
    /// #    VolcanoConfirmRows.cs  … 確認ボタンが積む唯一の場所
    /// #    VolcanoState.cs        … 受け口（このファイル）
    /// # 列挙の宣言（VolcanoHub.cs）は "Start," と書くのでこの grep には当たらない。
    /// </code>
    ///
    /// さらに <see cref="VolcanoRequest.Start"/> は<b>依頼に載っている座標を使わない</b>。
    /// 使うのは sim 側が持っている <see cref="Footprint"/> の中心である ——
    /// 「プレイヤーが見た概数」と「実際に着手する場所」がずれないことを、
    /// 型ではなくこの 1 行で担保する。
    ///
    /// ── 確認の直前に設定が変わっていたら、着手せずに調べ直す ─────────────
    ///
    /// <see cref="Footprint"/> は形態・半径・最終高を持っているので、
    /// <c>Start</c> を受けた時点で現在の設定と突き合わせる。違っていたら調査からやり直し、
    /// パネルは <c>Strings.VolcanoSettingsChanged</c> を出す。**古い概数で壊し始めない。**
    ///
    /// ── 同時に 1 つだけ ─────────────────────────────────
    ///
    /// 進行中（<see cref="VolcanoPhase.Clearing"/> 以降）なら <c>Survey</c> も
    /// <c>Start</c> も無視し、理由を <see cref="LastRefusal"/> に残す。
    ///
    /// > **計画 §4.1 からの 1 点の逸脱を明記する。** 計画は
    /// > 「Idle / Done / Refused 以外なら <c>Survey</c> も無視」と書いているが、
    /// > <see cref="VolcanoPhase.AwaitingConfirmation"/> は**まだ 1 つも壊していない状態**
    /// > である。ここで <c>Survey</c> を無視すると、確認を出したまま別の場所を
    /// > 指し直したプレイヤーに対して**画面が何も変わらない行き止まり**ができる
    /// > （配置ツールは押せるし、クリックも通るのに、結果だけが黙って捨てられる）。
    /// > したがって <c>AwaitingConfirmation</c> からの <c>Survey</c> は受け付け、
    /// > 前の調査結果を置き換える。計画の意図（**同時に 1 つの火山だけ**）は
    /// > <c>Clearing</c> 以降で守られている。
    ///
    /// **黙って何もしないをやらない。** 断ったときは必ず <see cref="LastRefusal"/> に
    /// 英語 1 文を残す（④の <c>TyphoonSnapshot.Refusal</c> と同じ扱い）。
    /// </summary>
    public static class VolcanoState
    {
        private static VolcanoPhase _phase = VolcanoPhase.Idle;
        private static VolcanoFootprint _footprint = VolcanoFootprint.None;
        private static string _lastRefusal;
        private static bool _settingsChanged;

        /// <summary>今の位相。</summary>
        public static VolcanoPhase Phase { get { return _phase; } }

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
        /// 確認の直前に設定が変わったので調べ直したか。パネルが
        /// <c>Strings.VolcanoSettingsChanged</c> を出すためだけに在る。
        /// </summary>
        public static bool SettingsChanged { get { return _settingsChanged; } }

        /// <summary>
        /// レベルのロード／アンロードで呼ぶ。**全状態を捨てる。**
        /// 持ち越すと、次の都市で前の都市の地点に確認が出る。
        /// </summary>
        public static void Reset()
        {
            _phase = VolcanoPhase.Idle;
            _footprint = VolcanoFootprint.None;
            _lastRefusal = null;
            _settingsChanged = false;
            // 準備の実績も持ち越さない。**進行中の火山は保存しない**ので、
            // 都市を出入りすると準備は 0 からになる（地形はそのままの形で残る）。
            VolcanoClearing.Reset();
            // ★ 隆起の退避配列も返す（半径 3 km で 279 KB）。**地形は戻らない。**
            VolcanoUplift.Reset();
            // ★ 噴火の予定も畳む。**描画側（main）の後始末はここではしない** ——
            //   Unity オブジェクトの破棄は main スレッドの仕事で、
            //   VolcanoEruption.Render がスナップショットを見て自分で畳む
            //   （レベルアンロードでは VolcanoFeature が Destroy を呼ぶ）。
            VolcanoEruption.Reset();
            // ★ 溶岩の軌跡も返す（8 本 × 128 点で 8 KB）。**焦げた地面と燃えた建物は
            //   戻らない** —— 捨てるのは「これからの予定」だけである。
            VolcanoLava.Reset();
        }

        /// <summary>
        /// sim スレッド。**必ず <c>VolcanoFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に山が育ち、建物が消える）。
        ///
        /// <see cref="VolcanoHub.TakeRequest"/> は<b>1 tick にちょうど 1 回</b>
        /// しか呼ばない（2 回呼ぶと 2 回目が必ず None になり、呼び出し順に依存した
        /// 取りこぼしを作る。あちらの doc）。
        ///
        /// <paramref name="snapshot"/> は**この tick の頭で publish した状態**なので、
        /// ここでは位相を読まず、地形が書けるか（<see cref="VolcanoTerrainFacts.Usable"/>）
        /// だけを見る。<paramref name="frame"/> / <paramref name="deltaMinutes"/> は
        /// T5〜T8 が使う（このタスクでは進める状態がまだ無い）。
        /// </summary>
        public static void Tick(VolcanoSnapshot snapshot, uint frame, float deltaMinutes)
        {
            VolcanoRequestData request = VolcanoHub.TakeRequest();

            // ★ 述語は⑤の門そのもの（VolcanoTerrainFacts.Usable）。
            //   「フィールドが解決した」で通すと、値が使えない環境で着手できてしまう。
            bool terrainUsable = snapshot != null && snapshot.Valid && snapshot.Terrain.Usable;
            if (!terrainUsable)
            {
                if (request.Kind != VolcanoRequest.None)
                {
                    Refuse("the terrain write path is not usable in this build of the game; "
                           + "no volcano can be placed");
                }
                return;
            }

            switch (request.Kind)
            {
                case VolcanoRequest.Survey:
                    HandleSurvey(request.Point);
                    break;

                case VolcanoRequest.Start:
                    HandleStart();
                    break;

                case VolcanoRequest.Cancel:
                    HandleCancel();
                    break;

                case VolcanoRequest.Stop:
                    HandleStop();
                    break;
            }

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

            if (_phase == VolcanoPhase.Erupting)
            {
                // T7。**噴出の予定を決めるだけ**で、地形も建物も 1 つも変えない。
                VolcanoEruption.Tick(_footprint, frame, deltaMinutes);

                if (!VolcanoEruption.Finished) return;

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
                VolcanoLava.Tick(_footprint, frame, deltaMinutes);

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
                VolcanoLava.Tick(_footprint, frame, deltaMinutes);

                if (!VolcanoLava.Finished) return;

                _phase = VolcanoPhase.Done;
                Log.Info("volcano finished; the terrain stays as it is (this is irreversible)");
                return;
            }

            if (_phase != VolcanoPhase.Uplifting) return;

            // ★★ **順序がこの 2 行そのものである**（設計書 §1.2 / 罠 1）。
            //    準備を先に、隆起の進捗を渡して前へ走らせ、そのあとで隆起が
            //    「準備が届いた半径」の内側だけを上げる。入れ替えてはいけない。
            VolcanoClearing.Tick(_footprint, VolcanoUplift.ProgressUnit, deltaMinutes);
            VolcanoUplift.Tick(_footprint, frame, deltaMinutes);

            if (!VolcanoUplift.Complete) return;

            // ★ T7 がここを <c>Done</c> から <c>Erupting</c> に差し替えた。位相が
            //   進行中のまま止まらないことは <c>VolcanoEruption.Finished</c> が担保する
            //   （噴火は必ず有限のゲーム内時間で終わり、例外が出た場合も終わる）。
            _phase = VolcanoPhase.Erupting;
            _lastRefusal = null;
            Log.Info("volcano uplift complete: summit +"
                     + VolcanoUplift.SummitMetres.ToString("F0")
                     + " m, crater " + (VolcanoUplift.CraterCarved ? "carved" : "NOT carved")
                     + "; the eruption starts now");
        }

        /// <summary>
        /// 調査。**何も壊さない。** 進行中（<see cref="VolcanoPhase.Clearing"/> 以降）なら断る。
        /// </summary>
        private static void HandleSurvey(Vec3 point)
        {
            if (InProgress())
            {
                Refuse("a volcano is already in progress (phase=" + _phase
                       + "); the survey request was ignored");
                return;
            }

            _phase = VolcanoPhase.Surveying;
            _settingsChanged = false;
            RunSurveyAt(point);
        }

        /// <summary>
        /// 着手。**<see cref="VolcanoPhase.AwaitingConfirmation"/> のときだけ通る。**
        /// 依頼に載っている座標は使わない（クラス doc）。
        /// </summary>
        private static void HandleStart()
        {
            if (_phase != VolcanoPhase.AwaitingConfirmation || !_footprint.Valid)
            {
                Refuse("no surveyed spot is waiting for confirmation (phase=" + _phase
                       + "); the start request was ignored");
                return;
            }

            // ★ 確認を出したあとで形態・半径・最終高が変わっていたら、
            //   **古い概数で壊し始めない**。調べ直して、もう一度確認を取る。
            VolcanoForm form = CurrentForm();
            float radius = VolcanoShape.RadiusFor(form, CurrentRadius());
            float height = VolcanoShape.HeightFor(form, CurrentHeight(),
                                                  _footprint.GroundHeightMetres);

            if (form != _footprint.Form
                || !Same(radius, _footprint.RadiusMetres)
                || !Same(height, _footprint.HeightMetres))
            {
                _phase = VolcanoPhase.Surveying;
                RunSurveyAt(_footprint.Centre);
                _settingsChanged = true;
                return;
            }

            // ★★ **道路を取り除く経路が無い環境では、1 本も壊さずにここで断る**
            //    （設計書 §1.2 / T5 Step 1）。「道路だけ諦めて隆起する」は選ばない ——
            //    それは §1.2 が発見した失敗（山の中の平らな溝）を、分かったうえで
            //    出荷することになる。**判定は着手の直前に、破壊より先に置く。**
            if (!VolcanoClearing.RoadPathAvailable)
            {
                _settingsChanged = false;
                _phase = VolcanoPhase.Refused;
                _lastRefusal = "no usable road destruction path; raising the ground would leave "
                               + "flat trenches where the roads are";
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcState",
                         _lastRefusal);
                return;
            }

            _settingsChanged = false;
            _lastRefusal = null;
            // ★ ここから先が「壊す」である。T5 の VolcanoClearing がこの位相を動かす。
            _phase = VolcanoPhase.Clearing;
            Log.Info("volcano confirmed at (" + _footprint.Centre.X.ToString("F0") + ","
                     + _footprint.Centre.Z.ToString("F0") + "); clearing starts now");
        }

        /// <summary>確認を閉じる。**何も起きずに <see cref="VolcanoPhase.Idle"/> へ戻る。**</summary>
        private static void HandleCancel()
        {
            if (InProgress())
            {
                Refuse("a volcano is already in progress (phase=" + _phase
                       + "); use Stop, not Cancel");
                return;
            }

            _phase = VolcanoPhase.Idle;
            _footprint = VolcanoFootprint.None;
            _lastRefusal = null;
            _settingsChanged = false;
        }

        /// <summary>
        /// 進行中の火山を止める。**既に変わった地形は戻らない**（設計書 §7.1 / §E-13）。
        /// T4 の時点では止める処理そのものがまだ無いので、位相を畳むだけである。
        /// </summary>
        private static void HandleStop()
        {
            _phase = VolcanoPhase.Idle;
            _footprint = VolcanoFootprint.None;
            _settingsChanged = false;
            // ★ 準備の実績も畳む。**既に壊した建物と道路は戻らない**（不可逆）。
            //   畳まないと、次に開いたパネルが前の火山の破壊数を名乗る。
            VolcanoClearing.Reset();
            // ★ 隆起の退避配列も返す（半径 3 km で 279 KB）。**地形は戻らない。**
            VolcanoUplift.Reset();
            // ★ 噴火の予定も畳む。**描画側（main）の後始末はここではしない** ——
            //   Unity オブジェクトの破棄は main スレッドの仕事で、
            //   VolcanoEruption.Render がスナップショットを見て自分で畳む
            //   （レベルアンロードでは VolcanoFeature が Destroy を呼ぶ）。
            VolcanoEruption.Reset();
            // ★ 溶岩の軌跡も返す（8 本 × 128 点で 8 KB）。**焦げた地面と燃えた建物は
            //   戻らない** —— 捨てるのは「これからの予定」だけである。
            VolcanoLava.Reset();
            _lastRefusal = "stopped by the player; the terrain that already changed stays changed";
        }

        private static void RunSurveyAt(Vec3 point)
        {
            VolcanoForm form = CurrentForm();
            VolcanoFootprint footprint;

            if (VolcanoSurvey.Run(point, form, CurrentRadius(), CurrentHeight(), out footprint))
            {
                _footprint = footprint;
                _phase = VolcanoPhase.AwaitingConfirmation;
                _lastRefusal = null;
                return;
            }

            _footprint = VolcanoFootprint.None;
            _phase = VolcanoPhase.Refused;
            _lastRefusal = VolcanoSurvey.LastFailure ?? "the survey failed for an unknown reason";
        }

        /// <summary>
        /// 「もう新しい火山は始められない」位相か。
        /// <see cref="VolcanoPhase.AwaitingConfirmation"/> は**含めない**（クラス doc の逸脱）。
        /// </summary>
        private static bool InProgress()
        {
            return _phase == VolcanoPhase.Clearing
                   || _phase == VolcanoPhase.Uplifting
                   || _phase == VolcanoPhase.Erupting
                   || _phase == VolcanoPhase.Flowing
                   || _phase == VolcanoPhase.Cooling;
        }

        private static void Refuse(string reason)
        {
            _lastRefusal = reason;
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcState", reason);
        }

        /// <summary>設定の形態。範囲外の値は <c>VolcanoShape.FormOf</c> が既定へ落とす。</summary>
        private static VolcanoForm CurrentForm()
        {
            return VolcanoShape.FormOf(ModSettings.VolcanoShapeSetting.value);
        }

        private static float CurrentRadius()
        {
            return ModSettings.VolcanoRadius.value;
        }

        private static float CurrentHeight()
        {
            return ModSettings.VolcanoHeight.value;
        }

        /// <summary>
        /// 1/64 m（raw 1 単位）より細かい差は「同じ」とみなす。スライダーは整数
        /// メートルしか作らないので実際には厳密一致するが、float の比較を
        /// <c>==</c> で書かない習慣のほうを守る。
        /// </summary>
        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }
    }
}
