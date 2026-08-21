namespace DisasterPlus.Game
{
    /// <summary>
    /// ④台風。バニラの雷雨災害スロット 1 個を土台に、④が経路・天候・落雷・風害・
    /// 河川氾濫・巨大な回転雲を毎 tick 駆動する**移動する台風**。
    ///
    /// **①②と違い、④はバニラに原資が無い。** 台風という現象はバニラに存在せず、
    /// 風による破壊機構も、ワールド座標を持つ雲も、洪水災害も存在しない
    /// （IL 事実文書 §B5 / §C7 / §D9）。したがって④が出す数値は**原則すべて本 MOD のもの**で、
    /// パネルは見出しで一度だけそう名乗り、行ごとの印は付けない（設計書 §1.2 / §7）。
    /// 例外は <c>WeatherManager</c> から読んだ雨量・雲量だけである。
    ///
    /// このタスク（Task 2）の時点では**パネルも台風も無い機能**である。やることは
    /// sim スレッドで読んで <see cref="TyphoonHub"/> へ publish することと、
    /// **プレハブ 6 値を診断ダンプに出すこと**だけ。その 6 値
    /// （<c>ThunderStormAI</c> の <c>m_radius</c> / <c>m_emergingDuration</c> /
    /// <c>m_activeDuration</c> と、<c>VortexAI</c> の <c>m_destructionRadiusMin</c> /
    /// <c>m_destructionRadiusMax</c>、<c>VehicleInfo.m_maxSpeed</c>）は
    /// **DLL に実数値が無く**（§A-0 / §B-1、どちらも PARTIAL）、④の以後の
    /// 持続時間・落雷本数・破壊半径・移動速度が全てその上に乗るので、先に実機で 1 回測る。
    ///
    /// <see cref="IPausedTickFeature"/> を実装しているのは①②と同じ理由
    /// （ロード直後にポーズしたままパネルを開くと全行が「読み取れません」になる）。
    /// **ただし④は T3 以降でゲームの状態を進める。** その契約を守る仕掛けは
    /// <see cref="OnSimulationTick"/> の中にある。
    /// </summary>
    public partial class TyphoonFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Typhoon";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            TyphoonHub.Clear();
            TyphoonReader.Reset();
            TyphoonController.Reset();
            TyphoonWeather.Reset();

            // ★★ **毎レベルロードで登録し直すこと。** ToolController.m_tools は Awake で
            //    一度だけ構築され、ToolsModifierControl.SetTool<T> は静的辞書を引くだけ
            //    なので、登録しないと**黙って空振りする**（「タイルは押せるのに
            //    カーソルが変わらない」という、例外の出ない壊れ方）。
            //    ToolController は都市ごとに作り直されるので前の都市の登録は使えない。
            ToolRegistration.Register<TyphoonPlacementTool>();
        }

        /// <summary>
        /// sim スレッド。<c>DisasterManager</c> / <c>WeatherManager</c> /
        /// <c>SimulationManager</c> の読み取りは必ずここで行う。
        ///
        /// ポーズ中（deltaMinutes == 0）にも呼ばれる（<see cref="IPausedTickFeature"/>）。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.TyphoonEnabled.value)
            {
                // ★★ 機能を切っても、**触ったものは全部返す。** プレイヤーが台風の
                //    最中にこの設定を切ると、以降この tick は 1 行も走らなくなるので、
                //    ここで返さなかったものは（都市を出るか保存するまで）返す機会が
                //    無くなる。3 つとも台帳が空なら 1 命令で返るので、毎 tick 通っても
                //    構わない（ログも確保も走らない）。
                //
                //    ここはポーズガードより上だが、**どれも「④が握っていたものを
                //    手放す」方向**であって進行ではないので IPausedTickFeature の
                //    契約は破らない（むしろポーズ中に切られたときに手放せるほうが
                //    正しい）。竜巻の StopAll はバニラの終了経路（DeactivateNow /
                //    ReleaseDisaster）を呼ぶので**状態は変わる**が、変わるのは
                //    「④が作ったものが畳まれる」ことだけで、ゲームが先へ進むわけではない。
                TyphoonFlood.RestoreAll();

                // ★★ 局所被害のカウンタと天候も返す（全体レビュー C2）。以前ここは
                //    水位しか戻しておらず、天候を握ったまま台風だけが止まった
                //    （雨がやまなくなる）。Reset / Release はどちらも冪等である。
                TyphoonGust.Reset();
                TyphoonWeather.Release();

                // ★ 風害の走査位置とカウンタも畳む。走査そのものはもう呼ばれないが、
                //   **診断が最後の走査の数字を抱えたままだと「切ったのにまだ
                //   壊している」と読める**（局所被害と同じ理由）。Reset は冪等で、
                //   台帳を持たないので毎 tick 通ってよい。
                TyphoonWind.Reset();
                return;
            }

            // ここまでが「読んで publish するだけ」。ポーズ中もここは通る。
            var snapshot = TyphoonReader.Read();
            TyphoonHub.Publish(snapshot);

            // Typhoon チャンネルは既定 OFF。この if が無いと、下の ToString と
            // 文字列連結が毎 sim tick（通常速度でおよそ 50 回/秒）実行されてから
            // Log.Diag に捨てられる——C# は引数を呼び出し前に評価し切るので、
            // Diag の内側のマスク判定では手遅れになる。
            //
            // ①の ForecastFeature と違い、ここは early-return にしてはいけない。
            // 下のポーズガードと全要素の処理を丸ごと飛ばすことになる
            // （②の EarthquakeFeature が同じ注記を持っている）。
            if (Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon))
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "typhoon",
                    snapshot.Valid
                        ? "prefab=" + (snapshot.Prefab.Usable ? "usable" : "UNUSABLE")
                          + " rain=" + snapshot.Rain.ToString("F2")
                          + " cloud=" + snapshot.Cloud.ToString("F2")
                        : "snapshot invalid");
            }

            // ★ ここから下は状態を進める。ポーズ中（deltaMinutes == 0）は絶対に通さない。
            //    T3〜T10 が足す処理は必ずこの行より下に置くこと。
            //    このコメントを消すと「ポーズ中に台風が動き、建物が倒れ、川が溢れる」が起きる。
            if (deltaMinutes <= 0f) return;

            TyphoonController.Tick(snapshot, frameIndex, deltaMinutes);
            if (TyphoonController.Active)
            {
                TyphoonWeather.Drive(snapshot, deltaMinutes);
                TyphoonLightning.Tick(snapshot, frameIndex);

                // ★ 風害は設定で切れる（既定 ON。強さ 0 でも完全に無効）。
                //   切ったときに Apply を呼ばないのは②の第 2 層と同じ形で、
                //   走査そのものを起こさないためである。
                if (ModSettings.TyphoonWindDamage.value)
                {
                    TyphoonWind.Apply(snapshot, deltaMinutes);
                }

                // ★ 竜巻並みの局所被害（既定 ON。強さ 0 でも完全に無効）。
                //   **竜巻の実体は 1 つも作らない**（TyphoonGust のクラス doc）。
                if (ModSettings.TyphoonGustEnabled.value)
                {
                    TyphoonGust.Tick(snapshot, frameIndex, deltaMinutes);
                }

                // ★ 暴風雨の吹き飛ばし（市民と車だけ）。**風害とは別の設定**で、
                //   建物・道路・樹木には一切触れない（ModSettings.TyphoonStormFx の doc）。
                //   走査ではないので、風害を切っていても吹く。
                if (ModSettings.TyphoonStormFx.value)
                {
                    TyphoonWind.Gale(snapshot, deltaMinutes);
                }
            }

            // ★ 河川氾濫は台風が居なくても呼ぶ。**持ち上げた水位を戻すのが
            //    この経路の仕事でもある**（TyphoonController.Forget が既に
            //    RestoreAll を呼んでいるが、取りこぼしをここで拾う）。
            //    設定を OFF にした瞬間に呼ばれなくなると川が溢れたままになるので、
            //    OFF のときも「台帳が空でなければ戻す」ところまでは通す。
            if (ModSettings.TyphoonFloodEnabled.value)
            {
                TyphoonFlood.Tick(snapshot, frameIndex, deltaMinutes);
            }
            else
            {
                TyphoonFlood.RestoreAll();
            }

            // ★★ 台風が居ない／設定を切ったときは**必ずここを通してカウンタを畳む**。
            //    パッチそのものは台風の経過フレームの関数なので台風が無ければ
            //    存在しないが、**診断が「直近の走査」の数字を抱えたままだと
            //    「台風が去ったのにまだ壊している」ように読める**。
            //    Reset は台帳を持たないので 1 命令で返る（毎 tick 通ってよい）。
            if (!TyphoonController.Active || !ModSettings.TyphoonGustEnabled.value)
            {
                TyphoonGust.Reset();
            }
        }

        /// <summary>
        /// main スレッド。**ここから sim 側の型（<see cref="TyphoonController"/> /
        /// <see cref="TyphoonWeather"/>）を呼ばないこと。** 読むのは
        /// <see cref="TyphoonHub.Latest"/> のスナップショットだけである。
        /// </summary>
        public void OnMainThreadUpdate()
        {
            // ボタンは DisasterPanelBar が 4 個まとめて持つ（FeatureHost が呼ぶ）。
            TyphoonPanel.Tick();

            // ★ 雲は main スレッドだけの機能で、**sim 側からは 1 度も呼ばれない。**
            //   それが T9 を④の他の要素から独立させている実体である
            //   （TyphoonCloud のクラス doc）。台風が終わったときの後始末も
            //   TyphoonCloud.Update が自分で行う——TyphoonController.Forget の
            //   後始末列にこの型を足さないこと。
            //   ★ TyphoonEnabled も見ること。機能そのものを切ると OnSimulationTick が
            //     早期 return して TyphoonHub.Latest が更新されなくなるので、最後に
            //     publish された「Active な」スナップショットが残り続ける ——
            //     見ないと**止まった雲が画面に貼り付いたまま**になる
            //     （TyphoonPanel が同じガードを持っている）。
            if (ModSettings.TyphoonEnabled.value && ModSettings.TyphoonCloudEnabled.value)
            {
                TyphoonCloud.Update(TyphoonHub.Latest);
            }
            else
            {
                TyphoonCloud.Destroy();
            }

            // ★ 横殴りの飛沫も main スレッドだけの機能である（雲とまったく同じ扱い）。
            //   sim 側からは 1 度も呼ばれないので、TyphoonController.Forget の
            //   後始末列にこの型を足さないこと。台風が終わったフレームには
            //   Active でないスナップショットが渡り、あちらが自分で出すのをやめる。
            if (ModSettings.TyphoonEnabled.value && ModSettings.TyphoonStormFx.value)
            {
                TyphoonSquallFx.Update(TyphoonHub.Latest);
            }
            else
            {
                TyphoonSquallFx.Destroy();
            }
        }

        public void OnLevelUnloading()
        {
            // ★★ 配置ツールが選ばれたまま都市を出させない。次の都市でカーソルが
            //    「台風を置く」のまま始まると、プレイヤーが意図せず地点を指しうる。
            //    **アクティブでないときは何もしない**ので、他 MOD が選んでいたツールを
            //    横から戻すことはない（⑤と同じ）。
            TyphoonPlacementTool.Deactivate();

            // ★ UI から先に畳む。2 つ目の都市が**ボタン 1 個・パネル 1 枚**で
            //    始まること（残すと都市を読み込むたびに 1 枚ずつ積み上がる）。
            //    ボタンの撤去は FeatureHost.LevelUnloading が DisasterPanelBar.Remove で行う。
            TyphoonPanel.Destroy();
            // ★ Mesh も Material も Component ではないので、GameObject を消しても
            //    道連れにならない。**自分で Object.Destroy する**（TyphoonCloud の
            //    クラス doc）。バニラ空の雲の設定もここで元へ戻る。
            TyphoonCloud.Destroy();
            // ★ 飛沫の複製も都市をまたがない（雲と同じ。破棄済みの粒子系を撃ちに行く）。
            TyphoonSquallFx.Destroy();

            TyphoonHub.Clear();
            TyphoonReader.Reset();
            // 予約も進行中の台風も都市をまたいで残らない。
            TyphoonController.Reset();
            // ★ 天候の上書きは必ずここでも戻す。都市を出た瞬間に台風が消えても、
            //    m_targetRain を握ったままにしない。
            TyphoonWeather.Reset();
            // ★ 落雷の在庫も持ち越さない。持ち越すと次の都市の台風が、実際には
            //    空いているキューを「埋まっている」と見て撃たなくなる。
            TyphoonLightning.Reset();
            // ★ 風害の走査位置とカウンタも都市をまたがない。持ち越すと次の都市で
            //    前の都市の序数から走り出す（＝中心の周りが 1 度も判定されない）。
            TyphoonWind.Reset();
            // ★★ 河川の水位を必ず戻す（罠 4 の復元経路 2 本目）。
            //    ここを忘れると、次に開いた都市で**前の都市のハンドル**を復元しに行き、
            //    無関係な川の水位を書き換える。TyphoonFlood.Reset は内部で
            //    RestoreAll を呼んでから台帳を捨てる。
            TyphoonFlood.Reset();
            // ★ 局所被害のカウンタと借りたエフェクトの参照も都市をまたがない。
            //   持ち越すと、次の都市で**前の都市の粒子エフェクト**（破棄済み）を
            //   撃ちに行く。
            TyphoonGust.Reset();
        }
    }
}
