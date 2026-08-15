# Disaster + — ⑤火山 設計書

- 日付: 2026-08-15
- 対象: Cities: Skylines 1（Unity 5.6 / .NET 3.5 / C# 7.3）
- MOD: `DisasterPlus`（表示名「Disaster +」）
- 状態: 承認済み
- 前提: Phase 0 ＋ ③火災旋風 ＋ Phase 0.5 診断基盤 ＋ ①天気予報タブ ＋ ②地震 が master にマージ済み。
  ④台風は `feature/typhoon` でレビュー中。**実機テスト未実施**
- **事実の典拠: `2026-08-15-volcano-il-facts.md`。IL 由来の主張は全てそこを参照する。再導出しない。**

---

## 1. 依頼と、調査で判明したこと

### 1.1 元の依頼

> 火山の実装：地形の変化を伴う災害です。クリックした位置で火山が生成され、溶岩が流れ出る
> アニメーションと溶岩の通ったところと一致する火災を実装してください。火山の生成は徐々に行われ、
> 地面が隆起して最後に頂上の噴火口からマグマが噴き出るようにしてください。
> Optionから火山の形態を３種類（盾状・成層・鐘状）から選べるようにしてください。

### 1.2 「地面を上げれば山ができる」は市街地では成立しない

**本プロジェクト 10 個目の前提の取り違えであり、実装前に捕まえた 2 個目である。**

`TerrainModify.UpdateAreaImplementation` は毎回 `Managers_TerrainUpdated` を呼ぶ。
`NetSegment.TerrainUpdated` は（`NetInfo.m_flattenTerrain` の道路について）`Heights.PrimaryLevel` を渡し、
これは `primaryMin = primaryMax = その道路の y` を設定する。適用段はセルをその値ちょうどに強制する。
`Building.TerrainUpdated` も `SecondaryLevel`（IL_05F5）で建物自身の y に固定し、1:4 の裾を付ける。

**これは更新のたびにゼロからやり直されるので、書き続けても勝てない。**

したがって市街地で `RawHeights` を上げると:

- 道路のところは**平らな溝**
- 建物のところは**窪み**
- 木・小物・歩道・`m_flattenTerrain == false` の建物は一緒に持ち上がる
- **崩壊も切り離しも起きない**（`NetAI.AfterTerrainUpdate` は `ret` 1 命令）

### 1.3 利用者の判断

| 論点 | 決定 |
|---|---|
| 市街地の扱い | **A. 隆起に巻き込むものを破壊する。** 山が育つ過程で範囲内の道路と建物を段階的に破壊してから地面を上げる |
| 地形の復元 | **B. 不可逆でよい。** 元の高さを保存する機能は作らない |

破壊は「火山が街を飲み込む」という災害として正しい挙動であり、
副作用としてバニラの地形固定も同時に外れる（道路と建物が無くなればセルを引き戻す者がいない）。

**ただしプレイヤーの資産を MOD が壊すことになるので、配置時に確認ダイアログを出す**
（破壊される道路と建物の概数を先に見せる）。

### 1.4 その他の確定事項

| 事実 | 判定 |
|---|---|
| 溶岩のマテリアル・シェーダ・エフェクト・プレハブ | **ABSENT。** DLL の文字列ヒープにヒット 0。全部自作 |
| 地形の最大勾配クランプ・浸食・平滑化 | **ABSENT。** 急峻な円錐は潰されない。3 形態は素直に作れる |
| `MakeCrater(pos, R, depth, raiseEdges)` の負の深さ | **クランプされない。** `MakeCrater(pos, R, −H, false)` がそのまま盾状火山になる |
| バニラの `DisasterInfo` プレハブ | **11 個すべて `m_requiredExpansion = NaturalDisasters`。** ND 無しでは `PrefabCount() == 0` で `FindDisasterInfo<T>()` は常に null |
| ND 無しでの `DetectDisaster` | **NRE になる。** `m_DisasterWrapper` は DLC 所有時しか生成されず、この 1 箇所だけ null 検査が無い |
| `TreeManager.BurnTree` | **ND 必須**（`SupportsExpansion` ゲート）。`BurnGround` / `BurnBuilding` は ND 不要 |
| 地形改変のスレッド | **sim スレッドが正解。** `SimulationManager.SimulationStep` 全体が `Begin/EndUpdateArea` で囲まれている |

---

## 2. 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| 災害スロットに載せるか | **載せない。** ⑤は自前の状態機械で動かす | ND 無しでは土台になるプレハブが 1 つも無く、自前プレハブの実行時登録はセーブにプレハブ名を焼き込む（MOD を外すとロード時にエラー）。得られるのは通知とカメラ追従だけで、代償が大きすぎる |
| 隆起の実行スレッド | **sim スレッドのみ。** `Begin/EndUpdateArea` は**呼ばない** | 既に入れ子なので自前の `End` は握り潰される。メインから呼ぶと `m_modifyingLevel` を奪い合い、sim の書き込み中にフラッシュしうる |
| 1 回の更新範囲 | **10000 セル未満に収め、128×128 を超えない** | 10000 超は強制フラッシュ、128×128 超は**タイル分割されず切り捨てられる** |
| 1 tick あたりの隆起量 | **必ず 1/64 m 以上** | `RawHeights` は `ushort`（`raw/64` m）。それ未満の増分は**丸めで消える**（無言） |
| 溶岩の経路 | `SampleDetailHeight(Vector3, out slopeX, out slopeZ)` で最急降下 | 地形にコライダーが無いので `Physics.Raycast` は常に失敗する（既知） |
| 溶岩の見た目 | **自作メッシュ＋自作マテリアル** | 既製品ゼロ。CS のマテリアルを借りると自前 `MeshRenderer` では不可視（③で確認済み） |
| 溶岩に沿う火災 | `DisasterHelpers.BurnGround` ＋ `BuildingAI.BurnBuilding` | 両方 ND 不要。樹木の着火だけ ND 分岐 |
| 3 形態 | 高さプロファイルの違いだけ | 盾状は `MakeCrater` の負の深さ、成層・鐘状は `RawHeights` を自前で書く。**鐘状の半径は 250 m 以上**（グリッド 16 m の解像度） |

---

## 3. 位相

```
配置（メイン）  クリック → 影響範囲の道路・建物を数えて確認ダイアログ
   ↓
準備（sim）     範囲内の道路と建物を段階的に破壊する。地面はまだ動かさない
   ↓
隆起（sim）     形態に応じた高さプロファイルへ、1 tick ずつ近づける
   ↓
噴火（sim+main） 山頂の火口からの噴出。プルームは m_mediumExplosion を借り、
                発光する噴出物は自作
   ↓
溶岩（sim+main） 最急降下で流れ、通った先で BurnGround / BurnBuilding
   ↓
終息            溶岩が冷える。地形はそのまま残る（不可逆）
```

**準備を隆起より先に置くのが本設計の要**である。逆にすると §1.2 の溝と窪みができる。

---

## 4. 各要素

### 4.1 配置と確認

- ③の `FireWhirlPlacementTool` / `ToolRegistration` を流用する。
  **`SetTool<T>()` は `ToolController` に事前登録しないと空振りする**（既知）
- バニラの `TerrainManager.RayCast` が public なので地点取得はこれを使う
- クリック後、**破壊される道路と建物の概数を出して確認を取る**。
  「A. 隆起に巻き込むものを破壊する」は利用者の判断だが、
  個別の実行は毎回プレイヤーの意思で行われるべきである

### 4.2 準備（破壊）

- 範囲内の建物: `BuildingAI.CollapseBuilding(..., demolish:true, ...)`。
  **`demolish:true` を使う**（④の風害は `false` だった）。跡地を残すと地形固定が続く
- 範囲内の道路: 破壊経路を IL で確定させてから書く（`NetManager.ReleaseSegment` 系）。
  **未確定なので着手前に調べる**
- 一度に全部壊さない。山が育つのと同じ速さで外側へ広げる
- **防災施設は `demolish:false` を拒否するが `true` は通るか未確認。** 調べること

### 4.3 隆起

3 形態の高さプロファイル（中心からの距離 `r`、半径 `R`、最終高 `H`）:

| 形態 | プロファイル | 特徴 |
|---|---|---|
| 盾状 | 緩い凸（`H·(1−r/R)^0.6` 相当） | 広く低い。`MakeCrater(pos, R, −H, false)` でほぼ得られる |
| 成層 | 直線に近い円錐（`H·(1−r/R)`） | 急峻。自前で書く |
| 鐘状 | 急峻で小さい（`H·(1−(r/R)^2)^2` 相当） | **半径 250 m 以上にする**。それ未満はグリッド 16 m で階段になる |

- 山頂に火口の窪みを付ける（最後に `MakeCrater` を正の深さで 1 回）
- **`MakeCrater` は呼ぶたびに強制フラッシュする。** 毎 tick 呼ばない
- 更新範囲を 10000 セル未満・128×128 以下に分割する。分割は⑤が明示的に行う
- **`m_blockHeights`（建設可能判定と水シミュが見る配列）は 64 sim フレームあたり
  上へ 2 m・下へ 8 m でしか追従しない。** 見た目の地形と数ゲーム時間ずれる。
  これは不具合ではないので、診断に 1 行出して説明する

### 4.4 噴火

- プルームは `DisasterProperties.m_mediumExplosion` を借りられる
  （**`EffectInfo` を借りるのはマテリアルを借りるのとは別**。`RenderEffect` の経路を使う）
- 発光する噴出物は自作。`DispatchEffect` の magnitude は**粒子密度であってサイズではない**（既知）
- 音は `AudioInfo.m_clip` を借りられる（既知）

### 4.5 溶岩と、それに沿う火災

```
1. 火口から数本の流れを出す（本数と初期方向は DeterministicRandom）
2. 各流れは SampleDetailHeight(pos, out slopeX, out slopeZ) で最急降下方向へ進む
3. 進んだ先で:
   - DisasterHelpers.BurnGround(pos, radius, intensity)   ← 地面を焦がす（ND 不要）
   - 範囲内の建物に BuildingAI.BurnBuilding(...)          ← ND 不要
   - 樹木は ND がある場合のみ TreeManager.BurnTree
4. 流れの軌跡をメッシュとして描く（自作マテリアル）
```

- **`Building.m_fireIntensity` を直接書かない**（既知の禁則）
- 溶岩は水に触れたら止める（`TerrainManager.HasWater`、sim スレッド専用）
- 流れの本数と長さに上限を置く。1 tick あたりの処理量を明示的に区切る

---

## 5. 構成

```
src/DisasterPlus/
  Core/Volcano/                    エンジン非依存・全部ユニットテスト対象
    VolcanoShape.cs                  3 形態の高さプロファイル（r, R, H → 高さ）
    UpliftSchedule.cs                経過 → 各セルの目標高（1/64 m 未満を出さない）
    LavaPath.cs                      最急降下の 1 歩（勾配 → 次の位置）。純関数
    ClearanceEstimate.cs             半径 → 破壊対象のセル数（確認ダイアログ用）
    TileSplit.cs                     矩形 → 10000 セル未満・128×128 以下の分割
  Game/Volcano/
    VolcanoFeature.cs                IDisasterFeature 実装
    VolcanoState.cs                  位相機械（sim）
    VolcanoClearing.cs               道路と建物の段階的破壊（sim）
    VolcanoUplift.cs                 RawHeights の書き込みと UpdateArea（sim）
    VolcanoEruption.cs               噴出エフェクトと音
    VolcanoLava.cs                   流れの前進・着火（sim）
    VolcanoLavaFx.cs                 溶岩の描画（メイン）
    VolcanoHub.cs                    snapshot-then-render
    VolcanoPanel.cs                  表示（④の TyphoonRows を流用）
  Game/UI/
    VolcanoPlacementTool.cs          ③のツールを流用
    VolcanoPanelButton.cs            FreeSlotFinder
```

---

## 6. 診断

| 検証項目 | 失敗時の影響 |
|---|---|
| `TerrainManager.RawHeights` が `ushort[]` で書けるか | ⑤全体が成立しない |
| `TerrainModify.UpdateArea` のシグネチャ | 同上 |
| `DisasterHelpers.MakeCrater` / `BurnGround` のシグネチャ | 火口と地面の焦げが出ない |
| `SampleDetailHeight` の 3 引数版（勾配つき）の存在 | 溶岩が流れない |
| `TerrainManager.RayCast` | クリック配置ができない |
| 道路の破壊経路（**未確定**） | 準備段が成立せず、§1.2 の溝ができる |
| `SupportsExpansion(NaturalDisasters)` | 樹木の着火だけ分岐する |
| `Shader.Find` が非 null か | 溶岩が不可視になる |

---

## 7. 出してよい断定の範囲

1. **地形変更は不可逆であると、配置前に明示する。** バニラにアンドゥは無く、セーブに焼き付く。
   利用者は「不可逆でよい」と判断したが、**それはプレイヤーに黙っていてよいという意味ではない**
2. **破壊される道路と建物の概数を先に見せる。** 概数であることも明示する
3. **建設可能判定と水位が数ゲーム時間遅れることを説明する**（§4.3、`m_blockHeights` の追従速度）
4. **⑤の数値は原則すべて本 MOD のものである。**④と同じ扱いで、見出しで一度言う

---

## 8. 実装順

1. `Core/Volcano` の純関数群＋テスト（形態・スケジュール・最急降下・分割）
2. 骨格・配置ツール・確認ダイアログ・前提検証
3. 準備（道路と建物の段階的破壊）— **道路の破壊経路を IL で確定させてから**
4. 隆起（`RawHeights` と `UpdateArea` の分割書き込み）
5. 噴火（エフェクトと音）
6. 溶岩の前進と着火
7. 溶岩の描画（自作メッシュ・自作マテリアル）— **単独。他から依存させない**

---

## 付録: 着手前に IL で確定させること

| 対象 | なぜ要るか | どの段 |
|---|---|---|
| **道路の破壊経路** | 準備段の中核。未確定のまま書くと §1.2 の溝ができる | 3 |
| 防災施設が `demolish:true` を通すか | 準備段の取りこぼし | 3 |
| `SampleDetailHeight` の 3 引数版 | 溶岩の全部がこれに乗る | 6 |
| `DisasterProperties.m_mediumExplosion` の借り方 | プルーム | 5 |
| `BurnGround` のグリッドと単位 | 焦げ跡の範囲 | 6 |

**「たぶんこうだろう」で書かない。** 本プロジェクトはこれまでに 10 個の前提の取り違えを出しており、
実装前に捕まえられたのは 2 個だけである。そのうち 1 個（§1.2）は⑤の設計そのものを書き換えた。
