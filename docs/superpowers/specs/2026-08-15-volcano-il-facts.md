# Disaster + — ⑤火山 IL 実測ファクト

- 日付: 2026-08-15
- 対象: Cities: Skylines 1 / `Assembly-CSharp.dll`（Steam 版、`Cities_Data/Managed`）＋ `ColossalManaged.dll`
- 目的: ⑤火山の設計に着手する前に、依頼文の各要求が**バニラの実装上どこまで成立するか**を IL で確定させる
- 状態: **調査のみ**。`src/` / `tests/` / `Locales/` は一切変更していない
- 前提資料（**既出の事実は再導出せず引用する**）:
  - `2026-08-11-…-firewhirl-design.md` 付録 A ＋ §4.8（`static Mesh[]` と fake-null）/ §4.9（CS のマテリアルを借りると自前 `MeshRenderer` では見えない、`DispatchEffect` の magnitude は粒子密度）/ §4.12（**⑤火山は Natural Disasters DLC 不要**）
  - `2026-08-14-earthquake-il-facts.md` §A-1（`SelfTrigger` の罠）/ §D-1（実行時地形改変）/ §D-2（`WaterSimulation`）/ §E-1（`CreateDisaster` の戻り値・256 上限）/ §E-2（NDR のパッチ面）
  - `2026-08-15-typhoon-il-facts.md` §C-1・§C-2（雲・渦の既製ビジュアルは無い、`DisasterInfo.m_effect` は存在しない）/ §D-3（波の寿命と外周リング制限）/ §F-2（`CollapseBuilding` を拒否する AI）
  - `2026-08-12-forecast-design.md` §1.3（ハザードマップの意味）

判定記号: **CONFIRMED**（IL を読んだ）/ **PARTIAL**（読んだが確定しきれない。何を見れば決まるかを書く）/ **ABSENT**（存在しない）

---

## 0. 要約ファクト表

| # | 問い | 判定 | 一行の答え |
|---|---|---|---|
| A1 | 更新面積のコスト・上限 | **CONFIRMED** | `UpdateArea` は**面積 > 10000 セルで即フラッシュ**。`m_tempHeights`(513²) の制約で**128×128 raw セル＝2048 m 角を超えた分は無言で切り捨てられる（タイル分割されない）**。1 フラッシュは detail 解像度（4 m）で `SmoothSample` を 5 回/セル＋7 マネージャの `TerrainUpdated` 全走査 |
| A2 | 地形を戻す仕組み | **CONFIRMED（ある。しかも致命的）** | **道路・建物が毎フラッシュ地形を押し戻す。** `NetSegment.TerrainUpdated` は `NetInfo.m_flattenTerrain` のとき `Heights.PrimaryLevel` を掛け、`primaryMin = primaryMax = 道路の高さ` になる → **道路の下の地形は道路の y に強制固定される**。加えて `m_blockHeights` は目標へ **ゲームモードで 2 m / 64 sim フレーム**しか動かない |
| A3 | 建物・道路・木・ゾーンはどうなるか | **CONFIRMED** | **自動倒壊も自動デタッチも一切無い。** 木・プロップ・`m_flattenTerrain == false` の建物・歩道は地面に**追随して上がる**。**道路／線路／送電線／水道管は `NetAI.AfterTerrainUpdate` が `ret` 1 命令で、動きも壊れもしない** |
| A4 | 水は地形に追随するか | **CONFIRMED** | 追随する。ただし `WaterSimulation.m_heightBuffer` は **`TerrainManager.m_blockHeights` そのもの**（`Awake` で参照を共有）なので、A2 の **2 m / 64 sim フレーム**の遅延を丸ごと被る |
| B5 | 溶岩の既製ビジュアル | **ABSENT（溶岩は無い）／ 火は借りられる（CONFIRMED）** | `lava/magma/molten/volcan` は DLL の文字列ヒープにも**ゼロ件**。ただし **`BuildingProperties.m_fireEffect` / `TreeProperties.m_fireEffect`（`EffectInfo`）を任意座標で `RenderEffect` できる**。溶岩の「光る面」は自作するしかない |
| B6 | 地形高さのサンプリング | **CONFIRMED** | 生値は `RawHeights[z*1081+x]`（1 読み、最安、**MOD 自身が書く配列**）。見た目に一致するのは `SampleDetailHeight(Vector3)`（4 m、ロック済みタイル内のみ）。`Physics.Raycast` は既知のとおり不可だが **`TerrainManager.RayCast(Segment3, out Vector3)` は public で存在する** |
| B7 | 溶岩の跡に火をつける | **CONFIRMED（1 つ ABSENT）** | `BuildingAI.BurnBuilding` OK（DLC 不要）。`DisasterHelpers.BurnGround` OK（512² / 33.75 m / 値 0-255 飽和 / DLC 不要）。**`TreeManager.BurnTree` は ND DLC が無いと常に false**。道路を「燃やす」API は **ABSENT**（`CollapseSegment` しかない） |
| C8 | `MakeCrater` の形 | **CONFIRMED** | `raiseEdges:true` は「中心 −0.7×depth・0.75R に +0.3×depth の縁」。**depth に負値を渡せば山になる**（`raiseEdges:false` + 負 depth = `1−(d/0.837R)⁴` 型の**楯状火山そのもの**）。加算合成なので重ね掛け可 |
| C9 | 傾斜の上限・平滑化・クランプ | **CONFIRMED（最大勾配は無い）** | 勾配クランプも侵食も平滑化パスも **ABSENT**。`SmoothSample` は単調制限付き cubic の**補間**であって平滑化ではない。制約は「16 m 格子」と「1/64 m 量子」だけ。**急峻な円錐は潰れない** |
| C10 | 高さの範囲・余裕 | **CONFIRMED** | `TERRAIN_HEIGHT = 1024`、`TERRAIN_LEVEL = 60`、`DEFAULT_SEA_LEVEL = 40`。天井は `raw = 65535` → **1023.98 m**。海面 40 m の平地なら**約 984 m の余裕** |
| D11 | 本物の `DisasterData` として動かせるか | **PARTIAL（推奨は「載せない」）** | `CreateDisaster` に DLC ゲートは無く、`DisasterAI` は抽象でもなく、`PrefabCollection.InitializePrefabs` も public。だが **ND 無し環境に `DisasterInfo` プレハブが存在するかは DLL から判定できない**。実機で `PrefabCount()` を見るまで設計に入れないこと |
| D12 | sim スレッド安全性・バッチ | **CONFIRMED** | `SimulationManager.SimulationStep` が先頭で `BeginUpdateArea`、末尾で `EndUpdateArea` を呼ぶ。MOD が sim tick 内で書くと**外側のバッチに自動的に載る**。ただし面積 > 10000 セルの単発要求は入れ子を無視して即フラッシュする |
| E13 | 可逆性 | **CONFIRMED（自前で持つしかない）** | `BackupHeights` / `UndoBuffer` は **`TerrainTool` / `DistrictTool` 専有**。MOD が使うと地形ツールを開いた瞬間に壊れる。自前保存の実費は**半径 1 km で 31 KB、2 km で 123 KB**（ushort × 影響セル） |
| E14 | NDR との衝突 | **CONFIRMED（衝突しない）** | ⑤が使う `MakeCrater` / `TerrainModify` / `TerrainManager` / `BurnGround` / `BurnBuilding` は NDR のパッチ面（`DisasterHelpers.DestroyBuildings` / `DestroyNetSegments`）を**一切通らない** |

| F15 | 道路セグメントのグリッド | **CONFIRMED（建物と同じ形）** | `NetManager.m_segmentGrid` は `ushort[72900]`（= 270²）。セル 64 m・オフセット +135・`[0,269]` クランプ・`index = z*270 + x` で、**建物グリッドと完全に同じ寸法**。鎖は `NetSegment.m_nextGridSegment`（`ushort`）。ただし**セルを決める位置は両端ノードの中点**であって `m_middlePosition` ではない（→ §F-15） |
| G16 | 準備段の破壊経路 | **CONFIRMED（成立する）** | `demolish:true` は `PlayerNetAI.CollapseSegment` へ集約され、**`NetManager.ReleaseSegment(id, keepNodes:false)` で本当に解放する**（孤児ノードも同時に解放される）。**`Collapsed` を立てただけの道路と建物は地形を固定し続ける**（`TerrainUpdated` は `Collapsed` を見ない）ので、⑤は `demolish:true` でなければならない。**`demolish:false` を断る 5 つの AI は `demolish:true` を通す**（→ §G-16） |

**今回いちばん危なかった思い込み（＝10 個目の候補）は A2。** 「地形を書けば地形が変わる」は、**道路と建物の下では成り立たない**。詳細は §A-2 と「設計への含意」。

---

## A. 時間をかけた地形変化

### A-1. `UpdateArea` のコストとフラッシュ条件 — CONFIRMED

`TerrainModify` は**フィールドを 1 つも持たない**（`Dump-Type` 実測）。状態はすべて `TerrainManager` 側にある。

```
TerrainModify（public static のみ）
  UpdateArea(Single minX, Single minZ, Single maxX, Single maxZ, Boolean heights, Boolean surface, Boolean zones)
  UpdateArea(Int32  minX, Int32  minZ, Int32  maxX, Int32  maxZ, Boolean heights, Boolean surface, Boolean zones)
  RefreshAllModifications()
  BeginUpdateArea() / EndUpdateArea()
  UpdateAreaImplementation()          // private
  ApplyQuad(...) ×4 / SolveDown / GetAdjustedLength / SqrOffset
```

**`RefreshAllModifications` は `UpdateAreaImplementation` を呼ぶだけの別名である。**

```
TerrainModify.RefreshAllModifications():
IL_0000:  call  TerrainModify::UpdateAreaImplementation
IL_0005:  ret
```

`BeginUpdateArea` / `EndUpdateArea` は `TerrainManager.m_modifyingLevel`（**public Int32**）の単なる増減カウンタ:

```
BeginUpdateArea:
IL_0008  ldfld TerrainManager::m_modifyingLevel ; ldc.i4.1 ; add ; stfld

EndUpdateArea:
IL_0008  m_modifyingLevel-- ; loc1 = 新値
IL_0017  if (loc1 != 0) return                                    // 入れ子の内側では何もしない
IL_001C  if (m_modifyingHeights || m_modifyingSurface || m_modifyingZones)
IL_003D      UpdateAreaImplementation()
```

`UpdateArea(int,int,int,int,bool,bool,bool)` の**フラッシュ条件**（IL_0000–03B5）:

```
tm      = TerrainManager.instance
hm/sm/zm= tm.m_heightsModified / m_surfaceModified / m_zonesModified   (TerrainArea, すべて public)
newArea = (maxX - minX + 1) * (maxZ - minZ + 1)                        // loc4、raw セル数

if (tm.m_modifyingLevel != 0) {                                        // IL_0028
    if (tm.m_modifyingHeights && heights) {                            // IL_0033
        pending = (hm.m_maxX - hm.m_minX + 1) * (hm.m_maxZ - hm.m_minZ + 1)
        merged  = (Max(maxX,hm.m_maxX) - Min(minX,hm.m_minX) + 1)
                * (Max(maxZ,hm.m_maxZ) - Min(minZ,hm.m_minZ) + 1)
        if (merged > ((pending + newArea) << 1) || merged > 10000)     // IL_00A8 / IL_00AF
            UpdateAreaImplementation();                                // ★ 途中フラッシュ
    }
    …surface / zones についても同一の判定（IL_00BE–01D4、しきい値も同じ 10000）
}
tm.m_modifyingHeights |= heights ; …Surface |= surface ; …Zones |= zones

if (heights) {
    hm.AddArea(minX, minZ, maxX, maxZ)
    w = ((hm.m_maxX - hm.m_minX) << 2) + 1 ; h = ((hm.m_maxZ - hm.m_minZ) << 2) + 1
    if (w * h > tm.m_tempHeights.Length) {                             // IL_024D
        hm.m_maxX = Min(hm.m_maxX, hm.m_minX + 120 + 8)                // IL_026C  ★ 128 セルに切り詰め
        hm.m_maxZ = Min(hm.m_maxZ, hm.m_minZ + 120 + 8)
    }
}
…surface（m_tempSurface）/ zones（m_tempZones）も同型

if (tm.m_modifyingLevel == 0 || newArea > 10000)                       // IL_0399–03AB
    UpdateAreaImplementation();                                        // ★ 最終フラッシュ
```

`TerrainManager.Awake` のバッファ実寸（IL_0012–01BE、すべて `newarr`）:

| 配列 | 要素数 | 意味 |
|---|---|---|
| `m_rawHeights` / `m_rawHeights2` / `m_finalHeights` / `m_blockHeights` / `m_blockHeights2` / `m_backupHeights` | **1168561 = 1081²** | UInt16、2.34 MB ずつ |
| `m_undoBuffer` | 3505684 ≒ 1081² × 3 | UInt16、7.0 MB |
| **`m_tempHeights`** | **263169 = 513²** | `HeightModification`（float ×8 = 32 B）＝ **8.4 MB** |
| `m_tempSurface` / `m_tempZones` | 263169 | |
| `m_detailHeights` / `m_detailHeights2` | 231361（=481²）× `m_detailPatchCount` | 4 m 解像度、**解放済みタイルの分だけ**（§B-6） |
| `m_modifiedBlockMinX` / `MaxX` | 1081 Int16 | |

→ **`513² = (128×4)+1` なので、`m_tempHeights` は 128×128 raw セル分ちょうど。** これが `120 + 8` クランプの正体で、
**1 回のフラッシュが扱える最大範囲は 128×128 raw セル = 2048 m × 2048 m** である。

**1 フラッシュのコスト**（`UpdateAreaImplementation`、IL 長 **4411 バイト**）:

1. 対象矩形を **detail 解像度（1 raw セルあたり 4×4）** で走査し、各 detail セルにつき
   `SmoothSample` を **5 回**（行方向 4 本 ＋ 列方向 1 本、IL_0403 / 0414 / 0425 / 0436 / 045A）。
   128×128 raw セル = 513² = 263169 detail セル → **約 130 万回の cubic 評価**。
2. `TerrainManager.Managers_TerrainUpdated(heightArea, surfaceArea, zoneArea)`（IL_08D6）。
   `ITerrainManager` の実装は **`BuildingManager` / `NetManager` / `TreeManager` / `PropManager` /
   `ZoneManager` / `WeatherManager` / `ToolManager` の 7 つ**（リフレクション実測）。
   それぞれが矩形内の全オブジェクトを走査して `ApplyQuad` する（§A-2）。
3. `m_finalHeights` / `m_blockHeightTargets` / `m_detailHeights` / `m_detailHeights2` へ書き戻し。
4. パッチごとに `Monitor.TryEnter(TerrainPatch.m_modifiedLock)`（IL_100F）→ `m_tmpDetailIndex` を更新 → `Monitor.Exit`。
5. `Managers_AfterTerrainUpdate`（IL_10CC）→ `TerrainWrapper.OnAfterHeightsModified`（IL_110E）。
6. 最後に `m_modifyingHeights/Surface/Zones = false`、3 つの `TerrainArea.Reset()`。

7. パッチごとの分岐が最大のコストレバー: `if (TerrainPatch.m_simDetailIndex == 0) → detail 分岐を丸ごとスキップ`（IL_0167）。
   **未購入タイルの上では detail 解像度のループが走らない。** 逆に購入済みタイル上では
   `HeightModification`（32 B）を 513² 分書く。

> **設計上の結論（3 つ、いずれも守らないと壊れる）。**
> 1. **`UpdateArea` の矩形は 128×128 raw セル（2048 m 角）を超えてはいけない。**
>    超えると**タイル分割されず、無言で切り詰められる**（IL_026C の `Min(m_maxX, m_minX + 128)`）。
>    はみ出した部分は**更新されないまま残る**。分割は呼び出し側の責任
>    （`TerrainManager.UpdateData` は 9×9 パッチ × 120 セルで自分で分割している）。
> 2. **10000 セル（=100×100 セル = 1600 m 角）を超える単発要求は `Begin/EndUpdateArea` を無視して即フラッシュする。**
>    「大きな山を一発で作る」は必ず 1 フレーム落ちる。段階的隆起は 1 tick あたりの矩形を小さく保つこと。
> 3. **`DisasterHelpers.MakeCrater` は先頭で `RefreshAllModifications()` を呼ぶ**（＝ `UpdateAreaImplementation` そのもの）。
>    1 tick に k 回呼べば**フラッシュが k+1 回**走る。⑤が `MakeCrater` で山を組むなら
>    「1 tick に 1 回だけ」を守るか、`RawHeights` を自前で書いて `UpdateArea` を 1 回にまとめること。

### A-2. 地形を押し戻す仕組み — **CONFIRMED。存在する。ここが 10 個目**

`UpdateAreaImplementation` の高さパイプライン（IL_01CC–0B19）。ローカルの対応は IL_0043–005C で確定:
`loc9 = RawHeights` / `loc10 = RawHeights2` / `loc11 = FinalHeights` / `loc12 = BlockHeightTargets`。

```
[1] 各 detail セルの HeightModification を初期化（IL_01EE–023B）
      m_divider = 1        m_target       = <未設定>
      m_primaryMin  = 0    m_primaryMax   = 1024
      m_secondaryMin= 0    m_secondaryMax = 1024
      m_blockHeight = 0    m_digHeight    = 1024

[2] m_target を m_rawHeights から作る（IL_03EE–046F）
      4 本の SmoothSample（列）→ 1 本の SmoothSample（行）→ × 0.015625   // raw → メートル
      hm.m_target = その値                                                // ★ MOD が書いた値はここに入る

[3] Managers_TerrainUpdated（IL_08D6）
      7 マネージャ →（下記）→ TerrainModify.ApplyQuad が m_tempHeights を書き換える

[4] 反映（IL_0A3E–0B19）
      v = hm.m_target / hm.m_divider
      v = Min(Max(v, hm.m_secondaryMin), hm.m_secondaryMax)
      v = Min(Max(v, hm.m_primaryMin),   hm.m_primaryMax)
      FinalHeights[z*1081+x]       = (ushort)Clamp((int)(v * 65536 / 1024), 0, 65535)   // IL_0ABF ldloc.s 11
      v2 = Min(Min(Max(v, hm.m_blockHeight), hm.m_digHeight), hm.m_primaryMax)
      BlockHeightTargets[z*1081+x] = (ushort)Clamp((int)(v2 * 65536 / 1024), 0, 65535)  // IL_0B13 ldloc.s 12
      （detail 解像度側にも同じ 2 段を m_detailHeights / m_detailHeights2 へ、IL_0BCD–0CFF）
```

`TerrainModify.ApplyQuad(Vector3 a,b,c,d, Edges, Heights, Single[] heightArray)` が
`m_tempHeights` に対して行うこと（IL_06E9–089E、`h = SolveDown(...)` が quad 面のその点の高さ）:

```
Heights.PrimaryLevel   (1)  : m_primaryMin   = Max(m_primaryMin,   h)      IL_06F6–0702
                              m_primaryMax   = Min(m_primaryMax,   h)      IL_070B–0717   ★ 両方 h になる
                              m_target      += h * 4 ; m_divider += 4      IL_071F–0740
Heights.SecondaryLevel (2)  : m_secondaryMin = Max(…, h) ; m_secondaryMax = Min(…, h)     IL_0752–0773
                              m_target      += h * 4 ; m_divider += 4      IL_077B–079C
Heights.PrimaryMax     (4)  : m_primaryMax   = Min(m_primaryMax,   h)      IL_07AE–07BA
Heights.BlockHeight    (8)  : m_blockHeight  = Max(m_blockHeight,  h)      IL_07CC–07D8
Heights.SecondaryMin  (16)  : m_secondaryMin = Max(…, h)                   IL_0806–0816
Heights.DigHeight     (32)  : m_digHeight    = Min(m_digHeight,   h)       IL_07EB–07F7
Heights.RawHeight     (64)  : (x&3)==0 && (z&3)==0 のとき
                              RawHeights2[z*1081+x] = Clamp(h*64, 0, 65535) IL_0872–089E
Heights.SecondaryMax (128)  : m_secondaryMax = Min(…, h)                   IL_082C–0838
```

`TerrainModify+Heights` の実値（`Enum.GetNames` 実測）:
`None=0 PrimaryLevel=1 SecondaryLevel=2 PrimaryMax=4 BlockHeight=8 SecondaryMin=16 DigHeight=32 RawHeight=64 SecondaryMax=128 Any=255`。
`TerrainModify+Edges`: `None=0 AB=1 BC=2 CD=4 DA=8 All=15 Expand=16`。

> **`PrimaryLevel` は `primaryMin` と `primaryMax` の両方を `h` にする。**
> 反映段の `Max(v, primaryMin)` → `Min(v, primaryMax)` は、その 2 つが等しければ **`v = h` に強制される**。
> `m_target` に何を入れていようが（＝MOD が `RawHeights` に何を書いていようが）**完全に上書きされる。**

**誰がそれを掛けるか — `ApplyQuad` / `UpdateArea` の全呼び出し元（全アセンブリのメソッド本体をトークン走査）:**

```
ApplyQuad を呼ぶ:  Building::TerrainUpdated (20 箇所) / NetSegment::TerrainUpdated (2) /
                   NetNode::TerrainUpdated / NetNode::UpdateJunctionTerrain (5) /
                   NetNode::UpdateBendTerrain / NetNode::UpdateEndTerrain (2) /
                   PropInstance::TerrainUpdated / TreeInstance::TerrainUpdated /
                   ZoneBlock::ZonesUpdated / TerrainModify::ApplyQuad（内部）
UpdateArea を呼ぶ: Building::UpdateBuilding / Building::UpdateTerrain / NetNode::UpdateNode /
                   NetSegment::UpdateSegment / PropInstance::UpdateProp / TreeInstance::UpdateTree /
                   ZoneBlock::UpdateBlock / DisasterHelpers::MakeCrater / DisasterHelpers::MakeCrack /
                   TerrainTool::ApplyBrush / TerrainTool::ApplyUndo / DistrictTool::ApplyTerrainBrush /
                   SurfaceTool::ApplyDrawing / TerrainManager::SetHeightMap{,8,16} /
                   TerrainManager::SetRawHeightMap / TerrainManager::SetDetailedPatch /
                   TerrainManager::UpdateData / ToolManager::LateUpdateData / TerrainWrapper::SetHeights /
                   DecorationPropertiesPanel::RefreshSize / 各種コルーチン
BeginUpdateArea / EndUpdateArea を呼ぶ: **SimulationManager::SimulationStep のみ**
```

**道路のマスク構築 — `NetSegment.TerrainUpdated`（IL_0262–029D）:**

```
loc28 = 0
if (info.m_flattenTerrain)               loc28 |= 1   (PrimaryLevel)    IL_0266–0273
if (netAI.GetSegmentLowerTerrain(...))   loc28 |= 4   (PrimaryMax)      IL_0276–0280
if (info.m_blockWater)                   loc28 |= 8   (BlockHeight)     IL_0284–0291
if (netAI.RaiseTerrain(...))             loc28  = 16  (SecondaryMin)    IL_0294–029D   ★ OR ではなく代入
…
IL_0804  TerrainModify.ApplyQuad(a, b, c, d, edges: loc64, heights: loc28, surface: loc63)
IL_096F  TerrainModify.ApplyQuad(a, b, c, d, edges: loc69, heights: 16 /*SecondaryMin*/, surface: 0)
```

→ **`m_flattenTerrain == true` の道路（バニラの一般道はこれ）の下では、地形は毎フラッシュ道路の高さに戻される。**
`RaiseTerrain` を返す AI（護岸・堤防系）だけが `SecondaryMin` になり、「道路より下げられないが上げるのは自由」になる。

**建物のマスク構築 — `Building.TerrainUpdated`（static、IL 長 7224、`ApplyQuad` 20 箇所）— CONFIRMED**

グリッド走査は 270²（64 m セル、オフセット +135）、半径 `Min(72, (width+length)*4)`、
`m_flags & 524291 == 1`（`Created` かつ `Deleted`/`Untouchable` でない）だけを対象にする。
支配的な 1 本:

```
IL_0559  callvirt TerrainManager::SampleRawHeightSmooth      // 生の地形（＝ MOD が書いた値）
IL_056C  ldc.r4 0.25                                          // 1:4 の傾斜許容
IL_057E  call Mathf::Clamp                                    // Clamp(rawH, cornerRef.y - 0.25d, cornerRef.y + 0.25d)
IL_0583  stfld Vector3::y
IL_05F5  call TerrainModify::ApplyQuad(a,b,c,d, edges, heights: 2 /*SecondaryLevel*/, surface: 0)
```

`cornerRef` は `Building.CalculateMeshPosition(...)` ＋ `BuildingInfoGen.m_min/m_max` から作られ、
**その `y` は建物自身が保持している高さ**である。`SecondaryLevel` は `secondaryMin` と `secondaryMax` の
両方に同じ `h` を入れるので（§A-2 の表）、**そのセルはハードピンされる。**
足元の pad には `GetTerrainLimits` 由来の `SecondaryMin(16)`（IL_0CE0）/ `SecondaryMax(128)`（IL_0D27）も掛かるが、
`BuildingAI.GetTerrainLimits` の既定は `min = meshPos.y - 255` / `max = 1000000` なので実質無害。

→ **建物のフットプリント上では、ゲームが描画・レイキャスト・衝突・浸水判定に使う地形が
建物自身の y にハードピンされ、外側へ 1:4 の勾配で素の地形に戻る。`m_rawHeights` はこの処理で書き換わらない。**

**`Heights.RawHeight(64)` は死にコード — CONFIRMED。** `ApplyQuad` は `RawHeight` フラグのとき
`RawHeights2[z*1081+x]` を直接書く（IL_083F–089E）が、
**アセンブリ内の `ApplyQuad` 呼び出し 10 箇所すべての `Heights` 実引数を検査してもビット 6 を立てるものは 1 つも無い。**
したがって `m_rawHeights2` は常に `m_rawHeights` のコピーである（毎 `UpdateAreaImplementation` の
IL_02E7 / IL_0838 で `rawHeights2[i] = rawHeights[i]`）。

**もう 1 つの押し戻し — `m_blockHeights` の追随速度（`TerrainManager.SimulationStepImpl`、IL_0000–0150）:**

```
m_waterSimulation.SimulationStep(subStep)
if (subStep == 0) return
k      = SimulationManager.m_currentFrameIndex & 63                     // IL_001C
zStart = (k * 1081) >> 6 ; zEnd = (((k+1) * 1081) >> 6) - 1             // IL_0020–0037  1/64 の行だけ
up     = (ToolController.m_mode & 1 /*Game*/) != 0 ? 128 : 512          // IL_0038–005D
down   = 512                                                            // IL_005E
for (z = zStart..zEnd) for (x = m_modifiedBlockMinX[z] .. m_modifiedBlockMaxX[z]) {
    cur = m_blockHeights[z*1081+x] ; tgt = m_blockHeights2[z*1081+x]
    if (tgt == cur) continue
    if (tgt > cur) cur = Min(tgt, cur + up)      // IL_00D2–00DD
    else           cur = Max(tgt, cur - down)    // IL_00E4–00F0
    m_blockHeights[z*1081+x] = (ushort)cur
}
```

`up = 128` / `down = 512` は **raw 単位（1/64 m）** なので **2 m / 8 m**、かつ**各行は 64 sim フレームに 1 回しか処理されない**。

| モード | 上昇 | 下降 |
|---|---|---|
| ゲーム（`ToolController.m_mode & 1`） | **2 m / 64 sim フレーム** | 8 m / 64 sim フレーム |
| マップエディタ等 | 8 m / 64 sim フレーム | 8 m / 64 sim フレーム |

`m_blockHeights` は「建てられる地面」の層で、`SampleBlockHeight*` と（④§D-3 既出のとおり）
`TerrainManager.HasWater` の水位計算が読む。**見た目（`m_finalHeights` / `m_detailHeights`）は即座に変わるが、
`m_blockHeights` は 300 m の山なら 150 × 64 = 9600 sim フレーム ≒ 3.5 ゲーム内時間かけて追いつく。**
段階的隆起を 2 m/64 フレームより速くしても、この層だけは遅れる。

### A-3. 建物・道路・木・ゾーンはどうなるか — CONFIRMED

**自動倒壊・自動デタッチ・自動撤去は ABSENT。**
`Managers_TerrainUpdated` / `Managers_AfterTerrainUpdate` から `CollapseBuilding` / `BurnBuilding` /
`CollapseSegment` / `ReleaseBuilding` / `ReleaseSegment` に至る経路は 1 本も無い。**ゲームは何も壊さない。**

代わりに、**2 段構え**になっている。前段 `TerrainUpdated`（地形を確定する前）が
「オブジェクト → 地形」の押し付け、後段 `AfterTerrainUpdated`（確定した後）が「地形 → オブジェクト」の追随である。

```
TerrainManager.Managers_AfterTerrainUpdate(TerrainArea, TerrainArea, TerrainArea):
IL_0007  foreach (m in TerrainManager.m_managers)          // static FastList<ITerrainManager>、7 個
IL_0016      m.AfterTerrainUpdate(heightArea, surfaceArea, zoneArea)
```

| 対象 | 前段（`TerrainUpdated`） | 後段（`AfterTerrainUpdated`） | 結果 |
|---|---|---|---|
| **道路・線路・送電線・水道管** | 自分の y へ地形をハードピン（§A-2） | **`NetAI.AfterTerrainUpdate(ushort, ref NetNode)` / `(ushort, ref NetSegment)` はどちらも `IL_0000: ret` の 1 命令** | **動かない。壊れない。地形の方が道路に合わせて凹む** |
| 歩行者道 | 同上 | `PedestrianPathAI.AfterTerrainUpdate(node)`: `m_useFixedHeight` でなく `Abs(SampleDetailHeight(pos) - pos.y) > 0.1f` なら `NetManager.MoveNode` | **地面に追随する** |
| 航路・航空路のノード | — | `ShipPathAI.CheckHeight` / `FlightPathAI.CheckHeight`: 差が `> 7f` なら `MoveNode` | 水面／地面に追随 |
| **建物（`m_flattenTerrain == true`。ゾーン建築とほとんどの ploppable）** | 自分の y へハードピン（1:4 の裾つき） | `Building.AfterTerrainUpdated` IL_0082 で `m_flattenTerrain` を見て**位置更新をスキップ** | **動かない。周囲だけ盛り上がって「すり鉢の底」に取り残される** |
| 建物（`m_flattenTerrain == false` かつ `m_flags & 32 (FixedHeight)` でない） | ピンしない | `m_position.y = SampleBuildingHeight(...)`（IL_00A6）、さらに `BuildingManager.TerrainHeightUpdateNeeded(id)` を積む | **地面に追随して上がる** |
| 建物の基礎（全建物共通） | — | `m_baseHeight = (byte)Clamp(CeilToInt(y - 足元最低地形) + 1, 0, 255)`（IL_0113–0125、しきい値 `ldc.r4 0.04`） | 土台が伸びる。**255 m で頭打ち** |
| **木（`TreeInstance`）** | `m_createRuining` のとき地面の荒れだけ（`heights = 0`） | `m_flags & 32` でなければ `m_posY = Clamp(RoundToInt(SampleDetailHeight(pos) * 64), 0, 65535)`（IL_002C–003D）→ `CheckOverlap` → `UpdateTree` | **地面に追随して上がる。** ただし `CheckOverlap` で道路・建物に埋もれると `GrowState` が変わり非表示になりうる |
| **プロップ（`PropInstance`）** | 荒れ／デカールのみ | 木と同型の `m_posY` スナップ → `CheckOverlap` → `Blocked` | **追随する** |
| ゾーン（`ZoneBlock`） | `ZonesUpdated` は `ApplyQuad(Vector2 …, Zone, …)` で `m_tempZones` だけを触る（高さに一切関与しない） | `ZoneManager.AfterTerrainUpdate` は `IL_0000: ret` | **無反応** |
| 風（`WeatherManager`） | `TerrainUpdated` は `IL_0000: ret` | `AfterTerrainUpdate` が 128² の風グリッド（135 m セル）の `WindCell.m_selfHeight / m_totalHeight / m_deltaHeight` を再計算 → `AreaModified` | 山が風向計算に反映される（副作用として正しい） |

**フィードバックループの警告 — CONFIRMED。**
`m_flattenTerrain == false` の建物が動くと `BuildingManager.m_terrainHeightUpdateNeeded` に積まれ、
`BuildingManager.SimulationStepImpl`（IL_01D1–0237）がそれを吸い出して
`Building.UpdateTerrain(heights: true, surface: false)` → **もう 1 回 `TerrainModify.UpdateArea`** を出す。
→ **地形を毎 tick 動かし続けると、動いた建物 1 軒につき毎 tick 追加のフラッシュが 1 回増える。**
段階的隆起の tick 間隔とフットプリント内の建物数の積がそのままコストになる。

> **依頼文の「地面が上がったら建物はどうなるか」への率直な答え:**
> **浮きはしない。だが、道路とほとんどの建物は上がらない。**
> 見た目は「**山が盛り上がり、道路と建物だけがその中に掘られた平らな溝と穴に残る**」になる。
> 木・プロップ・歩道は素直に上がってくる。ゲーム側の救済措置は存在せず、
> 「山ができたので建物が倒壊する」も「道路が切れる」も **⑤が自分で `CollapseBuilding` / `CollapseSegment` を
> 呼ばない限り絶対に起きない**。

### A-4. 水は地形に追随するか — CONFIRMED（追随する。ただし `m_blockHeights` 経由）

`TerrainManager.Awake`:
```
IL_0401  stfld    TerrainManager::m_waterSimulation
IL_040D  ldfld    TerrainManager::m_blockHeights
IL_0412  callvirt WaterSimulation::Initialize          // 中身は m_heightBuffer = heights の 2 命令
```

→ **`WaterSimulation.m_heightBuffer` は `TerrainManager.m_blockHeights` そのもの**（起動時に参照を共有）。
`SimulateWater` は毎ステップこれをローカルに読み、セルループで直接添字する。スナップショットもコピーもキャッシュも無い。

したがって:

- **「地形が変わったので水を再計算せよ」という通知は存在しない。ABSENT。**
  `UpdateAreaImplementation` は水シミュに一切触れず、`WaterSimulation` は `ITerrainManager` の実装でもない
  （実装は `BuildingManager` / `WeatherManager` / `NetManager` / `ToolManager` / `PropManager` / `TreeManager` / `ZoneManager` の 7 つだけ）。
  `TerrainManager.SimulationStepImpl` が先に `m_waterSimulation.SimulationStep(subStep)` を呼び、
  そのあとブロック高さの lerp をするだけ。水スレッドは次のステップで「少し違う地面」を見る。
- **水が見るのは `m_blockHeights` であって `m_rawHeights` ではない。**
  ⑤の隆起が水に届く経路は `RawHeights → UpdateArea → m_tempHeights → m_blockHeights2 →
  §A-2 の 2 m / 64 sim フレームのレートリミッタ → m_blockHeights` である。
  → **海底を 2 m / 64 フレームより速く上げても、水はそれより速く押しのけられない。**
  1 tick で 40 m 上げても、水が気づくまでに約 20 サイクル（1280 sim フレーム）かかる。
- 「水中か」を答えるものは全部この遅れた配列を読む:
  `TerrainManager.HasWater` / `WaterLevel` / `GetSurfaceHeight` / `GetSurfaceHeight2` /
  `CountWaterCoverage` / `CalculateWaterFlow` / `GetClosestWaterPos` / `GetShorePos` / `SampleWaterData`、
  および `TsunamiAI.FindSea` / `TsunamiAI.UpdateHazardMap`。
- `WaterSimulation.BeginTerrainUpdate(out uint, out uint)` / `EndTerrainUpdate(uint)` / `UpdatePatch(int,int)` は
  **描画側**（`m_heightMaps` / `m_surfaceMapsA/B` を `FillHeightMap` / `FillSurfaceMap` で埋める）であって、
  地形変更フックではない。`LateUpdateData` がデータ更新後に `EnableWaterFlow()` を呼ぶ。
- 局所的に水位を上げたいなら ④§D-4 の `WaterSource`（`m_target` = 目標水位、1/64 m の絶対値）。
  `TYPE_TSUNAMI` の波は ④§D-3 のとおり**マップ外周リングでしか評価されない**ので、火口湖には使えない。
- 定数（`Dump-Type` 実測）: `DEFAULT_SEA_LEVEL = 40` / `MAX_SEA_LEVEL = 500`、
  `m_currentSeaLevel` / `m_nextSeaLevel` は **public Single**。

---

## B. 溶岩

### B-5. 溶岩の既製ビジュアル — **ABSENT（溶岩は無い）／ 火は借りられる（CONFIRMED）**

**文字列ヒープの直接走査**（`Assembly-CSharp.dll` を UTF-16 の `#US` ヒープと ASCII の `#Strings` ヒープの両方でバイト検索）:

```
UTF16  'lava':0  'magma':0  'volcan':0  'molten':0  'ember':0  'scorch':0
ASCII  'lava':1  → "…demicWorksAvailable GetAllAvailableAcademicWorks…" の部分一致のみ
ASCII  'magma':0 'volcan':0 'molten':0 'scorch':0
'glow' のヒットは ColossalFramework.ToneMapping.m_EnableGlowSupport / m_GlowIntensityThreshold のみ
'smoke' のヒットは DistrictPolicies.Policies.SmokeDetectors のみ
```

**溶岩・マグマ・溶融物のプレハブ／マテリアル／シェーダ／エフェクトは 1 つも存在しない。**

`EffectInfo` のサブクラス（全型走査、両アセンブリ）:
`SoundEffect` / `EngineSoundEffect` / `LightEffect` / `ParticleEffect` / `MovementParticleEffect` /
`ShipTrailParticleEffect` / `MultiEffect` / **`FireEffect`** の 8 つ。

> **④§C-1 の記述を訂正する。** 「災害まわりで `EffectInfo` 型のフィールドを持つのは
> `DisasterProperties.m_mediumExplosion` と `MeteorAI.m_impactEffect` の 2 つだけ」は、
> **災害 AI の直下に限れば正しいが、⑤が使うべきものを見落としていた。**
> 全アセンブリの `EffectInfo` 型フィールドには次が含まれる:
>
> ```
> BuildingProperties.m_placementEffect / m_bulldozeEffect / m_levelupEffect /
>                    m_fireEffect / m_collapseEffect / m_collapseFloodedEffect     ★
> TreeProperties.m_placementEffect / m_bulldozeEffect / m_fireEffect               ★
> DisasterProperties.m_mediumExplosion         MeteorAI.m_impactEffect
> NetProperties / PropProperties / DistrictProperties / ZoneProperties /
> FiremanAI.m_waterEffect / FireCopterAI / PowerLineAI.m_breakEffect /
> RocketLaunchAI / RocketAI / 各種 AnimalAI.m_randomEffect / …
> FireEffect（合成型）: ParticleEffect m_particleEffect / LightEffect m_lightEffect / SoundEffect m_soundEffect
> EffectCollection.m_effects : EffectInfo[]
> ```

**炎は任意座標に描ける — CONFIRMED。**

```
CommonBuildingAI.RenderFireEffect(CameraInfo, ushort, ref Building, float):
IL_001B  ldc.r4 0.007843138        // 1/255。Building.m_fireIntensity を正規化
IL_0023  ldc.r4 0.01               // これ未満なら描かない
IL_0077  ldc.r4 0.0002             // magnitude /= (1 + m_triangleArea * 0.0002)
IL_008A  ldfld BuildingProperties::m_fireEffect
IL_00A0  call  SpawnArea::.ctor
IL_00BD  callvirt EffectInfo::RenderEffect

TreeManager.RenderFireEffect(CameraInfo, uint, ref TreeInstance, float, float):
IL_00B4  ldfld TreeProperties::m_fireEffect
IL_00DF  ldc.r4 0.05               // radius = (size.x + size.z) * scale * 0.05
IL_0104  call  SpawnArea::.ctor
IL_0126  callvirt EffectInfo::RenderEffect
```

- `SimulationManagerBase<T,P>.m_properties` は **public フィールド**なので
  `BuildingManager.instance.m_properties.m_fireEffect` / `TreeManager.instance.m_properties.m_fireEffect` に到達できる。
- `EffectInfo.RenderEffect(InstanceID, SpawnArea, Vector3 velocity, float acceleration, float magnitude, float timeOffset, float timeDelta, CameraInfo)` は **public virtual**。
- `EffectInfo+SpawnArea` の ctor（＝配置 API）:
  ```
  .ctor(Matrix4x4 matrix, MeshData meshData)
  .ctor(Matrix4x4 matrix, MeshData meshData, Vector4[] positions, Vector3[] positions2)
  .ctor(Bezier3 bezier, float halfWidth, float halfHeight)
  .ctor(Vector3 position, Vector3 direction, float radius)
  .ctor(Vector3 position, Vector3 direction, float radius, float halfHeight)
  ```
- 実行時のエフェクト一覧は `EffectCollection.FindEffect(string name)`（public static、内部は
  `private static Dictionary<string,EffectInfo> m_dict`）で引ける。名前はアセットバンドル側にあり DLL には無いので、
  **`m_dict` をリフレクションで列挙するのが唯一の在庫確認手段**。

**焦げ地面は既にある — CONFIRMED（ただし「黒」であって「赤熱」ではない）。**

```
NaturalResourceManager.Awake:
IL_00B9  ldstr "_NaturalDestruction"
IL_00C4  call Shader::SetGlobalTexture
UpdateTextureB: 3×3 ぼかし（角 5 / 辺 7 / 中心 14、Σ=62）→ Texture2D(512,512)
IL_016A/017A/018A  ldc.r4 6.325111E-05     // = 1/(62*255)
  r = m_pollution, g = m_burned, b = m_destroyed
```
→ `ResourceCell.m_burned` はグローバルテクスチャ `_NaturalDestruction` の**緑チャンネル**として地形シェーダに入る。

**借りられるマテリアル在庫（全 `*Properties` 型の Material / Shader / Mesh フィールド、実測抜粋）:**
```
DisasterProperties.m_markerMaterial            WeatherProperties.m_lightningMaterial / m_lightningMesh
TerrainProperties.m_terrainShader / m_waterShader / m_cliffDiffuse / m_oreDiffuse / …（全 Texture2D）
BuildingProperties.m_burnedDiffuse / m_highlightMaterial
DayNight*CloudsProperties.m_CloudMaterial      GameAreaProperties.m_areaMaterial …
```

> **結論（率直に）。溶岩の「光って流れる面」は⑤が自作するしかない。**
> ③で確立した規約がそのまま効く:
> - **CS のマテリアルを借りると自前 `MeshRenderer` では何も描かれない**（firewhirl §4.9）。
>   メッシュだけ借り、`Shader.Find("Standard")` → `"Legacy Shaders/Diffuse"` → `"Diffuse"` の順に自作する。
> - **`static Mesh[]` に入れると 2 つ目の都市で無言で消える**（同 §4.8）。要素で null 判定する。
> - `DispatchEffect` の `magnitude` は粒子密度であってサイズではない（同 §4.9）。
>
> ただし**「炎」だけは自作不要**である。`m_fireEffect` を自前 `SpawnArea` で流路に沿って `RenderEffect` すれば、
> バニラと完全に同じ見た目の炎が任意座標に出る。**「溶岩＝自作の発光メッシュ ＋ 借り物の炎 ＋ `BurnGround` の焦げ」**
> が最もコストの低い構成になる。

### B-6. 地形高さのサンプリング — CONFIRMED

**まず前提の再確認: 地形にコライダーは無く `Physics.Raycast` は当たらない**（既存 MEMORY / `FireWhirlPlacementTool` の
コメントのとおり）。ただし**バニラは public なレイキャストを持っている**:

```
public bool TerrainManager.RayCast(Segment3 ray, out Vector3 hit)
IL_002C  Bounds(center: (0, 512, 0), size: (17280, 1024, 17280))     // ★ y は 0..1024
IL_005C  Segment3.Clip(...)                                          // 先にバウンズでクリップ
IL_0086  セル座標へ: (v / 16) + 540、FloorToInt                       // 以降 raw セルをマーチ
private bool RayCastEdge(Segment3 ray, out Vector3 hit)
```
→ **`FireWhirlPlacementTool.TryPickGround` の自前マーチ＋二分法は、`TerrainManager.RayCast` で置き換えられる。**
⑤で新しく書く必要は無い（③のものをそのまま流用するか、`RayCast` に寄せるかは設計判断）。

サンプリング API の実測（すべて **public instance**、`TerrainManager.instance` 経由）:

| API | 読む配列 | 座標系 | 単位 | コスト |
|---|---|---|---|---|
| `RawHeights[z*1081 + x]` | `m_rawHeights` | raw セル（`(v/16)+540`） | raw（`/64` で m） | **配列読み 1 回。最安。** ただし**建物・道路の押し戻しが入る前の値**であり、**MOD 自身が書く配列** |
| `SampleRawHeight(float x, float z)` | **`m_rawHeights2`** | raw セル（小数可） | raw | 4 読み ＋ 3 `Lerp`（IL_0058–00A5） |
| `SampleRawHeightSmooth(float, float)` / `(Vector3)` | `m_rawHeights2` | 同上 | raw | 16 読み ＋ **`SmoothSample` 5 回**。約 20 倍 |
| `SampleOriginalRawHeightSmooth(float, float)` | **`m_rawHeights`**（IL_00A5 以降すべて `m_rawHeights`） | 同上 | raw | 同上。名前の "Original" は「建物の押し戻しが入る前」であって「MOD の編集前」ではない |
| `SampleFinalHeight(float, float)` | `m_finalHeights` | raw セル | raw | 4 読み ＋ 3 `Lerp` |
| `SampleBlockHeight*` | `m_blockHeights` | raw セル | raw | §A-2 の遅延あり |
| **`SampleDetailHeight(Vector3 worldPos)`** | `m_detailHeights`（4 m）、パッチ未解放なら `SampleFinalHeight` へフォールバック | ワールド | **メートル** | `GetDetailHeight` ×4 ＋ 3 `Lerp` |
| `SampleDetailHeight(Vector3, out float slopeX, out float slopeZ)` | 同上 | ワールド | m ＋ 傾斜 | **流路の下り方向を出すならこれが最適** |

```
TerrainManager.SampleDetailHeight(Vector3 worldPos):
IL_0007  x = worldPos.x / 4 + 2160        // 4 m セル、中心 2160 = 4320/2
IL_001B  z = worldPos.z / 4 + 2160
IL_002B  call SampleDetailHeight(float, float)
IL_0030  ldc.r4 0.015625 ; mul            // ★ メートルで返る（他の Sample* は raw のまま）

TerrainManager.GetDetailHeight(int x, int z):
IL_0000  px = Min(x / 480, 8) ; pz = Min(z / 480, 8)      // 9×9 パッチ
IL_002B  idx = m_patches[pz*9 + px].m_simDetailIndex
IL_0031  if (idx == 0) return SampleFinalHeight(x*0.25f, z*0.25f)    // ★ 未解放タイルは 16 m 補間
IL_00C9  return m_detailHeights[(idx-1)*481*481 + (z%480)*481 + (x%480)]
```

`m_simDetailIndex` を立てるのは **`GameAreaManager.UpdateData` と `GameAreaManager.UnlockArea` の 2 つだけ**
（トークン走査）。つまり **detail 解像度が有効なのは「購入済みタイル」だけ**であって、カメラ位置には依存しない。
→ **sim スレッドで使っても決定論は壊れない**（カメラ非依存）。
→ ただし**溶岩流が未購入タイルへ出た瞬間、サンプリング解像度が 4 m から 16 m 補間に落ちる**。段差は出ないが挙動は変わる。

`TerrainManager.SmoothSample(float h1, float h2, float h3, float h4, float t)`（public static、IL_0000–00C2）は
**単調制限付き三次 Bezier**であって平滑化フィルタではない:

```
m1 = h2 + (h3 > h1 ? Min((h3-h1)/6, Max(h2-h1, h3-h2))
                   : Max((h3-h1)/6, Min(h2-h1, h3-h2)))       // IL_0000–0043
m2 = h3 + (h2 > h4 ? Min((h2-h4)/6, Max(h3-h4, h2-h3))
                   : Max((h2-h4)/6, Min(h3-h4, h2-h3)))       // IL_0044–0087
s  = 1 - t
return h2*s³ + 3*m1*t*s² + 3*m2*t²*s + h3*t³                  // IL_0088–00C2
```
`0.1666667 = 1/6` はリテラル（IL_000B / IL_002C / IL_004F / IL_0075）。
接線が局所差分で Min/Max 制限されているので**オーバーシュートしない**＝リンギングも段差の削り取りも起きない。

### B-7. 溶岩の跡に火をつける — CONFIRMED（1 つ ABSENT）

**(a) 建物 — `BuildingAI.BurnBuilding` — 使える。DLC 不要。**

```
public virtual Boolean BuildingAI.BurnBuilding(ushort buildingID, ref Building data,
                                               InstanceManager.Group group, bool testOnly)
IL_0000:  ldc.i4.0
IL_0001:  ret                       // 既定は常に false
```
override は **`CommonBuildingAI` / `ShelterAI` / `TsunamiBuoyAI` の 3 つだけ**（④§F-2 と一致、再確認済み）。
`ShelterAI` / `TsunamiBuoyAI` は `IL_0000 ldc.i4.0 ; ret` の**ハード拒否**なので、実装は `CommonBuildingAI` 1 本。

`CommonBuildingAI.BurnBuilding` の拒否条件と副作用:
```
IL_000F  callvirt BuildingAI::GetFireParameters      → false なら return false
IL_001F  ldc.i4 4194304                              → (m_flags & 0x400000 Collapsed) なら return false
IL_003A  callvirt TerrainManager::WaterLevel         → 水位 > m_position.y なら return false   ★ 水没中は燃えない
IL_0053  ldarg.s 4 ; brtrue IL_020C                  → testOnly なら副作用なしで true
IL_00CF  ldc.i4 -536870913                           → m_flags &= ~0x20000000
IL_00E2  call Mathf::Max                             → m_fireIntensity = (byte)Max(m_fireIntensity, fireParam2)
IL_00FE  ldc.i4 133                                  → Frame.m_fireDamage = (byte)Max(m_fireDamage, 133)
IL_01E1  ldc.i4 49152                                → サブ建物ループの上限
```
**強度は引数で指定できない**（`GetFireParameters` が建物ごとに決める）。強めたいなら `BurnBuilding` の後で
`Building.m_fireIntensity` を自分で書く（firewhirl 付録 A-3 の規約に照らして、**まず `BurnBuilding` を通すこと**）。
`CommonBuildingAI.BurnBuilding` に `SupportsExpansion` / `IsDLCOwned` の呼び出しは**無い**（全メソッド走査で
ゲートを持つのは `HandleFire` / `HandleCommonConsumption` / `SimulationStep` の側）。

**(b) 地面 — `DisasterHelpers.BurnGround` — 使える。DLC 不要。**

```
public static void DisasterHelpers.BurnGround(Vector2 position, float radius, float intensity)

IL_0006  ldc.r4 33.75      cellSize   （= NaturalResourceManager.RESOURCEGRID_CELL_SIZE）
IL_000C  ldc.i4 512        resolution（= RESOURCEGRID_RESOLUTION）
IL_0014  ldc.r4 0.51       radius += 33.75 * 0.51   （= +17.2125 m の半セル余白）
IL_001E  ldc.r4 255        intensity *= 255         ★ 引数は 0.0–1.0 の正規化値

minX = Max((int)((position.x - radius)/33.75 + 256), 0)      IL_0026–0041
minZ = Max((int)((position.y - radius)/33.75 + 256), 0)      IL_0042–005D
maxX = Min((int)((position.x + radius)/33.75 + 256), 511)    IL_005F–007C
maxZ = Min((int)((position.y + radius)/33.75 + 256), 511)    IL_007E–009B

各セル: p = ((x - 256 + 0.5) * 33.75, (z - 256 + 0.5) * 33.75)
IL_00F1  if (Vector2.Distance(position, p) >= radius) continue
IL_0119  v = Clamp(RoundToInt(intensity255 * (1 - d/radius)), 0, 255)
IL_012D  m_naturalResources[z*512 + x].m_burned = (byte)Max(既存値, v)   ★ 単調非減少。下がらない
IL_0174  NaturalResourceManager.AreaModifiedB(minX, minZ, maxX, maxZ)
```
`ResourceCell` = `{ m_ore, m_oil, m_sand, m_forest, m_tree, m_fertility, m_pollution, m_water, m_shore, m_burned, m_destroyed, m_modified }`（すべて `Byte`）。
グリッドは **512 × 512 × 33.75 m = 17280 m ＝ マップ全体**。`BurnGround` に DLC ゲートは**無い**。
バニラの実引数: `MeteorAI.ArriveAtDestination` が `1.0`（IL_0273）、`SinkholeAI` / `EarthquakeAI` / `VortexAI` が `0.7`。

**(c) 木 — `TreeManager.BurnTree` — ⑤では ABSENT（DLC ゲート）。**

```
public bool TreeManager.BurnTree(uint treeIndex, InstanceManager.Group group, int fireIntensity)
   （public、instance、非 virtual）

IL_0000  treeIndex == 0                                       → return false
IL_001D  ldc.i4.s 64 ; and                                    → (m_flags & 64 FireDamage) → return false
IL_002C  ldc.i4.2 ; callvirt LoadingManager::SupportsExpansion → false なら return false     ★
IL_007F  m_fireIntensity = (byte)fireIntensity   （conv.u1。クランプ無し）
IL_0086  m_fireDamage = 4
IL_00C0  ldc.i4 192                                            → m_flags |= 0xC0 (FireDamage|Burning)
```
`LoadingManager.SupportsExpansion(ICities.Expansion)` は `m_supportsExpansion[]` の素の配列読み
（`IL_0001 ldfld ; IL_0007 ldelem.u1`）。`ICities.Expansion`: `AfterDark=0 Snowfall=1 **NaturalDisasters=2** InMotion=3 …`。
→ **`ldc.i4.2` は Natural Disasters。ND DLC が無いと `BurnTree` は必ず false を返す。**
全アセンブリで `SupportsExpansion` / `IsDLCOwned` を呼ぶメソッドを走査した結果、
**火まわりでゲートされているのは `TreeManager::BurnTree` と `CommonBuildingAI::HandleFire` だけ**である。

さらに `m_flags & 64 (FireDamage)` は**一度燃えた木は二度と燃えない**ことを意味する（`Burning=128` は
必ず 64 と同時に立つので 64 の判定で拾える）。樹種・`Created`・`Hidden`・`Deleted` は**見ていない**。
`fireIntensity` は `conv.u1` で**切り捨てられる（クランプされない）**ので、256 は 0 に、300 は 44 になる。呼び出し側で clamp すること。
バニラの実値: `DisasterHelpers.DestroyTrees` = 128 / `ForestFireAI` = `Min(255, 128 + intensity)` /
`WeatherManager.StrikeNow` = `Randomizer.Int32(140, 255)`。実用域は **128–255**。

> **⑤は DLC 不要（firewhirl §4.12）なので、「溶岩の通り道の木が燃える」は
> ND を持たないプレイヤーには出せない。** `LoadingManager.instance.SupportsExpansion(Expansion.NaturalDisasters)` で
> 分岐し、無い場合は `BurnGround` の焦げと（後述の）自作エフェクトだけにするか、
> 木を `TreeManager.ReleaseTree` で消すかを設計で選ぶこと。診断（Phase 0.5）に 1 行出す。

**(d) 道路 — 「燃やす」は ABSENT。「壊す」だけある。**

```
public virtual Boolean NetAI.CollapseSegment(ushort segmentID, ref NetSegment data,
                                             InstanceManager.Group group, bool demolish)
IL_0000:  ldc.i4.0 ; ret            // 既定は false
```
override は 19 型（`CableCarPathAI` / `DamAI` / `DecorationWallAI` / `MetroTrackBaseAI` / `MetroTrackTunnelAI` /
`MonorailTrackAI` / `NetAI` / `PedestrianBridgeAI` / `PedestrianPathAI` / `PedestrianWayAI` / `PlayerNetAI` /
`PowerLineAI` / `RoadBaseAI` / `RoadTunnelAI` / `RunwayAI` / `SupportCableAI` / `TaxiwayAI` /
`TrainTrackBaseAI` / `TrainTrackTunnelAI`）。`RoadBaseAI` の拒否条件:
```
IL_0002  demolish == true            → PlayerNetAI::CollapseSegment へ委譲
IL_0019  (m_flags & 32) != 0         → return false
IL_0029  (m_flags & 11) != 1         → return false
IL_0035  DefaultTool::IsOutOfCityArea → true なら return false
IL_0048  m_flags |= 8 (Collapsed)
```
`NetAI` / `NetManager` / `NetSegment` / `NetNode` を `Collapse|Burn|Destroy` で全走査したが、
**`BurnSegment` / `BurnNode` に相当するものは 1 つも無い。道路は倒壊するだけで燃えない。**

---

## C. 山そのもの

### C-8. `DisasterHelpers.MakeCrater` の実際の形 — CONFIRMED（全文読了）

```
public static void DisasterHelpers.MakeCrater(Vector2 position, float radius, float depth, bool raiseEdges)
```

```
IL_0006  TerrainModify.RefreshAllModifications()            // ★ 呼ぶたびに 1 回フラッシュする
IL_000B  cell = 16 ; res = 1080 ; heights = TerrainManager.instance.RawHeights
IL_001E  toMeters = 0.015625 (=1/64) ; toRaw = 64
IL_002C  minX = Max((int)((position.x - radius)/16 + 540),     0)
IL_0049  minZ = Max((int)((position.y - radius)/16 + 540),     0)
IL_0066  maxX = Min((int)((position.x + radius)/16 + 540) + 1, 1080)
IL_0085  maxZ = Min((int)((position.y + radius)/16 + 540) + 1, 1080)

for (z = minZ; z <= maxZ; z++) {                             // IL_00A4 / IL_0213
  p.y = (z - 540) * 16
  for (x = minX; x <= maxX; x++) {                           // IL_00C2 / IL_0204
    p.x = (x - 540) * 16
    raw = heights[z*1081 + x]                                // IL_00E0–00EA
    h   = raw * 0.015625                                     // IL_00ED–00F3   メートル
    v   = h
    d   = Vector2.Distance(position, p)                      // IL_00F9–0101

    if (raiseEdges) {                                        // IL_0103
      if (d < radius * 0.75) {                               // IL_0109–0112
        u = d / (radius * 0.75)                              // IL_0117–0121
        v += (u*u*u*u - 0.7) * depth                         // IL_0123–0139   ★ u⁴ − 0.7
      } else if (d < radius) {                               // IL_0140–0143
        w = (d - radius) / (radius * 0.25)                   // IL_0148–0154   ∈ [−1, 0)
        v += (w*w * 0.3) * depth                             // IL_0156–0166
      }
    } else {                                                 // IL_016D
      if (d < radius * 0.73) {                               // IL_016D–0176
        u = d / (radius * 0.837)                             // IL_017B–0185
        v += (u*u*u*u - 1) * depth                           // IL_0187–019D   ★ u⁴ − 1
      } else if (d < radius) {                               // IL_01A4–01A7
        w = d / (radius * 0.837) - 1.197                     // IL_01AC–01BC
        v -= (w*w * 4) * depth                               // IL_01BE–01CE   ★ 引く
      }
    }

    n = Clamp(RoundToInt(v * 64), 0, 65535)                  // IL_01D0–01E5
    if (n != raw) heights[z*1081 + x] = (ushort)n            // IL_01E7–01FD
  }
}
IL_021C  TerrainModify.UpdateArea(minX-2, minZ-2, maxX+2, maxZ+2, heights:true, surface:false, zones:false)
```

**`raiseEdges` が実際に作る形（`depth > 0`）:**

| `d / radius` | `raiseEdges = true` | `raiseEdges = false` |
|---|---|---|
| 0 | `−0.7000 × depth` | `−1.0000 × depth` |
| 0.375 | `−0.6375 × depth` | `−0.9597 × depth` |
| 0.73 | `+0.1975 × depth` | `−0.4214 / −0.4221 × depth`（分岐の境目。連続、誤差 7e-4） |
| 0.75 | `+0.3000 × depth`（＝ **縁の頂点**。内側分岐の `u=1` と外側分岐の `w=−1` が一致） | `−0.3623 × depth` |
| 0.875 | `+0.0750 × depth` | `−0.0919 × depth` |
| 1.0 | `0`（元の地形に戻る） | `−2.0e-5 × depth ≈ 0` |

→ **`raiseEdges = true` は「深さ 0.7×depth の穴」＋「0.75R に高さ 0.3×depth の環状の縁」**。
   縁は元の地形に対する相対量で、外側で滑らかに 0 に戻る。**穴の方が縁より 2.3 倍深い。**
→ **`raiseEdges = false` は縁の無いただの椀（深さ `depth`、`u⁴` プロファイルなので底が平ら）。**

**`depth` に負値を渡せるか → 渡せる。CONFIRMED。**
`depth`（`ldarg.2`）に対する符号チェック・`Mathf.Abs`・クランプは IL のどこにも無い。
最終クランプは `Clamp(Round(v*64), 0, 65535)` だけ。したがって:

```
MakeCrater(pos, R, -H, raiseEdges:false)
   → v = h + (1 − (d/(0.837R))⁴) * H              (d < 0.73R)
     v = h + (d/(0.837R) − 1.197)² * 4H           (0.73R ≤ d < R)
   = 高さ H・半径 R の「頂が平らで裾が滑らかな盛り上がり」＝ **楯状火山そのもの**

MakeCrater(pos, R, -H, raiseEdges:true)
   → 中心が +0.7H、0.75R に −0.3H の環（＝ドームの周りに堀）。火山には不向き
```

**重ね掛けできるか → できる。CONFIRMED。**
各セルで `v = (現在の raw 高さ) + f(d) * depth` という**加算**であり、書き戻し前に読み直している。
したがって N 回呼べば N 個のプロファイルが足し合わさる。
**ただし 2 つ注意:**
1. **量子化。** `Clamp(Round(v*64), …)` かつ `if (n != raw)` なので、**1 回の呼び出しでセル高さの変化が
   1/64 m = 0.015625 m 未満だと丸めで消えて何も書かれない。** 段階的隆起の 1 tick あたりの増分を
   これより小さくすると、**無言で完全に停止する。**
2. **コスト。** 先頭の `RefreshAllModifications()` は `UpdateAreaImplementation` そのものなので、
   1 tick に k 回呼べば**フラッシュが k+1 回**走る（各呼び出しの先頭で 1 回 ＋ 末尾の `UpdateArea` で 1 回）。

**他に地形を上げる API はあるか。**
`DisasterHelpers` の全メソッド（`Dump-Type` 実測）のうち地形を触るのは
`SplashWater` / `MakeCrater` / `MakeCrack` の 3 つだけ。
`TerrainManager` 側には `SetHeightMap(Color32[], int)` / `SetHeightMap8(byte[])` / `SetHeightMap16(byte[])` /
`SetRawHeightMap(byte[])` があるが、いずれも **`UpdateArea(0, 0, 1080, 1080, true, true, true)`＝マップ全面フラッシュ**
（`SetHeightMap16` IL_008D、`SetRawHeightMap` IL_0093）なので、部分的な隆起には使えない。
→ **円錐・成層火山の任意プロファイルは、⑤が `RawHeights` を自分で書くしかない**（`MakeCrack` / `MakeCrater` と同じ 6 行の型）。

### C-9. 急峻な円錐は潰されるか → **潰されない。CONFIRMED**

**最大勾配・侵食・整地パスは ABSENT。** 全型・全フィールド・全メソッドを
`slope|steep|gradient|smooth|erod|erosion|settle|MAX_HEIGHT|maxHeight|flatten` で走査した結果:

| ヒット | 実体 | ⑤への影響 |
|---|---|---|
| `NetInfo.m_maxSlope` / `m_maxHeight` | **道路プレハブごとの敷設制限** | 地形には作用しない。急斜面に道路が引けなくなるだけ |
| `ToolErrors.SlopeTooSteep` / `ElevationErrors.Slope` | **ツールの検証エラー** | 同上 |
| `BuildingInfo.m_ignoreSlopeTooSteep` / `m_flattenTerrain` / `m_flattenFullArea` | **建物の配置判定と pad**（§A-2） | 地形の勾配自体は制限しない |
| `LandscapingOptionPanel.m_SlopeBrushInfo` / `TerrainTool+Mode.Slope` | **プレイヤーの整地ブラシ** | 手動操作。自動では走らない |
| `DistrictTool.kFlatteningStrength` | 地区ツール | 手動 |
| `TerrainManager.SmoothSample` / `Sample*Smooth` | **補間関数**（§B-6） | 単調制限付き cubic。**格納値を書き換えない** |
| `NetAI.FlattenGroundNodes` / `FlattenOnlyOneDoubleSegment` | 道路の接続処理 | §A-2 の押し戻しの一部 |
| `ImmaterialResourceManager.m_tempSectorSlopes` | 資源計算のワーク | 無関係 |

**侵食（erosion）・沈降（settle）・時間経過による平滑化は 1 件もヒットしない。**
`UpdateAreaImplementation` の高さパイプライン（§A-2）にも勾配を見る分岐は無い
（クランプは `primaryMin/Max` / `secondaryMin/Max` / `blockHeight` / `digHeight` の 6 つだけで、
初期値はそれぞれ 0 / 1024 / 0 / 1024 / 0 / 1024 ＝ **何も制約していない**）。

したがって**実際に達成できる勾配を決めるのは 2 つだけ**:

1. **16 m の raw 格子。** 隣接セル間で表現できる最大の高低差は `65535 / 64 = 1023.98 m`。
   → 幾何的な上限勾配は `1023.98 / 16 ≒ 64 : 1`（約 89.1°）。**実用上は無制限。**
2. **`SmoothSample` による見え方。** 格子点の間は単調制限付き cubic で結ばれるので、
   **1 セルだけを尖らせても頂点は丸く見える**（尖点は表現できない）。
   `SmoothSample` は接線を `Min/Max` で局所差分に制限しているのでオーバーシュートは無く、**斜面が削られることもない。**

**3 形態を 16 m 格子で作ったときの実効勾配（参考、`h(d)` はセル中心での値）:**

| 形態 | 例 | 平均勾配 | 頂上付近の 1 セルあたり高低差 |
|---|---|---|---|
| 楯状（shield） | H = 200 m, R = 2000 m | 1 : 10（約 5.7°） | 小。`MakeCrater(pos, R, -H, false)` 1 発で作れる |
| 成層（stratovolcano） | H = 600 m, R = 1200 m | 1 : 2（約 26.6°） | 8 m/セル。まったく問題ない |
| 溶岩ドーム（lava dome） | H = 300 m, R = 350 m | 1 : 1.17（約 40.6°） | 13.7 m/セル。問題ない。**ただし半径 350 m は raw セル 22 個分**しかないので、形が格子で階段状に見える |

> **溶岩ドームだけは解像度が効く。** 半径 350 m は片側 ~11 セル。`SmoothSample` が丸めるので階段には見えないが、
> 「小さくて非常に急」という指定は 16 m 格子の下限に近い。**半径 250 m（片側 8 セル）を下回らせないこと。**
> それ以下は「山」ではなく「地面のノイズ」に見える。

### C-10. 高さの範囲と余裕 — CONFIRMED

```
TerrainManager（public static、すべて literal）:
  RAW_CELL_SIZE       = 16
  DETAIL_CELL_SIZE    = 4
  TERRAIN_HEIGHT      = 1024
  TERRAIN_LEVEL       = 60
  RAW_RESOLUTION      = 1080
  RAW_MAP_RESOLUTION  = 128
  DETAIL_RESOLUTION   = 480
  DETAIL_MAP_RESOLUTION = 512
  PATCH_RESOLUTION    = 9
  MAX_SUBPATCH_LEVELS = 4
  DETAIL_SHIFT        = 2
  MIN_WATER_AMOUNT    = 8
  DIRT_BUFFER_SIZE    = 1048576
  DIRT_BUYSELL_PRICE  = 5        （nonpublic）

WaterSimulation（public static）:
  DEFAULT_SEA_LEVEL = 40
  MAX_SEA_LEVEL     = 500
```

- `RawHeights` は `UInt16`、`raw / 64` メートル。**表現できるのは 0 〜 65535/64 = 1023.984375 m。**
- `TerrainModify.UpdateAreaImplementation` の初期化が `m_primaryMax = m_secondaryMax = m_digHeight = 1024`
  （IL_0206 / IL_021E / IL_0236、いずれも `ldc.r4 1024`）で、天井が `TERRAIN_HEIGHT` と一致する。
- `TerrainManager.RayCast` のバウンズも `center(0, 512, 0)` / `size(17280, 1024, 17280)`（IL_002C–0054）で、
  **y 方向の世界は 0 〜 1024 m しか無い。**
- `TERRAIN_LEVEL = 60` はマップの基準地面高。`DEFAULT_SEA_LEVEL = 40` が既定の海面。

| 出発点 | 天井までの余裕 |
|---|---|
| 海面（40 m） | **983.98 m** |
| 既定の地面（`TERRAIN_LEVEL` 60 m） | **963.98 m** |
| 高原（200 m） | 823.98 m |
| 山地（500 m） | 523.98 m |

> **成層火山 600 m は既定の平地からなら余裕で入る。** ただし**上限に当たっても例外は出ず、
> `Clamp(…, 0, 65535)` で無言に頭打ちになり、山頂が平らな台地になる**（`MakeCrater` IL_01DA、
> `UpdateAreaImplementation` IL_0AB2 / IL_0B06、いずれも `ldc.i4 65535`）。
> 起点の地形高さを読んでから最大高を決めること。

**「土（dirt）」の経済は⑤には掛からない — CONFIRMED。**
`TerrainManager` には `m_dirtBuffer` / `DirtBuffer` / `RawDirtBuffer` / `GetDisposeSoilPrice` / `GetBuySoilPrice` /
`m_notEnoughDirt` / `m_tooMuchDirt` という**整地の土量経済**があるが、
`MakeCrater` / `MakeCrack` / `TerrainModify.UpdateArea` の IL のどこにも `m_dirtBuffer` は現れない
（`RawHeights` を直接書く経路は素通りする）。②§D-1 の「コストは無い」はこの意味で正しい。
触るのは `TerrainTool.ApplyBrush` などプレイヤーの整地ツールだけである。

---

## D. 噴火と寿命

### D-11. 本物の `DisasterData` として動かせるか — PARTIAL（結論は「やらない方がよい」）

**確定していること:**

```
DisasterAI : PrefabAI        abstract = False        ★ 抽象クラスではない
  フィールドは 3 つだけ: ManualMilestone m_experienceMilestone / DisasterInfo m_info / FastList<?> m_tempList

DisasterAI のサブクラス（全型走査）:
  EarthquakeAI / ForestFireAI / MeteorStrikeAI / SinkholeAI /
  StructureCollapseAI / StructureFireAI /
  FloodBaseAI（→ GenericFloodAI, TsunamiAI） /
  WeatherDisasterAI（→ ThunderStormAI, TornadoAI）
```

`DisasterManager.CreateDisaster(out ushort, DisasterInfo)` に **DLC ゲートは無い**:
```
IL_000A  d.m_flags      = Created(1)
IL_001C  d.m_randomSeed = SimulationManager.m_randomizer.ULong64()
IL_0028  d.m_intensity  = 55
IL_0032  d.Info         = info
IL_00C3  ldc.i4 256                       // ②§E-1 既出の MAX_DISASTER_COUNT
IL_011A  ldc.i4.0 ; ret                   // 溢れたら false（例外なし）
```

DLC を見ているのは**リレーだけ**である:
```
DisasterManager.CreateRelay():
IL_0000  ldc.i4 515191                    // Natural Disasters の Steam AppID
IL_0005  call SteamHelper::IsDLCOwned
IL_000A  brfalse → ReleaseRelay()          // DLC 無しなら m_DisasterWrapper = null
IL_001C  else     m_DisasterWrapper = new DisasterWrapper(this)
```

そして `DisasterAI.IsStillEmerging`（base）は**その null を正しくチェックする**:
```
IL_0005  ldfld DisasterManager::m_DisasterWrapper
IL_000A  brfalse → IL_0020
IL_001A  callvirt DisasterWrapper::IsCustomDisaster
IL_0020  ldc.i4.0 ; ret                    // DLC 無しなら常に false
```
→ **ND DLC が無い環境で `DisasterData` を作っても、`IDisastersExtension` 経由の
「カスタム災害だから位相を進めない」罠（②§A-1 で既出）は発生しない。** クラッシュもしない。

`PrefabCollection<DisasterInfo>` の public static API（実測）:
```
PrefabCount() / GetPrefab(uint) / PrefabName(uint) / LoadedCount() / GetLoaded(uint) /
FindLoaded(string) / LoadedExists(string) /
InitializePrefabs(string, T[], string[]) / InitializePrefabs(string, T, string) /
DestroyPrefabs / DestroyAll / BindPrefabs /
Begin/EndSerialize / Begin/EndDeserialize / Deserialize(bool)
```
→ **実行時にプレハブを登録する口（`InitializePrefabs`）は public で存在する。**

**確定していないこと（PARTIAL）:**

1. **ND DLC 無しの環境に `DisasterInfo` プレハブが 1 つでも存在するか。**
   `StructureFireAI` / `StructureCollapseAI` は基本ゲームの建物火災・倒壊に対応しそうだが、
   **どの `DisasterInfo` プレハブが基本ゲームに同梱され、どれが DLC かは DLL からは判定できない**
   （プレハブはアセットバンドル側にある）。
   **決め方:** ND 無しの環境で `PrefabCollection<DisasterInfo>.PrefabCount()` と
   各 `GetPrefab(i).m_disasterAI.GetType().Name` をログに出す。Phase 0.5 の `DiagnosticDump` に数行。
   **これを確かめる前に「災害パネルに火山を出す」設計を書かないこと。**
2. 実行時に作った `DisasterInfo`（`m_IconAtlas` / `m_Icon` / `m_disasterWarningGuide` /
   `m_warningBroadcast` / `m_activeBroadcast` がすべて null）を
   `DetectDisaster` / `FollowDisaster` / 災害パネルに通したときに落ちないか。**未検証。**

**設計上の推奨（率直に）:**

- **`DisasterData` に載せない構成を既定にすること。** ⑤の火山は
  「地形を書き換える」「エフェクトを描く」「建物に火をつける」だけで完結し、
  そのどれも `DisasterManager` を必要としない。
  ③火災旋風が `FireWhirlRegistry` を自前で持ったのと同じ構造（`Core` にレジストリ、
  `Game` に sim tick のドライバ、`ISerializableDataExtension` で永続化）で足りる。
- 通知・カメラ追従・災害パネルが欲しくなったら**別タスクとして切り出す**。
  上の 1. と 2. を実機で確かめてからでないと、②の `SelfTrigger` と同種の罠に踏み込む。
- ②§A-1 の起動レシピ（`CreateDisaster` → `SelfTrigger` → `StartNow`、戻り値必須）と
  §E-1 の 256 上限は、載せる決定をした場合にそのまま適用される。

### D-12. sim スレッドでの安全性とバッチ — CONFIRMED

`BeginUpdateArea` / `EndUpdateArea` の**呼び出し元はアセンブリ全体で `SimulationManager.SimulationStep` の 1 箇所だけ**
（全メソッドのトークン走査）。その構造（IL 長 669）:

```
IL_0000  call TerrainModify::BeginUpdateArea                      ★ メソッドの最初の命令
IL_0010–0059  m_simulationActions を Dequeue → AsyncTaskBase.Execute
                                                                  ← SimulationManager.AddAction はここで走る
IL_00BE  ThreadingWrapper::OnBeforeSimulationTick                 ← IThreadingExtension
IL_0132  ThreadingWrapper::OnBeforeSimulationFrame
IL_0139  m_currentFrameIndex++
IL_01A0–0200  m_dayTimeFrame / m_currentDayHour / m_isNightTime   （②§F-1 既出）
IL_024E  foreach (m in m_managers) m.SimulationStep(subStep)      ← TerrainManager.SimulationStepImpl もここ
IL_0276  ThreadingWrapper::OnAfterSimulationFrame
IL_028C  ThreadingWrapper::OnAfterSimulationTick                  ← IThreadingExtension
IL_0296  call TerrainModify::EndUpdateArea                        ★ 末尾
```

**含意（重要）:**

1. **MOD が sim スレッドで使えるフック（`SimulationManager.AddAction` / `OnBeforeSimulationTick` /
   `OnBeforeSimulationFrame` / `OnAfterSimulationFrame` / `OnAfterSimulationTick`）は
   すべて `Begin`〜`End` の内側にある。** したがって `m_modifyingLevel >= 1` であり、
   **MOD の `UpdateArea` は自動的に外側のバッチに載り、tick の末尾（IL_0296）で 1 回だけフラッシュされる。**
   → **⑤は `BeginUpdateArea` / `EndUpdateArea` を自分で呼ぶ必要が無い。**
   呼んでも `m_modifyingLevel` が 1 増えて 1 減るだけで、`EndUpdateArea` の
   `if (loc1 != 0) return`（IL_0017）により**内側の `End` は何もしない**。害は無いが意味も無い。
2. **ただし単発の要求面積が 10000 セルを超えると、入れ子の有無に関係なく即フラッシュする**（§A-1 の IL_0399–03AB）。
   バッチ化の恩恵を受けたいなら 1 回の `UpdateArea` を 100×100 セル未満に保つこと。
3. **`DisasterHelpers.MakeCrater` / `MakeCrack` は先頭で `RefreshAllModifications()` を呼ぶ。**
   これは `UpdateAreaImplementation` そのものなので、**tick の途中で外側のバッチを強制フラッシュする**
   （＝その tick でバニラのマネージャが溜めた分まで巻き込んで吐き出す）。
   `MakeCrater` を毎 tick 呼ぶ設計は、バニラのバッチ最適化を毎 tick 無効化するのと同じである。
4. **メインスレッド（`IThreadingExtension.OnUpdate` / `ToolBase.OnToolUpdate` / UI コールバック）から
   `UpdateArea` を呼ぶと `m_modifyingLevel == 0` なので毎回フラッシュする。**
   ③以来の規約（地形・災害・バッファ操作は sim スレッドへ寄せる）は⑤でも守ること。

**同期の実測:**

- `TerrainManager.m_updateLock`（`private object`）。
- `UpdateAreaImplementation` はパッチごとに
  `Monitor.TryEnter(TerrainPatch.m_modifiedLock, SimulationManager.SYNCHRONIZE_TIMEOUT)`（IL_100F）→
  `TerrainPatch.AddArea` ×3 → `m_tmpDetailIndex = m_simDetailIndex` → `Monitor.Exit`（IL_10A5）。
- **`m_rawHeights` 自体にはロックが無い。ABSENT。**
  バニラで `m_rawHeights` に書くのは
  `Awake` / `Data.Deserialize` / `SetHeightMap*` / `SetRawHeightMap` / `TerrainWrapper.SetHeights` /
  `TerrainTool.ApplyBrush` / `TerrainTool.ApplyUndo` / `DistrictTool.ApplyTerrainBrush` /
  `DisasterHelpers.MakeCrack` / `DisasterHelpers.MakeCrater` の 10 箇所だけで、
  **sim スレッドで走るのは `MakeCrack` / `MakeCrater` のみ**（残りはメインスレッドかロード時）。
  → **`RawHeights` は「書いた人が `UpdateArea` を呼ぶまで誰も読まない」という規律で守られている。**
  ⑤も同じ規律を守る（sim スレッドで書き、同じ tick 内で `UpdateArea` する）。
- 一時停止中でも危険は無い。`SimulationStep` はゲームが一時停止していても回る（`m_simulationActions` の
  ドレインがそこにある）ので、**一時停止中に `AddAction` から書いても `Begin/End` の内側であることは変わらない。**



---

## E. 共存と安全

### E-13. 可逆性 — CONFIRMED（自前で持つしかない）

**`BackupHeights` と `UndoBuffer` は `TerrainTool` / `DistrictTool` の専有物である。**
`get_BackupHeights` / `get_UndoBuffer` / `get_RawHeights` 等の public プロパティ getter を
全メソッドのトークン走査で追った結果、利用者はこれだけ:

```
get_BackupHeights : TerrainTool::OnEnable / ResetUndoBuffer / EndStroke / ApplyUndo / ApplyBrush
                    DistrictTool::ApplyTerrainBrush
get_UndoBuffer    : TerrainTool::GetFreeUndoSpace / EndStroke / ApplyUndo
get_RawHeights    : DisasterHelpers::MakeCrater / MakeCrack / TerrainWrapper::SetHeights /
                    TerrainModify::UpdateAreaImplementation / TerrainTool::(4) / DistrictTool::ApplyTerrainBrush
get_RawHeights2   : TerrainModify::UpdateAreaImplementation / TerrainModify::ApplyQuad
get_FinalHeights  : TerrainModify::UpdateAreaImplementation / TerrainPatch::Refresh /
                    TerrainWrapper::GetHeights / ImmaterialResourceManager::AddObstructedResource /
                    TerrainTool::ApplyBrush
get_BlockHeights  : TsunamiAI::UpdateHazardMap / TsunamiAI::FindSea
get_BlockHeightTargets : TerrainModify::UpdateAreaImplementation / TerrainPatch::Refresh
```

`TerrainTool.OnEnable` と `ResetUndoBuffer` が `RawHeights` → `BackupHeights` を丸ごと複製する。
→ **MOD が `BackupHeights` を自分の退避先に使うと、プレイヤーが整地ツールを開いた瞬間に上書きされて消える。使ってはいけない。**
`m_undoBuffer` も同様（`TerrainTool.EndStroke` がストローク差分を積み、`OnEnable` でリセットされる）。

**⑤が自前で持つ場合の実費**（`ushort` × 影響セル数、セル = 16 m）:

| 火山の半径 | 影響 raw セル | 退避サイズ | 備考 |
|---|---|---|---|
| 350 m（溶岩ドーム） | (44+1)² = 2025 | **4.0 KB** | |
| 1000 m | (125+1)² = 15876 | **31.0 KB** | |
| 2000 m（楯状の裾） | (250+1)² = 63001 | **123.0 KB** | 1 フラッシュの上限 128×128 と同オーダー |
| 3000 m | (375+1)² = 141376 | 276.1 KB | 隆起を複数フラッシュに分割する必要あり |

→ **どの規模でも 1 MB に届かない。** 素直に「影響矩形の `RawHeights` を隆起開始前にコピーして持つ」でよい。
セーブに残すなら `ISerializableDataExtension`（firewhirl 付録 A-2b のロード順の注意がそのまま適用される）。

**注意 2 点:**
1. **復元は「元に戻す」ではなく「上書きする」。** 退避後にプレイヤーが整地ツールで同じ場所を触っていたら、
   復元でその編集も消える。⑤が「火山を消す」機能を出すなら、その旨を UI に書くこと。
2. **`m_blockHeights` の追随は下降 8 m / 64 フレーム**（§A-2）なので、
   `RawHeights` を一瞬で戻しても「建てられる地面」は 600 m なら 75 × 64 = 4800 フレーム ≒ 1.76 ゲーム内時間かけて下がる。

### E-14. Natural Disasters Renewal との衝突 — CONFIRMED（衝突しない）

②`2026-08-14-earthquake-il-facts.md` §E-2 で確定した NDR のパッチ面は
**`DisasterHelpers.DestroyBuildings` と `DisasterHelpers.DestroyNetSegments` の 2 つだけ**で、
災害種別は引数値の嗅ぎ分け（`probability == 0.02f` → 地震、`burnRadiusMin == 0 && burnRadiusMax == 0` → 竜巻）。

⑤が使う API を `DisasterHelpers` の全メソッド一覧（`Dump-Type` 実測）と突き合わせる:

```
DisasterHelpers（すべて public static）
  SplashWater / MakeCrater / MakeCrack                                     ← ⑤が使う。パッチ対象外
  BurnGround                                                               ← ⑤が使う。パッチ対象外
  DestroyStuff ×2 / DestroyBuildings ×2 / DestroyNetSegments / DestroyTrees
  DestroyProps / UpgradeBuildings / RemovePeople / SavePeople
  AddWind / AddWindCitizens / AddWindVehicles
  DestroySegment（private）
```

| ⑤が触るもの | NDR が触るか |
|---|---|
| `DisasterHelpers.MakeCrater` / `MakeCrack` / `BurnGround` | **触らない** |
| `TerrainModify.*` / `TerrainManager.*` / `TerrainManager.RawHeights` | **触らない** |
| `BuildingAI.BurnBuilding` / `BuildingAI.CollapseBuilding` を直接呼ぶ | **触らない**（③以来の方針どおり） |
| `NaturalResourceManager` / `EffectInfo.RenderEffect` / `EffectCollection` | **触らない** |
| `DisasterManager.CreateDisaster` / `DisasterAI.StartNow` | **触らない** |

→ **⑤は `DisasterHelpers.DestroyBuildings` / `DestroyNetSegments` を一度も呼ばないので、NDR との衝突は無い。**
③以来の規約（`DisasterHelpers` を経由せず `BuildingAI` を直接呼ぶ）を守ればそれで足りる。
NDR は「災害がいつ・どれだけ強く起きるか」を変えるが、⑤の火山はバニラの災害テーブルに乗らないので
発生確率の干渉も無い（→ §D-11 の結論しだい）。

**ただし NDR 以外に、⑤特有の共存相手が 2 つある:**

1. **地形ツール（`TerrainTool`）と地区ツール（`DistrictTool`）。**
   §E-13 のとおり `BackupHeights` / `UndoBuffer` を専有し、`OnEnable` で `RawHeights` 全体を複製する。
   ⑤が隆起の途中でプレイヤーが整地ツールを開いても壊れはしないが、
   **プレイヤーの「元に戻す」が⑤の隆起まで巻き戻す**（`ApplyUndo` は `undoBuffer → rawHeights` を書いて `UpdateArea`）。
   逆に⑤の復元がプレイヤーの整地を消す。**どちらも仕様として受け入れるしかない。UI に一言書くこと。**
2. **地形を書き換える他 MOD**（`TerrainWrapper.SetHeights` を使うもの、および `Terraform`/`Extra Landscaping Tools` 系）。
   `TerrainWrapper.SetHeights` は `RawHeights` を直接書いて `UpdateArea` する公式 API。
   ⑤の退避配列は「隆起開始時点のスナップショット」なので、途中で他 MOD が同じ矩形を触ると復元で上書きされる。

---

## F. 道路セグメントのグリッド（⑤ T4 で追加実測）

### F-15. `NetManager` のセグメントグリッド — CONFIRMED（全文読了）

⑤の T4（影響範囲の調査）は「範囲内の道路が何本か」を数える。建物グリッドの
`64 / 135 / [0,269]` に対応する 3 つの数値は、本プロジェクトのどの事実文書にも
無かったので、ここで確定させた。**推測していない。**

**確保（`NetManager.Awake`）:**

```
IL_0017: ldc.i4 36864     newobj Array16`1::.ctor   stfld NetManager::m_segments
IL_0077: ldc.i4 72900     newarr UInt16             stfld NetManager::m_segmentGrid
```

→ `m_segmentGrid` は `ushort[72900]` = **270 × 270**。セグメントバッファは **36864**
（連結リストを辿る保険の上限はこれ）。`m_nodeGrid` も同じ 72900 である。

**セルへの登録（`NetManager.InitializeSegment`、IL_0046–IL_00B6）:**

```
pos = (m_nodes.m_buffer[m_startNode].m_position
     + m_nodes.m_buffer[m_endNode].m_position) * 0.5f
x   = Mathf.Clamp((int)(pos.x / 64f + 135f), 0, 269)
z   = Mathf.Clamp((int)(pos.z / 64f + 135f), 0, 269)
idx = z * 270 + x
m_segments.m_buffer[id].m_nextGridSegment = m_segmentGrid[idx]
m_segmentGrid[idx] = id
```

→ **建物グリッド（セル 64・オフセット 135・`[0,269]`・`z*270+x`）と完全に同じ形**である。
`LongPeriodDamage` / `TyphoonWind` の走査をそのまま写してよい。

**フィールド:**

| フィールド | 型 | 用途 |
|---|---|---|
| `NetSegment.m_nextGridSegment` | `UInt16`（public instance） | 同一セルの次のセグメント。建物の `m_nextGridBuilding` に対応 |
| `NetSegment.m_flags` | `NetSegment.Flags`（`Int32` 基底） | `Created=1` / `Deleted=2` / `Original=4` / `Collapsed=8` / `Untouchable=0x20`。**`Demolishing` は無い**（建物側にはある） |
| `NetSegment.m_middlePosition` | `Vector3` | 位置を取る最も安いフィールド。`NetSegment.UpdateBounds`（IL_01DB）が**2 本のベジェの中点の平均**として書く |
| `NetSegment.m_bounds` | `Bounds` | 同じく `UpdateBounds` が書く AABB |

> ★★ **セルを決める位置（両端ノードの中点）と `m_middlePosition` は同じではない。**
> 曲がった道路ではベジェの中点がノードの中点から離れる。したがって
> 「矩形のセルを走査して `m_middlePosition` で距離を測る」を素直に書くと、
> **範囲の縁にある曲線道路を取りこぼす**。⑤は矩形を ±2 セル（128 m）広げてから走査する
> （`VolcanoSurvey.SegmentGridMargin`）。
>
> どちらの中点も、**長い 1 本の道路の一部だけが範囲に掛かる場合**を正しく表せない。
> ⑤がこの数を「概数」としてしか出さない理由の 1 つがこれである（設計書 §7.2）。

**再現手順:**

```powershell
. docs\tools\ilload.ps1 ; . docs\tools\ildasm.ps1
$bf = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
Disasm-Method -Method $global:A.GetType('NetManager').GetMethod('Awake', $bf)
Disasm-Method -Method $global:A.GetType('NetManager').GetMethod('InitializeSegment', $bf)
```

---

## G. 準備段の破壊経路（⑤ T5 で追加実測）

### G-16. `demolish: true` は何をするのか — CONFIRMED（全経路読了）

設計書 付録と計画 T5 Step 1 が「未確定」と名指ししていた 2 項目
（**道路の破壊経路**と、**`demolish:false` を断る AI が `demolish:true` を通すか**）を
ここで確定させた。**推測していない。**

**(a) 道路 — 実体は `PlayerNetAI.CollapseSegment` 1 本。本当に解放する。**

```
RoadBaseAI.CollapseSegment(id, ref seg, group, demolish)
  IL_0000  demolish == true -> PlayerNetAI::CollapseSegment へ委譲

PlayerNetAI.CollapseSegment(id, ref seg, group, demolish)
  IL_0000  demolish == false -> NetAI::CollapseSegment（= ldc.i4.0 ; ret）
  IL_000F  (m_flags & 32 Untouchable) なら
           NetSegment::FindOwnerBuilding(id, 363f)
           -> Building::m_parentBuilding を (m_flags & 16 Untouchable) の間だけ遡る
              （鎖の保険は 49152。超えると "Invalid list detected!"）
  IL_00C2  所有建物が Collapsed(0x400000) でないなら
           ai.CollapseBuilding(owner, ref b, group, testOnly:true, demolish:false, 0)
           -> false なら IL_00F8 で **セグメントごと断る**（return false）
  IL_00FA  NetManager::ReleaseSegment(id, keepNodes:false)    ★ 本当に解放する
  IL_010C  所有建物があれば
           ai.CollapseBuilding(owner, ref b, group, testOnly:false, demolish:false, 0)
           を本番で 1 回（戻り値は捨てる）
  IL_0147  return true
```

`CollapseSegment` の override は 19 型。**`demolish: true` の扱いは 3 通りしかない**
（全 19 型の先頭を逆アセンブルして確認）:

| 扱い | 型 |
|---|---|
| `PlayerNetAI` へ委譲（直接または `RoadBaseAI` / `TrainTrackBaseAI` 経由） | `CableCarPathAI` / `MetroTrackBaseAI` / `MetroTrackTunnelAI` / `MonorailTrackAI` / `PedestrianBridgeAI` / `PedestrianPathAI` / `PedestrianWayAI` / `PowerLineAI` / `RoadBaseAI` / `RoadTunnelAI` / `RunwayAI` / `TaxiwayAI` / `TrainTrackBaseAI` / `TrainTrackTunnelAI` |
| 同じ本体をインラインで持つ（`FindOwnerBuilding` → `ReleaseSegment`） | `DamAI` / `DecorationWallAI` |
| **断る** | `SupportCableAI`（`demolish: true` を基底 `NetAI::CollapseSegment` へ渡す＝必ず false）／ `NetAI` そのもの |

**`NetManager.ReleaseSegment(UInt16, Boolean)` は public / instance / 非 virtual。**

**(b) 最後のセグメントが消えたときノードはどうなるか — 解放される。**

```
NetManager.ReleaseSegment(id, keepNodes)
  -> PreReleaseSegmentImplementation / ReleaseSegmentImplementation
     -> ReleaseSegmentNode(id, ref node, keepNodes)
        IL_0019  NetNode::RemoveSegment(id)
        IL_0020  keepNodes なら以下を飛ばす
        IL_0038  (node.m_flags & 512 Untouchable) なら残す（建物が持つノード）
        IL_005A  NetNode::CountSegments() == 0 なら
                 NetManager::ReleaseNodeImplementation(node)      ★ 孤児ノードは残らない
        それ以外  NetManager::UpdateNode(node, id, 1)
        IL_007B  ref node = 0
```

`PlayerNetAI` は `keepNodes: false` を渡すので、**⑤の経路では孤児ノードが残らない**。
`NetNode.Flags.Untouchable = 512`（`Enum.GetNames` 実測）。
なお `NetManager::ReleaseNodeImplementation` は `BuildingManager.ReleaseBuilding` を
呼びうる（全メソッド走査で確認）ので、**道路の解放が建物を巻き込むことがある** ——
建物の走査側は「次の ID を行動前に控える」規律を必ず守ること。

**(c) ★ `Collapsed` を立てただけでは地形固定が止まらない。**

```
NetSegment.TerrainUpdated : IL_0000  (m_flags & 3) != 1 なら ret
                             -> Created(1) かつ Deleted(2) でないことだけを見る。
                                Collapsed(8) は見ていない。
Building.TerrainUpdated    : IL_0000  (m_flags & 524291) != 1 なら ret
                             -> 524291 = Created(1) | Deleted(2) | Demolishing(0x80000)。
                                Collapsed(0x400000) は見ていない。
```

> **これが「④の風害は `demolish:false`、⑤の準備は `demolish:true`」の IL 上の理由である。**
> 倒壊フラグを立てただけの道路と建物は `ApplyQuad` を出し続け、
> §A-2 のとおりセルを自分の高さへ固定し続ける。
> **道路は解放されなければならず、建物は `Demolishing` が立たなければならない。**
>
> ちなみに `Building.Flags` の `0x80000` は `Demolishing` であって `Untouchable`（= 16）ではない。
> §A-2 の括弧書き「`Created` かつ `Deleted`/`Untouchable` でない」は言い方が不正確で、
> 正しくは `Created` かつ `Deleted` でも `Demolishing` でもない、である。

**(d) `demolish:false` を断る 5 つの AI は `demolish:true` を通す。**

④ §F-2 が読んだのは `demolish: false` のときの挙動だけだった。全文を読み直した結果:

```
ShelterAI / DoomsdayVaultAI / DamPowerHouseAI / TsunamiBuoyAI :
  IL_0000  demolish(arg5) なら CommonBuildingAI::CollapseBuilding へそのまま委譲
  IL_0017  でなければ return false

DecorationBuildingAI :
  IL_0000  demolish なら -> IL_0007 testOnly でなければ m_flags |= 524288 (Demolishing)
                            IL_0020 return true
  IL_0022  でなければ return false
```

→ **5 つとも `demolish: true` は受け付ける。** 「防災施設の足元だけ地形が元の高さで残る」
は起きない。`DecorationBuildingAI` は `CommonBuildingAI` を通さず `Demolishing` を
立てるだけだが、(c) のとおりそれで地形固定は止まる。

`CommonBuildingAI.CollapseBuilding` 本体で `demolish` が効く箇所:

```
IL_0007  (m_flags & 0x400000 Collapsed) なら -> IL_0210 へ
IL_0013  testOnly なら return true
IL_0025  m_fireIntensity = 0                        ★ ⑤は自分では書かない（罠 5）
IL_0031  m_flags = (m_flags & 0x7FFFFFFF) | Collapsed
IL_0049  demolish なら m_flags |= 524288 (Demolishing)、problems をクリア
IL_0132  group が null なら災害集計を飛ばす          ★ null 安全
IL_0170  constructState != 0 なら InstanceManager::SetGroup(id, group)
IL_02A3  親を持たない建物は m_subBuilding の鎖に同じ引数で再帰（上限 49152）
IL_0210  （既に Collapsed のとき）demolish かつ Demolishing がまだなら
         testOnly で true、でなければ Demolishing を立てて true
IL_0284  フラグが 1 ビットも変わらなければ return false（＝冪等）
```

→ **既に `Collapsed` の瓦礫にも `demolish: true` は効き、`Demolishing` を立てて true を返す。**
⑤が候補マスクから `Collapsed` を弾いてはいけない理由がこれである（②④は弾いていた）。

`Demolishing` の建物を実際に配列から消すのは `CommonBuildingAI.SimulationStep` /
`DecorationBuildingAI.SimulationStep`（`BuildingManager.ReleaseBuilding` の全呼び出し元を
走査して確認）。**⑤は解放を自分では呼ばない。**

**(e) `InstanceManager.Group` は `null` を渡してよい。**

⑤は災害スロットに載らない（§D-11）ので束ねる先が無い。null 検査は 3 箇所とも在る:

```
CommonBuildingAI.CollapseBuilding : IL_0132  ldarg.3 ; brfalse -> 災害集計を飛ばす
RoadBaseAI.CollapseSegment        : IL_004F  ldarg.3 ; brfalse -> 災害集計を飛ばす
InstanceManager.SetGroup(id, g)   : IL_005E  g が null なら m_groups から Remove
                                    IL_0096  未登録かつ g が null なら何もしない
```

→ **空の `Group` を new して `m_ownerInstance` を空のまま渡すより、`null` のほうが実測どおり。**

**再現手順:**

```powershell
. docs\tools\ilload.ps1 ; . docs\tools\ildasm.ps1
$bf = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$d  = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly'
Disasm-Method -Method $global:A.GetType('PlayerNetAI').GetMethod('CollapseSegment', $bf)
Disasm-Method -Method $global:A.GetType('NetManager').GetMethod('ReleaseSegmentNode', $bf)
Disasm-Method -Method $global:A.GetType('ShelterAI').GetMethod('CollapseBuilding', $d)
Disasm-Method -Method $global:A.GetType('CommonBuildingAI').GetMethod('CollapseBuilding', $d)
Disasm-Method -Method $global:A.GetType('InstanceManager').GetMethod('SetGroup', $bf)
# NetSegment / Building の TerrainUpdated は先頭 10 命令だけでよい
```

---

## H. 噴火・溶岩・描画（⑤ T7-T9 で追加実測）

### H-17. 借りたエフェクトは音を鳴らさない — **CONFIRMED（計画 §7.1 の根拠を訂正する）**

計画 T7 §7.1 は「`m_fireEffect` は `FireEffect` 合成型で `SoundEffect` を子に持つので、
`RenderEffect` を通せば**音も一緒に出る**」と書いている。**IL はそれを否定した。**

```
FireEffect.RenderEffect(InstanceID, SpawnArea, Vector3, float, float, float, float, CameraInfo)
  IL_003A  ldfld FireEffect::m_particleEffect   -> ParticleEffect::EmitParticles
  IL_0088  ldfld FireEffect::m_lightEffect      -> RenderManager::get_lightSystem
                                                  -> LightSystem::DrawLight
  ★ m_soundEffect への参照は 1 つも無い（全 IL 読了）

FireEffect の宣言メソッドは 6 本:
  RenderEffect / **PlayEffect(InstanceID, SpawnArea, Vector3, float, float,
                              AudioManager.ListenerInfo, AudioManager.AudioGroup)** /
  CreateEffect / DestroyEffect / RequireRender / RequirePlay
```

→ **音は `PlayEffect` の側にある。** `AudioManager.CurrentListenerInfo`（public プロパティ、
型 `AudioManager+ListenerInfo`）と `AudioManager.EffectGroup` / `DefaultGroup` / `AmbientGroup`
（いずれも public プロパティ）は到達できるので、鳴らすこと自体は可能である。

**⑤は鳴らさない。** 計画の決定（音の経路を新設しない）は変えていないが、**理由が違う** ——
「`RenderEffect` で出るから足さない」のではなく、「`PlayEffect` は毎フレーム呼ぶ想定の API ではなく、
`ListenerInfo` / `AudioGroup` の扱いに本 MOD の前例が無いから」である。
**噴火は無音であり、それを診断とパネルとチェックリストで名乗る。**

### H-18. `RenderEffect` の引数と `CameraInfo` の到達経路 — CONFIRMED

```
public virtual Void EffectInfo.RenderEffect(InstanceID, EffectInfo+SpawnArea,
        Vector3 velocity, Single acceleration, Single magnitude,
        Single timeOffset, Single timeDelta, RenderManager+CameraInfo)
    基底の実装は IL_0000 ret（＝何もしない）。実体はサブクラス側。

public EffectInfo+SpawnArea..ctor(Vector3 position, Vector3 direction, Single radius)   ★ public

public RenderManager+CameraInfo RenderManager.CurrentCameraInfo { get; }
    IL_0001  ldfld RenderManager::m_cameraInfo ; ret      （public / instance / 非 static）

public BuildingProperties BuildingManager.m_properties     （public フィールド）
public EffectInfo BuildingProperties.m_fireEffect          （public フィールド）
```

`magnitude` の意味（呼ばれ方から確定）:

```
FireEffect.RenderEffect     : EmitParticles(id, area, velocity, timeDelta*0.01,
                                            RoundToInt(magnitude*100), ...)
ParticleEffect.RenderEffect : EmitParticles(id, area, velocity, magnitude*timeDelta*0.01,
                                            100, ...)
```
→ **`float` 引数が「時間ぶんの噴出量」、`int` 引数が「強さの百分率」**である。
バニラの建物火災は `magnitude = m_fireIntensity / 255`（§B-5）＝ **0.0–1.0**。⑤も同じ帯を使う。

`ParticleEffect.RenderEffect` は先頭で `CameraInfo::CheckRenderDistance` と `CameraInfo::Intersect`
を呼ぶので、**`CameraInfo` に null を渡すと NRE になる。**

### H-19. `SampleDetailHeight` の勾配は「上り方向」で、単位は無次元 — **CONFIRMED**

計画 §8.1 と設計書 付録が「未確定」と名指ししていた項目である。**読んだ。**

```
TerrainManager.SampleDetailHeight(float x, float z, out slopeX, out slopeZ)
  h00 = GetDetailHeight(x0,   z0  )   (loc 9)
  h10 = GetDetailHeight(x0+1, z0  )   (loc 10)
  h01 = GetDetailHeight(x0,   z0+1)   (loc 11)
  h11 = GetDetailHeight(x0+1, z0+1)   (loc 12)
  IL_0071  *slopeX = (h10 + h11 - h00 - h01) * 0.5    // 平均 (h(x+1) - h(x))
  IL_0084  *slopeZ = (h01 + h11 - h00 - h10) * 0.5    // 平均 (h(z+1) - h(z))
  戻り値は SmoothSample ではなく Lerp 3 回の双線形補間（raw のまま）

TerrainManager.SampleDetailHeight(Vector3, out slopeX, out slopeZ)
  IL_0033  戻り値  *= 0.015625      // 1/64        -> メートル
  IL_003B  *slopeX *= 0.00390625    // (1/64)/4 m  -> **無次元（m/m）**
  IL_0045  *slopeZ *= 0.00390625
```

→ **`(slopeX, slopeZ)` は勾配（上り方向）である。下り方向は `(-slopeX, -slopeZ)`。**
単位は無次元なので、`0.002` はそのまま「0.2 % の傾き」を意味する。
**それでも実行時の観測はやめない**（⑤は最初の 8 歩で標高が上がったら流れを止めて名乗る）。

### H-20. `TerrainManager.HasWater(Vector2)` は sim スレッド専用 — CONFIRMED

```
public Boolean TerrainManager.HasWater(Vector2 position)
  IL_00B7  m_waterSimulation.BeginRead()      ★ try/finally で EndRead
  セル座標: FloorToInt((v + 8640) * 16) >> 8 を [0,1080] にクランプ、添字 z*1081 + x
  水面   = m_blockHeights[i] + WaterSimulation.Cell::m_height（m_height == 0 のセルは無視）
  地形   = m_rawHeights2 の双線形補間
  IL_027B  return (水面 - 地形) >= 8          // raw 8 = 0.125 m
```
→ ②の `TsunamiChain.IsUnderWater` と同じ扱い（**main スレッドから呼ばない**）。
`m_blockHeights` 経由なので §A-2 の追随遅れをそのまま受ける。

### H-21. 樹木グリッドの寸法とワールド座標の対応 — CONFIRMED

```
TreeManager.TREEGRID_RESOLUTION = 540（const）   TREEGRID_CELL_SIZE = 32（const）
TreeManager.m_treeGrid : uint32[]                m_trees : Array32<TreeInstance>（262144）
TreeInstance : m_nextGridTree(uint32) / m_posX,m_posZ(int16) / m_posY(uint16) /
               m_flags(uint16) / m_infoIndex(uint16)
TreeInstance.Flags: None Created(1) Deleted(2) Hidden Single FixedHeight FireDamage(64) Burning(128)

TreeInstance.set_Position（ToolController.m_mode != 4、＝ゲームモード）:
  IL_0096  m_posX = Clamp(RoundToInt(world.x * 3.792593), -32767, 32767)
TreeManager.InitializeTree（同モード）:
  IL_005F  cell = Clamp((m_posX + 32768) * 540 / 65536, 0, 539)
  IL_00A1  index = cellZ * 540 + cellX
```
→ **ワールド → セルは `world / 32 + 270`**（建物グリッドの `/64 + 135` とは別の値）。
アセットエディタ（`m_mode == 4`）だけ `m_posX` の縮尺が 16 倍になり、`InitializeTree` も先に 16 で割る。

`TreeManager.BurnTree(uint, InstanceManager.Group, int)` は **`group` に null を渡してよい**
（`IL_0039 ldarg.2 brfalse` で災害集計を飛ばし、`InstanceManager.SetGroup` は null 安全。§G-16 (e)）。

### H-22. このビルドに実在する粒子シェーダ名 — CONFIRMED（アセット走査）

`Cities_Data` 以下の 378 ファイルを ASCII で全走査した:

```
'Particles/Additive'                      globalgamemanagers / resources.assets   ★ 在る
'Particles/Alpha Blended'                 globalgamemanagers / resources.assets   ★ 在る
'Legacy Shaders/Particles/Additive'       0 件
'Legacy Shaders/Particles/Alpha Blended'  0 件
```

→ **`Shader.Find("Particles/Additive")` がこのビルドで解決する名前である。**
`globalgamemanagers` に載っているのは「常に含めるシェーダ」の一覧なので、実行時に確実に読める。
`Legacy Shaders/...` は**このビルドには無い**（③④の多段フォールバックの 2 段目は、
将来のビルドに対する保険として残す価値はあるが、現状は 1 段目で決まる）。

> **`Shader.Find("Standard")` を「解決した」の検査に混ぜないこと。**
> Unity の組み込みで実質必ず非 null なので、`… || Shader.Find("Standard") != null` という
> 検査は**構造上 1 度も失敗できない**（④のレビューが同じ欠陥を見つけている）。

**再現手順:**

```powershell
. docs\tools\ilload.ps1 ; . docs\tools\ildasm.ps1
$bf = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$d  = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly'
Disasm-Method -Method ($global:A.GetType('FireEffect').GetMethods($d) | ? {$_.Name -eq 'RenderEffect'})
Disasm-Method -Method $global:A.GetType('TerrainManager').GetMethod('SampleDetailHeight', $bf, $null,
    @([float],[float],[float].MakeByRefType(),[float].MakeByRefType()), $null)
Disasm-Method -Method $global:A.GetType('TerrainManager').GetMethod('HasWater', $bf, $null,
    @([UnityEngine.Vector2]), $null)
Disasm-Method -Method $global:A.GetType('TreeManager').GetMethod('BurnTree', $bf)
# シェーダ名は Cities_Data 以下を ASCII でバイト検索する（IL ではない）
```


---

## 設計への含意

依頼文:
> 火山を実装してほしい。地形変化を伴う災害。クリックした位置に火山ができ、溶岩が流れるアニメーションと、
> 溶岩が通った経路に応じた火災が起きる。火山の生成は段階的に——地面が盛り上がり、最後に山頂の火口からマグマが噴出する。
> 火山の形はオプションから 3 種類（楯状火山 / 成層火山 / 溶岩ドーム）選べるようにする。

6 つの要求に仕分ける。

### 1. クリック位置に火山 — **安い。既にある**

`Game/UI/FireWhirlPlacementTool.cs` がそのまま雛形になる。地形にコライダーが無いので
`Physics.Raycast` は使えず、③は `Core/Common/RayGeometry.IntersectTerrain` を自前で書いた。
**バニラには `TerrainManager.RayCast(Segment3, out Vector3)`（public）があり（§B-6）、
`Bounds(center (0,512,0), size (17280,1024,17280))` でクリップしてから raw セルをマーチする。**
③のものを流用してもよいし `RayCast` に寄せてもよい。どちらでもコストはほぼゼロ。

**必ず守ること:** 付録 A の既出事項 —— `ToolsModifierControl.SetTool<T>()` は
`ToolController` に事前登録していないと**黙って空振りする**。③の `ToolRegistration` と
`FireWhirlPlacementTool.Activate()`（`controller.GetComponent<T>()` を取って `controller.CurrentTool` に代入）の形をそのまま使う。
また災害・地形の生成は `SimulationManager.instance.AddAction` で sim スレッドへ回す（③と同じ）。

### 2. 段階的な隆起 — **中。ただし 4 つの落とし穴がある**

技術的には成立する（§A-1, §C-8, §C-9, §D-12）。落とし穴:

| # | 落とし穴 | 対処 |
|---|---|---|
| 1 | **1 tick あたりのセル高さ増分が 1/64 m = 0.015625 m 未満だと丸めで消え、無言で完全停止する**（§C-8 の `if (n != raw)`） | 「1 tick の増分 × 64 ≥ 1 raw 単位」を Core 側の不変条件にしてテストする |
| 2 | **`UpdateArea` は 128×128 raw セル（2048 m 角）を超えた分を無言で切り捨てる**（タイル分割しない、§A-1） | 半径 1024 m を超える火山は矩形を自分で分割する。Core にタイル分割器を書く |
| 3 | **10000 セル超の単発 `UpdateArea` は必ず即フラッシュ**。`MakeCrater` は呼ぶたびに `RefreshAllModifications()` で強制フラッシュ | 1 tick 1 回、100×100 セル未満に保つ。または `RawHeights` を自前で書いて `UpdateArea` を 1 回にまとめる |
| 4 | **`m_flattenTerrain == false` の建物は動くたびに自分の `UpdateArea` を積む**（§A-3 のフィードバックループ） | 隆起の tick 間隔を粗くする（例: 16 sim フレームに 1 回）。フットプリント内の建物数を診断に出す |

**演出上の上限:** `m_blockHeights` は**ゲームモードで 2 m / 64 sim フレーム**しか追随しない（§A-2）。
見た目（`m_finalHeights` / `m_detailHeights`）は即時なので**プレイヤーには問題なく見える**が、
「建てられる地面」と「水」（§A-4）はその速度でしか追いつかない。
**300 m の山なら約 9600 sim フレーム ≒ 3.5 ゲーム内時間。** 隆起演出をそれより短くしても構わないが、
「山ができた直後にその上に建てられない／水が引かない」ことになる。**これは仕様として受け入れるのが正しい。**

### 3. 3 種類の形（楯状 / 成層 / 溶岩ドーム）— **安い。潰れない**

**§C-9 が最大の朗報である。** 最大勾配クランプも、侵食も、時間経過による平滑化も、整地パスも、**存在しない**。
`SmoothSample` は単調制限付き cubic の**補間**であって平滑化ではなく、斜面を削らない。
成層火山（1:2、約 27°）も溶岩ドーム（1:1.17、約 41°）も、16 m 格子で素直に表現できる（§C-9 の表）。

- 楯状火山は **`MakeCrater(pos, R, -H, raiseEdges:false)` 1 回で出せる**（§C-8）。
  プロファイルは `1 − (d/0.837R)⁴` で、頂が平らな裾広がり —— 楯状火山の教科書的な形そのもの。
- 成層火山と溶岩ドームは `MakeCrater` の `u⁴` プロファイルでは急峻さが足りないので、
  **`RawHeights` を自前で書く**（`MakeCrack` / `MakeCrater` と同じ 6 行の型）。
  ここは Core 側の純関数（`半径 → 高さ`）にでき、テストしやすい。
- 火口は `MakeCrater(summit, r, +d, raiseEdges:true)` を最後に 1 回（中心 −0.7d、0.75r に +0.3d の縁）。
  **`MakeCrater` は加算合成なので、山を作ってから火口を彫る順で重ねられる。**

**唯一の制約は解像度。** 溶岩ドームの半径を 250 m（片側 8 raw セル）より小さくしないこと。
それ以下は「山」ではなく地面のノイズに見える。**オプションの 3 択は半径・高さのプリセットで表現でき、
ゲーム側に何の制約も無い。**

高さの余裕（§C-10）: 海面 40 m から **983.98 m**、既定地面 60 m から **963.98 m**。
成層火山 600 m は余裕で入る。**天井に当たっても例外は出ず無言で山頂が平らな台地になる**ので、
起点の地形高さを読んでから最大高を決めること。

### 4. 山頂の火口からマグマ噴出 — **中。エフェクトは自作**

`DisasterProperties` に使えるものは無く（`m_mediumExplosion` だけ）、
**溶岩・マグマ・溶融物のプレハブ／マテリアル／シェーダは DLL の文字列ヒープにすらゼロ件**（§B-5）。
④が「雲も渦も無い」と結論したのと同じ状況である。

- **噴煙・噴石は借りられる。** `DisasterProperties.m_mediumExplosion`（`EffectInfo`）と
  `MeteorAI.m_impactEffect` を `EffectInfo.RenderEffect(InstanceID, SpawnArea, …)` で任意座標に出せる。
  `SpawnArea` の ctor 5 種は §B-5 に列挙した。
- **赤熱する噴出物**は自作。③の規約（**CS のマテリアルを借りない**、
  メッシュだけ借りて `Shader.Find("Standard")` で自作、`static Mesh[]` は要素で null 判定）をそのまま適用。
- `DispatchEffect` の `magnitude` は粒子密度でサイズではない（付録 A）。大きさは `SpawnArea` の半径で決める。

### 5. 溶岩が流れるアニメーション — **高い。ゼロから作る**

- **流路の計算は安い。** `TerrainManager.SampleDetailHeight(Vector3, out float slopeX, out float slopeZ)`（public）が
  高さと傾斜を同時に返す（§B-6）。`RawHeights` の直読み（配列 1 回）が最安だが、
  **見た目に一致するのは `SampleDetailHeight`** である。`m_simDetailIndex` は
  `GameAreaManager` だけが立てる（＝購入済みタイル）ので**カメラ非依存＝ sim スレッドでも決定論的**。
  ただし**未購入タイルへ出ると 4 m から 16 m 補間に落ちる**ことは診断に出すこと。
  流路探索そのものは Core の純関数にでき、`IHeightSampler`（既存）で抽象化済み。
- **見た目は全部自作。** 溶岩の面は自前メッシュ＋自前マテリアル（上記）。
  「流れる」は UV スクロールか、区間ごとのメッシュを時間で伸ばすかのどちらか。
- **`Physics.Raycast` は使わない**（地形にコライダーが無い、既知）。

### 6. 溶岩の経路に応じた火災 — **安い。ただし木は DLC 次第**

| 対象 | API | 判定 |
|---|---|---|
| 地面の焦げ | `DisasterHelpers.BurnGround(pos, radius, 0.0–1.0)` | **使える。DLC 不要。** 512²/33.75 m、値は単調非減少で 255 飽和 |
| 建物への着火 | `BuildingAI.BurnBuilding(id, ref d, group, testOnly:false)` | **使える。DLC 不要。** `Shelter` / `TsunamiBuoy` は拒否、水没中も拒否 |
| 炎の見た目 | `BuildingManager.instance.m_properties.m_fireEffect.RenderEffect(...)` | **使える。バニラと同じ炎を任意座標に出せる**（§B-5） |
| 木への着火 | `TreeManager.BurnTree(idx, group, 128–255)` | **ND DLC が無いと常に false**（§B-7c）。⑤は DLC 不要の機能なので**分岐が必須** |
| 道路を燃やす | — | **ABSENT。** `NetAI.CollapseSegment` で倒壊させることしかできない |

`LoadingManager.instance.SupportsExpansion(ICities.Expansion.NaturalDisasters)` で分岐し、
DLC 無しなら「木は燃えず、地面の焦げと自前の炎エフェクトだけ」にする。診断（Phase 0.5）に 1 行出す。
`BurnTree` の `fireIntensity` は `conv.u1` で**切り捨てられる（クランプされない）**ので呼び出し側で clamp すること。

---

### いちばん重要な一点 —— 依頼どおりには「ならない」部分

**依頼文には書かれていないが、都市の中に火山を作ると必ず起きること（§A-2, §A-3）:**

> **道路と、`m_flattenTerrain == true` の建物（＝ゾーン建築とほとんどの ploppable）は、地面が上がっても上がらない。**
> ゲームは毎 `UpdateArea` でそれらの高さに地形をハードピンし直す。壊しもしないし、動かしもしない。
> 結果、**山の中に道路の平らな溝と建物のすり鉢が残る。**
> 木・プロップ・歩道・`m_flattenTerrain == false` の建物だけが素直に上がってくる。

これは「バグ」ではなく、地形ツールで山を盛ったときにも起きるバニラの挙動である。取りうる方針は 3 つ:

- **(a) 無人地帯限定（最も安い・推奨）。** 設置時にフットプリント内の建物数と道路セグメント数を数え、
  0 でなければ警告するか設置を拒否する。「火山は都市の外にできる」で体験は成立する。
- **(b) ⑤が自分で片付ける（中）。** 隆起の進行に合わせてフットプリント内の建物を
  `BuildingAI.CollapseBuilding(id, ref d, group, testOnly:false, demolish:true, burnAmount:…)`、
  道路を `NetAI.CollapseSegment(id, ref d, group, demolish:true)` で消す。
  ④§F-2 の「拒否する AI」一覧（`Shelter` / `DoomsdayVault` / `DamPowerHouse` / `TsunamiBuoy` /
  `DecorationBuilding` は `demolish:false` だと無反応、`PowerPole` / `CableCarPylon` は `testOnly` で嘘をつく）がそのまま効く。
  **災害としては最も「らしい」が、これは新規の破壊ロジックであって可視化ではない。スコープを分けること。**
- **(c) 放置。** 溝と穴が残る。**これを選ぶなら、そう見えることを先に承知しておくこと。**

**判断は⑤の設計時に必要で、後戻りが効かない。** (b) を選ぶなら、可逆性（§E-13）は
「地形は戻せるが、壊した建物は戻らない」という非対称になる。

### 可逆性のまとめ（§E-13）

- **`BackupHeights` / `UndoBuffer` は使わない。** `TerrainTool` / `DistrictTool` の専有物で、
  プレイヤーが整地ツールを開いた瞬間に上書きされる。
- **自前で影響矩形の `RawHeights` をコピーして持つ。** 実費は半径 1 km で 31 KB、2 km で 123 KB。安い。
- セーブに残すなら `ISerializableDataExtension`（付録 A-2b のロード順の注意がそのまま適用）。
- 復元しても `m_blockHeights` は**下降 8 m / 64 sim フレーム**でしか追いつかない（§A-2）。

---

### 着手前にもう 1 つ確かめるべきもの

1. **ND DLC 無しの環境に `DisasterInfo` プレハブが存在するか**（§D-11）。
   `PrefabCollection<DisasterInfo>.PrefabCount()` と各 `GetPrefab(i).m_disasterAI.GetType().Name` を
   Phase 0.5 の `DiagnosticDump` に出す。**`DisasterData` に載せる設計を書く前に必須。**
   載せない設計（推奨）を選ぶなら不要。
2. **代表的な `BuildingInfo` の `m_flattenTerrain` の実値。** DLL には無い（プレハブ値）。
   ゾーン建築・ploppable・公園でそれぞれ何が立っているかで、上の (a)/(b)/(c) の判断の重みが変わる。
   実行時に `PrefabCollection<BuildingInfo>` を走査して集計するのが最短。
3. **`EffectCollection.FindEffect` の在庫**（§B-5）。名前はアセットバンドル側にあり DLL には無い。
   `private static Dictionary<string, EffectInfo> m_dict` をリフレクションで列挙して、
   噴煙・粉塵に流用できるものがあるか実機で見る。無ければ全部自作でよい（設計は変わらない）。

### 再現手順

`docs/tools/ilload.ps1` ＋ `docs/tools/ildasm.ps1` を ASCII パスへ複製して dot-source する
（リポジトリのパスに日本語が含まれるため）。`ColossalManaged.dll` は別途 `LoadFrom` する。
②の再現手順に記した `ShortInlineI` / `ShortInlineBrTarget` の符号修正は**すでに `docs/tools/ildasm.ps1` 本体に入っている**
（今回はそのまま動いた）。

本文書で使った補助関数（scratchpad の `boot.ps1`）:
`Dump-Type <型名>`（フィールド／プロパティ／メソッドをアクセシビリティ・static・virtual・リテラル値つきで列挙）、
`D <型名> <メソッド名>`（全オーバーロードを逆アセンブル）、`T <型名>`（両アセンブリから型解決）。
呼び出し元の特定は「全型・全メソッドの IL バイト列から `call`(0x28) / `callvirt`(0x6F) /
`ldfld`(0x7B) / `stfld`(0x7D) のトークンを走査して対象のメタデータトークンと突き合わせる」方式で行った。

