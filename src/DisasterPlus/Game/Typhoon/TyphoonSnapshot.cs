using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <c>ThunderStormAI</c> と <c>VortexAI</c> のプレハブに焼き込まれている 6 つの調整値。
    ///
    /// **この 6 個の実数値は DLL に存在しない**（IL 事実文書 §A-0 と §B-1。どちらも
    /// PARTIAL 判定）。プレハブのシリアライズ値なので IL 逆アセンブルでは見えず、
    /// **実行時に <c>DisasterManager.FindDisasterInfo&lt;T&gt;()</c> から読んで診断ダンプに
    /// 出すのが唯一の入手経路**であり、それが Task 2 の主目的である。
    /// ④の以後の持続時間・落雷本数・破壊半径・移動速度は全てこの上に乗る。
    ///
    /// **嵐と竜巻は独立に解決する。** 竜巻プレハブが読めなくても（随伴竜巻が
    /// 使えないだけで）台風本体は動く。だからフラグを 2 本に分けてある。
    ///
    /// struct にしているのは、キャッシュしても Unity の fake-null 自己修復問題を
    /// 持ち込まないため（float / uint しか持たないので <c>DisasterInfo</c> や
    /// <c>VehicleInfo</c> の参照を抱え込まずに済む）。既定値は両方 false ＝
    /// 「まだ／もう読めていない」。
    /// </summary>
    public struct TyphoonPrefabFacts
    {
        /// <summary>嵐側の 3 値を読めたか。false のとき下の 3 つは 0 で意味を持たない。</summary>
        public readonly bool StormResolved;

        /// <summary><c>ThunderStormAI.m_radius</c>。落雷散布半径とハザード円盤の基準（§A-1 / §A-2）。</summary>
        public readonly float StormRadius;

        /// <summary><c>ThunderStormAI.m_emergingDuration</c>（フレーム）。</summary>
        public readonly uint EmergingDuration;

        /// <summary>
        /// <c>ThunderStormAI.m_activeDuration</c>（フレーム）。
        /// **台風はこれより長生きできない**（<c>IsStillActive</c>、§A-1）。
        /// 進行速度はこの値からしか出せない（<c>TyphoonTrack.SpeedFor</c>）。
        /// </summary>
        public readonly uint ActiveDuration;

        /// <summary>竜巻側の 3 値を読めたか。**台風本体はこれが false でも動く。**</summary>
        public readonly bool VortexResolved;

        /// <summary><c>VortexAI.m_destructionRadiusMin</c>。</summary>
        public readonly float DestructionRadiusMin;

        /// <summary><c>VortexAI.m_destructionRadiusMax</c>。</summary>
        public readonly float DestructionRadiusMax;

        /// <summary>
        /// **<c>VehicleInfo.m_maxSpeed</c>**（<c>TornadoAI.m_vortexInfo</c> 側）。
        /// <c>VortexAI</c> に <c>m_maxSpeed</c> というフィールドは無い。
        /// 詳細は <see cref="TyphoonReader"/> の doc。
        /// </summary>
        public readonly float VortexMaxSpeed;

        public TyphoonPrefabFacts(bool stormResolved, float stormRadius,
                                  uint emergingDuration, uint activeDuration,
                                  bool vortexResolved, float destructionRadiusMin,
                                  float destructionRadiusMax, float vortexMaxSpeed)
        {
            StormResolved = stormResolved;
            StormRadius = stormRadius;
            EmergingDuration = emergingDuration;
            ActiveDuration = activeDuration;
            VortexResolved = vortexResolved;
            DestructionRadiusMin = destructionRadiusMin;
            DestructionRadiusMax = destructionRadiusMax;
            VortexMaxSpeed = vortexMaxSpeed;
        }

        /// <summary>
        /// 台風を 1 個でも起こしてよいか。
        ///
        /// 半径が 0 だと暴風域も強風域も 0 になり（<c>TyphoonProfile.StormRadiusOf</c>）、
        /// 持続時間が 0 だと速度が 0 になる（<c>TyphoonTrack.SpeedFor</c>）。
        /// **どちらも「推測した値で代替しない」ことを構造で保証している場所**なので、
        /// ここが false のとき呼び出し側は台風を起こさず、理由を診断に出す（設計書 §6）。
        /// </summary>
        public bool Usable
        {
            get { return StormResolved && StormRadius > 0f && ActiveDuration > 0u; }
        }
    }

    /// <summary>
    /// sim スレッドで作り main スレッドで読む不変スナップショット。
    /// ①の <c>WeatherSnapshot</c>・②の <see cref="EarthquakeSnapshot"/> と同じ規律で、
    /// **一度作ったら書き換えない**。
    ///
    /// **T3 以降がフィールドを足していく。追加は必ず ctor の末尾に付けること**
    /// （既存の呼び出し側を全部直させないため）。
    ///
    /// ④の表示規約: ここに載る値のうち **<see cref="Rain"/> / <see cref="Cloud"/> /
    /// <see cref="Fog"/> / <see cref="WindDirectionDegrees"/> だけがバニラの実測値**で、
    /// それ以外は全て本 MOD が決めた量である（設計書 §1.2 / §7）。
    /// </summary>
    public class TyphoonSnapshot
    {
        /// <summary>読み取りに成功したか。false なら表示側は「読み取れません」と出す。</summary>
        public readonly bool Valid;

        public readonly TyphoonPrefabFacts Prefab;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>。</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// <c>WeatherManager.m_currentRain</c>。**④で <c>[measured]</c> を名乗ってよい 2 値の 1 つ。**
        /// <see cref="WeatherReadable"/> が false のときこの値は無意味（0 と混ぜない）。
        /// </summary>
        public readonly float Rain;

        /// <summary><c>WeatherManager.m_currentCloud</c>。もう 1 つの <c>[measured]</c>。</summary>
        public readonly float Cloud;

        /// <summary><c>WeatherManager.m_currentFog</c>。診断専用（パネルには出さない）。</summary>
        public readonly float Fog;

        /// <summary><c>WeatherManager.m_windDirection</c>（度、-180〜180 に正規化済み）。</summary>
        public readonly float WindDirectionDegrees;

        /// <summary>
        /// <c>WeatherManager.m_enableWeather</c>。
        ///
        /// **false は不具合ではなくプレイヤーの正当な設定**である。ただしその環境では
        /// <c>m_forceWeatherOn</c> を毎 tick 書かない限り雨も雲も 0 へ潰される（§A-4）ので、
        /// T4 の天候駆動はこの値を見て振る舞いを変える。表示側は隠さないこと。
        /// </summary>
        public readonly bool WeatherEnabled;

        /// <summary>
        /// 天候の 4 値を実際に読めたか。
        /// **読めなかった 0 と、本当に 0 だった 0 を混ぜないための旗**
        /// （①②が繰り返し確立した規律）。
        /// </summary>
        public readonly bool WeatherReadable;

        // ── T3: 台風そのものの状態 ──────────────────────────────────
        //
        // **これは前 tick の状態である。** TyphoonReader.Read() は
        // TyphoonFeature.OnSimulationTick の先頭（＝ TyphoonController.Tick の前）で
        // 走るので、ここに載るのは 1 tick 前の値になる。パネルの表示としては
        // 差が出ないが、**sim 側のコードがこのスナップショットを「今の状態」として
        // 使ってはいけない**（sim 側は TyphoonController の static を直接読むこと）。

        /// <summary>④の台風が動いているか。</summary>
        public readonly bool Active;

        /// <summary>④が掴んでいる災害スロットの添字。<see cref="Active"/> のときだけ意味を持つ。</summary>
        public readonly ushort TyphoonId;

        /// <summary>**クランプ前の**真の中心（マップ外にもなる）。</summary>
        public readonly Vec3 Centre;

        /// <summary>進行方位（rad、[0, 2π)）。</summary>
        public readonly float HeadingRadians;

        /// <summary>今の強度（0〜255）。設定した最大値ではなく、包絡線と上陸減衰の後の値。</summary>
        public readonly byte Intensity;

        public readonly float StormRadius;
        public readonly float GaleRadius;
        public readonly TyphoonPhase Phase;
        public readonly uint ElapsedFrames;
        public readonly uint TotalFrames;
        public readonly bool OverLand;

        /// <summary>
        /// 上陸予測が立っているか。**false は「0 分後」ではなく「このまま海上を
        /// 通過する」である。** 0 と混ぜないこと（①②が繰り返し確立した規律）。
        /// </summary>
        public readonly bool LandfallKnown;

        /// <summary>上陸までのゲーム内分。<see cref="LandfallKnown"/> のときだけ意味を持つ。</summary>
        public readonly float MinutesToLandfall;

        /// <summary>
        /// 台風を起こせなかった／手放した理由（英語、診断用。無ければ null）。
        /// **「起こせなかった」を「何も起きていない」と見分ける手段がここにしか無い。**
        /// </summary>
        public readonly string Refusal;

        // ── T4: ④が書いている天候の**目標値** ──────────────────────────
        //
        // ★ 上の Rain / Cloud（WeatherManager の m_currentRain / m_currentCloud）とは
        //   別物である。あちらはバニラの実測値で [measured] を名乗ってよい唯一の 2 値、
        //   こちらは④が毎 tick 書き込んでいる目標値で、本 MOD の量である。
        //   **表示側でこの 2 組を取り違えないこと。**

        /// <summary>④が天候を駆動しているか。</summary>
        public readonly bool WeatherDriving;

        /// <summary>④が書いた <c>m_targetRain</c>。</summary>
        public readonly float DrivenRain;

        /// <summary>④が書いた <c>m_targetCloud</c>。</summary>
        public readonly float DrivenCloud;

        /// <summary>④が書いた <c>m_targetDirection</c>（度、0 = +Z / 90 = +X）。</summary>
        public readonly float DrivenDirectionDegrees;

        // ── T6: 落雷 ────────────────────────────────────────
        //
        // 4 つとも**④が数えた／見積もった量**であって、ゲームが公開している値ではない
        // （<c>m_lightningQueue</c> は private で読めない。IL 事実文書 §A-3）。
        // したがって表示側で <c>[measured]</c> を付けてはいけない。

        /// <summary>④がキューに載せている数（予定 + 45 フレームで落ちる）。</summary>
        public readonly int LightningInFlight;

        /// <summary>この台風で④が積んだ累計。台風ごとに 0 から数え直す。</summary>
        public readonly int LightningTotal;

        /// <summary>
        /// この台風でゲームに捨てられた累計。**0 以外なら上限 20 に当たっている**
        /// ＝宿主の嵐や他 MOD の落雷まで消えている。
        /// </summary>
        public readonly int LightningRejected;

        /// <summary>
        /// 宿主のバニラ雷雨のために空けている枠（<c>LightningBudget.VanillaMaxStrikes</c>
        /// の見積り）。強度が高いほど大きくなり、④の取り分は減る。
        /// </summary>
        public readonly int LightningVanillaReserve;

        // ── T7: 風害 ────────────────────────────────────────
        //
        // 5 つとも**④が数えた量**である。バニラには風による破壊機構が 1 つも無く
        // （§A-5 / §B5）、ここに対応するゲーム側の集計は存在しない。
        // したがって表示側で <c>[measured]</c> を付けてはいけない。

        /// <summary>これまでに走った風害の走査回数（セッション累計）。</summary>
        public readonly int WindPasses;

        /// <summary>直近 1 回の走査で倒壊した棟数。</summary>
        public readonly int WindLastCollapsed;

        /// <summary>セッション累計の倒壊棟数。</summary>
        public readonly int WindTotalCollapsed;

        /// <summary>直近 1 回で調べた棟数（候補マスクを通り、強風域内にあったもの）。</summary>
        public readonly int WindLastScanned;

        /// <summary>
        /// 直近 1 回で**バニラが設計上断った**棟数。
        /// **0 でないのは正常** —— 防災施設は台風で壊れない（§F-2）。
        /// </summary>
        public readonly int WindLastRefused;

        /// <summary>直近 1 回が上限で打ち切られたか（外縁はまだ判定されていない）。</summary>
        public readonly bool WindLastCapped;

        /// <summary>
        /// 直近 1 回で高さが読めなかった棟数。**②と違い対象からは外れていない**
        /// （高さボーナスを辞退しただけ。<c>WindDamageModel</c> の doc）。
        /// </summary>
        public readonly int WindLastUnknownHeight;

        // ── T8: 河川氾濫 ─────────────────────────────────────
        //
        // ここも④の量である。バニラに洪水災害は無く（§D-1）、
        // 「川がどれだけ増水したか」を公開しているゲーム側の値も無い。

        /// <summary>
        /// 氾濫の状態。**<see cref="TyphoonFloodState.NoSources"/> は不具合ではない**
        /// （設計書 §7.4）。表示側はその理由を出すこと。
        /// </summary>
        public readonly TyphoonFloodState FloodState;

        /// <summary>
        /// マップ全体の <c>TYPE_NATURAL</c> 水源の数。**マップ依存で未知**（§D-4）。
        /// 0 は「このマップには川を流している自然水源が無い」であって異常ではない。
        /// </summary>
        public readonly int FloodNaturalSources;

        /// <summary>今④が水位を持ち上げている水源の数。</summary>
        public readonly int FloodTouched;

        /// <summary>直近の走査で中心に適用した上げ幅（m）。</summary>
        public readonly float FloodPeakRiseMetres;

        // ── T10: 随伴竜巻 ─────────────────────────────────────
        //
        // 2 つとも④が数えた量である。ゲーム側に「④の竜巻が何個あるか」を
        // 公開している値は無い。

        /// <summary>④が掴んでいる竜巻の数。</summary>
        public readonly int TornadoCount;

        /// <summary>
        /// そのうち渦車両まで紐づいた数。**<see cref="TornadoCount"/> より小さい
        /// 状態を隠さない** —— 付いていない竜巻は④の軌道に乗らず、
        /// バニラの竜巻として自由に流れる。
        /// </summary>
        public readonly int TornadoAttached;

        public TyphoonSnapshot(bool valid, TyphoonPrefabFacts prefab, uint currentFrame,
                               float rain, float cloud, float fog, float windDirectionDegrees,
                               bool weatherEnabled, bool weatherReadable,
                               bool active, ushort typhoonId, Vec3 centre, float headingRadians,
                               byte intensity, float stormRadius, float galeRadius,
                               TyphoonPhase phase, uint elapsedFrames, uint totalFrames,
                               bool overLand, bool landfallKnown, float minutesToLandfall,
                               string refusal,
                               bool weatherDriving, float drivenRain, float drivenCloud,
                               float drivenDirectionDegrees,
                               int lightningInFlight, int lightningTotal,
                               int lightningRejected, int lightningVanillaReserve,
                               int windPasses, int windLastCollapsed, int windTotalCollapsed,
                               int windLastScanned, int windLastRefused, bool windLastCapped,
                               int windLastUnknownHeight,
                               TyphoonFloodState floodState, int floodNaturalSources,
                               int floodTouched, float floodPeakRiseMetres,
                               int tornadoCount, int tornadoAttached)
        {
            TornadoCount = tornadoCount;
            TornadoAttached = tornadoAttached;
            FloodState = floodState;
            FloodNaturalSources = floodNaturalSources;
            FloodTouched = floodTouched;
            FloodPeakRiseMetres = floodPeakRiseMetres;
            WindPasses = windPasses;
            WindLastCollapsed = windLastCollapsed;
            WindTotalCollapsed = windTotalCollapsed;
            WindLastScanned = windLastScanned;
            WindLastRefused = windLastRefused;
            WindLastCapped = windLastCapped;
            WindLastUnknownHeight = windLastUnknownHeight;
            LightningInFlight = lightningInFlight;
            LightningTotal = lightningTotal;
            LightningRejected = lightningRejected;
            LightningVanillaReserve = lightningVanillaReserve;
            WeatherDriving = weatherDriving;
            DrivenRain = drivenRain;
            DrivenCloud = drivenCloud;
            DrivenDirectionDegrees = drivenDirectionDegrees;
            Valid = valid;
            Prefab = prefab;
            CurrentFrame = currentFrame;
            Rain = rain;
            Cloud = cloud;
            Fog = fog;
            WindDirectionDegrees = windDirectionDegrees;
            WeatherEnabled = weatherEnabled;
            WeatherReadable = weatherReadable;
            Active = active;
            TyphoonId = typhoonId;
            Centre = centre;
            HeadingRadians = headingRadians;
            Intensity = intensity;
            StormRadius = stormRadius;
            GaleRadius = galeRadius;
            Phase = phase;
            ElapsedFrames = elapsedFrames;
            TotalFrames = totalFrames;
            OverLand = overLand;
            LandfallKnown = landfallKnown;
            MinutesToLandfall = minutesToLandfall;
            Refusal = refusal;
        }

        public static TyphoonSnapshot Invalid()
        {
            return new TyphoonSnapshot(false, new TyphoonPrefabFacts(), 0u,
                                       0f, 0f, 0f, 0f, false, false,
                                       false, 0, new Vec3(0f, 0f, 0f), 0f,
                                       0, 0f, 0f, TyphoonPhase.Idle, 0u, 0u,
                                       false, false, 0f, null,
                                       false, 0f, 0f, 0f,
                                       0, 0, 0, 0,
                                       0, 0, 0, 0, 0, false, 0,
                                       TyphoonFloodState.Idle, 0, 0, 0f,
                                       0, 0);
        }
    }
}
