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
| 随伴する竜巻 | **~~別設定として (a) も出す。既定 OFF~~ 撤去した（§4.6）** | 持ち主の指示「竜巻を発生させずに竜巻の被害だけを複数発生させて」。副産物として NDR との衝突面も消えた |
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

**★★ 発生地点はプレイヤーが指す（2026-08-20 変更）。**

災害パネルの④タイルは、バニラの災害ボタンと同じように**配置カーソルを構える**。
地図をクリックした地点が台風の**出発点**である（`TyphoonPlacementTool` →
`TyphoonRequestData` が座標を運ぶ → `TyphoonController.Start(…, origin)`）。

以前は進入点を種から引き、必ずマップの外から入ってきた（`TyphoonTrack.EntryOf`）。
**その関数と、それが要求していた定数（`EntryDistance` / `LateralMax` /
`OutsideRadius` / `SaltLateral`）は削除した。** 種が決めるのは
**進行方位と曲率だけ**である。

- 出発点はプレイヤー、進行方位・曲率・速度は④が決める。実在の台風のように
  **上陸後に減衰**させる（`TerrainManager.HasWater` で中心が海上か陸上かを見る。
  ②で確認済み・sim スレッド専用）
- 経路は決定論にする（`DeterministicRandom`、種は災害 ID）。
  **同じセーブで同じ地点を指せば同じ経路になること**
- 想定経路長 `NominalPathLength` は**マップの一辺そのもの**（`MapHalfExtent * 2` ＝ 17280 m）。
  「持続時間いっぱいでマップを 1 回横切る」という尺度で、発明した数ではない。
  進入距離を足した 24000 m は、進入点が無くなったので意味を失った
- 台風はマップの中から始まるので、`_wasInsideMap` は最初の tick で立つ。
  したがって**終了条件「1 度中に入ってから外へ出た」は必ず成立する**
  （Core のテスト `TheTrackLeavesTheMapWithinItsLifetime` が固定している）
- パネルに現在位置・進行方向・中心気圧相当（＝強度）を出す

**押した瞬間には壊れない。** 遅れは 2 つあり、どちらも既存のものである:

1. バニラの `m_emergingDuration`。IL 事実文書 §A-1 の Emerging 分岐は
   `QueueLightningStrike` を**1 度も呼ばない** —— 落雷は Active 分岐にしか無い。
   一方で雨と雲は活性化の **1755 フレーム手前**から先に立ち上がる
   （`currentFrame + 1755 >= m_activationFrame` の条件）。
   つまり**空が荒れてから落ちてくる**。これはバニラの災害そのものの挙動である
2. ④自身の強度包絡線 —— `IntensityAt` は elapsed 0 で **0** を返し、
   持続時間の 25% をかけて最盛期まで上がる。落雷の本数（§A-1 の `c`）も
   ④の風害も強度に比例するので、**最初は何も壊さない**

**人工的な遅延は足していない。** どちらも以前からある挙動で、変えたのは出発点だけである。

### 4.2 雷雨

`ImpactManager`（または該当マネージャ）の `QueueLightningStrike(uint, Vector3, Quaternion, InstanceManager.Group)` は public で、
落雷は**実体**として `BurnBuilding` / `BurnTree` / `CollapseSegment` を起こす（§A-3）。

- **飛行中の上限は 20 発。** キューが埋まると以降の要求は捨てられる。
  ④が自分で発数を管理し、**上限に当てないこと**（当てるとバニラの雷雨と競合して
  バニラ側の落雷が消える）
- 雨量が 0.8 を超えるとゲームが自前の雷雨災害を発生させる。**④が雨を強めると
  勝手に雷雨が増える**ので、④の落雷と二重にならないよう発数を抑える
- 落雷位置は台風の中心付近に偏らせる（実際の台風の眼の外側の壁雲）

**追記（全体レビュー I4、実装後に判明した境界）。** 宿主の嵐に譲る枠は
`1 + c/10`（`c` はランプ値で、頂点では強度そのもの）なので、**強度 170 以上では
④の取り分が 0 になり、④は落雷を 1 発も積まなくなる**。壁雲への偏り＝この節の
目的そのものが消えて、宿主の嵐の一様な円盤だけになる。予備枠は緩めない
（緩めると宿主と他 MOD の落雷が捨てられる）ので、**その状態をパネルと診断で
名指しする**（`Strings.TyphoonLightningYielded`）。導出は
`Core/Typhoon/LightningBudget.IntensityWithNoShareAtPeak` にある。

**追記（全体レビュー I2、セーブの扱い）。** ④が書く `m_targetRain` /
`m_targetCloud` / `m_targetFog` / `m_forceWeatherOn` / `m_targetDirection` は
**全部セーブに焼き付く**（`WeatherManager+Data.Serialize` を IL で実測）。
台風の最中に保存すると、開き直したとき④が走っていないのに最大の雨が
期待値 2 万 step 続く。**保存の直前に降ろし、`SimulationManager.AddAction` で
戻す**（河川氾濫とまったく同じ形）。

**追記（持ち主の指摘「台風が去ったら暴風雨や竜巻被害がなくなるように」）。**
終了の経路は 1 本に絞ってある: どんな終わり方でも
`TyphoonController.Forget` を必ず通る（`Stop` ＝ 寿命切れ／マップ外、
`LoseSlot` ＝ 災害スロットを奪われた、`Reset` ＝ アンロード）。
そこが天候・落雷・風害・河川・**竜巻並みの局所被害**を全部返し、
**何を返したかを `Log.Info` の 1 行に残す**。
設定で④そのものを切った tick も同じものを返す（`TyphoonFeature.OnSimulationTick`）。
確かめ方は診断ダンプの `typhoon: idle` の下の 5 行で、
**正常ならすべて `released` / `stopped` / `0` になる**（実機チェックリスト §7.10）。

いっぽう**宿主の `ThunderStormAI` 災害そのものはセーブから外さないと決めた。**
外す手は保存の前に `DeactivateNow` する以外に無く、それは「保存という操作が
シミュレーションの状態を変える」（セーブしただけで台風が消える）ことを意味する。
セーブに残るのはバニラが自分で作れる正当な災害で、バニラのライフサイクルが
`m_activeDuration` どおり終わらせて `ReleaseDisaster` まで行う。天候だけを
降ろすのは、あちらが**災害に属さないグローバルな上書き**で、持ち主が消えても
誰も戻さないからである。**ロード直後は「動かない雷雨がその場に残る」**
——これは仕様であり、実機チェックリストにも書いてある。

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

#### ★ 追加（持ち主の指摘「進行方向〔右〕側に被害半径や被害の確率を若干強化」）

実在の台風は左右対称ではない。渦の回転と台風自身の移動が足し算になる側を
**危険半円**と呼び、北半球では進行方向の**右**である
（指示は当初「左」だったが**あとから右へ訂正された**。左は南半球の話）。

`Core/Typhoon/TrackBias` が方位とオフセットから左右を出し、風害が

- **被害半径**を最大 **+18 %**: 風速の場を引き伸ばす（`WindAt(距離 ÷ RadiusFactor, …)`）。
  `TyphoonProfile` の半径の定数は 1 つも書き換えない
- **倒壊確率**を最大 **+30 %**: `CollapseChance` の**外**で掛ける。
  `WindDamageModel` は台風の向きを知らないままにする

**左側と正面・真後ろは倍率がちょうど 1**（弱めない）。指示は「右側を若干強化」である。

**走査の矩形も同じ倍率だけ広げる。** 広げないと伸びた側の外縁が走査に入らず、
半径を伸ばした意味が消える（例外の出ない壊れ方）。
**リングの順序（眼から外へ）は 1 ビットも変えない。**

偏りは**毎走査 `TyphoonController.HeadingRadians` を読み直す**ので、経路が曲がれば
その場で回る。**方位をキャッシュしないこと** —— それが「曲がる経路に付いてこない
偏り」の作り方である。

南半球（左が危険半円）は `typhoonSouthernHemisphere` で切り替わる（既定 OFF ＝ 北半球）。
向きは設定画面・パネル・診断ダンプの 3 箇所が名乗る ——
**左右が逆でもプレイヤーには気付けない**ため。

**建物ごとの当たり判定は②の `VanillaRandomizer` を使わない。**
これはバニラが引く値ではないので、`DeterministicRandom`（本 MOD 自身の生成器）を使う。
両者の使い分けは両ファイルの doc に書いてある。

### 4.6 竜巻並みの局所被害 —— 竜巻を出さずに（随伴竜巻の後継）

> 竜巻を発生させずに竜巻の被害だけを複数発生させてください

**随伴竜巻（バニラの `TornadoAI` 災害を借りて台風の周りを回らせる機能）は撤去した。**
代わりに `Game/Typhoon/TyphoonGust` が「台風の下のあちこちで、短いあいだ、
狭い範囲だけが竜巻並みに壊れる」という現象だけを起こす。
**災害の実体も渦の車両も漏斗のメッシュも 1 つも作らない。**

- 置き方と寿命は `Core/Typhoon/GustPatchPlan`、壊れ方は `Core/Typhoon/GustDamageModel`
  （どちらもテストつき）
- パッチは 128 台風フレームごとに 1 個生まれ、320 フレームで消える
  ＝ **同時に最大 4 個**。半径 45〜95 m。中心の倒壊確率は最大 70 %
  （台風全体の風害の上限 8 % に対しておよそ 9 倍）
- **危険半円へ寄る**（§4.3 の追加と同じ向き）。相対角で置くので、経路が曲がれば
  散らばりも一緒に回る
- 破壊は `BuildingAI.CollapseBuilding(demolish: false, burnAmount: 0)` を直接呼ぶ。
  `Building.m_fireIntensity` には 1 バイトも書かない
- **副産物: NDR との衝突面が消えた。** 旧実装の破壊は `DisasterHelpers.DestroyStuff` を
  通るので NDR に丸ごと置き換えられていた。パッチは④の風害と同じ経路なので**無衝突**である

**1 tick あたりの上限**: 走査は 16 台風フレームに 1 回、パッチ 4 個、
1 個あたりグリッド 25 セル、全パッチ合計 256 棟、`AddWind` / `DestroyTrees` /
`DispatchEffect` は 1 回につきパッチ 1 個あたり 1 度ずつ。

**設定の退役**: `typhoonTornado` / `typhoonTornadoCount` は**宣言だけ残して読まない**
（`.cgs` は公開契約。`ForecastButtonX` と同じ扱い）。
新設は `typhoonGust`（既定 ON）と `typhoonGustStrength`（既定 3、0 で完全無効）で、
**旧キーを別の意味で再利用していない**。撤去したことは
`Strings.TyphoonTornadoRetiredNote` が設定画面で 1 度名乗る。

`VortexAI` のプレハブ値（`m_destructionRadiusMin` / `Max`、`VehicleInfo.m_maxSpeed`）は
**読むのをやめた**。使う場所が 1 つも無くなったので、診断に並べ続けると
次の担当者が「これは効いている」と読む。`Assumptions` の竜巻 2 件も同じ理由で消し、
代わりに `BuildingAI.CollapseBuilding` の 1 件を置いた（＝実際に門にしている式）。

---

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

#### ★ 改訂（持ち主の指摘「現在の巨大な渦を雲から構成するように」）

**渦の本経路は「バニラの雲・煙の粒子エフェクトを借りて撒く」形になった**
（`Game/Typhoon/TyphoonCloudFx`、置き場所は `Core/Typhoon/VortexPuffLayout`）。
以下の自前メッシュの記述は**退避経路**として今も有効だが、**既定では使われない**。

理由は 2 つある。

1. 自前メッシュには自前マテリアルが要り、それにはシェーダが要る。
   **実機の `Shader.Find` は `"Standard"` を含めて全ての名前に null を返した。**
   `ShaderPool`（読み込み済みマテリアルからシェーダだけ借りる）は回避策だが
   **まだ 1 度も実機で通っていない**。
2. 通っても、下の「★」段落のとおり出来上がるのは**半径およそ 900 m の平らな
   渦巻き 1 枚＝渦の記号**であって、空を覆う雲ではない。

バニラの粒子エフェクトは**既に読み込まれ、既に動くマテリアルを持っている**。
しかもそのマテリアルは `ParticleSystemRenderer` に付くので、
③が確定させた「CS のマテリアルを借りると自前 `MeshRenderer` で不可視になる」問題には
**当たらない**（エフェクト実測文書 §D-3。バニラ自身が
`EffectsWrapper.CreateParticleEffect` で同じことをしている）。

- 素材は `Factory Smoke` → `Factory Steam` → `Large Pool Steam` → `Pool Steam` →
  `Collapse Particles` → `Factory Smoke Small` → `BuildingProperties.m_collapseEffect`
  の順に試し、**最初に取れたものを使う**。`Factory Smoke` は `EffectCollection` に
  登録されていないので `EffectsWrapper.GetBuiltinEffect` でしか取れない（§A-3）
- **必ずクローンしてから色・粒径・寿命・可視距離を変える。** 共有プレハブを直接
  書き換えると**街じゅうの工場の煙**が嵐雲色になり、メモリ上に残る（§D-5）
- クローンした `GameObject` はアクティブなシーンに入るので、`InitializeEffect()` の
  **前に** `emission.enabled = false` を書く。書かないとワールド原点で煙を吐く
- 撒き方は `RenderEffect(..., timeOffset: -1f, timeDelta: m_simulationTimeDelta, ...)`
  ＝**継続モード**（§B-3。`SinkholeAI.RenderInstance` と同じ形）
- **眼は穴のまま。** `VortexPuffLayout` は `EyeFraction`（0.16）より内側に 1 個も置かない
- **毎フレームの上限は 3 本で決まる**: `RenderEffect` は `PuffCount`（30）回ちょうど、
  新しく湧く粒子は 620 個／秒（`MagnitudeFor` が §B-4 の式を逆に解く）、
  生きている粒子はクローンの `maxParticles`（7000）で頭打ち（バニラ自身が
  `pps ×= (1 - fill²)` で絞る）。**ヒープ確保は 0 バイト**
- **1 つも借りられなければログ 1 行を出してメッシュへ退避する。** 例外は投げない。
  `Assumptions` の検証 1 件が同じ `Lookup` を呼んで名指しする

以下、退避経路（自前メッシュ）の設計:

- ④が円環状のスパイラルメッシュを手続き生成する（中心に眼の穴）。
  手本は `VortexAI.GenerateMesh()`（16250 頂点・高さ 2000 m の漏斗を
  `Randomizer(2975689)` の固定シードで生成）
- **CS のマテリアルを借りない。** 借りると自前 `MeshRenderer` では不可視になる
  （③で確認済み）。`Shader.Find("Standard")` 等で自作する
- 毎フレーム `Graphics.DrawMesh` で台風の座標に、ゆっくり回して描く
- **静的にキャッシュするなら `static Mesh[]` にせず要素で null 判定する。**
  Unity の fake-null は配列自体には効かないので、都市再読込で無言で不可視になる（③で確認済み）
- スカイドームは無限遠なので④の雲は必ずその手前に出る。深度の破綻は起きない
- **影は落とさないし受けない。** `Graphics.DrawMesh` の 4 引数版は
  `castShadows: true` を転送するので、900 m 上空の半透明な渦が影のパスに入って
  都市に渦巻きの影を落としうる（全体レビュー）。9 引数版で明示的に切る
- **UV は使う。** `SpiralMesh` はリボンに沿った UV を出しているので、
  `_MainTex` に「縁で 0・中央で 255」のアルファ（`Core/Typhoon/CloudBandAlpha`）を
  貼ってリボンの縁を落とす。**中央は 255 のまま**なので、雲の濃さは今までどおり
  マテリアルのティントの α だけで決まる（縁を柔らかくするだけで、濃くも薄くもしない）

**★ これは「渦がある」ことを示す記号であって、空を覆う雲ではない（全体レビューの記録）。**
実物の台風の雲は数十 km に広がるが、④が描くのは**半径およそ 900 m の平らな
渦巻き 1 枚**である。眼の上にそれらしい渦を置く記号として設計してあり、
画面写真では「思ったより小さい」と見える。**この枝では拡大しない**
（拡大は頂点数・透過の重なり・スカイドームとの見え方に別の判断が要る）。
本文がここまで「巨大な回転雲」と呼び続けているのは名前としての話で、
**実際の見え方はこの段落が正である。**

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
    CloudBandAlpha.cs                雲のリボンの縁を落とすアルファ（同上）
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
| ~~`VortexAI` の `m_destructionRadiusMin` / `m_destructionRadiusMax` / `m_maxSpeed`~~ | **撤去。** 随伴竜巻の土台だったが、竜巻並みの被害は④が自前で出すようになったので読む場所が無い（§4.6） |
| `BuildingAI.CollapseBuilding(ushort, ref Building, Group, bool, bool, ushort)` が引ける | 風害も竜巻並みの局所被害も 1 棟も壊せない。**これが両方の唯一の破壊経路である**（NDR のパッチ面を通らない理由でもある） |
| バニラの雲・煙の `ParticleEffect` が名前で引けるか | 渦を雲の粒で組めない（自前メッシュへ退避する。§4.5） |
| `QueueLightningStrike` のシグネチャ | 雷雨が出せない |
| `WaterSimulation.m_waterSources` に `TYPE_NATURAL` が何個あるか、`m_target` の値 | **マップ依存。0 個なら氾濫は起きない**（不具合ではない） |
| `DayNightDynamicCloudsProperties` が存在するか | バニラ雲の増強だけが効かない。④の自前の雲には影響しない |
| `Shader.Find` で透過シェーダが 1 つでも解決するか（**どれが勝ったかを診断に出す**） | 雲が不可視になる。`Standard` は組み込みでほぼ常に解決するので、「勝ったシェーダ名」を出さないとこの検証は原理的に FAIL しない（全体レビュー） |
| ④の落雷の取り分（`allowance`）が 0 のまま続いていないか | 強度 170 以上では宿主の嵐が枠を全部使い、④の壁雲散布が消える（仕様。パネルと診断が名乗る） |

**★ 前提検証は「読めた」ではなく「使える」を見ること（全体レビュー C1）。**
プレハブ値は**実行時にしか取れない**ので、`Resolved`（＝オブジェクトが見つかり
フィールドを読み終えた）で PASS を出すと、値が 0 の環境で「機能は何もしないのに
検証は全部 PASS」になる。判定には、その機能が実際に使っているゲート
（`TyphoonPrefabFacts.Usable` など）と同じ式を使う。

**プレハブ実数値は実機で 1 回取る。**②と同じ扱いで `DiagnosticDump` に行を足す。

---

## 7. 出してよい断定の範囲

1. **④の数値は原則すべて本 MOD のものである。**見出しで一度言い、行ごとの印は付けない。
   例外は `WeatherManager` から読んだ雨量・雲量で、これだけ①と同じ `[実測]` を付ける。
   **その見出しの文に印の文字列を直接書かないこと**（全体レビュー I5）。ja.txt は
   「`[measured]` が付いた行だけ」と案内していたのに、日本語の印は `[実測]` だった
   ——日本語のプレイヤーは画面に一度も出ない文字列を探すことになっていた。
   翻訳文には `{measured}` トークンを置き、表示の直前に `Strings.SourceVanilla` へ
   差し替える（`TyphoonRows.SetModelNote` が唯一の差し替え箇所）。
   `build.ps1` の locale 検査がこの契約を機械的に見る。
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
7. ~~随伴竜巻（既定 OFF）~~ → **竜巻並みの局所被害**（竜巻を出さない。§4.6）— 中

---

## 付録: 着手前に IL / 実機で確定させること

| 対象 | なぜ要るか | どの段 |
|---|---|---|
| `ThunderStormAI` のプレハブ実数値 3 つ | 持続時間・落雷本数・半径の土台。**DLL に無い** | 1, 3 |
| `DayNightDynamicCloudsProperties` の実在 | 無ければバニラ雲の増強を諦める | 6 |
| 対象マップの `TYPE_NATURAL` 水源の個数と `m_target` | 氾濫が成立するかがこれで決まる | 5 |
| NDR のバイナリ | ④が `DisasterHelpers` を通らない限り実害無し | — |

**「たぶんこうだろう」で書かない。** 本プロジェクトはこれまでに 9 個の前提の取り違えを出しており、
そのうち 8 個は実装を積んでから発覚した。9 個目（`TYPE_TSUNAMI` の内陸波）だけが
実装前に潰せたもので、それはこの事実文書のおかげである。
