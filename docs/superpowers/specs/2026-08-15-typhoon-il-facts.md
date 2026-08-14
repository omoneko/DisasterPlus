# Disaster + — ④台風 IL 実測ファクト

- 日付: 2026-08-15
- 対象: Cities: Skylines 1 / `Assembly-CSharp.dll`（Steam 版、`Cities_Data/Managed`）＋ `ColossalManaged.dll`
- 目的: ④台風の設計に着手する前に、依頼文の各要求が**バニラの実装上どこまで成立するか**を IL で確定させる
- 状態: **調査のみ**。`src/` / `tests/` / `Locales/` / `.superpowers/` は一切変更していない。コミットもしていない
- 前提資料（再導出せず引用する）:
  - `2026-08-11-disasterplus-phase0-firewhirl-design.md` 付録 A（`TornadoAI` の実体＝`VortexAI` 車両 / `StartDisaster` は protected / `StartNow` ラッパー / `DAYTIME_FRAMES = 65536` / `burnRadius` リテラル 0）、§4.8 静的キャッシュ fake-null、§4.9 マテリアル借用不可
  - `2026-08-14-earthquake-il-facts.md`（§A-1 `SelfTrigger(64)` の罠 / §E-1 `CreateDisaster` の戻り値と 256 上限 / §B-2 `CreateWaterWave` と `WaterWave` / §B-3 `TsunamiAI.FindSea` / §D-1 地形改変 / §E-2 NDR / §F-1 時刻）
  - `2026-08-12-forecast-design.md` §1.3（ハザードマップの意味の訂正）

判定記号: **CONFIRMED**（IL を読んだ）/ **PARTIAL**（読んだが確定しきれない。何を見れば決まるかを書く）/ **ABSENT**（存在しない）

---

## 0. 要約ファクト表

| # | 問い | 判定 | 一行の答え |
|---|---|---|---|
| A1 | `ThunderStormAI` の全挙動 | **CONFIRMED** | **移動しない**（`m_targetPosition` を書くコードが位相中どこにも無い）。壊すのは落雷経由のみ。`m_intensity` は落雷本数と散布半径に入る |
| A1b | 嵐を MOD から動かせるか | **CONFIRMED（動かせる）** | `DisasterData.m_targetPosition` は public で、Active 中にバニラが書かない。毎 tick 書き戻せば落雷群がついてくる。バニラ自身（`DefaultTool.EndMoving`）が同じことをやっている |
| A2 | 落雷は実体か | **CONFIRMED（実体）** | `WeatherManager.QueueLightningStrike(uint,Vector3,Quaternion,Group)` が **public**。落着点は MOD が完全に指定できる。**同時 20 発が上限**。落雷は `BurnBuilding` / `BurnTree` / 送電線 `CollapseSegment` を実行する |
| A2b | 落雷位置を読めるか | **ABSENT（公開 API 無し）** | `m_lightningQueue` は private。`InstanceManager.GetPosition` の switch に Lightning 分岐は無い。リフレクションのみ |
| A3 | `WeatherManager` に書けるか | **CONFIRMED（書ける／要毎 tick）** | `m_targetRain/Fog/Cloud`、`m_targetDirection`、`m_forceWeatherOn` はすべて public。**再抽選は `target == current` のときだけ**なので毎 tick 書けば奪われない。sim スレッド |
| A3b | 風速を書けるか | **ABSENT** | 風速フィールドは存在しない。`GetWindSpeed` は地形・建物の遮蔽から導出、`SampleWindSpeed` はそれに `1 + rain*0.5 - fog*0.5` を掛けるだけ。**雨を上げる以外に風速を上げる手段は無い**（上限 1.5 倍） |
| B4 | 竜巻の経路と破壊 | **CONFIRMED** | `m_targetPos0` へ直進＋ジッタ、`velocity = 0.9*旧 + 0.1*目標方向`。破壊は `DestroyStuff(preRadius = rMax)`、`rMax = m_destructionRadiusMax * (0.2 + intensity*0.01454545)` |
| B5 | 風による破壊機構 | **ABSENT** | 風速の読み手は `WindTurbineAI`（発電量）/ 木・道路・霧の描画 / ハザードマップ / `ImmaterialResourceManager` だけ。**ダメージ経路はゼロ**。`DisasterHelpers.AddWind` も市民と車両を押すだけで無傷 |
| B6 | `DisasterHelpers` 抜きの広域破壊 | **CONFIRMED（可能。ただし拒否 AI あり）** | `CollapseBuilding` / `BurnBuilding` とも public virtual。**`demolish:false` だと Shelter / DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy が黙って false を返す**。`PowerPoleAI` / `CableCarPylonAI` は `testOnly:true` で false を返す（実際には壊れる） |
| C7 | 使える雲／渦の既製ビジュアル | **ABSENT** | `DisasterInfo.m_effect` は**フィールドごと存在しない**。災害系の `EffectInfo` は `DisasterProperties.m_mediumExplosion` と `MeteorAI.m_impactEffect` の 2 つだけ。雲も渦もプレハブ化されていない |
| C7b | 竜巻の漏斗メッシュ | **CONFIRMED（実行時生成）** | `VortexAI.GenerateMesh()` が 16250 頂点・高さ 2000・半径 50→200 を手続き生成し `m_info.m_mesh` に差す。借りられるのは**メッシュだけ**（§4.9） |
| C8 | バニラの雲の描き方 | **CONFIRMED** | `DayNightDynamicCloudsProperties.Update` が **半径 6400 km のスカイドームメッシュ 1 枚**を `Graphics.DrawMesh` する。雲はノイズシェーダのパラメータ。**ワールド座標を持たない＝台風の上に置けない** |
| D9 | 洪水災害の実装 | **ABSENT** | `GenericFloodAI` は**フィールド 0 個・メソッド 0 個**の空クラス。`FloodBaseAI` も `GetHazardSubMode` しか持たない。**バニラに「水を出す洪水災害」は存在しない** |
| D10a | `TYPE_TSUNAMI` 波を任意地点に置けるか | **ABSENT（既出資料の訂正）** | `GetSeaLevel` は `SimulateWater` の**マップ外周リング（x=0/1080 と z=0/1080）でしか評価されない**。内陸を原点にした津波波は**何も起こさない**。地震ファクト §B-3 の「逃げ道」は成立しない |
| D10b | `TYPE_IMPACT` 波 | **CONFIRMED（任意地点で効く）** | 内陸セルに二次減衰の**水頭差（＝押し）**を足す。ただし**体積は増えない**。既にある水を揺らすだけで、乾いた谷は濡れない。`m_currentTime > m_duration` で自動解放 |
| D10c | 局所的に水位を上げる正解 | **CONFIRMED** | `WaterSimulation.CreateWaterSource` / `LockWaterSource` / `UnlockWaterSource`。`m_type = TYPE_NATURAL(1)` は **`m_target` 水位まで注水し、超えたら吸い戻す自己調整泉**。河川の既存水源の `m_target` を上げれば川全体が上がる |
| D10d | 波・水源の後始末 | **CONFIRMED（要注意）** | `TYPE_TSUNAMI` 波は**自動解放されない**。水源も波も**セーブに焼き付く**（`WaterSimulation.Data.Serialize`）。`LockWaterSource` は Monitor を**取ったまま返る** — `UnlockWaterSource` を必ず finally で呼ぶ |
| E11 | 移動する災害の仕組み | **CONFIRMED** | 竜巻だけが「車両が動く」型。嵐・地震・津波は `m_targetPosition` 固定型。**MOD が経路を持つなら `m_targetPosition` を書くのが正攻法**（嵐）、竜巻は `Vehicle.SetTargetPos(0/1)` で操舵 |
| E12 | 複数災害を束ねられるか | **PARTIAL** | `InstanceManager` のグループは 1 災害 1 グループ（`DisasterAI.CreateDisaster` が `m_ownerInstance.Disaster = id`）。**位置を同期する仕組みはバニラに無い。MOD が自前のコントローラで各災害を毎 tick 動かす**しかない |
| F13 | NDR との衝突 | **PARTIAL（本機に未インストール）** | ④が `DisasterHelpers.DestroyBuildings/DestroyNetSegments` を**一切呼ばなければ衝突ゼロ**。ただし**バニラ竜巻を起こすと NDR が竜巻として横取りする**（`burnRadiusMin == 0 && burnRadiusMax == 0`） |

**今回いちばん危なかった思い込み（＝9 個目の候補）は D10a。**
地震ファクト §B-3 は「`TsunamiAI` を通さず `CreateWaterWave` を直接呼べば任意地点に波を作れる」を**逃げ道として明記していた**。
`SimulateWater` を読んだ結果、**`TYPE_TSUNAMI` の `GetSeaLevel` はマップ外周リングでしか呼ばれない**ことが確定した。
内陸の川に津波波を置く実装は、**エラーも警告も出さずに何も起こさない**。§B-3 の該当行は本書 §D-3 で訂正する。

---

## A. `ThunderStormAI` — 嵐の側

### A-0. 型の輪郭

```
ThunderStormAI : WeatherDisasterAI : DisasterAI
  public Single m_radius
  public UInt32 m_emergingDuration
  public UInt32 m_activeDuration
```
override: `UpdateHazardMap` / `CreateDisaster` / `SimulationStep` / `StartDisaster`(protected) /
`ActivateDisaster`(protected) / `DeactivateDisaster`(protected) / `IsStillEmerging`(protected) /
`IsStillActive`(protected) / `GetFireSpreadProbability` / `CanSelfTrigger` / `GetPosition` / `GetHazardSubMode`。

> **`IsStillClearing` の override は無い。** base（`DisasterAI.IsStillClearing`）は
> 「災害グループの `m_refCount > 1`」＝**燃えている建物などが残っている間だけ Clearing**。
> `WeatherDisasterAI` は `GetEstimatedActivationFrame` しか持たない薄い中間クラス。
> `m_radius` / `m_emergingDuration` / `m_activeDuration` の実数値は DLL に無い（プレハブ値）。
> 実行時に `DisasterManager.FindDisasterInfo<ThunderStormAI>()` から読むこと。**PARTIAL**。

### A-1. 位相と、嵐が何をするか — CONFIRMED

`ThunderStormAI.StartDisaster`（地震・竜巻と同型）:
```
IL_0003  call DisasterAI::StartDisaster          // Emerging(4) を立て m_startFrame = 現フレーム
IL_000E  ldc.i4.s 64 ; and ; brfalse -> ret      // ★ SelfTrigger(64) が無ければ以降を一切やらない
IL_0027  m_targetPosition.y = TerrainManager.SampleDetailHeight(m_targetPosition)
IL_003F  m_activationFrame = m_startFrame + m_emergingDuration
IL_004B  m_flags |= 256 (Significant)
IL_005C  DisasterManager.m_randomDisasterCooldown = 0
```
`IsStillEmerging`: `m_activationFrame == 0` なら **true（永久）**、それ以外は `currentFrame < m_activationFrame`。
→ **地震と同じ「SelfTrigger を落とすと Emerging で固まる」型**（津波とは違う。地震ファクト §A-1 の区別がそのまま効く）。
`IsStillActive`: `currentFrame - m_activationFrame < m_activeDuration`。
`ActivateDisaster`: base ＋ `DisasterManager.FollowDisaster(id)` だけ。
`DeactivateDisaster`: base ＋ `Significant` なら `UnDetectDisaster` ＋ `SelfTrigger` なら `m_targetRain = 0; m_targetCloud = 0`。

`ThunderStormAI.SimulationStep`（全 IL は §A-2 に掲載）:

**Emerging 分岐（IL_0008–00A9）**
```
if (m_activationFrame != 0 && currentFrame + 1755 >= m_activationFrame) {
    if (m_flags & 256 /*Significant*/) DisasterManager.DetectDisaster(id, located: false);   // ldc.i4.0
    if (m_flags & 64  /*SelfTrigger*/) {
        WeatherManager.m_forceWeatherOn = 2f;
        WeatherManager.m_targetFog      = 0f;
        WeatherManager.m_targetRain     = 1f;
        WeatherManager.m_targetCloud    = 1f;
    }
}
return;
```
> `located: false` なので、**嵐は自分ではハザードマップに載らない**。載せられるのは
> 気象レーダー（`WeatherRadarAI.ProduceGoods`）だけ。`2026-08-12-forecast-design.md` §1.3 の通り。

**Active 分岐（IL_00A9–02A6）**
```
if ((m_flags & 64) == 0) return;                       // ★ SelfTrigger 必須
WeatherManager.m_forceWeatherOn = 2f; m_targetFog = 0f; m_targetRain = 1f; m_targetCloud = 1f;

f = SimulationManager.m_currentFrameIndex
c = 100
c = Min(c, (f - m_activationFrame) >> 3)                          // IL_010E–011E 立ち上がり
c = Min(c, (m_activationFrame + m_activeDuration - f) >> 3)       // IL_011F–0136 立ち下がり
c = (c * m_intensity + 50) / 100                                  // IL_0137–0145 整数
r = new Randomizer(m_randomSeed ^ (f >> 8))                       // IL_0146–0153 ステップごと
n = r.Int32(Max(1, c/20), Max(1, 1 + c/10))                       // IL_0158–0175 本数
R = m_radius * (0.25f + m_intensity * 0.0075f)                    // IL_0177–0191 散布半径
grp = InstanceManager.GetGroup(new InstanceID{ Disaster = id })

for (k = 0; k < n; k++) {
    a    = r.Int32(10000) * 0.0006283185f                         // 0 .. 2π
    d    = Sqrt(r.Int32(10000) * 0.0001f) * R                     // 円板一様
    when = f + r.UInt32(256)                                      // 0..255 フレーム後
    p    = m_targetPosition + (cos(a)*d, 0, sin(a)*d)
    p.y  = TerrainManager.SampleRawHeightSmoothWithWater(p, false, 0f)
    q    = AngleAxis(r.Int32(360), up) * AngleAxis(r.Int32(-15,15), right)
    WeatherManager.QueueLightningStrike(when, p, q, grp);          // IL_0291 戻り値は pop
}
```

確定する事実:

1. **嵐は移動しない。** `m_targetPosition` に書き込む命令は `StartDisaster` の `.y` 成分だけ。
   `SimulationStep` にも `ActivateDisaster` にも無い。全アセンブリで `stfld DisasterData::m_targetPosition` を
   走査した結果、災害 AI で書いているのは `ForestFireAI` / `StructureCollapseAI` / `StructureFireAI` の
   `StartDisaster`、`DisasterManager.StartRandomDisaster`、`WeatherManager.QueueLightningStrike`、
   `DisasterWrapper.CreateDisaster`、`DisasterTool.<CreateDisaster>`、**`DefaultTool.<EndMoving>`** のみ。
2. **嵐自身は建物を壊さない。** `DisasterHelpers` の呼び出しが 1 つも無い。被害はすべて落雷経由。
3. `m_intensity` は**本数（`c`）と散布半径（`R`）の両方**に線形に入る。クランプは無い。
   intensity 55 で `R = 0.6625 × m_radius`、100 で `1.0 ×`、255 で `2.1625 ×`。
4. `n` は最大でも `Int32(c/20, 1 + c/10)`。intensity 255・最盛期で `c = 255` → `n = 12..25`。
   **ただし落雷キューの上限は 20（§A-3）** なので、それを超える要求は黙って捨てられる。
5. 落雷は災害グループ `grp` に紐づく。→ 落雷で燃えた建物が `m_refCount` を上げ、
   `IsStillClearing`（base）を伸ばす。**嵐は火が消えるまで終わらない。**

### A-2. `UpdateHazardMap` — CONFIRMED（ゲートは嵐・竜巻・地震で完全同一）

```
IL_0000  ldarg.2; ldfld m_flags; ldc.i4 4096; and; brfalse -> ret     // Located
IL_0011  ldarg.2; ldfld m_flags; ldc.i4.s 12;  and; brfalse -> ret    // Emerging|Active
```
塗る形:
```
r      = new Randomizer(m_randomSeed)                       // 災害ごとに固定
c      = m_targetPosition + (r.Int32(-1000,1000)*0.001f, 0, r.Int32(-1000,1000)*0.001f) * 200f
R      = m_radius * (0.25f + m_intensity * 0.0075f)         // 落雷散布半径と同じ式
rIn    = R * 0.5f
rOut   = R + 400f                                           // 200 * 2
セル 38.4 m / 256² / 中心オフセット 128 / インデックスは [2, 253] にクランプ
各セル:
    d = |cellCenter - c|
    if (d >= rOut) continue
    v = Min(0.6f, 1f - (d - rIn) / (rOut - rIn))                            // IL_01D6–01F1
    v = Min(1f, v + WeatherManager.GetWindSpeed(cellXZ) * 0.3f - 0.3f)      // IL_01F3–021B
    if (v <= 0) continue
    map[z*256 + x] = 255 - (255 - map[i]) * (255 - Round(v*255)) / 255      // 乗算合成
```
`GetHazardSubMode` → `subMode = 1` = **`InfoManager.SubInfoMode.LightningHazard`**（enum 実測）。
参考: `TornadoAI` → 4 (`TornadoHazard`)、`FloodBaseAI` → 0 (`FloodHazard`)、`EarthquakeAI` → 5。

> **注意（雨で落雷ハザードは上がらない）。** ここで使われる `GetWindSpeed(Vector2)` は
> `Clamp((selfHeight - totalHeight)*0.015625*0.02 + 1, 0, 2)`、すなわち**地形・建物の遮蔽だけ**の値で、
> 天候は一切入らない（`GetWindSpeedFactor` を呼ばない）。天候が入るのは `SampleWindSpeed` のほうだけ。

### A-3. 落雷は実体か → **実体である（CONFIRMED）**

`WeatherManager.QueueLightningStrike(UInt32 startFrame, Vector3 position, Quaternion rotation, InstanceManager.Group group) : bool` — **public**。

```
IL_0000  startFrame = Max(startFrame, currentFrameIndex + 15)              // 最短 15 フレーム後
IL_0018  maxDistance = 200f
IL_002A  FindStrikeTarget(position, 200f, heightFactor: 3f, out ushort building, out uint tree)
IL_0035  building != 0 → position = Building.CalculateMeshPosition(...) の上面から
                          Building.RayCast で実際の着弾点を取る
IL_0109  else tree != 0 → position = 樹木頂点付近（m_generatedInfo.m_size.y * scale * 0.5）
IL_01A6  LightningStrike { m_startFrame, m_position, m_rotation } を組む
IL_01C6  m_lightningQueue の m_startFrame == 0 の空きスロットを探して書く
IL_020E  group != null なら InstanceManager.SetGroup(InstanceID{ Lightning = (byte)i }, group)
IL_0246  空きが無ければ: if (m_lightningQueue.m_size >= 20) return false;   // ★ ldc.i4.s 20
IL_024D  そうでなければ FastList.Add
```

`WeatherManager.SimulationStepImpl` のキュー処理（IL_08E3–09D3）:
```
foreach i in m_lightningQueue:
    sf = queue[i].m_startFrame
    if (sf == 0) continue
    if (sf == m_currentFrameIndex)  StrikeNow(i);                 // ★ 完全一致
    else if (sf + 45 < m_currentFrameIndex) {                     // 45 フレームで期限切れ
        InstanceManager.ReleaseInstance(InstanceID{ Lightning = (byte)i });
        queue[i].m_startFrame = 0;
    }
末尾の空スロットを詰める
```

`WeatherManager.StrikeNow(int lightningIndex)`（private）:
```
IL_004D  AudioManager.AddEvent(EffectGroup, m_properties.m_lightningSound の variation,
                               pos, zero, 10000f, 1f, Randomizer.Int32(600,1100)*0.001f, index)
IL_00AF  FindStrikeTarget(strike.m_position, maxDistance: 5f, heightFactor: 0f, out b, out t)
IL_00C7  grp = InstanceManager.GetGroup(InstanceID{ Lightning = (byte)index })

if (b != 0) {
    if (BuildingInfo.m_buildingAI is PowerPoleAI) {                        // IL_0102
        node = Building.FindParentNode(b);
        foreach seg of node (最大 8):  NetAI.CollapseSegment(seg, ref data, grp, demolish:false)
        return;                                                            // ★ 送電線が落ちる
    }
    policies = District.m_cityPlanningPolicies at building.m_position       // IL_0206
    if ((policies & 4096 /*CityPlanning.LightningRods*/) != 0
        && Randomizer.Int32(100) < 70) return 0;                            // ★ 70% を無効化
    return BuildingAI.BurnBuilding(b, ref data, grp, testOnly:false);       // IL_026B
}
if (t != 0) return TreeManager.BurnTree(t, grp, Randomizer.Int32(140, 255)); // IL_02A3
return false;
```

`FindStrikeTarget(Vector3 position, float maxDistance, float heightFactor, out ushort building, out uint tree)`（private）:
建物グリッド（セル 64、オフセット 135、解像度 270）を `±72` で走査。
`m_flags & 4194323 (0x400013 = Created|Deleted|Untouchable|Collapsed)` で除外、
`m_fireIntensity` を見て既燃を除外、**`BuildingAI.BurnBuilding(..., testOnly: true)` で燃えるか事前判定**（IL_02E2）、
`m_generatedInfo.m_size.y`（建物高さ）に `heightFactor` を掛けて距離から引く＝**高い建物が優先される**。
続いて樹木グリッド（セル 32、解像度 540）を同様に走査。

| 問い | 答え |
|---|---|
| 落着点を MOD が決められるか | **できる。** 4 引数版が public。座標・回転・遅延・所属グループを全部指定できる |
| 落着点を MOD が読めるか | **公開 API 無し。** `m_lightningQueue` は private `FastList<LightningStrike>`。`InstanceManager.GetPosition` の 20 分岐 switch に Lightning は無い。リフレクションのみ |
| 同時発数 | **20 が上限**（`ldc.i4.s 20`）。超えた要求は `false` を返して黙って捨てられる |
| 被害 | 建物着火（`BurnBuilding`）／樹木着火（`BurnTree` 140–255）／送電線切断（`CollapseSegment`） |
| 無効化 | 地区政策 `CityPlanning.LightningRods (4096)` が建物落雷の **70%** を消す |
| スレッド | `QueueLightningStrike` の呼び元は `ThunderStormAI.SimulationStep` と `WeatherManager.SimulationStepImpl` — **sim スレッド** |

**環境落雷（雨だけで起きる分）— CONFIRMED、④に直撃する副作用**

`WeatherManager.SimulationStepImpl` 末尾（IL_09D3–0A30）:
```
if (m_currentRain > 0.8f && m_lightningQueue.m_size == 0) {
    x     = m_currentRain * 5f - 4f;                       // 0 .. 1
    delay = 5000 - Round(x * 4000f);                       // 1000 .. 5000 フレーム
    QueueLightningStrike(Randomizer.UInt32(delay));        // 1 引数版
}
```
1 引数版（IL_00B1–01B6）は `DisasterManager.FindDisasterInfo<ThunderStormAI>()`（generic 引数をトークン解決で確認）で
プレハブを引き、**Active な雷雨災害を探し、無ければ `CreateDisaster` → `m_intensity = 10` → `m_targetPosition = ランダム点`
→ `StartNow` → `ActivateNow` で新規に雷雨災害を作る**。
`SelfTrigger` は立てないので、その嵐自身は天候も落雷も生成しない（`& 64` ゲート）— **落雷をグループに束ねるだけの器**。

> **④の設計に効く帰結。** 台風が `m_currentRain` を 0.8 超に保つと、**ゲームが勝手に雷雨災害を作り続ける**。
> 災害スロットを食い、④が「自分の嵐の数」を数える処理を狂わせる。
> 逆に、④が落雷キューを空にしない限り環境落雷は発生しない（`m_size == 0` 条件）。
> **④が自前で落雷を撒くなら、環境落雷は自動的に抑制される。**

### A-4. `WeatherManager` に何が書けるか — CONFIRMED

public フィールド（`SimulationManagerBase<WeatherManager, WeatherProperties>`）:
```
WindCell[] m_windGrid          Texture2D m_windTexture
Single m_windDirection / m_targetDirection / m_directionSpeed
Single m_currentTemperature / m_targetTemperature / m_temperatureSpeed
Single m_currentRain  / m_targetRain
Single m_currentFog   / m_targetFog
Single m_currentCloud / m_targetCloud
Single m_forceWeatherOn
Single m_groundWetness
Single m_currentNorthernLights / m_targetNorthernLights
Single m_currentRainbow / m_targetRainbow
Boolean m_enableWeather
Single m_lastLightningIntensity / m_flashingReduction
static Single WINDGRID_CELL_SIZE = 135      static Int32 WINDGRID_RESOLUTION = 128
```
（private: `m_lightningQueue`, `m_modifiedX1/X2`, `m_modified`, `m_windMapVisible`, `m_windZone`, `m_materialBlock`）

`WeatherManager.SimulationStepImpl(int subStep)`（**sim スレッド**）の要点:

```
IL_0000  if (subStep == 0) return;  if (subStep == 1000) return;

// --- 風向 ---
IL_0017  delta = Abs(Mathf.DeltaAngle(m_targetDirection, m_windDirection) * 0.001f)
IL_0034  m_directionSpeed = Min(m_directionSpeed + 0.001f, delta)
IL_004C  m_windDirection  = Mathf.MoveTowardsAngle(m_windDirection, m_targetDirection, m_directionSpeed)
IL_0069  m_windDirection  = Mathf.DeltaAngle(0f, m_windDirection)          // -180 .. 180 に正規化
IL_007F  if (delta < 0.0002f)                                              // ★ 到達したら
IL_008A      m_targetDirection = Randomizer.Int32(10000) * 0.036f          //    勝手に再抽選

// --- 天候を動かしてよいか ---
IL_00A7  w = m_enableWeather ? 1f : 0f
IL_00C2  if (m_forceWeatherOn != 0f) {
IL_00D2      m_forceWeatherOn = Max(0f, m_forceWeatherOn - 0.001f)         // ★ 1 ステップ 0.001 減衰
IL_00EE      w = Max(w, m_forceWeatherOn)
         }
IL_00FB  if (w >= 1f && (ToolController.m_mode & 1) != 0) {                // ゲームモード
             // 雨
IL_011E      if (m_targetRain > m_currentRain) m_currentRain = Min(target, current + 0.0002f)
IL_01EF      else if (m_targetRain < m_currentRain) m_currentRain = Max(target, current - 0.0002f)
IL_0222      else if (Randomizer.Int32(20000) == 0) { ...再抽選... }       // ★ 等しいときだけ
             // 霧: 同型、レート 0.0002
             // 雲: 同型、レート 0.0008                                    // IL_046D / IL_04A0
         } else {
IL_053D      m_targetRain = m_targetFog = m_targetCloud = 0f;
             m_currentRain  = Min(m_currentRain,  w);
             m_currentFog   = Min(m_currentFog,   w);
             m_currentCloud = Min(m_currentCloud, w);                      // ★ 全部潰される
         }

// --- 地面の湿り ---
IL_0594  m_groundWetness = Clamp01(w - (w*0.00013f + 0.00001f) + Min(m_currentRain, 0.25f)*0.00061f)
// --- 気温 ---
IL_0777  m_temperatureSpeed = Min(m_temperatureSpeed + 0.0001f, |(target - current)*0.001f|)
IL_07AA  m_currentTemperature = Mathf.MoveTowards(current, target, m_temperatureSpeed)
IL_07C7  昼夜位相 t = |m_dayTimeFrame/DAYTIME_FRAMES - 0.5| * 2 から
         WeatherProperties の min/max を Lerp して target を再抽選
```

| 問い | 答え |
|---|---|
| 雨・雲・霧を MOD が駆動できるか | **できる。`m_targetRain/Cloud/Fog` を毎 sim tick 書く。** 再抽選は `target == current` のときにしか起きないので、毎 tick 書いていれば奪われない。**1 回書いて放置すると、到達後 1/20000/step で勝手に上書きされる** |
| 遷移速度 | 雨・霧 `0.0002/step`、**雲 `0.0008/step`**。0 → 1 に雨は 5000 step、雲は 1250 step かかる。`m_currentRain` を直接書けば即時だが、`m_targetRain` と一致させないと再抽選対象になる |
| 天候 OFF のプレイヤー | `m_enableWeather == false` だと `w = 0` になり、**`m_forceWeatherOn >= 1` を毎 tick 書かない限り全部 0 に潰される**。バニラの嵐・竜巻は毎災害ステップ `m_forceWeatherOn = 2f` を書いている（減衰 0.001/step なので 2000 step 分の猶予） |
| 風向 | `m_targetDirection` を毎 tick 書けば追従する。到達（0.2° 未満）した瞬間にランダム再抽選されるので**毎 tick 書き続ける** |
| 旋回速度 | `m_directionSpeed` は `+0.001/step` ずつしか上がらず、上限は残角×0.001。**台風の急旋回は表現できない**（`m_windDirection` を直接書けば可能。書き手は本メソッドと `Data.Deserialize` だけ） |
| 風速 | **フィールドが存在しない（ABSENT）**。§A-5 |
| スレッド | `SimulationStepImpl` は sim スレッド。`Update`（`m_windZone` の回転）と `EndRenderingImpl` はメイン |

### A-5. 風速 — **書けるフィールドは存在しない（ABSENT）**

```
WeatherManager.GetWindSpeedFactor() : Single      // private
IL_0000  return 1f + m_currentRain * 0.5f - m_currentFog * 0.5f          // 上限 1.5

WeatherManager.GetWindSpeed(Vector3 pos) : Single // public
IL_0000  x = Clamp(FloorToInt(pos.x/135 + 64 - 0.5f), 0, 127) ; z 同様
IL_0062  h = m_windGrid[z*128 + x].m_totalHeight
IL_0079  return Clamp((pos.y - h*0.015625f) * 0.02f + 1f, 0f, 2f)        // ★ 天候は入らない

WeatherManager.GetWindSpeed(Vector2 pos) : Single // public
IL_0082  return Clamp((selfHeight - totalHeight)*0.015625f*0.02f + 1f, 0f, 2f)

WeatherManager.SampleWindSpeed(Vector3 pos, bool ignoreWeather) : Single  // public
         m_windGrid の 4 隅を双一次補間 → 高度補正 → Clamp01
IL_015A  × GetWindSpeedFactor()   （ignoreWeather のときは掛けない）
```
`m_windGrid` は `WindCell { m_selfHeight, m_totalHeight, m_deltaHeight }`（すべて UInt16）で、
中身は**地形＋建物の高さ（遮蔽）**。`CalculateSelfHeight` / `CalculateTotalHeight` / `CalculateDeltaHeight` /
`AfterTerrainUpdate` / `AreaModified` が書く。**風の速度も向きも入っていない。**

風速の読み手（全アセンブリ走査）:

| 読み手 | 用途 |
|---|---|
| `WindTurbineAI` の `GetColor` / `GetElectricityRate` / `BuildingLoaded` / `CheckBuildPosition` / `ProduceGoods` / `GetLocalizedStats` | **発電量** |
| `NetNode.RefreshEndData` ほか多数 / `NetSegment.RenderInstance` / `TreeInstance.RenderInstance` | 描画（フェンス・木の揺れ） |
| `ThunderStormAI.UpdateHazardMap` / `TornadoAI.UpdateHazardMap` | ハザード値の補正 |
| `ImmaterialResourceManager.CalculateLocalResources` → `AverageWind(Rect)` | 汚染の拡散 |
| `WeatherManager.EndRenderingImpl` | シェーダ大域変数 `_WindDirection`（xyz＝向き、w＝`GetWindSpeedFactor()`） |
| `FogEffect.OnRenderImage` / `FogProperties.Update` / `DayNightDynamicCloudsProperties.Update` | 霧と雲のスクロール |

→ **「風速による破壊」はバニラのどこにも無い。** ④が風被害を作るなら**新規の物理**であり、
「バニラの数値を見せる」枠ではない（③火災旋風と同じスコープ）。
唯一の間接効果は **`m_currentRain` を上げると `SampleWindSpeed` が最大 1.5 倍になり、風力発電が増える**こと。

---

## B. 風 — 破壊モデル

### B-1. `VortexAI.SimulationStep`(6 引数) の全体 — CONFIRMED（付録 A-1 の続き）

付録 A-1 で既出の事実（`m_targetPos0 → m_targetPos1` の入れ替え、スピンダウン、`ArriveAtDestination`、
`burnRadiusMin/Max` のリテラル 0）は再掲しない。**新しく確定した部分だけ**を書く。

```
IL_0000  grp = InstanceManager.GetGroup(InstanceID{ Vehicle = vehicleID })
IL_0022  s = 1f ; v = 1f
IL_002F  if (grp != null && grp.m_ownerInstance.Disaster != 0) {
IL_0049      s = 0.2f + disaster.m_intensity * 0.01454545f       // 破壊半径スケール
IL_006E      v = disaster.m_intensity * 0.01818182f              // 見た目（m_lightIntensity.y）
         }
IL_008E  frame.m_position += frame.m_velocity * 0.5f             // 前半ステップ
IL_00AF  maxSpeed = m_info.m_maxSpeed
IL_00BC  dir  = (Vector3)m_targetPos0 - frame.m_position ; dist = LengthXZ(dir)
IL_00DD  if (dist < maxSpeed) { m_targetPos0 = m_targetPos1; dir/dist を取り直す }   // 既出
IL_0113  dist > 1 → m_angleVelocity = Min(1, +0.05) ; >0.95 で m_flags |= 0x4000
IL_0162  dist <= 1 → m_angleVelocity = Max(0, -0.05) ; <0.95 で m_flags &= ~0x4000
IL_01A0            → <0.05 かつ ArriveAtDestination() なら DeactivateNow + Vehicle.Unspawn

// ---- 経路（新規）----
IL_021C  r = new Randomizer(vehicleID ^ (m_currentFrameIndex >> 9))    // ★ 512 フレームごとに変わる
IL_0232  dir.y = 0
IL_023E  dir = Vector3.ClampMagnitude(dir, maxSpeed * 0.5f)
IL_024F  j   = Min(maxSpeed * 0.5f, dist * 0.1f)
         dir.x += r.Int32(-1000, 1000) * j * 0.001f
         dir.z += r.Int32(-1000, 1000) * j * 0.001f
IL_02C7  dir = Vector3.ClampMagnitude(dir, maxSpeed)
IL_02D2  vel = frame.m_velocity * 0.9f + dir * 0.1f                    // ★ 一次遅れ
IL_0304  vel.y = SampleRawHeightSmoothWithWater(pos + vel) - pos.y     // 地形追従
IL_0329  frame.m_velocity = vel
IL_0331  frame.m_position += vel * 0.5f                                // 後半ステップ
IL_0352  frame.m_rotation = identity ; m_swayVelocity = m_swayPosition = zero
IL_0373  frame.m_steerAngle = m_angleVelocity ; frame.m_travelDistance = dist
IL_0387  frame.m_lightIntensity = (vehicleID / 16384f, v, 0, 0)

// ---- 被害（新規：実引数を確定）----
IL_03C7  if ((vehicle.m_flags & 0x4000) != 0) {
IL_03D8    if (m_destructionRadiusMax >= 1f) {
             rMin = m_destructionRadiusMin * s
             rMax = m_destructionRadiusMax * s
IL_0466      DisasterHelpers.AddWind(
                 position:        (pos.x, pos.y + rMax*0.75f, pos.z),
                 radius:          rMax * 1.5f,
                 directionalWind: (vel.x, 80f, vel.z),
                 rotationalWind:  0.5f,
                 radialWind:      -40f,
                 group:           grp)
IL_048A      DisasterHelpers.DestroyStuff(
                 seed: vehicleID, group: grp, position: frame.m_position,
                 totalRadius: rMax, preRadius: rMax, removeRadius: 0f,
                 destructionRadiusMin: rMin, destructionRadiusMax: rMax,
                 burnRadiusMin: 0f, burnRadiusMax: 0f)                 // リテラル（既出）
IL_04A1      DisasterHelpers.BurnGround(XZ(frame.m_position), rMax, 0.7f)
           }
IL_04A6    if (m_upgradeRadiusMax >= 1f)
IL_04DD      DisasterHelpers.UpgradeBuildings(vehicleID, grp, pos,
                 preRadius: m_upgradeRadiusMax*s, upgradeRadiusMin: m_upgradeRadiusMin*s,
                 upgradeRadiusMax: m_upgradeRadiusMax*s, probability: 1f)
         }
IL_04EC  VehicleAI.SimulationStep(...)
```

`DisasterHelpers.DestroyStuff` の 10 引数版が実際に何を呼ぶか（IL_0000–0044、引数の対応が紛らわしいので明記）:

```
DestroyBuildings  (seed, group, position, preRadius=arg4, removeRadius=arg5,
                   dMin=arg6, dMax=arg7, bMin=arg8, bMax=arg9, probability = 1f)   // ★ totalRadius は渡らない
DestroyNetSegments(seed, group, position, totalRadius=arg3, removeRadius=arg5, dMin=arg6, dMax=arg7)
DestroyTrees      (seed, group, position, totalRadius=arg3, removeRadius=arg5, dMin, dMax, bMin, bMax)
DestroyProps      (seed,        position, totalRadius=arg3, removeRadius=arg5, dMin, dMax)
```

> **半径の読み違いに注意（断層帯幅と同じ罠）。**
> 竜巻の建物破壊で効くのは **`preRadius = rMax`（一次カリング）** と
> `fD = (rMax - dist) / Max(1, rMax - rMin)`、`probability = 1f`。
> **`totalRadius` は建物に一切効かない**（道路・木・プロップ専用）。
> `rMin` 以内は `fD >= 1` で確実に倒壊、`rMax` で 0。`rMax` を超えた建物は `preRadius` で先に落ちる。
> `m_destructionRadiusMin` / `m_destructionRadiusMax` の実数値は DLL に無い（プレハブ値）。**PARTIAL**。

`DisasterHelpers.AddWind(Vector3 position, float radius, Vector3 directionalWind, float rotationalWind, float radialWind, InstanceManager.Group group)` — **public static**。
中身は `AddWindCitizens` ＋ `AddWindVehicles` の 2 行だけ（IL_0000–001A）。
`AddWindCitizens` は市民グリッド（セル 8、解像度 1080）を走査して `CitizenAI.AddWind` を呼ぶ。
**建物・道路・樹木には一切触らない。純粋に市民と車両を吹き飛ばす演出。**

### B-2. `TornadoAI` 側（新規部分のみ）— CONFIRMED

`TornadoAI.SimulationStep`（IL_0000–00CA、全 202 バイト）は**天候しか触らない**:
```
Emerging: if (currentFrame + 1755 > m_activationFrame) {
              DisasterManager.DetectDisaster(id, located: false);      // ldc.i4.0（既出）
              m_forceWeatherOn = 2f; m_targetFog = 0f;
              m_targetRain = m_targetRain(AI フィールド); m_targetCloud = 1f;
          }
Active:   m_forceWeatherOn = 2f; m_targetFog = 0f;
          m_targetRain = m_targetRain(AI); m_targetCloud = 1f;         // ★ SelfTrigger ゲート無し
```
`TornadoAI.m_targetRain` は **AI の public Single フィールド**（プレハブ値）。嵐と違い 1f 固定ではない。
`IsStillActive` は **`base || グループ m_refCount >= 2`** ＝ **渦車両が生きている間 Active**。
`DeactivateDisaster` は `UnDetectDisaster` ＋ `m_targetRain = 0; m_targetCloud = 0`。

`TornadoAI.UpdateHazardMap`（IL 抜粋）: 同じ 2 段ゲート（4096 / 12）。
`m_vortexInfo.m_vehicleAI` の null 判定のあと、`m_targetPosition` を中心に
`m_angle` 方向へ `±(m_intensity*10 + 400) * 2 * 0.25` の線分を張り、両端に `±300` のジッタ、
線分距離 `Segment2.DistanceSqr` から `m_intensity*20 + …` の幅で減衰、
最後に `Min(0.75f + GetWindSpeed(cell)*0.25f, …)` を掛けて乗算合成。

---

## C. 雲

### C-1. 使える既製の雲／渦ビジュアル → **ABSENT**

```
DisasterInfo のフィールド全列挙（リフレクション）:
  MilestoneInfo m_UnlockMilestone   Placement m_placementStyle
  Color m_markerColor               Mesh m_markerMesh
  Int32 m_randomProbability         Int32 m_cooldownFrames
  UITextureAtlas m_IconAtlas        String m_Icon
  DisasterInfo m_baseInfo           RadioContentInfo m_warningBroadcast / m_activeBroadcast
  DisasterAI m_disasterAI           UInt32 m_lastSpawnFrame
  DisasterTypeGuide m_disasterWarningGuide   Int32 m_finalRandomProbability
```
> **`DisasterInfo.m_effect` は存在しない。** フィールドごと無い。依頼調査票の前提はここで崩れる。

`EffectInfo` のサブクラス（全型走査）: `SoundEffect` / `EngineSoundEffect` / `FireEffect` / `LightEffect` /
`ParticleEffect` / `MovementParticleEffect` / `ShipTrailParticleEffect` / `MultiEffect` の 8 つ。
災害まわりで `EffectInfo` 型のフィールドを持つのは **`DisasterProperties.m_mediumExplosion`** と
**`MeteorAI.m_impactEffect`** の 2 つだけ。**雲も渦も嵐も、プレハブ化された効果は存在しない。**

`Mesh` / `Material` を持つ関連フィールドの全列挙:
```
DisasterProperties.m_markerMaterial : Material     // 災害マーカー
DisasterInfo.m_markerMesh           : Mesh         // 災害マーカー
WeatherProperties.m_lightningMesh   : Mesh         // 稲妻（GenerateLightningMesh で実行時生成）
WeatherProperties.m_lightningMaterial : Material
VortexAI.m_generatedMesh            : private static Mesh   // ★ 竜巻の漏斗
TornadoAI.m_vortexInfo              : VehicleInfo
MeteorStrikeAI.m_meteorInfoSmall / m_meteorInfoLarge : VehicleInfo
```

**竜巻の漏斗メッシュ — CONFIRMED（実行時生成）**
```
VortexAI.InitializeAI():
IL_0006  if (m_generatedMesh == null) m_generatedMesh = GenerateMesh();   // ★ private static
IL_0021  m_info.m_mesh = m_generatedMesh;

VortexAI.GenerateMesh() の定数（IL_0000–0043）:
  頂点数 16250 / 高さ 2000 / 下端半径 50 / 上端半径 200 /
  リング分割 4・4・6 / Randomizer(2975689) 固定シード
  以降、sin/cos と 6.283185(2π)・25.13274(8π) で螺旋状のリボンを組む
```
`VortexAI.RenderExtraStuff` は `m_info.m_mesh` と `m_info.m_material` を
`VehicleManager.m_materialBlock`（`ID_TyreMatrix` / `ID_TyrePosition` / `ID_LightState` / `ID_Color` を設定）付きで
`Graphics.DrawMesh` する。**§4.9 の「CS のマテリアルはエンジン供給の per-instance データを要求する」実例。**

| 問い | 答え |
|---|---|
| 巨大な渦メッシュは既にあるか | **ある（高さ 2000 m の漏斗）**。ただし `private static Mesh` なのでリフレクションか `m_vortexInfo.m_mesh` 経由 |
| そのマテリアルを借りられるか | **借りてはいけない**（§4.9）。`MaterialPropertyBlock` を自前で組めば理屈上は可能だが、④の規約は「メッシュだけ借り `Shader.Find("Standard")` で自作」 |
| `private static Mesh` の fake-null | **`m_generatedMesh` は CS 側の静的キャッシュで、`== null` 比較で自己修復する（IL_0006）**。要素判定が必要な `static Mesh[]` の罠（§4.8）とは別物。④が同じパターンを書くときは配列にしないこと |
| 「巨大でゆっくり回る雲」の既製品 | **無い。④が自分でメッシュを組むしかない** |

### C-2. バニラの雲の描き方 — CONFIRMED

`DayNightDynamicCloudsProperties`（MonoBehaviour）:
```
public Int32   m_CloudLayer          public Single m_WindForce
public Single  m_Coverage            public Single m_MaxCoverage
public Single  m_NoiseTiling         public Single m_EvolutionSpeed
public Single  m_HorizonOffset       public Vector3 m_PlanetCenterKm / m_PlanetNormal
public Material m_CloudMaterial      public Mesh    m_SkydomeBaseMesh
private Mesh   m_SkydomeMesh
protected static Single EARTH_RADIUS = 6400
```
`Update()`:
```
IL_0016  m_Coverage = m_MaxCoverage * WeatherManager.SampleCloudCoverage(Vector3.zero, false)   // ★ 毎フレーム上書き
IL_0033  a = WeatherManager.m_windDirection * 0.01745329f    (deg→rad)
IL_0044  dt = SimulationManager.m_simulationTimeDelta
IL_004F  if (m_Coverage <= 0) return;                        // 雲量 0 なら描画自体しない
IL_0094  scroll = m_WindForce * new Vector2(sin(a), cos(a))
IL_00B1  m_CloudPosition.xy += m_NoiseTiling  * scroll * dt
         m_CloudPosition.zw += m_EvolutionSpeed * m_NoiseTiling * scroll * dt
IL_01A3  m_CloudMaterial.SetVector("_ColorFromLight" / "_ColorFromSky" / "_CloudPosition" /
                                   "_PlanetCenterKm" / "_PlanetNormal" / "_PlanetTangent" /
                                   "_PlanetBiTangent" / "_SunDirection")
IL_0264  SetFloat(ID_Coverage, m_Coverage) ; SetFloat(ID_NoiseTiling, m_NoiseTiling)
IL_0317  Graphics.DrawMesh(m_SkydomeMesh, new Vector3(0f, m_HorizonOffset, 0f),
                           Quaternion.identity, m_CloudMaterial, m_CloudLayer)
```
`InitSkydomeMesh()` は `m_SkydomeBaseMesh` を複製し、**`bounds = Vector3.one * 2e9`** を設定して
`name = "SkydomeMesh"`、`hideFlags = 52` にする。

| 事実 | 内容 |
|---|---|
| 雲の実体 | **半径 6400 km の球殻に貼られたノイズシェーダ**。原点固定・回転なし・スクロールのみ |
| 位置 | `(0, m_HorizonOffset, 0)` 固定。**都市座標に対応する雲の塊は存在しない** |
| 雲量 | 毎フレーム `m_MaxCoverage × SampleCloudCoverage()` で上書きされる。**`m_Coverage` を書いても無意味** |
| MOD が書ける（上書きされない）もの | `m_MaxCoverage` / `m_WindForce` / `m_NoiseTiling` / `m_EvolutionSpeed` / `m_HorizonOffset` / `m_CloudLayer`。**雲を濃く・速く流すことはできる** |
| 台風の雲と合成できるか | **できない。** バニラの雲は無限遠のスカイドームなので、④が作る雲は**必ずその下**に入る。深度的にも自然に見える（スカイドームは背景として描かれる）が、「切れ目のある目（eye）」のような相互作用は不可能 |
| 到達方法 | `MonoBehaviour`。`UnityEngine.Object.FindObjectOfType<DayNightDynamicCloudsProperties>()`。**PARTIAL** — DLC/設定によって存在しない可能性を実機で確認すること |

---

## D. 河川の氾濫

### D-1. `FloodBaseAI` とその派生 — **ABSENT（洪水災害は中身が空）**

```
FloodBaseAI : DisasterAI
  フィールド: 無し
  メソッド:   public override Boolean GetHazardSubMode(out SubInfoMode)   // subMode = 0 (FloodHazard)

GenericFloodAI : FloodBaseAI
  フィールド: 無し
  メソッド:   無し          // ★ 宣言メソッドが 1 つも無い

TsunamiAI : FloodBaseAI     // 地震ファクト §B-1 で既出
```
→ **`GenericFloodAI` は `DisasterAI` の既定挙動しか持たない。水を 1 滴も出さない。**
水面を扱う災害はバニラでは `TsunamiAI` 1 つだけ。

海面を上げるコードも災害側には無い。`m_nextSeaLevel` の書き手を全アセンブリで走査した結果:
```
WaterSimulation.Awake                （初期化）
WaterSimulation.Data.Deserialize     （ロード）
SeaHeightOptionPanel.SetHeight       （マップエディタ）
WaterTool.<PrimaryUp>c__Iterator1    （マップエディタ）
```
**ゲームプレイ中に海面を動かすバニラのコードは存在しない。**

### D-2. `WaterSimulation` の全体像 — CONFIRMED

```
WaterSimulation : MonoBehaviour
  public Byte[][]  m_heightMaps       public Vector4[] m_heightBounds
  public Byte[][]  m_surfaceMapsA / B
  public FastList<WaterSource> m_waterSources     // ★ public
  public FastList<WaterWave>   m_waterWaves       // ★ public
  private FastList<WaterWave>  m_tsunamiWaves / m_impactWaves     // 毎ステップ再構築
  public Single m_currentSeaLevel / m_nextSeaLevel
  public Boolean m_resetWater
  public static Single DEFAULT_SEA_LEVEL = 40 ;  MAX_SEA_LEVEL = 500
  private Cell[][] m_waterBuffers ; UInt16[] m_heightBuffer
  private Thread m_simulationThread                              // ★ 専用スレッド

  public Cell[] BeginRead() / void EndRead()
  public bool  WaterExists(int x, int z) / (int,int,int,int)
  public WaterSource LockWaterSource(ushort)                     // ★ ロックを取ったまま返る
  public void  UnlockWaterSource(ushort, WaterSource)            // ★ ロックを返す
  public bool  CreateWaterSource(out ushort, WaterSource)
  public void  ReleaseWaterSource(ushort)
  public bool  CreateWaterWave(out ushort, WaterWave)
  public void  ReleaseWaterWave(ushort)
  public void  SimulationStep(int subStep) / EnableWaterFlow() / UpdatePatch(int,int)
  private void WaterThread() / SimulateWater(int pollutionDisposeRate)
```

`SimulateWater` の定数（IL_002D–0091）:
```
セル 16 m / グリッド 1080（配列は 1081²）/ ブロック 120 / マップ幅 17280 m
海面の raw = m_currentSeaLevel * 64      // 1/64 m 単位
m_currentSeaLevel = m_nextSeaLevel を毎回コピー（volatile）
```

### D-3. 波の寿命・減衰・反射 — CONFIRMED（**地震ファクト §B-3 の逃げ道を訂正する**）

`SimulateWater` の波の前処理（IL_0318–048C、`Monitor.TryEnter(m_waterWaves, 0)` のスピンロック内）:
```
m_tsunamiWaves.Clear(); m_impactWaves.Clear();
foreach wave in m_waterWaves:
    if (wave.m_type == 1 /*TYPE_TSUNAMI*/) {
        m_tsunamiWaves.Add(wave);
        wave.m_currentTime = (ushort)Min(wave.m_currentTime + 64, 65535);
        // ★ 解放判定が無い
    }
    else if (wave.m_type == 2 /*TYPE_IMPACT*/) {
        m_impactWaves.Add(wave);
        wave.m_currentTime = (ushort)Min(wave.m_currentTime + 64, 65535);
        if (wave.m_currentTime > wave.m_duration) ReleaseWaterWave(index + 1);   // IL_046F
    }
```

**(a) `TYPE_TSUNAMI` はマップ外周でしか評価されない — これが今回の最大の発見**

`m_tsunamiWaves` を消費するのは `SimulateWater` の 1 箇所だけ（IL_16BB–16F9）:
```
IL_1690  base = z * (1080 + 1)                                  // z は行インデックス（loc47）
IL_1699  stride = (z == 0 || z == 1080) ? 1 : 1080               // ★ 中間行では x = 0 と x = 1080 だけ
IL_16B6  for (x = 0; x <= 1080; x += stride) {
IL_16BB      level = (int)(m_nextSeaLevel * 64)
IL_16C7      foreach w in m_tsunamiWaves:  level = w.GetSeaLevel(level, x, z)
IL_16FE      … 境界セルの水位を level に合わせる（差分 > 0 なら流入、< 0 なら流出）
         }
```
`stride` の分岐（`z == 0 || z == 1080` のときだけ 1、それ以外は 1080）は、
**走査対象がマップ外周リング（上下 2 行の全セル＋左右 2 列）に限られる**ことを意味する。

→ **`WaterWave` の `m_type = TYPE_TSUNAMI` は、`GetSeaLevel` が外周セルでしか呼ばれないため、
内陸を `m_origX/m_origZ` にしても、`m_minX..m_maxX` の箱が外周を含まなければ、何も起こさない。**
例外も警告もログも出ない。

> **`2026-08-14-earthquake-il-facts.md` §B-3 の表の最終行「逃げ道はある。`TsunamiAI` を通さず
> `WaterSimulation.CreateWaterWave` を直接呼べば、任意の `m_origX/m_origZ/m_dirX/m_dirZ/m_delta` の
> 波を作れる（すべて public）」は、`TYPE_TSUNAMI` については誤りである。**
> API を呼べることと、波が効くことは別だった。`SimulateWater` を読まずに書いた「逃げ道」で、
> 同節が自ら「`SimulateWater` は未読。**PARTIAL**」と注記していた箇所そのもの。

`WaterWave.GetSeaLevel(int original, int x, int z) : Int32`（public、IL_0000–00B6）:
```
if (x < m_minX || z < m_minZ || x > m_maxX || z > m_maxZ) return original;
phase = ((x - m_origX) * m_dirX + (z - m_origZ) * m_dirZ) >> 8      // 進行方向への射影
t     = m_currentTime - phase
if (t <= 0 || t >= m_duration) return original;                     // まだ来ていない／もう過ぎた
amp   = (m_delta * (65536 - m_currentTime)) >> 16                   // ★ m_currentTime で線形減衰
q     = m_duration >> 6
mag   = amp - FixedMath.Cos((t << 10) / q, amp)
return original - (FixedMath.Sin((t * 1536) / q, mag) >> 1);
```
（`FixedMath.Sin(int angle, int multiplier) : Int32` / `Cos` 同型。実測。）

| 事実 | 内容 |
|---|---|
| 減衰 | `amp = m_delta * (65536 - m_currentTime) / 65536`。`m_currentTime` は 1 水ステップで **+64**。1024 ステップで振幅ゼロ |
| 反射 | **無い。** `GetSeaLevel` は外周セルの水位を書き換える境界条件で、反射は浅水シミュ本体が勝手に作る |
| 寿命 | `TYPE_IMPACT` のみ `m_currentTime > m_duration` で自動 `ReleaseWaterWave`。**`TYPE_TSUNAMI` は自動解放されない** |
| 誰が津波波を消すか | `DisasterManager.ReleaseDisaster` → `DisasterAI.ReleaseDisaster`（`m_waveIndex != 0` のとき）、または `TsunamiAI.StartDisaster` の再利用のみ |
| MOD が直接作った津波波 | **誰も消さない。** `WaterSimulation.Data.Serialize` が `m_waterWaves` を保存するので**セーブに焼き付く**。作ったら自分で `ReleaseWaterWave` する |

**(b) `TYPE_IMPACT` は内陸で効く — ただし体積は増えない**

`m_impactWaves` は流れの計算ループの中で、**各内陸セルごと**に評価される（IL_0845–0A49）:
```
foreach w in m_impactWaves:
    if (x < w.m_minX || x > w.m_maxX + 1 || z < w.m_minZ || z > w.m_maxZ + 1) continue;
    r2 = (Max(w.m_maxX - w.m_origX, w.m_origX - w.m_minX) + 1)^2          // IL_08BD–0925
    dx = x - w.m_origX ; dz = z - w.m_origZ
    d2   = dx*dx + dz*dz         ; d2x = (dx+1)^2 + dz*dz  ; d2z = dx*dx + (dz+1)^2
    if (d2  < r2) { a = m_delta - m_delta*d2 /r2 ; accX += a ; accZ += a }
    if (d2x < r2) { a = m_delta - m_delta*d2x/r2 ; accX -= a }
    if (d2z < r2) { a = m_delta - m_delta*d2z/r2 ; accZ -= a }
```
`accX` / `accZ` は直後の流量計算に**水頭差として足される**（IL_0ACD / IL_0BF8）:
```
headX = (terrain[i] + accX) + cell.m_height - terrain[i+1] - cellRight.m_height
headZ = (terrain[i] + accZ) + cell.m_height - terrain[i+row] - cellDown.m_height
```
→ **`TYPE_IMPACT` は「その場に仮想的な水面の盛り上がりがある」ことにして隣接セルへ水を押し出すだけ。
セルの `m_height` を直接増やさない。水は保存される。**
`DisasterHelpers.SplashWater` が地震の断層で使っているのはこれ（`m_duration = 256`、`m_dirX = m_dirZ = 0`）。

| 問い | 答え |
|---|---|
| 内陸の任意点で効くか | **効く** |
| 乾いた谷を水で満たせるか | **できない。** 既にある水を揺らすだけ |
| 波紋の大きさ | `m_minX..m_maxX` の箱の半幅が二次減衰の半径。`SplashWater` は `CeilToInt(radius/16)` セル |
| 自動解放 | **される**（`m_currentTime > m_duration`）。`SplashWater` は `m_duration = 256`、+64/step なので 5 水ステップ |

### D-4. 局所的に水位を上げる正解 — `WaterSource`（CONFIRMED）

```
WaterSource（public struct、Serialize/Deserialize あり）
  public Vector3 m_inputPosition      // 吸い込み口（ワールド座標）
  public Vector3 m_outputPosition     // 吐き出し口（ワールド座標）
  public UInt16  m_type               // TYPE_NONE=0 / TYPE_NATURAL=1 / TYPE_FACILITY=2 / TYPE_CLEANER=3
  public UInt16  m_target             // ★ 目標水位（絶対値、1/64 m 単位。海面 40 m = 2560）
  public UInt32  m_water / m_pollution / m_inputRate / m_outputRate / m_flow
```

`SimulateWater` の水源処理（IL_184A–20C5、`Monitor.TryEnter(m_waterSources, 0)` のスピンロック内）:

```
foreach src in m_waterSources:
    if (src.m_type == 0) continue;
    natural = (src.m_type == 1);
    src.m_flow = 0;
    if (natural) { src.m_water = 0; src.m_pollution = 0; }        // ★ 無限の泉／無限の排水口

    // ---- 吸い込み側 ----
    inRate = src.m_inputRate;
    if (inRate > 0) {
        rIn = Sqrt(inRate) * 0.4f + 10f;
        if (src.m_type == 2 || src.m_type == 3) rIn = Min(50f, rIn);      // ★ 施設は 50 m 上限
        excess = Σ over cells within rIn of
                 Min(terrain[i] + cell.m_height - Max(src.m_target, terrain[i]), cell.m_height)
        inRate = natural ? Min(inRate, excess >> 1) : Min(inRate, excess)
        if (inRate > 0) 各セルから比例配分で m_height を引き、src.m_water / m_pollution に足す
    }

    // ---- 吐き出し側 ----
    outRate = natural ? src.m_outputRate : Min(src.m_outputRate, src.m_water);
    if (outRate > 0) {
        rOut = Sqrt(outRate) * 0.4f + 10f;
        if (src.m_type == 2 || src.m_type == 3) rOut = Clamp(rOut, 10f, 50f);
        // 1 周目: 受け入れ余地を数える
        headroom = 0 ; count = 0
        foreach cell within rOut:
            if (natural && terrain[i] >= src.m_target) continue;                   // IL_1E5E ★
            headroom += Min(terrain[i] + cell.m_height - Max(src.m_target, terrain[i]), cell.m_height)
            count++
        if (natural) outRate = Min(outRate, -(headroom >> 1));                      // IL_1EBF ★
        // 2 周目: 実際に注ぐ
        foreach cell within rOut:
            if (natural && terrain[i] >= src.m_target) continue;
            add = Min((outRate + count/2) / count, 65535 - cell.m_height)
            cell.m_height += add;   src.m_water -= add;   src.m_flow += add;        // ★ 体積が増える
            water-exists ビットを立てる
    }
```

| 事実 | 内容 |
|---|---|
| `m_type = TYPE_NATURAL(1)` の意味 | **自己調整の泉**。水面が `m_target` に届くまで注ぎ、超えたら吸い戻す。`m_water` は毎ステップ 0 に戻るので**枯れない／溢れない** |
| `m_target` の単位 | **絶対水位、1/64 m**（`TerrainManager.RawHeights` と同じスケール）。UInt16 なので 0 〜 1023.98 m |
| 半径 | `Sqrt(rate) * 0.4 + 10` メートル。**TYPE_NATURAL は 50 m 上限が掛からない**。`outputRate = 1,000,000` で半径 410 m |
| 地形が高いセル | `natural && terrain >= m_target` のセルは**スキップされる**。丘の上には水を載せない。**川の谷筋だけが濡れる** |
| 上限 | `m_waterSources.m_size >= 65535` で `CreateWaterSource` が false を返す。**戻り値を必ず見る** |
| スレッド | `m_waterSources` を `Monitor.TryEnter(_, 0)` のスピンロックで守る。水スレッドは**永久にスピンする** |
| **`LockWaterSource` の罠** | `LockWaterSource(ushort)` は **`Monitor.TryEnter` でロックを取ったまま値を返す**（IL_0005–002E に `Monitor.Exit` が無い）。`UnlockWaterSource(ushort, WaterSource)` が唯一の解放経路。**try/finally で必ず対にすること。落とすと水スレッドが固まり、ゲームが無反応になる** |
| 永続化 | `WaterSimulation.Data.Serialize` が `m_waterSources` を書く（IL_0263–029B）。**既存水源の `m_target` を書き換えるとセーブに焼き付く。地形改変（地震ファクト §D-1）と同じ扱いが必要** |

**河川氾濫の実装経路（IL から導かれる 2 択）**

- **(i) 既存の自然水源の `m_target` を持ち上げる。** マップの川は `WaterTool.PlaceSource` が置いた
  `TYPE_NATURAL` 水源で流れている。`m_waterSources`（public）を走査して `m_type == 1` を集め、
  台風の進路に近いものの `m_target` を `+Δ` する → **川全体が物理的に正しく増水し、谷から溢れる。**
  戻すのは `m_target` を元に戻すだけ（吸い込み側が自動で水位を下げる）。
  **元の値を④が保存し、`OnLevelUnloading` と災害終了時に必ず復元すること。**
- **(ii) 一時的な `TYPE_NATURAL` 水源を新規に作る。** `m_outputPosition` を河川上に、
  `m_target` を「その地点の平常水位 + Δ」に、`m_outputRate` で半径を決める。
  終了時は `ReleaseWaterSource`。**ただし注いだ水は残る**ので、
  下げるには「`m_target` を平常値にした水源を吸い込み側として少し残す」か、自然排水を待つ。

どちらも `DisasterHelpers` も `TsunamiAI` も通らない。**NDR とも無衝突。**

---

## E. 移動と寿命

### E-1. 移動する災害の仕組み — CONFIRMED

バニラで「動く」災害は**竜巻だけ**で、動いているのは災害ではなく `VortexAI` の車両である（付録 A-1）。
それ以外（雷雨・地震・津波・隕石・陥没・森林火災）は `m_targetPosition` に固定される。

| 方式 | 対象 | 手段 |
|---|---|---|
| `DisasterData.m_targetPosition` を毎 tick 書く | **雷雨**（および `UpdateHazardMap` を持つ全災害） | **public フィールド。Active 中にバニラが書かない**（全アセンブリ走査で確認）。バニラ自身 `DefaultTool.<EndMoving>c__Iterator2.MoveNext` が同じことをやっている（IL_01AF 付近） |
| `Vehicle.SetTargetPos(0, …)` / `(1, …)` | **竜巻** | スロット 0 は到達時にスロット 1 に置換されるので**両方書く**（付録 A-1）。目標へ一次遅れで向かうので、遠くに置けば直進する |
| `VortexAI.SimulationStep` Postfix で `frameData.m_position` を書く | 竜巻 | ③火災旋風で既に使っている固定手法。任意の経路を与えることもできる |

雷雨を動かした場合に自動で追随するもの（IL 上で `m_targetPosition` を毎回読み直しているもの）:
- 落雷の散布中心（`SimulationStep` IL_01FB）
- ハザードマップの円盤（`UpdateHazardMap` IL_002C。ただし `Located` ゲートの奥）
- 災害マーカーと `GetPosition`（IL_0001）
- `DisasterManager.FindDisaster(Vector3)` / `CanAffectAt`

追随しないもの: `m_activationFrame`（開始時に確定）、`m_angle`（誰も更新しない）。

### E-2. 複数の災害を束ねられるか — PARTIAL

`DisasterAI.CreateDisaster`(base) は
```
IL_0000  grp = new InstanceManager.Group();
IL_0006  grp.m_ownerInstance.Disaster = disasterID;
IL_0012  InstanceManager.SetGroup(grp.m_ownerInstance, grp);
IL_0023  DisasterManager.m_DisasterWrapper?.OnDisasterCreated(disasterID);
```
**1 災害 = 1 グループ**で、`m_ownerInstance` は単一の `InstanceID`。
複数災害を 1 つの親に束ねる構造はバニラに存在しない。
`InstanceManager.CopyGroup` は「車両を災害のグループに入れる」等、**子を親に足す**方向にしか使われない。

位置の同期についても、`DisasterManager.SimulationStepImpl` は
```
idx = SimulationManager.m_currentFrameIndex & 255;   // 1 呼び出しで 1 災害だけ進める
```
（地震ファクト §E-1）なので、**災害 i の `SimulationStep` は 256 sim フレームに 1 回しか回らない**。
台風の位置を滑らかに動かすには、④が `ThreadingExtensionBase.OnAfterSimulationTick` から
**自前のコントローラで毎 tick 全構成要素の座標を書く**しかない。

> **結論: 台風は「バニラ災害の合成体」ではなく「④が持つ 1 個の論理オブジェクト」として設計し、
> 雷雨災害・竜巻車両・水源・自前の雲を毎 tick 追従させる。**
> 既存の③火災旋風が `FireWhirlPinner` でやっているのと同じ構造で、向きが逆（固定ではなく追従）。

### E-3. 位相と `SelfTrigger` の再確認 — CONFIRMED

`DisasterAI.StartNow(ushort, ref DisasterData)`（public）:
```
IL_0000  if ((m_flags & 60) == 0) StartDisaster(...)          // Emerging|Active|Clearing|Finished が全部落ちている
IL_001B  else if (m_flags & 32768) m_flags |= 65536;
```
`DisasterAI.ActivateNow(ushort, ref DisasterData)`（public）:
```
IL_0000  if (m_flags & 4 /*Emerging*/) ActivateDisaster(...)  // ★ Emerging 必須
```
→ **`StartNow` の直後に `ActivateNow` を呼べば Emerging をスキップできる**（環境落雷がこれをやっている）。
ただし `ThunderStormAI.ActivateDisaster` は base ＋ `FollowDisaster` だけなので副作用は小さい。
`SelfTrigger(64)` を立てずに `StartNow` すると `m_activationFrame == 0` のままになり、
`ThunderStormAI.IsStillEmerging` が永久 true になる（地震と同型）。**`ActivateNow` を続けて呼ぶなら回避される。**

MOD から雷雨を起こす正しい手順（`DisasterTool.<CreateDisaster>c__Iterator0` と同じ）:
```csharp
var info = DisasterManager.FindDisasterInfo<ThunderStormAI>();   // null なら ND DLC 無し
if (!DisasterManager.instance.CreateDisaster(out ushort id, info)) return;   // ★ 戻り値を見る（256 上限）
ref var d = ref DisasterManager.instance.m_disasters.m_buffer[id];
info.m_disasterAI.ClampDisasterTarget(ref pos);                  // public
d.m_targetPosition = pos;
d.m_angle          = angle;
d.m_flags         |= DisasterData.Flags.SelfTrigger;             // ★ 必須
d.m_intensity      = (byte)intensity;
info.m_disasterAI.StartNow(id, ref d);                           // public
```

---

## F. 共存

### F-1. Natural Disasters Renewal — PARTIAL（本機に未インストール）

`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods` を走査した結果、NDR の DLL は無い
（あるのは `AICityDirector` / `AlienInvasion` / `CitizenRiot` / `CSWarfront` / `DisasterPlus` /
`GodzillaDisaster` / `MegaCity` / `MissileDisaster` / `NuclearMeltdown` / `SirenAlert`）。
地震ファクト §E-2 のソース読解を前提とする。NDR のパッチ面は
`DisasterHelpers.DestroyBuildings` と `DisasterHelpers.DestroyNetSegments` の 2 つだけで、
災害種別を引数値で嗅ぎ分ける（`probability == 0.02f` → 地震、`burnRadiusMin == 0 && burnRadiusMax == 0` → 竜巻）。

今回の IL と突き合わせた④の衝突点:

| ④がやりうること | NDR の反応 |
|---|---|
| `DisasterHelpers.DestroyStuff(... burnRadiusMin: 0f, burnRadiusMax: 0f)` を呼ぶ | **竜巻と誤認され全置換される。絶対に呼ばない** |
| `DisasterHelpers.DestroyBuildings(... probability: 0.02f)` を呼ぶ | 地震と誤認される。呼ばない |
| **バニラ竜巻（`TornadoAI`）を起こす** | `VortexAI.SimulationStep` が上記の実引数で `DestroyStuff` を呼ぶので、**破壊は NDR の竜巻設定に従う**。③火災旋風で既に受け入れている仕様（設計書 3.3(b)） |
| `BuildingAI.CollapseBuilding` / `BurnBuilding` を直接呼ぶ | **無衝突** |
| `WeatherManager.QueueLightningStrike` | **無衝突**（NDR は `WeatherManager` をパッチしない） |
| `WeatherManager` の天候フィールドを書く | **無衝突** |
| `WaterSimulation.CreateWaterSource` / `LockWaterSource` / `CreateWaterWave` | **無衝突** |
| `DisasterData.m_targetPosition` を書く | **無衝突** |
| ハザードマップ / `InfoManager` / `TerrainManager` / `TerrainModify` | **無衝突** |

**④が守るべき線: `DisasterHelpers` の `DestroyBuildings` / `DestroyNetSegments` / `DestroyStuff` を一切呼ばない。**
`AddWind` / `SplashWater` / `BurnGround` / `MakeCrater` / `MakeCrack` / `UpgradeBuildings` /
`DestroyTrees` / `DestroyProps` は NDR のパッチ対象外なので使ってよい。

### F-2. 建物破壊の直接経路 — CONFIRMED（拒否 AI の一覧が新規）

```
public virtual bool BuildingAI.CollapseBuilding(ushort buildingID, ref Building data,
        InstanceManager.Group group, bool testOnly, bool demolish, int burnAmount)
    IL_0000  ldc.i4.0 ; ret          // 既定は常に false

public virtual bool BuildingAI.BurnBuilding(ushort buildingID, ref Building data,
        InstanceManager.Group group, bool testOnly)
    IL_0000  ldc.i4.0 ; ret          // 既定は常に false
```

`CollapseBuilding` を override する 13 型:
`AirportBuildingAI` / `CableCarPylonAI` / `CampusBuildingAI` / **`CommonBuildingAI`** / `DamPowerHouseAI` /
`DecorationBuildingAI` / `DoomsdayVaultAI` / `MonorailPylonAI` / `PowerPoleAI` / `RaceBuildingAI` /
`ShelterAI` / `TsunamiBuoyAI` / `VarsitySportsArenaAI`。

`BurnBuilding` を override する 3 型: **`CommonBuildingAI`** / `ShelterAI` / `TsunamiBuoyAI`。

**拒否する AI（新規に確定）**

```
ShelterAI / DoomsdayVaultAI / DamPowerHouseAI / TsunamiBuoyAI .CollapseBuilding:
IL_0000  ldarg.s 5 ; brfalse -> IL_0017        // ★ demolish == false なら
IL_0007  base.CollapseBuilding(...)
IL_0017  ldc.i4.0 ; ret                        //    黙って false

DecorationBuildingAI.CollapseBuilding:
IL_0000  if (!demolish) return false;
IL_0007  if (!testOnly) m_flags |= 524288 (0x80000);
IL_0020  return true;

PowerPoleAI / CableCarPylonAI .CollapseBuilding:
IL_0000  if (testOnly) return false;           // ★ 事前判定では常に「壊せない」と答える
IL_0009  if ((m_flags & 4194304 /*Collapsed*/) != 0) return false;
         m_flags |= Collapsed ; m_electricityBuffer = 0 ;
         m_problems |= 0x8000000C00000000 ; UpdateNotifications / UpdateBuildingRenderer / UpdateFlags
         return true;

ShelterAI / TsunamiBuoyAI .BurnBuilding:
IL_0000  ldc.i4.0 ; ret                        // ★ 常に false。防災施設は燃えない

CommonBuildingAI.CollapseBuilding:
IL_0007  if ((m_flags & 4194304 /*Collapsed*/) != 0) return false;   // 既に倒壊済み
IL_0013  if (testOnly) return true;
         m_levelUpProgress = 0 ; m_fireIntensity = 0 ; m_garbageBuffer = 0 ; m_flags |= Collapsed
         demolish なら m_flags |= 524288 と m_problems クリア …
```

| ④が知っておくべきこと | 内容 |
|---|---|
| 風で「倒壊」させたい | `CollapseBuilding(id, ref d, grp, testOnly:false, **demolish:false**, burnAmount:0)`。**この形だと Shelter / DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy は無反応**（意図どおり：防災施設は台風で壊れない） |
| 事前に壊せるか調べたい | `testOnly:true`。ただし **`PowerPoleAI` / `CableCarPylonAI` は testOnly で false を返すのに、実行すると壊れる**。事前判定の結果で送電柱を除外すると、実際には壊せるものを取りこぼす |
| 燃やしたい | `BurnBuilding(id, ref d, grp, testOnly:false)`。火勢は `GetFireParameters` の `out fireSize` が建物ごとに決める（付録 A-3） |
| 送電線を切りたい | `NetAI.CollapseSegment(ushort segmentID, ref NetSegment data, InstanceManager.Group group, bool demolish)` — **public virtual**。落雷が `demolish: false` で使っている |
| 木を燃やしたい | `TreeManager.BurnTree(uint treeIndex, InstanceManager.Group group, int fireIntensity)` — **public**。落雷は 140–255 |
| 市民・車両を吹き飛ばしたい | `DisasterHelpers.AddWind(position, radius, directionalWind, rotationalWind, radialWind, group)` — **public static、NDR 無関係、無害** |

---

## 設計への含意

依頼文:
> 台風を実装してほしい。雷雨をともなって移動し、上空には巨大でゆっくり回転する雲。
> 竜巻のモデルを参考に暴風を再現し、都市にランダムな風害と河川の氾濫をもたらす。

5 つの構成要素に分けて仕分ける。

### 1. 「移動する台風」— **安い。ほぼ全部そろっている**

`DisasterData.m_targetPosition` は public で、雷雨災害に対して**バニラは Active 中に一度も書かない**。
毎 tick 書き戻せば、落雷の散布中心・災害マーカー・`FindDisaster` の判定・ハザード円盤がすべて追随する。
バニラ自身（`DefaultTool.EndMoving`）が同じことをやっているので、想定外の使い方ではない。

**ただし④は自前のコントローラを持つことになる**（§E-2）。災害の `SimulationStep` は
256 sim フレームに 1 回しか回らないので、位置更新は `OnAfterSimulationTick` から行う。
③火災旋風の `FireWhirlPinner` と同じ構造で、固定ではなく経路追従にする。

進路・速度・寿命は④が完全に自由に決められる。バニラに参照すべき制約は無い。

### 2. 「雷雨をともなう」— **安い。むしろバニラより強く作れる**

`WeatherManager.QueueLightningStrike(startFrame, position, rotation, group)` が public で、
**落着点を完全に指定できる**。落雷は本物の被害（建物着火・樹木着火・送電線切断）を出す。

安いが、3 つ数字を守ること:

- **同時 20 発が上限。** 超えた要求は `false` を返して消える。台風の壁雲に沿って撒くなら、
  「1 tick に n 発」ではなく「キューの空きを見て補充する」設計にする。
- **`startFrame` は最短で `currentFrameIndex + 15`。** それより手前を指定しても切り上げられる。
- **`m_currentRain > 0.8` かつキューが空だと、ゲームが勝手に雷雨災害を作る**（§A-3）。
  ④が常に落雷を撒いていればこれは起きない。逆に、雨だけ上げて落雷を撒かない設計にすると
  **災害スロットに知らない雷雨が増え続ける**。どちらかに倒して意識的に選ぶこと。

バニラの雷雨災害（`ThunderStormAI`）をそのまま起こして `m_targetPosition` を動かす手もあり、
その場合は音・通知・チャープ・ハザードマップが無料で付いてくる。**これが第一候補。**
ただし `Located` ゲート（気象レーダー必須）は雷雨のままなので、
「レーダーが無いとハザードマップに出ない」という①で書いた説明が④にもそのまま必要。

### 3. 「巨大でゆっくり回転する雲」— **高い。ゼロから作るしかない**

**既製品は無い。** `DisasterInfo.m_effect` はフィールドごと存在せず（§C-1）、
災害系の `EffectInfo` は爆発 1 つと隕石衝突 1 つだけ。雲のプレハブも渦のプレハブも無い。

**バニラの雲と合成することもできない。** 雲は半径 6400 km のスカイドームに貼られた
ノイズシェーダで、ワールド座標を持たない（§C-2）。雲量は毎フレーム `WeatherManager` から
上書きされる。**「台風の位置に雲の渦がある」という表現はバニラの雲では原理的に不可能。**

したがって④の雲は:
- **④が自分でメッシュを組み**（円環状のスパイラル、目の穴あき）、
- `Shader.Find("Standard")` 系で自作マテリアルを作り（§4.9。CS のマテリアルは借りない）、
- 毎フレーム `Graphics.DrawMesh` で台風の座標に、ゆっくり回して描く。
- スカイドームは無限遠なので、④の雲は**必ずその手前**に出る。深度の破綻は起きない。

参考にできるのは `VortexAI.GenerateMesh()`（§C-1）— 16250 頂点・高さ 2000 m の漏斗を
`Randomizer(2975689)` の固定シードで手続き生成している。**同じ作り方で水平の渦巻きを組む**のが最短。
メッシュを静的にキャッシュするなら `static Mesh[]` にせず**要素で null 判定する**（§4.8）。

**コスト評価: これが④で一番高い。単独のタスクに切り出すこと。**
「雲は出さない／薄い円盤で妥協する」でも他の 4 要素は成立するので、依存を作らないこと。

補助的に安い手も併用できる: `DayNightDynamicCloudsProperties` の
`m_MaxCoverage` / `m_WindForce` / `m_EvolutionSpeed` は上書きされないので、
**台風接近中は空全体の雲を濃く・速く流す**ことはできる。これは 3 行で済む。

### 4. 「都市にランダムな風害」— **中。バニラに風害は存在しない**

**`WeatherManager` の風速で何かが壊れる機構はゼロ**（§A-5）。
風速の読み手は風力発電・木と道路の揺れ・霧と雲のスクロール・ハザードマップの補正だけ。
`DisasterHelpers.AddWind` も市民と車両を押すだけで無傷。
**風速を上げるフィールドすら無い**（`GetWindSpeedFactor = 1 + rain*0.5 - fog*0.5` が唯一の乗数で、上限 1.5）。

→ **④の風害は新規の物理である。**「可視化」枠（①②）ではなく「新現象」枠（③）。スコープを分けること。

作り方は 2 つ:

- **(a) 竜巻を借りる（安い）。** 台風に随伴する竜巻を 1〜数基置き、`VortexAI.SimulationStep` Postfix で
  台風の周囲を旋回させる。破壊はバニラ品質。**ただし NDR がいると破壊が NDR の竜巻設定に従う**（§F-1）。
  ③で既に受け入れている仕様なので一貫している。渦の見た目も無料。
- **(b) 自前の広域風害（中）。** ④が建物バッファを走査し、
  `CollapseBuilding(..., demolish:false, burnAmount:0)` を直接呼ぶ。`DisasterHelpers` を通らないので
  **NDR と完全に無衝突**。距離減衰・建物高さ・`m_fireHazard` など④の好きな確率モデルを載せられる。
  **拒否 AI に注意**（§F-2）: Shelter / DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy は
  `demolish:false` では壊れない（防災施設が台風で壊れないのは正しい挙動）。
  送電柱・ケーブルカー支柱は `testOnly:true` で false を返すのに実際には壊れるので、
  **事前判定でフィルタすると取りこぼす**。
  加えて `AddWind` で市民と車両を吹き飛ばし、`TreeManager.BurnTree` は使わず
  `DisasterHelpers.DestroyTrees`（NDR パッチ対象外）で倒木を出せる。

**推奨: (b) を主、(a) を「台風に伴う竜巻」オプションとして別設定。**
(b) なら「ランダムな風害」という依頼をそのまま実装でき、NDR とも無衝突。

### 5. 「河川の氾濫」— **可能。ただし想定していた経路とは違う**

**まず潰しておく 3 つの不成立ルート:**

1. **洪水災害はバニラに無い。** `GenericFloodAI` はフィールド 0・メソッド 0 の空クラス（§D-1）。
2. **海面上昇は使えない。** ゲームプレイ中に `m_nextSeaLevel` を動かすバニラのコードは無く、
   これは全マップ一律の変更で、河川の局所氾濫にはならない。
3. **`TYPE_TSUNAMI` の波を川に置いても何も起きない**（§D-3(a)）。
   `GetSeaLevel` はマップ外周リングでしか評価されない。
   **地震ファクト §B-3 が「逃げ道」として提示していた経路は成立しない。**
   ここが本調査で一番危なかった箇所で、放置すれば「API は呼べているのに水位が動かない」を
   丸一日デバッグすることになった。

**成立するルート（§D-4）:**

`WaterSimulation.CreateWaterSource` / `LockWaterSource` / `UnlockWaterSource` の
`WaterSource` を使う。`m_type = TYPE_NATURAL(1)` は**目標水位 `m_target` まで注ぎ、
超えたら吸い戻す自己調整の泉**で、しかも `natural && terrain >= m_target` のセルは
スキップされるので**谷筋しか濡れない**。これは河川氾濫そのものである。

- **推奨(i): 既存の自然水源の `m_target` を持ち上げる。** マップの川はこの型の水源で流れている。
  `m_waterSources`（public FastList）を走査し、台風の進路近傍の `m_type == 1` を集めて
  `m_target += Δ` する。川全体が正しく増水し、低地に溢れ、`m_target` を戻せば自然に引く。
  **④が元の値を保存し、災害終了時と `OnLevelUnloading` で必ず復元すること。**
- 代案(ii): 一時的な `TYPE_NATURAL` 水源を新設し、終了時に `ReleaseWaterSource`。
  この場合、注いだ水は自動では引かないので、引き潮フェーズを④が明示的に作る。
- 演出として `TYPE_IMPACT` の波（`DisasterHelpers.SplashWater`）を重ねると水面が荒れる。
  自動解放されるので後始末が要らない。**ただし体積は増えないので、これ単独では氾濫にならない。**

**必ず守る 3 点:**
- **`LockWaterSource` はロックを取ったまま返る。** `UnlockWaterSource` を `finally` で必ず呼ぶ。
  落とすと水シミュ専用スレッドがスピンロックで固まり、ゲームが無反応になる（§D-4）。
- **水源も波もセーブに焼き付く**（`WaterSimulation.Data.Serialize`）。
  地形改変（地震ファクト §D-1）と同じ「恒久変更」扱いにし、④が消えても壊れないようにする。
- **`CreateWaterSource` / `CreateWaterWave` は上限 65535 で false を返す。戻り値を見る。**

### 6. 実装順序の提案

| 段 | 内容 | コスト | 依存 |
|---|---|---|---|
| 1 | 台風の論理オブジェクトと経路（`m_targetPosition` 追従コントローラ） | 低 | — |
| 2 | 天候駆動（`m_targetRain/Cloud/Fog` ＋ `m_forceWeatherOn` を毎 tick） | 低 | 1 |
| 3 | 落雷（`QueueLightningStrike`、20 発上限のキュー管理） | 低 | 1 |
| 4 | 風害（自前の `CollapseBuilding` ＋ `AddWind`） | 中 | 1 |
| 5 | 河川氾濫（`WaterSource.m_target` の一時変更と復元） | 中 | 1 |
| 6 | 巨大な回転雲（自前メッシュ＋自前マテリアル） | **高** | 1。他の段に依存させない |

### 着手前にもう 1 つ実機で確かめるべきもの

- `ThunderStormAI` の `m_radius` / `m_emergingDuration` / `m_activeDuration`、
  `VortexAI` の `m_destructionRadiusMin` / `m_destructionRadiusMax` / `m_maxSpeed`、
  `TornadoAI.m_targetRain`。**すべてプレハブ値で DLL に無い（PARTIAL）。**
  `DisasterManager.FindDisasterInfo<ThunderStormAI>()` から読んで Phase 0.5 の `DiagnosticDump` に出すのが最短。
  落雷本数（`c`）も破壊半径も、この 5 値の上に乗る。**持続時間の設計を始める前に実測すること。**
- `DayNightDynamicCloudsProperties` が実機に存在するか（`FindObjectOfType`）。DLC・グラフィック設定で
  無い可能性がある。**PARTIAL。** 無くても④の自前の雲には影響しない（バニラ雲の増強だけが効かなくなる）。
- 対象マップに `TYPE_NATURAL` の水源が実際に何個あるか、`m_target` がどんな値か。
  §D-4(i) はこれに依存する。診断ダンプに `m_waterSources` の `m_type == 1` を列挙する 5 行を足せば分かる。
- NDR のバイナリ（本機に未インストール）。④が `DisasterHelpers` を通らない限り実害は無い。

### 再現手順

`docs/tools/ilload.ps1` + `ildasm.ps1` をドット・ソースし、
`ColossalManaged.dll` を `Assembly.LoadFrom` で追加ロードする。
`ildasm.ps1` の `ShortInlineI` / `ShortInlineBrTarget` の符号付き変換修正（地震ファクト §再現手順）は
**既に `docs/tools/ildasm.ps1` 本体に取り込まれている**（今回は修正不要だった）。
日本語パスを避けるため ASCII の一時ディレクトリに複製して作業した。`docs/tools/` は未変更。
