# Disaster + — ④台風 設計書

- 日付: 2026-08-15
- 対象: Cities: Skylines 1（Unity 5.6 / .NET 3.5 / C# 7.3）
- MOD: `DisasterPlus`（表示名「Disaster +」）
- 状態: 承認済み
- 前提: Phase 0 ＋ ③火災旋風 ＋ Phase 0.5 診断基盤 ＋ ①天気予報タブ ＋ ②地震 が master にマージ済み
  （Core 297 テスト、ビルド警告 0、ロケール 156/156、**実機テスト未実施**）
- **事実の典拠: `2026-08-15-typhoon-il-facts.md`。IL 由来の主張は全てそこを参照する。再導出しない。**

---

## 1. 依頼と、調査で判明したこと

### 1.1 元の依頼

> 台風の実装：雷雨を伴い、上空に巨大なゆっくり回転する雲を伴いながら移動する台風を実装してほしいです。
> 竜巻のモデルを参考に暴風の再現も行い、都市中に風によるランダムな破壊・川の増水をもたらしてください。

### 1.2 ①②と違い、④は「見せる」機能ではない

①はバニラが既に計算していたハザードマップを見せ、②はバニラが既に持っていた決定論的な被害モデルを見せた。
**④にはその原資が無い。**

| 依頼の要素 | バニラの実態 | 判定 |
|---|---|---|
| 移動する災害 | `m_targetPosition` は public で、Active 中バニラは書かない | **ほぼ完成品がある** |
| 雷雨 | `QueueLightningStrike` が public。落雷は**実体**で、着火・倒木・道路破壊まで実害を出す | **バニラより強く作れる** |
| 風による破壊 | **存在しない（ABSENT）。** 風速の読み手は風力発電・木と道路の揺れ・霧のスクロールだけ。**風速を上げるフィールドすら無い** | **新規の物理** |
| 巨大な回転雲 | **存在しない（ABSENT）。** `DisasterInfo.m_effect` はフィールドごと無く、雲は半径 6400 km のスカイドームに貼られたシェーダで**ワールド座標を持たない** | **ゼロから作る** |
| 洪水 | **`GenericFloodAI` はフィールド 0・メソッド 0 の空クラス。バニラに洪水災害は無い** | **新規** |

**したがって④は全体が「新規現象」枠**であり、③火災旋風と同じ扱いになる。
①②が確立した `[実測]` / `[Disaster + の推定]` の二層表示は④には当てはまらない——
**④が出す数値は原則すべて本 MOD のものである。**その旨をパネルの見出しで一度だけ言い、
行ごとの印は付けない（全部同じ出所なら印は情報を持たない）。

例外は「台風が今どこにいるか」「風速がいくつか」のような④自身の状態で、これは
④にとっての実測値である。**バニラ由来の値と混同させないため、`WeatherManager` から
読んだ値（雨量・雲量）だけは①と同じ `[実測]` を付ける。**

### 1.3 調査が潰した 3 つの不成立ルート

**これを設計に書き残すのは、どれも「API は呼べているのに何も起きない」という
最も高くつく失敗の形をしているからである。**

1. **`TYPE_TSUNAMI` の波を川や内陸に置いても何も起きない。**
   `WaterWave.GetSeaLevel` は `stride = (z==0 || z==1080) ? 1 : 1080` で
   **マップ外周リングでしか評価されない**（§D-3(a)）。
   **②の事実文書 §B-3 が「逃げ道」として提示していた経路は成立しない。**
   本プロジェクト 9 個目の前提の取り違えであり、**唯一、実装前に潰せたもの**である。
2. **海面上昇は使えない。** ゲームプレイ中に `m_nextSeaLevel` を動かすバニラのコードは無く、
   全マップ一律の変更なので河川の局所氾濫にならない。
3. **風速を上げて風害を起こすことはできない。** 書けるフィールドが無く、
   唯一の乗数 `GetWindSpeedFactor = 1 + rain*0.5 - fog*0.5` は上限 1.5 倍で、
   しかもその読み手に破壊は 1 つも無い（§A-5）。

---

## 2. 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| 風害の作り方 | **自前の広域走査（(b)）を主。** `BuildingAI.CollapseBuilding(demolish:false, burnAmount:0)` を直接呼ぶ | `DisasterHelpers` を通らないので **NDR と完全に無衝突**（§F-1）。距離減衰・建物高さ・確率モデルを④が持てる。②の長周期被害と同じ経路・同じ規律 |
| 随伴する竜巻 | **別設定として (a) も出す。既定 OFF** | 見た目が無料でバニラ品質。ただし NDR がいると破壊が NDR の竜巻設定に従う（③で既に受け入れている仕様） |
| 河川氾濫 | **既存の自然水源の `m_target` を持ち上げる（(i)）** | マップの川はこの型の水源で流れている。`natural && terrain >= m_target` のセルはスキップされるので**谷筋しか濡れない**＝河川氾濫そのもの。`m_target` を戻せば自然に引く（§D-4） |
| 雲 | **④が自前でメッシュとマテリアルを作り、毎フレーム `Graphics.DrawMesh`** | 既製品が無く、バニラ雲はワールド座標を持たないので合成もできない（§C-1、§C-2）。`VortexAI.GenerateMesh()` の手続き生成が手本 |
| バニラ雲の増強 | **併用する。3 行で済む** | `DayNightDynamicCloudsProperties` の `m_MaxCoverage` / `m_WindForce` / `m_EvolutionSpeed` は上書きされない。**存在しない環境がありうる（PARTIAL）ので、無ければ黙って諦める** |
| 天候の駆動 | `m_targetRain` / `m_targetCloud` / `m_targetFog` を毎 tick 書く | current は target へ補間されるので、target を握れば天候を駆動できる（§A-4） |
| 強度 | `DisasterData.m_intensity`（Byte）に載せる | ①で 255 解放済み。クランプはコードに無い |

---

## 3. 台風の構造

④は**単一の災害ではなく、④が管理する 1 つの論理オブジェクト**である。

```
TyphoonState（④の所有、sim スレッド）
  中心位置 / 進行方向 / 進行速度
  強度（m_intensity 由来）
  半径（暴風域 / 強風域）
  位相（接近 → 最盛 → 通過 → 消滅）
  経過フレーム
```

バニラの災害スロットを 1 つ取り（`ThunderStormAI` を土台にする）、
その `m_targetPosition` を④が毎 tick 書き換えて移動させる（§E-1）。

**`m_targetPosition` は Active 中バニラが書かない**ことが確認済みで、
`DefaultTool.EndMoving` も同じことをしている。**ただし `ThunderStormAI` の
`m_radius` などはプレハブ値で DLL に無い（PARTIAL）。実行時に読むこと。**

### 3.1 必ず守る 5 点（②で確立した規律の継続）

1. **`m_flags |= SelfTrigger (64)`。** 立てないと `StartDisaster` が即 return し、
   `Significant` も付かず、周囲の建物が `DetectDisaster` を呼ばないので
   **ハザードマップにも通知にも出ない**（②の I4 で③が実際にこれを踏んでいた）。
2. **`CreateDisaster` / `CreateWaterSource` / `CreateWaterWave` の戻り値を必ず見る。**
   災害は上限 256、水源と波は 65535 で false を返す。無視すると他人のスロットを壊す。
3. **`LockWaterSource` はロックを取ったまま返る。** `UnlockWaterSource` を `finally` で必ず呼ぶ。
   落とすと水シミュ専用スレッドがスピンロックで固まり、**ゲームが無反応になる**（§D-4）。
4. **`Building.m_fireIntensity` を直接書かない。** `BurnBuilding` / `CollapseBuilding` だけ。
5. **水源も波もセーブに焼き付く**（`WaterSimulation.Data.Serialize`）。
   **④が元の `m_target` を保存し、災害終了時と `OnLevelUnloading` で必ず復元する。**
   ④をアンインストールしても川が溢れたままにならないこと。

---

## 4. 各要素

### 4.1 移動と経路

- 進入方向・速度・経路の曲がりは④が決める。実在の台風のように**上陸後に減衰**させる
  （`TerrainManager.HasWater` で中心が海上か陸上かを見る。②で確認済み・sim スレッド専用）
- 経路は決定論にする（`DeterministicRandom`、種は災害 ID）。同じセーブで再現できること
- パネルに現在位置・進行方向・中心気圧相当（＝強度）を出す

### 4.2 雷雨

`ImpactManager`（または該当マネージャ）の `QueueLightningStrike(uint, Vector3, Quaternion, InstanceManager.Group)` は public で、
落雷は**実体**として `BurnBuilding` / `BurnTree` / `CollapseSegment` を起こす（§A-3）。

- **飛行中の上限は 20 発。** キューが埋まると以降の要求は捨てられる。
  ④が自分で発数を管理し、**上限に当てないこと**（当てるとバニラの雷雨と競合して
  バニラ側の落雷が消える）
- 雨量が 0.8 を超えるとゲームが自前の雷雨災害を発生させる。**④が雨を強めると
  勝手に雷雨が増える**ので、④の落雷と二重にならないよう発数を抑える
- 落雷位置は台風の中心付近に偏らせる（実際の台風の眼の外側の壁雲）

### 4.3 風害（新規の物理）

**既定 ON。**③火災旋風と同じく、④を有効にした時点でプレイヤーは新現象を求めている。
ただし**強度は設定可能**にする。

走査は②の `LongPeriodDamage` と同じ形（建物グリッド、セル 64、オフセット 135、`[0,269]` クランプ、
**震央ではなく台風中心から外向き**、1 パスあたりの上限つき、sim スレッド専用）。

```
中心からの距離 d、暴風域半径 R
風速相当 = 中心付近で最大、眼の中では弱まる（実際の台風の構造）
倒壊確率 = 風速相当 × 建物ごとの係数
```

- `CollapseBuilding(id, ref d, group, testOnly:false, demolish:false, burnAmount:0)` を直接呼ぶ
- **拒否 AI に注意**（§F-2）: Shelter / DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy は
  `demolish:false` では壊れない。**防災施設が台風で壊れないのは正しい挙動なので、そのままにする**
- **送電柱・ケーブルカー支柱は `testOnly:true` で false を返すのに実際には壊れる。**
  事前判定でフィルタすると取りこぼす（②で同じ判断をしている）
- `DisasterHelpers.AddWind` で市民と車両を吹き飛ばす（無害だが演出として効く）
- 倒木は `DisasterHelpers.DestroyTrees`（NDR のパッチ対象外）

**建物ごとの当たり判定は②の `VanillaRandomizer` を使わない。**
これはバニラが引く値ではないので、`DeterministicRandom`（本 MOD 自身の生成器）を使う。
両者の使い分けは両ファイルの doc に書いてある。

### 4.4 河川氾濫（新規）

```
1. WaterSimulation.m_waterSources（public FastList）を走査
2. m_type == TYPE_NATURAL(1) かつ台風の進路近傍のものを集める
3. 元の m_target を④が保存する
4. m_target += Δ（Δ は台風の強度と降雨量から）
5. 台風の通過後、Δ を戻す。水は自然に引く
```

- **`LockWaterSource` / `UnlockWaterSource` を `finally` で対にする**（§3.1-3）
- 対象マップに `TYPE_NATURAL` の水源が何個あるか、`m_target` がどんな値かは
  **マップ依存で未知**。診断ダンプに列挙を出し、**0 個なら何もしない**（内陸の池だけのマップなど）
- 演出として `DisasterHelpers.SplashWater`（`TYPE_IMPACT` の波、自動解放）を重ねてよいが、
  **体積は増えないのでこれ単独では氾濫にならない**

### 4.5 巨大な回転雲

**④で最も高い要素。単独のタスクに切り出し、他の要素から依存されないこと。**
雲が出せなくても残り 4 要素は成立する。

- ④が円環状のスパイラルメッシュを手続き生成する（中心に眼の穴）。
  手本は `VortexAI.GenerateMesh()`（16250 頂点・高さ 2000 m の漏斗を
  `Randomizer(2975689)` の固定シードで生成）
- **CS のマテリアルを借りない。** 借りると自前 `MeshRenderer` では不可視になる
  （③で確認済み）。`Shader.Find("Standard")` 等で自作する
- 毎フレーム `Graphics.DrawMesh` で台風の座標に、ゆっくり回して描く
- **静的にキャッシュするなら `static Mesh[]` にせず要素で null 判定する。**
  Unity の fake-null は配列自体には効かないので、都市再読込で無言で不可視になる（③で確認済み）
- スカイドームは無限遠なので④の雲は必ずその手前に出る。深度の破綻は起きない

補助として `DayNightDynamicCloudsProperties` の `m_MaxCoverage` / `m_WindForce` /
`m_EvolutionSpeed` を上げ、空全体の雲を濃く速く流す。**存在しない環境がありうる。
無ければ黙って諦め、診断に 1 行出す。**

---

## 5. 構成

```
src/DisasterPlus/
  Core/Typhoon/                    エンジン非依存・全部ユニットテスト対象
    TyphoonTrack.cs                  経路（決定論、上陸で減衰）
    TyphoonProfile.cs                中心距離 → 風速相当（眼の構造を含む）
    WindDamageModel.cs               風速相当 + 建物係数 → 倒壊確率
    LightningBudget.cs               20 発上限のキュー配分
    FloodTarget.cs                   強度と降雨 → 水位上昇量
    SpiralMesh.cs                    雲メッシュの頂点生成（UnityEngine を使わない純データ）
  Game/Typhoon/
    TyphoonFeature.cs                IDisasterFeature 実装
    TyphoonController.cs             災害スロットの取得・移動・位相（sim）
    TyphoonWeather.cs                m_target* の駆動（sim）
    TyphoonLightning.cs              QueueLightningStrike（sim）
    TyphoonWind.cs                   広域風害の走査（sim）
    TyphoonFlood.cs                  WaterSource の m_target 退避と復元（sim）
    TyphoonCloud.cs                  メッシュ・マテリアル・DrawMesh（メイン）
    TyphoonHub.cs                    snapshot-then-render
    TyphoonPanel.cs                  表示（②の EarthquakeRows を流用）
  Game/UI/
    TyphoonPanelButton.cs            FreeSlotFinder（①で整備済み）
```

`Core/` の禁則は据え置き: `UnityEngine` 不可、CS API 不可、`System.Random` 不可、LINQ 不可。
`SpiralMesh` は**頂点座標を `Vec3` の配列として返すだけ**にし、`Mesh` の組み立ては `Game/` 側で行う。

---

## 6. 診断

| 検証項目 | 失敗時の影響 |
|---|---|
| `ThunderStormAI` の `m_radius` / `m_emergingDuration` / `m_activeDuration` が読めるか | **プレハブ値で DLL に無い。** 落雷本数も破壊半径もこの上に乗る。**持続時間の設計を始める前に実測する** |
| `VortexAI` の `m_destructionRadiusMin` / `m_destructionRadiusMax` / `m_maxSpeed` | 随伴竜巻の設計の土台 |
| `QueueLightningStrike` のシグネチャ | 雷雨が出せない |
| `WaterSimulation.m_waterSources` に `TYPE_NATURAL` が何個あるか、`m_target` の値 | **マップ依存。0 個なら氾濫は起きない**（不具合ではない） |
| `DayNightDynamicCloudsProperties` が存在するか | バニラ雲の増強だけが効かない。④の自前の雲には影響しない |
| `Shader.Find("Standard")` が非 null か | 雲が不可視になる |

**プレハブ実数値は実機で 1 回取る。**②と同じ扱いで `DiagnosticDump` に行を足す。

---

## 7. 出してよい断定の範囲

1. **④の数値は原則すべて本 MOD のものである。**見出しで一度言い、行ごとの印は付けない。
   例外は `WeatherManager` から読んだ雨量・雲量で、これだけ①と同じ `[実測]` を付ける。
2. **「あと何分で上陸」は出してよい。** ④が経路を決定論的に持っているので確定値である。
   ①の乱数由来の発生判定とは根拠が違う。**その違いをパネルに書く。**
3. **風速を「m/s」で出さない。** ④の風速相当はゲームの倒壊確率係数であって、
   実在の風速ではない。②が気象庁震度階級を名乗らなかったのと同じ理由。
4. **氾濫が起きなかったとき、その理由を出す。** 対象マップに自然水源が無ければ
   正常に何も起きない。①の「なぜハザードマップが空か」と同じ扱いにする。

---

## 8. 実装順

事実文書 §6 の順序に従う。**雲（6）を他から依存させない。**

1. 台風の論理オブジェクトと経路（`m_targetPosition` 追従コントローラ）— 低
2. 天候駆動（`m_target*` を毎 tick）— 低
3. 落雷（20 発上限のキュー管理）— 低
4. 風害（自前の `CollapseBuilding` ＋ `AddWind`）— 中
5. 河川氾濫（`WaterSource.m_target` の一時変更と復元）— 中
6. 巨大な回転雲（自前メッシュ＋自前マテリアル）— **高。単独タスク**
7. 随伴竜巻（既定 OFF）— 低

---

## 付録: 着手前に IL / 実機で確定させること

| 対象 | なぜ要るか | どの段 |
|---|---|---|
| `ThunderStormAI` / `VortexAI` のプレハブ実数値 6 つ | 持続時間・落雷本数・破壊半径の土台。**DLL に無い** | 1, 3, 7 |
| `DayNightDynamicCloudsProperties` の実在 | 無ければバニラ雲の増強を諦める | 6 |
| 対象マップの `TYPE_NATURAL` 水源の個数と `m_target` | 氾濫が成立するかがこれで決まる | 5 |
| NDR のバイナリ | ④が `DisasterHelpers` を通らない限り実害無し | — |

**「たぶんこうだろう」で書かない。** 本プロジェクトはこれまでに 9 個の前提の取り違えを出しており、
そのうち 8 個は実装を積んでから発覚した。9 個目（`TYPE_TSUNAMI` の内陸波）だけが
実装前に潰せたもので、それはこの事実文書のおかげである。
