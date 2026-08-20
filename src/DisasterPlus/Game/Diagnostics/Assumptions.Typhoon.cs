using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="Assumptions"/> のうち④台風 の前提。
    ///
    /// **このファイルには検証しか置かない。** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> は本体側の private のままで、
    /// partial なので可視性を 1 つも上げずに使える（分割の要件そのもの）。
    ///
    /// 件数は <see cref="TyphoonCheckCount"/> がこのファイルの中で宣言する。
    /// **検証を足したらここも増やすこと** —— 本体の <c>TotalCheckCount</c> は
    /// これらの和である。
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>このファイルが持つ検証の数。</summary>
        private const int TyphoonCheckCount = 9;

        private static void RunTyphoon()
        {
            // --- ④台風（Task 2: 骨格・プレハブ実測）ここから ---

            // ②の EarthquakeAI の項目と同じ性質の検証で、**実行時にしか値が取れない**。
            // m_radius / m_emergingDuration / m_activeDuration の実数値は DLL に無く
            // （プレハブのシリアライズ値、IL 事実文書 §A-0、PARTIAL）、④の
            // 暴風域半径も持続時間も**進行速度**も全部この 3 値の上に乗る
            // （TyphoonTrack.SpeedFor は m_activeDuration が 0 なら 0 を返し、
            //  呼び出し側は台風を 1 個も起こさない）。読めないなら読めないと
            // 名指しする以外に防波堤が無い。
            //
            // DLC 非所持環境ではこれが FAIL するのが正常。TornadoAI / TsunamiAI の
            // 項目が既に同じ性質を持っており、それが確立した扱いである。
            // 母数からは外さない——台風機能そのものが DLC 依存なので、
            // 「使えない」と名指しするのが正しい。
            Check("ThunderStormAI disaster prefab exposes m_radius / m_emergingDuration / "
                  + "m_activeDuration, and m_radius / m_activeDuration are non-zero",
                  "no typhoon can be started at all: its radius, its lifetime and its travel "
                  + "speed are all derived from these three numbers, and the mod refuses to "
                  + "guess them. This also FAILs when the Natural Disasters DLC is not owned, "
                  + "which is expected.",
                  delegate
                  {
                      var t = typeof(ThunderStormAI);
                      if (!HasField(t, "m_radius", typeof(float))
                          || !HasField(t, "m_emergingDuration", typeof(uint))
                          || !HasField(t, "m_activeDuration", typeof(uint)))
                      {
                          return false;
                      }
                      // 副作用の無い純粋な走査を使う。TyphoonReader の内部キャッシュは
                      // sim スレッドが回しており、main スレッドのここから巻き戻しては
                      // いけない（EarthquakeAI の項目と同じ理由）。
                      //
                      // ★★ **StormResolved ではなく Usable を見る**（全体レビュー C1）。
                      //    StormResolved は「プレハブという*オブジェクト*が見つかり、
                      //    3 つのフィールドを読み終えた」だけで立つ ——
                      //    **中身は 1 バイトも見ていない。** 台風を起こしてよいかを
                      //    実際に決めているのは TyphoonPrefabFacts.Usable
                      //    （＝ StormResolved && StormRadius > 0 && ActiveDuration > 0）で、
                      //    TyphoonController.Start はそちらで断っている。
                      //    ここが Resolved のままだと、m_radius か m_activeDuration が
                      //    0 でデシリアライズされた環境で**ボタンを押しても何も起きないのに、
                      //    ダンプも設定画面もこの検証だけ PASS と名乗る**。
                      //    「読めた」を「使える」の代わりに使わない。
                      return TyphoonReader.ScanPrefabFacts().Usable;
                  },
                  true);

            // --- ④台風（Task 2）ここまで ---

            // --- ④台風（Task 3: 論理オブジェクトと経路追従）ここから ---

            // ④の移動機構そのもの。DisasterData.m_targetPosition を毎 sim tick 書き換えて
            // 災害を動かす（IL 事実文書 §E-1。本タスクで全アセンブリの
            // stfld DisasterData::m_targetPosition を走査し直し、既存の災害のそれを
            // 書くバニラのコードが 1 つも無いことを再確認した）。
            //
            // m_activationFrame は罠 1 の見張りに使う——SelfTrigger が効いていなければ
            // StartDisaster が即 return し、この値が 0 のままになる（§A-1 IL_003F）。
            // ここが読めなければ見張りごと成立しないので、同じ項目で照合する。
            //
            // メソッドは引数の型まで指定して見る（②の BuildingAI.CollapseBuilding の
            // 検査と同じ形）。名前だけの一致では、シグネチャが変わったときに
            // 偽 PASS を出す。
            Check("DisasterData exposes m_targetPosition / m_angle / m_intensity / "
                  + "m_activationFrame, and DisasterAI.StartNow / DeactivateNow / "
                  + "ClampDisasterTarget are resolvable",
                  "the typhoon cannot be created, moved or stopped; the feature does nothing "
                  + "at all",
                  delegate
                  {
                      var d = typeof(DisasterData);
                      if (!HasField(d, "m_targetPosition", typeof(UnityEngine.Vector3))
                          || !HasField(d, "m_angle", typeof(float))
                          || !HasField(d, "m_intensity", typeof(byte))
                          || !HasField(d, "m_activationFrame", typeof(uint)))
                      {
                          return false;
                      }

                      var byRef = new Type[]
                      {
                          typeof(ushort), typeof(DisasterData).MakeByRefType()
                      };
                      if (typeof(DisasterAI).GetMethod("StartNow",
                              BindingFlags.Public | BindingFlags.Instance, null, byRef,
                              null) == null)
                      {
                          return false;
                      }
                      if (typeof(DisasterAI).GetMethod("DeactivateNow",
                              BindingFlags.Public | BindingFlags.Instance, null, byRef,
                              null) == null)
                      {
                          return false;
                      }

                      return typeof(DisasterAI).GetMethod("ClampDisasterTarget",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[] { typeof(UnityEngine.Vector3).MakeByRefType() },
                          null) != null;
                  });

            // --- ④台風（Task 3）ここまで ---

            // --- ④台風（Task 4: 天候の駆動）ここから ---

            // ④が天候を握る 6 フィールド（§A-4）。どれが欠けても**例外は出ず**、
            // 台風が晴天の下を進むだけになる。
            //
            // m_forceWeatherOn だけは性質が違う。これが無いと、天候を切っている
            // プレイヤーの環境で m_targetRain / Cloud / Fog が毎ステップ 0 へ潰される
            // （IL_053D の枝）。「一部の環境でだけ静かに何も起きない」という、
            // いちばん報告されにくい壊れ方をするので、名指しで検証する。
            Check("WeatherManager exposes m_targetRain / m_targetCloud / m_targetFog / "
                  + "m_targetDirection / m_forceWeatherOn / m_enableWeather",
                  "the typhoon cannot drive the weather; it would move across the map under "
                  + "a clear sky",
                  delegate
                  {
                      var w = typeof(WeatherManager);
                      return HasField(w, "m_targetRain", typeof(float))
                             && HasField(w, "m_targetCloud", typeof(float))
                             && HasField(w, "m_targetFog", typeof(float))
                             && HasField(w, "m_targetDirection", typeof(float))
                             && HasField(w, "m_forceWeatherOn", typeof(float))
                             && HasField(w, "m_enableWeather", typeof(bool));
                  });

            // --- ④台風（Task 4）ここまで ---

            // --- ④台風（Task 6: 落雷）ここから ---

            // 落雷は**実体**で、BurnBuilding / BurnTree / CollapseSegment を起こす
            // （IL 事実文書 §A-3）。このメソッドが解決できなければ台風は雷を 1 発も
            // 運ばないが、**例外は出ず、嵐は動き天候も駆動され続ける** ——
            // 「雷の少ない台風」に見えるだけで、原因を指すものが他に無い。
            //
            // 引数の型まで指定して見る（1 引数版 QueueLightningStrike(uint) が別に
            // 存在するので、名前だけの一致では偽 PASS になる）。
            Check("WeatherManager.QueueLightningStrike(uint, Vector3, Quaternion, "
                  + "InstanceManager.Group) is resolvable",
                  "the typhoon carries no lightning; the storm still moves and drives the "
                  + "weather",
                  delegate
                  {
                      return typeof(WeatherManager).GetMethod("QueueLightningStrike",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(uint), typeof(UnityEngine.Vector3),
                              typeof(UnityEngine.Quaternion), typeof(InstanceManager.Group)
                          },
                          null) != null;
                  });

            // --- ④台風（Task 6）ここまで ---

            // --- ④台風（Task 7: 風害）ここから ---

            // 風害の 3 経路。**どれが欠けても例外は出ない** ——
            // 台風が通っても建物が 1 棟も倒れないだけになる。
            //
            // CollapseBuilding は②が既に検証している 6 引数版と同じ形で見る。
            // AddWind / DestroyTrees の並びは Task 7 Step 1 で IL 実測し、
            // §B-1 が DestroyStuff の転送から導いていた並びと一致することを確認した:
            //
            //   public static void AddWind(Vector3, float, Vector3, float, float,
            //                              InstanceManager.Group)
            //   public static void DestroyTrees(int, InstanceManager.Group, Vector3,
            //                                   float, float, float, float, float, float)
            //
            // ★ DestroyTrees だけが解決できない場合はこの検査を FAIL にしない。
            //   風害本体（倒壊と AddWind）は動くので、FAIL にすると狼少年になる。
            //   倒木を諦めた事実は TyphoonWind が FeatureHost.NoteDegraded で名乗る。
            //
            // ★★ **ただし「見ていないものを名前で名乗らない」**（全体レビュー）。
            //    この検査は以前 "AddWind / DestroyTrees are reachable" と名乗りながら
            //    DestroyTrees を 1 度も引いていなかった —— PASS が、一度も調べて
            //    いない相手について断定していたことになる。合否には入れないが
            //    **実際に引き、結果を名前に書く**。名前は Check に渡す前に組み立てる
            //    （AssumptionResult は Name しか表示しないので、ここが唯一の出口）。
            Check("BuildingAI.CollapseBuilding is resolvable and DisasterHelpers.AddWind is "
                  + "reachable (DisasterHelpers.DestroyTrees: "
                  + (DestroyTreesIsReachable()
                        ? "reachable"
                        : "MISSING - the typhoon fells no trees; the rest of the wind sweep runs")
                  + ")",
                  "wind damage cannot be applied. The typhoon still moves, drives the weather "
                  + "and drops lightning; the mod disables the wind sweep rather than reaching "
                  + "for DisasterHelpers.DestroyBuildings, which Natural Disasters Renewal "
                  + "replaces wholesale",
                  delegate
                  {
                      if (typeof(BuildingAI).GetMethod("CollapseBuilding",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(ushort), typeof(Building).MakeByRefType(),
                                  typeof(InstanceManager.Group), typeof(bool), typeof(bool),
                                  typeof(int)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      // AddWind が無ければ演出だけでなく「風害の経路が丸ごと違う」
                      // 合図なので、こちらは FAIL に含める。
                      return typeof(DisasterHelpers).GetMethod("AddWind",
                          BindingFlags.Public | BindingFlags.Static, null,
                          new Type[]
                          {
                              typeof(UnityEngine.Vector3), typeof(float),
                              typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                              typeof(InstanceManager.Group)
                          },
                          null) != null;
                  });

            // --- ④台風（Task 7）ここまで ---

            // --- ④台風（Task 8: 河川氾濫）ここから ---

            // 氾濫の到達経路。**解決できなければ TyphoonFlood は 1 バイトも書かない。**
            //
            // Task 8 Step 1 で IL 実測して確定させたこと:
            //   TerrainManager.WaterSimulation  … public インスタンスプロパティ
            //                                     （裏は private m_waterSimulation）
            //   WaterSimulation.m_waterSources  … public FastList<WaterSource>
            //   LockWaterSource(ushort)         … public、m_buffer[source - 1] を返す
            //                                     （**1 基点**）。Monitor を取ったまま返る
            //   UnlockWaterSource(ushort, WaterSource) … public、書き戻して Monitor.Exit
            //   WaterSource                     … public struct、m_type / m_target は
            //                                     public UInt16、TYPE_NATURAL = 1
            //
            // ★ 代替経路へ逃げないことを impact に書く。CreateWaterWave は内陸で
            //   **何も起こさない**（津波の波はマップ外周リングでしか評価されない。§D-3(a)）。
            Check("WaterSimulation is reachable and exposes m_waterSources / LockWaterSource / "
                  + "UnlockWaterSource, and WaterSource exposes m_type / m_target",
                  "river flooding cannot run. The mod does nothing rather than reaching for "
                  + "CreateWaterWave, which does nothing at all inland (the tsunami wave is "
                  + "only evaluated on the map border ring)",
                  delegate
                  {
                      var wsProperty = typeof(TerrainManager).GetProperty("WaterSimulation",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (wsProperty == null
                          || wsProperty.PropertyType != typeof(WaterSimulation))
                      {
                          return false;
                      }

                      // FastList<WaterSource> であることまで見る（実測で確認済み）。
                      if (!HasField(typeof(WaterSimulation), "m_waterSources",
                                    typeof(FastList<WaterSource>)))
                      {
                          return false;
                      }

                      if (typeof(WaterSimulation).GetMethod("LockWaterSource",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[] { typeof(ushort) }, null) == null)
                      {
                          return false;
                      }
                      if (typeof(WaterSimulation).GetMethod("UnlockWaterSource",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[] { typeof(ushort), typeof(WaterSource) },
                              null) == null)
                      {
                          return false;
                      }

                      // ★ 型まで見る（全体レビュー）。ここのコメントは以前から
                      //    「public UInt16」と断定していたのに、検査は名前しか見て
                      //    いなかった。TyphoonFlood は ushort として読み書きする。
                      return HasField(typeof(WaterSource), "m_type", typeof(ushort))
                             && HasField(typeof(WaterSource), "m_target", typeof(ushort));
                  });

            // --- ④台風（Task 8）ここまで ---

            // --- ④台風（竜巻並みの局所被害）ここから ---

            // ★★ **随伴竜巻の 2 件（VortexAI のプレハブ値と Vehicle.SetTargetPos の
            //    操舵経路）はここから消えた。** バニラの竜巻災害を借りる機能そのものが
            //    退役し、竜巻並みの被害は TyphoonGust が自前で出すようになったので、
            //    どちらも「この MOD が依存していない事実」になった。
            //    使っていない依存を検証し続けると、FAIL したときに何が壊れるのか
            //    誰も答えられなくなる。
            //
            // 局所被害が実際に門にしているのは **BuildingAI.CollapseBuilding** ただ 1 本で、
            // ④の風害と同じ経路である（DisasterHelpers は 1 度も通らない ＝
            // Natural Disasters Renewal のパッチ面を完全に迂回する）。
            // 引数の型まで指定して見る —— 名前だけの一致では、シグネチャが変わったときに
            // 偽 PASS を出す。
            Check("BuildingAI.CollapseBuilding(ushort, ref Building, InstanceManager.Group, "
                  + "bool, bool, ushort) is resolvable",
                  "the typhoon's tornado-strength damage patches cannot collapse anything, "
                  + "and neither can its wind damage. Both call this method directly and "
                  + "never go through DisasterHelpers, which is what keeps them clear of "
                  + "Natural Disasters Renewal. The rain, the lightning, the flooding and "
                  + "the cloud are unaffected",
                  delegate
                  {
                      return typeof(BuildingAI).GetMethod("CollapseBuilding",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(ushort), typeof(Building).MakeByRefType(),
                              typeof(InstanceManager.Group), typeof(bool), typeof(bool),
                              typeof(ushort)
                          },
                          null) != null;
                  });

            // --- ④台風（竜巻並みの局所被害）ここまで ---

            // --- ④台風（Task 9: 巨大な回転雲）ここから ---

            // ④の雲は**全部自前**である。バニラに流用できる雲は 1 つも無く
            // （DisasterInfo.m_effect はフィールドごと存在しない。§C-1）、バニラの雲は
            // ワールド座標を持たないスカイドームなので合成もできない（§C-2）。
            // したがって雲の生死は「自前のマテリアルが作れるか」だけに掛かっている。
            //
            // ★ ここが FAIL のとき、④は**何も描かない**。CS のマテリアルを借りる
            //   逃げ道は取らない —— CS のシェーダはエンジンが供給する per-instance
            //   データを要求するので、自前の DrawMesh に載せると不可視か真っ黒になる
            //   （VortexAI.RenderExtraStuff がその実例。§C-1 / 火災旋風 §4.9）。
            //   将来のゲーム更新でシェーダ名が変わったとき、黙って雲が消えるのではなく
            //   ここが名指しする。
            //
            // ShaderPool は main スレッド専用だが、Run() 自体が main スレッド専用なので
            // 問題ない。**DayNightDynamicCloudsProperties の実在はここに入れない** ——
            // 無いのは正当な環境（DLC・グラフィック設定）で、④の自前の雲には
            // 影響しないため（FAIL にすると狼少年になる）。
            //
            // ★★ **述語に「Standard が取れた」を混ぜない**（全体レビュー）。
            //    以前はこの検査が `|| Shader.Find("Standard") != null` で終わっていた。
            //    Standard は Unity 組み込みなので実質いつでも解決すると思われており、
            //    **この検査は原理的に FAIL しない**形だった（実機では Shader.Find が
            //    その Standard にすら null を返したので結果的に FAIL したが、
            //    構造上の欠陥は欠陥のままである）。
            //    ここが見るのは⑤の溶岩と同じく **「粒子系が取れたか」** で、
            //    取れなければ Standard を透過モードにして描く（＝見えるが光らない）。
            //    「1 つも取れなかった」は名前のほうに出る。
            //
            // ★ **述語は④が実際に門にしている式でなければならない。**
            //    だから自前に順序を書き写さず、TyphoonCloud と**同じ** ShaderPool を
            //    同じ preference で呼ぶ（順序を写すと、検査が報告する名前と
            //    実際に使うシェーダが黙ってずれる）。
            ShaderPick cloudShader = ResolveCloudShader();
            Check("Graphics.DrawMesh(Mesh, Matrix4x4, Material, int, Camera, int, "
                  + "MaterialPropertyBlock, bool, bool) is reachable and an additive or "
                  + "alpha-blended particle shader resolves for the cloud, by name or by "
                  + "borrowing the shader off a loaded material (resolved: "
                  + cloudShader.Describe() + ")",
                  "the typhoon's own cloud falls back to the Standard shader forced into "
                  + "transparent mode, so it draws but does not glow; if nothing resolves at "
                  + "all it is not drawn. Every other part of the typhoon is unaffected. The "
                  + "mod draws nothing rather than borrowing a Cities material instance, which "
                  + "renders invisible or black in a hand-rolled DrawMesh - borrowing only the "
                  + "shader off such a material is a different thing and is what it does",
                  delegate
                  {
                      // ★ 4 引数版ではなく**実際に呼んでいる 9 引数版**を見る。
                      //   4 引数版は castShadows: true / receiveShadows: true を転送するので、
                      //   ④は影を落とさない 9 引数版へ移した（TyphoonCloud の doc）。
                      //   検査する相手は、実際に呼ぶオーバーロードでなければ意味が無い。
                      if (typeof(UnityEngine.Graphics).GetMethod("DrawMesh",
                              BindingFlags.Public | BindingFlags.Static, null,
                              new Type[]
                              {
                                  typeof(UnityEngine.Mesh), typeof(UnityEngine.Matrix4x4),
                                  typeof(UnityEngine.Material), typeof(int),
                                  typeof(UnityEngine.Camera), typeof(int),
                                  typeof(UnityEngine.MaterialPropertyBlock),
                                  typeof(bool), typeof(bool)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      return cloudShader.Particle;
                  });

            // --- ④台風（Task 9）ここまで ---

            // --- ④台風（渦を雲の粒で組む）ここから ---

            // 渦の**本経路**はバニラの粒子エフェクトを借りて雲の粒を撒くことである
            // （TyphoonCloudFx）。借りられるかどうかは実行時にしか分からない ——
            // EffectCollection の登録は 186 個だが Factory Smoke はそこに**入っておらず**
            // （エフェクト実測文書 §A-3 の未登録 17 個）、実行時の在庫は
            // EffectsWrapper.m_BuiltinEffects（Resources.FindObjectsOfTypeAll の結果）
            // でしか確かめられない（同 §A-2、PARTIAL）。
            //
            // ★ 述語は**この機能が実際に門にしている式**である。候補の並びをここへ
            //   書き写すと、報告する名前と実際に借りる素材が黙ってずれる ——
            //   だから TyphoonCloudFx.Lookup をそのまま共有する（雲のシェーダ検証が
            //   ShaderPool を共有しているのと同じ形）。
            //
            // ★ FAIL は「雲が消える」ではない。旧来の自前スパイラルメッシュへ退避する
            //   （そちらの可否は 1 つ上の検証が名乗る）。台風の他の要素は 1 つも止まらない。
            string borrowed;
            bool canBorrow = TyphoonCloudFx.CanBorrow(out borrowed);
            Check("EffectInfo.RenderEffect(InstanceID, SpawnArea, Vector3, float, float, "
                  + "float, float, CameraInfo) is reachable and a vanilla cloud/smoke "
                  + "ParticleEffect can be borrowed for the vortex (resolved: "
                  + (canBorrow ? borrowed : "none") + ")",
                  "the typhoon's vortex falls back to the mod's own spiral mesh, which is a "
                  + "flat ~900 m symbol of a vortex rather than a sky-filling canopy, and "
                  + "which needs a shader of its own. Every other part of the typhoon is "
                  + "unaffected.",
                  delegate
                  {
                      if (typeof(EffectInfo).GetMethod("RenderEffect",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(InstanceID), typeof(EffectInfo.SpawnArea),
                                  typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                                  typeof(float), typeof(float),
                                  typeof(RenderManager.CameraInfo)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      return canBorrow;
                  });

            // --- ④台風（渦を雲の粒で組む）ここまで ---
        }

        /// <summary>
        /// ④の雲が実際に使うシェーダ。**<c>TyphoonCloud.BuildMaterial</c> と
        /// 同じ <c>ShaderPool</c> を同じ preference で呼ぶ** —— 順序をここへ写すと、
        /// 検査が報告する名前と実際に使うシェーダが黙ってずれる。
        /// main スレッド専用だが、<see cref="Run"/> 自体が main スレッド専用なので問題ない。
        /// </summary>
        private static ShaderPick ResolveCloudShader()
        {
            // ★ **自分で try/catch する。** ここは検証の*名前*を組み立てるために
            //   Check() の外側（＝あの try/catch の外）で呼ばれる。Assumptions.Run() は
            //   DisasterPlusLoading.OnLevelLoaded から素で呼ばれているので、
            //   ここから例外を投げるとレベルロードが壊れる
            //   （DestroyTreesIsReachable が同じ理由で同じ形をしている）。
            try
            {
                return ShaderPool.Resolve(ShaderPreference.AlphaBlended);
            }
            catch
            {
                // 「解決しなかった」側に倒す。検証も FAIL になるので、
                // 黙って PASS を出すことにはならない。
                return new ShaderPick(null, false, false, false);
            }
        }

        /// <summary>
        /// <c>DisasterHelpers.DestroyTrees</c> が引数の型まで込みで解決できるか。
        /// **合否には使わない**（倒木だけが使えない環境で風害まで FAIL にすると
        /// 狼少年になる）。検証の名前に事実を書くためだけに引く。
        /// 並びは Task 7 Step 1 で IL 実測したものと同じ。
        /// </summary>
        private static bool DestroyTreesIsReachable()
        {
            try
            {
                return typeof(DisasterHelpers).GetMethod("DestroyTrees",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new Type[]
                    {
                        typeof(int), typeof(InstanceManager.Group), typeof(UnityEngine.Vector3),
                        typeof(float), typeof(float), typeof(float), typeof(float),
                        typeof(float), typeof(float)
                    },
                    null) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
