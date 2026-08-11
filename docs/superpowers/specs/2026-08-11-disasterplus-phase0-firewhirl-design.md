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

**実装箇所は IL 実測で確定済み（付録 A-2）。Harmony は不要。**
`DisastersOptionPanel.m_slider`（`Find<UISlider>("Slider")`）の `maxValue` を実行時に 255 へ書き換える
だけでよい。スライダーの生値がそのまま byte 強度で、ラベルだけが `value / 10` で表示される。
上限値はコードではなく UI プレハブ側に置かれているため、パッチ対象が存在しない。

**(b) ③火災旋風が NDR に竜巻と誤認される**

NDR の竜巻判定は `burnRadiusMin == 0 && burnRadiusMax == 0`。火災旋風は竜巻扱いされ、破壊確率が
半減（1.0→0.5）し、NDR の `EnableDestruction` が OFF なら**バニラ由来の破壊が丸ごと消える**。

**当初案（`burnRadius > 0` を設定して誤認を避ける）は IL 実測により却下された。**
`VortexAI.SimulationStep` の `DestroyStuff` 呼び出しで `burnRadiusMin` / `burnRadiusMax` は
**リテラル `0f` にハードコードされている**（付録 A-1）。フィールドでも引数でもないため設定できない。

**採用する対処: 火災旋風の本質的な挙動を、`DisasterHelpers` を経由しない自前コードに置く。**

| 被害の層 | 実装 | NDR の影響 |
|---|---|---|
| 物理破壊（建物・樹木・道路） | バニラの `VortexAI` 由来 | **受ける**（確率半減、`EnableDestruction` で無効化されうる） |
| **延焼拡大（火災旋風の核心）** | **`FireWhirlDamage` による自前の発火適用** | **受けない** |

「自前の発火適用」とは**着火する建物を自分で選ぶ**という意味であって、
`Building.m_fireIntensity` を自分で書くという意味ではない（付録 A-3）。実際の着火は
`BuildingAI.BurnBuilding` に委ねる。これは `DisasterHelpers` を経由しないので、
NDR の差し替えの影響を受けないという以下の性質はそのまま成り立つ。

自前の発火適用は `DisasterHelpers` を一切呼ばないため、NDR がどう設定されていても火災旋風は
「周囲に火を撒く」という定義的な挙動を失わない。バニラ由来の物理破壊が NDR の竜巻設定に従うのは、
**プレイヤーが NDR で行ったチューニングを尊重する**という意味で妥当な挙動であり、そのように扱う。

この事実を設定画面のツールチップに明記する（NDR 検出時のみ）。

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

**`Building.Flags.Fire` は存在しない。** 列挙にあるのは `Abandoned` と
`Collapsed`（`BurnedDown` と同値 `0x400000` のエイリアス）まで。**燃焼中の判定は
`Building.m_fireIntensity`（Byte）が 0 より大きいかで行う。** 隣接する `m_fireHazard`（Byte）は
出火しやすさであって燃焼中フラグではない（①天気予報で使える）。

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

**発生条件割り込みの猶予は既定 3 ゲーム内分。** 燃焼中建物の走査は 8 tick で 1 周し、
ゲーム速度 3 では 1 tick = 9 sim フレームなので最悪 72 フレーム ≒ 1.6 分（付録 A-4）。
猶予がこれを下回ると、走査 1 周ぶんの古い結果だけで旋風が消える。走査 1 周の約 2 倍を既定にする。

**手動発生（災害パネルのボタン）は発生条件の割り込み判定を免除する。** 火の無い場所に置けることが
手動発生の存在意義なので、免除しないと置いた次の tick から猶予の消化が始まる。終了は絶対上限だけ。
このフラグはセーブに保存する（保存しないとロードで自動発生扱いに変わり、猶予だけで消えてしまう）。

**絶対上限は必須。** 「その場に留まる」状態に時間上限を付けないと、永久に居座るか、逆にスタック
ユニット掃除に消される。これは既知の事故パターンであり、**回帰テストで固定する**。

延焼拡大は自己強化ループ（延焼が増える → 発生条件を満たし続ける → 旋風が延命する）を作るため、
絶対上限がその安全弁になる。

### 5.5 被害

**(1) 物理破壊** — バニラ竜巻と同じ建物・樹木の破壊を旋風半径内に継続適用する。

**(2) 延焼拡大** — 旋風の周囲に確率的に新規出火を発生させる。火災旋風という現象の核心であり、
これが無いと「ただの動かない竜巻」になる。設定で強度を 0〜最大まで調整可能にし、既定は控えめに
する（放置すると都市が焼き尽くされうるため）。

**着火は `Building.m_fireIntensity` の直接書き込みではなく `BuildingAI.BurnBuilding` で行う**
（付録 A-3）。直接書くと、火災処理を持たない `BuildingAI` 直系の建物にセーブへ焼き付く火勢が残る。

**判定間隔はフレーム番号の剰余で決めない。** 経過ゲーム内時間を積算して閾値越えで判定する
（理由は付録 A-4）。

**強度スケール** — 燃焼中の棟数に応じて旋風の半径と破壊力をスケールさせる。小さい火災なら小さい
旋風、大火災なら大きい旋風。単調増加＋クランプ。

### 5.6 実体（位置固定の実装）— IL 実測で確定

バニラの竜巻災害を `DisasterManager.CreateDisaster(out ushort id, DisasterInfo info)` で生成し、
**自分が作った災害 ID を記録する。** 渦のメッシュ・音・破壊判定・災害通知・市民の避難行動が
バニラ品質で手に入る。

**当初の案 C（`DisasterData.m_targetPosition` の毎tick書き戻し）は IL 実測により却下された。**
竜巻の実体は `DisasterData` ではなく、`TornadoAI.m_vortexInfo` を持つ **`VortexAI` の車両**である
（付録 A-1）。`TornadoAI.GetPosition` はその車両群のバウンディングボックス中心を返し、車両が無い
ときだけ `m_targetPosition` にフォールバックする。`m_targetPosition` を書き戻しても渦は止まらない。

**採用する方式 — `VortexAI.SimulationStep`(6 引数) への Harmony Postfix。**

```
VortexAI.SimulationStep(ushort vehicleID, ref Vehicle vehicleData, ref Vehicle.Frame frameData,
                        ushort leaderID, ref Vehicle leaderData, int lodPhysics)
```

自分が生成した渦車両に対してのみ、Postfix で **`frameData.m_position` を発生地点に書き戻す**。
バニラは同メソッド内で `m_position = m_position + m_velocity * dt` として移動を積算するため、
毎ステップ書き戻せばドリフトが蓄積せず、その場に留まる。

**`m_velocity` は書き換えない。** 毎ステップ再計算されるので書き換えても無意味であり、かつ
`DisasterHelpers.AddWind` が向きに使っているため、ゼロにすると風の演出が死ぬ。

**移動目標に到達させないことが存続の条件になる。** 到達すると
`ArriveAtDestination`（`m_waitCounter > 4` で true）が `DisasterAI.DeactivateNow` と
`Vehicle.Unspawn` を呼び、**災害が消える**（付録 A-1）。

> **訂正（最終レビュー時の IL 再確認）。** 初版はここに「バニラの移動目標（`m_targetPos0`）は
> 遠方のまま維持する」と書いていたが**誤り**。`TornadoAI.ActivateDisaster` は
> `SetTargetPos(0, m_targetPosition)` — つまり**スロット 0 は発生地点そのもの**を入れる。
> そして `VortexAI.SimulationStep` は冒頭で
> `if (LengthXZ(m_targetPos0 - frame.m_position) < m_info.m_maxSpeed) m_targetPos0 = m_targetPos1;`
> を行うため、固定した最初のステップでスロット 0 は 1000m 先のスロット 1 に置き換わる。
> 正しくは「**スロット 1 が遠方であることによって存続する**」。詳細は付録 A-1。

**終了はバニラの解体経路に乗せる。** 5.4 の寿命条件を満たしたら
`Vehicle.SetTargetPos` で **スロット 0 と 1 の両方**を現在位置に書き換える。片方だけでは
上記の入れ替えで打ち消され、`ArriveAtDestination` に永遠に到達しない。
**終了処理中も位置固定を続ける。** 固定を解くと、スピンダウンが終わるまでの間
（角速度 1.0 から −0.05/step で 0.05 未満まで約 20 ステップ ＋ `m_waitCounter > 4` の 5 ステップ）
渦が自由に動き回り、炎エフェクトだけが発生地点に取り残される。固定したままなら目標との距離が
0 のままなのでスピンダウンが確実に進む。渦車両の `SimulationStep` は 16 sim フレームに 1 回
（`VehicleManager.SimulationStepImpl` が `m_currentFrameIndex & 15` で 16 分割）なので、
畳み終わるまで実時間で数秒。破壊は角速度が 0.95 を割った時点で
`Vehicle.m_flags` の 0x4000 が落ちて止まる。
**車両を自前で解放しない** — それはスレッド境界事故の温床であり、バニラの解体処理を
丸ごと再実装することになる。

渦車両がまだ紐づいていないうちに寿命が尽きた場合は、レジストリから外すだけにしない
（バニラの災害が追跡不能なドリフト竜巻として生き残る）。`DisasterAI.DeactivateNow`
（public、IL 確認済み）で正規に止める。

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
| `BurningBuildingScanner` | sim（読取のみ） | **`Building.m_fireIntensity > 0`** を走査。49152 スロットを**複数 tick に分割して巡回**する |
| `FireWhirlSpawner` | sim | `CreateDisaster` で竜巻生成、自分の災害 ID と渦車両 ID を記録 |
| `FireWhirlPinner` | sim | `VortexAI.SimulationStep` Postfix による位置固定と、寿命到達時の終了（5.6） |
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

## 6. IL 実測（完了）

計画着手前に 4 項目すべてを `Assembly-CSharp.dll` の IL で確定させた。手順は
`cities-skylines-modding` スキルの `pitfalls.md#reading-il`。**2 項目で当初の設計が誤りだと判明し、
本書を訂正した。**

| # | 確定させたこと | 結果 | 影響 |
|---|---|---|---|
| 1 | `TornadoAI` が位置をどこに保持するか | **`DisasterData` ではなく `VortexAI` の車両**。案 C は不成立 | 5.6 を書き換え |
| 2 | 強度を byte 100 でクランプしている箇所 | **コードに存在しない**。`DisastersOptionPanel.m_slider.maxValue`（UI プレハブ値）。Harmony 不要 | 3.3(a) を書き換え |
| 3 | `CreateDisaster` の引数と `burnRadius` の意味 | `CreateDisaster(out ushort, DisasterInfo)`。**`burnRadius` は `VortexAI` 内でリテラル 0 固定、設定不可** | 3.3(b) を書き換え |
| 4 | 燃焼中建物の判定方法 | **`Building.Flags.Fire` は存在しない**。`Building.m_fireIntensity`（Byte）> 0 | 4.10 / 5.8 を訂正 |

詳細は付録 A。

---

## 付録 A: IL 実測結果

### A-1. 竜巻の実体と被害経路

`TornadoAI.SimulationStep` は `DisasterData.m_targetPosition` を**一切書かない**。天候
（`WeatherManager.m_forceWeatherOn` / `m_targetFog` / `m_targetRain` / `m_targetCloud`）のみを操作する。

`TornadoAI.GetPosition` は災害グループの全インスタンスを走査し、`Info == m_vortexInfo` の**車両**の
バウンディングボックス中心を位置として返す。該当車両が 1 つも無いときだけ `m_targetPosition` に
フォールバックする。

**`DisasterAI.StartDisaster` は `protected`。** MOD から起動するときは公開ラッパーの
`DisasterAI.StartNow(ushort, ref DisasterData)` を使う。`StartNow` は `(m_flags & 0x3C) == 0`
（Emerging / Active / Clearing / Finished のいずれでもない）のときだけ `StartDisaster` に委譲する。
`CreateDisaster` 直後は `Created` しか立っていないので必ず通る。（実装中に発覚し IL で確認）

`TornadoAI.ActivateDisaster` は `m_targetPosition` からランダム角のオフセット位置を計算して
`VehicleManager.CreateVehicle` で渦車両を作り、`InstanceManager.CopyGroup` で災害グループに結び付け、
`Vehicle.SetTargetPos` を 2 回呼んで移動目標を与え、`DisasterManager.FollowDisaster` を呼ぶ。

**移動目標の中身（初版の記述は誤り。最終レビューで IL 再確認し訂正）:**

```
dir  = normalize(sin(m_angle), 0, -cos(m_angle))
dist = m_intensity * 10 + 400                    // intensity 60 なら 1000
車両の生成位置 = m_targetPosition + dir * dist
SetTargetPos(0, m_targetPosition)                // ← スロット 0 は発生地点そのもの
SetTargetPos(1, m_targetPosition - dir * dist)   // ← 反対側 1000m
```

`Vector3` → `Vector4` の暗黙変換で入るので **`w` は 0**。`VortexAI` 側も `Vector4` → `Vector3` の
暗黙変換で読むだけなので `w` に意味は無い。

`VortexAI.SimulationStep`(6 引数) は距離判定の直前に

```
if (VectorUtils.LengthXZ(m_targetPos0 - frame.m_position) < m_info.m_maxSpeed)
    m_targetPos0 = m_targetPos1;     // 入れ替えてから測り直す
```

を行う。したがって**位置を固定した最初のステップでスロット 0 はスロット 1 に置き換わる**。
`Vehicle.SetTargetPos(0, …)` はスロット 0 しか書かない（IL は `switch` 1 本で
`m_targetPos0..3` に分岐するだけ）ので、**終了させたいときは 0 と 1 の両方に書く必要がある**。

スピンダウンの算術（同メソッド）:

```
distance > 1f : m_angleVelocity = Min(1, m_angleVelocity + 0.05); >0.95 なら m_flags |= 0x4000
distance <= 1f: m_angleVelocity = Max(0, m_angleVelocity - 0.05); <0.95 なら m_flags &= ~0x4000
                if (m_angleVelocity < 0.05 && ArriveAtDestination()) { DeactivateNow; Unspawn; }
```

角速度 1.0 → 0.05 未満まで 20 ステップ、`ArriveAtDestination`（`m_waitCounter > 4`）にさらに
5 ステップ。加えて速度がゼロに落ちるまでの数〜十数ステップがかかる。1 ステップは
16 sim フレーム（`VehicleManager.SimulationStepImpl` の `m_currentFrameIndex & 15` による 16 分割）。
破壊（`DestroyStuff` / `BurnGround`）は `m_flags & 0x4000` が立っている間だけなので、
角速度が 0.95 を割った時点で止まる。

`DisasterAI.CreateDisaster` は `new InstanceManager.Group()` を作り
`m_ownerInstance.Disaster = 災害ID` を入れて `InstanceManager.SetGroup` で登録する。
したがって `InstanceManager.GetGroup(new InstanceID { Disaster = id })` で災害グループが引ける。
`DisasterAI.DeactivateNow(ushort, ref DisasterData)` は **public**（`StartDisaster` /
`ActivateDisaster` / `DeactivateDisaster` は protected）。

`VortexAI : VehicleAI` のフィールドは `m_destructionRadiusMin` / `m_destructionRadiusMax` /
`m_upgradeRadiusMin` / `m_upgradeRadiusMax` / `m_debrisCount`。

`VortexAI.ArriveAtDestination` は `m_waitCounter` をインクリメントし **`m_waitCounter > 4` を返す**。
`SimulationStep`(6 引数) 内でこれが true になると `DisasterAI.DeactivateNow` と `Vehicle.Unspawn` が
呼ばれる。

`VortexAI.SimulationStep`(6 引数) の順序:

1. `Frame.m_position` を書く（第 1 段）
2. `m_targetPos0` までの距離を `VectorUtils.LengthXZ` で測り `m_targetPos0` を更新
3. `ArriveAtDestination` → true なら `DeactivateNow` + `Unspawn`
4. `Randomizer` と `TerrainManager.SampleRawHeightSmoothWithWater` から `Frame.m_velocity` を再計算
5. **`Frame.m_position = m_position + m_velocity * dt`（第 2 段。ここが実際の移動）**
6. `DisasterHelpers.AddWind` → `DestroyStuff` → `BurnGround` → `UpgradeBuildings`
7. `VehicleAI.SimulationStep` へ委譲

被害呼び出しの実引数（`loc15 = m_destructionRadiusMin * s`, `loc16 = m_destructionRadiusMax * s`）:

```
DestroyStuff(seed: vehicleID, group, position: frame.m_position,
             totalRadius: loc16, preRadius: loc16, removeRadius: 0f,
             destructionRadiusMin: loc15, destructionRadiusMax: loc16,
             burnRadiusMin: 0f,   // ldc.r4 0 — リテラル
             burnRadiusMax: 0f)   // ldc.r4 0 — リテラル
BurnGround(VectorUtils.XZ(frame.m_position), radius: loc16, intensity: 0.7f)
```

`burnRadiusMin` / `burnRadiusMax` は**リテラル 0**。フィールド由来でも引数由来でもないため、
外部から値を与える手段が無い。

### A-2. 災害強度スライダー

`DisastersOptionPanel` のフィールドは `m_slider`（`UISlider`）/ `m_label`（`UILabel`）/
`m_disasterTool`（`DisasterTool`）の 3 つ。

`Awake` は `Find<UISlider>("Slider")` と `Find<UILabel>("LabelIntensity")` で参照を取り、
`ToolsModifierControl.GetTool<DisasterTool>()` を保持する。

```
OnSliderValueChanged(component, float value):
    m_label.text          = (value / 10).ToString("F1")
    m_disasterTool.m_intensity = (int)value
```

**スライダーの生値がそのまま byte 強度**で、表示だけが `/10`。`set_maxValue` の呼び出しは
アセンブリ内に存在せず、上限は UI プレハブ側に置かれている。よって**実行時に
`m_slider.maxValue = 255f` を書けばよく、パッチ対象が無い。**

`DisasterData.m_intensity` は `Byte`。`DisasterTool` 側は `m_intensity` / `m_mouseIntensity` ともに
`Int32` で保持している。

### A-2b. ロード時のコールバック順序

`ISerializableDataExtension.OnLoadData` は `LoadingManager.LoadSimulationData` 内の
`SimulationManager.LateUpdateData` 経由で走り、**`LoadingExtensionBase.OnLevelLoaded`
（`LoadingManager.LoadLevelComplete` 経由）より先に完了する。**

したがって `OnLevelLoaded` で状態をクリアする設計にしている場合、`OnLoadData` で直接復元すると
**直後に消される**。復元データは静的フィールドに退避し、`OnLevelLoaded` のクリア直後に適用する。
退避用フィールドは `OnLoadData` の先頭で無条件にクリアすること（ロードが中断して `OnLevelLoaded`
に到達しなかった場合、次の別都市のロードに前回のデータが漏れる）。（実装中に発覚し IL で確認）

`ISerializableDataExtension` は `PluginManager.GetImplementations<T>()` による自動検出なので、
`IUserMod` / `ThreadingExtensionBase` / `LoadingExtensionBase` と同じく登録作業は不要。

### A-3. 建物の火災状態

`Building.Flags` に火災を表すメンバーは無い（`Abandoned = 0x40000`、
`Collapsed` = `BurnedDown` = `0x400000` のエイリアス）。

`Building` の Byte フィールド **`m_fireIntensity`** が燃焼中かどうかを表し、**`m_fireHazard`** は
出火しやすさを表す。③の検出は `m_fireIntensity > 0` で行う。`m_fireHazard` は①天気予報の
火災リスク表示に使える。

**`m_fireIntensity` を MOD から直接書いてはいけない（最終レビューで判明）。**
このフィールドを消費するのは `CommonBuildingAI.SimulationStepActive` → `HandleFire` **だけ**で、
`BuildingAI.SimulationStep` には火災処理が無い。`BuildingAI` を直接継承しているのは
`PowerPoleAI` / `DecorationBuildingAI` / `WaterJunctionAI` / `OutsideConnectionAI` /
`IntersectionAI` / `CableCarPylonAI` / `MonorailPylonAI` / `RaceStartGantryAI` /
`WildlifeSpawnPointAI`（＋ MOD 製 AI）。これらに火勢を書き込むと**誰も消さない**。
値はバニラの建物配列に入るのでセーブに焼き付き、リロードでも MOD の削除でも消えない。
`BurningBuildingScanner` はそれを永久に「燃焼中」と数え続ける。

**正しい経路は `BuildingAI.BurnBuilding(ushort buildingID, ref Building data,
InstanceManager.Group group, bool testOnly)`（public virtual、`InstanceManager.Group` は public
nested）。** `DisasterHelpers` を経由しないので、競合MOD の設定に左右されない要件（3.3(b)）は保たれる。

- `BuildingAI` の既定実装は `return false` のみ。燃えない建物は自然に弾かれる。
- `CommonBuildingAI` の実装は `GetFireParameters`（`PlayerBuildingAI` は `m_fireHazard == 0` で
  false を返す）→ `Collapsed`/`BurnedDown` 拒否 → 水位拒否 の順に判定し、`testOnly` が true なら
  そこで `true` を返して何も変更しない。false なら
  `InstanceManager.SetGroup` → `DisasterData.m_buildingFireCount++`（グループの
  `m_ownerInstance.Disaster != 0` かつ元の火勢 0 のときのみ）→ `Active` フラグ解除 →
  `m_fireIntensity = Max(現在値, fireSize)` → `Frame.m_fireDamage = Max(現在値, 133)` →
  `BuildingDeactivated` → レンダラ／色／フラグ更新 → サブ建物への伝播、を行う。
- **火勢は `GetFireParameters` の `out fireSize` で建物ごとに決まる**（`PlayerBuildingAI` は 255）。
  MOD 側で固定値を持つ必要はない。
- 戻り値 `true` = 実際に着火した。

### A-4. ゲーム内時間とフレームの換算（最終レビューで訂正）

`SimulationManager.DAYTIME_FRAMES` は **65536**（`public static UInt32`。const ではないので
コンパイル時定数にはならない）。同じクラスに `DAYTIME_HOUR_TO_FRAME = 2730.667`（= 65536 / 24）と
`DAYTIME_FRAME_TO_HOUR = 0.0003662109` があり、整合する。

したがって **1 ゲーム内分 = 65536 / 1440 ≒ 45.51 sim フレーム**。
初版の実装が使っていた 262144 は**誤り**で、あらゆる持続時間がラベルの 4 倍長く動いていた。
値は直書きせず `SimulationManager.DAYTIME_FRAMES / 1440f` から求める。

**フレーム番号は tick ごとに 1 ずつ進むとは限らない。** `SimulationManager.SimulationStep()` は
`FinalSimulationSpeed` 回のループで `m_currentFrameIndex` を加算し、`OnAfterSimulationTick` は
そのループを抜けてから **1 回だけ**呼ばれる。`FinalSimulationSpeed` はゲームモードで
速度 1/2/3 に対し **1 / 3 / 9**（エディタでは速度 3 が 4）。

→ **`frameIndex % N` で周期処理を組んではいけない。** ゲーム速度 2 で 3 倍、速度 3 で 9 倍
まばらになり、処理頻度が黙ってゲーム速度に依存する。周期は経過ゲーム内時間の積算で判定する。

`VehicleManager.SimulationStepImpl` は `m_currentFrameIndex & 15` で車両を 16 分割して回すので、
**1 台の車両の `SimulationStep` は 16 sim フレームに 1 回**。

### A-5. 再現手順

IL 逆アセンブラのスクリプトは `docs/tools/` に置く（`ilload.ps1` / `ildasm.ps1`）。
`Disasm-Method -Method $m -Filter 'stfld'` の形で使う。

---

## 7. 完了条件（Phase 0 ＋ ③）

- `build.ps1` でビルドと配置が通り、ゲームが MOD をエラーなしで読み込む
- 設定画面が日本語・英語で表示され、言語切替に追従する
- 災害パネルに「火災旋風」ボタンが出て、クリック地点に手動発生できる
- 密集火災を起こすと自動で火災旋風が発生し、その場に留まり、炎をまとって見える
- 周囲に延焼が広がり、火が収まるか最大持続時間で消滅する
- セーブ・ロードで生存中の旋風が復元される
- **2つ目の都市をロードしても正常に動く**（静的キャッシュと状態漏れの検証）
- 災害パネルの強度スライダーが 25.5 まで動く
- **NDR を併用し、NDR の `EnableDestruction` を OFF にしても、火災旋風の延焼拡大が動く**
  （バニラ由来の物理破壊は NDR の竜巻設定に従う — これは仕様。3.3(b)）
- Core テストが全て緑
