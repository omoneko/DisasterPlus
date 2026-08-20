# バニラの視覚エフェクトを借りる — IL / アセット実測

> 対象: `DisasterPlus`（Cities: Skylines 無印, Unity 5.6）
> 手段: `docs/tools/ilload.ps1` + `docs/tools/ildasm.ps1`（`Assembly-CSharp.dll` / `ColossalManaged.dll`）と
> UnityPy による `globalgamemanagers` / `level*` / `sharedassets*.assets` の走査。
> 記法: 各項に **CONFIRMED**（IL かアセットのバイト列で確定）/ **PARTIAL**（アセットには在るが実行時 API が
> 返すことは未確認）/ **ABSENT**（存在しないことを確定）を付す。

## 0. なぜこの調査か

火山を遊んだ持ち主の指摘:

> 炎や噴煙、噴石、火砕流のアニメーションやモデルはもっとリアルに（ポリゴンではなくバニラのエフェクトを参考に）してください。

指摘は正しく、理由も判明している。**実機テストで `Shader.Find` が `"Standard"` を含めて全ての名前に
`null` を返した**（本プロジェクト 13 回目の誤った「確認済み」）。自前メッシュ＋自前マテリアルの
プルーム／溶岩は何も描画しない。バニラの粒子エフェクトは**既に読み込まれ、既に動くマテリアルを
持っている**。

### 既知として引用する（再導出しない）

| 出典 | 内容 |
|---|---|
| `2026-08-15-volcano-il-facts.md` §H-17 | `FireEffect.RenderEffect` は `m_soundEffect` に触れない。音は `PlayEffect` 側 |
| 同 §H-18 | `RenderEffect` / `SpawnArea(Vector3,Vector3,float)` / `RenderManager.CurrentCameraInfo` の到達経路、`CameraInfo` に null を渡すと NRE |
| 同 §H-22 | シェーダ名がアセットに在ることと `Shader.Find` が解決することは別（本書 §D-3 で決着） |
| `2026-08-11-…-firewhirl-design.md` §4.8 / §4.9 | `static Mesh[]` の fake-null / CS のマテリアルを借りると自前 `MeshRenderer` では見えない / `DispatchEffect` の magnitude は粒子密度 |
| `2026-08-15-typhoon-il-facts.md` §C-1 | `DisasterInfo.m_effect` は存在しない。災害まわりの `EffectInfo` フィールドは `DisasterProperties.m_mediumExplosion` と `MeteorAI.m_impactEffect` の 2 つだけ |

§C-1 の「災害まわりのフィールドは 2 つだけ」は**フィールドの話としては今も正しい**。しかし
「プレハブ化された効果は存在しない」という結論は**本書で覆る**。効果プレハブは
`DisasterInfo` からではなく **`EffectCollection`** と **各種 `*Properties`** からぶら下がっている。

---

## 1. サマリ

| 問い | 答え | 確度 |
|---|---|---|
| `PrefabCollection<EffectInfo>` で列挙できるか | **できない。** `PrefabCollection<T>` は `where T : PrefabInfo`、`EffectInfo : MonoBehaviour` | **ABSENT** |
| ではどう列挙するか | `EffectCollection.FindEffect(name)` / `EffectCollection.Effects`（public static）。全体は `EffectManager.instance.m_EffectsWrapper.m_BuiltinEffects`（`Dictionary<string,EffectInfo>`、中身は `Resources.FindObjectsOfTypeAll<EffectInfo>()`） | **CONFIRMED** |
| 何個あるか | 出荷アセットに `EffectInfo` 派生コンポーネントは **277 個**。うち **234 個が基本ゲーム**（DLC 不要）、43 個が DLC シーン内 | **CONFIRMED**（アセット）／実行時個数は **PARTIAL** |
| 名前で引ける登録数 | `EffectCollection` の登録は **186 個**、5 つのテーマシーン（Sunny/North/Tropical/Europe/Winter）**全てが同一の 186 個**を登録する＝**全部 DLC 不要** | **CONFIRMED** |
| DLC ゲートは効くか | 効く。`Expansion3Prefabs`（＝Natural Disasters, `sharedassets55`）は `LoadingManager.m_supportsExpansion[2]` で読み込みが分岐する。テーマの `<env>Prefabs` / `<env>Properties` は**無条件** | **CONFIRMED** |
| 任意座標で鳴らす（描く）方法 | `effect.RenderEffect(id, new EffectInfo.SpawnArea(pos, dir, radius), velocity, accel, magnitude, **-1f**, simulationTimeDelta, RenderManager.instance.CurrentCameraInfo)` — バニラの `SinkholeAI.RenderInstance` と同一形 | **CONFIRMED** |
| `RenderEffect` と `PlayEffect` の違い | `RenderEffect` = 粒子・光（描画スレッド）／`PlayEffect` = 音のみ（`ListenerInfo` + `AudioGroup` が要る）。`ParticleEffect` は `PlayEffect` を override せず `RequirePlay()` は基底の `false` | **CONFIRMED** |
| 本物の `InstanceID` / `Group` が要るか | **不要。** ただし `default(InstanceID)` は `Randomizer` の種が 0 に固定される（§B-6 の罠） | **CONFIRMED** |
| 毎フレーム main スレッドから駆動してよいか | よい。ただし `timeDelta` は `Time.deltaTime` ではなく `SimulationManager.instance.m_simulationTimeDelta` を使う（一時停止で止まる） | **CONFIRMED** |
| `DispatchEffect` の magnitude | 粒子密度で正しい。大きさは `SpawnArea` 半径。`DispatchEffect` 自体は**キューに積むだけ**でスレッド安全 | **CONFIRMED** |
| 実行時にクローンして色・寿命を変えられるか | **できる。** 内部フィールドは全部 `[NonSerialized]` なので `Object.Instantiate` したクローンは未初期化状態になり、`InitializeEffect()` で自前の `ParticleSystem` を持つ。マテリアルは `ParticleSystemRenderer` に付くので③の「借りたマテリアルが見えない」問題には**当たらない** | **CONFIRMED** |
| 毎フレームのコスト | `RenderEffect` 経路に**ヒープ確保は無い**（`EmitParams` / `Randomizer` / `SpawnArea` は全て struct）。重いのは粒子数 `max(100, πr²) × pps` のループ。`ParticleSystem.maxParticles` に対する自動絞り込みがある | **CONFIRMED** |

---

## 2. 型の全体像 — CONFIRMED

```
EffectInfo : UnityEngine.MonoBehaviour          ★ PrefabInfo ではない
  m_refCount : Int32 (private)

  public virtual void RenderEffect(InstanceID id, SpawnArea area, Vector3 velocity,
                                   float acceleration, float magnitude,
                                   float timeOffset, float timeDelta,
                                   RenderManager.CameraInfo cameraInfo)   基底は ret のみ
  public virtual void PlayEffect  (InstanceID id, SpawnArea area, Vector3 velocity,
                                   float acceleration, float magnitude,
                                   AudioManager.ListenerInfo listenerInfo,
                                   AudioManager.AudioGroup audioGroup)    基底は ret のみ
  public void InitializeEffect()      // m_refCount++ が 0→1 のとき CreateEffect()
  public void ReleaseEffect()         // m_refCount-- が 0 になったとき DestroyEffect()
  protected virtual void CreateEffect() / DestroyEffect()
  public virtual bool  RequireRender()   基底 false
  public virtual bool  RequirePlay()     基底 false
  public virtual float RenderDistance()  基底 0
  public virtual float RenderDuration()  基底 0
  public virtual int   GroupLayer()
  public virtual bool  CalculateGroupData(...) / void PopulateGroupData(...)
  public ushort GetBuilding(InstanceID id)

派生（アセンブリ全型走査、8 つ。§C-1 の列挙と一致）:
  SoundEffect : EffectInfo            EngineSoundEffect : SoundEffect
  ParticleEffect : EffectInfo         MovementParticleEffect : ParticleEffect
                                      ShipTrailParticleEffect : MovementParticleEffect
  FireEffect : EffectInfo             LightEffect : EffectInfo
  MultiEffect : EffectInfo
```

> **`SmokeEffect` / `BuildingEffect` は ABSENT。** アセンブリに存在しない（CS2 側の名前である）。
> 煙は `ParticleEffect` の 1 インスタンス（`"Factory Smoke"` 等）でしかない。

### 能力で選ぶための対応表 — CONFIRMED

| 型 | `RequireRender` | `RequirePlay` | `RenderEffect` が使うもの |
|---|---|---|---|
| `ParticleEffect` | **true**（定数） | false | `SpawnArea`, `velocity`, `magnitude`, `timeOffset`, `timeDelta`, `cameraInfo`。`acceleration` は**無視** |
| `MovementParticleEffect` | true | false | 上に加えて `acceleration` と `velocity` の**大きさ**で magnitude を上書きする（下記 §B-7） |
| `ShipTrailParticleEffect` | true | false | 同上（水面追随の派生） |
| `FireEffect` | 子の OR | 子の OR | `m_particleEffect` と `m_lightEffect` のみ。**`m_soundEffect` には触れない**（§H-17） |
| `LightEffect` | true | false | `SpawnArea` の行列と `RenderManager.lightSystem.DrawLight`。粒子は出ない |
| `MultiEffect` | 子の OR | 子の OR | 引数をそのまま全ての子へ配る。`m_duration` があると位相ゲートが掛かる |
| `SoundEffect` / `EngineSoundEffect` | false | true | `RenderEffect` を override して**いない**＝呼んでも何も起きない |

---

## 3. §A 在庫

### A-1. `PrefabCollection<EffectInfo>` は成立しない — **ABSENT**

```
PrefabCollection`1 の型引数制約: T : PrefabInfo
EffectInfo の継承鎖:  EffectInfo -> UnityEngine.MonoBehaviour -> Behaviour -> Component -> Object
```

`EffectInfo` は `PrefabInfo` を継承していないので `PrefabCollection<EffectInfo>` は**コンパイルが通らない**。
調査依頼が想定していた経路はここで消える。

### A-2. 正しい列挙経路 — **CONFIRMED**

```csharp
// (a) 名前で 1 個引く。public static。
EffectInfo e = EffectCollection.FindEffect("Factory Smoke");   // 無ければ null + CODebugBase.Warn

// (b) 登録済みを全部なめる。public static プロパティ。
foreach (EffectInfo e in EffectCollection.Effects) { ... }

// (c) 「読み込まれている EffectInfo すべて」。EffectCollection 未登録のものも含む。
var dict = Singleton<EffectManager>.instance.m_EffectsWrapper.m_BuiltinEffects; // Dictionary<string,EffectInfo>
EffectInfo e2 = Singleton<EffectManager>.instance.m_EffectsWrapper.GetBuiltinEffect("Fire Effect");
```

IL:

```
EffectCollection : MonoBehaviour
  public  EffectInfo[]                       m_effects
  private static Dictionary<string,EffectInfo> m_dict
  Awake()                 -> InitializeEffects(m_effects)      // 名前をキーに Add、重複は Error
  OnDestroy()             -> DestroyEffects(m_effects)         // Remove
  public static EffectInfo FindEffect(string name)             // TryGetValue、無ければ Warn して null
  public static IEnumerable<EffectInfo> Effects { get; }

EffectsWrapper.InitEffectCollection():
  IL_0021  Resources::FindObjectsOfTypeAll<EffectInfo>()        ★ ここが「全部」の定義
  IL_003F  m_BuiltinEffects[o.name] = o
  IL_004A  o.GetType() == typeof(ParticleEffect) のとき
           GetComponent<Renderer>().sharedMaterial を
           m_BuiltinParticleMaterials[mat.name] = mat           ★★ 動く粒子マテリアルの辞書
  IL_009B  o.GetType() == typeof(SoundEffect) のとき
           m_BuiltinAudioClips[clip.name] = clip

EffectManager.Awake():
  LoadingManager.m_metaDataReady  += CreateRelay          // m_EffectsWrapper を new
  LoadingManager.m_levelLoaded    += InitEffectCollection // ここで上の 3 辞書が埋まる
  LoadingManager.m_levelUnloaded  += ReleaseRelay
```

> **`m_BuiltinParticleMaterials` は本件の核心である。** `Shader.Find` が全滅する環境でも、
> ここには「このビルドで実際に粒子を描いているマテリアル」が名前付きで入っている（§D-3）。

### A-3. 登録されている 186 個 — **CONFIRMED**（アセット）

`level11 = Assets/Data/Scenes/Sunny/SunnyPrefabs.unity` の `EffectCollection.m_effects` を
生バイトから復元した（`int count` + `count × PPtr{int fileID, long pathID}`）。

```
count = 186
fileID ヒストグラム: {3: 180, 4: 6}
externals[2] = sharedassets11.assets   externals[3] = sharedassets9.assets
```

同じ復元を 5 つのテーマシーンで行い、**pathID 列が完全に一致**することを確認した:

```
level11 (SunnyPrefabs)    186   \
level14 (NorthPrefabs)    186    |  pathID 列は 5 本とも byte 単位で同一
level17 (TropicalPrefabs) 186    |  (level28 だけ fileID が 1 ずれるが指す先は同じ)
level28 (EuropePrefabs)   186    |
level40 (WinterPrefabs)   186   /
level132/133 (Expansion14Prefabs / Winter版) は別の 10 個
```

`LoadingManager+<LoadLevelCoroutine>c__Iterator1.MoveNext`:

```
IL_0690  prefabScenes.Add(SimulationManager.instance.m_metaData.m_environment + "Prefabs", 1.27f)
         ★ 無条件。m_supportsExpansion の分岐は無い
IL_1974  propertiesScene = m_metaData.m_environment + "Properties"
         ★ これも無条件

対して DLC は:
IL_082C  if (m_supportsExpansion[2]) prefabScenes.Add("Expansion3Prefabs", 0.04f)   // Natural Disasters
LoadingManager.DLC(uint id) { return SteamHelper.IsDLCOwned(id); }  // m_supportsExpansion の出所
```

→ **`EffectCollection` の 186 個は基本ゲームのプレイヤー全員が持つ。**

内訳（型別）:

| 型 | 個数 | 備考 |
|---|---|---|
| `LightEffect` | 79 | 街灯・車両灯・投光器 |
| `SoundEffect` (35) / `EngineSoundEffect` (26) | 61 | |
| `MultiEffect` | 30 | 車両の「movement」束ね |
| `ParticleEffect` | 11 | **視覚的に使えるのはここ** |
| `MovementParticleEffect` | 2 | `Gravel Dust` / `Snowplow Particles` |
| `ShipTrailParticleEffect` | 2 | `Ship Trail` / `Boat Trail` |
| `FireEffect` | 1 | `Fire Effect Rocket` |

`EffectCollection` に**登録されていない**が同じ `sharedassets11/9` に居る 17 個（`*Properties` や
`BuildingInfo.m_effects` から参照される）:

```
Factory Smoke / Factory Smoke Small / Factory Steam / Fireman Water / Bird Poo /
Fire Light / Fire Sound / Cow Random Effect / Pig Random Effect / Seagull Random Effect /
Seagull Scream / Aircraft Sound Medium / Park Streetlight / Street Lamp Dim /
Street Light Eco / Power Line Ready Effect / Water Pipe Ready Effect
```

> **重要:** `EffectCollection.FindEffect("Factory Smoke")` は **null を返す**（未登録）。
> これらは `EffectsWrapper.GetBuiltinEffect(name)` か、参照元のフィールド経由で取る。

### A-4. `*Properties` 側の 31 個（`sharedassets12` = `SunnyProperties`）— **CONFIRMED**

**全部が基本ゲーム。** `<env>Properties` シーンは無条件で読み込まれる（上記 IL_1974）。

```
FireEffect      Fire Effect              <- BuildingManager.instance.m_properties.m_fireEffect
FireEffect      Fire Effect Small
ParticleEffect  Fire Particles           <- Fire Effect の子
ParticleEffect  Collapse Particles       <- Collapse Effect の子
ParticleEffect  Placement Particles
ParticleEffect  Medium Explosion Particles <- Medium Explosion Effect の子
MultiEffect     Collapse Effect          <- m_properties.m_collapseEffect
MultiEffect     Collapse Effect Flooded  <- m_collapseFloodedEffect
MultiEffect     Medium Explosion Effect  <- DisasterProperties.m_mediumExplosion
MultiEffect     Levelup Effect / Building|Prop|Road Placement|Bulldoze Effect
LightEffect     Levelup Light / Medium Explosion Light
SoundEffect     Collapse Sound / Fire Sound Small / … （音は本件の対象外）
```

### A-5. DLC ゲートが掛かる 43 個 — **CONFIRMED**

| ファイル | シーン | DLC | 個数 | 視覚的に目を引くもの |
|---|---|---|---|---|
| `sharedassets55` | `Expansion3Prefabs` | **Natural Disasters** | 11 | `Huge Explosion Effect` / `Large Explosion Effect` / `Meteor Effect` とその粒子・光 |
| `sharedassets63` | `FestivalPrefabs` | Concerts | 12 | トラス照明のみ |
| `sharedassets132` | `Expansion14Prefabs` | （拡張 14） | 10 | `Glitter Cannon Particles` / `Event Firework Particles 1-3` |
| `sharedassets114` | `Expansion10Prefabs` | Airports | 5 | 航空機の movement/音 |
| `sharedassets75` | `Expansion7Prefabs` | Industries | 4 | 街灯・貨物機 |
| `sharedassets69` | `TropicalExpansion6Prefabs` | Parklife | 1 | 街灯 |

> **`Huge/Large Explosion` と `Meteor Particles` は使えない。** 火砕流や噴石に一番近い見た目だが
> Natural Disasters 所持者にしか存在しない。⑤火山は ND 非依存が要件（火災旋風設計 §4.12）なので**却下**。
> どうしても使うなら `EffectCollection.FindEffect` ではなく `GetBuiltinEffect("Huge Explosion Effect")` で
> **null チェック付きの任意追加**にする（所持者だけ豪華になる）。

### A-6. 使える `ParticleEffect` の実測値一覧 — **CONFIRMED**（アセットのバイト列。全件 `bytes_left = 0` で検算済み）

`ParticleEffect` の serialize 順（宣言順）は
`m_useSimulationTime, m_canUseMeshData, m_canUsePositions, m_canUseBezier`（bool は 1 byte + 4 整列）,
`m_maxVisibilityDistance, m_minLifeTime, m_maxLifeTime, m_minStartSpeed, m_maxStartSpeed,
m_minSpawnAngle, m_maxSpawnAngle, m_renderDuration, m_extraRadius, m_intensityCurve,
m_buildingFlagRequired, m_simulationSpeedScale`。

| 名前 | 由来 | life(s) | speed | spawnAngle° | renderDur | extraR | vis | bezier可 | size | rate | gravity | maxP | マテリアル (shader) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Factory Smoke** | 基本 | 1–5 | 10–15 | 0–5 | 0 | 0 | 1000 | ○ | 7 | 15 | 0 | 10000 | `Smoke` (Alpha Blended) |
| Factory Smoke Small | 基本 | 1–3 | 5–8 | 0–5 | 0 | 0 | 1000 | ○ | 6 | 15 | 0 | 10000 | `Smoke` |
| Factory Steam | 基本 | 1–5 | 8–12 | 0–5 | 0 | 0 | 1000 | ○ | 11 | 10 | 0 | 10000 | `Steam` |
| Pool Steam | 基本 | 1–5 | 8–12 | 0–5 | 0 | 0 | 1000 | ○ | 11 | 9.67 | 0 | 10000 | `Steam` |
| Large Pool Steam | 基本 | 3–6 | 0.1–0.2 | 0–5 | 0 | **9** | 1000 | ○ | 12 | 10 | 0 | 10000 | `Steam` |
| **Fire Particles** | 基本 | 1.5–2 | 0–1 | 0–90 | 0 | 0 | 1000 | ○ | 4 (min2) | **200** | −0.6 | 10000 | `Fire` (**Additive Soft**) |
| Fire Particles Small | 基本 | 1.5–2 | 0–1 | 0–90 | 0 | 0 | 750 | ○ | 1.25 | 200 | −0.1 | 10000 | `Fire` |
| Fire Particles Rocket | 基本 | – | – | – | – | – | – | – | 8 (min4) | 200 | −0.6 | 10000 | `FireRocket` (Additive Soft) |
| **Medium Explosion Particles** | 基本 | 1–1.5 | **100–150** | **0–80** | **1.0** | 0 | **10000** | **×** | 60 (min40) | 150 | −1.0 | 2000 | `Explosion` (Additive Soft) |
| **Collapse Particles** | 基本 | 1.5–2.5 | 4–8 | **50–80** | 0 | 0 | 1000 | ○ | 12 | 20 | −0.2 | 10000 | `Placement Dust` (Alpha Blended) |
| Placement Particles | 基本 | 1.5–2.5 | 2–3 | 50–80 | 0 | 0 | 1000 | ○ | 12 | 3 | −0.2 | 10000 | `Placement Dust` |
| Dust Marker 01 | 基本 | 1–3 | 0.01–0.2 | 0–5 | 0 | 2 | 1000 | ○ | 5 | 10 | +0.3 | 10000 | `IndustryDust` (Alpha Blended) |
| Ore Debris 01 | 基本 | 1.3 | 0.01–0.2 | 0–5 | 0 | 0.5 | 1000 | ○ | 1 | 200 | +0.9 | 1000 | `OreDebris` |
| Sand Debris 01 | 基本 | 1.3 | 0.01–0.2 | 0–5 | 0 | 0.3 | 1000 | ○ | 0.35 | 200 | +0.9 | 1000 | `SandDebris` |
| **Fireman Water** | 基本 | 1–2 | 8 | 0–1 | 0 | 0 | 500 | **×** | 3 | 35 | +0.6 | 1000 | `Water` (Alpha Blended) |
| Fire Copter Water Particles | 基本 | 3–4 | 0 | 0–1 | 0 | 0 | 2000 | **×** | 10 | 35 | +1.0 | 1000 | `Water` |
| Snowplow Particles | 基本(Move) | 1–1.5 | 0 | 0 | 0 | 0 | 500 | × | 6 | 6 | +0.2 | 10000 | `Snow` |
| Ship Trail | 基本(Trail) | – | – | – | – | – | – | – | 15 | 40 | +2.0 | 10000 | `Ship Trail` (Alpha Tested) |
| Boat Trail | 基本(Trail) | – | – | – | – | – | – | – | 10 | 40 | −0.5 | 10000 | `Ship Trail` |
| Electricity Break Particles | 基本 | – | – | – | – | – | – | – | 1 | 10 | +1.0 | 1000 | `Explosion` (Additive Soft) |
| FireworksComplexIngame | 基本 | – | – | – | – | – | – | – | 0.9 | 2 | 0 | 10000 | `FireworksIngame` + `FireworksTrail` |
| RocketLaunch particles | 基本 | – | – | – | – | – | – | – | 60 (min40) | 150 | −1.0 | 500 | `RocketLaunch` (Additive Soft) |
| *Large Explosion Particles* | **ND** | 2–4 | 50–75 | 80–90 | 2.0 | 0 | 10000 | × | 85 | 800 | 0 | 10000 | `Explosion` |
| *Large Explosion Particles 2* | **ND** | 4–6 | 25–38 | 0–80 | 2.0 | 0 | 10000 | × | – | – | – | – | `Explosion` |
| *Huge Explosion Particles* | **ND** | 2–4 | 100–150 | 80–90 | 2.0 | 0 | 10000 | × | 100 | 1000 | 0 | 10000 | `Explosion` |
| *Huge Explosion Particles 2* | **ND** | 4–6 | 50–75 | 0–80 | 2.0 | 0 | 10000 | × | – | – | – | – | `Explosion` |
| *Meteor Particles* | **ND** | 0.5–1 | 10–15 | 0–90 | 0 | 0 | 10000 | ○ | 15 | 200 | 0 | 10000 | `Explosion` |

`size` = `ParticleSystem.main.startSize.constant`、`rate` = `ParticleSystem.emission.rateOverTime.constant`
（**粒子数の直接の乗数。§B-4**）、`gravity` = `main.gravityModifier`、`maxP` = `main.maxParticles`。
斜体は DLC。

**このビルドで実際に粒子を描いているシェーダ名は 3 つしかない:**

```
Custom/Particles/Additive (Soft)      Fire / FireRocket / Explosion / RocketLaunch / Fireworks*
Custom/Particles/Alpha Blended        Smoke / Steam / Placement Dust / IndustryDust /
                                      OreDebris / SandDebris / Water / Snow / Poo
Custom/Particles/Alpha Tested         Ship Trail
```

→ §H-22 が挙げた `"Particles/Additive"` は**バニラの粒子が使っている名前ではない**。
`Shader.Find` に賭けるなら `"Custom/Particles/Additive (Soft)"` の方が筋が良いが、
**賭けずに `m_BuiltinParticleMaterials` から `Material` を取る**のが正解である（§D）。

> **PARTIAL の範囲:** 上表は**アセットに何が入っているか**の証拠であり、
> 「実行時に API がそれを返す」証拠ではない（§H-22 で高くついた教訓）。
> 実機で決着させる 1 行:
> ```csharp
> Log.Info(string.Join(", ", Singleton<EffectManager>.instance.m_EffectsWrapper.GetEffectList()));
> ```
> （`GetEffectList()` は `m_BuiltinEffects.Keys.ToArray()`。**配列を確保するので毎フレーム呼ばない**。）
> 期待値: 基本ゲームで 234 前後、ND 所持で +11。

---

## 4. §B 任意座標での再生

### B-1. `RenderEffect` と `PlayEffect` の分業 — **CONFIRMED**

```
EffectManager.EndRenderingImpl(CameraInfo)          <- IRenderableManager.EndRendering（描画スレッド）
  m_renderEventBuffer を先頭から走査
  RenderEvent(cameraInfo, ref e):
    IL_0011  e.m_startFrame != 0 && e.m_startFrame > SimulationManager.m_referenceFrameIndex
             -> まだ来ていない。true を返して残す（＝遅延発火）
    IL_0033  dt = SimulationManager.instance.m_simulationTimeDelta        ★ Time.deltaTime ではない
    IL_006A  e.m_effectInfo.RenderEffect(e.m_instance, e.m_spawnArea, e.m_velocity,
                                         e.m_acceleration, e.m_magnitude,
                                         e.m_timeOffset, dt, cameraInfo)
    IL_006F  e.m_timeOffset += dt
    IL_007D  return e.m_timeOffset < e.m_renderDuration    // false で列から除去
  除去は lock(m_renderEventBuffer) の中で詰め直す

EffectManager.PlayAudioImpl(ListenerInfo)           <- IAudibleManager.PlayAudio
  m_audioEventBuffer に対して PlayEvent -> EffectInfo.PlayEffect(..., listenerInfo, audioGroup)
```

- `RenderEffect` は**音を一切出さない**（§H-17 の再確認。`FireEffect.RenderEffect` の全 IL に
  `m_soundEffect` への参照は無い）。
- `PlayEffect` は `AudioManager.ListenerInfo` と `AudioManager.AudioGroup` を要求する。
  `ParticleEffect` はこれを override して**いない**うえ `RequirePlay()` が基底の `false` なので、
  粒子エフェクトに対して `PlayEffect` を呼ぶ意味は無い。⑤の音は §H-23 の
  `AudioManager.EffectGroup` 経路のまま。

### B-2. `SpawnArea` の 4 つの形 — **CONFIRMED**

```
public struct EffectInfo.SpawnArea
  Matrix4x4 m_matrix;  MeshData m_meshData;  Vector4[] m_positions;  Vector3[] m_positions2;
  Bezier3 m_bezier;    float m_halfWidth;    float m_halfHeight;

public .ctor(Vector3 position, Vector3 direction, float radius)
  dir.sqrMagnitude > 1e-4 ? Quaternion.LookRotation(dir) : identity
  m_matrix.SetTRS(position, rot, Vector3.one)
  m_meshData = m_positions = m_positions2 = null
  m_bezier   = new Bezier3(position, position, position, position)   ★ d == a になる
  m_halfWidth = radius ;  m_halfHeight = 0
public .ctor(Vector3 position, Vector3 direction, float radius, float halfHeight)   // 上の halfHeight 版
public .ctor(Bezier3 bezier, float halfWidth, float halfHeight)
  m_matrix.SetTRS((a+b+c+d)*0.25f, LookRotation((d-a).normalized), one)
  m_meshData = m_positions = m_positions2 = null ;  m_bezier = bezier
public .ctor(Matrix4x4, MeshData [, Vector4[], Vector3[]])
```

`ParticleEffect.EmitParticles(id, area, …)` の分岐（優先順）:

```
1. m_canUseMeshData  && area.m_meshData  != null  -> メッシュ面から湧かす
2. m_canUsePositions && area.m_positions != null  -> 点群から湧かす
3. m_canUseBezier    && area.m_bezier.d != a      -> ベジェ帯から湧かす   ★ 火砕流はここ
4. それ以外 -> 点/円盤:
     point = area.m_matrix.MultiplyPoint(Vector3.zero)
     dir   = area.m_matrix.MultiplyVector(Vector3.forward)
     radius = area.m_halfWidth + this.m_extraRadius
     halfHeight = area.m_halfHeight
```

→ **`SpawnArea(pos, dir, r)` を渡すと必ず 4 番に落ちる**（`d == a` なのでベジェ分岐が飛ぶ）。
ベジェ帯を使いたいときは `SpawnArea(bezier, halfWidth, halfHeight)` を明示的に作る。

### B-3. `timeOffset` の符号が動作モードを決める — **CONFIRMED**（最重要）

`ParticleEffect.RenderEffect` の IL_00A2 以降:

```
if (timeOffset < 0f) {                       // ★ 継続モード
    pps = timeDelta * magnitude * 0.01f;     //   m_renderDuration も m_intensityCurve も無視
}
else if (m_renderDuration != 0f) {           // ★ 有限持続モード
    if (timeOffset > m_renderDuration) return;                       // 打ち切り
    magnitude *= m_intensityCurve.Evaluate(timeOffset / m_renderDuration);
    pps = timeDelta * magnitude * 0.01f;
}
else {                                       // ★ 一発モード（m_renderDuration == 0）
    if (timeOffset != 0f) return;
    pps = magnitude * 0.01f;                 //   timeDelta を掛けない
}
EmitParticles(id, area, velocity, pps, /*probability*/ 100, ref min, ref max);
```

`probability` は `ParticleEffect` からは常に **100**（＝必ず出す）。

**バニラ自身が継続モードを使っている実例**（`SinkholeAI.RenderInstance`, IL_0197–0292）:

```
InstanceID id = default(InstanceID);
id.Disaster = disasterID;                                     // IL_01A1
float w = m_holeWidth * data.m_intensity * 0.01f + 16f;
SpawnArea area = new SpawnArea(data.m_targetPosition, Vector3.up, w * 0.5f + 8f);
float magnitude = t*t*2f;                                     // 0 -> 2 まで上がる（1 を超えてよい）
BuildingManager.instance.m_properties.m_collapseEffect.RenderEffect(
    id, area, Vector3.zero, 0f, magnitude,
    -1f,                                                      // IL_0287  ldc.r4 -1  ★ 継続モード
    SimulationManager.instance.m_simulationTimeDelta,          // IL_018E
    cameraInfo);
```

→ **⑤が今使っている `timeOffset = -1f` は正しい。** ただし `timeDelta` に `Time.deltaTime` を
渡しているのは**バニラと違う**。`m_simulationTimeDelta` にすると一時停止・速度変更に追随し、
`ParticleEffect.Update()` が `m_useSimulationTime` で `ParticleSystem` を `Pause()` する挙動とも一致する。

### B-4. 粒子数の式（点／円盤） — **CONFIRMED**

`ParticleEffect.EmitParticles(id, point, direction, radius, halfHeight, velocity, pps, probability, ref min, ref max)`:

```
Randomizer r = new Randomizer(id.RawData);
pps *= m_particleSystem.emission.rateOverTime.constant;        // ★ プレハブ側の rate が乗る
if (r.Int32(100) > probability) return;                        // 1 回だけの判定（§B-6）

perp1 = GetPerpedicular(direction) = (-dir.y, dir.x, 0)        // ※ 正規化されない
perp2 = Vector3.Cross(direction, perp1)
count = FloorToInt( Mathf.Max(100f, PI*radius*radius) * pps + Random.value )

各粒子:
  th = Random.value * 2PI ;  u = sqrt(Random.value)
  position  = point + perp1*cos(th)*u*radius + perp2*sin(th)*u*radius
                    + direction * halfHeight * Random.value
  startSize     = m_particleSystem.main.startSize.constant
  startLifetime = lerp(m_minLifeTime, m_maxLifeTime, Random.value)
  startColor    = m_particleSystem.main.startColor.color        // ★ 呼び出し側から変えられない
  speed = lerp(m_minStartSpeed, m_maxStartSpeed, Random.value)
  a     = lerp(m_minSpawnAngle, m_maxSpawnAngle, Random.value) * Deg2Rad
  velocity = argVelocity + (direction*cos(a) + perp1*cos(th)*sin(a) + perp2*sin(th)*sin(a)) * speed
  m_particleSystem.Emit(emitParams, 1)
```

そして `EmitParticles(id, area, …)` の入口には**自動絞り込み**がある:

```
fill = Clamp01(m_particleSystem.particleCount / max(1, main.maxParticles))
pps *= (1f - fill*fill)          // 満杯に近づくと 0 に漸近する
```

→ 撃ち過ぎても `maxParticles` で頭打ちになり、既存の粒子を食い潰さない。

**まとめると密度の乗数は 4 つある:**
`magnitude` × `timeDelta` × `0.01` × `emission.rateOverTime.constant`、
面積は `max(100, π·(area.m_halfWidth + m_extraRadius)²)`、
向きは `direction`、広がりは `m_min/maxSpawnAngle`、初速は `m_min/maxStartSpeed`、
寿命は `m_min/maxLifeTime`、**色と粒径は `ParticleSystem` 側**（＝共有。§D）。

`m_extraRadius` に注意: `Large Pool Steam` は 9m、`Dust Marker 01` は 2m を**勝手に足す**。
半径 0 を渡しても粒子は湧く。

### B-5. ベジェ帯（火砕流に一番近い形） — **CONFIRMED**

`EmitParticles(id, bezier, halfWidth, halfHeight, velocity, pps, probability, ref min, ref max)`:

```
pps *= emission.rateOverTime.constant
32 分割で t = i/32 を走査。各区間ごとに:
   pos  = bezier.Position(t) ;  tan = bezier.Tangent(t)
   side = normalize(tan.z, 0, -tan.x) * halfHeight        ← 帯の横方向。第 3 引数が「幅」
   if (r.Int32(100) > probability) この区間は飛ばす        ← 区間ごとに判定
   count = FloorToInt( halfHeight*2 * Distance(前の点, pos) * pps + Random.value )
   位置は「前の区間の端 → 今の区間の端」を線形補間したどこか（帯を埋める）
   velocity = argVelocity + Vector3(0, speed, 0)           ★ 上向き成分だけが speed で入る
```

> `halfWidth`（第 3 引数）は使われず、**`halfHeight`（第 4 引数）が帯の半幅**として働く。
> 引数名と実装がずれているので、`new SpawnArea(bezier, w, w)` のように両方に同じ値を入れるのが安全。

### B-6. `default(InstanceID)` の罠 — **CONFIRMED**

`EmitParticles` は毎回 `new Randomizer(id.RawData)` を作る。`default(InstanceID)` の `RawData` は 0 で、
`ColossalManaged.dll` を実際にロードして測ると:

```
new Randomizer(0u).Int32(100u) の 1 発目 = 7   （以降 10, 60, 40, 38, 56 …）
```

**1 発目しか使われない**（`EmitParticles` は毎フレーム新しい `Randomizer` を作る）ので、
`id = default` のときの確率判定は**毎フレーム必ず `7 > probability`** になる。

- `ParticleEffect` 経由: `probability = 100` → `7 > 100` は偽 → **常に出る**。実害なし。
- **`FireEffect` 経由: `probability = RoundToInt(magnitude * 100)`** → `magnitude ≥ 0.07` なら常に出て、
  `magnitude < 0.07` なら永遠に出ない。**間に階調が無い。**

`FireEffect.RenderEffect` の IL:

```
IL_002C  probability = Mathf.RoundToInt(magnitude * 100f)        ← magnitude は「確率」
IL_004B  particlesPerSquare = timeDelta * 0.01f                  ← magnitude が入っていない！
IL_0054  m_particleEffect.EmitParticles(id, area, velocity, pps, probability, ref min, ref max)
IL_0088  m_lightEffect が非 null なら、放出した粒子の bbox 中心に DrawLight
         （Randomizer は m_referenceFrameIndex >> 2 で別に作る）
★ m_soundEffect への参照は全 IL に 1 件も無い（§H-17 の再確認）
★ timeOffset は 1 度も読まれない（＝ FireEffect に継続/一発の区別は無い）
```

→ **⑤の現行コードの `magnitude = 0.25f + 0.75f * unit` は、噴煙の濃さに何の影響も与えていない。**
`0.25 ≥ 0.07` なので常に「出る」に張り付き、密度は `timeDelta * 0.01 * 200`（`Fire Particles` の rate）で固定。
噴火の強弱を出したいなら `m_fireEffect` ではなく **`m_fireEffect.m_particleEffect`（= `Fire Particles`）を
直接 `RenderEffect` する**か、`SpawnArea` の半径で面積を変える。

### B-7. その他の型 — **CONFIRMED**

```
MultiEffect.RenderEffect:
  t = 0
  if (m_duration != 0) t = frac((m_useSimulationTime ? SimulationManager.m_simulationTimer
                                                     : Time.time) / m_duration) * m_duration
  foreach (SubEffect s in m_effects)
      if (s.m_effect != null && s.m_startTime <= t && s.m_endTime >= t)
          （m_probability < 1 なら m_fixedRandom ? Randomizer(id.Index>>1) : Random.value で抽選）
          s.m_effect.RenderEffect(引数をそのまま全部横流し)
  RenderDuration() = 子の RenderDuration の最大値
  RenderDistance() / GroupLayer() も子の集約

MovementParticleEffect.RenderEffect:
  magnitude = Min(magnitude,
                  m_minMagnitude + velocity.magnitude * m_magnitudeSpeedMultiplier
                                 + acceleration      * m_magnitudeAccelerationMultiplier)
  pps = timeDelta * magnitude * 0.01f
  ★ timeOffset / m_intensityCurve / m_renderDuration を一切見ない
  ★ acceleration 引数が効くのはこの型だけ

LightEffect.RenderEffect:
  RenderManager.instance.lightSystem.DrawLight(...) を呼ぶだけ。粒子は出ない。
  m_variationColors / m_blinkType / m_intensityCurve / m_renderDuration を持つ。

SoundEffect / EngineSoundEffect:
  RenderEffect を override していない ＝ 基底の ret。呼んでも無音・無害。
```

参考: 既に確認済みの構造（`Medium Explosion Effect` などは `MultiEffect`）

```
Fire Effect            [FireEffect]  -> Fire Particles / Fire Light / Fire Sound
Fire Effect Small      [FireEffect]  -> Fire Particles / Fire Light / Fire Sound Small
Collapse Effect        [MultiEffect] -> Collapse Particles(1.0,0,0) / Collapse Sound(1.0,0,0)   m_duration=0
Medium Explosion Effect[MultiEffect] -> Medium Explosion Particles / Medium Explosion Light      m_duration=0
Large / Huge Explosion Effect (ND)   -> 粒子 2 種 + 光
Meteor Effect (ND)                   -> Meteor Particles / Meteor Light
```

### B-8. スレッドと `CameraInfo` — **CONFIRMED**

- `RenderEffect` の本来の呼び出し元は `EffectManager.EndRenderingImpl`（描画パス）。
  `ParticleSystem.Emit` は main スレッド専用なので、**sim スレッドから直接 `RenderEffect` を呼んではいけない**。
- MOD の `IThreadingExtension.OnUpdate` / `OnAfterSimulationTick` のうち、**`OnUpdate` は main スレッド**なので
  そこから毎フレーム呼んでよい（⑤の現行実装と③の火災旋風が既にこれ）。
- `cameraInfo` に `null` は不可（`ParticleEffect.RenderEffect` の先頭で `CheckRenderDistance` / `Intersect` を
  呼ぶ＝ NRE。§H-18）。`Singleton<RenderManager>.instance.CurrentCameraInfo` を使う。
- 本物の `InstanceID` は不要。`m_buildingFlagRequired` が立っている効果でも、
  `GetBuilding(id) == 0` なら旗の検査を飛ばす（`IL_0049 brfalse IL_0076`）。
  ただし §B-6 の理由で、`id.Disaster = 災害ID` のように**何か非 0 を入れておく方が良い**
  （`FireEffect` を使う場合は必須、`ParticleEffect` 直呼びなら任意）。
- `InstanceManager.Group` は `RenderEffect` の経路に**一切登場しない**。

---

## 5. §C `DispatchEffect` — **CONFIRMED**

```
public void EffectManager.DispatchEffect(EffectInfo effect, InstanceID instance, SpawnArea spawnArea,
                                         Vector3 velocity, float acceleration, float magnitude,
                                         AudioManager.AudioGroup audioGroup,
                                         [uint startFrame], [bool avoidMultipleAudio])
    e.m_effectInfo = effect ; e.m_instance = instance ; e.m_spawnArea = spawnArea
    e.m_velocity = velocity ; e.m_acceleration = acceleration ; e.m_magnitude = magnitude
    e.m_renderDuration = effect.RenderDuration()      ★ 持続はここで決まる
    e.m_timeOffset = 0f                               ★ 0 固定。継続モードには入れない
    e.m_audioGroup = audioGroup ; e.m_startFrame = startFrame
    if (effect.RequireRender()) lock(m_renderEventBuffer) m_renderEventBuffer.Add(e)
    if (effect.RequirePlay())   lock(m_audioEventBuffer)  m_audioEventBuffer.Add(e)
```

- **スレッド安全。** `Monitor.TryEnter(buffer, SimulationManager.SYNCHRONIZE_TIMEOUT)` のリトライループ
  なので sim スレッドから呼んでよい。`RenderEffect` 自体は描画スレッドでバニラが呼ぶ。
- **`m_timeOffset = 0` 固定** ＝ §B-3 の「継続モード（負の timeOffset）」には**絶対に入らない**。
  `m_renderDuration == 0` の効果（`Fire Particles`, `Collapse Particles`, `Factory Smoke` …）は
  **1 フレームだけ**出て消える。持続させたいなら `RenderEffect` を毎フレーム自分で呼ぶしかない。
- `m_renderDuration != 0` の効果（`Medium Explosion Particles` は 1.0s、ND の爆発は 2.0s）は
  1 回 `DispatchEffect` するだけで `m_intensityCurve` に沿って自然に減衰して消える。**一発ものはこちらが正解。**
- `startFrame` で `SimulationManager.m_referenceFrameIndex` まで発火を遅らせられる（連続爆発の時間差に使える）。
- `magnitude` は**密度**（§B-4）で、火災旋風設計 付録 A の記述どおり。**大きさは `SpawnArea` 半径**。
  ただし付録 A の式は `rateOverTime` の乗算が抜けていたので、本書 §B-4 の式で置き換える。
- `avoidMultipleAudio` は音側のみ（同一フレームで 2 個目以降の音を捨てる）。

### `EffectsWrapper` 側の `DispatchEffect` は使わない

`ICities` 向けの `EffectsWrapper.DispatchEffect(object, ref UserEffectParameters)` は

```
IL_0050  new SpawnArea(position, Vector3.up, 1f)      ★ 半径 1m 固定、向きも上固定
IL_007D  AudioManager.instance.DefaultGroup
```

と**半径 1m に潰される**。MOD からは `EffectManager.instance.DispatchEffect` を直接呼ぶこと。

---

## 6. §D 実行時クローン — **CONFIRMED**

### D-1. 内部フィールドは全部 `[NonSerialized]`

```
ParticleEffect:
  [NonSerialized] GameObject      m_effectObject
  [NonSerialized] ParticleEffect  m_effectComponent
  [NonSerialized] ParticleSystem  m_particleSystem
  [NonSerialized] ParticleSystem[] m_particleSystems
LightEffect: m_groupLayer / m_lightType / m_lightColor … も全て [NonSerialized]
EffectInfo:  m_refCount は private（[SerializeField] 無し）
```

→ `Object.Instantiate(vanilla.gameObject)` で作ったクローンの `ParticleEffect` は
`m_effectObject == null` / `m_refCount == 0` の**未初期化状態**で始まる。
元のプレハブと `ParticleSystem` を共有してしまう事故は起きない。

### D-2. 初期化の中身

```
EffectInfo.InitializeEffect()   { if (m_refCount++ == 0) CreateEffect(); }
EffectInfo.ReleaseEffect()      { if (--m_refCount == 0) DestroyEffect(); }

ParticleEffect.CreateEffect():
  if (m_effectObject == null) {
      Transform root = GetEffectRoot();                       // GameObject.Find("Particle Effects")
                                                              // 無ければ new + DontDestroyOnLoad
      GameObject clone = Object.Instantiate(this.gameObject);
      clone.transform.parent = root;
      clone.name = this.gameObject.name;
      m_effectObject     = clone;
      m_effectComponent  = clone.GetComponent<ParticleEffect>();
      m_particleSystem   = clone.GetComponent<ParticleSystem>();
      m_particleSystem.emission.enabled = false;              // ★ 自動放出は止める（Emit だけ使う）
      m_particleSystems  = clone.GetComponentsInChildren<ParticleSystem>();
      m_effectComponent.m_particleSystem  = m_particleSystem; // クローン側にも同じ参照を入れる
      m_effectComponent.m_particleSystems = m_particleSystems;
  }

ParticleEffect.DestroyEffect():
  m_particleSystem = null; m_particleSystems = null;
  if (m_effectObject != null) { Object.Destroy(m_effectObject); m_effectObject = null; }
```

> `emission.enabled = false` にしても **`emission.rateOverTime.constant` は読まれ続ける**（§B-4）。
> 自作 `ParticleSystem` で rate を 0 にすると**粒子が 1 個も出ない**。ここが一番踏みやすい罠。

### D-3. 「マテリアルを借りると見えない」の線引き — **CONFIRMED**

火災旋風設計 §4.9 が言う「CS のマテリアルを借りると描画されない」は、
**`Graphics.DrawMesh` / 自前 `MeshRenderer` に CS の建物・車両マテリアルを差した場合**の話である
（エンジンが供給する per-instance の `MaterialPropertyBlock` を要求するため。
`VortexAI.RenderExtraStuff` が `ID_TyreMatrix` / `ID_Color` を毎回詰めている実例が §C-1 にある）。

**粒子マテリアルは別である。** バニラ自身が
`EffectsWrapper.CreateParticleEffect` で「借りたマテリアルを `ParticleSystemRenderer` に差す」ことを
やっている:

```
EffectsWrapper.CreateParticleEffect(string materialName, ParticleSystem ps, ref UserParticleSettings s):
  if (materialName != "" && m_BuiltinParticleMaterials.TryGetValue(materialName, out mat)) {
      var r = ps.gameObject.GetComponent<ParticleSystemRenderer>()
              ?? ps.gameObject.AddComponent<ParticleSystemRenderer>();
      r.renderMode = ParticleSystemRenderMode.Billboard;   // ldc.i4.0
      r.material   = mat;                                  // ★ 借りたマテリアルをそのまま差す
      r.alignment  = ParticleSystemRenderSpace.View;       // ldc.i4.0
  }
  ParticleEffect pe = ps.gameObject.AddComponent<ParticleEffect>();
  pe.m_maxVisibilityDistance = s.maxVisibilityDistance;
  pe.m_minLifeTime = s.minLifetime;   pe.m_maxLifeTime  = s.maxLifetime;
  pe.m_minStartSpeed = s.minStartSpeed; pe.m_maxStartSpeed = s.maxStartSpeed;
  pe.m_minSpawnAngle = s.minSpawnAngle; pe.m_maxSpawnAngle = s.maxSpawnAngle;
  pe.m_renderDuration = s.renderDuration;
  pe.InitializeEffect();
  return pe;
```

→ **粒子マテリアルは per-instance データを要求しない。** `Shader.Find` は一切出てこない。
`m_BuiltinParticleMaterials` にある `Fire` / `Smoke` / `Steam` / `Placement Dust` / `Explosion` /
`IndustryDust` / `Water` / `OreDebris` / `SandDebris` / `Snow` は**そのまま差せる**。

### D-4. 推奨する 2 つのクローン手順

**(a) 既存効果を丸ごと複製して数値だけ変える（最小・最も安全）**

```csharp
var src = EffectCollection.FindEffect("Factory Smoke");           // または GetBuiltinEffect(...)
var go  = UnityEngine.Object.Instantiate(src.gameObject);
go.name = "DisasterPlus_AshPlume";
UnityEngine.Object.DontDestroyOnLoad(go);
var pe  = go.GetComponent<ParticleEffect>();

pe.m_minLifeTime = 6f;  pe.m_maxLifeTime = 14f;                   // 高く長く昇らせる
pe.m_minStartSpeed = 25f; pe.m_maxStartSpeed = 45f;
pe.m_minSpawnAngle = 0f;  pe.m_maxSpawnAngle = 12f;
pe.m_maxVisibilityDistance = 10000f;                              // 遠景から見える
pe.m_renderDuration = 0f;                                         // 継続モードで使う

var ps   = go.GetComponent<ParticleSystem>();                     // ← クローン側の PS
var main = ps.main;
main.startColor  = new Color(0.20f, 0.18f, 0.17f, 1f);            // 灰色の噴煙
main.startSize   = 30f;
main.gravityModifier = -0.05f;
main.maxParticles = 4000;
var em = ps.emission; em.rateOverTime = 40f;                      // ★ 0 にしない

pe.InitializeEffect();                                            // ここで実体（子クローン）が出来る
// 都市を降りるときに pe.ReleaseEffect() と Object.Destroy(go)
```

**(b) `EffectsWrapper` に作ってもらう（マテリアルだけ借りる）**

```csharp
var w  = Singleton<EffectManager>.instance.m_EffectsWrapper;
var go = new GameObject("DisasterPlus_Pyroclast");
var ps = go.AddComponent<ParticleSystem>();
// ... ps の main/colorOverLifetime/sizeOverLifetime を好きに組む ...
var settings = new UserParticleSettings { /* lifetime, speed, spawnAngle, renderDuration, vis */ };
var pe = (ParticleEffect)w.CreateParticleEffect("Smoke", ps, ref settings);   // 名前でマテリアルを借りる
```

(b) は `ICities` の `UserParticleSettings` 構造体に縛られる（色・粒径・重力は自分で `ps` に設定する）。
**(a) の方が調整項目が多く、依存も少ない。**

### D-5. クローンしない場合の副作用

`ParticleSystem.main.startColor` / `startSize` / `maxParticles` / `emission.rateOverTime` は
**その効果プレハブの共有状態**である。`Fire Particles` の `startColor` を直接書き換えると
**街じゅうの建物火災の色が変わり、しかもセーブではなくメモリ上に残る**。
色・粒径・重力を変えるなら必ずクローンする。
`m_minLifeTime` 等の `ParticleEffect` 側フィールドも同じく共有。

---

## 7. §E コスト — **CONFIRMED**

**ヒープ確保**

| 呼び出し | 確保 |
|---|---|
| `EffectInfo.RenderEffect` → `EmitParticles` | **無し。** `EmitParams` / `Randomizer` / `Vector3` / `ParticleSystem.MainModule` / `EmissionModule` は全て struct。`ParticleSystem.Emit(EmitParams,int)` も確保しない |
| `new EffectInfo.SpawnArea(...)` | **無し**（struct）。ただし約 150 byte を値渡しでコピーする |
| `EffectManager.DispatchEffect` | `FastList<SimulationEvent>.Add` が容量を超えたときだけ配列を確保（償却） |
| `EffectCollection.FindEffect` | **無し**（`Dictionary.TryGetValue`） |
| `EffectsWrapper.GetEffectList()` | **配列を確保する**（`Keys.ToArray()`）。診断のときだけ |
| `Resources.FindObjectsOfTypeAll<EffectInfo>()` | 大きな配列 + ネイティブ走査。**起動時のみ** |
| `RenderManager.CurrentCameraInfo` | 単なる `ldfld` |

**CPU**

1 回の `RenderEffect` の主コストは `count = max(100, π·r²) × pps` 回の `ParticleSystem.Emit` ループ。

```
例: r = 60m, magnitude = 1, timeDelta = 0.0167, rate = 150（Medium Explosion 相当）
    pps   = 0.0167 * 1 * 0.01 * 150 = 0.025
    count = max(100, 11310) * 0.025 = 283 粒子/フレーム
例: r = 25m, magnitude = 1, timeDelta = 0.0167, rate = 15（Factory Smoke）
    pps   = 0.0167 * 1 * 0.01 * 15 = 0.0025
    count = max(100, 1963) * 0.0025 = 5 粒子/フレーム   ← 明らかに足りない
```

→ **`Factory Smoke` を噴煙に使うなら `magnitude` を 20〜80 まで上げる**（`magnitude ≤ 1` の帯は
建物火災の `FireEffect` の慣習で、`ParticleEffect` 直呼びには当てはまらない。
`SinkholeAI` も 2.0 まで上げている）。

**自動絞り込み**が `maxParticles` に対して働く（§B-4）ので、上限は
`main.maxParticles`（既定 10000、`Medium Explosion` は 2000）で決まる。
複数の火山や火砕流が同じ効果インスタンスを共有すると**互いに粒子予算を奪い合う**。
独立に制御したいならクローンを分ける。

**距離カリング**は無料で効く: `CheckRenderDistance(pos, m_maxVisibilityDistance)` と
`Intersect(pos, 100f)` が先頭にあるので、画面外・遠方では `Emit` ループに入らない。
ただし `m_maxVisibilityDistance` は `Factory Smoke` 系で **1000m しかない**。
火山の噴煙は遠景から見えてほしいのでクローンして 10000 にする（爆発系は元から 10000）。

---

## 8. §F 設計への意味

### ① 噴煙・火山灰の柱 — **`Factory Smoke` をクローンして使う**

- 素材: **`Factory Smoke`**（基本ゲーム、`sharedassets11`、マテリアル `Smoke` /
  `Custom/Particles/Alpha Blended`）。初速 10–15、放出角 0–5° の**細い上昇ジェット**で、
  柱状のプルームそのもの。`Factory Steam` はより白く太い（粒径 11）。
- 取得: `EffectCollection.FindEffect("Factory Smoke")` は **null**（未登録）。
  `Singleton<EffectManager>.instance.m_EffectsWrapper.GetBuiltinEffect("Factory Smoke")` を使う。
  取れなければ `BuildingManager.instance.m_properties.m_fireEffect.m_particleEffect`（= `Fire Particles`）へ退避。
- クローンして: `m_maxVisibilityDistance = 10000`, 寿命 6–14s, 初速 25–45, 角 0–12°,
  `main.startColor` を灰黒、`startSize` 25–40、`gravityModifier` を僅かに負（浮上）。
- 呼び方（main スレッド、毎フレーム）:

```csharp
InstanceID id = default(InstanceID); id.Disaster = volcanoDisasterId;   // 0 以外にしておく
var area = new EffectInfo.SpawnArea(craterTop, Vector3.up, craterRadius * 0.5f);
ashPlume.RenderEffect(id, area, windVelocity, 0f,
                      magnitude,                    // 20〜80 の帯（§E）
                      -1f,                          // 継続モード
                      Singleton<SimulationManager>.instance.m_simulationTimeDelta,
                      Singleton<RenderManager>.instance.CurrentCameraInfo);
```

  `velocity` に風ベクトルを入れると柱が風下へ倒れる。**自前メッシュのプルームは削除できる。**

### ② 火口とラバの炎 — **`Fire Particles` を直接呼ぶ（`Fire Effect` 経由をやめる）**

- 素材: **`Fire Particles`**（基本ゲーム、`sharedassets12`、マテリアル `Fire` /
  `Custom/Particles/Additive (Soft)`、rate 200、gravity −0.6）。
- 取得: `BuildingManager.instance.m_properties.m_fireEffect.m_particleEffect`（フィールドで直に届く。
  名前引きも `GetBuiltinEffect("Fire Particles")` で可）。
- **現行の `m_fireEffect.RenderEffect(...)` はやめる。** §B-6 のとおり `FireEffect` では
  `magnitude` が確率にしかならず、`default(InstanceID)` と組み合わさって階調が消えている。
  光が欲しければ `m_fireEffect.m_lightEffect` を別途 `RenderEffect` すればよい（音は出ない）。
- 溶岩流に沿って燃やすには **ベジェ帯**を使う（`Fire Particles` は `m_canUseBezier = true`）:

```csharp
var b    = new Bezier3(lavaA, lavaB, lavaC, lavaD);
var area = new EffectInfo.SpawnArea(b, flowHalfWidth, flowHalfWidth);   // §B-5: 第4引数が半幅
fireParticles.RenderEffect(id, area, Vector3.zero, 0f, magnitude, -1f, dt, cam);
```

### ③ 噴石（ejecta） — **`Medium Explosion Particles` を `DispatchEffect` で単発**

- 素材: **`Medium Explosion Particles`**（基本ゲーム、`sharedassets12`、`Explosion` /
  `Custom/Particles/Additive (Soft)`）。**初速 100–150 m/s、放出角 0–80°、寿命 1–1.5s、
  `m_renderDuration = 1.0` に線形減衰カーブ、`m_maxVisibilityDistance = 10000`、粒径 40–60。**
  これは「火口から弧を描いて飛ぶ光る破片」そのもので、**設計が探していた形と一致する**。
- 取得: `DisasterManager.instance.m_properties.m_mediumExplosion` は `MultiEffect`
  （`Medium Explosion Particles` + `Medium Explosion Light`）。粒子だけ欲しければ
  `GetBuiltinEffect("Medium Explosion Particles")`。光ごと出すなら `m_mediumExplosion` を丸ごと。
- **一発ものなので `DispatchEffect` が正解**（`m_renderDuration != 0` なので 1 回積めば
  1 秒かけて減衰して消える。§C）:

```csharp
Singleton<EffectManager>.instance.DispatchEffect(
    Singleton<DisasterManager>.instance.m_properties.m_mediumExplosion,
    id,
    new EffectInfo.SpawnArea(ventPos, Vector3.up, ventRadius * 0.3f),
    Vector3.zero, 0f,
    magnitude,                                        // 密度。0.5〜4 で調整
    Singleton<AudioManager>.instance.EffectGroup,
    startFrame);                                      // 噴出のタイミングをずらせる
```

  sim スレッドから呼んでよい（`DispatchEffect` は lock 付きのキュー投入）。
  重力は `main.gravityModifier = -1.0`（＝上向き）なので、**放物線にしたいならクローンして
  `gravityModifier` を +1〜+3 に、`startSize` を 3〜8 に落とす**（60 は爆炎用の巨大サイズ）。
  ND の `Huge/Large Explosion Particles`（初速 100–150、角 80–90°＝水平の衝撃波）は
  見た目が更に良いが **DLC 必須なので既定経路にはできない**（§A-5）。

### ④ 火砕流 — **完全一致は ABSENT。`Collapse Particles` のベジェ帯が最も近い**

**バニラに火砕流は無い。** 「地面を這って高速で流れ下る濃密な雲」に相当する `EffectInfo` は
基本ゲームにも ND にも存在しない（277 個を全数確認）。近いものを 3 つ挙げる:

| 候補 | 由来 | 良い点 | 悪い点 |
|---|---|---|---|
| **`Collapse Particles`** | 基本 | 放出角 **50–80°**（＝ほぼ横に広がる）、初速 4–8、gravity −0.2 で立ち上る土煙、粒径 12、**ベジェ可**。建物崩壊の粉塵そのもので、色も灰色 | 速度が遅い。`velocity` 引数で押す必要がある |
| `Factory Smoke` | 基本 | 粒子が多く量感が出る | 角 0–5° の縦ジェットなので、横倒しにするには `direction` を水平にして `m_maxSpawnAngle` を上げる改造が要る（＝クローン必須） |
| `Dust Marker 01` | 基本 | gravity **+0.3**（沈む）、`m_extraRadius = 2`、`IndustryDust` の茶灰色 | 初速 0.01–0.2 でほぼ静止。移動は `velocity` 引数頼み |

**推奨: `Collapse Particles` をクローンし、ベジェ帯 + `velocity` で流す。**

```csharp
// 火口 -> 下り斜面に沿った 4 点（§H-19 の (-slopeX, -slopeZ) で下り方向を取る）
var b    = new Bezier3(p0, p1, p2, p3);
var area = new EffectInfo.SpawnArea(b, flowHalfWidth, flowHalfWidth);
pyroclast.RenderEffect(id, area,
                       downSlopeDir * flowSpeed,   // ★ 這わせるのはこの velocity
                       0f, magnitude, -1f, dt, cam);
```

  クローン側で `m_minSpawnAngle/m_maxSpawnAngle = 70/95`（ほぼ水平〜やや下向き）、
  `m_minStartSpeed/m_maxStartSpeed = 2/6`（ベジェ経路では**上向き成分にしか入らない**ので小さく）、
  寿命 4–9s、`main.startColor` を暗い灰、`startSize` 20–35、`gravityModifier` を +0.1（沈降）。
  帯の制御点を毎フレーム前進させれば「流れ下る」。
  **正直に言えばこれは「土煙の帯」であって火砕流ではない。** それでも自前ポリゴンより桁違いに良く、
  持ち主の指摘（「ポリゴンではなくバニラのエフェクトを参考に」）には正面から応えている。

### ⑤ 建物崩壊の粉塵 — **`Collapse Effect` をそのまま `DispatchEffect`**

- 素材: **`BuildingManager.instance.m_properties.m_collapseEffect`**（`MultiEffect`、
  `Collapse Particles` + `Collapse Sound`）。**音まで付いてくる**（`MultiEffect.PlayEffect` があるので
  `DispatchEffect` に `AudioManager.EffectGroup` を渡せば鳴る）。
- 水没時は `m_collapseFloodedEffect`。
- `m_renderDuration = 0` なので `DispatchEffect` では**1 フレームだけ**。バニラの `SinkholeAI` は
  そのため毎フレーム `RenderEffect(-1f, …)` している。崩壊は一瞬でよければ `DispatchEffect`、
  数秒立ち上らせたいなら `RenderEffect` を自前で回す。

### ⑥ 台風の吹き付ける飛沫 — **`Fire Copter Water Particles`（または `Fireman Water`）**

- 素材: **`Fire Copter Water Particles`**（基本、マテリアル `Water` / `Custom/Particles/Alpha Blended`、
  粒径 10、rate 35、gravity **+1.0** ＝落ちる）。`Fireman Water` は粒径 3 の細かい版。
  `EffectCollection.FindEffect("Fire Copter Water Particles")` で**登録済みなので名前で引ける**。
- 白い飛沫の帯が欲しいなら `Ship Trail` / `Boat Trail`（`Custom/Particles/Alpha Tested`、粒径 10–15）
  も候補だが、`ShipTrailParticleEffect` は `MovementParticleEffect` 派生で
  **`magnitude` が `velocity.magnitude` に上書きされる**（§B-7）ため制御が効きにくい。
- **どちらも `m_canUseMeshData / m_canUsePositions / m_canUseBezier` が全て `false`** ＝ ベジェ帯は使えず、
  必ず点/円盤経路になる（**CONFIRMED**）。海岸線に沿わせたいなら円盤を複数箇所に並べる。
  `m_maxSpawnAngle` は 1° しかないので、横殴りにするならクローンして 60–90° に上げ、
  `direction` を風向きの水平ベクトル、`velocity` に風速を入れる。
  `Fireman Water` の可視距離は 500m しかないので、これもクローンで上げる。

### 実機で 1 度だけ取るべき数字（PARTIAL の解消）

```csharp
var w = Singleton<EffectManager>.instance.m_EffectsWrapper;
Log.Info("effects=" + w.m_BuiltinEffects.Count
       + " particleMaterials=" + w.m_BuiltinParticleMaterials.Count
       + " collectionRegistered=" + System.Linq.Enumerable.Count(EffectCollection.Effects));
foreach (var n in new[]{"Factory Smoke","Fire Particles","Medium Explosion Particles",
                        "Collapse Particles","Fire Copter Water Particles"})
    Log.Info(n + " -> " + (w.GetBuiltinEffect(n) != null ? "ok" : "MISSING"));
```

期待値: `effects` ≈ 234（ND 所持で ≈ 245）、`collectionRegistered` = 186、
`particleMaterials` ≈ 20。5 つの名前は全て `ok`。
**これが返ってくるまで、本書の在庫は PARTIAL のままである。**

---

## 9. 付録: 実測に使った手順（再現用）

```powershell
# IL
. docs\tools\ilload.ps1 ; . docs\tools\ildasm.ps1
Disasm-Method -Method ([EffectManager].GetMethod('DispatchEffect', ...))
# ※ 日本語を含むパスから dot-source すると PowerShell 5.1 が BOM 無し UTF-8 を ANSI と読んで壊れる。
#   ツールを ASCII パスへコピーしてから読むこと。
```

```python
# アセット（UnityPy 1.25）
env = UnityPy.load(r"...\Cities_Data\level11")          # SunnyPrefabs
# MonoBehaviour の型は m_Script -> MonoScript.m_ClassName で判る（typetree 不要）
# EffectCollection.m_effects は生バイトを手で解く:
#   PPtr m_GameObject(12) + m_Enabled(1,4整列) + PPtr m_Script(12) + string m_Name
#   -> int count -> count * PPtr{int fileID, long pathID}
#   fileID は assets_file.externals[fileID-1].path で解決
# ParticleEffect の serialize 順は DLL の宣言順と一致し、全件 bytes_left == 0 で検算できた
```

走査対象: `globalgamemanagers`（`BuildSettings.scenes` 137 本）、`level0`–`level136`、
`sharedassets0`–`sharedassets136.assets`、`resources.assets`。
