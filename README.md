# Disaster + (DisasterPlus)

Cities: Skylines（無印）向けの災害 MOD。Natural Disasters DLC の災害に、
現象としてのリアルさと可視化を足す。

## 機能

Steam Workshop: <https://steamcommunity.com/sharedfiles/filedetails/?id=3794409201>

- **天気予報** — 気温・雨・雲・霧・風向を、シミュレーションの実測値と
  **向かっている先の値**を並べて出す。落雷・竜巻のハザードマップへ 1 クリックで飛ぶ。
- **地震** — 震度分布の可視化（バニラは距離で被害を減衰させていたが表示していなかった）。
  **海溝型地震**のタイルを新設 —— クリック地点にいちばん近い海で起き、
  **津波を連れてくる唯一の地震**である（断層は割れない。破壊は海底の下だから）。
  津波は震央から同心円で広がる（DLC のマップ端の波ではなく、ゲーム自身の水ソルバで作る）。
  遠地の火災・倒壊、長周期地震動（既定 OFF）、合成記象（既定 OFF）、カメラの揺れの補正。
- **火災旋風** — 密集した大規模火災から、その場に留まる炎の渦が自然発生する。
  周囲に火の粉を撒いて延焼を広げる。**意図的に起こすことはできない** ——
  大火事の結果として生まれる現象なので、災害パネルにボタンは無い。
  出ないときは診断（F11）が「あと何棟足りないか」を教える。
  セーブ・ロードで生存中の旋風は残り寿命ごと復元される。
- **台風** — マップの外で発生し、クリック地点へ近づき、通過して去る。
  眼と螺旋の腕を持つ自前の雲、風害、高潮と河川の氾濫、突風の局所被害、落雷、暴風雨の音。
  **進行方向の右が危険半円**（南半球は左。設定あり）。
- **火山** — 地面そのものが隆起して火口を持つ円錐になる。噴煙、火山雷、
  斜面を下る土煙、溶岩流（建物に着火する）、火山性微動。形態は 3 種。
  **地形の変化は不可逆で、セーブに残る。**
- **災害強度の解放** — 手動発生の強度スライダー上限を 10.0 から 25.5 に
  引き上げる（既定で有効。設定でオフにできる）。

各機能に個別の ON/OFF があり、被害の種類ごとに **0 まで下げられる強さのスライダー**
がある —— 演出だけ残して破壊を切る（その逆も）ことができる。

## ソースを読むときに

コメントは英語だが、5 つの機能を指す**丸数字**がそのまま使われている。
ファイルをまたいだ相互参照の短縮記号で、grep しやすいように字形のまま残してある。

| 記号 | 機能 | 主な場所 |
|---|---|---|
| ① | 天気予報 | `Game/Forecast/` |
| ② | 地震・津波 | `Game/Earthquake/`, `Core/Earthquake/` |
| ③ | 火災旋風 | `Game/FireWhirl/`, `Core/FireWhirl/` |
| ④ | 台風 | `Game/Typhoon/`, `Core/Typhoon/` |
| ⑤ | 火山 | `Game/Volcano/`, `Core/Volcano/` |

コメント中の **★** は「重要」、**★★** は「ここを踏むと壊れる」の印である。
`§A-3` のような参照は、ゲーム本体の IL を読んで確かめた事実をまとめた
設計文書（`docs/`）の節番号を指す。

`Core/` はエンジン非依存（UnityEngine もゲーム API も使わない）で、
net35 の MOD 本体と net8.0 の xunit プロジェクトの**両方**にコンパイルされる。
だからゲームを起動せずにルールを検証できる。

## 必要なもの

- Cities: Skylines 1
- **Natural Disasters DLC** — 火災旋風・地震・台風はバニラの災害機構を借りているため必須。
  （⑤火山だけは災害スロットを使わず地形を自分で書くので DLC 無しでも動くが、
  Workshop では DLC を必須として案内している。）
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

`DisasterPlus.dll` と `CitiesHarmony.API.dll`、`Locales\`、`Audio\` が Mods
フォルダへ配置される。ゲーム DLL の場所は環境変数 `CITIES_SKYLINES_MANAGED` で
上書きできる。未設定なら既定の Steam パスを使う。

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

## 音源

`Audio/erupting-volcano.wav`（44.1 kHz / 2ch / 16 bit / 36.1 秒）は⑤火山の噴火音
として MOD に同梱され、`build.ps1` が `Locales\` と同じように Mods フォルダへ
配置する。実行時に読み込まれ、バニラの効果音グループを通して鳴るので、
**ゲーム本体の「効果音」スライダーとミュートがそのまま効く**（MOD 側に音量つまみは無い）。

ファイルを消しても火山はこれまでどおり動く —— 噴火が無音になり、ログに 1 行残るだけである。

> **この音源はコードのライセンス（MIT）の対象外である。** 本 MOD の所有者が
> 用意したファイルで、**その配布条件の確認は所有者の責任**である。
> Workshop へ公開する成果物に第三者の音声を同梱することになるため、公開前に確かめること。

## ライセンス

MIT（`Audio/` 以下の音源を除く。上の「音源」を参照）
