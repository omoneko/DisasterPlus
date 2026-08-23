using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 隆起 —— <c>RawHeights</c> の書き込みと分割 <c>UpdateArea</c>、そして山頂の火口。
    /// **sim スレッド専用。⑤が地形を書く唯一の型である。**
    ///
    /// ── ★★ 上げてよい半径は <c>VolcanoClearing.ClearedRadiusMetres</c> だけで決まる ──
    ///
    /// <code>
    /// activeRadius = UpliftSchedule.ActiveRadiusMetres(
    ///                    footprint.RadiusMetres, VolcanoClearing.ClearedRadiusMetres)
    /// </code>
    ///
    /// **第 2 引数にこれ以外を渡してはいけない**（計画の罠 1）。準備が届いていない場所を
    /// 上げると、道路は <c>Heights.PrimaryLevel</c> でセルを道路の y に、建物は
    /// <c>SecondaryLevel</c> で建物の y に**強制固定**し、しかもそれは毎フラッシュ
    /// ゼロからやり直される（§A-2）。**書き続けても勝てない。** 例外は 1 つも出ず、
    /// 「なんとなく変な山」——山の中の平らな溝とすり鉢——になる（設計書 §1.2）。
    /// <c>ClearedRadiusMetres</c> が 0 なら <c>ActiveRadiusMetres</c> は 0 を返し、
    /// この型は 1 セルも書かない。**Core 側のテストがその 0 を固定している。**
    ///
    /// **レビューの grep**: このファイルの <c>ActiveRadiusMetres</c> の呼び出しは 1 箇所で、
    /// 第 2 引数が <c>VolcanoClearing.ClearedRadiusMetres</c> であること。
    ///
    /// ── 増分ではなく「その時刻における絶対目標」を書く（罠 2）───────────────
    ///
    /// 素朴な <c>raw[i] += step</c> は**必ず無言で壊れる**。<c>RawHeights</c> は
    /// <c>ushort</c> の <c>raw/64</c> メートルで、書き込み側は <c>if (n != raw)</c> で
    /// 省略する（§C-8 IL_01E7）。1 tick の変化がセル高さで 1/64 m = 0.015625 m を
    /// 下回ると**丸めで消え、そのセルは永久に動かない**。外周ほど遅く上がるので、
    /// 素朴な実装では**裾だけが最初から完全停止する**。
    ///
    /// 絶対目標（<c>UpliftSchedule.RawTargetAt</c>）にすると、まだ 1 raw 単位に届かない
    /// セルは「書かれないだけ」で、進捗は <c>progress</c> という float に蓄積されている。
    /// 山頂だけは毎 tick 動かなければならないので、<c>UpliftSchedule.TotalTicksFor</c> が
    /// 要求 tick 数を <c>H×64</c> で切り詰める。**この切り詰めを外さないこと。**
    ///
    /// ── 山は一様に膨らむのではなく、山頂から外へ広がる ───────────────
    ///
    /// 1 セルの目標は <c>profile × progress</c> **ではない**。あれは山全体が同じ割合で
    /// 膨らむので、完成した山が音もなく地面から膨らんだように見える。
    /// SimCity 4 の隆起は逆で、噴出したものが**積もって**山になる。式は
    /// <c>UpliftSchedule.GrowthMetresAt</c> の 1 行:
    ///
    /// <code>
    /// grown(d, p) = max(0, profile(d) − H·(1 − p))
    /// </code>
    ///
    /// つまり「最終形を H(1−p) だけ地面へ沈めて、出ている分だけが今の山」である。
    /// 直線の円錐（成層）なら前線はちょうど <c>R·p</c> で、
    /// <c>UpliftSchedule.ClearingFrontMetres</c> が先行させる準備の前線と噛み合う。
    ///
    /// **罠 2 に対してはむしろ強くなる。** 育っているセルは<b>どれも同じ速さ</b>
    /// <c>H/totalTicks</c> で上がる（<see cref="RiseMetresPerTick"/>）ので、
    /// <c>TotalTicksFor</c> の切り詰めが**全セル**に効く。
    /// <c>profile × progress</c> では外周ほど 1 tick の変化が小さく、
    /// 「合計の盛り上がりが小さいセル」は丸めで消えていた。
    ///
    /// ── 山肌の凹凸は開始時に 1 回だけ焼く（<see cref="_profile"/>）──────────
    ///
    /// 形は <c>Core/Volcano/VolcanoRelief</c> が決める。半径 R も最終高 H も
    /// **決して超えない** —— 実効半径は縮む向きにしか動かず、起伏は削る向きにしか
    /// 働かない（掛け算だけで組んである。あちらのクラス doc）。
    /// 設定 <c>ModSettings.VolcanoReliefStrength</c> が 0 なら
    /// <c>VolcanoShape.ProfileAt</c> そのものに戻り、**今日の出力と 1 bit も違わない**。
    /// 1 セル 300 flop ほどあるので**毎 tick は呼ばない**。
    ///
    /// ── <see cref="_baseRaw"/> は開始時に 1 回だけ控える ───────────────────
    ///
    /// **毎 tick 読み直してはいけない。** 読み直すと⑤が前 tick に書いた値が「元の高さ」に
    /// なり、**プロファイルが毎 tick 積算されて山が天井まで伸びる**。
    /// 実費は影響矩形ぶんの <c>ushort</c>（半径 1 km で 31 KB、2 km で 123 KB、3 km で 279 KB。§E-13）。
    ///
    /// > **<c>TerrainManager.BackupHeights</c> / <c>UndoBuffer</c> を使わない**（§E-13）。
    /// > あれは <c>TerrainTool</c> / <c>DistrictTool</c> の専有物で、<c>TerrainTool.OnEnable</c> が
    /// > <c>RawHeights</c> 全体を複製し直す ——
    /// > **プレイヤーが整地ツールを開いた瞬間に⑤の退避が消える。**
    /// >
    /// > **この配列を「元に戻す機能」の土台だと誤解しないこと。** ⑤は火山を消す機能を
    /// > 持たない（設計書 §1.3 の「不可逆でよい」）。用途は
    /// > **「元の高さからの絶対目標を計算する」ことだけ**で、セーブにも残さない。
    ///
    /// ── <c>UpdateArea</c> は 1 tick にちょうど 1 回（罠 3）────────────────
    ///
    /// <c>UpdateArea</c> は矩形が 128×128 raw セルを超えた分を**タイル分割せずに無言で
    /// 切り捨てる**（§A-1 の <c>Min(m_maxX, m_minX + 120 + 8)</c>）。加えて**単発の要求面積が
    /// 10000 セルを超えると入れ子のバッチを無視して即フラッシュする**。
    /// <c>TileSplit</c> が両方を同時に満たす矩形（99×99 = 9801 セル）を返す。
    ///
    /// **全タイルを 1 tick で呼ばない。** 2 枚目以降は <c>merged &gt; 10000</c> の判定に
    /// 掛かって毎回途中フラッシュする（§A-1 IL_00A8）。
    ///
    /// ── 流すのは「実際に変わった矩形」だけ（2026-08-20、実機の指摘⑤）──────────
    ///
    /// 実機の指摘は「噴火のアニメーションをもっとスムーズに（現在は断続的な
    /// せり上がりです）」。**目に見える 1 段は「1 tick の上昇量」ではなく
    /// 「そのタイルが再び流されるまでの上昇量」である。**
    ///
    /// 以前はフットプリント全体（既定 R=1200 m で 151×151）を 4 枚に割って
    /// **1 tick 1 枚の総当たり**で流していたので、1 枚が流れ直すまで 4 tick ＝ 64 フレーム
    /// 掛かっていた。600 m / 85 tick = 7.06 m/tick なので、**見える 1 段は 28 m**、
    /// しかも 4 分割の四半分ずつ順番に跳ね上がる。指摘そのものである。
    ///
    /// 直し方は 2 つを同時にやる:
    ///
    ///   1. <see cref="IntervalFrames"/> を 16 → 4 にする（tick 数 85 → 341、
    ///      1 tick の上昇 7.06 m → 1.76 m）。**ゲーム内の所要時間は変えない**
    ///      （30 ゲーム内分のまま）ので、準備（<c>VolcanoClearing</c>）との
    ///      追いかけっこも噴火の包絡線との関係も 1 つも変わらない。
    ///   2. 流す矩形を**その tick に実際に書き換えたセルの外接矩形**にする。
    ///      隆起は山頂から外へ広がるので（<c>UpliftSchedule.GrowthMetresAt</c>）、
    ///      変わっているのは半径 R×progress の円盤だけである。既定の成層火山なら
    ///      progress 0.63 まで 95×95 セルに収まり、その間は
    ///      **変化した全域が毎 tick 1 回の <c>UpdateArea</c> で流れる**
    ///      ＝ 見える 1 段が 1.76 m、15 Hz（速度 1）になる。
    ///
    /// 収まらなくなったら（progress 0.63 以降）今までどおりタイル総当たりに落ちる ——
    /// そこでも 4 tick × 4 フレーム ＝ 16 フレームなので、見える 1 段は 7.0 m・3.75 Hz で、
    /// 従来の 28 m・0.94 Hz より 4 倍細かい。
    ///
    /// **<c>UpdateArea</c> の回数は 1 tick に 1 回のまま**で、1 回に渡す矩形も
    /// 99×99 = 9801 セルを超えない（<c>TileSplit.FitsSinglePass</c> が
    /// 95 セル以下でしか単発を許さない）。増えるのは**頻度だけ**である。
    /// 平均のフラッシュ面積は 356 → 約 1,370 セル/フレーム（既定の成層火山、
    /// オフライン試算）。**1 フレームあたりのピークは変わらない。**
    ///
    /// 継ぎ目に段差は出ない —— <c>TileAt</c> / <c>ExpandForPass</c> がどちらも
    /// ±2 セルの重なりを持って返すからである。
    ///
    /// ── ★★ 火口は「最後に彫る穴」ではなく「最初から在る窪み」（2026-08-22、指摘①）──
    ///
    /// 所有者の指摘は
    /// 「噴火口が一番最後に生成されるのではなく最初から窪みとして生成される方がいい」。
    ///
    /// かつてここは隆起の最後に <c>DisasterHelpers.MakeCrater</c> を 1 回だけ呼んでいた。
    /// あれは先頭で <c>TerrainModify.RefreshAllModifications()</c> を呼ぶ（§C-8 IL_0006）ので
    /// **呼ぶたびに強制フラッシュが 1 回走り**、毎 tick の経路には置けない ——
    /// つまり「最後に 1 回」という制約が、窪みの生まれる時刻をそのまま決めていた。
    ///
    /// **いまは火口を <see cref="_profile"/> そのものに畳み込んである**
    /// （<c>Core/Volcano/VolcanoCrater</c>）。隆起は毎 tick「その時刻における絶対目標」を
    /// 書いているので、目標の形に窪みが在れば窪みも山と一緒に育つ。
    /// 縁は <c>H·p</c>、底は <c>max(0, H·p − depth)</c> で上がり、窪みの深さは
    /// 進捗 <c>depth/H</c>（既定の成層火山で 10 %）で満杯になってからずっと一定である。
    /// <b>⑤は <c>MakeCrater</c> をもうどこからも呼ばない</b>（強制フラッシュも 1 回消えた）。
    ///
    /// **レビューの grep**（コメント行を落として 0 件）:
    /// <code>
    /// grep -rn --include=*.cs "MakeCrater" src/DisasterPlus/ \
    ///   | grep -vE ':[0-9]+: *//' | grep -v '///' | wc -l          # -> 0
    /// </code>
    ///
    /// ── 呼んではいけないもの（§D-12）───────────────────────────
    ///
    /// **<c>Begin/EndUpdateArea</c> を⑤が呼んではいけない。**
    /// <c>SimulationManager.SimulationStep</c> が先頭で <c>BeginUpdateArea</c>、末尾で
    /// <c>EndUpdateArea</c> を呼んでおり、MOD の sim tick フックは全部その内側にある。
    /// ⑤が足しても <c>m_modifyingLevel</c> が 1 増えて 1 減るだけで、内側の <c>End</c> は
    /// <c>if (loc1 != 0) return</c>（IL_0017）で握り潰される。害は無いが意味も無く、
    /// 「バッチしているつもり」という誤解だけが残る。
    /// **レビューの grep**（★ 実際に走らせて件数を合わせてある。全体レビュー M12 ——
    /// 素で走らせると**この規則の文そのもの**が 4 件引っかかり、
    /// 「0 件」という手順が最初から成立していなかった。コメント行を落とすこと）:
    ///
    /// <code>
    /// grep -rn --include=*.cs -E "BeginUpdateArea|EndUpdateArea" src/DisasterPlus/ \
    ///   | grep -vE ':[0-9]+: *//' | wc -l          # -> 0
    /// </code>
    ///
    /// <c>grep -v '///'</c> では足りない —— <c>//</c> 1 本のコメントも落とす必要がある。
    ///
    /// **main スレッドから <c>UpdateArea</c> を呼ぶのが本当に危ないほう**である。
    /// <c>m_modifyingLevel == 0</c> なので毎回フラッシュし、sim スレッドが溜めている
    /// 途中の蓄積と競合する。⑤の地形書き込みは全部この型（sim スレッド）に置く。
    ///
    /// ── 「建てられる地面」と水位は遅れる。これは不具合ではない（設計書 §7.3）─────
    ///
    /// <c>m_blockHeights</c> はゲームモードで上へ 2 m / 64 sim フレームしか動かず（§A-2）、
    /// <c>WaterSimulation.m_heightBuffer</c> は**その配列そのもの**である（§A-4）。
    /// 見た目（<c>m_finalHeights</c> / <c>m_detailHeights</c>）は即座に変わるが、
    /// 600 m の山なら 300 × 64 = 19200 sim フレーム ≒ 7 ゲーム内時間かけて追いつく。
    /// **隆起をこの速度に合わせて遅くしない** —— 合わせても <c>m_blockHeights</c> は
    /// 64 フレームに 1 回しか動かないので意味が無い。見積りをパネルと診断に出す。
    ///
    /// ── 1 tick あたりの仕事量の上限（明示する）──────────────────────
    ///
    /// 間隔は <see cref="IntervalFrames"/> フレームぶんの**経過ゲーム内時間**
    /// （<c>frameIndex % N</c> にしない。火災旋風 付録 A-4）。毎 sim tick にしないのは
    /// §A-3 のフィードバックループのためで、<c>m_flattenTerrain == false</c> の建物が
    /// 動くたび <c>BuildingManager.SimulationStepImpl</c> が追加の <c>UpdateArea</c> を出す。
    /// 準備段がフットプリント内の建物を取り除いているので残るのは⑤が壊せなかった建物だけだが、
    /// その規模は <c>VolcanoClearing.LastBuildingsRefused</c> がそのまま示す（診断に出す）。
    ///
    /// 開始時に 1 回だけ <c>float[]</c> 1 枚（<see cref="_profile"/>）を焼く。
    /// 実費は影響矩形ぶんの float で、半径 3 km の最大で 378² × 4 B = 558 KB。
    /// 焼いてしまえば毎 tick の仕事は引き算 1 本だけになり、
    /// 今日の「<c>sqrt</c> ＋ <c>ProfileAt</c>」より**むしろ安い**。
    ///
    /// 1 tick の上限は<b>影響矩形のセル数ぶんの配列書き込み 1 回</b>と
    /// <b><c>UpdateArea</c> ちょうど 1 回（99×99 = 9801 セル）</b>。
    /// 矩形は半径 R で <c>(2R/16 + 3)²</c> セル —— 既定の成層火山（R=1200 m）で 153² ≒ 23,409、
    /// 形態の最大（R=3000 m）でも 378² ≒ 142,884 である。
    /// <c>RawHeights</c> への書き込みは**ただの配列書き込み**で、
    /// <c>UpdateArea</c> を呼ぶまで誰も読まない（§D-12）。
    ///
    /// ── ファイルが 2 つに分かれている ────────────────────────────
    ///
    /// 地形を実際に触る部分（高さの書き込み・<c>UpdateArea</c>・火口）は
    /// <c>VolcanoUplift.Terrain.cs</c> にある。プロジェクト規約の 800 行を超えたための
    /// 分割で、**規律はこのクラス doc が全部持っている**
    /// （<c>VolcanoClearing.Sweep.cs</c> / <c>VolcanoLava.Ignite.cs</c> と同じ形）。
    /// </summary>
    /// <summary>
    /// <see cref="VolcanoUplift"/> が今どの形を書いているか。
    ///
    /// ── 破局噴火のために足した（2026-08-22、所有者の依頼）────────────────────
    ///
    /// &gt; 地下のマグマ上昇による火山の形成 → 数万年かけた巨大なマグマだまりの成長
    /// &gt; → 内圧限界による破局噴火（大爆発） → 地面の自重による大陥没とカルデラ形成
    ///
    /// 地形を書く仕組みは<b>1 本しか作らない</b>。矩形の取り方・退避・フラッシュ・
    /// 天井の数え方は 3 つとも同じで、違うのは<b>矩形の半径と、焼くプロファイルと、
    /// 進み方の規則</b>だけである。だから同じ型に段を持たせる。
    /// </summary>
    public enum UpliftStage
    {
        /// <summary>円錐を立てる（今までの唯一の形）。**山頂から外へ広がる。**</summary>
        Cone = 0,

        /// <summary>
        /// マグマだまりの膨らみ。**裾よりずっと広く、ごく低い**ドームを一様に持ち上げる
        /// （<c>SuperEruption.InflationAt</c>）。
        /// </summary>
        Inflation = 1,

        /// <summary>
        /// カルデラの陥没。**平底の窪地**を一様に掘り下げる
        /// （<c>SuperEruption.BowlProfileAt</c>。プロファイルは負）。
        /// </summary>
        Collapse = 2,
    }

    public static partial class VolcanoUplift
    {
        /// <summary>
        /// 隆起の間隔（フレーム相当のゲーム内時間）。**毎 sim tick にしない**（§A-3）。
        ///
        /// 16 → 4（2026-08-20、実機の指摘⑤）。**ゲーム内の所要時間は変わらない** ——
        /// <c>VolcanoUpliftMinutes</c>（既定 30 ゲーム内分 ＝ 1365 sim フレーム）を
        /// この間隔で割ったものが tick 数なので、間隔を 1/4 にすると tick が 4 倍になり
        /// 1 tick の上昇が 1/4 になるだけである。準備の前線（<c>ClearingFrontMetres</c>）は
        /// progress の関数で、progress の進み方はゲーム内時間で決まるので**変わらない**。
        ///
        /// **これ以上短くしないこと。** <c>UpdateArea</c> 1 回は対象矩形を detail 解像度
        /// （raw 1 セルにつき 4×4）で走査して 1 セルあたり <c>SmoothSample</c> を 5 回呼ぶ
        /// （§A-1）。99×99 のタイルで約 78 万回である。間隔を半分にすれば
        /// そのぶん毎フレームの平均が倍になる。
        ///
        /// ★ <b>1 段は 1 sim tick に 1 回までである。</b> <c>_minutesSinceTick</c> は
        ///   <c>interval</c> で頭打ちなので余りが繰り越されず、
        ///   <c>m_currentFrameIndex</c> は 1 tick で <c>FinalSimulationSpeed</c>
        ///   （速度 1/2/3 で 1/9 まで）進む。したがって 1 段の実効間隔は
        ///   <c>max(IntervalFrames, FinalSimulationSpeed)</c> フレームで、
        ///   **速度 2 / 3 では隆起にかかるゲーム内時間が伸びる**
        ///   （既定で 30 分 → 45 分 / 67 分。設計書 §4.3 の表）。
        ///   伸びる向きは安全側である —— 準備（64 フレーム間隔）は progress が
        ///   遅くなるぶん余裕が増え、噴火は育っている間ずっと持続の入口で止まる。
        ///   **1 tick に 2 回 <c>UpdateArea</c> を出して埋め合わせないこと。**
        /// </summary>
        private const int IntervalFrames = 4;

        /// <summary><c>RawHeights</c> の 1 行のセル数（1081²、§C-8）。</summary>
        private const int RawStride = 1081;

        /// <summary>期待する <c>RawHeights</c> の長さ。合わなければ 1 セルも書かない。</summary>
        private const int RawLength = RawStride * RawStride;

        /// <summary>
        /// 開始時に控えた「元の高さ」。**毎 tick 読み直さない**（クラス doc）。
        /// 火口を彫ったら捨てる。
        /// </summary>
        private static ushort[] _baseRaw;

        /// <summary>
        /// 開始時に 1 回だけ焼いた「最終形の盛り上がり」（m）。<see cref="_baseRaw"/> と同じ並び。
        ///
        /// <c>VolcanoRelief.ProfileAt</c> は 1 セル 300 flop ほどあるので、**毎 tick
        /// 全セルぶん呼ばない**。焼いてしまえば毎 tick の仕事は
        /// <c>UpliftSchedule.GrowthMetresAt</c>（引き算 1 本）だけになり、
        /// 今日の「sqrt ＋ ProfileAt」より**むしろ安くなる**。
        /// float で持つのは、強さ 0 のときに今日の出力と 1 bit も違わないようにするため
        /// （raw 単位へ丸めて持つと二重丸めで 1/64 m ずれる）。
        /// 半径 3 km の最大で 378² × 4 B = 558 KB。火口を彫ったら捨てる。
        /// </summary>
        private static float[] _profile;

        private static int _minX, _minZ, _maxX, _maxZ;
        private static int _width;

        private static int _tileCount;

        /// <summary>
        /// **どの矩形をいつ流すか**を決める（<c>Core/Volcano/UpliftFlushPlan</c>）。
        /// 判断は整数演算だけなので Core にあり、ユニットテストと
        /// <c>tools/VolcanoPreview</c> がゲームを起動せずに同じ物を回せる。
        /// </summary>
        private static readonly UpliftFlushPlan _flush = new UpliftFlushPlan();

        /// <summary>この tick に実際に書き換えたセルの外接矩形。<see cref="_dirtyValid"/> で有効判定。</summary>
        private static int _dirtyMinX, _dirtyMinZ, _dirtyMaxX, _dirtyMaxZ;
        private static bool _dirtyValid;

        private static int _tick;
        private static int _totalTicks;

        private static float _progress;
        private static float _riseMetresPerTick;
        private static float _activeRadius;
        private static float _summitMetres;
        private static int _cellsWrittenLastTick;

        /// <summary>
        /// 直近の tick で**ゲームの高さの天井（1024 m）に当たって削られた**セル数。
        /// 0 でないなら山頂は平らになっている。
        /// 天井を上げられない理由は <c>UpliftSchedule.CeilingClipped</c> の doc にある。
        /// </summary>
        private static int _ceilingClippedCells;

        private static bool _started;
        private static bool _complete;

        /// <summary>今どの形を書いているか。<see cref="StartStage"/> だけが入れる。</summary>
        private static UpliftStage _stage = UpliftStage.Cone;

        /// <summary>この山の最終高（m）。**火口の深さと底の高さを出すのに使う。**</summary>
        private static float _heightMetres;

        /// <summary>
        /// 火口の縁が H に届くよう円錐を立て直した倍率（<c>VolcanoCrater.SummitScale</c>）。
        /// **準備の前線を決めるのにも要る** —— 隆起の前線がこの倍率のぶん先へ出るので、
        /// 倍率を渡さないと準備が追いつかず、山の外周が切り立った円で止まって見える。
        /// </summary>
        private static float _summitScale = 1f;

        private static Vec3 _centre;
        private static float _minutesSinceTick;

        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>隆起の進捗 [0,1]。**準備が届いていない間は進まない。**</summary>
        public static float ProgressUnit { get { return _progress; } }

        /// <summary>
        /// 今この tick に上げてよい半径（m）。**＝準備が届いた範囲**（クラス doc）。
        /// パネルはこれに「準備が届いた範囲」と添えて出す —— 罠 1 を実機で
        /// 目で確かめられる唯一の行である。
        /// </summary>
        public static float ActiveRadiusMetres { get { return _activeRadius; } }

        /// <summary>今の山頂の盛り上がり（m）。元の地形高さからの相対量である。</summary>
        public static float SummitMetres { get { return _summitMetres; } }

        /// <summary>
        /// 山頂が**ゲームの高さの天井で削られた**セルの数（直近の tick）。
        /// **0 でないのは不具合ではないが、黙っていてもいけない** ——
        /// 高い土地に大きな山を置くとここが増え、山頂が平らになる。
        /// 天井は 1023.98 m で、**MOD からは上げられない**
        /// （<c>UpliftSchedule.CeilingClipped</c> の doc に IL 実測と理由）。
        /// </summary>
        public static int CeilingClippedCells { get { return _ceilingClippedCells; } }

        /// <summary>
        /// 育っているセルが 1 tick で上がる量（m）。**山頂から外へ広がる隆起では
        /// どのセルも同じ速さで上がる**（<c>UpliftSchedule.GrowthMetresAt</c> の doc）。
        ///
        /// <c>VolcanoLava</c> がこれを「地形が上がっているぶんの許容差」として使う ——
        /// 隆起の途中に出した溶岩は、進んだ先の標高が**溶岩のせいではなく山のせいで**
        /// 上がることがあり、その分を許さないと下り勾配の符号の観測が誤って発火する。
        /// 隆起が終わっていれば 0 である。
        /// </summary>
        public static float RiseMetresPerTick
        {
            get { return _complete ? 0f : _riseMetresPerTick; }
        }

        /// <summary>
        /// 育っているセルが**1 sim フレーム**で上がる量（m）。
        ///
        /// ★ <see cref="RiseMetresPerTick"/> をそのまま他の機能へ渡さないこと。
        ///   ⑤の各段は間隔が違う（隆起 <see cref="IntervalFrames"/> ＝ 4、
        ///   溶岩 8）ので、「1 tick」の長さが揃っていない。**溶岩が要るのは
        ///   「自分の 1 歩のあいだに地形がどれだけ上がるか」**であり、
        ///   それはこの値に溶岩自身の間隔を掛けたものである。
        ///   隆起の間隔を変えた瞬間に溶岩の許容差が狂うのを、この単位が防ぐ。
        /// </summary>
        public static float RiseMetresPerFrame
        {
            get { return _complete ? 0f : _riseMetresPerTick / IntervalFrames; }
        }

        /// <summary>隆起が終わったか。</summary>
        public static bool Complete { get { return _complete; } }

        /// <summary>今書いている形。診断と、状態機械が段を見分けるのに使う。</summary>
        public static UpliftStage Stage { get { return _stage; } }

        /// <summary>
        /// 山頂の窪みが満杯の深さに達したか。**「彫ったか」ではない** ——
        /// 火口は形の一部なので最初の tick から在り、縁が <c>depth</c> だけ上がった時点で
        /// 深さが揃う（クラス doc）。
        /// </summary>
        public static bool CraterFormed
        {
            get { return VolcanoCrater.FullDepthReached(_summitMetres, _heightMetres); }
        }

        /// <summary>
        /// 今の火口の底の盛り上がり（m。元の地形高さからの相対量）。**診断に出すためだけ**に在る。
        ///
        /// ★ **噴出口の Y はここから取っていない。** あちらは
        ///   <c>VolcanoEruption.SampleVent</c> が中心の地形を毎 tick 引き直したもので、
        ///   <c>UpdateArea</c> の遅れまで含んだ「実際に描かれている高さ」である。
        ///   こちらは⑤の予定（モデル側の値）なので、**2 つがずれていたら
        ///   地形の反映が遅れている**ことが読み取れる。それがこの行の使い道である。
        /// </summary>
        public static float CraterFloorMetres
        {
            get { return VolcanoCrater.FloorMetresAt(_summitMetres, _heightMetres); }
        }

        /// <summary>
        /// 隆起の前線が今どこまで出ているか [0,1]。**準備（<c>VolcanoClearing</c>）へ渡すのは
        /// 進捗そのものではなくこちら**である（<c>UpliftSchedule.GrowthFrontUnit</c> の doc）。
        /// </summary>
        public static float GrowthFrontUnit
        {
            get { return UpliftSchedule.GrowthFrontUnit(_progress, _summitScale); }
        }

        /// <summary>これまでに進んだ tick 数。</summary>
        public static int Ticks { get { return _tick; } }

        /// <summary>隆起に使う tick 数（<c>UpliftSchedule.TotalTicksFor</c> で切り詰め済み）。</summary>
        public static int TotalTicks { get { return _totalTicks; } }

        /// <summary>直近 1 tick で実際に値が変わったセル数（0 なら丸めで消えている）。</summary>
        public static int CellsWrittenLastTick { get { return _cellsWrittenLastTick; } }

        /// <summary>
        /// **今この瞬間、変わった範囲を画面に出し切るのに要る <c>UpdateArea</c> の回数。**
        ///
        /// 1 なら「その tick に変わった全域が同じ tick で流れている」＝ いちばん滑らかな状態
        /// （隆起の前半はここ）。2 以上なら分割してタイル総当たりに落ちており、
        /// 見える 1 段はこの回数ぶんの上昇量になる（クラス doc の計算）。
        /// **フットプリント全体のタイル数ではない** —— そちらは <see cref="FootprintTileCount"/>。
        /// </summary>
        public static int TileCount { get { return _flush.TileCount; } }

        /// <summary>総当たりの何枚目か。単発で流せているときは 0。</summary>
        public static int TileCursor { get { return _flush.Cursor; } }

        /// <summary>フットプリント全体を覆うタイル数（開始時に 1 回決まる）。</summary>
        public static int FootprintTileCount { get { return _tileCount; } }

        /// <summary>
        /// 直近に上げられなかった理由（**英語・診断用**）。上げられていれば null。
        /// **黙って何もしないをやらない**ための口である。
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// 火山を手放すときとレベルアンロードで呼ぶ。冪等である。
        /// **既に変わった地形は戻らない**（不可逆・設計書 §1.3）。畳むのは予定だけである。
        /// </summary>
        public static void Reset()
        {
            // ★ 退避配列は必ず捨てる。半径 3 km で 279 KB あり、都市をまたいで
            //    持ち越すと前の都市の地形を「元の高さ」として名乗ることになる。
            _baseRaw = null;
            _profile = null;
            _minX = 0;
            _minZ = 0;
            _maxX = 0;
            _maxZ = 0;
            _width = 0;
            _tileCount = 0;
            _flush.Reset();
            _dirtyValid = false;
            _dirtyMinX = 0;
            _dirtyMinZ = 0;
            _dirtyMaxX = 0;
            _dirtyMaxZ = 0;
            _tick = 0;
            _totalTicks = 0;
            _progress = 0f;
            _riseMetresPerTick = 0f;
            _activeRadius = 0f;
            _summitMetres = 0f;
            _cellsWrittenLastTick = 0;
            _ceilingClippedCells = 0;
            _started = false;
            _complete = false;
            _stage = UpliftStage.Cone;
            _heightMetres = 0f;
            _summitScale = 1f;
            _centre = new Vec3(0f, 0f, 0f);
            _minutesSinceTick = 0f;
            _lastFailure = null;

            // _errorLogged は戻さない（この DLL が参照しているゲームのビルドに対する事実）。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>VolcanoFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に山が育つ）。<paramref name="frame"/> は診断用で、
        /// **周期の判定には使わない**（<c>frameIndex % N</c> にしない）。
        /// </summary>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes)
        {
            try
            {
                Step(footprint, deltaMinutes);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the uplift tick threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano uplift failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcUplift",
                             "volcano uplift failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre))
            {
                // ★ 自分から始まるのは**円錐だけ**である。膨らみとカルデラは
                //   VolcanoState が StartStage で明示的に始める。
                if (!StartStage(footprint, UpliftStage.Cone)) return;
            }

            if (_complete) return;

            // ★ 間隔の累積は対象より先に進める（④の TyphoonWind と同じ形）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSinceTick += deltaMinutes;
            if (interval > 0f && _minutesSinceTick > interval) _minutesSinceTick = interval;

            if (framesPerMinute <= 0f) return;
            if (_minutesSinceTick < interval) return;

            // ★★ **罠 1。第 2 引数はこれ以外を渡してはいけない。**
            _activeRadius = UpliftSchedule.ActiveRadiusMetres(
                footprint.RadiusMetres, VolcanoClearing.ClearedRadiusMetres);

            // 準備が 1 mm も届いていないなら、tick も進めない。進めると
            // 「上げていないのに進捗だけ終わる」——山が育たないまま完成する。
            // **累積は消費しない**ので、準備が届いた次の tick で即座に走る。
            if (!(_activeRadius > 0f)) return;

            _minutesSinceTick = 0f;

            _progress = UpliftSchedule.ProgressAt(_tick, _totalTicks);
            // 山頂のプロファイルは H なので、山頂の盛り上がりは今も H×progress である。
            // ★★ **カルデラでは山頂ではなく「床がどれだけ落ちたか」である。**
            //    ここで正の値を入れると、火山タブが陥没を「+400 m の山頂」と名乗る。
            _summitMetres = _stage == UpliftStage.Collapse
                ? -footprint.HeightMetres * _progress
                : UpliftSchedule.GrowthMetresAt(
                      footprint.HeightMetres, footprint.HeightMetres, _progress);

            if (!WriteHeights(footprint)) return;
            FlushPending();

            if (_tick < _totalTicks)
            {
                _tick++;
                return;
            }

            // ★★ **準備が外周まで届くまでは終わらせない。**
            //    書いたのは activeRadius の内側だけなので、ここで打ち切ると
            //    activeRadius と R のあいだの帯が**元の高さのまま残る**——
            //    山の外側に環状の段差ができる。準備の走査は隆起より間隔が長い
            //    （64 対 4 フレーム）ので、この待ちは普通に発生する。
            //    進捗はもう 1 なので、待っている間の WriteHeights は
            //    「準備が届いた分だけ」を毎 tick 埋め足していく。
            if (_activeRadius < footprint.RadiusMetres - VolcanoShape.MetresPerRawUnit) return;

            // ★★ **目標の高さは書き終えたが、まだ全部は見えていない。**
            //    1 tick に流せる矩形は 1 枚なので（罠 3）、最後の書き込みのうち
            //    画面に出ているのは 1 枚ぶんだけで、残りは 1 tick 前の高さ
            //    ——山頂の 1/_totalTicks ぶん低い形——のまま止まっている。
            //    **そこで火口を彫って終わると、山の外側が永久に低いまま残る。**
            //    全タイルをここでまとめて呼ぶと merged > 10000 の判定で毎回
            //    途中フラッシュするので（§A-1 IL_00A8）、**1 tick 1 枚のまま
            //    残りを流し切ってから**火口へ進む。
            //
            //    progress = 1 に届いた以降の WriteHeights は 0 セルしか書かないので、
            //    _flush.HasPending は流し切った時点で自然に false になる。
            //    **枚数を数え直さないこと** —— 数えると、待っている間に届いた
            //    最後の書き込み（準備が外周に届いた瞬間の分）を取りこぼす。
            if (_flush.HasPending) return;

            // ★ 火口はここで彫らない。**最初の tick から形の一部として在る**（クラス doc）。
            _progress = 1f;
            _summitMetres = _stage == UpliftStage.Collapse
                ? -footprint.HeightMetres
                : footprint.HeightMetres;
            _complete = true;

            // もう使わない。メモリを返す（クラス doc の実費表）。
            _baseRaw = null;
            _profile = null;
        }

        /// <summary>
        /// <paramref name="stage"/> の形を書きはじめる。**円錐以外もここを通る**
        /// （<see cref="UpliftStage"/>）。矩形・退避・フラッシュ・天井の数え方は
        /// 3 段とも同じで、違うのは焼くプロファイルと進み方だけである。
        ///
        /// <paramref name="footprint"/> は<b>その段ぶんに広げたもの</b>を渡すこと
        /// （<c>VolcanoFootprint.Resized</c>）。半径をここで広げないのは、
        /// 準備（<c>VolcanoClearing</c>）と同じ半径を見ていないと
        /// 道路の下だけ地面が押し戻されるからである（設計書 §1.2 / 罠 1）。
        ///
        /// ★★ <b><see cref="Reset"/> より後に段を入れる。</b> Reset を挟んで
        ///   段を持ち回すと、前の火山のカルデラ段が次の火山の円錐に化ける。
        /// </summary>
        internal static bool StartStage(VolcanoFootprint footprint, UpliftStage stage)
        {
            Reset();
            _stage = stage;
            return StartCore(footprint);
        }

        /// <summary>
        /// 影響矩形を決めて「元の高さ」を控える。**開始時に 1 回だけ**（クラス doc）。
        /// 失敗したら <see cref="_lastFailure"/> を残して false を返す。
        /// **<see cref="Reset"/> はここでは呼ばない**（<see cref="StartStage"/> が済ませている）。
        /// </summary>
        private static bool StartCore(VolcanoFootprint footprint)
        {
            ushort[] raw = ReadRawHeights();
            if (raw == null) return false;

            if (!TileSplit.CellRangeFor(footprint.Centre.X, footprint.Centre.Z,
                                        footprint.RadiusMetres,
                                        out _minX, out _minZ, out _maxX, out _maxZ))
            {
                _lastFailure = "the volcano footprint does not cover a single terrain cell";
                return false;
            }

            _width = _maxX - _minX + 1;
            int height = _maxZ - _minZ + 1;
            _baseRaw = new ushort[_width * height];

            for (int z = 0; z < height; z++)
            {
                int source = (_minZ + z) * RawStride + _minX;
                Array.Copy(raw, source, _baseRaw, z * _width, _width);
            }

            _tileCount = TileSplit.TileCountFor(_minX, _minZ, _maxX, _maxZ);

            // ★ 火口の分だけ円錐を立て直す倍率。**準備の前線もこれを見る**（_summitScale の doc）。
            _heightMetres = footprint.HeightMetres;
            // ★ 火口のぶんの立て直しは**円錐にしか要らない**。膨らみもカルデラも
            //   山頂を彫らないので、1 のままでよい（掛けると前線が実際より先へ出る）。
            _summitScale = _stage == UpliftStage.Cone
                ? VolcanoCrater.SummitScale(footprint.Form, footprint.RadiusMetres)
                : 1f;

            // ★ 山頂が毎 tick 1 raw 単位以上動くよう切り詰める（罠 2）。
            //   換算は FeatureHost.FramesPerMinute から出す（定数を直書きしない）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            int requestedTicks = framesPerMinute > 0f
                ? (int)(StageMinutes() * framesPerMinute / IntervalFrames)
                : 1;
            _totalTicks = UpliftSchedule.TotalTicksFor(footprint.HeightMetres, requestedTicks);

            // ★ 育っているセルはどれも同じ速さで上がる（GrowthMetresAt の doc）。
            //   TotalTicksFor が totalTicks を H×64 で切り詰めているので、
            //   これは必ず 1 raw 単位（1/64 m）以上である。
            _riseMetresPerTick = footprint.HeightMetres / _totalTicks;

            BakeProfile(footprint, height);

            _centre = footprint.Centre;
            _started = true;
            _lastFailure = null;

            Log.Info("volcano uplift started: rect " + _width + "x" + height
                     + " cells, " + _tileCount + " tiles, " + _totalTicks + " ticks, relief "
                     + ModSettings.VolcanoReliefStrength.value + "%, crater r="
                     + VolcanoShape.CraterRadiusOf(footprint.RadiusMetres).ToString("F0")
                     + " m depth=" + VolcanoShape.CraterDepthOf(footprint.HeightMetres).ToString("F0")
                     + " m (part of the profile from the first tick), cone scale "
                     + _summitScale.ToString("F3"));
            return true;
        }

        /// <summary>
        /// 最終形の盛り上がりを 1 回だけ全セルぶん焼く（<see cref="_profile"/>）。
        ///
        /// **ここが⑤で唯一 <c>VolcanoRelief</c> を呼ぶ場所である。** 毎 tick 呼ぶと
        /// 半径 3 km で 1 tick 当たり 14 万セル × 300 flop になる。
        /// 種は火山の地点から出す（<c>VolcanoEruption</c> / <c>VolcanoLava</c> と同じ作り方）
        /// ので、**同じ場所に作り直せば同じ山が生える**。
        /// <c>VanillaRandomizer</c> は使わない —— ⑤はバニラの災害スロットに載らないので、
        /// 同期すべきバニラの引きが構造上 1 つも存在しない。
        /// </summary>
        private static void BakeProfile(VolcanoFootprint footprint, int height)
        {
            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.Z)));

            var relief = VolcanoRelief.For(footprint.Form, seed,
                                           ModSettings.VolcanoReliefStrength.value / 100f);

            float centreX = footprint.Centre.X;
            float centreZ = footprint.Centre.Z;
            float radius = footprint.RadiusMetres;
            float metres = footprint.HeightMetres;
            float radiusSquared = radius * radius;

            _profile = new float[_width * height];

            for (int z = 0; z < height; z++)
            {
                float worldZ = (_minZ + z - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                float dz = worldZ - centreZ;
                float dz2 = dz * dz;
                if (dz2 > radiusSquared) continue;

                int row = z * _width;
                for (int x = 0; x < _width; x++)
                {
                    float worldX = (_minX + x - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                    float dx = worldX - centreX;
                    if (dx * dx + dz2 > radiusSquared) continue;

                    // ★★ **半径の外は 0、最終高 H は超えない。** 起伏は掛け算だけ、
                    //    火口は min だけで働くので、どちらも構造的に守られている
                    //    （VolcanoRelief / VolcanoCrater のクラス doc）。
                    //    **山頂の窪みはここで入る。あとから彫らない。**
                    _profile[row + x] = ProfileFor(relief, dx, dz, radius, metres);
                }
            }
        }

        /// <summary>
        /// <c>RawHeights</c> を取る。**長さが 1081² でなければ 1 セルも書かない** ——
        /// <c>z*1081 + x</c> の添字が別のセルを指し、**マップの無関係な場所が隆起する**
        /// （<see cref="VolcanoTerrainFacts.Usable"/> と同じ述語）。
        ///
        /// <c>Singleton&lt;T&gt;.exists</c> を先に見る（<c>instance</c> は main スレッド専用 API）。
        /// </summary>
        /// <summary>
        /// 今の段にかける時間（ゲーム内分）。
        ///
        /// ★ 陥没は**速い**。屋根が抜けて落ちるのに数万年はかからない ——
        ///   数万年かかるのは<b>その前のマグマだまりの成長</b>のほうである。
        ///   膨らみは逆にゆっくりで、隆起より長くかける。
        /// </summary>
        private static float StageMinutes()
        {
            float baseMinutes = ModSettings.VolcanoUpliftMinutes.value;

            switch (_stage)
            {
                case UpliftStage.Inflation: return baseMinutes * 1.5f;
                case UpliftStage.Collapse: return baseMinutes * 0.35f;
                default: return baseMinutes;
            }
        }

        /// <summary>
        /// 1 セルぶんのプロファイル（m）。**カルデラだけ負を返す。**
        ///
        /// ★ <paramref name="radius"/> と <paramref name="metres"/> は
        ///   <b>その段ぶんに広げた影響範囲そのもの</b>である（<c>VolcanoState</c> が
        ///   <c>VolcanoFootprint.Resized</c> で作って渡す）。ここで倍率を掛け直さない ——
        ///   掛けると準備（<c>VolcanoClearing</c>）と隆起の見ている半径がずれて、
        ///   道路の下の地面だけ押し戻される（設計書 §1.2 / 罠 1）。
        /// </summary>
        private static float ProfileFor(VolcanoRelief relief, float dx, float dz,
                                        float radius, float metres)
        {
            switch (_stage)
            {
                case UpliftStage.Inflation:
                    return SuperEruption.InflationAt(
                        (float)Math.Sqrt(dx * dx + dz * dz), radius, metres);

                case UpliftStage.Collapse:
                    return SuperEruption.BowlProfileAt(
                        (float)Math.Sqrt(dx * dx + dz * dz), radius, metres);

                default:
                    return VolcanoCrater.ProfileAt(relief, dx, dz, radius, metres);
            }
        }

        private static ushort[] ReadRawHeights()
        {
            if (!Singleton<TerrainManager>.exists)
            {
                _lastFailure = "TerrainManager is not available";
                return null;
            }

            var tm = Singleton<TerrainManager>.instance;
            if (tm == null)
            {
                _lastFailure = "TerrainManager is not available";
                return null;
            }

            ushort[] raw = tm.RawHeights;
            if (raw == null || raw.Length != RawLength)
            {
                _lastFailure = "RawHeights is not a ushort[1081^2]; nothing was raised";
                return null;
            }

            return raw;
        }

        /// <summary>1/64 m（raw 1 単位）より細かい差は「同じ地点」とみなす。</summary>
        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }

        /// <summary>
        /// **セルを 1 つも書かなかった tick も出す。** 「機能が死んでいる」と
        /// 「もう目標に届いている」がログ上で区別できなくなる。
        /// 引数の文字列連結は毎回走るので <c>DiagEnabled</c> で先に落とす。
        /// </summary>
        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcUplift",
                "uplift frame=" + frame
                + " tick " + _tick + "/" + _totalTicks
                + " progress=" + _progress.ToString("F3")
                + " summit=" + _summitMetres.ToString("F1")
                + " active=" + _activeRadius.ToString("F0")
                + " cells=" + _cellsWrittenLastTick
                // ★ flush=1/1 は「この tick に変わった全域が同じ tick で画面に出た」
                //   ＝ いちばん滑らかな状態。2 以上なら分割して総当たりに落ちており、
                //   見える 1 段はその枚数ぶんの上昇量になる（クラス doc）。
                + " flush=" + _flush.Cursor + "/" + _flush.TileCount
                + " rect=" + (_dirtyValid ? (_dirtyMaxX - _dirtyMinX + 1) + "x"
                                            + (_dirtyMaxZ - _dirtyMinZ + 1) : "0")
                + " tiles=" + _tileCount
                + " crater=" + (CraterFormed ? "full" : "growing")
                // ★ **0 のときも出す**。出さないと「削られていない」と
                //   「削られたかどうか見ていない」がログ上で区別できない。
                + " ceilingClipped=" + _ceilingClippedCells
                + " floor=" + CraterFloorMetres.ToString("F1")
                + (_complete ? " (complete)" : ""));
        }
    }
}
