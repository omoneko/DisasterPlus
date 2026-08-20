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
    /// ── 経路をどこから取るか ────────────────────────────────
    ///
    /// 自分で斜面を辿らない。<see cref="VolcanoHub"/> のスナップショットが運んでくる
    /// **溶岩の軌跡（不変配列）**をそのまま経路にする。地形の解釈を sim 側の 1 か所に
    /// 閉じたままにできるうえ、火砕流も溶岩も同じ谷を下るので経路が一致しているほうが正しい。
    /// 幾何は <see cref="PyroclasticSurge"/>（Core、テスト付き）にある。
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
    /// 帯は最大 <see cref="MaxBands"/> 本。1 本あたり
    /// <c>SampleDetailHeight</c> 4 回（読み取り。<c>TerrainHeightSampler</c> の doc）と
    /// <c>RenderEffect</c> 1 回。<c>Bezier3</c> / <c>SpawnArea</c> / <c>Vector3</c> は
    /// すべて struct なので **ヒープ確保は 0 バイト**である。
    ///
    /// ── この型は sim スレッドから 1 度も呼ばれない ────────────────────
    /// </summary>
    public static class VolcanoPyroclasticFx
    {
        /// <summary>同時に出す帯の本数の上限。**費用の上限そのもの。**</summary>
        public const int MaxBands = 2;

        /// <summary>帯を地面からどれだけ浮かせるか（m）。</summary>
        private const float LiftMetres = 5f;

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

            if (snapshot == null || !snapshot.Valid
                || snapshot.LavaTrailPoints == null || snapshot.LavaTrailCounts == null
                || snapshot.LavaTrailPoints.Length < PyroclasticSurge.MinPoints)
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

            Vec2[] points = snapshot.LavaTrailPoints;
            int[] counts = snapshot.LavaTrailCounts;

            int cursor = 0;
            for (int i = 0; i < counts.Length && _bandsDrawn < MaxBands; i++)
            {
                int declared = counts[i] > 0 ? counts[i] : 0;
                int available = declared;
                if (cursor + available > points.Length) available = points.Length - cursor;

                if (available >= PyroclasticSurge.MinPoints
                    && RenderBand(dust, camera, points, cursor, available, unit, dt))
                {
                    _bandsDrawn++;
                }

                cursor += declared;
                if (cursor >= points.Length) break;
            }
        }

        /// <summary>
        /// 1 本ぶんの帯。出せなければ <c>false</c> を返すだけで、例外は投げない。
        /// </summary>
        private static bool RenderBand(ParticleEffect effect, RenderManager.CameraInfo camera,
                                       Vec2[] points, int start, int count, float unit, float dt)
        {
            float path = PyroclasticSurge.PathLengthMetres(points, start, count);
            if (path < PyroclasticSurge.MinPathMetres) return false;

            float head = PyroclasticSurge.HeadMetres(_clockSeconds, path);

            float magnitude = PyroclasticSurge.Magnitude(unit, head, path);
            if (magnitude <= 0f) return false;

            Vec2 a, b, c, d;
            if (!PyroclasticSurge.TryBand(points, start, count, head, out a, out b, out c, out d))
            {
                return false;
            }

            Vector3 pa = OnGround(a);
            Vector3 pb = OnGround(b);
            Vector3 pc = OnGround(c);
            Vector3 pd = OnGround(d);

            float halfWidth = PyroclasticSurge.HalfWidthMetres(head);

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
