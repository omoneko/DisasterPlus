using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>震源から同心円状に立つ津波。</b>**sim スレッド専用。**
    ///
    /// ── 何をしているのか ──────────────────────────────────────────
    ///
    /// 震源に <c>WaterSource</c> の <c>TYPE_NATURAL</c> を 1 個置き、その
    /// <c>m_target</c>（＝円を満たす目標水位）を DLC の津波と<b>同じ波形</b>で
    /// 上下させる。円は目標より低ければ<b>水を湧かせ</b>、高ければ<b>抜く</b>ので、
    /// <b>引き波 → 押し波 → 引き波</b>がそのまま海に出る。
    /// 波形の中身は <see cref="TsunamiRingShape"/>（Core 層）。
    ///
    /// ── なぜ <c>TYPE_IMPACT</c> をやめたのか ─────────────────────────
    ///
    /// 前身の <c>TsunamiWave</c> は <c>TYPE_IMPACT</c>、海中に置く<b>仮想の丘</b>だった。
    /// 丘は水を押しのけるだけで<b>作らない</b>ので、出せるのは正味ゼロの双極子である。
    /// オフラインで同じ棚に並べた結果（2026-08-31、水深 174 m、汀線 6.1 km）:
    ///
    /// <list type="bullet">
    /// <item>DLC の津波（外周の境界条件、強度 100）… 汀線 <b>84.8 m</b></item>
    /// <item><c>TYPE_IMPACT</c> の丘（drive 3857）… 汀線 <b>19.3 m</b></item>
    /// </list>
    ///
    /// 差は振幅ではなく<b>水を作るかどうか</b>だった。丘をいくら大きくしても
    /// 湧き出しの真似はできない（海底を掘り抜くだけである）。
    ///
    /// ★ ただし DLC の 84.8 m と直接は競えない。あちらは<b>マップの辺全体</b>を
    ///   使う線の波源で、こちらは点の波源だから、幾何的な広がりのぶんだけ落ちる。
    ///   競うべきは数字ではなく<b>大津波に見えるか</b>である。
    ///
    /// ── 出せる津波（実測、2026-08-31。円が汀線に掛からない配置のみ）──────
    ///
    /// **深い海**（水深 174 m ＝ 海面の高いマップ。押し波の蓋 40 m）:
    ///
    /// <list type="bullet">
    /// <item>震源から 5.2 km … 汀線 <b>35.5 m</b>、内陸へ <b>1,328 m</b></item>
    /// <item>震源から 8.7 km … 汀線 25.9 m、内陸へ 912 m</item>
    /// <item>震源から 13.0 km … 汀線 23.0 m、内陸へ 848 m</item>
    /// </list>
    ///
    /// **標準的なマップ**（水深 40 m ＝ 海面 40 m の既定。蓋は水深に縛られ 20 m）:
    ///
    /// <list type="bullet">
    /// <item>震源から 5.2 km … 汀線 <b>20.6 m</b>、内陸へ 704 m</item>
    /// <item>震源から 8.7 km … 汀線 13.1 m、内陸へ 432 m</item>
    /// </list>
    ///
    /// ★★ **どちらも「実機で int32 が溢れない」ことを確かめた設定である。**
    ///   再現ツールに<b>実機の int32 なら溢れていた回数</b>を数えさせている
    ///   （<c>tools/WaterSolverSim/SourceDisc.cs</c> の <c>Int32Overflows</c>）。
    ///   これを付けるまでは、<b>再現側だけが綺麗な答えを返す</b>設定を
    ///   良い設定だと思い込んでいた。
    ///
    /// ★★ **だが DLC の津波をそのまま呼ぶのは解析ではない。**（所有者、2026-08-31）
    ///   あれは<b>マップ外周でしか評価されない</b>ので、震源から同心円にはならない。
    ///   <c>WaterSource</c> は同じ「目標水位まで満たす」装置を<b>どこにでも置ける</b>形で
    ///   持っているので、<b>波形は DLC のまま、置き場所だけ震源へ移す</b>。
    ///
    /// ── 危険（<c>TyphoonFlood</c> のクラス doc と同じ、ただし一段重い）─────
    ///
    /// <list type="number">
    /// <item><c>LockWaterSource</c> は <b>Monitor を取ったまま返る</b>。
    ///   <c>UnlockWaterSource</c> が<b>唯一の解放経路</b>なので必ず
    ///   <c>finally</c> に置く。落とすと<b>水スレッドが永久に止まる</b>。</item>
    /// <item><c>LockWaterSource</c> は添字を検査しない。
    ///   <see cref="OwnsSource"/> を通ってからでなければ呼ばない。</item>
    /// <item>★★ <b><c>WaterSource</c> はセーブに焼き付く</b>
    ///   （<c>WaterSimulation+Data.Serialize</c> が書く、IL 実測）。
    ///   置いたまま保存されると、MOD を外してもその都市に<b>永久に水が湧き続ける</b>。
    ///   だから解放は
    ///   <list type="bullet">
    ///   <item>波形が終わったとき</item>
    ///   <item>都市を出るとき（<c>Reset</c>）</item>
    ///   <item><b>保存の直前</b>（<see cref="SuspendForSave"/>）</item>
    ///   </list>
    ///   の三箇所すべてで行う。<c>TyphoonFlood</c> は「<c>CreateWaterSource</c> に
    ///   手を伸ばすな」と書いてあるが、それはこの三箇所を守れないなら、の意味である。</item>
    /// <item><c>CreateWaterSource</c> は <c>m_type == 0</c> の枠を<b>使い回す</b>
    ///   （first-fit、IL_0020-005A）。だから<b>握った番号だけでは足りない</b> ——
    ///   書く前に必ず種別と位置を照合する。</item>
    /// </list>
    /// </summary>
    public static class TsunamiRing
    {
        /// <summary><c>WaterSource.TYPE_NATURAL</c>。</summary>
        private const ushort TypeNatural = 1;

        /// <summary>1 水ステップ ＝ 64 sim フレーム（IL 実測、<c>SetCurrentWaterFrame</c>）。</summary>
        private const int FramesPerWaterStep = 64;

        /// <summary>マップ半幅（m）。</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// 震源の円の半径（m）。**遠くへ届かせるのは高さではなく体積**なので、
        /// ここが効く（オフライン実測 2026-08-31: 半径 1280 m → 汀線 35 m、
        /// 半径 3840 m → 汀線 83 m、いずれも強度 100・水深 174 m・汀線 6.1 km）。
        /// </summary>
        public const float RadiusMetres = 3840f;

        /// <summary>
        /// <b>押し波の頭打ち（m、絶対値）。</b>強度 255 のときの値。
        ///
        /// ★★ **2026-09-02 に 40 m から 130 m へ上げた。**
        ///   所有者の実機報告「最大規模でも高潮程度」。原因は<b>この蓋</b>だった。
        ///
        ///   下の「上げると弱くなる」という表は<b>引き波の円が 3,840 m だった頃</b>の
        ///   ものである。その設定は実機の int32 を壊すので使えず
        ///   （<see cref="DrainRadiusMetres"/>）、いまの安全な設定では
        ///   <b>関係が逆転していた</b>。測り直した結果（水深 165 m・半径 2,048 m ＝
        ///   所有者のマップと同じ条件、陸の勾配 2 m/セル）:
        ///
        /// <list type="bullet">
        /// <item>蓋 40 m … 汀線 32.8 m、内陸へ 288 m、海底の露出 0</item>
        /// <item>蓋 80 m … 汀線 57.8 m、内陸へ 528 m、露出 0</item>
        /// <item><b>蓋 130 m … 汀線 82.2 m、内陸へ 768 m、露出 5 歩</b></item>
        /// <item>蓋 190 m … 汀線 89.9 m、内陸へ 944 m、露出 <b>63 歩</b></item>
        /// <item>蓋 260 m … 汀線 89.7 m、内陸へ 944 m、露出 65 歩</item>
        /// </list>
        ///
        /// ★★ **130 m が分岐点である。** そこまでは蓋にほぼ比例して伸びるが、
        ///   それ以上は頭打ちになる一方で<b>海底の露出だけが 12 倍に増える</b>
        ///   （9% の伸びと引き換えに）。だからここで止める。
        ///
        /// ★ 蓋を上げると引き波の円は自動的に縮む（溢れ防止、<c>_drainRate</c> の
        ///   切り下げ）。130 m なら 101 m。それでも溢れは 0 件で、上の値が出る。
        ///
        /// ── 以下は旧設定（引き波の円 3,840 m）での表。**参考にしないこと。**
        ///
        /// （オフライン実測 2026-08-31、1081 格子・汀線 13 km・768 水ステップ）
        ///
        /// <list type="bullet">
        /// <item>蓋 40 m … 汀線 <b>66.98 m</b>、浸水 <b>2592 m</b>、震源 189.7 m</item>
        /// <item>蓋 55 m … 汀線 65.38 m、浸水 2384 m、震源 233.2 m</item>
        /// <item>蓋 70 m … 汀線 64.56 m、浸水 2240 m、震源 255.4 m</item>
        /// <item>蓋 20 m … 汀線 25.44 m、浸水 1280 m、震源 100.0 m</item>
        /// </list>
        ///
        /// 高い塔を立てても遠くへは行かない —— 効くのは<b>体積</b>だからである。
        ///
        /// ★★ <b>この表は引き波の円が 3,840 m だったときのものである。</b>
        ///   その設定は実機の int32 を壊すので使えない（<see cref="DrainRadiusMetres"/>）。
        ///   採れる設定での実際の値はクラス doc の表を見ること。
        ///   ここに残すのは<b>「蓋を上げると弱くなる」という向き</b>のためだけである。
        ///
        /// ★★ ただし<b>水深でも縛る</b>（<see cref="MaxRiseFraction"/>）。
        ///   蓋そのものは深さによらず効くのだが、<b>浅い海では出ていく波が
        ///   震源の水を持ち去って海底を剥き出しにする</b>。実測（震源距離 5.2 km）:
        ///
        /// <list type="bullet">
        /// <item>水深 25 m・蓋 40 m … 海底の露出 <b>297 水ステップ（約 5 実分）</b></item>
        /// <item>水深 25 m・蓋 12 m … 露出 <b>0</b>、汀線 12.8 m</item>
        /// <item>水深 40 m・蓋 20 m … 露出 <b>0</b>、汀線 20.6 m</item>
        /// <item>水深 174 m・蓋 40 m … 露出 <b>0</b>、汀線 35.5 m</item>
        /// </list>
        ///
        ///   ★ 深い海を要求して逃げることはできない ——
        ///     バニラの既定の海面は 40 m で、<b>海底は標高 0 より下へ行けない</b>ので、
        ///     標準的なマップの海は最大でも 40 m しかない。
        /// </summary>
        private const float MaxRiseMetres = 130f;

        /// <summary>
        /// 押し波の頭打ち（水深に対する割合）。<see cref="MaxRiseMetres"/> と
        /// <b>小さいほうを採る</b>。
        ///
        /// ★★ **0.5 から 0.8 へ上げた**（2026-09-02）。0.5 のままだと、
        ///   水深 165 m のマップでも蓋が 82 m で頭打ちになり、
        ///   <see cref="MaxRiseMetres"/> を 130 m にした意味が消える。
        ///   浅い海を守るのがこの割合の役目なので、深い海まで縛る必要は無い。
        ///
        /// ★ 浅い側の裏取り: 水深 40 m・蓋 32 m（＝0.8）で海底の露出 4 歩、
        ///   水深 25 m・蓋 20 m で 0 歩（実測 2026-08-31）。
        ///
        /// ★ 結果として<b>深い海ほど大きな津波</b>になる。物理的にも正しく、
        ///   海溝型地震を沖に置く動機にもなる。
        /// </summary>
        private const float MaxRiseFraction = 0.8f;

        /// <summary>
        /// <b>引き波の円の半径（m）。押し波の円とは別である。</b>
        ///
        /// ★★ **これがゲームの整数演算で決まる上限である。**（2026-08-31、IL 検証）
        ///
        ///   取り込みパス（<c>SimulateWater</c> IL_1B4C-1B58）は
        ///   <c>share * take</c> を<b>int32 の <c>mul</c></b> で計算する
        ///   （<c>conv.i8</c> はどこにも無い）。<c>take = min(inputRate, total&gt;&gt;1)</c>
        ///   で、<c>inputRate</c> は半径から <c>((r-10)/0.4)^2</c> と決まるので、
        ///   <b>円を大きくすると必ず積が 2^31 を越える</b>。越えた先は
        ///   <c>m_height = (ushort)(h - share)</c> なので<b>セルの高さが壊れる</b>。
        ///
        ///   押し波（吐き出し）側にはこの積が無いので、そちらは 3840 m で構わない。
        ///   バニラでこれが踏まれないのは、マップの川の水源が小さいからである。
        ///
        /// ★ 実測（オフライン再現に「実機の int32 なら溢れたか」を数えさせた）:
        ///   半径 250 m は一部の配置で溢れる（最悪の積 2.35e9）。
        ///   <b>160 m は水深 174/60 m × 震源距離 2.6-13.0 km の 8 通りすべてで 0 件。</b>
        ///
        /// ★ 引きを大きくできないぶん威力は落ちるが、落ち幅は小さい ——
        ///   汀線で 2.6 km 54 m / 5.2 km 35 m / 13 km 23 m は変わらず、
        ///   むしろ震源が穏やかになる（水柱 190 m → 63 m）。
        /// </summary>
        private const float DrainRadiusMetres = 160f;

        /// <summary>
        /// 取り込み円の <c>total</c> の上限（1/64 m の総和）。
        /// 半径 160 m ＝ 10 セル ＝ 314 セル、1 セルは最大 65535。余裕を見て切り上げ。
        /// </summary>
        private const long MaxTotalUnits = 22000000L;

        /// <summary>
        /// 引き波で抜いてよい水深の割合。**海底を露出させない。**
        /// 押しと違いこちらは<b>水深で縛る</b> —— 深さ 60 m で蓋 60 m にすると
        /// 水柱が 100% 抜けて海底が 94 水ステップ露出した（実測 2026-08-31）。
        /// 0.5 にしても深い海の威力は落ちない（汀線 66.98 m のまま）。
        /// </summary>
        private const float MaxDrawFraction = 0.5f;

        /// <summary>
        /// <summary>
        /// 断層に並べる円の最大数。**5 が頭打ち**（<see cref="_sources"/> の表）。
        /// </summary>
        private const int MaxSegments = 5;

        /// 波形の長さ（水ステップ）。バニラは 256。
        ///
        /// ★★ **長さは効く。** 同じ蓋 40 m で 512 歩なら汀線 52.77 m、
        ///   768 歩なら 66.98 m（実測 2026-08-31）。ただし 1024 歩は 29.80 m と
        ///   かえって落ちる —— 振幅の減衰項 <c>(65536 - t)/65536</c> が
        ///   終盤で 0 に近づき、後半の押しが消えるからである。
        ///   768 水ステップ ＝ 49,152 sim フレーム ≒ 13.6 実分。
        /// </summary>
        private const int DurationSteps = 768;

        /// <summary>同じ長さをティックで。波形の<b>周期でもある</b>（LevelOffsetUnits の doc）。</summary>
        private const int DurationTicks = DurationSteps * TsunamiRingShape.TicksPerWaterStep;

        /// <summary>
        /// <c>_source</c> と <c>_running</c> を守る錠。
        ///
        /// ★★ **必要である。**（2026-08-31、相互検証）<c>Reset</c> は
        ///   <c>LoadingExtensionBase.OnLevelUnloading</c> から<b>メインスレッド</b>で
        ///   呼ばれ、<c>SuspendForSave</c> は <c>OnSaveData</c> から呼ばれる。
        ///   どちらも sim スレッドの <see cref="Tick"/> と<b>同時に走る</b>
        ///   （<c>LoadingManager.UnloadLevel</c> は sim を止める前に
        ///    <c>OnLevelUnloading</c> を同期で呼ぶ）。
        ///
        /// ★★ <b>ゲームは決してこの錠を取らない</b>ので、
        ///   <c>m_waterSources</c> の Monitor との間に輪はできない。
        ///   ただし順序は<b>必ず _gate → m_waterSources</b> にすること。
        /// </summary>
        private static readonly object _gate = new object();

        // ★★ **ここに「二度と立てない」錠を置いてはいけない。**（2026-08-31、第 3 回検証）
        //    第 2 回の指摘に応えて <c>_shutDown</c> を入れたが、**それが最悪の欠陥だった。**
        //    <c>OnReleased</c> は「MOD が外された」ときだけ来るのではない ——
        //    IL（<c>ThreadingWrapper.GetImplementations</c>）を読むと、
        //    <c>eventPluginsChanged</c> / <c>eventPluginsStateChanged</c> と
        //    <b>メインメニューへ戻るたび</b>に来て、そのあと<b>すぐ作り直される</b>。
        //    つまり「無関係な MOD を切り替えた」「街を出た」だけで錠が下り、
        //    <b>プロセスを再起動するまで津波が二度と起きなくなる</b>。
        //    防ごうとした漏れより、ずっと起きやすくずっと悪い。
        //
        //    いま漏れを止めているのは <c>OnReleased</c> の
        //    <c>TsunamiChain.Reset()</c>（予約を消す）＋ <c>TsunamiRing.Reset()</c>
        //    （水源を解放して掃除する）である。拡張が本当に外されるなら
        //    予約が消えているので <c>Begin</c> は呼ばれず、
        //    作り直されるなら次の tick が普通に面倒を見る。

        // ★ 下の 4 つは sim スレッドが錠の外で読み書きし、main スレッドが
        //   パネルのために読む。**正しさの拠り所は Drive / Release の中の
        //   _gate 越しの再確認であって、volatile ではない** ——
        //   volatile は「古い値を見たまま回り続ける」のを防ぐだけである。
        /// <summary>
        /// 置いた水源の番号。**0 の要素は「持っていない」。**
        ///
        /// ★★ **1 個ではなく線である。**（2026-09-02、所有者の報告
        ///   「最大規模でも高潮程度」への答え）点の波源は円周に比例して薄まるので、
        ///   遠くへ行くほど必ず落ちる —— DLC の津波が強いのは
        ///   <b>マップの辺全体を使う線の波源</b>だからだった。
        ///
        ///   海溝型地震の断層は現実にも数百 km にわたって線状に割れるので、
        ///   点で表すほうが不自然である。実測（水深 165 m・半径 2,048 m・
        ///   蓋 130 m、汀線 5.2 km）:
        ///
        /// <list type="bullet">
        /// <item>1 個（点）… 汀線 81.8 m、内陸へ 768 m、海底の露出 5 歩、震源の引き 100%</item>
        /// <item>3 個 …… 汀線 <b>126.8 m</b>、内陸へ 1,280 m、露出 <b>0</b>、引き 29%</item>
        /// <item><b>5 個 …… 汀線 152.2 m、内陸へ 1,504 m、露出 0、引き 25%</b></item>
        /// <item>9 個 …… 汀線 148.2 m（<b>頭打ち</b>）、内陸へ 1,424 m</item>
        /// </list>
        ///
        /// ★ 強くなるだけでなく<b>行儀も良くなる</b> —— 水を 1 点から吸い上げず
        ///   広い前線から集めるので、海底が露出しなくなる。
        ///
        /// ★★ int32 の制約（<see cref="DrainRadiusMetres"/>）は<b>円ごとに独立</b>
        ///   なので、並べるぶんには壊れない。
        /// </summary>
        private static ushort[] _sources = new ushort[MaxSegments];

        /// <summary>並べた円の数（0 なら何も置いていない）。</summary>
        private static volatile int _segmentCount;

        /// <summary>保存のために外したときの区画数（戻すときに要る）。</summary>
        private static int _suspendedCount;

        /// <summary>
        /// 震源。<b><see cref="Reset"/> で消してはいけない。</b>
        ///
        /// ★★ これは所有権の指紋である。消すと <see cref="OwnsSource"/> が
        ///   <c>Vector3.zero</c> に居る他人の水源を「自分のもの」と誤認し、
        ///   <b>他人の川を消す</b>。次の都市に持ち越しても、位置が一致しない限り
        ///   何も起きないので無害である。
        /// </summary>
        private static Vector3 _centre;

        /// <summary>
        /// 各円の中心。<b><see cref="Reset"/> で消してはいけない</b>（<see cref="_centre"/> と同じ理由）。
        /// </summary>
        private static Vector3[] _centres = new Vector3[MaxSegments];
        private static long _rate;
        private static int _deltaUnits;
        private static volatile int _ticks;
        private static int _seaUnits;
        private static int _depthUnits;
        private static int _riseCapUnits;
        private static int _drawCapUnits;
        private static long _drainRate;
        private static volatile uint _lastFrame;
        private static volatile bool _running;

        /// <summary>いま津波を出しているか。</summary>
        public static bool Running { get { return _running; } }

        /// <summary>直近の理由（パネルと診断に出す）。</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// <b>断った理由の種類。</b>プレイヤーに出す 1 行を選ぶために要る。
        ///
        /// ★★ <see cref="Detail"/> は英語の 1 文なので、そのまま画面には出せない。
        ///   長らくパネルは<b>どの理由でも「内陸マップです」</b>と言っていた ——
        ///   沖の深海で断られた人を正反対の方向へ送る、最悪の 1 行だった
        ///   （2026-08-31、相互検証）。
        /// </summary>
        public enum Refusal
        {
            None = 0,
            NotSea,       // 震源が海ではない（内陸マップでは正常）
            NotEnoughRoom,// 海が狭すぎて円が入らない
            Busy,         // 前の津波がまだ走っている
            NoRoomInGame, // ゲームが水源の枠をくれなかった
            TooWeak,      // 地震が弱すぎて、立てても見えない
        }

        /// <summary>
        /// <see cref="Begin"/> を呼ぶ前に断ったときに、その理由を記録する。
        ///
        /// ★★ これが無いと、<c>TsunamiChain</c> が震度で断ったときに
        ///   <see cref="LastRefusal"/> が <c>None</c> のままになり、パネルが
        ///   <b>「内陸マップです」</b>と言う（2026-08-31、第 5 回検証）。
        /// </summary>
        public static void NoteRefusal(Refusal reason, string detail)
        {
            LastRefusal = reason;
            Detail = detail;
        }

        /// <summary>直近の断りの種類。</summary>
        public static Refusal LastRefusal { get; private set; }

        /// <summary>いまの目標水位の平常からのずれ（m）。診断用。</summary>
        public static float OffsetMetres { get; private set; }

        /// <summary>震源の水深（m）。</summary>
        public static float DepthMetres { get { return _depthUnits / 64f; } }

        /// <summary>これまでに見た最高の押し波（m、目標水位ベース）。</summary>
        public static float PeakRiseMetres { get; private set; }

        /// <summary>波形の何ステップ目か。</summary>
        public static int ElapsedSteps
        {
            get { return _ticks / TsunamiRingShape.TicksPerWaterStep; }
        }

        /// <summary>波形の長さ（水ステップ）。</summary>
        public static int TotalSteps { get { return DurationSteps; } }

        /// <summary>
        /// 都市を出るとき／機能を切るときに呼ぶ。**冪等。例外を投げない。**
        /// ★★ ここを通らないと水源がセーブに残る（クラス doc §3）。
        /// </summary>
        public static void Reset()
        {
            lock (_gate)
            {
                ReleaseLocked();

                // ★★ 握っていた番号が何かの理由で外れていても、**自分の指紋の
                //    水源は必ず消す**。ここを抜けると MOD を外しても消えない
                //    湧き水がその都市に残る（クラス doc §3）。
                SweepOursLocked();

                _running = false;
                _ticks = 0;
                _lastFrame = 0u;
                // ★ 断りの理由を都市をまたいで持ち越さない（第 5 回検証）。
                LastRefusal = Refusal.None;
                Detail = null;
                _deltaUnits = 0;
                _rate = 0L;
                OffsetMetres = 0f;
                PeakRiseMetres = 0f;
                // ★ _centre は消さない（そのフィールドの doc）。
            }
        }

        /// <summary>
        /// 津波を立てる。**sim スレッドから呼ぶこと。**
        /// </summary>
        /// <returns>立てられたか。false のとき <see cref="Detail"/> に理由が入る。</returns>
        public static bool Begin(Vec3 epicentre, byte intensity, uint frame)
        {
            Detail = null;
            LastRefusal = Refusal.None;

            lock (_gate)
            {
            if (_running)
            {
                Detail = "a tsunami is already running";
                LastRefusal = Refusal.Busy;
                return false;
            }

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                Detail = "the water simulation is not there";
                LastRefusal = Refusal.NoRoomInGame;
                return false;
            }

            float depth = TsunamiWave.DepthAt(terrain, epicentre.X, epicentre.Z);
            if (depth <= 0f)
            {
                Detail = "the epicentre is not in the sea";
                LastRefusal = Refusal.NotSea;
                return false;
            }

            _seaUnits = (int)(terrain.WaterSimulation.m_currentSeaLevel * 64f);
            _depthUnits = (int)(depth * 64f);
            _centre = new Vector3(epicentre.X, 0f, epicentre.Z);
            // ★★ **円は海の広さに合わせる。**（2026-08-31、第 2 回検証）
            //    吐き出しは<b>目標より低い陸のセルにも水を置く</b>
            //    （IL_1E51: 飛ばすのは <c>terrain &gt;= target</c> のセルだけ）。
            //    だから円が岸に掛かっていると、波が来るのではなく
            //    <b>低い土地が円の形にいきなり満たされる</b>。
            //    しかも取り込みの円は 160 m しかないので、<b>戻せない</b>。
            float toLandX, toLandZ;
            float radius = OpenWaterRadius(terrain, epicentre.X, epicentre.Z,
                                           out toLandX, out toLandZ);
            if (radius <= 0f)
            {
                Detail = "there is not enough open water around the epicentre - a source "
                         + "circle needs at least "
                         + MinUsefulRadiusMetres.ToString("F0")
                         + " m of sea at least " + MinSourceDepthMetres.ToString("F0")
                         + " m deep in every direction, or it would simply fill the low "
                         + "ground around it instead of making a wave";
                LastRefusal = Refusal.NotEnoughRoom;
                return false;
            }

            _rate = TsunamiRingShape.RateForRadiusMetres(radius);
            _drainRate = TsunamiRingShape.RateForRadiusMetres(DrainRadiusMetres);
            _deltaUnits = TsunamiRingShape.VanillaDeltaUnits(intensity);

            // ★ 蓋は震度で決まる。255 で 40 m。**これより上げても弱くなる**ので、
            //   強い地震ほど高い塔、にはしない（MaxRiseMetres の doc）。
            // ★★ **255 で割ってはいけなかった。**（2026-08-31、第 4 回検証）
            //    タイルが送る既定値はバニラのスライダーの既定 55 である。
            //    255 で割ると蓋は 8.6 m にしかならず、クラス doc の表
            //    （蓋 40 m / 20 m で測った値）とはまるで別の波になる ——
            //    <b>既定のまま遊ぶ人が「弱い」と言うのは当たり前だった。</b>
            //
            //    バニラのスライダーの上限は 100 なので、**100 で飽和**させる。
            //    既定 55 → 71 m、上限 100 → 130 m。
            //
            // ★ 100 を超えても蓋は上げない。130 m から先は伸びが頭打ちになり、
            //   海底の露出だけが増えるからである（MaxRiseMetres の ★★ の表）。
            //   解禁した強度は地震の揺れと被害のほうに効く。
            int forCap = intensity > 100 ? 100 : intensity;
            _riseCapUnits = (int)(MaxRiseMetres * 64f * forCap / 100f);

            // ★ 弱い地震でも波形が消えないように下限を置く。**水深の蓋より先に**置く
            //   —— あとに置くと、水深 2 m 未満のとき下限が蓋を打ち消して
            //   浅い海に大きな押し波が戻ってくる（2026-08-31、Codex の指摘）。
            if (_riseCapUnits < 64) _riseCapUnits = 64;

            // ★★ 浅い海では水深で縛る（MaxRiseFraction の doc）。**ここが最後**。
            int byDepth = (int)(_depthUnits * MaxRiseFraction);
            if (byDepth < 0) byDepth = 0;
            if (_riseCapUnits > byDepth) _riseCapUnits = byDepth;

            // ★ 引きは水深に縛る。押しの蓋より深くは引かない。
            _drawCapUnits = (int)(_depthUnits * MaxDrawFraction);
            if (_drawCapUnits > _riseCapUnits) _drawCapUnits = _riseCapUnits;
            if (_drawCapUnits < 0) _drawCapUnits = 0;

            // ★★ **溢れない流量に切り下げる。**（DrainRadiusMetres の doc）
            //    1 セルの超過が最悪どこまで行くかを見積もり、
            //    `share * take` が int32 に収まる流量までしか出さない。
            //    見積もりは実測（蓋 40 m のとき最悪 102 m）に 3 倍の余裕を見た値。
            // ★★ ゲームが int32 で持つのは <c>share*take + total - 1</c> である。
            //
            // ★ **4 で割るのはやりすぎだった。**（2026-08-31、第 3 回検証）
            //   <c>total</c> は掛け算の相手ではなく<b>足し算の相手</b>なので、
            //   要る余裕は掛け算ではなく引き算である。<c>total</c> の上限は
            //   「取り込み円のセル数 × 65535」——半径 160 m なら 314 セルで
            //   およそ 2.1e7、int の 1% に過ぎない。4 で割ると円が 85 m まで縮み、
            //   <b>実測で保証した 160 m から外れてしまう</b>。
            long worstShare = 3L * (_riseCapUnits + _drawCapUnits);
            if (worstShare > 0L)
            {
                long safe = (int.MaxValue - MaxTotalUnits) / worstShare;
                if (_drainRate > safe) _drainRate = safe;
                if (_drainRate < 1L) _drainRate = 1L;
            }

            _ticks = 0;
            _lastFrame = frame;
            OffsetMetres = 0f;
            PeakRiseMetres = 0f;

            // ★★ **断層を組み立てる。**（<see cref="_sources"/> の ★★）
            //    向きは「いちばん近い陸への方向に直交」——
            //    波面が海岸と平行になり、海岸へ向かって来る。海溝と同じ形である。
            //    区画数は震度で決まる（弱い地震は点、強い地震は 5 区画）。
            int wanted = 1 + (int)(MaxSegments - 1) * (intensity > 100 ? 100 : intensity) / 100;
            if (wanted < 1) wanted = 1;
            if (wanted > MaxSegments) wanted = MaxSegments;

            float alongX = -toLandZ;   // 陸への向きに直交（90 度回す）
            float alongZ = toLandX;
            float alongLen = Mathf.Sqrt(alongX * alongX + alongZ * alongZ);
            if (alongLen < 0.001f) { alongX = 0f; alongZ = 1f; }
            else { alongX /= alongLen; alongZ /= alongLen; }

            _segmentCount = 0;

            for (int k = 0; k < wanted; k++)
            {
                float offset = (k - (wanted - 1) * 0.5f) * radius;
                float px = epicentre.X + alongX * offset;
                float pz = epicentre.Z + alongZ * offset;

                // ★★ **区画は全部確かめる。**（2026-09-02、Codex の指摘）
                //    `k != 0` を免除していたが、区画が 2 つ以上あるとき
                //    <b>k == 0 は震源ではなく断層の端</b>である。震源で通した検査を
                //    端に流用すると、陸やマップ縁に円を置いてしまう ——
                //    まさにこの検査が防ぐはずの場所である。
                //    入らない区画は落とすだけで、津波そのものは中止しない
                //    （断層は端で細くなるものである）。
                if (OpenWaterRadius(terrain, px, pz) < radius) continue;

                Vector3 at = new Vector3(px, 0f, pz);

                WaterSource src = new WaterSource();
                src.m_type = TypeNatural;
                src.m_inputPosition = at;
                src.m_outputPosition = at;
                src.m_target = (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);
                src.m_inputRate = 0u;
                src.m_outputRate = 0u;
                src.m_water = 0u;
                src.m_pollution = 0u;
                src.m_flow = 0u;

                ushort handle;
                if (!terrain.WaterSimulation.CreateWaterSource(out handle, src) || handle == 0)
                {
                    // ★ 1 個も置けなければ失敗。途中まで置けたなら、それで進む。
                    break;
                }

                _centres[_segmentCount] = at;
                _sources[_segmentCount] = handle;
                _segmentCount++;
            }

            // ★★ 端が全部落ちても、**震源そのものは上で検査済み**なので
            //    そこに 1 本だけ置いて成立させる（点の波源に退化する）。
            if (_segmentCount == 0)
            {
                WaterSource only = new WaterSource();
                only.m_type = TypeNatural;
                only.m_inputPosition = _centre;
                only.m_outputPosition = _centre;
                only.m_target = (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);

                ushort onlyHandle;
                if (terrain.WaterSimulation.CreateWaterSource(out onlyHandle, only)
                    && onlyHandle != 0)
                {
                    _centres[0] = _centre;
                    _sources[0] = onlyHandle;
                    _segmentCount = 1;
                }
            }

            if (_segmentCount == 0)
            {
                Detail = "the game would not give us a water source slot";
                LastRefusal = Refusal.NoRoomInGame;
                return false;
            }

            _running = true;

            // ★ バニラの津波と同じ物差しで自分の波も測る（SeaWatch のクラス doc）。
            SeaWatch.Arm("Disaster+ concentric tsunami from the epicentre", frame);

            Log.Info("tsunami rising at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): the open water around it reaches "
                     + radius.ToString("F0") + " m of the " + RadiusMetres.ToString("F0")
                     + " m the source would like, so it uses a water source of radius "
                     + TsunamiRingShape.RadiusMetresForRate(_rate).ToString("F0")
                     + " m, " + _segmentCount + " of them in a line along the rupture "
                     + "(a megathrust tears open along a fault, it is not a point), and "
                     + "their target sea level is driven with "
                     + "the DLC's own waveform (retreat, crest, retreat) for "
                     + DurationSteps + " water steps = "
                     + (DurationSteps * FramesPerWaterStep / 3600f).ToString("F1")
                     + " real minutes. The raw amplitude for intensity " + intensity
                     + " would be " + (_deltaUnits / 64f).ToString("F0")
                     + " m but it is held to " + (_riseCapUnits / 64f).ToString("F0")
                     + " m up and " + (_drawCapUnits / 64f).ToString("F0")
                     + " m down (the water is " + depth.ToString("F1")
                     + " m deep here). The push covers "
                     + TsunamiRingShape.RadiusMetresForRate(_rate).ToString("F0")
                     + " m and the retreat only "
                     + TsunamiRingShape.RadiusMetresForRate(_drainRate).ToString("F0")
                     + " m - the game's own take-water maths is int32 and would corrupt "
                     + "cell heights if the retreat circle were any bigger. **A taller source does not travel further - what "
                     + "travels is volume - and unlike the old impact wave this MAKES "
                     + "water instead of borrowing it from the hole it digs.**");

            return true;
            }
        }

        /// <summary>**sim スレッド。** 毎 tick 呼んでよい（自分で間引く）。</summary>
        public static void Tick(uint frame)
        {
            if (!_running) return;
            if (frame - _lastFrame < FramesPerWaterStep) return;
            _lastFrame = frame;

            // ★ 保存の最中は水源を外してある。戻るまで時計も止める ——
            //   進めてしまうと、戻ってきたときに波形が飛ぶ。
            if (_segmentCount == 0) return;

            _ticks += TsunamiRingShape.TicksPerWaterStep;

            // ★★ **DurationTicks は既にティックである。**（2026-08-31、第 2 回検証）
            //    ここで 64 を掛け直していたせいで、打ち切りが 3,145,728 ティック
            //    ＝ 49,152 水ステップ ＝ <b>14.5 実時間</b>になっていた。
            //    波形自体は 768 歩で 0 に戻るので<b>見た目には終わって見え</b>、
            //    そのあいだ水源だけが生き続けて半径 3.8 km の海を
            //    海面ちょうどに固定し続ける（次の波を平らに均してしまう）。
            //    <b>例外的な解放経路ばかり固めて、正常な経路を壊していた。</b>
            if (_ticks >= DurationTicks)
            {
                lock (_gate)
                {
                    ReleaseLocked();
                    SweepOursLocked();
                    _running = false;
                }

                Log.Info("tsunami source finished after " + DurationSteps
                         + " water steps and is released. The highest the target got was "
                         + PeakRiseMetres.ToString("F1")
                         + " m above the normal sea. From here the wave is carried by the "
                         + "solver alone.");
                return;
            }

            int offset = TsunamiRingShape.LevelOffsetUnits(
                _ticks, _deltaUnits, DurationTicks);

            // ★ 押しは絶対値で、引きは水深で縛る（それぞれの定数の doc）。
            if (offset > _riseCapUnits) offset = _riseCapUnits;
            if (offset < -_drawCapUnits) offset = -_drawCapUnits;

            OffsetMetres = offset / 64f;
            if (OffsetMetres > PeakRiseMetres) PeakRiseMetres = OffsetMetres;

            int target = Clamp(_seaUnits + offset, 0, TsunamiRingShape.MaxLevelUnits);
            Drive(target);
        }

        /// <summary>
        /// 円をいまの目標水位へ押し引きする。**ロックを取るのはここだけ。**
        ///
        /// ★★ 押しと引きを<b>両方いつも入れる</b>。片方ずつにすると、寄せ集まった水を
        ///   抜く力が無く、震源が目標の 2〜3 倍に盛り上がる（オフライン実測 2026-08-31:
        ///   目標 +102 m に対して実測 +330 m）。両方入れると円は目標水位に張り付き、
        ///   DLC が外周セルにやっている Dirichlet 境界とまったく同じ振る舞いになる。
        /// </summary>
        private static void Drive(int target)
        {
            lock (_gate)
            {
                if (!_running || _segmentCount == 0) return;

                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain == null || terrain.WaterSimulation == null) return;

                WaterSimulation sim = terrain.WaterSimulation;

                // ★★ **番号は一度だけ読んで、以後はその控えを使う。**
                //    ロックの前後で読み直すと、あいだに 0 になった場合に
                //    <c>LockWaterSource(0)</c> が <b>Monitor を取ったあとで</b>
                //    IndexOutOfRange を投げ、<c>Monitor.Exit</c> に到達しない ——
                //    水スレッドが永久に止まる（2026-08-31 の相互検証で指摘）。
                // ★★ 線のどれか 1 本でも自分のものでなくなったら、全部畳む。
                //    部分的に生きた水源を残すと、解放経路が届かなくなる。
                for (int k = 0; k < _segmentCount; k++)
                {
                    if (OwnsSource(sim, _sources[k], _centres[k])) continue;

                    // ★ 枠が自分のものでなくなった。**番号を捨てるだけにしない** ——
                    //   捨てると以後どの解放経路も届かず、湧き水が残る。
                    ReleaseLocked();
                    SweepOursLocked();
                    _running = false;
                    Detail = "the water source slot was taken by something else";
                    return;
                }

                for (int k = 0; k < _segmentCount; k++)
                {
                ushort handle = _sources[k];
                bool foreign = false;

                // ★ LockWaterSource は Monitor を取ったまま返る。
                //   UnlockWaterSource が唯一の解放経路なので必ず finally に置く。
                WaterSource src = sim.LockWaterSource(handle);

                try
                {
                    // ★★ 錠の中でもう一度確かめる。OwnsSource は錠の外の読みなので、
                    //    そこから先で枠が入れ替わっている余地がある。
                    // ★★ **型だけでは足りない。**（2026-08-31、第 2 回検証）
                    //    錠の外の <c>OwnsSource</c> は型と位置の両方を見ているのに、
                    //    錠の中の確認が型だけだと<b>弱いほうが最後に立つ</b>。
                    //    枠が別の TYPE_NATURAL に入れ替わっていたら、
                    //    他人の川に自分の目標水位と 3.8 km ぶんの流量を書いてしまう。
                    if (src.m_type != TypeNatural
                        || src.m_inputPosition != _centres[k]
                        || src.m_outputPosition != _centres[k])
                    {
                        foreign = true;
                    }
                    else
                    {
                        src.m_target = (ushort)target;

                        // ★★ 引きと押しで流量が違う。**同じにしてはいけない**
                        //    （DrainRadiusMetres の doc: 実機の int32 が壊れる）。
                        src.m_inputRate = (uint)_drainRate;
                        src.m_outputRate = (uint)_rate;
                    }
                }
                finally
                {
                    sim.UnlockWaterSource(handle, src);
                }

                if (foreign)
                {
                    ReleaseLocked();
                    SweepOursLocked();
                    _running = false;
                    Detail = "the water source slot was taken by something else";
                    return;
                }
                }
            }
        }

        /// <summary>16 m セルの数。<c>BlockHeights</c> の添字は <c>z*(1080+1)+x</c>。</summary>
        private const int GridCells = 1080;

        /// <summary>円の中で「海」と認める最小の水深（m）。</summary>
        private const float MinSourceDepthMetres = 2f;

        /// <summary>
        /// この半径すら取れないなら津波は立てない（m）。
        /// これより狭い水域で 3.8 km の円を名乗っても意味が無い。
        /// </summary>
        private const float MinUsefulRadiusMetres = 320f;

        /// <summary>
        /// 震源から<b>陸に当たらずに広げられる半径</b>（m）。0 なら立てられない。
        ///
        /// ★★ 吐き出しの円は<b>陸にも水を置く</b>（<see cref="Begin"/> の ★★）。
        ///   だから <see cref="RadiusMetres"/> をそのまま使わず、
        ///   <b>実際の海の広さまで縮める</b>。狭い湾では弱い津波になるが、
        ///   それは<b>正しい</b> —— 湾の奥で外洋規模の波は立たない。
        ///
        /// ★★ **光線を放つ実装は捨てた。**（2026-08-31、第 3 回検証）
        ///   16 方位・160 m 刻みでは、3,840 m のところで隣の光線と 1,508 m 離れる。
        ///   そのあいだに在る島も岬も防波堤も<b>見えない</b>し、
        ///   160 m より細い砂州は<b>またいで通り過ぎる</b>。
        ///   どちらも「安全」と答えて陸を水浸しにする。
        ///   さらに、最初の 1 点が陸だったときに 0 を下限で 160 m へ押し戻していた ——
        ///   <b>陸が 160 m 以内にあると証明した場合にちょうど 160 m を返していた。</b>
        ///
        ///   いまは<b>格子をそのまま舐める</b>。<c>BlockHeights</c> は生の配列で
        ///   錠を取らないので、241×241 を 2 セルおきに見ても 1 万数千回の配列読みで済む
        ///   （置くときに 1 度だけ）。取りこぼすのは 32 m 未満の構造物だけである。
        ///
        /// ★ <c>DepthAt</c> は使わない。あれは水面が地形より 0.125 m 高ければ
        ///   「海」と答えるので、干潟や側溝を素通りする。ここでは
        ///   <see cref="MinSourceDepthMetres"/> を要求する。
        /// </summary>
        public static float OpenWaterRadius(TerrainManager terrain, float x, float z)
        {
            float ignoreX, ignoreZ;
            return OpenWaterRadius(terrain, x, z, out ignoreX, out ignoreZ);
        }

        /// <summary>
        /// 同上。<paramref name="toLandX"/>/<paramref name="toLandZ"/> に
        /// <b>いちばん近い陸への向き</b>（正規化しない）を返す。
        /// 断層はこれに直交させる（<see cref="_sources"/> の ★★）。
        /// </summary>
        public static float OpenWaterRadius(TerrainManager terrain, float x, float z,
                                            out float toLandX, out float toLandZ)
        {
            toLandX = 1f;
            toLandZ = 0f;

            ushort[] block = terrain.BlockHeights;
            if (block == null) return 0f;
            if (terrain.WaterSimulation == null) return 0f;

            int seaUnits = (int)(terrain.WaterSimulation.m_currentSeaLevel * 64f);
            int minDepthUnits = (int)(MinSourceDepthMetres * 64f);

            // ★★ **水柱そのものを見る**（<c>TrenchQuakeSlot.NearestSea</c> の ★★）。
            //    地面の高さだけでは、海面より低いまま乾いている土地
            //    （干拓地・クレーター）を「海」と読んでしまう。
            //    <c>BeginRead</c> は円盤 1 枚につき 1 回だけ取る。
            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
            if (cells == null) return 0f;

            int cx = CellOf(x);
            int cz = CellOf(z);
            int reach = (int)(RadiusMetres / 16f);
            int best = reach * reach;   // セル単位の二乗距離で持つ（平方根を避ける）

            for (int dz = -reach; dz <= reach; dz += 2)
            {
                int gz = cz + dz;
                int row = gz * (GridCells + 1);

                for (int dx = -reach; dx <= reach; dx += 2)
                {
                    int square = dx * dx + dz * dz;
                    if (square >= best) continue;      // 既に見つけた陸より遠い

                    int gx = cx + dx;

                    bool land;
                    if (gx < 0 || gx > GridCells || gz < 0 || gz > GridCells)
                    {
                        land = true;                  // マップの外は陸として扱う
                    }
                    else
                    {
                        land = !IsOpenSeaCell(block, cells, row + gx,
                                              seaUnits, minDepthUnits);
                    }

                    if (land)
                    {
                        best = square;
                        toLandX = dx;
                        toLandZ = dz;
                    }
                }
            }

            float metres = Mathf.Sqrt(best) * 16f;

            // ★ 見つけた陸のセルそのものには掛からないよう、1 セルぶん内側で止める。
            metres -= 16f;

            // ★ best <= reach^2 なので metres は必ず RadiusMetres 未満。頭打ちは要らない。
            return metres < MinUsefulRadiusMetres ? 0f : metres;
            }
            finally
            {
                terrain.WaterSimulation.EndRead();
            }
        }

        /// <summary>
        /// <b>そのセルは「外洋」か。</b>この 1 つの式に統一する。
        ///
        /// <code>水柱 &gt;= 最小水深   かつ   海底 &lt;= 海面 - 最小水深</code>
        ///
        /// ★★ **片方だけでは必ずどこかで間違える。**（2026-08-31、第 5・6 回検証）
        ///
        /// <list type="bullet">
        /// <item><b>海底だけ</b>見ると、堤防で囲まれた干拓地やクレーターが
        ///   「海」になる —— 海面より低いまま<b>乾いている</b>のに。
        ///   そこへ半径 3.8 km の水源を置くと、引きの円は 160 m しかないので
        ///   <b>戻せない水が永久に残る</b>（セーブを壊す）。</item>
        /// <item><b>水柱だけ</b>見ると、川・高い湖・<b>前の津波で冠水した街</b>が
        ///   「海」になる。2 本目の津波が円をその上に広げ、
        ///   目標より低い陸をいきなり満たす —— 同じ失敗に裏口から入る。</item>
        /// </list>
        ///
        /// ★ 両方を要求すると、<b>高潮の最中でも海は海のまま</b>である
        ///   （海底は動かない）。海面からの高さで川を落とす旧実装は、
        ///   バニラの津波が来ているあいだ<b>外洋を丸ごと「海ではない」と答えて</b>いた。
        /// </summary>
        internal static bool IsOpenSeaCell(ushort[] block, WaterSimulation.Cell[] cells,
                                           int at, int seaUnits, int minDepthUnits)
        {
            if (at < 0 || at >= block.Length || at >= cells.Length) return false;

            // ★★ 式そのものは Core にある（SeaCell）。
            //    3 度書き直して 3 度とも別の地形を取り違えたので、
            //    テストで縛める場所へ出した。
            return DisasterPlus.Core.Earthquake.SeaCell.IsOpenSea(
                block[at], cells[at].m_height, seaUnits, minDepthUnits);
        }

        /// <summary>ワールド座標を 16 m セルへ（<c>TsunamiWave.CellOf</c> と同じ式）。</summary>
        private static int CellOf(float world)
        {
            int c = (int)((world + MapHalfExtent) / 16f + 0.5f);
            return c < 0 ? 0 : (c > GridCells ? GridCells : c);
        }

        /// <summary>
        /// その枠が<b>いまも自分のもの</b>か。<c>CreateWaterSource</c> は
        /// <c>m_type == 0</c> の枠を使い回すので、番号だけでは足りない（クラス doc §4）。
        /// </summary>
        private static bool OwnsSource(WaterSimulation sim, ushort handle, Vector3 centre)
        {
            if (handle == 0) return false;

            FastList<WaterSource> list = sim.m_waterSources;
            if (list == null || list.m_buffer == null) return false;

            int at = handle - 1;
            if (at < 0 || at >= list.m_size) return false;

            WaterSource s = list.m_buffer[at];
            if (s.m_type != TypeNatural) return false;

            // 位置は自分で書いた値そのものなので、ビット一致で照合できる。
            return s.m_outputPosition == centre && s.m_inputPosition == centre;
        }

        /// <summary>
        /// 置いた水源を解放する。**冪等。例外を投げない。**
        /// ここが最後の砦である —— 通らないとセーブに水源が残る。
        /// </summary>
        private static void ReleaseLocked()
        {
            // ★★ **先に番号を手放す。** こうしておけば、この下で何が起きても
            //    「まだ持っている」と誤解した別の経路が同じ枠を触らない。
            // ★★ **先に番号を手放す。** こうしておけば、この下で何が起きても
            //    「まだ持っている」と誤解した別の経路が同じ枠を触らない。
            ushort[] handles = new ushort[MaxSegments];
            Vector3[] centres = new Vector3[MaxSegments];
            int count = _segmentCount;

            for (int k = 0; k < count; k++)
            {
                handles[k] = _sources[k];
                centres[k] = _centres[k];
                _sources[k] = 0;
            }

            _segmentCount = 0;
            if (count == 0) return;

            try
            {
                if (!Singleton<TerrainManager>.exists) return;

                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain == null || terrain.WaterSimulation == null) return;

                WaterSimulation sim = terrain.WaterSimulation;

                for (int k = 0; k < count; k++)
                {
                    ushort handle = handles[k];
                    if (handle == 0) continue;
                    if (!OwnsSource(sim, handle, centres[k])) continue;

                    // ★ 解放の前に流量を 0 にする。ReleaseWaterSource は m_type を
                    //   0 にするだけなので、枠を拾い直した誰かが古い流量を見る余地を消す。
                    WaterSource src = sim.LockWaterSource(handle);
                    try
                    {
                        if (src.m_type != TypeNatural) continue;
                        src.m_inputRate = 0u;
                        src.m_outputRate = 0u;
                    }
                    finally
                    {
                        sim.UnlockWaterSource(handle, src);
                    }

                    sim.ReleaseWaterSource(handle);
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water source failed ("
                         + e.GetType().Name + "); it may persist in this save");
            }
        }

        /// <summary>
        /// <b>指紋の合う水源をすべて消す。</b>呼び出し側は <c>_gate</c> を握っていること。
        ///
        /// ★★ **番号を失っても取り戻せる唯一の手段である。**（2026-08-31、相互検証）
        ///   <c>CreateWaterSource</c> が成功したのに握り損ねた、枠を横取りされた、
        ///   例外で経路が飛んだ —— どの筋でも、置いた水源は
        ///   <b>両方の位置が震源にビット一致する TYPE_NATURAL</b> という
        ///   他に例のない形をしている。走査は数十件の配列なのでただ同然。
        ///
        /// ★ 震源が <c>Vector3.zero</c> のときは何もしない。まだ何も置いていないか、
        ///   マップ中央にたまたま在る他人の川を巻き込む恐れがあるからである。
        /// </summary>
        /// <summary>その位置は自分が置いた円のどれかか（線の全区画を見る）。</summary>
        private static bool IsOneOfOurs(Vector3 at)
        {
            if (at == _centre) return true;

            for (int k = 0; k < _centres.Length; k++)
            {
                if (_centres[k] == Vector3.zero) continue;
                if (_centres[k] == at) return true;
            }

            return false;
        }

        private static void SweepOursLocked()
        {
            if (_centre == Vector3.zero) return;

            try
            {
                if (!Singleton<TerrainManager>.exists) return;

                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain == null || terrain.WaterSimulation == null) return;

                WaterSimulation sim = terrain.WaterSimulation;
                FastList<WaterSource> list = sim.m_waterSources;
                if (list == null || list.m_buffer == null) return;

                // ★★ **走査と解放をひとつの錠の中で行う。**（2026-08-31、第 2 回検証）
                //    配列を錠の外で読んでから <c>ReleaseWaterSource</c> を呼ぶと、
                //    そのあいだに枠が動いて番号が古くなる。古い番号を渡すと
                //    ゲームは<b>Monitor を取ったあとで</b>例外を投げ、
                //    <c>Monitor.Exit</c> に届かない —— 水スレッドが永久に止まる。
                //
                // ★ <c>Monitor</c> は同じスレッドに対して再入可能なので、
                //   ここで取ったまま <c>ReleaseWaterSource</c> を呼んでよい。
                //   ゲーム自身と同じ相手（<c>m_waterSources</c>）を掴む。
                // ★ 譲らずに回すと、main スレッドから来たときに
                //   水スレッドを飢えさせる（第 3 回検証）。
                while (!System.Threading.Monitor.TryEnter(list, 0))
                {
                    System.Threading.Thread.Sleep(0);
                }

                try
                {
                    int size = list.m_size;
                    if (size > list.m_buffer.Length) size = list.m_buffer.Length;

                    for (int i = size - 1; i >= 0; i--)
                    {
                        WaterSource s = list.m_buffer[i];
                        if (s.m_type != TypeNatural) continue;
                        if (s.m_outputPosition != s.m_inputPosition) continue;
                        if (!IsOneOfOurs(s.m_outputPosition)) continue;

                        sim.ReleaseWaterSource((ushort)(i + 1));
                        Log.Info("tsunami: swept a stray water source at the epicentre "
                                 + "(slot " + (i + 1) + "). It would have kept making "
                                 + "water in this city forever.");
                    }
                }
                finally
                {
                    System.Threading.Monitor.Exit(list);
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: sweeping stray water sources failed ("
                         + e.GetType().Name + ")");
            }
        }

        /// <summary>
        /// **保存の直前に呼ぶ。** 水源を外して、戻すべきかどうかを返す。
        ///
        /// ★★ <c>WaterSource</c> はセーブに焼き付く。MOD の <c>OnSaveData</c> は
        ///   バニラの配列書き込みより先に走るので、ここで外せば書かれない。
        ///   <b>すぐ戻し直してはいけない</b> —— <c>AddAction</c> で遅らせること
        ///   （<c>DisasterPlusSerialization.RestoreFloodedRiversForSave</c> と同じ理由）。
        /// </summary>
        public static bool SuspendForSave()
        {
            lock (_gate)
            {
                if (!_running) return false;

                // ★★ **既に外してあるときも true を返す。**（2026-08-31、第 2 回検証）
                //    false を返すと呼び出し側は「戻すものは無い」と読み、
                //    2 回目の保存が始まった窓で<b>戻し忘れる</b>。
                //    <c>ReapplyAfterSave</c> は <c>_source != 0</c> なら何もしないので、
                //    余分に予約されても害は無い。
                if (_segmentCount == 0) return true;

                // ★ 戻すときに何区画あったかを覚えておく（ReleaseLocked が 0 にする）。
                _suspendedCount = _segmentCount;

                ReleaseLocked();
                SweepOursLocked();
                return true;
            }
        }

        /// <summary>保存が終わってから <c>AddAction</c> 越しに呼ぶ。**sim スレッド。**</summary>
        public static void ReapplyAfterSave()
        {
            lock (_gate)
            {
                if (!_running || _segmentCount != 0) return;

                try
                {
                    // ★★ <c>.instance</c> は sInstance が null のとき
                    //    FindObjectOfType と new GameObject を走らせる。都市を出た
                    //    あとにこの遅延処理が届くことがあるので、**必ず exists で先に確かめる**
                    //    （DisasterPlusSerialization のコメントと同じ理由）。
                    if (!Singleton<TerrainManager>.exists) { _running = false; return; }

                    TerrainManager terrain = Singleton<TerrainManager>.instance;
                    if (terrain == null || terrain.WaterSimulation == null)
                    {
                        _running = false;
                        return;
                    }

                    // ★★ **線の全区画を戻す。**（2026-09-02）1 本だけ戻すと、
                    //    残りの区画の中心が <c>_centres</c> に残ったまま
                    //    水源が無い状態になり、掃除の指紋だけが宙に浮く。
                    int back = 0;

                    for (int k = 0; k < _suspendedCount; k++)
                    {
                        WaterSource src = new WaterSource();
                        src.m_type = TypeNatural;
                        src.m_inputPosition = _centres[k];
                        src.m_outputPosition = _centres[k];
                        src.m_target =
                            (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);

                        ushort handle;
                        if (!terrain.WaterSimulation.CreateWaterSource(out handle, src)
                            || handle == 0)
                        {
                            break;
                        }

                        _sources[back] = handle;
                        back++;
                    }

                    _segmentCount = back;

                    if (back == 0)
                    {
                        _running = false;
                        Log.Warn("tsunami: could not put the water source back after saving; "
                                 + "the wave stops here");
                    }
                }
                catch (System.Exception e)
                {
                    _running = false;

                    // ★★ 生成が通ってから落ちた場合、番号を受け取れていないので
                    //    <b>掃除でしか回収できない</b>（SweepOursLocked の doc）。
                    SweepOursLocked();
                    Log.Error("tsunami: putting the water source back after saving failed", e);
                }
            }
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
