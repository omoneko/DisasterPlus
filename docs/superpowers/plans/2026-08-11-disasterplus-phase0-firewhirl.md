# Disaster + — Phase 0 ＋ 火災旋風 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cities: Skylines 1 向け MOD「Disaster +」の基盤を作り、密集火災から自動発生する「移動しない・炎をまとった竜巻＝火災旋風」を動く状態で届ける。

**Architecture:** エンジン非依存の `Core`（全ユニットテスト対象）と、CS/Unity に触る唯一の層 `Game` に分ける。`Core` は net35 の MOD と modern .NET のテストプロジェクトの両方にコンパイルされる。火災旋風はバニラの竜巻災害を生成して流用し、`VortexAI.SimulationStep` への Harmony Postfix 1 箇所だけで位置を固定する。延焼拡大は `DisasterHelpers` を経由しない自前実装に置き、競合MOD の設定に左右されないようにする。

**Tech Stack:** C# 7.3 / .NET Framework 3.5（MOD 本体）、xunit / .NET 8（Core テスト）、HarmonyLib（CitiesHarmony 経由）、MSBuild、PowerShell。

## Global Constraints

仕様書 `docs/superpowers/specs/2026-08-11-disasterplus-phase0-firewhirl-design.md` の全体要件。**全タスクの要件に暗黙に含まれる。**

- `<TargetFrameworkVersion>v3.5</TargetFrameworkVersion>`、`<LangVersion>7.3</LangVersion>`。C# 8 以降の構文（`switch` 式、`??=`、範囲演算子、`using` 宣言、null 許容参照型）は**コンパイルエラーになる**
- `Core/` は `UnityEngine`・CS API・`System.Random` を**一切参照しない**。乱数は `hash(tick, id)`
- `Core/` は `System.Collections.Generic` と `System.Math` までに留める。LINQ は net35 でも使えるが Core テストとの両対応のため使わない
- net35 に `System.Collections.Concurrent` は無い。共有状態は単一の `lock`
- 建物・車両・市民・災害バッファの**生成と変更は sim スレッド**（`OnAfterSimulationTick()` か `Singleton<SimulationManager>.instance.AddAction(...)`）。GameObject・Mesh・Material・ParticleSystem・UI は**main スレッド**
- 設定ファイル名は **`DisasterPlusSettings`**（MOD 名・アセンブリ名と同名にすると毎起動で設定が消える）
- アセンブリ名・出力 DLL 名・MOD フォルダ名は **`DisasterPlus`**。表示名のみ **`Disaster +`**
- MOD 配置先は `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\DisasterPlus`
- ゲーム DLL の参照解決は 環境変数 `CITIES_SKYLINES_MANAGED` → 既定 `C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed`。**マシン固有パスをリポジトリに焼き込まない**
- ログは `<Steam>\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt`（AppData ではない）
- **日本語コメントを含むファイルを PowerShell の `Get-Content -Raw` → `Set-Content` で編集しない**（文字化けする）。Write/Edit ツールか、明示的に UTF-8 を指定した Python を使う
- コミットは各タスク末尾で 1 回。メッセージは `<type>: <説明>` 形式（`feat` / `fix` / `docs` / `test` / `chore` / `refactor`）
- `console.log` 相当（`Debug.Log` の垂れ流し）を残さない。診断ログは必ずスロットリングして `[DisasterPlus]` プレフィックスを付ける

## 実測済みの CS API 事実（推測で上書きしないこと）

付録 A（仕様書）より。**これらは IL で確認済み。**

- `DisasterData.m_intensity` は `Byte`。`m_targetPosition` は `Vector3`
- `DisasterManager.CreateDisaster(out ushort disasterIndex, DisasterInfo info)` → `bool`
- 竜巻の実体は `TornadoAI.m_vortexInfo`（`VehicleInfo`）を持つ **`VortexAI` の車両**
- `VortexAI.SimulationStep(ushort vehicleID, ref Vehicle vehicleData, ref Vehicle.Frame frameData, ushort leaderID, ref Vehicle leaderData, int lodPhysics)` が移動と被害を担う
- `VortexAI.ArriveAtDestination` は `m_waitCounter > 4` を返し、true で `DisasterAI.DeactivateNow` + `Vehicle.Unspawn`
- `Vehicle.m_targetPos0`〜`m_targetPos3` は `Vector4`。`GetTargetPos(int)` / `SetTargetPos(int, Vector4)`
- `Vehicle.Frame` は `m_position`（`Vector3`）/ `m_velocity`（`Vector3`）/ `m_rotation` 等を持つ
- **`Building.Flags.Fire` は存在しない。** 燃焼中は `Building.m_fireIntensity`（`Byte`）> 0
- `DisastersOptionPanel` は `m_slider`（`UISlider`）/ `m_label`（`UILabel`）/ `m_disasterTool`（`DisasterTool`）を持つ。スライダーの生値がそのまま byte 強度、表示のみ `/10`

---

## File Structure

### 新規作成

```
src/DisasterPlus/
  DisasterPlus.csproj                       net35 プロジェクト定義
  Properties/AssemblyInfo.cs                アセンブリ属性
  Core/Common/Vec2.cs                       2D（水平面）ベクトル値型
  Core/Common/Vec3.cs                       3D ベクトル値型（レイ交差用）
  Core/Common/DeterministicRandom.cs        hash(a,b) ベースの決定論的乱数
  Core/Common/LifetimeClock.cs              ゲーム内時間の経過蓄積（不変）
  Core/Common/GridVote.cs                   空間グリッドへの投票と近傍集計
  Core/Common/IHeightSampler.cs             高さ場サンプリングの抽象
  Core/Common/RayGeometry.cs                レイと高さ場の交差
  Core/FireWhirl/BurningBuilding.cs         検出入力の値型
  Core/FireWhirl/FireWhirlConfig.cs         ③の設定値の束
  Core/FireWhirl/FireWhirlCandidate.cs      検出結果の値型
  Core/FireWhirl/FireWhirlDetector.cs       密集度による発生判定
  Core/FireWhirl/FireWhirlStrength.cs       燃焼棟数→半径・破壊力
  Core/FireWhirl/FireWhirlLifecycle.cs      3段構えの寿命状態機械
  Core/FireWhirl/IgnitionCandidate.cs       延焼判定入力の値型
  Core/FireWhirl/IgnitionSpread.cs          延焼拡大の確率モデル
  Game/Mod.cs                               IUserMod。設定UIと機能の配線
  Game/ModSettings.cs                       SavedInt/SavedBool
  Game/IDisasterFeature.cs                  機能ごとの登録インターフェース
  Game/Compat/ModCompat.cs                  NDR 検出
  Game/Common/DisasterPlusThreading.cs      ThreadingExtensionBase
  Game/Common/DisasterPlusLoading.cs        LoadingExtensionBase
  Game/Common/DisasterPlusSerialization.cs  ISerializableDataExtension
  Game/Common/TerrainHeightSampler.cs       IHeightSampler の CS 実装
  Game/Common/Log.cs                        スロットリング付き診断ログ
  Game/Localization/Strings.cs              全表示文字列（public static field）
  Game/Localization/LocaleLoader.cs         Locales/<lang>.txt のリフレクション上書き
  Game/UI/IntensityUnlock.cs                強度スライダー上限の解放
  Game/UI/FireWhirlPanelButton.cs           災害パネルへのボタン追加
  Game/UI/FireWhirlPlacementTool.cs         クリック配置ツール
  Game/UI/ToolRegistration.cs               ToolController へのリフレクション登録
  Game/FireWhirl/FireWhirlFeature.cs        IDisasterFeature 実装
  Game/FireWhirl/BurningBuildingScanner.cs  m_fireIntensity 走査（分割巡回）
  Game/FireWhirl/FireWhirlSpawner.cs        竜巻災害の生成と ID 記録
  Game/FireWhirl/FireWhirlRegistry.cs       生存中の旋風の管理（唯一の共有状態）
  Game/FireWhirl/VortexPinPatch.cs          Harmony Postfix（位置固定）
  Game/FireWhirl/FireWhirlDamage.cs         自前の発火適用
  Game/FireWhirl/FireWhirlFlameFx.cs        炎の ParticleSystem
tests/DisasterPlus.Core.Tests/
  DisasterPlus.Core.Tests.csproj            Core/**/*.cs のみコンパイル
  Common/DeterministicRandomTests.cs
  Common/LifetimeClockTests.cs
  Common/GridVoteTests.cs
  Common/RayGeometryTests.cs
  FireWhirl/FireWhirlDetectorTests.cs
  FireWhirl/FireWhirlStrengthTests.cs
  FireWhirl/FireWhirlLifecycleTests.cs
  FireWhirl/IgnitionSpreadTests.cs
Locales/en.txt                              自動生成テンプレート
Locales/ja.txt                              日本語
build.ps1                                   ビルドとローカル配置
.github/workflows/core-tests.yml            Core テストのみの CI
README.md
```

### 責務の境界

- `FireWhirlRegistry` が **唯一の共有可変状態**。sim スレッドと main スレッドの両方から触るため、単一の `lock` で snapshot-then-render する。他のクラスは状態を持たない
- `Core` の各クラスは純関数か不変値型。状態遷移は新しい値を返す
- `VortexPinPatch` は静的 Harmony パッチなので、`FireWhirlRegistry` の静的読み取り API 越しにのみ状態へ触れる

---

## Task 1: Core プロジェクトと決定論的乱数

**Files:**
- Create: `src/DisasterPlus/Core/Common/Vec2.cs`
- Create: `src/DisasterPlus/Core/Common/DeterministicRandom.cs`
- Create: `tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj`
- Create: `tests/DisasterPlus.Core.Tests/Common/DeterministicRandomTests.cs`

**Interfaces:**
- Consumes: なし（最初のタスク）
- Produces:
  - `DisasterPlus.Core.Common.Vec2` — `struct`、`readonly float X, Z`、`Vec2(float x, float z)`、`float DistanceSquaredTo(Vec2 other)`、`static Vec2 operator +(Vec2, Vec2)`、`static Vec2 operator *(Vec2, float)`
  - `DisasterPlus.Core.Common.DeterministicRandom` — `static uint Hash(uint a, uint b)`、`static float Unit(uint a, uint b)`（`[0,1)`）

- [ ] **Step 1: テストプロジェクトを作る**

`tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <LangVersion>7.3</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RootNamespace>DisasterPlus.Core.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- Core だけを直接コンパイルする。ゲーム DLL は net35 で読めないため
         プロジェクト参照は張らない。 -->
    <Compile Include="..\..\src\DisasterPlus\Core\**\*.cs" />
    <!-- EnableDefaultCompileItems=false は既定の include だけでなく既定の exclude も無効にする。
         bin/obj を明示的に除かないと obj\**\*.AssemblyInfo.cs を拾って重複属性でビルドが壊れる。 -->
    <Compile Include="**\*.cs" Exclude="bin\**\*.cs;obj\**\*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 失敗するテストを書く**

`tests/DisasterPlus.Core.Tests/Common/DeterministicRandomTests.cs`:

```csharp
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    public class DeterministicRandomTests
    {
        [Fact]
        public void Unit_SameInputs_ReturnsSameValue()
        {
            Assert.Equal(DeterministicRandom.Unit(42u, 7u), DeterministicRandom.Unit(42u, 7u));
        }

        [Fact]
        public void Unit_IsAlwaysInZeroToOne()
        {
            for (uint tick = 0; tick < 200; tick++)
            {
                float v = DeterministicRandom.Unit(tick, tick * 3u + 1u);
                Assert.True(v >= 0f && v < 1f, "value out of range: " + v);
            }
        }

        [Fact]
        public void Unit_DifferentIds_ProduceDifferentValues()
        {
            // 同一 tick で id 違いが同じ値を返すと、全建物が一斉に発火してしまう。
            int distinct = 0;
            float first = DeterministicRandom.Unit(100u, 0u);
            for (uint id = 1; id < 64; id++)
            {
                if (DeterministicRandom.Unit(100u, id) != first) distinct++;
            }
            Assert.True(distinct >= 60, "too few distinct values: " + distinct);
        }

        [Fact]
        public void Unit_SpreadsAcrossRange()
        {
            // 4 分位すべてに値が落ちること。偏ると延焼確率が意味を失う。
            var buckets = new int[4];
            for (uint id = 0; id < 400; id++)
            {
                int b = (int)(DeterministicRandom.Unit(9u, id) * 4f);
                if (b > 3) b = 3;
                buckets[b]++;
            }
            foreach (int c in buckets) Assert.True(c > 40, "bucket too small: " + c);
        }

        [Fact]
        public void Vec2_DistanceSquared_IsCorrect()
        {
            var a = new Vec2(0f, 0f);
            var b = new Vec2(3f, 4f);
            Assert.Equal(25f, a.DistanceSquaredTo(b), 3);
        }
    }
}
```

- [ ] **Step 3: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'Vec2' could not be found`

- [ ] **Step 4: Vec2 を実装する**

`src/DisasterPlus/Core/Common/Vec2.cs`:

```csharp
namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 水平面（X-Z）の 2 次元ベクトル。
    /// Core はエンジン非依存でなければならないので UnityEngine.Vector2 は使えない。
    /// </summary>
    public struct Vec2
    {
        public readonly float X;
        public readonly float Z;

        public Vec2(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float DistanceSquaredTo(Vec2 other)
        {
            float dx = X - other.X;
            float dz = Z - other.Z;
            return dx * dx + dz * dz;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X + b.X, a.Z + b.Z);
        }

        public static Vec2 operator *(Vec2 a, float s)
        {
            return new Vec2(a.X * s, a.Z * s);
        }
    }
}
```

- [ ] **Step 5: DeterministicRandom を実装する**

`src/DisasterPlus/Core/Common/DeterministicRandom.cs`:

```csharp
namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// (tick, id) から再現可能な乱数を作る。
    /// System.Random を使うと、セーブ・ロード・ユニットテストで同じ結果にならない。
    /// </summary>
    public static class DeterministicRandom
    {
        /// <summary>32bit の混合関数（MurmurHash3 の finalizer を 2 入力に拡張したもの）。</summary>
        public static uint Hash(uint a, uint b)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u;
                h ^= b + 0x85EBCA6Bu + (h << 6) + (h >> 2);
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>[0, 1) の一様乱数。</summary>
        public static float Unit(uint a, uint b)
        {
            // 上位 24bit を使う。float の仮数は 24bit なので、これ以上使っても精度が出ない。
            return (Hash(a, b) >> 8) * (1.0f / 16777216.0f);
        }
    }
}
```

- [ ] **Step 6: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 5 件すべて PASS

- [ ] **Step 7: コミット**

```bash
git add src/DisasterPlus/Core tests/DisasterPlus.Core.Tests && git commit -m "feat: Core の基礎（Vec2 と決定論的乱数）とテスト基盤"
```

---

## Task 2: 火災旋風の発生判定

**Files:**
- Create: `src/DisasterPlus/Core/Common/GridVote.cs`
- Create: `src/DisasterPlus/Core/FireWhirl/BurningBuilding.cs`
- Create: `src/DisasterPlus/Core/FireWhirl/FireWhirlConfig.cs`
- Create: `src/DisasterPlus/Core/FireWhirl/FireWhirlCandidate.cs`
- Create: `src/DisasterPlus/Core/FireWhirl/FireWhirlDetector.cs`
- Create: `tests/DisasterPlus.Core.Tests/FireWhirl/FireWhirlDetectorTests.cs`

**Interfaces:**
- Consumes: `Vec2`（Task 1）
- Produces:
  - `DisasterPlus.Core.FireWhirl.BurningBuilding` — `struct`、`readonly ushort Id`、`readonly Vec2 Position`、コンストラクタ `BurningBuilding(ushort id, Vec2 position)`
  - `DisasterPlus.Core.FireWhirl.FireWhirlConfig` — `class`、`float DetectRadius`、`int DetectCount`、`float MinSeparation`、`float MaxLifetimeMinutes`、`float ConditionGraceMinutes`、`int SpreadStrength`。`static FireWhirlConfig Defaults()`
  - `DisasterPlus.Core.FireWhirl.FireWhirlCandidate` — `struct`、`readonly Vec2 Center`、`readonly int BurningCount`
  - `DisasterPlus.Core.FireWhirl.FireWhirlDetector` — `static List<FireWhirlCandidate> Detect(IList<BurningBuilding> burning, FireWhirlConfig config, IList<Vec2> existingWhirls)`
  - `DisasterPlus.Core.Common.GridVote` — `class`、`GridVote(float cellSize)`、`void Add(int index, Vec2 position)`、`void CollectNear(Vec2 position, float radius, List<int> into)`。**セル単位の粗い絞り込みだけを行う。呼び出し側が実距離を必ず再判定すること**

- [ ] **Step 1: 失敗するテストを書く**

`tests/DisasterPlus.Core.Tests/FireWhirl/FireWhirlDetectorTests.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class FireWhirlDetectorTests
    {
        private static FireWhirlConfig Config(float radius = 150f, int count = 12, float sep = 300f)
        {
            var c = FireWhirlConfig.Defaults();
            c.DetectRadius = radius;
            c.DetectCount = count;
            c.MinSeparation = sep;
            return c;
        }

        /// <summary>半径 r の円内に n 棟を等間隔で並べる。</summary>
        private static List<BurningBuilding> Cluster(int n, Vec2 center, float r, ushort startId)
        {
            var list = new List<BurningBuilding>();
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * System.Math.PI * i / n;
                var p = new Vec2(
                    center.X + (float)System.Math.Cos(a) * r,
                    center.Z + (float)System.Math.Sin(a) * r);
                list.Add(new BurningBuilding((ushort)(startId + i), p));
            }
            return list;
        }

        [Fact]
        public void Detect_DenseCluster_ProducesCandidate()
        {
            // 半径 40m の円周上に 12 棟 → どの棟から見ても R=150m 以内に 12 棟ある
            var burning = Cluster(12, new Vec2(1000f, 1000f), 40f, 1);
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Single(result);
            Assert.Equal(1000f, result[0].Center.X, 0);
            Assert.Equal(1000f, result[0].Center.Z, 0);
            Assert.Equal(12, result[0].BurningCount);
        }

        [Fact]
        public void Detect_ScatteredBuildings_ProducesNothing()
        {
            // 同じ 12 棟でも 2km 間隔で散らばっていれば発生しない
            var burning = new List<BurningBuilding>();
            for (ushort i = 0; i < 12; i++)
                burning.Add(new BurningBuilding((ushort)(i + 1), new Vec2(i * 2000f, 0f)));

            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Empty(result);
        }

        [Fact]
        public void Detect_OneBelowThreshold_ProducesNothing()
        {
            var burning = Cluster(11, new Vec2(500f, 500f), 40f, 1);
            Assert.Empty(FireWhirlDetector.Detect(burning, Config(count: 12), new List<Vec2>()));
        }

        [Fact]
        public void Detect_ExactlyAtThreshold_ProducesCandidate()
        {
            var burning = Cluster(12, new Vec2(500f, 500f), 40f, 1);
            Assert.Single(FireWhirlDetector.Detect(burning, Config(count: 12), new List<Vec2>()));
        }

        [Fact]
        public void Detect_JustOutsideRadius_DoesNotCount()
        {
            // 中心に 1 棟、R をわずかに超えた位置に 11 棟。中心棟から見ると近傍は自分だけ。
            var burning = new List<BurningBuilding> { new BurningBuilding(1, new Vec2(0f, 0f)) };
            var far = Cluster(11, new Vec2(0f, 0f), 150.5f, 2);
            burning.AddRange(far);
            // 外周の 11 棟同士は互いに近いので、そこで 11 棟クラスタになるが閾値 12 に届かない
            Assert.Empty(FireWhirlDetector.Detect(burning, Config(count: 12), new List<Vec2>()));
        }

        [Fact]
        public void Detect_TwoDistantClusters_ProducesTwoCandidates()
        {
            var burning = Cluster(12, new Vec2(0f, 0f), 40f, 1);
            burning.AddRange(Cluster(12, new Vec2(3000f, 0f), 40f, 100));
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Detect_NearbyCandidates_AreMergedByMinSeparation()
        {
            // 250m 離れた 2 クラスタ。MinSeparation = 300m なので 1 つに統合される。
            var burning = Cluster(12, new Vec2(0f, 0f), 30f, 1);
            burning.AddRange(Cluster(12, new Vec2(250f, 0f), 30f, 100));
            var result = FireWhirlDetector.Detect(burning, Config(sep: 300f), new List<Vec2>());
            Assert.Single(result);
        }

        [Fact]
        public void Detect_SuppressedNearExistingWhirl()
        {
            var burning = Cluster(12, new Vec2(0f, 0f), 40f, 1);
            var existing = new List<Vec2> { new Vec2(100f, 0f) };  // MinSeparation=300m 以内
            Assert.Empty(FireWhirlDetector.Detect(burning, Config(sep: 300f), existing));
        }

        [Fact]
        public void Detect_StrongestClusterWins_WhenMerged()
        {
            // 統合されるとき、燃焼棟数の多い側が残ること
            var burning = Cluster(12, new Vec2(0f, 0f), 30f, 1);
            burning.AddRange(Cluster(20, new Vec2(200f, 0f), 30f, 100));
            var result = FireWhirlDetector.Detect(burning, Config(sep: 300f), new List<Vec2>());
            Assert.Single(result);
            Assert.True(result[0].BurningCount >= 20, "expected the denser cluster to win");
        }

        [Fact]
        public void Detect_IsDeterministic()
        {
            var burning = Cluster(30, new Vec2(0f, 0f), 60f, 1);
            var a = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            var b = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Center.X, b[i].Center.X, 4);
                Assert.Equal(a[i].Center.Z, b[i].Center.Z, 4);
            }
        }

        [Fact]
        public void Detect_EmptyInput_ProducesNothing()
        {
            Assert.Empty(FireWhirlDetector.Detect(
                new List<BurningBuilding>(), Config(), new List<Vec2>()));
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'FireWhirlDetector' could not be found`

- [ ] **Step 3: 値型と設定を実装する**

`src/DisasterPlus/Core/FireWhirl/BurningBuilding.cs`:

```csharp
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>燃焼中の建物 1 棟。Game 層が m_fireIntensity &gt; 0 の建物から作る。</summary>
    public struct BurningBuilding
    {
        public readonly ushort Id;
        public readonly Vec2 Position;

        public BurningBuilding(ushort id, Vec2 position)
        {
            Id = id;
            Position = position;
        }
    }
}
```

`src/DisasterPlus/Core/FireWhirl/FireWhirlCandidate.cs`:

```csharp
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>発生候補地点。Center は近傍燃焼建物の重心。</summary>
    public struct FireWhirlCandidate
    {
        public readonly Vec2 Center;
        public readonly int BurningCount;

        public FireWhirlCandidate(Vec2 center, int burningCount)
        {
            Center = center;
            BurningCount = burningCount;
        }
    }
}
```

`src/DisasterPlus/Core/FireWhirl/FireWhirlConfig.cs`:

```csharp
namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// ③火災旋風の設定値。Game 層の ModSettings から詰め替えて Core に渡す。
    /// Core は SavedInt を知らないので、ここは素の値だけを持つ。
    /// </summary>
    public class FireWhirlConfig
    {
        /// <summary>発生判定の半径（メートル）。この距離内に DetectCount 棟あれば発生。</summary>
        public float DetectRadius;

        /// <summary>発生判定の棟数。</summary>
        public int DetectCount;

        /// <summary>候補統合と多重発生抑制に使う最小離隔距離（メートル）。</summary>
        public float MinSeparation;

        /// <summary>絶対上限の寿命（ゲーム内分）。これを超えたら必ず打ち切る。</summary>
        public float MaxLifetimeMinutes;

        /// <summary>発生条件を割り込んでから消滅するまでの猶予（ゲーム内分）。</summary>
        public float ConditionGraceMinutes;

        /// <summary>延焼拡大の強さ。0 で延焼拡大なし、10 が最大。</summary>
        public int SpreadStrength;

        public static FireWhirlConfig Defaults()
        {
            return new FireWhirlConfig
            {
                DetectRadius = 150f,
                DetectCount = 12,
                MinSeparation = 300f,
                MaxLifetimeMinutes = 10f,
                ConditionGraceMinutes = 1f,
                SpreadStrength = 3,
            };
        }
    }
}
```

- [ ] **Step 4: GridVote を実装する**

`src/DisasterPlus/Core/Common/GridVote.cs`:

```csharp
using System.Collections.Generic;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 点を正方セルに投票し、ある地点の近傍セルを引けるようにする。
    /// 全建物同士の総当たり（O(n^2)）を避けるためだけの構造で、それ以上の意味はない。
    /// </summary>
    public class GridVote
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

        public GridVote(float cellSize)
        {
            // 0 除算とセル爆発を防ぐ。呼び出し側の設定ミスをここで吸収する。
            _cellSize = cellSize < 1f ? 1f : cellSize;
        }

        private long KeyOf(int cx, int cz)
        {
            // int 2 つを long 1 つに詰める。負座標があるので unchecked キャストで畳む。
            return ((long)cx << 32) ^ (uint)cz;
        }

        private int CellOf(float v)
        {
            return (int)System.Math.Floor(v / _cellSize);
        }

        /// <summary>インデックス index の点を position のセルに登録する。</summary>
        public void Add(int index, Vec2 position)
        {
            long key = KeyOf(CellOf(position.X), CellOf(position.Z));
            List<int> bucket;
            if (!_cells.TryGetValue(key, out bucket))
            {
                bucket = new List<int>();
                _cells[key] = bucket;
            }
            bucket.Add(index);
        }

        /// <summary>
        /// position から radius 以内にありうる点のインデックスを集める。
        /// セル単位の粗い絞り込みなので、呼び出し側で実距離を必ず再判定すること。
        /// </summary>
        public void CollectNear(Vec2 position, float radius, List<int> into)
        {
            into.Clear();
            int span = (int)System.Math.Ceiling(radius / _cellSize);
            int cx = CellOf(position.X);
            int cz = CellOf(position.Z);
            for (int dx = -span; dx <= span; dx++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    List<int> bucket;
                    if (_cells.TryGetValue(KeyOf(cx + dx, cz + dz), out bucket))
                    {
                        into.AddRange(bucket);
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 5: FireWhirlDetector を実装する**

`src/DisasterPlus/Core/FireWhirl/FireWhirlDetector.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 密集度による火災旋風の発生判定。
    /// 「半径 R 以内に N 棟以上が同時に延焼中」を満たす地点を探す。
    /// 面積だけを見ると郊外の点在火災を足し算してしまうので、密度で判定する。
    /// </summary>
    public static class FireWhirlDetector
    {
        public static List<FireWhirlCandidate> Detect(
            IList<BurningBuilding> burning,
            FireWhirlConfig config,
            IList<Vec2> existingWhirls)
        {
            var result = new List<FireWhirlCandidate>();
            if (burning == null || burning.Count < config.DetectCount) return result;

            // セルは半径と同じ大きさにする。近傍探索が 3x3 セルで済む。
            var grid = new GridVote(config.DetectRadius);
            for (int i = 0; i < burning.Count; i++) grid.Add(i, burning[i].Position);

            float r2 = config.DetectRadius * config.DetectRadius;
            var near = new List<int>();
            var raw = new List<FireWhirlCandidate>();

            for (int i = 0; i < burning.Count; i++)
            {
                grid.CollectNear(burning[i].Position, config.DetectRadius, near);

                int count = 0;
                float sx = 0f, sz = 0f;
                for (int k = 0; k < near.Count; k++)
                {
                    var other = burning[near[k]];
                    if (burning[i].Position.DistanceSquaredTo(other.Position) > r2) continue;
                    count++;
                    sx += other.Position.X;
                    sz += other.Position.Z;
                }

                if (count < config.DetectCount) continue;
                raw.Add(new FireWhirlCandidate(new Vec2(sx / count, sz / count), count));
            }

            if (raw.Count == 0) return result;

            // 燃焼棟数の多い順に確定させ、近すぎる候補を捨てる。
            // 入力順に依存しないよう、同数のときはインデックスで決着させる（決定論のため）。
            var order = new int[raw.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            SortByCountDescending(order, raw);

            float sep2 = config.MinSeparation * config.MinSeparation;

            for (int oi = 0; oi < order.Length; oi++)
            {
                var cand = raw[order[oi]];

                bool blocked = false;

                if (existingWhirls != null)
                {
                    for (int e = 0; e < existingWhirls.Count; e++)
                    {
                        if (cand.Center.DistanceSquaredTo(existingWhirls[e]) < sep2) { blocked = true; break; }
                    }
                }

                if (!blocked)
                {
                    for (int a = 0; a < result.Count; a++)
                    {
                        if (cand.Center.DistanceSquaredTo(result[a].Center) < sep2) { blocked = true; break; }
                    }
                }

                if (!blocked) result.Add(cand);
            }

            return result;
        }

        /// <summary>
        /// 挿入ソート。件数は同時延焼中の建物数どまりなので O(n^2) で足りる。
        /// List.Sort は比較が等しいとき順序を保証しないため、決定論のために自前で書く。
        /// </summary>
        private static void SortByCountDescending(int[] order, List<FireWhirlCandidate> raw)
        {
            for (int i = 1; i < order.Length; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && IsBefore(key, order[j], raw))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
        }

        private static bool IsBefore(int a, int b, List<FireWhirlCandidate> raw)
        {
            if (raw[a].BurningCount != raw[b].BurningCount)
                return raw[a].BurningCount > raw[b].BurningCount;
            return a < b;   // 同数ならインデックス順。入力が同じなら結果も同じになる。
        }
    }
}
```

- [ ] **Step 6: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 16 件すべて PASS（Task 1 の 5 件 + Task 2 の 11 件）

- [ ] **Step 7: コミット**

```bash
git add src/DisasterPlus/Core tests/DisasterPlus.Core.Tests && git commit -m "feat: 密集度による火災旋風の発生判定"
```

---

## Task 3: 寿命の状態機械と強度スケール

**居座り事故の再発防止がこのタスクの主目的。** 「その場に留まる」状態に絶対上限を付けないと、
旋風が永久に居座るか、逆にスタックユニット掃除に消される。上限の回帰テストを必ず書く。

**Files:**
- Create: `src/DisasterPlus/Core/Common/LifetimeClock.cs`
- Create: `src/DisasterPlus/Core/FireWhirl/FireWhirlLifecycle.cs`
- Create: `src/DisasterPlus/Core/FireWhirl/FireWhirlStrength.cs`
- Create: `tests/DisasterPlus.Core.Tests/Common/LifetimeClockTests.cs`
- Create: `tests/DisasterPlus.Core.Tests/FireWhirl/FireWhirlLifecycleTests.cs`
- Create: `tests/DisasterPlus.Core.Tests/FireWhirl/FireWhirlStrengthTests.cs`

**Interfaces:**
- Consumes: `FireWhirlConfig`（Task 2）
- Produces:
  - `DisasterPlus.Core.Common.LifetimeClock` — `struct`、`readonly float ElapsedMinutes`、`static LifetimeClock Start()`、`LifetimeClock Advance(float deltaMinutes)`
  - `DisasterPlus.Core.FireWhirl.FireWhirlVerdict` — `enum { Continue, Dissipate }`
  - `DisasterPlus.Core.FireWhirl.FireWhirlLifecycle` — `struct`、`readonly float ElapsedMinutes`、`readonly float ConditionBrokenMinutes`、`static FireWhirlLifecycle Start()`、`FireWhirlLifecycle Advance(float deltaMinutes, bool conditionMet)`、`FireWhirlVerdict Evaluate(FireWhirlConfig config)`
  - `DisasterPlus.Core.FireWhirl.FireWhirlStrength` — `static float RadiusFor(int burningCount)`、`static float DamageScaleFor(int burningCount)`、`const float MinRadius = 40f`、`const float MaxRadius = 220f`

- [ ] **Step 1: 失敗するテストを書く（寿命）**

`tests/DisasterPlus.Core.Tests/FireWhirl/FireWhirlLifecycleTests.cs`:

```csharp
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class FireWhirlLifecycleTests
    {
        private static FireWhirlConfig Config(float maxLife = 10f, float grace = 1f)
        {
            var c = FireWhirlConfig.Defaults();
            c.MaxLifetimeMinutes = maxLife;
            c.ConditionGraceMinutes = grace;
            return c;
        }

        [Fact]
        public void FreshWhirl_Continues()
        {
            Assert.Equal(FireWhirlVerdict.Continue, FireWhirlLifecycle.Start().Evaluate(Config()));
        }

        [Fact]
        public void ConditionMet_ContinuesUpToMaxLifetime()
        {
            var life = FireWhirlLifecycle.Start();
            for (int i = 0; i < 9; i++) life = life.Advance(1f, true);
            Assert.Equal(FireWhirlVerdict.Continue, life.Evaluate(Config(maxLife: 10f)));
        }

        [Fact]
        public void MaxLifetime_AlwaysDissipates_EvenWhileFireRages()
        {
            // 回帰テスト: 条件を満たし続けても絶対上限で必ず打ち切られること。
            // これが無いと延焼拡大の自己強化ループで旋風が永久に居座る。
            var life = FireWhirlLifecycle.Start();
            for (int i = 0; i < 100; i++) life = life.Advance(1f, true);
            Assert.Equal(FireWhirlVerdict.Dissipate, life.Evaluate(Config(maxLife: 10f)));
        }

        [Fact]
        public void MaxLifetime_BoundaryIsInclusive()
        {
            var life = FireWhirlLifecycle.Start().Advance(10f, true);
            Assert.Equal(FireWhirlVerdict.Dissipate, life.Evaluate(Config(maxLife: 10f)));
        }

        [Fact]
        public void ConditionBroken_ShorterThanGrace_Continues()
        {
            var life = FireWhirlLifecycle.Start().Advance(0.5f, false);
            Assert.Equal(FireWhirlVerdict.Continue, life.Evaluate(Config(grace: 1f)));
        }

        [Fact]
        public void ConditionBroken_BeyondGrace_Dissipates()
        {
            var life = FireWhirlLifecycle.Start().Advance(1.5f, false);
            Assert.Equal(FireWhirlVerdict.Dissipate, life.Evaluate(Config(grace: 1f)));
        }

        [Fact]
        public void ConditionRecovered_ResetsGraceCounter()
        {
            // 火が一瞬弱まってもすぐ戻れば存続する。ちらつきで消えないこと。
            var life = FireWhirlLifecycle.Start()
                .Advance(0.9f, false)
                .Advance(0.1f, true)
                .Advance(0.9f, false);
            Assert.Equal(FireWhirlVerdict.Continue, life.Evaluate(Config(grace: 1f)));
        }

        [Fact]
        public void Advance_AccumulatesElapsedRegardlessOfCondition()
        {
            var life = FireWhirlLifecycle.Start().Advance(2f, true).Advance(3f, false);
            Assert.Equal(5f, life.ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_NegativeDelta_IsIgnored()
        {
            // ポーズやセーブロードで時間が巻き戻ることがある。負値で寿命が伸びてはいけない。
            var life = FireWhirlLifecycle.Start().Advance(5f, true).Advance(-3f, true);
            Assert.Equal(5f, life.ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_ZeroDelta_ChangesNothing()
        {
            // ポーズ中は経過ゼロ。呼ばれても状態が動かないこと。
            var life = FireWhirlLifecycle.Start().Advance(4f, false).Advance(0f, false);
            Assert.Equal(4f, life.ElapsedMinutes, 4);
            Assert.Equal(4f, life.ConditionBrokenMinutes, 4);
        }
    }
}
```

- [ ] **Step 2: 失敗するテストを書く（強度とクロック）**

`tests/DisasterPlus.Core.Tests/FireWhirl/FireWhirlStrengthTests.cs`:

```csharp
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class FireWhirlStrengthTests
    {
        [Fact]
        public void Radius_IsMonotonicNonDecreasing()
        {
            float prev = 0f;
            for (int n = 0; n <= 300; n++)
            {
                float r = FireWhirlStrength.RadiusFor(n);
                Assert.True(r >= prev, "radius decreased at n=" + n);
                prev = r;
            }
        }

        [Fact]
        public void Radius_IsClampedToBounds()
        {
            Assert.Equal(FireWhirlStrength.MinRadius, FireWhirlStrength.RadiusFor(0), 3);
            Assert.Equal(FireWhirlStrength.MinRadius, FireWhirlStrength.RadiusFor(-5), 3);
            Assert.Equal(FireWhirlStrength.MaxRadius, FireWhirlStrength.RadiusFor(100000), 3);
        }

        [Fact]
        public void Radius_SmallFire_IsSmallerThanLargeFire()
        {
            Assert.True(FireWhirlStrength.RadiusFor(12) < FireWhirlStrength.RadiusFor(80));
        }

        [Fact]
        public void DamageScale_IsMonotonicAndBounded()
        {
            float prev = 0f;
            for (int n = 0; n <= 300; n++)
            {
                float s = FireWhirlStrength.DamageScaleFor(n);
                Assert.True(s >= prev, "scale decreased at n=" + n);
                Assert.True(s >= 0f && s <= 1f, "scale out of range: " + s);
                prev = s;
            }
        }
    }
}
```

`tests/DisasterPlus.Core.Tests/Common/LifetimeClockTests.cs`:

```csharp
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    public class LifetimeClockTests
    {
        [Fact]
        public void Start_IsZero()
        {
            Assert.Equal(0f, LifetimeClock.Start().ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_Accumulates()
        {
            var c = LifetimeClock.Start().Advance(1.5f).Advance(2.5f);
            Assert.Equal(4f, c.ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_ClampsNegativeToZero()
        {
            Assert.Equal(3f, LifetimeClock.Start().Advance(3f).Advance(-10f).ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_ReturnsNewValue_DoesNotMutate()
        {
            var a = LifetimeClock.Start().Advance(2f);
            var b = a.Advance(3f);
            Assert.Equal(2f, a.ElapsedMinutes, 4);
            Assert.Equal(5f, b.ElapsedMinutes, 4);
        }
    }
}
```

- [ ] **Step 3: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'FireWhirlLifecycle' could not be found`

- [ ] **Step 4: LifetimeClock を実装する**

`src/DisasterPlus/Core/Common/LifetimeClock.cs`:

```csharp
namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// ゲーム内時間（分）の経過を貯める不変の時計。
    /// 実時間ではない。ポーズ中は Advance(0) が呼ばれるだけで何も進まない。
    /// </summary>
    public struct LifetimeClock
    {
        public readonly float ElapsedMinutes;

        private LifetimeClock(float elapsed)
        {
            ElapsedMinutes = elapsed;
        }

        public static LifetimeClock Start()
        {
            return new LifetimeClock(0f);
        }

        /// <summary>
        /// 経過を足した新しい時計を返す。負の delta は無視する
        /// （セーブロードやポーズ解除でフレーム差が巻き戻ることがあるため）。
        /// </summary>
        public LifetimeClock Advance(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return this;
            return new LifetimeClock(ElapsedMinutes + deltaMinutes);
        }
    }
}
```

- [ ] **Step 5: FireWhirlLifecycle を実装する**

`src/DisasterPlus/Core/FireWhirl/FireWhirlLifecycle.cs`:

```csharp
namespace DisasterPlus.Core.FireWhirl
{
    public enum FireWhirlVerdict
    {
        Continue,
        Dissipate,
    }

    /// <summary>
    /// 火災旋風 1 基の寿命。3 段構えで消える。
    ///   1. 発生元の火災が続いている限り存続
    ///   2. 発生条件を割り込んだ状態が猶予を超えて続いたら消滅
    ///   3. 絶対上限に達したら、火災が続いていても必ず打ち切る
    ///
    /// 3 は必須。延焼拡大は「延焼が増える → 条件を満たし続ける → 旋風が延命する」という
    /// 自己強化ループを作るので、上限が唯一の安全弁になる。
    /// </summary>
    public struct FireWhirlLifecycle
    {
        public readonly float ElapsedMinutes;
        public readonly float ConditionBrokenMinutes;

        private FireWhirlLifecycle(float elapsed, float broken)
        {
            ElapsedMinutes = elapsed;
            ConditionBrokenMinutes = broken;
        }

        public static FireWhirlLifecycle Start()
        {
            return new FireWhirlLifecycle(0f, 0f);
        }

        /// <param name="conditionMet">この時点でまだ発生条件（R 内に N 棟）を満たしているか。</param>
        public FireWhirlLifecycle Advance(float deltaMinutes, bool conditionMet)
        {
            if (deltaMinutes <= 0f) return this;

            // 条件が戻ったら猶予カウンタをリセットする。火勢のちらつきで消えないように。
            float broken = conditionMet ? 0f : ConditionBrokenMinutes + deltaMinutes;
            return new FireWhirlLifecycle(ElapsedMinutes + deltaMinutes, broken);
        }

        public FireWhirlVerdict Evaluate(FireWhirlConfig config)
        {
            if (ElapsedMinutes >= config.MaxLifetimeMinutes) return FireWhirlVerdict.Dissipate;
            if (ConditionBrokenMinutes >= config.ConditionGraceMinutes) return FireWhirlVerdict.Dissipate;
            return FireWhirlVerdict.Continue;
        }
    }
}
```

- [ ] **Step 6: FireWhirlStrength を実装する**

`src/DisasterPlus/Core/FireWhirl/FireWhirlStrength.cs`:

```csharp
namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 燃焼棟数から旋風の大きさと破壊力を決める。
    /// 小さい火災なら小さい旋風、大火災なら大きい旋風。
    /// </summary>
    public static class FireWhirlStrength
    {
        public const float MinRadius = 40f;
        public const float MaxRadius = 220f;

        /// <summary>この棟数で MaxRadius に到達する。</summary>
        private const int SaturationCount = 120;

        /// <summary>旋風の半径（メートル）。</summary>
        public static float RadiusFor(int burningCount)
        {
            return MinRadius + (MaxRadius - MinRadius) * Curve(burningCount);
        }

        /// <summary>破壊力の倍率 [0, 1]。Game 層が VortexAI の破壊半径に掛ける。</summary>
        public static float DamageScaleFor(int burningCount)
        {
            return Curve(burningCount);
        }

        /// <summary>
        /// 0 から 1 へ単調増加し、SaturationCount で 1 に達して飽和する曲線。
        /// 平方根なので序盤の伸びが大きく、大火災でも半径が発散しない。
        /// </summary>
        private static float Curve(int burningCount)
        {
            if (burningCount <= 0) return 0f;
            if (burningCount >= SaturationCount) return 1f;
            return (float)System.Math.Sqrt((double)burningCount / SaturationCount);
        }
    }
}
```

- [ ] **Step 7: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 34 件すべて PASS（Task 1-2 の 16 件 + Task 3 の 18 件）

- [ ] **Step 8: コミット**

```bash
git add src/DisasterPlus/Core tests/DisasterPlus.Core.Tests && git commit -m "feat: 火災旋風の寿命状態機械と強度スケール"
```

---

## Task 4: 延焼拡大の確率モデル

**火災旋風という現象の核心。** これが無いと「ただの動かない竜巻」になる。
競合MOD が `DisasterHelpers` の破壊を差し替えても、この層は自前なので影響を受けない。

**Files:**
- Create: `src/DisasterPlus/Core/FireWhirl/IgnitionCandidate.cs`
- Create: `src/DisasterPlus/Core/FireWhirl/IgnitionSpread.cs`
- Create: `tests/DisasterPlus.Core.Tests/FireWhirl/IgnitionSpreadTests.cs`

**Interfaces:**
- Consumes: `Vec2`、`DeterministicRandom`（Task 1）、`FireWhirlConfig`（Task 2）
- Produces:
  - `DisasterPlus.Core.FireWhirl.IgnitionCandidate` — `struct`、`readonly ushort Id`、`readonly Vec2 Position`、`readonly bool AlreadyBurning`、コンストラクタ `IgnitionCandidate(ushort id, Vec2 position, bool alreadyBurning)`
  - `DisasterPlus.Core.FireWhirl.IgnitionSpread` — `static void Select(Vec2 center, float radius, int spreadStrength, IList<IgnitionCandidate> nearby, uint tick, List<ushort> into)`

- [ ] **Step 1: 失敗するテストを書く**

`tests/DisasterPlus.Core.Tests/FireWhirl/IgnitionSpreadTests.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class IgnitionSpreadTests
    {
        private static List<IgnitionCandidate> Ring(int n, float distance, bool burning = false)
        {
            var list = new List<IgnitionCandidate>();
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * System.Math.PI * i / n;
                var p = new Vec2((float)System.Math.Cos(a) * distance, (float)System.Math.Sin(a) * distance);
                list.Add(new IgnitionCandidate((ushort)(i + 1), p, burning));
            }
            return list;
        }

        private static int CountOverTicks(List<IgnitionCandidate> near, int strength, int ticks, float radius = 100f)
        {
            var into = new List<ushort>();
            int total = 0;
            for (uint t = 0; t < ticks; t++)
            {
                IgnitionSpread.Select(new Vec2(0f, 0f), radius, strength, near, t, into);
                total += into.Count;
            }
            return total;
        }

        [Fact]
        public void Strength0_IgnitesNothing()
        {
            Assert.Equal(0, CountOverTicks(Ring(50, 60f), 0, 200));
        }

        [Fact]
        public void Strength10_IgnitesSomething()
        {
            Assert.True(CountOverTicks(Ring(50, 60f), 10, 200) > 0);
        }

        [Fact]
        public void HigherStrength_IgnitesMore()
        {
            var near = Ring(50, 60f);
            int low = CountOverTicks(near, 2, 400);
            int high = CountOverTicks(near, 9, 400);
            Assert.True(high > low, "high=" + high + " low=" + low);
        }

        [Fact]
        public void AlreadyBurning_IsNeverSelected()
        {
            var near = Ring(50, 60f, burning: true);
            Assert.Equal(0, CountOverTicks(near, 10, 200));
        }

        [Fact]
        public void OutsideRadius_IsNeverSelected()
        {
            // 半径 100m に対して 400m 先の建物は対象外
            Assert.Equal(0, CountOverTicks(Ring(50, 400f), 10, 200, radius: 100f));
        }

        [Fact]
        public void CloserBuildings_IgniteMoreOften()
        {
            // 距離減衰があること。旋風のすぐ脇の方が燃えやすい。
            int close = CountOverTicks(Ring(40, 20f), 6, 400, radius: 200f);
            int far = CountOverTicks(Ring(40, 190f), 6, 400, radius: 200f);
            Assert.True(close > far, "close=" + close + " far=" + far);
        }

        [Fact]
        public void SameTickAndId_ProducesSameResult()
        {
            var near = Ring(40, 50f);
            var a = new List<ushort>();
            var b = new List<ushort>();
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 5, near, 77u, a);
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 5, near, 77u, b);
            Assert.Equal(a, b);
        }

        [Fact]
        public void DifferentTicks_ProduceDifferentResults()
        {
            // 毎 tick 同じ建物だけが選ばれると、延焼が広がらない。
            var near = Ring(60, 50f);
            var seen = new HashSet<ushort>();
            var into = new List<ushort>();
            for (uint t = 0; t < 100; t++)
            {
                IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 8, near, t, into);
                foreach (var id in into) seen.Add(id);
            }
            Assert.True(seen.Count > 5, "only " + seen.Count + " distinct buildings ignited");
        }

        [Fact]
        public void Select_ClearsOutputList()
        {
            var into = new List<ushort> { 999 };
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 0, Ring(10, 50f), 1u, into);
            Assert.DoesNotContain((ushort)999, into);
        }

        [Fact]
        public void EmptyCandidates_IsSafe()
        {
            var into = new List<ushort>();
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 10, new List<IgnitionCandidate>(), 1u, into);
            Assert.Empty(into);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'IgnitionSpread' could not be found`

- [ ] **Step 3: IgnitionCandidate を実装する**

`src/DisasterPlus/Core/FireWhirl/IgnitionCandidate.cs`:

```csharp
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>延焼拡大の判定対象になる建物 1 棟。</summary>
    public struct IgnitionCandidate
    {
        public readonly ushort Id;
        public readonly Vec2 Position;
        public readonly bool AlreadyBurning;

        public IgnitionCandidate(ushort id, Vec2 position, bool alreadyBurning)
        {
            Id = id;
            Position = position;
            AlreadyBurning = alreadyBurning;
        }
    }
}
```

- [ ] **Step 4: IgnitionSpread を実装する**

`src/DisasterPlus/Core/FireWhirl/IgnitionSpread.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 旋風の周囲に火の粉を撒く確率モデル。
    /// 現実の火災旋風は火の粉で延焼を加速させるので、これが現象の核心になる。
    ///
    /// 意図的に DisasterHelpers を経由しない。競合MOD が DisasterHelpers.DestroyBuildings を
    /// 差し替えていても、この層だけは必ず動く（設計書 3.3(b)）。
    /// </summary>
    public static class IgnitionSpread
    {
        /// <summary>SpreadStrength の最大値。ModSettings のスライダー上限と一致させること。</summary>
        public const int MaxStrength = 10;

        /// <summary>強度 10・距離ゼロのときの 1 tick あたり発火確率。</summary>
        private const float BaseProbabilityAtMaxStrength = 0.02f;

        public static void Select(
            Vec2 center,
            float radius,
            int spreadStrength,
            IList<IgnitionCandidate> nearby,
            uint tick,
            List<ushort> into)
        {
            into.Clear();
            if (spreadStrength <= 0 || nearby == null || nearby.Count == 0) return;
            if (radius <= 0f) return;

            int strength = spreadStrength > MaxStrength ? MaxStrength : spreadStrength;
            float strengthScale = strength / (float)MaxStrength;
            float r2 = radius * radius;

            for (int i = 0; i < nearby.Count; i++)
            {
                var c = nearby[i];
                if (c.AlreadyBurning) continue;

                float d2 = center.DistanceSquaredTo(c.Position);
                if (d2 > r2) continue;

                // 距離減衰。中心で 1、外周で 0 になる線形フォールオフ。
                float falloff = 1f - (float)System.Math.Sqrt(d2 / r2);
                float p = BaseProbabilityAtMaxStrength * strengthScale * falloff;
                if (p <= 0f) continue;

                // System.Random は使わない。(tick, buildingId) から決めるので、
                // セーブ・ロードしても、テストを何度回しても同じ結果になる。
                if (DeterministicRandom.Unit(tick, c.Id) < p) into.Add(c.Id);
            }
        }
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 44 件すべて PASS

- [ ] **Step 6: コミット**

```bash
git add src/DisasterPlus/Core tests/DisasterPlus.Core.Tests && git commit -m "feat: 延焼拡大の確率モデル"
```

---

## Task 5: レイと地形の交差

**CS の地形に Unity コライダーは無い。** `Physics.Raycast` は地面に**絶対に当たらない**。
クリック配置のために、カメラレイと高さ場の交差を自前で計算する。純粋な数学なので Core に置く。

**Files:**
- Create: `src/DisasterPlus/Core/Common/Vec3.cs`
- Create: `src/DisasterPlus/Core/Common/IHeightSampler.cs`
- Create: `src/DisasterPlus/Core/Common/RayGeometry.cs`
- Create: `tests/DisasterPlus.Core.Tests/Common/RayGeometryTests.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `DisasterPlus.Core.Common.Vec3` — `struct`、`readonly float X, Y, Z`、コンストラクタ `Vec3(float x, float y, float z)`、`Vec2 ToVec2()`
  - `DisasterPlus.Core.Common.IHeightSampler` — `float SampleHeight(float x, float z)`
  - `DisasterPlus.Core.Common.RayGeometry` — `static bool IntersectTerrain(Vec3 origin, Vec3 direction, IHeightSampler sampler, float maxDistance, out Vec3 hit)`。`direction` は正規化済みを前提とする

- [ ] **Step 1: 失敗するテストを書く**

`tests/DisasterPlus.Core.Tests/Common/RayGeometryTests.cs`:

```csharp
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    public class RayGeometryTests
    {
        private class FlatGround : IHeightSampler
        {
            private readonly float _h;
            public FlatGround(float h) { _h = h; }
            public float SampleHeight(float x, float z) { return _h; }
        }

        /// <summary>X が増えるほど高くなる斜面。</summary>
        private class Slope : IHeightSampler
        {
            public float SampleHeight(float x, float z) { return x * 0.5f; }
        }

        private static Vec3 Down45()
        {
            float k = (float)(1.0 / System.Math.Sqrt(2.0));
            return new Vec3(k, -k, 0f);
        }

        [Fact]
        public void StraightDown_HitsFlatGroundAtSampledHeight()
        {
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(100f, 500f, 200f), new Vec3(0f, -1f, 0f),
                new FlatGround(80f), 2000f, out hit);

            Assert.True(ok);
            Assert.Equal(100f, hit.X, 1);
            Assert.Equal(200f, hit.Z, 1);
            Assert.Equal(80f, hit.Y, 1);
        }

        [Fact]
        public void Diagonal_HitsFlatGround_AtGeometricallyCorrectPoint()
        {
            // 高さ 100 から 45 度で降りる → 水平に 100 進んだ地点で高さ 0 に到達
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(0f, 100f, 0f), Down45(), new FlatGround(0f), 2000f, out hit);

            Assert.True(ok);
            Assert.Equal(100f, hit.X, 0);
            Assert.Equal(0f, hit.Y, 0);
        }

        [Fact]
        public void RayPointingUp_Misses()
        {
            Vec3 hit;
            Assert.False(RayGeometry.IntersectTerrain(
                new Vec3(0f, 100f, 0f), new Vec3(0f, 1f, 0f),
                new FlatGround(0f), 2000f, out hit));
        }

        [Fact]
        public void ShortMaxDistance_Misses()
        {
            Vec3 hit;
            Assert.False(RayGeometry.IntersectTerrain(
                new Vec3(0f, 1000f, 0f), new Vec3(0f, -1f, 0f),
                new FlatGround(0f), 50f, out hit));
        }

        [Fact]
        public void OriginAlreadyBelowGround_HitsImmediately()
        {
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(10f, -5f, 10f), new Vec3(0f, -1f, 0f),
                new FlatGround(0f), 500f, out hit);
            Assert.True(ok);
        }

        [Fact]
        public void Slope_HitIsOnTheSurface()
        {
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(0f, 400f, 0f), Down45(), new Slope(), 4000f, out hit);

            Assert.True(ok);
            // 収束した点で「レイの高さ ≒ 地形の高さ」になっていること
            Assert.Equal(new Slope().SampleHeight(hit.X, hit.Z), hit.Y, 0);
        }

        [Fact]
        public void IsDeterministic()
        {
            Vec3 a, b;
            RayGeometry.IntersectTerrain(new Vec3(5f, 300f, 5f), Down45(), new Slope(), 3000f, out a);
            RayGeometry.IntersectTerrain(new Vec3(5f, 300f, 5f), Down45(), new Slope(), 3000f, out b);
            Assert.Equal(a.X, b.X, 5);
            Assert.Equal(a.Y, b.Y, 5);
            Assert.Equal(a.Z, b.Z, 5);
        }
    }
}
```

- [ ] **Step 2: 失敗を確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: コンパイルエラー `The type or namespace name 'RayGeometry' could not be found`

- [ ] **Step 3: Vec3 と IHeightSampler を実装する**

`src/DisasterPlus/Core/Common/Vec3.cs`:

```csharp
namespace DisasterPlus.Core.Common
{
    /// <summary>3 次元ベクトル。Core は UnityEngine.Vector3 を参照できないので自前で持つ。</summary>
    public struct Vec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vec2 ToVec2()
        {
            return new Vec2(X, Z);
        }
    }
}
```

`src/DisasterPlus/Core/Common/IHeightSampler.cs`:

```csharp
namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 地形の高さを引く。Game 層が TerrainManager.SampleDetailHeight で実装する。
    /// SampleDetailHeight は読み取り専用でどちらのスレッドからも安全。
    /// </summary>
    public interface IHeightSampler
    {
        float SampleHeight(float x, float z);
    }
}
```

- [ ] **Step 4: RayGeometry を実装する**

`src/DisasterPlus/Core/Common/RayGeometry.cs`:

```csharp
namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// カメラレイと地形高さ場の交差。
    ///
    /// CS の地形は Unity Physics に登録されていないので Physics.Raycast では絶対に当たらない。
    /// 粗くマーチして地面を跨いだ区間を見つけ、その中で二分法に切り替える。
    /// </summary>
    public static class RayGeometry
    {
        /// <summary>粗マーチの刻み幅（メートル）。詳細マップは約 4m/セルなので 16m で跨ぎを見逃さない。</summary>
        private const float MarchStep = 16f;

        /// <summary>二分法の反復回数。16m を 2^20 分割すれば十分に収束する。</summary>
        private const int BisectionIterations = 20;

        /// <param name="direction">正規化済みの方向ベクトル。</param>
        /// <returns>地面と交差したら true。hit に交点が入る。</returns>
        public static bool IntersectTerrain(
            Vec3 origin,
            Vec3 direction,
            IHeightSampler sampler,
            float maxDistance,
            out Vec3 hit)
        {
            hit = origin;
            if (sampler == null || maxDistance <= 0f) return false;

            // 上を向いているレイは地面に当たらない。
            if (direction.Y >= 0f) return false;

            float prevT = 0f;
            bool prevBelow = IsBelowGround(origin, direction, 0f, sampler);
            if (prevBelow)
            {
                // 始点が既に地中。そこを交点として扱う。
                hit = PointAt(origin, direction, 0f);
                return true;
            }

            for (float t = MarchStep; ; t += MarchStep)
            {
                if (t > maxDistance) t = maxDistance;

                bool below = IsBelowGround(origin, direction, t, sampler);
                if (below)
                {
                    hit = Bisect(origin, direction, prevT, t, sampler);
                    return true;
                }

                if (t >= maxDistance) return false;
                prevT = t;
            }
        }

        private static Vec3 PointAt(Vec3 origin, Vec3 direction, float t)
        {
            return new Vec3(
                origin.X + direction.X * t,
                origin.Y + direction.Y * t,
                origin.Z + direction.Z * t);
        }

        private static bool IsBelowGround(Vec3 origin, Vec3 direction, float t, IHeightSampler sampler)
        {
            Vec3 p = PointAt(origin, direction, t);
            return p.Y <= sampler.SampleHeight(p.X, p.Z);
        }

        /// <summary>lo は地上、hi は地中と分かっている区間を詰める。</summary>
        private static Vec3 Bisect(Vec3 origin, Vec3 direction, float lo, float hi, IHeightSampler sampler)
        {
            for (int i = 0; i < BisectionIterations; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (IsBelowGround(origin, direction, mid, sampler)) hi = mid;
                else lo = mid;
            }
            return PointAt(origin, direction, hi);
        }
    }
}
```

- [ ] **Step 5: テストが通ることを確認する**

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

Expected: 51 件すべて PASS。**これで Core は完成し、以降のタスクはすべて Game 層になる。**

- [ ] **Step 6: コミット**

```bash
git add src/DisasterPlus/Core tests/DisasterPlus.Core.Tests && git commit -m "feat: レイと地形高さ場の交差（コライダー非依存）"
```

---

## Task 6: ゲームに読み込まれる MOD 骨格

**このタスクの成果物は「ゲームが MOD をエラーなしで読み込み、設定画面が出る」こと。**
Game 層はユニットテストできないので、検証はゲームを起動して `output_log.txt` を読む。

**Files:**
- Create: `src/DisasterPlus/DisasterPlus.csproj`
- Create: `src/DisasterPlus/Properties/AssemblyInfo.cs`
- Create: `src/DisasterPlus/Game/Mod.cs`
- Create: `src/DisasterPlus/Game/ModSettings.cs`
- Create: `src/DisasterPlus/Game/Common/Log.cs`
- Create: `build.ps1`

**Interfaces:**
- Consumes: `FireWhirlConfig`（Task 2）、`IgnitionSpread.MaxStrength`（Task 4）
- Produces:
  - `DisasterPlus.Game.Log` — `static void Info(string message)`、`static void Warn(string message)`、`static void Error(string message, System.Exception e)`、`static void Diag(string key, string message)`（同一 key は 10 秒に 1 回まで）
  - `DisasterPlus.Game.ModSettings` — `static void Ensure()`、`static SavedBool FireWhirlEnabled`、`static SavedInt DetectRadius`、`static SavedInt DetectCount`、`static SavedInt MaxLifetimeMinutes`、`static SavedInt SpreadStrength`、`static SavedInt MinSeparation`、`static SavedBool IntensityUnlock`、`static SavedInt EarthquakeDamageOwner`、`static FireWhirlConfig ToFireWhirlConfig()`
  - `DisasterPlus.Game.Mod` — `IUserMod` 実装

- [ ] **Step 1: csproj を作る**

`src/DisasterPlus/DisasterPlus.csproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="4.0" DefaultTargets="Build"
         xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Release</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{6E1B0A62-5B4E-4C1F-9C1D-3F0A2D7C4B10}</ProjectGuid>
    <OutputType>Library</OutputType>
    <RootNamespace>DisasterPlus</RootNamespace>
    <AssemblyName>DisasterPlus</AssemblyName>
    <TargetFrameworkVersion>v3.5</TargetFrameworkVersion>
    <LangVersion>7.3</LangVersion>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>

  <PropertyGroup Condition=" '$(Configuration)' == 'Release' ">
    <DebugType>none</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin\Release\</OutputPath>
    <WarningLevel>4</WarningLevel>
    <!-- CS0184 (常に false になる is 判定) を警告で見逃すと、
         継承チェーンの取り違えが黙って機能を殺す。エラーに昇格させる。 -->
    <WarningsAsErrors>0184</WarningsAsErrors>
  </PropertyGroup>

  <!-- マシン固有パスを焼き込まない。環境変数が無ければ既定の Steam パスを使う。 -->
  <PropertyGroup>
    <ManagedDir Condition=" '$(CITIES_SKYLINES_MANAGED)' != '' ">$(CITIES_SKYLINES_MANAGED)</ManagedDir>
    <ManagedDir Condition=" '$(ManagedDir)' == '' ">C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed</ManagedDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ManagedDir)\Assembly-CSharp.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="ICities">
      <HintPath>$(ManagedDir)\ICities.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="ColossalManaged">
      <HintPath>$(ManagedDir)\ColossalManaged.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(ManagedDir)\UnityEngine.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>

  <!-- glob。新規ファイルを足しても csproj を触らなくてよい。 -->
  <ItemGroup>
    <Compile Include="Core\**\*.cs" />
    <Compile Include="Game\**\*.cs" />
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>

  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

`src/DisasterPlus/Properties/AssemblyInfo.cs`:

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("DisasterPlus")]
[assembly: AssemblyProduct("Disaster +")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: ComVisible(false)]
```

- [ ] **Step 2: 診断ログを作る**

`src/DisasterPlus/Game/Common/Log.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断ログ。output_log.txt は
    /// &lt;Steam&gt;\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt にある（AppData ではない）。
    /// Diag はキーごとにスロットリングする。毎 tick 垂れ流すとログが使い物にならなくなる。
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[DisasterPlus] ";
        private const float DiagIntervalSeconds = 10f;

        private static readonly Dictionary<string, float> _lastDiag = new Dictionary<string, float>();

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message, System.Exception e)
        {
            Debug.LogError(Prefix + message + (e == null ? "" : " :: " + e));
        }

        /// <summary>同じ key では DiagIntervalSeconds に 1 回しか出さない。</summary>
        public static void Diag(string key, string message)
        {
            float now = Time.realtimeSinceStartup;
            float last;
            if (_lastDiag.TryGetValue(key, out last) && now - last < DiagIntervalSeconds) return;
            _lastDiag[key] = now;
            Debug.Log(Prefix + "DIAG " + key + ": " + message);
        }

        /// <summary>レベルアンロード時に呼ぶ。都市をまたいでスロットル状態を持ち越さない。</summary>
        public static void Reset()
        {
            _lastDiag.Clear();
        }
    }
}
```

- [ ] **Step 3: 設定を作る**

`src/DisasterPlus/Game/ModSettings.cs`:

```csharp
using ColossalFramework;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 永続設定。
    ///
    /// ファイル名を MOD 名・アセンブリ名と同じ "DisasterPlus" にしてはいけない。
    /// 毎起動で "An element with the same key already exists ... Deleting" が出て
    /// 設定が消え、MOD がエラー扱いになり Workshop 公開まで壊れる。
    ///
    /// 保存されるキーと値は公開契約として扱う。列挙由来の整数値は意味を固定し、
    /// 項目を廃止するときも番号を詰めない（既存プレイヤーの .cgs の値が別物になるため）。
    /// </summary>
    public static class ModSettings
    {
        public const string FileName = "DisasterPlusSettings";

        /// <summary>地震の被害計算の担当。0 = 競合MODに任せる、1 = Disaster + が担当。
        /// この数値は .cgs に書かれる公開契約。値の意味を変えたり詰めたりしないこと。</summary>
        public const int EarthquakeOwnerOther = 0;
        public const int EarthquakeOwnerSelf = 1;

        private static bool _ready;

        public static SavedBool FireWhirlEnabled;
        public static SavedInt DetectRadius;
        public static SavedInt DetectCount;
        public static SavedInt MaxLifetimeMinutes;
        public static SavedInt SpreadStrength;
        public static SavedInt MinSeparation;
        public static SavedBool IntensityUnlock;
        public static SavedInt EarthquakeDamageOwner;

        public static void Ensure()
        {
            if (_ready) return;

            if (GameSettings.FindSettingsFileByName(FileName) == null)
            {
                GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });
            }

            FireWhirlEnabled      = new SavedBool("fireWhirlEnabled", FileName, true, true);
            DetectRadius          = new SavedInt("fwDetectRadius", FileName, 150, true);
            DetectCount           = new SavedInt("fwDetectCount", FileName, 12, true);
            MaxLifetimeMinutes    = new SavedInt("fwMaxLifetime", FileName, 10, true);
            SpreadStrength        = new SavedInt("fwSpreadStrength", FileName, 3, true);
            MinSeparation         = new SavedInt("fwMinSeparation", FileName, 300, true);
            IntensityUnlock       = new SavedBool("intensityUnlock", FileName, true, true);
            EarthquakeDamageOwner = new SavedInt("eqDamageOwner", FileName, EarthquakeOwnerOther, true);

            _ready = true;
        }

        /// <summary>設定値を Core の設定オブジェクトへ詰め替える。Core は SavedInt を知らない。</summary>
        public static FireWhirlConfig ToFireWhirlConfig()
        {
            Ensure();
            var c = FireWhirlConfig.Defaults();
            c.DetectRadius = DetectRadius.value;
            c.DetectCount = DetectCount.value;
            c.MaxLifetimeMinutes = MaxLifetimeMinutes.value;
            c.MinSeparation = MinSeparation.value;
            c.SpreadStrength = SpreadStrength.value;
            return c;
        }
    }
}
```

- [ ] **Step 4: IUserMod を作る**

`src/DisasterPlus/Game/Mod.cs`:

```csharp
using ICities;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    public class Mod : IUserMod
    {
        public string Name { get { return "Disaster +"; } }

        public string Description
        {
            get { return "Adds realistic disaster phenomena: fire whirls, typhoons, volcanoes and hazard visualisation."; }
        }

        /// <summary>
        /// メインメニューの起動中に 1 回だけ呼ばれる（言語切替でも再実行される）。
        /// レベルロード後にしか分からない情報からオプションを組み立ててはいけない。
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            ModSettings.Ensure();

            var fw = helper.AddGroup("Fire whirl");
            fw.AddCheckbox("Enable fire whirls", ModSettings.FireWhirlEnabled.value,
                v => ModSettings.FireWhirlEnabled.value = v);

            fw.AddSlider("Detection radius (m)", 50f, 400f, 10f, ModSettings.DetectRadius.value,
                v => ModSettings.DetectRadius.value = (int)v);

            fw.AddSlider("Buildings required", 4f, 40f, 1f, ModSettings.DetectCount.value,
                v => ModSettings.DetectCount.value = (int)v);

            fw.AddSlider("Maximum lifetime (in-game minutes)", 1f, 60f, 1f, ModSettings.MaxLifetimeMinutes.value,
                v => ModSettings.MaxLifetimeMinutes.value = (int)v);

            fw.AddSlider("Fire spread strength (0 = off)", 0f, IgnitionSpread.MaxStrength, 1f,
                ModSettings.SpreadStrength.value, v => ModSettings.SpreadStrength.value = (int)v);

            fw.AddSlider("Minimum separation (m)", 100f, 800f, 25f, ModSettings.MinSeparation.value,
                v => ModSettings.MinSeparation.value = (int)v);

            var general = helper.AddGroup("General");
            general.AddCheckbox("Unlock disaster intensity up to 25.5", ModSettings.IntensityUnlock.value,
                v => ModSettings.IntensityUnlock.value = v);
        }
    }
}
```

- [ ] **Step 5: build.ps1 を作る**

`build.ps1`:

```powershell
$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe |
           Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found" }

& $msbuild "src\DisasterPlus\DisasterPlus.csproj" /t:Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$modDir = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\DisasterPlus"
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item "src\DisasterPlus\bin\Release\DisasterPlus.dll" $modDir -Force
Write-Host "Deployed DisasterPlus.dll -> $modDir"

# LocaleLoader は実行時に Locales\<lang>.txt を読む。
if (Test-Path "Locales") {
    $dst = Join-Path $modDir "Locales"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item "Locales\*" $dst -Force
    Write-Host "Deployed Locales"
} else {
    Write-Host "Note: Locales\ not found; skipped (added in a later task)."
}
```

- [ ] **Step 6: ビルドして配置する**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: `Deployed DisasterPlus.dll -> ...\Addons\Mods\DisasterPlus`。警告 0。

- [ ] **Step 7: ゲームで確認する（手動）**

1. Cities: Skylines を起動する
2. Content Manager → Mods に **Disaster +** が出ていること。**赤いエラー表示が無いこと**
3. 有効化して Options を開き、「Fire whirl」グループのスライダーが 6 つ出ること
4. ゲームを終了し、`<Steam>\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt` を確認する

**確認するログ:**
- `Loading DisasterPlusSettings` が 1 回出ていること
- **`trying to load .* Deleting` が出ていないこと**（出ていたら設定ファイル名の衝突。`ModSettings.FileName` を見直す）
- `DisasterPlus` を含む例外が無いこと

- [ ] **Step 8: コミット**

```bash
git add src/DisasterPlus build.ps1 && git commit -m "feat: MOD 骨格（csproj・設定・ログ・ビルドスクリプト）"
```

---

## Task 7: ローカライズ

`OnSettingsUI` は言語切替で再実行される（`OptionsMainPanel.OnLocaleChanged` → `CreateCategories`
→ `AddUserMods` → `OnSettingsUI`）。ローダーを先頭で呼べば、オプション画面は再起動なしで追従する。

**Files:**
- Create: `src/DisasterPlus/Game/Localization/Strings.cs`
- Create: `src/DisasterPlus/Game/Localization/LocaleLoader.cs`
- Create: `Locales/en.txt`
- Create: `Locales/ja.txt`
- Modify: `src/DisasterPlus/Game/Mod.cs`（文字列リテラルを `Strings` 参照に置換、先頭で `LocaleLoader.Apply()`）

**Interfaces:**
- Consumes: `Log`（Task 6）
- Produces:
  - `DisasterPlus.Game.Strings` — 全表示文字列を `public static string` フィールドで保持
  - `DisasterPlus.Game.LocaleLoader` — `static void Apply()`、`static void WriteTemplate(string path)`

- [ ] **Step 1: Strings を作る**

`src/DisasterPlus/Game/Localization/Strings.cs`:

```csharp
namespace DisasterPlus.Game
{
    /// <summary>
    /// 全ての表示文字列。英語を既定値として持ち、LocaleLoader がフィールド名をキーに
    /// リフレクションで上書きする。
    ///
    /// 必ず public static フィールドにすること（const にすると書き換えられない）。
    /// ドロップダウン用の配列をここに static readonly で置いてはいけない。
    /// 型初期化時の言語で凍結する。必要ならメソッドにして毎回組み直す。
    /// </summary>
    public static class Strings
    {
        public static string ModDescription =
            "Adds realistic disaster phenomena: fire whirls, typhoons, volcanoes and hazard visualisation.";

        public static string GroupFireWhirl = "Fire whirl";
        public static string GroupGeneral = "General";

        public static string FireWhirlEnabled = "Enable fire whirls";
        public static string DetectRadius = "Detection radius (m)";
        public static string DetectCount = "Buildings required";
        public static string MaxLifetime = "Maximum lifetime (in-game minutes)";
        public static string SpreadStrength = "Fire spread strength (0 = off)";
        public static string MinSeparation = "Minimum separation (m)";

        public static string IntensityUnlock = "Unlock disaster intensity up to 25.5";
        public static string IntensityUnlockHandledByOther =
            "Handled by Natural Disasters Renewal. Enable only if you want Disaster + to control it.";

        public static string EarthquakeDamageOwner = "Earthquake damage is calculated by";
        public static string EarthquakeOwnerOther = "Natural Disasters Renewal";
        public static string EarthquakeOwnerSelf = "Disaster +";

        public static string FireWhirlName = "Fire whirl";
        public static string FireWhirlTooltip = "Place a stationary, burning vortex";

        public static string NdrDetected =
            "Natural Disasters Renewal detected. Vanilla-side destruction follows its tornado settings; "
            + "fire spread is unaffected.";
    }
}
```

- [ ] **Step 2: LocaleLoader を作る**

`src/DisasterPlus/Game/Localization/LocaleLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ColossalFramework.Globalization;
using ColossalFramework.Plugins;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Locales/&lt;lang&gt;.txt を読み、Strings の public static string フィールドを
    /// フィールド名をキーに上書きする。
    ///
    /// 英語の既定値を最初に 1 回だけ退避し、別言語を適用する前に必ず復元する。
    /// そうしないと部分翻訳の言語を行き来したとき、前の言語の訳が残る。
    /// </summary>
    public static class LocaleLoader
    {
        private static Dictionary<string, string> _englishDefaults;
        private static string _appliedLanguage;

        private static FieldInfo[] Fields()
        {
            return typeof(Strings).GetFields(BindingFlags.Public | BindingFlags.Static);
        }

        private static void CaptureDefaults()
        {
            if (_englishDefaults != null) return;
            _englishDefaults = new Dictionary<string, string>();
            foreach (var f in Fields())
            {
                if (f.FieldType != typeof(string)) continue;
                _englishDefaults[f.Name] = (string)f.GetValue(null);
            }
        }

        private static void RestoreDefaults()
        {
            foreach (var f in Fields())
            {
                if (f.FieldType != typeof(string)) continue;
                string v;
                if (_englishDefaults.TryGetValue(f.Name, out v)) f.SetValue(null, v);
            }
        }

        /// <summary>MOD の配置フォルダ。Workshop 版とローカル版の両方に対応する。</summary>
        private static string ModDirectory()
        {
            foreach (var p in PluginManager.instance.GetPluginsInfo())
            {
                if (p == null || !p.isEnabled) continue;
                try
                {
                    foreach (var inst in p.GetInstances<Mod>())
                    {
                        if (inst != null) return p.modPath;
                    }
                }
                catch { /* 壊れた MOD の列挙で落ちない */ }
            }
            return null;
        }

        public static void Apply()
        {
            try
            {
                CaptureDefaults();

                string lang = LocaleManager.exists ? LocaleManager.instance.language : "en";
                if (string.IsNullOrEmpty(lang)) lang = "en";
                if (lang == _appliedLanguage) return;

                RestoreDefaults();
                _appliedLanguage = lang;
                if (lang == "en") return;

                string dir = ModDirectory();
                if (dir == null) return;

                string path = Path.Combine(Path.Combine(dir, "Locales"), lang + ".txt");
                if (!File.Exists(path)) return;   // 未翻訳の言語は英語のまま

                var byName = new Dictionary<string, FieldInfo>();
                foreach (var f in Fields())
                {
                    if (f.FieldType == typeof(string)) byName[f.Name] = f;
                }

                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim().Replace("\\n", "\n");

                    FieldInfo f;
                    if (byName.TryGetValue(key, out f)) f.SetValue(null, value);
                }
            }
            catch (Exception e)
            {
                Log.Error("locale load failed", e);
            }
        }

        /// <summary>en.txt を既定値から書き出す。手書きテンプレートは黙って乖離するので使わない。</summary>
        public static void WriteTemplate(string path)
        {
            CaptureDefaults();
            var sb = new StringBuilder();
            sb.AppendLine("# Disaster + - English template (generated).");
            sb.AppendLine("# Copy to <lang>.txt and translate the right-hand side. Use \\n for line breaks.");
            foreach (var f in Fields())
            {
                if (f.FieldType != typeof(string)) continue;
                sb.AppendLine(f.Name + " = " + ((string)f.GetValue(null)).Replace("\n", "\\n"));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
```

- [ ] **Step 3: Mod.cs を文字列参照に書き換える**

`OnSettingsUI` の先頭に `LocaleLoader.Apply();` を足し、リテラルを置き換える。
`Description` プロパティも `Strings.ModDescription` を返すようにする。

```csharp
public string Description { get { return Strings.ModDescription; } }

public void OnSettingsUI(UIHelperBase helper)
{
    // 言語切替でもこのメソッドは再実行される。ここで読み直せばオプション画面が追従する。
    // ただしゲーム内ボタンのツールチップはレベルロード時に一度設定されるだけなので、
    // 次のロードまで前の言語のままになる。
    LocaleLoader.Apply();
    ModSettings.Ensure();

    var fw = helper.AddGroup(Strings.GroupFireWhirl);
    fw.AddCheckbox(Strings.FireWhirlEnabled, ModSettings.FireWhirlEnabled.value,
        v => ModSettings.FireWhirlEnabled.value = v);
    fw.AddSlider(Strings.DetectRadius, 50f, 400f, 10f, ModSettings.DetectRadius.value,
        v => ModSettings.DetectRadius.value = (int)v);
    fw.AddSlider(Strings.DetectCount, 4f, 40f, 1f, ModSettings.DetectCount.value,
        v => ModSettings.DetectCount.value = (int)v);
    fw.AddSlider(Strings.MaxLifetime, 1f, 60f, 1f, ModSettings.MaxLifetimeMinutes.value,
        v => ModSettings.MaxLifetimeMinutes.value = (int)v);
    fw.AddSlider(Strings.SpreadStrength, 0f, IgnitionSpread.MaxStrength, 1f,
        ModSettings.SpreadStrength.value, v => ModSettings.SpreadStrength.value = (int)v);
    fw.AddSlider(Strings.MinSeparation, 100f, 800f, 25f, ModSettings.MinSeparation.value,
        v => ModSettings.MinSeparation.value = (int)v);

    var general = helper.AddGroup(Strings.GroupGeneral);
    general.AddCheckbox(Strings.IntensityUnlock, ModSettings.IntensityUnlock.value,
        v => ModSettings.IntensityUnlock.value = v);
}
```

- [ ] **Step 4: en.txt と ja.txt を作る**

`Locales/en.txt` は Task 16 でビルド済み DLL から自動生成する。いまは `Strings` の既定値を
そのまま写した内容を置く。`Locales/ja.txt`:

```
# Disaster + - 日本語
ModDescription = 火災旋風・台風・火山などのリアルな災害現象と、危険度の可視化を追加します。
GroupFireWhirl = 火災旋風
GroupGeneral = 全般
FireWhirlEnabled = 火災旋風を有効にする
DetectRadius = 発生判定の半径 (m)
DetectCount = 発生に必要な延焼棟数
MaxLifetime = 最大持続時間 (ゲーム内分)
SpreadStrength = 延焼拡大の強さ (0 で無効)
MinSeparation = 多重発生の最小離隔距離 (m)
IntensityUnlock = 災害の強度上限を 25.5 まで解放する
IntensityUnlockHandledByOther = Natural Disasters Renewal が担当しています。Disaster + 側で制御したい場合のみ有効にしてください。
EarthquakeDamageOwner = 地震の被害計算の担当
EarthquakeOwnerOther = Natural Disasters Renewal
EarthquakeOwnerSelf = Disaster +
FireWhirlName = 火災旋風
FireWhirlTooltip = その場に留まる炎の渦を発生させます
NdrDetected = Natural Disasters Renewal を検出しました。バニラ由来の破壊はその竜巻設定に従いますが、延焼拡大は影響を受けません。
```

- [ ] **Step 5: ビルドしてゲームで確認する（手動）**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

1. ゲームを起動し、言語を日本語にして Options を開く → 項目名が日本語になること
2. **ゲームを再起動せずに**言語を英語に戻す → 項目名が英語に戻ること（`OnLocaleChanged` 経由の再実行）
3. もう一度日本語に戻す → 日本語に戻ること（既定値の復元が効いている）
4. `output_log.txt` に `locale load failed` が無いこと

- [ ] **Step 6: コミット**

```bash
git add src/DisasterPlus Locales && git commit -m "feat: 日英ローカライズ（言語切替に追従）"
```

---

## Task 8: 競合MOD 検出と強度上限の解放

`PluginManager` は起動時点で既に埋まっているので、`OnSettingsUI`（メインメニューで 1 回だけ実行）
から安全に参照できる。**レベルロード後にしか分からない情報は使わない。**

強度解放は **Harmony 不要**。IL 実測の結果、クランプはコードに存在せず
`DisastersOptionPanel.m_slider.maxValue`（UI プレハブ値）だった。

**Files:**
- Create: `src/DisasterPlus/Game/Compat/ModCompat.cs`
- Create: `src/DisasterPlus/Game/UI/IntensityUnlock.cs`
- Modify: `src/DisasterPlus/Game/Mod.cs`（競合検知時のみ地震担当の設定を出す）

**Interfaces:**
- Consumes: `Log`（Task 6）、`Strings`（Task 7）、`ModSettings`（Task 6）
- Produces:
  - `DisasterPlus.Game.ModCompat` — `static bool NdrPresent { get; }`（初回参照時に 1 回だけ判定してキャッシュ）
  - `DisasterPlus.Game.IntensityUnlock` — `static void Apply()`（レベルロード後に main スレッドから呼ぶ）、`const float MaxIntensityByte = 255f`

- [ ] **Step 1: ModCompat を作る**

`src/DisasterPlus/Game/Compat/ModCompat.cs`:

```csharp
using ColossalFramework.Plugins;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 競合MOD の検出。起動時に 1 回だけ判定してキャッシュする。
    ///
    /// PluginManager はメインメニューが立ち上がる時点で既に埋まっているので、
    /// OnSettingsUI から参照してよい（SteamHelper.IsDLCOwned と同じ扱い）。
    /// </summary>
    public static class ModCompat
    {
        /// <summary>Natural Disasters Renewal の Workshop ID。</summary>
        private const ulong NdrWorkshopId = 2957578256UL;

        /// <summary>同 MOD のアセンブリ名。ローカル配置・開発コピーには Workshop ID が無いので併用する。</summary>
        private const string NdrAssemblyName = "NaturalDisastersRenewal";

        private static bool _evaluated;
        private static bool _ndrPresent;

        public static bool NdrPresent
        {
            get
            {
                if (!_evaluated) Evaluate();
                return _ndrPresent;
            }
        }

        private static void Evaluate()
        {
            _evaluated = true;
            _ndrPresent = false;

            try
            {
                foreach (var p in PluginManager.instance.GetPluginsInfo())
                {
                    if (p == null || !p.isEnabled) continue;

                    if (p.publishedFileID.AsUInt64 == NdrWorkshopId) { _ndrPresent = true; break; }

                    bool matched = false;
                    try
                    {
                        foreach (var asm in p.GetAssemblies())
                        {
                            if (asm != null && asm.GetName().Name == NdrAssemblyName) { matched = true; break; }
                        }
                    }
                    catch { /* 壊れた MOD のアセンブリ列挙で落ちない */ }

                    if (matched) { _ndrPresent = true; break; }
                }
            }
            catch (System.Exception e)
            {
                // 判定に失敗したら「居ない」に倒す。
                // 偽陽性で機能を隠すより、偽陰性で余分な設定が出る方が害が小さい。
                Log.Error("plugin scan failed; assuming NDR absent", e);
                _ndrPresent = false;
            }

            Log.Info("Natural Disasters Renewal detected: " + _ndrPresent);
        }
    }
}
```

- [ ] **Step 2: IntensityUnlock を作る**

`src/DisasterPlus/Game/UI/IntensityUnlock.cs`:

```csharp
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// バニラの災害パネルの強度スライダー上限を解放する。
    ///
    /// IL 実測（設計書 付録 A-2）:
    ///   DisastersOptionPanel.OnSliderValueChanged(c, value):
    ///       m_label.text = (value / 10).ToString("F1")
    ///       m_disasterTool.m_intensity = (int)value
    /// つまりスライダーの生値がそのまま byte 強度で、表示だけが /10。
    /// set_maxValue の呼び出しはアセンブリ内に存在せず、上限は UI プレハブ側にある。
    /// よってパッチ対象が無く、実行時に maxValue を書けばよい。
    /// </summary>
    public static class IntensityUnlock
    {
        /// <summary>DisasterData.m_intensity は Byte。255 が真の上限で、表示は 25.5 になる。</summary>
        public const float MaxIntensityByte = 255f;

        /// <summary>再試行の間隔（main スレッド更新の回数）。FindObjectOfType は毎フレーム回すには重い。</summary>
        private const int RetryIntervalFrames = 120;

        /// <summary>再試行の上限。パネルが現れない環境で永久に探し続けない。</summary>
        private const int MaxAttempts = 100;

        private static bool _applied;
        private static bool _gaveUp;
        private static int _framesSinceTry;
        private static int _attempts;

        public static void Reset()
        {
            _applied = false;
            _gaveUp = false;
            _framesSinceTry = 0;
            _attempts = 0;
        }

        /// <summary>
        /// main スレッドから毎フレーム呼ばれる。実際の試行は RetryIntervalFrames ごと。
        ///
        /// 災害パネルはレベルロード時点ではまだ構築されていないことがある。
        /// ロード時 1 回きりの試行にすると、その都市では二度と上限が上がらない。
        /// </summary>
        public static void Tick()
        {
            if (_applied || _gaveUp) return;
            if (_framesSinceTry++ < RetryIntervalFrames) return;
            _framesSinceTry = 0;
            Apply();
        }

        /// <summary>main スレッドから呼ぶ。</summary>
        public static void Apply()
        {
            if (_applied || _gaveUp) return;

            ModSettings.Ensure();
            if (!ModSettings.IntensityUnlock.value) { _gaveUp = true; return; }

            if (++_attempts > MaxAttempts)
            {
                _gaveUp = true;
                Log.Warn("gave up looking for the disaster intensity slider after "
                         + MaxAttempts + " attempts; cap not raised");
                return;
            }

            try
            {
                var panel = Object.FindObjectOfType<DisastersOptionPanel>();
                if (panel == null)
                {
                    // まだ構築されていない。Tick が後で再試行する。
                    Log.Diag("intensityUnlock", "DisastersOptionPanel not found yet; will retry");
                    return;
                }

                var slider = panel.Find<UISlider>("Slider");
                if (slider == null)
                {
                    Log.Warn("intensity slider not found; cap not raised");
                    return;
                }

                if (slider.maxValue >= MaxIntensityByte)
                {
                    _applied = true;
                    return;   // 他 MOD が既に上げている
                }

                Log.Info("raising intensity slider cap " + slider.maxValue + " -> " + MaxIntensityByte);
                slider.maxValue = MaxIntensityByte;
                _applied = true;
            }
            catch (System.Exception e)
            {
                Log.Error("intensity unlock failed", e);
            }
        }
    }
}
```

- [ ] **Step 3: 競合検知時のみ現れる設定を Mod.cs に足す**

`OnSettingsUI` の `General` グループの末尾に追加する。

```csharp
    // 競合MOD が居るときだけ出す。居ないときはこの設定に意味が無く、
    // 値は保存したまま既定動作（Disaster + が担当）に戻る。
    if (ModCompat.NdrPresent)
    {
        var compat = helper.AddGroup(Strings.NdrDetected);

        // ラベル配列は static readonly にしてはいけない。型初期化時の言語で凍結する。
        // 毎回組み直すことで言語切替に追従する。
        string[] owners = { Strings.EarthquakeOwnerOther, Strings.EarthquakeOwnerSelf };

        int current = ModSettings.EarthquakeDamageOwner.value;
        if (current < 0 || current >= owners.Length) current = ModSettings.EarthquakeOwnerOther;

        compat.AddDropdown(Strings.EarthquakeDamageOwner, owners, current,
            v => ModSettings.EarthquakeDamageOwner.value = v);
    }
```

- [ ] **Step 4: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: 警告 0 でビルド成功

- [ ] **Step 4b: DLC ゲートを足す**

火災旋風はバニラの竜巻災害を流用するので **Natural Disasters DLC が要る**。
⑤火山（今後）は DLC 不要なので、機能ごとに判定する。

`Game/Compat/ModCompat.cs` に追加:

```csharp
        /// <summary>
        /// Natural Disasters DLC を持っているか。
        ///
        /// SteamHelper.IsDLCOwned は起動時から使えるので、OnSettingsUI
        /// （メインメニューで 1 回だけ実行）から参照してよい。
        /// レベルロード後にしか分からない情報でオプションを組み立ててはいけない。
        /// </summary>
        public static bool NaturalDisastersOwned
        {
            get
            {
                try { return SteamHelper.IsDLCOwned(SteamHelper.DLC.NaturalDisastersDLC); }
                catch (System.Exception e)
                {
                    // 判定できないときは「持っている」に倒す。
                    // 機能を永久に隠す偽陰性より、実行時に諦める偽陽性の方が害が小さい
                    // （DisasterInfo が見つからなければ Task 10 が警告を出して黙って止まる）。
                    Log.Error("DLC check failed; assuming owned", e);
                    return true;
                }
            }
        }
```

`Mod.OnSettingsUI` の火災旋風グループをこれで囲む:

```csharp
    if (ModCompat.NaturalDisastersOwned)
    {
        var fw = helper.AddGroup(Strings.GroupFireWhirl);
        // ... 既存のスライダー群 ...
    }
    else
    {
        helper.AddGroup(Strings.GroupFireWhirl).AddSpace(4);
        // グループ名の下に理由を出す。設定が「消えた」ように見えないようにする。
        helper.AddGroup(Strings.FireWhirlNeedsDlc);
    }
```

`Strings` に追加し、`ja.txt` にも足す:

```csharp
        public static string FireWhirlNeedsDlc =
            "Fire whirls require the Natural Disasters DLC.";
```

```
FireWhirlNeedsDlc = 火災旋風には Natural Disasters DLC が必要です。
```

`FireWhirlFeature.OnSimulationTick` の先頭でも早期 return する:

```csharp
            if (!ModCompat.NaturalDisastersOwned) return;
```

- [ ] **Step 5: ゲームで確認する（手動・2 パターン）**

**パターン A — NDR 無しで起動:**
1. Options に「Earthquake damage is calculated by」の**ドロップダウンが出ないこと**
2. 都市を読み込み、災害パネルを開いて強度スライダーを右端まで動かす → 表示が **25.5** になること
3. `output_log.txt` に `Natural Disasters Renewal detected: False` と
   `raising intensity slider cap 100 -> 255` が出ていること

**パターン B — NDR を購読して起動:**
1. Options にドロップダウンが出て、既定が「Natural Disasters Renewal」であること
2. `output_log.txt` に `Natural Disasters Renewal detected: True` が出ていること

- [ ] **Step 6: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 競合MOD検出と災害強度スライダーの 25.5 解放"
```

---

## Task 9: 機能の配線と共有状態

以降の全機能が乗る土台。**`FireWhirlRegistry` がこの MOD で唯一の共有可変状態**であり、
sim スレッドと main スレッドの両方から触るので単一の `lock` で守る
（net35 に `System.Collections.Concurrent` は無い）。

**Files:**
- Create: `src/DisasterPlus/Game/IDisasterFeature.cs`
- Create: `src/DisasterPlus/Game/Common/TerrainHeightSampler.cs`
- Create: `src/DisasterPlus/Game/Common/FeatureHost.cs`
- Create: `src/DisasterPlus/Game/Common/DisasterPlusThreading.cs`
- Create: `src/DisasterPlus/Game/Common/DisasterPlusLoading.cs`
- Create: `src/DisasterPlus/Game/FireWhirl/FireWhirlRegistry.cs`

**Interfaces:**
- Consumes: `Vec2`、`Vec3`、`IHeightSampler`、`LifetimeClock`、`FireWhirlLifecycle`（Core）、`Log`、`IntensityUnlock`
- Produces:
  - `DisasterPlus.Game.IDisasterFeature` — `string Name { get; }`、`void OnLevelLoaded()`、`void OnSimulationTick(uint frameIndex, float deltaMinutes)`、`void OnMainThreadUpdate()`、`void OnLevelUnloading()`
  - `DisasterPlus.Game.FeatureHost` — `static IList<IDisasterFeature> Features { get; }`、`static void LevelLoaded()`、`static void SimulationTick()`、`static void MainThreadUpdate()`、`static void LevelUnloading()`
  - `DisasterPlus.Game.TerrainHeightSampler` — `IHeightSampler` 実装、`static readonly TerrainHeightSampler Instance`
  - `DisasterPlus.Game.FireWhirlView` — **不変**の `struct`。`readonly ushort DisasterId`、`readonly ushort VehicleId`、`readonly Vec3 Center`、`readonly float Radius`、`readonly int BurningCount`、`readonly float ElapsedMinutes`、`readonly bool Ending`
  - `DisasterPlus.Game.FireWhirlRegistry` — `static void Add(ushort disasterId, ushort vehicleId, Vec3 center, float radius, int burningCount)`、`static void Remove(ushort disasterId, float cooldownMinutes)`、`static List<FireWhirlView> Snapshot()`、`static bool TryGetPinnedCenter(ushort vehicleId, out Vec3 center)`、`static List<Vec2> Centers()`、`static void AdvanceLife(ushort disasterId, float deltaMinutes, bool conditionMet)`、`static void AdvanceCooldowns(float deltaMinutes)`、`static void UpdateStrength(ushort disasterId, float radius, int burningCount)`、`static void MarkEnding(ushort disasterId)`、`static void SetVehicle(ushort disasterId, ushort vehicleId)`、`static FireWhirlVerdict EvaluateVerdict(ushort disasterId, FireWhirlConfig config)`、`static void RestoreFromSave(List<SavedFireWhirl> saved)`、`static int Count { get; }`、`static void Clear()`

**`ActiveFireWhirl` は `internal`（レジストリの内部表現）。外へは必ず `FireWhirlView` を返す。**
可変オブジェクトの参照を渡すと、リストを複製しても中身は共有されたままで、
snapshot-then-render のスレッド境界が成立しない。

- [ ] **Step 1: IDisasterFeature と FeatureHost を作る**

`src/DisasterPlus/Game/IDisasterFeature.cs`:

```csharp
namespace DisasterPlus.Game
{
    /// <summary>
    /// 1 つの災害機能。①天気予報・②地震・④台風・⑤火山も同じ形で足せるようにする。
    /// Mod.cs はこのリストを回すだけで、機能追加で既存コードを変更しなくて済む。
    /// </summary>
    public interface IDisasterFeature
    {
        string Name { get; }

        /// <summary>都市のロード完了時。main スレッド。</summary>
        void OnLevelLoaded();

        /// <summary>シミュレーション tick 後。sim スレッド。バッファの生成・変更はここから。</summary>
        /// <param name="deltaMinutes">前回からのゲーム内経過（分）。ポーズ中は 0。</param>
        void OnSimulationTick(uint frameIndex, float deltaMinutes);

        /// <summary>毎フレーム。main スレッド。Unity オブジェクトはここからのみ触る。</summary>
        void OnMainThreadUpdate();

        /// <summary>都市のアンロード時。全セッション状態と静的キャッシュをここで捨てる。</summary>
        void OnLevelUnloading();
    }
}
```

`src/DisasterPlus/Game/Common/FeatureHost.cs`:

```csharp
using System.Collections.Generic;
using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>機能の登録と、例外を 1 機能に閉じ込めたディスパッチ。</summary>
    public static class FeatureHost
    {
        private static readonly List<IDisasterFeature> _features = new List<IDisasterFeature>();
        private static uint _lastFrame;
        private static bool _hasLastFrame;

        public static IList<IDisasterFeature> Features { get { return _features; } }

        public static void Register(IDisasterFeature feature)
        {
            if (feature != null && !_features.Contains(feature)) _features.Add(feature);
        }

        public static void LevelLoaded()
        {
            _hasLastFrame = false;
            IntensityUnlock.Apply();

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnLevelLoaded(); }
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnLevelLoaded", e); }
            }
        }

        public static void SimulationTick()
        {
            uint frame = SimulationManager.instance.m_currentFrameIndex;

            // ゲーム内時間の経過（分）を出す。ポーズ中はフレームが進まないので 0 になる。
            // CS のシミュレーションは 1 日 = 262144 フレーム。1 分 = 262144 / 1440 フレーム。
            const float framesPerMinute = 262144f / 1440f;

            float deltaMinutes = 0f;
            if (_hasLastFrame && frame > _lastFrame)
            {
                deltaMinutes = (frame - _lastFrame) / framesPerMinute;
            }
            _lastFrame = frame;
            _hasLastFrame = true;

            if (deltaMinutes <= 0f) return;   // ポーズ中は何もしない。負にも絶対にしない。

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnSimulationTick(frame, deltaMinutes); }
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnSimulationTick", e); }
            }
        }

        public static void MainThreadUpdate()
        {
            // 災害パネルはロード直後にはまだ無いことがある。見つかるまで間隔をあけて再試行する。
            IntensityUnlock.Tick();

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnMainThreadUpdate(); }
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnMainThreadUpdate", e); }
            }
        }

        public static void LevelUnloading()
        {
            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnLevelUnloading(); }
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnLevelUnloading", e); }
            }

            _hasLastFrame = false;
            IntensityUnlock.Reset();
            Log.Reset();
        }
    }
}
```

**注意:** `framesPerMinute` の 262144 は「1 ゲーム内日あたりのシミュレーションフレーム数」の想定値。
Step 6 の実機確認で、旋風の寿命が設定どおり（既定 10 分）で尽きるかを必ず測ること。ズレていたら
この定数だけを直す（Core 側は分を受け取るだけなので影響しない）。

- [ ] **Step 2: TerrainHeightSampler を作る**

`src/DisasterPlus/Game/Common/TerrainHeightSampler.cs`:

```csharp
using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Core の IHeightSampler を CS の地形で実装する。
    /// SampleDetailHeight は読み取り専用で、sim / main どちらのスレッドからも安全。
    /// </summary>
    public class TerrainHeightSampler : IHeightSampler
    {
        public static readonly TerrainHeightSampler Instance = new TerrainHeightSampler();

        private TerrainHeightSampler() { }

        public float SampleHeight(float x, float z)
        {
            return TerrainManager.instance.SampleDetailHeight(new Vector3(x, 0f, z));
        }
    }
}
```

- [ ] **Step 3: FireWhirlRegistry を作る**

`src/DisasterPlus/Game/FireWhirl/FireWhirlRegistry.cs`:

```csharp
using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>生存中の火災旋風 1 基。レジストリの内部表現なので外に漏らさない。</summary>
    internal class ActiveFireWhirl
    {
        public ushort DisasterId;
        public ushort VehicleId;
        public Vec3 Center;
        public float Radius;
        public int BurningCount;
        public FireWhirlLifecycle Life;

        /// <summary>終了処理に入った。移動目標を現在地に置いてバニラの解体を待っている状態。</summary>
        public bool Ending;
    }

    /// <summary>
    /// レジストリの外へ渡す不変のコピー。
    ///
    /// 可変オブジェクトの参照を返すと、リストを複製しても中身は共有されたままで、
    /// main スレッドがロックの外で Center や Ending を読んでいる最中に
    /// sim スレッドが書き換えられてしまう。値でコピーして初めて境界が成立する。
    /// </summary>
    public struct FireWhirlView
    {
        public readonly ushort DisasterId;
        public readonly ushort VehicleId;
        public readonly Vec3 Center;
        public readonly float Radius;
        public readonly int BurningCount;
        public readonly float ElapsedMinutes;
        public readonly bool Ending;

        internal FireWhirlView(ActiveFireWhirl w)
        {
            DisasterId = w.DisasterId;
            VehicleId = w.VehicleId;
            Center = w.Center;
            Radius = w.Radius;
            BurningCount = w.BurningCount;
            ElapsedMinutes = w.Life.ElapsedMinutes;
            Ending = w.Ending;
        }
    }

    /// <summary>
    /// この MOD で唯一の共有可変状態。
    /// sim スレッド（判定・生成・被害）と main スレッド（描画）と
    /// Harmony パッチ（位置固定）の 3 者から触られる。
    ///
    /// net35 に System.Collections.Concurrent は無いので、素の lock で snapshot-then-render する。
    /// </summary>
    public static class FireWhirlRegistry
    {
        /// <summary>旋風が消えた地点。この間は同じ場所に再発生させない。</summary>
        private struct CoolingSpot
        {
            public Vec2 Center;
            public float RemainingMinutes;
        }

        private static readonly object _gate = new object();
        private static readonly List<ActiveFireWhirl> _active = new List<ActiveFireWhirl>();
        private static readonly List<CoolingSpot> _cooling = new List<CoolingSpot>();

        public static void Add(ushort disasterId, ushort vehicleId, Vec3 center, float radius, int burningCount)
        {
            lock (_gate)
            {
                _active.Add(new ActiveFireWhirl
                {
                    DisasterId = disasterId,
                    VehicleId = vehicleId,
                    Center = center,
                    Radius = radius,
                    BurningCount = burningCount,
                    Life = FireWhirlLifecycle.Start(),
                    Ending = false,
                });
            }
        }

        /// <param name="cooldownMinutes">
        /// この地点を抑制し続ける時間。仕様では最大持続時間と同値
        /// （呼び出し側が ModSettings.MaxLifetimeMinutes を渡す）。
        /// </param>
        public static void Remove(ushort disasterId, float cooldownMinutes)
        {
            lock (_gate)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (_active[i].DisasterId != disasterId) continue;

                    if (cooldownMinutes > 0f)
                    {
                        _cooling.Add(new CoolingSpot
                        {
                            Center = _active[i].Center.ToVec2(),
                            RemainingMinutes = cooldownMinutes,
                        });
                    }
                    _active.RemoveAt(i);
                }
            }
        }

        public static int Count
        {
            get { lock (_gate) { return _active.Count; } }
        }

        /// <summary>
        /// 値でコピーした不変ビューを返す。呼び出し側はロックの外で自由に走査してよい。
        /// ActiveFireWhirl の参照は決して外に出さない。
        /// </summary>
        public static List<FireWhirlView> Snapshot()
        {
            lock (_gate)
            {
                var list = new List<FireWhirlView>(_active.Count);
                for (int i = 0; i < _active.Count; i++) list.Add(new FireWhirlView(_active[i]));
                return list;
            }
        }

        /// <summary>寿命を進める。sim スレッドから呼ぶ。</summary>
        public static void AdvanceLife(ushort disasterId, float deltaMinutes, bool conditionMet)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Life = _active[i].Life.Advance(deltaMinutes, conditionMet);
                    return;
                }
            }
        }

        /// <summary>燃焼規模の変化を反映する。sim スレッドから呼ぶ。</summary>
        public static void UpdateStrength(ushort disasterId, float radius, int burningCount)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Radius = radius;
                    _active[i].BurningCount = burningCount;
                    return;
                }
            }
        }

        /// <summary>終了処理に入ったことを記録する。以降は位置固定をやめる。</summary>
        public static void MarkEnding(ushort disasterId)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Ending = true;
                    return;
                }
            }
        }

        /// <summary>
        /// 発生を抑制すべき地点の一覧。生存中の旋風の中心と、
        /// 最近まで旋風があった地点（クールダウン中）の両方を返す。
        ///
        /// クールダウンが無いと、消えた直後に同じ大火災が同じ場所で再発生させ続けて
        /// 実質的に永久の旋風になる（絶対上限の意味が無くなる）。
        /// </summary>
        public static List<Vec2> Centers()
        {
            lock (_gate)
            {
                var list = new List<Vec2>(_active.Count + _cooling.Count);
                for (int i = 0; i < _active.Count; i++) list.Add(_active[i].Center.ToVec2());
                for (int i = 0; i < _cooling.Count; i++) list.Add(_cooling[i].Center);
                return list;
            }
        }

        /// <summary>
        /// クールダウンを進め、明けた地点を捨てる。sim スレッドから毎 tick 呼ぶ。
        /// </summary>
        public static void AdvanceCooldowns(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return;
            lock (_gate)
            {
                for (int i = _cooling.Count - 1; i >= 0; i--)
                {
                    var c = _cooling[i];
                    c.RemainingMinutes -= deltaMinutes;
                    if (c.RemainingMinutes <= 0f) _cooling.RemoveAt(i);
                    else _cooling[i] = c;
                }
            }
        }

        /// <summary>
        /// Harmony パッチ（VortexAI.SimulationStep の Postfix）から毎ステップ呼ばれる。
        /// 自分が作った渦かどうかを車両 ID で判定し、固定先の座標を返す。
        /// </summary>
        public static bool TryGetPinnedCenter(ushort vehicleId, out Vec3 center)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].VehicleId != vehicleId) continue;
                    if (_active[i].Ending) break;   // 終了中は固定を解いてバニラに解体させる
                    center = _active[i].Center;
                    return true;
                }
            }
            center = new Vec3(0f, 0f, 0f);
            return false;
        }

        /// <summary>レベルアンロード時。都市をまたいで状態を持ち越さない。</summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _active.Clear();
                _cooling.Clear();
            }
        }
    }
}
```

- [ ] **Step 4: ThreadingExtension と LoadingExtension を作る**

`src/DisasterPlus/Game/Common/DisasterPlusThreading.cs`:

```csharp
using ICities;

namespace DisasterPlus.Game
{
    public class DisasterPlusThreading : ThreadingExtensionBase
    {
        /// <summary>
        /// sim スレッド。建物・車両・災害バッファの生成と変更はここからのみ行う。
        /// main スレッド（OnUpdate）からこれらを触ると、スタックトレースの無い
        /// IndexOutOfRangeException が後から出て、自分の try/catch にも掛からない。
        /// </summary>
        public override void OnAfterSimulationTick()
        {
            FeatureHost.SimulationTick();
        }

        /// <summary>main スレッド。Unity オブジェクト・描画・UI 専用。</summary>
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            FeatureHost.MainThreadUpdate();
        }
    }
}
```

`src/DisasterPlus/Game/Common/DisasterPlusLoading.cs`:

```csharp
using ICities;

namespace DisasterPlus.Game
{
    public class DisasterPlusLoading : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame &&
                mode != LoadMode.NewGameFromScenario) return;

            LocaleLoader.Apply();
            ModSettings.Ensure();
            FireWhirlRegistry.Clear();

            FeatureHost.LevelLoaded();
            Log.Info("level loaded; features=" + FeatureHost.Features.Count);
        }

        public override void OnLevelUnloading()
        {
            // アンロード中は sim スレッドが既に停止しているので、
            // ここから直接クリアしてよい（この場面に限り安全）。
            FeatureHost.LevelUnloading();
            FireWhirlRegistry.Clear();
            base.OnLevelUnloading();
        }
    }
}
```

- [ ] **Step 5: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: 警告 0 でビルド成功

- [ ] **Step 6: ゲームで確認する（手動）**

1. 都市を読み込む → `output_log.txt` に `level loaded; features=0` が出ること
2. メインメニューに戻り、**別の都市**を読み込む → もう一度 `level loaded; features=0` が出ること、
   例外が無いこと
3. **`framesPerMinute` の検証:** この時点では旋風がまだ無いので、Task 11 の実機確認で測る。
   Task 11 のチェックリストに含めてある

- [ ] **Step 7: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 機能配線・スレッド境界・共有状態レジストリ"
```

---

## Task 10: 燃焼建物の走査と竜巻の生成

**このタスクで初めて画面に結果が出る。** 密集火災を起こすと竜巻が湧く（まだ動き回る）。

**Files:**
- Create: `src/DisasterPlus/Game/FireWhirl/BurningBuildingScanner.cs`
- Create: `src/DisasterPlus/Game/FireWhirl/FireWhirlSpawner.cs`
- Create: `src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs`
- Modify: `src/DisasterPlus/Game/Common/DisasterPlusLoading.cs`（`FeatureHost.Register` を呼ぶ）

**Interfaces:**
- Consumes: `BurningBuilding`、`FireWhirlDetector`、`FireWhirlStrength`、`FireWhirlConfig`（Core）、`FireWhirlRegistry`、`IDisasterFeature`、`ModSettings`、`Log`
- Produces:
  - `DisasterPlus.Game.BurningBuildingScanner` — `void ScanSlice()`（sim スレッド）、`IList<BurningBuilding> Current { get; }`、`void Reset()`
  - `DisasterPlus.Game.FireWhirlSpawner` — `static bool TrySpawn(Vec3 center, byte intensity, out ushort disasterId)`（sim スレッド）、`static DisasterInfo FindTornadoInfo()`、`static void Reset()`。**渦車両は `ActivateDisaster` が作るので生成直後には存在しない。車両 ID は Task 11 の `AttachVehicles` が後から埋める**
  - `DisasterPlus.Game.FireWhirlFeature` — `IDisasterFeature` 実装

- [ ] **Step 1: 走査を作る**

`src/DisasterPlus/Game/FireWhirl/BurningBuildingScanner.cs`:

```csharp
using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 燃焼中の建物を集める。sim スレッドから読み取り専用で走査する。
    ///
    /// Building.Flags.Fire は存在しない（IL 確認済み）。燃焼中は m_fireIntensity &gt; 0。
    ///
    /// 建物バッファは 49152 スロットの固定長でほとんど空。毎 tick 全走査すると無駄なので、
    /// 数 tick かけて一周する。1 周が完了したときだけ結果を差し替える
    /// （走査途中の中途半端なリストで発生判定をしないため）。
    /// </summary>
    public class BurningBuildingScanner
    {
        /// <summary>1 tick あたりの走査スロット数。49152 を 8 tick で一周する。</summary>
        private const int SliceSize = 6144;

        private int _cursor;
        private List<BurningBuilding> _building = new List<BurningBuilding>();
        private List<BurningBuilding> _current = new List<BurningBuilding>();

        /// <summary>直近に完了した 1 周の結果。</summary>
        public IList<BurningBuilding> Current { get { return _current; } }

        public void Reset()
        {
            _cursor = 0;
            _building = new List<BurningBuilding>();
            _current = new List<BurningBuilding>();
        }

        /// <summary>1 スライスぶん進める。sim スレッドから呼ぶこと。</summary>
        public void ScanSlice()
        {
            var buffer = BuildingManager.instance.m_buildings.m_buffer;
            int len = buffer.Length;

            int end = _cursor + SliceSize;
            if (end > len) end = len;

            for (int i = _cursor; i < end; i++)
            {
                if ((buffer[i].m_flags & Building.Flags.Created) == Building.Flags.None) continue;
                if (buffer[i].m_fireIntensity == 0) continue;

                var p = buffer[i].m_position;
                _building.Add(new BurningBuilding((ushort)i, new Vec2(p.x, p.z)));
            }

            _cursor = end;

            if (_cursor >= len)
            {
                // 一周した。ここで初めて結果を差し替える。
                _cursor = 0;
                _current = _building;
                _building = new List<BurningBuilding>();
            }
        }
    }
}
```

- [ ] **Step 2: 生成を作る**

`src/DisasterPlus/Game/FireWhirl/FireWhirlSpawner.cs`:

```csharp
using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// バニラの竜巻災害を生成する。渦のメッシュ・音・破壊・災害通知・避難行動が
    /// そのまま手に入る。自前で作り直すのは割に合わない。
    ///
    /// sim スレッドからのみ呼ぶこと。CreateDisaster は災害バッファを触る。
    /// </summary>
    public static class FireWhirlSpawner
    {
        private static DisasterInfo _tornadoInfo;
        private static bool _searched;

        public static void Reset()
        {
            _tornadoInfo = null;
            _searched = false;
        }

        /// <summary>
        /// 竜巻の DisasterInfo を探す。
        /// 名前ではなく AI の型で判定する。ローカライズや MOD の改名に影響されない。
        /// TornadoAI は WeatherDisasterAI 派生であって MeteorAI(VehicleAI) の仲間ではない。
        /// </summary>
        public static DisasterInfo FindTornadoInfo()
        {
            // Unity のフェイク null に対応するため、参照だけでなく実体を毎回確認する。
            if (_searched && _tornadoInfo != null) return _tornadoInfo;

            _searched = true;
            _tornadoInfo = null;

            int count = PrefabCollection<DisasterInfo>.LoadedCount();
            for (uint i = 0; i < count; i++)
            {
                DisasterInfo info;
                try { info = PrefabCollection<DisasterInfo>.GetLoaded(i); }
                catch { continue; }   // 境界チェックをしない API なので 1 件ずつ守る

                if (info == null) continue;
                if (info.m_disasterAI is TornadoAI) { _tornadoInfo = info; break; }
            }

            if (_tornadoInfo == null)
            {
                Log.Warn("no TornadoAI DisasterInfo found; Natural Disasters DLC required for fire whirls");
            }
            return _tornadoInfo;
        }

        /// <summary>
        /// 指定地点に竜巻災害を作り、その渦車両の ID を返す。
        /// 渦車両は ActivateDisaster が作るので、生成直後にはまだ存在しない。
        /// vehicleId は 0 で返り、Task 11 の追従処理が後から埋める。
        /// </summary>
        public static bool TrySpawn(Vec3 center, byte intensity, out ushort disasterId)
        {
            disasterId = 0;

            var info = FindTornadoInfo();
            if (info == null) return false;

            ushort id;
            if (!DisasterManager.instance.CreateDisaster(out id, info))
            {
                Log.Diag("spawnFail", "CreateDisaster returned false (disaster buffer full?)");
                return false;
            }

            var buffer = DisasterManager.instance.m_disasters.m_buffer;
            buffer[id].m_targetPosition = new Vector3(center.X, center.Y, center.Z);
            buffer[id].m_intensity = intensity;
            buffer[id].m_angle = 0f;

            // 起動は AI に任せる。StartDisaster -> ActivateDisaster の順で渦車両が作られる。
            info.m_disasterAI.StartDisaster(id, ref buffer[id]);

            disasterId = id;
            Log.Info("fire whirl disaster created id=" + id + " intensity=" + intensity);
            return true;
        }
    }
}
```

- [ ] **Step 3: 機能本体を作る**

`src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs`:

```csharp
using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ③火災旋風。密集火災を検出して竜巻を生成し、その場に留めて延焼を撒く。
    /// </summary>
    public class FireWhirlFeature : IDisasterFeature
    {
        public string Name { get { return "FireWhirl"; } }

        private readonly BurningBuildingScanner _scanner = new BurningBuildingScanner();

        /// <summary>強度は byte。竜巻としては中程度の 60 から始める（表示 6.0）。</summary>
        private const byte SpawnIntensityBase = 60;

        public void OnLevelLoaded()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
        }

        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.FireWhirlEnabled.value) return;

            _scanner.ScanSlice();

            var config = ModSettings.ToFireWhirlConfig();
            var burning = _scanner.Current;

            // 消滅地点のクールダウンを進める。これが無いと、消えた直後に同じ大火災が
            // 同じ場所で再発生し続け、絶対上限の意味が無くなる。
            FireWhirlRegistry.AdvanceCooldowns(deltaMinutes);

            UpdateExisting(config, burning, deltaMinutes);
            TrySpawnNew(config, burning);

            Log.Diag("fireWhirl",
                "burning=" + burning.Count + " active=" + FireWhirlRegistry.Count);
        }

        private void UpdateExisting(FireWhirlConfig config, IList<BurningBuilding> burning, float deltaMinutes)
        {
            var views = FireWhirlRegistry.Snapshot();
            float r2 = config.DetectRadius * config.DetectRadius;

            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v.Ending) continue;

                // この旋風の周りにまだ発生条件ぶんの火災が残っているか。
                var centre2d = v.Center.ToVec2();
                int near = 0;
                for (int b = 0; b < burning.Count; b++)
                {
                    if (centre2d.DistanceSquaredTo(burning[b].Position) <= r2) near++;
                }
                bool conditionMet = near >= config.DetectCount;

                FireWhirlRegistry.UpdateStrength(v.DisasterId, FireWhirlStrength.RadiusFor(near), near);
                FireWhirlRegistry.AdvanceLife(v.DisasterId, deltaMinutes, conditionMet);
            }

            // 寿命判定は更新後の値で行う。
            // 判定そのものは Core の FireWhirlLifecycle.Evaluate が持っており、
            // Life をレジストリの外に出さないので評価もレジストリ経由で行う。
            var updated = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < updated.Count; i++)
            {
                if (updated[i].Ending) continue;
                if (FireWhirlRegistry.EvaluateVerdict(updated[i].DisasterId, config)
                    != FireWhirlVerdict.Dissipate) continue;

                FireWhirlPinner.BeginEnding(updated[i]);
            }
        }

        private void TrySpawnNew(FireWhirlConfig config, IList<BurningBuilding> burning)
        {
            var candidates = FireWhirlDetector.Detect(burning, config, FireWhirlRegistry.Centers());
            if (candidates.Count == 0) return;

            // 1 tick に 1 基まで。連鎖的に湧いて都市が一瞬で消えるのを防ぐ。
            var c = candidates[0];

            float y = TerrainManager.instance.SampleDetailHeight(new Vector3(c.Center.X, 0f, c.Center.Z));
            var center = new Vec3(c.Center.X, y, c.Center.Z);

            ushort disasterId;
            if (!FireWhirlSpawner.TrySpawn(center, SpawnIntensityBase, out disasterId)) return;

            FireWhirlRegistry.Add(disasterId, 0, center, FireWhirlStrength.RadiusFor(c.BurningCount), c.BurningCount);
        }

        public void OnMainThreadUpdate()
        {
            // 見た目は Task 13 で足す。
        }

        public void OnLevelUnloading()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlRegistry.Clear();
        }
    }
}
```

- [ ] **Step 4: レジストリに寿命評価を足す**

`FireWhirlRegistry` に追加する（`Life` はレジストリの外に出さないので、評価もここで行う）:

```csharp
        /// <summary>寿命判定。Life を外に漏らさないため、評価もレジストリ内で行う。</summary>
        public static FireWhirlVerdict EvaluateVerdict(ushort disasterId, FireWhirlConfig config)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    return _active[i].Life.Evaluate(config);
                }
            }
            return FireWhirlVerdict.Dissipate;   // 見つからない = 既に消えている
        }
```

- [ ] **Step 5: 機能を登録する**

`DisasterPlusLoading.OnLevelLoaded` の `FeatureHost.LevelLoaded()` の**前**に追加する:

```csharp
            if (FeatureHost.Features.Count == 0)
            {
                FeatureHost.Register(new FireWhirlFeature());
            }
```

- [ ] **Step 6: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: 警告 0。`FireWhirlPinner` はまだ無いのでコンパイルエラーになる。
**Task 11 の `FireWhirlPinner` を先に空実装で置いてからビルドすること**:

```csharp
namespace DisasterPlus.Game
{
    public static class FireWhirlPinner
    {
        public static void BeginEnding(FireWhirlView v)
        {
            // Task 11 で実装する。いまは記録だけして消す。
            FireWhirlRegistry.MarkEnding(v.DisasterId);
        }
    }
}
```

- [ ] **Step 7: ゲームで確認する（手動）**

1. Natural Disasters DLC を有効にした都市を読み込む
2. 密集した住宅地に**バニラの災害パネルから構造物火災を複数回**撃ち込み、12 棟以上を同時に燃やす
3. **竜巻が発生すること**（まだ動き回ってよい）
4. `output_log.txt` を確認する:
   - `DIAG fireWhirl: burning=<n> active=<m>` が 10 秒に 1 回出ていること
   - `fire whirl disaster created id=...` が出ていること
   - **`no TornadoAI DisasterInfo found` が出ていないこと**（出たら DLC 無効か prefab 探索の失敗）
   - **スタックトレースの無い `IndexOutOfRangeException` ポップアップが出ないこと**（出たらスレッド境界違反）
5. 郊外に散らばった 12 棟を燃やす → **発生しないこと**

- [ ] **Step 8: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 燃焼建物の分割走査と密集火災からの竜巻生成"
```

---

## Task 11: 位置の固定と終了

**火災旋風が「火災旋風」になるタスク。** Harmony はこの 1 箇所だけに閉じ込める。

**Files:**
- Create: `src/DisasterPlus/Game/FireWhirl/VortexPinPatch.cs`
- Create: `src/DisasterPlus/Game/FireWhirl/HarmonyBootstrap.cs`
- Rewrite: `src/DisasterPlus/Game/FireWhirl/FireWhirlPinner.cs`
- Modify: `src/DisasterPlus/DisasterPlus.csproj`（`CitiesHarmony.API` 参照を追加）
- Modify: `src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs`（渦車両 ID の追従）
- Modify: `README.md`（CitiesHarmony 依存を明記）

**Interfaces:**
- Consumes: `FireWhirlRegistry`、`FireWhirlView`、`Log`
- Produces:
  - `DisasterPlus.Game.HarmonyBootstrap` — `static void Install()`、`static void Uninstall()`、`static bool Installed { get; }`
  - `DisasterPlus.Game.VortexPinPatch` — Harmony Postfix（`static void Postfix(ushort vehicleID, ref Vehicle.Frame frameData)`）
  - `DisasterPlus.Game.FireWhirlPinner` — `static void AttachVehicles()`（sim）、`static void BeginEnding(FireWhirlView v)`（sim）、`static void CollectFinished()`（sim）

- [ ] **Step 1: CitiesHarmony を参照に追加する**

`DisasterPlus.csproj` の `ItemGroup` に追加する。DLL は Workshop の CitiesHarmony
（ID `2040656402`）が配置するものを使う。

```xml
    <Reference Include="CitiesHarmony.API">
      <HintPath>$(LOCALAPPDATA)\Colossal Order\Cities_Skylines\Addons\Mods\CitiesHarmony\CitiesHarmony.API.dll</HintPath>
      <Private>True</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(LOCALAPPDATA)\Colossal Order\Cities_Skylines\Addons\Mods\CitiesHarmony\0Harmony.dll</HintPath>
      <Private>False</Private>
    </Reference>
```

`build.ps1` に `CitiesHarmony.API.dll` のコピーを足す（`<Private>True</Private>` で
`bin\Release` に出力されるので、それを配置先にもコピーする）:

```powershell
$apiDll = "src\DisasterPlus\bin\Release\CitiesHarmony.API.dll"
if (Test-Path $apiDll) { Copy-Item $apiDll $modDir -Force; Write-Host "Deployed CitiesHarmony.API.dll" }
```

- [ ] **Step 2: Harmony の導入と撤去を作る**

`src/DisasterPlus/Game/FireWhirl/HarmonyBootstrap.cs`:

```csharp
using CitiesHarmony.API;
using HarmonyLib;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Harmony パッチはこの MOD で 1 箇所だけ（VortexAI.SimulationStep の Postfix）。
    /// 他の機能は読み取りと自前 AI で済ませ、パッチ面を意図的に最小に保つ。
    /// </summary>
    public static class HarmonyBootstrap
    {
        private const string HarmonyId = "jp.disasterplus.mod";

        private static Harmony _harmony;

        public static bool Installed { get { return _harmony != null; } }

        public static void Install()
        {
            if (_harmony != null) return;

            if (!HarmonyHelper.IsHarmonyInstalled)
            {
                Log.Warn("CitiesHarmony not installed; fire whirls will drift instead of staying put");
                return;
            }

            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(HarmonyBootstrap).Assembly);
                Log.Info("Harmony patches installed");
            }
            catch (System.Exception e)
            {
                _harmony = null;
                Log.Error("Harmony patch failed", e);
            }
        }

        public static void Uninstall()
        {
            if (_harmony == null) return;
            try { _harmony.UnpatchAll(HarmonyId); }
            catch (System.Exception e) { Log.Error("Harmony unpatch failed", e); }
            _harmony = null;
        }
    }
}
```

- [ ] **Step 3: 位置固定パッチを作る**

`src/DisasterPlus/Game/FireWhirl/VortexPinPatch.cs`:

```csharp
using DisasterPlus.Core.Common;
using HarmonyLib;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 渦車両をその場に留める。
    ///
    /// IL 実測（設計書 付録 A-1）で分かった VortexAI.SimulationStep(6 引数) の順序:
    ///   1. Frame.m_position を書く
    ///   2. m_targetPos0 までの距離を測る
    ///   3. ArriveAtDestination が true なら DeactivateNow + Unspawn
    ///   4. Frame.m_velocity を再計算
    ///   5. Frame.m_position = m_position + m_velocity * dt   ← 実際の移動
    ///   6. AddWind / DestroyStuff / BurnGround / UpgradeBuildings
    ///
    /// Postfix で m_position を発生地点に戻せば、5 の積算が毎ステップ帳消しになる。
    ///
    /// m_velocity は書き換えない。4 で毎ステップ再計算されるので無意味であり、
    /// 6 の AddWind が向きに使っているのでゼロにすると風の演出が死ぬ。
    /// </summary>
    [HarmonyPatch(typeof(VortexAI), "SimulationStep",
        new[] { typeof(ushort), typeof(Vehicle), typeof(Vehicle.Frame),
                typeof(ushort), typeof(Vehicle), typeof(int) },
        new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref,
                ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class VortexPinPatch
    {
        public static void Postfix(ushort vehicleID, ref Vehicle.Frame frameData)
        {
            // 自分が作った渦だけを固定する。バニラの竜巻には一切触らない。
            Vec3 center;
            if (!FireWhirlRegistry.TryGetPinnedCenter(vehicleID, out center)) return;

            frameData.m_position = new Vector3(center.X, frameData.m_position.y, center.Z);
        }
    }
}
```

**高さ（`y`）は書き戻さない。** バニラが `SampleRawHeightSmoothWithWater` で地形に合わせているので、
水平位置だけ固定すれば地面から浮いたり埋まったりしない。

- [ ] **Step 4: FireWhirlPinner を実装する**

`src/DisasterPlus/Game/FireWhirl/FireWhirlPinner.cs`（Task 10 の空実装を置き換える）:

```csharp
using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 渦車両の紐づけと、寿命が尽きたときの終了。すべて sim スレッドから呼ぶこと。
    /// </summary>
    public static class FireWhirlPinner
    {
        /// <summary>
        /// まだ車両 ID が分かっていない旋風に、渦車両を紐づける。
        /// 車両は ActivateDisaster が作るので、CreateDisaster の直後には存在しない。
        /// </summary>
        public static void AttachVehicles()
        {
            var views = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].VehicleId != 0) continue;

                ushort found = FindVortexVehicle(views[i].DisasterId);
                if (found == 0) continue;

                FireWhirlRegistry.SetVehicle(views[i].DisasterId, found);
                Log.Info("fire whirl " + views[i].DisasterId + " attached to vortex vehicle " + found);
            }
        }

        /// <summary>
        /// 災害グループに属する車両のうち、渦の VehicleAI を持つものを探す。
        /// TornadoAI.GetPosition が使っているのと同じ経路
        /// （InstanceID.Disaster → InstanceManager.GetAllGroupInstances）を辿る。
        /// </summary>
        private static ushort FindVortexVehicle(ushort disasterId)
        {
            var id = InstanceID.Empty;
            id.Disaster = disasterId;

            _tempInstances.Clear();
            InstanceManager.GetAllGroupInstances(id, _tempInstances);

            var buffer = VehicleManager.instance.m_vehicles.m_buffer;
            for (int i = 0; i < _tempInstances.m_size; i++)
            {
                ushort v = _tempInstances.m_buffer[i].Vehicle;
                if (v == 0) continue;

                var info = buffer[v].Info;
                if (info != null && info.m_vehicleAI is VortexAI) return v;
            }
            return 0;
        }
```

型はリフレクションで確認済み（`DisasterAI.m_tempList` と同じ）。クラス冒頭に置く:

```csharp
        // sim スレッド専用の使い回しバッファ。毎 tick 確保しない。
        // 型は DisasterAI.m_tempList と同じ FastList<InstanceID>（確認済み）。
        private static readonly FastList<InstanceID> _tempInstances = new FastList<InstanceID>();
```

シグネチャも確認済み: `static void InstanceManager.GetAllGroupInstances(InstanceID id, FastList<InstanceID> list)`。
`InstanceID.Vehicle` プロパティも存在する。`FastList<T>` は `ColossalFramework` 名前空間。

終了処理:

```csharp
        /// <summary>
        /// 寿命が尽きた旋風を、バニラの解体経路に乗せる。
        ///
        /// 車両を自前で解放しない。移動目標を現在位置に置けば、数ステップ後に
        /// VortexAI.ArriveAtDestination が true を返し（m_waitCounter > 4）、
        /// バニラが DisasterAI.DeactivateNow と Vehicle.Unspawn を正しく実行する。
        /// </summary>
        public static void BeginEnding(FireWhirlView v)
        {
            FireWhirlRegistry.MarkEnding(v.DisasterId);

            if (v.VehicleId == 0)
            {
                // 車両が付く前に寿命が尽きた。災害だけ落として掃除する。
                FireWhirlRegistry.Remove(v.DisasterId, ModSettings.MaxLifetimeMinutes.value);
                return;
            }

            var buffer = VehicleManager.instance.m_vehicles.m_buffer;
            Vector3 here = buffer[v.VehicleId].GetLastFrameData().m_position;

            // w には元の値を残す（速度・半径の意味を持つため、0 にすると挙動が変わる）。
            Vector4 t0 = buffer[v.VehicleId].m_targetPos0;
            buffer[v.VehicleId].SetTargetPos(0, new Vector4(here.x, here.y, here.z, t0.w));

            Log.Info("fire whirl " + v.DisasterId + " ending; target moved to current position");
        }

        /// <summary>バニラに解体された旋風をレジストリから外す。</summary>
        public static void CollectFinished()
        {
            var views = FireWhirlRegistry.Snapshot();
            var disasters = DisasterManager.instance.m_disasters.m_buffer;
            float cooldown = ModSettings.MaxLifetimeMinutes.value;

            for (int i = 0; i < views.Count; i++)
            {
                ushort d = views[i].DisasterId;
                if (d >= disasters.Length) { FireWhirlRegistry.Remove(d, cooldown); continue; }

                if ((disasters[d].m_flags & DisasterData.Flags.Created) == DisasterData.Flags.None)
                {
                    FireWhirlRegistry.Remove(d, cooldown);
                    Log.Info("fire whirl " + d + " collected");
                }
            }
        }
```

- [ ] **Step 5: レジストリに車両 ID の設定を足す**

```csharp
        public static void SetVehicle(ushort disasterId, ushort vehicleId)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].VehicleId = vehicleId;
                    return;
                }
            }
        }
```

- [ ] **Step 6: 機能に配線する**

`FireWhirlFeature.OnSimulationTick` の先頭（走査の前）に足す:

```csharp
            FireWhirlPinner.AttachVehicles();
            FireWhirlPinner.CollectFinished();
```

`FireWhirlFeature.OnLevelLoaded` に `HarmonyBootstrap.Install();`、
`OnLevelUnloading` に `HarmonyBootstrap.Uninstall();` を足す。

- [ ] **Step 7: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

- [ ] **Step 8: ゲームで確認する（手動）— このタスクの本番**

1. CitiesHarmony を購読した状態で都市を読み込む
2. `output_log.txt` に `Harmony patches installed` が出ること
3. 密集火災を起こして火災旋風を発生させる
4. **旋風がその場に留まること**（バニラの竜巻のように移動しないこと）
5. **`framesPerMinute` の検証（Task 9 から持ち越し）:** 設定の最大持続時間を **2 分**にして
   発生させ、ゲーム内時計で約 2 分後に消えることを確認する。大きくズレていたら
   `FeatureHost.framesPerMinute` の定数を測定値で直す
6. 寿命が尽きたら**渦が消え、災害通知も閉じること**。`fire whirl <id> ending` と
   `fire whirl <id> collected` がログに出ること
7. **バニラの竜巻を災害パネルから撃ち、それは今までどおり移動すること**（パッチが自分の渦だけに
   効いていること）
8. **2 つ目の都市を読み込んで 3〜6 を再確認する**

- [ ] **Step 9: コミット**

```bash
git add src/DisasterPlus build.ps1 README.md && git commit -m "feat: 渦の位置固定（Harmony Postfix）とバニラ解体経路での終了"
```

---

## Task 12: 延焼拡大

**火災旋風という現象の核心。** そして**競合MOD の影響を受けない唯一の被害層**でもある。
`DisasterHelpers` を一切呼ばないので、NDR が `EnableDestruction` を切っていてもここは必ず動く。

**Files:**
- Create: `src/DisasterPlus/Game/FireWhirl/FireWhirlDamage.cs`
- Modify: `src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs`（毎 tick 呼ぶ）

**Interfaces:**
- Consumes: `IgnitionSpread`、`IgnitionCandidate`、`Vec2`（Core）、`FireWhirlRegistry`、`FireWhirlView`、`ModSettings`
- Produces:
  - `DisasterPlus.Game.FireWhirlDamage` — `static void Apply(uint frameIndex, int spreadStrength)`（sim スレッド）、`static void Reset()`

- [ ] **Step 1: 実装する**

`src/DisasterPlus/Game/FireWhirl/FireWhirlDamage.cs`:

```csharp
using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 旋風の周囲に火の粉を撒く。sim スレッドからのみ呼ぶこと（建物バッファを書く）。
    ///
    /// 意図的に DisasterHelpers を経由しない。
    /// 競合MOD（Natural Disasters Renewal）は DisasterHelpers.DestroyBuildings を
    /// Prefix で完全置換しており、竜巻と判定した呼び出しの破壊確率を半減させ、
    /// 設定次第では破壊を丸ごと無効化する。しかも竜巻判定に使われる burnRadius は
    /// VortexAI 内でリテラル 0 に固定されていて外から変えられない（設計書 3.3(b)）。
    ///
    /// そこで「周囲に火を撒く」という定義的な挙動だけは自前の経路に置き、
    /// 他 MOD の設定に左右されないようにする。
    /// </summary>
    public static class FireWhirlDamage
    {
        /// <summary>延焼判定を行う間隔（sim tick）。毎 tick 判定すると重く、燃え広がりも速すぎる。</summary>
        private const int IntervalTicks = 16;

        /// <summary>着火時に入れる火勢。バニラの出火と同程度にする。</summary>
        private const byte IgnitionIntensity = 128;

        private static readonly List<IgnitionCandidate> _candidates = new List<IgnitionCandidate>();
        private static readonly List<ushort> _selected = new List<ushort>();

        public static void Reset()
        {
            _candidates.Clear();
            _selected.Clear();
        }

        public static void Apply(uint frameIndex, int spreadStrength)
        {
            if (spreadStrength <= 0) return;
            if (frameIndex % IntervalTicks != 0) return;

            var views = FireWhirlRegistry.Snapshot();
            if (views.Count == 0) return;

            var buildings = BuildingManager.instance.m_buildings.m_buffer;

            for (int w = 0; w < views.Count; w++)
            {
                var v = views[w];
                if (v.Ending) continue;

                CollectNearby(buildings, v);
                if (_candidates.Count == 0) continue;

                IgnitionSpread.Select(v.Center.ToVec2(), v.Radius, spreadStrength,
                                      _candidates, frameIndex, _selected);

                for (int i = 0; i < _selected.Count; i++)
                {
                    ushort id = _selected[i];
                    if (buildings[id].m_fireIntensity != 0) continue;   // 判定後に燃え出した分を弾く
                    buildings[id].m_fireIntensity = IgnitionIntensity;
                }

                if (_selected.Count > 0)
                {
                    Log.Diag("ignite", "fire whirl " + v.DisasterId + " ignited " + _selected.Count);
                }
            }
        }

        /// <summary>
        /// 旋風の半径内の建物を集める。
        /// BuildingManager の空間グリッドを使い、全 49152 スロットの走査を避ける。
        /// </summary>
        private static void CollectNearby(Building[] buildings, FireWhirlView v)
        {
            _candidates.Clear();

            var bm = BuildingManager.instance;
            float r = v.Radius;

            // 建物グリッドは 1 セル 64m、270x270。境界をはみ出さないようクランプする。
            int minX = Clamp((int)((v.Center.X - r) / 64f + 135f));
            int maxX = Clamp((int)((v.Center.X + r) / 64f + 135f));
            int minZ = Clamp((int)((v.Center.Z - r) / 64f + 135f));
            int maxZ = Clamp((int)((v.Center.Z + r) / 64f + 135f));

            var centre2d = v.Center.ToVec2();
            float r2 = r * r;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ushort id = bm.m_buildingGrid[z * 270 + x];
                    int guard = 0;

                    while (id != 0)
                    {
                        if ((buildings[id].m_flags & Building.Flags.Created) != Building.Flags.None)
                        {
                            var p = buildings[id].m_position;
                            var pos = new Vec2(p.x, p.z);
                            if (centre2d.DistanceSquaredTo(pos) <= r2)
                            {
                                _candidates.Add(new IgnitionCandidate(
                                    id, pos, buildings[id].m_fireIntensity != 0));
                            }
                        }

                        id = buildings[id].m_nextGridBuilding;

                        // 連結リストが壊れている保存データで無限ループしないための保険。
                        if (++guard > 32768) break;
                    }
                }
            }
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 269) return 269;
            return v;
        }
    }
}
```

**グリッド定数は IL で確認済み**（推測ではない）。`BuildingManager.AddToGrid`:

```
x / 64 + 135 → Mathf.Clamp(_, 0, 269)
z / 64 + 135 → Mathf.Clamp(_, 0, 269)
index = z * 270 + x
m_buildingGrid[index] を先頭とする m_nextGridBuilding の連結リスト
```

`BuildingManager.Awake` の `newarr` から `m_buildingGrid` は **72900 要素**（= 270 × 270）、
建物バッファは **49152 スロット**。上のコードの定数はこれと一致している。

**なお `AddToGrid` は `Monitor.TryEnter(m_buildingGrid, SYNCHRONIZE_TIMEOUT)` でグリッドを
ロックしてから書く。** ここは sim スレッドからの読み取りなのでロックは取らないが、
連結リストが書き換え途中の状態を読む可能性はある。`guard` カウンタはそのための保険でもある。

- [ ] **Step 2: 機能に配線する**

`FireWhirlFeature.OnSimulationTick` の末尾に足す:

```csharp
            FireWhirlDamage.Apply(frameIndex, config.SpreadStrength);
```

`OnLevelLoaded` と `OnLevelUnloading` に `FireWhirlDamage.Reset();` を足す。

- [ ] **Step 3: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

- [ ] **Step 4: ゲームで確認する（手動）**

1. 延焼拡大の強さを **0** にして火災旋風を発生させる → 周囲に新規の出火が**起きないこと**
2. 強さを **10** にして発生させる → 周囲の建物が次々に燃え出すこと
3. `output_log.txt` に `DIAG ignite: fire whirl <id> ignited <n>` が出ること
4. **自己強化ループの確認:** 強さ 10 でも、設定した最大持続時間で**必ず消えること**
   （延焼が増え続けても寿命の絶対上限が効いていること）
5. **NDR を入れた状態で、NDR 側の破壊を無効化して**同じことをする →
   **延焼拡大は変わらず動くこと**（これがこのタスクの設計意図）

- [ ] **Step 5: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 延焼拡大（DisasterHelpers 非経由の自前実装）"
```

---

## Task 13: 炎をまとった見た目

**Files:**
- Create: `src/DisasterPlus/Game/FireWhirl/FireWhirlFlameFx.cs`
- Modify: `src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs`（`OnMainThreadUpdate` から呼ぶ）

**Interfaces:**
- Consumes: `FireWhirlRegistry`、`FireWhirlView`
- Produces:
  - `DisasterPlus.Game.FireWhirlFlameFx` — `static void Sync()`（main スレッド）、`static void Clear()`

- [ ] **Step 1: 実装する**

`src/DisasterPlus/Game/FireWhirl/FireWhirlFlameFx.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 渦に炎をまとわせる。main スレッドからのみ呼ぶこと（Unity オブジェクトを作る）。
    ///
    /// CS のマテリアルは借りない。CS のシェーダはエンジンが供給する per-instance データを
    /// 要求するので、素の Renderer に載せると何も描画されないか真っ黒になる。
    /// シェーダは Shader.Find で自前に作る。
    ///
    /// DispatchEffect を使わないのは、magnitude が粒子密度でしかなく、大きさは
    /// EffectInfo.SpawnArea 半径でしか変えられないため。旋風は 40〜220m とスケールが
    /// 大きく変わるので、自前の ParticleSystem の方が素直になる。
    /// </summary>
    public static class FireWhirlFlameFx
    {
        private static readonly Dictionary<ushort, GameObject> _objects = new Dictionary<ushort, GameObject>();
        private static Material _flameMaterial;

        /// <summary>レジストリの内容に合わせてエフェクトを生成・更新・破棄する。</summary>
        public static void Sync()
        {
            var views = FireWhirlRegistry.Snapshot();

            var alive = new HashSet<ushort>();
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                alive.Add(v.DisasterId);

                GameObject go;
                if (!_objects.TryGetValue(v.DisasterId, out go) || go == null)
                {
                    // go == null は Unity のフェイク null（都市を跨いで破棄済み）にも当たる。
                    go = Create(v.DisasterId);
                    _objects[v.DisasterId] = go;
                }

                go.transform.position = new Vector3(v.Center.X, v.Center.Y, v.Center.Z);
                Configure(go, v.Radius);
            }

            // 消えた旋風のエフェクトを片付ける。
            var stale = new List<ushort>();
            foreach (var kv in _objects)
            {
                if (!alive.Contains(kv.Key)) stale.Add(kv.Key);
            }
            for (int i = 0; i < stale.Count; i++)
            {
                if (_objects[stale[i]] != null) Object.Destroy(_objects[stale[i]]);
                _objects.Remove(stale[i]);
            }
        }

        private static Material FlameMaterial()
        {
            if (_flameMaterial != null) return _flameMaterial;

            Shader s = Shader.Find("Particles/Additive")
                    ?? Shader.Find("Legacy Shaders/Particles/Additive")
                    ?? Shader.Find("Standard");

            _flameMaterial = new Material(s);
            _flameMaterial.color = new Color(1f, 0.45f, 0.1f, 1f);
            return _flameMaterial;
        }

        private static GameObject Create(ushort disasterId)
        {
            var go = new GameObject("DisasterPlus_FireWhirl_" + disasterId);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 2.2f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.75f, 0.2f, 1f), new Color(1f, 0.25f, 0.05f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // 渦を巻きながら上昇させる。velocityOverLifetime の orbital が渦の芯を作る。
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = FlameMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return go;
        }

        /// <summary>半径に合わせて形と量を変える。旋風は 40〜220m まで大きさが変わる。</summary>
        private static void Configure(GameObject go, float radius)
        {
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return;

            var main = ps.main;
            main.startSize = radius * 0.20f;
            main.startSpeed = radius * 0.25f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = radius * 0.35f;
            shape.angle = 12f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 40f + radius * 1.5f;

            var vel = ps.velocityOverLifetime;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(radius * 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(radius * 0.30f);
        }

        /// <summary>
        /// レベルアンロード時に必ず呼ぶ。
        /// 静的なコレクションに破棄済みの Unity オブジェクトを抱えたままにすると、
        /// 2 つ目の都市で無言のまま炎が出なくなる。
        /// </summary>
        public static void Clear()
        {
            foreach (var kv in _objects)
            {
                if (kv.Value != null) Object.Destroy(kv.Value);
            }
            _objects.Clear();
            _flameMaterial = null;
        }
    }
}
```

- [ ] **Step 2: 機能に配線する**

```csharp
        public void OnMainThreadUpdate()
        {
            FireWhirlFlameFx.Sync();
        }
```

`OnLevelUnloading` に `FireWhirlFlameFx.Clear();` を足す。

- [ ] **Step 3: ビルドする**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

- [ ] **Step 4: ゲームで確認する（手動）**

1. 火災旋風を発生させる → **渦に炎がまとわりついて見えること**（真っ黒でも透明でもないこと）
2. 小さい火災（12 棟）と大きい火災（60 棟以上）で**炎の大きさが変わること**
3. 旋風が消えたとき、**炎も消えること**（GameObject が残らないこと）
4. **2 つ目の都市を読み込んで 1 を再確認する** — ここで炎が出なければ静的キャッシュの
   フェイク null 問題。`Clear()` が呼ばれているかを確認する
5. `output_log.txt` に `NullReferenceException` が無いこと

- [ ] **Step 5: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 炎をまとった渦の見た目（自前 ParticleSystem）"
```

---

## Task 14: 手動発生（災害パネルのボタンとクリック配置）

テストに必須であり、プレイヤーの遊べる幅も広がる。**地形に Unity コライダーは無い**ので
`Physics.Raycast` は使わず、Task 5 の `RayGeometry` を使う。

**Files:**
- Create: `src/DisasterPlus/Game/UI/FireWhirlPlacementTool.cs`
- Create: `src/DisasterPlus/Game/UI/ToolRegistration.cs`
- Create: `src/DisasterPlus/Game/UI/FireWhirlPanelButton.cs`
- Modify: `src/DisasterPlus/Game/FireWhirl/FireWhirlFeature.cs`（`OnLevelLoaded` で登録）

**Interfaces:**
- Consumes: `RayGeometry`、`Vec3`（Core）、`TerrainHeightSampler`、`FireWhirlSpawner`、`FireWhirlRegistry`、`FireWhirlStrength`、`Strings`
- Produces:
  - `DisasterPlus.Game.FireWhirlPlacementTool : ToolBase` — `static void Activate()`、`static void Deactivate()`
  - `DisasterPlus.Game.ToolRegistration` — `static T Register<T>() where T : ToolBase`
  - `DisasterPlus.Game.FireWhirlPanelButton` — `static void Install()`、`static void Remove()`

- [ ] **Step 1: ツール登録のリフレクションを書く**

`src/DisasterPlus/Game/UI/ToolRegistration.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// カスタム ToolBase を ToolController に認識させる。
    ///
    /// ToolController.m_tools は Awake で GetComponents&lt;ToolBase&gt;() から一度だけ構築され、
    /// ToolsModifierControl.SetTool&lt;T&gt; は静的辞書を TryGetValue するだけ。
    /// どちらも起動後に足したツールを知らないので、リフレクションで両方に差し込む。
    /// 毎レベルロードで行うこと。
    /// </summary>
    public static class ToolRegistration
    {
        public static T Register<T>() where T : ToolBase
        {
            var controller = ToolsModifierControl.toolController;
            if (controller == null)
            {
                Log.Warn("toolController not available; " + typeof(T).Name + " not registered");
                return null;
            }

            var tool = controller.GetComponent<T>() ?? controller.gameObject.AddComponent<T>();

            try
            {
                // 1. private な ToolController.m_tools 配列に足す
                var field = typeof(ToolController).GetField("m_tools",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var tools = (ToolBase[])field.GetValue(controller);

                bool present = false;
                for (int i = 0; i < tools.Length; i++)
                {
                    if (tools[i] == tool) { present = true; break; }
                }

                if (!present)
                {
                    var grown = new ToolBase[tools.Length + 1];
                    Array.Copy(tools, grown, tools.Length);
                    grown[tools.Length] = tool;
                    field.SetValue(controller, grown);
                }

                // 2. 静的な ToolsModifierControl.m_Tools 辞書にも足す
                var dictField = typeof(ToolsModifierControl).GetField("m_Tools",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (dictField != null)
                {
                    var dict = dictField.GetValue(null) as Dictionary<Type, ToolBase>;
                    if (dict != null) dict[typeof(T)] = tool;
                }

                Log.Info(typeof(T).Name + " registered");
            }
            catch (Exception e)
            {
                Log.Error("tool registration failed for " + typeof(T).Name, e);
            }

            return tool;
        }
    }
}
```

- [ ] **Step 2: 配置ツールを書く**

`src/DisasterPlus/Game/UI/FireWhirlPlacementTool.cs`:

```csharp
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// クリックした地点に火災旋風を発生させる。
    ///
    /// CS の地形は Unity Physics に登録されていないので Physics.Raycast は絶対に当たらない。
    /// カメラレイと高さ場の交差を Core の RayGeometry で自前に解く。
    /// </summary>
    public class FireWhirlPlacementTool : ToolBase
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>手動発生の強度（byte）。自動発生より少し強めにする。</summary>
        private const byte ManualIntensity = 80;

        /// <summary>手動発生の初期規模。自動発生の閾値と揃える。</summary>
        private const int ManualBurningCount = 12;

        public static void Activate()
        {
            var tool = ToolsModifierControl.toolController.GetComponent<FireWhirlPlacementTool>();
            if (tool == null) { Log.Warn("placement tool not registered"); return; }
            ToolsModifierControl.toolController.CurrentTool = tool;
        }

        public static void Deactivate()
        {
            ToolsModifierControl.SetTool<DefaultTool>();
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            if (Input.GetMouseButtonUp(1)) { Deactivate(); return; }
            if (!Input.GetMouseButtonUp(0)) return;
            if (UIView.IsInsideUI()) return;

            Vec3 hit;
            if (!TryPickGround(out hit))
            {
                Log.Diag("pick", "ray did not hit the terrain");
                return;
            }

            // 災害の生成は sim スレッドで行う。
            // main スレッドから災害・車両バッファを触ると、スタックトレースの無い
            // IndexOutOfRangeException が後から出て、自分の try/catch にも掛からない。
            SimulationManager.instance.AddAction(() =>
            {
                ushort disasterId;
                if (!FireWhirlSpawner.TrySpawn(hit, ManualIntensity, out disasterId)) return;

                FireWhirlRegistry.Add(disasterId, 0, hit,
                    FireWhirlStrength.RadiusFor(ManualBurningCount), ManualBurningCount);
            });
        }

        private static bool TryPickGround(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            var cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 d = ray.direction.normalized;

            return RayGeometry.IntersectTerrain(
                new Vec3(ray.origin.x, ray.origin.y, ray.origin.z),
                new Vec3(d.x, d.y, d.z),
                TerrainHeightSampler.Instance,
                MaxRayDistance,
                out hit);
        }
    }
}
```

- [ ] **Step 3: 災害パネルのボタンを書く**

`src/DisasterPlus/Game/UI/FireWhirlPanelButton.cs`:

```csharp
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 災害パネルに「火災旋風」ボタンを足す。
    ///
    /// ツールチップの文言はここで一度設定されるだけなので、ゲーム内で言語を切り替えても
    /// 次のレベルロードまで変わらない。設定画面と違って OnLocaleChanged では再構築されない。
    /// </summary>
    public static class FireWhirlPanelButton
    {
        private const string ButtonName = "DisasterPlusFireWhirlButton";

        private static UIButton _button;

        public static void Install()
        {
            if (_button != null) return;

            var panel = UIView.Find<UIPanel>("DisastersPanel");
            if (panel == null)
            {
                Log.Diag("panelButton", "DisastersPanel not found yet");
                return;
            }

            _button = panel.AddUIComponent<UIButton>();
            _button.name = ButtonName;
            _button.text = Strings.FireWhirlName;
            _button.tooltip = Strings.FireWhirlTooltip;
            _button.width = 140f;
            _button.height = 28f;
            _button.relativePosition = new Vector3(8f, 8f);
            _button.normalBgSprite = "ButtonMenu";
            _button.hoveredBgSprite = "ButtonMenuHovered";
            _button.pressedBgSprite = "ButtonMenuPressed";

            _button.eventClick += (c, e) => FireWhirlPlacementTool.Activate();

            Log.Info("fire whirl panel button installed");
        }

        public static void Remove()
        {
            if (_button == null) return;
            Object.Destroy(_button.gameObject);
            _button = null;
        }
    }
}
```

**実装前に確認すること:** `UIView.Find<UIPanel>("DisastersPanel")` の名前と、ボタンを置く
親パネルの実際の名前は、ゲーム内の UI ツリーをダンプして確かめる。合わなければボタンが
出ないだけで例外にはならないので、`Log.Diag` の出力で気づけるようにしてある。
ダンプは配置ツール内から一時的に次を実行して行う:

```csharp
foreach (var c in UIView.GetAView().GetComponentsInChildren<UIComponent>())
    if (c.name.Contains("Disaster")) Log.Info("UI: " + c.name + " (" + c.GetType().Name + ")");
```

- [ ] **Step 4: 機能に配線する**

`FireWhirlFeature.OnLevelLoaded`:

```csharp
            ToolRegistration.Register<FireWhirlPlacementTool>();
            FireWhirlPanelButton.Install();
```

`OnLevelUnloading` に `FireWhirlPanelButton.Remove();` を足す。
パネルがまだ無い場合に備えて `OnMainThreadUpdate` でも `FireWhirlPanelButton.Install();`
を呼ぶ（`_button != null` で早期 return するので繰り返し呼んで安全）。

- [ ] **Step 5: ビルドして確認する（手動）**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

1. 都市を読み込み、災害パネルを開く → 「火災旋風」ボタンが出ること
2. ボタンを押して地面をクリック → **その地点に火災旋風が発生すること**
3. 山の斜面や高低差のある場所をクリック → **地面の高さに正しく乗ること**
   （空中に浮かない・地中に埋まらない）
4. 右クリックでツールが解除されること
5. UI の上をクリックしても発生しないこと
6. `output_log.txt` に `FireWhirlPlacementTool registered` が出ていること、
   **スタックトレースの無い `IndexOutOfRangeException` が出ないこと**
7. **2 つ目の都市でボタンとクリックが動くこと**（ツール登録は毎ロード必要）

- [ ] **Step 6: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 災害パネルのボタンとクリック配置ツール"
```

---

## Task 15: 永続化

**Files:**
- Create: `src/DisasterPlus/Game/Common/DisasterPlusSerialization.cs`
- Modify: `src/DisasterPlus/Game/FireWhirl/FireWhirlRegistry.cs`（保存・復元 API）

**Interfaces:**
- Consumes: `FireWhirlRegistry`、`FireWhirlView`、`Vec3`
- Produces:
  - `DisasterPlus.Game.SavedFireWhirl` — `class`、`ushort DisasterId`、`Vec3 Center`、`float Radius`、`int BurningCount`、`float ElapsedMinutes`
  - `DisasterPlus.Game.DisasterPlusSerialization : ISerializableDataExtension`
  - `DisasterPlus.Game.FireWhirlRegistry` — `static void RestoreFromSave(List<SavedFireWhirl> saved)`

- [ ] **Step 1: 実装する**

`src/DisasterPlus/Game/Common/DisasterPlusSerialization.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using DisasterPlus.Core.Common;
using ICities;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 生存中の火災旋風を保存する。
    ///
    /// 論理状態のみを保存し、パーティクル・エフェクト・車両参照は再構築する。
    /// 見た目や実行時の参照まで保存すると、ロード順に依存するバグを作るだけになる。
    ///
    /// 先頭にバージョンを書き、読み込み側は追加ブロックごとに if (version >= N) で分岐する。
    /// 旧セーブは既定値で読める。
    /// </summary>
    public class DisasterPlusSerialization : ISerializableDataExtension
    {
        private const string DataId = "DisasterPlus.FireWhirl";
        private const int CurrentVersion = 1;

        private ISerializableData _data;

        public void OnCreated(ISerializableData serializedData) { _data = serializedData; }
        public void OnReleased() { _data = null; }

        public void OnSaveData()
        {
            if (_data == null) return;

            var views = FireWhirlRegistry.Snapshot();

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CurrentVersion);
                w.Write(views.Count);

                for (int i = 0; i < views.Count; i++)
                {
                    var v = views[i];
                    w.Write(v.DisasterId);
                    w.Write(v.Center.X);
                    w.Write(v.Center.Y);
                    w.Write(v.Center.Z);
                    w.Write(v.Radius);
                    w.Write(v.BurningCount);
                    w.Write(v.ElapsedMinutes);
                }

                _data.SaveData(DataId, ms.ToArray());
            }

            Log.Info("saved " + views.Count + " fire whirls");
        }

        public void OnLoadData()
        {
            if (_data == null) return;

            byte[] bytes = _data.LoadData(DataId);
            if (bytes == null || bytes.Length == 0) return;

            var restored = new List<SavedFireWhirl>();

            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms))
                {
                    int version = r.ReadInt32();
                    if (version < 1) return;

                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var s = new SavedFireWhirl();
                        s.DisasterId = r.ReadUInt16();
                        float x = r.ReadSingle();
                        float y = r.ReadSingle();
                        float z = r.ReadSingle();
                        s.Center = new Vec3(x, y, z);
                        s.Radius = r.ReadSingle();
                        s.BurningCount = r.ReadInt32();
                        s.ElapsedMinutes = r.ReadSingle();
                        restored.Add(s);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error("fire whirl load failed; starting with none", e);
                return;
            }

            FireWhirlRegistry.RestoreFromSave(restored);
            Log.Info("restored " + restored.Count + " fire whirls");
        }
    }

    /// <summary>セーブから読み戻した 1 基。車両 ID は保存しない（ロード後に付け直す）。</summary>
    public class SavedFireWhirl
    {
        public ushort DisasterId;
        public Vec3 Center;
        public float Radius;
        public int BurningCount;
        public float ElapsedMinutes;
    }
}
```

- [ ] **Step 2: レジストリに復元を足す**

```csharp
        /// <summary>
        /// セーブから復元する。車両 ID は 0 のままにして、
        /// FireWhirlPinner.AttachVehicles がロード後に付け直す。
        /// 経過時間は保存値から積み直すので、ロードしても寿命が延びない。
        /// </summary>
        public static void RestoreFromSave(List<SavedFireWhirl> saved)
        {
            lock (_gate)
            {
                _active.Clear();
                for (int i = 0; i < saved.Count; i++)
                {
                    _active.Add(new ActiveFireWhirl
                    {
                        DisasterId = saved[i].DisasterId,
                        VehicleId = 0,
                        Center = saved[i].Center,
                        Radius = saved[i].Radius,
                        BurningCount = saved[i].BurningCount,
                        Life = FireWhirlLifecycle.Start().Advance(saved[i].ElapsedMinutes, true),
                        Ending = false,
                    });
                }
            }
        }
```

- [ ] **Step 3: ビルドして確認する（手動）**

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

1. 火災旋風を発生させ、消える前にセーブする
2. `output_log.txt` に `saved <n> fire whirls` が出ること
3. メインメニューに戻り、そのセーブを読み込む
4. `restored <n> fire whirls` が出ること
5. **旋風がその場に留まったまま復活し、炎も出ていること**
6. **寿命が延びていないこと** — セーブ時に残り 3 分だったら、ロード後も約 3 分で消えること
7. **MOD を無効化して同じセーブを読み込む** → エラーが出ないこと、都市が壊れていないこと
8. もう一度 MOD を有効化して読み込む → 正常に動くこと

- [ ] **Step 4: コミット**

```bash
git add src/DisasterPlus && git commit -m "feat: 火災旋風の永続化（バージョン付き blob）"
```

---

## Task 16: 出荷準備

**Files:**
- Create: `README.md`
- Create: `.github/workflows/core-tests.yml`
- Create: `tools/GenerateLocaleTemplate.ps1`
- Modify: `Locales/en.txt`（ビルド済み DLL から再生成）

**Interfaces:**
- Consumes: `LocaleLoader.WriteTemplate`（Task 7）
- Produces: なし（成果物のみ）

- [ ] **Step 1: en.txt をビルド済み DLL から生成する**

`tools/GenerateLocaleTemplate.ps1`:

```powershell
# en.txt を DLL のリフレクションで生成する。手書きテンプレートは黙って乖離する。
$ErrorActionPreference = "Stop"
$dll = Resolve-Path "src\DisasterPlus\bin\Release\DisasterPlus.dll"
$managed = if ($env:CITIES_SKYLINES_MANAGED) { $env:CITIES_SKYLINES_MANAGED }
           else { "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed" }

$preloaded = @{}
foreach ($n in @('UnityEngine','ColossalManaged','ICities')) {
    $f = Join-Path $managed ($n + '.dll')
    if (Test-Path $f) { $a = [Reflection.Assembly]::LoadFrom($f); $preloaded[$a.GetName().Name] = $a }
}
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($s, $e)
    $n = (New-Object Reflection.AssemblyName $e.Name).Name
    if ($preloaded.ContainsKey($n)) { return $preloaded[$n] }
    return $null
})

$asm = [Reflection.Assembly]::LoadFrom($dll)
$t = $asm.GetType('DisasterPlus.Game.Strings')

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Disaster + - English template (generated by tools\GenerateLocaleTemplate.ps1).")
$lines.Add("# Copy to <lang>.txt and translate the right-hand side. Use \n for line breaks.")
foreach ($f in $t.GetFields('Public,Static')) {
    if ($f.FieldType -ne [string]) { continue }
    $lines.Add($f.Name + " = " + ($f.GetValue($null) -replace "`n", "\n"))
}
[IO.File]::WriteAllLines((Join-Path (Get-Location) "Locales\en.txt"), $lines,
                         (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote Locales\en.txt ($($lines.Count - 2) keys)"
```

実行する:

```bash
powershell -ExecutionPolicy Bypass -File tools/GenerateLocaleTemplate.ps1
```

- [ ] **Step 2: ja.txt のキー整合を確認する**

```bash
powershell -Command "$en=(Get-Content Locales\en.txt | Where-Object { $_ -match '=' -and $_ -notmatch '^#' } | ForEach-Object { ($_ -split '=')[0].Trim() }); $ja=(Get-Content Locales\ja.txt -Encoding UTF8 | Where-Object { $_ -match '=' -and $_ -notmatch '^#' } | ForEach-Object { ($_ -split '=')[0].Trim() }); Write-Host ('missing in ja: ' + (($en | Where-Object { $ja -notcontains $_ }) -join ', ')); Write-Host ('unknown in ja: ' + (($ja | Where-Object { $en -notcontains $_ }) -join ', '))"
```

Expected: どちらも空。差分があれば `ja.txt` を直す。

- [ ] **Step 3: CI を作る**

`.github/workflows/core-tests.yml`:

```yaml
name: Core tests

# 走るのは Core のユニットテストだけ。MOD 本体はゲームの再配布不可な net35 DLL に
# 依存するため、GitHub のランナーではビルドできない。
# 緑は「純ロジックが健全」を意味するのであって、「MOD がビルドできる」ではない。
# ビルドの確認はローカルの build.ps1 で行う。
on:
  push:
    branches: [ master, main ]
  pull_request:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj --verbosity normal
```

**ワークフローを push する前にローカルでテストを走らせること。** 新しいバッジが初回で赤くならない。

- [ ] **Step 4: README を書く**

`README.md`:

```markdown
# Disaster + (DisasterPlus)

Cities: Skylines 1 用の災害 MOD。Natural Disasters DLC の災害に、
現象としてのリアルさと可視化を足す。

## 実装済み

- **火災旋風** — 密集した大規模火災から、その場に留まる炎の渦が発生する。
  周囲に火の粉を撒いて延焼を広げ、火が収まるか最大持続時間で消える。
  災害パネルのボタンから手動でも発生させられる。
- **災害強度の解放** — 手動発生の強度スライダー上限を 10.0 から 25.5 に引き上げる。

## 予定

天気予報タブ（落雷・竜巻確率のヒートマップ）、地震オーバーホール（震度分布・
長周期地震動・地震計波形・津波連動）、台風、火山。

## 必要なもの

- Cities: Skylines 1
- Natural Disasters DLC（火災旋風に必要）
- [Harmony 2.2](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)（渦の位置固定に必要。
  無くても MOD は動くが、旋風がその場に留まらず移動する）

## 他 MOD との併用

**Natural Disasters Renewal** と併用できる。役割を分けている。

| | Natural Disasters Renewal | Disaster + |
|---|---|---|
| 既存災害の発生頻度・強度・避難 | 担当 | 触らない |
| 新しい災害現象 | なし | 火災旋風（今後 台風・火山） |
| 可視化 | なし | 今後 予報・震度分布 |

併用時の注意:

- **強度解放**は両方が持つ機能なので、Disaster + 側は既定で無効になる（設定で有効にできる）
- **火災旋風のバニラ由来の破壊**は Natural Disasters Renewal の竜巻設定に従う。
  同 MOD は `DisasterHelpers.DestroyBuildings` を置き換えており、竜巻の判定に使う
  `burnRadius` は CS 本体側で 0 に固定されているため、Disaster + から回避できない
- **延焼拡大は影響を受けない。** 火災旋風の核心である「周囲に火を撒く」挙動は
  `DisasterHelpers` を経由しない自前実装なので、併用しても必ず動く

## ビルド

```powershell
.\build.ps1
```

ゲーム DLL の場所は環境変数 `CITIES_SKYLINES_MANAGED` で上書きできる。
未設定なら既定の Steam パスを使う。

## テスト

```bash
dotnet test tests/DisasterPlus.Core.Tests/DisasterPlus.Core.Tests.csproj
```

`Core` はエンジン非依存なので、ゲームを起動せずに全ルールを検証できる。
`Game` 層はユニットテストできないので、`Cities_Data\output_log.txt` を読んで確認する。

## ライセンス

MIT
```

- [ ] **Step 5: 総合確認（手動）— 完了条件の全項目**

仕様書 7 章の完了条件を 1 つずつ確認する。

1. `build.ps1` が通り、ゲームが MOD をエラーなしで読み込む
2. 設定画面が日本語・英語で表示され、**再起動せずに**言語切替に追従する
3. 災害パネルに「火災旋風」ボタンが出て、クリック地点に手動発生できる
4. 密集火災で**自動発生**し、**その場に留まり**、**炎をまとって見える**
5. 周囲に延焼が広がる
6. 火が収まるか最大持続時間で消滅する
7. セーブ・ロードで生存中の旋風が復元され、**寿命が延びない**
8. **2 つ目の都市をロードしても全部動く**
9. 災害パネルの強度スライダーが **25.5** まで動く
10. **NDR を併用し、その `EnableDestruction` を OFF にしても延焼拡大が動く**
11. `dotnet test` が全て緑
12. `output_log.txt` に `DisasterPlus` 由来の例外が無く、`trying to load .* Deleting` も無い

- [ ] **Step 6: コミット**

```bash
git add README.md .github tools Locales && git commit -m "docs: README・CI・ロケールテンプレート生成"
```

---

## 付録: 実装中に IL で確認すべき残件

計画作成時に主要 4 項目は確定済み（仕様書 付録 A）。実装中に**推測で埋めてはいけない**残件:

| タスク | 確認すること | 手段 |
|---|---|---|
| 12 | 建物グリッドの定数（セル寸法・グリッド辺長・`m_buildingGrid` / `m_nextGridBuilding`） | `BuildingManager.AddToGrid` の IL と `Awake` の `newarr` 直前の `ldc.i4` |
| 14 | 災害パネルの UI コンポーネント名 | ゲーム内で UI ツリーをダンプ（Task 14 Step 3 にコード掲載） |
| 9 / 11 | 1 ゲーム内分あたりのシミュレーションフレーム数 | 実測（Task 11 Step 8-5 の手順） |

`docs/tools/ilload.ps1` と `docs/tools/ildasm.ps1` を使う。使い方:

```powershell
. docs\tools\ilload.ps1 ; . docs\tools\ildasm.ps1
$bf = [Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly'
Disasm-Method -Method $A.GetType('BuildingManager').GetMethod('AddToGrid', $bf) -Filter 'ldc.i4|stfld'
```

**リフレクションで分かるのは型・可視性・シグネチャまで。**
単位・実際の配列長・フィールドの意味はメソッド本体からしか分からない。
