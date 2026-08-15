namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="Assumptions"/> のうち⑤火山 の前提。
    ///
    /// **このファイルには検証しか置かない。** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> は本体側の private のままで、
    /// partial なので可視性を 1 つも上げずに使える（分割の要件そのもの）。
    ///
    /// 件数は <see cref="VolcanoCheckCount"/> がこのファイルの中で宣言する。
    /// **検証を足したらここも増やすこと** —— 本体の <c>TotalCheckCount</c> は
    /// これらの和である。
    ///
    /// ── ★ この 3 件の述語について（④のレビューと②の監査が見つけた欠陥）─────
    ///
    /// ④で「未実測のプレハブ値を捕まえるはずの検証が、まさにその場合に PASS した」
    /// ことがあった。原因は述語が「フィールドが解決したか」を見ていて、
    /// **機能が実際に門にしている「値が使えるか」を見ていなかった**ことである。
    /// ②にも同じ形が残っていた。
    ///
    /// そこで⑤の 3 件は、**機能そのものが門にしている式を、そのまま述語にする**:
    ///
    /// <code>
    /// 検証 1 ← VolcanoTerrainFacts.Usable
    ///          （= HeightsResolved &amp;&amp; RawArrayLength == 1081^2 &amp;&amp; UpdateAreaResolved）
    /// 検証 2 ← CraterResolved &amp;&amp; BurnGroundResolved   （T6 の火口と T8 の焦げの門）
    /// 検証 3 ← SlopeSampleResolved                    （T8 の溶岩の門）
    /// </code>
    ///
    /// 特に検証 1 は<b>配列の長さまで見る</b>。<c>RawHeights</c> が解決しても長さが
    /// 1081² でなければ <c>z*1081 + x</c> の添字が別のセルを指し、**マップの
    /// 無関係な場所が隆起する**。「解決した」だけを見る述語は、そこで PASS を出す。
    ///
    /// 走査は <see cref="VolcanoReader.ScanTerrainFacts"/> に委ねる。あちらは
    /// **副作用なし**で、sim スレッドが回しているキャッシュを巻き戻さない
    /// （<see cref="Run"/> は main スレッドから呼ばれる）。
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>このファイルが持つ検証の数。</summary>
        private const int VolcanoCheckCount = 4;

        private static void RunVolcano()
        {
            // --- ⑤火山（Task 2: 地形 API）ここから ---

            // ★ 走査は 1 回だけ。3 つの述語はその結果を見る。
            //   **Check() の外側で呼ぶので、自分で例外を抑える**（Run() は
            //   DisasterPlusLoading.OnLevelLoaded から素で呼ばれているため、
            //   ここから投げるとレベルロードが壊れる。④の
            //   FirstResolvableCloudShader が同じ理由で同じ形をしている）。
            //   ScanTerrainFacts は項目ごとに try/catch しているが、二重に守る。
            VolcanoTerrainFacts facts;
            try
            {
                facts = VolcanoReader.ScanTerrainFacts();
            }
            catch
            {
                // 既定値は全て false ＝「読めていない」。3 件とも FAIL になる。
                facts = new VolcanoTerrainFacts();
            }

            // 1. 地形の書き込み経路。**⑤全体がこの 1 件に乗っている。**
            //
            //   述語は VolcanoTerrainFacts.Usable そのもの ——
            //   VolcanoUplift（T6）が「山を上げてよいか」を決めるのに使う式と
            //   1 文字も違わない。名前に実測した長さを出すのは、
            //   **1081² でなかったときにその数がここにしか出ないから**である。
            Check("TerrainManager.RawHeights is a ushort[1081^2] and "
                  + "TerrainModify.UpdateArea(int,int,int,int,bool,bool,bool) is resolvable "
                  + "(RawHeights: "
                  + (facts.HeightsResolved
                        ? facts.RawArrayLength + " cells"
                        : "unavailable")
                  + ")",
                  "no volcano can be built at all: raising the ground and publishing the "
                  + "change are the two calls the whole feature rests on",
                  delegate { return facts.Usable; });

            // 2. 火口と焦げ。**山と溶岩そのものはこれが無くても動く**ので、
            //   impact にそこまで書く（狼少年にしない）。
            //   述語は T6 の火口と T8 の焦げが実際に門にする 2 つの and である。
            //   どちらも DLC ゲートが無い（§C-8 / §B-7b）ので、
            //   expectedWithoutDlc は付けない —— 付けると DLC 非所持環境で
            //   本当の欠落まで「正常な FAIL」に紛れる。
            Check("DisasterHelpers.MakeCrater(Vector2,float,float,bool) and "
                  + "BurnGround(Vector2,float,float) are resolvable",
                  "the summit crater is not carved and the ground is not scorched along the "
                  + "lava; the mountain and the lava still work",
                  delegate { return facts.CraterResolved && facts.BurnGroundResolved; });

            // 3. 勾配サンプリング。**溶岩の門はこれ 1 つだけ**（T8）。
            //   ★ 1 引数版ではなく 3 引数版（out float slopeX, out float slopeZ）を見る。
            //     1 引数版は高さしか返さないので、それが解決しても溶岩は
            //     下り方向を見つけられない（§B-6）。同名 4 本のオーバーロードが
            //     あるので、引数の型まで指定しないと別物を掴む。
            Check("TerrainManager.SampleDetailHeight(Vector3, out float, out float) is resolvable",
                  "the lava cannot find its way downhill, so no lava flows at all. The "
                  + "mountain, the clearing and the eruption are unaffected",
                  delegate { return facts.SlopeSampleResolved; });

            // --- ⑤火山（Task 2: 地形 API）ここまで ---

            // --- ⑤火山（Task 5: 準備の破壊経路）ここから ---

            // 4. 破壊経路。**述語は VolcanoClearing が実際に門にしている式そのもの**
            //   （VolcanoDestructionFacts.Usable）である。「フィールドが解決した」を
            //   述語にすると、経路が使えない環境で PASS が出る（④のレビューと
            //   ②の監査が同じ欠陥を見つけている）。
            //
            //   ★ この検査が FAIL なら⑤は火山を 1 つも作らない。**degraded ではない** ——
            //     準備せずに地面を上げるのは劣化した動作ではなく、設計書 §1.2 が
            //     発見した失敗そのものだからである。
            //
            //   ScanFacts はキャッシュを触らない純粋な走査なので main スレッドから
            //   呼んでよい（あちらのクラス doc）。Check の外で例外を抑えるのは
            //   上の 3 件と同じ理由（Run はレベルロードから素で呼ばれている）。
            VolcanoDestructionFacts destruction;
            try
            {
                destruction = VolcanoClearing.ScanFacts();
            }
            catch
            {
                destruction = new VolcanoDestructionFacts();
            }

            Check("BuildingAI.CollapseBuilding is resolvable and a road destruction path "
                  + "(NetAI.CollapseSegment / NetManager.ReleaseSegment) is reachable",
                  "the volcano refuses to start. Raising the ground without clearing it first "
                  + "is not a degraded mode: the game pins the terrain back to the height of "
                  + "every road and building on every update, so the mountain would come out "
                  + "full of flat trenches and bowls",
                  delegate { return destruction.Usable; });

            // --- ⑤火山（Task 5: 準備の破壊経路）ここまで ---
        }
    }
}
