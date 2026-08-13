# Phase 0.5 診断基盤 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全5機能を通貫実装して最後にまとめて実機確認する方針のために、「どこで壊れているか」を実機で特定できる診断基盤を用意する。

**Architecture:** `Core/Diagnostics/` に不変の値型・チャンネル判定・整形（全てユニットテスト対象）を置き、`Game/Diagnostics/` に CS/Unity に触る層を置く。オーバーレイは CS の UI フレームワークに依存しない Unity IMGUI。状態は `FireWhirlRegistry` と同じ snapshot-then-render で sim → main へ渡す。設計書 付録A の前提を実行時にゲームへ問い合わせて照合する仕組みが中核。

**Tech Stack:** C# 7.3 / .NET Framework 3.5（MOD 本体）、Unity 5.6 IMGUI、xunit / net8.0（Core テスト）、MSBuild、PowerShell。

**Spec:** `docs/superpowers/specs/2026-08-12-diagnostics-design.md`

## Global Constraints

- **.NET Framework 3.5 / C# 7.3**。C# 8+ 構文（switch 式、`??=`、範囲演算子、`using` 宣言、null 許容参照型）は**コンパイルエラー**になる
- **Unity 5.6**。それ以降で追加された API は存在しない。使う前に `docs/tools/ilload.ps1` で `UnityEngine.dll` をリフレクションして確認する
- `Core/` は `UnityEngine`・CS API・`System.Random`・**LINQ** を参照しない。`System.Collections.Generic` と `System.Math` まで
- net35 に `System.Collections.Concurrent` は無い。共有状態は単一の `lock`
- **スレッド境界**: 建物・車両・災害バッファの生成と変更は sim スレッド（`OnAfterSimulationTick` / `SimulationManager.AddAction`）。GameObject・Material・IMGUI・UI は main スレッド。破ると自分の try/catch に掛からないスタックトレース無しの `IndexOutOfRangeException` ポップアップが後から出る
- **`OnGUI` は 1 フレームに複数回呼ばれる**（レイアウトと描画）。状態取得とホットキー判定は `Update` で行い、`OnGUI` では描画のみ
- 全ての `Game/` ファイルは、サブフォルダに関わらず名前空間 `DisasterPlus.Game`
- `Log.Diag(key, msg)` のキーは短い静的文字列。エンティティごとの動的文字列は禁止（スロットル辞書は `Log.Reset()` でしか消えない）
- セッション状態はレベルアンロードで全てリセットする
- 保存設定は公開契約。ビット位置・列挙値を詰め直さない
- `Locales/*.txt` は UTF-8 **BOM なし**。日本語コメントを含む `.ps1` は UTF-8 **BOM あり**
- **PowerShell の `Get-Content -Raw` → `Set-Content` でファイルを編集しない**（日本語コメントが文字化けする）。Write/Edit ツールを使う
- コミットは `<type>: <説明>` 形式（feat/fix/docs/test/chore/refactor）。本文は日本語で可
- **`Strings` / `Locales/en.txt` / `Locales/ja.txt` のキー集合は常に一致させる**（現在 18 件）。追加したら `tools/GenerateLocaleTemplate.ps1` で `en.txt` を再生成する

## 既存コードで前提にしてよい事実

- `Core/`: `Vec2`, `Vec3`, `FireWhirlConfig`, `FireWhirlLifecycle`, `FireWhirlStrength`, `IgnitionSpread.MaxStrength`
- `Game/`: `Log`（`Info`/`Warn`/`Error(string,Exception)`/`Diag(key,msg)`/`Reset()`）、`ModSettings`（`Ensure()`, `FileName = "DisasterPlusSettings"`）、`ModCompat`（`NdrPresent`, `NaturalDisastersOwned`、どちらもキャッシュ済み）、`Strings`, `LocaleLoader.Apply()`
- `IDisasterFeature`: `Name`, `OnLevelLoaded()`, `OnSimulationTick(uint, float)`, `OnMainThreadUpdate()`, `OnLevelUnloading()`
- `FeatureHost`: `Features`, `Register()`, `LevelLoaded()`, `SimulationTick()`, `MainThreadUpdate()`, `LevelUnloading()`。既に機能ごとに try/catch し、`_levelReady` で準備完了までディスパッチを止めている
- `FireWhirlRegistry`: `Snapshot() → List<FireWhirlView>`、`Count`。`FireWhirlView` は不変 struct（`DisasterId`, `VehicleId`, `Center`, `Radius`, `BurningCount`, `ElapsedMinutes`, `Ending`, `Manual`）
- `BurningBuildingScanner`, `FireWhirlSpawner`, `HarmonyBootstrap.Installed`
- `SimulationManager.DAYTIME_FRAMES == 65536`（IL 確認済み）

---

## File Structure

### 新規作成

```
src/DisasterPlus/Core/Diagnostics/
  FeatureHealth.cs         enum Healthy / Degraded / Disabled
  DiagnosticLine.cs        1 行（インデント・ラベル・値）
  DiagnosticSection.cs     1 機能ぶん（名前・健全性・注記・行）
  AssumptionResult.cs      前提 1 件の結果
  DiagnosticReport.cs      不変スナップショット
  LogChannel.cs            ビットフラグとマスク判定
  DiagnosticFormatter.cs   レポート → テキスト行

src/DisasterPlus/Game/Diagnostics/
  DiagnosticBuilder.cs     行の組み立て（sim スレッドで使う）
  DiagnosticsHub.cs        唯一の共有状態。snapshot-then-render
  Assumptions.cs           前提検証の定義と実行
  DiagnosticOverlay.cs     MonoBehaviour + OnGUI
  DiagnosticDump.cs        ファイル書き出し

tests/DisasterPlus.Core.Tests/Diagnostics/
  LogChannelTests.cs
  DiagnosticFormatterTests.cs
  DiagnosticReportTests.cs
```

### 変更

```
src/DisasterPlus/Game/IDisasterFeature.cs            WriteDiagnostics を追加
src/DisasterPlus/Game/Common/FeatureHost.cs          劣化記録・診断収集
src/DisasterPlus/Game/Common/Log.cs                  チャンネル対応
src/DisasterPlus/Game/Common/DisasterPlusLoading.cs  オーバーレイ生成/破棄・前提検証実行
src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs  WriteDiagnostics 実装
src/DisasterPlus/Game/ModSettings.cs                 デバッグ設定
src/DisasterPlus/Game/Mod.cs                         デバッググループ・前提警告
src/DisasterPlus/Game/Localization/Strings.cs        設定画面の文字列
Locales/en.txt, Locales/ja.txt                       同上
```

### 責務の境界

- `Core/Diagnostics` は純粋。値型は全て不変、`DiagnosticFormatter` は純関数
- `DiagnosticsHub` が唯一の共有可変状態。単一の `lock`、外へは不変コピーのみ
- `Assumptions` はゲームに問い合わせるだけで、判定結果の解釈はしない
- `DiagnosticOverlay` は描画のみ。状態を持たない

---

## Task 1: Core の値型とログチャンネル

**Files:**
- Create: `src/DisasterPlus/Core/Diagnostics/FeatureHealth.cs`
- Create: `src/DisasterPlus/Core/Diagnostics/DiagnosticLine.cs`
- Create: `src/DisasterPlus/Core/Diagnostics/DiagnosticSection.cs`
- Create: `src/DisasterPlus/Core/Diagnostics/AssumptionResult.cs`
- Create: `src/DisasterPlus/Core/Diagnostics/DiagnosticReport.cs`
- Create: `src/DisasterPlus/Core/Diagnostics/LogChannel.cs`
- Create: `tests/DisasterPlus.Core.Tests/Diagnostics/LogChannelTests.cs`
- Create: `tests/DisasterPlus.Core.Tests/Diagnostics/DiagnosticReportTests.cs`

**Interfaces:**
- Consumes: なし
- Produces（後続タスクがこの名前と型に依存する）:
  - `DisasterPlus.Core.Diagnostics.FeatureHealth` — `enum { Healthy, Degraded, Disabled }`
  - `DiagnosticLine` — `struct`、`readonly int Indent`、`readonly string Label`、`readonly string Value`、ctor `DiagnosticLine(int indent, string label, string value)`
  - `DiagnosticSection` — `class`、`readonly string Name`、`readonly FeatureHealth Health`、`readonly string Note`、`readonly IList<DiagnosticLine> Lines`、ctor `DiagnosticSection(string name, FeatureHealth health, string note, IList<DiagnosticLine> lines)`
  - `AssumptionResult` — `struct`、`readonly string Name`、`readonly bool Passed`、`readonly string Impact`、ctor `AssumptionResult(string name, bool passed, string impact)`
  - `DiagnosticReport` — `class`、`readonly IList<DiagnosticLine> Header`、`readonly IList<AssumptionResult> Assumptions`、`readonly IList<DiagnosticSection> Sections`、ctor `DiagnosticReport(IList<DiagnosticLine> header, IList<AssumptionResult> assumptions, IList<DiagnosticSection> sections)`、`int PassedCount`、`int FailedCount`、`bool HasFailures`
  - `LogChannel` — `static class`。`const int General = 1`, `FireWhirl = 2`, `Forecast = 4`, `Earthquake = 8`, `Typhoon = 16`, `Volcano = 32`, `Diagnostics = 64`, `const int DefaultMask = General`、`static bool IsEnabled(int channel, int mask)`

- [ ] **Step 1: 失敗するテストを書く**

`tests/DisasterPlus.Core.Tests/Diagnostics/LogChannelTests.cs`:

```csharp
using DisasterPlus.Core.Diagnostics;
using Xunit;

namespace DisasterPlus.Core.Tests.Diagnostics
{
    public class LogChannelTests
    {
        [Fact]
        public void IsEnabled_ChannelInMask_ReturnsTrue()
        {
            Assert.True(LogChannel.IsEnabled(LogChannel.FireWhirl,
                LogChannel.General | LogChannel.FireWhirl));
        }

        [Fact]
        public void IsEnabled_ChannelNotInMask_ReturnsFalse()
        {
            Assert.False(LogChannel.IsEnabled(LogChannel.Volcano,
                LogChannel.General | LogChannel.FireWhirl));
        }

        [Fact]
        public void IsEnabled_ZeroMask_EverythingOff()
        {
            Assert.False(LogChannel.IsEnabled(LogChannel.General, 0));
            Assert.False(LogChannel.IsEnabled(LogChannel.Volcano, 0));
        }

        [Fact]
        public void DefaultMask_EnablesGeneralOnly()
        {
            // 既存の Log.Diag(key, msg) 呼び出しは General 扱いになる。
            // ここを 0 にすると現在出ている診断ログが黙って消え、
            // docs/playtest-checklist.md の手順が壊れる。
            Assert.True(LogChannel.IsEnabled(LogChannel.General, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.FireWhirl, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Forecast, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Earthquake, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Typhoon, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Volcano, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Diagnostics, LogChannel.DefaultMask));
        }

        [Fact]
        public void BitPositions_AreFrozen()
        {
            // 保存値は公開契約。ビット位置を変えると既存プレイヤーの
            // .cgs に入っている値が別の意味になる。廃止するときも詰めない。
            Assert.Equal(1, LogChannel.General);
            Assert.Equal(2, LogChannel.FireWhirl);
            Assert.Equal(4, LogChannel.Forecast);
            Assert.Equal(8, LogChannel.Earthquake);
            Assert.Equal(16, LogChannel.Typhoon);
            Assert.Equal(32, LogChannel.Volcano);
            Assert.Equal(64, LogChannel.Diagnostics);
        }

        [Fact]
        public void IsEnabled_UndefinedBit_ReturnsFalseAgainstDefault()
        {
            Assert.False(LogChannel.IsEnabled(1 << 20, LogChannel.DefaultMask));
        }

        [Fact]
        public void IsEnabled_MultipleChannelsAtOnce_RequiresAll()
        {
            int mask = LogChannel.General | LogChannel.Volcano;
            Assert.True(LogChannel.IsEnabled(LogChannel.General | LogChannel.Volcano, mask));
            Assert.False(LogChannel.IsEnabled(LogChannel.General | LogChannel.Typhoon, mask));
        }

        [Fact]
        public void IsEnabled_ZeroChannel_ReturnsFalse()
        {
            Assert.False(LogChannel.IsEnabled(0, LogChannel.DefaultMask));
        }
    }
}
```

`tests/DisasterPlus.Core.Tests/Diagnostics/DiagnosticReportTests.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;
using Xunit;

namespace DisasterPlus.Core.Tests.Diagnostics
{
    public class DiagnosticReportTests
    {
        private static DiagnosticReport Build(params AssumptionResult[] assumptions)
        {
            return new DiagnosticReport(
                new List<DiagnosticLine>(),
                new List<AssumptionResult>(assumptions),
                new List<DiagnosticSection>());
        }

        [Fact]
        public void Counts_AreDerivedFromAssumptions()
        {
            var r = Build(
                new AssumptionResult("a", true, ""),
                new AssumptionResult("b", false, "x breaks"),
                new AssumptionResult("c", true, ""));

            Assert.Equal(2, r.PassedCount);
            Assert.Equal(1, r.FailedCount);
            Assert.True(r.HasFailures);
        }

        [Fact]
        public void NoFailures_HasFailuresIsFalse()
        {
            Assert.False(Build(new AssumptionResult("a", true, "")).HasFailures);
        }

        [Fact]
        public void EmptyReport_IsSafe()
        {
            var r = Build();
            Assert.Equal(0, r.PassedCount);
            Assert.Equal(0, r.FailedCount);
            Assert.False(r.HasFailures);
        }

        [Fact]
        public void NullCollections_AreTreatedAsEmpty()
        {
            // Game 層がうっかり null を渡してもオーバーレイが落ちないこと。
            var r = new DiagnosticReport(null, null, null);
            Assert.NotNull(r.Header);
            Assert.NotNull(r.Assumptions);
            Assert.NotNull(r.Sections);
            Assert.False(r.HasFailures);
        }

        [Fact]
        public void SectionOrder_FollowsInputOrder()
        {
            var sections = new List<DiagnosticSection>
            {
                new DiagnosticSection("B", FeatureHealth.Healthy, null, null),
                new DiagnosticSection("A", FeatureHealth.Healthy, null, null),
            };
            var r = new DiagnosticReport(null, null, sections);
            Assert.Equal("B", r.Sections[0].Name);
            Assert.Equal("A", r.Sections[1].Name);
        }

        [Fact]
        public void Section_NullLines_AreTreatedAsEmpty()
        {
            var s = new DiagnosticSection("X", FeatureHealth.Disabled, null, null);
            Assert.NotNull(s.Lines);
            Assert.Empty(s.Lines);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'LogChannel' could not be found`

- [ ] **Step 3: 値型を実装する**

`src/DisasterPlus/Core/Diagnostics/FeatureHealth.cs`:

```csharp
namespace DisasterPlus.Core.Diagnostics
{
    public enum FeatureHealth
    {
        Healthy,
        Degraded,
        Disabled,
    }
}
```

`src/DisasterPlus/Core/Diagnostics/DiagnosticLine.cs`:

```csharp
namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>診断出力の 1 行。Indent は整形時の字下げ段数。</summary>
    public struct DiagnosticLine
    {
        public readonly int Indent;
        public readonly string Label;
        public readonly string Value;

        public DiagnosticLine(int indent, string label, string value)
        {
            Indent = indent < 0 ? 0 : indent;
            Label = label ?? "";
            Value = value ?? "";
        }
    }
}
```

`src/DisasterPlus/Core/Diagnostics/DiagnosticSection.cs`:

```csharp
using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>1 機能ぶんの診断。Note は劣化理由など。</summary>
    public class DiagnosticSection
    {
        public readonly string Name;
        public readonly FeatureHealth Health;
        public readonly string Note;
        public readonly IList<DiagnosticLine> Lines;

        public DiagnosticSection(string name, FeatureHealth health, string note, IList<DiagnosticLine> lines)
        {
            Name = name ?? "";
            Health = health;
            Note = note ?? "";
            // DiagnosticReport と同じ理由で複製する（呼び出し側の後からの変更を遮断）。
            Lines = lines == null ? new List<DiagnosticLine>() : new List<DiagnosticLine>(lines);
        }
    }
}
```

`src/DisasterPlus/Core/Diagnostics/AssumptionResult.cs`:

```csharp
namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// 前提 1 件の検証結果。
    /// Impact は「破れたときに何が壊れるか」で、プレイヤーにそのまま見せる文。
    /// </summary>
    public struct AssumptionResult
    {
        public readonly string Name;
        public readonly bool Passed;
        public readonly string Impact;

        public AssumptionResult(string name, bool passed, string impact)
        {
            Name = name ?? "";
            Passed = passed;
            Impact = impact ?? "";
        }
    }
}
```

`src/DisasterPlus/Core/Diagnostics/DiagnosticReport.cs`:

```csharp
using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// 診断の不変スナップショット。sim スレッドで作り、main スレッドで読む。
    /// 一度作ったら書き換えない。
    /// </summary>
    public class DiagnosticReport
    {
        public readonly IList<DiagnosticLine> Header;
        public readonly IList<AssumptionResult> Assumptions;
        public readonly IList<DiagnosticSection> Sections;

        private readonly int _passed;
        private readonly int _failed;

        public DiagnosticReport(
            IList<DiagnosticLine> header,
            IList<AssumptionResult> assumptions,
            IList<DiagnosticSection> sections)
        {
            // null を渡されてもオーバーレイが毎フレーム落ちないようにする。
            //
            // 参照をそのまま持たず複製する。呼び出し側が渡したリストを後から
            // 書き換えたり空にしたりしても、公開済みのレポートが変わらないため。
            // 具体的には FeatureHost.BuildReport が Assumptions.LastResults という
            // 長命な静的リストを渡し、それは Assumptions.Reset() でクリアされる。
            // 複製しないと、アンロード後にオーバーレイが空の Assumptions を
            // 古い PassedCount と一緒に読むことになる。
            Header = header == null
                ? new List<DiagnosticLine>() : new List<DiagnosticLine>(header);
            Assumptions = assumptions == null
                ? new List<AssumptionResult>() : new List<AssumptionResult>(assumptions);
            Sections = sections == null
                ? new List<DiagnosticSection>() : new List<DiagnosticSection>(sections);

            for (int i = 0; i < Assumptions.Count; i++)
            {
                if (Assumptions[i].Passed) _passed++;
                else _failed++;
            }
        }

        public int PassedCount { get { return _passed; } }
        public int FailedCount { get { return _failed; } }
        public bool HasFailures { get { return _failed > 0; } }
    }
}
```

- [ ] **Step 4: LogChannel を実装する**

`src/DisasterPlus/Core/Diagnostics/LogChannel.cs`:

```csharp
namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// ログチャンネルのビットフラグ。
    ///
    /// このビット位置は .cgs に保存される公開契約であり、凍結扱いとする。
    /// 値を詰め直したり並べ替えたりしてはいけない。チャンネルを廃止するときも
    /// ビットを残し、UI から外すだけにする。
    /// </summary>
    public static class LogChannel
    {
        public const int General    = 1;
        public const int FireWhirl  = 2;
        public const int Forecast   = 4;
        public const int Earthquake = 8;
        public const int Typhoon    = 16;
        public const int Volcano    = 32;
        public const int Diagnostics = 64;

        /// <summary>
        /// 既定マスク。General だけを有効にする。
        ///
        /// 0 にしてはいけない。チャンネル指定のない既存の Log.Diag(key, msg) は
        /// General 扱いになるため、0 にすると現在出ている診断ログが黙って消え、
        /// docs/playtest-checklist.md が参照している実機確認の手順が壊れる。
        /// </summary>
        public const int DefaultMask = General;

        /// <summary>channel のビットが全て mask に立っていれば true。</summary>
        public static bool IsEnabled(int channel, int mask)
        {
            if (channel == 0) return false;
            return (mask & channel) == channel;
        }
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 73 件すべて PASS（既存 59 件 + 新規 14 件）

- [ ] **Step 6: コミット**

```bash
git add src/DisasterPlus/Core/Diagnostics tests/DisasterPlus.Core.Tests/Diagnostics
git commit -m "feat: 診断の値型とログチャンネル"
```

---

## Task 2: 整形（オーバーレイとダンプで共用）

**Files:**
- Create: `src/DisasterPlus/Core/Diagnostics/DiagnosticFormatter.cs`
- Create: `tests/DisasterPlus.Core.Tests/Diagnostics/DiagnosticFormatterTests.cs`

**Interfaces:**
- Consumes: `DiagnosticReport`, `DiagnosticSection`, `DiagnosticLine`, `AssumptionResult`, `FeatureHealth`（Task 1）
- Produces:
  - `DiagnosticFormatter` — `static class`。`static List<string> Format(DiagnosticReport report)`、`const int MaxValueLength = 120`

**なぜ Core に置くか:** オーバーレイとダンプが**同じ整形結果**を使うため。表示とファイルで内容が食い違わない。純関数なのでユニットテストできる。

- [ ] **Step 1: 失敗するテストを書く**

`tests/DisasterPlus.Core.Tests/Diagnostics/DiagnosticFormatterTests.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;
using Xunit;

namespace DisasterPlus.Core.Tests.Diagnostics
{
    public class DiagnosticFormatterTests
    {
        private static string Joined(DiagnosticReport r)
        {
            return string.Join("\n", DiagnosticFormatter.Format(r).ToArray());
        }

        [Fact]
        public void EmptyReport_ProducesNoCrashAndNoSections()
        {
            var lines = DiagnosticFormatter.Format(new DiagnosticReport(null, null, null));
            Assert.NotNull(lines);
        }

        [Fact]
        public void NullReport_ProducesEmptyList()
        {
            // オーバーレイは publish 前に一度描画されうる。
            var lines = DiagnosticFormatter.Format(null);
            Assert.NotNull(lines);
            Assert.Empty(lines);
        }

        [Fact]
        public void HeaderLines_AppearBeforeSections()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "DLC", "ND ok") },
                null,
                new List<DiagnosticSection>
                {
                    new DiagnosticSection("Fire whirl", FeatureHealth.Healthy, null, null)
                });

            string text = Joined(r);
            Assert.True(text.IndexOf("DLC") < text.IndexOf("Fire whirl"));
        }

        [Fact]
        public void LabelAndValue_AppearOnTheSameLine()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "scan", "burning 34") },
                null, null);
            Assert.Contains("scan", Joined(r));
            Assert.Contains("burning 34", Joined(r));
        }

        [Fact]
        public void Indent_ProducesLeadingSpaces()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine>
                {
                    new DiagnosticLine(0, "a", "1"),
                    new DiagnosticLine(2, "b", "2"),
                },
                null, null);

            var lines = DiagnosticFormatter.Format(r);
            string deep = lines.Find(l => l.Contains("b"));
            string shallow = lines.Find(l => l.Contains("a"));
            Assert.NotNull(deep);
            Assert.NotNull(shallow);
            int deepSpaces = deep.Length - deep.TrimStart().Length;
            int shallowSpaces = shallow.Length - shallow.TrimStart().Length;
            Assert.True(deepSpaces > shallowSpaces, "indent did not increase");
        }

        [Fact]
        public void EmptyValue_StillRendersLabel()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "cooldown", "") }, null, null);
            Assert.Contains("cooldown", Joined(r));
        }

        [Fact]
        public void LongValue_IsTruncated()
        {
            string huge = new string('x', DiagnosticFormatter.MaxValueLength + 200);
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "k", huge) }, null, null);

            foreach (string line in DiagnosticFormatter.Format(r))
            {
                Assert.True(line.Length < DiagnosticFormatter.MaxValueLength + 60,
                    "line not truncated: " + line.Length);
            }
        }

        [Fact]
        public void NewlinesInValue_DoNotBreakLineStructure()
        {
            // 例外メッセージには改行が入る。1 行 = 1 要素を崩さないこと。
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "err", "line1\nline2\r\nline3") },
                null, null);

            foreach (string line in DiagnosticFormatter.Format(r))
            {
                Assert.DoesNotContain("\n", line);
                Assert.DoesNotContain("\r", line);
            }
        }

        [Fact]
        public void AssumptionSummary_ShowsCounts()
        {
            var r = new DiagnosticReport(null, new List<AssumptionResult>
            {
                new AssumptionResult("a", true, ""),
                new AssumptionResult("b", false, "thing breaks"),
            }, null);

            string text = Joined(r);
            Assert.Contains("1", text);
            Assert.Contains("FAIL", text);
        }

        [Fact]
        public void PassedAssumptions_AreNotListedIndividually_ButFailuresAre()
        {
            // 通った前提を全部出すとオーバーレイが埋まる。FAIL だけ列挙する。
            var r = new DiagnosticReport(null, new List<AssumptionResult>
            {
                new AssumptionResult("QUIETPASS", true, ""),
                new AssumptionResult("LOUDFAIL", false, "thing breaks"),
            }, null);

            string text = Joined(r);
            Assert.DoesNotContain("QUIETPASS", text);
            Assert.Contains("LOUDFAIL", text);
            Assert.Contains("thing breaks", text);
        }

        [Fact]
        public void SectionHealth_IsVisible()
        {
            var r = new DiagnosticReport(null, null, new List<DiagnosticSection>
            {
                new DiagnosticSection("Volcano", FeatureHealth.Degraded, "3 errors", null),
            });

            string text = Joined(r);
            Assert.Contains("Volcano", text);
            Assert.Contains("Degraded", text);
            Assert.Contains("3 errors", text);
        }

        [Fact]
        public void DisabledSection_IsShownAsDisabled()
        {
            var r = new DiagnosticReport(null, null, new List<DiagnosticSection>
            {
                new DiagnosticSection("Typhoon", FeatureHealth.Disabled, null, null),
            });
            Assert.Contains("Disabled", Joined(r));
        }

        [Fact]
        public void Output_IsAsciiOnly()
        {
            // IMGUI の既定フォントは日本語グリフを持たない可能性が高く、豆腐になる。
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "state", "ready") },
                new List<AssumptionResult> { new AssumptionResult("x", false, "breaks") },
                new List<DiagnosticSection>
                {
                    new DiagnosticSection("Fire whirl", FeatureHealth.Healthy, null,
                        new List<DiagnosticLine> { new DiagnosticLine(1, "active", "2") })
                });

            foreach (string line in DiagnosticFormatter.Format(r))
            {
                foreach (char c in line)
                {
                    Assert.True(c < 128, "non-ASCII character in output: " + c);
                }
            }
        }

        [Fact]
        public void IsDeterministic()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "a", "1") },
                new List<AssumptionResult> { new AssumptionResult("b", false, "c") },
                new List<DiagnosticSection>
                {
                    new DiagnosticSection("S", FeatureHealth.Healthy, null, null)
                });

            Assert.Equal(Joined(r), Joined(r));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'DiagnosticFormatter' could not be found`

- [ ] **Step 3: 実装する**

`src/DisasterPlus/Core/Diagnostics/DiagnosticFormatter.cs`:

```csharp
using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// レポートをテキスト行に整形する。オーバーレイとダンプが共用するので、
    /// 画面に見えているものとファイルの内容が食い違わない。
    ///
    /// 出力は ASCII のみ。IMGUI の既定フォントは日本語グリフを持たない
    /// 可能性が高く、豆腐になるため。開発者向けツールなので翻訳しない。
    /// </summary>
    public static class DiagnosticFormatter
    {
        public const int MaxValueLength = 120;

        private const string IndentUnit = "  ";

        public static List<string> Format(DiagnosticReport report)
        {
            var outLines = new List<string>();
            if (report == null) return outLines;

            for (int i = 0; i < report.Header.Count; i++)
            {
                outLines.Add(Render(report.Header[i]));
            }

            if (report.Assumptions.Count > 0)
            {
                outLines.Add("ASSUMPTIONS  " + report.PassedCount + " passed, "
                             + report.FailedCount + " FAILED");

                // 通った前提は列挙しない。全部出すとオーバーレイが埋まって
                // 肝心の FAIL が読みにくくなる。ダンプ側も同じ判断でよい。
                for (int i = 0; i < report.Assumptions.Count; i++)
                {
                    var a = report.Assumptions[i];
                    if (a.Passed) continue;
                    outLines.Add(IndentUnit + "FAIL  " + Sanitize(a.Name));
                    if (a.Impact.Length > 0)
                    {
                        outLines.Add(IndentUnit + IndentUnit + "-> " + Sanitize(a.Impact));
                    }
                }
            }

            for (int i = 0; i < report.Sections.Count; i++)
            {
                var s = report.Sections[i];
                outLines.Add("");
                string head = "-- " + Sanitize(s.Name) + " --  " + HealthText(s.Health);
                if (s.Note.Length > 0) head += "  (" + Sanitize(s.Note) + ")";
                outLines.Add(head);

                for (int k = 0; k < s.Lines.Count; k++)
                {
                    outLines.Add(Render(s.Lines[k]));
                }
            }

            return outLines;
        }

        private static string HealthText(FeatureHealth h)
        {
            switch (h)
            {
                case FeatureHealth.Degraded: return "Degraded";
                case FeatureHealth.Disabled: return "Disabled";
                default: return "Healthy";
            }
        }

        private static string Render(DiagnosticLine line)
        {
            string pad = "";
            for (int i = 0; i < line.Indent; i++) pad += IndentUnit;

            string label = Sanitize(line.Label);
            string value = Sanitize(line.Value);
            if (value.Length == 0) return pad + label;
            return pad + label + ": " + value;
        }

        /// <summary>
        /// 改行と非 ASCII を潰し、長すぎる値を切る。
        /// 1 行 = 1 要素の構造を、例外メッセージのような多行文字列でも崩さない。
        /// </summary>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new System.Text.StringBuilder(s.Length);

            // 打ち切ったかどうかは「実際に捨てた出力文字があるか」で判断する。
            //
            // sb.Length >= MaxValueLength で判断してはいけない。ちょうど
            // MaxValueLength 文字の値は、切っていないのに "..." が付く。
            // 残り入力の有無で判断するのも駄目で、'\r' は出力を生まないため
            // 「超過分が '\r' だけ」の値に嘘の "..." が付く。
            //
            // そこで、まず 1 文字ぶんの出力を決めてから上限を判定する。
            // 上限に達した状態で「出力を生む文字」が来たときだけ truncated を立てる。
            bool truncated = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r') continue;   // 出力を生まないので、上限判定より先に飛ばす

                char emitted = (c == '\n' || c == '\t') ? ' '
                             : (c < 32 || c > 126) ? '?' : c;

                if (sb.Length >= MaxValueLength) { truncated = true; break; }
                sb.Append(emitted);
            }

            if (truncated) sb.Append("...");
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 87 件すべて PASS（既存 73 件 + 新規 14 件）

- [ ] **Step 5: コミット**

```bash
git add src/DisasterPlus/Core/Diagnostics tests/DisasterPlus.Core.Tests/Diagnostics
git commit -m "feat: 診断レポートの整形（オーバーレイとダンプで共用）"
```

---

## Task 3: 収集の配線と劣化の記録

**Files:**
- Create: `src/DisasterPlus/Game/Diagnostics/DiagnosticBuilder.cs`
- Create: `src/DisasterPlus/Game/Diagnostics/DiagnosticsHub.cs`
- Modify: `src/DisasterPlus/Game/IDisasterFeature.cs`
- Modify: `src/DisasterPlus/Game/Common/FeatureHost.cs`
- Modify: `src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs`

**Interfaces:**
- Consumes: `DiagnosticReport`, `DiagnosticSection`, `DiagnosticLine`, `FeatureHealth`（Task 1）、既存の `FireWhirlRegistry.Snapshot()`, `BurningBuildingScanner`, `ModCompat`, `HarmonyBootstrap.Installed`, `Log`
- Produces:
  - `DiagnosticBuilder` — `class`。`void Line(int indent, string label, string value)`、`void Line(int indent, string label)`、`IList<DiagnosticLine> Take()`（内部リストを返して自身を空にする）
  - `IDisasterFeature.WriteDiagnostics(DiagnosticBuilder b)` — **インターフェースに追加**。②〜⑤の全機能に診断実装を強制する
  - `DiagnosticsHub` — `static`。`static bool CollectionEnabled { get; set; }`、`static void Publish(DiagnosticReport r)`、`static DiagnosticReport Latest { get; }`、`static void Clear()`
  - `FeatureHost.NoteFailure(string featureName, System.Exception e)`（内部利用）、`static DiagnosticReport BuildReport()`（sim スレッドから呼ぶ）

**スレッド:** `BuildReport()` は **sim スレッド**で呼ぶ（各機能の状態がそこにあるため）。`Latest` は main スレッドから読む。`FireWhirlRegistry` と同じ snapshot-then-render。

- [ ] **Step 1: DiagnosticBuilder を作る**

`src/DisasterPlus/Game/Diagnostics/DiagnosticBuilder.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断行の組み立て。sim スレッドで 1 機能ずつ使い回す。
    /// Take() で中身を引き渡し、自分は空になる（次の機能で再利用するため）。
    /// </summary>
    public class DiagnosticBuilder
    {
        private List<DiagnosticLine> _lines = new List<DiagnosticLine>();

        public void Line(int indent, string label, string value)
        {
            _lines.Add(new DiagnosticLine(indent, label, value));
        }

        public void Line(int indent, string label)
        {
            _lines.Add(new DiagnosticLine(indent, label, ""));
        }

        /// <summary>組み立てた行を渡し、自身を空にする。</summary>
        public IList<DiagnosticLine> Take()
        {
            var taken = _lines;
            _lines = new List<DiagnosticLine>();
            return taken;
        }
    }
}
```

- [ ] **Step 2: DiagnosticsHub を作る**

`src/DisasterPlus/Game/Diagnostics/DiagnosticsHub.cs`:

```csharp
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断スナップショットの受け渡し。sim スレッドが Publish し、main スレッドが Latest を読む。
    /// FireWhirlRegistry と同じ snapshot-then-render（net35 に Concurrent は無いので素の lock）。
    ///
    /// DiagnosticReport は不変なので、参照を渡すだけで安全。
    /// </summary>
    public static class DiagnosticsHub
    {
        private static readonly object _gate = new object();
        private static DiagnosticReport _latest;
        private static bool _collectionEnabled;

        /// <summary>
        /// 収集を走らせるか。オーバーレイが閉じていてダンプ要求も無ければ false で、
        /// そのとき収集コストはゼロになる。
        /// </summary>
        public static bool CollectionEnabled
        {
            get { lock (_gate) { return _collectionEnabled; } }
            set { lock (_gate) { _collectionEnabled = value; } }
        }

        public static void Publish(DiagnosticReport report)
        {
            lock (_gate) { _latest = report; }
        }

        /// <summary>まだ一度も publish されていなければ null。呼び出し側で判定すること。</summary>
        public static DiagnosticReport Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        public static void Clear()
        {
            lock (_gate)
            {
                _latest = null;
                _collectionEnabled = false;
            }
        }
    }
}
```

- [ ] **Step 3: IDisasterFeature に WriteDiagnostics を追加する**

`src/DisasterPlus/Game/IDisasterFeature.cs` にメソッドを 1 つ足す。既存メンバーはそのまま。

```csharp
        /// <summary>
        /// 自分の状態を診断行として書き出す。sim スレッドから呼ばれる。
        /// 収集が有効なときだけ呼ばれるので、コストを気にしすぎなくてよい。
        ///
        /// インターフェースに置いているのは意図的で、②〜⑤の機能を足すときに
        /// 診断の実装を忘れられないようにするため。
        /// </summary>
        void WriteDiagnostics(DiagnosticBuilder b);
```

- [ ] **Step 4: FeatureHost に劣化記録とレポート組み立てを足す**

`src/DisasterPlus/Game/Common/FeatureHost.cs` に追加する。既存の try/catch は**握り潰しをやめず**、記録だけ足す（一過性の可能性があるので呼び出しは止めない）。

```csharp
        // 機能ごとの例外記録。レベルアンロードでリセットする。
        //
        // 必ずロックで守ること。NoteFailure は MainThreadUpdate の catch から
        // main スレッドで呼ばれ、BuildReport は sim スレッドから読む。
        // 無防備な Dictionary の同時変更は内部バケットを壊し、まれに再現しない形で
        // 例外や終わらないルックアップを起こす。
        // DiagnosticsHub の gate とは別にすることで、ロック順序の問題を作らない。
        private static readonly object _errorGate = new object();
        private static readonly Dictionary<string, int> _errorCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> _lastErrors = new Dictionary<string, string>();

        /// <summary>既存の catch 節から呼ぶ。呼び出しは止めない。</summary>
        private static void NoteFailure(string featureName, System.Exception e)
        {
            lock (_errorGate)
            {
                int n;
                _errorCounts.TryGetValue(featureName, out n);
                _errorCounts[featureName] = n + 1;
                _lastErrors[featureName] = e == null ? "unknown" : e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>
        /// 全機能の診断を集めて 1 つの不変レポートにする。sim スレッドから呼ぶこと。
        /// </summary>
        public static DiagnosticReport BuildReport()
        {
            var header = new List<DiagnosticLine>();
            header.Add(new DiagnosticLine(0, "DLC:ND",
                ModCompat.NaturalDisastersOwned ? "owned" : "MISSING"));
            header.Add(new DiagnosticLine(0, "NDR",
                ModCompat.NdrPresent ? "detected" : "absent"));
            header.Add(new DiagnosticLine(0, "Harmony",
                HarmonyBootstrap.Installed ? "patched" : "NOT PATCHED"));
            header.Add(new DiagnosticLine(0, "Level", _levelReady ? "ready" : "not ready"));

            var sections = new List<DiagnosticSection>();
            var builder = new DiagnosticBuilder();

            // 先にロック内でスナップショットを取り、ロックを離してから組み立てる。
            // WriteDiagnostics は任意の機能コードなので、ロックを保持したまま呼ばない。
            var errorSnapshot = new Dictionary<string, string>();
            lock (_errorGate)
            {
                foreach (var kv in _errorCounts)
                {
                    if (kv.Value <= 0) continue;
                    string last;
                    _lastErrors.TryGetValue(kv.Key, out last);
                    errorSnapshot[kv.Key] = kv.Value + " errors, last: " + (last ?? "unknown");
                }
            }

            for (int i = 0; i < _features.Count; i++)
            {
                var f = _features[i];
                var health = FeatureHealth.Healthy;
                string note;
                if (errorSnapshot.TryGetValue(f.Name, out note)) health = FeatureHealth.Degraded;
                else note = "";

                try
                {
                    f.WriteDiagnostics(builder);
                }
                catch (System.Exception e)
                {
                    // 診断の失敗で他機能の診断まで巻き込まない。
                    // バッジも Degraded にする。本文が「失敗」なのに Healthy と出るのは嘘になる。
                    builder.Line(1, "diagnostics failed", e.GetType().Name);
                    health = FeatureHealth.Degraded;
                    if (note.Length == 0) note = "WriteDiagnostics threw";
                }

                sections.Add(new DiagnosticSection(f.Name, health, note, builder.Take()));
            }

            return new DiagnosticReport(header, Assumptions.LastResults, sections);
        }
```

各 `catch` 節に `NoteFailure(_features[i].Name, e);` を 1 行足す（`OnLevelLoaded` / `OnSimulationTick` / `OnMainThreadUpdate` / `OnLevelUnloading` の 4 箇所）。

`SimulationTick()` の末尾、機能ディスパッチの後に収集を足す:

```csharp
            // オーバーレイが閉じていてダンプ要求も無ければ何もしない。
            if (DiagnosticsHub.CollectionEnabled)
            {
                try { DiagnosticsHub.Publish(BuildReport()); }
                catch (System.Exception e) { Log.Error("diagnostics collection failed", e); }
            }
```

`LevelUnloading()` に追加（**クリアも同じロックの中で行う**）:

```csharp
            lock (_errorGate)
            {
                _errorCounts.Clear();
                _lastErrors.Clear();
            }
            DiagnosticsHub.Clear();
```

- [ ] **Step 5: FireWhirlFeature に WriteDiagnostics を実装する**

`src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs` に追加。`FireWhirlView` の各フィールドをそのまま出す。

```csharp
        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.FireWhirlEnabled.value ? "yes" : "no");
            b.Line(1, "scan", _scanner.DiagnosticSummary());

            var views = FireWhirlRegistry.Snapshot();
            b.Line(1, "active", views.Count.ToString());

            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                string s = "#" + v.DisasterId
                    + "  (" + (int)v.Center.X + "," + (int)v.Center.Z + ")"
                    + "  r=" + (int)v.Radius
                    + "  " + v.ElapsedMinutes.ToString("F1")
                    + "/" + ModSettings.MaxLifetimeMinutes.value + "min"
                    + "  n=" + v.BurningCount
                    + (v.VehicleId != 0 ? "  pinned" : "  NO VEHICLE")
                    + (v.Manual ? "  manual" : "")
                    + (v.Ending ? "  ending" : "");
                b.Line(2, s);
            }
        }
```

`BurningBuildingScanner` に 1 行サマリを足す（内部状態を外へ漏らさずに済む）。
既存フィールドは `private int _cursor` と `private const int SliceSize = 6144`、
公開プロパティは `IList<BurningBuilding> Current`（直近に完了した 1 周の結果）。

```csharp
        /// <summary>診断表示用の 1 行。走査の進み具合が分かる。</summary>
        public string DiagnosticSummary()
        {
            // _cursor は建物バッファ内の走査位置。1 周ぶんの進捗として出す。
            return "burning " + (_current == null ? 0 : _current.Count)
                 + " / cursor " + _cursor
                 + " (+" + SliceSize + "/tick)";
        }
```

- [ ] **Step 6: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: 警告 0。Core テストも再実行して 87 件緑のまま。

- [ ] **Step 7: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 診断収集の配線と機能ごとの劣化記録"
```

---

## Task 4: 前提検証

**この基盤の中核。** 設計書 付録A の前提を実行時にゲームへ問い合わせて照合する。

**Files:**
- Create: `src/DisasterPlus/Game/Diagnostics/Assumptions.cs`
- Modify: `src/DisasterPlus/Game/Common/DisasterPlusLoading.cs`

**Interfaces:**
- Consumes: `AssumptionResult`（Task 1）、`Log`、`ModCompat`、`HarmonyBootstrap`、`FireWhirlSpawner`
- Produces:
  - `Assumptions` — `static`。`static void Run()`（レベルロード後に 1 回）、`static IList<AssumptionResult> LastResults { get; }`（未実行なら空）、`static void Reset()`

- [ ] **Step 1: 実装する**

`src/DisasterPlus/Game/Diagnostics/Assumptions.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using DisasterPlus.Core.Diagnostics;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 設計書 付録A の前提を、実行時に実際のゲームへ問い合わせて照合する。
    ///
    /// なぜ要るか: ③の実装中、IL から読んだ前提が 3 件も誤っていた
    /// (DAYTIME_FRAMES の 4 倍ズレ、m_targetPos0 の意味、m_fireIntensity 直接書込)。
    /// 共通点は「前提が破れても何も起きない」こと。ゲームは平然と動き、
    /// 挙動だけが静かに違う。ここで名指しでログに出すのが唯一の防波堤になる。
    ///
    /// 前提を 1 ファイルに集約しているのは、付録A と突き合わせて監査するため。
    /// ②〜⑤で前提が増えたらここに足すこと。
    /// </summary>
    public static class Assumptions
    {
        private static readonly List<AssumptionResult> _results = new List<AssumptionResult>();
        private static bool _ran;

        public static IList<AssumptionResult> LastResults { get { return _results; } }

        public static void Reset()
        {
            _results.Clear();
            _ran = false;
        }

        /// <summary>
        /// レベルロード完了後に 1 回だけ呼ぶ。起動時ではないのは、
        /// Harmony の適用状況と prefab の解決を見る必要があるため。
        /// </summary>
        public static void Run()
        {
            if (_ran) return;
            _ran = true;
            _results.Clear();

            Check("SimulationManager.DAYTIME_FRAMES == 65536",
                  "all in-game durations will be wrong",
                  delegate { return SimulationManager.DAYTIME_FRAMES == 65536; });

            Check("VortexAI.SimulationStep(6) is patched",
                  "fire whirls will drift instead of staying put",
                  delegate { return HarmonyBootstrap.Installed && VortexStepIsPatched(); });

            Check("BuildingAI.BurnBuilding is resolvable",
                  "fire spread will not work",
                  delegate
                  {
                      return typeof(BuildingAI).GetMethod("BurnBuilding",
                          BindingFlags.Public | BindingFlags.Instance) != null;
                  });

            Check("TornadoAI disaster prefab is available",
                  "fire whirls cannot be created",
                  delegate { return FireWhirlSpawner.HasTornadoPrefab(); });

            Check("Disasters panel intensity slider is reachable",
                  "disaster intensity cannot be unlocked to 25.5",
                  delegate { return IntensityUnlock.SliderReachable(); });

            Report();
        }

        private static bool VortexStepIsPatched()
        {
            // Harmony が実際にこのメソッドを持っているかを見る。
            // [HarmonyPatch] の引数が実メソッドと 1 つでも食い違うと
            // パッチは無言で当たらず、MOD は正常に見えたまま竜巻だけが流れる。
            var target = typeof(VortexAI).GetMethod("SimulationStep",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(ushort), typeof(Vehicle).MakeByRefType(),
                    typeof(Vehicle.Frame).MakeByRefType(), typeof(ushort),
                    typeof(Vehicle).MakeByRefType(), typeof(int)
                },
                null);
            if (target == null) return false;

            var info = HarmonyLib.Harmony.GetPatchInfo(target);
            return info != null && info.Postfixes != null && info.Postfixes.Count > 0;
        }

        private static void Check(string name, string impact, Func<bool> predicate)
        {
            bool passed;
            string detail = impact;
            try
            {
                passed = predicate();
            }
            catch (Exception e)
            {
                // 検証そのものが落ちても起動を壊さない。FAIL として扱う。
                passed = false;
                detail = impact + " (check threw " + e.GetType().Name + ")";
            }
            _results.Add(new AssumptionResult(name, passed, passed ? "" : detail));
        }

        private static void Report()
        {
            int passed = 0, failed = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].Passed) passed++; else failed++;
            }

            Log.Info("ASSUMPTIONS  " + passed + " passed, " + failed + " FAILED");
            for (int i = 0; i < _results.Count; i++)
            {
                var a = _results[i];
                if (a.Passed)
                {
                    Log.Info("  PASS  " + a.Name);
                }
                else
                {
                    Log.Warn("  FAIL  " + a.Name);
                    Log.Warn("        -> " + a.Impact);
                }
            }
        }
    }
}
```

**注記:** DLC 所持は前提の破れではなく正常な構成なので、検証項目に入れない（設定画面に既に DLC 注記があり、二重に警告する意味がない）。ヘッダー行には出す。

- [ ] **Step 2: 検証に必要な公開メンバーを足す**

`FireWhirlSpawner` に:

```csharp
        /// <summary>前提検証用。副作用なしに prefab の解決可否だけを返す。</summary>
        public static bool HasTornadoPrefab()
        {
            return FindTornadoInfo() != null;
        }
```

`IntensityUnlock` に:

```csharp
        /// <summary>前提検証用。スライダーに到達できるかだけを返す（値は変えない）。</summary>
        public static bool SliderReachable()
        {
            var panel = SceneObjects.FindInScene<DisastersOptionPanel>();
            if (panel == null) return false;
            return panel.Find<ColossalFramework.UI.UISlider>("Slider") != null;
        }
```

`SceneObjects.FindInScene<T>()` は既存（`public static T FindInScene<T>() where T : Component`、
`src/DisasterPlus/Game/UI/SceneObjects.cs`）。Unity 5.6 の `FindObjectOfType` が非アクティブな
GameObject を返さない問題への対処として追加済みで、そのまま使える。

- [ ] **Step 3: レベルロードで実行する**

`DisasterPlusLoading.OnLevelLoaded` の末尾（`FeatureHost.LevelLoaded()` の**後**）に:

```csharp
            // Harmony の適用と prefab の解決を見るので、機能の初期化が終わってから走らせる。
            Assumptions.Run();
```

`OnLevelUnloading` に `Assumptions.Reset();` を足す。

- [ ] **Step 4: ビルドして確認する**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: 警告 0。`HarmonyLib.Harmony.GetPatchInfo` が解決できることを確認する。解決できなければ**推測で代替を書かず**、`docs/tools/ilload.ps1` で `CitiesHarmony.Harmony.dll` をリフレクションして正しい API を確認すること。

- [ ] **Step 5: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 実行時の前提検証（付録Aの前提をゲームに問い合わせて照合）"
```

---

## Task 5: オーバーレイとホットキー

**Files:**
- Create: `src/DisasterPlus/Game/Diagnostics/DiagnosticOverlay.cs`
- Modify: `src/DisasterPlus/Game/Common/DisasterPlusLoading.cs`

**Interfaces:**
- Consumes: `DiagnosticsHub`, `DiagnosticFormatter`, `Log`、`ModSettings`（Task 6 で足すが、本タスクでは `ModSettings.OverlayHotkey`（`SavedInt`, `KeyCode` の整数値）と `ModSettings.OverlayEnabled`（`SavedBool`）を**先に**足してよい）
- Produces:
  - `DiagnosticOverlay : MonoBehaviour` — `static DiagnosticOverlay Create()`、`static void Destroy()`、`static bool Visible { get; }`

**なぜ IMGUI か:** CS の UI フレームワークは親パネルの探索が要り、③でそこに 2 回ハマっている。**診断ツールが診断対象と同じ仕組みに依存するのは筋が悪い。** 他の全部が動かないときに動いてほしいので、依存をゼロに寄せる。

- [ ] **Step 1: 実装する**

`src/DisasterPlus/Game/Diagnostics/DiagnosticOverlay.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// IMGUI のデバッグオーバーレイ。CS の UI フレームワークには一切依存しない。
    ///
    /// OnGUI は 1 フレームに複数回呼ばれる(レイアウト用と描画用)。
    /// したがってホットキー判定と状態取得は Update で行い、OnGUI では描画だけする。
    /// </summary>
    public class DiagnosticOverlay : MonoBehaviour
    {
        private const string HostName = "DisasterPlusDiagnosticOverlay";

        private static DiagnosticOverlay _instance;

        private bool _visible;
        private List<string> _lines = new List<string>();
        private Vector2 _scroll;
        private GUIStyle _style;
        private Texture2D _background;

        public static bool Visible { get { return _instance != null && _instance._visible; } }

        public static DiagnosticOverlay Create()
        {
            // fake-null に注意: 破棄済みの MonoBehaviour は == null が true になる。
            // ここはコレクションではなくオブジェクト自体を見ているので正しく再生成される。
            if (_instance != null) return _instance;

            var go = new GameObject(HostName);
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<DiagnosticOverlay>();
            return _instance;
        }

        public static void Destroy()
        {
            if (_instance == null) return;
            Object.Destroy(_instance.gameObject);
            _instance = null;
        }

        private void Update()
        {
            ModSettings.Ensure();

            var key = (KeyCode)ModSettings.OverlayHotkey.value;
            if (Input.GetKeyDown(key))
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl) DiagnosticDump.Write();
                else Toggle();
            }

            // 収集が走るのは表示中だけ。閉じていればコストはゼロ。
            DiagnosticsHub.CollectionEnabled = _visible;

            if (_visible)
            {
                _lines = DiagnosticFormatter.Format(DiagnosticsHub.Latest);
            }
        }

        private void Toggle()
        {
            _visible = !_visible;
            if (!_visible) _lines.Clear();
            Log.Info("diagnostic overlay " + (_visible ? "shown" : "hidden"));
        }

        private void OnGUI()
        {
            if (!_visible) return;
            EnsureStyle();

            const float width = 560f;
            float height = Mathf.Min(Screen.height - 80f, 24f + _lines.Count * 16f);

            var area = new Rect(12f, 60f, width, height);
            GUI.DrawTexture(area, _background);
            GUILayout.BeginArea(area);
            GUILayout.Label("Disaster +  [" + (KeyCode)ModSettings.OverlayHotkey.value
                            + " toggle / Ctrl+ dump]", _style);

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _lines.Count; i++)
            {
                GUILayout.Label(_lines[i], _style);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void EnsureStyle()
        {
            // fake-null 対策: 都市を跨ぐと Texture2D が破棄されている場合がある。
            // オブジェクト自体を見て作り直す。
            if (_background == null)
            {
                _background = new Texture2D(1, 1);
                _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
                _background.Apply();
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = 12;
                _style.wordWrap = false;
                _style.normal.textColor = Color.white;
                _style.padding = new RectOffset(4, 4, 0, 0);
            }
        }

        private void OnDestroy()
        {
            if (_background != null) Object.Destroy(_background);
            _background = null;
            _style = null;
            if (_instance == this) _instance = null;
        }
    }
}
```

- [ ] **Step 2: ライフサイクルを配線する**

`DisasterPlusLoading.OnLevelLoaded` に、`Assumptions.Run()` の後で:

```csharp
            if (ModSettings.OverlayEnabled.value) DiagnosticOverlay.Create();
```

`OnLevelUnloading` に:

```csharp
            DiagnosticOverlay.Destroy();
```

- [ ] **Step 3: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: 警告 0。`GUILayout` / `GUIStyle` / `RectOffset` は Unity 5.6 に存在するが、疑わしければ `docs/tools/ilload.ps1` で `UnityEngine.dll` を確認すること。

- [ ] **Step 4: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: IMGUI デバッグオーバーレイとホットキー"
```

---

## Task 6: ダンプ・ログチャンネル・設定・前提警告

**Files:**
- Create: `src/DisasterPlus/Game/Diagnostics/DiagnosticDump.cs`
- Modify: `src/DisasterPlus/Game/Common/Log.cs`
- Modify: `src/DisasterPlus/Game/ModSettings.cs`
- Modify: `src/DisasterPlus/Game/Mod.cs`
- Modify: `src/DisasterPlus/Game/Localization/Strings.cs`
- Modify: `Locales/en.txt`, `Locales/ja.txt`

**Interfaces:**
- Consumes: 全て
- Produces:
  - `DiagnosticDump` — `static void Write()`
  - `Log.Diag(int channel, string key, string message)`（既存の `Diag(key, message)` は残す）
  - `ModSettings.OverlayEnabled`（`SavedBool`, 既定 false）、`ModSettings.OverlayHotkey`（`SavedInt`, 既定 `(int)KeyCode.F11`）、`ModSettings.LogChannelMask`（`SavedInt`, 既定 `LogChannel.DefaultMask`）

- [ ] **Step 1: Log にチャンネルを足す**

`src/DisasterPlus/Game/Common/Log.cs` に追加。**既存の `Diag(key, message)` は消さない**（呼び出し箇所を書き換えずに済み、既存の実機確認手順も壊れない）。

```csharp
        /// <summary>
        /// チャンネル付きの診断ログ。マスクで無効なら何も出さない。
        ///
        /// 既存の Diag(key, message) は General 扱いのまま残してある。
        /// 本フェーズで既存呼び出しを機能チャンネルへ移行しないこと。移行すると
        /// 既定 OFF になり、docs/playtest-checklist.md の手順が壊れる。
        /// </summary>
        public static void Diag(int channel, string key, string message)
        {
            if (!DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(channel, CurrentMask())) return;
            Diag(key, message);
        }

        private static int CurrentMask()
        {
            try
            {
                ModSettings.Ensure();
                return ModSettings.LogChannelMask.value;
            }
            catch
            {
                // 設定がまだ用意できていない場面でもログで落ちない。
                return DisasterPlus.Core.Diagnostics.LogChannel.DefaultMask;
            }
        }
```

- [ ] **Step 2: 設定を足す**

`ModSettings` の `Ensure()` に追加（既存キーは触らない）:

```csharp
            OverlayEnabled  = new SavedBool("diagOverlayEnabled", FileName, false, true);
            OverlayHotkey   = new SavedInt("diagOverlayHotkey", FileName, (int)UnityEngine.KeyCode.F11, true);
            LogChannelMask  = new SavedInt("diagLogChannels", FileName,
                                           DisasterPlus.Core.Diagnostics.LogChannel.DefaultMask, true);
```

対応するフィールド宣言も足す。

- [ ] **Step 3: ダンプを作る**

`src/DisasterPlus/Game/Diagnostics/DiagnosticDump.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 状態を 1 ファイルに書き出す。オーバーレイと同じ DiagnosticFormatter を使うので、
    /// 画面に見えているものとファイルの内容が食い違わない。
    /// </summary>
    public static class DiagnosticDump
    {
        public const string FileName = "DisasterPlus-diagnostics.txt";

        public static void Write()
        {
            try
            {
                // 表示していなくてもダンプできるよう、その場で 1 回収集する。
                var report = FeatureHost.BuildReport();
                DiagnosticsHub.Publish(report);

                var sb = new StringBuilder();
                sb.AppendLine("Disaster + diagnostics");
                sb.AppendLine("generated at frame " + ColossalFramework.Singleton<SimulationManager>
                    .instance.m_currentFrameIndex);
                sb.AppendLine();

                foreach (string line in DiagnosticFormatter.Format(report))
                {
                    sb.AppendLine(line);
                }

                string dir = LocaleLoader.ModDirectoryPath();
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Warn("diagnostics dump skipped: mod directory not resolved");
                    return;
                }

                string path = Path.Combine(dir, FileName);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Log.Info("diagnostics written to " + path);
            }
            catch (Exception e)
            {
                Log.Error("diagnostics dump failed", e);
            }
        }
    }
}
```

`LocaleLoader` には既に `private static string ModDirectory()`（`LocaleLoader.cs:50`）がある。
**これを `public static string ModDirectoryPath()` にリネームして公開**し、既存の呼び出し
（`LocaleLoader.cs:94`）も新しい名前に直す。フォルダ解決を二重に実装しないこと。

- [ ] **Step 4: 設定画面と前提警告**

`Strings` に 8 件追加し、`en.txt` / `ja.txt` にも同じキーを足す（**3 つの集合を一致させる**。18 → 26 件）:

| フィールド | 英語既定値 |
|---|---|
| `GroupDebug` | `Debug` |
| `OverlayEnabled` | `Enable diagnostic overlay` |
| `OverlayHotkey` | `Overlay hotkey (Ctrl + key writes a dump file)` |
| `LogChannels` | `Verbose log channels` |
| `LogChannelGeneral` | `General` |
| `LogChannelFireWhirl` | `Fire whirl` |
| `AssumptionsFailedTitle` | `Some features are unavailable` |
| `AssumptionsFailedHint` | `Load a city once, then reopen this page to refresh.` |

`Mod.OnSettingsUI` の**先頭**（`LocaleLoader.Apply()` と `ModSettings.Ensure()` の直後）に前提警告を足す:

```csharp
            // Assumptions はレベルロード後に走るので、初回起動時はまだ空。
            // つまり警告は「一度都市を読み込んだ後、次にオプションを開いたとき」に出る。
            // OnSettingsUI はメインメニュー起動時に 1 回しか走らないため、これは避けられない。
            var failures = Assumptions.LastResults;
            bool anyFailed = false;
            for (int i = 0; i < failures.Count; i++) { if (!failures[i].Passed) anyFailed = true; }

            if (anyFailed)
            {
                var warn = helper.AddGroup(Strings.AssumptionsFailedTitle);
                for (int i = 0; i < failures.Count; i++)
                {
                    if (failures[i].Passed) continue;
                    warn.AddGroup("- " + failures[i].Impact);
                }
                warn.AddGroup(Strings.AssumptionsFailedHint);
            }
```

末尾に「デバッグ」グループを足す:

```csharp
            var dbg = helper.AddGroup(Strings.GroupDebug);
            dbg.AddCheckbox(Strings.OverlayEnabled, ModSettings.OverlayEnabled.value,
                v => ModSettings.OverlayEnabled.value = v);

            // ラベル配列は static にしないこと。型初期化時の言語で凍結する。
            string[] keys = { "F9", "F10", "F11", "F12" };
            int[] codes = { (int)UnityEngine.KeyCode.F9, (int)UnityEngine.KeyCode.F10,
                            (int)UnityEngine.KeyCode.F11, (int)UnityEngine.KeyCode.F12 };
            int current = 2;
            for (int i = 0; i < codes.Length; i++)
                if (codes[i] == ModSettings.OverlayHotkey.value) current = i;

            dbg.AddDropdown(Strings.OverlayHotkey, keys, current,
                v => ModSettings.OverlayHotkey.value = codes[v]);

            dbg.AddCheckbox(Strings.LogChannelGeneral,
                LogChannel.IsEnabled(LogChannel.General, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | LogChannel.General)
                       : (ModSettings.LogChannelMask.value & ~LogChannel.General));

            dbg.AddCheckbox(Strings.LogChannelFireWhirl,
                LogChannel.IsEnabled(LogChannel.FireWhirl, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | LogChannel.FireWhirl)
                       : (ModSettings.LogChannelMask.value & ~LogChannel.FireWhirl));
```

②〜⑤のチャンネルは**まだ出さない**（機能が存在しないため）。各フェーズで 1 行ずつ足す。

- [ ] **Step 5: en.txt を再生成してキー一致を確認する**

```bash
powershell -ExecutionPolicy Bypass -File tools/GenerateLocaleTemplate.ps1
```

`Strings` / `en.txt` / `ja.txt` の 3 つが 26 件で一致していることを確認する。`ja.txt` は手で訳を足す。

- [ ] **Step 6: ビルドしてテストする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 警告 0、Core テスト 87 件緑。

- [ ] **Step 7: コミット**

```bash
git add -A && git commit -m "feat: 状態ダンプ・ログチャンネル・デバッグ設定・前提警告"
```

---

## 実機確認（全機能完成後にまとめて実施）

本計画ぶんを `docs/playtest-checklist.md` に追記する。

1. `F11` でオーバーレイが開閉する
2. 火災旋風を出し、オーバーレイに座標・半径・経過・`pinned` が出る
3. **`Ctrl+F11` でダンプが生成され、パスがログに出る。内容がオーバーレイと一致する**
4. 起動ログに `ASSUMPTIONS  N passed, 0 FAILED` が出る
5. **CitiesHarmony を無効化して起動 → `VortexAI.SimulationStep(6) is patched` が FAIL し、設定画面に警告が出る**（検証機構自体が動いていることの確認。これをやらないと意味がない）
6. デバッグ設定でオーバーレイを OFF にすると出ない
7. **2 つ目の都市をロードしてもオーバーレイが正常に動く**
8. オーバーレイを閉じているとき、`DiagnosticsHub.CollectionEnabled` が false になっている（ログか一時的な計測で確認）
