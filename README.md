# Disaster + (DisasterPlus)

Cities: Skylines（無印）向けの災害 MOD。Natural Disasters DLC の災害に、
現象としてのリアルさと可視化を足す。

## 実装済み

- **火災旋風（Fire Whirl）** — 密集した大規模火災から、その場に留まる炎の渦が
  自然発生する。周囲に火の粉を撒いて延焼を広げ、火が収まるか最大持続時間で
  消える。災害パネルのボタンから、クリックした地点に手動でも発生させられる。
  セーブ・ロードで生存中の旋風は残り寿命ごと復元される。
- **災害強度の解放** — 手動発生の強度スライダー上限を 10.0 から 25.5 に
  引き上げる（既定で有効。設定でオフにできる）。

## 予定

天気予報タブ（落雷・竜巻確率のヒートマップ）、地震オーバーホール（震度分布・
長周期地震動・地震計波形・津波連動）、台風、火山。
「地震の被害計算の担当」設定は、この地震オーバーホールに備えて既に設定画面に
用意してあるが、Phase 0 時点では何も参照しておらず動作に影響しない。

## 必要なもの

- Cities: Skylines 1
- **Natural Disasters DLC** — 竜巻（`TornadoAI`）を借りて渦の見た目・音・避難行動を
  実現しているため火災旋風機能に必須。DLC を持たない都市ではこの機能が無効化され、
  設定画面にその旨が表示される。
- **[Harmony (CitiesHarmony)](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)** —
  火災旋風をその場に固定するために `VortexAI.SimulationStep` に 1 箇所だけ
  Postfix パッチを当てている（`Game/FireWhirl/VortexPinPatch.cs`）。Harmony 本体
  （`HarmonyLib`）は同梱せず、`CitiesHarmony.API.dll` の shim のみを MOD フォルダに
  配置する。CitiesHarmony MOD が Steam Workshop 経由で実際の Harmony アセンブリを
  供給する。CitiesHarmony が導入されていない場合、火災旋風はバニラの竜巻と同様に
  移動してしまうが、MOD 自体はクラッシュせず警告ログを出すだけで動作を続ける。

## 他 MOD との併用

**Natural Disasters Renewal (NDR)** と併用できる。役割を分けている。

| | Natural Disasters Renewal | Disaster + |
|---|---|---|
| 既存災害の発生頻度・強度・避難 | 担当 | 触らない |
| 新しい災害現象 | なし | 火災旋風（今後 台風・火山） |
| 可視化 | なし | 今後 予報・震度分布 |

併用時の注意:

- **強度解放**は両方が持つ機能だが、どちらが先に適用しても実害はない。
  `IntensityUnlock` は上限が既に 255（表示 25.5）まで上がっていれば何もしない
  ため、二重に上げても壊れない。片方だけに任せたければ Disaster + 側の
  「全般」設定でオフにできる。
- **火災旋風のバニラ由来の破壊**は Natural Disasters Renewal の竜巻設定に従う。
  NDR は `DisasterHelpers.DestroyBuildings` を Prefix で置き換えており、竜巻の
  判定に使う `burnRadius` は CS 本体側（`VortexAI`）でリテラル 0 に固定されて
  いて Disaster + からは変更できないため
- **延焼拡大は影響を受けない。** 火災旋風の核心である「周囲に火を撒く」挙動は
  `DisasterHelpers` を経由しない自前実装（`Game/FireWhirl/FireWhirlDamage.cs`）
  なので、NDR がその `DestroyBuildings` をどう設定していても必ず動く

## ビルド

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

`DisasterPlus.dll` と `CitiesHarmony.API.dll`、`Locales\` が Mods フォルダへ配置
される。ゲーム DLL の場所は環境変数 `CITIES_SKYLINES_MANAGED` で上書きできる。
未設定なら既定の Steam パスを使う。

## テスト

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

`Core` はエンジン非依存なので、ゲームを起動せずに全ルールを検証できる
（CI もこれだけを走らせる — 詳細は `.github/workflows/core-tests.yml` のコメント
を参照）。`Game` 層はユニットテストできないので、`Cities_Data\output_log.txt`
を読んで確認する。

## ロケール

`Locales/en.txt` は翻訳者向けテンプレートで、`tools/GenerateLocaleTemplate.ps1`
がビルド済み `DisasterPlus.dll` をリフレクションして生成する（手書きしない）。
`ja.txt` を編集した／キーを追加した後は、両ファイルのキー集合が一致することを
確認すること。

## ライセンス

MIT
