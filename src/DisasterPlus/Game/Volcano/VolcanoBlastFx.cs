using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **爆発と噴石。** 火口が区切りごとに<b>ドンと弾け</b>、岩塊が放物線を描いて
    /// 山肌と裾へ落ちる。**main スレッド専用、毎フレーム。**
    /// <see cref="VolcanoEruptionFx"/> が自分の時計と一緒に呼ぶ（時計を 2 本持たない）。
    ///
    /// ── 何が変わったのか（2026-08-22、所有者の依頼）───────────────────
    ///
    /// > 噴火の際に爆発＋噴石のアニメーションも実装してほしいです。
    ///
    /// 今までの「噴石」は火口の上の円から粒子を湧かせ続けるだけで、
    /// **弾ける瞬間も、飛んで落ちる岩も無かった**（あれは噴出口の噴水である。
    /// そのまま残してある —— 消すと火口が静かになりすぎる）。ここが足すのは 2 つ:
    ///
    /// <code>
    /// 爆発   EffectManager.DispatchEffect(Medium Explosion Particles, 火口, …)
    ///        ★ 一発もの。m_renderDuration = 1.0 秒あるので 1 回積めば減衰して消える
    /// 噴石   Core/Volcano/EjectaBallistics が決めた放物線に沿って、
    ///        毎フレーム RenderEffect で小さな熱い玉を湧かせ直す（1 回に最大 16 個、
    ///        枠は 48 個ぶん）
    ///        → 山肌 / 裾に落ちたら、そこに 0.9 秒だけ土煙を出す
    /// </code>
    ///
    /// ── 「1 発」ではなく「脈打つ」──────────────────────────────
    ///
    /// 実際の噴火は連続ではなく脈動する。区切りは
    /// <c>EruptionEffectPlan.EjectaPeriodSeconds</c>（強いほど短い。7.5 → 1.6 秒）
    /// で、**噴出口の噴水と同じ式**である ——
    /// 別の式にすると弾ける瞬間と噴水の山がずれて、2 つの別々の演出に見える。
    ///
    /// ★ さらに 1 回の爆発を <see cref="BlastPulses"/> 発に割り、
    ///   <c>DispatchEffect</c> の <c>startFrame</c> でずらす（IL 事実 §C:
    ///   <c>m_startFrame</c> まで発火を遅らせられる）。1 発だと「ポン」で終わるが、
    ///   3 発ずれると**ドドン**と鳴っているように見える。
    ///
    /// ── ★ <c>DispatchEffect</c> に渡すのは複製ではない ────────────────────
    ///
    /// あれは**キューに積むだけで、描くのはあとのフレーム**である。⑤の複製を積んだ直後に
    /// レベルアンロードで複製を破棄すると、バニラの中で破棄済みオブジェクトを触る。
    /// だから積むのは <c>VolcanoVanillaFx.BlastOneShot()</c>
    /// （＝**ゲーム自身のプレハブそのもの**）だけである。飛ぶ岩の尾のほうは
    /// <c>RenderEffect</c> の継続モードなので、生存期間は完全にこちらの手の内にある
    /// （<see cref="VolcanoEruptionFx"/> の同じ判断）。
    ///
    /// ── 毎フレームの費用 ─────────────────────────────────
    ///
    /// 生きている岩の数だけ <c>RenderEffect</c>（枠は 48 個。飛んでいる岩と、
    /// 落ちてから 0.9 秒の土煙が同じ枠を使う）。
    /// 岩 1 個の粒子は玉 1 つぶん（半径 9 m ＝ <c>max(100, πr²)</c> の下限側）なので、
    /// 噴煙柱 1 段よりずっと軽い。**弾道は噴出のたびに 1 回だけ解いて配列に焼く。**
    /// 配列は固定長で作り置きし、<b>毎フレームの確保は 0 バイト</b>。
    /// **<c>EjectaBlock</c> は struct なので、この配列は Unity の fake-null の罠に当たらない。**
    /// </summary>
    public static class VolcanoBlastFx
    {
        /// <summary>1 回の爆発を何発に割るか。**1 だと「ポン」で終わる。**</summary>
        private const int BlastPulses = 3;

        /// <summary>発と発のあいだ（シミュレーションフレーム）。60 fps でおよそ 0.1 秒。</summary>
        private const uint BlastPulseFrames = 6u;

        /// <summary>発ごとに半径を細らせる比（2 発目以降）。</summary>
        private const float BlastPulseShrink = 0.72f;

        /// <summary>爆発と岩を火口の底からどれだけ上げるか（m）。</summary>
        private const float LaunchLiftMetres = 4f;

        /// <summary>噴出の番号を種に混ぜる塩。</summary>
        private const uint BlastSalt = 0x424C5354u;

        // ── 状態（**全部 struct と平の値。Unity の参照を 1 つも持たない**）──────

        /// <summary>
        /// 同時に空を飛んでいられる岩の数。
        ///
        /// ★★ <b>1 回の噴出ぶん（16 個）では足りない。</b> いちばん長い弾道は 30 秒を
        /// 超えるのに、噴出の間隔は強いときで 1.6 秒しかない ——
        /// 1 回ぶんの配列にすると、**次の噴出が来るたびに前の岩が空中で消える。**
        /// 3 回ぶん置いておけば、いちばん詰まった状況でも消える岩は出ない。
        /// </summary>
        private const int MaxLiveBlocks = EjectaBallistics.MaxBlocksPerBlast * 3;

        private static readonly EjectaBlock[] _blocks = new EjectaBlock[MaxLiveBlocks];

        /// <summary>各枠の打ち上げ時刻（⑤の効果時計の秒）。</summary>
        private static readonly float[] _launchedAt = new float[MaxLiveBlocks];

        /// <summary>最後に弾けた区切りの番号。<see cref="NoBlast"/> は「まだ 1 度も」。</summary>
        private static int _lastBlastIndex = NoBlast;

        private const int NoBlast = int.MinValue;

        private static int _blastsSoFar;
        private static int _drawnLastFrame;
        private static bool _dispatchFailedLogged;

        /// <summary>これまでに弾けた回数（診断用）。</summary>
        public static int BlastsSoFar { get { return _blastsSoFar; } }

        /// <summary>今フレームに描いた岩の数（診断用）。</summary>
        public static int BlocksDrawn { get { return _drawnLastFrame; } }

        /// <summary>
        /// **main スレッド。** 噴火が終わったフレームと、レベルアンロードで呼ぶ。
        /// 借り物の後始末は <see cref="VolcanoVanillaFx"/> の仕事なので、
        /// ここで畳むのは⑤自身の予定だけである。冪等。
        /// </summary>
        public static void Reset()
        {
            for (int i = 0; i < _blocks.Length; i++)
            {
                _blocks[i] = default(EjectaBlock);
                _launchedAt[i] = 0f;
            }
            _lastBlastIndex = NoBlast;
            _blastsSoFar = 0;
            _drawnLastFrame = 0;
        }

        /// <summary>
        /// **main スレッド、毎フレーム。**
        /// <paramref name="clockSeconds"/> は⑤の効果時計（一時停止で止まり、
        /// ゲーム速度に追随する。<see cref="VolcanoEruptionFx"/> が持っている）。
        /// </summary>
        public static void Update(RenderManager.CameraInfo camera, Vec3 vent, Vec3 centre,
                                  VolcanoFootprint footprint, float craterRadiusMetres,
                                  float unit, float clockSeconds, float dt)
        {
            _drawnLastFrame = 0;
            if (camera == null || dt <= 0f) return;
            if (!footprint.Valid) return;

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(centre.X)),
                unchecked((uint)Mathf.RoundToInt(centre.Z)));

            float period = EruptionEffectPlan.EjectaPeriodSeconds(unit);
            if (period > 0f)
            {
                int blastIndex = (int)(clockSeconds / period);
                if (blastIndex != _lastBlastIndex)
                {
                    _lastBlastIndex = blastIndex;
                    _blastsSoFar++;
                    Detonate(seed, blastIndex, vent, footprint, craterRadiusMetres,
                             unit, clockSeconds);
                }
            }

            RenderBlocks(camera, vent, clockSeconds, dt);
        }

        /// <summary>
        /// 弾ける瞬間。**一発ものを <see cref="BlastPulses"/> 発ずらして積み**、
        /// 同時に岩の弾道を解いて配列へ焼く。
        /// </summary>
        private static void Detonate(uint seed, int blastIndex, Vec3 vent,
                                     VolcanoFootprint footprint, float craterRadiusMetres,
                                     float unit, float clockSeconds)
        {
            DispatchBlast(vent, craterRadiusMetres, unit);

            // ★ 火口の底が地面（山を置いた地点）から何 m 上か。
            //   VolcanoFootprint.GroundHeightMetres は調査時の地形高さである。
            float ventAboveBase = vent.Y - footprint.GroundHeightMetres;
            if (float.IsNaN(ventAboveBase) || ventAboveBase < 0f) ventAboveBase = 0f;

            int count = EjectaBallistics.BlocksPerBlast(unit);
            uint blastSeed = DeterministicRandom.Hash(seed, BlastSalt);

            for (int i = 0; i < count; i++)
            {
                EjectaBlock block = EjectaBallistics.Plan(
                    blastSeed, blastIndex, i, unit, footprint.Form,
                    footprint.RadiusMetres, footprint.HeightMetres, ventAboveBase);

                if (!block.Valid) continue;

                int slot = FreeSlot(clockSeconds);
                _blocks[slot] = block;
                _launchedAt[slot] = clockSeconds;
            }
        }

        /// <summary>
        /// 空いている枠。**空きが無ければいちばん古い枠を潰す** ——
        /// そのときに消えるのは「もう落ちている確率がいちばん高い岩」である。
        /// </summary>
        private static int FreeSlot(float clockSeconds)
        {
            int oldest = 0;
            float oldestAge = float.MinValue;

            for (int i = 0; i < _blocks.Length; i++)
            {
                EjectaBlock b = _blocks[i];
                if (!b.Valid) return i;

                float age = clockSeconds - _launchedAt[i];
                if (age >= b.FlightSeconds + EruptionEffectPlan.ImpactSeconds) return i;
                if (age > oldestAge) { oldestAge = age; oldest = i; }
            }

            return oldest;
        }

        /// <summary>
        /// ゲーム自身の爆発を <c>DispatchEffect</c> で積む。
        /// **引けなければ 1 行だけ残して何もしない**（岩は飛ぶ）。
        /// </summary>
        private static void DispatchBlast(Vec3 vent, float craterRadiusMetres, float unit)
        {
            ParticleEffect blast = VolcanoVanillaFx.BlastOneShot();
            if (blast == null) return;

            try
            {
                if (!Singleton<EffectManager>.exists) return;
                EffectManager effects = Singleton<EffectManager>.instance;

                uint startFrame = 0u;
                if (Singleton<SimulationManager>.exists)
                {
                    startFrame = Singleton<SimulationManager>.instance.m_referenceFrameIndex;
                }

                float radius = EruptionEffectPlan.BlastRadiusMetres(craterRadiusMetres, unit);
                float magnitude = EruptionEffectPlan.BlastMagnitude(unit);
                var position = new Vector3(vent.X, vent.Y + LaunchLiftMetres, vent.Z);

                for (int p = 0; p < BlastPulses; p++)
                {
                    var area = new EffectInfo.SpawnArea(position, Vector3.up, radius);

                    // ★ audioGroup は null でよい。ParticleEffect は RequirePlay() が
                    //   false なので、音のキューには 1 件も積まれない（IL 事実 §C）。
                    effects.DispatchEffect(blast, default(InstanceID), area,
                                           Vector3.zero, 0f, magnitude, null,
                                           startFrame + (uint)p * BlastPulseFrames, false);

                    radius *= BlastPulseShrink;
                    magnitude *= BlastPulseShrink;
                }
            }
            catch (System.Exception e)
            {
                // ★ 区切りごとの経路（数秒に 1 回）。それでも 1 度だけにする。
                if (!_dispatchFailedLogged)
                {
                    _dispatchFailedLogged = true;
                    Log.Info("volcano blast: DispatchEffect refused ("
                             + e.GetType().Name + "); the eruption keeps its plume, "
                             + "flames and flying blocks");
                }
            }
        }

        /// <summary>飛んでいる岩と、落ちた跡の土煙。**継続モードで自分で窓を作る。**</summary>
        private static void RenderBlocks(RenderManager.CameraInfo camera, Vec3 vent,
                                         float clockSeconds, float dt)
        {
            ParticleEffect trail = VolcanoVanillaFx.FlyingBlock();
            ParticleEffect dust = VolcanoVanillaFx.PyroclasticDust();
            if (trail == null && dust == null) return;

            float baseY = vent.Y + LaunchLiftMetres;

            for (int i = 0; i < _blocks.Length; i++)
            {
                EjectaBlock block = _blocks[i];
                if (!block.Valid) continue;

                float age = clockSeconds - _launchedAt[i];
                if (age < 0f) continue;

                // ★ 落ちて土煙も消えた枠は**その場で空ける**。空けないと、
                //   長く続く噴火で枠が全部埋まり、新しい岩が古い岩を空中で消す。
                if (age >= block.FlightSeconds + EruptionEffectPlan.ImpactSeconds)
                {
                    _blocks[i] = default(EjectaBlock);
                    continue;
                }

                if (age < block.FlightSeconds)
                {
                    if (trail == null) continue;

                    float dx, dy, dz;
                    EjectaBallistics.OffsetAt(block, age, out dx, out dy, out dz);

                    var area = new EffectInfo.SpawnArea(
                        new Vector3(vent.X + dx, baseY + dy, vent.Z + dz),
                        Vector3.up,
                        EruptionEffectPlan.BlockTrailRadiusMetres);

                    trail.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                       EruptionEffectPlan.BlockTrailMagnitude(block.SizeUnit),
                                       -1f, dt, camera);
                    _drawnLastFrame++;
                    continue;
                }

                // 着弾。**落ちた場所**に短い土煙を出す。
                if (dust == null) continue;

                float impactMagnitude = EruptionEffectPlan.ImpactMagnitude(
                    block.SizeUnit, age - block.FlightSeconds);
                if (impactMagnitude <= 0f) continue;

                float ix, iy, iz;
                EjectaBallistics.OffsetAt(block, block.FlightSeconds, out ix, out iy, out iz);

                var impact = new EffectInfo.SpawnArea(
                    new Vector3(vent.X + ix, baseY + iy, vent.Z + iz),
                    Vector3.up,
                    EruptionEffectPlan.ImpactRadiusMetres(block.SizeUnit));

                dust.RenderEffect(default(InstanceID), impact, Vector3.zero, 0f,
                                  impactMagnitude, -1f, dt, camera);
                _drawnLastFrame++;
            }
        }
    }
}
