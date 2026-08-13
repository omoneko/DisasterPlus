# Disaster + — Phase 0.5：診断基盤 設計書

- 日付: 2026-08-12
- 対象: Cities: Skylines 1（Unity 5.6 / .NET 3.5 / C# 7.3）
- MOD: `DisasterPlus`（表示名「Disaster +」）
- 状態: 承認済み
- 前提: Phase 0 ＋ ③火災旋風が master にマージ済み（Core テスト 59 件、ビルド警告 0、**実機テスト未実施**）

---

## 1. なぜこれを作るのか

### 1.1 方針変更

当初は「1機能ごとに実機確認」の計画だったが、**全5機能（①天気予報・②地震・③火災旋風・④台風・⑤火山）を通貫で実装し、最後にまとめて実機確認する**方針に変更した。実機確認はユーザーの時間を使うため、まとめた方が効率が良い。

代償として、**最後の実機確認が唯一の検証機会**になる。そこで「何かおかしい」となったとき、5機能ぶんの相互作用から原因を探すことになる。本基盤はその切り分けコストを下げるために作る。

### 1.2 ③火災旋風で実証された問題

`Game` 層はユニットテストできない。③の実装中、レビューが捕まえた実バグは4件：

| 発見 | 内容 |
|---|---|
| Task 7 | 言語切替が例外時に恒久固着する |
| Task 10 | prefab 未解決時に毎tick全走査＋未スロットルのログ洪水 |
| Task 11 | 災害ID再利用でバニラの竜巻を固定してしまう |
| Task 15 | 都市Aの旋風が都市Bに湧きうる |

さらに深刻なのは、**IL から読んだ前提が3件も誤っていた**こと：

| 誤っていた前提 | 実際 | 症状 |
|---|---|---|
| `DAYTIME_FRAMES` = 262144 | **65536** | 全ての時間設定が 4 倍長く動く |
| `m_targetPos0` は遠方の目標 | **火災の中心そのもの** | 終了処理が原理的に発火しない |
| `m_fireIntensity` 直接書込で着火 | `CommonBuildingAI` 派生以外は誰も処理しない | セーブに焼き付く永久の幽霊火災 |

**共通点は「前提が破れても何も起きないこと」。** ゲームは何事もなく動き、挙動だけが静かに違う。これが本基盤の設計を決めている。

### 1.3 目的

1. 前提が破れたことを**その場で検出して名指しでログに出す**
2. 実機で「何が起きているか」を**画面上で直接見られる**ようにする
3. 「動かない」を「どの機能のどの段階で止まっているか」に変える

---

## 2. 決定事項（ブレインストーミングで確定）

| 論点 | 決定 | 理由 |
|---|---|---|
| オーバーレイの描画方式 | **Unity IMGUI（`OnGUI`）** | CS の UI フレームワークに依存しない。診断ツールが診断対象と同じ仕組みに依存するのは筋が悪い |
| 通常プレイヤーへの既定 | **層ごとに変える**（下記 2.1） | 前提検証はコストほぼゼロで価値が最大 |
| 前提 FAIL の見せ方 | **ログ ＋ 設定画面の警告**（ゲーム内ポップアップはしない） | 誤検出時の害が小さい。DLC 注記と同じ前例がある |
| ホットキー | **設定可能なキーは 1 つ**（F9〜F12 のドロップダウン、既定 F11）。そのキー単独でオーバーレイ、`Ctrl` 併用でダンプ | CS のキーマッピング UI は自前実装が必要で割に合わない |
| ダンプ先 | MOD フォルダ直下 `DisasterPlus-diagnostics.txt`（上書き） | パスをログに出すので迷わない |

### 2.1 層ごとの既定値

| 層 | 既定 | コスト | 切れるか |
|---|---|---|---|
| **前提検証** | **常時 ON** | 起動時 1 回のみ | **切れない** |
| 機能ごとのログ | OFF | ON にした機能のぶんだけ | 設定から個別に |
| オーバーレイ | OFF | 毎フレーム描画（GC あり） | 設定から |

**前提検証を切れなくするのは意図的。** 前提が破れたことを誰も知らないまま動き続けるのが最悪の状態であり、コストがほぼゼロだから。

---

## 3. アーキテクチャ

### 3.1 ディレクトリ

```
src/DisasterPlus/
  Core/Diagnostics/            エンジン非依存・全部ユニットテスト対象
    DiagnosticLine.cs            1 行（インデント・ラベル・値）
    DiagnosticSection.cs         1 機能ぶんのセクション（名前・健全性・行）
    DiagnosticReport.cs          不変スナップショット
    AssumptionResult.cs          前提 1 件の結果
    FeatureHealth.cs             Healthy / Degraded / Disabled
    LogChannel.cs                チャンネルのビットフラグと判定
    DiagnosticFormatter.cs       レポート → テキスト行（オーバーレイとダンプで共用）
  Game/Diagnostics/            CS/Unity に触る層
    DiagnosticBuilder.cs         行の組み立て（sim スレッドで使う）
    DiagnosticsHub.cs            唯一の共有状態。snapshot-then-render
    Assumptions.cs               前提検証の定義と実行
    DiagnosticOverlay.cs         MonoBehaviour + OnGUI
    DiagnosticDump.cs            ファイル書き出し
```

### 3.2 スレッド境界

**`OnGUI` は 1 フレームに複数回呼ばれる**（レイアウト用と描画用）。ここで状態収集をすると無駄なうえ、sim スレッドが所有する状態を素手で触ることになる。

`FireWhirlRegistry` で実証済みの **snapshot-then-render** をそのまま使う：

```
sim スレッド                        main スレッド
─────────────                      ─────────────
各機能が自分の状態を書き出す
  ↓
DiagnosticBuilder が行を積む
  ↓
DiagnosticsHub.Publish(report)  ──►  DiagnosticsHub.Latest
   （lock）                              （lock、複製を返す）
                                          ↓
                                     DiagnosticOverlay.OnGUI が描く
```

- `DiagnosticReport` は**不変**。`DiagnosticsHub` は内部リストへの参照を絶対に外へ出さない
- ロックは 1 つ（net35 に `System.Collections.Concurrent` は無い）
- **収集はオーバーレイが開いているか、ダンプ要求があるときだけ走る。** 閉じていればコストはゼロ
- ホットキー判定は `Update`（main）で行う。`OnGUI` では**描画だけ**する

### 3.3 なぜ IMGUI か

CS の UI フレームワーク（`UIPanel` / `UILabel`）はゲームに馴染む見た目になるが、親パネルの探索が要る。**③の実装でこの探索に 2 回ハマっており、「ボタンが実際に出るか」は未検証の宿題として残っている**（`FindObjectOfType` は Unity 5.6 で非アクティブな GameObject を返さない）。

診断ツールは**他の全部が動かないときに動いてほしいもの**なので、依存をゼロに寄せる。見た目の良さは開発者向けツールにはほぼ価値がない。

---

## 4. 前提検証（Assumptions）

### 4.1 データとして持つ

散らばった `if` ではなく、1 ファイルにリストとして置く。1 件は 3 つ組：

- **名前** — 何を確かめているか
- **影響** — 破れたときに何が壊れるか（プレイヤーに見せる文）
- **述語** — 実際のゲームに問い合わせて真偽を返す

1 ファイルに集約するのは、**設計書 付録A と突き合わせて監査できる**ようにするため。②〜⑤で前提が増えたらここに足す。

### 4.2 初期の検証項目

Phase 0 ＋ ③ が依存している前提。

| 名前 | 破れたときの影響 |
|---|---|
| `SimulationManager.DAYTIME_FRAMES == 65536` | 全ての時間設定が実際とズレる |
| `VortexAI.SimulationStep`(6引数) にパッチが適用されている | 火災旋風がその場に留まらず流れていく |
| `BuildingAI.BurnBuilding` が解決できる | 延焼拡大が動作しない |
| `TornadoAI` を持つ `DisasterInfo` prefab が見つかる | 火災旋風が発生しない |
| `DisastersOptionPanel` のスライダーが見つかる | 災害強度 25.5 が使えない |
| Natural Disasters DLC を所持している | ①②③④ が無効（⑤火山のみ動作） |

**注意:** 「DLC 未所持」は前提の破れではなく正常な構成である。FAIL ではなく情報として扱い、設定画面の警告にも出さない（既に DLC 注記があるため）。

### 4.3 実行と安全性

- レベルロード完了後に **1 回だけ**実行する（Harmony の適用状況を見るため、起動時ではなくロード後）
- **各検証を個別に try/catch** する。例外を投げた検証は FAIL として報告し、起動を壊さない
- 結果は 3 箇所へ流れる: ログ（常時）／オーバーレイ／設定画面先頭の警告
- ログ出力の形は次のとおり

```
[DisasterPlus] ASSUMPTIONS  5 passed, 1 FAILED
[DisasterPlus]   PASS  SimulationManager.DAYTIME_FRAMES == 65536
[DisasterPlus]   PASS  VortexAI.SimulationStep(6) patched
[DisasterPlus]   PASS  BuildingAI.BurnBuilding resolved
[DisasterPlus]   PASS  TornadoAI prefab found
[DisasterPlus]   FAIL  DisastersOptionPanel slider not found
[DisasterPlus]         -> disaster intensity cannot be unlocked to 25.5
```

### 4.4 設定画面の警告

FAIL が 1 件以上あるとき、設定画面の**先頭**にグループを 1 つ足し、影響文を列挙する。DLC 未所持の注記と同じ仕組み・同じ場所。

`OnSettingsUI` はメインメニュー起動時に 1 回だけ走り、レベルロード後の情報は使えない。前提検証はレベルロード後に走るため、**警告はその都市を一度読み込んだ後、次にオプションを開いたときに反映される**。この制約はコードにコメントで明記する。

---

## 5. ログチャンネル

### 5.1 互換性を壊さない

既存の `Log.Diag(key, msg)` は**そのまま残す**。`Log.Diag(channel, key, msg)` を追加し、チャンネル指定なしの呼び出しは `General` 扱いとする。既存の呼び出し箇所を書き換えずに済む。

`Log.Info` / `Warn` / `Error` は**常に出る**。これらは常に意味がある情報であり、チャンネルで抑制しない。

### 5.2 チャンネル

```
General / FireWhirl / Forecast / Earthquake / Typhoon / Volcano / Diagnostics
```

②〜⑤は未実装だが、**チャンネルは先に定義しておく**（各フェーズで追加する手間を省く）。設定画面には実装済みの機能ぶんだけ出す。

マスクは `SavedInt`（ビットフラグ）。**既定は `General` のみ ON、機能チャンネルは全て OFF。**

**既定を「全 OFF」にしてはいけない。** 既存の `Log.Diag(key, msg)` 呼び出しは `General` 扱いになるため、
全 OFF にすると現在出ているスロットル済み診断ログが黙って消える。`docs/playtest-checklist.md` は
その一部（例: `DIAG fireWhirl: burning=N active=M`）を実機確認の手順として参照している。

**本フェーズでは既存の `Log.Diag` 呼び出しを機能チャンネルへ移行しない。** `General` のまま残す。
機能チャンネルは、②〜⑤で新たに書く詳細ログのために使う。移行すると既定 OFF になり、
実機確認の手順が壊れるため。

**保存値は公開契約。** チャンネルのビット位置は凍結し、廃止するときも番号を詰めない。列挙のメンバー名と順序を凍結扱いにする旨を定義箇所にコメントする。

---

## 6. 劣化（Degraded）の記録

`FeatureHost` は既に機能ごとに try/catch しているが、いまは 1 行ログを出すだけ。

- **例外の回数と直近の内容を機能ごとに記録**する
- オーバーレイに `Degraded (3 errors, last: NullReferenceException in OnSimulationTick)` のように出す
- **呼び出しは止めない**（一過性かもしれない）。ただし記録は残す
- レベルアンロードでリセット

これにより「火山だけ死んでいる」が一目で分かる。

---

## 7. オーバーレイ

### 7.1 表示内容

```
Disaster +  [F11]
  DLC:ND ✔   NDR:検出   Harmony:適用済   Level:ready
  ASSUMPTIONS  5 passed, 1 FAILED
    FAIL  DisastersOptionPanel slider not found

  -- Fire whirl --------------  Healthy
     scan: burning 34 / sweep 3-8
     active 2:
       #4021  (1204,890)  r=118  3.2/10min  pinned  n=22
       #4038  ( 380,152)  r= 47  0.4/10min  pinned  n=13  manual
     cooldown 1
```

### 7.2 制約

- **ASCII のみ。** IMGUI の既定フォントは日本語グリフを持たない可能性が高く、豆腐になる。ローカライズしない（開発者向けツールであり、翻訳する価値がない）
- 画面左上固定、半透明の背景。マウス入力は取らない（ゲーム操作を妨げない）
- 1 フレームに複数回呼ばれる `OnGUI` では**描画のみ**。状態取得・ホットキー判定は `Update`

### 7.3 ライフサイクル

レベルロードで GameObject を生成、アンロードで破棄。都市を跨いで持ち越さない。

`UnityEngine.Object` の fake-null に注意する。**コレクションではなくオブジェクト自体を `== null` で判定**すること（コレクションは破棄済みオブジェクトを抱えたまま非 null であり続ける）。

---

## 8. 状態ダンプ

`Ctrl+F11` で MOD フォルダ直下 `DisasterPlus-diagnostics.txt` に書き出す（上書き）。書き出したパスを `Log.Info` に出す。

内容：MOD バージョン／ゲームバージョン／DLC 所持／検出した競合 MOD／Harmony 適用状況／前提検証の全結果／全機能の診断セクション／劣化記録。

オーバーレイと**同じ `DiagnosticFormatter`** を使う。表示とダンプで内容が食い違わない。

---

## 9. 既存コードへの変更

| ファイル | 変更 |
|---|---|
| `Core/` | `Diagnostics/` を新規追加。既存の Core には触らない |
| `IDisasterFeature` | `WriteDiagnostics(DiagnosticBuilder)` を追加。**以後の全機能に診断実装を強制する** |
| `FireWhirlFeature` | `WriteDiagnostics` を実装 |
| `FeatureHost` | 劣化記録、診断収集の呼び出し、`_levelReady` と連動 |
| `Log` | チャンネル対応のオーバーロードとマスク |
| `ModSettings` | デバッグ設定（オーバーレイ ON/OFF、チャンネルマスク、ホットキー） |
| `Mod` | 「デバッグ」グループ、設定画面先頭の前提警告 |
| `Strings` / `Locales` | 設定画面の文字列（**オーバーレイの中身は対象外**） |
| `DisasterPlusLoading` | オーバーレイ GameObject の生成・破棄、前提検証の実行 |

---

## 10. テスト

`Core/Diagnostics` はユニットテスト対象：

| 対象 | ケース |
|---|---|
| `DiagnosticFormatter` | インデント・整列・空セクション・長い値の切り詰め・改行を含む値 |
| `LogChannel` | マスク判定・複数チャンネル・未定義ビット・0 マスクで全 OFF・**既定マスクで `General` が ON かつ機能チャンネルが OFF** |
| `DiagnosticReport` | 組み立てが不変であること・セクション順が入力順で決まること |
| `AssumptionResult` | PASS/FAIL の集計 |
| 保存値の不変条件 | チャンネルのビット位置が変わらないこと |

`Game` 側は従来どおりユニットテスト不可。**前提検証そのものが実機での自己テストになる**のが本基盤の要点。

---

## 11. 完了条件

- `build.ps1` が警告 0 で通り、Core テストが全て緑
- 設定画面に「デバッグ」グループが出る（日英）
- `F11` でオーバーレイが開閉し、火災旋風の生存状況がリアルタイムに見える
- `Ctrl+F11` でダンプファイルが生成され、パスがログに出る
- 前提検証の結果が起動ログに出る。**わざと 1 件壊した状態**（例: CitiesHarmony を無効化）で FAIL が出て、設定画面にも警告が出る
- 機能ログを個別に ON/OFF でき、OFF のときログが出ない
- **2 つ目の都市をロードしてもオーバーレイが正常に動く**（静的キャッシュと fake-null の検証）
- オーバーレイを閉じているとき、診断のための状態収集が走らない

---

## 12. 本基盤を使う次のフェーズ

Phase 0.5 完了後、①天気予報 → ②地震 → ④台風 → ⑤火山 を通貫で実装する。各フェーズは：

- 新しい前提を `Assumptions` に追加する
- `WriteDiagnostics` を実装する（`IDisasterFeature` が強制する）
- 自分のログチャンネルを使う

実機確認は全機能の実装完了後にまとめて 1 回。`docs/playtest-checklist.md` に③のぶんが既にあり、各フェーズで追記していく。
