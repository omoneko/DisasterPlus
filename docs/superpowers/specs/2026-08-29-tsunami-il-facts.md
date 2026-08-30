# Natural Disasters DLC の津波 — IL 実測（2026-08-29）

所有者:「natural DisasterDLC の津波のメカニズムを研究して、私が求めているものを一から作り直してください。」

`Assembly-CSharp.dll` を `docs/tools/ilload.ps1` + `ildasm.ps1` で読んだ**実測**である。推測は含まない。

---

## 結論を先に

> **バニラの津波は「波」というオブジェクトではない。**
> **マップ外周の一区画で海面を 1.5 周期ぶん上下させるだけの「境界条件」であり、
> 街を襲う水の壁はすべて、ゲーム自身の浅水方程式ソルバ（`SimulateWater`）が
> その境界の揺さぶりを内陸へ伝播させた結果である。**

したがって「震源から同心円状に広がる水の壁」を作る正しい方法は、
波を自分で描くことでも水源で海面を持ち上げることでもなく、
**同じソルバに、震源で別の形の外力を与えること**である。その口は用意されている（後述 §4）。

---

## 1. `TsunamiAI` は何をするか

`TsunamiAI` の宣言フィールドは 2 つだけ:

| フィールド | 型 | **プレハブ実測値** |
|---|---|---|
| `m_height` | `float` | **64** |
| `m_duration` | `int` | **256** |

（`Cities_Data/sharedassets55.assets` の GameObject `Tsunami`(path_id 655) に付く
MonoBehaviour path_id 1301 を UnityPy で読み、`DisasterAI` の
`m_experienceMilestone` PPtr 12 バイトの直後から復元。`m_info` は非直列化。
同じ手で `Earthquake` は `1000 / 100 / 8192 / 2048`、`Sinkhole` は `80 / 40 / 8192 / 512`。）

`StartDisaster`（IL_0000–0075）:

```
base.StartDisaster(...)
if ((data.m_flags & 64) == 0) return;               // 64 = Significant
if (data.m_waveIndex != 0) { ReleaseWaterWave(data.m_waveIndex); data.m_waveIndex = 0; }
if (!FindSea(disasterID, ref data, out WaterWave w)) return;
data.m_flags |= 256;
WaterSimulation.CreateWaterWave(out data.m_waveIndex, w);
```

**それだけ。**以後 `TsunamiAI` は水に一切触らない。

`IsStillEmerging / IsStillActive / IsStillClearing` は**水とは無関係の寿命判定**で、
`(currentFrame - startFrame) * 0.125` [m] を、マップ隅（±4800）までの
進行方向成分と比べているだけ。0.125 m/フレームなので 4800 m ≒ 38400 フレーム
≒ **10.7 実分**、その間ずっと Active に留まる（水がまだ揺れているから）。

## 2. `FindSea` — どこから来るか（IL_0000–043E）

1. マップ外周を 1 周 4320 セル（1080×4）として `GetSeaSideLocation(i, out x, out z)` で辿る
   （i<1080 → (i,0)／<2160 → (1080,i−1080)／<3240 → (1080−i,1080)／それ以外 → (0,1080−i)）。
   ループは 8640 回まわして**巻き戻りをまたぐ連なり**も拾う。
2. 「海」の判定は `seaLevel*64 - BlockHeights[z*1081+x] >= 8`（＝水深 0.125 m 以上）。
3. **いちばん長く続く海の区画**を採る。同じ長さなら災害の目標地点に近いほう。
   区画長が 10 セル未満なら `false`（＝津波は起きない）。
4. その区画の中央付近を原点 `m_origX/m_origZ`、区画の両端 3 点から
   **海岸線に垂直な内向きベクトル**を作り、長さ 32768 に正規化して `m_dirX/m_dirZ`。
5. `m_minX/m_minZ/m_maxX/m_maxZ` はその 3 点の bbox（＝**外周の一区画**）。

```
m_type     = 1 (TYPE_TSUNAMI)
m_delta    = round(m_height * 65536/1024 * intensity / 55)
           = round(64 * 64 * intensity / 55)        // ★ 実測値を入れた式
           = 7447  (intensity 100)  = 116.4 m       // 単位は 1/64 m
m_duration = m_duration << 6 = 16384   (clamp 0..65472)
```

## 3. `WaterWave.GetSeaLevel` — 波の形（IL_0000–00B6）

`SimulateWater` は**マップ最外周セルを回るループの中でだけ**これを呼ぶ（IL_16C7–16F9）。
呼ばれるのは `m_tsunamiWaves`（＝ `m_type==1`）のみ。

```
if (x,z が bbox の外) return original;

phase = ((x - origX)*dirX + (z - origZ)*dirZ) >> 8      // dir 長 32768 → 1 セル = 128
t     = m_currentTime - phase
if (t <= 0 || t >= m_duration) return original

amp = m_delta * (65536 - m_currentTime) / 65536          // ゆっくり減衰（1024 步で 0）
T   = m_duration                                          // = 16384
arg = amp - amp*cos(2*pi * t / T)                         // = amp*(1 - cos)  … 0→2amp→0 の包絡
off = arg * sin(2*pi * 1.5 * t / T) / 2                   // ★ 1.5 周期
return original - off
```

（`FixedMath.Sin/Cos(angle, m)` は **65536 = 1 周**で `m*sin/cos`。IL_0000–005D で確認。）

つまり外周の海面は

```
      引き波  →  巨大な押し波  →  引き波
   t/T: 0        1/3      1/2      2/3        1
```

**押し波の頂点は t = T/2 で、`original + amp`。intensity 100 なら +116 m。**

`m_currentTime` は水ステップごとに **+64**（IL_03B3–03C6）。
`m_duration = 16384` なので **256 水ステップ**。
★★ **1 水ステップ ＝ 64 sim フレームである**（2026-08-30 に訂正。ここは当初
「≒ 1 sim フレーム」と書いていたが**まちがい**で、それが「津波が発生しない」の
真因そのものだった）。`SimulateWater` は最後に

```
SetCurrentWaterFrame(start, progress, .., max, ..)
  -> m_waterFrameIndex = (start & ~63) + (progress << 6)/max = start + 64
```

を呼び、`WaterThread` は `m_waterFrameIndex < m_simulationFrameIndex` のあいだだけ回り、
`SimulationStep` は `m_simulationFrameIndex > m_waterFrameIndex + 1` で待つ。
＝ **SimulateWater は 64 sim フレームに 1 回しか走らない。**

裏取り 2 件: オフライン再現（`tools/WaterSolverSim`）で測った波速
8.22 m/水ステップ ÷ 64 ＝ 0.128 m/sim フレーム。`TsunamiAI.IsStillActive` が
バニラで使う寿命定数は 0.125 m/sim フレーム（IL_0024）。小数第 2 位まで一致する。

> ★★ **バニラの津波の「発生源」は 256 水ステップ ＝ 16,384 sim フレーム
>    ≒ 4.5 実分である。**（当初「4.3 実秒」と書いていたのは上の誤りによる。）
>    そのあと 10 分ちかく続く水の壁は、全部ソルバの伝播である。

## 4. `TYPE_IMPACT`(2) — **マップのどこにでも置ける外力**（IL_0845–0A49）

`m_impactWaves` は外周に限らず**全セルのループの中**で評価される。1 セルにつき:

```
R  = 1 + max(m_maxX - m_origX, m_origX - m_minX)          // セル単位。★ X しか見ない
R2 = R*R
dx = x - m_origX ; dz = z - m_origZ

f(dx,dz) = m_delta - m_delta*(dx*dx + dz*dz)/R2           // = delta*(1 - d^2/R^2)、d>=R なら 0

accX += f(dx, dz) - f(dx+1, dz)
accZ += f(dx, dz) - f(dx, dz+1)
```

`accX/accZ` は高さに足されるのではない。**流れを駆動する「水面の傾き」に足される**:

```
diff = (terrainA + heightA + accX) - (terrainB + heightB)     // IL_0ACD–0AE5
v    = damp(A.m_velocityX)
v   += diff/4                (符号つき、Randomizer で丸め)     // IL_0B2F–0B47
v    = clamp(v, -B.m_height, A.m_height)                       // 水深で頭打ち
A.m_velocityX = clamp(v, -32766, 32766)
```

> ★★ **IMPACT 波は「そこに水の山があるかのように」ソルバを騙す仮想の山である。**
>   体積は足さない。**海底が隆起したのと同じ**で、これは津波の教科書どおりの発生源。
>   `m_delta > 0` なら山 → 水は**外へ**流れる。`m_delta < 0` なら窪み → 水は**中へ**流れる。

その他の実測:

| 事実 | 内容 |
|---|---|
| `m_delta` の単位 | **1/64 m**（`SplashWater` は `depth*65536/1024` を入れる）。Int16 なので ±511 m |
| 半径 | `R = 1 + max(maxX-origX, origX-minX)` セル。1 セル 16 m |
| bbox 判定 | `x >= minX && x <= maxX+1 && z >= minZ && z <= maxZ+1` |
| `m_dirX/m_dirZ` | **IMPACT では読まれない**（`SplashWater` も 0 を入れる） |
| 寿命 | 毎水ステップ `m_currentTime += 64`。`m_currentTime > m_duration` で**ソルバが自動で解放する**（IL_0436–046F）。TSUNAMI 型は自動解放**されない** |
| `SplashWater` | `cells = ceil(radius/16)`、`delta = ceil(depth*64)`、`m_duration = 256`（＝4 水ステップの一撃） |
| コスト | 全セル × 波の数。bbox で即棄却するので **1 個なら実質ただ** |
| 上限 | `m_waterWaves.m_size >= 65535` で `CreateWaterWave` が false |
| 解放 | `ReleaseWaterWave(i)` は `m_buffer[i-1].m_type = 0` にし、**末尾の空きだけ**詰める。自分の枠は type!=0 なので添字は動かない |
| 永続化 | `WaterWave.Serialize/Deserialize` があり `DisasterData.m_waveIndex` に持たれる。**セーブに残る**ので必ず解放する |

## 5. だから、こう作る

| バニラ | Disaster + の海溝型 |
|---|---|
| 外周の一区画で海面を 1.5 周期上下（境界条件） | **震源に仮想の窪み→仮想の山**（IMPACT 波、内部の外力） |
| 平面波（幾何減衰なし） | 円形波（同心円） |
| 伝播はソルバ | **伝播はソルバ（同じ）** |
| 発生源 256 フレーム | 発生源 900 フレーム |
| 水の壁は勝手にできる | **水の壁は勝手にできる** |

段取りは所有者の指示そのままで、しかも**全部ソルバがやる**:

```
① 隆起   m_delta < 0  仮想の窪み → 水が中心へ集まる → 震源の海面が盛り上がる
② 台地   m_delta > 0  仮想の山   → 盛り上がりが外へ押し出されて広がる
③ ドーナツ            中心の水が外へ出たので中央は海面へ戻る
④ 伝播   m_delta = 0  外力を切る → あとは重力波として同心円状に走り続ける
```

**旧実装（`TsunamiSurge` の水源 240 個＋`SplashWater` 連射）は捨てた。**
あれは海面を「塗って」いただけで、ソルバの中を一度も通っていなかった。
だから壁にならず、減衰し、水源の届く範囲で止まった。
