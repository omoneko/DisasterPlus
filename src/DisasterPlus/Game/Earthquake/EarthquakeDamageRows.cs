using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 断層帯と、建物ごとの余裕度（＝本機能の目玉）の行。**main スレッド専用。**
    ///
    /// <see cref="EarthquakePanel"/> から切り出したのは、あのファイルがプロジェクト規約の
    /// 800 行を大きく超えていたためで、**内容は 1 文字も変えていない**。
    /// 行の生成も <c>.text</c> の代入もこのファイルには無く、
    /// <see cref="EarthquakeRows"/> を通してしか行えない（あちらのクラス doc の担保）。
    ///
    /// ここは全て**第 1 層**である。バニラが (建物, 災害) の組ごとに引く固定のしきい値を
    /// 同じ種から再構成しているだけで、新しい物理は 1 つも足していない（§A-3）。
    /// </summary>
    internal static class EarthquakeDamageRows
    {
        private static UILabel _faultLabel;
        private static UILabel _faultNoteLabel;
        private static UILabel _marginBuildingLabel;
        private static UILabel _marginVerdictLabel;
        private static UILabel _marginBurnLabel;
        private static UILabel _marginNoteLabel;
        private static UILabel _ndrNoteLabel;

        internal static void Build(UIPanel p, ref float y)
        {
            _faultLabel = EarthquakeRows.AddLayer1Row(p, "FaultBand", ref y);

            // 断層帯の行には**常に**この注記が付く（計画の共通規則）。
            // 4 円盤の位置は毎ステップ振り直されるので、帯は「当たりうる範囲」であって
            // 「当たる場所」ではない。テキストは固定なのでここで一度だけ入れる。
            _faultNoteLabel = EarthquakeRows.AddPlainRow(p, "FaultBandNote", ref y,
                Strings.EarthquakeFaultBandNote, 28f);

            _marginBuildingLabel = EarthquakeRows.AddLayer1Row(p, "MarginBuilding", ref y);
            _marginVerdictLabel = EarthquakeRows.AddLayer1Row(p, "MarginVerdict", ref y);

            // ★ 出火の行（全体レビュー M1）。依頼文が「揺れによる火災や建物の倒壊」と
            //    名指ししていたうちの半分がここで、材料（2 回目の引き）は
            //    BuildingMargin.BurnThresholdValue に最初から入っていた。
            _marginBurnLabel = EarthquakeRows.AddLayer1Row(p, "MarginBurn", ref y, 28f);

            // この注記は**常に**併記する。全体円盤についての判定でしかないことと、
            // それが地震開始の瞬間に既に決まっていることの両方を、行の隣で名乗る。
            _marginNoteLabel = EarthquakeRows.AddPlainRow(p, "MarginNote", ref y,
                Strings.EarthquakeGlobalDiscOnly, 38f);

            // ★ NDR が居るなら、倒壊・出火の判定は**この環境では出せない**ことを
            //    常設で名乗る（全体レビュー C2、§E-2）。NDR は
            //    DisasterHelpers.DestroyBuildings を完全置換して probability を
            //    0.02 → 0.04 に差し替えるので、この MOD が読んでいるランプは
            //    どこでも実行されていない。①の ForecastNdrNote と同じ扱い。
            if (ModCompat.NdrPresent)
            {
                _ndrNoteLabel = EarthquakeRows.AddPlainRow(p, "NdrNote", ref y,
                    Strings.EarthquakeNdrNote, 38f);
            }
        }

        internal static void Refresh(EarthquakeSnapshot snapshot, EarthquakeReading primary,
                                     bool haveCursor, Vec3 cursor)
        {
            RefreshFaultRow(primary, haveCursor, cursor);
            RefreshMarginRows(snapshot);
        }

        /// <summary>
        /// 値だけを消す。**NDR の注記は消さない** —— あれは「この環境では判定を出せない」
        /// という常設の説明であって観測値ではない。
        /// </summary>
        internal static void Clear()
        {
            EarthquakeRows.SetPlain(_faultLabel, "");
            EarthquakeRows.SetPlain(_faultNoteLabel, "");
            EarthquakeRows.SetPlain(_marginBuildingLabel, "");
            EarthquakeRows.SetPlain(_marginVerdictLabel, "");
            EarthquakeRows.SetPlain(_marginBurnLabel, "");
            EarthquakeRows.SetPlain(_marginNoteLabel, "");
        }

        /// <summary>レベルアンロード時。参照を捨てるだけ（実体はパネルごと消える）。</summary>
        internal static void Destroy()
        {
            _faultLabel = null;
            _faultNoteLabel = null;
            _marginBuildingLabel = null;
            _marginVerdictLabel = null;
            _marginBurnLabel = null;
            _marginNoteLabel = null;
            _ndrNoteLabel = null;
        }

        /// <summary>
        /// 断層帯。プレハブ 4 値（<c>m_crackLength</c> / <c>m_crackWidth</c>）が読めて
        /// いなければ幾何が確定しないので、**行ごと出さない**。
        /// 「分からない」を「外側」と言い換えない（<see cref="FaultBand.Known"/> の doc）。
        /// </summary>
        private static void RefreshFaultRow(EarthquakeReading primary, bool haveCursor, Vec3 cursor)
        {
            EarthquakeRows.SetPlain(_faultLabel, "");
            EarthquakeRows.SetPlain(_faultNoteLabel, "");

            if (!haveCursor) return;

            // ★ 収束中の地震には破壊円盤が落ちない（§A-3、Active 分岐にしか無い）。
            //    「断層帯: 内側」はこれから壊れうる場所の話なので、行ごと出さない。
            if (!QuakeSelection.RunsDamage(primary.Phase)) return;

            var band = new FaultBand(primary.Epicentre.ToVec2(), primary.AngleRadians,
                                     primary.CrackLength, primary.CrackWidth);
            if (!band.Known) return;

            EarthquakeRows.SetLayer1(_faultLabel, Strings.EarthquakeFaultBand + ": "
                + (band.Contains(cursor.ToVec2())
                    ? Strings.EarthquakeFaultInside
                    : Strings.EarthquakeFaultOutside));
            // この注記は必ず併記する（計画の共通規則）。帯は「当たりうる範囲」であって
            // 「当たる場所」ではない。
            EarthquakeRows.SetPlain(_faultNoteLabel, Strings.EarthquakeFaultBandNote);
        }

        /// <summary>
        /// **本機能の目玉。** カーソル下の建物が、その地震で倒れるかどうか。
        ///
        /// バニラは建物ごとに <c>new Randomizer(buildingID | (disasterID &lt;&lt; 16))</c> から
        /// 固定のしきい値を引く（§A-3）。この種はフレームにもステップにも依存せず、
        /// 全体円盤の震央も動かないので、**結論は地震が始まった瞬間に既に確定している**。
        /// 依頼文の「揺れによる火災や倒壊はおそらくランダム」への回答がこれで、
        /// だからこの行だけは「予測」ではなく事実として書ける。
        ///
        /// ただし断定してよい範囲は狭い。ここで扱っているのは全体円盤
        /// （probability = 0.02、震央中心）だけで、断層 4 円盤（probability = 1、
        /// 毎ステップ位置が振り直される）については何も言えない。**帯の内側と、
        /// 帯の幾何が読めていないときは、「倒壊しません」と言わない**
        /// —— それを保証しているのは <see cref="BuildingMargin.Evaluate"/> 側の分岐順で、
        /// ここはその結論を書き出すだけである。
        ///
        /// 値は全て 1 tick 前の sim スレッドの読み取りで、**このメソッドは建物バッファに
        /// 一切触らない**（<see cref="BuildingProbe"/> のクラス doc）。
        /// </summary>
        private static void RefreshMarginRows(EarthquakeSnapshot snapshot)
        {
            var margin = snapshot.CursorBuilding;

            // 注記は行が出ているときだけ添える（空行の下に注記だけ残さない）。
            EarthquakeRows.SetPlain(_marginNoteLabel, "");

            // CursorQuakeId == 0 は「まだ調べていない」——カーソルが地形の上に無い、
            // あるいは破壊判定が走る地震（Active / Emerging）が 1 つも無い。
            // **この状態で「カーソルの下に建物がありません」と書いてはいけない。**
            // 建物の上にカーソルがあっても同じ 0 になるので、それは嘘になる。
            // 言えることが無いときは、何も言わない。
            if (snapshot.CursorQuakeId == 0)
            {
                EarthquakeRows.SetPlain(_marginBuildingLabel, "");
                EarthquakeRows.SetPlain(_marginVerdictLabel, "");
                EarthquakeRows.SetPlain(_marginBurnLabel, "");
                return;
            }

            // ★ 「調べたが建物が無かった」と「調べられなかった」を言い分ける
            //    （全体レビュー I3）。以前は BuildingManager が取れなくても走査が
            //    例外を投げても、同じ「カーソルの下に建物がありません」が出ていた
            //    —— 読み取り失敗が実測値の顔で出てくる、この機能が他の全ての行で
            //    禁じている壊れ方そのものである。
            if (snapshot.CursorProbe == BuildingProbeOutcome.Failed)
            {
                EarthquakeRows.SetPlain(_marginBuildingLabel, Strings.EarthquakeProbeFailed);
                EarthquakeRows.SetPlain(_marginVerdictLabel, "");
                EarthquakeRows.SetPlain(_marginBurnLabel, "");
                return;
            }

            if (!margin.HasBuilding)
            {
                EarthquakeRows.SetLayer1(_marginBuildingLabel,
                    Strings.EarthquakeBuildingUnderCursor + ": " + Strings.EarthquakeNoBuilding);
                EarthquakeRows.SetPlain(_marginVerdictLabel, "");
                EarthquakeRows.SetPlain(_marginBurnLabel, "");
                return;
            }

            // どの地震についての判定かを必ず名乗る。複数同時進行のとき、上の行が
            // 選んでいる地震（SelectPrimary）とここで判定した地震（sim 側の
            // QuakeSelection.SelectDamaging）は一致しないことがある。
            EarthquakeRows.SetLayer1(_marginBuildingLabel,
                Strings.EarthquakeBuildingUnderCursor + ": #" + margin.BuildingId
                + "   (#" + snapshot.CursorQuakeId + ")");

            EarthquakeRows.SetLayer1(_marginVerdictLabel, VerdictText(margin));
            EarthquakeRows.SetLayer1(_marginBurnLabel, BurnVerdictText(margin));
            EarthquakeRows.SetPlain(_marginNoteLabel, Strings.EarthquakeGlobalDiscOnly);
        }

        /// <summary>
        /// 結論の 1 行。**設計書 §3.2 と計画 5.2 の表がそのままこの switch である。**
        /// 断定してよい状態としてはいけない状態を、ここで取り違えないこと。
        /// </summary>
        private static string VerdictText(BuildingMargin margin)
        {
            string current = Strings.EarthquakeCurrentDistance + " "
                             + margin.Distance.ToString("F0") + " m";
            // 「倒壊するのは震央から X m 以内」。X ≦ 0 の建物は、震央に居ても
            // 全体円盤では倒れない（しきい値が 200 以上）。
            string within = Strings.EarthquakeCollapseWithin + " "
                            + margin.CollapseWithin.ToString("F0") + " m";

            switch (margin.Verdict)
            {
                case CollapseVerdict.AlreadyDown:
                    return Strings.EarthquakeAlreadyDown;

                case CollapseVerdict.OutOfRange:
                    // バニラが preRadius で判定自体を打ち切っている領域。
                    return Strings.EarthquakeOutOfRange + "   (" + current + ")";

                case CollapseVerdict.Unknown:
                    // 断層の幾何が読めていない。数値は出すが、判定は出さない。
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeVerdictUnknown;

                case CollapseVerdict.DamageModelReplaced:
                    // ★ 破壊コードが他 MOD に置き換えられている（§E-2）。
                    //    **距離も出さない。** バニラの 0.02 から導いた「X m 以内」は、
                    //    その環境では誰も使っていない数字であり、隣に書けば
                    //    判定を伏せた意味が無くなる。
                    return current + "   -> " + Strings.EarthquakeVerdictNdr;

                case CollapseVerdict.InsideFaultZone:
                    // 倒壊距離は出す。しかし「倒れません」とは言わない
                    // （帯の内側は probability = 1 の破壊円盤が別に判定する）。
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeFaultBand + ": " + Strings.EarthquakeFaultInside;

                case CollapseVerdict.WillCollapse:
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeVerdictCollapse;

                case CollapseVerdict.Survives:
                    // 全体円盤についてのみの「倒壊しません」。
                    // ここへ来られるのは断層帯の**外側**の建物だけである
                    // （BuildingMargin.Evaluate の分岐順がそれを保証している）。
                    return margin.CollapseWithin > 0f
                        ? within + " / " + current + "   -> " + Strings.EarthquakeVerdictSurvive
                        : current + "   -> " + Strings.EarthquakeVerdictSurviveAnyDistance;

                default:
                    // ★ 既定を「倒壊しません」にしない。将来 CollapseVerdict に
                    //    値が増えてここを直し忘れたとき、黙って生存を断定することに
                    //    なる——この機能がいちばん避けたい壊れ方そのもの。
                    //    知らない結論は「判定できません」に倒す。
                    return Strings.EarthquakeVerdictUnknown;
            }
        }

        /// <summary>
        /// 出火の結論の 1 行（全体レビュー M1）。**構造は倒壊とまったく同じ**で、
        /// 違うのは引くしきい値（2 回目の引き）と文言だけである（§A-3）。
        ///
        /// **倒壊が優先する。** IL は <c>else if (hitB &amp;&amp; ...)</c> なので、
        /// 同じ建物で倒壊も当たっているならバニラは出火の分岐へ行かない
        /// （倒壊側が <c>burnAmount = Round(fB*255)</c> を持って行く）。
        /// その順序を隠すと、「倒壊します」と「出火します」が同時に出て
        /// 両方起きるように読める。
        /// </summary>
        private static string BurnVerdictText(BuildingMargin margin)
        {
            string within = Strings.EarthquakeBurnWithin + " "
                            + margin.BurnWithin.ToString("F0") + " m";

            string body;
            switch (margin.BurnVerdict)
            {
                case CollapseVerdict.AlreadyDown:
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeAlreadyDown;

                case CollapseVerdict.OutOfRange:
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeOutOfRange;

                case CollapseVerdict.Unknown:
                    body = within + "   -> " + Strings.EarthquakeVerdictUnknown;
                    break;

                case CollapseVerdict.DamageModelReplaced:
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeVerdictNdr;

                case CollapseVerdict.InsideFaultZone:
                    body = within + "   -> " + Strings.EarthquakeFaultBand + ": "
                           + Strings.EarthquakeFaultInside;
                    break;

                case CollapseVerdict.WillCollapse:
                    body = within + "   -> " + Strings.EarthquakeVerdictBurn;
                    break;

                case CollapseVerdict.Survives:
                    body = margin.BurnWithin > 0f
                        ? within + "   -> " + Strings.EarthquakeVerdictNoBurn
                        : Strings.EarthquakeVerdictNoBurnAnyDistance;
                    break;

                default:
                    // 倒壊側と同じ理由で、知らない結論は「判定できません」に倒す。
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeVerdictUnknown;
            }

            // 倒壊が確定しているなら、出火の分岐には来ないことを併記する。
            if (margin.Verdict == CollapseVerdict.WillCollapse)
            {
                body += "\n" + Strings.EarthquakeBurnAfterCollapse;
            }
            return body;
        }
    }
}
