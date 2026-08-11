# Disaster + — Phase 0（基盤）＋ ③火災旋風 設計書

- 日付: 2026-08-11
- 対象: Cities: Skylines 1（Unity 5.6 / .NET 3.5 / C# 7.3）
- MOD名: `DisasterPlus`（表示名「Disaster +」）
- 状態: 承認済み

---

## 1. 背景と目的

Natural Disasters DLC の災害は演出こそ良いが、現象としてのリアルさに欠ける。予報の概念が実感できず、
地震は単独の断層型のみで震度分布や津波連動がなく、大規模火災と竜巻の関係（火災旋風）も、台風も、
火山も存在しない。

Disaster + は、これらを **1本のMOD** として段階的に追加する。

### 全体スコープ（全5機能）

| # | 機能 | 概要 |
|---|---|---|
| ① | 天気予報タブ | 都市マップ上に落雷予報・竜巻発生確率をヒートマップ表示 |
| ② | 地震オーバーホール | プレート境界型地震（津波連動）、震源距離に応じた震度分布、震度別の倒壊・出火、長周期地震動、時間帯係数、地震計の波形グラフ |
| ③ | 火災旋風 | 大規模密集火災から、移動しない・炎をまとった竜巻を発生させる |
| ④ | 台風 | 雷雨を伴い巨大な回転雲とともに移動、暴風による破壊と河川増水 |
| ⑤ | 火山 | クリック地点に地形隆起を伴って生成、溶岩流と経路一致の火災、3形態（盾状・成層・鐘状） |

### 実装順序

```
Phase 0  基盤
  ↓
③ 火災旋風      ← 本書が対象
  ↓
① 天気予報タブ   （ヒートマップ描画基盤とパネル枠を作る。②が再利用する）
  ↓
② 地震
  ↓
④ 台風
  ↓
⑤ 火山
```

**本書が仕様として確定させるのは Phase 0 と ③ のみ。** ①②④⑤ は各フェーズ開始時に個別の仕様書を切る。
先に全部書くと、実装で判明した事実が反映されない仕様書の山になるため。

### 決定事項（ブレインストーミングで確定）

| 論点 | 決定 |
|---|---|
| パッケージング | 5機能を1本のMODにまとめる |
| バニラ災害との関係 | ハイブリッド。既定は自前の災害として追加し、バニラ挙動の抑制が本質的に必要な箇所だけ Harmony |
| 実装順序 | Phase 0 → ③ → ① → ② → ④ → ⑤ |
| ③の発生判定 | 密集度ベース（半径 R 内に N 棟以上が同時延焼） |
| ③の延焼拡大 | 実装する（強度は設定可、既定は控えめ） |
| ③の実体 | バニラ竜巻を生成。位置固定は C（毎tick書き戻し）を IL 実測し、不可なら A（Harmony） |
| ②のNDR競合 | 競合検知時のみ現れる設定で選択。既定は「NDRに任せる」 |

---

## 2. 競合MOD調査：Natural Disasters Renewal (NDR)

- Workshop ID: `2957578256` / リポジトリ: `luisgobo/EnhancedDisastersModRenewal` / v1.3.0
- Zenya「Natural Disaster Overhaul」と「Ragnarok」の統合版

### 2.1 強度モデル（ソースで確定した事実）

`Source/Models/NaturalDisaster/DisasterBaseModel.cs`:

```csharp
public const float ConservativeGeneratedIntensityLimit = 10f;
public const float ExtremeGeneratedIntensityLimit     = 25.5f;
LocalizationService.Format("tooltip.intensity", string.Format("{0:#.##}", value * 25.5f));
return GetIntensityTooltip(maxGeneratedIntensity / 255f);
```

→ **`DisasterData.m_intensity` は byte (0–255)、表示値は `intensity / 10`。**
バニラは 100（表示 10.0）までしか生成せず、byte の真の上限 255 が表示 25.5。

### 2.2 NDR のパッチ面

Harmony パッチは **2つだけ**、いずれも `DisasterHelpers` に対して。

| パッチ | 対象メソッド | 内容 |
|---|---|---|
| `DestroyBuildingsPatch` | `DisasterHelpers.DestroyBuildings` | Prefix が `false` を返す完全置換。地震は破壊確率 0.02→0.04、竜巻は 1.0→0.5 |
| `DestroyRoadsPatch` | `DisasterHelpers.DestroyNetSegments` | 竜巻の強度が閾値以下なら道路破壊をスキップ |

**`TornadoAI` も `EarthquakeAI` もパッチしていない。** 災害種別は引数値の嗅ぎ分けで判定している:

```csharp
if (probability == 0.02f)                          dt = DisasterType.Earthquake;
else if (burnRadiusMin == 0 && burnRadiusMax == 0) dt = DisasterType.Tornado;
```

### 2.3 すみ分け

| | NDR | Disaster + |
|---|---|---|
| 担当 | 既存災害が**いつ・どれだけ強く**起きるか、避難管理、パネル刷新 | **新しい災害現象**と**物理の可視化** |
| 新災害の追加 | なし | ③④⑤ |
| 可視化 | なし | ①②の中核 |

機能の重複はほぼない。衝突点は次章の3点のみ。

---

## 3. 互換レイヤー

`Game/Compat/ModCompat.cs`。起動時に一度だけ判定してキャッシュする。

### 3.1 検出

`PluginManager.instance.GetPluginsInfo()` を走査し、**Workshop ID `2957578256`** と
**アセンブリ名 `NaturalDisastersRenewal`** の**いずれかに合致**したら NDR ありと判定する。
ID のみだとローカル配置・開発コピーを見落とすため、二重にする。

`PluginManager` は起動時点で既に埋まっているため、`OnSettingsUI`（メインメニューで一度だけ実行）から
安全に参照できる。`SteamHelper.IsDLCOwned` と同じ扱い。**レベルロード後にしか分からない情報は使わない。**

### 3.2 誤検出時に倒れる方向

機能を永久に隠す偽陰性より、実行時フォールバックのある偽陽性を選ぶ。

| 機能 | NDR 検出時の挙動 | 誤検出したときの影響 |
|---|---|---|
| ②地震の被害計算 | 設定を表示し、既定は「NDR に任せる」 | 設定が1つ余分に出るだけ。無害 |
| 強度上限の解放 | 既定 OFF ＋「NDR のパネルが担当中」と注記。手動で ON にできる | ユーザーが ON にすれば済む。機能は消えない |
| ③④⑤ | 常に有効 | 影響なし |

NDR をアンインストールしても保存済み設定値は残る。**NDR 不在時はこれらの設定を無視する**
（設定自体を非表示にし、値は保持したまま既定動作に戻す）。

### 3.3 衝突点と対処

**(a) 強度上限の解放（重複）**

これは2つの別物であり、Disaster + が触るのは後者のみ。

- **自動発生の強度** — NDR が人口連動で 1.0 → 25.5 にランプさせる部分。**Disaster + は触らない**
- **手動発生スライダーの上限** — バニラのパネルが byte 100（表示 10.0）でクランプしている部分。**Disaster + はここを 255（表示 25.5）に解放する**

軸が異なるため衝突しない。NDR は独自パネルに差し替えているので、NDR 併用時のウチのスライダー解放は
無害だが冗長 → 既定 OFF にする。

**バニラがどこで 100 にクランプしているかは IL で確定させる。** パネル UI 側か生成側かで実装箇所が
変わる。推測で実装しない（計画フェーズの最初のタスク）。

**(b) ③火災旋風が NDR に竜巻と誤認される**

NDR の竜巻判定は `burnRadiusMin == 0 && burnRadiusMax == 0`。放置すると火災旋風が竜巻扱いされ、
破壊力が半減（1.0→0.5）し、NDR の `EnableDestruction` が OFF なら破壊が丸ごと消える。

**対処は仕様から自然に出る。火災旋風は建物に火をつけるのが本質なので `burnRadius > 0` を設定する。**
これにより NDR の竜巻判定に合致せず、Prefix は `true` を返してバニラ経路に落ちる。
偶然の回避ではなく、**仕様上そうならざるを得ない形**にする。

**(c) ②地震の被害計算（本質的な競合）**

NDR は地震の建物破壊を完全置換している。Disaster + の②は震度分布が被害を決めるため、同じ処理を
奪い合う。

→ **NDR 検出時のみ設定を表示**し、次から選ばせる。既定は前者。

- 「NDR に任せる」— Disaster + は**可視化のみ**提供（震度分布ヒートマップ、地震計波形、長周期振動の演出）
- 「Disaster + が担当」— Disaster + の震度分布モデルが被害を決め、NDR 側の地震処理を抑制

②のフェーズで詳細を詰める。本書では方針のみ確定。

---

## 4. Phase 0：プロジェクト基盤

### 4.1 ディレクトリ構成

5機能が同居するため機能ごとの縦割りにする。後から ①②④⑤ を差し込んでも既存に触らずに済む。

```
リアル災害プロジェクト/
  src/DisasterPlus/
    Core/                      # エンジン非依存。全部ユニットテスト対象
      Common/                  #   グリッド演算, 決定論的ハッシュ乱数, 寿命クロック, レイ交差
      FireWhirl/               #   ③ 検出ルール・ライフサイクル・延焼拡大の確率モデル
    Game/                      # CS/Unity に触る唯一の層
      Common/                  #   sim tick 配線, 災害バッファ走査, 地形サンプラ実装
      Compat/                  #   ModCompat（NDR 検出）
      Localization/            #   Strings + LocaleLoader
      UI/                      #   災害パネルへのボタン追加, ツール登録
      FireWhirl/               #   ③ 竜巻生成・位置固定・炎パーティクル・被害適用
      Mod.cs / ModSettings.cs
    Properties/AssemblyInfo.cs
    DisasterPlus.csproj        # net35 / C# 7.3
  tests/DisasterPlus.Core.Tests/    # xunit（modern .NET）, Core/**/*.cs のみをコンパイル
  Locales/                     # en.txt（自動生成）+ ja.txt
  docs/superpowers/specs/
  build.ps1
```

`Core` は **net35 の MOD と modern .NET のテストプロジェクトの両方**にコンパイルされる。
ゲーム DLL は net35 でモダンなテストランナーに読めないため、ルールをテスト可能にする唯一の方法。

### 4.2 機能拡張のための共通インターフェース

```
IDisasterFeature
  ├ 災害パネルへのボタン登録
  ├ sim tick フック
  ├ main thread フック（描画）
  ├ 保存 / 復元
  └ レベルアンロード時のリセット
```

`Mod.cs` は機能のリストを回すだけにする。②④⑤ の追加が既存コードへの変更ゼロで済む。

### 4.3 スレッド境界（厳守）

| スレッド | 所有するもの |
|---|---|
| sim | 建物・車両・市民・災害バッファの**生成と変更**。読み取り専用の走査も可 |
| main | GameObject, Mesh, Material, ParticleSystem, UI |

sim 側の変更は `OnAfterSimulationTick()` か `SimulationManager.instance.AddAction(...)` から行う。
main スレッドから建物や災害を生成すると、**スタックトレースのない `IndexOutOfRangeException`
ポップアップ**が、自分の try/catch にも掛からない形で後から出る。

net35 に `System.Collections.Concurrent` はない。共有状態は単一の `lock` で
snapshot-then-render する。

例外: レベルアンロード中は sim スレッドが既に停止しているため、main スレッドから直接クリアしてよい。

### 4.4 決定論

`Core` で `System.Random` を使わない。`hash(tick, id)` で乱数を作る。セーブ・リロード・テストが
完全に再現する。

### 4.5 設定

`SavedInt` / `SavedBool`。設定ファイル名は **`DisasterPlusSettings`**
（MOD名・アセンブリ名と同名にすると毎起動で
`An element with the same key already exists... Deleting...` が出て設定が消え、MOD がエラー扱いになり
Workshop 公開も壊れうる）。

保存値は公開契約として扱う。

- 番兵インデックスを `= Count` にしない。項目を廃止するときは**枠を残して UI から除外**し、配列を短くしない。この不変条件はユニットテストで固定する
- 移行判定は**旧キーの `.exists`** で行う。新キーの `.exists` は全アップグレーダーで `false` になるため、新キーで判定すると全員の選択を上書きする
- 列挙メンバー名からキーを導出する場合、メンバー名と順序を凍結扱いにし、その旨を列挙の定義箇所にコメントする

### 4.6 ローカライズ

日本語・英語を同梱する。

1. 全ての表示文字列を `Strings` の `public static` フィールドに英語既定値付きで置く
2. `Locales/<lang>.txt`（`key = value`, `#` コメント, `\n` エスケープ）を読み、フィールド名をキーに
   **リフレクションで上書き**する
3. 言語は `LocaleManager.instance.language`
4. 英語既定値を一度キャプチャし、別言語適用前に復元する。部分翻訳が言語間で漏れない
5. `Locales/en.txt` が無ければ既定値から再生成する

`OnSettingsUI` は言語切替で**再実行される**（`OptionsMainPanel.OnLocaleChanged` → `CreateCategories`
→ `AddUserMods` → `OnSettingsUI`）。ローダーを `OnSettingsUI` の先頭で呼べば、オプション画面は再起動
なしで言語に追従する。ただしゲーム内ボタンのツールチップはレベルロード時に一度設定されるだけなので
次のロードまで変わらない。**その旨をコードにコメントする。**

**ラベル配列は型初期化時に凍結する。** `static readonly string[] { Strings.A, ... }` は起動時の言語で
固定される。**メソッドにして毎回組み直す。** 同じ配列が `.Length` で設定値をクランプしている場合は
分離する（ラベルは `Strings` のメソッド、件数は `const int`）。

`en.txt` はビルド済み DLL へのリフレクションでオフライン生成し、実行時ライターと同じ列挙・
エスケープを使う。手書きテンプレートは黙って乖離する。

### 4.7 永続化

`ISerializableDataExtension` に先頭 `int Version` 付きのバイナリ blob。

- 読み込み時は後から追加した全ブロックを `if (version >= N)` で分岐。旧セーブは既定値で読める
- 新しい状態は既存レコードを広げず、**id をキーにした並列ブロック**として追加する
- **論理状態のみ保存**する。経路・グラフ・見た目・エフェクトキューは実行時に再構築する
- `OnLevelUnloading` で全セッション状態をリセットする

一時フラグをセーブに漏らさない。全 MOD の `OnSaveData` はバニラが建物・セグメント配列を書く
**前**に走るため、「クリア → 保存 → `finally` で復元」は復元がバニラの読み取り前に走る no-op になる。
**クリアは `OnSaveData` で、復元は `SimulationManager.instance.AddAction(...)` で遅延**させる。

### 4.8 静的キャッシュ

`UnityEngine.Object` は `==` を多重定義しており破棄済みオブジェクトが null と等価になるが、
**配列参照の比較には効かない**。`static Mesh[]` は破棄済みメッシュを抱えたまま非 null であり続け、
2つ目の都市で無言・無ログのまま見えなくなる。

```csharp
if (_meshes == null || _meshes.Length == 0 || _meshes[0] == null) Reload();
```

**要素で判定する。** 「一度だけ警告」フラグも同じ分岐でリセットする。レベルアンロード時に全静的
キャッシュをクリアする。

### 4.9 レンダリング

CS のマテリアルを借りると何も描画されない（CS のシェーダはエンジンが供給する per-instance データを
要求するため）。**メッシュだけ借り**（`m_mesh`、フォールバック `m_lodMesh`）、
`Shader.Find("Standard")` → `"Legacy Shaders/Diffuse"` → `"Diffuse"` の順で自前マテリアルを作る。
マテリアルは用途ごとにキャッシュして共有する。

`DispatchEffect` の `magnitude` は**粒子密度**であって大きさではない。

```
particlesPerSquare = timeDelta * magnitude * 0.01
count = max(100, PI*r*r) * particlesPerSquare      // r = EffectInfo.SpawnArea 半径
```

大きさは **SpawnArea 半径**で決まる。`magnitude` は 0.5〜8 程度に収め、半径は 250m 程度で頭打ちに
する。km スケールの表現が要る場合は自前 `ParticleSystem` を書く。

### 4.10 災害バッファの走査

`DisasterManager.instance.m_disasters` は `FastList<DisasterData>`（`m_buffer` / `m_size`）。
`m_flags` の `Created` / `Active` を見て、`m_infoIndex` を
`PrefabCollection<DisasterInfo>.GetPrefab()` で解決する。**この API は境界チェックをしないので
1件ごとに try/catch** する。

`DisasterAI` のサブクラスは限定列挙: `EarthquakeAI`, `ForestFireAI`, `MeteorStrikeAI`, `SinkholeAI`,
`StructureCollapseAI`, `StructureFireAI`, `FloodBaseAI`（`GenericFloodAI`, `TsunamiAI`),
`WeatherDisasterAI`（`ThunderStormAI`, `TornadoAI`）。

`MeteorAI` は `VehicleAI`（落下する岩）であって災害ではない。継承チェーンで確認し、`CS0184` 警告を
無視しない。

建物バッファは固定長（49152 スロット）でほとんど空。**走査結果の件数を都市の実数として報告しない。**

### 4.11 ビルドと検証

- `build.ps1` — msbuild（Release）→ `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\DisasterPlus`
  に DLL と `Locales/` を配置
- ゲーム DLL の参照解決は 環境変数 `CITIES_SKYLINES_MANAGED` → 既定パスの順。
  **マシン固有パスをリポジトリに焼き込まない**
- ソースは glob（`Game\**\*.cs`）。新規ファイルで csproj を触らずに済む
- CI は `Core` テストのみ（ubuntu-latest / `actions/setup-dotnet`）。MOD 本体はゲームの再配布不可な
  net35 DLL に依存するため CI でビルドできない。**「緑＝純ロジックが健全であって、MOD がビルドできる
  保証ではない」**旨をワークフローにコメントする
- ログは `…\Steam\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt`（AppData ではない）

### 4.12 DLC ゲート

①②③④ は Natural Disasters 必須、⑤火山は不要。判定は `SteamHelper.IsDLCOwned`
（起動時から使える）。`OnSettingsUI` はメインメニュー時に一度だけ走るため、レベルロード後にしか
分からない情報からオプションを組み立てない。

### 4.13 日本語コメントの取り扱い

日本語コメントを含むファイルを PowerShell の `Get-Content -Raw` → `Set-Content` で編集すると
文字化けする。**Edit/Write ツールか、明示的に UTF-8 を指定した Python を使う。**

---

## 5. ③火災旋風

### 5.1 現象

現実の火災旋風は、広範囲の同時多発火災が生む上昇気流の収束によって発生し、火の粉を撒いて延焼を
加速させる。**面積だけでなく密度が要る**のがポイント。郊外に点在する火災では起きない。

### 5.2 発生条件

**半径 R メートル以内に N 棟以上が同時に延焼中**であれば、その重心に発生する。

- 既定: R = 150m, N = 12 棟（どちらも設定で調整可）
- 判定は燃焼中の建物だけを空間グリッドに投票して数える（安価）
- 最小離隔距離より近い候補は統合する
- 既に火災旋風が生きている領域では再発生させない。**判定は距離チェックとクールダウンの併用**で、
  最小離隔距離（5.10）以内に生存中の旋風があれば抑制し、旋風が消滅した地点はクールダウン
  （既定は最大持続時間と同値）が明けるまで再発生させない

### 5.3 手動発生

災害パネルに「火災旋風」ボタンを置き、クリック地点に強制発生させる。テストに必須であり、
プレイヤーの遊べる幅も広がる。

**CS の地形に Unity コライダーは無いので `Physics.Raycast` は絶対に当たらない。**
カメラレイと高さ場の交差を自前で計算する: 16m 程度で粗くマーチし（詳細マップは約 4m/セル）、
レイ高さが `TerrainManager.SampleDetailHeight` を下回ったら二分法で収束させる。
`SampleDetailHeight` は読み取り専用でどちらのスレッドからも安全。

これは純粋な数学なので `Core` に置き、`IHeightSampler` の裏でユニットテストする。

**カスタムツールは `SetTool<T>()` から見えない。** `ToolController.m_tools` は `Awake` で一度だけ
構築されるため、毎レベルロードで次を行う:

1. `ToolsModifierControl.toolController.gameObject.AddComponent<T>()`
2. インスタンスを private な `ToolController.m_tools` 配列にリフレクションで差し込む
3. 静的な `ToolsModifierControl.m_Tools` 辞書にも差し込む

### 5.4 ライフサイクル

位置は固定（移動しない）。**寿命は3段構えで決める。**

| 条件 | 挙動 |
|---|---|
| 発生元の火災が続いている | 存続 |
| 発生条件（R 内に N 棟）を割り込んだ状態が一定時間続いた | 消滅 |
| 最大持続時間に達した | **必ず打ち切る**（既定 10 ゲーム内分、設定可） |

**時間は全てゲーム内時間で測る**（`SimulationManager.m_currentGameTime` 由来、`Core` には経過時間として
渡す）。実時間ではない。値は 0 でクランプし、ポーズ中は経過ゼロで早期リターンする。

**絶対上限は必須。** 「その場に留まる」状態に時間上限を付けないと、永久に居座るか、逆にスタック
ユニット掃除に消される。これは既知の事故パターンであり、**回帰テストで固定する**。

延焼拡大は自己強化ループ（延焼が増える → 発生条件を満たし続ける → 旋風が延命する）を作るため、
絶対上限がその安全弁になる。

### 5.5 被害

**(1) 物理破壊** — バニラ竜巻と同じ建物・樹木の破壊を旋風半径内に継続適用する。

**(2) 延焼拡大** — 旋風の周囲に確率的に新規出火を発生させる。火災旋風という現象の核心であり、
これが無いと「ただの動かない竜巻」になる。設定で強度を 0〜最大まで調整可能にし、既定は控えめに
する（放置すると都市が焼き尽くされうるため）。

**強度スケール** — 燃焼中の棟数に応じて旋風の半径と破壊力をスケールさせる。小さい火災なら小さい
旋風、大火災なら大きい旋風。単調増加＋クランプ。

### 5.6 実体（位置固定の実装）

バニラの竜巻災害を `DisasterManager.CreateDisaster` で生成し、**自分が作った災害 ID を記録する。**
渦のメッシュ・音・破壊判定・災害通知・市民の避難行動がバニラ品質で手に入る。

`burnRadius > 0` を設定する（火災旋風の本質であり、NDR の竜巻誤認を仕様上回避する — 3.3(b)）。

位置固定の方式は **IL 実測で決める**。

- **C（第一候補）** — sim tick ごとに `DisasterData` の座標を発生地点へ書き戻す。Harmony 不要
- **A（フォールバック）** — `TornadoAI` の移動処理に Harmony パッチ。**自分が作った災害 ID のときだけ**
  位置更新をスキップする

計画フェーズの最初に `TornadoAI` の IL を読み、「`TornadoAI` が位置をどこに保持し、いつ書くか」を
確定させてから選ぶ。C が成立すれば Harmony 依存はゼロになる。書き戻しが tick 後になると 1 フレーム
ぶん振動して見える恐れがあるため、**見た目で確認するまで C を成功と見なさない。**

どちらに転んでも ③ の仕様・見た目・被害モデルは変わらない。

### 5.7 見た目

炎をまとった渦。**自前の `ParticleSystem`** で炎の粒子を上昇渦として描く。

CS のマテリアルは借りない（4.9）。`DispatchEffect` を併用する場合、大きさは `SpawnArea` 半径で
決まり `magnitude` は密度である点に注意する（4.9）。

### 5.8 コンポーネント

#### Core（エンジン非依存・全部テスト対象）

| クラス | 責務 |
|---|---|
| `FireWhirlDetector` | 燃焼中建物の座標リストと (R, N) を受け、発生候補点（重心・強度）を返す。空間グリッド投票＋最小離隔での候補統合 |
| `FireWhirlLifecycle` | 旋風 1 基の状態機械。`Update(経過時間, 現在の燃焼棟数)` → 継続 / 消滅。5.4 の 3 条件を実装 |
| `FireWhirlStrength` | 燃焼棟数 → 旋風半径・破壊力の写像（単調増加＋クランプ） |
| `IgnitionSpread` | 旋風周囲で今 tick 発火する建物を選ぶ確率モデル。延焼拡大の強さ設定でスケール。`hash(tick, buildingId)` を使う |
| `Common/RayGeometry` | カメラレイと高さ場の交差（粗マーチ→二分法）。`IHeightSampler` 越し |
| `Common/GridVote` | 空間グリッドへの投票と近傍集計 |
| `Common/LifetimeClock` | ゲーム内時間ベースの経過時間管理 |
| `Common/DeterministicRandom` | `hash(tick, id)` ベースの乱数 |

#### Game（CS/Unity に触る唯一の層）

| クラス | スレッド | 責務 |
|---|---|---|
| `FireWhirlFeature` | — | `IDisasterFeature` 実装。登録と配線 |
| `BurningBuildingScanner` | sim（読取のみ） | `Building.Flags.Fire` を走査。49152 スロットを**複数 tick に分割して巡回**する |
| `FireWhirlSpawner` | sim | `CreateDisaster` で竜巻生成、`burnRadius > 0`、自分の災害 ID を記録 |
| `FireWhirlPinner` | sim | 位置固定（C または A） |
| `FireWhirlDamage` | sim | Core が選んだ建物に発火を適用 |
| `FireWhirlFlameFx` | main | 自前 `ParticleSystem` による炎の渦 |
| `FireWhirlPlacementTool` | main | 手動発生のクリック配置（5.3） |

### 5.9 永続化

生存中の旋風の**論理状態のみ**（座標・経過時間・強度・発生元の識別）をバージョン付き blob に保存
する。パーティクル・エフェクトキューは実行時に再構築する。`OnLevelUnloading` で全リセット。

### 5.10 設定

| 設定 | 型・範囲 | 既定 |
|---|---|---|
| 火災旋風を有効化 | bool | ON |
| 発生半径 R | 50–400m | 150m |
| 発生棟数 N | 4–40 棟 | 12 棟 |
| 最大持続時間 | 1–60 ゲーム内分 | 10 ゲーム内分 |
| 延焼拡大の強さ | 整数 0–10（0＝延焼拡大なし） | 3 |
| 多重発生の最小離隔距離 | 100–800m | 300m |
| 再発生クールダウン | — | 最大持続時間と同値（設定なし） |

### 5.11 テスト（Core / xunit）

| 対象 | ケース |
|---|---|
| `FireWhirlDetector` | 密集 12 棟で発生 / 分散 12 棟で不発 / N と R の境界値 / 近接候補の統合 / 同入力で同出力 |
| `FireWhirlLifecycle` | 火災継続で存続 / 条件割れ継続で消滅 / **最大寿命で必ず打ち切られる（居座り回帰テスト）** |
| `FireWhirlStrength` | 単調増加 / 下限・上限クランプ |
| `IgnitionSpread` | 強度 0 で発火ゼロ / 確率の単調性 / 同 tick 同 id で同結果 |
| `Common/RayGeometry` | 既知の高さ場でのレイ交差収束 / 交差なしケース |
| 設定の不変条件 | 番兵インデックスが配列長の変更で動かないこと |

ユニットテストが捕まえられないもの: Game 層が渡す値の誤り、スレッド境界違反、描画。
これらは絞った診断ログを出して `output_log.txt` を読む。1 tick ごとの `DIAG` 行（旋風数・状態の要約）は
「全部消えた」という報告を受けたときに効く。スロットリングすること。

---

## 6. IL で確定させる項目（計画フェーズの最初）

推測で実装してはならない項目。リフレクションで分かるのは型・可視性・シグネチャのみで、
**単位・実際の配列長・フィールドの意味はメソッド本体からしか分からない。**

| # | 確定させること | 影響 |
|---|---|---|
| 1 | `TornadoAI` が位置をどこに保持し、いつ書くか | 5.6 の C / A の選択 |
| 2 | バニラが災害強度を byte 100 でクランプしている箇所（パネル UI 側か生成側か） | 3.3(a) の実装箇所 |
| 3 | `DisasterManager.CreateDisaster` の引数の単位と `burnRadius` の意味 | 5.6 / 3.3(b) |
| 4 | `Building.Flags.Fire` の立つタイミングと解除条件 | 5.2 の検出精度 |

手順は `cities-skylines-modding` スキルの `pitfalls.md#reading-il` に従う。

---

## 7. 完了条件（Phase 0 ＋ ③）

- `build.ps1` でビルドと配置が通り、ゲームが MOD をエラーなしで読み込む
- 設定画面が日本語・英語で表示され、言語切替に追従する
- 災害パネルに「火災旋風」ボタンが出て、クリック地点に手動発生できる
- 密集火災を起こすと自動で火災旋風が発生し、その場に留まり、炎をまとって見える
- 周囲に延焼が広がり、火が収まるか最大持続時間で消滅する
- セーブ・ロードで生存中の旋風が復元される
- **2つ目の都市をロードしても正常に動く**（静的キャッシュと状態漏れの検証）
- NDR を併用しても火災旋風の破壊が減衰しない
- Core テストが全て緑
