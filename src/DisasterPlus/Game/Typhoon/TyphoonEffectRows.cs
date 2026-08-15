using ColossalFramework.UI;

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

        /// <summary>パネル構築時に 1 回。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            TyphoonRows.AddSectionHeader(p, "EffectsHeader", ref y, Strings.TyphoonEffectsHeader);

            _lightningLabel = TyphoonRows.AddRow(p, "Lightning", ref y);

            // 上限 20 発の説明。**常設**にする —— 「宿主の嵐に譲っている数」が
            // 何のことか、この 1 行が無いと分からない。
            _lightningNoteLabel = TyphoonRows.AddRow(p, "LightningNote", ref y, 40f);
            TyphoonRows.SetPlain(_lightningNoteLabel, Strings.TyphoonLightningNote);
        }

        /// <summary>パネル表示中に毎フレーム。<paramref name="s"/> は null でありうる。</summary>
        internal static void Refresh(TyphoonSnapshot s)
        {
            if (s == null || !s.Valid || !s.Active)
            {
                // 台風が居ないときに 0 を並べない（「撒いていない」と「0 発だった」は違う）。
                TyphoonRows.SetPlain(_lightningLabel, "");
                return;
            }

            // 並びは Strings.TyphoonLightningRow が語で名乗っている順:
            // 飛行中 / 累計 / 宿主の嵐に残している数 / 捨てられた数。
            TyphoonRows.SetPlain(_lightningLabel,
                Strings.TyphoonLightningRow + ": "
                + s.LightningInFlight + " / " + s.LightningTotal + " / "
                + s.LightningVanillaReserve + " / " + s.LightningRejected);
        }

        /// <summary>
        /// レベルアンロード時。**参照を捨てるだけ**（実体はパネルの GameObject と
        /// 一緒に消える）。
        /// </summary>
        internal static void Destroy()
        {
            _lightningLabel = null;
            _lightningNoteLabel = null;
        }
    }
}
