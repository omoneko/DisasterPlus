# Disaster + — ②地震 IL 実測ファクト

- 日付: 2026-08-14
- 対象: Cities: Skylines 1 / `Assembly-CSharp.dll`（Steam 版、`Cities_Data/Managed`）
- 目的: ②地震の設計に着手する前に、依頼文の各要求が**バニラの実装上どこまで成立するか**を IL で確定させる
- 状態: **調査のみ**。`src/` は一切変更していない
- 前提資料: `2026-08-11-…-firewhirl-design.md` 付録 A（災害フラグ / `DisasterData` / `StartDisaster` が protected / `DAYTIME_FRAMES = 65536`）、
  `2026-08-12-forecast-design.md` §1.3（ハザードマップの意味の訂正）。**既出の事実は再導出せず引用する。**

判定記号: **CONFIRMED**（IL を読んだ）/ **PARTIAL**（読んだが確定しきれない。何を見れば決まるかを書く）/ **ABSENT**（存在しない）

---

## 0. 要約ファクト表

| # | 問い | 判定 | 一行の答え |
|---|---|---|---|
| A1 | 位相と長さ | CONFIRMED / 数値は PARTIAL | Emerging→Active→Clearing→Finished。長さは `m_emergingDuration` / `m_activeDuration`（プレハブ値、DLL に無い） |
| A2 | 何を壊すか | **CONFIRMED** | `m_targetPosition` からの半径ではない。**断層線に沿った 4 個の円盤（毎ステップ）＋ 半径 `2000+20×intensity` の全体円盤 1 個**。両方とも距離で線形に減衰 |
| A3 | 出火するか | **CONFIRMED** | する。`DisasterHelpers.DestroyBuildings` 内の `BuildingAI.BurnBuilding(id, ref data, group, false)` が正規経路。加えて `BurnGround` が地面を焦がす |
| A4 | intensity の入り口 / 100 超 | **CONFIRMED** | 亀裂幅・長さ・全体半径・音量・ハザード半径に入る。**100 でクランプする箇所はコードに無い**。飽和もオーバーフローも無い |
| A5 | `UpdateHazardMap` のゲート | **CONFIRMED** | 嵐と**完全に同じ** `Located && (Emerging\|Active)`。塗る形は断層に沿った帯（2次減衰） |
| A6 | 揺れ・カメラシェイク | **CONFIRMED** | `EarthquakeAI.RenderInstance` が `ref Vector3 shake` に加算 → `CameraController.m_cameraShake`(public)。`DisasterManager.m_disableCameraShake`(public) で無効化可。**振幅に `m_intensity` は入っていない** |
| B7 | 津波の生成方法 | **CONFIRMED** | `TerrainManager.WaterSimulation.CreateWaterWave(out ushort, WaterWave)`。水面シミュへの波の注入 |
| B8 | 任意地点に置けるか | **ABSENT（置けない）** | `TsunamiAI.FindSea` が**マップ外周セルしか候補にしない**。原点も向きも派生値で、設定できるフィールドは無い。`m_targetPosition` は「どの外周区間か」を選ぶだけで、開始時に**上書きされる** |
| B9 | `TsunamiAI.EndDisaster` | CONFIRMED（前提の誤りを訂正） | `DetectDisaster` の呼び出し元ではない。中身は base + `UnDetectDisaster`。津波を `located:true` で検知するのは `TsunamiBuoyAI.ProduceGoods` |
| B10 | DLC 検出 | **CONFIRMED** | `DisasterManager.FindDisasterInfo<TsunamiAI>()`（public static generic）が null を返すかどうかが権威。DLC 無しならプレハブ自体が存在しない |
| C11 | 地震計が持つデータ | **ABSENT** | `EarthquakeSensorAI` のフィールドは `m_detectionRange` と勤務者数だけ。**時系列データは一切無い。波形グラフの材料はゼロから自作するしかない** |
| C12 | 地震計と `DetectDisaster` | **CONFIRMED** | 地震計は `DetectDisaster` を呼ばない。`EarthquakeAI.SimulationStep` 自身が震源の `EarthquakeCoverage` を読み、`located: coverage != 0` で呼ぶ |
| D13 | 地形改変 | **CONFIRMED（可能）** | `TerrainManager.RawHeights`(ushort[]) に直接書いて `TerrainModify.UpdateArea`。コストもアンドゥも無い。`MakeCrater` という既製 API もある |
| D14 | 水の注入 | **CONFIRMED** | `WaterSimulation.CreateWaterWave`。単位は 1/64 m、グリッド 1080²・16 m セル、sim スレッド |
| E15 | 同時実行 | **CONFIRMED** | 可能。上限 256（`MAX_DISASTER_COUNT`）。超えると `CreateDisaster` が **false を返すだけ**（例外なし） |
| E16 | NDR との衝突 | **PARTIAL** | NDR は本機に未インストールでバイナリを再読できず。既出のソース読解（設計書 §2.2）＋今回の呼び出し側 IL から衝突点を特定（§E16） |
| F17 | 時刻の取得 | **CONFIRMED（ただし要注意）** | `m_currentDayTimeHour` は**メインスレッドで書かれる**。sim スレッドの正解は `m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR`。**日夜サイクル OFF だと時刻は永久に 12.0 に固定される** |

**今回いちばん危なかった思い込み**（＝8個目の候補）は F17 と C11 と B8 の 3 つ。詳細は §「設計への含意」。

---

## A. バニラ `EarthquakeAI` の実際

### A-0. 型の輪郭（リフレクション）

```
EarthquakeAI : DisasterAI
  public Single    m_crackLength
  public Single    m_crackWidth
  public UInt32    m_emergingDuration
  public UInt32    m_activeDuration
  public AudioInfo m_loopSound
```

override: `RenderInstance` / `PlayInstance` / `UpdateHazardMap` / `CreateDisaster` / `SimulationStep` /
`StartDisaster`(protected) / `ActivateDisaster`(protected) / `DeactivateDisaster`(protected) /
`IsStillEmerging` / `IsStillActive` / `IsStillClearing`(いずれも protected) /
`GetFireSpreadProbability` / `CanAffectAt` / `GetMinimumEdgeDistance` / `CanSelfTrigger` /
`HasDirection` / `GetPosition` / `GetHazardSubMode`。

> **`FinalizeDisaster` は ABSENT。** アセンブリ全型・全メソッドを走査して 1 件もヒットしない。
> 終了処理は `DisasterAI.EndDisaster`（protected virtual）。`EarthquakeAI` はこれを override して**いない**。

`m_emergingDuration` / `m_activeDuration` / `m_crackLength` / `m_crackWidth` の**実数値は DLL に無い**（プレハブの
シリアライズ値）。UnityPy で `sharedassets*.assets` の MonoBehaviour を読もうとしたが、CS の MonoBehaviour は
型ツリーが実質読めない（226 個中 6 個しか `read_typetree` が成功しない）ため今回は取れなかった。**PARTIAL**。
実行時に取るなら:

```csharp
var info = DisasterManager.FindDisasterInfo<EarthquakeAI>();   // public static generic
var ai   = (EarthquakeAI)info.m_disasterAI;                     // m_activeDuration 等が読める
```

### A-1. 位相機械 — CONFIRMED

`DisasterAI.SimulationStep`（base、`EarthquakeAI.SimulationStep` の先頭で呼ばれる）:

```
IL_0000  m_flags & 16 (Clearing) ≠0 → IsStillClearing() が false なら EndDisaster()
IL_0028  m_flags &  8 (Active)   ≠0 → IsStillActive()   が false なら DeactivateDisaster()
IL_004F  m_flags &  4 (Emerging) ≠0 → IsStillEmerging() が false なら ActivateDisaster()
```

`EarthquakeAI.StartDisaster`:

```
IL_0003  call DisasterAI::StartDisaster        // m_flags = (m_flags & 0xFFFC88C7) | Emerging(4), m_startFrame = 現フレーム
IL_000E  ldc.i4.s 64 ; and ; brfalse → return  // ★ SelfTrigger(64) が立っていないと以下を一切やらない
IL_0016  m_targetPosition.y = TerrainManager.SampleDetailHeight(m_targetPosition)
IL_0031  m_activationFrame  = m_startFrame + m_emergingDuration
IL_0044  m_flags |= 256 (Significant)
IL_0056  DisasterManager.m_randomDisasterCooldown = 0
```

> **落とし穴（重要）。** `SelfTrigger` が無いと `m_activationFrame` が 0 のままになり、
> `EarthquakeAI.IsStillEmerging` は `if (m_activationFrame == 0) return true;` なので **Emerging のまま永久に止まる**。
> `TsunamiAI.StartDisaster` も同じ `& 64` ゲートを持つ。
> フラグ 64 を立てているのは `DisasterManager.StartRandomDisaster` / `DisasterTool.CreateDisaster`(iterator) /
> `DisasterWrapper.CreateDisaster` / `DeveloperUI.StartDisaster` の 4 箇所だけ（全アセンブリ走査）。

**MOD から地震を起こす正しい手順**（`DisasterTool.<CreateDisaster>c__Iterator0.MoveNext` の IL_0071–01E8 をそのまま）:

```csharp
DisasterManager.instance.CreateDisaster(out ushort id, info);   // 失敗なら false
ai.ClampDisasterTarget(ref pos);                                 // public
d.m_targetPosition = pos;
d.m_angle          = angle;
d.m_flags         |= DisasterData.Flags.SelfTrigger;             // ★ 必須
d.m_intensity      = (byte)intensity;
ai.StartNow(id, ref d);                                          // public。付録 A-1 の通り
```

`DisasterAI.ActivateDisaster`（base）:
```
m_flags = (m_flags & ~4) | 8 (Active)
m_activationFrame = SimulationManager.m_currentFrameIndex     // ★ 予定値を実測値で上書きする
```
`EarthquakeAI.ActivateDisaster` はこれに `DisasterManager.FollowDisaster(id)` を足すだけ。
`EarthquakeAI.DeactivateDisaster` は base（`m_flags = (m_flags & ~12) | 16 Clearing`）＋ `UnDetectDisaster(id)`。

各位相の終了条件:

| 位相 | 判定 | 式 |
|---|---|---|
| Emerging | `EarthquakeAI.IsStillEmerging` | `m_activationFrame == 0` → true（永久）。それ以外は `currentFrame < m_activationFrame` |
| Active | `EarthquakeAI.IsStillActive` | `(currentFrame - m_activationFrame) < m_activeDuration` |
| Clearing | `EarthquakeAI.IsStillClearing` | `(|m_targetPosition.x| + 4800) > elapsed*0.125f - 1000f - L` （`elapsed = currentFrame - m_activationFrame`、`L = m_crackLength*(0.5 + m_intensity*0.005)`） |

いずれも先頭で base を OR している（`base が true ならそのまま true`）。base の中身:
- `IsStillEmerging` / `IsStillActive` → `DisasterWrapper.IsCustomDisaster(id)`。バニラ災害では false。
  **ただし MOD が `IDisastersExtension` でカスタム災害として登録すると常に true になり、位相が進まなくなる。**
- `IsStillClearing` → 災害グループ（`InstanceManager.GetGroup(new InstanceID{Disaster=id})`）の `m_refCount > 1`。
  つまり **Clearing は「波がマップ外に出る」かつ「グループに紐づく実体（燃えている建物など）が残っていない」まで続く**。

**`IsStillClearing` にはバニラのバグがある — CONFIRMED。**

```
IL_0050  loc3 = new Vector2(Abs(m_targetPosition.x) + 4800, Abs(m_targetPosition.z) + 4800)
IL_008A  loc3 → Vector2.op_Implicit → Vector3      // (x, y, 0)
IL_0090  VectorUtils.LengthXZ(Vector3)             // sqrt(x² + z²) = sqrt(x² + 0²) = |x|
```
トークンを直接解決して確認済み: `UnityEngine.Vector2.op_Implicit(Vector2) : Vector3` →
`ColossalFramework.Math.VectorUtils.LengthXZ(Vector3) : Single`。
`Vector2 → Vector3` は `(x, y, 0)` なので **z 成分が落ち、`LengthXZ` は `|targetPosition.x| + 4800` を返すだけ**になる。
→ **Clearing の長さは震源の x 座標だけで決まり、z 座標は影響しない。**

フレーム→ゲーム内時間（付録 A-4 の `DAYTIME_FRAMES = 65536` を使う。1 ゲーム内時 = 2730.667 フレーム）:

| 量 | フレーム | ゲーム内時間 |
|---|---|---|
| Clearing 終了（x=0, L=0 の下限） | `8 × 5800 = 46400` | 約 17.0 時間 |
| 警報リードタイム（地震計カバレッジ 0） | 1755 | 約 38.6 分 |
| 警報リードタイム（カバレッジ 100） | `1755 + 6437 = 8192` | **ちょうど 3.0 時間**（= 65536/8） |
| 災害 1 個あたりの `SimulationStep` 間隔 | 256 | 約 5.63 分（1 ゲーム日に 256 回） |

### A-2. 警報（Emerging 中）— CONFIRMED

`EarthquakeAI.SimulationStep` IL_0008–0079:

```
if (m_flags & 4 /*Emerging*/) {
    if (m_activationFrame != 0) {
        loc0 = SimulationManager.m_currentFrameIndex
        ImmaterialResourceManager.CheckLocalResource(
            (Resource)22 /*EarthquakeCoverage*/, m_targetPosition, out loc1)
        loc1 = Mathf.Min(loc1, 100)                                 // IL_0040
        loc2 = loc1 * 6437 / 100 + 1755                             // IL_0049–0057 (整数, div.un)
        if (loc0 + loc2 >= m_activationFrame)
            DisasterManager.DetectDisaster(disasterID, located: loc1 != 0)   // IL_0067–0074
    }
    return;
}
```

`ldc.i4 6437` / `ldc.i4 1755` はリテラル。`located` は `ldloc.1; ldc.i4.0; ceq; ldc.i4.0; ceq` = `(coverage != 0)`。

Active に入った直後にも保険がある（IL_008B–00C2）:
```
if ((m_flags & 4096 /*Located*/) == 0) {
    CheckLocalResource(22, m_targetPosition, out loc3);
    if (loc3 != 0) DetectDisaster(disasterID, true);
}
```

### A-3. 何をどう壊すか — CONFIRMED（依頼文の前提を半分だけ否定する）

`EarthquakeAI.SimulationStep` の Active 分岐（IL_00C2–IL_040B）。**半径ではなく断層線**である。

```
c    = VectorUtils.XZ(m_targetPosition)                       // 断層の中心
dir  = new Vector2(-sin(m_angle), cos(m_angle))               // 断層の走向（単位ベクトル）
r    = new Randomizer(m_randomSeed)
freq = r.Int32(70, 200) * 0.1f                                // 7.0 .. 20.0   蛇行の周波数
ph   = r.Int32(-15, 15) * 0.1f                                // -1.5 .. 1.5   蛇行の位相
r    = new Randomizer(r.Bits32(16) ^ (m_currentFrameIndex >> 8))   // ステップごとに振り直し
grp  = InstanceManager.GetGroup(new InstanceID { Disaster = disasterID })

W = m_crackWidth  * (0.5f + m_intensity * 0.005f)             // IL_016D–0187
L = m_crackLength * (0.5f + m_intensity * 0.005f)             // IL_0189–01A3

for (k = 0; k < 4; k++) {                                     // IL_01A5 / IL_03C6: ldc.i4.4 ; blt
    t = r.Int32(-400, 400) * 0.001f                           // -0.4 .. 0.4（断層上の正規化位置）
    s = sin(t * freq + ph)
    w = W * (1f - 4f * t * t)                                 // 断層端で 0 に細る（|t|=0.5 で 0）
    p = c + dir * (t * L)
    p.x += dir.y * (s * w * 0.5f)                             // 断層に直交する蛇行
    p.y -= dir.x * (s * w * 0.5f)

    DisasterHelpers.SplashWater(p, w, w * 0.5f)               // IL_024F
    P = VectorUtils.X_Y(p); P.y = TerrainManager.SampleDetailHeight(P)

    DisasterHelpers.DestroyBuildings(                          // IL_0298
        seed: disasterID, group: grp, position: P,
        preRadius: w, removeRadius: w * 0.5f,
        destructionRadiusMin: w,   destructionRadiusMax: w * 2f,
        burnRadiusMin: w,          burnRadiusMax: w * 1.5f,
        probability: 1f)                                       // ldc.r4 1

    DisasterHelpers.DestroyNetSegments(                        // IL_02B6
        seed: disasterID, group: grp, position: P,
        totalRadius: w, removeRadius: w * 0.5f,
        destructionRadiusMin: w, destructionRadiusMax: w * 2f)

    a = c + dir * (t*L - w);  a を sin((t - w/L)*freq + ph) * w * 0.5 だけ直交方向にずらす
    b = c + dir * (t*L + w);  b を sin((t + w/L)*freq + ph) * w * 0.5 だけ直交方向にずらす
    DisasterHelpers.MakeCrack(a, b, width: w * 0.5f, depth: w * 0.05f)   // IL_03AD 実地形を彫る
    DisasterHelpers.BurnGround(p, radius: w, intensity: 0.7f)            // IL_03BB
}

// ループの外、1 ステップに 1 回だけ（IL_03CE–0406）
R = 2000f + m_intensity * 20f
DisasterHelpers.DestroyBuildings(
    seed: disasterID, group: grp, position: m_targetPosition,
    preRadius: R, removeRadius: 0f,
    destructionRadiusMin: 0f, destructionRadiusMax: R,
    burnRadiusMin: 0f,        burnRadiusMax: R,
    probability: 0.02f)                                        // ldc.r4 0.02
```

`DisasterHelpers.DestroyBuildings` の 10 引数版の中身（IL_0000–0568）:

```
// 建物グリッド走査範囲 = position ± (preRadius + 72)、セル 64、オフセット 135、[0,269] にクランプ
foreach 建物:
    if ((m_flags & 524307 /*0x80013 = Created|Deleted|Untouchable|Demolishing*/) != 1) continue;   // Created のみ
    dist = VectorUtils.LengthXZ(building.m_position - position)
    if (dist >= preRadius) continue;                                          // IL_0130 ★ 一次カリング

    rnd = new Randomizer(buildingID | (seed << 16))                           // IL_0138 ★ フレーム非依存
    fD  = (destructionRadiusMax - dist) / Max(1, destructionRadiusMax - destructionRadiusMin)
    fB  = (burnRadiusMax        - dist) / Max(1, burnRadiusMax        - burnRadiusMin)
    hitD = rnd.Int32(10000) < fD * probability * 10000                        // IL_0177
    hitB = rnd.Int32(10000) < fB * probability * 10000                        // IL_0193

    remove = false
    if (removeRadius != 0 && dist - 72 < removeRadius) remove = 建物フットプリント Quad2 と円の交差判定

    if      (remove) BuildingAI.CollapseBuilding(id, ref d, grp, testOnly:false, demolish:true,  burnAmount:Round(fB*255))
    else if (hitD)   BuildingAI.CollapseBuilding(id, ref d, grp, testOnly:false, demolish:false, burnAmount:Round(fB*255))
    else if (hitB && !(m_flags & 4194304 /*Collapsed*/) && m_fireIntensity == 0)
                     BuildingAI.BurnBuilding(id, ref d, grp, testOnly:false)   // IL_0510
```

これで確定する事実:

1. **距離減衰は既に存在する。** `Min` 半径で 1、`Max` 半径で 0 の**線形ランプ**。
   全体円盤は `probability 0.02 × (1 - dist/R)` なので、震央で 2%、`R` で 0%。
2. **`preRadius` がハードカリング。** 全体円盤は `preRadius = destructionRadiusMax = R` なので、
   `R = 2000 + 20×intensity`（intensity 55 で 3100 m、100 で 4000 m、255 で **7100 m**）。
3. **乱数はフレーム依存でない。** `Randomizer(buildingID | disasterID << 16)` は
   (建物, 災害) の組に対して**定数**。したがって
   **各建物は地震ごとに固定のしきい値を 2 個持ち、「局所の強度係数 × probability」がそれを超えた瞬間に倒壊/出火する。**
   同じステップ内の 5 回の呼び出しも、次のステップも、同じ 2 個の乱数を引く。
   → 見た目はランダムだが、**中身はすでに決定論的な強度分布**である。依頼文の「揺れによる火災や倒壊はおそらくランダム」は
   半分正しく（建物ごとのしきい値が一様乱数）、半分間違い（距離減衰は既にある）。
4. **道路（`DestroyNetSegments`）は断層の 4 円盤だけ**。全体円盤は建物のみで、道路は壊さない。

### A-4. 出火 — CONFIRMED

- 建物への着火は `BuildingAI.BurnBuilding(buildingID, ref data, group, testOnly:false)` **のみ**（上記 IL_0510）。
  付録 A-3 で確立した「`m_fireIntensity` を直接書かない」という規則と**バニラの実装が一致している**。
- 選ばれ方: `hitB = rnd.Int32(10000) < ((burnRadiusMax - dist)/Max(1, burnRadiusMax-burnRadiusMin)) × probability × 10000`。
  すでに倒壊している建物（`Collapsed`）と、すでに燃えている建物（`m_fireIntensity != 0`）は除外。
  倒壊が優先で、倒壊した建物は `CollapseBuilding` の `burnAmount = Round(fB × 255)` で焼損量を受け取る。
- 断層 4 円盤は `burnRadiusMin = w, burnRadiusMax = w*1.5, probability = 1` なので、
  `w` 以内はほぼ確実に着火（`fB > 1` になるため）、`1.5w` で 0。
  全体円盤は `burnRadiusMin = 0, burnRadiusMax = R, probability = 0.02` なので震央 2%。
- `DisasterHelpers.BurnGround(pos, radius, 0.7f)` は建物ではなく
  `NaturalResourceManager.m_naturalResources[].m_burned`（512² グリッド、セル 33.75）を焼く。
  最後に `NaturalResourceManager.AreaModifiedB` を呼ぶ。**建物の火災とは別系統。**
- 延焼速度: `EarthquakeAI.GetFireSpreadProbability`

```
IL_0000  DisasterManager.m_disableFireSpread && m_randomDisastersProbability == 0
         && string.IsNullOrEmpty(SimulationManager.m_metaData.m_ScenarioAsset)  → return 0
IL_003E  elapsed = m_currentFrameIndex - m_activationFrame
IL_0050  return 1500 / (8 + (elapsed >> 10))
```
  `CommonBuildingAI.HandleFireSpread` / `TreeManager.HandleFireSpread` が
  `延焼強度 *= GetFireSpreadProbability(...) * 0.01f` として使う（IL_020B–021D）。
  → 発生直後は **1500/8 = 187 → ×1.87**、8192 フレーム後 93 → ×0.93、65536 フレーム後 20 → ×0.20。
  **ただしこれは災害グループに属している建物にしか効かない**（`InstanceManager.GetGroup(building).m_ownerInstance.Disaster != 0`）。

### A-5. intensity の入り口と 100 超の挙動 — CONFIRMED

| 使われる場所 | 式 |
|---|---|
| 亀裂の幅 | `W = m_crackWidth * (0.5 + i*0.005)` |
| 亀裂の長さ | `L = m_crackLength * (0.5 + i*0.005)` |
| 全体円盤の半径 | `R = 2000 + i*20` |
| ハザードマップの半径 | `2000 + i*20 + 400` |
| `GetPosition` のサイズ | `L` の立方（マーカー表示用） |
| `PlayInstance` の音量 | `sqrt(clamp01(残存率)) * (0.5 + i*0.005)` |
| 津波の波高 | `m_height * 65536/1024 * i / 55`（`TsunamiAI.FindSea` IL_0377–03AD） |

- **クランプは存在しない。** `Mathf.Min(…, 100)` は §A-2 の**地震計カバレッジ**にしか掛かっていない。
- `DisasterData.m_intensity` は `Byte`。全部 float 演算に流れるので飽和もオーバーフローも無い。
  唯一の飽和は津波の `m_delta` の `Mathf.Clamp(…, -32767, 32767)`（= ±511.98 m）で、
  `m_height` が 30 程度なら intensity 255 でも届かない。
- 既定値: `DisasterManager.CreateDisaster` が `m_intensity = 55`（IL_0028）。
  `StartRandomDisaster` は `Randomizer.Int32(10, 100)`（IL_0078）。→ **バニラのランダム発生は 100 が上限**。
- 付録 A-2 のとおり `DisastersOptionPanel.m_slider.maxValue` は UI プレハブ値なので、
  255 解放は既に本 MOD が実現済み。**②で追加のクランプ対策は不要。**

### A-6. `UpdateHazardMap` — CONFIRMED（嵐と同じゲート）

```
IL_0000  ldarg.2; ldfld m_flags; ldc.i4 4096; and; brfalse → return    // Located
IL_0011  ldarg.2; ldfld m_flags; ldc.i4.s 12; and; brfalse → return    // Emerging|Active
```

`2026-08-12-forecast-design.md` §1.3 で確定した `ThunderStormAI` / `TornadoAI` の 2 段ゲートと
**バイト単位で同一**。したがって「①で書いた注意書き（測位済みで進行中の災害しか塗らない）は
②の地震にもそのまま適用される」。

塗る形（IL_001F–03B8）:

```
seg.a = c - dir * (L*0.5f);  seg.b = c + dir * (L*0.5f)
両端に ±(Randomizer.Int32(-1000,1000)*0.001f) * 200f のジッタ（軸ごと）
freq = r.Int32(30,100)*0.1f  (3.0..10.0),  ph = r.Int32(-15,15)*0.1f
Rmax = (2000 + i*20) + 200*2
セル 38.4 m / 256² / 中心オフセット 128（HAZARDMAP_CELL_SIZE, HAZARDMAP_RESOLUTION と一致）
各セル:
    d = sqrt(Segment2.DistanceSqr(cellCenter, out u))
    u' = 2u - 1
    d -= W * sqrt(Max(0, 1 - u'*u'))                 // 断層の芯を太らせる
    d += W * (0.5f + 0.5f*sin(u*freq + ph))          // 蛇行
    if (d < Rmax) {
        v = Min(1, 1 - d/Rmax); v *= v;              // 2 次減衰
        map[z*256 + x] = 255 - (255 - map[i]) * (255 - Round(v*255)) / 255;   // 乗算合成（和集合）
    }
```

`GetHazardSubMode` は `subMode = 5` を返す → `InfoManager.SubInfoMode.EarthquakeHazard = 5`（enum 実測）。
①のモード切替ラッパーがそのまま使える。

### A-7. 揺れの表現とカメラシェイク — CONFIRMED

`EarthquakeAI.RenderInstance(CameraInfo, ushort, ref DisasterData, ref Vector3 shake)`:

```
IL_0000  if ((m_flags & 12 /*Emerging|Active*/) == 0) return
IL_0014  e = m_referenceFrameIndex - m_activationFrame + 128
IL_0028  if (e <= 0) return
IL_0030  if (e >= m_activeDuration) return
IL_003F  v = camera.transform.InverseTransformPoint(m_targetPosition); v.z *= 0.25f
IL_0069  amp = 0.3f / (1f + v.magnitude * 0.001f)
IL_0083  t   = (float)e + SimulationManager.m_referenceTimer
IL_008E  amp *= (0.5f - 0.5f * cos(t * 0.02454369f))       // 2π/0.02454369 = 256.0 → 256 フレーム周期の脈動
IL_00AA  dir = new Vector2(-sin(m_angle), cos(m_angle))
IL_00CF  s   = (sin(t * 0.63f) + sin(t * 0.17f)) * amp
IL_00F2  shake.x += s * dir.y
IL_010A  shake.z -= s * dir.x
```

- **揺れは断層に直交する 1 方向**（`(dir.y, -dir.x)`）。
- **振幅に `m_intensity` は入っていない。** カメラからの距離だけ（`0.3 / (1 + dist*0.001)`）。
  → 強度 25.5 の地震でもカメラの揺れは 5.5 の地震と同じ。**ここは依頼の「距離による震度分布」に直結する空白。**
- 成分は 0.63 rad/frame（周期 ≈10 フレーム）と 0.17 rad/frame（周期 ≈37 フレーム）の 2 本だけ。
  **長周期成分は存在しない**（低周波成分は 256 フレーム周期の包絡線だけで、これは振幅の脈動であって地震動ではない）。

適用経路（`DisasterManager.EndRenderingImpl` IL_00C6–016F）:

```
Vector3 shake = Vector3.zero;
foreach 災害 with (m_flags & 3) == Created:  info.m_disasterAI.RenderInstance(cameraInfo, id, ref data, ref shake);
if (!m_disableCameraShake)                                                    // ★ public bool
    m_cameraController.m_cameraShake = m_cameraController.m_cameraShake + shake;   // ★ public Vector3
```

`CameraController.m_cameraShake` は **public フィールド**。書き手は `DisasterManager.EndRenderingImpl` と
`CameraController.LateUpdate`（毎フレーム書き戻し＝リセット）、読み手は `CameraController.UpdateTransform`。
`DisasterManager.m_disableCameraShake` も **public**（`DeveloperUI.OnGUI` が触っている）。
→ **MOD から揺れを足すのも殺すのも 1 行でできる。** ただし加算はレンダリング側で毎フレーム消費されるので、
毎フレーム足し続ける必要がある（`IRenderableManager` か `ThreadingExtensionBase.OnUpdate` から）。

### A-8. `CanAffectAt` — 進行する波（避難判定の実体）— CONFIRMED

```
IL_0012  if (m_flags & 4 /*Emerging*/) return false
IL_0021  dist = LengthXZ(position - m_targetPosition)
IL_0033  elapsed = currentFrame - m_activationFrame
IL_0045  inner = elapsed * 0.125f - 1000f
IL_0055  outer = elapsed * 0.125f + 100f
IL_0065  L = m_crackLength * (0.5f + m_intensity*0.005f)
IL_0081  inner -= L ; outer += L
IL_008B  priority = Clamp01(Min((outer - dist) * 0.02f, (dist - inner) * 0.002f))
IL_00AA  return dist >= inner && dist <= outer
```

**地震には「震央から 0.125 units/frame で外へ広がるリング」が既にある**（幅 1100 + 2L）。
0.125 units/frame = 8192 m / ゲーム日。`TsunamiAI` の位相判定も同じ 0.125 定数を使う。
このリングは `ShelterAI` 等の避難判定と `TsunamiBuoyAI` に使われるだけで、**破壊にも描画にも使われていない**。
→ **「震央からの距離による震度分布」の骨組みは既に存在し、可視化されていないだけ。**

---

## B. 津波

### B-1. `TsunamiAI` の輪郭 — CONFIRMED

```
TsunamiAI : FloodBaseAI : DisasterAI
  public Single m_height
  public Int32  m_duration
  private static Byte[]        m_tempHazardLevel
  private static FastList<?>[] m_tempCellPositions
```
override: `UpdateHazardMap` / `CreateDisaster` / `StartDisaster`(protected) / **`EndDisaster`(protected)** /
`IsStillEmerging` / `IsStillActive` / `IsStillClearing` / `CanAffectAt` / `CanSelfTrigger`。
private helper: `FindSea(ushort, ref DisasterData, out WaterWave)` / `GetSeaSideLocation(int, out int, out int)`。

**`SimulationStep` の override は無い。** 波が立った後は `WaterSimulation` が全部やる。
`FloodBaseAI` は `GetHazardSubMode` しか持たない薄い中間クラス。

### B-2. 波の生成 — CONFIRMED

`TsunamiAI.StartDisaster`:

```
IL_0003  base.StartDisaster
IL_000E  if ((m_flags & 64 /*SelfTrigger*/) == 0) return       // ★ 地震と同じゲート
IL_0016  if (m_waveIndex != 0) { WaterSimulation.ReleaseWaterWave(m_waveIndex); m_waveIndex = 0; }
IL_003D  if (!FindSea(disasterID, ref data, out wave)) return  // ★ 海が見つからなければ何も起きない
IL_004C  m_flags |= 256 (Significant)
IL_005E  TerrainManager.instance.WaterSimulation.CreateWaterWave(out data.m_waveIndex, wave)
```

`WaterWave`（public struct）:
```
UInt16 m_type      // TYPE_NONE=0, TYPE_TSUNAMI=1, TYPE_IMPACT=2  （public static readonly）
UInt16 m_minX/m_minZ/m_maxX/m_maxZ    // セル座標の影響範囲
UInt16 m_origX/m_origZ                // 発生セル
Int16  m_dirX/m_dirZ                  // 進行方向（2^15 = 1.0 の固定小数）
Int16  m_delta                        // 波高（1/64 m 単位）
UInt16 m_duration, m_currentTime
Int32  GetSeaLevel(int original, int x, int z)   // public
```
API: `WaterSimulation.CreateWaterWave(out ushort wave, WaterWave waveData) : bool`（public）/
`ReleaseWaterWave(ushort)`（public）。取得は `TerrainManager.instance.WaterSimulation`（public property）。

### B-3. 原点は任意に置けるか → **ABSENT（置けない）**

`TsunamiAI.FindSea`（IL_0000–043E）:

```
tx, tz  = m_targetPosition を 16 m セル座標へ（(v + 8640)/16 + 0.5、[0,1080] にクランプ）
総候補   = 1080 << 3 = 4320 個                              // ★ マップ外周セルだけ
GetSeaSideLocation(i, out x, out z) が外周を一周する順で (x,z) を返す
seaRaw   = WaterSimulation.m_currentSeaLevel * 64
for i in 0..4319:
    depth = seaRaw - TerrainManager.BlockHeights[z*(1080+1) + x]
    if (depth < 8)  → 連続区間を打ち切り、長さ 10 以上なら候補として評価
    else            → 区間を継続し、(x,z) と (tx,tz) の平方距離の最小値を更新
評価: 最小平方距離が小さい方を優先、同点なら区間が長い方
最終区間長 < 10 → return false                              // ★ 津波は起きない
```

その後（IL_0211–043E）:
- 原点セル = 区間内で目標に最も近いセル（区間の中央 1/3 にクランプ）
- 方向 = 区間の 3 点から取った**内向き法線**（`FixedMath.Sqrt` で正規化、2^15 スケール、±32767 クランプ）
- 原点を外周（`x==0 || x==1080` など）にスナップ
- `m_type = 1 (TYPE_TSUNAMI)`
- `m_delta = Clamp(Round(m_height * 65536/1024 * m_intensity / 55), -32767, 32767)`
- `m_duration = Clamp(m_duration << 6, 0, 65472)`
- **`data.m_targetPosition` を `(origX*16 - 8640, m_currentSeaLevel, origZ*16 - 8640)` で上書きする**（IL_03D3–0426）
- **`data.m_angle = Atan2(-dirX, dirZ)`** で上書きする（IL_042B–0438）

したがって:

| 問い | 答え |
|---|---|
| 海上の任意点から起こせるか | **できない。** 原点は必ずマップ外周セル |
| 方向を MOD から設定できるか | **できない。** 区間の法線から導出され、`m_angle` は事前に何を入れても上書きされる |
| `m_targetPosition` の役割 | 「どの外周区間を選ぶか」の**ヒントだけ**。実行後に上書きされる |
| 沖から岸へ向かうか | **常にそう**。マップ外周から内向きにしか進まない |
| 逃げ道はあるか | **ある。** `TsunamiAI` を通さず `WaterSimulation.CreateWaterWave` を直接呼べば、任意の `m_origX/m_origZ/m_dirX/m_dirZ/m_delta` の波を作れる（すべて public） |

### B-4. `TsunamiAI.EndDisaster` — CONFIRMED（依頼文の前提を訂正）

```
IL_0003  call DisasterAI::EndDisaster      // m_flags = (m_flags & ~28) | 32 (Finished)
IL_0008  DisasterManager.UnDetectDisaster(disasterID)
```

**`DetectDisaster` の呼び出し元リストには入っていない。**（全アセンブリ走査での `DetectDisaster` 呼び出し元は
`CommonBuildingAI.HandleCommonConsumption` / `CommonBuildingAI.NearObjectInFire` / `FirewatchTowerAI.NearObjectInFire` /
`SpaceRadarAI.ProduceGoods` / **`TsunamiBuoyAI.ProduceGoods`** / `WeatherRadarAI.ProduceGoods` /
`EarthquakeAI.SimulationStep` / `MeteorStrikeAI.SimulationStep` / `SinkholeAI.SimulationStep` /
`ThunderStormAI.SimulationStep` / `TornadoAI.SimulationStep` / `DisasterManager.FollowDisaster` /
各種 `HandleCommonConsumption` / `DisasterWrapper.DetectDisaster`。）

津波を `located: true` にするのは `TsunamiBuoyAI.ProduceGoods`（IL_0127–017F）:
```
threshold = Max(10, 1000 / Max(0.01, finalProductionRate))
if (TerrainManager.WaterLevel(XZ(building.m_position)) >= building.m_position.y + threshold) {
    id = DisasterManager.FindDisaster(building.m_position);
    if (id != 0) { InstanceManager.CopyGroup(...); DisasterManager.DetectDisaster(id, true); }
}
```
→ **ブイは「予報」ではなく「もう水が来ている」検知**。稼働率が高いほど閾値が下がって早く反応する。

**注意:** `EndDisaster` は `ReleaseWaterWave` を呼ばない。波の後始末は
`WaterSimulation` 側（`m_currentTime >= m_duration`）か、同じ災害スロットの次回 `StartDisaster` に委ねられている。
`WaterSimulation.SimulateWater` は未読。**PARTIAL**（波が確実に消えるかを断言するには `SimulateWater` を読む必要がある）。

### B-5. DLC の検出 — CONFIRMED

```
public static DisasterInfo FindDisasterInfo<T>()      // DisasterManager、generic=True
IL_000D  foreach (i in PrefabCollection<DisasterInfo>.PrefabCount())
IL_001B      if (PrefabCollection<DisasterInfo>.GetPrefab(i).m_disasterAI is T) return prefab;
IL_0037  return null;
```
DLC チェックは中に無い。**DLC が無ければ `TsunamiAI` を持つプレハブが `PrefabCollection` に存在せず、null が返る。**
`ModCompat.NaturalDisastersOwned`（`SteamHelper.IsDLCOwned`）は UI を出すかどうかの事前判定に使い、
**実行直前の権威は `FindDisasterInfo<TsunamiAI>() != null`** にすること（現行の Task 10 と同じ扱い）。

---

## C. 地震計

### C-1. `EarthquakeSensorAI` は時系列データを持たない — **ABSENT**

```
EarthquakeSensorAI : PlayerBuildingAI
  public Int32  m_workPlaceCount0 / 1 / 2 / 3
  public Single m_detectionRange          // ★ フィールドはこれだけ
```

`ProduceGoods`（IL_0023–0057）:
```
if (finalProductionRate == 0) return;
radius = m_detectionRange * finalProductionRate / 100f;
if (radius > 10f)
    ImmaterialResourceManager.AddResource((Resource)22 /*EarthquakeCoverage*/, 200, buildingData.m_position, radius);
HandleDead(...)
```

`SimulationStep` は base に委譲するだけ（IL_0000–0009、5 命令）。
`GetLocalizedStats` は `AIINFO_EARTHQUAKE_SENSOR_RANGE` に `Round(m_detectionRange * productionRate / 100)` を出すだけ。
`GetColor` / `GetPlacementInfoMode` は `InfoMode 28` / `SubInfoMode 2`。

> **バニラの地震計は「半径 R の免疫的リソースを毎 tick 撒く装置」でしかない。**
> 波形も、履歴も、観測値も、直近の揺れの大きさも、**何ひとつ保持していない。**
> 波形グラフを出したければ、**MOD が自分でサンプリングして自分でリングバッファに貯める**しかない。

### C-2. `DetectDisaster` との関係 — CONFIRMED

- **地震計は `DetectDisaster` を呼ばない**（全メソッド走査で該当なし）。
- 呼ぶのは `EarthquakeAI.SimulationStep` 自身で、`located: (震央の EarthquakeCoverage != 0)`（§A-2）。
- したがって「地震計を建てる」の効果は 2 つ:
  1. `located = true` になる → **ハザードマップに地震が塗られるようになる**（§A-6 のゲート）
  2. 警報リードタイムが 1755 → 最大 8192 フレーム（38.6 分 → 3 時間）に伸びる
- `Resource` enum 実測: `21 FirewatchCoverage` / **`22 EarthquakeCoverage`** / `23 DisasterCoverage`。
  resource 22 を参照するのは `EarthquakeSensorAI.ProduceGoods` / `EarthquakeAI.SimulationStep` / `SinkholeAI.SimulationStep` の 3 つだけ。
- 読み取り API: `ImmaterialResourceManager.CheckLocalResource(Resource, Vector3, out int)` および
  `CheckLocalResource(Resource, Vector3, float radius, out int)`（どちらも public）。

---

## D. 地形と水

### D-1. 実行時の地形改変は可能 — CONFIRMED

`DisasterHelpers.MakeCrack(Vector2 a, Vector2 b, float width, float depth)`（public static）:

```
IL_0006  TerrainModify.RefreshAllModifications()
IL_000B  cell = 16f ; res = 1080 ; heights = TerrainManager.instance.RawHeights   // ushort[]
IL_001E  toMeters = 0.015625f (= 1/64) ; toRaw = 64f
         セル範囲 = [min-width, max+width] を /16 して +540、[0, 1080] にクランプ
IL_00F2  raw = heights[z*(res+1) + x]
IL_00FF  h   = raw * 0.015625f
IL_010B  d   = sqrt(Segment2.DistanceSqr(new Segment2(a,b), cellPos, out u))
IL_011D  if (d < width*0.5f) h -= (1f - d/(width*0.5f)) * depth
IL_0142  v = Clamp(RoundToInt(h * 64f), 0, 65535)
IL_0159  if (v != raw) heights[z*(res+1)+x] = (ushort)v
IL_018E  TerrainModify.UpdateArea(minX-2, minZ-2, maxX+2, maxZ+2, heights:true, surface:false, zones:false)
```

| 事実 | 値 |
|---|---|
| 配列 | `TerrainManager.RawHeights` : `ushort[]`、`(1080+1)²`、index = `z*1081 + x` |
| セル | 16 m。ワールド → セルは `(v/16) + 540`（`MakeCrack` では `+ 1080*0.5`） |
| 高さ | `raw / 64` メートル（0 〜 1024 m）。書くときは `Clamp(Round(m*64), 0, 65535)` |
| 反映 | `TerrainModify.UpdateArea(int/float ×4, bool heights, bool surface, bool zones)`（public static、両オーバーロードあり） |
| バッチ | `TerrainModify.BeginUpdateArea()` / `EndUpdateArea()`（public static）。`SimulationManager.SimulationStep` の先頭が `BeginUpdateArea` |
| コスト | **無い。** 金銭も、実績も、`EconomyManager` も一切絡まない |
| アンドゥ | **無い。** `TerrainTool` は自前の undo バッファ（`ResetUndoBuffer`/`EndStroke`/`ApplyUndo`）を持つが `MakeCrack`/`MakeCrater` は使わない。**セーブに焼き付く恒久変更** |
| スレッド | バニラは両方から呼ぶ（`EarthquakeAI.SimulationStep` = sim、`TerrainTool.ApplyBrush` / `DistrictTool.ApplyTerrainBrush` = メイン）。本 MOD の既存規則どおり **sim スレッドに寄せる** |
| 関連配列 | `RawHeights` / `RawHeights2` / `FinalHeights` / `BackupHeights` / `BlockHeights` / `BlockHeightTargets` / `RawSurface`（すべて public property） |

**⑤火山にそのまま使える既製 API:**
```
DisasterHelpers.MakeCrater(Vector2 position, float radius, float depth, bool raiseEdges)   // public static
```
`MakeCrack` と同じ構成（`RefreshAllModifications` → `RawHeights` を直接書く → `UpdateArea`）で、
`raiseEdges` でクレーターの縁を盛り上げられる。**⑤の「山を作る／崩す」は技術的に成立する。**
全体置換が要るなら `TerrainManager.SetRawHeightMap(byte[])` / `SetHeightMap16(byte[])` / `SetHeightMap8` / `SetHeightMap(Color32[], int)`。

### D-2. 水の注入 — CONFIRMED

`DisasterHelpers.SplashWater(Vector2 position, float radius, float depth)`（public static）:

```
IL_0000  r     = CeilToInt(radius / 16f)                          // セル数
IL_000D  delta = Clamp(CeilToInt(depth * 65536f / 1024f), -32767, 32767)   // = depth[m] * 64
IL_002F  origX = Clamp((int)((position.x + 8640f)/16f + 0.5f), 0, 1080)
IL_005C  origZ = 同上（position.y）
IL_0089  dirX = dirZ = 0
IL_0099  minX/minZ = orig - r（0 でクランプ）、maxX/maxZ = orig + r（1080 でクランプ）
IL_00FD  m_type = 2 (TYPE_IMPACT)
IL_0105  m_delta = delta ; m_duration = 256 ; m_currentTime = 0
IL_0122  TerrainManager.instance.WaterSimulation.CreateWaterWave(out _, wave)
```

| 項目 | 値 |
|---|---|
| API | `WaterSimulation.CreateWaterWave(out ushort wave, WaterWave waveData) : bool`（public） |
| 取得 | `TerrainManager.instance.WaterSimulation`（public property。`WaterSimulation` は MonoBehaviour） |
| 座標 | セル座標 0..1080、16 m セル。ワールド → セル `(v + 8640)/16 + 0.5`、セル → ワールド `cell*16 - 8640` |
| 高さ単位 | `m_delta` は **1/64 m**（Int16、±32767 = ±511.98 m） |
| 波の種類 | `TYPE_TSUNAMI = 1`（指向性・進行波）/ `TYPE_IMPACT = 2`（`m_dirX = m_dirZ = 0` の放射状）|
| 期間 | `m_duration`（UInt16）。津波は `m_duration << 6`、`SplashWater` は 256 |
| スレッド | 呼び出し元はどちらも sim スレッド（`EarthquakeAI.SimulationStep` / `TsunamiAI.StartDisaster`）。`WaterSimulation` は専用スレッド（`m_simulationThread` / `WaterThread`）を持つので、**MOD からも sim スレッドで呼ぶ** |
| 海面 | `WaterSimulation.m_currentSeaLevel` / `m_nextSeaLevel`（public float）。`DEFAULT_SEA_LEVEL = 40`、`MAX_SEA_LEVEL = 500` |
| 継続的な水源 | `CreateWaterSource(out ushort, WaterSource)` / `LockWaterSource` / `UnlockWaterSource` / `ReleaseWaterSource`（すべて public） |

---

## E. 並行実行と共存

### E-1. 同時実行 — CONFIRMED

`DisasterManager.CreateDisaster(out ushort, DisasterInfo)`:
```
IL_0008  d.m_flags = Created
IL_0012  d.m_randomSeed = SimulationManager.m_randomizer.ULong64()
IL_0028  d.m_intensity = 55
IL_003E  空きスロット（m_flags == 0）を 1 から線形探索して再利用
IL_00B8  無ければ: if (m_disasters.m_size >= 256) { disasterIndex = 0; return false; }   // ★ 例外は出ない
IL_00CD  そうでなければ FastList.Add
```
`MAX_DISASTER_COUNT = 256`（public static Int32）。`MAX_SCENARIO_DISASTERS = 200`。

`DisasterManager.SimulationStepImpl(int subStep)`:
```
IL_015D  if (subStep == 0) skip
IL_0163  idx = SimulationManager.m_currentFrameIndex & 255
IL_0175  loop 変数の下限 = 上限 = idx        // ★ 1 回の呼び出しで 1 個の災害しか進めない
IL_019C  if ((data.m_flags & Created) != 0) info.m_disasterAI.SimulationStep(idx, ref data)
IL_0203  if ((data.m_flags & 163872 /*Finished|Persistent|UnReported*/) == 32) ReleaseDisaster(idx)
```

→ **災害 i の `SimulationStep` は `(currentFrameIndex & 255) == i` のときだけ回る = 256 sim フレームに 1 回**
（≈5.63 ゲーム分、1 ゲーム日に 256 回）。地震の破壊ラウンド数 ≒ `m_activeDuration / 256`。

結論: **地震 → 津波の同時進行はバニラが日常的にやっていること。** 上限 256 で、超えたら
`CreateDisaster` が `false` を返し `disasterIndex = 0` になる。**戻り値を必ず見ること**（無視すると
インデックス 0 のスロットを他人の災害として書き換える）。

### E-2. Natural Disasters Renewal との衝突 — PARTIAL

**本機に NDR はインストールされていない**（`Addons\Mods` / workshop content を走査、DLL 無し）。
したがってバイナリの再確認はできず、設計書 §2.2 のソース読解（`luisgobo/EnhancedDisastersModRenewal` v1.3.0）を前提とする。
そこで確定していること:

- パッチは 2 つだけ。`DisasterHelpers.DestroyBuildings`（Prefix が false を返す完全置換）と
  `DisasterHelpers.DestroyNetSegments`。
- 災害種別は引数値の嗅ぎ分け: `probability == 0.02f` → Earthquake、
  `burnRadiusMin == 0 && burnRadiusMax == 0` → Tornado。

今回読んだ呼び出し側 IL と突き合わせると:

| バニラ地震の呼び出し | NDR の嗅ぎ分け結果 |
|---|---|
| 全体円盤 `probability = 0.02f` | **Earthquake と判定される** → NDR が置換（0.02 → 0.04） |
| 断層 4 円盤 `probability = 1f, burnRadiusMin = w ≠ 0` | **どちらの条件にも当たらない**。この分岐で NDR が何をするかは**未確認（要バイナリ）** |
| `DestroyNetSegments`（断層 4 円盤のみ） | 竜巻用の分岐。地震には強度閾値スキップが掛からないはず（**未確認**） |

**②が守るべき線:**

1. **`DisasterHelpers.DestroyBuildings` を `probability = 0.02f` で呼ばない。** 呼んだ瞬間に NDR が
   「バニラの地震だ」と誤認して全部書き換える。同様に `burnRadiusMin == 0 && burnRadiusMax == 0` も避ける。
2. **そもそも `DisasterHelpers` を経由しない。** Phase 0 で確立した方針（`BuildingAI.BurnBuilding` /
   `BuildingAI.CollapseBuilding` を直接呼ぶ）を続ければ、NDR の 2 つのパッチ面を**完全に迂回できる**。
   `CollapseBuilding(ushort, ref Building, InstanceManager.Group, bool testOnly, bool demolish, int burnAmount)` は
   public virtual（override するのは `AirportBuildingAI` / `CableCarPylonAI` / `CampusBuildingAI` /
   `CommonBuildingAI` / `DamPowerHouseAI` / `DecorationBuildingAI` / `DoomsdayVaultAI` / `MonorailPylonAI` /
   `PowerPoleAI` / `RaceBuildingAI` / `ShelterAI` / `TsunamiBuoyAI` / `VarsitySportsArenaAI`）。
3. **NDR がパッチしていない面はすべて安全:**
   `EarthquakeAI` / `TsunamiAI` / `DisasterManager` / `ImmaterialResourceManager` /
   `TerrainManager` / `TerrainModify` / `WaterSimulation` / `CameraController` / `InfoManager`。
   → **ハザードマップ、カメラシェイク、地形改変、水の波、地震計カバレッジ、震度分布の可視化は全部衝突しない。**
4. NDR は「いつ・どれだけ強く起きるか」を変える。②が強度や発生確率を**予測して数値で見せる**なら、
   ①と同じく `NdrPresent` のときは注記を出す（バニラ由来の実測値だけは常に正しい）。

---

## F. 時刻

### F-1. sim スレッドから読める時刻 — CONFIRMED（ただし `m_currentDayTimeHour` は罠）

`SimulationManager.SimulationStep`（**sim スレッド**、IL_0161–0200）:

```
if (!m_enableDayNight && (ToolController.m_mode & 1) != 0)
    m_dayTimeOffsetFrames = (DAYTIME_FRAMES/2 - m_currentFrameIndex) & (DAYTIME_FRAMES - 1);   // ★
m_dayTimeFrame = (m_currentFrameIndex + m_dayTimeOffsetFrames) & (DAYTIME_FRAMES - 1);
float hour = m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR;
m_metaData.m_currentDayHour = hour;
m_isNightTime = (hour < SUNRISE_HOUR) || (hour > SUNSET_HOUR);
```

`SimulationManager.Update`（**メインスレッド**、IL_01E9–022C）:

```
float f = (m_referenceFrameIndex + m_dayTimeOffsetFrames) & (DAYTIME_FRAMES - 1);
float h = (f + m_referenceTimer) * DAYTIME_FRAME_TO_HOUR;
if (h >= 24f) h -= 24f;
m_currentDayTimeHour = h;                    // ★ ここでしか書かれない
```

静的値（実測）: `DAYTIME_FRAMES = 65536` / `DAYTIME_FRAME_TO_HOUR = 0.0003662109` /
`DAYTIME_HOUR_TO_FRAME = 2730.667` / **`SUNRISE_HOUR = 5`** / **`SUNSET_HOUR = 20`** /
`SIMULATION_DAY_FRAMES = 585` / `SIMULATION_WEEK_FRAMES = 4096`。

| 読みたいもの | sim スレッドでの正解 | 備考 |
|---|---|---|
| 時刻（時） | **`m_dayTimeFrame * SimulationManager.DAYTIME_FRAME_TO_HOUR`**（0 〜 23.99963） | sim スレッド自身が毎フレーム書く |
| 同上（別経路） | `m_metaData.m_currentDayHour` | 上と同じ値。sim スレッドで書かれる |
| 夜か | `m_isNightTime`（bool） | sim スレッドで書かれる。`hour < 5 || hour > 20` |
| `m_currentDayTimeHour` | **使わない** | メインスレッドが `m_referenceFrameIndex`（描画補間側）から書く。sim から読むとスレッド跨ぎ、しかも値の出所が違う |

> **落とし穴（②の「時間帯による差」に直撃）。**
> `m_enableDayNight == false`（日夜サイクル OFF）かつゲームモードのとき、**毎 sim フレーム**
> `m_dayTimeOffsetFrames` が「`m_dayTimeFrame` が常に 32768 になる」値に再設定される。
> → `hour = 32768 × 0.0003662109 = **12.0** で永久固定`、`m_isNightTime` も常に false。
> つまり日夜サイクルを切っているプレイヤーにとって、**時間帯係数は黙って定数になる**。
> ②で時間帯を使うなら、`m_enableDayNight` を見て「この設定では時間帯差は出ません」と明示するか、
> 別の時間軸（`m_currentFrameIndex` ベースの自前の周期）を使うこと。

---

## 設計への含意

依頼文の 6 つの要求を、上の事実で仕分ける。

### 1. 「海中で起こしてもプレート境界型の大地震・津波にならない」

**安い部分:** 地震と津波を**続けて起こす**こと自体は簡単。同時実行に制限は無く（256 枠）、
`CreateDisaster → SelfTrigger → StartNow` の 6 行レシピが確定している（§A-1）。
「震源が海中なら遅延して津波を起こす」という連鎖は、`EarthquakeAI` に触らずに
`TerrainManager.HasWater(Vector2)` で震源が水中かを見て、②側のタイマーで `TsunamiAI` を起こせばよい。

**高い／作り直しが要る部分:** **依頼の「海中の震源から津波が広がる」は `TsunamiAI` では literally 不可能**（§B-3）。
`FindSea` はマップ外周しか候補にせず、`m_targetPosition` も `m_angle` も開始時に上書きされる。
選択肢は 2 つ:

- **(a) 再フレーミング（安い）:** 「震源に最も近い外周区間から津波が来る」。
  `m_targetPosition` に震源を入れて `TsunamiAI` を起こせば、`FindSea` が**震源に最も近い海側外周**を選ぶ。
  プレイヤーから見れば「沖の地震 → その方角から津波」で、体験としてはほぼ要求どおり。実装コストはほぼゼロ。
- **(b) 自前の波（中〜高い）:** `WaterSimulation.CreateWaterWave` を直接叩き、
  震源セルを原点に `TYPE_IMPACT` の初期変位 → その後 `TYPE_TSUNAMI` を任意方向へ、という波を自前で組む。
  すべて public API なので技術的には可能だが、`WaterSimulation.SimulateWater` の挙動（波の減衰・反射・寿命）を
  読んでいないので**振る舞いの予測がつかない**。着手前に `SimulateWater` と `WaterWave.GetSeaLevel` の IL を読むこと。

**推奨: (a) を既定にし、(b) は別タスクとして切り離す。**

### 2. 「震源からの距離による震度分布の概念が無い」

**依頼の前提は半分だけ正しい。** 距離減衰は既に 2 系統ある:
- 破壊の距離ランプ（`DestroyBuildings` の `fD`/`fB`、§A-3）
- `CanAffectAt` の「0.125 units/frame で外へ広がるリング」（§A-8）

**無いのは「見せる」部分と「カメラの揺れが距離と強度を反映しない」こと**（§A-7、`RenderInstance` に `m_intensity` が入っていない）。

→ **これが本機能の最良の中核。安い。**
- ハザードマップは既にある（`SubInfoMode.EarthquakeHazard = 5`）。①のモード切替ラッパーをそのまま流用できる。
  ただし `Located && (Emerging|Active)` ゲート（§A-6）は嵐と同じなので、**①で書いた「なぜ空なのか」の説明が
  そのまま地震にも必要**。地震計を建てないと地震はハザードマップに出ない。
- 「震度」は既存の量から**導出できる**。震央距離 `d` と `R = 2000 + 20i` から `1 - d/R` を出せば、
  それは**バニラが実際に倒壊判定に使っている値そのもの**。捏造ではない。
- カメラシェイクは `CameraController.m_cameraShake`(public) に足せる（§A-7）。
  距離と `m_intensity` を反映した揺れを自前で足すのは数十行で済む。

### 3. 「揺れによる火災や倒壊はおそらくランダム」

**ここが今回いちばんの収穫。** `Randomizer(buildingID | disasterID << 16)` は
**(建物, 災害) ごとに固定**で、フレームにもステップにも依存しない（§A-3 の 3）。
つまりバニラは既に「建物ごとに固定のしきい値 → 局所強度がそれを超えたら壊れる」という
**決定論的な強度モデル**になっている。見えていないだけ。

→ **「ランダムに見えるものを、実は強度分布であると見せる」だけで依頼の大半が満たせる。** 非常に安い。
建物ごとの `fD`（倒壊余裕度）と乱数しきい値は、`Randomizer` を同じ種で再構成すれば**MOD 側から完全に再現できる**。

**追加の非ランダム性（建物の構造・築年数など）を入れたい場合**は、`DisasterHelpers` を経由せず
②が自前で建物を走査して `CollapseBuilding` / `BurnBuilding` を呼ぶ形になる。中コスト。
NDR 互換の観点でもこちらが正しい（§E-2）。

### 4. 「長周期地震動による差が無い」

**ABSENT に近い。** `RenderInstance` の揺れは 0.63 と 0.17 rad/frame の正弦 2 本だけで、
低周波成分は 256 フレーム周期の**包絡線**（振幅の脈動）であって地震動ではない（§A-7）。
建物の高さや構造は揺れにも被害にも一切入っていない（`DestroyBuildings` は `m_position` の距離しか見ない）。

→ **長周期地震動を「物理として」入れるのは、②が自前の被害判定を持つことを意味する**（項目 3 の後半と同じコスト）。
建物の高さは `Building.Info.m_generatedInfo` / `Building.m_height` 系から取れるので、
「高層ほど遠方でも被害を受ける」という追加項は作れる。ただし**これは新規の物理であって、可視化ではない**。
③火災旋風と同じ「新しい現象」枠であり、①②の「可視化」枠ではない。**スコープを分けること。**

### 5. 「時間帯による差が無い」

**取得は簡単だが、罠が 1 つ**（§F-1）。
- sim スレッドで使うのは `m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR` か `m_metaData.m_currentDayHour`。
  **`m_currentDayTimeHour` を sim スレッドから読まない。**
- **日夜サイクル OFF だと時刻が永久に 12.0 に固定される。** 時間帯係数がその設定では黙って無効になるので、
  診断（Phase 0.5 の `Assumptions`）に「日夜サイクルが無効なので時間帯差は出ません」を出すこと。

安い。ただし「夜の方が被害が大きい」は**バニラのどこにも根拠が無い新規の物理**なので、
項目 4 と同じくスコープを分けるか、控えめな係数にとどめる。

### 6. 「地震計があるのに波形グラフが見られない」

**`EarthquakeSensorAI` は時系列を一切持たない（ABSENT、§C-1）。**
持っているのは `m_detectionRange` だけで、やっていることは
「毎 tick、半径 `m_detectionRange × 稼働率/100` に `EarthquakeCoverage` を撒く」だけ。

→ **波形を出すなら、②が自分で観測して自分で貯める。** 具体的には:
- サンプル源として使えるものは既にある: `EarthquakeAI.RenderInstance` が計算しているのと同じ式
  （`(sin(t*0.63) + sin(t*0.17)) * 0.3/(1 + dist*0.001) * (0.5 - 0.5cos(t*0.02454369))`）を
  **観測点（地震計の位置）に対して②が独立に評価すれば、バニラの揺れと同位相の波形が得られる。**
  つまり「バニラが描いている揺れの、地震計地点での値」を再現できる。捏造ではない。
- 貯めるのは②側のリングバッファ（sim スレッド、`m_dayTimeFrame` で時刻を付ける）。
- セーブに残すなら `ISerializableDataExtension`（付録 A-2b のロード順の注意がそのまま適用される）。

**コスト: 中。** ただし「地震計を建てるとハザードマップに地震が出るようになり、警報が 38.6 分 → 3 時間に伸びる」
という**既存の効果すら UI に出ていない**ので、波形より先にそれを見せる方が費用対効果は高い（①と同じ構図）。

---

### 着手前にもう 1 つ読むべきもの

- `WaterSimulation.SimulateWater` / `WaterWave.GetSeaLevel` — 上の 1-(b) を選ぶ場合のみ。
  波の減衰・反射・寿命がここにしかない。**PARTIAL のまま実装に入らないこと。**
- `EarthquakeAI` の `m_emergingDuration` / `m_activeDuration` / `m_crackLength` / `m_crackWidth` の実数値。
  DLL には無く、UnityPy の型ツリーも読めなかった。**実行時に `FindDisasterInfo<EarthquakeAI>()` から読んで
  診断ダンプに出すのが最短**（Phase 0.5 の `DiagnosticDump` に 4 行足すだけ）。
  持続時間の設計をこの 4 値の上に組むなら、先に実測すること。
- NDR のバイナリ（本機に未インストール）。§E-2 の「断層 4 円盤の呼び出しを NDR がどう扱うか」だけが未確定。
  ②が `DisasterHelpers` を経由しない限り実害は無い。

### 再現手順

`docs/tools/ilload.ps1` + `ildasm.ps1`。ただし **`ildasm.ps1` には修正が必要**（今回発覚）:

```powershell
# 'ShortInlineI' と 'ShortInlineBrTarget' の [sbyte]$il[$i] は
# 値 > 127 で「値が大きすぎるか小さすぎます」で落ちる（PowerShell の checked 変換）。
# 例: EarthquakeAI.SimulationStep は最初の 1 メソッドで落ちる。
'ShortInlineI'       { $v=[int]$il[$i]; if($v -gt 127){$v-=256}; $text = $v }
'ShortInlineBrTarget'{ $d=[int]$il[$i]; if($d -gt 127){$d-=256}; $text = "IL_{0:X4}" -f ($i + 1 + $d) }
```
今回は scratchpad の複製（`ildasm2.ps1`）に当てて作業した。`docs/tools/` 本体は未変更。
