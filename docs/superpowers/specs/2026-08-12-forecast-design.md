# Disaster + — ①天気予報タブ 設計書

- 日付: 2026-08-12
- 対象: Cities: Skylines 1（Unity 5.6 / .NET 3.5 / C# 7.3）
- MOD: `DisasterPlus`（表示名「Disaster +」）
- 状態: 承認済み
- 前提: Phase 0 ＋ ③火災旋風 ＋ Phase 0.5 診断基盤 が master にマージ済み（Core 116 テスト、**実機テスト未実施**）

---

## 1. 依頼と、調査で判明したこと

### 1.1 元の依頼

> 雷雨や竜巻については、アニメーションや効果については申し分ない一方で、予報の概念がいまいち実感しづらいです。
> （もしかしたら私が見逃しているだけかもしれませんが）天気予報タブを追加して、
> 都市のMAPと落雷予報・竜巻発生確率をヒートマップで表示できるようにしてほしいです。

### 1.2 「見逃しているだけ」は当たっていた

`Assembly-CSharp.dll` の IL 実測により、**ヒートマップはバニラに既に実装されている**ことが判明した。

| 確認した事実 | 値 |
|---|---|
| `InfoManager.InfoMode.DisasterHazard` | 存在する（= 29） |
| `InfoManager.SubInfoMode.LightningHazard` / `TornadoHazard` | 両方とも定義済み |
| `ThunderStormAI.UpdateHazardMap` | **237 命令の実装あり** |
| `TornadoAI.UpdateHazardMap` | **335 命令の実装あり** |
| `EarthquakeAI` / `ForestFireAI` / `MeteorStrikeAI` / `SinkholeAI` / `TsunamiAI` | いずれも `UpdateHazardMap` 実装あり |
| `DisasterManager.m_hazardTexture` / `HAZARDMAP_RESOLUTION` / `HAZARDMAP_CELL_SIZE` | ハザードマップのテクスチャ機構 |
| `DisasterManager.SampleDisasterHazardMap` | 任意地点のハザード値を読める |
| `DisasterRiskInfoViewPanel` | グラデーション付きの実在する UI |
| `InfoManager.SetCurrentMode(InfoMode, SubInfoMode)` | **public**。モード切替は 1 行で済む |

**したがって自前のヒートマップは作らない。** 丸ごと重複になる。

### 1.3 では本当の欠落は何か

バニラが持つのは **hazard（危険度）**、すなわち地形・建物・設備から決まる**静的なリスク**である。
依頼文の不満は「**予報**の概念が実感しづらい」であり、これは
**「今後どうなりそうか」という時間軸が無い**ことだと解釈する。

**本機能はその時間軸を足す。** ヒートマップはバニラを流用し、1 クリックで出せるようにする。

---

## 2. 決定事項

| 論点 | 決定 |
|---|---|
| ヒートマップ | **バニラを流用**。`SetCurrentMode` で切り替えるだけ。自前描画はしない |
| 本機能の付加価値 | 時間軸（傾向・推定・クールダウン）と、ハザード値の**数値化** |
| NDR 併用時 | NDR の内部にリフレクションで踏み込まない。確率表示に注記を出し、天候・傾向・ハザード数値（全てバニラ由来で常に正しい）は出す |
| ボタン配置 | **実行時に空き位置を走査**。固定座標にしない |
| ②地震との関係 | パネル枠・モード切替ラッパー・ハザード読み取り・ボタン配置を②が再利用する |

---

## 3. データ源（全て IL / リフレクションで実測済み）

### 3.1 天候の現在値と目標値

`WeatherManager` の public フィールド。**current と target の両方がある**ことが本機能の核心で、
差分から「向かっている方向」＝傾向が取れる。

| フィールド | 用途 |
|---|---|
| `m_currentRain` / `m_targetRain` | 降雨の現在値と目標値 |
| `m_currentCloud` / `m_targetCloud` | 雲量 |
| `m_currentFog` / `m_targetFog` | 霧 |
| `m_currentTemperature` / `m_targetTemperature` | 気温（季節推移） |
| `m_windDirection` / `m_targetDirection` / `m_windGrid` | 風向 |
| `m_groundWetness` | 地面の湿り |
| `m_lastLightningIntensity` | 直近の落雷強度 |

### 3.2 発生確率とクールダウン

| フィールド | 用途 |
|---|---|
| `DisasterManager.m_randomDisastersProbability` | ランダム災害の発生確率 |
| `DisasterManager.m_randomDisasterCooldown` | クールダウン残量 |

### 3.3 地点ごとのハザード値

`DisasterManager.SampleDisasterHazardMap` — バニラは色でしか見せないので、**数値化が付加価値**になる。

---

## 4. パネルの内容

```
天気予報                                    [×]
  現在   気温 18°C ↗   雨 0.2 ↗   雲 0.6 ↗   風 南南西
  ─────────────────────────────────
  落雷      ▓▓▓▓▓▓░░░░  高まっている
     雨と雲が増加中
     [マップに表示]
  竜巻      ▓▓░░░░░░░░  低い
     クールダウン中
     [マップに表示]
  ─────────────────────────────────
  カーソル位置のハザード: 落雷 34 / 竜巻 12
```

- **傾向の矢印**は current と target の差から出す。バニラに無い情報
- **「マップに表示」**が `InfoManager.SetCurrentMode(InfoMode.DisasterHazard, <各サブモード>)` を呼ぶ
- **カーソル位置の数値**は `SampleDisasterHazardMap` から

### 4.1 出してよい断定の範囲

**「あと何時間で来る」と断定しない。** バニラの発生判定は乱数であり、時刻を予言できない。
出すのは「傾向」「相対的な高低」「クールダウン中かどうか」に留める。
根拠のない数字を出すことは、本プロジェクトが繰り返し戒めてきた「静かに嘘をつく」に当たる。

---

## 5. ボタンの重なり回避

**固定座標は必ずいつか衝突する。** CS1 は左上に MOD のボタンが積み上がる慣習があり、
③で追加した災害パネルのボタンも重なり懸念が未解決のまま残っている。

### 5.1 方式

1. `UIView.GetAView()` から全 `UIComponent` を走査する
2. 各要素の `absolutePosition` と `size` から矩形を作る。**`isVisible` が false の要素は無視する**
3. 既定位置（左上）から縦方向に一定間隔でずらし、**可視の要素と重ならない最初の位置**を採る
4. 見つからなければ既定位置に置き、`Log.Warn` を出す
   （隠れて見つからないより、見えて重なる方がマシ）

### 5.2 永続化

決まった位置を `SavedInt`（x, y）に保存し、次回は同じ場所に出す。
設定画面に「ボタン位置をリセット」を置き、走査をやり直せるようにする。

### 5.3 検証できない部分

**実際に重ならないかは実機でしか確認できない。** 走査対象に入らない描画物（IMGUI など）とは
原理的に衝突を検出できない。実機確認の項目に含める。

---

## 6. 構成

```
src/DisasterPlus/
  Core/Forecast/                 エンジン非依存・全部ユニットテスト対象
    Trend.cs                       enum Rising / Falling / Steady
    TrendMath.cs                   current と target の差 → Trend（不感帯つき）
    HazardLevel.cs                 0-255 → 表示段階（10 段）とラベル
    ForecastReading.cs             1 災害種ぶんの読み取り結果（不変）
    WindDirection.cs               角度 → 16 方位のラベル
  Game/Forecast/
    ForecastFeature.cs             IDisasterFeature 実装
    WeatherReader.cs               WeatherManager / DisasterManager からの読み取り（sim スレッド）
    HazardMapReader.cs             SampleDisasterHazardMap のラッパー
    InfoModeSwitch.cs              SetCurrentMode のラッパー
    ForecastPanel.cs               パネル UI（main スレッド）
  Game/UI/
    FreeSlotFinder.cs              空き位置の走査（②以降も使う）
    ForecastPanelButton.cs         トグルボタン
```

### 6.1 スレッド境界

`WeatherManager` / `DisasterManager` の読み取りは **sim スレッド**（`OnSimulationTick`）で行い、
不変のスナップショットにして `DiagnosticsHub` と同じ snapshot-then-render で main へ渡す。
パネル UI と `SetCurrentMode` は **main スレッド**のみ。

`SampleDisasterHazardMap` の呼び出しスレッド安全性は**未確認**。実装時に IL で確かめ、
不明なら sim スレッド側で読む。

---

## 7. 診断基盤との連携（Phase 0.5 の成果を使う）

- `Assumptions` に本機能ぶんの前提を追加する:
  - `InfoManager.SetCurrentMode` が解決できる
  - `SubInfoMode.LightningHazard` / `TornadoHazard` が存在する
  - `ThunderStormAI` / `TornadoAI` の `UpdateHazardMap` が存在する（ハザードマップが埋まる前提）
  - `WeatherManager` の current/target フィールド群が解決できる
- `IDisasterFeature.WriteDiagnostics` を実装する（インターフェースが強制する）
- `LogChannel.Forecast`（= 4、定義済み）を使う。設定画面にチェックボックスを 1 つ足す

---

## 8. テスト

`Core/Forecast` はユニットテスト対象:

| 対象 | ケース |
|---|---|
| `TrendMath` | 上昇 / 下降 / 横ばい・不感帯の境界・target と current が同値・負値 |
| `HazardLevel` | 0 と 255 の端・段階の単調性・境界値 |
| `WindDirection` | 0/90/180/270 度・境界（11.25 度刻み）・360 を超える角度・負の角度 |
| `ForecastReading` | 不変であること・null 安全 |

`Game/` はユニットテスト不可。前提検証と実機確認で担保する。

---

## 9. 完了条件

- `build.ps1` が警告 0 で通り、Core テストが全て緑
- 設定画面に予報のグループが出る（日英）
- **ボタンがバニラや他MODのボタンと重ならない位置に出る**
- ボタンでパネルが開閉する
- パネルに気温・雨・雲・風向と、その傾向矢印が出る
- 「マップに表示」でバニラのハザードヒートマップに切り替わる
- カーソル位置のハザード数値が出る
- NDR 併用時、確率表示に注記が出る
- 前提検証に本機能ぶんが追加され、起動ログに出る
- **2 つ目の都市をロードしても正常に動く**
