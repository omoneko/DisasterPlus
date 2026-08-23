using System;
using ColossalFramework;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 隆起のうち**地形を実際に触る部分**（高さの書き込み・<c>UpdateArea</c>・火口）。
    /// <c>VolcanoUplift.cs</c> から切り出したのは、あのファイルがプロジェクト規約の
    /// 800 行を超えたためで、**内容は 1 文字も変えていない**
    /// （<c>VolcanoClearing.Sweep.cs</c> / <c>VolcanoLava.Ignite.cs</c> と同じ形）。
    ///
    /// 規律は本体側のクラス doc がすべて持っている。特にここで守るのは 3 つ:
    ///   - <c>UpdateArea</c> は **1 tick にちょうど 1 回**（罠 3）
    ///   - <c>MakeCrater</c> は**呼ばない**（火口は高さプロファイルの一部。指摘①）
    ///   - <c>Begin/EndUpdateArea</c> は**呼ばない**（§D-12）
    ///
    /// **sim スレッド専用。**
    /// </summary>
    public static partial class VolcanoUplift
    {
        /// <summary>
        /// 準備が届いた範囲の全セルに、**その時刻における絶対目標**を書く（罠 2）。
        ///
        /// **書き込みだけで <c>UpdateArea</c> は呼ばない。** 書いた人が <c>UpdateArea</c> を
        /// 呼ぶまで誰も読まないのが地形の規律で（§D-12）、こちらは全域へ先に行き、
        /// 表示がタイル 1 枚ずつ追いつく形になる。
        /// </summary>
        private static bool WriteHeights(VolcanoFootprint footprint)
        {
            ushort[] raw = ReadRawHeights();
            if (raw == null || _baseRaw == null) return false;

            if (_profile == null) return false;

            float centreX = footprint.Centre.X;
            float centreZ = footprint.Centre.Z;
            float activeSquared = _activeRadius * _activeRadius;
            float height = footprint.HeightMetres;

            int written = 0;
            int clipped = 0;
            _dirtyValid = false;
            _dirtyMinX = 0;
            _dirtyMinZ = 0;
            _dirtyMaxX = 0;
            _dirtyMaxZ = 0;

            for (int z = _minZ; z <= _maxZ; z++)
            {
                float worldZ = (z - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                float dz = worldZ - centreZ;
                float dz2 = dz * dz;
                if (dz2 > activeSquared) continue;

                int rowRaw = z * RawStride;
                int rowBase = (z - _minZ) * _width;

                for (int x = _minX; x <= _maxX; x++)
                {
                    float worldX = (x - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                    float dx = worldX - centreX;
                    float d2 = dx * dx + dz2;
                    if (d2 > activeSquared) continue;

                    int cell = rowBase + (x - _minX);

                    // ★★ **山頂から外へ広がる**（UpliftSchedule.GrowthMetresAt の doc）。
                    //    profile × progress ではない —— あれは山全体が一様に膨らむ。
                    //
                    // ★★ <b>その規則は円錐にしか当てはまらない。</b>（2026-08-22）
                    //    <c>GrowthMetresAt</c> は <c>profileMetres &lt;= 0</c> で 0 を返すので、
                    //    **カルデラ（負のプロファイル）はこれでは 1 mm も掘れない。**
                    //    膨らみと陥没は「全体が一様に」動くのが正しい姿でもあるので、
                    //    素直に <c>profile × progress</c> で書く。
                    float grown = _stage == UpliftStage.Cone
                        ? UpliftSchedule.GrowthMetresAt(_profile[cell], height, _progress)
                        : _profile[cell] * _progress;

                    // 絶対目標なので progress は 1 を渡す（grown が既に「今の高さ」である）。
                    ushort target = UpliftSchedule.RawTargetAt(_baseRaw[cell], grown, 1f);

                    // ★★ **ゲームの高さの天井（1023.98 m）に当たったかを数える。**
                    //    当たれば山頂はそこで平らになる。**黙って平らな山を出さない** ——
                    //    プレイヤーからは「高さの設定が効いていない」にしか見えない。
                    //    天井を上げられない理由は <c>UpliftSchedule.CeilingClipped</c> の doc。
                    if (UpliftSchedule.CeilingClipped(_baseRaw[cell], grown, 1f)) clipped++;

                    int index = rowRaw + x;
                    // ★ バニラの MakeCrater と同じ「変わったときだけ書く」（§C-8 IL_01E7）。
                    if (raw[index] == target) continue;

                    raw[index] = target;
                    written++;

                    // ★ **流すのは実際に変わった範囲だけ**（クラス doc）。
                    //   ここで数えた外接矩形が、そのまま UpdateArea の対象になる。
                    if (!_dirtyValid)
                    {
                        _dirtyValid = true;
                        _dirtyMinX = x;
                        _dirtyMaxX = x;
                        _dirtyMinZ = z;
                        _dirtyMaxZ = z;
                    }
                    else
                    {
                        if (x < _dirtyMinX) _dirtyMinX = x;
                        if (x > _dirtyMaxX) _dirtyMaxX = x;
                        if (z < _dirtyMinZ) _dirtyMinZ = z;
                        if (z > _dirtyMaxZ) _dirtyMaxZ = z;
                    }
                }
            }

            _cellsWrittenLastTick = written;
            _ceilingClippedCells = clipped;
            return true;
        }

        /// <summary>
        /// **まだ画面に出ていない範囲を 1 tick に 1 回だけ** <c>UpdateArea</c> する（罠 3）。
        ///
        /// 手順:
        ///   1. この tick に書き換えた矩形（<see cref="_dirtyValid"/>）を、
        ///      まだ流し切っていない矩形へ union で足す。
        ///   2. 溜まった矩形が 1 回で出せるなら（<c>TileSplit.FitsSinglePass</c>）
        ///      そのまま出して**溜まりを空にする**。隆起の前半はここを通るので、
        ///      **変わった全域が毎 tick 画面に出る**。
        ///   3. 出せないならタイルを 1 枚ずつ総当たりし、一周したところで空にする。
        ///
        /// **<c>TileAt</c> / <c>ExpandForPass</c> が返すのは「そのまま渡す矩形」である。**
        /// ここで margin を足し直してはいけない —— 足すと 103×103 = 10609 セルになり、
        /// 10000 の閾値を跨いで毎回フラッシュする（<c>TileSplit</c> のクラス doc）。
        ///
        /// <c>surface</c> / <c>zones</c> を false にしてあるのはバニラの <c>MakeCrater</c> /
        /// <c>MakeCrack</c> と同じ引数だからである（§C-8 IL_021C）。
        /// </summary>
        private static void FlushPending()
        {
            int tMinX, tMinZ, tMaxX, tMaxZ;
            if (!_flush.Next(_dirtyValid, _dirtyMinX, _dirtyMinZ, _dirtyMaxX, _dirtyMaxZ,
                             out tMinX, out tMinZ, out tMaxX, out tMaxZ))
            {
                return;
            }

            // ★ 返ってきた矩形を**そのまま**渡す。margin を足し直さないこと
            //   （足すと 10000 の閾値を跨いで毎回途中フラッシュする）。
            TerrainModify.UpdateArea(tMinX, tMinZ, tMaxX, tMaxZ, true, false, false);
        }

        // ★★ かつてここに CarveCrater（DisasterHelpers.MakeCrater を 1 回）が在った。
        //    火口は高さプロファイルの一部になったので消した（2026-08-22、実機の指摘①）。
        //    **戻さないこと** —— 戻すと窪みの生まれる時刻がまた「隆起の最後」になり、
        //    しかも強制フラッシュが 1 回増える。形は Core/Volcano/VolcanoCrater にある。
    }
}
