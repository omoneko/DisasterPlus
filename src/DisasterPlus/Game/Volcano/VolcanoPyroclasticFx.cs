using System;
using ColossalFramework.Math;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 斜面を駆け下りる灰色の土煙の帯。**main スレッド専用、毎フレーム。**
    ///
    /// ── ★★ これは火砕流ではない。名乗り方を間違えないこと ─────────────
    ///
    /// <b>バニラに火砕流は無い。</b> 出荷アセットの <c>EffectInfo</c> を全数
    /// （277 個。基本 234 ＋ DLC 43）調べても「地面を這って高速で流れ下る濃密な雲」に
    /// 当たるものは基本ゲームにも DLC にも 1 つも無い。
    ///
    /// ⑤が出しているのは <b><c>Collapse Particles</c>（建物が崩れるときの粉塵）</b>を
    /// 溶岩の経路に沿ったベジェ帯へ湧かせ、<c>RenderEffect</c> の <c>velocity</c> 引数で
    /// 下り方向へ押したものである。見えるのは
    /// **「谷筋を下っていく、幅 50〜180 m の灰色の土煙の帯」** ——
    /// 火砕流の再現ではなく、それらしく見える代用品である。
    /// パネルの注記（<c>Strings.VolcanoPyroclasticNote</c>）と設計書 §4.4 も**そう書く**。
    ///
    /// ── ★ 何も壊さない ─────────────────────────────────
    ///
    /// この型は<b>ゲームの状態を 1 バイトも変えない</b>。建物にも木にも地面にも触らない
    /// （触るとしたら <c>BuildingAI.BurnBuilding</c> / <c>CollapseBuilding</c> を
    /// 直に呼ぶ経路になるが、**燃やすのは溶岩の仕事**であり、
    /// 同じ経路を 2 本にすると「どちらが燃やしたか」が誰にも分からなくなる）。
    /// <c>Building.m_fireIntensity</c> は**決して直接書かない**。
    ///
    /// ── ★★ 扇である。溶岩の上のリボンではない（2026-08-22、実機の指摘⑤）───────
    ///
    /// > 火砕流については溶岩流の上だけを今は流れ落ちていますが、実際はもっと裾野に
    /// > 広がっていくはずです。
    ///
    /// 以前は**溶岩の軌跡そのもの**を経路にしていた。溶岩と同じ谷を下るのは正しいが、
    /// **火砕流は溶岩の幅では流れない** —— 重い雲であって流体の筋ではないので、
    /// 下るにつれて横へ広がり、裾野いっぱいに扇を作る。地形に完全には従わず、
    /// 源に近いところでは尾根を越える。
    ///
    /// いまは <see cref="PyroclasticSurge"/>（Core、テスト付き）が扇そのものを組む ——
    /// 火口のまわりに <c>LobeCount</c> 本の舌を配り、裾へ行くほど幅を広げ（最大 260 m）、
    /// **裾へ行くほどだけ**谷（＝溶岩が下った向き）へ引かれる。
    /// 溶岩の軌跡はここで「谷がどこにあるか」を知る手がかりとしてだけ使い、
    /// 方位を 1 本ずつ取り出して Core へ渡す（<see cref="_bearings"/>）。
    ///
    /// ── ベジェ帯の落とし穴（IL 実測）──────────────────────────
    ///
    /// <code>
    /// SpawnArea(Bezier3 bezier, float halfWidth, float halfHeight)
    ///   → EmitParticles は **halfHeight（第 4 引数）を帯の半幅として使い**、
    ///     halfWidth（第 3 引数）は読まない。引数名と実装がずれている。
    ///   → 初速は**上向き成分にしか入らない**。横へ流すのは velocity 引数のほう。
    /// </code>
    ///
    /// したがって半幅は**両方に同じ値**を入れ、押すのは <c>velocity</c> で行う。
    ///
    /// ── 毎フレームの費用 ─────────────────────────────────
    ///
    /// 帯は <c>PyroclasticSurge.LobeCount</c> 本（2 → 5）。1 本あたり
    /// <c>SampleDetailHeight</c> 4 回（読み取り。<c>TerrainHeightSampler</c> の doc）と
    /// <c>RenderEffect</c> 1 回。<c>Bezier3</c> / <c>SpawnArea</c> / <c>Vector3</c> は
    /// すべて struct で、方位の配列は**開始時に 1 本だけ確保して使い回す**ので
    /// **ヒープ確保は 0 バイト**である。
    ///
    /// ★ 粒子の総量は増えない。<c>PyroclasticSurge.Magnitude</c> が帯の面積で
    ///   正規化するので、**扇ぜんぶで従来の 2 本ぶんと同じ量**に収まる。
    ///
    /// ── この型は sim スレッドから 1 度も呼ばれない ────────────────────
    /// </summary>
    public static class VolcanoPyroclasticFx
    {
        /// <summary>帯を地面からどれだけ浮かせるか（m）。</summary>
        private const float LiftMetres = 5f;

        /// <summary>
        /// 溶岩が下った向き（ラジアン）。**谷がどこにあるかの手がかり**で、
        /// 経路そのものではない（クラス doc）。
        ///
        /// ★ 配列は 1 本だけ確保して使い回す。毎フレーム作ると 60 fps で
        ///   1 秒に 60 個のごみになる（この型は毎フレーム走る）。
        ///   <c>float[]</c> なので Unity の fake-null は関係が無い
        ///   （**あの罠は <c>UnityEngine.Object</c> の配列の話である**）。
        /// </summary>
        private static readonly float[] _bearings = new float[VolcanoLava.MaxFlows];

        /// <summary>
        /// バニラの効果時計（秒）。<c>EffectManager</c> 自身が描画 1 フレームごとに
        /// <c>m_simulationTimeDelta</c> を足しているので、⑤も同じ足し方をする。
        /// **一時停止で止まり、ゲーム速度に追随する。**
        /// </summary>
        private static float _clockSeconds;

        private static bool _renderErrorLogged;
        private static int _bandsDrawn;

        /// <summary>今フレームに出した帯の本数（診断用）。</summary>
        public static int BandsDrawn { get { return _bandsDrawn; } }

        /// <summary>
        /// 土煙のエフェクトを直近に引けていたか。**診断専用の平の読み取り。**
        ///
        /// ★★ ここから解決を走らせないこと。診断は sim スレッドから組み立てられる
        ///   （<c>FeatureHost.BuildReport</c>）ので、Unity のオブジェクトには
        ///   参照比較ですら触れない。<see cref="Step"/> が main スレッドで
        ///   引いた結果を読むだけである。
        /// </summary>
        public static bool DustResolved { get { return VolcanoVanillaFx.DustResolvedCached; } }

        /// <summary>**main スレッド、毎フレーム。**</summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (Exception e)
            {
                _bandsDrawn = 0;
                if (!_renderErrorLogged)
                {
                    _renderErrorLogged = true;
                    Log.Error("volcano pyroclastic effects failed", e);
                }
            }
        }

        /// <summary>
        /// **main スレッド。** 設定で切ったときとレベルアンロードで呼ぶ。
        /// 借りているエフェクトは <see cref="VolcanoVanillaFx"/> が持っているので、
        /// ここで畳むのは⑤自身の時計だけ。冪等。
        /// </summary>
        public static void Destroy()
        {
            _clockSeconds = 0f;
            _bandsDrawn = 0;
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            _bandsDrawn = 0;

            if (snapshot == null || !snapshot.Valid || !snapshot.Footprint.Valid)
            {
                _clockSeconds = 0f;
                return;
            }

            // ★★ **位相で門にする。** 経路を溶岩の軌跡から外した（扇にした）ので、
            //    「軌跡が空なら描かない」という以前の門はもう働かない。
            //    <c>VolcanoLava.CoolUnit</c> は火山が 1 つも無いとき **1 を返す**
            //    （AllStopped が false ＝「まだ止まっていない」）ので、位相を見ないと
            //    **山が無いところで土煙が全力で出る**。
            if (!snapshot.EruptionActive
                && snapshot.Phase != VolcanoPhase.Flowing
                && snapshot.Phase != VolcanoPhase.Cooling)
            {
                _clockSeconds = 0f;
                return;
            }

            // 噴火が続いているあいだはその強さ、終わったあとは溶岩の冷え具合で薄れる。
            // **どちらも 0 になったら 1 粒も出さない**（止まった谷に灰が残り続けない）。
            float unit = snapshot.EruptionActive
                ? snapshot.EruptionIntensityUnit : snapshot.LavaCoolUnit;
            if (float.IsNaN(unit) || unit <= 0f) return;
            if (unit > 1f) unit = 1f;

            RenderManager.CameraInfo camera = VolcanoVanillaFx.CameraInfo();
            if (camera == null) return;

            ParticleEffect dust = VolcanoVanillaFx.PyroclasticDust();
            if (dust == null) return;

            float dt = VolcanoVanillaFx.EffectTimeDelta();
            if (dt <= 0f) return;
            _clockSeconds += dt;

            // ★ 扇は火口から出る。噴出口（火口の底）の水平位置をそのまま使う。
            var vent = new Vec2(snapshot.VentWorld.X, snapshot.VentWorld.Z);

            int channels = ReadBearings(snapshot, vent);

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(snapshot.Footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(snapshot.Footprint.Centre.Z)));

            // ★ 扇は**今そこに在る山の大きさ**までしか下れない。隆起の途中に
            //   出来上がりの半径を渡すと、まだ平らな地面の上を土煙が走る。
            //   短くなりすぎた舌は PyroclasticSurge.MinPathMetres で自然に消える。
            float reachRadius = snapshot.UpliftComplete
                ? snapshot.Footprint.RadiusMetres
                : snapshot.Footprint.RadiusMetres * Clamp01(snapshot.ProgressUnit);

            for (int i = 0; i < PyroclasticSurge.LobeCount; i++)
            {
                if (RenderLobe(dust, camera, vent, seed, i, channels, reachRadius, unit, dt))
                {
                    _bandsDrawn++;
                }
            }
        }

        /// <summary>
        /// 溶岩の軌跡から「谷の向き」を <see cref="_bearings"/> へ取り出す。
        /// **溶岩が 1 本も流れていなくてもよい**（0 を返し、扇は谷に引かれないだけ）。
        /// </summary>
        private static int ReadBearings(VolcanoSnapshot snapshot, Vec2 vent)
        {
            Vec2[] points = snapshot.LavaTrailPoints;
            int[] counts = snapshot.LavaTrailCounts;
            if (points == null || counts == null) return 0;

            int found = 0;
            int cursor = 0;
            for (int i = 0; i < counts.Length && found < _bearings.Length; i++)
            {
                int declared = counts[i] > 0 ? counts[i] : 0;
                int available = declared;
                if (cursor + available > points.Length) available = points.Length - cursor;

                float bearing;
                if (available >= 2
                    && PyroclasticSurge.TryBearing(points, cursor, available, vent, out bearing))
                {
                    _bearings[found++] = bearing;
                }

                cursor += declared;
                if (cursor >= points.Length) break;
            }

            return found;
        }

        /// <summary>
        /// 舌 1 本ぶんの帯。出せなければ <c>false</c> を返すだけで、例外は投げない。
        /// </summary>
        private static bool RenderLobe(ParticleEffect effect, RenderManager.CameraInfo camera,
                                       Vec2 vent, uint seed, int index, int channels,
                                       float radiusMetres, float unit, float dt)
        {
            float reach = PyroclasticSurge.ReachMetres(radiusMetres, unit, seed, index);
            if (reach < PyroclasticSurge.MinPathMetres) return false;

            float azimuth = PyroclasticSurge.LobeAzimuth(seed, index, PyroclasticSurge.LobeCount);

            // ★ 谷の手がかり。1 本も無ければ引かれないだけで、扇そのものは出る。
            bool found;
            float channel = PyroclasticSurge.NearestChannel(azimuth, _bearings, channels,
                                                            out found);
            if (!found) channel = azimuth;

            // ★ 舌ごとに位相をずらす（5 本が隊列を組んで走らないため）。
            float clock = _clockSeconds
                          + PyroclasticSurge.LobePhaseSeconds(index, PyroclasticSurge.LobeCount,
                                                              reach);
            float head = PyroclasticSurge.HeadMetres(clock, reach);
            float halfWidth = PyroclasticSurge.HalfWidthMetres(head);

            float magnitude = PyroclasticSurge.Magnitude(unit, head, reach, halfWidth);
            if (magnitude <= 0f) return false;

            Vec2 a, b, c, d;
            if (!PyroclasticSurge.TryLobe(vent, azimuth, channel, reach, head,
                                          out a, out b, out c, out d))
            {
                return false;
            }

            Vector3 pa = OnGround(a);
            Vector3 pb = OnGround(b);
            Vector3 pc = OnGround(c);
            Vector3 pd = OnGround(d);

            // ★ 第 3 引数は読まれない（IL 実測）。両方に同じ値を入れておく。
            var area = new EffectInfo.SpawnArea(new Bezier3(pa, pb, pc, pd),
                                                halfWidth, halfWidth);

            // ★ 這わせるのはこの velocity である（ベジェ経路では初速が
            //   上向き成分にしか入らない）。尾 → 頭の向きへ押す。
            Vector3 push = pd - pa;
            push.y = 0f;
            float length = push.magnitude;
            if (length > 0.001f)
            {
                push *= PyroclasticSurge.PushMetresPerSecond / length;
            }
            else
            {
                push = Vector3.zero;
            }

            effect.RenderEffect(default(InstanceID), area, push, 0f,
                                magnitude,
                                -1f,   // ★ 継続モード
                                dt, camera);
            return true;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }

        /// <summary>
        /// 地面の高さに乗せた点。読めなければ 0 を使う（**帯が消えるより良い**）。
        /// <c>SampleDetailHeight</c> は読み取りなのでどちらのスレッドからでも安全
        /// （<see cref="TerrainHeightSampler"/> の doc）。
        /// </summary>
        private static Vector3 OnGround(Vec2 point)
        {
            float y;
            try
            {
                y = TerrainHeightSampler.Instance.SampleHeight(point.X, point.Z);
            }
            catch
            {
                y = 0f;
            }

            if (float.IsNaN(y) || float.IsInfinity(y)) y = 0f;
            return new Vector3(point.X, y + LiftMetres, point.Z);
        }
    }
}
