using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="Assumptions"/> のうち②地震 の前提。
    ///
    /// **このファイルには検証しか置かない。** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> は本体側の private のままで、
    /// partial なので可視性を 1 つも上げずに使える（分割の要件そのもの）。
    ///
    /// 件数は <see cref="EarthquakeCheckCount"/> がこのファイルの中で宣言する。
    /// **検証を足したらここも増やすこと** —— 本体の <c>TotalCheckCount</c> は
    /// これらの和である。
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>このファイルが持つ検証の数。</summary>
        private const int EarthquakeCheckCount = 11;

        private static void RunEarthquake()
        {
            // --- ②地震（Task 3）ここから ---

            // このプロジェクトで唯一「実行時にしか値が取れない」前提。
            // m_crackLength / m_crackWidth / m_emergingDuration / m_activeDuration の
            // 実数値は DLL に無く（プレハブのシリアライズ値、IL 事実文書 §A-0）、
            // UnityPy による sharedassets の読み出しも失敗している。②の以後の
            // 持続時間の設計は全てこの 4 値の上に乗るので、読めないなら読めないと
            // 名指しする以外に防波堤が無い。
            //
            // DLC 非所持環境ではこれが FAIL するのが正常。TornadoAI の項目が既に
            // 同じ性質を持っており、それが確立した扱い。母数からは外さない
            // （ReportSliderNotApplicable 方式にしない）——地震機能そのものが
            // DLC 依存なので、「使えない」と名指しするのが正しい。
            Check("EarthquakeAI disaster prefab exposes its four tuning fields and "
                  + "m_activeDuration is non-zero",
                  "no earthquake durations or fault geometry can be read; every duration in this "
                  + "feature is designed on top of these four numbers. This also FAILs when the "
                  + "Natural Disasters DLC is not owned, which is expected.",
                  delegate
                  {
                      var t = typeof(EarthquakeAI);
                      if (!HasField(t, "m_crackLength", typeof(float))
                          || !HasField(t, "m_crackWidth", typeof(float))
                          || !HasField(t, "m_emergingDuration", typeof(uint))
                          || !HasField(t, "m_activeDuration", typeof(uint)))
                      {
                          return false;
                      }
                      // 副作用の無い純粋な走査を使う。EarthquakeReader の内部キャッシュは
                      // sim スレッドが回しており、main スレッドのここから巻き戻してはいけない
                      // （FireWhirlSpawner.HasTornadoPrefab と同じ理由）。
                      //
                      // ★★ **Resolved だけを見ない**（全体レビュー C1 の監査で見つかった
                      //    3 件目）。Resolved は「オブジェクトが見つかり 4 値を読み終えた」
                      //    だけで立ち、**中身を 1 バイトも見ていない**。②で実際に振る舞いを
                      //    決めているのは 4 箇所の `Resolved || ActiveDuration == 0u` という
                      //    ゲート（CameraShakeBooster / EarthquakeFeature /
                      //    EarthquakePanel / EarthquakeSensorRows）である。
                      //    m_activeDuration が 0 だと波形もカメラ補正も地震計も黙って
                      //    止まるのに、この検証だけ PASS を出していた。
                      //
                      //    m_crackLength / m_crackWidth は**ゲートに含めない**。
                      //    こちらは 0 のとき表示側が「unknown」と名乗る経路を既に持って
                      //    いる（EarthquakeFeature の "fault (L/W)" 行）ので、
                      //    黙って壊れる値ではない。
                      var quake = EarthquakeReader.ScanPrefabFacts();
                      return quake.Resolved && quake.ActiveDuration > 0u;
                  },
                  true);

            Check("DisasterData exposes m_intensity / m_activationFrame / m_startFrame / m_angle",
                  "neither the shaking strength nor the per-building margin can be shown",
                  delegate
                  {
                      var t = typeof(DisasterData);
                      return HasField(t, "m_intensity", typeof(byte))
                          && HasField(t, "m_activationFrame", typeof(uint))
                          && HasField(t, "m_startFrame", typeof(uint))
                          && HasField(t, "m_angle", typeof(float));
                  });

            // 列挙メンバは文字列で見る。コード内の直接参照はコンパイル時に整数へ
            // 畳み込まれるので、名前の変更を検出できない（SubInfoMode の検証と同じ理由）。
            Check("ImmaterialResourceManager.Resource.EarthquakeCoverage exists and "
                  + "CheckLocalResource is resolvable",
                  "seismograph coverage cannot be read, so the mod cannot explain why the hazard map is empty",
                  delegate
                  {
                      if (!Enum.IsDefined(typeof(ImmaterialResourceManager.Resource), "EarthquakeCoverage"))
                      {
                          return false;
                      }
                      return typeof(ImmaterialResourceManager).GetMethod("CheckLocalResource",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[]
                          {
                              typeof(ImmaterialResourceManager.Resource),
                              typeof(UnityEngine.Vector3),
                              typeof(int).MakeByRefType()
                          },
                          null) != null;
                  });

            // sim スレッドの時計。ここが解決できないと、残るのは main スレッドが書く
            // m_currentDayTimeHour だけになる——それはスレッド境界を跨ぐ上に、
            // m_referenceFrameIndex（描画補間側）由来の別の量である（§F-1）。
            //
            // なお m_enableDayNight が false であること自体は前提の破れではない
            // （プレイヤーが選べる正当な設定で、hour が 12.0 に固定されるだけ）。
            // ここで FAIL にすると偽 FAIL になるので、その事実は診断ダンプの
            // "sim clock" 行とパネルで名乗る。
            Check("SimulationManager exposes m_dayTimeFrame / DAYTIME_FRAME_TO_HOUR / m_enableDayNight",
                  "the sim-thread clock cannot be read; the mod would have to fall back to "
                  + "m_currentDayTimeHour, which is written by the main thread",
                  delegate
                  {
                      var t = typeof(SimulationManager);
                      return HasField(t, "m_dayTimeFrame", typeof(uint))
                          && HasField(t, "m_enableDayNight", typeof(bool))
                          && HasStaticField(t, "DAYTIME_FRAME_TO_HOUR", typeof(float));
                  });

            // --- ②地震（Task 3）ここまで ---

            // --- ②地震（Task 4）ここから ---

            // 地震のハザードビューへの切替と、そこに何かを塗る側の両方。
            // ここが FAIL すると「ハザードマップが空である理由」——本機能が出す
            // いちばん重要な説明——を、そもそも見せる場所が無くなる。
            // 列挙メンバは文字列で見る（コード内の直接参照はコンパイル時に整数へ
            // 畳み込まれるので、名前の変更を検出できない）。
            Check("SubInfoMode.EarthquakeHazard exists and EarthquakeAI.UpdateHazardMap exists",
                  "the earthquake hazard heatmap cannot be shown, so the mod cannot explain "
                  + "the Located gate",
                  delegate
                  {
                      return Enum.IsDefined(typeof(InfoManager.SubInfoMode), "EarthquakeHazard")
                          && HasUpdateHazardMap(typeof(EarthquakeAI));
                  });

            // --- ②地震（Task 4）ここまで ---

            // --- ②地震（Task 5）ここから ---

            // **これが Task 1 のビット一致を実際に保証する唯一の検査である。**
            // 建物ごとの倒壊判定は全て VanillaRandomizer の再現の上に乗っており、
            // 1 ビットずれても何も壊れない——もっともらしい数字が出続けたまま、
            // パネルの断定だけが全部嘘になる。ユニットテストは LCG の定義からの
            // 逸脱しか捕まえられない（本物の DLL を参照できない）ので、
            // ゲーム本体との一致はここでしか見られない。
            Check("VanillaRandomizer reproduces ColossalFramework.Math.Randomizer bit for bit",
                  "every per-building collapse verdict is wrong; the panel would keep showing "
                  + "plausible numbers that do not match what the game draws",
                  delegate
                  {
                      // 本物と自前の実装を並べて回す。ビット列だけでなく「引く順序」も見る
                      // （1 個ずれる壊れ方をこの検査で捕まえるため、必ず 2 回以上引く）。
                      // Randomizer は struct なので、必ずローカル変数に置いて使うこと
                      // （プロパティやフィールド経由で呼ぶとコピーが進んで列が分岐する）。
                      int[] seeds = { 0, 1, -1, 12345, 0x00070000 | 1234, int.MinValue, int.MaxValue };
                      for (int i = 0; i < seeds.Length; i++)
                      {
                          var real = new ColossalFramework.Math.Randomizer(seeds[i]);
                          var ours = new DisasterPlus.Core.Earthquake.VanillaRandomizer(seeds[i]);
                          for (int k = 0; k < 4; k++)
                          {
                              if (real.Int32(10000u) != ours.Int32(10000u)) return false;
                          }
                      }
                      return true;
                  });

            // --- ②地震（Task 5）ここまで ---

            // --- ②地震（Task 6）ここから ---

            // カメラの揺れの補正は、この 2 つの public フィールドの上にしか成り立たない。
            // どちらも Harmony を使わずに触れることが前提で（§A-7）、片方でも
            // 非公開化・改名・型変更されると CameraShakeBooster は例外を 1 回吐いた後
            // 黙って何も足さなくなる——そして**追加分 0 は強度 55 では正常な状態**
            // なので、画面を見ても機能が死んでいることに気付けない。
            //
            // m_disableCameraShake の方が重い。読めなければ「揺らすな」という
            // プレイヤーの明示的な選択を無視して足すことになるので、
            // CameraShakeBooster は読めない場合に**何も足さない**側へ倒している。
            Check("CameraController.m_cameraShake and DisasterManager.m_disableCameraShake "
                  + "are public fields",
                  "camera shake cannot be scaled with intensity and distance, and the mod cannot "
                  + "honour the player's \"disable camera shake\" choice",
                  delegate
                  {
                      var shake = typeof(CameraController).GetField("m_cameraShake",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (shake == null || shake.FieldType != typeof(UnityEngine.Vector3)) return false;

                      var disable = typeof(DisasterManager).GetField("m_disableCameraShake",
                          BindingFlags.Public | BindingFlags.Instance);
                      return disable != null && disable.FieldType == typeof(bool);
                  });

            // --- ②地震（Task 6）ここまで ---

            // --- ②地震（震度分布の地図オーバーレイ）ここから ---

            // **この機能はまるごとこの 4 つの API の上に乗っている。**
            // どれか 1 つでも消えると、オーバーレイは例外を 1 回吐いた後
            // 黙って何も描かなくなる —— そして「何も描かない」は
            // 「地震が無い」「トグルが OFF」とも見分けが付かない。
            //
            // IL 実測（この機能の着手時に自分で逆アセンブルして確認した）:
            //   RenderManager::RegisterRenderableManager  public static、m_renderables へ Add するだけ
            //   OverlayEffect::OnPostRender  IL_00A3  → RenderManager::Managers_RenderOverlay
            //   Managers_RenderOverlay       IL_0050  → 各 IRenderableManager::EndOverlay
            //   OverlayEffect::DrawCircle / DrawQuad → DrawEffect → Graphics::DrawMeshNow（即時描画）
            //
            // 列挙メンバではなくメソッドなので、型引数まで込みで照合する
            // （オーバーロードが増えたときに GetMethod(name) が
            //  AmbiguousMatchException を投げて偽 FAIL になるのを避ける）。
            Check("RenderManager overlay drawing API is reachable "
                  + "(RegisterRenderableManager / OverlayEffect.DrawCircle / DrawQuad)",
                  "the earthquake intensity distribution cannot be drawn on the map at all; "
                  + "the panel would keep offering a toggle that does nothing",
                  delegate
                  {
                      if (typeof(RenderManager).GetMethod("RegisterRenderableManager",
                              BindingFlags.Public | BindingFlags.Static,
                              null, new Type[] { typeof(IRenderableManager) }, null) == null)
                      {
                          return false;
                      }

                      var effect = typeof(RenderManager).GetProperty("OverlayEffect",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (effect == null || effect.PropertyType != typeof(OverlayEffect)) return false;

                      if (typeof(OverlayEffect).GetMethod("DrawCircle",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(RenderManager.CameraInfo), typeof(UnityEngine.Color),
                                  typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                                  typeof(float), typeof(bool), typeof(bool)
                              }, null) == null)
                      {
                          return false;
                      }

                      return typeof(OverlayEffect).GetMethod("DrawQuad",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(RenderManager.CameraInfo), typeof(UnityEngine.Color),
                              typeof(ColossalFramework.Math.Quad3), typeof(float), typeof(float),
                              typeof(bool), typeof(bool)
                          }, null) != null;
                  });

            // --- ②地震（震度分布の地図オーバーレイ）ここまで ---

            // --- ②地震（Task 9: 第 2 層 — 海中震源からの津波連鎖）ここから ---

            // **DLC が無い環境ではここが FAIL するのが正常である。** TsunamiAI の
            // *型* は DLC の有無に関わらず Assembly-CSharp に同梱されているので、
            // 型の存在検査は通ってしまう。実在を決めるのは PrefabCollection に
            // TsunamiAI を持つ DisasterInfo が居るかどうかで（§B-5）、
            // ModCompat.NaturalDisastersOwned は UI を出すかどうかの事前判定にすぎない。
            // 影響の文にその期待を書いておかないと、正常な環境の FAIL が不具合に見える。
            //
            // 走査は副作用の無い純粋な問い合わせを使う（FireWhirlSpawner.HasTornadoPrefab
            // と同じ理由。ここは main スレッドで、sim スレッドのキャッシュを
            // 巻き戻してはいけない）。
            Check("TsunamiAI disaster prefab is available",
                  "the tsunami chain cannot run (this also FAILs when the Natural Disasters DLC "
                  + "is not owned, which is expected)",
                  delegate { return TsunamiChain.HasTsunamiPrefab(); },
                  true);

            // 津波連鎖の入口と出口。HasWater が解決できなければ「震源が水中か」を
            // 判断できず、m_waveIndex が読めなければ「波が実際に立ったか」を判断できない
            // ——後者が読めないと、内陸マップの正常な「何も起きない」を
            // 「起こしたつもり」と取り違える。
            //
            // 引数の型まで込みで照合する（オーバーロードが 2 つあり、名前だけで
            // GetMethod を引くと AmbiguousMatchException で偽 FAIL になる。
            // 実測: HasWater(Vector2) と HasWater(Segment2, float, bool)）。
            Check("TerrainManager.HasWater is resolvable and DisasterData exposes m_waveIndex",
                  "the mod cannot tell whether the epicentre is under water, nor whether a wave "
                  + "was actually raised",
                  delegate
                  {
                      var hasWater = typeof(TerrainManager).GetMethod("HasWater",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[] { typeof(UnityEngine.Vector2) }, null);
                      if (hasWater == null || hasWater.ReturnType != typeof(bool)) return false;

                      var wave = typeof(DisasterData).GetField("m_waveIndex",
                          BindingFlags.Public | BindingFlags.Instance);
                      return wave != null && wave.FieldType == typeof(ushort);
                  });

            // --- ②地震（Task 9）ここまで ---

            // --- ②地震（Task 10: 第 2 層 — 長周期地震動）ここから ---

            // **この機能は建物を実際に壊す。** だから前提が破れたときに
            // 「静かに違う挙動」になることを許さない。見るのは 2 つ:
            //
            //   1. BuildingAI.CollapseBuilding が引数まで込みで解決できるか。
            //      DisasterHelpers を経由しないのが NDR 回避の要点（§E-2）なので、
            //      迂回先そのものが消えていないかを名指しする。
            //   2. 建物の高さが読めるか。**Building 構造体に高さのフィールドは無い**
            //      （IL 実測。あるのは m_baseHeight / m_width / m_length だけ）。
            //      高さはプレハブ側の BuildingInfo.m_size（Vector3、m）の y で、
            //      InitializePrefab が m_generatedInfo.m_size から入れる（IL_09BE）。
            //      単位がメートルであることは CommonBuildingAI.CollapseIfFlooded の
            //      `waterLevel > m_position.y + Max(4f, m_collisionHeight)` で確定
            //      （m_collisionHeight の出発点が m_size.y。BuildingHeight の
            //      クラス doc に IL 全文がある）。
            //
            //      ★ **m_collisionHeight は見ない**（第 2 層レビュー I2）。あちらは
            //      CheckReferences が敷地のプロップと樹木の上端まで Mathf.Max で
            //      取り込むので、平屋が 20 m 以上を名乗る。読めるかを確かめる相手は、
            //      実際に使うフィールドでなければ意味が無い。
            //
            // ここが FAIL したとき LongPeriodDamage は**何もしない**（推測した高さで
            // 建物を壊さない）ので、影響の文にもそう書く。
            Check("BuildingAI.CollapseBuilding is resolvable and building height can be read",
                  "long-period damage cannot be applied; the feature disables itself rather than "
                  + "guessing a height",
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

                      var size = typeof(BuildingInfo).GetField("m_size",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (size == null || size.FieldType != typeof(UnityEngine.Vector3))
                      {
                          return false;
                      }

                      // 予備経路（BuildingHeight.MetresOf）が使う出所そのもの。
                      var generated = typeof(BuildingInfo).GetField("m_generatedInfo",
                          BindingFlags.Public | BindingFlags.Instance);
                      return generated != null
                             && typeof(BuildingInfoGen).IsAssignableFrom(generated.FieldType);
                  });

            // --- ②地震（Task 10）ここまで ---
        }
    }
}
