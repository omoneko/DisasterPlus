using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 暴風が<b>看板などのプロップ</b>を持っていく。**sim スレッド専用。**
    ///
    /// ── 所有者の依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 街に暴風および大雨による小さな建物の破壊や看板プロップの破壊、
    /// &gt; 局所的な洪水を発生させることです。
    ///
    /// 建物（<c>TyphoonWind.Gale</c>）と洪水（<c>TyphoonFlood</c>）は既にあった。
    /// <b>プロップだけが 1 つも壊れていなかった。</b>
    ///
    /// ── 走査の作り（IL 実測）─────────────────────────────────────
    ///
    /// <c>PropManager.m_propGrid</c> は <b>270×270</b>（72,900 要素。
    /// <c>PropManager.Awake</c> の <c>newarr</c> で確認）で、マップは一辺 17,280 m
    /// なので<b>1 セル 64 m</b>である。セルの中は
    /// <c>PropInstance.m_nextGridProp</c> の片方向リストで繋がっている
    /// （建物・道路のグリッドと同じ作り）。
    ///
    /// ★★ <b>1 tick で暴風域を全部舐めない。</b> 半径 4 km なら 125×125 ＝
    ///   15,625 セルあり、そこに数万個のプロップが入りうる。
    ///   <see cref="CellsPerTick"/> ずつカーソルを進める。
    ///
    /// ★★ <b><c>ReleaseProp</c> は取り消せない。</b> だからしきい値は渋め
    ///   （<c>PropGaleModel</c> のクラス doc）。「台風が来たら看板が全部消える」は
    ///   直せない壊れ方である。
    ///
    /// ★ 木には触らない。木は <c>TreeManager</c> の持ち物で、ここには入っていない。
    /// </summary>
    public static class TyphoonPropDamage
    {
        /// <summary>プロップのグリッドの一辺（<c>PropManager.Awake</c> の 72,900 ＝ 270²）。</summary>
        private const int GridSide = 270;

        /// <summary>1 セルの辺（m）。17,280 / 270。</summary>
        private const float CellSizeMetres = 64f;

        /// <summary>マップ半辺（m）。グリッドの原点を出すのに使う。</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>1 tick に舐めるセル数。**暴風域を一度に全部見ない。**</summary>
        private const int CellsPerTick = 256;

        /// <summary>判定の間隔（フレーム）。<c>TyphoonWind.Gale</c> と同じ刻み。</summary>
        private const int IntervalFrames = 64;

        /// <summary>1 tick に飛ばす上限。**一帯が一度に消えるのを防ぐ最後の砦。**</summary>
        private const int MaxTakenPerTick = 24;

        private static int _cursor;
        private static uint _round;
        private static uint _lastFrame;
        private static int _takenTotal;
        private static int _takenLastTick;
        private static int _scannedLastTick;
        private static bool _errorLogged;

        /// <summary>これまでに飛ばしたプロップの数（診断用）。</summary>
        public static int TakenTotal { get { return _takenTotal; } }

        /// <summary>直近の tick で飛ばした数（診断用）。</summary>
        public static int TakenLastTick { get { return _takenLastTick; } }

        /// <summary>直近の tick で見たセル数（診断用）。0 は「走っていない」。</summary>
        public static int ScannedLastTick { get { return _scannedLastTick; } }

        /// <summary>直近の失敗（診断用）。**黙って何もしないをやらない。**</summary>
        public static string LastFailure { get; private set; }

        /// <summary>レベルのロード／アンロードと、台風が終わったときに呼ぶ。</summary>
        public static void Reset()
        {
            _cursor = 0;
            _round = 0u;
            _lastFrame = 0u;
            _takenTotal = 0;
            _takenLastTick = 0;
            _scannedLastTick = 0;
            LastFailure = null;
            // _errorLogged は戻さない（この環境に対する事実である）。
        }

        /// <summary>
        /// **sim スレッド、ポーズガードより下。**
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame)
        {
            _takenLastTick = 0;
            _scannedLastTick = 0;

            if (snapshot == null || !snapshot.Valid || !snapshot.Active) return;
            if (frame - _lastFrame < IntervalFrames) return;
            _lastFrame = frame;

            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                LastFailure = "the prop sweep threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon prop damage failed", e);
                }
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            if (!Singleton<PropManager>.exists) { LastFailure = "no PropManager"; return; }

            PropManager props = Singleton<PropManager>.instance;
            ushort[] grid = props.m_propGrid;
            PropInstance[] buffer = props.m_props.m_buffer;
            if (grid == null || buffer == null) { LastFailure = "the prop grid is not readable"; return; }

            float radius = snapshot.GaleRadius;
            if (!(radius > 0f)) return;

            Vec3 centre = snapshot.Centre;

            // 暴風域の矩形をセル座標へ。**マップの外へはみ出す分は落とす。**
            int minX = CellOf(centre.X - radius);
            int maxX = CellOf(centre.X + radius);
            int minZ = CellOf(centre.Z - radius);
            int maxZ = CellOf(centre.Z + radius);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width <= 0 || height <= 0) return;

            int total = width * height;
            if (_cursor >= total)
            {
                // ★ 一巡した。**回を進める** —— 進めないと、1 度助かった看板が
                //   二度と飛ばない（PropGaleModel.Takes の doc）。
                _cursor = 0;
                _round++;
            }

            // 種は台風ごと。**同じ台風なら何度読んでも同じ結果**である。
            uint seed = DeterministicRandom.Hash(snapshot.TyphoonId, 0x50524F50u);

            int end = _cursor + CellsPerTick;
            if (end > total) end = total;

            for (int i = _cursor; i < end; i++)
            {
                int cx = minX + (i % width);
                int cz = minZ + (i / width);
                _scannedLastTick++;

                if (cx < 0 || cx >= GridSide || cz < 0 || cz >= GridSide) continue;

                ushort id = grid[cz * GridSide + cx];
                int guard = 0;

                while (id != 0 && guard++ < 16384)
                {
                    ushort next = buffer[id].m_nextGridProp;

                    if (_takenLastTick < MaxTakenPerTick) TryTake(props, buffer, id, centre,
                                                                  snapshot, seed);

                    id = next;
                }
            }

            _cursor = end;
            LastFailure = null;
        }

        private static void TryTake(PropManager props, PropInstance[] buffer, ushort id,
                                    Vec3 centre, TyphoonSnapshot snapshot, uint seed)
        {
            // ★ 既に消えているスロットは触らない（Created が落ちている）。
            if ((buffer[id].m_flags & (ushort)PropInstance.Flags.Created) == 0) return;

            PropInfo info = buffer[id].Info;
            if (info == null) return;

            // ★ デカール・マーカーは「物」ではない。飛ばしても意味が無く、
            //   消すと地面の模様だけが欠ける。
            if (info.m_isDecal || info.m_isMarker) return;

            float size = SizeOf(info);
            float fragility = PropGaleModel.FragilityOf(size);
            if (fragility <= 0f) return;

            Vector3 position = buffer[id].Position;
            float dx = position.x - centre.X;
            float dz = position.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance > snapshot.GaleRadius) return;

            float wind = TyphoonProfile.WindAt(distance, snapshot.Intensity,
                                               snapshot.StormRadius);

            if (!PropGaleModel.Takes(id, _round, wind, fragility, seed)) return;

            props.ReleaseProp(id);
            _takenLastTick++;
            _takenTotal++;
        }

        /// <summary>
        /// プロップの代表寸法（m）。**当たり判定の箱のいちばん長い辺**を使う
        /// （<c>PropInfo.m_generatedInfo.m_size</c>）。読めなければ 0 ＝ 飛ばさない。
        /// </summary>
        private static float SizeOf(PropInfo info)
        {
            if (info.m_generatedInfo == null) return 0f;

            Vector3 s = info.m_generatedInfo.m_size;
            float longest = s.x;
            if (s.y > longest) longest = s.y;
            if (s.z > longest) longest = s.z;

            return longest;
        }

        private static int CellOf(float world)
        {
            return Mathf.FloorToInt((world + MapHalfExtent) / CellSizeMetres);
        }
    }
}
