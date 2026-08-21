using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 山頂の噴火の<b>進行そのもの</b>（強さの包絡線と噴出口の座標）。**sim スレッド専用。**
    ///
    /// <code>
    /// [sim ] Tick()   噴出の強さと山頂座標を決めるだけ。Unity オブジェクトを 1 つも作らない
    /// [main]          描くのは VolcanoEruptionFx / VolcanoPyroclasticFx（別の型）
    /// </code>
    ///
    /// ── ★ 描画はこの型から出ていった ──────────────────────────
    ///
    /// かつてここは自前の <c>ParticleSystem</c> ＋ 自前 <c>Material</c> で噴煙を描き、
    /// その上に <c>BuildingProperties.m_fireEffect</c> を重ねていた。
    /// **実機では 1 粒も描かれていなかった** —— <c>Shader.Find</c> が組み込みの
    /// <c>"Standard"</c> を含めて全ての名前に null を返す環境だったためである。
    /// いまは<b>ゲーム自身の粒子エフェクト</b>を借りて描く。その一式は
    /// <see cref="VolcanoEruptionFx"/> と <see cref="VolcanoPyroclasticFx"/> にあり、
    /// **この型は Unity のオブジェクトを 1 つも持たない。**
    /// 借り物の選び方・複製・後始末は <see cref="VolcanoVanillaFx"/> の 1 か所にある。
    ///
    /// ── 音の経路（実測に合わせた記述）───────────────────────────
    ///
    /// <c>FireEffect.RenderEffect</c> は <c>m_soundEffect</c> に 1 度も触れない（IL 実測）。
    /// **粒子を描く経路から音は出ない。** ⑤の噴火音は
    /// <see cref="VolcanoEruptionAudio"/> が同梱 wav をゲームの効果音グループへ流す
    /// 別経路である。
    ///
    /// ── 毎 tick の費用（sim）───────────────────────────────
    ///
    /// <c>SampleDetailHeight</c> 1 回（4 読み ＋ 3 <c>Lerp</c>、§B-6）と float 20 本ほど。
    /// **確保は 0 バイト。** 山頂の高さを毎 tick 引き直すのは、火口を彫った
    /// <c>UpdateArea</c> が <c>m_detailHeights</c> に反映されるのが数フレーム遅れるためで、
    /// 1 回だけ読むと**火口を彫る前の高さに噴煙が張り付く**。
    /// </summary>
    public static class VolcanoEruption
    {
        /// <summary>噴火が続くゲーム内時間（分）。**⑤が決めた演出値。**</summary>
        private const float TotalMinutes = 24f;

        /// <summary>強さを引き直す 1 区切り（ゲーム内分）。**フレーム番号は混ぜない。**</summary>
        private const float BurstMinutes = 2f;

        /// <summary>立ち上がりに使う割合（0〜この値で 0 → 1）。</summary>
        private const float RiseFraction = 0.08f;

        /// <summary>衰退が始まる割合（ここから 1 へ向けて 1 → 0）。</summary>
        private const float DecayFraction = 0.65f;

        /// <summary>強さの下限側のゆらぎ（1 区切りごとに <c>[Floor, 1]</c> を引く）。</summary>
        private const float JitterFloor = 0.62f;

        /// <summary>
        /// 山が育っているあいだの強さの下限（持続レベルに対する比）。
        /// **噴火は山ができてから始まるのではなく、噴火が山を積み上げる**（SimCity 4 の順序）。
        /// 隆起の最初から噴煙と発光を出し、隆起の進みとともにここから 1 へ上げる。
        /// </summary>
        private const float BuildFloor = 0.35f;

        /// <summary>
        /// 噴出口を火口の底からどれだけ上げるか（m）。
        /// **「山頂から」ではない**（<see cref="SampleVent"/>）。
        /// </summary>
        private const float VentLiftMetres = 6f;

        // ── sim 側の状態 ──────────────────────────────────────

        private static bool _started;
        private static bool _active;
        private static bool _finished;
        private static Vec3 _centre;
        private static Vec3 _vent;
        private static float _elapsedMinutes;

        /// <summary>
        /// 実時間の積算（ゲーム内分）。<see cref="_elapsedMinutes"/> は山が育っている
        /// あいだ持続の入口で止めるので、**ゆらぎの区切りにはこちらを使う** ——
        /// 止まったほうを使うと、育っているあいだ強さが 1 度も引き直されず、
        /// 噴煙が完全に静止して見える。
        /// </summary>
        private static float _clockMinutes;

        /// <summary>山がまだ育っているか（＝隆起と同時に噴いている）。</summary>
        private static bool _building;
        private static float _intensity;
        private static int _burstIndex;
        private static int _bursts;
        private static uint _seed;
        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>噴火が進行中か（**sim が決め、スナップショットに載る**）。</summary>
        public static bool Active { get { return _active; } }

        /// <summary>噴火が終わったか。<see cref="VolcanoState"/> が次の位相へ進む合図。</summary>
        public static bool Finished { get { return _finished; } }

        /// <summary>
        /// 山をまだ積み上げている最中か（＝隆起と同時に噴いている）。
        /// **SimCity 4 と同じ順序**で、噴火は山ができてから始まるのではなく、
        /// 噴火が山を積み上げる。
        /// </summary>
        public static bool Building { get { return _building; } }

        /// <summary>今の噴出の強さ <c>[0,1]</c>。**⑤が決めた量**で、ゲームの値ではない。</summary>
        public static float IntensityUnit { get { return _intensity; } }

        /// <summary>
        /// 噴出口のワールド座標。<c>Y</c> は<b>火口の底</b>（＋少しの浮き）である。
        /// **山頂（縁）ではない** —— 縁に乗せると、炎も噴煙も噴石も窪みの上に浮く
        /// （2026-08-22、実機の指摘②「噴火口の炎が浮いて見える」）。
        /// </summary>
        public static Vec3 VentWorld { get { return _vent; } }

        /// <summary>これまでに強さを引き直した回数（診断用）。</summary>
        public static int BurstsSoFar { get { return _bursts; } }

        /// <summary>直近の失敗（**英語・診断用**）。無ければ null。</summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// **sim スレッド。** 噴出の予定を決めるだけで、Unity オブジェクトを 1 つも作らない。
        /// <see cref="VolcanoState"/> の位相分岐からのみ呼ぶこと。
        /// </summary>
        /// <param name="buildProgressUnit">
        /// 隆起の進捗 [0,1]。**1 未満なら「山はまだ育っている」**という意味で、
        /// 包絡線は持続の入口で止まり、強さは進捗に合わせて上がる。
        /// 隆起が終わっている位相からは 1 を渡すこと。
        /// </param>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes,
                                float buildProgressUnit)
        {
            try
            {
                Step(footprint, deltaMinutes, buildProgressUnit);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the eruption tick threw " + e.GetType().Name;
                _active = false;
                // ★ 例外で位相を止めない。噴火は演出であって、ここで固まると
                //   プレイヤーは 2 つ目の火山を永久に置けなくなる。
                _finished = true;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano eruption failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcErupt",
                             "volcano eruption failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes,
                                 float buildProgressUnit)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre)) Start(footprint);
            if (_finished) return;

            float build = Clamp01(float.IsNaN(buildProgressUnit) ? 1f : buildProgressUnit);
            _building = build < 1f;

            if (deltaMinutes > 0f)
            {
                _elapsedMinutes += deltaMinutes;
                _clockMinutes += deltaMinutes;
            }

            // ★★ **山が育っているあいだは包絡線を持続の入口で止める。**
            //    止めないと、隆起（既定 30 ゲーム内分）のほうが噴火（24 分）より長いので、
            //    山ができあがったときには噴火がもう終わっている。
            //    止めるのは包絡線だけで、_clockMinutes は進み続ける（ゆらぎのため）。
            if (_building)
            {
                float sustainStart = RiseFraction * TotalMinutes;
                if (_elapsedMinutes > sustainStart) _elapsedMinutes = sustainStart;
            }

            if (_elapsedMinutes >= TotalMinutes)
            {
                _active = false;
                _finished = true;
                _intensity = 0f;
                return;
            }

            // ★★ 噴出口は毎 tick 引き直す。**山はまだ育っていて、火口の底も
            //    一緒に上がっている**（VolcanoCrater のクラス doc）ので、
            //    1 回しか読まないと炎が置き去りになって宙に浮く（指摘②）。
            _vent = SampleVent(footprint);

            // ★ 区切りは経過ゲーム内時間から出す。**frameIndex % N で組まない**
            //   （DAYTIME_FRAMES = 65536、1 ゲーム内分 ≒ 45.51 フレーム。火災旋風 付録 A-4）。
            int burst = (int)(_clockMinutes / BurstMinutes);
            if (burst != _burstIndex)
            {
                _burstIndex = burst;
                _bursts++;
            }

            // ★ 乱数にフレーム番号を混ぜない（計画「2 つの乱数生成器」）。混ぜると
            //   同じ噴火が tick ごとに抽選し直され、強さが毎フレーム跳ねる。
            float jitter = JitterFloor
                           + (1f - JitterFloor)
                             * DeterministicRandom.Unit(_seed, (uint)_burstIndex);

            float envelope = Envelope(_elapsedMinutes / TotalMinutes);
            // 育っているあいだは山の大きさに合わせて強くしていく（小さい山に巨大な噴煙は乗らない）。
            if (_building) envelope *= BuildFloor + (1f - BuildFloor) * build;

            _intensity = Clamp01(envelope * jitter);
            _active = true;
        }

        /// <summary>
        /// 立ち上がり → 持続 → 衰退の包絡線 <c>[0,1]</c>。
        /// **物理量ではない**（設計書 §7.4 / 計画「出してよい断定の範囲」の 5）。
        /// </summary>
        private static float Envelope(float t)
        {
            if (float.IsNaN(t)) return 0f;
            if (t <= 0f) return 0f;
            if (t >= 1f) return 0f;
            if (t < RiseFraction) return t / RiseFraction;
            if (t > DecayFraction) return (1f - t) / (1f - DecayFraction);
            return 1f;
        }

        private static void Start(VolcanoFootprint footprint)
        {
            Reset();
            _started = true;
            _centre = footprint.Centre;
            _vent = SampleVent(footprint);

            // 地点から決まる種。**都市をまたいでも同じ地点なら同じ噴火**になる
            // （DeterministicRandom は状態を持たないハッシュ）。
            _seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.Z)));
        }

        /// <summary>
        /// 噴出口（＝**火口の底**）のワールド座標。**sim スレッド専用**（<c>TerrainManager</c>）。
        ///
        /// 中心の地形高さをそのまま読めばよい —— 火口は高さプロファイルの一部なので
        /// （<c>Core/Volcano/VolcanoCrater</c>）、**中心のセルはもう火口の底そのもの**である。
        /// 育っているあいだも毎 tick 読み直すので、底が上がれば噴出口も上がる。
        ///
        /// 読めなければ「調査時の地形高さ ＋ 出来上がりの火口の底」で代用する ——
        /// **0 を並べた「それらしい」座標を作らない**（地面の中で噴火することになる）し、
        /// **山頂で代用もしない**（それが指摘②の見え方そのものである）。
        /// </summary>
        private static Vec3 SampleVent(VolcanoFootprint footprint)
        {
            float x = footprint.Centre.X;
            float z = footprint.Centre.Z;
            float fallback = footprint.GroundHeightMetres
                             + VolcanoCrater.FloorMetresAt(footprint.HeightMetres,
                                                           footprint.HeightMetres);

            float y = fallback;
            try
            {
                if (Singleton<TerrainManager>.exists)
                {
                    y = Singleton<TerrainManager>.instance
                            .SampleDetailHeight(new Vector3(x, 0f, z));
                }
            }
            catch
            {
                y = fallback;
            }

            if (float.IsNaN(y)) y = fallback;
            return new Vec3(x, y + VentLiftMetres, z);
        }

        /// <summary>
        /// sim 側の状態を捨てる。**レベルアンロードと、新しい火山の開始で呼ぶ。**
        /// <c>_errorLogged</c> は戻さない（ゲームのビルドに対する事実であって
        /// 都市ごとの状態ではない）。
        /// </summary>
        public static void Reset()
        {
            _started = false;
            _active = false;
            _finished = false;
            _centre = new Vec3(0f, 0f, 0f);
            _vent = new Vec3(0f, 0f, 0f);
            _elapsedMinutes = 0f;
            _clockMinutes = 0f;
            _building = false;
            _intensity = 0f;
            _burstIndex = 0;
            _bursts = 0;
            _seed = 0u;
            _lastFailure = null;
        }

        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcErupt",
                     "eruption " + (_active ? "active" : (_finished ? "finished" : "idle"))
                     + " intensity=" + _intensity.ToString("F2")
                     + " bursts=" + _bursts
                     + " elapsed=" + _elapsedMinutes.ToString("F0") + " min"
                     + " frame=" + frame);
        }

        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
