using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 溶岩の流れ 1 本。**不変の値型**で、進めるたびに新しい 1 個を作る
    /// （この MOD の不変の規律。書き換えない）。
    /// </summary>
    public struct LavaFlow
    {
        /// <summary>まだ動いているか。false なら <see cref="StopReason"/> に理由が入る。</summary>
        public readonly bool Alive;

        /// <summary>今の先端のワールド座標（<c>X</c> / <c>Z</c>）。</summary>
        public readonly Vec2 Head;

        /// <summary>火口を出てからの距離（m）。広がりの計算に使う。</summary>
        public readonly float TravelledMetres;

        /// <summary>これまでに踏んだ歩数。<c>LavaPath.MaxSteps</c> で必ず止まる。</summary>
        public readonly int Steps;

        /// <summary>止まった理由（<see cref="VolcanoLava"/> の <c>Stop*</c> 定数）。</summary>
        public readonly int StopReason;

        public LavaFlow(bool alive, Vec2 head, float travelledMetres, int steps, int stopReason)
        {
            Alive = alive;
            Head = head;
            TravelledMetres = travelledMetres;
            Steps = steps;
            StopReason = stopReason;
        }
    }

    /// <summary>
    /// 火口から出た溶岩の前進と着火。**sim スレッド専用。**
    ///
    /// ── 勾配の符号は本タスクで IL を読んで確定させた（推測していない）─────────
    ///
    /// 計画 §8.1 は「<c>slopeX</c> / <c>slopeZ</c> が上りか下りかを読んでいない」と
    /// 名指ししていた。**読んだ。**
    ///
    /// <code>
    /// TerrainManager.SampleDetailHeight(float x, float z, out slopeX, out slopeZ)
    ///   h00 = GetDetailHeight(x0, z0)   h10 = GetDetailHeight(x0+1, z0)
    ///   h01 = GetDetailHeight(x0, z0+1) h11 = GetDetailHeight(x0+1, z0+1)
    ///   IL_0071  *slopeX = (h10 + h11 - h00 - h01) * 0.5      // = 平均 (h(x+1) - h(x))
    ///   IL_0084  *slopeZ = (h01 + h11 - h00 - h10) * 0.5      // = 平均 (h(z+1) - h(z))
    ///
    /// TerrainManager.SampleDetailHeight(Vector3, out slopeX, out slopeZ)
    ///   IL_0033  戻り値 *= 0.015625       // = 1/64。raw -> メートル
    ///   IL_003B  *slopeX *= 0.00390625    // = (1/64) / 4 m。**無次元の勾配（m/m）**
    ///   IL_0045  *slopeZ *= 0.00390625
    /// </code>
    ///
    /// ★★ <b><c>slopeX</c> / <c>slopeZ</c> は「+x / +z へ進むと高さがどれだけ増えるか」
    /// ＝ 上り方向の勾配である。したがって下り方向は符号を反転した
    /// <c>(-slopeX, -slopeZ)</c> になる。</b> 単位は無次元（m/m）なので、
    /// <c>LavaPath.MinSlope = 0.002</c> はそのまま「0.2 % の傾き」を意味する。
    ///
    /// **それでも実行時の観測を止めない**（計画 §8.1）。最初の
    /// <see cref="ObservationSteps"/> 歩のあいだ先端の標高が上がっていないかを見て、
    /// 上がったらその流れを止め、<see cref="SlopeSignVerified"/> を false にして
    /// <c>Log.Warn</c> を**1 回だけ**出す。
    /// **符号を実行時に反転させて「直そう」としない** —— 直った気になって
    /// 別の環境で逆に壊れる。止めて名乗るほうが正しい。
    ///
    /// ── 1 tick あたりの仕事量（明示的に区切る）────────────────────────
    ///
    /// <code>
    /// 流れの本数            <= MaxFlows = 8            （設定の上限。0 で完全に無効）
    /// 前進する間隔          IntervalFrames = 8 sim フレームぶんのゲーム内時間
    /// 1 回の前進の歩数      <= MaxStepsPerTickPerFlow = 2  （1 本あたり）
    ///  => 1 回の前進で      <= 16 歩
    /// 1 歩あたり            SampleDetailHeight 1 回（4 読み + 3 Lerp、§B-6）
    ///                       HasWater 1 回（BeginRead/EndRead の中で 4 セル、§A-4）
    ///                       BurnGround 1 回（半径 <= 60 m なので 512^2 のうち高々 5x5 セル）
    ///                       建物グリッド <= MaxBuildingCellsPerStep = 25 セル
    ///                       樹木グリッド <= MaxTreeCellsPerStep = 49 セル
    /// 確保                  前進した回だけ Vec2[<= 8*128] と int[<= 8]（軌跡のコピー）
    /// </code>
    ///
    /// **`frameIndex % N` で周期を組まない**（1 ゲーム内分 ≒ 45.51 フレーム。
    /// 火災旋風 付録 A-4）。間隔は経過ゲーム内時間の積算で判定する。
    ///
    /// ── 着火の 3 経路と、DLC 分岐は 1 つだけ（§B-7）────────────────────
    ///
    /// <code>
    /// 地面   DisasterHelpers.BurnGround(Vector2, radius, intensity)   DLC 不要
    /// 建物   BuildingAI.BurnBuilding(id, ref b, group, testOnly)      DLC 不要
    /// 樹木   TreeManager.BurnTree(idx, group, intensity)              ★ ND 必須
    /// 道路   ABSENT。道路は燃えない（BurnSegment に相当する API が無い）
    /// </code>
    ///
    /// - <c>BurnGround</c> の <c>intensity</c> は **0.0–1.0 の正規化値**（§B-7b の
    ///   IL_001E で ×255 される）。バニラは隕石が 1.0、陥没・地震・竜巻が 0.7。
    ///   **⑤は溶岩なので 1.0** を使う。値は単調非減少で、下がることはない
    /// - <c>BurnTree</c> の <c>intensity</c> は <c>conv.u1</c> で**切り捨てられる
    ///   （クランプされない）**ので、呼び出し側で <c>[128, 255]</c> に収める
    ///   （256 は 0 に、300 は 44 になる。§B-7c）
    /// - ND 非所持なら**木は燃やさない。** <c>TreeManager.ReleaseTree</c> で消す代替は
    ///   やらない —— 「燃えない」と「消える」は別の嘘であり、燃えないことを
    ///   説明するほうが正しい（<c>Strings.VolcanoTreesNeedDlc</c>）
    /// - 一度燃えた木は二度と燃えない（<c>m_flags &amp; 64 FireDamage</c>）。
    ///   空振りを異常として数えない
    ///
    /// > ★★ **罠 5。<c>BurnBuilding</c> の後で <c>Building</c> の火勢フィールドを
    /// > 書き足さない。** 火勢は <c>GetFireParameters</c> が建物ごとに決める（§B-7a）。
    /// > 強めたくなっても書かない —— 消費するのは <c>CommonBuildingAI</c> の系だけで、
    /// > それ以外の AI に書くと誰も消さない永久の幽霊火災になり、バニラの建物配列に
    /// > 入るのでセーブに焼き付き、MOD を外しても残る。**本プロジェクトは一度これを
    /// > 出荷している。**
    /// >
    /// > **レビューの grep（実際に走らせて 0 件を確認してある）**:
    /// > <code>
    /// > grep -rn "m_fireIntensity" src/DisasterPlus/Game/Volcano --include=*.cs \
    /// >   | grep -v '///' | grep -v '^[^:]*:[0-9]*: *//'
    /// > # -> 0 件（doc コメントの中の言及だけを除く。素の grep は doc の
    /// > #    「書かない」という説明そのものに当たるので 0 件にはならない）
    /// > </code>
    ///
    /// > **<c>DisasterHelpers.DestroyBuildings</c> / <c>DestroyNetSegments</c> を通らない**
    /// > （§E-14。通ると Natural Disasters Renewal の Prefix 全置換と衝突する）。
    /// > ⑤が呼ぶのは <c>BuildingAI.BurnBuilding</c> だけである。
    ///
    /// ── 建物と樹木の走査を行優先にした理由（②のレビュー指摘 I1 との関係）────────
    ///
    /// ②④と T5 は <c>OutwardCellOrder</c> で中心から外へ走査している。あれは
    /// **上限で打ち切られる大きな矩形**を扱うためで、打ち切られたときに
    /// 「どこまで確実に終わったか」を半径で言えることが要件だった。
    /// こちらの矩形は <c>SpreadRadiusFor</c> が最大 60 m なので、
    /// **建物グリッドで高々 3×3、樹木グリッドで高々 5×5 セル**である。
    /// 上限（25 / 49）で切り捨てられる余地が構造的に無いので、行優先で足りる。
    /// **知らずに②の指摘を破ったのではない。**
    ///
    /// ── 溶岩は地形を変えない ─────────────────────────────────
    ///
    /// <c>RawHeights</c> を書くのは T6（<c>VolcanoUplift</c>）だけである。
    ///
    /// **レビューの grep（実際に走らせて件数を合わせてある。doc の言及を除く）**:
    /// <code>
    /// grep -rn "RawHeights\|TerrainModify" src/DisasterPlus/Game/Volcano/VolcanoLava*.cs \
    ///   | grep -v '///'
    /// # -> 0 件（溶岩は地形を変えない）
    ///
    /// grep -rn "Physics.Raycast" src/DisasterPlus/Game/Volcano --include=*.cs | grep -v '///'
    /// # -> 0 件（地形にコライダーは無く、必ず外れる。§B-6）
    /// </code>
    /// </summary>
    public static partial class VolcanoLava
    {
        /// <summary>まだ止まっていない。</summary>
        public const int StopNone = 0;

        /// <summary>勾配が <c>LavaPath.MinSlope</c> 未満（平ら・窪み）＝ 溜まった。</summary>
        public const int StopFlat = 1;

        /// <summary>水に触れた（設計書 §4.5）。</summary>
        public const int StopWater = 2;

        /// <summary>歩数の上限に達した。</summary>
        public const int StopSteps = 3;

        /// <summary>マップの外へ出た。</summary>
        public const int StopOffMap = 4;

        /// <summary>★ 標高が上がった＝勾配の符号がこの環境では違う（§8.1）。</summary>
        public const int StopUphill = 5;

        /// <summary>地形を読めなかった。</summary>
        public const int StopNoTerrain = 6;

        /// <summary>流れの本数の上限。設定の上限でもある。</summary>
        public const int MaxFlows = 8;

        /// <summary>1 本の流れが 1 回の前進で踏む歩数の上限。</summary>
        private const int MaxStepsPerTickPerFlow = 2;

        /// <summary>前進の間隔（sim フレーム相当。**時間の積算で判定する**）。</summary>
        private const int IntervalFrames = 8;

        /// <summary>1 本あたりの軌跡の点数の上限（超えたら間引いて畳む）。</summary>
        private const int MaxTrailPoints = 128;

        /// <summary>★ 勾配の符号を実行時に観測する歩数（クラス doc）。</summary>
        private const int ObservationSteps = 8;

        /// <summary>観測で「上がった」と判定する高さの差（m）。地形の量子 1/64 m の 3 倍強。</summary>
        private const float UphillToleranceMetres = 0.05f;

        /// <summary>マップの半分の広さ（m）。17280 / 2 = 8640 の少し内側で止める。</summary>
        private const float MapHalfExtentMetres = 8600f;

        /// <summary><c>BurnGround</c> の強さ。**0.0–1.0 の正規化値**（§B-7b）。</summary>
        private const float GroundBurnIntensity = 1f;

        /// <summary>全部止まってから溶岩が冷えるまでのゲーム内時間（分）。</summary>
        private const float CoolMinutes = 15f;

        private static bool _started;
        private static bool _finished;
        private static Vec3 _centre;
        private static float _minutesSinceAdvance;
        private static float _cooledMinutes;

        private static LavaFlow[] _flows = new LavaFlow[MaxFlows];
        private static Vec2[][] _trails;
        private static int[] _trailCounts = new int[MaxFlows];
        private static int[] _trailStride = new int[MaxFlows];
        private static int[] _sinceCommit = new int[MaxFlows];
        private static float[] _lastHeight = new float[MaxFlows];

        private static int _flowCount;
        private static int _aliveCount;
        private static float _longestMetres;

        private static int _buildingsIgnited;
        private static int _buildingsRefused;
        private static int _treesIgnited;
        private static bool _treesAvailable;
        private static bool _outsidePurchasedArea;

        /// <summary>
        /// 地形がまだ隆起で上がっている量（m / 隆起 1 tick）。
        /// **下り勾配の符号の観測に足す許容差**であって、勾配そのものには影響しない。
        /// 隆起が終わっていれば 0 なので、観測はこれまでどおり厳しいままである。
        /// </summary>
        private static float _terrainRiseMetres;

        private static bool _slopeSignVerified;
        private static bool _slopeSignWarned;
        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>軌跡の点（全流路を連結した**不変配列**）。main はこれを読む。</summary>
        private static Vec2[] _trailPoints = new Vec2[0];

        /// <summary>各流路の点数（**不変配列**）。<see cref="_trailPoints"/> と対で使う。</summary>
        private static int[] _trailPointCounts = new int[0];

        /// <summary>火口から出した流れの本数（設定の値。0 なら完全に無効）。</summary>
        public static int FlowCount { get { return _flowCount; } }

        /// <summary>まだ動いている流れの本数。</summary>
        public static int AliveCount { get { return _aliveCount; } }

        /// <summary>いちばん長く流れた距離（m）。</summary>
        public static float LongestMetres { get { return _longestMetres; } }

        /// <summary>これまでに火を付けた建物の数。</summary>
        public static int BuildingsIgnited { get { return _buildingsIgnited; } }

        /// <summary>
        /// バニラが着火を断った**呼び出しの回数**。**0 でないのは異常ではない** ——
        /// <c>CommonBuildingAI.BurnBuilding</c> は水没中の建物と瓦礫を断る（§B-7a）。
        ///
        /// ★★ <b>これは「断られた建物の数」ではない</b>（全体レビュー M17）。
        /// 溶岩は 1 歩 12 m しか進まないのに着火半径は最大 60 m なので、
        /// **同じ建物が 1 本の流れに 5 回前後、8 本で最大 40 回叩かれる。**
        /// 2 回目以降は既に燃えているので断られる ——
        /// したがってこの数は<b>ほとんどが「もう燃えている建物への再着火」</b>であり、
        /// <see cref="BuildingsIgnited"/> より遥かに大きくなるのが正常である。
        /// **重複を数えないようにするには「どの建物に火を付けたか」を覚える必要があり、
        /// そのための配列を⑤は持たない**（<c>m_fireIntensity</c> を読みに行くのは
        /// 罠 5 の grep を壊すのでやらない）。数の意味のほうを正確に名乗る。
        /// </summary>
        public static int BuildingsRefused { get { return _buildingsRefused; } }

        /// <summary>これまでに火を付けた木の数。ND 非所持なら常に 0。</summary>
        public static int TreesIgnited { get { return _treesIgnited; } }

        /// <summary>木に火を付けられる環境か（＝ ND DLC を持っているか。§B-7c）。</summary>
        public static bool TreesAvailable { get { return _treesAvailable; } }

        /// <summary>
        /// 溶岩が購入していないタイルへ出たか。**不具合ではない** ——
        /// <c>GetDetailHeight</c> のサンプリングが 4 m から 16 m 補間に落ちる（§B-6）。
        /// </summary>
        public static bool OutsidePurchasedArea { get { return _outsidePurchasedArea; } }

        /// <summary>
        /// 勾配の符号がこの環境で確かに「下り」になっていることを、
        /// **実行時に観測して確かめられたか**（§8.1）。
        /// 1 本でも観測窓を無事に抜ければ true になる。
        /// </summary>
        public static bool SlopeSignVerified { get { return _slopeSignVerified; } }

        /// <summary>全ての流れが止まったか（＝冷え始めてよいか）。</summary>
        public static bool AllStopped { get { return _started && _aliveCount == 0; } }

        /// <summary>冷え切ったか（＝次の位相へ進んでよいか）。</summary>
        public static bool Finished { get { return _finished; } }

        /// <summary>
        /// 冷え具合 <c>[0,1]</c>。1 が「まだ熱い」、0 が「冷え切った」。
        /// **T9 の描画がこれで色を落とす**（止まった溶岩が永久に光っていないこと）。
        /// </summary>
        public static float CoolUnit
        {
            get
            {
                if (!AllStopped) return 1f;

                // CoolMinutes は 0 より大きい定数である（0 にすると冷える段が
                // 消えて、止まった瞬間に溶岩が消える）。定数なので割り算の前に
                // 0 を検査すると、コンパイラが到達不能コードとして警告する。
                float left = 1f - _cooledMinutes / CoolMinutes;
                if (left < 0f) return 0f;
                if (left > 1f) return 1f;
                return left;
            }
        }

        /// <summary>直近の失敗（**英語・診断用**）。無ければ null。</summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// 軌跡の点（全流路を連結した配列）。**publish 後は 1 バイトも書き換えない** ——
        /// 前進のたびに新しい配列を作って差し替える。だから main スレッドが
        /// 参照を持ったままでも安全で、コピーの口を別に設ける必要が無い。
        ///
        /// > 計画は <c>CopyHeads(Vec2[] into, out int count)</c> という口を挙げていたが、
        /// > **作っていない。** 計画の意図は「sim が書き込み中の配列を main に
        /// > 読ませない」ことで、それは「差し替えるだけで書き換えない」ことで既に
        /// > 満たされている。使われないコピー API を 1 本置くほうが、
        /// > 「どちらを使うのが正しいか」を次の担当者に考えさせる分だけ悪い。
        /// </summary>
        public static Vec2[] TrailPoints { get { return _trailPoints; } }

        /// <summary>各流路の点数（**不変配列**）。合計が <see cref="TrailPoints"/> の長さ。</summary>
        public static int[] TrailPointCounts { get { return _trailPointCounts; } }

        /// <summary>
        /// レベルアンロード・新しい火山・中止で呼ぶ。**全状態を捨てる。**
        /// <c>_errorLogged</c> / <c>_slopeSignWarned</c> は戻さない（ゲームのビルドに
        /// 対する事実であって都市ごとの状態ではない）。
        /// </summary>
        public static void Reset()
        {
            _started = false;
            _finished = false;
            _centre = new Vec3(0f, 0f, 0f);
            _minutesSinceAdvance = 0f;
            _cooledMinutes = 0f;

            for (int i = 0; i < MaxFlows; i++)
            {
                _flows[i] = new LavaFlow(false, new Vec2(0f, 0f), 0f, 0, StopNone);
                _trailCounts[i] = 0;
                _trailStride[i] = 1;
                _sinceCommit[i] = 0;
                _lastHeight[i] = 0f;
            }

            // ★ 軌跡の実体も返す（8 本 × 128 点で 8 KB）。都市をまたいで持ち越すと、
            //   次の都市で前の都市の溶岩が描かれる。
            _trails = null;

            _flowCount = 0;
            _aliveCount = 0;
            _longestMetres = 0f;
            _buildingsIgnited = 0;
            _buildingsRefused = 0;
            _treesIgnited = 0;
            _treesAvailable = false;
            _outsidePurchasedArea = false;
            _slopeSignVerified = false;
            _terrainRiseMetres = 0f;
            _lastFailure = null;

            _trailPoints = new Vec2[0];
            _trailPointCounts = new int[0];
        }

        /// <summary>
        /// **sim スレッド。** <see cref="VolcanoState"/> の位相分岐からのみ呼ぶこと。
        /// 例外が出ても位相を固めない（固めるとプレイヤーは 2 つ目の火山を置けなくなる）。
        /// </summary>
        /// <param name="terrainRiseMetresPerTick">
        /// 地形が隆起で 1 tick に上がる量（m）。**隆起の途中に流れを出したときだけ 0 でない。**
        /// 溶岩が進んだ先で標高が上がるのは、その場合「溶岩が登った」のではなく
        /// 「山が育った」ためなので、その分を観測の許容差に足す
        /// （<c>VolcanoUplift.RiseMetresPerTick</c> をそのまま渡すこと）。
        /// </param>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes,
                                float terrainRiseMetresPerTick)
        {
            try
            {
                Step(footprint, deltaMinutes, terrainRiseMetresPerTick);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the lava tick threw " + e.GetType().Name;
                _aliveCount = 0;
                _finished = true;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano lava failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcLava",
                             "volcano lava failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes,
                                 float terrainRiseMetresPerTick)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre))
            {
                Start(footprint);
            }

            // ★ Start は Reset を通るので、**許容差は Start より後で入れる**
            //   （前に入れると開始した tick だけ 0 に戻る）。
            _terrainRiseMetres = float.IsNaN(terrainRiseMetresPerTick)
                                 || terrainRiseMetresPerTick < 0f
                ? 0f : terrainRiseMetresPerTick;

            if (_finished) return;

            if (_aliveCount == 0)
            {
                // 全部止まった。冷えるまでの時間を数えるだけ。
                if (deltaMinutes > 0f) _cooledMinutes += deltaMinutes;
                if (_cooledMinutes >= CoolMinutes) _finished = true;
                return;
            }

            // ★ 間隔は経過ゲーム内時間の積算で判定する。**frameIndex % N にしない。**
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSinceAdvance += deltaMinutes;
            if (interval > 0f && _minutesSinceAdvance > interval) _minutesSinceAdvance = interval;

            if (framesPerMinute <= 0f) return;
            if (_minutesSinceAdvance < interval) return;

            _minutesSinceAdvance = 0f;
            Advance();
        }

        /// <summary>
        /// 火口の縁から <see cref="MaxFlows"/> 以下の本数の流れを出す。
        /// **本数 0 は「完全に無効」**で、着火も描画も何も起きない。
        /// </summary>
        private static void Start(VolcanoFootprint footprint)
        {
            Reset();
            _started = true;
            _centre = footprint.Centre;
            _treesAvailable = ReadTreesAvailable();

            int flows = ModSettings.VolcanoLavaFlows.value;
            if (flows < 0) flows = 0;
            if (flows > MaxFlows) flows = MaxFlows;

            if (flows == 0)
            {
                // 設定で切っている。**冷える時間も待たずに終わる。**
                _flowCount = 0;
                _aliveCount = 0;
                _finished = true;
                return;
            }

            _trails = new Vec2[MaxFlows][];

            float craterRadius = VolcanoShape.CraterRadiusOf(footprint.RadiusMetres);
            if (!(craterRadius > 0f)) craterRadius = LavaPath.StepMetres;

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.Z)));

            for (int i = 0; i < flows; i++)
            {
                Vec2 dir = LavaPath.InitialDirection(seed, i, flows);
                var head = new Vec2(footprint.Centre.X + dir.X * craterRadius,
                                    footprint.Centre.Z + dir.Z * craterRadius);

                _flows[i] = new LavaFlow(true, head, 0f, 0, StopNone);
                _trails[i] = new Vec2[MaxTrailPoints];
                _trails[i][0] = head;
                _trailCounts[i] = 1;
                _trailStride[i] = 1;
                _sinceCommit[i] = 0;

                float h, sx, sz;
                _lastHeight[i] = SampleSlope(head, out h, out sx, out sz) ? h : 0f;
            }

            _flowCount = flows;
            _aliveCount = flows;
            RebuildTrailSnapshot();
        }

        /// <summary>1 回ぶんの前進。**歩数は本数 × <see cref="MaxStepsPerTickPerFlow"/> で頭打ち。**</summary>
        private static void Advance()
        {
            int alive = 0;

            for (int i = 0; i < _flowCount; i++)
            {
                LavaFlow f = _flows[i];
                if (!f.Alive) continue;

                for (int s = 0; s < MaxStepsPerTickPerFlow && f.Alive; s++)
                {
                    f = StepFlow(i, f);
                }

                _flows[i] = f;
                if (f.Alive) alive++;
                if (f.TravelledMetres > _longestMetres) _longestMetres = f.TravelledMetres;
            }

            _aliveCount = alive;
            RebuildTrailSnapshot();
        }

        /// <summary>
        /// 1 歩。止まる条件は 6 つあり、どれも <c>StopReason</c> に記録して診断へ出す。
        ///
        /// ★ <b>下り方向は <c>(-slopeX, -slopeZ)</c> である</b>（クラス doc の IL 実測）。
        ///   <c>LavaPath</c> は符号を知らないので、ここで直してから渡す。
        /// </summary>
        private static LavaFlow StepFlow(int index, LavaFlow f)
        {
            float height, slopeX, slopeZ;
            if (!SampleSlope(f.Head, out height, out slopeX, out slopeZ))
            {
                return Stop(f, StopNoTerrain);
            }

            // ★★ 実行時の観測（§8.1）。**符号を反転させて「直そう」としない。**
            //    最初の ObservationSteps 歩のあいだに標高が上がったら、その流れを
            //    止めて名乗る。IL では符号を確定させてあるので、ここが発火するのは
            //    ゲームの更新で挙動が変わったときである。
            if (f.Steps > 0 && f.Steps <= ObservationSteps
                && height > _lastHeight[index] + UphillToleranceMetres + _terrainRiseMetres)
            {
                NoteUphill();
                return Stop(f, StopUphill);
            }

            if (f.Steps >= ObservationSteps) _slopeSignVerified = true;
            _lastHeight[index] = height;

            Vec2 next;
            if (!LavaPath.NextPosition(f.Head, new Vec2(-slopeX, -slopeZ),
                                       LavaPath.StepMetres, out next))
            {
                // 平ら・窪み・NaN。**素通りさせずに溜まる**（LavaPath のクラス doc）。
                return Stop(f, StopFlat);
            }

            if (OffMap(next)) return Stop(f, StopOffMap);

            // ★ 水は sim スレッド専用（HasWater は WaterSimulation.BeginRead/EndRead を
            //   取る。§A-4 / ②の TsunamiChain.IsUnderWater と同じ扱い）。
            if (HasWater(next)) return Stop(f, StopWater);

            int steps = f.Steps + 1;
            float travelled = f.TravelledMetres + LavaPath.StepMetres;

            AppendTrail(index, next);
            NoteArea(next);
            Ignite(next, travelled);

            var moved = new LavaFlow(true, next, travelled, steps, StopNone);
            if (steps >= LavaPath.MaxSteps) return Stop(moved, StopSteps);
            return moved;
        }

        private static LavaFlow Stop(LavaFlow f, int reason)
        {
            return new LavaFlow(false, f.Head, f.TravelledMetres, f.Steps, reason);
        }

        /// <summary>
        /// 勾配の符号がこの環境では違う。**<c>Log.Warn</c> は 1 回だけ**
        /// （ここは毎 tick の経路の内側である）。
        /// </summary>
        private static void NoteUphill()
        {
            _slopeSignVerified = false;
            _lastFailure = "a lava flow gained altitude within the first "
                           + ObservationSteps + " steps; the downhill sign does not hold in "
                           + "this build of the game, so the flow was stopped";

            if (_slopeSignWarned) return;
            _slopeSignWarned = true;
            Log.Warn("volcano lava: " + _lastFailure
                     + " (Disaster + does not flip the sign at runtime - that would look fixed "
                     + "here and break the other way somewhere else)");
        }

        /// <summary>
        /// 軌跡へ 1 点。**最後のスロットは常に「生きた先端」**で、
        /// <see cref="_trailStride"/> 歩ごとにそれを確定させて次のスロットを開ける。
        ///
        /// 上限（<see cref="MaxTrailPoints"/>）に達したら 1 つおきに間引いて畳み、
        /// 間隔を倍にする。**配列は伸ばさない** —— <c>MaxSteps = 512</c> なので
        /// 畳むのは高々 2 回で、形は保たれる。
        /// </summary>
        private static void AppendTrail(int index, Vec2 head)
        {
            if (_trails == null || _trails[index] == null) return;

            Vec2[] trail = _trails[index];
            int count = _trailCounts[index];

            if (count <= 0)
            {
                trail[0] = head;
                _trailCounts[index] = 1;
                _sinceCommit[index] = 0;
                return;
            }

            trail[count - 1] = head;

            if (++_sinceCommit[index] < _trailStride[index]) return;
            _sinceCommit[index] = 0;

            if (count >= MaxTrailPoints)
            {
                int folded = (count + 1) / 2;
                for (int k = 0; k < folded; k++) trail[k] = trail[k * 2];
                count = folded;
                _trailCounts[index] = folded;
                _trailStride[index] *= 2;
            }

            trail[count] = head;
            _trailCounts[index] = count + 1;
        }

        /// <summary>
        /// main が読む不変配列を作り直す。**前進した回にだけ走る**（クラス doc の費用表）。
        /// 既に publish した配列は 1 バイトも書き換えず、丸ごと差し替える。
        /// </summary>
        private static void RebuildTrailSnapshot()
        {
            if (_flowCount <= 0 || _trails == null)
            {
                _trailPoints = new Vec2[0];
                _trailPointCounts = new int[0];
                return;
            }

            int total = 0;
            for (int i = 0; i < _flowCount; i++) total += _trailCounts[i];

            var points = new Vec2[total];
            var counts = new int[_flowCount];

            int cursor = 0;
            for (int i = 0; i < _flowCount; i++)
            {
                int c = _trailCounts[i];
                counts[i] = c;
                if (c > 0 && _trails[i] != null)
                {
                    Array.Copy(_trails[i], 0, points, cursor, c);
                    cursor += c;
                }
            }

            _trailPoints = points;
            _trailPointCounts = counts;
        }

        private static bool OffMap(Vec2 p)
        {
            return p.X < -MapHalfExtentMetres || p.X > MapHalfExtentMetres
                   || p.Z < -MapHalfExtentMetres || p.Z > MapHalfExtentMetres;
        }

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

        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcLava",
                     "lava flows=" + _flowCount + " alive=" + _aliveCount
                     + " longest=" + _longestMetres.ToString("F0") + " m"
                     + " ignited b=" + _buildingsIgnited + " t=" + _treesIgnited
                     + " refused=" + _buildingsRefused
                     + " slopeSign=" + (_slopeSignVerified ? "verified" : "not verified yet")
                     + " frame=" + frame);
        }
    }
}
