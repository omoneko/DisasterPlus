# Disaster + — ⑤火山「水を赤くして溶岩にする」案 IL 実測ファクト

- 日付: 2026-08-20
- 対象: Cities: Skylines 1 / `Assembly-CSharp.dll`（Steam 版 `Cities_Data/Managed`）＋ `ColossalManaged.dll`
  ＋ **同梱アセット**（`resources.assets` / `sharedassets7.assets` の `Shader` オブジェクトを UnityPy で展開し、
  GLSL に逆展開済みのフラグメントプログラムを直読）
- 目的: 依頼「**火山については水を赤く着色して光らせて流すことで溶岩っぽくできませんか？水源が噴火口に合わせてせりあがってくる感じで。**」
  が、バニラの実装上どこまで成立するかを確定させる
- 状態: **調査のみ**。`src/` / `tests/` / `Locales/` は一切変更していない
- 前提資料（**既出の事実は再導出せず引用する**）:
  - `2026-08-15-typhoon-il-facts.md` §D-1（`GenericFloodAI` は空）/ §D-2（`WaterSimulation` の全体像・定数）/
    §D-3（`TYPE_TSUNAMI` はマップ外周リングでしか評価されない）/ **§D-4（`WaterSource` の全挙動・`LockWaterSource` の罠）**
  - `2026-08-15-volcano-il-facts.md` §A-2（`m_blockHeights` は 2 m / 64 sim フレームでしか動かない）/
    **§A-4（`WaterSimulation.m_heightBuffer` は `TerrainManager.m_blockHeights` そのもの）**/
    §B-5（溶岩の既製ビジュアルは ABSENT・炎は借りられる）/ §B-7（`BurnBuilding` / `BurnGround` / `BurnTree`）/
    §C-10（高さの天井 1023.98 m）/ §H-20（`HasWater` は sim スレッド専用）/ §H-22（実在するシェーダ名）
  - `2026-08-11-disasterplus-phase0-firewhirl-design.md` 付録 A ＋ §4.9（**CS のマテリアルを借りると自前 `MeshRenderer` では見えない**、
    `DispatchEffect` の magnitude は粒子密度であってサイズではない）

判定記号: **CONFIRMED**（IL／アセットを読んだ）/ **PARTIAL**（読んだが確定しきれない。何を見れば決まるかを書く）/ **ABSENT**（存在しない）

> **この調査を始める動機になった前提の訂正について。**
> 「`Shader.Find` が `"Standard"` を含めどの名前でも null を返す」という実機所見（13 個目の外れ）から、
> 「バニラが既に描いている水を借りれば材質を作らずに済む」という発想が出た。
> 本書はその発想を IL とアセットで検証したもので、**結論は「色の局所化はできない。この案は溶岩には使えない」**である。
> ただし *なぜ* 使えないかは「水の色が 1 つのグローバルだから」だけではない。**水であること自体が火山と噛み合わない**（→ §E）。

---

## 0. 要約ファクト表

| # | 問い | 判定 | 一行の答え |
|---|---|---|---|
| A1 | 水の色は誰が持つか | **CONFIRMED** | `TerrainProperties.m_waterColorClean / m_waterColorDirty / m_waterColorUnder`（**public Color**）。`InitializeShaderProperties()` が **`Shader.SetGlobalColor`** で撒く。**マップ全体で 1 組のグローバル**。マップテーマ（`MapThemeMetaData.waterClean/waterDirty/waterUnder`）が起動時に上書きする |
| A2 | 実行時に MOD が書けるか | **CONFIRMED** | 書ける。`TerrainManager.instance.m_properties.m_waterColorDirty = c; …InitializeShaderProperties();` が**バニラのテーマエディタと同一の経路**（`WaterPropertiesPanel.OnWaterColorDirtyChanged` の IL 全文が 8 命令）。`m_properties` も `InitializeShaderProperties` も public。**main スレッド専用**（`Shader.SetGlobalColor` は Unity API） |
| A3 | 描画は patch 単位か。per-patch の色フックはあるか | **ABSENT** | `TerrainPatch.Render` は **全 patch 共通の 1 つの `TerrainManager.m_waterMaterial`（または `m_waterTransparentMaterial`）と 1 つの `m_materialBlock2`** で `Graphics.DrawMesh` する。`SetWaterMaterialProperties` が block に入れるのは**テクスチャ 5 枚とマッピング Vector4 3 本だけで、色は 1 つも入らない** |
| B4 | 位置で見た目が変わるものはあるか | **CONFIRMED（1 つだけある＝汚染）** | `surfaceMapA` の **G チャンネル ＝ セルの汚染率**。水シェーダはこれで `lerp(_WaterColorClean, _WaterColorDirty, t)` する。**汚染だけが唯一の「場所ごとの色」チャンネル** |
| B5 | 深さによる色の変化はあるか | **ABSENT（色ではない）** | `surfaceMapB` の G ＝ 深さ指標だが、シェーダでの用途は **`if (depth - 0.001 < 0) discard;` の描画可否判定のみ**。色相には一切効かない |
| B6 | 汚染→色 の変換式 | **CONFIRMED（GLSL 実読）** | `t0 = clamp((foam*0.02 + pollutionG - 0.02) * 4, 0, 1)` → smoothstep → 微小なアニメ揺らぎ加算 → `albedo = lerp(clean, dirty, t)`。**汚染率が約 25 % を超えた時点で完全に `_WaterColorDirty` に振り切る** |
| B7 | 汚染は局所か。どう動くか | **CONFIRMED** | セル単位（16 m）。**流れに乗って移流する**（`transfer = velocityX * pollution / height` を隣セルへ渡す）。つまり**赤い染みは下流へ流れ、最後は海へ出る** |
| C8 | 発光・エミッシブは届くか | **ABSENT（マテリアルには無い）** | 水フラグメントの最終式は `SV_Target0.xyz = 反射*const + (albedo * ライトバッファ + スペキュラ)`。**自己発光項が 1 つも無い。夜は暗くなる** |
| C9 | 画面側の glow は使えるか | **PARTIAL** | `Hidden/ToneMapping` に**輝度しきい値の glow** が実在する（`smoothstep(_GlowIntensityThreshold.x, .y, tonemapされた輝度)`）。材質に依らず「明るければ光る」。**`_WaterColorDirty` を 1.0 超の HDR 色にすれば昼間はしきい値を超え得る**が、ライト乗算なので**夜は絶対に光らない**。昼に本当に閾値を越えるかは実機でしか決まらない |
| D10 | 内陸任意点の水源は成立するか | **CONFIRMED** | `CreateWaterSource` に**座標の検証は 1 つも無い**。空きスロット再利用 or `FastList.Add`、`m_size >= 65535` で false。マップの川は `WaterTool` が `m_type = TYPE_NATURAL`, `m_inputRate = m_outputRate = capacity*65535`, `m_target = (hit.y + m_height) * 63.99902` で置いたもの |
| D11 | `m_target` をせり上げられるか | **CONFIRMED** | できる。`m_target` は**絶対水位・1/64 m・UInt16（上限 1023.98 m）**。`LockWaterSource` → 書き換え → `UnlockWaterSource` の対で毎 tick 動かせる。**火山の高さ天井（§C-10 の 1023.98 m）と同じ天井**なので足りる |
| D12 | 火口の下の地形を上げたら病的な事は起きるか | **CONFIRMED（クラッシュはしない。ただし挙動が反転する）** | 吐き出し側は `count <= 0` と `outRate <= 0` の**両方にガードがある**（IL_1ECD / IL_1ED8）ので**ゼロ除算しない**。しかし**吸い込み側には地形スキップが無い**ため、`terrain >= m_target` になった瞬間**その水源は半径内の水を全部吸い上げる側に反転する**。→ **`m_inputRate = 0` にしておくこと** |
| E13 | 「水の溶岩」は火を消すか | **CONFIRMED（消す）** | 深い冠水で `Frame.m_constructState` が削られ、倒壊時に **`Building.m_fireIntensity = 0`** が直接書かれる。`BurnBuilding` も冠水中の建物を拒否する（⑤§B-7）。**溶岩が火災を起こす設計（⑤設計含意 6）と真正面から矛盾する** |
| E14 | 建物を水没させるか | **CONFIRMED（する。しかも「洪水」災害が出る）** | `waterLevel > pos.y + max(4, m_collisionHeight)` で `CommonBuildingAI.HandleCommonConsumption` が **`DisasterManager.CreateDisaster` で強度 10 の洪水災害を生成**し `Building.Flags.Flooded` を立てる。**プレイヤーには「洪水」の通知とアイコンが出る** |
| E15 | 取水ポンプが飲むか | **CONFIRMED（飲む）** | `WaterFacilityAI.HandleWaterSource` は `TerrainManager.HasWater` / `GetClosestWaterPos` しか見ない。水源の種類を区別しない。**溶岩を飲み、`Building.m_waterPollution = min(255, 255*pollution/water)` として汚染度まで引き継ぐ** |
| F16 | 局所的に赤くする抜け道はあるか | **PARTIAL（1 本だけある）** | `TerrainMesh.GetLodMesh`（public static）＋ `TerrainPatch.SetWaterMaterialProperties(MaterialPropertyBlock)`（public）＋ **`new Material(m_waterMaterial)`（`Shader.Find` を通らない）** で、**特定 patch にだけ自前の水パスを重ね描き**できる。ただし **粒度は patch ＝ 1920 m 角**、z ファイティング必至、未検証 |

**今回いちばん危なかった思い込み。** 「`m_waterColorDirty` があるのだから、汚染を局所的に上げれば局所的に赤くできる」——
**半分正しく、半分間違い。局所なのは「汚染の量」だけで、「汚染したときの色」はグローバル**である。
赤くすると**市内の下水プルームも、そこから海へ流れ出た汚染も、全部赤くなる。**

---

## A. 水の色は誰のものか

### A-1. 所有者は `TerrainProperties` の 3 本の `Color`、撒き方は `Shader.SetGlobalColor` — CONFIRMED

`TerrainProperties : MonoBehaviour`（`TerrainManager.m_properties` の型。`SimulationManagerBase<T,P>.m_properties` は **public**）の
水まわりフィールド（`Dump-Type` 実測、抜粋）:

```
F m_waterShader              Shader   public
F m_waterTransparentShader   Shader   public
F m_waterNormal              Texture2D public
F m_waterFoam                Texture2D public
F m_waterArrow               Texture2D public
F m_waterColorClean          Color    public      ★
F m_waterColorDirty          Color    public      ★
F m_waterColorUnder          Color    public      ★
F m_waterRainFoam            Single   public
M InitializeShaderProperties () : Void            ★ public
M OverrideFromMapTheme       () : Void
```

`InitializeShaderProperties`（IL 全文のうち水の部分。**`Shader::SetGlobalColor` = グローバル**）:

```
IL_00C0  ldstr "_WaterColorClean"
IL_00C6  m_waterColorClean.r / .g / .b            // ★ a には m_waterRainFoam を詰め替える
IL_00E7  ldfld TerrainProperties::m_waterRainFoam
IL_00EC  newobj Color::.ctor
IL_00F1  call  Shader::SetGlobalColor             // ★ グローバル
IL_00F6  ldstr "_WaterColorDirty"
IL_0101  call  Shader::SetGlobalColor             // ★ グローバル
IL_0106  ldstr "_WaterColorUnder"
IL_0111  call  Shader::SetGlobalColor             // ★ グローバル
```

→ **水の色はマテリアルのプロパティではなく、Unity のグローバル uniform である。**
`_WaterColorClean` の **アルファには `m_waterRainFoam` が詰められている**（雨の泡の強さ）。ここを触ると雨の見た目が変わる。

初期値の供給元は 2 つ:

```
TerrainProperties.OverrideFromMapTheme():
  meta = SimulationManager.instance.m_metaData.m_MapThemeMetaData
  if (meta != null) {
     … grass/ruined/pavement/gravel/cliff/sand/oil/ore/cliffSandNormal の各 Texture と tiling …
     IL_02A1  m_waterColorClean = meta.waterClean       // ★
     IL_02AD  m_waterColorDirty = meta.waterDirty       // ★
     IL_02B9  m_waterColorUnder = meta.waterUnder       // ★
  }
```

→ **色そのものはアセット（マップテーマ）にある。** ただし DLL 側の `TerrainProperties` に読み込まれた後は**ただの public フィールド**なので、
**アセットを書き換える必要は無い**（＝「アセットにあるから触れない」ではない）。

### A-2. 実行時に MOD が書ける。バニラと同じ経路がある — CONFIRMED

マップテーマエディタの `WaterPropertiesPanel` が**そのままお手本**である（IL 全文、8 命令）:

```
WaterPropertiesPanel.OnWaterColorDirtyChanged(_, _, Color value):
IL_0000  call     Singleton`1<TerrainManager>::get_instance
IL_0005  ldfld    SimulationManagerBase`2::m_properties
IL_000A  ldarg.2
IL_000B  stfld    TerrainProperties::m_waterColorDirty
IL_0010  call     Singleton`1<TerrainManager>::get_instance
IL_0015  ldfld    SimulationManagerBase`2::m_properties
IL_001A  callvirt TerrainProperties::InitializeShaderProperties      // public
IL_001F  ret
```

（`OnWaterColorCleanChanged` / `OnWaterColorUnderChanged` も同形。）

| 事実 | 内容 |
|---|---|
| 到達性 | `TerrainManager.instance.m_properties`（public フィールド）→ `m_waterColorDirty`（public）→ `InitializeShaderProperties()`（public）。**リフレクション不要** |
| スレッド | `Shader.SetGlobalColor` は Unity API。**main スレッドのみ**（`OnUpdate` / `RenderOverlay` 側）。sim スレッドから呼んではいけない |
| 副作用 | `InitializeShaderProperties` は水以外に**地形テクスチャ 9 枚と草の色オフセット 4 本と tiling 8 本も撒き直す**。値を変えずに呼ぶ分には冪等だが、**呼ぶたびに全部撒く**ので毎フレーム呼ぶものではない |
| 復元 | 元の `Color` を退避して戻すだけでよい。**セーブには焼き付かない**（`TerrainProperties` は `MonoBehaviour` でセーブ対象外。テーマから毎回再適用される）。ただし**同じセッションでマップテーマが再適用されると（`OnLocaleChanged` などではなく `OverrideFromMapTheme`）上書きされる**ので、退避値は「MOD が触る直前の値」であること |

### A-3. patch ごとの色フックは無い — ABSENT

`TerrainPatch.Render(CameraInfo)`（IL_02A2 以降）:

```
IL_02A2  SetWaterMaterialProperties(TerrainManager.m_materialBlock2)      // ★ 全 patch 共通の block
…
IL_03BE  TerrainManager.TransparentWater ?
IL_03C9      m_waterTransparentMaterial
IL_03D5  :   m_waterMaterial                                              // ★ 全 patch 共通の material
IL_043A  Graphics::DrawMesh(mesh, matrix, material, m_waterLayer,
                            null, 0, m_materialBlock2, false, false)
```

`TerrainPatch.SetWaterMaterialProperties(MaterialPropertyBlock)`（IL 全文の中身。**色は 1 つも無い**）:

```
ID_MainTex      ← m_waterHeight[frame].m_waterHeight   (Texture2D)
ID_SurfaceAOld  ← m_waterSurfaceA[old]                 (Texture2D)
ID_SurfaceANew  ← m_waterSurfaceA[new]                 (Texture2D)
ID_SurfaceBOld  ← m_waterSurfaceB[old]                 (Texture2D)
ID_SurfaceBNew  ← m_waterSurfaceB[new]                 (Texture2D)
ID_TerrainHeight← m_heightMap                          (Texture2D)
ID_HeightMapping   ← m_heightMappingRaw    (Vector4)
ID_TerrainMapping  ← m_heightMappingDetail or Raw (Vector4)
ID_SurfaceMapping  ← m_surfaceMappingRaw   (Vector4)
```

寸法（実測定数）: `TerrainManager.PATCH_RESOLUTION = 9`、`RAW_RESOLUTION = 1080`、`RAW_CELL_SIZE = 16`
→ **1 patch = 120 raw セル = 1920 m 角**。`m_patches : TerrainPatch[]` は public。

→ **「patch ごとに色を変える」フックはバニラには無い。**
（ただし *自分で追加のパスを描く* 抜け道はある。§F-16。）

---

## B. 位置で変わるもの — 汚染だけが色に効く

### B-4. `surfaceMapA` の G ＝ セルの汚染率 — CONFIRMED（`FillSurfaceMap` 全文読了）

`WaterSimulation.FillSurfaceMap(minX, minZ, maxX, maxZ, targetA, targetB, xOffset, zOffset, buffer, waterExists)`
は 2×2 セルを重み 1/2/2/4 で集計し（`Cell::m_height == 0` のセルは無視）、**1 テクセルあたり 3 バイト**を 2 枚に書く:

```
// 集計（IL_0113–064A の 4 回展開、重み 1,2,2,4）
sumH   += h                                  // loc25
depth  += Min(1024, h) * h                   // loc21
poll   += cell.m_pollution                   // loc22   ★
velX   += cell.m_velocityX                   // loc23
velZ   += cell.m_velocityZ                   // loc24

// 正規化（IL_0756–07B3、sumH != 0 のとき）
depth = Clamp(depth / (sumH << 2),   0, 255)
poll  = Clamp(poll  * 255 / sumH,    0, 255)   // ★ 「汚染率」＝ 汚染 / 水量
velX  = Clamp(velX  * 255 / sumH, -127, 127)
velZ  = Clamp(velZ  * 255 / sumH, -127, 127)

// 書き出し（IL_07C5–0814）
targetA[i+0] = nx + 128     targetA[i+1] = poll     targetA[i+2] = nz + 128    // ★ A.g = 汚染
targetB[i+0] = velX + 128   targetB[i+1] = depth    targetB[i+2] = velZ + 128  //   B.g = 深さ
// 水の無いテクセル（IL_081A–0865）: A = (128, 0, 128), B = (128, 0, 128)
```

（`nx` / `nz` は近傍の水面高さから作った法線。`sumH == 0` のときは正規化を飛ばす＝ゼロ除算しない。）

### B-5. 深さは色ではなく「描くか捨てるか」にしか効かない — ABSENT（色としては）

`Custom/Water/Transparent` の GLSL（`resources.assets` の Shader path_id 48 を UnityPy で展開、
`compressedBlob` を lz4 で解凍して得た **GLSL 逆展開ソース**そのもの。DXBC ではなく可読テキストが同梱されている）:

```glsl
u_xlat10_3 = texture(_SurfaceBNew, u_xlat2.xy);
u_xlat10_4 = texture(_SurfaceBOld, u_xlat2.xy);
u_xlat6.xyz = _TerrainStatus.yyy * (New - Old) + Old;     // ← 2 フレーム間の時間補間
u_xlat12.x = u_xlat6.y + -0.00100000005;                  // ← B.g（深さ）
…
if((int(u_xlatb12) * int(0xffffffffu))!=0){discard;}       // ★ 使い道はこれだけ
```

`Custom/Water/Default` も同じ（`u_xlat22 = u_xlat4.y + -0.001;` → `discard`）。
→ **深さで色が変わる仕組みは存在しない。** 深さが効くのは「透明度」（カメラ深度との差）であって色相ではない。

### B-6. 汚染 → 色 の変換式 — CONFIRMED（GLSL 実読）

`Custom/Water/Default`（不透明版）の該当箇所:

```glsl
u_xlat10_3 = texture(_SurfaceAOld, u_xlat12.xy);
u_xlat10_4 = texture(_SurfaceANew, u_xlat12.xy);
u_xlat3.xyz = _TerrainStatus.yyy * (New - Old) + Old;   // A.rgb を時間補間

u_xlat13 = u_xlat5.y * 0.0199999996 + u_xlat3.y;        // foam*0.02 + 汚染          ★
u_xlat13 = u_xlat13 + -0.0199999996;
u_xlat13 = u_xlat13 * 4.0;                              // ★ ×4
u_xlat13 = clamp(u_xlat13, 0.0, 1.0);
u_xlat13 = u_xlat13 * u_xlat13;                         // smoothstep の t²
u_xlat13 = u_xlat15.x * u_xlat13 + (-u_xlat35);         //   × (3-2t) − 揺らぎ
u_xlat13 = u_xlat33 + u_xlat13;                         //   + 揺らぎ（[0,0.2]）
…
u_xlat14.xyz = (-_WaterColorClean.xyz) + _WaterColorDirty.xyz;
u_xlat14.xyz = vec3(u_xlat13) * u_xlat14.xyz + _WaterColorClean.xyz;   // ★ lerp(clean, dirty, t)
```

`Custom/Water/Transparent` も完全に同形（`u_xlat19` が t、`u_xlat2.xyz` が差分）。

| 事実 | 内容 |
|---|---|
| 汚染 0 のとき | `t = clamp((0 + foam*0.02 - 0.02)*4, 0, 1)`。泡が無ければ **t = 0 ＝ 完全に `_WaterColorClean`** |
| 完全に dirty になる汚染率 | `poll/255 >= 0.25` → **汚染バイト 64 以上**（＝ `Cell.m_pollution / Cell.m_height >= 0.25`） |
| 中間の見え方 | smoothstep なので急に切り替わる。加えて `fract(depth*5)` 由来の**時間揺らぎが ±0.2 乗る**（＝汚水がゆらめく） |
| 効く水 | **`_WaterColorDirty` はグローバルなので、マップ上の汚染した水は全部この色になる** |

### B-7. 汚染は流れに乗って移流する — CONFIRMED

`SimulateWater` の流量ループ（X 方向、IL_1140–1198。Z 方向・逆方向にも同形が計 4 回ある）:

```
if (cell.m_pollution != 0 && cell.m_height != 0) {
    transfer = (velocityX * cell.m_pollution + ((cell.m_height - 1) & mask)) / cell.m_height;
    cell.m_pollution      -= transfer;
    neighbour.m_pollution += transfer;
}
cell.m_height -= velocityX;  neighbour.m_height += velocityX;
```

減衰は別に 1 箇所（IL_174B–1785）:

```
if (cell.m_pollution != 0) {
    d = (maxNeighbourHeight * cell.m_pollution + ((cell.m_height - 1) & mask)) / cell.m_height;
    cell.m_pollution -= d;                       // ← 水位が均される過程で薄まる
}
```

→ **汚染は水と一緒に動く。** 火口で赤くした水は**下流の川を赤く染めながら海まで届く**。
下水処理（`WaterSimulation.AddPollutionDisposeRate` / `GetPollutionDisposeRate`）で消える速度が変わる。

---

## C. 光らせられるか

### C-8. 水マテリアルに自己発光項は無い — ABSENT（CONFIRMED）

`Custom/Water/Default` フラグメントの**最終 3 行**:

```glsl
u_xlat1.xyz = u_xlat1.xzw * u_xlat2.xyz;          // albedo(=lerp(clean,dirty)) × ライト
u_xlat2.xyz = u_xlat2.xyz * _SpecColor.xyz;
u_xlat0.xyz = u_xlat0.xxx * u_xlat2.xyz;          // スペキュラ
SV_Target0.xyz = u_xlat1.xyz * u_xlat0.www + u_xlat0.xyz;
SV_Target0.w = 1.0;
```

`Custom/Water/Transparent` の最終:

```glsl
u_xlat10_2 = texture(_LightBuffer, …);
u_xlat2.xyz = u_xlat10_2.xyz + vs_TEXCOORD7.xyz;              // ライトバッファ + 環境光
u_xlat1.xyz = u_xlat7.xyz * u_xlat2.xyz + u_xlat3.xyz;        // albedo × ライト + スペキュラ
SV_Target0.xyz = u_xlat0.yzw * vec3(0.18, 0.20, 0.22) + u_xlat1.xyz;   // + キューブマップ反射
SV_Target0.w = 1.0;
```

宣言されている uniform も `_WaterColorClean` / `_WaterColorDirty` / `_SpecColor` / `_LightColor0` /
`_WaterNormal` / `_WaterFoam` / `_TerrainHeight` / `_Surface{A,B}{Old,New}` / `_EnvironmentCubemap` /
`_CameraDepthTexture` / `_LightBuffer` / `_TerrainStatus` / `_SimulationTime` / `_WeatherParams` だけで、
**emission / glow / HDR intensity の類は 1 つも無い**。

→ **「水を光らせる」は、マテリアル側からは不可能。** 色は必ずライトに乗算される＝**夜は必ず暗くなる**。
これは「溶岩が夜こそ映える」という期待と正反対である。

（`_WaterColorClean.a` は `m_waterRainFoam` に転用されており、alpha を発光に使う余地も無い。）

### C-9. 画面側の glow は実在するが、材質を選ばない — PARTIAL

`ColossalFramework.ToneMapping : PostProcessEffect`（`ColossalManaged.dll`）:

```
F m_EnableGlowSupport        Boolean public
F m_GlowIntensityThreshold   Vector2 public
OnRenderImage:
  IL_00AC  ldstr "_GlowSupport"            SetFloat
  IL_00C1  ldstr "_GlowUseMax"             SetFloat
  IL_00D6  ldstr "_GlowIntensityThreshold" SetVector
```

`Hidden/ToneMapping`（`sharedassets7.assets`）の GLSL:

```glsl
u_xlat0.x = u_xlat0.y + (-_GlowIntensityThreshold.x);            // トーンマップ後の輝度 − 下限
u_xlat4.x = 1.0 / (_GlowIntensityThreshold.y - _GlowIntensityThreshold.x);
u_xlat0.x = clamp(u_xlat4.x * u_xlat0.x, 0.0, 1.0);
u_xlat8   = u_xlat0.x * u_xlat0.x * (3.0 - 2.0*u_xlat0.x);       // smoothstep
u_xlat12  = max(u_xlat10_2.w, u_xlat8);                          // 元のアルファ（glow マスク）と合成
SV_Target0.w = _GlowSupport * u_xlat0.x + u_xlat10_2.w;
```

`ColossalFramework` に `Bloom` / `Glow` という独立クラスは **ABSENT**（`PostProcessEffect` の派生は
`ColorCorrectionLut` / `FilmGrainEffect` / `ToneMapping` の 3 つだけ）。

| 事実 | 判定 |
|---|---|
| glow は「材質の発光」ではなく「**画面の輝度がしきい値を超えたらにじむ**」 | **CONFIRMED** |
| したがって `_WaterColorDirty` を `Color(4f, 0.5f, 0.1f)` のような **1.0 超の値**にすれば、昼間はにじむ可能性がある | **PARTIAL**（`Color` は float なので値は入る。実際に閾値を超えるかは `m_GlowIntensityThreshold` の実値と時刻とトーンマップ設定次第） |
| 夜に光るか | **CONFIRMED で「光らない」**（`albedo × _LightBuffer` なので、光が無ければ 0） |
| これを確かめる方法 | 実機で `TerrainManager.instance.m_properties.m_waterColorDirty = new Color(4,0.5f,0.1f)` を入れ、**昼と夜のスクリーンショットを比べる**。1 分で決まる。ただし成功しても**マップ中の汚水が全部にじむ** |

---

## D. 水源 — 「火口に合わせてせりあがる水源」は作れるか

> ④§D-4 に `WaterSource` の全挙動（`TYPE_NATURAL` の自己調整、半径 `Sqrt(rate)*0.4+10`、
> `terrain >= m_target` のセルはスキップ、`LockWaterSource` はロックを握ったまま返る、セーブに焼き付く）が既にある。
> ここでは**⑤に固有の 3 点だけ**を追加で確定させる。

### D-10. 内陸任意点に水源を作ることに制約は無い — CONFIRMED

`WaterSimulation.CreateWaterSource(out ushort source, WaterSource sourceData)`（IL 全文、約 200 バイト）:

```
*source = 0
spin: while (!Monitor.TryEnter(m_waterSources, 0)) ;          // ★ 水スレッドと同じスピンロック
try {
    for (i = 0; i < m_waterSources.m_size; i++)
        if (m_waterSources.m_buffer[i].m_type == 0) {          // 空きスロット再利用
            *source = (ushort)(i + 1);
            m_waterSources.m_buffer[i] = sourceData;
            return true;
        }
    if (*source == 0 && m_waterSources.m_size < 65535) {
        *source = (ushort)(m_waterSources.m_size + 1);
        m_waterSources.Add(sourceData);
        return true;
    }
    return false;
} finally { Monitor.Exit(m_waterSources); }
```

→ **座標の検証・海／陸の判定・高さの判定は 1 つも無い。** どこにでも置ける。**戻り値を必ず見ること。**

マップの川がどう置かれているか（`WaterTool.SimulationStep` IL_0216–02CB、＝**バニラの正解の数値**）:

```
rate            = Clamp((int)(m_capacity * 65535), 0, 65535)
m_inputPosition = m_outputPosition = レイキャストのヒット点
m_inputRate     = m_outputRate     = rate
m_target        = Clamp((int)((hit.y + m_height) * 63.99902f), 0, 65535)
m_type          = TYPE_NATURAL (1)
```

（`WaterTool.PlaceSource` は `CreateWaterSource` を呼んで `m_tempSource.m_type = 0` に戻すだけ。IL_0000–0028。）
最大 rate 65535 → **半径 = √65535 × 0.4 + 10 ≈ 112.4 m**。

### D-11. `m_target` をせり上げる — CONFIRMED（ただし `Lock` の作法を守ること）

`m_target` は `UInt16`・**絶対水位・1/64 m**（`WaterTool` の `* 63.99902` が単位の裏取り）。
上限 65535 → **1023.98 m**。⑤§C-10 の地形天井と同じなので、火山の高さに対して不足しない。

書き換え経路（IL 全文。**`LockWaterSource` に `Monitor.Exit` が無い**ことを再確認した）:

```
LockWaterSource(ushort source):
IL_0005  while (!Monitor.TryEnter(m_waterSources, 0)) ;     // ★ 取ったまま
IL_0021  return m_waterSources.m_buffer[source - 1];        // ★ 境界チェック無し
                                                            // ★ Monitor.Exit が 1 つも無い

UnlockWaterSource(ushort source, WaterSource data):
IL_000E  m_waterSources.m_buffer[source - 1] = data;
IL_001F  Monitor.Exit(m_waterSources);                      // ★ 唯一の解放経路
```

| 罠 | 内容 |
|---|---|
| `source == 0` を渡す | `m_buffer[-1]` で **`IndexOutOfRangeException`。しかもモニタを握ったまま**投げる → **水スレッドが永久にスピンし、ゲームが無反応になる**。呼ぶ前に `source != 0 && source <= m_waterSources.m_size` を確かめること |
| `Unlock` を落とす | 同上。**必ず `try { … } finally { UnlockWaterSource(…) } `** |
| 例外を挟む | `Lock` と `Unlock` の間で**一切例外を投げうるコードを書かない**（ログ出力すら入れない） |
| 永続化 | `WaterSimulation.Data.Serialize` が `m_waterSources` を書く（④§D-4）。**⑤は終了時に `ReleaseWaterSource` すること**。落とすとセーブに残る |

### D-12. 火口の下の地形を上げたときに何が起きるか — CONFIRMED

`SimulateWater` の水源処理を、**⑤の観点（地形が上がる）で読み直した結果**:

**(a) 吐き出し側 — ゼロ除算はしない。無害に止まる。**

```
// 1 周目: 受け入れ余地と対象セル数を数える（IL_1E3A–1EA4）
foreach cell within rOut:
    if (natural && terrain[i] >= src.m_target) continue;              // IL_1E51–1E61 ★ スキップ
    headroom += Min(terrain[i] + cell.m_height - Max(target, terrain[i]), cell.m_height)
    count++
if (natural) outRate = Min(outRate, -(headroom >> 1));                // IL_1EBF
if (outRate <= 0) goto next_source;                                   // IL_1ECD ★ ガード
if (count   <= 0) goto next_source;                                   // IL_1ED8 ★ ガード
// 2 周目: 実際に注ぐ（IL_1F5C–2044）
foreach cell within rOut:
    if (natural && terrain[i] >= src.m_target) continue;
    add = Min((outRate + count/2) / count, 65535 - cell.m_height)
    if (outPollution != 0) cell.m_pollution = Min(65535, cell.m_pollution + outPollution/count)
    cell.m_height += add;  src.m_water -= add;  src.m_flow += add;
```

→ **`count == 0` にも `outRate <= 0` にも明示のガードがある。地形が `m_target` を越えても落ちない。黙って何もしなくなるだけ。**

**(b) 吸い込み側 — ここに地形スキップが無い。挙動が反転する。**

```
// IL_1A1C–1A4F（半径 rIn の全セル。natural 判定も terrain 判定も無い）
excess += Min(terrain[i] + cell.m_height - Max(src.m_target, terrain[i]), cell.m_height)
inRate  = natural ? Min(inRate, excess >> 1) : Min(inRate, excess)
if (inRate > 0) 各セルから比例配分で m_height を引き、src.m_water / m_pollution に足す
```

`terrain >= m_target` のセルでは `Max(target, terrain) == terrain` なので、寄与は `Min(h, h) = h`
＝ **その柱の水を丸ごと「余剰」として数える**。

→ **火山が水源の下の地形を `m_target` より高く押し上げた瞬間、その水源は「泉」から「排水口」に反転し、
半径内の水を `m_inputRate` の速度で吸い上げる。** クラッシュはしないが、**溶岩湖が消える。**

> **⑤が水源を使うなら `m_inputRate = 0` にすること。** 吐き出し側の `headroom` による自己制限だけで
> 水位は `m_target` で頭打ちになるので、吸い込みは要らない。

**(c) 追随の遅れ。** 水源が読む `terrain[i]` は `WaterSimulation.m_heightBuffer`＝`TerrainManager.m_blockHeights`
（⑤§A-4）であり、**上昇 2 m / 64 sim フレーム**（⑤§A-2）の律速を丸ごと被る。
→ **火口を 1 tick で 40 m 上げても、水源から見た地面は約 20 サイクル遅れて上がる。**
「水源が火口に合わせてせりあがる」を素直に実装すると、**見た目の円錐と水面が 20 サイクルずれる。**

**(d) 円錐に水は載らない。** `natural && terrain >= m_target` のスキップは、
**`m_target` より高い地面には水を 1 滴も置かない**という意味である。
火口に水源を置いて `m_target` を火口底より上に取ると、**火口の椀にだけ水が溜まる。**
外輪より高くしない限り溢れない。溢れれば浅水シミュが斜面を流し下すので「流れる」自体は成立する。

---

## E. 水であることの副作用 — ここが致命的

### E-13. 冠水は火を消す — CONFIRMED

`CommonBuildingAI.HandleCommonConsumption`（12 引数版）の冠水判定:

```
IL_0EC3  if (!CanSufferFromFlood(out canFlood)) goto end;
IL_0EDD  waterLevel = TerrainManager.instance.WaterLevel(building.m_position.xz);
IL_0EF1  if (waterLevel <= m_position.y) goto notFlooded;
IL_0F19  deep = (waterLevel > m_position.y + Max(4f, m_info.m_collisionHeight))
                && (m_flags & Untouchable) == 0;
IL_0F49  if ((m_flags & Flooded) != 0)      goto afterDisaster;
IL_0F4F  if (m_fireIntensity != 0)          goto afterDisaster;   // ★ 燃えている建物は洪水災害にしない
…
IL_10A6  if (deep) {
IL_10BB     m_constructState -= 1088 / GetCollapseTime();          // 徐々に崩れる
IL_1162     m_fireIntensity = 0;                                   // ★★ 火を直接 0 にする
IL_117B     m_flags = (m_flags & ~Filling) | Collapsed;
IL_118D     RemovePeople(building, 90);
IL_1195     BuildingDeactivated(…);
         }
```

→ **深い冠水は建物を「燃やす」のではなく「倒壊させ、その際に火を消す」。**
⑤の設計（⑤設計含意 6「溶岩の経路に応じた火災」＝ `BurnBuilding` / `BurnGround` / `m_fireEffect`）と**真っ向から矛盾する**。
また `BuildingAI.BurnBuilding` は冠水中の建物を拒否する（⑤§B-7 既出）ので、
**「溶岩（＝水）が来た所ほど火がつかない」**という完全に逆の挙動になる。

### E-14. 冠水はバニラの「洪水」災害を発生させる — CONFIRMED

同じ経路の続き（IL_0F59–10A1）:

```
disaster = DisasterManager.instance.FindDisaster(m_position);
if (disaster == 0) {
    info = DisasterManager.FindDisasterInfo<GenericFloodAI>();       // ← §D-1 の空の AI
    if (info != null && DisasterManager.instance.CreateDisaster(out disaster, info)) {
        m_disasters.m_buffer[disaster].m_intensity = 10;             // ★ 強度 10
        …
        DetectDisaster(disaster, false);  FollowDisaster(disaster);
    }
}
m_flags |= Flooded;                                                  // 0x20000000
```

→ **プレイヤーの画面には「洪水」の災害通知・カメラ追従・災害アイコンが出る。**
火山を出したのに洪水警報が鳴る。これは**ユーザーが 100 % 気付く**。
（`GuideController.m_buildingFlooded` のガイドポップアップも出る。IL_13D9。）

### E-15. 取水ポンプが溶岩を飲む — CONFIRMED

`WaterFacilityAI.HandleWaterSource`（取水側、IL_00E1–01A8）:

```
if (src.m_water != 0)
    building.m_waterPollution = (byte)Min(255f, 255 * src.m_pollution / src.m_water);   // ★
else
    building.m_waterPollution = 0;
src.m_water -= taken;
if (!TerrainManager.instance.HasWater(src.m_inputPosition.xz)) {
    pos = building.CalculatePosition(m_waterLocationOffset); pos.y = 0;
    if (TerrainManager.instance.GetClosestWaterPos(ref pos, maxDist)) {
        src.m_inputPosition = pos; src.m_outputPosition = pos;      // ★ 近くの水へ勝手に移動する
    } else releaseSource = true;
}
```

| 事実 | 内容 |
|---|---|
| ポンプは水源の種類を見るか | **見ない。** `TerrainManager.HasWater` / `GetClosestWaterPos` しか使わない |
| つまり | **⑤が作った「溶岩」を、給水塔・取水ポンプが普通に汲み上げて市民に配る** |
| 汚染で赤くした場合 | `Building.m_waterPollution` に汚染度が入り、**市民が病気になる**。UI の「Water: {1} ({2}% polluted)」に出る |
| さらに | ポンプは水が引くと**近くの水へ取水口を自動で移動する**ので、溶岩が近づくと**勝手に溶岩を取りに行く** |

（下水処理は `TYPE_FACILITY(2)` 側。`m_pollution = amount * m_outletPollution / Max(100, disposeRate*100)`。IL_007F–009A。）

---

## F. 局所的に赤くする唯一の抜け道

### F-16. 自前の水パスを patch 単位で重ね描きする — PARTIAL

必要な部品が**全部 public** であることは確認した:

```
TerrainManager.instance.m_patches               : TerrainPatch[]          public
TerrainManager.instance.m_waterMaterial         : Material                public
TerrainManager.instance.m_waterTransparentMaterial : Material             public
TerrainManager.instance.m_waterLayer            : Int32                   public
TerrainManager.instance.TransparentWater        : Boolean (get/set public)
TerrainMesh.GetLodMesh(Flags lod, out Mesh, out Quaternion)               public static
TerrainPatch.SetWaterMaterialProperties(MaterialPropertyBlock)            public
TerrainPatch.m_bounds / m_terrainPosition / m_waterExists / m_waterHeight public
```

`TerrainPatch.Render` と**同一の `Graphics.DrawMesh` 呼び出し**を、赤い色を持つ別マテリアルで、
火山を含む patch にだけもう一度出す、という手はある。

**重要（③の教訓との違い）:** ここで作るマテリアルは `new Material(TerrainManager.instance.m_waterMaterial)` である。
これは**`Shader.Find` を一切通らない**（既存 `Material` からシェーダ参照をコピーする）ので、
13 個目の外れ（`Shader.Find` が null を返す）の影響を受けない。
また `MeshRenderer` ではなく `Graphics.DrawMesh` で、**ゲームと同じ mesh・同じ matrix・同じ layer** に出すため、
firewhirl 付録 A の「借りたマテリアルは自前 `MeshRenderer` では見えない」とも条件が違う。

| 事実 | 判定 |
|---|---|
| API が全部 public で到達できる | **CONFIRMED** |
| 実際に描けるか | **PARTIAL。未検証。** 判定方法: 火山を出さずに、`m_patches[i]` の 1 枚に対して緑の `_WaterColorClean` を持つクローンで 1 パス足し、その patch の水だけ緑になるかを見る |
| 粒度 | **1 patch = 1920 m 角**（`PATCH_RESOLUTION=9`, `RAW_CELL_SIZE=16`, 120 セル/patch）。**火山だけを塗ることはできない。同じ patch にある海・川・湖も全部赤くなる** |
| z ファイティング | **必至。** バニラの水パスの上に同じ深度で描くことになる。オフセットを入れるか、`m_waterMaterial` を差し替えて元のパスを潰すか（＝またグローバル）の二択 |
| コスト | patch あたり `DrawMesh` 1 回。安い |

→ **「patch 単位でよければ局所化できる（かもしれない）」。**
1.9 km 角がまるごと赤くなるので、**火山が海や川から 1 km 以上離れた無人地帯にある場合にしか使えない。**

---

## 設計への含意

### 結論：赤い水の溶岩は **却下（dead）**。部分的にも勧めない。

決め手は 1 つに絞れる:

> **水の色は `Shader.SetGlobalColor("_WaterColorClean" / "_WaterColorDirty" / "_WaterColorUnder")` の
> マップ全体で 1 組のグローバル uniform であり、`TerrainPatch.Render` は全 patch を共通の 1 マテリアル・
> 1 `MaterialPropertyBlock` で描く。位置で変わるチャンネルは汚染 1 本だけで、それが選ぶ 2 色もまたグローバルである。**（§A-1, §A-3, §B-4, §B-6）

**「赤くする」と実際に起きること:**

1. **海と川が全部赤くなる。**（`_WaterColorClean` を赤にした場合）
2. **市内の下水プルームが全部赤くなり、赤い染みが下流へ流れて海に出る。**（`_WaterColorDirty` を赤にし、火口の汚染を上げた場合。§B-7）
3. **夜は光らない。むしろ黒くなる。** 水シェーダには自己発光項が無く、色は必ずライトに乗算される。（§C-8）
4. **火山なのに「洪水」災害の通知が出る。**（§E-14）
5. **溶岩が火を消す。** 深い冠水は `m_fireIntensity = 0` を直接書き、`BurnBuilding` も冠水中の建物を拒否する。
   ⑤の「溶岩の経路に応じた火災」が**成立しなくなる**。（§E-13）
6. **取水ポンプが溶岩を汲む。** しかも水が引くと**取水口を溶岩の方へ自動で寄せる**。（§E-15）

**(3)〜(6) は色の問題ではないので、色をどう解決しても消えない。**
つまり「グローバル色さえ何とかなれば行ける」案ではない。**水を溶岩に見立てること自体が破綻している。**

### いちばん安い正直版（もしどうしても水を使うなら）

やるとしたら **「火口湖」だけ**である。**流さない。**

- 火口の椀の中にだけ `TYPE_NATURAL` の水源を 1 つ、`m_inputRate = 0` / `m_outputRate` 小さめ / `m_target` = 火口底 + 数 m。
  **外輪より低く保てば絶対に溢れない** → §E-13〜E-15 の副作用が全部発生しない（建物も道路もポンプも火口の中には無い）。
- 色は触らない。**「火口に水（＝湖）がある」**という、それ自体が自然な見た目。
- `m_target` を噴火の進行で上下させれば「火口がせり上がる／マグマ溜まりが上がってくる」演出にはなる。
- 終了時に `ReleaseWaterSource`（④§D-4：セーブに焼き付く）。`Lock/Unlock` は必ず `try/finally`（§D-11）。

これは**「溶岩」ではなく「火口湖」**である。名前を変えて出すなら成立する。溶岩の代わりにはならない。

### では溶岩はどうするのか — オーナーに伝えるべきこと

**⑤§B-5 の結論は変わらない。溶岩の光る面は自作するしかない。**
ただし 13 個目の外れ（`Shader.Find` が null）を踏まえた**今回の新しい材料**が 2 つある:

1. **`Shader.Find` を使わずにシェーダを手に入れる経路が実在する。**
   `new Material(existingMaterial)` / `existingMaterial.shader` は**名前解決を通らない**。
   到達できる既存 `Material` / `Shader` は少なくとも:
   `TerrainManager.m_waterMaterial` / `m_waterTransparentMaterial` / `m_terrainMaterial` /
   `TerrainProperties.m_waterShader` / `m_waterTransparentShader` / `m_terrainShader` /
   `DisasterProperties.m_markerMaterial` / `WeatherProperties.m_lightningMaterial`（⑤§B-5 既出）。
   **`Shader.Find` が全滅でも、`XxxProperties` 上の `Shader` フィールドは生きている**（`InitializeShaderProperties` が
   実際にこれらを使って毎回動いているのが証拠）。**これは「文字列があるから解決するはず」ではなく「参照そのもの」である。**
   → **⑤の溶岩メッシュは、`Shader.Find` ではなく `TerrainProperties.m_terrainShader` 等の既存 `Shader` 参照から
     `new Material(shader)` する方向で再検討する価値がある。** ただし CS のカスタムシェーダは
     `_HeightMapping` / `_SurfaceMapping` などのグローバル／per-draw uniform を要求するので、
     そのまま任意メッシュに使って絵になるとは限らない。**PARTIAL。実機 1 回で判る。**
2. **「光る」は材質でなく画面側にある。** `Hidden/ToneMapping` の輝度しきい値 glow（§C-9）は
   材質を問わない。**十分明るい色を出せる面さえ描ければ、glow は勝手に乗る。**
   逆に言えば、**発光シェーダを自作する必要は無い**（明るい色を出せれば足りる）。

**オーナーへの一行:**
> 「水を赤くする」は、水の色がマップ全体で 1 個のグローバルなので、**海と川も全部赤くなります**。
> しかも水は**夜に光らず**、**火を消し**、**「洪水」の警報を出し**、**水道ポンプが飲んでしまいます**。
> 溶岩には使えません。ただし**火口湖**としてなら安全に作れますし、
> 「光らせる」ほうは**画面側の glow が材質を問わない**ので、明るい面さえ描ければ達成できます。

### 着手前にもう 1 つ確かめるべきもの

1. **`new Material(TerrainProperties.m_terrainShader)` あるいは `new Material(m_waterMaterial)` が
   自前メッシュで見えるか。** これが見えるなら⑤の溶岩は一気に安くなる。見えないなら③の結論どおり全部自作。
   （**`Shader.Find` の検査に `"Standard"` を混ぜないこと。**⑤§H-22 の警告がそのまま効く。）
2. **`m_GlowIntensityThreshold` の実値。** `ToneMapping` インスタンスをリフレクションで 1 回読めば、
   「どのくらい明るければ光るのか」が数値で判る。**溶岩の色を決める前にこれを読む。**

---

### 再現手順

```powershell
. docs\tools\ilload.ps1 ; . docs\tools\ildasm.ps1
$d  = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
$A  = $global:A

# 色の所有者と撒き方
Disasm-Method -Method $A.GetType('TerrainProperties').GetMethod('InitializeShaderProperties', $d)
Disasm-Method -Method $A.GetType('TerrainProperties').GetMethod('OverrideFromMapTheme', $d)
Disasm-Method -Method $A.GetType('WaterPropertiesPanel').GetMethod('OnWaterColorDirtyChanged', $d)

# 描画が patch 共通であること
Disasm-Method -Method $A.GetType('TerrainPatch').GetMethod('Render', $d)
$A.GetType('TerrainPatch').GetMethods($d) | ? {$_.Name -eq 'SetWaterMaterialProperties'} | % { Disasm-Method -Method $_ }

# 汚染チャンネル
Disasm-Method -Method $A.GetType('WaterSimulation').GetMethod('FillSurfaceMap', $d)

# 水源の全挙動（3432 行。ファイルに落として読む）
Disasm-Method -Method $A.GetType('WaterSimulation').GetMethod('SimulateWater', $d) | Out-File -Encoding utf8 SimulateWater.txt
Disasm-Method -Method $A.GetType('WaterSimulation').GetMethod('CreateWaterSource', $d)
Disasm-Method -Method $A.GetType('WaterSimulation').GetMethod('LockWaterSource', $d)
Disasm-Method -Method $A.GetType('WaterTool').GetMethod('SimulationStep', $d)

# 冠水の副作用
$A.GetType('CommonBuildingAI').GetMethods($d) | ? {$_.Name -eq 'HandleCommonConsumption' -and $_.GetParameters().Count -eq 12} | % { Disasm-Method -Method $_ }
Disasm-Method -Method $A.GetType('WaterFacilityAI').GetMethod('HandleWaterSource', $d)
```

シェーダ本体（**IL ではない。同梱アセット**）:

```python
# UnityPy 1.25.3 + lz4
import UnityPy, lz4.block, re
env = UnityPy.load(r"...\Cities_Data\resources.assets")
for obj in env.objects:
    if obj.type.name != "Shader": continue
    tt = obj.read_typetree()
    if tt["m_ParsedForm"]["m_Name"] != "Custom/Water/Default": continue
    data = bytes(tt["compressedBlob"]); buf = b""
    for o, c, d in zip(tt["offsets"], tt["compressedLengths"], tt["decompressedLengths"]):
        buf += lz4.block.decompress(data[o:o+c], uncompressed_size=d)
    # buf の中に "#version 150 …" の GLSL 逆展開ソースがそのまま入っている
```

- `m_Name` は空文字。**名前は `m_ParsedForm.m_Name` にある**（ここで 1 回引っかかった）。
- `platforms = [1, 4, 15]`。**15 の変種が GLSL テキストで、そのまま読める。**
  DXBC を眺める必要は無い。
- `Hidden/ToneMapping` は `resources.assets` ではなく **`sharedassets7.assets`** にある。
