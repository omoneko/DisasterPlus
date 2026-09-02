using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>稼働している気象レーダーが街にあるか。</b>**sim スレッドが数え、main が読む。**
    ///
    /// ── 所有者の依頼（2026-09-02）────────────────────────────────
    ///
    /// &gt; 今後の天気予報タブ（気象レーダーを置くことで解禁）
    ///
    /// 予報を<b>建てて手に入れるもの</b>にする。天気が読めるのは観測しているから、
    /// というのは筋が通っているし、ND DLC の気象レーダーに<b>今まで無かった用途</b>を
    /// 与えることにもなる（バニラでは雷雨と竜巻の検知範囲を広げるだけ）。
    ///
    /// ── ★★ サービス種別を決め打ちしない ───────────────────────────
    ///
    /// <c>ItemClass.Service.Disaster</c> だろうと<b>推測して書かない</b>。
    /// プレハブ側から <c>m_class.m_service</c> を<b>読んで</b>覚える
    /// （<see cref="ResolveService"/>）。決め打ちして外れると、
    /// <b>レーダーを建てても永久に解禁されない</b>という、例外の出ない壊れ方をする。
    ///
    /// プレハブが 1 つも見つからない ＝ ND DLC を持っていない。そのときは
    /// <see cref="PrefabKnown"/> が false になり、パネルは
    /// 「解禁されていない」ではなく「DLC が要る」と言う。
    ///
    /// ── 費用 ────────────────────────────────────────────
    ///
    /// プレハブ走査は<b>都市ごとに 1 回</b>。数えるほうは
    /// <c>GetServiceBuildings</c> が返す<b>そのサービスの一覧だけ</b>を見るので、
    /// 建物バッファ 49152 件の全走査にはならない。しかも
    /// <see cref="IntervalMinutes"/> ごとにしか走らない —— レーダーは
    /// 1 分に何度も建ったり壊れたりしない。
    /// </summary>
    public static class WeatherRadarWatch
    {
        /// <summary>数え直す間隔（ゲーム内分）。</summary>
        private const float IntervalMinutes = 1f;

        /// <summary>
        /// 「動いている」の条件。<c>Active</c> は電気・道路・従業員が足りているとき
        /// だけ立つ（<c>WeatherRadarAI.GetColor</c> が IL_001D で同じ
        /// <c>131072</c> を見て、情報ビューの色を分けている）。
        /// **建てただけで停電している建物を数えない。**
        /// </summary>
        private const Building.Flags Working =
            Building.Flags.Created | Building.Flags.Active;

        private static float _minutesSinceScan;
        private static bool _serviceResolved;
        private static bool _prefabKnown;
        private static ItemClass.Service _service;

        private static volatile bool _hasWorking;
        private static volatile int _workingCount;
        private static bool _errorLogged;
        private static bool _announced;

        /// <summary>
        /// 稼働しているレーダーが 1 基以上あるか。**main スレッドから読んでよい。**
        /// </summary>
        public static bool HasWorkingRadar { get { return _hasWorking; } }

        /// <summary>稼働している基数（診断とパネル用）。</summary>
        public static int WorkingCount { get { return _workingCount; } }

        /// <summary>
        /// 気象レーダーのプレハブが見つかっているか。
        /// **false は「DLC を持っていない」の意味**で、「まだ建てていない」ではない。
        /// この 2 つを同じ文言にしないこと。
        /// </summary>
        public static bool PrefabKnown { get { return _prefabKnown; } }

        /// <summary>レベルアンロード時。**都市をまたいで持ち越さない。**</summary>
        public static void Reset()
        {
            _minutesSinceScan = 0f;
            _serviceResolved = false;
            _prefabKnown = false;
            _hasWorking = false;
            _workingCount = 0;
            _errorLogged = false;
            _announced = false;
        }

        /// <summary>**sim スレッド。** 毎 tick 呼んでよい（中で間引く）。</summary>
        public static void Poll(float deltaMinutes)
        {
            if (deltaMinutes > 0f) _minutesSinceScan += deltaMinutes;
            if (_minutesSinceScan < IntervalMinutes && _serviceResolved) return;
            _minutesSinceScan = 0f;

            try
            {
                if (!_serviceResolved)
                {
                    _serviceResolved = true;
                    _prefabKnown = ResolveService(out _service);

                    if (!_prefabKnown)
                    {
                        // ★ DLC が無い環境。以後は数えない（一覧そのものが無い）。
                        _hasWorking = false;
                        _workingCount = 0;
                        return;
                    }
                }

                if (!_prefabKnown) return;

                _workingCount = CountWorking(_service);
                bool has = _workingCount > 0;

                // ★ 解禁された瞬間だけ 1 行残す。毎分書かない。
                if (has && !_announced)
                {
                    _announced = true;
                    Log.Info("weather radar is running (" + _workingCount
                             + "); the coming-weather forecast is unlocked");
                }

                _hasWorking = has;
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("could not count the weather radars", e);
                }
                // ★ 数えられなかったときは**解禁しない**。解禁したまま固まるより、
                //   ロックされたままのほうが「何かがおかしい」と気付ける。
                _hasWorking = false;
                _workingCount = 0;
            }
        }

        /// <summary>
        /// 気象レーダーのプレハブを探し、その <c>ItemClass.Service</c> を覚える。
        /// **1 度だけ。** 見つからなければ false（＝ND DLC を持っていない）。
        /// </summary>
        private static bool ResolveService(out ItemClass.Service service)
        {
            service = ItemClass.Service.None;

            int count = PrefabCollection<BuildingInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
            {
                var info = PrefabCollection<BuildingInfo>.GetLoaded((uint)i);
                if (info == null) continue;
                if (!(info.m_buildingAI is WeatherRadarAI)) continue;
                if (info.m_class == null) continue;

                service = info.m_class.m_service;
                Log.Info("weather radar prefab '" + info.name + "' is in service "
                         + service + "; the coming-weather forecast will watch that list");
                return true;
            }

            Log.Info("no weather radar prefab found; the coming-weather forecast needs "
                     + "the Natural Disasters DLC");
            return false;
        }

        /// <summary>
        /// そのサービスの一覧から、稼働している気象レーダーを数える。
        ///
        /// ★ サービスの一覧には他の災害系建物（避難所・地震計など）も並ぶので、
        ///   <c>is WeatherRadarAI</c> で必ず絞ること。
        /// </summary>
        private static int CountWorking(ItemClass.Service service)
        {
            if (!Singleton<BuildingManager>.exists) return 0;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null || bm.m_buildings == null) return 0;

            var list = bm.GetServiceBuildings(service);
            if (list == null) return 0;

            var buildings = bm.m_buildings.m_buffer;
            if (buildings == null) return 0;

            int found = 0;
            for (int i = 0; i < list.m_size; i++)
            {
                ushort id = list.m_buffer[i];
                if (id == 0 || id >= buildings.Length) continue;
                if ((buildings[id].m_flags & Working) != Working) continue;

                var info = buildings[id].Info;
                if (info == null || !(info.m_buildingAI is WeatherRadarAI)) continue;

                found++;
            }

            return found;
        }
    }
}
