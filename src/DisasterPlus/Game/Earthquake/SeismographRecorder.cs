using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 観測点 1 個ぶんの、不変な波形スナップショット。
    /// sim スレッドが作り、main スレッドが読むだけで書き換えない
    /// （<see cref="EarthquakeReading"/> と同じ規律）。
    ///
    /// <see cref="Frames"/> / <see cref="Values"/> は容量ぶんの長さを持つことがある。
    /// **有効なのは先頭 <see cref="Count"/> 件だけ**で、それより後ろは
    /// リングバッファの未使用領域に対応する 0 である。0 は変位 0（＝揺れていない）と
    /// 見分けが付かないので、長さではなく必ず <see cref="Count"/> で回すこと。
    /// </summary>
    public class SeismographTrace
    {
        /// <summary>観測点になった地震計の建物 ID。</summary>
        public readonly ushort BuildingId;

        public readonly Vec3 Position;

        /// <summary>震央からこの観測点までの水平距離（m）。</summary>
        public readonly float DistanceToEpicentre;

        public readonly uint[] Frames;
        public readonly float[] Values;

        /// <summary><see cref="Frames"/> / <see cref="Values"/> の**有効な**件数。</summary>
        public readonly int Count;

        /// <summary>保持しているサンプルの中の最大振幅（絶対値）。</summary>
        public readonly float PeakAbsolute;

        public SeismographTrace(ushort buildingId, Vec3 position, float distanceToEpicentre,
                                uint[] frames, float[] values, int count, float peakAbsolute)
        {
            BuildingId = buildingId;
            Position = position;
            DistanceToEpicentre = distanceToEpicentre;
            Frames = frames;
            Values = values;
            Count = count;
            PeakAbsolute = peakAbsolute;
        }

        /// <summary>最も新しいサンプルのフレーム。空なら 0。</summary>
        public uint NewestFrame
        {
            get { return Count <= 0 ? 0u : Frames[Count - 1]; }
        }
    }

    /// <summary>
    /// 地震計の位置で地動を観測して貯める。**sim スレッド専用。**
    ///
    /// ── 何を記録しているのか（捏造しないための境界）───────────────────
    ///
    /// バニラの地震計（<c>EarthquakeSensorAI</c>）は**時系列データを一切持たない**
    /// （IL 事実文書 §C-1、ABSENT）。フィールドは <c>m_detectionRange</c> だけで、
    /// 毎 tick 半径内に <c>EarthquakeCoverage</c> を撒くだけの装置である。
    /// 波形も履歴も直近の揺れも、ゲーム側には**存在しない**。したがってここに
    /// 出る線は「ゲーム内のセンサーが計測した値」では**ない**。
    ///
    /// 出る線が第 1 層（＝バニラの実測）を名乗ってよい理由は 1 つだけ:
    /// **バニラ自身の揺れの式**（<c>EarthquakeAI.RenderInstance</c>、§A-7）を、
    /// 別の点で評価しているからである。カメラを動かしているのと同じ式・同じ定数・
    /// 同じ窓を、カメラの代わりに地震計の位置で評価する。これは同じ式の別評価であって
    /// 近似でも模擬でもない。**その差は UI にも書く**（<c>Strings.EarthquakeWaveformNote</c>、
    /// 設計書 §3.5 が設計書と UI の両方に書けと要求している）。
    ///
    /// **Task 6 の追加シェイクは混ぜない。** <c>ShakeWaveform.IntensityFactor</c> は
    /// この MOD の補正であって、バニラの式ではない。波形はバニラの式そのものを出す。
    ///
    /// ── 観測点の探索コスト ────────────────────────────────
    ///
    /// 地震計を探すには建物バッファ全スロットの走査が要る。**地震が新しく始まった
    /// ときに 1 回だけ**走査し、震央に近い順に <see cref="MaxObservationPoints"/> 個まで
    /// 覚える。毎 tick は走査しない。
    ///
    /// **例外が 1 つだけある: 観測点が 0 個のとき。** その状態のパネルは
    /// 「地動を記録するには地震計を建ててください」と書いており、言われたとおりに
    /// 建てても次の地震まで何も起きない —— 説明の直後に、その説明どおりに
    /// 動かない画面が出る。ここだけは <see cref="RescanIntervalFrames"/> フレームに
    /// 1 回だけ走査をやり直す。**観測点が 1 個でも見つかっていれば二度と走査しない**
    /// ので、通常のプレイでは追加コストはゼロである（走査が走るのは、地震計を
    /// 持たない都市で地震が起きている間だけ）。地震の途中で**2 個目以降**を建てても
    /// その地震には反映されない —— これは既知の制限として残す。
    ///
    /// ── セーブには残さない（設計書 §3.5）──────────────────────────
    ///
    /// 進行中の地震が無くなったら捨てる。レベルアンロードでも捨てる（<see cref="Reset"/>）。
    /// </summary>
    public static class SeismographRecorder
    {
        /// <summary>観測点の上限。グラフは 1 枚しか描かないので、多く持っても使い道が無い。</summary>
        public const int MaxObservationPoints = 4;

        /// <summary>
        /// 1 観測点あたりの保持サンプル数。サンプルは**フレームごと**に取る
        /// （ゲーム速度によらず 1 フレーム 1 点。<see cref="MaxSubSamplesPerTick"/>）ので、
        /// 512 件はちょうど表示窓の <see cref="PlotFrameWindow"/> フレームぶんになる。
        /// </summary>
        public const int Capacity = 512;

        /// <summary>表示窓の幅（フレーム）。包絡線 2 周期ぶん（§A-7、256 フレーム周期）。</summary>
        public const int PlotFrameWindow = 512;

        /// <summary>
        /// 不変スナップショットを組み直す最短間隔（フレーム）。
        ///
        /// <see cref="Snapshot"/> は毎 sim tick 呼ばれる（<c>EarthquakeReader.Read</c> から）。
        /// 毎回 <see cref="Capacity"/> 件ぶんの配列を新しく作ると、揺れている間ずっと
        /// 毎秒数百 KB のごみを出し続けることになる。**変化していないときは前回と
        /// 同じ参照を返し**、変化していても最短この間隔でしか組み直さない。
        /// 512 フレームの窓を 320 px で描くので、8 フレームは 5 px 未満のずれである。
        /// </summary>
        private const int SnapshotIntervalFrames = 8;

        /// <summary>
        /// 観測点が 0 個のときだけ走る、地震計の再走査の間隔（フレーム）。
        /// 512 フレームは速度 1 でおよそ 10 秒。建てた地震計が「そのうち」出てくる
        /// 程度には短く、全スロット走査（65536 件）が体感に出ない程度には長い。
        /// </summary>
        private const int RescanIntervalFrames = 512;

        /// <summary>
        /// 1 sim tick で埋める最大サンプル数。<c>FinalSimulationSpeed</c> の最大値
        /// （ゲーム速度 3 で 9）に合わせてある。<see cref="ShakeWaveform.FirstUnsampledFrame"/>
        /// の doc に、なぜ 1 tick 1 サンプルでは足りないかの導出がある。
        /// </summary>
        private const int MaxSubSamplesPerTick = 9;

        /// <summary>観測点 1 個。バッファは使い回し、識別情報だけを再走査で上書きする。</summary>
        private class ObservationPoint
        {
            public ushort BuildingId;
            public Vec3 Position;
            public float DistanceToEpicentre;
            public readonly WaveformBuffer Buffer = new WaveformBuffer(Capacity);
        }

        private static readonly ObservationPoint[] _points = CreatePoints();

        /// <summary>震央に近い順に埋まっている件数。</summary>
        private static int _pointCount;

        // 再走査で使う一時領域。毎回確保しないためだけに static にしてある（sim 専用）。
        private static readonly ushort[] _scanIds = new ushort[MaxObservationPoints];
        private static readonly Vector3[] _scanPositions = new Vector3[MaxObservationPoints];
        private static readonly float[] _scanDistances = new float[MaxObservationPoints];

        /// <summary>今記録している地震の ID。0 は「記録していない」。</summary>
        private static ushort _quakeId;

        private static IList<SeismographTrace> _cached;
        private static uint _cachedAtFrame;
        private static bool _dirty;
        private static uint _lastSampleFrame;

        /// <summary>
        /// <see cref="_lastSampleFrame"/> に意味があるか。フレーム 0 は実在しうるので、
        /// 「まだ 1 件も取っていない」を 0 で表さない（この機能が他の全ての行で
        /// 守っている、ゼロと未読を分ける規律の内部版）。
        /// </summary>
        private static bool _hasLastSample;

        /// <summary>観測点 0 個での再走査を最後に行ったフレーム。0 は「まだ」。</summary>
        private static uint _lastRescanFrame;

        private static bool _scanErrorLogged;

        private static readonly IList<SeismographTrace> NoTraces =
            new List<SeismographTrace>(0).AsReadOnly();

        /// <summary>共有の空リスト。読み取り側専用。</summary>
        public static IList<SeismographTrace> EmptyTraceList
        {
            get { return NoTraces; }
        }

        /// <summary>
        /// 今どの地震を記録しているか（災害バッファ上の添字）。0 は記録していない。
        /// **sim スレッドから読むこと**（<c>EarthquakeReader</c> が snapshot に載せる）。
        /// </summary>
        public static ushort RecordingQuakeId
        {
            get { return _quakeId; }
        }

        /// <summary>レベルアンロード／ロード時。都市をまたいで何も持ち越さない。</summary>
        public static void Reset()
        {
            for (int i = 0; i < _points.Length; i++)
            {
                _points[i].BuildingId = 0;
                _points[i].Position = new Vec3(0f, 0f, 0f);
                _points[i].DistanceToEpicentre = 0f;
                _points[i].Buffer.Clear();
            }
            _pointCount = 0;
            _quakeId = 0;
            _cached = null;
            _cachedAtFrame = 0u;
            _dirty = false;
            _lastSampleFrame = 0u;
            _hasLastSample = false;
            _lastRescanFrame = 0u;
            // _scanErrorLogged は戻さない。「投げる」はこの DLL が参照しているゲームの
            // ビルドに対する事実であって、都市ごとの状態ではない
            // （EarthquakeReader._readErrorLogged と同じ判断）。
        }

        /// <summary>
        /// 1 sim tick ぶんのサンプリング。**sim スレッド専用**（建物バッファに触る）。
        ///
        /// **ポーズガードより下から呼ぶこと。** これは状態を進める処理で、
        /// ポーズ中に波形が伸び続けるのは嘘になる（ポーズ中はゲーム内時間が
        /// 進んでいないので、地動も進んでいない）。
        /// </summary>
        public static void Sample(EarthquakeSnapshot snapshot, uint frame)
        {
            if (snapshot == null || !snapshot.Valid) return;

            // 順位付けは QuakeSelection に一本化してある（以前ここには
            // EarthquakeReader.SelectDamagingQuake と 1 バイトも違わない複製があった）。
            var quake = QuakeSelection.SelectDamaging(snapshot.Quakes);
            if (quake == null)
            {
                // 進行中の地震が無い。設計書 §3.5 のとおり、ここで捨てる。
                if (_quakeId != 0 || _pointCount != 0) ClearAll();
                return;
            }

            if (quake.DisasterId != _quakeId)
            {
                ClearAll();
                _quakeId = quake.DisasterId;
                _lastRescanFrame = frame;
                Rescan(quake);
            }

            if (_pointCount == 0)
            {
                // ★ 唯一の再走査経路（クラス doc）。地震計を 1 個も持たない都市で
                //    地震が起きている間だけ走り、1 個でも見つかれば以後は走らない。
                if (frame - _lastRescanFrame >= RescanIntervalFrames)
                {
                    _lastRescanFrame = frame;
                    Rescan(quake);
                }
                if (_pointCount == 0) return;
            }

            // m_activationFrame == 0 は「今」ではなく「未定」（§A-1 の罠）。
            // 引き算に使うと途方も無い e になる。
            if (!quake.ActivationScheduled) return;

            // ★ m_activeDuration はプレハブ値で、まだ誰も実測していない（§A-0）。
            //    読めていなければ揺れの窓が分からないので、**1 サンプルも取らない**。
            //    ここでマグニチュードや持続時間を決め打ちすると、地震が終わった後も
            //    伸び続ける波形になる。
            if (!snapshot.Prefab.Resolved) return;
            uint activeDuration = snapshot.Prefab.ActiveDuration;
            if (activeDuration == 0u) return;

            // ★ 1 tick に 1 サンプルでは足りない。m_currentFrameIndex は
            //    FinalSimulationSpeed（1/3/9）ずつ飛ぶのに、揺れの主成分は
            //    0.63 rad/frame（周期 ≒10 フレーム）なので、速度 3 では
            //    周期 ≒92 フレームの**偽の長周期波**に折り返す。しかも §A-7 は
            //    バニラに長周期成分が無いことを確定させているので、それは
            //    第 1 層のグラフが第 2 層の現象を描いている状態になる。
            //    DisplacementAt は e の閉じた式なので、飛んだフレームで評価するのは
            //    1 回評価するのと同じだけ「実測」である（ShakeWaveform の doc）。
            uint first = ShakeWaveform.FirstUnsampledFrame(
                _lastSampleFrame, _hasLastSample, frame, MaxSubSamplesPerTick);

            bool wrote = false;
            for (uint f = first; f <= frame; f++)
            {
                long e = (long)f - quake.ActivationFrame + ShakeWaveform.FrameOffset;
                if (!ShakeWaveform.IsShaking(e, activeDuration)) continue;

                // t に m_referenceTimer は足さない。あれは main スレッドの描画補間用の
                // 値で、sim スレッドから読むべきものではない（フレーム単位の整数で足りる）。
                float t = e;

                for (int i = 0; i < _pointCount; i++)
                {
                    var point = _points[i];

                    // ★ バニラ式の distance を「カメラから」→「震源から」に置き換えた版
                    //    （設計書 §3.5）。式・定数・窓はバニラのまま。
                    float value = ShakeWaveform.DisplacementAt(point.DistanceToEpicentre, t);
                    point.Buffer.Add(f, value);
                }
                wrote = true;
            }

            if (!wrote) return;

            _lastSampleFrame = frame;
            _hasLastSample = true;
            _dirty = true;
        }

        /// <summary>
        /// main スレッドへ渡す不変スナップショット。**震央に近い順**に並ぶ。
        /// **sim スレッド専用**（<see cref="Sample"/> と同じスレッドから呼ぶこと）。
        ///
        /// 中身が変わっていなければ**前回と同じ参照**を返す。毎 sim tick 配列を
        /// 作り直すのはこの機能が最も出しやすい無駄で、しかも描画側は
        /// <see cref="SnapshotIntervalFrames"/> より細かい更新を見分けられない。
        /// </summary>
        public static IList<SeismographTrace> Snapshot()
        {
            if (_pointCount == 0) return NoTraces;

            if (_cached != null)
            {
                if (!_dirty) return _cached;
                if (_lastSampleFrame - _cachedAtFrame < SnapshotIntervalFrames) return _cached;
            }

            var traces = new List<SeismographTrace>(_pointCount);
            for (int i = 0; i < _pointCount; i++)
            {
                var point = _points[i];
                int count = point.Buffer.Count;

                var frames = new uint[count];
                var values = new float[count];
                int written = point.Buffer.CopyTo(frames, values);

                traces.Add(new SeismographTrace(point.BuildingId, point.Position,
                                                point.DistanceToEpicentre,
                                                frames, values, written,
                                                point.Buffer.PeakAbsolute));
            }

            _cached = traces.AsReadOnly();
            _cachedAtFrame = _lastSampleFrame;
            _dirty = false;
            return _cached;
        }

        private static void ClearAll()
        {
            for (int i = 0; i < _points.Length; i++) _points[i].Buffer.Clear();
            _pointCount = 0;
            _quakeId = 0;
            _cached = null;
            _cachedAtFrame = 0u;
            _dirty = false;
            _lastSampleFrame = 0u;
            _hasLastSample = false;
        }

        /// <summary>
        /// 地震計を探し、震央に近い順に <see cref="MaxObservationPoints"/> 個まで覚える。
        /// 呼んでよいのは**地震が新しく始まったとき**と、**観測点が 0 個のまま
        /// <see cref="RescanIntervalFrames"/> フレーム経ったとき**の 2 箇所だけ
        /// （全スロット走査なので毎 tick は不可。クラス doc に経緯がある）。
        ///
        /// 条件は <c>Created</c> が立っていることと <c>m_buildingAI is EarthquakeSensorAI</c>
        /// の 2 つだけ。稼働率は見ない —— 見るとしたら
        /// <c>ImmaterialResourceManager</c> の側であり、ここは「地震計という建物が
        /// どこに建っているか」を知るための走査である。
        /// </summary>
        private static void Rescan(EarthquakeReading quake)
        {
            _pointCount = 0;

            try
            {
                var bm = BuildingManager.instance;
                if (bm == null) return;

                var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
                if (buildings == null) return;

                Vec2 epicentre = quake.Epicentre.ToVec2();
                int found = 0;

                // 添字 0 は「無効」の予約枠なので 1 から回す。
                for (int i = 1; i < buildings.Length; i++)
                {
                    if ((buildings[i].m_flags & Building.Flags.Created) == Building.Flags.None) continue;

                    BuildingInfo info;
                    try
                    {
                        info = buildings[i].Info;
                    }
                    catch
                    {
                        continue;
                    }

                    // UnityEngine.Object の == オーバーロードで破棄済み(fake-null)も弾く。
                    if (info == null) continue;
                    if (!(info.m_buildingAI is EarthquakeSensorAI)) continue;

                    var p = buildings[i].m_position;
                    float distance = Mathf.Sqrt(epicentre.DistanceSquaredTo(new Vec2(p.x, p.z)));
                    found = InsertNearest(found, (ushort)i, p, distance);
                }

                for (int i = 0; i < found; i++)
                {
                    var point = _points[i];
                    point.BuildingId = _scanIds[i];
                    point.Position = new Vec3(_scanPositions[i].x, _scanPositions[i].y,
                                              _scanPositions[i].z);
                    point.DistanceToEpicentre = _scanDistances[i];
                    // ClearAll() で既に空だが、観測点の入れ替えとバッファの中身が
                    // 食い違う経路を将来作らないための保険。
                    point.Buffer.Clear();
                }
                _pointCount = found;
            }
            catch (System.Exception e)
            {
                // 地震 1 回につき 1 度しか通らない経路だが、Log.Error はスロットル
                // されないので確立した形に揃える（1 回大きく鳴らし、以後は Diag）。
                if (!_scanErrorLogged)
                {
                    _scanErrorLogged = true;
                    Log.Error("earthquake sensor scan failed", e);
                }
                else
                {
                    Log.Diag("EqSensors", "sensor scan failed: " + e.GetType().Name);
                }
                _pointCount = 0;
            }
        }

        /// <summary>
        /// 走査結果を距離の昇順に保つ挿入。戻り値は挿入後の件数。
        /// 上限を超える遠い地震計は捨てる（グラフは最も近い 1 個しか描かない）。
        /// </summary>
        private static int InsertNearest(int count, ushort id, Vector3 position, float distance)
        {
            int at = 0;
            while (at < count && _scanDistances[at] <= distance) at++;
            if (at >= MaxObservationPoints) return count;

            int last = count < MaxObservationPoints ? count : MaxObservationPoints - 1;
            for (int i = last; i > at; i--)
            {
                _scanIds[i] = _scanIds[i - 1];
                _scanPositions[i] = _scanPositions[i - 1];
                _scanDistances[i] = _scanDistances[i - 1];
            }

            _scanIds[at] = id;
            _scanPositions[at] = position;
            _scanDistances[at] = distance;

            return count < MaxObservationPoints ? count + 1 : MaxObservationPoints;
        }

        private static ObservationPoint[] CreatePoints()
        {
            var points = new ObservationPoint[MaxObservationPoints];
            for (int i = 0; i < points.Length; i++) points[i] = new ObservationPoint();
            return points;
        }
    }
}
