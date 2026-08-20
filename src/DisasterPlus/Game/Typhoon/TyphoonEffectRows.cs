using ColossalFramework.UI;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 「台風がもたらすもの」の節 —— 各要素が今どう動いているかを出す行。
    /// **main スレッド専用。**
    ///
    /// **T7〜T10 はこのファイルに行を足す。** T7 は風害、T8 は河川氾濫、T9 は雲、
    /// T10 は随伴竜巻。**800 行を超えたら分割すること**（要素ごとにファイルを分け、
    /// このファイルは並べる順序だけを持つ形にする）。
    ///
    /// ── ここでも行ごとの印は付けない（④の表示規約）─────────────────
    ///
    /// この節に出る数字は、④が自分で数えている台帳と、バニラの式から見積もった上限で
    /// ある。**どれも「ゲームが計算して公開している値」ではない。** 出所はパネルの
    /// 見出し（<c>Strings.TyphoonModelNote</c>）が一度だけ名乗っているので、
    /// ここでは <see cref="TyphoonRows.AddRow(UIPanel,string,ref float)"/> と
    /// <see cref="TyphoonRows.SetPlain"/> しか使わない。
    /// **<see cref="TyphoonRows.SetMeasured"/> をこのファイルから呼ばないこと。**
    ///
    /// ── 数字が 0 のときも出す ────────────────────────────────
    ///
    /// ③は「延焼が動いているか診断から一切見えなかった」という失敗をしている。
    /// 落雷が 0 発なのか、そもそも撒いていないのか、上限に当たって捨てられているのかは
    /// 画面上どれも同じ顔（何も起きない）になるので、**台風が動いている間は必ず
    /// 4 つの数を出す**。
    /// </summary>
    internal static class TyphoonEffectRows
    {
        private static UILabel _lightningLabel;
        private static UILabel _lightningNoteLabel;

        /// <summary>宿主の嵐に枠を全部譲っている間だけ出す行（全体レビュー I4）。</summary>
        private static UILabel _lightningYieldedLabel;
        private static UILabel _windLabel;
        private static UILabel _windNoteLabel;
        private static UILabel _windShelterNoteLabel;
        private static UILabel _floodLabel;
        private static UILabel _floodReasonLabel;
        private static UILabel _floodNoteLabel;
        private static UILabel _tornadoLabel;
        private static UILabel _tornadoNdrNoteLabel;
        private static UILabel _cloudNoteLabel;

        /// <summary>パネル構築時に 1 回。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            TyphoonRows.AddSectionHeader(p, "EffectsHeader", ref y, Strings.TyphoonEffectsHeader);

            _lightningLabel = TyphoonRows.AddRow(p, "Lightning", ref y);

            // 上限 20 発の説明。**常設**にする —— 「宿主の嵐に譲っている数」が
            // 何のことか、この 1 行が無いと分からない。
            _lightningNoteLabel = TyphoonRows.AddRow(p, "LightningNote", ref y, 40f);
            TyphoonRows.SetPlain(_lightningNoteLabel, Strings.TyphoonLightningNote);

            // ★ 「④の落雷が 1 発も出ていない」を出す行。**中身は Refresh が
            //    出し入れする** —— 常設にすると、譲っていない普通の強度でも
            //    「譲っています」と読める。高さは 4 行に折り返すぶんを確保する。
            _lightningYieldedLabel = TyphoonRows.AddRow(p, "LightningYielded", ref y, 72f);

            _windLabel = TyphoonRows.AddRow(p, "Wind", ref y);

            // **常設**にする。「バニラには風害が存在しない」ことと「数値は風速ではない」
            // ことは、風害が動いていない瞬間にこそ読まれるべき説明である。
            _windNoteLabel = TyphoonRows.AddRow(p, "WindNote", ref y, 40f);
            TyphoonRows.SetPlain(_windNoteLabel, Strings.TyphoonWindNote);

            // 「壊れていない」と「壊せない」を取り違えさせない（§F-2）。
            _windShelterNoteLabel = TyphoonRows.AddRow(p, "WindShelterNote", ref y, 40f);
            TyphoonRows.SetPlain(_windShelterNoteLabel, Strings.TyphoonWindShelterNote);

            _floodLabel = TyphoonRows.AddRow(p, "Flood", ref y);

            // 「なぜ氾濫しなかったか」の行。**中身は状態によって出し入れする**
            // （設計書 §7.4）。高さは説明文が 3 行に折り返すぶんを確保する。
            _floodReasonLabel = TyphoonRows.AddRow(p, "FloodReason", ref y, 56f);

            // 復元が 3 箇所から掛かることを**常設**で名乗る。氾濫の唯一の怖さは
            // 「MOD を外したら川が溢れたままだった」なので、そこを先に打ち消す。
            _floodNoteLabel = TyphoonRows.AddRow(p, "FloodNote", ref y, 40f);
            TyphoonRows.SetPlain(_floodNoteLabel, Strings.TyphoonFloodNote);

            _tornadoLabel = TyphoonRows.AddRow(p, "Tornado", ref y);

            // ★ NDR がいる環境でだけ出す注記（設計書 §2 の代償）。**中身は
            //    Refresh が出し入れする** —— 竜巻を切っているときにこの説明だけが
            //    残ると、切っているのに影響を受けていると読める。
            //    高さは 3 行に折り返すぶんを確保する。
            _tornadoNdrNoteLabel = TyphoonRows.AddRow(p, "TornadoNdrNote", ref y, 56f);

            // ★ 雲は行を持たない（画面を見れば出ているかどうか分かる）。**出ない理由**
            //    だけを出す —— バニラ空の雲の設定がこの環境に無いのは正当な状態で
            //    （§C-2、PARTIAL）、それを黙っていると「④の雲が壊れている」と読まれる。
            _cloudNoteLabel = TyphoonRows.AddRow(p, "CloudNote", ref y, 40f);
        }

        /// <summary>パネル表示中に毎フレーム。<paramref name="s"/> は null でありうる。</summary>
        internal static void Refresh(TyphoonSnapshot s)
        {
            if (s == null || !s.Valid || !s.Active)
            {
                // 台風が居ないときに 0 を並べない（「撒いていない」と「0 発だった」は違う）。
                TyphoonRows.SetPlain(_lightningLabel, "");
                TyphoonRows.SetPlain(_lightningYieldedLabel, "");
                TyphoonRows.SetPlain(_windLabel, "");
                TyphoonRows.SetPlain(_floodLabel, "");
                TyphoonRows.SetPlain(_floodReasonLabel, "");
                TyphoonRows.SetPlain(_tornadoLabel, "");
                TyphoonRows.SetPlain(_tornadoNdrNoteLabel, "");
                TyphoonRows.SetPlain(_cloudNoteLabel, "");
                return;
            }

            // 並びは Strings.TyphoonLightningRow が語で名乗っている順:
            // 飛行中 / 累計 / 宿主の嵐に残している数 / 捨てられた数。
            TyphoonRows.SetPlain(_lightningLabel,
                Strings.TyphoonLightningRow + ": "
                + s.LightningInFlight + " / " + s.LightningTotal + " / "
                + s.LightningVanillaReserve + " / " + s.LightningRejected);

            // ★ 「宿主に全部譲っていて④は 1 発も撃っていない」を名指しする
            //    （全体レビュー I4）。判定は在庫（一時的に 0）ではなく**宿主の
            //    取り分だけ**を見る —— 前者は次の tick で戻るが、後者は強度を
            //    下げるまで戻らない。区別は LightningBudget.YieldsCompletely の doc。
            TyphoonRows.SetPlain(_lightningYieldedLabel,
                LightningBudget.YieldsCompletely(s.LightningVanillaReserve)
                    ? Strings.TyphoonLightningYielded
                    : "");

            RefreshWind(s);
            RefreshFlood(s);
            RefreshTornado(s);
            RefreshCloud();
        }

        /// <summary>
        /// 雲の行（T9）。**出ているときは何も言わない** —— 空を見れば分かる。
        /// バニラ空の雲の増強がこの環境で使えないときだけ、その理由を出す
        /// （<c>DayNightDynamicCloudsProperties</c> は DLC・グラフィック設定によっては
        /// 存在しない。§C-2、PARTIAL。**不具合ではない**）。
        /// </summary>
        private static void RefreshCloud()
        {
            // ★ 渦が出ているのは「粒」でも「退避のメッシュ」でも同じ扱いにする。
            //   ここが名乗るのはバニラ空の増強が使えないことだけで、
            //   どちらの経路で渦を出しているかは診断ダンプの担当である。
            bool drawing = TyphoonCloud.State == TyphoonCloudState.Puffs
                           || TyphoonCloud.State == TyphoonCloudState.Drawing;

            bool unavailable = ModSettings.TyphoonCloudEnabled.value
                               && ModSettings.TyphoonVanillaCloudBoost.value
                               && drawing
                               && !TyphoonCloud.VanillaBoostApplied;

            TyphoonRows.SetPlain(_cloudNoteLabel,
                unavailable ? Strings.TyphoonCloudUnavailable : "");
        }

        /// <summary>
        /// 随伴竜巻の行（T10）。**既定 OFF なので「off」が普通の表示である。**
        ///
        /// NDR がいる環境では、**この要素だけが他 MOD の設定に従う**ことを
        /// その場で名乗る（設計書 §2 / IL 事実文書 §F-1）。同じ都市で風害と竜巻の
        /// 両方が動いているとき、片方だけ壊れ方が違う理由はここにしか出ない。
        /// </summary>
        private static void RefreshTornado(TyphoonSnapshot s)
        {
            if (!ModSettings.TyphoonTornadoes.value
                || ModSettings.TyphoonTornadoCount.value <= 0)
            {
                TyphoonRows.SetPlain(_tornadoLabel, Strings.TyphoonTornadoRow + ": off");
                TyphoonRows.SetPlain(_tornadoNdrNoteLabel, "");
                return;
            }

            // 並びは「今出ている数 / ④が操舵できている数」。後者が小さいときは
            // その竜巻がバニラの経路で流れている（診断に理由が出る）。
            TyphoonRows.SetPlain(_tornadoLabel,
                Strings.TyphoonTornadoRow + ": " + s.TornadoCount + " / " + s.TornadoAttached);

            TyphoonRows.SetPlain(_tornadoNdrNoteLabel,
                ModCompat.NdrPresent ? Strings.TyphoonTornadoNdrNote : "");
        }

        /// <summary>
        /// 河川氾濫の行（設計書 §7.4 の 5 状態）。
        ///
        /// **<c>NoSources</c> のとき理由を出すのがこの機能の要件である。**
        /// 対象マップに自然水源が無ければ正常に何も起きないので、
        /// 空欄のままにすると「壊れている」と読まれる。
        /// </summary>
        private static void RefreshFlood(TyphoonSnapshot s)
        {
            if (!ModSettings.TyphoonFloodEnabled.value
                || ModSettings.TyphoonFloodStrength.value <= 0)
            {
                TyphoonRows.SetPlain(_floodLabel, Strings.TyphoonFloodRow + ": off");
                TyphoonRows.SetPlain(_floodReasonLabel, "");
                return;
            }

            switch (s.FloodState)
            {
                case TyphoonFloodState.NoSources:
                    TyphoonRows.SetPlain(_floodLabel, Strings.TyphoonFloodRow + ": -");
                    // ★ 「なぜ起きないか」を出す。不具合ではない（設計書 §7.4）。
                    TyphoonRows.SetPlain(_floodReasonLabel, Strings.TyphoonFloodNoSources);
                    return;

                case TyphoonFloodState.Raised:
                    TyphoonRows.SetPlain(_floodLabel,
                        Strings.TyphoonFloodRow + ": " + Strings.TyphoonFloodRaised + " "
                        + s.FloodTouched + " / " + s.FloodNaturalSources
                        + "   +" + s.FloodPeakRiseMetres.ToString("F1") + " m");
                    TyphoonRows.SetPlain(_floodReasonLabel, "");
                    return;

                case TyphoonFloodState.Restored:
                    TyphoonRows.SetPlain(_floodLabel, Strings.TyphoonFloodRow + ": -");
                    TyphoonRows.SetPlain(_floodReasonLabel, "");
                    return;

                default:
                    // Idle（まだ強風域に入っていない）と Failed（理由は診断へ）は
                    // どちらも行を出さない。
                    TyphoonRows.SetPlain(_floodLabel, "");
                    TyphoonRows.SetPlain(_floodReasonLabel, "");
                    return;
            }
        }

        /// <summary>
        /// 風害の行。**倒壊 0 のときも数を出す** —— 「効いていない」と「近くに建物が
        /// 無い」を画面上で区別できるようにするため（<c>scanned</c> がその手がかり）。
        ///
        /// 設定で切っているときは数字を並べず、切っていることを言う
        /// （0 を並べると「動いているのに 1 棟も倒れない」と読める）。
        /// </summary>
        private static void RefreshWind(TyphoonSnapshot s)
        {
            if (!ModSettings.TyphoonWindDamage.value
                || ModSettings.TyphoonWindStrength.value <= 0)
            {
                TyphoonRows.SetPlain(_windLabel, Strings.TyphoonWindRow + ": off");
                return;
            }

            // 並びは Strings.TyphoonWindRow が語で名乗っている順:
            // 直近の走査の倒壊 / 累計 / 調べた棟数 / ゲームに断られた棟数。
            string text = Strings.TyphoonWindRow + ": "
                + s.WindLastCollapsed + " / " + s.WindTotalCollapsed + " / "
                + s.WindLastScanned + " / " + s.WindLastRefused
                // ★ 危険半円の向き。**左右が逆でもプレイヤーには気付けない**ので、
                //   風害が動いているあいだは常に出す。
                + "   " + (ModSettings.TyphoonSouthernHemisphere.value
                    ? Strings.TyphoonDangerousSideLeft
                    : Strings.TyphoonDangerousSideRight);

            // 外縁がまだ判定されていないことを黙って隠さない（巨大都市で起きる）。
            if (s.WindLastCapped) text += "   " + Strings.TyphoonWindCapped;

            TyphoonRows.SetPlain(_windLabel, text);
        }

        /// <summary>
        /// レベルアンロード時。**参照を捨てるだけ**（実体はパネルの GameObject と
        /// 一緒に消える）。
        /// </summary>
        internal static void Destroy()
        {
            _lightningLabel = null;
            _lightningNoteLabel = null;
            _lightningYieldedLabel = null;
            _windLabel = null;
            _windNoteLabel = null;
            _windShelterNoteLabel = null;
            _floodLabel = null;
            _floodReasonLabel = null;
            _floodNoteLabel = null;
            _tornadoLabel = null;
            _tornadoNdrNoteLabel = null;
            _cloudNoteLabel = null;
        }
    }
}
