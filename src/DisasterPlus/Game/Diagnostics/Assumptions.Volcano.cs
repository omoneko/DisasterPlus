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
    /// 検証 2 ← BurnGroundResolved                     （T8 の焦げの門）
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
        private const int VolcanoCheckCount = 9;

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

            // 2. 溶岩の焦げ。**山も火口も溶岩の前進もこれが無くても動く**ので、
            //   impact にそこまで書く（狼少年にしない）。
            //   述語は T8 の焦げが実際に門にする 1 つである。DLC ゲートは無い（§B-7b）ので
            //   expectedWithoutDlc は付けない —— 付けると DLC 非所持環境で
            //   本当の欠落まで「正常な FAIL」に紛れる。
            //
            //   ★ かつてここは MakeCrater も見ていた。火口は高さプロファイルの一部に
            //     なったので（Core/Volcano/VolcanoCrater）、あの呼び出しはもう存在しない。
            Check("DisasterHelpers.BurnGround(Vector2,float,float) is resolvable",
                  "the ground is not scorched along the lava; the mountain, the crater and "
                  + "the lava still work",
                  delegate { return facts.BurnGroundResolved; });

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

            // --- ⑤火山（Task 7: 噴火の借り物エフェクト）ここから ---

            // 5. 借りるバニラの粒子エフェクト 3 つ（噴煙・炎・噴石）。
            //   **この検査が FAIL でも山は育ち、溶岩は流れて建物を燃やす** ——
            //   欠けた 1 つが描かれなくなるだけである。impact 文にそう書くのは
            //   狼少年にしないためである（④が DestroyTrees で同じ判断をしている）。
            //
            //   ★★ 述語は VolcanoEruptionFx が実際に門にしている式そのものである。
            //     VolcanoVanillaFx は**複製に失敗しても元のプレハブをそのまま描く**ので、
            //     「引けたか」が「描けるか」と一致する。ここが一致していないと
            //     「検査は通ったのに機能が動かない」が起きる（本プロジェクトで 2 度出た形）。
            //
            //   ★ この 3 つはどれも DLC 不要である。Natural Disasters の
            //     爆発・隕石のほうが見た目は良いが、非所持環境には存在しないので
            //     既定経路には決してしない。
            //
            //   ScanFacts は main スレッド専用（Run() も main）。副作用として
            //   複製を 1 度だけ作りうるが、それは描画側が作るものと同一の 1 個で、
            //   レベルアンロードで VolcanoVanillaFx.Destroy が畳む。
            //   Check の外で例外を抑えるのは上の 4 件と同じ理由。
            VolcanoVanillaFacts vanilla;
            try
            {
                vanilla = VolcanoVanillaFx.ScanFacts();
            }
            catch
            {
                vanilla = new VolcanoVanillaFacts();
            }

            Check("the game's own particle effects \"" + VolcanoVanillaFx.AshName + "\", \""
                  + VolcanoVanillaFx.FlameName + "\" and \"" + VolcanoVanillaFx.EjectaName
                  + "\" can be looked up and rendered (ash: "
                  + (vanilla.AshResolved ? "ok" : "missing")
                  + ", flames: " + (vanilla.FlameResolved ? "ok" : "missing")
                  + ", ejecta: " + (vanilla.EjectaResolved ? "ok" : "missing")
                  + ", cameraInfo: " + (vanilla.CameraInfoResolved ? "ok" : "missing") + ")",
                  "the missing piece of the eruption is simply not drawn. The mountain still "
                  + "rises, the lava still flows and it still sets buildings on fire. None of "
                  + "these effects needs a DLC",
                  delegate { return vanilla.EruptionUsable; });

            // --- ⑤火山（Task 7: 噴火の借り物エフェクト）ここまで ---

            // --- ⑤火山（火砕流の代用）ここから ---

            // 6. 斜面を下る土煙の帯。**バニラに火砕流のエフェクトは 1 つも無い**ので、
            //   ⑤は建物崩壊の粉塵を溶岩の経路へ流している。噴火の 3 つとは成否が
            //   別に決まるので、検査も別にする（片方の欠けでもう片方を巻き込まない）。
            //
            //   ★ 述語は VolcanoPyroclasticFx.Step が実際に門にしている式
            //     （VolcanoVanillaFacts.PyroclasticUsable）そのものである。
            Check("the game's own particle effect \"" + VolcanoVanillaFx.DustName
                  + "\" can be looked up and rendered (dust: "
                  + (vanilla.DustResolved ? "ok" : "missing")
                  + ", cameraInfo: " + (vanilla.CameraInfoResolved ? "ok" : "missing") + ")",
                  "the dust surge that stands in for a pyroclastic flow is not drawn. Nothing "
                  + "else changes - that surge damages nothing, and the game has no real "
                  + "pyroclastic flow effect to fall back to",
                  delegate { return vanilla.PyroclasticUsable; });

            // --- ⑤火山（火砕流の代用）ここまで ---

            // --- ⑤火山（Task 8: 溶岩の着火経路）ここから ---

            // 7. 着火と水の 3 つ。**溶岩そのものはこれが無くても流れて描かれる**ので、
            //   impact にそこまで書く。
            //
            //   ★ **TreeManager.BurnTree はこの検査に含めない。** ND 非所持で
            //     常に false になるのは正常であり（§B-7c の SupportsExpansion ゲート）、
            //     FAIL にすると狼少年になる。所持／非所持は診断の 1 行で名乗る。
            //
            //   ★ 述語は VolcanoLava が実際に呼ぶ 3 つのメソッドの解決である。
            //     引数の型まで指定するのは、SampleDetailHeight のように
            //     同名オーバーロードがあるものを取り違えないためである（②が確立した形）。
            Check("DisasterHelpers.BurnGround, BuildingAI.BurnBuilding and "
                  + "TerrainManager.HasWater are resolvable",
                  "the lava still flows and is still drawn, but it neither scorches the ground "
                  + "nor sets buildings on fire, and it does not stop at water",
                  delegate { return LavaIgnitionResolvable(); });

            // --- ⑤火山（Task 8: 溶岩の着火経路）ここまで ---

            // --- ⑤火山（Task 9: 溶岩の描画のシェーダ）ここから ---

            // 8. 溶岩の面のシェーダ。
            //
            //   ★★ **述語に「Standard が取れた」を混ぜない。** Standard は
            //     Unity の組み込みなので実質必ず非 null だと思われており、
            //     「… || Shader.Find("Standard") != null」という検査は**構造上 1 度も
            //     失敗できない**形だった（④のレビューがまさにこれを見つけている。
            //     実機では Shader.Find がその Standard にすら null を返したが、
            //     構造上の欠陥は欠陥のままである）。
            //     ここが見るのは **粒子系（加算 / アルファブレンド）が取れたか**で、
            //     取れなければ Standard を透過モードにして描く（＝見えるが光らない）。
            //
            //   ★ 名前に「実際に何で解決したか」を出すのは、失敗したときにその情報が
            //     ここにしか出ないからである（T2 の RawHeights の件数と同じ扱い）。
            //     **借りてきたのかどうかも出す** —— Shader.Find で引けた環境と
            //     読み込み済み Material から借りた環境は、次に何を疑うかが違う。
            //
            //   ★ 述語は VolcanoLavaFx が実際に門にしている式そのものである
            //     （ScanShaderFacts は BuildMaterial と同じ ShaderPool を呼ぶ）。
            //     ShaderPool は main スレッド専用で、Run() も main である。
            //     Check の外で例外を抑えるのは上の 7 件と同じ理由。
            VolcanoLavaShaderFacts lavaShader;
            try
            {
                lavaShader = VolcanoLavaFx.ScanShaderFacts();
            }
            catch
            {
                lavaShader = new VolcanoLavaShaderFacts(null, false, "NONE (the scan threw)");
            }

            Check("an additive or alpha-blended particle shader resolves for the lava surface, "
                  + "by name or by borrowing the shader off a loaded material (resolved: "
                  + (string.IsNullOrEmpty(lavaShader.ResolvedShaderName)
                        ? "nothing" : lavaShader.Detail) + ")",
                  "the lava surface falls back to the Standard shader forced into transparent "
                  + "mode, so it draws but does not glow; if nothing resolves at all it is not "
                  + "drawn. The lava still flows, still scorches the ground and still sets "
                  + "buildings on fire either way",
                  delegate { return lavaShader.ParticleShaderResolved; });

            // --- ⑤火山（Task 9: 溶岩の描画のシェーダ）ここまで ---

            // --- ⑤火山（噴火の音）ここから ---

            // 9. 音の経路。**この検査が FAIL でも噴火はそのまま出る**（音だけが消える）ので、
            //   impact にそう書く（狼少年にしない）。
            //
            //   ★ 述語は VolcanoEruptionAudio.Update が実際に門にしている式
            //     （VolcanoAudioFacts.Usable）そのものである。
            //
            //   ★★ **同梱 wav の有無を述語に混ぜない。** ファイルはプレイヤーが
            //     消せるもので、消えていることは「ゲーム更新で前提が壊れた」ではない。
            //     混ぜると、自分で消した人に前提違反を名乗ることになる。
            //     代わりに**名前のほうに実測を出す** —— 音が出ないときに
            //     「経路が無い」のか「ファイルが無い」のかは、ここでしか区別できない。
            //
            //   ScanAudioFacts は副作用の無い走査で main スレッド専用（Run() も main）。
            //   クリップは読まないので、レベルロードに 6 MB の I/O を持ち込まない。
            //   Check の外で例外を抑えるのは上の 7 件と同じ理由。
            VolcanoAudioFacts audio;
            try
            {
                audio = VolcanoEruptionAudio.ScanAudioFacts();
            }
            catch
            {
                audio = new VolcanoAudioFacts();
            }

            Check("AudioManager.EffectGroup and AddEvent(AudioGroup,AudioInfo,Vector3,Vector3,"
                  + "float,float,float,int) are reachable, and AudioClip.Create/SetData resolve "
                  + "(bundled wav: "
                  + (audio.FileFound ? audio.FileBytes + " bytes" : "NOT PRESENT")
                  + ")",
                  "the eruption is silent. Nothing else changes: the mountain, the plume and "
                  + "the lava never touch the audio path. This is also the only route through "
                  + "which the player's effect volume and mute reach the sound, so Disaster + "
                  + "does not fall back to playing it at a raw fixed volume",
                  delegate { return audio.Usable; });

            // --- ⑤火山（噴火の音）ここまで ---
        }

        /// <summary>
        /// <see cref="VolcanoLava"/> が呼ぶ 3 つのメソッドが解決できるか。
        /// **これは⑤の門ではない**（無くても溶岩は流れる）ので、
        /// <c>VolcanoTerrainFacts.Usable</c> のような 1 本の式にはまとめていない ——
        /// まとめると「溶岩が焦がさないだけ」の環境で山まで止めることになる。
        /// </summary>
        private static bool LavaIgnitionResolvable()
        {
            try
            {
                bool burnGround = typeof(DisasterHelpers).GetMethod(
                    "BurnGround",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new System.Type[]
                    {
                        typeof(UnityEngine.Vector2), typeof(float), typeof(float)
                    },
                    null) != null;

                bool burnBuilding = typeof(BuildingAI).GetMethod(
                    "BurnBuilding",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new System.Type[]
                    {
                        typeof(ushort), typeof(Building).MakeByRefType(),
                        typeof(InstanceManager.Group), typeof(bool)
                    },
                    null) != null;

                bool hasWater = typeof(TerrainManager).GetMethod(
                    "HasWater",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new System.Type[] { typeof(UnityEngine.Vector2) },
                    null) != null;

                return burnGround && burnBuilding && hasWater;
            }
            catch
            {
                return false;
            }
        }
    }
}
