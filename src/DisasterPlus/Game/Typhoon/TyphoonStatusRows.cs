using ColossalFramework.UI;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風そのものの状態を出す行（位置・進行方向・強さ・半径・位相・上陸予測と、
    /// バニラの雨量・雲量）。**main スレッド専用。**
    ///
    /// 行を作るのも文字を入れるのも <see cref="TyphoonRows"/> を通す。
    /// **このファイルに <c>UILabel</c> の生成も <c>.text</c> への代入も 1 つも無い。**
    ///
    /// ── ここが守っている 4 つの約束（設計書 §7）───────────────────────
    ///
    /// 1. **行ごとの出所の印は付けない。** ④の数値は原則すべて本 MOD のもので、
    ///    それはパネルの見出し（<c>TyphoonModelNote</c>）が一度だけ名乗る。
    ///    <b>唯一の例外</b>が雨量・雲量の 2 行で、そこだけ
    ///    <see cref="TyphoonRows.SetMeasured"/> を通す。**このファイルの
    ///    <c>SetMeasured</c> の呼び出しは 2 箇所しか無い。**
    /// 2. **「あと何分で上陸」は出してよい。** ④が経路を決定論的に持っているので
    ///    確定値である（①の天気予報パネルは乱数で発生を判定しているので同じことを
    ///    出せない）。<b>その違いを <c>TyphoonLandfallNote</c> が必ず併記する。</b>
    /// 3. **m/s を出さない。** ④の「強さ」は 0〜255 の強度と ASCII バーだけで出す。
    ///    <see cref="TyphoonProfile"/> に m/s を返すメソッドが無いのはそのためで、
    ///    ここでも作らないこと。
    /// 4. **読めない値は数字にしない。** 天候が読めていないときの雨量・雲量は
    ///    「0」ではなく「読み取れません」であり、そのときは <c>[measured]</c> も
    ///    付けない（読めていない値をバニラの実測値として名乗らない）。
    /// </summary>
    internal static class TyphoonStatusRows
    {
        /// <summary>rad → deg。</summary>
        private const float DegreesPerRadian = 57.29578f;

        /// <summary>強度の最大値。バーの分母に使う（<c>DisasterData.m_intensity</c> は byte）。</summary>
        private const float MaxIntensity = 255f;

        private static UILabel _stateLabel;
        private static UILabel _centreLabel;
        private static UILabel _strengthLabel;
        private static UILabel _radiusLabel;
        private static UILabel _phaseLabel;
        private static UILabel _landfallLabel;
        private static UILabel _landfallNoteLabel;
        private static UILabel _rainLabel;
        private static UILabel _cloudLabel;
        private static UILabel _windNoteLabel;

        /// <summary>パネル構築時に 1 回。行は常に作り、中身の有無で出し分ける。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            // 「台風が居ない」「まだ読んでいない」「読めない」「起こせなかった理由」を
            // 全部ここに出す。**折り返す高さを取る** —— refusal は英語の 1 文なので、
            // 折り返さない行に入れると途中で切れて理由が消える。
            _stateLabel = TyphoonRows.AddRow(p, "State", ref y, 40f);

            _centreLabel = TyphoonRows.AddRow(p, "Centre", ref y);
            _strengthLabel = TyphoonRows.AddRow(p, "CoreStrength", ref y);
            _radiusLabel = TyphoonRows.AddRow(p, "Radius", ref y);
            _phaseLabel = TyphoonRows.AddRow(p, "Phase", ref y);
            _landfallLabel = TyphoonRows.AddRow(p, "Landfall", ref y);

            // ★ 上陸予測の根拠。**予測の隣から離してはいけない**（設計書 §7-2）。
            _landfallNoteLabel = TyphoonRows.AddRow(p, "LandfallNote", ref y, 40f);

            // ★ ここから 2 行だけがバニラの実測値である。
            _rainLabel = TyphoonRows.AddMeasuredRow(p, "Rain", ref y);
            _cloudLabel = TyphoonRows.AddMeasuredRow(p, "Cloud", ref y);

            // 風向がゆっくりしか変わらないのはゲームの制約であって不具合ではない
            // （IL 事実文書 §A-4、m_directionSpeed は +0.001/step ずつしか上がらない）。
            // 常設で出す。
            _windNoteLabel = TyphoonRows.AddRow(p, "WindNote", ref y, 40f);
            TyphoonRows.SetPlain(_windNoteLabel, Strings.TyphoonWindDirectionNote);
        }

        /// <summary>パネル表示中に毎フレーム。<paramref name="s"/> は null でありうる。</summary>
        internal static void Refresh(TyphoonSnapshot s)
        {
            if (s == null)
            {
                // 「まだ 1 回も読んでいない」と「読んだが読めなかった」を同じ文言に
                // しない（①②が確立した規律）。ロード直後にポーズしたままだと前者が
                // 普通に起きる。
                TyphoonRows.SetPlain(_stateLabel, Strings.TyphoonWaiting);
                ClearTyphoonRows();
                ClearWeatherRows();
                return;
            }

            if (!s.Valid)
            {
                TyphoonRows.SetPlain(_stateLabel, Strings.TyphoonUnavailable);
                ClearTyphoonRows();
                ClearWeatherRows();
                return;
            }

            // 天候は台風の有無と関係なく読める。台風が居ないときも出す。
            RefreshWeatherRows(s);

            if (!s.Active)
            {
                TyphoonRows.SetPlain(_stateLabel, InactiveText(s));
                ClearTyphoonRows();
                return;
            }

            TyphoonRows.SetPlain(_stateLabel, "");
            RefreshTyphoonRows(s);
        }

        /// <summary>
        /// 台風が居ないときに出す 1 行。**「何も起きていない」と「起こせなかった」を
        /// 見分けられるようにする。**
        ///
        /// 優先順は「配置カーソルを構えている（地点待ち）」＞「依頼を出した直後
        /// （次の sim tick を待っている）」＞「プレハブが読めない（＝この環境では
        /// 1 個も起こせない）」＞「理由つきで断られた」＞
        /// 「ただ起きていない」。<see cref="TyphoonHub.PendingRequest"/> を見るのは
        /// **二度押しで 2 個発生する誤解を防ぐため**で、依頼から発生までには
        /// 設計上 1 tick の遅れがある（計画 §5.4）。
        /// </summary>
        private static string InactiveText(TyphoonSnapshot s)
        {
            // ★ 構えている間は「地図をクリックしてください」。これが無いと、
            //   タイルを押した直後のパネルが「台風は発生していません」のままになり、
            //   **プレイヤーは指す前にもう一度タイルを押す**。
            if (TyphoonPlacementTool.IsActive) return Strings.TyphoonPlaceHint;

            if (TyphoonHub.PendingRequest.Kind != TyphoonRequest.None) return Strings.TyphoonWaiting;

            // 設計書 §6: 読めなければ推測せず何もしない。**その事実を隠さない。**
            if (!s.Prefab.Usable) return Strings.TyphoonPrefabUnreadable;

            // refusal は診断用の英語 1 文（TyphoonSnapshot.Refusal）。翻訳は無いが、
            // 出さないほうが悪い —— これが「起こせなかった」の唯一の手がかりである。
            if (!string.IsNullOrEmpty(s.Refusal))
            {
                return Strings.TyphoonInactive + "  (" + s.Refusal + ")";
            }

            return Strings.TyphoonInactive;
        }

        private static void RefreshTyphoonRows(TyphoonSnapshot s)
        {
            TyphoonRows.SetPlain(_centreLabel,
                Strings.TyphoonCentre + ": (" + s.Centre.X.ToString("F0")
                + ", " + s.Centre.Z.ToString("F0") + ")    "
                // 度記号は言語に依らない記号なのでキーを作らない（"deg" と書くと
                // 日本語の画面に英単語が 1 つだけ残る）。
                + Strings.TyphoonHeading + ": " + DegreesOf(s.HeadingRadians).ToString("F0") + "°");

            // ★ **m/s は出さない**（設計書 §7-3）。0〜255 の強度と ASCII バーだけ。
            //   バーは TyphoonProfile.BarOf（10 段・ASCII 固定）を強度の割合に対して
            //   使い回している。あれは [0,1] を 10 段の棒にするだけの描画で、
            //   風速の意味を持ち込まない。
            TyphoonRows.SetPlain(_strengthLabel,
                Strings.TyphoonCoreStrength + ": " + s.Intensity + " / 255    "
                + TyphoonProfile.BarOf(s.Intensity / MaxIntensity));

            // 半径はプレハブ値から出す。読めていなければ 0 になるが、そのときは
            // そもそも台風が起きていない（TyphoonPrefabFacts.Usable）。念のため
            // 0 を「半径 0 m」として出さない。
            if (s.StormRadius > 0f)
            {
                TyphoonRows.SetPlain(_radiusLabel,
                    Strings.TyphoonStormRadius + ": " + s.StormRadius.ToString("F0") + " m    "
                    + Strings.TyphoonGaleRadius + ": " + s.GaleRadius.ToString("F0") + " m");
            }
            else
            {
                TyphoonRows.SetPlain(_radiusLabel,
                    Strings.TyphoonStormRadius + ": " + Strings.TyphoonUnavailable);
            }

            string phase = PhaseWordOf(s.Phase);
            TyphoonRows.SetPlain(_phaseLabel,
                phase == null ? "" : Strings.TyphoonPhaseLabel + ": " + phase);

            RefreshLandfallRows(s);
        }

        /// <summary>
        /// 上陸予測。**3 つの状態を混ぜない**（設計書 §7-2 / §7-4）:
        /// 既に陸の上／あと何分／このまま海上を通過。
        /// 最後の 2 つを「0 分」で表すと、まったく違う 2 つの状態が同じ顔になる。
        /// </summary>
        private static void RefreshLandfallRows(TyphoonSnapshot s)
        {
            string body;
            if (s.OverLand) body = Strings.TyphoonLandfallNow;
            else if (s.LandfallKnown)
            {
                // ★ F0。上陸フレームの推定は 256 フレーム（ゲーム内でおよそ 5.6 分）
                //   刻みでしか打っていないので、0.1 分は持っていない精度である
                //   （全体レビュー。TyphoonController.LandfallStepFrames の doc）。
                body = s.MinutesToLandfall.ToString("F0") + " " + Strings.TyphoonMinutes;
            }
            else body = Strings.TyphoonNoLandfall;

            TyphoonRows.SetPlain(_landfallLabel, Strings.TyphoonLandfall + ": " + body);

            // 注記は「あと何分」を出しているときだけ。上陸済み・上陸しないときに
            // 「到達時刻は確定値です」と書くと、存在しない予測の根拠を説明することになる。
            TyphoonRows.SetPlain(_landfallNoteLabel,
                !s.OverLand && s.LandfallKnown ? Strings.TyphoonLandfallNote : "");
        }

        /// <summary>
        /// **④で <c>[measured]</c> を名乗ってよい 2 行。** ここが
        /// <see cref="TyphoonRows.SetMeasured"/> を呼ぶ唯一の場所である。
        ///
        /// 読めていないときは <b><c>[measured]</c> を付けない</b> ——
        /// 読めなかった値をバニラの実測値として名乗ることになる。
        /// </summary>
        private static void RefreshWeatherRows(TyphoonSnapshot s)
        {
            if (!s.WeatherReadable)
            {
                TyphoonRows.SetPlain(_rainLabel,
                    Strings.TyphoonRainRow + ": " + Strings.TyphoonUnavailable);
                TyphoonRows.SetPlain(_cloudLabel,
                    Strings.TyphoonCloudRow + ": " + Strings.TyphoonUnavailable);
                return;
            }

            TyphoonRows.SetMeasured(_rainLabel,
                Strings.TyphoonRainRow + ": " + s.Rain.ToString("F2"));
            TyphoonRows.SetMeasured(_cloudLabel,
                Strings.TyphoonCloudRow + ": " + s.Cloud.ToString("F2"));
        }

        /// <summary>
        /// <see cref="TyphoonPhase.Idle"/> には語を当てない。名前を付けると
        /// 「進行中の位相のひとつ」に見える（②の <c>RefreshPhaseRow</c> と同じ判断）。
        /// </summary>
        private static string PhaseWordOf(TyphoonPhase phase)
        {
            switch (phase)
            {
                case TyphoonPhase.Approaching: return Strings.TyphoonPhaseApproaching;
                case TyphoonPhase.Peak: return Strings.TyphoonPhasePeak;
                case TyphoonPhase.Passing: return Strings.TyphoonPhasePassing;
                case TyphoonPhase.Gone: return Strings.TyphoonPhaseGone;
                default: return null;
            }
        }

        private static float DegreesOf(float radians)
        {
            return radians * DegreesPerRadian;
        }

        private static void ClearTyphoonRows()
        {
            TyphoonRows.SetPlain(_centreLabel, "");
            TyphoonRows.SetPlain(_strengthLabel, "");
            TyphoonRows.SetPlain(_radiusLabel, "");
            TyphoonRows.SetPlain(_phaseLabel, "");
            TyphoonRows.SetPlain(_landfallLabel, "");
            TyphoonRows.SetPlain(_landfallNoteLabel, "");
        }

        private static void ClearWeatherRows()
        {
            TyphoonRows.SetPlain(_rainLabel, "");
            TyphoonRows.SetPlain(_cloudLabel, "");
        }

        /// <summary>
        /// レベルアンロード時。**参照を捨てるだけ**（実体はパネルの GameObject と
        /// 一緒に消える）。持ち越すと、次の都市で破棄済みのラベルに書き込む。
        /// </summary>
        internal static void Destroy()
        {
            _stateLabel = null;
            _centreLabel = null;
            _strengthLabel = null;
            _radiusLabel = null;
            _phaseLabel = null;
            _landfallLabel = null;
            _landfallNoteLabel = null;
            _rainLabel = null;
            _cloudLabel = null;
            _windNoteLabel = null;
        }
    }
}
