# ②地震 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** バニラが既に持っている決定論的な地震の強度モデル（震央距離のランプと、建物ごとに固定された乱数しきい値）を**そのまま可視化**し、その上に長周期地震動・時間帯・海中震源からの津波連鎖という**本 MOD が発明した物理**を、出所を混ぜずに足す。

**Architecture:** 二層構造。**第 1 層（タスク 1〜8）** はバニラが実際に計算している量だけを出す — `Core/Earthquake/` にバニラの `Randomizer` をビット単位で再現した LCG と震度・しきい値・波形の純関数、`Game/Earthquake/` に sim スレッドの読み取りと main スレッドのパネル。**第 2 層（タスク 9〜11）** は津波連鎖・長周期地震動・時間帯係数で、既定 OFF、パネル上で視覚的に分離し、数値の出所を明示する。読み取りは sim スレッド、UI は main スレッドで、①と同じ snapshot-then-render（単一の `lock`）で渡す。ヒートマップは自前で描かず①の `InfoModeSwitch` を流用する。被害は `DisasterHelpers` を経由せず `BuildingAI.CollapseBuilding` / `BurnBuilding` を直接呼ぶ。

**Tech Stack:** C# 7.3 / .NET Framework 3.5、Unity 5.6、ColossalFramework UI、xunit / net8.0（Core テスト）、MSBuild。

**Spec:** `docs/superpowers/specs/2026-08-15-earthquake-design.md`
**事実の典拠:** `docs/superpowers/specs/2026-08-14-earthquake-il-facts.md`（本計画の IL 由来の主張は全てここを参照する。**再導出しない**）

---

## Global Constraints

- **.NET Framework 3.5 / C# 7.3**、Unity 5.6。C# 8+ 構文（switch 式、`??=`、範囲演算子、`using` 宣言、null 許容参照型）は**コンパイルエラー**になる。Unity 5.6 以降に追加された API は存在しない
- `Core/` はエンジン非依存。`UnityEngine` 不可、Cities API 不可、`System.Random` 不可、**LINQ 不可**。`System.Collections.Generic` と `System.Math`（＋`System.Text.StringBuilder`）まで。net35 の MOD と net8.0 の xunit テストプロジェクトの**両方**にコンパイルされる
- net35 に `System.Collections.Concurrent` は無い。共有状態は単一の素の `lock`
- **スレッド境界**: `DisasterManager` / `BuildingManager` / `TerrainManager` / `SimulationManager` のフィールドと全ゲームバッファは **sim スレッド**が所有する。`UIComponent` / `InfoManager.SetCurrentMode` / `CameraController` は **main スレッド**が所有する。境界を破るとスタックトレースの無い `IndexOutOfRangeException` が後からバニラのコードの中で出て、この MOD の try/catch では捕まえられない。確立した型は snapshot-then-render（単一の素の `lock`）
- 名前空間は `Game/` が `DisasterPlus.Game`（サブフォルダに関わらず）、`Core/` が `DisasterPlus.Core.*`。**この MOD の `LogChannel` は常に完全修飾する** — `Assembly-CSharp` がグローバル名前空間に同名の enum を宣言している
- `Strings` / `Locales/en.txt` / `Locales/ja.txt` のキー集合は常に一致させる（**現在 50**）。追加したら `tools/GenerateLocaleTemplate.ps1` で `en.txt` を再生成する。`Locales/*.txt` は UTF-8 **BOM なし**。**`.ps1` は ASCII のみにする** — PowerShell 5.1 は BOM 無し UTF-8 を ANSI として読むためパースが壊れる
- 保存設定は公開契約。既存のキー番号もビット値も詰め直さない
- 全セッション状態はレベルアンロードでリセットする。2 つ目の都市がきれいにロードできること
- `Log.Warn` / `Log.Error` はスロットルされない。**毎フレーム・毎 sim tick の経路に置かない。** 1 回だけ出して以後は `Log.Diag` のキー単位スロットルへ落とす（`HazardMapReader._sampleErrorLogged` / `WeatherReader._readErrorLogged` が確立した形）。`Log.Diag` のキーは短い静的文字列
- ビルド: `powershell -ExecutionPolicy Bypass -File build.ps1`、**警告 0**。テスト: `dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj`（**現在 144 件緑**）
- **PowerShell の `Get-Content -Raw` → `Set-Content` でファイルを編集しない**（日本語が化ける）。Write/Edit ツールを使う
- コミットは `<type>: <説明>` 形式（feat/fix/docs/test/chore/refactor）。説明は日本語で、既存の履歴に合わせる
- ブランチは `feature/earthquake`（`4b5b866` から作成済み）

---

## 事実の典拠（推測で上書きしないこと）

**IL は再導出しない。** 下の節番号は全て `2026-08-14-earthquake-il-facts.md` のもの。実装中に「本当にそうか」と思ったら、まずそちらを読み直す。それでも足りないときだけ `docs/tools/ilload.ps1` を使い、**判明した事実を報告に書く**。

| 事実 | 典拠 | 使うタスク |
|---|---|---|
| `Randomizer` は MMIX 定数の LCG。`Int32(UInt32)` は戻り値を作った**後**に種を進める | 設計書 付録 A-1 | 1, 2, 5 |
| `DestroyBuildings` の種は `buildingID \| (disasterID << 16)`。フレーム非依存 | §A-3 | 2, 5 |
| 全体円盤は `R = 2000 + 20i`、`probability = 0.02`、`fD = 1 - d/R` | §A-3 | 2, 4, 5 |
| 断層 4 円盤は毎ステップ位置が振り直され、`probability = 1`、幅 `w = W(1-4t²)` | §A-3 | 4, 5 |
| 位相は Emerging→Active→Clearing→Finished。`m_activationFrame == 0` なら Emerging に永久停止 | §A-1 | 2, 3 |
| `SelfTrigger (64)` が無いと `StartDisaster` が即 return する（地震・津波の両方） | §A-1, §B-2 | 3, 9 |
| 警報リードタイム = `Min(cov,100) * 6437 / 100 + 1755` フレーム（整数除算） | §A-2 | 7 |
| ハザードマップのゲートは `Located && (Emerging\|Active)`。`GetHazardSubMode` は 5 | §A-6 | 4 |
| `Located` を立てるのは震央の `EarthquakeCoverage != 0`＝地震計だけ | §A-2, §C-2 | 4, 7 |
| 揺れの式 `(sin(0.63t)+sin(0.17t)) * 0.3/(1+dist*0.001) * (0.5-0.5cos(0.02454369t))`、`m_intensity` は入っていない | §A-7 | 6, 8 |
| `CameraController.m_cameraShake` / `DisasterManager.m_disableCameraShake` はどちらも public | §A-7 | 6 |
| `EarthquakeSensorAI` は時系列データを一切持たない（ABSENT） | §C-1 | 7, 8 |
| `TsunamiAI.FindSea` はマップ外周セルしか候補にしない。`m_targetPosition` と `m_angle` は上書きされる | §B-3 | 9 |
| `CreateDisaster` は失敗時に `disasterIndex = 0` を返す（例外なし） | §E-1 | 9 |
| `DisasterHelpers` を経由しなければ NDR のパッチ面 2 つを完全に迂回できる | §E-2 | 10 |
| sim スレッドの時刻は `m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR`。日夜 OFF だと永久に 12.0 | §F-1 | 3, 11 |
| `m_emergingDuration` / `m_activeDuration` / `m_crackLength` / `m_crackWidth` の実数値は **DLL に無い**（プレハブ値） | §A-0 | 3 |
| `DisasterAI.StartDisaster` は protected。公開ラッパーは `StartNow` / `DeactivateNow` | 火災旋風設計書 付録 A-1 | 9 |
| `DAYTIME_FRAMES = 65536`、1 ゲーム内分 = 65536/1440 ≒ 45.51 フレーム。`frameIndex % N` で周期を組まない | 火災旋風設計書 付録 A-4 | 8, 10 |
| `BuildingAI.BurnBuilding` は `testOnly:true` が副作用の無い問い合わせになる | 火災旋風設計書 付録 A-3 | 10 |

---

## 早い順に効く 4 つの罠

**この 4 つはどれも「破っても何も起きない」形で壊れる。** 該当タスクの中で必ず潰すこと。

| 罠 | 症状 | 潰すタスク |
|---|---|---|
| `SelfTrigger (64)` を立てずに `StartNow` する | 災害が Emerging のまま永久に固まり、例外は出ない（§A-1） | **9**（書く側）／**3**（`m_activationFrame == 0` を「未定」として読む側） |
| `CreateDisaster` の戻り値を見ない | false のとき `disasterIndex = 0` になり、**他人の災害スロットを書き潰す**（§E-1） | **9** |
| sim スレッドから `m_currentDayTimeHour` を読む | メインスレッドが描画補間側から書くフィールド。しかも日夜 OFF だと時刻は永久に 12.0（§F-1） | **3**（読み方）／**11**（12.0 固定を隠さない） |
| ハザードマップが空なのを「安全」と読ませる | 地震は `Located && (Emerging\|Active)` のときしか塗られず、`Located` を立てるのは地震計だけ（§A-2, §A-6） | **4**（①の `ForecastNoStormDetected` と同じ扱い） |

---

## 既存コードで前提にしてよいもの

**実装者は自分のタスクしか見ない。** 下は全て実在する型とシグネチャで、確認済み。

### Core

- `DisasterPlus.Core.Common`: `Vec2`（`readonly float X, Z` / `DistanceSquaredTo(Vec2)` / `+` / `*`）、`Vec3`（`readonly float X, Y, Z` / `ToVec2()`）
- `DisasterPlus.Core.Common.DeterministicRandom`: `static uint Hash(uint a, uint b)` / `static float Unit(uint a, uint b)` — MurmurHash3 finalizer 由来の**ハッシュ**。状態を持たない
- `DisasterPlus.Core.Common.RayGeometry.IntersectTerrain(Vec3 origin, Vec3 dir, IHeightSampler s, float maxDistance, out Vec3 hit)`
- `DisasterPlus.Core.Diagnostics`: `DiagnosticLine(int indent, string label, string value)`、`AssumptionResult(string name, bool passed, string impact)`、`FeatureHealth`、`LogChannel`（`General=1` / `FireWhirl=2` / `Forecast=4` / **`Earthquake=8`（定義済み・未使用）** / `Typhoon=16` / `Volcano=32` / `Diagnostics=64` / `DefaultMask=General` / `IsEnabled(int,int)`）
- `DisasterPlus.Core.Forecast.HazardLevel`: `const int Steps = 10` / `const char FilledChar = '#'` / `const char EmptyChar = '-'` / `StepOf(byte)` / `BarOf(byte)` — **バーは ASCII 固定**。CS の UI フォントに罫線素片がある保証は無い

### Game

- `Log`: `Info(string)` / `Warn(string)` / `Error(string, Exception)` / `Diag(string key, string msg)` / `Diag(int channel, string key, string msg)` / `DiagEnabled(int channel)` / `Reset()`。`Diag` は同一キーで 512 sim フレームに 1 回
- `ModSettings`: `Ensure()`、`const string FileName = "DisasterPlusSettings"`、既存の `SavedBool` / `SavedInt` 群
- `ModCompat`: `NdrPresent` / `NaturalDisastersOwned`（どちらもキャッシュ済み static プロパティ）
- `Strings` / `LocaleLoader.Apply()` / `LocaleLoader.ModDirectoryPath()`
- `IDisasterFeature`: `string Name { get; }` / `OnLevelLoaded()` / `OnSimulationTick(uint frameIndex, float deltaMinutes)` / `OnMainThreadUpdate()` / `OnLevelUnloading()` / `WriteDiagnostics(DiagnosticBuilder b)`
- `IPausedTickFeature`: メンバー無しの印。**「ゲームの状態を進めない読んで表示するだけの機能」にしか付けてはいけない**
- `FeatureHost`: `Register(IDisasterFeature)` / `Features` / `float FramesPerMinute`（= `SimulationManager.DAYTIME_FRAMES / 1440f`）/ `NoteDegraded(string feature, string noteKey, string reason)` / `ClearDegraded(string feature, string noteKey)`
- `DiagnosticBuilder`: `Line(int indent, string label, string value)` / `Line(int indent, string label)`
- `Assumptions`: `Run()` / `LastResults` / `Reset()` / `private static void Check(string name, string impact, Func<bool> predicate)` / `private const int TotalCheckCount`（**現在 12**）/ `private static bool HasField(Type, string)`
- `FreeSlotFinder`: `const string SelfPrefix = "DisasterPlus"` / `static Vector2 Find(Vector2 preferred, Vector2 size, float stepY, int maxTries, UIComponent owner, out bool foundFree)`。**owner は「今まさに配置しようとしている当人」だけ**。他の DisasterPlus 製コンポーネントは除外してはいけない
- `InfoModeSwitch`: `bool IsShowingHazard { get; }` / `bool IsShowingHazardFor(InfoManager.SubInfoMode)` / `ShowHazard(InfoManager.SubInfoMode)` / `Clear()` — **main スレッド専用**。②はこれを一切変更せずそのまま使う
- `HazardMapReader.SampleAt(Vector3 worldPos, InfoManager.SubInfoMode subMode, out bool ok)` — **main スレッド専用**（書き手も読み手も main）。要求した subMode が表示中でなければ `ok = false`
- `ForecastHub`: `Publish(WeatherSnapshot)` / `Latest` / `Clear()` — ②の Hub はこれと同形にする
- `TerrainHeightSampler.Instance`（`IHeightSampler`）
- `SceneObjects.FindInScene<T>() where T : Component`
- `FireWhirlDamage.CollectNearby` — 建物グリッド走査の手本（セル 64、オフセット 135、`[0,269]` クランプ、`m_nextGridBuilding` 連結、`guard > 32768` の保険）
- `FireWhirlSpawner.TrySpawn` — `CreateDisaster` → バッファ書き込み → `StartNow` の手本（**ただし SelfTrigger を立てていない。②の津波では必ず立てる**）

---

## 第 1 層と第 2 層の境界（全タスク共通の規則）

**この 2 層を混ぜたら、この機能は存在価値を失う。**

| | 第 1 層 | 第 2 層 |
|---|---|---|
| 定義 | **バニラ自身の式と定数だけ**から導いた量。新しい物理を一切足していない | 本 MOD が発明した物理 |
| 例 | `s = 1 - d/R`、建物ごとのしきい値、警報リードタイム、揺れの波形 | 長周期地震動、時間帯係数、津波連鎖 |
| 既定 | ON | **OFF** |
| 表示 | `Strings.SourceVanilla`（`[measured]`）を行頭に付ける | `Strings.SourceModel`（`[Disaster + model]`）を行頭に付ける |
| 配置 | `Strings.EarthquakeLayer1Header` の下 | `Strings.EarthquakeLayer2Header` の下（常に第 1 層より下） |
| 色 | `EarthquakePanel.Layer1Color`（白） | `EarthquakePanel.Layer2Color`（青みがかった色） |

**色だけに頼らない。** 接頭辞・セクション見出し・色の 3 つを同時に使う。色覚や UI テーマの違いで区別が消える可能性を、この機能の中核的な誠実さの担保に賭けない。

**機械的に確認できる形にする**（タスク 4 で導入し、以後のタスクは守るだけ）:

- `EarthquakePanel` で `UILabel` を作ってよいのは `AddLayer1Row` / `AddLayer2Row` / `AddSectionHeader` / `AddPlainRow` の 4 つのヘルパーの中だけ
- `AddLayer1Row` は `Strings.SourceVanilla` を、`AddLayer2Row` は `Strings.SourceModel` を、それぞれ**必ず**行頭に付ける。呼び出し側が選ぶ余地を作らない
- レビュー時の確認: `EarthquakePanel.cs` で `AddUIComponent(typeof(UILabel))` が現れるのは 1 箇所だけ（4 ヘルパーが共有する `AddLabel`）。`SourceVanilla` / `SourceModel` が現れるのは対応するヘルパーの中だけ

**第 1 層の中の「確率的な帯」の扱い。** 断層 4 円盤の位置は毎ステップ振り直される（§A-3）。したがって「断層帯」は**バニラの式だけから導いた範囲**なので第 1 層だが、確定した予言ではない。この行には常に `Strings.EarthquakeFaultBandNote`（「4 つの破壊円盤は毎ステップ位置が変わるので、この帯は当たりうる範囲であって当たる場所ではありません」）を添える。**第 3 の層は作らない。**

---

## File Structure

### 新規作成

```
src/DisasterPlus/Core/Earthquake/
  VanillaRandomizer.cs      T1   バニラ Randomizer のビット単位再現（LCG）
  SeismicIntensity.cs       T2   距離と強度 → 局所係数 s（= バニラの fD）
  SeismicScale.cs           T2   s → 段階・ASCII バー・バンド名
  CollapseThreshold.cs      T2   種の再構成・しきい値 2 個・倒壊距離
  DisasterPhases.cs         T2   m_flags → 位相・Located・ハザード塗布ゲート
  EarthquakeReading.cs      T3   地震 1 個ぶんの不変な読み取り結果
  FaultBand.cs              T4   断層帯の幾何（中心・向き・長さ・幅・内外判定）
  BuildingMargin.cs         T5   1 建物ぶんの余裕度（不変）と評価
  ShakeWaveform.cs          T6   バニラの揺れ式（時刻と距離 → 変位）
  WarningLeadTime.cs        T7   カバレッジ → 警報リードタイム（フレーム）
  WaveformBuffer.cs         T8   固定長リングバッファ（フレーム印つき）
  WaveformPlot.cs           T8   サンプル列 → 列ごとの高さ（純関数）
  LongPeriodResponse.cs     T10  建物高さ → 固有周期 → 共振応答（第 2 層）
  TimeOfDayFactor.cs        T11  時刻 → 係数（第 2 層）

src/DisasterPlus/Game/Earthquake/
  EarthquakeSnapshot.cs     T3   sim で作り main が読む不変スナップショット
  EarthquakeReader.cs       T3   災害・地震計・時刻の読み取り（sim）
  EarthquakeHub.cs          T3   snapshot-then-render（両方向）
  EarthquakeFeature.cs      T3   IDisasterFeature 実装
  EarthquakePanel.cs        T4   パネル UI（main）
  BuildingProbe.cs          T5   カーソル下の建物の特定と余裕度算出（sim）
  CameraShakeBooster.cs     T6   m_cameraShake への加算（main、毎フレーム）
  SeismographRecorder.cs    T8   観測点ごとのリングバッファ更新（sim）
  WaveformView.cs           T8   波形の描画（main）
  TsunamiChain.cs           T9   海中震源 → 津波の連鎖（sim）
  LongPeriodDamage.cs       T10  第 2 層の被害走査（sim）

src/DisasterPlus/Game/UI/
  EarthquakePanelButton.cs  T4   FreeSlotFinder を使うトグルボタン

tests/DisasterPlus.Core.Tests/Earthquake/
  VanillaRandomizerTests.cs T1    SeismicIntensityTests.cs   T2
  SeismicScaleTests.cs      T2    CollapseThresholdTests.cs  T2
  DisasterPhasesTests.cs    T2    FaultBandTests.cs          T4
  BuildingMarginTests.cs    T5    ShakeWaveformTests.cs      T6
  WarningLeadTimeTests.cs   T7    WaveformBufferTests.cs     T8
  WaveformPlotTests.cs      T8    LongPeriodResponseTests.cs T10
  TimeOfDayFactorTests.cs   T11
```

### 変更

```
src/DisasterPlus/Core/Common/DeterministicRandom.cs   T1（相互参照コメントのみ）
src/DisasterPlus/Game/Diagnostics/Assumptions.cs      T3 T4 T5 T6 T9 T10
src/DisasterPlus/Game/Common/DisasterPlusLoading.cs   T3（機能登録）
src/DisasterPlus/Game/ModSettings.cs                  T3 T6 T9 T10
src/DisasterPlus/Game/Mod.cs                          T3 T6 T9 T10
src/DisasterPlus/Game/Localization/Strings.cs         全タスク
Locales/en.txt, Locales/ja.txt                        全タスク
docs/playtest-checklist.md                            全タスク
```

`src/DisasterPlus/DisasterPlus.csproj` は `Core\**\*.cs` / `Game\**\*.cs` の glob なので**触らない**。テストプロジェクトも `..\..\src\DisasterPlus\Core\**\*.cs` の glob なので触らない。

**小さな付随型は専用ファイルを作らず、使う型と同じファイルに置く**（既存の `Trend` / `ForecastReading` と同じ扱い）:

| 型 | 置く場所 |
|---|---|
| `SeismicBand`（enum） | `Core/Earthquake/SeismicScale.cs` |
| `BuildingThresholds`（struct） | `Core/Earthquake/CollapseThreshold.cs` |
| `EarthquakePhase`（enum） | `Core/Earthquake/DisasterPhases.cs` |
| `CollapseVerdict`（enum） | `Core/Earthquake/BuildingMargin.cs` |
| `EarthquakePrefabFacts`（struct） | `Game/Earthquake/EarthquakeSnapshot.cs` |
| `SeismographTrace`（class） | `Game/Earthquake/SeismographRecorder.cs` |
| `TsunamiChainState`（enum） | `Game/Earthquake/TsunamiChain.cs` |
| `BuildingHeight`（static class） | `Game/Earthquake/LongPeriodDamage.cs` |

### ロケールキーの推移（3 ファイルを常に一致させる）

| タスク | 追加 | 合計 |
|---|---|---|
| 開始時 | — | **50** |
| T3 | 5 | 55 |
| T4 | 29 | 84 |
| T5 | 7 | 91 |
| T6 | 2 | 93 |
| T7 | 5 | 98 |
| T8 | 4 | 102 |
| T9 | 6 | 108 |
| T10 | 6 | 114 |
| T11 | 3 | **117** |

### `Assumptions.TotalCheckCount` の推移

| タスク | 追加 | 合計 |
|---|---|---|
| 開始時 | — | **12** |
| T3 | 4 | 16 |
| T4 | 1 | 17 |
| T5 | 1 | 18 |
| T6 | 1 | 19 |
| T9 | 2 | 21 |
| T10 | 1 | **22** |

### Core テスト件数の推移

| タスク | 追加 | 合計 |
|---|---|---|
| 開始時 | — | **144** |
| T1 | 8 | 152 |
| T2 | 29 | 181 |
| T4 | 6 | 187 |
| T5 | 6 | 193 |
| T6 | 8 | 201 |
| T7 | 6 | 207 |
| T8 | 13 | 220 |
| T10 | 7 | 227 |
| T11 | 7 | **234** |

---

# 第 1 層 — 見せる（タスク 1〜8）

**タスク 8 まで終えた時点で、依頼の過半が満たされ、機能として完結していること。** 第 2 層が難航しても、ここで止めて出荷できる状態を保つ。

---

## Task 1: バニラ `Randomizer` のビット単位再現

**この 1 個が間違っていると、②の中核である「この建物は震央から X m 以内なら倒れる」が全部嘘になる。** 1 ビットずれても気付かない形で壊れる（もっともらしい数字が出続ける）ので、単独のタスクにして単独のレビュー関門を置く。

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/VanillaRandomizer.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/VanillaRandomizerTests.cs`
- Modify: `src/DisasterPlus/Core/Common/DeterministicRandom.cs`（クラス doc に相互参照を足すだけ）

**Interfaces:**
- Consumes: なし
- Produces:
  - `DisasterPlus.Core.Earthquake.VanillaRandomizer` — `struct`
    - `const ulong Multiplier = 6364136223846793005UL`
    - `const ulong Increment = 1442695040888963407UL`
    - `VanillaRandomizer(int value)`
    - `ulong Seed { get; }`
    - `int Int32(uint max)` — 戻り値は `[0, max)`。**種を進める前**の値を返す

### なぜ 2 つの乱数生成器が併存するのか（読者が迷わないように必ず書く）

`Core/Common/DeterministicRandom` は**既に存在する**。これは別物であり、片方を捨ててはいけない。

| | `DeterministicRandom` | `VanillaRandomizer` |
|---|---|---|
| 正体 | MurmurHash3 finalizer 由来の**ハッシュ**。状態を持たない | ゲームの `ColossalFramework.Math.Randomizer` の LCG を**ビット単位で写したもの**。状態（種）を持つ |
| 目的 | **この MOD が自分で決める**ことを、セーブ・ロード・テストで再現可能にする | **バニラが何を引くかを先読みする** |
| 使う場所 | ③の延焼選定、②の第 2 層（長周期の被害選定） | ②の第 1 層（建物ごとのしきい値） |
| 変えてよいか | よい（この MOD の内部仕様） | **絶対に不可**。ゲーム側の実装が唯一の正解 |

**判別の規則（両ファイルの doc コメントに同じ文で書く）:**
> その数字が「この MOD が発明した判断」を決めるなら `DeterministicRandom`。
> 「バニラが引く値と一致しなければならない」なら `VanillaRandomizer`。

- [ ] **Step 1: 失敗するテストを書く**

`tests/DisasterPlus.Core.Tests/Earthquake/VanillaRandomizerTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// これは「バニラと同じ値が出るか」の最終検査ではない（それは実行時に
    /// Assumptions が本物の ColossalFramework.Math.Randomizer と突き合わせる、Task 5）。
    /// ここで固定するのは、実装が LCG の定義から外れないこと、特に
    /// 「戻り値は種を進める前に作る」という順序と、ctor の符号拡張。
    /// </summary>
    public class VanillaRandomizerTests
    {
        [Fact]
        public void Ctor_Zero_LeavesTheIncrementAlone()
        {
            // seed = M * 0 + I
            Assert.Equal(VanillaRandomizer.Increment, new VanillaRandomizer(0).Seed);
        }

        [Fact]
        public void Ctor_One_IsMultiplierPlusIncrement()
        {
            ulong expected = unchecked(VanillaRandomizer.Multiplier + VanillaRandomizer.Increment);
            Assert.Equal(expected, new VanillaRandomizer(1).Seed);
        }

        [Fact]
        public void Ctor_NegativeValue_SignExtendsToSixtyFourBits()
        {
            // IL は conv.i8（符号拡張）。ゼロ拡張で書くと別の種になり、
            // disasterID >= 0x8000 の合成種で全部ずれる。
            ulong signExtended = unchecked(VanillaRandomizer.Multiplier * ulong.MaxValue
                                           + VanillaRandomizer.Increment);
            ulong zeroExtended = unchecked(VanillaRandomizer.Multiplier * (ulong)uint.MaxValue
                                           + VanillaRandomizer.Increment);

            Assert.Equal(signExtended, new VanillaRandomizer(-1).Seed);
            Assert.NotEqual(zeroExtended, new VanillaRandomizer(-1).Seed);
        }

        [Fact]
        public void Int32_ReturnsTheValueBuiltBeforeAdvancingTheSeed()
        {
            // 順序を入れ替えると全ての引きが 1 個ずれる。実装の中で
            // いちばん壊しやすく、いちばん気付きにくい 1 行。
            var r = new VanillaRandomizer(12345);
            ulong before = r.Seed;

            int drawn = r.Int32(10000u);

            int expected = (int)(((before >> 32) * 10000UL) >> 32);
            Assert.Equal(expected, drawn);
            Assert.Equal(unchecked(VanillaRandomizer.Multiplier * before + VanillaRandomizer.Increment),
                         r.Seed);
        }

        [Fact]
        public void Int32_IsAlwaysInsideTheRequestedRange()
        {
            for (int v = -3000; v < 3000; v += 7)
            {
                var r = new VanillaRandomizer(v);
                for (int k = 0; k < 8; k++)
                {
                    int drawn = r.Int32(10000u);
                    Assert.InRange(drawn, 0, 9999);
                }
            }
        }

        [Fact]
        public void Int32_WithMaxOne_IsAlwaysZero()
        {
            for (int v = 0; v < 500; v++)
            {
                var r = new VanillaRandomizer(v);
                Assert.Equal(0, r.Int32(1u));
            }
        }

        [Fact]
        public void SameSeed_ProducesTheSameSequence()
        {
            var a = new VanillaRandomizer(777);
            var b = new VanillaRandomizer(777);
            for (int k = 0; k < 32; k++)
            {
                Assert.Equal(a.Int32(10000u), b.Int32(10000u));
            }
        }

        [Fact]
        public void GoldenSequence_MatchesTheLcgDefinitionStepByStep()
        {
            // 実装の内部構造に依存せず、LCG の定義だけから期待値を組み立てて突き合わせる。
            // 実装を「速くする」書き換えで黙って別物にならないための固定。
            ulong seed = unchecked(VanillaRandomizer.Multiplier * 4242UL + VanillaRandomizer.Increment);
            var r = new VanillaRandomizer(4242);

            for (int k = 0; k < 16; k++)
            {
                int expected = (int)(((seed >> 32) * 255UL) >> 32);
                seed = unchecked(VanillaRandomizer.Multiplier * seed + VanillaRandomizer.Increment);
                Assert.Equal(expected, r.Int32(255u));
            }
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'VanillaRandomizer' could not be found`

- [ ] **Step 3: `VanillaRandomizer` を実装する**

`src/DisasterPlus/Core/Earthquake/VanillaRandomizer.cs`:

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// ゲームの <c>ColossalFramework.Math.Randomizer</c> を**ビット単位で写したもの**
    /// （ColossalManaged.dll。Assembly-CSharp ではない）。設計書 付録 A-1 が唯一の典拠で、
    /// そこに IL がそのまま載っている。**推測で書き換えないこと。**
    ///
    /// なぜ写す必要があるか: DisasterHelpers.DestroyBuildings は建物ごとに
    /// <c>new Randomizer(buildingID | (disasterID &lt;&lt; 16))</c> を作り、そこから
    /// 倒壊しきい値と出火しきい値を 1 個ずつ引く（IL 事実文書 §A-3）。この種は
    /// フレームにもステップにも依存しないので、**同じ種を再構成すれば、バニラが
    /// これから引く値をこちらで先に知ることができる**。②の第 1 層の中核
    /// （「この建物は震央から X m 以内なら倒れる」を予言ではなく事実として言う）は
    /// 全てこの再現の上に乗っている。1 ビットずれると、もっともらしい数字が
    /// 出続けたまま全部が嘘になる。
    ///
    /// **Core/Common/DeterministicRandom とは別物。片方を消さないこと。**
    ///   - その数字が「この MOD が発明した判断」を決めるなら DeterministicRandom。
    ///   - 「バニラが引く値と一致しなければならない」なら VanillaRandomizer。
    ///
    /// 実装上の注意:
    ///   - 乗算は必ず 64 bit で溢れる。<c>unchecked</c> を明示する（C# の既定と同じだが、
    ///     将来 CheckForOverflowUnderflow を有効化しても壊れないため）。
    ///   - 戻り値は**種を進める前**に作られる。順序を入れ替えると全部ずれる。
    ///   - ctor の乗算は long（conv.i8 の符号拡張が効く）。long と ulong の乗算・加算は
    ///     2 の補数で同一ビットになるので、ここでは ulong に寄せている。負の入力での
    ///     一致はテストで固定してある。
    ///   - これは **struct** で、引くたびに自分の種を書き換える。値渡しでコピーすると
    ///     そこから列が分岐する。呼び出し側はローカル変数に置いて使い切ること。
    ///
    /// net35 のビルドと net8.0 のテストビルドで同じ結果になることは、整数演算だけで
    /// 構成されていることから保証される。**ゲーム本体との一致**は実行時に
    /// Assumptions が本物の Randomizer と突き合わせて確認する（Task 5）。
    /// </summary>
    public struct VanillaRandomizer
    {
        /// <summary>Knuth MMIX の乗数。</summary>
        public const ulong Multiplier = 6364136223846793005UL;

        /// <summary>Knuth MMIX の増分。</summary>
        public const ulong Increment = 1442695040888963407UL;

        private ulong _seed;

        /// <summary>IL: <c>seed = 6364136223846793005 * (long)v + 1442695040888963407</c></summary>
        public VanillaRandomizer(int value)
        {
            unchecked
            {
                // (ulong)(long)value で conv.i8 の符号拡張をそのまま再現する。
                _seed = Multiplier * (ulong)(long)value + Increment;
            }
        }

        /// <summary>テストと前提検証のためだけに公開する。通常の利用では読まない。</summary>
        public ulong Seed { get { return _seed; } }

        /// <summary>
        /// IL: <c>戻り値 = (int)(((seed &gt;&gt; 32) * (ulong)max) &gt;&gt; 32)</c> のあとに
        /// <c>seed = M * seed + I</c>。DestroyBuildings が呼ぶのはこの UInt32 版で、
        /// <c>Int32(int)</c> というオーバーロードは**存在しない**。
        /// </summary>
        public int Int32(uint max)
        {
            unchecked
            {
                // ★ 戻り値を先に作る。ここを下に動かすと全ての引きが 1 個ずれる。
                //    (seed >> 32) も max も 32 bit なので、この乗算は溢れない。
                int result = (int)(((_seed >> 32) * (ulong)max) >> 32);
                _seed = Multiplier * _seed + Increment;
                return result;
            }
        }
    }
}
```

- [ ] **Step 4: `DeterministicRandom` に相互参照を足す**

`src/DisasterPlus/Core/Common/DeterministicRandom.cs` のクラス doc を次に差し替える（本体のコードは 1 行も変えない）:

```csharp
    /// <summary>
    /// (tick, id) から再現可能な乱数を作る。
    /// System.Random を使うと、セーブ・ロード、ユニットテストで同じ結果にならない。
    ///
    /// **Core/Earthquake/VanillaRandomizer とは別物。取り違えないこと。**
    /// こちらは状態を持たないハッシュで、**この MOD が自分で決めること**
    /// （③の延焼選定、②第 2 層の被害選定）に使う。中身はこの MOD の内部仕様なので
    /// 変えてよい。あちらはゲームの ColossalFramework.Math.Randomizer を
    /// ビット単位で写したもので、**バニラが引く値を先読みする**ためだけにあり、
    /// 1 ビットも変えてはいけない。
    ///
    /// 判別の規則:
    ///   その数字が「この MOD が発明した判断」を決めるなら DeterministicRandom。
    ///   「バニラが引く値と一致しなければならない」なら VanillaRandomizer。
    /// </summary>
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: **152 件緑**（既存 144 + 新規 8）、ビルド警告 0。

- [ ] **Step 6: `docs/playtest-checklist.md` に②の節を新設し、本タスクぶんを書く**

ファイル末尾に次を追記する（以後のタスクはこの節に項目を足していく）:

```markdown
---

# ②地震 — 実機確認

**Game/ はユニットテスト不可。ここに書く項目は実機でしか確認できない。**

## ⚠ 先に読むこと — 地震も「空が正常」

地震のハザードマップも①の嵐と**バイト単位で同じ 2 段ゲート**（`Located` と
`Emerging|Active`、IL 事実文書 §A-6）を持つ。そして地震で `Located` を立てられるのは
**地震計（Earthquake Sensor）だけ**である（§A-2 / §C-2）。地震計を建てていなければ、
地震が起きていてもハザードマップは真っ白で、それが正常。

1. **`VanillaRandomizer` が本物と一致していること**は起動ログの `ASSUMPTIONS` で確認する
   （Task 5 で追加する検証項目。ここが FAIL なら、建物ごとの余裕度の表示は
   全て信用してはいけない）
```

- [ ] **Step 7: コミット**

```bash
git add src/DisasterPlus/Core/Earthquake tests/DisasterPlus.Core.Tests/Earthquake src/DisasterPlus/Core/Common/DeterministicRandom.cs docs/playtest-checklist.md
git commit -m "feat: バニラ Randomizer のビット単位再現と、2 つの乱数生成器の使い分け"
```

---

## Task 2: 震度・段階・しきい値・位相（Core、純関数）

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/SeismicIntensity.cs`
- Create: `src/DisasterPlus/Core/Earthquake/SeismicScale.cs`
- Create: `src/DisasterPlus/Core/Earthquake/CollapseThreshold.cs`
- Create: `src/DisasterPlus/Core/Earthquake/DisasterPhases.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/SeismicIntensityTests.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/SeismicScaleTests.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/CollapseThresholdTests.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/DisasterPhasesTests.cs`

**Interfaces:**
- Consumes: `VanillaRandomizer`（Task 1）
- Produces:
  - `SeismicIntensity` — `const float BaseRadius = 2000f` / `const float RadiusPerIntensity = 20f` / `const byte VanillaDefaultIntensity = 55` / `static float RadiusOf(byte intensity)` / `static float At(float distance, byte intensity)` / `static bool IsInside(float distance, byte intensity)`
  - `SeismicBand` — `enum { None, Weak, Moderate, Strong, Severe }`
  - `SeismicScale` — `const int Steps = 10` / `const char FilledChar = '#'` / `const char EmptyChar = '-'` / `static int StepOf(float s)` / `static string BarOf(float s)` / `static SeismicBand BandOf(float s)`
  - `BuildingThresholds` — `struct`、`readonly int Collapse` / `readonly int Burn` / ctor `(int collapse, int burn)`
  - `CollapseThreshold` — `const uint Draws = 10000u` / `const float GlobalDiscProbability = 0.02f` / `static int SeedFor(ushort buildingId, ushort disasterId)` / `static BuildingThresholds For(ushort buildingId, ushort disasterId)` / `static bool Hits(int threshold, float localFactor, float probability)` / `static float CollapseDistance(int threshold, byte intensity, float probability)`
  - `EarthquakePhase` — `enum { Unknown, Emerging, Active, Clearing, Finished }`
  - `DisasterPhases` — `const int Created/Deleted/Emerging/Active/Clearing/Finished/SelfTrigger/Significant/Located` / `static EarthquakePhase PhaseOf(int flags)` / `static bool IsAlive(int flags)` / `static bool IsLocated(int flags)` / `static bool PaintsHazardMap(int flags)`

### 設計書からの逸脱を 1 つだけ明示する

設計書 §3.2 は「`Randomizer` は `Core/` に持ち込めないので、種の再構成は `Game/` 側で行う」と書いている。**これは同じ設計書の付録 A-1（後から追記された確定情報）で覆っている** — LCG は `ulong` の算術だけで書けるので Core に持ち込めることが確定した。したがって種の再構成も `Core/Earthquake/CollapseThreshold` に置く（`buildingID | (disasterID << 16)` は単なる int 演算であり、Cities API に一切触れない）。**利点はユニットテストできること**で、これは②の中核の主張を守るために必要。

- [ ] **Step 1: 失敗するテストを書く（4 ファイル、計 29 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/SeismicIntensityTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class SeismicIntensityTests
    {
        [Fact]
        public void RadiusMatchesTheVanillaFormula()
        {
            // IL 事実文書 §A-3: R = 2000 + m_intensity * 20
            Assert.Equal(2000f, SeismicIntensity.RadiusOf(0), 3);
            Assert.Equal(3100f, SeismicIntensity.RadiusOf(55), 3);   // バニラ既定
            Assert.Equal(4000f, SeismicIntensity.RadiusOf(100), 3);  // バニラのランダム発生上限
            Assert.Equal(7100f, SeismicIntensity.RadiusOf(255), 3);  // 本 MOD が解放した上限
        }

        [Fact]
        public void AtEpicentre_IsOne()
        {
            Assert.Equal(1f, SeismicIntensity.At(0f, 55), 4);
        }

        [Fact]
        public void AtTheRadius_IsZero()
        {
            Assert.Equal(0f, SeismicIntensity.At(SeismicIntensity.RadiusOf(55), 55), 4);
        }

        [Fact]
        public void HalfwayOut_IsAHalf()
        {
            // 線形ランプであることを固定する。2 次にしたり滑らかにしたりしない。
            // バニラが倒壊判定に使っているのはこの直線そのもの。
            Assert.Equal(0.5f, SeismicIntensity.At(1550f, 55), 4);
        }

        [Fact]
        public void BeyondTheRadius_IsZeroAndOutside()
        {
            // R の外はバニラが preRadius で先に弾くので、判定自体が起きない。
            // 0 を返すが、呼び出し側はこれを「揺れていない」ではなく「圏外」と表示すること。
            Assert.Equal(0f, SeismicIntensity.At(9999f, 55), 4);
            Assert.False(SeismicIntensity.IsInside(9999f, 55));
            Assert.True(SeismicIntensity.IsInside(0f, 55));
            Assert.False(SeismicIntensity.IsInside(SeismicIntensity.RadiusOf(55), 55));
        }

        [Fact]
        public void IsMonotonicallyDecreasingWithDistance()
        {
            float prev = 2f;
            for (float d = 0f; d < 3200f; d += 10f)
            {
                float s = SeismicIntensity.At(d, 55);
                Assert.True(s <= prev, "s increased at " + d);
                Assert.InRange(s, 0f, 1f);
                prev = s;
            }
        }

        [Fact]
        public void GarbageInput_IsZeroNotNaN()
        {
            // 壊れた読み取りで「強度 NaN」を表示しない。
            Assert.Equal(0f, SeismicIntensity.At(float.NaN, 55), 4);
            Assert.Equal(0f, SeismicIntensity.At(-1f, 55), 4);
        }
    }
}
```

`tests/DisasterPlus.Core.Tests/Earthquake/SeismicScaleTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class SeismicScaleTests
    {
        [Fact]
        public void Zero_IsStepZeroAndBandNone()
        {
            Assert.Equal(0, SeismicScale.StepOf(0f));
            Assert.Equal(SeismicBand.None, SeismicScale.BandOf(0f));
        }

        [Fact]
        public void One_IsTheTopStepAndSevere()
        {
            Assert.Equal(SeismicScale.Steps, SeismicScale.StepOf(1f));
            Assert.Equal(SeismicBand.Severe, SeismicScale.BandOf(1f));
        }

        [Fact]
        public void StepIsMonotonicAndBounded()
        {
            int prev = -1;
            for (int i = 0; i <= 1000; i++)
            {
                int step = SeismicScale.StepOf(i / 1000f);
                Assert.True(step >= prev, "step decreased at " + i);
                Assert.InRange(step, 0, SeismicScale.Steps);
                prev = step;
            }
        }

        [Fact]
        public void OutOfRangeInput_IsClampedNotWrapped()
        {
            Assert.Equal(0, SeismicScale.StepOf(-5f));
            Assert.Equal(SeismicScale.Steps, SeismicScale.StepOf(5f));
            Assert.Equal(0, SeismicScale.StepOf(float.NaN));
            Assert.Equal(SeismicBand.None, SeismicScale.BandOf(float.NaN));
        }

        [Fact]
        public void BarLengthIsAlwaysSteps()
        {
            for (int i = 0; i <= 1000; i += 7)
            {
                Assert.Equal(SeismicScale.Steps, SeismicScale.BarOf(i / 1000f).Length);
            }
        }

        [Fact]
        public void BarFilledCountMatchesStep()
        {
            for (int i = 0; i <= 1000; i += 7)
            {
                float s = i / 1000f;
                string bar = SeismicScale.BarOf(s);
                int filled = 0;
                foreach (char c in bar) if (c == SeismicScale.FilledChar) filled++;
                Assert.Equal(SeismicScale.StepOf(s), filled);
            }
        }

        [Fact]
        public void BarIsAsciiOnly()
        {
            // ①の HazardLevel と同じ判断。CS の UI フォントに罫線素片がある保証は無い。
            for (int i = 0; i <= 1000; i += 13)
            {
                foreach (char c in SeismicScale.BarOf(i / 1000f))
                {
                    Assert.True(c < 128, "non-ASCII character in bar: " + (int)c);
                    Assert.True(c == SeismicScale.FilledChar || c == SeismicScale.EmptyChar,
                        "unexpected char: " + c);
                }
            }
        }
    }
}
```

`tests/DisasterPlus.Core.Tests/Earthquake/CollapseThresholdTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class CollapseThresholdTests
    {
        [Fact]
        public void SeedMatchesTheVanillaComposition()
        {
            // IL 事実文書 §A-3: new Randomizer(buildingID | (disasterID << 16))
            Assert.Equal(0x0007_0000 | 1234, CollapseThreshold.SeedFor(1234, 7));
            Assert.Equal(1234, CollapseThreshold.SeedFor(1234, 0));
        }

        [Fact]
        public void CollapseIsDrawnBeforeBurn()
        {
            // 順序が逆だと、倒壊しきい値と出火しきい値が入れ替わったまま
            // もっともらしい数字が出続ける。
            var rnd = new VanillaRandomizer(CollapseThreshold.SeedFor(4242, 9));
            int expectedCollapse = rnd.Int32(CollapseThreshold.Draws);
            int expectedBurn = rnd.Int32(CollapseThreshold.Draws);

            var t = CollapseThreshold.For(4242, 9);
            Assert.Equal(expectedCollapse, t.Collapse);
            Assert.Equal(expectedBurn, t.Burn);
        }

        [Fact]
        public void ThresholdsAreStableForTheSameBuildingAndDisaster()
        {
            // フレームにもステップにも依存しない、が主張の土台。
            var a = CollapseThreshold.For(500, 3);
            var b = CollapseThreshold.For(500, 3);
            Assert.Equal(a.Collapse, b.Collapse);
            Assert.Equal(a.Burn, b.Burn);
        }

        [Fact]
        public void ThresholdsAreInsideTheDrawRange()
        {
            for (ushort id = 1; id < 400; id++)
            {
                var t = CollapseThreshold.For(id, 5);
                Assert.InRange(t.Collapse, 0, (int)CollapseThreshold.Draws - 1);
                Assert.InRange(t.Burn, 0, (int)CollapseThreshold.Draws - 1);
            }
        }

        [Fact]
        public void DifferentBuildingsGetDifferentThresholds()
        {
            int distinct = 0;
            int first = CollapseThreshold.For(1, 5).Collapse;
            for (ushort id = 2; id < 64; id++)
            {
                if (CollapseThreshold.For(id, 5).Collapse != first) distinct++;
            }
            Assert.True(distinct >= 60, "too few distinct thresholds: " + distinct);
        }

        [Fact]
        public void HitsUsesStrictLessThan()
        {
            // IL: rnd.Int32(10000) < f * probability * 10000。等号は含まない。
            // f * p * 10000 == 200 のとき、しきい値 199 は当たり、200 は外れる。
            Assert.True(CollapseThreshold.Hits(199, 1f, 0.02f));
            Assert.False(CollapseThreshold.Hits(200, 1f, 0.02f));
        }

        [Fact]
        public void HitsIsFalseOutsideTheDisc()
        {
            Assert.False(CollapseThreshold.Hits(0, 0f, 0.02f));
        }

        [Fact]
        public void CollapseDistanceAgreesWithHits()
        {
            // 「震央から X m 以内なら倒れる」という主張そのものを固定する。
            const byte intensity = 100;
            for (int threshold = 0; threshold < 250; threshold += 7)
            {
                float limit = CollapseThreshold.CollapseDistance(
                    threshold, intensity, CollapseThreshold.GlobalDiscProbability);

                if (limit <= 0f)
                {
                    // どの距離でも倒れないこと（震央でも）。
                    Assert.False(CollapseThreshold.Hits(
                        threshold, SeismicIntensity.At(0f, intensity),
                        CollapseThreshold.GlobalDiscProbability));
                    continue;
                }

                float inside = limit * 0.99f;
                float outside = limit * 1.01f;
                Assert.True(CollapseThreshold.Hits(
                    threshold, SeismicIntensity.At(inside, intensity),
                    CollapseThreshold.GlobalDiscProbability));
                Assert.False(CollapseThreshold.Hits(
                    threshold, SeismicIntensity.At(outside, intensity),
                    CollapseThreshold.GlobalDiscProbability));
            }
        }

        [Fact]
        public void CollapseDistanceGrowsWithIntensity()
        {
            float at55 = CollapseThreshold.CollapseDistance(50, 55, CollapseThreshold.GlobalDiscProbability);
            float at255 = CollapseThreshold.CollapseDistance(50, 255, CollapseThreshold.GlobalDiscProbability);
            Assert.True(at255 > at55, "a stronger quake must reach further");
        }
    }
}
```

`tests/DisasterPlus.Core.Tests/Earthquake/DisasterPhasesTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class DisasterPhasesTests
    {
        [Fact]
        public void FlagBitsMatchTheGame()
        {
            // IL 事実文書 §A-1 / §A-6。ここがずれると全ての判定が静かに壊れる。
            Assert.Equal(1, DisasterPhases.Created);
            Assert.Equal(2, DisasterPhases.Deleted);
            Assert.Equal(4, DisasterPhases.Emerging);
            Assert.Equal(8, DisasterPhases.Active);
            Assert.Equal(16, DisasterPhases.Clearing);
            Assert.Equal(32, DisasterPhases.Finished);
            Assert.Equal(64, DisasterPhases.SelfTrigger);
            Assert.Equal(256, DisasterPhases.Significant);
            Assert.Equal(4096, DisasterPhases.Located);
        }

        [Fact]
        public void PhaseIsResolvedMostAdvancedFirst()
        {
            Assert.Equal(EarthquakePhase.Emerging,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Emerging));
            Assert.Equal(EarthquakePhase.Active,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Active));
            Assert.Equal(EarthquakePhase.Clearing,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Clearing));
            Assert.Equal(EarthquakePhase.Finished,
                DisasterPhases.PhaseOf(DisasterPhases.Created | DisasterPhases.Finished));
            Assert.Equal(EarthquakePhase.Unknown, DisasterPhases.PhaseOf(DisasterPhases.Created));
        }

        [Fact]
        public void AliveMirrorsTheVanillaScanCondition()
        {
            // IL: (m_flags & 3) == 1
            Assert.True(DisasterPhases.IsAlive(DisasterPhases.Created));
            Assert.False(DisasterPhases.IsAlive(DisasterPhases.Created | DisasterPhases.Deleted));
            Assert.False(DisasterPhases.IsAlive(0));
        }

        [Fact]
        public void HazardMapNeedsBothGates()
        {
            // IL 事実文書 §A-6: Located && (Emerging|Active)。嵐と**バイト単位で同一**。
            int located = DisasterPhases.Created | DisasterPhases.Located;
            Assert.False(DisasterPhases.PaintsHazardMap(located));                              // 進行中でない
            Assert.True(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Emerging));
            Assert.True(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Active));
            Assert.False(DisasterPhases.PaintsHazardMap(located | DisasterPhases.Clearing));    // 進行中でない
        }

        [Fact]
        public void HazardMapIsNeverPaintedWithoutLocated()
        {
            // 地震計が無ければ地震はハザードマップに出ない。0 を「安全」と読ませない根拠。
            int inProgress = DisasterPhases.Created | DisasterPhases.Active;
            Assert.False(DisasterPhases.PaintsHazardMap(inProgress));
            Assert.False(DisasterPhases.IsLocated(inProgress));
        }

        [Fact]
        public void SelfTriggerIsIndependentOfPhase()
        {
            // 立て忘れると Emerging で永久に止まる（§A-1）。読み側はこの区別が要る。
            int emerging = DisasterPhases.Created | DisasterPhases.Emerging;
            Assert.Equal(EarthquakePhase.Emerging, DisasterPhases.PhaseOf(emerging));
            Assert.Equal(EarthquakePhase.Emerging,
                DisasterPhases.PhaseOf(emerging | DisasterPhases.SelfTrigger));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー（`SeismicIntensity` / `SeismicScale` / `CollapseThreshold` / `DisasterPhases` が存在しない）

- [ ] **Step 3: `SeismicIntensity` を実装する**

`src/DisasterPlus/Core/Earthquake/SeismicIntensity.cs`:

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 震央からの距離と地震の強度から、**バニラが実際に倒壊判定へ使っている
    /// 局所係数**を出す。捏造ではない。
    ///
    /// IL 事実文書 §A-3 の全体円盤の呼び出し:
    ///   DestroyBuildings(preRadius: R, destructionRadiusMin: 0, destructionRadiusMax: R,
    ///                    probability: 0.02f)   ただし R = 2000 + m_intensity * 20
    /// その中で建物ごとに
    ///   fD = (destructionRadiusMax - dist) / Max(1, destructionRadiusMax - destructionRadiusMin)
    /// を計算する。min = 0、max = R ≧ 2000 なので Max(1, ...) は常に R になり、
    /// **fD = 1 - dist/R** に簡約される。この値がここでいう s である。
    ///
    /// 依頼文の「震源からの距離に応じた震度分布の概念が無い」は半分だけ正しかった。
    /// 距離減衰は最初から効いている。**欠けていたのは可視化だけ。**
    /// </summary>
    public static class SeismicIntensity
    {
        /// <summary>全体円盤の基底半径（IL: ldc.r4 2000）。</summary>
        public const float BaseRadius = 2000f;

        /// <summary>強度 1 あたりの半径増分（IL: ldc.r4 20）。</summary>
        public const float RadiusPerIntensity = 20f;

        /// <summary>
        /// DisasterManager.CreateDisaster が入れる既定値（IL_0028）。
        /// カメラシェイクの補正がここでゼロになる基準点でもある（Task 6）。
        /// </summary>
        public const byte VanillaDefaultIntensity = 55;

        /// <summary>全体円盤の半径 R。強度 55 で 3100 m、100 で 4000 m、255 で 7100 m。</summary>
        public static float RadiusOf(byte intensity)
        {
            return BaseRadius + intensity * RadiusPerIntensity;
        }

        /// <summary>
        /// 震央から distance の地点の局所係数 s。震央で 1、R で 0 の線形ランプ。
        ///
        /// R の外では 0 を返すが、**それは「揺れていない」ではなく「バニラが判定すら
        /// していない」**（preRadius によるハードカリング）。呼び出し側は
        /// <see cref="IsInside"/> で区別し、圏外を「強度 0.0」と表示しないこと。
        /// </summary>
        public static float At(float distance, byte intensity)
        {
            if (float.IsNaN(distance) || distance < 0f) return 0f;

            float r = RadiusOf(intensity);
            if (distance >= r) return 0f;
            return 1f - distance / r;
        }

        /// <summary>バニラが倒壊判定を行う範囲の内側か。</summary>
        public static bool IsInside(float distance, byte intensity)
        {
            if (float.IsNaN(distance) || distance < 0f) return false;
            return distance < RadiusOf(intensity);
        }
    }
}
```

- [ ] **Step 4: `SeismicScale` を実装する**

`src/DisasterPlus/Core/Earthquake/SeismicScale.cs`:

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>揺れの大きさの粗い区分。実在の震度階級の名前は使わない。</summary>
    public enum SeismicBand
    {
        None,
        Weak,
        Moderate,
        Strong,
        Severe,
    }

    /// <summary>
    /// 局所係数 s（0-1）を表示用の段階・バー・区分名にする。
    ///
    /// **気象庁震度階級を名乗らない**（設計書 §3.1）。s は加速度でも計測震度でもなく、
    /// ゲームの倒壊係数である。実在の尺度の名前を借りると、実在の意味があると
    /// 誤解させる。表示は「強度」「揺れの大きさ」に留め、必ず 0.0-1.0 の生値と併記する。
    ///
    /// 段階が 10 でバンドが 5 なのは意図的。バーの分解能は 10 段階で欲しいが、
    /// 10 個の段階名をローカライズすると、訳語の差がそのまま「意味のある尺度」に
    /// 見えてしまう。名前を付けるのは 5 区分までにする。
    ///
    /// バーは ASCII 固定（①の HazardLevel と同じ理由。CS の UI フォントに
    /// 罫線素片がある保証は無く、無ければ豆腐になる）。
    /// </summary>
    public static class SeismicScale
    {
        public const int Steps = 10;
        public const char FilledChar = '#';
        public const char EmptyChar = '-';

        /// <summary>s を 0-Steps に写す。単調増加。s = 1 でちょうど Steps。</summary>
        public static int StepOf(float s)
        {
            if (float.IsNaN(s) || s <= 0f) return 0;
            if (s >= 1f) return Steps;

            int step = (int)(s * Steps);
            if (step < 0) step = 0;
            if (step > Steps) step = Steps;
            return step;
        }

        /// <summary>長さ Steps のバー。埋まった数は StepOf と一致する。</summary>
        public static string BarOf(float s)
        {
            int filled = StepOf(s);
            var sb = new System.Text.StringBuilder(Steps);
            for (int i = 0; i < Steps; i++) sb.Append(i < filled ? FilledChar : EmptyChar);
            return sb.ToString();
        }

        /// <summary>区分名。境界は下側を含む（0.25 は Moderate）。</summary>
        public static SeismicBand BandOf(float s)
        {
            if (float.IsNaN(s) || s <= 0f) return SeismicBand.None;
            if (s < 0.25f) return SeismicBand.Weak;
            if (s < 0.5f) return SeismicBand.Moderate;
            if (s < 0.75f) return SeismicBand.Strong;
            return SeismicBand.Severe;
        }
    }
}
```

- [ ] **Step 5: `CollapseThreshold` を実装する**

`src/DisasterPlus/Core/Earthquake/CollapseThreshold.cs`:

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>1 建物ぶんの、この地震に対する固定しきい値 2 個。不変。</summary>
    public struct BuildingThresholds
    {
        /// <summary>倒壊しきい値。1 回目の引き。</summary>
        public readonly int Collapse;

        /// <summary>出火しきい値。2 回目の引き。</summary>
        public readonly int Burn;

        public BuildingThresholds(int collapse, int burn)
        {
            Collapse = collapse;
            Burn = burn;
        }
    }

    /// <summary>
    /// **本機能の目玉。** バニラが引くのと同一のしきい値を先読みする。
    ///
    /// IL 事実文書 §A-3、DisasterHelpers.DestroyBuildings の建物ループ:
    ///   rnd  = new Randomizer(buildingID | (seed &lt;&lt; 16))   // seed は災害 ID
    ///   fD   = (destructionRadiusMax - dist) / Max(1, destructionRadiusMax - destructionRadiusMin)
    ///   hitD = rnd.Int32(10000) &lt; fD * probability * 10000
    ///   hitB = rnd.Int32(10000) &lt; fB * probability * 10000
    ///
    /// **この種はフレームにもステップにも依存しない。**（建物, 災害）の組に対して定数で、
    /// 同じステップ内の 5 回の呼び出しも、次のステップも、同じ 2 個を引く。
    /// したがって全体円盤に関する限り、**倒れるかどうかは地震が始まった瞬間に既に
    /// 決まっている**。依頼文の「倒壊はおそらくランダム」は、建物ごとのしきい値が
    /// 一様乱数だという意味では正しく、距離減衰が無いという意味では間違っていた。
    ///
    /// **主張できる範囲の限界（呼び出し側は必ず併記すること）:**
    /// ここで出せるのは**全体円盤（probability = 0.02、震央中心）についてだけ**である。
    /// 断層に沿った 4 個の円盤は毎ステップ位置が振り直され、probability = 1 で
    /// その場をほぼ確実に壊す。断層帯の内側の建物について「倒れません」と言うと、
    /// 確信を持って誤った断定になる。<see cref="FaultBand"/> で内外を判定し、
    /// 内側なら別判定であることを明示すること。
    /// </summary>
    public static class CollapseThreshold
    {
        /// <summary>IL: rnd.Int32(10000)。UInt32 版のオーバーロードが呼ばれている。</summary>
        public const uint Draws = 10000u;

        /// <summary>全体円盤の probability（IL: ldc.r4 0.02）。震央で 2%。</summary>
        public const float GlobalDiscProbability = 0.02f;

        /// <summary>
        /// IL: buildingID | (disasterID &lt;&lt; 16)。
        /// どちらも ushort なので int に広げてから合成する。災害 ID は 1〜255 なので
        /// 実際には負にならないが、上位ビットが立った場合の符号拡張は
        /// VanillaRandomizer の ctor が引き受ける（そちらのテスト参照）。
        /// </summary>
        public static int SeedFor(ushort buildingId, ushort disasterId)
        {
            return buildingId | (disasterId << 16);
        }

        /// <summary>
        /// この（建物, 災害）の組に固定された 2 個のしきい値。
        /// **引く順序が意味を持つ** — 1 回目が倒壊、2 回目が出火。入れ替えないこと。
        /// </summary>
        public static BuildingThresholds For(ushort buildingId, ushort disasterId)
        {
            var rnd = new VanillaRandomizer(SeedFor(buildingId, disasterId));
            int collapse = rnd.Int32(Draws);
            int burn = rnd.Int32(Draws);
            return new BuildingThresholds(collapse, burn);
        }

        /// <summary>
        /// IL の比較そのもの: <c>threshold &lt; localFactor * probability * 10000</c>。
        /// int が float へ昇格して比較される。等号は含まない。
        /// </summary>
        public static bool Hits(int threshold, float localFactor, float probability)
        {
            if (float.IsNaN(localFactor) || float.IsNaN(probability)) return false;
            return threshold < localFactor * probability * Draws;
        }

        /// <summary>
        /// この建物が全体円盤で倒壊し始める震央距離。0 以下ならどの距離でも倒れない。
        ///
        ///   threshold &lt; (1 - d/R) * p * 10000
        ///   d &lt; R * (1 - threshold / (p * 10000))
        ///
        /// 「予言」ではなく、バニラが既に決めた値から導いた**事実**である。
        /// ただし全体円盤についてのみ（クラス doc の限界を参照）。
        /// </summary>
        public static float CollapseDistance(int threshold, byte intensity, float probability)
        {
            float denominator = probability * Draws;
            if (denominator <= 0f || float.IsNaN(denominator)) return 0f;

            float d = SeismicIntensity.RadiusOf(intensity) * (1f - threshold / denominator);
            return d > 0f ? d : 0f;
        }
    }
}
```

- [ ] **Step 6: `DisasterPhases` を実装する**

`src/DisasterPlus/Core/Earthquake/DisasterPhases.cs`:

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>災害の進行位相。</summary>
    public enum EarthquakePhase
    {
        Unknown,
        Emerging,
        Active,
        Clearing,
        Finished,
    }

    /// <summary>
    /// DisasterData.m_flags のビットと、そこから導ける判定。
    ///
    /// Core に置いているのは、これがゲームの型に一切依存しない整数演算であり、
    /// **ここを取り違えると全てが静かに壊れる**からユニットテストで固定したいため。
    /// Game 側は <c>(int)data.m_flags</c> を渡すだけにして、フラグの意味を
    /// 2 箇所に書かない。
    ///
    /// 典拠: IL 事実文書 §A-1（位相機械）/ §A-6（ハザードマップの 2 段ゲート）。
    /// </summary>
    public static class DisasterPhases
    {
        public const int Created = 1;
        public const int Deleted = 2;
        public const int Emerging = 4;
        public const int Active = 8;
        public const int Clearing = 16;
        public const int Finished = 32;

        /// <summary>
        /// これが立っていないと EarthquakeAI.StartDisaster / TsunamiAI.StartDisaster は
        /// 即 return し、m_activationFrame が 0 のまま **Emerging で永久に固まる**（§A-1）。
        /// 災害を起こす側（Task 9）は必ず立てる。読む側（Task 3）は
        /// m_activationFrame == 0 を「未定」として扱う。
        /// </summary>
        public const int SelfTrigger = 64;

        public const int Significant = 256;

        /// <summary>
        /// 測位済み。地震にこれを立てるのは、震央の EarthquakeCoverage != 0、
        /// すなわち**地震計だけ**（§A-2 / §C-2）。
        /// </summary>
        public const int Located = 4096;

        /// <summary>進んでいる方から順に見る。</summary>
        public static EarthquakePhase PhaseOf(int flags)
        {
            if ((flags & Finished) != 0) return EarthquakePhase.Finished;
            if ((flags & Clearing) != 0) return EarthquakePhase.Clearing;
            if ((flags & Active) != 0) return EarthquakePhase.Active;
            if ((flags & Emerging) != 0) return EarthquakePhase.Emerging;
            return EarthquakePhase.Unknown;
        }

        /// <summary>IL: (m_flags &amp; 3) == 1。バニラ自身の走査条件をそのまま写す。</summary>
        public static bool IsAlive(int flags)
        {
            return (flags & (Created | Deleted)) == Created;
        }

        public static bool IsLocated(int flags)
        {
            return (flags & Located) != 0;
        }

        /// <summary>
        /// この災害が今ハザードマップに何かを塗るか。
        /// **嵐（ThunderStormAI / TornadoAI）とバイト単位で同一のゲート**（§A-6）。
        /// これが false なのにグリッドが 0 だからといって「安全」と読ませてはいけない。
        /// </summary>
        public static bool PaintsHazardMap(int flags)
        {
            return IsLocated(flags) && (flags & (Emerging | Active)) != 0;
        }
    }
}
```

- [ ] **Step 7: テストとビルド**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: **181 件緑**（152 + 29）、ビルド警告 0。

- [ ] **Step 8: コミット**

```bash
git add src/DisasterPlus/Core/Earthquake tests/DisasterPlus.Core.Tests/Earthquake
git commit -m "feat: 震度の局所係数・表示段階・建物ごとのしきい値・災害位相"
```

---

## Task 3: 骨格・読み取り・プレハブ 4 値の実測・前提検証

**このタスクの成果物は「パネルのない機能」である。** 起動して都市をロードし、地震を 1 個起こすと、**診断ダンプに全ての生の値が出る**。以後のタスクの持続時間の設計は、ここで初めて実測される 4 個のプレハブ値の上に乗る（§A-0: これらの実数値は DLL に無い）。

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/EarthquakeReading.cs`
- Create: `src/DisasterPlus/Game/Earthquake/EarthquakeSnapshot.cs`
- Create: `src/DisasterPlus/Game/Earthquake/EarthquakeReader.cs`
- Create: `src/DisasterPlus/Game/Earthquake/EarthquakeHub.cs`
- Create: `src/DisasterPlus/Game/Earthquake/EarthquakeFeature.cs`
- Modify: `src/DisasterPlus/Game/Common/DisasterPlusLoading.cs`
- Modify: `src/DisasterPlus/Game/Diagnostics/Assumptions.cs`
- Modify: `src/DisasterPlus/Game/ModSettings.cs`
- Modify: `src/DisasterPlus/Game/Mod.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `DisasterPhases` / `EarthquakePhase` / `SeismicIntensity`（Task 2）、`Vec3`、`Log`、`ModSettings`、`FeatureHost`、`DiagnosticBuilder`
- Produces:
  - `DisasterPlus.Core.Earthquake.EarthquakeReading` — `class`（不変）
    `readonly ushort DisasterId` / `readonly Vec3 Epicentre` / `readonly float AngleRadians` / `readonly byte Intensity` / `readonly EarthquakePhase Phase` / `readonly bool Located` / `readonly uint StartFrame` / `readonly uint ActivationFrame` / `readonly bool ActivationScheduled` / `readonly int CoverageAtEpicentre` / `readonly float CrackLength` / `readonly float CrackWidth`
    ctor は全フィールドを取る。`float Radius { get; }`（= `SeismicIntensity.RadiusOf(Intensity)`）
  - `DisasterPlus.Game.EarthquakePrefabFacts` — `struct`、`readonly bool Resolved` / `readonly float CrackLength` / `readonly float CrackWidth` / `readonly uint EmergingDuration` / `readonly uint ActiveDuration`
  - `DisasterPlus.Game.EarthquakeSnapshot` — `class`（不変）
    `readonly IList<EarthquakeReading> Quakes` / `readonly EarthquakePrefabFacts Prefab` / `readonly uint CurrentFrame` / `readonly float HourOfDay` / `readonly bool DayNightEnabled` / `readonly bool Valid`、`static EarthquakeSnapshot Invalid()`
  - `DisasterPlus.Game.EarthquakeReader` — `static EarthquakeSnapshot Read()`（**sim スレッド専用**）
  - `DisasterPlus.Game.EarthquakeHub` — `static void Publish(EarthquakeSnapshot)` / `static EarthquakeSnapshot Latest { get; }` / `static void Clear()`
  - `DisasterPlus.Game.EarthquakeFeature` — `class : IDisasterFeature, IPausedTickFeature`、`const string FeatureName = "Earthquake"`

### 3.1 スレッド境界（②全体を通じて守る契約）

| 経路 | スレッド | 触ってよいもの |
|---|---|---|
| `EarthquakeFeature.OnSimulationTick` → `EarthquakeReader.Read()` → `EarthquakeHub.Publish()` | **sim** | `DisasterManager` / `BuildingManager` / `TerrainManager` / `SimulationManager` / `ImmaterialResourceManager` |
| `EarthquakeFeature.OnMainThreadUpdate` → パネル・ボタン・カメラシェイク | **main** | `UIComponent` / `InfoManager` / `CameraController` / `Camera` |
| `EarthquakeHub` | 両方 | 素の `lock` 1 本。外へは不変オブジェクトの参照だけ |

### 3.2 `IPausedTickFeature` を名乗る条件（第 2 層で破らないための約束）

`EarthquakeFeature` は `IPausedTickFeature` を実装する。理由は①と同じ（ロード直後にポーズしたままパネルを開くと全行が「読み取れません」になる）。

**ただし②はやがてゲームの状態を進める**（Task 9 の津波生成、Task 10 の追加被害）。`IPausedTickFeature` の契約は「deltaMinutes で状態を進めない」なので、`OnSimulationTick` を必ずこの形にする:

```csharp
public void OnSimulationTick(uint frameIndex, float deltaMinutes)
{
    if (!ModSettings.EarthquakeEnabled.value) return;

    // ここまでが「読んで publish するだけ」。ポーズ中もここは通る。
    var snapshot = EarthquakeReader.Read();
    EarthquakeHub.Publish(snapshot);

    // ★ ここから下は状態を進める。ポーズ中（deltaMinutes == 0）は絶対に通さない。
    //    Task 9 / Task 10 が足す処理は必ずこの行より下に置くこと。
    if (deltaMinutes <= 0f) return;

    // （Task 9: TsunamiChain.Tick / Task 10: LongPeriodDamage.Apply がここに入る）
}
```

**この 1 行のコメントを消さないこと。** 消した瞬間に「ポーズ中に地震の被害が進む」が起きる。

### 3.3 `EarthquakeReader` の要件

**sim スレッド専用。**`WeatherReader.CountLocatedStorms` の走査をそのまま手本にする。

1. `Singleton<DisasterManager>.exists` を確認し、無ければ `EarthquakeSnapshot.Invalid()`
2. `d.m_disasters.m_buffer` を `m_size`（かつ `buffer.Length`）まで走査
3. `DisasterPhases.IsAlive((int)buffer[i].m_flags)` で生存判定
4. `buffer[i].Info` を **要素ごとに try/catch** で取る（`get_Info` は境界検査をしない、4 命令。`WeatherReader` のコメント参照）。`info == null` は Unity の `==` オーバーロード経由で弾く
5. `info.m_disasterAI is EarthquakeAI` のものだけ拾う
6. 各地震について:
   - `m_targetPosition` → `Vec3`、`m_angle`、`m_intensity`
   - `DisasterPhases.PhaseOf` / `IsLocated`
   - `m_startFrame` / `m_activationFrame`
   - **`ActivationScheduled = (m_activationFrame != 0)`** — §A-1 の罠。`0` は「未定」であって「今」ではない。ここを `0` のまま時刻計算に流すと、Emerging に永久停止した地震について「あと 4739 年」のような数字が出る
   - `ImmaterialResourceManager.instance.CheckLocalResource(ImmaterialResourceManager.Resource.EarthquakeCoverage, m_targetPosition, out coverage)` → `Math.Min(coverage, 100)` **は行わない**（生値を持ち、クランプは表示側の `WarningLeadTime` が行う。生値と表示値の両方を診断に出せるようにする）
   - `CrackLength` / `CrackWidth` は `prefab.CrackLength * (0.5f + intensity * 0.005f)` / 同 Width（§A-3 の `L` / `W`）。プレハブが解決できなければ 0 を入れ、`FaultBand` 側が「不明」として扱う
7. プレハブ 4 値: `DisasterManager.FindDisasterInfo<EarthquakeAI>()` → `(EarthquakeAI)info.m_disasterAI` から `m_crackLength` / `m_crackWidth` / `m_emergingDuration` / `m_activeDuration`。**レベルごとに 1 回だけ走査してキャッシュ**する（`FireWhirlSpawner` のミスキャッシュと同じ間引き: 失敗したら 64 回の呼び出しはスキップ）。`OnLevelUnloading` でリセット
8. 時刻: **`m_currentDayTimeHour` を読まない**（§F-1 の罠）。sim スレッドの正解は

```csharp
float hour = SimulationManager.instance.m_dayTimeFrame
             * SimulationManager.DAYTIME_FRAME_TO_HOUR;
bool dayNight = SimulationManager.instance.m_enableDayNight;
```

   日夜サイクルが OFF だと `m_dayTimeOffsetFrames` が毎 sim フレーム再設定され、**hour は永久に 12.0 に固定される**。ここでは黙って通し、`DayNightEnabled = false` を snapshot に載せる。**表示側がその事実を隠さない**（Task 11 が文言を足す。それまでは診断行に出す）
9. 例外は `WeatherReader._readErrorLogged` と同じ形: 1 回目だけ `Log.Error`、以後は `Log.Diag("EqRead", ...)`。**レベルアンロードでリセットしない**（これはゲームのビルドに対する事実で、都市ごとの状態ではない）

### 3.4 `Assumptions` に足す 4 件（`TotalCheckCount` 12 → **16**）

| 名前 | 影響（impact、英語） |
|---|---|
| `EarthquakeAI disaster prefab exposes its four tuning fields` | `no earthquake durations or fault geometry can be read; every duration in this feature is designed on top of these four numbers. This also FAILs when the Natural Disasters DLC is not owned, which is expected.` |
| `DisasterData exposes m_intensity / m_activationFrame / m_startFrame / m_angle` | `neither the shaking strength nor the per-building margin can be shown` |
| `ImmaterialResourceManager.Resource.EarthquakeCoverage exists and CheckLocalResource is resolvable` | `seismograph coverage cannot be read, so the mod cannot explain why the hazard map is empty` |
| `SimulationManager exposes m_dayTimeFrame / DAYTIME_FRAME_TO_HOUR / m_enableDayNight` | `the sim-thread clock cannot be read; the mod would have to fall back to m_currentDayTimeHour, which is written by the main thread` |

実装の形は既存の `Check(...)` に合わせ、**個別に try/catch し、判定できないものは FAIL とする（偽 PASS を出さない）**。列挙メンバは文字列で見る（`Enum.IsDefined(typeof(ImmaterialResourceManager.Resource), "EarthquakeCoverage")`）—— コード内の直接参照はコンパイル時に整数へ畳み込まれるので、名前の変更を検出できない（既存の `SubInfoMode` 検証と同じ理由）。

> **DLC 非所持環境で FAIL することについて。** `TornadoAI disaster prefab is available` が既に同じ性質を持っており（DLC が無ければ FAIL）、それが確立した扱いである。impact 文に「DLC が無い環境ではこれが FAIL するのが正常」と明記して踏襲する。母数から外す（`ReportSliderNotApplicable` 方式）にはしない —— 地震機能そのものが DLC 依存なので、「使えない」と名指しするのが正しい。

### 3.5 設定・ログチャンネル

`ModSettings.Ensure()` に追加（**既存キーは 1 文字も触らない**）:

```csharp
EarthquakeEnabled  = new SavedBool("earthquakeEnabled", FileName, true, true);
// -1 = 未決定。ForecastButtonX/Y と全く同じ扱い（EarthquakePanelButton が決めて書き戻す）。
EarthquakeButtonX  = new SavedInt("earthquakeButtonX", FileName, -1, true);
EarthquakeButtonY  = new SavedInt("earthquakeButtonY", FileName, -1, true);
```

`Mod.OnSettingsUI` に「地震」グループ（有効化チェックボックス＋ボタン位置リセット）を、①の予報グループの直後に足す。DLC が無い環境では `Strings.EarthquakeNeedsDlc` を出す（`ForecastHazardNeedsDlc` と同じ形）。

`LogChannel.Earthquake`（= 8、**定義済み・未使用**）のチェックボックスを `channels` グループに足す。**`LogChannel` は常に完全修飾する。** ①の Forecast と同じく、**実際にそのチャンネルの `Log.Diag` を出す**こと（`EarthquakeFeature.OnSimulationTick` で `Log.DiagEnabled` ガードの内側に置く）——出さないなら死んだ設定になるので UI に載せない。

`EarthquakeFeature.OnSimulationTick` の診断ログ（①の `ForecastFeature` と同形。**引数の評価コストを避けるため必ず `DiagEnabled` で先に弾く**）:

```csharp
if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake)) return;
Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "earthquake",
    snapshot.Valid
        ? "quakes=" + snapshot.Quakes.Count + " hour=" + snapshot.HourOfDay.ToString("F1")
          + " dayNight=" + (snapshot.DayNightEnabled ? "on" : "off")
        : "snapshot invalid");
```

### 3.6 `WriteDiagnostics` に出すもの（**このタスクの主目的**）

```
Earthquake
  enabled                      yes
  snapshot                     valid
  prefab (EarthquakeAI)        resolved
    m_crackLength              <実測値>
    m_crackWidth               <実測値>
    m_emergingDuration         <実測値> frames (= <x> in-game hours)
    m_activeDuration           <実測値> frames (= <x> in-game hours)
  sim clock                    hour=12.0  dayNight=off (hour is pinned at 12.0)
  active quakes                1
    #7 intensity=100 phase=Active located=yes coverage=42 R=4000.0
       startFrame=... activationFrame=... (scheduled)
```

**フレーム → ゲーム内時間の換算は `FeatureHost.FramesPerMinute` から出す**（定数を直書きしない。③でこれを直書きして 4 倍ずれた前科がある）。

`m_activationFrame == 0` のときは `(not scheduled — SelfTrigger was never set)` と出す。

- [ ] **Step 1: `Strings` に 5 件足し、3 つのロケール源を一致させる**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `GroupEarthquake` | `Earthquake` | `地震` |
| `EarthquakeEnabled` | `Enable the earthquake panel` | `地震パネルを有効にする` |
| `EarthquakeResetButton` | `Reset the earthquake button position (takes effect next time you load a city)` | `地震ボタンの位置をリセットする（次に都市を読み込んだときに反映されます）` |
| `EarthquakeNeedsDlc` | `Earthquakes require the Natural Disasters DLC.` | `地震機能には Natural Disasters DLC が必要です。` |
| `LogChannelEarthquake` | `Earthquake` | `地震` |

**50 → 55 件になる。** `ja.txt` に訳を書き、`tools/GenerateLocaleTemplate.ps1` で `en.txt` を再生成し、3 つの集合が一致すること・BOM が無いことを確認する。

> 既に `EarthquakeDamageOwner` / `EarthquakeOwnerOther` / `EarthquakeOwnerSelf` が存在する（③の NDR 互換ドロップダウン用）。**名前を衝突させないこと。**

- [ ] **Step 2: `Core/Earthquake/EarthquakeReading.cs` を作る**

`ForecastReading` と同じ「不変の読み取り結果」。全フィールド `readonly`、ctor で全部受け取る。`Radius` は `SeismicIntensity.RadiusOf(Intensity)` を返す計算プロパティにして、半径を 2 箇所に持たない。

- [ ] **Step 3: `EarthquakeSnapshot` / `EarthquakeHub` を作る**

`WeatherSnapshot` / `ForecastHub` をそのまま手本にする。`Quakes` は `IList<EarthquakeReading>` だが、**構築後に一切変更しない**こと（`Invalid()` は空のリストを持つ）。`ForecastHub` と同じく素の `lock` 1 本。

- [ ] **Step 4: `EarthquakeReader` を作る**

3.3 の要件どおり。**`m_currentDayTimeHour` を書かないこと** — レビューで最初に見る行になる。

- [ ] **Step 5: `EarthquakeFeature` を作り、配線する**

- 3.2 のポーズガードの形を厳守する
- `OnLevelLoaded`: `EarthquakeHub.Clear()`、プレハブキャッシュのリセット
- `OnLevelUnloading`: `EarthquakeHub.Clear()`、プレハブキャッシュのリセット
- `OnMainThreadUpdate`: このタスクでは何もしない（Task 4 でパネルとボタンが入る）
- `DisasterPlusLoading.OnLevelLoaded` の `FeatureHost.Register(new ForecastFeature());` の直後に `FeatureHost.Register(new EarthquakeFeature());` を足す。**登録は `FeatureHost.LevelLoaded()` の前**（`_features` は同期されておらず、ディスパッチ開始前に揃っている必要がある）

- [ ] **Step 6: `Assumptions` に 4 件足し、`TotalCheckCount` を 16 にする**

`TotalCheckCount` の doc コメントの内訳も更新する（`火災旋風 4 件 ＋ 天気予報 7 件 ＋ 地震 4 件 ＋ スライダー 1 件`）。

- [ ] **Step 7: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **181 件緑**（このタスクは Core にテストを足さない。`EarthquakeReading` は不変のデータ運搬でロジックを持たないため）、ロケール 55/55/55 一致、BOM なし。

- [ ] **Step 8: `docs/playtest-checklist.md` の②の節に足す**

```markdown
2. **プレハブ 4 値の実測（このタスクの主目的。以後の全ての持続時間の設計がこの上に乗る）**
   都市をロードし、`Ctrl+F11` でダンプを取る。`Earthquake` セクションに
   `m_crackLength` / `m_crackWidth` / `m_emergingDuration` / `m_activeDuration` の
   4 行が出ること。**この 4 個の数値を報告すること**（DLL にも UnityPy にも
   無く、実機でしか取れない）
3. `sim clock` の行が出ること。**日夜サイクルを OFF にすると `dayNight=off` に変わり、
   `hour` が 12.0 に固定される**こと（これはバグではなくゲームの仕様。§F-1）
4. 災害パネルから地震を 1 個起こす。`active quakes` に 1 行出て、
   `phase` が Emerging → Active → Clearing と進むこと。
   **`(not scheduled)` と出たら不具合**（SelfTrigger が立っていない地震を掴んでいる）
5. 起動ログの `ASSUMPTIONS` の母数が **16**（スライダー検証が対象外なら 15）になり、
   地震ぶんの 4 項目が出ること
6. **2 つ目の都市をロードしても正常に動く**（プレハブキャッシュが持ち越されないこと）
```

- [ ] **Step 9: コミット**

```bash
git add -A && git commit -m "feat: 地震の読み取り・スナップショット・プレハブ実測値の診断出力"
```

---

## Task 4: パネル・震度分布・断層帯・ハザードビュー

**このタスクが第 1 層と第 2 層の分離機構を導入する。** 以後のタスクは行を足すだけで、この機構を守る。

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/FaultBand.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/FaultBandTests.cs`
- Create: `src/DisasterPlus/Game/Earthquake/EarthquakePanel.cs`
- Create: `src/DisasterPlus/Game/UI/EarthquakePanelButton.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeFeature.cs`（`OnMainThreadUpdate` / `OnLevelUnloading` / `WriteDiagnostics`）
- Modify: `src/DisasterPlus/Game/Diagnostics/Assumptions.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `EarthquakeHub` / `EarthquakeSnapshot` / `EarthquakeReading`（Task 3）、`SeismicIntensity` / `SeismicScale` / `DisasterPhases`（Task 2）、`InfoModeSwitch` / `FreeSlotFinder` / `RayGeometry` / `TerrainHeightSampler`（既存）
- Produces:
  - `DisasterPlus.Core.Earthquake.FaultBand` — `struct`
    ctor `FaultBand(Vec2 centre, float angleRadians, float length, float width)`
    `readonly Vec2 Centre` / `readonly Vec2 Direction` / `readonly float Length` / `readonly float Width` / `readonly bool Known`
    `float HalfWidthAt(float t)` / `bool Contains(Vec2 p)` / `Vec2 EndA { get; }` / `Vec2 EndB { get; }`
    `const float MaxOffset = 0.4f`
  - `DisasterPlus.Game.EarthquakePanel` — `static bool IsVisible { get; }` / `Show()` / `Hide()` / `Toggle()` / `Tick()` / `Destroy()`
    `static readonly Color32 Layer1Color` / `static readonly Color32 Layer2Color`
  - `DisasterPlus.Game.EarthquakePanelButton` — `static bool Installed { get; }` / `static bool UsedSavedPosition { get; }` / `static bool FoundFreeSlot { get; }` / `Tick()` / `Install()` / `Remove()`

### 4.1 `FaultBand` の意味と限界

§A-3 の断層 4 円盤:

```
dir = (-sin(m_angle), cos(m_angle))
L   = m_crackLength * (0.5 + i*0.005)
W   = m_crackWidth  * (0.5 + i*0.005)
毎ステップ 4 回:  t = Randomizer.Int32(-400, 400) * 0.001   // -0.4 .. 0.4
                  w = W * (1 - 4t²)
                  中心 p = centre + dir * (t*L) （＋蛇行）
                  DestroyBuildings(preRadius: w, destructionRadiusMax: w*2, probability: 1)
```

したがって「断層帯」＝ `t ∈ [-0.4, 0.4]` の各点で、断層線からの直交距離が `2·w(t)` 以内の領域。これが**当たりうる範囲**である。蛇行（`sin(t*freq + ph) * w * 0.5`）は `freq` / `ph` が `m_randomSeed` 由来で読めるが、包絡としては `w` の内側に収まるので**帯の幅には算入しない**（帯は `2w` なので蛇行 `0.5w` を含んでいる）。

**`Contains` は「当たる」ではなく「当たりうる」を返す。** 円盤の位置は毎ステップ振り直されるので、帯の中にいても当たらないことがあるし、1 回のステップで壊されることもある。パネルはこの行に必ず `Strings.EarthquakeFaultBandNote` を添える。

プレハブ 4 値が読めなかった場合は `Known == false` にし、`Contains` は常に false を返す。**そのとき Task 5 の「倒れません」という断定は出してはいけない**（断層帯の内外が分からないため）。

- [ ] **Step 1: `FaultBand` の失敗するテストを書く（6 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/FaultBandTests.cs`:

```csharp
using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class FaultBandTests
    {
        // 角度 0 のとき dir = (-sin 0, cos 0) = (0, 1)、つまり断層は Z 軸に沿う。
        private static FaultBand Sample()
        {
            return new FaultBand(new Vec2(0f, 0f), 0f, length: 1000f, width: 100f);
        }

        [Fact]
        public void DirectionMatchesTheVanillaFormula()
        {
            var band = Sample();
            Assert.Equal(0f, band.Direction.X, 4);
            Assert.Equal(1f, band.Direction.Z, 4);

            var rotated = new FaultBand(new Vec2(0f, 0f), (float)(Math.PI / 2), 1000f, 100f);
            Assert.Equal(-1f, rotated.Direction.X, 3);
            Assert.Equal(0f, rotated.Direction.Z, 3);
        }

        [Fact]
        public void HalfWidthTapersToZeroAtTheEnds()
        {
            var band = Sample();
            // w = W * (1 - 4t²) なので、|t| = 0.5 でちょうど 0。
            Assert.Equal(200f, band.HalfWidthAt(0f), 3);      // 2 * W * (1-0)
            Assert.Equal(0f, band.HalfWidthAt(0.5f), 3);
            Assert.True(band.HalfWidthAt(0.4f) > 0f);
            Assert.True(band.HalfWidthAt(0.4f) < band.HalfWidthAt(0f));
        }

        [Fact]
        public void EpicentreIsInside()
        {
            Assert.True(Sample().Contains(new Vec2(0f, 0f)));
        }

        [Fact]
        public void BeyondTheRuptureRangeIsOutside()
        {
            var band = Sample();
            // 円盤の中心は t ∈ [-0.4, 0.4]。L = 1000 なので |z| > 400 は帯の外。
            Assert.False(band.Contains(new Vec2(0f, 450f)));
            Assert.True(band.Contains(new Vec2(0f, 350f)));
        }

        [Fact]
        public void AcrossTheFaultIsBoundedByTwiceTheTaperedWidth()
        {
            var band = Sample();
            Assert.True(band.Contains(new Vec2(190f, 0f)));    // 2w(0) = 200
            Assert.False(band.Contains(new Vec2(210f, 0f)));
        }

        [Fact]
        public void UnknownGeometryNeverClaimsContainment()
        {
            // プレハブが読めなかったとき（length = width = 0）は、
            // 「内側」とも「外側」とも断定しない。Contains は常に false、Known も false。
            var unknown = new FaultBand(new Vec2(0f, 0f), 0f, 0f, 0f);
            Assert.False(unknown.Known);
            Assert.False(unknown.Contains(new Vec2(0f, 0f)));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認し、`FaultBand` を実装する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```
Expected: `The type or namespace name 'FaultBand' could not be found`

`src/DisasterPlus/Core/Earthquake/FaultBand.cs`:

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 断層に沿った 4 個の破壊円盤が**落ちうる範囲**の幾何。
    ///
    /// 典拠は IL 事実文書 §A-3。毎ステップ 4 回、断層上の正規化位置
    /// t ∈ [-0.4, 0.4] を引き直し、そこに幅 w = W(1 - 4t²) の円盤を置いて
    /// probability = 1、destructionRadiusMax = 2w で建物を壊す。
    ///
    /// **これは「当たる場所」ではなく「当たりうる場所」である。**
    /// 位置は毎ステップ振り直されるので、帯の中にいても当たらないことがある。
    /// 呼び出し側は必ずその旨を併記すること（Strings.EarthquakeFaultBandNote）。
    /// 逆に、全体円盤について「倒れません」と断定してよいのは
    /// **この帯の外側の建物だけ**である（CollapseThreshold のクラス doc 参照）。
    ///
    /// 蛇行（sin(t*freq + ph) * w * 0.5）は帯の幅 2w の内側に収まるので算入しない。
    ///
    /// Length / Width が 0（＝プレハブ 4 値が読めなかった）ときは
    /// <see cref="Known"/> が false になり、Contains は常に false を返す。
    /// 「分からない」を「外側」と言い換えないため、呼び出し側は Known を必ず見る。
    /// </summary>
    public struct FaultBand
    {
        /// <summary>円盤中心が落ちる正規化位置の上限（IL: Randomizer.Int32(-400, 400) * 0.001）。</summary>
        public const float MaxOffset = 0.4f;

        public readonly Vec2 Centre;

        /// <summary>断層の走向。IL: new Vector2(-sin(m_angle), cos(m_angle))。</summary>
        public readonly Vec2 Direction;

        /// <summary>L = m_crackLength * (0.5 + intensity * 0.005)。呼び出し側が計算済みの値を渡す。</summary>
        public readonly float Length;

        /// <summary>W = m_crackWidth * (0.5 + intensity * 0.005)。同上。</summary>
        public readonly float Width;

        public FaultBand(Vec2 centre, float angleRadians, float length, float width)
        {
            Centre = centre;
            double a = angleRadians;
            Direction = new Vec2(-(float)System.Math.Sin(a), (float)System.Math.Cos(a));
            Length = length > 0f && !float.IsNaN(length) ? length : 0f;
            Width = width > 0f && !float.IsNaN(width) ? width : 0f;
        }

        /// <summary>幾何が確定しているか。false なら内外を判定してはいけない。</summary>
        public bool Known { get { return Length > 0f && Width > 0f; } }

        public Vec2 EndA { get { return Centre + Direction * (-Length * 0.5f); } }
        public Vec2 EndB { get { return Centre + Direction * (Length * 0.5f); } }

        /// <summary>
        /// 正規化位置 t における、断層線からの直交方向の到達距離。
        /// 円盤の幅は w = W(1 - 4t²)、破壊は destructionRadiusMax = 2w まで届く。
        /// </summary>
        public float HalfWidthAt(float t)
        {
            if (!Known) return 0f;
            float taper = 1f - 4f * t * t;
            if (taper <= 0f) return 0f;
            return 2f * Width * taper;
        }

        /// <summary>p が破壊円盤の落ちうる範囲の内側か。**「当たる」ではない。**</summary>
        public bool Contains(Vec2 p)
        {
            if (!Known) return false;

            float dx = p.X - Centre.X;
            float dz = p.Z - Centre.Z;

            // 断層に沿った成分と、それに直交する成分に分解する。
            float along = dx * Direction.X + dz * Direction.Z;
            float across = dx * Direction.Z - dz * Direction.X;
            if (across < 0f) across = -across;

            float t = along / Length;
            if (t < -MaxOffset || t > MaxOffset) return false;

            return across <= HalfWidthAt(t);
        }
    }
}
```

- [ ] **Step 3: `EarthquakePanelButton` を作る**

`ForecastPanelButton` を丸ごと手本にする。差分だけ:

- `ButtonName = FreeSlotFinder.SelfPrefix + "EarthquakeButton"`
- `PreferredPosition` は予報ボタンと同じ `(8f, 50f)` のままでよい。**`FreeSlotFinder` が予報ボタンを避けて下にずらす**のがこの仕組みの目的で、①で `SelfPrefix` による一括除外を撤去した理由そのもの（`FreeSlotFinder.SelfPrefix` の doc）。**新しい preferred 座標を発明しないこと**——それをすると、他 MOD との衝突回避という当初の目的が空洞化する
- `owner` は `null`（初回配置なので、除外すべき「自分自身」がまだ画面に無い）
- 保存座標は `ModSettings.EarthquakeButtonX/Y`
- クリックで `EarthquakePanel.Toggle()`
- `ModSettings.EarthquakeEnabled.value` が false なら設置しない／設置済みなら撤去

- [ ] **Step 4: `EarthquakePanel` を作る**

`ForecastPanel` を手本にする（`UIView.AddUIComponent(typeof(UIPanel))` の非総称オーバーロード、構築途中の例外で孤児 GameObject を残さない `try/catch`、`backgroundSprite = "MenuPanel2"`、`Camera.main` のキャッシュ）。**新規に導入するのは層の分離機構だけ。**

#### 層の分離機構（本計画の共通規則を実装する部分）

```csharp
private static readonly Color32 Layer1Color = new Color32(255, 255, 255, 255);
// 第 2 層は「この MOD が発明した数字」。白と明確に違う色にするが、
// 色だけには頼らない（接頭辞とセクション見出しが本体）。
private static readonly Color32 Layer2Color = new Color32(150, 190, 255, 255);

// ★ UILabel を生成してよいのはこの 1 箇所だけ。
//   レビュー時の確認: このファイルで AddUIComponent(typeof(UILabel)) が 1 回しか現れないこと。
private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                float width, float height, Color32 color) { ... }

private static UILabel AddSectionHeader(UIPanel p, string suffix, ref float y) { ... }
private static UILabel AddPlainRow(UIPanel p, string suffix, ref float y) { ... }

/// <summary>第 1 層の行。バニラ自身の式と定数だけから導いた量にのみ使う。</summary>
private static UILabel AddLayer1Row(UIPanel p, string suffix, ref float y) { ... }

/// <summary>第 2 層の行。この MOD が発明した物理にのみ使う。</summary>
private static UILabel AddLayer2Row(UIPanel p, string suffix, ref float y) { ... }
```

- `AddLayer1Row` が返すラベルへ文字列を書くときは、**必ず** `Strings.SourceVanilla + " " + 本文` の形にする。ヘルパーの中で接頭辞を付ける専用のセッター `SetLayer1(UILabel, string)` / `SetLayer2(UILabel, string)` を用意し、`Refresh()` はそれ以外の方法でラベルの `text` に代入しない
- レビュー時の確認: `grep -n "\.text = " EarthquakePanel.cs` が返す行が、`SetLayer1` / `SetLayer2` / `AddSectionHeader` / `AddPlainRow` の中と、タイトル・閉じるボタンだけであること
- 第 2 層のセクションは**常に第 1 層より下**に構築する（構築順で保証する。実行時の並べ替えをしない）
- 第 2 層のセクションは、そのタスクの設定が OFF のときは**行ごと出さない**（空の見出しだけを残さない）

#### このタスクで出す内容

```
地震                                             [×]
  ── ゲームが実際に計算している値 ──             ← Strings.EarthquakeLayer1Header
  [実測] 発生中の地震: 1
  [実測] 強度: 10.0   影響半径: 4000 m
  [実測] 位相: 揺れています
  [実測] 本震まで: 12 分  ← Emerging かつ activationFrame != 0 のときだけ
  [実測] カーソル地点の強度: 0.62 [######----] 強い
  [実測] 断層帯: 内側
         4 つの破壊円盤は毎ステップ位置が変わるので、この帯は当たりうる範囲です
  [マップに表示]
  この地震は測位されていないため、ハザードマップには描かれません。測位するのは地震計です。
```

| 行 | 出典 | 出さない条件 |
|---|---|---|
| 発生中の地震 | `snapshot.Quakes.Count` | — |
| 強度 | `m_intensity / 10f` を `F1` で（災害パネルの表示と揃える。§A-2b の `m_label.text = (value/10).ToString("F1")`） | — |
| 影響半径 | `SeismicIntensity.RadiusOf` | — |
| 位相 | `DisasterPhases.PhaseOf` → `EarthquakePhaseEmerging/Active/Clearing` | Finished / Unknown なら行を出さない |
| 本震まで | `(m_activationFrame - currentFrame) / FeatureHost.FramesPerMinute` 分 | **`ActivationScheduled == false` なら `EarthquakeTimeUnknown`**。負なら行を出さない |
| カーソル地点の強度 | `SeismicIntensity.At` + `SeismicScale` | 圏外なら `EarthquakeOutOfRange`（**「0.0」と出さない**）。カーソル座標が取れなければ `ForecastUnavailable` 相当 |
| 断層帯 | `FaultBand.Contains` | `Known == false` なら行ごと出さない |

#### 「本震まで」を出してよい理由（①との違いを必ずコメントに書く）

①は「あと何時間で来る」を**禁じている**（発生判定が乱数だから）。②の「本震まで」は違う。`m_activationFrame` は `StartDisaster` が `m_startFrame + m_emergingDuration` として**確定値**を書き込んだものであり（§A-1）、`IsStillEmerging` はそれと現在フレームを比べているだけ。つまりこれは予測ではなく**予定表の読み上げ**である。設計書 §7-2 がこの区別を要求している。ただし `m_activationFrame == 0` は「予定が無い」であって「今」ではない。

#### ハザードビューと「空が正常」の説明

- 「マップに表示」は `InfoModeSwitch.ShowHazard(InfoManager.SubInfoMode.EarthquakeHazard)` を呼ぶだけ。①のラッパーは**一切変更しない**
- **`DisasterPhases.PaintsHazardMap` が true の地震が 1 つも無いときは、ハザードの数値を出さず、空である理由を書く**（`Strings.EarthquakeNotLocated`）。①の `ForecastNoStormDetected` と同じ構図で、同じ文体にする
- ハザードビューが出ていないときは `Strings.EarthquakeSwitchHazardView`（①の `ForecastSwitchHazardView` と同じ扱い。「読み取れません」を使い回さない）
- カーソル位置のハザード数値は `HazardMapReader.SampleAt(worldPos, InfoManager.SubInfoMode.EarthquakeHazard, out ok)`。**main スレッドから呼ぶ**（そのクラスの doc の通り）
- DLC が無い環境ではパネル全体を構築せず、`Strings.EarthquakeNeedsDlc` を 1 行出す（`ForecastPanel._hazardRowsBuilt` と同じ扱い）

#### カーソル座標

`ForecastPanel.TryPickCursorGround` と全く同じ実装（`UIView.IsInsideUI()` → `Camera.main` キャッシュ → `RayGeometry.IntersectTerrain(..., MaxRayDistance = 8000f, ...)`）をこのファイルにも置く。**共通化しない** — ①の実機確認の申し送りに「かすめて外すレイでは 501 回の高さサンプリングが毎フレーム走る。今回は意図的に最適化していない」があり、そこに手を入れるのは②のスコープ外。②も同じ性質を持つことを playtest チェックリストに書く。

- [ ] **Step 5: `EarthquakeFeature` に配線する**

```csharp
public void OnMainThreadUpdate()
{
    EarthquakePanelButton.Tick();
    EarthquakePanel.Tick();
}

public void OnLevelUnloading()
{
    EarthquakeHub.Clear();
    EarthquakePanelButton.Remove();
    EarthquakePanel.Destroy();
    // （Task 3 で入れたプレハブキャッシュのリセットはそのまま）
}
```

`WriteDiagnostics` に足す（①の `ForecastFeature` と同形）: ボタン位置と設置の経緯（`Installed` / `UsedSavedPosition` / `FoundFreeSlot`）、`InfoModeSwitch.IsShowingHazard`、`ハザードマップに塗られている地震の数`（＝ `PaintsHazardMap` が true の件数）。**最後の 1 つは重要**——テスターが「マップが空」なのか「本当に地震が無い」のかを切り分ける唯一の手段になる。

- [ ] **Step 6: `Assumptions` に 1 件足す（16 → 17）**

| 名前 | 影響 |
|---|---|
| `SubInfoMode.EarthquakeHazard exists and EarthquakeAI.UpdateHazardMap exists` | `the earthquake hazard heatmap cannot be shown, so the mod cannot explain the Located gate` |

`Enum.IsDefined(typeof(InfoManager.SubInfoMode), "EarthquakeHazard")` と、既存の `HasUpdateHazardMap(typeof(EarthquakeAI))` ヘルパーの再利用。

- [ ] **Step 7: `Strings` に 29 件足す（55 → 84）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeTitle` | `Earthquake` | `地震` |
| `SourceVanilla` | `[measured]` | `[実測]` |
| `SourceModel` | `[Disaster + model]` | `[本MODの推定]` |
| `EarthquakeLayer1Header` | `What the game actually computes` | `ゲームが実際に計算している値` |
| `EarthquakeLayer2Header` | `Added by Disaster + (not vanilla behaviour)` | `Disaster + が足した挙動（バニラにはありません）` |
| `EarthquakeNoneActive` | `No earthquake in progress.` | `発生中の地震はありません。` |
| `EarthquakeCount` | `Earthquakes in progress` | `発生中の地震` |
| `EarthquakeIntensity` | `Intensity` | `強度` |
| `EarthquakeRadius` | `Affected radius` | `影響半径` |
| `EarthquakePhase` | `Phase` | `位相` |
| `EarthquakePhaseEmerging` | `before the main shock` | `本震前` |
| `EarthquakePhaseActive` | `shaking` | `揺れています` |
| `EarthquakePhaseClearing` | `aftermath` | `収束中` |
| `EarthquakeTimeToShock` | `Time to the main shock` | `本震まで` |
| `EarthquakeTimeUnknown` | `not scheduled` | `未定` |
| `EarthquakeMinutes` | `min` | `分` |
| `EarthquakeAtCursor` | `Shaking at cursor` | `カーソル地点の強度` |
| `EarthquakeOutOfRange` | `outside the shaken area` | `圏外` |
| `EarthquakeFaultBand` | `Fault zone` | `断層帯` |
| `EarthquakeFaultInside` | `inside` | `内側` |
| `EarthquakeFaultOutside` | `outside` | `外側` |
| `EarthquakeFaultBandNote` | `The four rupture patches move every step, so this zone is where they can land, not where they will.` | `4 つの破壊円盤は毎ステップ位置が変わるので、この帯は「当たりうる範囲」であって「当たる場所」ではありません。` |
| `EarthquakeShowOnMap` | `Show on map` | `マップに表示` |
| `EarthquakeNotLocated` | `No earthquake is located right now. This map only shows a located, in-progress quake, and an Earthquake Sensor is what locates one.` | `現在、測位されている地震はありません。このマップは測位済みで進行中の地震だけを描き、測位するのは地震計です。` |
| `EarthquakeSwitchHazardView` | `Switch the earthquake hazard view on to read a value here.` | `地震のハザード表示に切り替えると数値が出ます。` |
| `EarthquakeBand` | `Weak/Moderate/Strong/Severe` の 4 語を 1 フィールドにまとめず、`EarthquakeBandWeak` / `EarthquakeBandModerate` / `EarthquakeBandStrong` / `EarthquakeBandSevere` の 4 件にする | `弱い` / `中程度` / `強い` / `非常に強い` |

**合計 29 件**（表の行は 26 行だが、最終行が 4 件ぶんのキーを表す）。`SeismicBand.None` にラベルは要らない（強度 0 の行は出さない）。

- [ ] **Step 8: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **187 件緑**（181 + 6）、ロケール 84/84/84 一致、BOM なし。

- [ ] **Step 9: `docs/playtest-checklist.md` の②の節に足す**

```markdown
7. **ボタンが予報ボタンの真上に出ないこと**（`FreeSlotFinder` の回帰確認。①の項目 13 が
   「②が入るまで単独では確認できない」としていた検証がここで初めて実施できる）。
   `output_log.txt` に `earthquake panel button installed at (x,y)` が出て、
   予報ボタンの座標と重ならないこと
8. ボタンでパネルが開閉する（`[×]` でも閉じられる）
9. **第 1 層と第 2 層が見て区別できること**: この時点では第 2 層の行はまだ無いので、
   全ての行に `[実測]` が付いていること。`[本MODの推定]` が 1 つも出ないこと
10. 地震を起こし、カーソルを震央から遠ざける。**強度が単調に下がり、影響半径の外で
    「0.0」ではなく「圏外」になること**
11. **地震計を建てずに**「マップに表示」を押す。**ヒートマップは真っ白で正常**。
    パネルに「現在、測位されている地震はありません…」と出ること。
    **ここに数値が出たら不具合**
12. 「本震まで」の行: Emerging 中だけ出て、Active になったら消えること。
    **`未定` と出たら、その地震は SelfTrigger が立っていない**（他 MOD 由来）
13. 断層帯の行に必ず注記文が併記されること
```

- [ ] **Step 10: コミット**

```bash
git add -A && git commit -m "feat: 地震パネル・震度分布・断層帯・ハザードビュー切替"
```

---

## Task 5: 建物ごとの余裕度（本機能の目玉）

**「この建物は震央から X m 以内なら倒れる」を、予言ではなく事実として出す。** ここが②で唯一「バニラより先を知っている」部分であり、Task 1 のビット一致が唯一の担保である。

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/BuildingMargin.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/BuildingMarginTests.cs`
- Create: `src/DisasterPlus/Game/Earthquake/BuildingProbe.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeHub.cs`（カーソル座標の main → sim 経路）
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeSnapshot.cs`（`CursorBuilding` を載せる）
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeReader.cs`（`BuildingProbe` を呼ぶ）
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakePanel.cs`（行の追加とカーソル座標の publish）
- Modify: `src/DisasterPlus/Game/Diagnostics/Assumptions.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `CollapseThreshold` / `BuildingThresholds` / `SeismicIntensity` / `FaultBand`（Task 2, 4）、`VanillaRandomizer`（Task 1）
- Produces:
  - `DisasterPlus.Core.Earthquake.CollapseVerdict` — `enum { Unknown, WillCollapse, Survives, InsideFaultZone, AlreadyDown, OutOfRange }`
  - `DisasterPlus.Core.Earthquake.BuildingMargin` — `struct`（不変）
    `readonly ushort BuildingId` / `readonly float Distance` / `readonly float LocalFactor` / `readonly int CollapseThresholdValue` / `readonly int BurnThresholdValue` / `readonly float CollapseWithin` / `readonly CollapseVerdict Verdict`
    `static BuildingMargin Evaluate(ushort buildingId, ushort disasterId, Vec2 buildingPos, Vec2 epicentre, byte intensity, FaultBand band, bool alreadyDown)`
    `static BuildingMargin None()`
  - `DisasterPlus.Game.BuildingProbe` — `static BuildingMargin ProbeAt(Vec3 worldPos, EarthquakeReading quake, FaultBand band, out ushort foundBuilding)`（**sim スレッド専用**）、`const float PickRadius = 64f`
  - `EarthquakeHub` に追加 — `static void PublishCursor(Vec3 pos, bool valid)`（main → sim）、`static bool TakeCursor(out Vec3 pos)`（sim）

### 5.1 なぜカーソルの建物特定を sim スレッドでやるのか

`BuildingManager.m_buildings.m_buffer` と `m_buildingGrid` は **sim スレッドが所有する**。一方カーソル座標は `Input.mousePosition` と `Camera.main` から来るので **main スレッドでしか取れない**。したがって:

```
main（EarthquakePanel.Tick）
  → 地形との交点を出す（既存の TryPickCursorGround）
  → EarthquakeHub.PublishCursor(worldPos, valid)
sim（EarthquakeReader.Read）
  → EarthquakeHub.TakeCursor(out pos)
  → BuildingProbe.ProbeAt(...)  ← ここで初めて建物バッファに触る
  → snapshot.CursorBuilding に載せる
main（EarthquakePanel.Refresh）
  → snapshot.CursorBuilding を描くだけ
```

**1 tick ぶん（最大 1/50 秒相当）の遅延が出る。** カーソルを速く動かすと表示が 1 フレーム遅れて追従する。これは正しい代償であり、遅延を消すために main から建物バッファを読んではいけない。この段落をそのまま `BuildingProbe` のクラス doc に書くこと。

`EarthquakeHub` の 2 方向は**同じ `_gate` 1 本**で守る（別の lock を足すと順序問題を作る）。

### 5.2 主張してよい範囲（このタスクの核心。ここを外すと機能が嘘になる）

`CollapseThreshold` が答えられるのは**全体円盤についてだけ**である。断層 4 円盤は毎ステップ位置が振り直され `probability = 1` で壊すので、帯の内側では別判定になる。

| 状態 | `Verdict` | 表示 |
|---|---|---|
| 震央距離 ≥ R | `OutOfRange` | 「この地震の影響範囲の外です」 |
| 既に倒壊／炎上 | `AlreadyDown` | 「既に倒壊または炎上しています」 |
| 断層帯の内側 | `InsideFaultZone` | 倒壊距離は**出すが**、「断層帯の内側なので破壊円盤による別判定があります」を必ず併記。**「倒れません」とは言わない** |
| 帯の外・`Hits` が true | `WillCollapse` | 「倒壊します（震央から X m 以内。現在 Y m）」 |
| 帯の外・`Hits` が false | `Survives` | 「全体円盤では倒壊しません（倒壊するのは震央から X m 以内）」。X ≤ 0 なら「この強度ではどれだけ近くても倒壊しません」 |
| 断層幾何が不明（`band.Known == false`） | `Unknown` | 数値は出すが判定を出さない。理由（プレハブ値が読めていない）を書く |

**もう 1 つ必ず出すこと。** 全体円盤の判定は地震が始まった瞬間に確定しており、しかも震央は動かないので、**Active の間ずっと同じ結論**になる。つまり `Survives` と出た建物は（帯の外なら）その地震で本当に最後まで残る。この「既に決まっている」という事実こそが依頼文の「倒壊はおそらくランダム」への回答なので、パネルの説明文（`Strings.EarthquakeGlobalDiscOnly`）に含める。

- [ ] **Step 1: 失敗するテストを書く（6 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/BuildingMarginTests.cs`:

```csharp
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class BuildingMarginTests
    {
        private static FaultBand NoBand()
        {
            // 幾何が確定した、しかしどの試験点も含まない小さな帯。
            return new FaultBand(new Vec2(0f, 0f), 0f, length: 10f, width: 1f);
        }

        private static BuildingMargin Eval(ushort buildingId, float distance, byte intensity,
                                           FaultBand band, bool alreadyDown)
        {
            return BuildingMargin.Evaluate(
                buildingId, disasterId: 7,
                buildingPos: new Vec2(distance, 0f), epicentre: new Vec2(0f, 0f),
                intensity: intensity, band: band, alreadyDown: alreadyDown);
        }

        [Fact]
        public void ThresholdsComeFromTheVanillaSeed()
        {
            var expected = CollapseThreshold.For(1234, 7);
            var m = Eval(1234, 500f, 100, NoBand(), false);
            Assert.Equal(expected.Collapse, m.CollapseThresholdValue);
            Assert.Equal(expected.Burn, m.BurnThresholdValue);
        }

        [Fact]
        public void OutsideTheRadius_IsOutOfRangeNotSurvives()
        {
            // 「圏外」と「耐える」を同じ結論にしない。前者はバニラが判定すらしていない。
            var m = Eval(10, 99999f, 55, NoBand(), false);
            Assert.Equal(CollapseVerdict.OutOfRange, m.Verdict);
        }

        [Fact]
        public void AlreadyDown_ShortCircuitsEverything()
        {
            var m = Eval(10, 100f, 255, NoBand(), true);
            Assert.Equal(CollapseVerdict.AlreadyDown, m.Verdict);
        }

        [Fact]
        public void InsideTheFaultZone_NeverClaimsSurvival()
        {
            // 帯の内側では、全体円盤で耐える建物でも「倒れません」と言ってはいけない。
            var wideBand = new FaultBand(new Vec2(0f, 0f), 0f, length: 4000f, width: 500f);
            for (ushort id = 1; id < 200; id++)
            {
                var m = Eval(id, 50f, 100, wideBand, false);
                Assert.NotEqual(CollapseVerdict.Survives, m.Verdict);
                Assert.Equal(CollapseVerdict.InsideFaultZone, m.Verdict);
            }
        }

        [Fact]
        public void UnknownFaultGeometry_ProducesUnknownVerdict()
        {
            var unknown = new FaultBand(new Vec2(0f, 0f), 0f, 0f, 0f);
            var m = Eval(10, 100f, 100, unknown, false);
            Assert.Equal(CollapseVerdict.Unknown, m.Verdict);
            // 数値そのものは出せる（しきい値は幾何と無関係）。
            Assert.True(m.CollapseThresholdValue >= 0);
        }

        [Fact]
        public void VerdictAgreesWithTheCollapseDistance()
        {
            // 「X m 以内で倒れる」という表示と、実際の判定が食い違わないこと。
            // これが食い違うと、いちばんもっともらしい形で嘘をつくことになる。
            var band = NoBand();
            for (ushort id = 1; id < 300; id++)
            {
                var far = Eval(id, 3000f, 255, band, false);
                if (far.Verdict == CollapseVerdict.OutOfRange) continue;

                bool expected = far.Distance < far.CollapseWithin;
                Assert.Equal(expected ? CollapseVerdict.WillCollapse : CollapseVerdict.Survives,
                             far.Verdict);
            }
        }
    }
}
```

- [ ] **Step 2: 失敗を確認し、`BuildingMargin` を実装する**

`src/DisasterPlus/Core/Earthquake/BuildingMargin.cs`:

```csharp
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>この建物のこの地震に対する結論。</summary>
    public enum CollapseVerdict
    {
        /// <summary>断層の幾何が読めていないので判定を出せない。</summary>
        Unknown,
        WillCollapse,
        /// <summary>**全体円盤では**倒壊しない。断層帯の外側でしか名乗ってはいけない。</summary>
        Survives,
        /// <summary>断層帯の内側。破壊円盤による別判定があるので生存を断定しない。</summary>
        InsideFaultZone,
        AlreadyDown,
        /// <summary>震央距離が R 以上。バニラは判定すらしていない。</summary>
        OutOfRange,
    }

    /// <summary>
    /// 1 建物ぶんの「余裕度」。**予言ではない。**
    ///
    /// バニラは建物ごとに new Randomizer(buildingID | (disasterID &lt;&lt; 16)) から
    /// 固定のしきい値を 2 個引き、局所係数 × probability がそれを超えたら壊す
    /// （IL 事実文書 §A-3）。この種はフレームにもステップにも依存しないので、
    /// 同じ種を再構成すれば**バニラがこれから引く値をここで先に知ることができる**。
    /// しかも全体円盤の震央は動かないので、**倒れるかどうかは地震が始まった瞬間に
    /// 既に決まっている**。依頼文の「倒壊はおそらくランダム」への回答がこれ。
    ///
    /// **限界（呼び出し側は必ず併記すること）:** ここで答えられるのは全体円盤
    /// （probability = 0.02、震央中心）についてだけ。断層に沿った 4 個の円盤は
    /// 毎ステップ位置が振り直され probability = 1 で壊すので、帯の内側では
    /// 「倒れません」と言ってはいけない。<see cref="CollapseVerdict.InsideFaultZone"/>。
    /// </summary>
    public struct BuildingMargin
    {
        public readonly ushort BuildingId;
        public readonly float Distance;
        public readonly float LocalFactor;
        public readonly int CollapseThresholdValue;
        public readonly int BurnThresholdValue;

        /// <summary>この距離より内側なら全体円盤で倒壊する。0 ならどの距離でも倒壊しない。</summary>
        public readonly float CollapseWithin;

        public readonly CollapseVerdict Verdict;

        private BuildingMargin(ushort buildingId, float distance, float localFactor,
                               int collapseThreshold, int burnThreshold,
                               float collapseWithin, CollapseVerdict verdict)
        {
            BuildingId = buildingId;
            Distance = distance;
            LocalFactor = localFactor;
            CollapseThresholdValue = collapseThreshold;
            BurnThresholdValue = burnThreshold;
            CollapseWithin = collapseWithin;
            Verdict = verdict;
        }

        /// <summary>カーソルの下に建物が無い、あるいは地震が無い。</summary>
        public static BuildingMargin None()
        {
            return new BuildingMargin(0, 0f, 0f, 0, 0, 0f, CollapseVerdict.Unknown);
        }

        public bool HasBuilding { get { return BuildingId != 0; } }

        public static BuildingMargin Evaluate(ushort buildingId, ushort disasterId,
                                              Vec2 buildingPos, Vec2 epicentre,
                                              byte intensity, FaultBand band, bool alreadyDown)
        {
            float dx = buildingPos.X - epicentre.X;
            float dz = buildingPos.Z - epicentre.Z;
            float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

            var thresholds = CollapseThreshold.For(buildingId, disasterId);
            float local = SeismicIntensity.At(distance, intensity);
            float within = CollapseThreshold.CollapseDistance(
                thresholds.Collapse, intensity, CollapseThreshold.GlobalDiscProbability);

            CollapseVerdict verdict;
            if (alreadyDown)
            {
                verdict = CollapseVerdict.AlreadyDown;
            }
            else if (!SeismicIntensity.IsInside(distance, intensity))
            {
                verdict = CollapseVerdict.OutOfRange;
            }
            else if (!band.Known)
            {
                // 帯の内外が分からないので、生存も倒壊も断定しない。
                verdict = CollapseVerdict.Unknown;
            }
            else if (band.Contains(buildingPos))
            {
                verdict = CollapseVerdict.InsideFaultZone;
            }
            else if (CollapseThreshold.Hits(thresholds.Collapse, local,
                                            CollapseThreshold.GlobalDiscProbability))
            {
                verdict = CollapseVerdict.WillCollapse;
            }
            else
            {
                verdict = CollapseVerdict.Survives;
            }

            return new BuildingMargin(buildingId, distance, local,
                                      thresholds.Collapse, thresholds.Burn, within, verdict);
        }
    }
}
```

- [ ] **Step 3: `EarthquakeHub` にカーソル経路を足す**

```csharp
private static Vec3 _cursor;
private static bool _cursorValid;

/// <summary>main スレッドから。カーソル直下の地形座標を sim 側へ渡す。</summary>
public static void PublishCursor(Vec3 pos, bool valid)
{
    lock (_gate) { _cursor = pos; _cursorValid = valid; }
}

/// <summary>sim スレッドから。最後に publish された座標を読む。</summary>
public static bool TakeCursor(out Vec3 pos)
{
    lock (_gate) { pos = _cursor; return _cursorValid; }
}
```

`Clear()` は `_cursorValid = false` も戻すこと（都市をまたいで前の都市の座標を残さない）。**`_gate` は既存の 1 本を使い回す。**

- [ ] **Step 4: `BuildingProbe` を作る（sim スレッド専用）**

- `FireWhirlDamage.CollectNearby` の走査を手本にする（セル 64、オフセット 135、`[0,269]` クランプ、`m_nextGridBuilding` 連結、`guard > 32768` の保険）
- `PickRadius = 64f`（グリッド 1 セルぶん）以内で、カーソル座標に**最も近い**建物を 1 個選ぶ
- 候補条件は `(m_flags & Building.Flags.Created) != 0`。**`Collapsed` は弾かない** — 弾くと「既に倒壊しています」と表示できなくなる。代わりに `alreadyDown = (m_flags & Building.Flags.Collapsed) != 0 || m_fireIntensity != 0` として `Evaluate` に渡す
- 見つからなければ `foundBuilding = 0` と `BuildingMargin.None()`
- **例外を出さない。** `buildings[id]` の添字は必ず `id < buildings.Length` で守る（sim スレッドでの `IndexOutOfRange` はスタックトレース無しのポップアップになる）

`EarthquakeReader.Read()` の末尾で、**最も強度の高い進行中の地震 1 個**（`Active` を `Emerging` より優先し、同位なら `m_intensity` が大きい方）についてだけ `BuildingProbe.ProbeAt` を呼ぶ。**全地震ぶん走査しない** — 毎 sim tick のコストを地震の数に比例させない。どの地震についての判定かは snapshot に `CursorQuakeId` として載せ、パネルがそれを表示する（複数の地震が同時進行しているのに 1 個ぶんの判定だけを出して黙っていると、また「確信を持って誤った数値」になる）。

- [ ] **Step 5: `Assumptions` に 1 件足す（17 → 18）**

**これが Task 1 のビット一致を実際に保証する唯一の検査。**

| 名前 | 影響 |
|---|---|
| `VanillaRandomizer reproduces ColossalFramework.Math.Randomizer bit for bit` | `every per-building collapse verdict is wrong; the panel would keep showing plausible numbers that do not match what the game draws` |

```csharp
Check("VanillaRandomizer reproduces ColossalFramework.Math.Randomizer bit for bit",
      "every per-building collapse verdict is wrong; the panel would keep showing "
      + "plausible numbers that do not match what the game draws",
      delegate
      {
          // 本物と自前の実装を並べて回す。ビット列だけでなく「引く順序」も見る
          // （1 個ずれる壊れ方をこの検査で捕まえるため、必ず 2 回以上引く）。
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
```

**`Randomizer` は struct なので、必ずローカル変数に置いて使う**（プロパティやフィールド経由で呼ぶとコピーが進んで列が分岐する）。`Check` は例外を FAIL として扱うので、型が見つからない場合も安全に倒れる。

- [ ] **Step 6: パネルに行を足す**

`EarthquakePanel` に第 1 層の行として追加（`AddLayer1Row` を使う。**このタスクで新しいラベル生成経路を作らない**）:

```
  [実測] カーソル下の建物: #1234
  [実測] 倒壊するのは震央から 2,340 m 以内 / 現在 3,102 m → 倒壊しません
         全体円盤についての判定です。断層帯の内側では破壊円盤が別に判定します。
```

- `EarthquakePanel.Tick()` の中で、カーソル座標を毎フレーム `EarthquakeHub.PublishCursor` する（`TryPickCursorGround` の結果をそのまま流す）
- 表示は 5.2 の表のとおり。**`Survives` を断層帯の内側で出さないこと**は `BuildingMargin` が保証しているが、文言側でも `EarthquakeGlobalDiscOnly` を常に併記する
- 距離は `F0` で桁区切り無し（ロケール依存の桁区切りを避ける）

- [ ] **Step 7: `Strings` に 7 件足す（84 → 91）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeBuildingUnderCursor` | `Building under the cursor` | `カーソル下の建物` |
| `EarthquakeNoBuilding` | `no building under the cursor` | `カーソルの下に建物がありません` |
| `EarthquakeCollapseWithin` | `Collapses within` | `倒壊するのは震央から` |
| `EarthquakeCurrentDistance` | `current distance` | `現在の距離` |
| `EarthquakeVerdictCollapse` | `will collapse` | `倒壊します` |
| `EarthquakeVerdictSurvive` | `will not collapse` | `倒壊しません` |
| `EarthquakeGlobalDiscOnly` | `This is decided for the city-wide disc, and it was already decided the moment the quake started. Inside the fault zone the four rupture patches judge separately.` | `これは全体円盤についての判定で、地震が始まった瞬間に既に決まっています。断層帯の内側では 4 つの破壊円盤が別に判定します。` |

`AlreadyDown` / `OutOfRange` / `InsideFaultZone` / `Unknown` の文言は既存キーを使い回す（`EarthquakeOutOfRange` / `EarthquakeFaultInside` / `EarthquakeFaultBandNote`）。新しいキーを増やさない。

- [ ] **Step 8: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **193 件緑**（187 + 6）、ロケール 91/91/91 一致。

- [ ] **Step 9: `docs/playtest-checklist.md` の②の節に足す**

```markdown
14. **【この機能の目玉・実機でしか検証できない】** 強度を上げた地震を起こし、
    震央から少し離れた建物にカーソルを合わせる。「倒壊します／倒壊しません」が出ること。
    **その建物が実際にその通りになるか**を見届ける（倒壊すると出た建物が残る、
    あるいは倒壊しないと出た建物が全体円盤で倒れたら、Task 1 の再現が壊れている）。
    ただし**断層帯の内側の建物は対象外**（別判定なので、倒れても矛盾ではない）
15. 起動ログの `ASSUMPTIONS` に
    `VanillaRandomizer reproduces ColossalFramework.Math.Randomizer bit for bit` が
    **PASS** で出ること。**ここが FAIL なら 14 の表示は全て信用してはいけない**
16. カーソルを速く動かすと表示が 1 フレーム遅れて追従すること（sim スレッド経由の
    設計上の代償。カクつきではない）
17. 地震が 2 個同時に進行しているとき、判定がどちらの地震についてのものか
    パネルに出ていること
```

- [ ] **Step 10: コミット**

```bash
git add -A && git commit -m "feat: 建物ごとの倒壊余裕度と、バニラ乱数との一致検証"
```

---

## Task 6: 揺れの式（Core）とカメラシェイクの強度・距離補正

**設計書 §8 のタスク 1 に入っていた「波形」の Core 実装をここへ移した。** 最初にそれを必要とするのがこのタスクで、Task 8 の波形グラフが同じ関数を再利用する。「setup は、それを必要とする成果物のタスクに畳み込む」という原則に従っただけで、§8 の順序（4 → 6）は変えていない。

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/ShakeWaveform.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/ShakeWaveformTests.cs`
- Create: `src/DisasterPlus/Game/Earthquake/CameraShakeBooster.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeFeature.cs`（`OnMainThreadUpdate` / `OnLevelUnloading` / `WriteDiagnostics`）
- Modify: `src/DisasterPlus/Game/ModSettings.cs`、`src/DisasterPlus/Game/Mod.cs`
- Modify: `src/DisasterPlus/Game/Diagnostics/Assumptions.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `EarthquakeHub` / `EarthquakeSnapshot`（Task 3）、`SeismicIntensity`（Task 2）
- Produces:
  - `DisasterPlus.Core.Earthquake.ShakeWaveform` —
    `const float BaseAmplitude = 0.3f` / `const float DistanceFalloff = 0.001f` / `const float EnvelopeRate = 0.02454369f` / `const float FastRate = 0.63f` / `const float SlowRate = 0.17f` / `const int FrameOffset = 128` / `const int EnvelopePeriodFrames = 256`
    `static bool IsShaking(long elapsedPlusOffset, uint activeDuration)`
    `static float AmplitudeAt(float distance, float t)`
    `static float DisplacementAt(float distance, float t)`
    `static float IntensityFactor(byte intensity)`
  - `DisasterPlus.Game.CameraShakeBooster` — `static void Update()`（**main スレッド、毎フレーム**）/ `static void Reset()` / `static float LastAdded { get; }`（診断用）
  - `ModSettings.EarthquakeShakeBoost`（`SavedBool "eqShakeBoost"`、既定 **true**）

### 6.1 何を足すのか（バニラを抑制しない）

§A-7 のバニラの `RenderInstance`:

```
e   = m_referenceFrameIndex - m_activationFrame + 128       // e <= 0 または e >= m_activeDuration なら何もしない
v   = camera.transform.InverseTransformPoint(m_targetPosition); v.z *= 0.25
amp = 0.3 / (1 + v.magnitude * 0.001)
t   = e + SimulationManager.m_referenceTimer
amp *= 0.5 - 0.5*cos(t * 0.02454369)                        // 256 フレーム周期の包絡線
s   = (sin(t*0.63) + sin(t*0.17)) * amp
dir = (-sin(m_angle), cos(m_angle))
shake.x += s * dir.y ;  shake.z -= s * dir.x
```

**振幅に `m_intensity` が入っていない。** 強度 25.5 の地震でも 5.5 と同じ揺れになる。これが依頼の「距離による震度分布」に直結する空白である。

②は同じ式を main スレッドで独立に評価し、`(intensity / 55 - 1)` を掛けたものを `CameraController.m_cameraShake` に**足す**。抑制はしない（Harmony も不要）。結果として合計は `バニラの揺れ × (intensity / 55)` になる。

- **強度 55（バニラ既定）で追加分は厳密に 0** ＝ バニラと完全に同一の挙動。既定 ON にできる根拠がこれ
- 55 未満では負。弱い地震はより弱く揺れる
- `DisasterManager.m_disableCameraShake` が true のときは**足さない**（プレイヤーの意思を尊重）
- 上限を設ける。強度 255 で画面が使い物にならなくなるのは不具合であって迫力ではない

### 6.2 バニラと同位相にするための 3 点（ここを外すと二重に揺れて汚くなる）

1. **時刻は `m_referenceFrameIndex` と `m_referenceTimer` を使う**（`m_currentFrameIndex` ではない）。バニラの `RenderInstance` は描画側のこの 2 つで `t` を作っている。これは main スレッドが所有する値で、②の加算も main スレッドなので整合する
2. **距離もバニラと同じカメラ空間で測る**（`Camera.main.transform.InverseTransformPoint(epicentre)` して `z *= 0.25f` してから `magnitude`）。震央距離に変えると、同じ波に別の振幅が掛かって位相はともかく形が崩れる
3. **`e` の窓判定も同じ**（`e <= 0` / `e >= m_activeDuration` で何もしない）。`m_activeDuration` はプレハブ値で、Task 3 の snapshot に載っている。**読めていないときは加算しない**（窓が分からないまま足すと、地震が終わった後も揺れ続ける）

### 6.3 上限

```csharp
/// <summary>
/// 追加できる強度倍率の上限。強度 255 だと (255/55 - 1) = 3.64 倍になり、
/// 合計はバニラの 4.64 倍。画面が使い物にならなくなるのは迫力ではなく不具合。
/// </summary>
public const float MaxIntensityFactor = 2f;

/// <summary>
/// 1 フレームに足せる変位の絶対値の上限。バニラの理論最大は
/// |sin + sin| = 2 × amp = 0.6 なので、これはその 2 倍にあたる。
/// </summary>
private const float MaxAddedShake = 1.2f;
```

`IntensityFactor` は Core 側で `MaxIntensityFactor` にクランプする（テストで固定する）。

- [ ] **Step 1: 失敗するテストを書く（8 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/ShakeWaveformTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class ShakeWaveformTests
    {
        [Fact]
        public void EnvelopeRateGivesA256FramePeriod()
        {
            // IL: 0.5 - 0.5*cos(t * 0.02454369)。2π / 0.02454369 = 256.0。
            double period = 2.0 * System.Math.PI / ShakeWaveform.EnvelopeRate;
            Assert.Equal(ShakeWaveform.EnvelopePeriodFrames, period, 1);
        }

        [Fact]
        public void EnvelopeIsZeroAtTheStartAndPeakInTheMiddle()
        {
            Assert.Equal(0f, ShakeWaveform.AmplitudeAt(0f, 0f), 4);
            Assert.Equal(0f, ShakeWaveform.AmplitudeAt(0f, ShakeWaveform.EnvelopePeriodFrames), 3);
            Assert.Equal(ShakeWaveform.BaseAmplitude,
                         ShakeWaveform.AmplitudeAt(0f, ShakeWaveform.EnvelopePeriodFrames / 2f), 3);
        }

        [Fact]
        public void AmplitudeFallsOffWithDistance()
        {
            float t = ShakeWaveform.EnvelopePeriodFrames / 2f;
            float near = ShakeWaveform.AmplitudeAt(0f, t);
            float mid = ShakeWaveform.AmplitudeAt(1000f, t);
            float far = ShakeWaveform.AmplitudeAt(5000f, t);
            Assert.True(near > mid && mid > far, "amplitude must decrease with distance");
            // 1 / (1 + 1000*0.001) = 0.5
            Assert.Equal(ShakeWaveform.BaseAmplitude * 0.5f, mid, 3);
        }

        [Fact]
        public void DisplacementIsBoundedByTwiceTheAmplitude()
        {
            for (float t = 0f; t < 2048f; t += 0.37f)
            {
                float amp = ShakeWaveform.AmplitudeAt(250f, t);
                float d = ShakeWaveform.DisplacementAt(250f, t);
                Assert.True(System.Math.Abs(d) <= 2f * amp + 1e-4f, "displacement out of envelope at t=" + t);
            }
        }

        [Fact]
        public void DisplacementActuallyOscillates()
        {
            // 2 本の正弦の合成なので符号が何度も変わる。定数化していないことを固定する。
            int signChanges = 0;
            float prev = ShakeWaveform.DisplacementAt(0f, 128f);
            for (float t = 128.5f; t < 200f; t += 0.5f)
            {
                float d = ShakeWaveform.DisplacementAt(0f, t);
                if ((d < 0f) != (prev < 0f)) signChanges++;
                prev = d;
            }
            Assert.True(signChanges > 5, "too few sign changes: " + signChanges);
        }

        [Fact]
        public void IntensityFactorIsZeroAtTheVanillaDefault()
        {
            // 強度 55 で追加分がちょうど 0 = バニラと完全に同一の挙動。
            // 既定 ON にしてよい根拠そのものなので、必ず固定する。
            Assert.Equal(0f, ShakeWaveform.IntensityFactor(SeismicIntensity.VanillaDefaultIntensity), 5);
        }

        [Fact]
        public void IntensityFactorIsNegativeBelowTheDefaultAndClampedAbove()
        {
            Assert.True(ShakeWaveform.IntensityFactor(0) < 0f);
            Assert.Equal(-1f, ShakeWaveform.IntensityFactor(0), 4);
            Assert.Equal(ShakeWaveform.MaxIntensityFactor, ShakeWaveform.IntensityFactor(255), 4);
        }

        [Fact]
        public void ShakingWindowMatchesTheVanillaGuard()
        {
            // IL: e <= 0 なら return、e >= m_activeDuration なら return。
            Assert.False(ShakeWaveform.IsShaking(0, 1000u));
            Assert.False(ShakeWaveform.IsShaking(-5, 1000u));
            Assert.True(ShakeWaveform.IsShaking(1, 1000u));
            Assert.True(ShakeWaveform.IsShaking(999, 1000u));
            Assert.False(ShakeWaveform.IsShaking(1000, 1000u));
            // m_activeDuration が読めていない（0）ときは常に false。
            Assert.False(ShakeWaveform.IsShaking(1, 0u));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認し、`ShakeWaveform` を実装する**

`src/DisasterPlus/Core/Earthquake/ShakeWaveform.cs`:

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// **バニラ自身の揺れの式**（IL 事実文書 §A-7、EarthquakeAI.RenderInstance）。
    /// 捏造ではなく、ゲームが毎フレーム計算しているものをそのまま写している。
    ///
    ///   amp = 0.3 / (1 + distance * 0.001)
    ///   amp *= 0.5 - 0.5 * cos(t * 0.02454369)      // 2π/0.02454369 = 256 フレーム周期
    ///   s   = (sin(t * 0.63) + sin(t * 0.17)) * amp
    ///
    /// 成分は 0.63 rad/frame（周期 ≒10 フレーム）と 0.17 rad/frame（≒37 フレーム）の
    /// 2 本だけで、**長周期成分は存在しない**（256 フレーム周期のものは振幅の
    /// 包絡線であって地震動ではない）。長周期地震動は Task 10 が別に足す
    /// —— あちらは第 2 層、こちらは第 1 層。
    ///
    /// **振幅に m_intensity は入っていない。** それがこの MOD が補正する空白であり、
    /// <see cref="IntensityFactor"/> がその補正倍率を返す。
    ///
    /// distance の意味は呼び出し側で変わる:
    ///   - カメラシェイク補正（Task 6）は**カメラからの距離**（バニラと同じ）
    ///   - 波形グラフ（Task 8）は**震源から観測点までの距離**
    /// 同じ式の別評価であって近似ではない。UI にもその差を書くこと（設計書 §3.5）。
    ///
    /// Core は UnityEngine.Mathf を使えないので System.Math（double）で計算して
    /// float に落とす。バニラの Mathf.Sin（float）とは最下位ビットが異なりうるが、
    /// これは表示と演出のための量であって予測ではないので許容する。
    /// </summary>
    public static class ShakeWaveform
    {
        public const float BaseAmplitude = 0.3f;
        public const float DistanceFalloff = 0.001f;
        public const float EnvelopeRate = 0.02454369f;
        public const int EnvelopePeriodFrames = 256;
        public const float FastRate = 0.63f;
        public const float SlowRate = 0.17f;

        /// <summary>IL: e = m_referenceFrameIndex - m_activationFrame + 128。</summary>
        public const int FrameOffset = 128;

        /// <summary>
        /// 追加できる強度倍率の上限。強度 255 だと素の値は (255/55 - 1) = 3.64 になり、
        /// 合計でバニラの 4.64 倍になる。画面が使い物にならなくなるのは迫力ではなく不具合。
        /// </summary>
        public const float MaxIntensityFactor = 2f;

        /// <summary>
        /// バニラの表示窓。<paramref name="activeDuration"/> はプレハブ値で、
        /// **読めていないとき（0）は false を返す** —— 窓が分からないまま揺らすと
        /// 地震が終わった後も揺れ続ける。
        /// </summary>
        public static bool IsShaking(long elapsedPlusOffset, uint activeDuration)
        {
            if (activeDuration == 0u) return false;
            return elapsedPlusOffset > 0 && elapsedPlusOffset < activeDuration;
        }

        public static float AmplitudeAt(float distance, float t)
        {
            if (float.IsNaN(distance) || float.IsNaN(t)) return 0f;
            if (distance < 0f) distance = 0f;

            float amp = BaseAmplitude / (1f + distance * DistanceFalloff);
            amp *= 0.5f - 0.5f * (float)System.Math.Cos(t * EnvelopeRate);
            return amp < 0f ? 0f : amp;
        }

        public static float DisplacementAt(float distance, float t)
        {
            float amp = AmplitudeAt(distance, t);
            if (amp <= 0f) return 0f;
            return (float)(System.Math.Sin(t * FastRate) + System.Math.Sin(t * SlowRate)) * amp;
        }

        /// <summary>
        /// バニラの揺れに掛ける**追加**倍率。
        /// 強度 55（DisasterManager.CreateDisaster の既定値）でちょうど 0 になり、
        /// そのとき合計はバニラと完全に一致する。
        /// </summary>
        public static float IntensityFactor(byte intensity)
        {
            float f = (float)intensity / SeismicIntensity.VanillaDefaultIntensity - 1f;
            if (f > MaxIntensityFactor) return MaxIntensityFactor;
            if (f < -1f) return -1f;
            return f;
        }
    }
}
```

- [ ] **Step 3: `CameraShakeBooster` を作る（main スレッド、毎フレーム）**

`src/DisasterPlus/Game/Earthquake/CameraShakeBooster.cs`:

- `EarthquakeFeature.OnMainThreadUpdate()` から毎フレーム呼ぶ。**加算はレンダリング側が毎フレーム消費する**ので（`CameraController.LateUpdate` が書き戻してリセットする）、足し続ける必要がある
- `ModSettings.EarthquakeShakeBoost.value` が false なら何もしない
- `Singleton<DisasterManager>.exists` を確認し、`instance.m_disableCameraShake` が true なら**何もしない**
- `CameraController` は `SceneObjects.FindInScene<CameraController>()` で取り、**参照をキャッシュしつつ Unity の `== null`（fake-null）で毎回確認する**（③で静的キャッシュが都市をまたいで無言で死んだ前例がある）。`Camera.main` も `ForecastPanel._mainCameraCache` と同じ形でキャッシュする
- `EarthquakeHub.Latest` の各地震について（`Emerging|Active` のものだけ）:

```csharp
float t = e + SimulationManager.instance.m_referenceTimer;   // e は下で計算
// バニラと同じカメラ空間の距離（§A-7 IL_003F）。
Vector3 v = cam.transform.InverseTransformPoint(epicentre);
v.z *= 0.25f;

float displacement = ShakeWaveform.DisplacementAt(v.magnitude, t)
                     * ShakeWaveform.IntensityFactor(quake.Intensity);

// 断層に直交する 1 方向（§A-7）。
float dirX = -Mathf.Sin(quake.AngleRadians);
float dirZ = Mathf.Cos(quake.AngleRadians);
added.x += displacement * dirZ;
added.z -= displacement * dirX;
```

  `e` は `(long)SimulationManager.instance.m_referenceFrameIndex - quake.ActivationFrame + ShakeWaveform.FrameOffset`。**`ActivationScheduled == false` の地震は飛ばす**（`m_activationFrame == 0` を引き算に使わない）。`ShakeWaveform.IsShaking(e, snapshot.Prefab.ActiveDuration)` が false なら飛ばす
- 全地震ぶんを合計してから `MaxAddedShake` でクランプし、`controller.m_cameraShake += added`
- 例外は 1 回だけ `Log.Error`、以後 `Log.Diag("EqShake", ...)`。**毎フレームの経路に `Log.Warn` を置かない**
- `LastAdded` に合計の大きさを持ち、`WriteDiagnostics` に出す（効いているかを実機で確認する唯一の手段）
- `Reset()` をレベルアンロードで呼び、キャッシュした `CameraController` / `Camera` を捨てる

- [ ] **Step 4: 設定と前提検証**

`ModSettings`:
```csharp
EarthquakeShakeBoost = new SavedBool("eqShakeBoost", FileName, true, true);
```
既定 ON にできるのは、強度 55 で追加分が厳密に 0 になり**バニラと完全に同じ挙動になる**から（Step 1 のテストが固定している）。`Mod.OnSettingsUI` の地震グループにチェックボックスと注記（`Strings.EarthquakeShakeBoostNote`）を足す。

`Assumptions` に 1 件（18 → **19**）:

| 名前 | 影響 |
|---|---|
| `CameraController.m_cameraShake and DisasterManager.m_disableCameraShake are public fields` | `camera shake cannot be scaled with intensity and distance, and the mod cannot honour the player's "disable camera shake" choice` |

`typeof(CameraController).GetField("m_cameraShake", BindingFlags.Public \| BindingFlags.Instance)` が非 null かつ `FieldType == typeof(UnityEngine.Vector3)`、`typeof(DisasterManager).GetField("m_disableCameraShake", ...)` が非 null かつ `FieldType == typeof(bool)`。

- [ ] **Step 5: `Strings` に 2 件足す（91 → 93）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeShakeBoost` | `Scale camera shake with intensity and distance` | `カメラの揺れを強度と距離に連動させる` |
| `EarthquakeShakeBoostNote` | `Vanilla ignores intensity here, so a 25.5 quake shakes exactly as much as a 5.5 one. At the vanilla default intensity (5.5) this option changes nothing.` | `バニラは揺れの振幅に強度を入れていないため、25.5 の地震も 5.5 と同じ揺れになります。強度が既定の 5.5 のときは、この設定を入れても挙動は一切変わりません。` |

- [ ] **Step 6: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **201 件緑**（193 + 8）、ロケール 93/93/93 一致。

- [ ] **Step 7: `docs/playtest-checklist.md` の②の節に足す**

```markdown
18. **強度 5.5 の地震で、この設定の ON / OFF によって揺れが変わらないこと。**
    ここが変わったら実装が間違っている（追加分は強度 55 でちょうど 0 になるはず）
19. 強度 25.5 の地震で、ON のとき明らかに強く揺れること。OFF に戻すと
    5.5 のときと同じ揺れになること
20. 強度 25.5 でも**画面が使い物にならないほど揺れないこと**（上限が効いていること）
21. **ゲームの設定でカメラの揺れを無効にすると、この機能も足さないこと**
22. 地震が終わった瞬間に追加分も止まること（`m_activeDuration` の窓の確認）。
    **プレハブ値が読めていない環境では追加分が一切出ないのが正しい**
23. 診断ダンプの `camera shake added` の行が、揺れている間だけ 0 でない値になること
```

- [ ] **Step 8: コミット**

```bash
git add -A && git commit -m "feat: バニラの揺れの式と、強度・距離に連動するカメラシェイク補正"
```

---

## Task 7: 地震計の既存効果を見せる

**波形より先にこれを出す。** 「地震計を建てると何が変わるか」がゲーム内のどこにも書かれていない。①で「なぜハザードマップが空なのか」を説明したのと同じ構図であり、同じ文体で書く。

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/WarningLeadTime.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/WarningLeadTimeTests.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeReader.cs`（カーソル地点のカバレッジも読む）
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeSnapshot.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakePanel.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeFeature.cs`（`WriteDiagnostics`）
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `EarthquakeReading.CoverageAtEpicentre`（Task 3）、`FeatureHost.FramesPerMinute`
- Produces:
  - `DisasterPlus.Core.Earthquake.WarningLeadTime` —
    `const int BaseFrames = 1755` / `const int BonusFrames = 6437` / `const int MaxCoverage = 100`
    `static int ClampCoverage(int coverage)` / `static int FramesFor(int coverage)` / `static float MinutesFor(int coverage, float framesPerMinute)`
  - `EarthquakeSnapshot` に追加 — `readonly int CursorCoverage` / `readonly bool CursorCoverageValid`

- [ ] **Step 1: 失敗するテストを書く（6 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/WarningLeadTimeTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class WarningLeadTimeTests
    {
        [Fact]
        public void NoCoverage_IsTheBaseLeadTime()
        {
            // IL 事実文書 §A-2: loc2 = Min(cov,100) * 6437 / 100 + 1755
            Assert.Equal(WarningLeadTime.BaseFrames, WarningLeadTime.FramesFor(0));
        }

        [Fact]
        public void FullCoverage_IsExactlyThreeInGameHours()
        {
            // 1755 + 6437 = 8192 = 65536 / 8 = ちょうど 3.0 ゲーム内時間。
            Assert.Equal(8192, WarningLeadTime.FramesFor(100));
        }

        [Fact]
        public void CoverageIsClampedAtOneHundred()
        {
            // Min(coverage, 100) はバニラ側にある。255 でも 100 と同じ。
            Assert.Equal(WarningLeadTime.FramesFor(100), WarningLeadTime.FramesFor(255));
            Assert.Equal(100, WarningLeadTime.ClampCoverage(255));
            Assert.Equal(0, WarningLeadTime.ClampCoverage(-5));
        }

        [Fact]
        public void UsesIntegerDivisionLikeTheGame()
        {
            // IL は div.un（整数除算）。6437 * 1 / 100 = 64（64.37 ではない）。
            Assert.Equal(WarningLeadTime.BaseFrames + 64, WarningLeadTime.FramesFor(1));
            Assert.Equal(WarningLeadTime.BaseFrames + 643, WarningLeadTime.FramesFor(10));
        }

        [Fact]
        public void LeadTimeIsMonotonic()
        {
            int prev = -1;
            for (int c = 0; c <= 120; c++)
            {
                int f = WarningLeadTime.FramesFor(c);
                Assert.True(f >= prev, "lead time decreased at coverage " + c);
                prev = f;
            }
        }

        [Fact]
        public void MinutesMatchTheKnownFigures()
        {
            // 1 ゲーム内分 = 65536 / 1440 ≒ 45.51 フレーム（火災旋風設計書 付録 A-4）。
            // 定数を直書きせず、呼び出し側から渡す。
            const float framesPerMinute = 65536f / 1440f;
            Assert.Equal(38.6f, WarningLeadTime.MinutesFor(0, framesPerMinute), 1);
            Assert.Equal(180.0f, WarningLeadTime.MinutesFor(100, framesPerMinute), 1);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認し、`WarningLeadTime` を実装する**

```csharp
namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 地震計のカバレッジが警報のリードタイムをどれだけ延ばすか。
    ///
    /// IL 事実文書 §A-2、EarthquakeAI.SimulationStep の Emerging 分岐:
    ///   CheckLocalResource(EarthquakeCoverage, m_targetPosition, out coverage)
    ///   coverage = Mathf.Min(coverage, 100)
    ///   lead     = coverage * 6437 / 100 + 1755      // 整数除算（div.un）
    ///   if (currentFrame + lead >= m_activationFrame) DetectDisaster(id, located: coverage != 0)
    ///
    /// カバレッジ 0 で 1755 フレーム（約 38.6 ゲーム内分）、100 で 8192 フレーム
    /// （= 65536/8 = **ちょうど 3.0 ゲーム内時間**）。
    ///
    /// **地震計の効果はもう 1 つある。** located が coverage != 0 で決まるので、
    /// 地震計が無いと地震はハザードマップに一切描かれない（§A-6 のゲート）。
    /// そちらは DisasterPhases.PaintsHazardMap が扱う。
    /// </summary>
    public static class WarningLeadTime
    {
        public const int BaseFrames = 1755;
        public const int BonusFrames = 6437;
        public const int MaxCoverage = 100;

        public static int ClampCoverage(int coverage)
        {
            if (coverage < 0) return 0;
            if (coverage > MaxCoverage) return MaxCoverage;
            return coverage;
        }

        public static int FramesFor(int coverage)
        {
            // 整数除算であることが重要。float にすると端数の扱いがゲームとずれる。
            return ClampCoverage(coverage) * BonusFrames / MaxCoverage + BaseFrames;
        }

        /// <summary>
        /// framesPerMinute は呼び出し側から渡す（Game 側は FeatureHost.FramesPerMinute）。
        /// 定数を直書きしない —— ③でこれを直書きして全ての持続時間が 4 倍ずれた前科がある。
        /// </summary>
        public static float MinutesFor(int coverage, float framesPerMinute)
        {
            if (framesPerMinute <= 0f || float.IsNaN(framesPerMinute)) return 0f;
            return FramesFor(coverage) / framesPerMinute;
        }
    }
}
```

- [ ] **Step 3: カーソル地点のカバレッジを読む**

`EarthquakeReader` に追加。sim スレッドで、`EarthquakeHub.TakeCursor` が返した座標について

```csharp
int coverage;
bool ok = ImmaterialResourceManager.instance.CheckLocalResource(
    ImmaterialResourceManager.Resource.EarthquakeCoverage, cursorPos, out coverage);
```

を呼び、`CursorCoverage` / `CursorCoverageValid` として snapshot に載せる（**新しい前提検証は不要** — Task 3 で `Resource.EarthquakeCoverage` と `CheckLocalResource` は既に検証済み）。座標が無効なら `CursorCoverageValid = false`。

- [ ] **Step 4: パネルに「地震計」セクションを足す**

全て**第 1 層**（`AddLayer1Row`）:

```
  [実測] 震央の地震計カバレッジ: 42 (上限 100)
  [実測] 警報のリードタイム: 100 分   （カバレッジ 0 なら 38.6 分、100 なら 180 分）
  [実測] カーソル地点のカバレッジ: 0
  地震計を建てると、警報が 38.6 分前から最大 3 時間前に延び、地震がハザードマップに
  描かれるようになります。
```

- カバレッジが 0 のときは `Strings.EarthquakeNoSensor` を出し、**上の 2 行の数値も出す**（0 という数値自体は本物で、「読めなかった」ではない。①の全ゼロのグリッドとは事情が違うので、ここでは数値を出してよい）。この違いをコメントに書くこと
- 「38.6 分 → 3 時間」の説明文（`EarthquakeSensorEffect`）は、地震が発生しているかどうかに関わらず**常に出す**。これは地震計というゲーム内建物の性質の説明であって、今この瞬間の観測値ではない
- 分の表示は `WarningLeadTime.MinutesFor(coverage, FeatureHost.FramesPerMinute)` を `F1` で

`WriteDiagnostics` にも同じ 3 つの数値と、`located` の有無を出す。

- [ ] **Step 5: `Strings` に 5 件足す（93 → 98）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeSensorSection` | `Earthquake sensors` | `地震計` |
| `EarthquakeCoverageAtEpicentre` | `Sensor coverage at the epicentre` | `震央の地震計カバレッジ` |
| `EarthquakeCoverageAtCursor` | `Sensor coverage at cursor` | `カーソル地点のカバレッジ` |
| `EarthquakeWarningLead` | `Warning lead time` | `警報のリードタイム` |
| `EarthquakeSensorEffect` | `An Earthquake Sensor extends the warning from 38.6 minutes to up to 3 hours, and makes the quake appear on the hazard map at all.` | `地震計を建てると、警報が 38.6 分前から最大 3 時間前まで延び、そもそも地震がハザードマップに描かれるようになります。` |

- [ ] **Step 6: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **207 件緑**（201 + 6）、ロケール 98/98/98 一致。

- [ ] **Step 7: `docs/playtest-checklist.md` の②の節に足す**

```markdown
24. 地震計を建てる前と後で、パネルの「震央の地震計カバレッジ」と
    「警報のリードタイム」が変わること。カバレッジ 0 で **38.6 分**、
    地震計の効果範囲の中心で **180 分**に近づくこと
25. **地震計を建ててから地震を起こすと、ハザードマップに実際に描かれること**
    （Task 4 の項目 11 の裏返し。ここで初めて「マップに表示」が意味を持つ）
26. 地震計の説明文が、地震が起きていないときにも出ていること
```

- [ ] **Step 8: コミット**

```bash
git add -A && git commit -m "feat: 地震計の警報リードタイムとカバレッジの可視化"
```

---

## Task 8: 波形グラフ（第 1 層の最後）

`EarthquakeSensorAI` は**時系列データを一切持たない**（§C-1、ABSENT）。持っているのは `m_detectionRange` だけで、やっていることは毎 tick 半径内に `EarthquakeCoverage` を撒くことだけ。**波形を出すなら、この MOD が自分で観測して自分で貯めるしかない。**

**このタスクが終われば第 1 層は完成し、機能として単独で出荷できる状態になる。**

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/WaveformBuffer.cs`
- Create: `src/DisasterPlus/Core/Earthquake/WaveformPlot.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/WaveformBufferTests.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/WaveformPlotTests.cs`
- Create: `src/DisasterPlus/Game/Earthquake/SeismographRecorder.cs`
- Create: `src/DisasterPlus/Game/Earthquake/WaveformView.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeSnapshot.cs` / `EarthquakeReader.cs` / `EarthquakeFeature.cs` / `EarthquakePanel.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `ShakeWaveform`（Task 6）、`EarthquakeReading`（Task 3）
- Produces:
  - `DisasterPlus.Core.Earthquake.WaveformBuffer` — `class`
    ctor `WaveformBuffer(int capacity)`、`int Capacity { get; }` / `int Count { get; }`
    `void Add(uint frame, float value)` / `void Clear()`
    `int CopyTo(uint[] frames, float[] values)`（古い順に詰め、実際に書いた件数を返す）
    `uint OldestFrame { get; }` / `uint NewestFrame { get; }` / `float PeakAbsolute { get; }`
  - `DisasterPlus.Core.Earthquake.WaveformPlot` —
    `static int[] Columns(uint[] frames, float[] values, int count, uint fromFrame, uint toFrame, int width, int height, float scale)`
    戻り値は長さ `width` の配列で、各要素は `[0, height]` の行数。サンプルが 1 個も無い列は `-1`
  - `DisasterPlus.Game.SeismographRecorder` — `static void Sample(EarthquakeSnapshot s, uint frame)`（**sim 専用**）/ `static void Reset()` / `static IList<SeismographTrace> Snapshot()`
  - `DisasterPlus.Game.SeismographTrace` — `class`（不変）`readonly ushort BuildingId` / `readonly Vec3 Position` / `readonly float DistanceToEpicentre` / `readonly uint[] Frames` / `readonly float[] Values` / `readonly int Count`
  - `DisasterPlus.Game.WaveformView` — `static bool Available { get; }` / `static void Build(UIPanel parent, string suffix, float x, float y, float w, float h)` / `static void Render(SeismographTrace trace)` / `static void Destroy()`

### 8.1 何を記録するのか（捏造しないための境界）

観測点は**地震計の建物の位置**。そこで `ShakeWaveform.DisplacementAt(震源からの距離, t)` を評価する。

**バニラ式との唯一の違いを UI にも書く。** §A-7 の `RenderInstance` は `camera.transform.InverseTransformPoint` を使うので、厳密には「**カメラ**からの距離」である。観測点版はそこを「**震源**からの距離」に置き換える。これは同じ式の別評価であって近似ではない（設計書 §3.5 が両方に書けと要求している）。`Strings.EarthquakeWaveformNote` がこれを言う。

**Task 6 の追加シェイクを混ぜない。** あれは第 1 層の「補正」だが、波形はバニラの式そのものを出すためのものなので、`IntensityFactor` を掛けない。

**セーブには残さない**（設計書 §3.5）。地震終了とレベルアンロードで捨てる。セーブ形式を増やす価値が無い。

### 8.2 サンプリングの単位はフレームであって tick ではない

`SimulationManager.SimulationStep` は 1 tick の中で `FinalSimulationSpeed` 回（ゲーム速度 1/2/3 で 1/3/9 回）ループするので、`m_currentFrameIndex` は tick ごとに 1/3/9 ずつ飛ぶ（火災旋風設計書 付録 A-4）。**サンプルの間隔はゲーム速度で変わる。**

したがって:
- `WaveformBuffer` は `(frame, value)` の**組**を記録する
- `WaveformPlot` は**フレーム範囲でバケットに割る**（配列添字で割らない）。これでゲーム速度に関わらず時間軸が正しくなる
- 表示窓は `PlotFrameWindow = 512`（＝包絡線 2 周期ぶん）
- バッファ容量は `Capacity = 512`（速度 1 なら 512 tick ぶん、速度 3 でも窓の 512 フレームは常に覆える）

### 8.3 観測点の探索コスト

地震計を探すには建物を走査する必要がある。**地震が始まったときに 1 回だけ**走査し、上限 `MaxObservationPoints = 4` 個までキャッシュする。毎 tick は走査しない。

- 走査は `BuildingManager.instance.m_buildings.m_buffer` の全スロット（49152）を 1 回。`FireWhirlSpawner` の prefab 走査と同じく「レベルロード 1 回ぶんのコスト」の範疇
- 条件: `(m_flags & Building.Flags.Created) != 0` かつ `info.m_buildingAI is EarthquakeSensorAI`
- **地震の途中で建てられた地震計は次の地震まで反映されない。** これは既知の制限としてチェックリストに書く（毎 tick 走査するより、動かないことが分かっている方がよい）
- 地震計が 0 個なら波形を出さず、`Strings.EarthquakeWaveformNeedsSensor` を出す。**カメラ位置や市の中心で代用しない** — 「地震計があるのに波形が見られない」という依頼への回答なので、地震計に紐づけないと意味が変わる

- [ ] **Step 1: 失敗するテストを書く（13 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/WaveformBufferTests.cs`（7 件）:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class WaveformBufferTests
    {
        [Fact]
        public void StartsEmpty()
        {
            var b = new WaveformBuffer(8);
            Assert.Equal(8, b.Capacity);
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void KeepsSamplesInChronologicalOrder()
        {
            var b = new WaveformBuffer(8);
            for (uint f = 0; f < 5; f++) b.Add(f, f * 0.5f);

            var frames = new uint[8];
            var values = new float[8];
            int n = b.CopyTo(frames, values);

            Assert.Equal(5, n);
            for (int i = 0; i < n; i++)
            {
                Assert.Equal((uint)i, frames[i]);
                Assert.Equal(i * 0.5f, values[i], 4);
            }
        }

        [Fact]
        public void WrapsAroundAndDropsTheOldest()
        {
            var b = new WaveformBuffer(4);
            for (uint f = 0; f < 10; f++) b.Add(f, f);

            var frames = new uint[4];
            var values = new float[4];
            int n = b.CopyTo(frames, values);

            Assert.Equal(4, n);
            Assert.Equal(4, b.Count);
            // 直近 4 件（6,7,8,9）が古い順に並ぶこと。
            Assert.Equal(6u, frames[0]);
            Assert.Equal(9u, frames[3]);
            Assert.Equal(6u, b.OldestFrame);
            Assert.Equal(9u, b.NewestFrame);
        }

        [Fact]
        public void CopyToSmallerArraysDoesNotOverflow()
        {
            var b = new WaveformBuffer(8);
            for (uint f = 0; f < 8; f++) b.Add(f, f);

            var frames = new uint[3];
            var values = new float[3];
            int n = b.CopyTo(frames, values);

            // 落ちないこと、書いた件数を正直に返すこと。
            Assert.Equal(3, n);
        }

        [Fact]
        public void ClearResetsEverything()
        {
            var b = new WaveformBuffer(4);
            b.Add(1u, 1f);
            b.Clear();
            Assert.Equal(0, b.Count);
            Assert.Equal(0f, b.PeakAbsolute, 4);
        }

        [Fact]
        public void PeakTracksTheLargestMagnitudeHeld()
        {
            var b = new WaveformBuffer(4);
            b.Add(0u, 0.2f);
            b.Add(1u, -0.9f);
            b.Add(2u, 0.5f);
            Assert.Equal(0.9f, b.PeakAbsolute, 4);
        }

        [Fact]
        public void RejectsNonsenseCapacity()
        {
            // 0 や負の容量でも落ちない。最低 1 件は持つ。
            var b = new WaveformBuffer(0);
            b.Add(1u, 1f);
            Assert.True(b.Capacity >= 1);
            Assert.Equal(1, b.Count);
        }
    }
}
```

`tests/DisasterPlus.Core.Tests/Earthquake/WaveformPlotTests.cs`（6 件）:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class WaveformPlotTests
    {
        private static void Fill(out uint[] frames, out float[] values, int n, uint step)
        {
            frames = new uint[n];
            values = new float[n];
            for (int i = 0; i < n; i++)
            {
                frames[i] = (uint)(i * step);
                values[i] = 0f;
            }
        }

        [Fact]
        public void ReturnsOneEntryPerColumn()
        {
            uint[] f; float[] v;
            Fill(out f, out v, 100, 1u);
            int[] cols = WaveformPlot.Columns(f, v, 100, 0u, 99u, width: 32, height: 20, scale: 1f);
            Assert.Equal(32, cols.Length);
        }

        [Fact]
        public void EmptyColumnsAreMarkedMinusOne()
        {
            // サンプルが届いていない列を「値 0」として描くと、揺れていないように見える。
            uint[] f; float[] v;
            Fill(out f, out v, 2, 1u);
            int[] cols = WaveformPlot.Columns(f, v, 2, 0u, 1000u, width: 16, height: 20, scale: 1f);

            int empty = 0;
            foreach (int c in cols) if (c < 0) empty++;
            Assert.True(empty > 10, "expected mostly empty columns, got " + empty);
        }

        [Fact]
        public void BucketsByFrameNotByIndex()
        {
            // ゲーム速度でサンプル間隔が変わっても、時間軸が伸び縮みしないこと。
            // 同じフレーム範囲を、密なサンプルと疎なサンプルで埋めて比べる。
            uint[] dense; float[] dv;
            Fill(out dense, out dv, 64, 1u);            // frame 0..63
            uint[] sparse; float[] sv;
            Fill(out sparse, out sv, 8, 9u);            // frame 0..63（9 フレーム刻み）

            int[] a = WaveformPlot.Columns(dense, dv, 64, 0u, 63u, 8, 10, 1f);
            int[] b = WaveformPlot.Columns(sparse, sv, 8, 0u, 63u, 8, 10, 1f);

            // どちらも全列が埋まる（＝時間軸の広がりが同じ）。
            foreach (int c in a) Assert.True(c >= 0);
            foreach (int c in b) Assert.True(c >= 0);
        }

        [Fact]
        public void ZeroValueMapsToTheMiddleRow()
        {
            uint[] f; float[] v;
            Fill(out f, out v, 32, 1u);
            int[] cols = WaveformPlot.Columns(f, v, 32, 0u, 31u, width: 8, height: 20, scale: 1f);
            foreach (int c in cols) Assert.Equal(10, c);
        }

        [Fact]
        public void ValuesAreClampedToTheHeight()
        {
            uint[] f = { 0u, 1u };
            float[] v = { 999f, -999f };
            int[] cols = WaveformPlot.Columns(f, v, 2, 0u, 1u, width: 2, height: 20, scale: 1f);
            foreach (int c in cols) Assert.InRange(c, 0, 20);
        }

        [Fact]
        public void GarbageInputDoesNotThrow()
        {
            Assert.Equal(4, WaveformPlot.Columns(null, null, 0, 0u, 10u, 4, 10, 1f).Length);
            uint[] f = { 0u };
            float[] v = { float.NaN };
            int[] cols = WaveformPlot.Columns(f, v, 1, 0u, 0u, 4, 10, 0f);
            Assert.Equal(4, cols.Length);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認し、`WaveformBuffer` と `WaveformPlot` を実装する**

要点だけ:
- `WaveformBuffer` は `uint[] _frames` / `float[] _values` と書き込み位置 `_head` / 件数 `_count` の素直なリングバッファ。`Capacity` は `capacity < 1` なら 1 に切り上げる。`PeakAbsolute` は `CopyTo` と同じ走査で毎回求め直す（`Add` で更新して古いサンプルが落ちた後も残る、という嘘をつかない）
- `WaveformPlot.Columns` は `(toFrame - fromFrame)` を `width` 等分し、各サンプルを `col = (int)((frame - fromFrame) * width / span)` で振り分ける。**同じ列に複数落ちたら絶対値が最大のものを採る**（間引きで揺れが小さく見えないように）。値 `v` は `row = height/2 - (int)(v * scale * height/2)` にして `[0, height]` にクランプ。サンプルが無い列は `-1`。`frames == null` / `count <= 0` / `span == 0` / `scale <= 0` でも長さ `width` の配列を返す

- [ ] **Step 3: 描画の可否を IL で確認する（推測で書かない）**

`docs/tools/ilload.ps1` で `ColossalManaged.dll` と `UnityEngine.dll` を読み、次を確認して**報告に書く**:

- `ColossalFramework.UI.UITextureSprite` が存在し、`texture` プロパティ（`UnityEngine.Texture`）に代入できるか
- `UnityEngine.Texture2D(int, int, TextureFormat, bool)` / `SetPixels32(Color32[])` / `Apply()` が Unity 5.6 に存在するか

**ASCII でグラフを組む案は採らない。** CS の UILabel フォントが等幅である保証が無く、列が揃わない。①でバー文字を ASCII に固定したのと同じ「フォントを前提にしない」規律の帰結として、**文字ではなくテクスチャで描く**。

**確認できなかった場合のフォールバック**（推測で代替 API を書かない）:
`WaveformView.Available` を false にし、パネルは波形の代わりに次の 2 行だけを出す。

```
[実測] 観測点 #1234（震源から 1,240 m）の最大振幅: 0.42 [####------]
波形の描画はこのビルドでは利用できません（Strings.EarthquakeWaveformUnavailable）
```

バー文字列は `SeismicScale.BarOf` を使い回す。**これは劣化であって嘘ではない**ので、そのまま出荷してよい。

- [ ] **Step 4: `SeismographRecorder` を作る（sim 専用）**

- 進行中（`Emerging|Active`）の地震が 1 つも無ければ、全バッファを `Clear()` して即 return
- 地震が新しく始まったら（`DisasterId` が変わったら）観測点を再走査し、バッファを全部捨てる
- 各観測点について毎 sim tick 1 サンプル:

```csharp
long e = (long)frame - quake.ActivationFrame + ShakeWaveform.FrameOffset;
if (!quake.ActivationScheduled) continue;
if (!ShakeWaveform.IsShaking(e, snapshot.Prefab.ActiveDuration)) continue;

// バニラ式の distance を「震源から観測点まで」に置き換えた版（設計書 §3.5）。
float value = ShakeWaveform.DisplacementAt(point.DistanceToEpicentre, e);
buffer.Add(frame, value);
```

  `t` に `m_referenceTimer` は足さない（あれは main スレッドの描画補間用で、sim スレッドから読むべき値ではない。フレーム単位の整数で十分）
- `Snapshot()` は不変の `SeismographTrace` を新しく組んで返す（配列を共有しない）。`EarthquakeReader` がこれを呼んで snapshot に載せる
- `Reset()` をレベルアンロードで呼ぶ

**呼び出し位置に注意。** `SeismographRecorder.Sample` は状態を進める（バッファに書く）ので、`EarthquakeFeature.OnSimulationTick` の**ポーズガードより下**に置く。ポーズ中に波形が伸び続けるのは嘘になる。

- [ ] **Step 5: `WaveformView` とパネル行を作る**

- Step 3 の確認結果に従って `UITextureSprite` + `Texture2D` を組む。幅 `PlotWidth = 320`、高さ `PlotHeight = 80`
- `Render(trace)` は main スレッドで `WaveformPlot.Columns` を呼び、各列に 1 本の縦線（中央行から `row` まで）を `Color32[]` に描いて `SetPixels32` → `Apply()`
- **`Texture2D` は `Destroy()` で必ず `Object.Destroy` する**（レベルアンロードで捨てないと都市をまたいでリークする）
- パネルには第 1 層のセクションとして:

```
  [実測] 地震計 #1234 の地動（震源から 1,240 m）
  <グラフ>
  ゲーム自身の揺れの式を、カメラではなく震源からの距離で観測点に対して評価したものです。
```

- 観測点が複数ある場合は**震源に最も近い 1 個**だけを描き、他の観測点の数を添える（グラフを 4 枚並べない）

- [ ] **Step 6: `Strings` に 4 件足す（98 → 102）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeWaveform` | `Ground motion at the sensor` | `地震計の地動` |
| `EarthquakeWaveformNeedsSensor` | `Build an Earthquake Sensor to record ground motion. The game itself keeps no ground-motion history at all, so Disaster + samples it at the sensor.` | `地動を記録するには地震計が必要です。ゲーム自身は地動の履歴を一切保持していないため、Disaster + が地震計の位置で観測しています。` |
| `EarthquakeWaveformNote` | `This is the game's own shake formula, evaluated at the sensor using the distance from the epicentre instead of the distance from the camera.` | `ゲーム自身の揺れの式を、カメラからの距離ではなく震源からの距離を使って観測点で評価したものです。` |
| `EarthquakeWaveformUnavailable` | `Waveform drawing is unavailable on this build; showing the peak amplitude instead.` | `このビルドでは波形の描画が利用できないため、最大振幅のみを表示しています。` |

- [ ] **Step 7: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **220 件緑**（207 + 13）、ロケール 102/102/102 一致。

- [ ] **Step 8: `docs/playtest-checklist.md` の②の節に足す**

```markdown
27. **地震計を建てずに地震を起こすと、波形の代わりに「地動を記録するには地震計が
    必要です」が出ること**（数値やグラフを出したら不具合）
28. 地震計を建ててから地震を起こすと、波形が描かれて**動くこと**。
    地震が終わると止まり、次の地震で作り直されること
29. **ゲーム速度を 1 → 3 に上げても、波形の時間軸が伸び縮みしないこと**
    （フレーム基準でバケットに割っているため。ここが崩れると
    `frameIndex % N` と同じ「速度に依存する処理」の罠に落ちている）
30. **ポーズ中に波形が伸びないこと**
31. 波形の下に「カメラからの距離ではなく震源からの距離」という注記が出ること
32. **2 つ目の都市をロードしても波形が出ること**（Texture2D の静的キャッシュが
    fake-null で死んでいないこと。③で実際に起きた失敗の形）
33. `UITextureSprite` が使えず最大振幅表示にフォールバックした場合は、
    その旨の 1 行が出ていること（黙って空欄にならないこと）
```

- [ ] **Step 9: コミット**

```bash
git add -A && git commit -m "feat: 地震計での地動の観測と波形表示（第1層の完成）"
```

---

> **ここで第 1 層は完成している。** 以降のタスクが難航しても、この時点で機能として出荷できる。
> 実機テストを挟むならここが最良の区切り —— 特にプレハブ 4 値（Task 3）と
> 建物ごとの余裕度の的中（Task 5）は、第 2 層の設計がその上に乗るため先に確かめたい。

---

# 第 2 層 — 足す（タスク 9〜11）

**ここから先はバニラに存在しない挙動である。** 全て既定 OFF、パネルでは `Strings.EarthquakeLayer2Header` の下に `[本MODの推定]` 付きで出す。

**既定 OFF にする理由（3 タスク共通）。** 設計書 §4.2 は長周期について明示的にそう書いているが、同じ理屈は津波連鎖にも当てはまる —— 新規の物理を既定で入れると、プレイヤーは**バニラの挙動が変わった理由を知る手段が無い**。「地震のあと勝手に津波が来る」は、原因が MOD だと気付けない形の変化そのものである。3 つとも設定で明示的に有効化させる。

---

## Task 9: 海中震源からの津波連鎖

**Files:**
- Create: `src/DisasterPlus/Game/Earthquake/TsunamiChain.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeFeature.cs`（ポーズガードの**下**で呼ぶ）
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeSnapshot.cs`（連鎖の状態を載せる）
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakePanel.cs`（第 2 層セクションの新設）
- Modify: `src/DisasterPlus/Game/ModSettings.cs`、`src/DisasterPlus/Game/Mod.cs`
- Modify: `src/DisasterPlus/Game/Diagnostics/Assumptions.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `EarthquakeReading` / `EarthquakeSnapshot`（Task 3）、`FeatureHost.FramesPerMinute`
- Produces:
  - `DisasterPlus.Game.TsunamiChain` — `static void Tick(EarthquakeSnapshot s, uint frame)`（**sim 専用**）/ `static void Reset()` / `static TsunamiChainState State { get; }`
  - `DisasterPlus.Game.TsunamiChainState` — `enum { Idle, Scheduled, Raised, NoSea, NoDlc, Failed }`
  - `ModSettings.EarthquakeTsunamiChain`（`SavedBool "eqTsunamiChain"`、既定 **false**）
  - `ModSettings.EarthquakeTsunamiDelayMinutes`（`SavedInt "eqTsunamiDelay"`、既定 **30**、範囲 5〜120）

### 9.1 実装できることと、できないこと（UI の文言がこれで決まる）

依頼文は「海中で地震を起こしてもプレート境界型の津波が来ない」だった。**「震源から波が広がる」は `TsunamiAI` では literally 不可能**である（§B-3）:

- `FindSea` は**マップ外周セルしか候補にしない**（1080 << 3 = 4320 個）
- `m_targetPosition` は「どの外周区間を選ぶか」のヒントにしかならず、開始時に**上書きされる**
- `m_angle` も区間の内向き法線から導出されて**上書きされる**

したがって②が実現するのは「**震源に最も近い海側の外周から津波が来る**」である。体験としては「沖の地震 → その方角から津波」でほぼ要求どおりだが、**UI の文言で「震源から波が広がります」と書いてはいけない。** `Strings.EarthquakeTsunamiFromShore` が正確な説明を担う。

### 9.2 起こし方（4 つの罠を全部踏まない手順）

`DisasterTool.<CreateDisaster>c__Iterator0.MoveNext` の IL_0071–01E8 をそのまま写す（§A-1）:

```csharp
var info = DisasterManager.FindDisasterInfo<TsunamiAI>();
if (info == null) { /* DLC 無し。何もせず理由を診断に出す（§B-5） */ return; }

ushort id;
// ★ 罠 2: 戻り値を必ず見る。false のとき id = 0 になり、
//   そのまま書き込むと**他人の災害スロットを書き潰す**（§E-1）。
if (!DisasterManager.instance.CreateDisaster(out id, info))
{
    Log.Diag("EqTsunamiFull", "CreateDisaster returned false (disaster buffer full?)");
    return;
}

var buffer = DisasterManager.instance.m_disasters.m_buffer;
var ai = info.m_disasterAI;

Vector3 pos = epicentre;
ai.ClampDisasterTarget(ref pos);            // public
buffer[id].m_targetPosition = pos;          // FindSea への「ヒント」にしかならない（§B-3）
buffer[id].m_angle = 0f;                    // どうせ上書きされる
buffer[id].m_intensity = tsunamiIntensity;
// ★ 罠 1: これが無いと StartDisaster は即 return し、
//   災害は Emerging のまま永久に固まる（§A-1 / §B-2）。
buffer[id].m_flags |= DisasterData.Flags.SelfTrigger;

ai.StartNow(id, ref buffer[id]);            // StartDisaster は protected（火災旋風 付録 A-1）
```

`tsunamiIntensity` は**地震の強度をそのまま使う**。波高は `m_height * 64 * i / 55`（§A-5）で `i` に線形なので、地震の強さに比例した波が立つ。クランプは `Math.Max((byte)10, quakeIntensity)`（強度 0 の津波は何も起きず、起こした意味が分からなくなる）。

### 9.3 `FindSea` が失敗したときの後始末（**着手前に IL で確定させること**）

`FindSea` は海側区間が 10 セル未満だと false を返し、`StartDisaster` はそこで return する（§B-2 / §B-3）。**内陸マップでは正常に何も起きない。これを失敗として扱わない。**

検出は可能: `StartNow` の直後に `buffer[id].m_waveIndex == 0` なら波は立っていない。

**しかし、そのまま放置してよいかは未確定である。** §B-4 は `WaterSimulation.SimulateWater` を **PARTIAL** としており、`TsunamiAI.StartDisaster` が早期 return したときに `m_activationFrame` が設定されるかどうかも読んでいない。設定されないなら、地震と同じく **Emerging で永久に固まった災害スロットが残る**。

- [ ] **Step 1: 後始末の方法を IL で確定させる（推測で書かない）**

`docs/tools/ilload.ps1` + `ildasm.ps1` で次を読み、**判明した事実を報告に書く**:

1. `TsunamiAI.StartDisaster` の全命令 —— `FindSea` が false のとき `m_activationFrame` に何か書かれるか
2. `TsunamiAI.IsStillEmerging` / `IsStillActive` / `IsStillClearing` —— `m_activationFrame == 0` で永久ループするか
3. `DisasterManager.ReleaseDisaster(ushort)` のアクセス修飾子（`SimulationStepImpl` が呼んでいる）
4. `DisasterData.m_waveIndex` の存在と型

結果に応じて後始末を選ぶ:

| 判明したこと | 採る手 |
|---|---|
| 早期 return でも位相が自然に進む | 何もしない（ログのみ） |
| 固まる & `ReleaseDisaster` が public | **`ReleaseDisaster(id)`**（自分が作って一度も開始しなかったスロットだけを解放する） |
| 固まる & `ReleaseDisaster` が非 public | `DisasterAI.DeactivateNow(id, ref buffer[id])`（public、火災旋風 付録 A-1）で Clearing へ落とす |

**いずれの場合も `m_waveIndex == 0` の判定は必ず行い**、状態を `TsunamiChainState.NoSea` にして UI に理由を出す。

- [ ] **Step 2: `TsunamiChain` を実装する（sim 専用）**

- 監視対象は「今回の地震」1 個だけ（同時に複数の地震から津波を出さない。上限 256 の災害スロットを②が食い潰す形を作らない）
- 地震が `Emerging → Active` に遷移した瞬間を検出する（前回 tick の位相を覚えておく）
- 震源が水中か: `TerrainManager.instance.HasWater(VectorUtils.XZ(epicentre))`。**シグネチャと public 性は Step 3 の前提検証で確認する**（設計書 付録が「存在・シグネチャ・public 性を IL で確認すること」と指定している項目）
- 水中なら `dueFrame = activationFrame + (uint)(ModSettings.EarthquakeTsunamiDelayMinutes.value * FeatureHost.FramesPerMinute)` を記録して `Scheduled`
- `frame >= dueFrame` になったら 9.2 の手順で起こす
- 地震が消えたら（`Finished` またはバッファから消えたら）`Reset()`
- 例外は 1 回だけ `Log.Error`、以後 `Log.Diag("EqTsunami", ...)`
- `Reset()` をレベルアンロードで呼ぶ。**都市をまたいで予約が残らないこと**

`EarthquakeFeature.OnSimulationTick` の**ポーズガードより下**で呼ぶ:

```csharp
if (deltaMinutes <= 0f) return;
if (ModSettings.EarthquakeTsunamiChain.value) TsunamiChain.Tick(snapshot, frameIndex);
```

- [ ] **Step 3: `Assumptions` に 2 件足す（19 → 21）**

| 名前 | 影響 |
|---|---|
| `TsunamiAI disaster prefab is available` | `the tsunami chain cannot run (this also FAILs when the Natural Disasters DLC is not owned, which is expected)` |
| `TerrainManager.HasWater is resolvable and DisasterData exposes m_waveIndex` | `the mod cannot tell whether the epicentre is under water, nor whether a wave was actually raised` |

`FindDisasterInfo<TsunamiAI>()` は副作用の無い走査なので、`FireWhirlSpawner.HasTornadoPrefab()` と同じく**専用の純粋な問い合わせ**として書く（キャッシュを書き換える関数に委譲しない）。

- [ ] **Step 4: 設定と第 2 層セクション**

`ModSettings`:
```csharp
EarthquakeTsunamiChain        = new SavedBool("eqTsunamiChain", FileName, false, true);
EarthquakeTsunamiDelayMinutes = new SavedInt("eqTsunamiDelay", FileName, 30, true);
```

`Mod.OnSettingsUI` の地震グループに、チェックボックスと `AddSlider(Strings.EarthquakeTsunamiDelay, 5f, 120f, 5f, ...)` を足す。**DLC が無い環境では出さない**（`ModCompat.NaturalDisastersOwned` で囲む）。

パネルに**第 2 層セクションを新設**する（このタスクが `AddSectionHeader(Strings.EarthquakeLayer2Header)` と `AddLayer2Row` を初めて使う）。設定が OFF ならセクションごと出さない。

```
  ── Disaster + が足した挙動（バニラにはありません） ──
  [本MODの推定] 津波: 30 分後に到達予定
  波は震源からではなく、震源に最も近い海から到達します。
```

状態別の文言:

| `State` | 表示 |
|---|---|
| `Idle` | 行を出さない |
| `Scheduled` | 残り時間（`(dueFrame - frame) / FeatureHost.FramesPerMinute` 分）＋ `EarthquakeTsunamiFromShore` |
| `Raised` | 「津波が発生しました」＋ `EarthquakeTsunamiFromShore` |
| `NoSea` | `EarthquakeTsunamiNoSea`（「このマップには津波が届く海がないため、津波は起きませんでした」）。**これは失敗ではない** |
| `NoDlc` | `EarthquakeTsunamiNeedsDlc` |
| `Failed` | 汎用の理由（災害スロットが満杯など）を診断に出し、パネルには行を出さない |

- [ ] **Step 5: `Strings` に 6 件足す（102 → 108）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeTsunamiChain` | `Raise a tsunami after an undersea earthquake` | `海中で地震が起きたら津波を発生させる` |
| `EarthquakeTsunamiDelay` | `Tsunami delay (in-game minutes)` | `津波までの遅延 (ゲーム内分)` |
| `EarthquakeTsunamiPending` | `Tsunami expected in` | `津波の到達予定` |
| `EarthquakeTsunamiRaised` | `Tsunami raised` | `津波が発生しました` |
| `EarthquakeTsunamiFromShore` | `The wave arrives from the sea nearest the epicentre, not from the epicentre itself. The game can only start a tsunami at the map edge.` | `波は震源そのものからではなく、震源に最も近い海から到達します。ゲームは津波をマップ外周からしか起こせません。` |
| `EarthquakeTsunamiNoSea` | `No sea close enough to this map edge, so no tsunami was raised. This is normal on an inland map.` | `十分な広さの海がマップ外周に無いため、津波は起きませんでした。内陸マップでは正常な結果です。` |

`EarthquakeTsunamiNeedsDlc` は `Strings.EarthquakeNeedsDlc`（Task 3）を使い回す。新しいキーを増やさない。

- [ ] **Step 6: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **220 件緑**（このタスクは Core にテストを足さない。全て Cities API に触る処理のため）、ロケール 108/108/108 一致。

- [ ] **Step 7: `docs/playtest-checklist.md` の②の節に足す**

```markdown
34. **既定で OFF になっていること**（設定を触っていない状態で、海中に地震を起こしても
    津波が来ないこと）
35. ON にして**海の上に**地震を起こす。設定した遅延のあと津波が来ること。
    **波はマップ外周から来る**（震源からではない）。パネルにその旨の説明が出ていること
36. **内陸マップで ON にして地震を起こす。何も起きず、パネルに
    「十分な広さの海が…正常な結果です」と出ること。**エラーにならないこと
37. **災害スロットが埋まらないこと**: 海中地震を 5 回連続で起こしてから、
    災害パネルから普通の災害を出せること（`CreateDisaster` の戻り値を無視して
    スロット 0 を書き潰していたら、ここで他の災害が壊れる）
38. **津波が Emerging のまま固まらないこと**（`SelfTrigger` の確認）。
    災害情報パネルで津波が進行し、最後に消えること
39. **ポーズ中に予約が進まないこと**
40. **2 つ目の都市をロードしたとき、前の都市の予約が残っていないこと**
41. 第 2 層のセクションが第 1 層より下に出て、行に `[本MODの推定]` が付いていること。
    設定を OFF に戻すとセクションごと消えること
```

- [ ] **Step 8: コミット**

```bash
git add -A && git commit -m "feat: 海中震源からの津波連鎖（第2層）"
```

---

## Task 10: 長周期地震動（第 2 層）

**これは可視化ではなく新規の物理である。** バニラの揺れは 0.63 / 0.17 rad/frame の正弦 2 本だけで、低周波成分は 256 フレーム周期の**包絡線**（振幅の脈動）であって地震動ではない（§A-7）。建物の高さは**揺れにも被害にも一切入っていない**（`DestroyBuildings` は `m_position` の距離しか見ない）。

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/LongPeriodResponse.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/LongPeriodResponseTests.cs`
- Create: `src/DisasterPlus/Game/Earthquake/LongPeriodDamage.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeFeature.cs` / `EarthquakeSnapshot.cs` / `EarthquakePanel.cs`
- Modify: `src/DisasterPlus/Game/ModSettings.cs`、`src/DisasterPlus/Game/Mod.cs`
- Modify: `src/DisasterPlus/Game/Diagnostics/Assumptions.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `SeismicIntensity`（Task 2）、`DeterministicRandom`（既存 Core）、`FeatureHost.FramesPerMinute`
- Produces:
  - `DisasterPlus.Core.Earthquake.LongPeriodResponse` —
    `const float WaveRateRadPerFrame = 0.03f` / `const float PeriodFramesPerMetre = 4f` / `const float ResonanceWidthFrames = 60f` / `const float RangeFactor = 2f` / `const float MinHeightMetres = 20f` / `const float MaxExtraChance = 0.25f`
    `static float WavePeriodFrames { get; }`
    `static float BuildingPeriodFrames(float heightMetres)`
    `static float Resonance(float buildingPeriodFrames)`
    `static float RangeOf(byte intensity)`
    `static float ExtraCollapseChance(float heightMetres, float distance, byte intensity, float strength)`
  - `DisasterPlus.Game.LongPeriodDamage` — `static void Apply(EarthquakeSnapshot s, uint frame, float deltaMinutes)`（**sim 専用**）/ `static void Reset()` ＋ 診断カウンタ（`Passes` / `LastScanned` / `LastSelected` / `LastAttempted` / `LastRefused` / `LastCollapsed` / `TotalCollapsed`）
  - `DisasterPlus.Game.BuildingHeight` — `static float MetresOf(ushort id, ref Building b)`
  - `ModSettings.EarthquakeLongPeriod`（`SavedBool "eqLongPeriod"`、既定 **false**）
  - `ModSettings.EarthquakeLongPeriodStrength`（`SavedInt "eqLongPeriodStrength"`、既定 **3**、範囲 0〜10）

### 10.1 モデル（全て本 MOD が発明した数字。定数名でそれが分かるようにする）

```
波の周期      WavePeriodFrames = 2π / 0.03 ≒ 209 フレーム
              （バニラの 0.63 / 0.17 rad/frame ＝ 周期 ≒10 / ≒37 より十分低い）
建物の固有周期 BuildingPeriodFrames(h) = h * 4
              （高さ 52 m の建物が波と共振する。実在の T ≒ 0.02h 秒の関係を
                「フレーム」という単位に置き換えただけで、実在の値ではない）
共振          Resonance(T) = 1 / (1 + ((T - WavePeriodFrames) / 60)²)     ∈ (0, 1]
到達範囲      RangeOf(i) = SeismicIntensity.RadiusOf(i) * 2
              （短周期より遠くまで届く、を表現する）
追加倒壊確率  ExtraCollapseChance = Resonance × (1 - d/Range) × (strength/10) × 0.25
```

- 高さが `MinHeightMetres = 20f` 未満の建物は対象外（低層は長周期の影響を受けない、という前提を明示する）
- `MaxExtraChance = 0.25f` が上限。**バニラの全体円盤が 0.02 なのに対し、これは最大 0.25 と 1 桁大きい。** 意図的だが、だからこそ既定 OFF で、`strength` スライダーで抑えられる必要がある
- **これらは物理定数ではない。** 実在の建築や地震学の値を名乗らないこと。doc コメントに明記する

### 10.2 被害の与え方（NDR と正面衝突しないための線）

**`DisasterHelpers` を経由しない**（§E-2）。NDR は `DisasterHelpers.DestroyBuildings` を Prefix で完全置換しており、`probability == 0.02f` を「バニラの地震だ」と嗅ぎ分ける。②が `DisasterHelpers` を呼ばない限り、NDR のパッチ面 2 つを**完全に迂回できる**。

```csharp
// 火災旋風で確立した手順（FireWhirlDamage.Ignite / CanBurn を手本にする）。
var groupId = InstanceID.Empty;
groupId.Disaster = quake.DisasterId;
var group = InstanceManager.instance.GetGroup(groupId);

// dry-run で「バニラが設計上受け付けるか」を実物に訊く。条件を自前で写さない。
bool acceptable = ai.CollapseBuilding(id, ref buildings[id], group,
                                      testOnly: true, demolish: false, burnAmount: 0);
if (acceptable) attempted++; else refused++;

// dry-run が false でも本番は呼ぶ（他 MOD のパッチで dry-run と本番が食い違う場合に
// 本物の倒壊を握り潰さないため。FireWhirlDamage の同じ判断を踏襲する）。
if (ai.CollapseBuilding(id, ref buildings[id], group, false, false, burnAmount)) collapsed++;
```

`burnAmount` は `0`（長周期は「揺すられて潰れる」であって焼損ではない）。既に倒壊・炎上している建物は `CollapseBuilding` が弾くので、二重被害にはならない。

**選定に使う乱数は `DeterministicRandom`**（`VanillaRandomizer` ではない）。これは**この MOD が発明した判断**であり、バニラが引く値と一致する必要は無い —— むしろ一致させると、第 1 層で先読みしたしきい値と混ざって、どちらの層の結論なのかが追えなくなる。Task 1 で定めた判別の規則がここで効く。

```csharp
float roll = DeterministicRandom.Unit((uint)quake.DisasterId, (uint)buildingId);
if (roll < chance) { /* 倒壊させる */ }
```

（`frame` を混ぜないこと。混ぜると同じ建物が毎回抽選し直されて、走査のたびに壊れる建物が増え続ける。）

### 10.3 走査

- バニラの `DestroyBuildings` と同じグリッド走査（セル 64、オフセット 135、`[0,269]` クランプ）。`FireWhirlDamage.CollectNearby` をそのまま手本にする
- 範囲は `LongPeriodResponse.RangeOf(intensity)`（＝ 全体円盤の 2 倍）
- **周期は経過ゲーム内時間の積算で決める**。`frameIndex % N` にしない（ゲーム速度で 1/3/9 倍にまばらになる。火災旋風設計書 付録 A-4）。間隔はバニラの災害 1 個あたりの `SimulationStep` 間隔と揃えて **256 フレームぶんの分数**（`256f / FeatureHost.FramesPerMinute`）
- **sim スレッド専用。** `EarthquakeFeature.OnSimulationTick` のポーズガードより下で呼ぶ
- **診断カウンタを必ず持つ**（③で「延焼が動いているか診断から一切見えなかった」失敗を繰り返さない）: `scanned` / `selected` / `attempted` / `refused` / `collapsed`。`Log.Diag(LogChannel.Earthquake, "longPeriod", ...)` に毎回出す（**倒壊 0 のときも出す** —— 「壊れていない」と「近くに対象が無い」がログで区別できなくなる）

- [ ] **Step 1: 建物の高さのフィールド名を IL で確定させる（推測で書かない）**

設計書 付録が「建物高さのフィールド（`m_generatedInfo` 配下の実名）を IL で確定させてから着手する」と指定している項目。事実文書は「`Building.Info.m_generatedInfo` / `Building.m_height` 系」としか書いていない。

`docs/tools/ilload.ps1` で `Assembly-CSharp.dll` を読み、**判明した事実を報告に書く**:

- `Building.m_height` の存在と型（`Byte` か）と、**単位**（そのままメートルか、何かの係数が要るか）。バニラ内でこのフィールドを読んでいるメソッドを探し、そこでの使われ方から単位を決める
- `BuildingInfo.m_generatedInfo` の型と、その中の高さらしきフィールド（`m_max` / `m_size` / `m_heights` 等）の実名と型

`BuildingHeight.MetresOf(ushort id, ref Building b)` にその 1 経路だけを実装する。**両方読めなかった場合は 0 を返し、`LongPeriodDamage` は何もしない**（高さが分からないのに「高層ほど壊れる」を適用したら、それはこの MOD が最も嫌う形の嘘になる）。その場合は `FeatureHost.NoteDegraded` で自己申告し、パネルに理由を出す。

- [ ] **Step 2: `LongPeriodResponse` の失敗するテストを書く（7 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/LongPeriodResponseTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class LongPeriodResponseTests
    {
        [Fact]
        public void WavePeriodIsWellBelowTheVanillaComponents()
        {
            // バニラは 0.63 rad/frame（周期 ≒10）と 0.17（≒37）の 2 本だけ。
            // 「長周期」を名乗る以上、それより十分長いこと自体をテストで固定する。
            double fast = 2.0 * System.Math.PI / ShakeWaveform.FastRate;
            double slow = 2.0 * System.Math.PI / ShakeWaveform.SlowRate;
            Assert.True(LongPeriodResponse.WavePeriodFrames > slow * 4.0,
                "the long-period component must be far slower than vanilla's slowest");
            Assert.True(LongPeriodResponse.WavePeriodFrames > fast * 4.0);
        }

        [Fact]
        public void TallerBuildingsHaveLongerPeriods()
        {
            Assert.True(LongPeriodResponse.BuildingPeriodFrames(80f)
                        > LongPeriodResponse.BuildingPeriodFrames(30f));
            Assert.Equal(0f, LongPeriodResponse.BuildingPeriodFrames(0f), 4);
        }

        [Fact]
        public void ResonancePeaksAtTheWavePeriod()
        {
            float peak = LongPeriodResponse.Resonance(LongPeriodResponse.WavePeriodFrames);
            Assert.Equal(1f, peak, 4);
            Assert.True(LongPeriodResponse.Resonance(LongPeriodResponse.WavePeriodFrames + 200f) < peak);
            Assert.True(LongPeriodResponse.Resonance(0f) < peak);
        }

        [Fact]
        public void ResonanceIsAlwaysInsideZeroToOne()
        {
            for (float t = 0f; t < 2000f; t += 7f)
            {
                Assert.InRange(LongPeriodResponse.Resonance(t), 0f, 1f);
            }
        }

        [Fact]
        public void ReachesFurtherThanTheVanillaDisc()
        {
            Assert.True(LongPeriodResponse.RangeOf(100) > SeismicIntensity.RadiusOf(100));
        }

        [Fact]
        public void LowBuildingsAndFarBuildingsGetNothing()
        {
            // 低層は対象外。範囲外も 0。ここが 0 にならないと「全部壊れる」になる。
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(
                LongPeriodResponse.MinHeightMetres - 1f, 100f, 100, 10f), 5);
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(
                60f, LongPeriodResponse.RangeOf(100) + 1f, 100, 10f), 5);
            // 強さ 0 は完全に無効（設定で切れることを保証する）。
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(60f, 100f, 100, 0f), 5);
        }

        [Fact]
        public void ChanceIsCappedAndNeverNegative()
        {
            for (float h = 0f; h < 300f; h += 3f)
            {
                for (float d = 0f; d < 9000f; d += 250f)
                {
                    float c = LongPeriodResponse.ExtraCollapseChance(h, d, 255, 10f);
                    Assert.InRange(c, 0f, LongPeriodResponse.MaxExtraChance);
                }
            }
        }
    }
}
```

- [ ] **Step 3: 失敗を確認し、`LongPeriodResponse` を実装する**

クラス doc の冒頭に**必ず**次を書く:

```csharp
/// <summary>
/// 長周期地震動の応答モデル。**これは本 MOD が発明した物理であって、
/// バニラにも現実の地震学にも対応する値は無い。**
///
/// バニラの揺れは 0.63 / 0.17 rad/frame の正弦 2 本だけで、低周波成分は
/// 256 フレーム周期の包絡線（振幅の脈動）であって地震動ではない
/// （IL 事実文書 §A-7）。建物の高さは揺れにも被害にも一切入っていない。
/// つまりここにあるのは「可視化」ではなく「追加」である。
///
/// **単位は全て sim フレーム。** 秒でも実在の周期でもない。実在の
/// T ≒ 0.02h [秒] という関係の形だけを借りて、フレームという単位に
/// 置き換えている。したがって PeriodFramesPerMetre = 4 は物理定数ではなく、
/// 「高さ 52 m 前後の建物が最も揺すられる」という遊びの調整値である。
///
/// 呼び出し側はこの値を必ず第 2 層として表示すること（Strings.SourceModel）。
/// </summary>
```

- [ ] **Step 4: `LongPeriodDamage` を実装する**

10.2 / 10.3 のとおり。**`FireWhirlDamage` を隣に開いて書くこと**（走査・dry-run・診断カウンタ・空振り検出の形が全てそこにある）。

- [ ] **Step 5: `Assumptions` に 1 件足す（21 → 22）**

| 名前 | 影響 |
|---|---|
| `BuildingAI.CollapseBuilding is resolvable and building height can be read` | `long-period damage cannot be applied; the feature disables itself rather than guessing a height` |

```csharp
typeof(BuildingAI).GetMethod("CollapseBuilding",
    BindingFlags.Public | BindingFlags.Instance, null,
    new Type[] { typeof(ushort), typeof(Building).MakeByRefType(),
                 typeof(InstanceManager.Group), typeof(bool), typeof(bool), typeof(int) },
    null) != null
```
＋ Step 1 で確定した高さフィールドの解決可否（`HasField` を使う）。

- [ ] **Step 6: 設定とパネル**

`ModSettings`:
```csharp
EarthquakeLongPeriod         = new SavedBool("eqLongPeriod", FileName, false, true);
EarthquakeLongPeriodStrength = new SavedInt("eqLongPeriodStrength", FileName, 3, true);
```

`Mod.OnSettingsUI` にチェックボックス（ラベルに「バニラには無い被害を足します」と書く）とスライダー、および `Strings.EarthquakeLongPeriodNote`。

パネルの第 2 層セクションに `AddLayer2Row` で:

```
  [本MODの推定] 長周期地震動: この建物の固有周期 240 フレーム / 共振 0.85 / 追加倒壊リスク 6.4%
  バニラは建物の高さを一切見ていません。これは Disaster + が発明したモデルです。
```

カーソル下の建物について出す（`BuildingProbe` が高さと応答も一緒に載せる）。設定が OFF なら行を出さない。高さが読めない環境では理由の 1 行だけを出す。

- [ ] **Step 7: `Strings` に 6 件足す（108 → 114）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeLongPeriod` | `Long-period ground motion` | `長周期地震動` |
| `EarthquakeLongPeriodEnabled` | `Enable long-period ground motion (adds damage vanilla never does)` | `長周期地震動を有効にする（バニラには無い被害を足します）` |
| `EarthquakeLongPeriodStrength` | `Long-period strength (0 = off)` | `長周期地震動の強さ (0 で無効)` |
| `EarthquakeLongPeriodNote` | `Vanilla ignores building height entirely, both in the shaking and in the damage. This is a model Disaster + invented, not something the game computes.` | `バニラは揺れにも被害にも建物の高さを一切使っていません。これはゲームが計算している値ではなく、Disaster + が発明したモデルです。` |
| `EarthquakeBuildingHeight` | `Building height` | `建物の高さ` |
| `EarthquakeResonance` | `Resonance` | `共振` |

追加倒壊リスクは既存の `EarthquakeLongPeriod` ラベルの行にまとめて出し、新しいキーを増やさない。

- [ ] **Step 8: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **227 件緑**（220 + 7）、ロケール 114/114/114 一致。

- [ ] **Step 9: `docs/playtest-checklist.md` の②の節に足す**

```markdown
42. **既定で OFF になっていること。** OFF のまま地震を起こしたとき、
    バニラと全く同じ被害になること（追加で倒れる建物が 1 棟も無いこと）
43. ON にして強度の高い地震を起こす。**高層ビルが、同じ距離の低層より
    多く倒れること**。`強さ` を 0 にすると追加被害が止まること
44. **バニラの影響半径の外でも高層が倒れること**（到達範囲が 2 倍になっている確認）
45. 診断ダンプの `long period` 行で `scanned` / `selected` / `attempted` /
    `refused` / `collapsed` が動くこと。**倒壊 0 のときも行が出ること**
    （「壊れている」と「対象が無い」を区別するため）
46. **NDR を併用し、NDR の破壊設定を OFF にしても、長周期の追加被害が動くこと**
    （`DisasterHelpers` を経由していないことの確認。③の同じ項目の地震版）
47. 建物の高さが読めない環境では、追加被害が一切起きず、パネルにその理由が出ること
    （**推測した高さで被害を出さないこと**）
48. **ポーズ中に被害が進まないこと**
```

- [ ] **Step 10: コミット**

```bash
git add -A && git commit -m "feat: 長周期地震動による高層建物への追加被害（第2層）"
```

---

## Task 11: 時間帯係数（第 2 層）

**Files:**
- Create: `src/DisasterPlus/Core/Earthquake/TimeOfDayFactor.cs`
- Create: `tests/DisasterPlus.Core.Tests/Earthquake/TimeOfDayFactorTests.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/LongPeriodDamage.cs`（係数を掛ける）
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakePanel.cs`
- Modify: `src/DisasterPlus/Game/Earthquake/EarthquakeFeature.cs`（`WriteDiagnostics`）
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`、`Locales/ja.txt`、`Locales/en.txt`
- Modify: `docs/playtest-checklist.md`

**Interfaces:**
- Consumes: `EarthquakeSnapshot.HourOfDay` / `DayNightEnabled`（Task 3 で既に読んでいる）、`LongPeriodResponse`（Task 10）
- Produces:
  - `DisasterPlus.Core.Earthquake.TimeOfDayFactor` —
    `const float SunriseHour = 5f` / `const float SunsetHour = 20f` / `const float DayFactor = 1f` / `const float NightFactor = 1.15f` / `const float RampHours = 1f`
    `static float Of(float hour)` / `static bool IsNight(float hour)`

### 11.1 何に掛けるのか（境界を絶対に越えない）

**第 2 層の追加被害にだけ掛ける。** `LongPeriodDamage` が `LongPeriodResponse.ExtraCollapseChance(...)` に掛けるところが唯一の適用点であり、**バニラの被害には一切触れない**。第 1 層の表示（震度・余裕度・警報・波形）にも掛けない。

係数は控えめにする。「夜の方が被害が大きい」は**バニラのどこにも根拠が無い**（設計書 §4.3）。`NightFactor = 1.15f` の 15 % は、体感できるが第 1 層の数字を疑わせない程度として選んだ値であり、物理的な根拠は無い。doc コメントにそう書く。

境界は 1 時間かけて滑らかに変える（`RampHours = 1f`）。段差にすると、日の出の 1 フレームで追加被害が 15 % 跳ねる。

**時間帯係数だけを単独で有効化する設定は作らない。** 長周期が OFF なら適用先が無いので、設定を増やすとそれ自体が死んだスイッチになる。長周期の設定に従属させ、パネルには常に現在の係数を表示する。

### 11.2 日夜サイクル OFF のときに黙らない（§F-1 の罠の最終処理）

`m_enableDayNight == false` かつゲームモードのとき、`m_dayTimeOffsetFrames` が毎 sim フレーム再設定され、**時刻は永久に 12.0 に固定される**。したがって時間帯係数はその設定では黙って定数（`DayFactor`）になる。

**これを機能の無効化として扱わない。無効化を隠すのはこの MOD が最も嫌う形の出力である。**

- Task 3 の `EarthquakeReader` は既に `DayNightEnabled` を読んで snapshot に載せている
- パネルの時間帯の行に、`DayNightEnabled == false` のとき `Strings.EarthquakeNoDayNight` を併記する
- `WriteDiagnostics` の `sim clock` 行にも同じ事実を出す（Task 3 で既に実装済み）

> **`Assumptions` に「`m_enableDayNight` が true か」を検証項目として足さない。**
> 設計書 §6 の表はこれを検証項目として挙げているが、日夜サイクルを切るのは
> **プレイヤーの正当な設定**であって前提の破れではない。ここを FAIL にすると、
> 何も壊れていない環境の設定画面に「一部機能が利用できません」が出続ける
> —— 誤検知を消すための層が誤検知を出す、という①で既に 1 度直した形
> （`Assumptions` の I5 の指摘）に戻ることになる。**フィールドが読めるか**の
> 検証は Task 3 で済ませてあり、**値がどうなっているか**はパネルと診断で伝える。
> これが設計書の意図（「黙って無効にしない」）を、誤検知を作らずに満たす形である。

- [ ] **Step 1: 失敗するテストを書く（7 件）**

`tests/DisasterPlus.Core.Tests/Earthquake/TimeOfDayFactorTests.cs`:

```csharp
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class TimeOfDayFactorTests
    {
        [Fact]
        public void MiddayIsTheDayFactor()
        {
            // 日夜サイクル OFF のとき時刻は永久に 12.0 に固定される（IL 事実文書 §F-1）。
            // その設定で係数がちょうど 1.0 になることを固定しておくと、
            // 「切っている人には何も足されない」が保証できる。
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(12f), 5);
        }

        [Fact]
        public void DeepNightIsTheNightFactor()
        {
            Assert.Equal(TimeOfDayFactor.NightFactor, TimeOfDayFactor.Of(2f), 5);
            Assert.Equal(TimeOfDayFactor.NightFactor, TimeOfDayFactor.Of(23f), 5);
        }

        [Fact]
        public void SunriseAndSunsetMatchTheGameConstants()
        {
            // IL 実測: SUNRISE_HOUR = 5、SUNSET_HOUR = 20。
            Assert.Equal(5f, TimeOfDayFactor.SunriseHour, 5);
            Assert.Equal(20f, TimeOfDayFactor.SunsetHour, 5);
            Assert.True(TimeOfDayFactor.IsNight(4.9f));
            Assert.False(TimeOfDayFactor.IsNight(5.1f));
            Assert.False(TimeOfDayFactor.IsNight(19.9f));
            Assert.True(TimeOfDayFactor.IsNight(20.1f));
        }

        [Fact]
        public void TheBoundaryIsRampedNotStepped()
        {
            // 段差にすると、日の出の 1 フレームで追加被害が跳ねる。
            float justBefore = TimeOfDayFactor.Of(TimeOfDayFactor.SunriseHour - 0.01f);
            float justAfter = TimeOfDayFactor.Of(TimeOfDayFactor.SunriseHour + 0.01f);
            Assert.True(System.Math.Abs(justBefore - justAfter) < 0.01f,
                "the sunrise boundary must not be a step");
        }

        [Fact]
        public void IsAlwaysBetweenTheTwoFactors()
        {
            for (float h = -48f; h < 72f; h += 0.05f)
            {
                float f = TimeOfDayFactor.Of(h);
                Assert.InRange(f, TimeOfDayFactor.DayFactor, TimeOfDayFactor.NightFactor);
            }
        }

        [Fact]
        public void HoursOutsideTheDayAreFolded()
        {
            Assert.Equal(TimeOfDayFactor.Of(2f), TimeOfDayFactor.Of(26f), 5);
            Assert.Equal(TimeOfDayFactor.Of(2f), TimeOfDayFactor.Of(-22f), 5);
        }

        [Fact]
        public void GarbageHourIsTheNeutralFactor()
        {
            // 壊れた読み取りで被害倍率が跳ねないこと。
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.NaN), 5);
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.PositiveInfinity), 5);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認し、`TimeOfDayFactor` を実装する**

- `Of` は時刻を `[0, 24)` に畳んでから、`SunriseHour` / `SunsetHour` の前後 `RampHours` で `DayFactor` ↔ `NightFactor` を線形補間する
- NaN / Infinity は `DayFactor`（中立）に倒す
- クラス doc に**必ず**書く: 「`NightFactor = 1.15` に物理的な根拠は無い。バニラのどこにも『夜の方が被害が大きい』という根拠は無く（設計書 §4.3）、これは本 MOD が選んだ控えめな演出値である」

- [ ] **Step 3: `LongPeriodDamage` に掛ける**

```csharp
float chance = LongPeriodResponse.ExtraCollapseChance(height, distance, quake.Intensity, strength);
// ★ 第 2 層の追加被害にだけ掛ける。バニラの被害にも第 1 層の表示にも触れない。
chance *= TimeOfDayFactor.Of(snapshot.HourOfDay);
```

`snapshot.DayNightEnabled == false` でも**式は変えない**（時刻が 12.0 に固定されているので係数は自然に 1.0 になる）。**特別扱いのコードを書かない** —— 分岐を足すと、「日夜 OFF のときだけ別の道を通る」という検証しにくい経路が増える。

- [ ] **Step 4: パネルと診断**

第 2 層セクションに `AddLayer2Row` で:

```
  [本MODの推定] 時間帯: 21:30（夜） 係数 1.15
  日夜サイクルが無効なため、ゲーム内時刻は 12:00 に固定されており、時間帯による差は出ません。
                                                     ← DayNightEnabled == false のときだけ
```

時刻は `HourOfDay` を `HH:MM` 形式に整形して出す（`(int)hour` と `(int)((hour % 1f) * 60f)`）。長周期が OFF のときは行を出さない（適用先が無いため）。

- [ ] **Step 5: `Strings` に 3 件足す（114 → 117）**

| フィールド | 英語既定値 | 日本語 |
|---|---|---|
| `EarthquakeTimeOfDay` | `Time of day` | `時間帯` |
| `EarthquakeTimeFactor` | `factor` | `係数` |
| `EarthquakeNoDayNight` | `The day/night cycle is off, so the in-game hour is pinned at 12:00 and the time-of-day factor never changes.` | `日夜サイクルが無効なため、ゲーム内時刻は 12:00 に固定されており、時間帯による差は出ません。` |

夜／昼の語は既存の `EarthquakePhase*` とは別物なので、`EarthquakeTimeOfDay` の行に `IsNight` の結果を反映した記号（`21:30` のみ）で済ませ、**新しい語彙キーを増やさない**。

- [ ] **Step 6: ビルドとテスト**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core **234 件緑**（227 + 7）、ロケール **117/117/117** 一致、BOM なし。

- [ ] **Step 7: `docs/playtest-checklist.md` の②の節に足し、②の節を仕上げる**

```markdown
49. 長周期を ON にした状態で、パネルに時間帯と係数の行が出ること。
    ゲーム内時間を進めると 21:00 過ぎに係数が 1.00 → 1.15 に**滑らかに**変わること
50. **日夜サイクルを OFF にする。時刻が 12:00 に固定され、
    「日夜サイクルが無効なため…」の説明文が出ること。**黙って係数が 1.00 に
    なるだけで説明が出なかったら不具合
51. 長周期を OFF にすると、時間帯の行が消えること（適用先が無いため）

## 未検証・注意点（実装者からの申し送り）

- **カーソル地点を求めるレイマーチは①と同じ性質を持つ。**
  `RayGeometry.IntersectTerrain` は 16 m 刻みで最大 8000 m まで進むので、
  地形に当たらない「かすめて外す」レイでは 501 回の高さサンプリングが
  1 フレームで走る。①の申し送りと同じく**今回も意図的に最適化していない**。
  ①と②のパネルを同時に開くと 2 倍になる点だけが新しいので、
  実機で FPS への影響を確認すること
- **波形の観測点は地震が始まった時点で確定する。** 地震の途中で建てた地震計は
  次の地震まで反映されない（毎 tick の全建物走査を避けるための意図的な制限）
- **地震パネルの位置は `(200, 150)` 固定**（①と同じ。ボタンと違って空き位置の
  走査をしていないので、①のパネルと重なる可能性がある。今回のスコープ外）
- `m_activeDuration` などプレハブ 4 値が読めない環境では、カメラシェイク補正と
  波形が動かない。**推測値で代替しない**という判断の結果であり、そのときは
  診断ダンプに理由が出る
```

- [ ] **Step 8: コミット**

```bash
git add -A && git commit -m "feat: 時間帯係数と、日夜サイクル無効時の明示（第2層の完成）"
```

---

## 完了条件

- `build.ps1` が**警告 0** で通り、Core テストが **234 件全て緑**
- ロケールが **117 / 117 / 117** で一致し、`Locales/*.txt` に BOM が無い
- `Assumptions.TotalCheckCount` が **22**、起動ログに②ぶんの 10 項目が出る
- 設定画面に「地震」グループが日英で出る
- ボタンが**予報ボタンと重ならない**位置に出て、パネルが開閉する
- 第 1 層の全ての行に `[実測]`、第 2 層の全ての行に `[本MODの推定]` が付いており、
  セクションが分かれ、色も違う
- 第 2 層の 3 つ（津波連鎖・長周期・時間帯）が**既定 OFF**
- **地震計が無いとき、ハザードマップの数値を出さず、理由を出す**
- **2 つ目の都市をロードしても正常に動く**
- `docs/playtest-checklist.md` に②の節が 51 項目ぶん揃っている
