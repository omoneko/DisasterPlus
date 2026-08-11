# Disaster +

Cities: Skylines（無印）向けの災害拡張 MOD。密集火災から火災旋風（Fire Whirl）が
自然発生し、その場に留まって延焼を撒く。

## 依存関係

- **Natural Disasters DLC** — 竜巻（`TornadoAI`）を借りて渦の見た目・音・避難行動を実現しているため必須。
  DLC を持たない都市では火災旋風機能は無効化される（設定画面にその旨を表示）。
- **[Harmony (CitiesHarmony)](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)** — 火災旋風をその場に固定するために
  `VortexAI.SimulationStep` に 1 箇所だけ Postfix パッチを当てている（`Game/FireWhirl/VortexPinPatch.cs`）。
  Harmony 本体（`HarmonyLib`）は同梱せず、`CitiesHarmony.API.dll` の shim のみを MOD フォルダに配置する。
  CitiesHarmony MOD が Steam Workshop 経由で実際の Harmony アセンブリを供給する。
  CitiesHarmony が導入されていない場合、火災旋風はバニラの竜巻と同様に移動してしまうが、
  MOD 自体はクラッシュせず警告ログを出すだけで動作を続ける。

## ビルド

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

`DisasterPlus.dll` と `CitiesHarmony.API.dll` の両方がビルドされ、Mods フォルダへ配置される。
