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

        /// <summary>
        /// **合成記象**（<see cref="SeismogramModel"/>）の同じフレームでの値。
        /// <see cref="Frames"/> と 1 対 1 で、有効なのは同じく先頭 <see cref="Count"/> 件。
        ///
        /// **null は「モデルを記録していない」であって「変位 0」ではない。**
        /// <c>ModSettings.EarthquakeSeismogram</c> が OFF のときはここが null になり、
        /// 表示側は第 2 層の線を 1 本も描かない。0 で埋めて渡すと、
        /// 「モデルは動いていたが揺れていなかった」という別の意味になる。
        /// </summary>
        public readonly float[] ModelValues;

        /// <summary>
        /// **火山性微動**（第 3 層、<c>Core/Volcano/VolcanicTremor</c>）の同じフレームでの値。
        /// <see cref="Frames"/> と 1 対 1 で、有効なのは同じく先頭 <see cref="Count"/> 件。
        ///
        /// **null は「記録していない」であって「揺れていない」ではない**
        /// （<see cref="ModelValues"/> とまったく同じ約束）。火山が揺れていない
        /// あいだはここが null になり、表示側は第 3 層の線を 1 本も描かない。
        ///
        /// ★ これは<b>この MOD のモデル</b>であって、バニラの式ではない
        ///   （<c>Game/Volcano/VolcanoTremorTrace</c> のクラス doc）。
        ///   凡例も第 2 層と同じ側に置くこと。
        /// </summary>
        public readonly float[] TremorValues;

        /// <summary>
        /// <see cref="Values"/>（第 1 層）に**意味があるか** ——
        /// バニラの地震が実際に進行しているか。
        ///
        /// **false のとき <see cref="Values"/> は全部 0 で、それは
        /// 「バニラの地震が無い」という値である**（読めなかったのではない。
        /// 火山だけが揺れている状態がこれで、そのとき第 1 層の線は描かない）。
        /// </summary>
        public readonly bool HasQuake;

        /// <summary>この観測点から⑤の影響範囲の中心までの水平距離（m）。火山が無ければ 0。</summary>
        public readonly float DistanceToVolcano;

        /// <summary><see cref="Frames"/> / <see cref="Values"/> の**有効な**件数。</summary>
        public readonly int Count;

        /// <summary>保持しているサンプルの中の最大振幅（絶対値）。</summary>
        public readonly float PeakAbsolute;

        /// <summary>合成記象の側の最大振幅（絶対値）。記録していなければ 0。</summary>
        public readonly float ModelPeakAbsolute;

        /// <summary>火山性微動の側の最大振幅（絶対値）。記録していなければ 0。</summary>
        public readonly float TremorPeakAbsolute;

        public SeismographTrace(ushort buildingId, Vec3 position, float distanceToEpicentre,
                                uint[] frames, float[] values, int count, float peakAbsolute,
                                float[] modelValues, float modelPeakAbsolute,
                                float[] tremorValues, float tremorPeakAbsolute,
                                bool hasQuake, float distanceToVolcano)
        {
            BuildingId = buildingId;
            Position = position;
            DistanceToEpicentre = distanceToEpicentre;
            Frames = frames;
            Values = values;
            Count = count;
            PeakAbsolute = peakAbsolute;
            ModelValues = modelValues;
            ModelPeakAbsolute = modelPeakAbsolute;
            TremorValues = tremorValues;
            TremorPeakAbsolute = tremorPeakAbsolute;
            HasQuake = hasQuake;
            DistanceToVolcano = distanceToVolcano;
        }

        /// <summary>合成記象の線を描いてよいか（記録があり、件数と噛み合っている）。</summary>
        public bool HasModel
        {
            get { return ModelValues != null && ModelValues.Length >= Count && Count > 0; }
        }

        /// <summary>火山性微動の線を描いてよいか（記録があり、件数と噛み合っている）。</summary>
        public bool HasTremor
        {
            get { return TremorValues != null && TremorValues.Length >= Count && Count > 0; }
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

            /// <summary>
            /// 合成記象（第 2 層）の側。**バニラの側と同じフレームに同じ件数だけ**
            /// 入れる —— 片方にしか入れないと、表示側で 2 本の線の時間軸がずれる。
            /// 設定が OFF のあいだは 1 件も入らない（<see cref="_modelRecorded"/>）。
            /// </summary>
            public readonly WaveformBuffer ModelBuffer = new WaveformBuffer(Capacity);

            /// <summary>
            /// 火山性微動（第 3 層）の側。**他の 2 本と同じフレームに同じ件数だけ**入れる。
            /// 火山が揺れていないあいだは 1 件も入らない（<see cref="_tremorRecorded"/>）。
            /// </summary>
            public readonly WaveformBuffer TremorBuffer = new WaveformBuffer(Capacity);

            /// <summary>この観測点から⑤の影響範囲の中心までの水平距離（m）。毎 tick 引き直す。</summary>
            public float DistanceToVolcano;
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

        /// <summary>
        /// 今のバッファに合成記象（第 2 層）が入っているか。
        ///
        /// **設定を途中で切り替えたときのため**にある。ON にした瞬間、バニラ側の
        /// バッファには既に数百件入っているのにモデル側は空なので、そのまま
        /// 2 本並べると時間軸が食い違った絵になる。切り替わりを見たら
        /// **両方まとめて捨てて**、そこから揃えて貯め直す。
        /// </summary>
        private static bool _modelRecorded;

        /// <summary>
        /// 今のバッファに火山性微動（第 3 層）が入っているか。
        /// <see cref="_modelRecorded"/> と同じ理由で要る —— 噴火が途中で始まると、
        /// 他の 2 本には既に数百件入っているのにこちらは空なので、
        /// そのまま並べると時間軸が食い違う。切り替わりを見たら**全部まとめて
        /// 捨てて**、そこから揃えて貯め直す。
        /// </summary>
        private static bool _tremorRecorded;

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
                _points[i].ModelBuffer.Clear();
                _points[i].TremorBuffer.Clear();
                _points[i].DistanceToVolcano = 0f;
            }
            _pointCount = 0;
            _modelRecorded = false;
            _tremorRecorded = false;
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

            // ★★ **バニラの地震が無くても、火山が揺れていれば記録する**
            //    （2026-08-22、所有者の依頼「火山性地震は震度計に記録されていない」）。
            //    以前はここが「地震が無い ⇒ 観測点ごと捨てる」だったので、
            //    火山だけが揺れているあいだ地震計は空欄のままだった。
            bool tremor = VolcanoTremorTrace.Active;

            if (quake == null && !tremor)
            {
                // 揺らしている者が 1 つも無い。設計書 §3.5 のとおり、ここで捨てる。
                if (_quakeId != 0 || _pointCount != 0) ClearAll();
                return;
            }

            if (quake != null && quake.DisasterId != _quakeId)
            {
                ClearAll();
                _quakeId = quake.DisasterId;
                _lastRescanFrame = frame;
                Rescan(quake.Epicentre.ToVec2());
            }
            else if (quake == null && _quakeId != 0)
            {
                // ★ 地震だけが終わった。**観測点は捨てない** —— 火山はまだ揺れており、
                //   ここで捨てると記象が 1 度途切れてから貯め直しになる。
                //   第 1 層はこの先 0 になり、<c>HasQuake</c> が false を名乗る。
                _quakeId = 0;
            }

            if (_pointCount == 0)
            {
                // ★ 唯一の再走査経路（クラス doc）。地震計を 1 個も持たない都市で
                //    揺れが続いている間だけ走り、1 個でも見つかれば以後は走らない。
                if (frame - _lastRescanFrame >= RescanIntervalFrames)
                {
                    _lastRescanFrame = frame;
                    Rescan(quake != null
                           ? quake.Epicentre.ToVec2()
                           : VolcanoTremorTrace.Centre.ToVec2());
                }
                if (_pointCount == 0) return;
            }

            // ★ ⑤の中心までの距離は**毎 tick 引き直す**。観測点は 4 個までなので
            //   費用は無視できるし、地震で並べ直した観測点にも必ず入る。
            if (tremor)
            {
                for (int i = 0; i < _pointCount; i++)
                {
                    _points[i].DistanceToVolcano =
                        VolcanoTremorTrace.DistanceFromCentre(_points[i].Position);
                }
            }

            // ★ バニラの地震の側が「実際に評価できる」か。
            //   m_activationFrame == 0 は「今」ではなく「未定」（§A-1 の罠）。
            //   m_activeDuration はプレハブ値で、読めていなければ揺れの窓が分からない
            //   （§A-0）—— そこを決め打つと、地震が終わった後も伸び続ける波形になる。
            //   **どれか 1 つでも欠けたら第 1 層は評価しない。火山の側は止めない。**
            uint activeDuration = 0u;
            bool quakeUsable = false;
            if (quake != null && quake.ActivationScheduled && snapshot.Prefab.Resolved)
            {
                activeDuration = snapshot.Prefab.ActiveDuration;
                quakeUsable = activeDuration != 0u;
            }

            if (!quakeUsable && !tremor) return;

            // ★ 第 2 層の合成記象。**設定が OFF なら 1 件も貯めない**（既定 OFF）。
            //   **バニラの地震が無いときも貯めない** —— あれは 1 回の断層破壊の
            //   モデルであって、火山性微動はそこに載らない。
            bool wantModel = ModSettings.EarthquakeSeismogram.value && quakeUsable;

            // 3 本のうちどれかの「入れる／入れない」が変わったら、**まとめて捨てる**。
            // 片方だけ空のまま並べると、時間軸の食い違った絵になる。
            if (wantModel != _modelRecorded || tremor != _tremorRecorded)
            {
                for (int i = 0; i < _points.Length; i++)
                {
                    _points[i].Buffer.Clear();
                    _points[i].ModelBuffer.Clear();
                    _points[i].TremorBuffer.Clear();
                }
                _modelRecorded = wantModel;
                _tremorRecorded = tremor;
                _cached = null;
                _dirty = true;
                _hasLastSample = false;
            }

            // 種は地震そのものから出す（フレーム番号を混ぜない）ので、
            // **同じ地震なら同じ記象になる**。VanillaRandomizer は使わない
            // —— 合成記象はこの MOD が自分で決めることである。
            SeismogramModel model = wantModel
                ? SeismogramModel.For(SeismogramSeed(quake), activeDuration)
                : new SeismogramModel();

            uint first = ShakeWaveform.FirstUnsampledFrame(
                _lastSampleFrame, _hasLastSample, frame, MaxSubSamplesPerTick);

            bool wrote = false;
            for (uint f = first; f <= frame; f++)
            {
                // ★ 1 tick に 1 サンプルでは足りない。m_currentFrameIndex は
                //    FinalSimulationSpeed（1/3/9）ずつ飛ぶのに、揺れの主成分は
                //    0.63 rad/frame（周期 ≒10 フレーム）なので、速度 3 では
                //    周期 ≒92 フレームの**偽の長周期波**に折り返す。
                //    DisplacementAt は e の閉じた式なので、飛んだフレームで評価するのは
                //    1 回評価するのと同じだけ「実測」である（ShakeWaveform の doc）。
                long e = 0L;
                bool quakeShaking = false;
                if (quakeUsable)
                {
                    e = (long)f - quake.ActivationFrame + ShakeWaveform.FrameOffset;
                    quakeShaking = ShakeWaveform.IsShaking(e, activeDuration);
                }

                // 誰も揺らしていないフレームは 1 件も入れない（3 本とも入れない）。
                if (!quakeShaking && !_tremorRecorded) continue;

                // t に m_referenceTimer は足さない。あれは main スレッドの描画補間用の
                // 値で、sim スレッドから読むべきものではない（フレーム単位の整数で足りる）。
                float t = e;

                for (int i = 0; i < _pointCount; i++)
                {
                    var point = _points[i];

                    // ★ バニラ式の distance を「カメラから」→「震源から」に置き換えた版
                    //    （設計書 §3.5）。式・定数・窓はバニラのまま。
                    //    ★ 揺れていないフレームの 0 は**「バニラの地震が無い」という値**
                    //      であって「読めなかった」ではない（<c>HasQuake</c> が名乗る）。
                    float value = quakeShaking
                        ? ShakeWaveform.DisplacementAt(point.DistanceToEpicentre, t)
                        : 0f;
                    point.Buffer.Add(f, value);

                    // ★ 3 本は**同じフレーム・同じ観測点**で評価する。
                    //   1 本だけ間引くと線の時間軸がずれる。
                    if (_modelRecorded)
                    {
                        point.ModelBuffer.Add(
                            f, quakeShaking
                               ? model.DisplacementAt(point.DistanceToEpicentre, t)
                               : 0f);
                    }

                    if (_tremorRecorded)
                    {
                        point.TremorBuffer.Add(
                            f, VolcanoTremorTrace.DisplacementAt(point.DistanceToVolcano, f));
                    }
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

                // ★ null は「記録していない」。0 で埋めて渡すと「揺れていない」に化ける。
                float[] modelValues = null;
                float modelPeak = 0f;
                if (_modelRecorded && point.ModelBuffer.Count == count)
                {
                    var modelFrames = new uint[count];
                    modelValues = new float[count];
                    point.ModelBuffer.CopyTo(modelFrames, modelValues);
                    modelPeak = point.ModelBuffer.PeakAbsolute;
                }

                // ★ 第 3 層も同じ約束（null は「記録していない」）。
                float[] tremorValues = null;
                float tremorPeak = 0f;
                if (_tremorRecorded && point.TremorBuffer.Count == count)
                {
                    var tremorFrames = new uint[count];
                    tremorValues = new float[count];
                    point.TremorBuffer.CopyTo(tremorFrames, tremorValues);
                    tremorPeak = point.TremorBuffer.PeakAbsolute;
                }

                traces.Add(new SeismographTrace(point.BuildingId, point.Position,
                                                point.DistanceToEpicentre,
                                                frames, values, written,
                                                point.Buffer.PeakAbsolute,
                                                modelValues, modelPeak,
                                                tremorValues, tremorPeak,
                                                _quakeId != 0, point.DistanceToVolcano));
            }

            _cached = traces.AsReadOnly();
            _cachedAtFrame = _lastSampleFrame;
            _dirty = false;
            return _cached;
        }

        /// <summary>
        /// 合成記象の種。**地震そのものから出す**（ID と発動フレーム）ので、
        /// 同じ地震のあいだは何度作り直しても同じ形になり、地震が変われば形も変わる。
        /// **フレーム番号そのものを混ぜないこと** —— 混ぜると tick ごとに別の記象になる。
        /// </summary>
        private static uint SeismogramSeed(EarthquakeReading quake)
        {
            return DeterministicRandom.Hash(quake.DisasterId, quake.ActivationFrame);
        }

        private static void ClearAll()
        {
            for (int i = 0; i < _points.Length; i++)
            {
                _points[i].Buffer.Clear();
                _points[i].ModelBuffer.Clear();
                _points[i].TremorBuffer.Clear();
                _points[i].DistanceToVolcano = 0f;
            }
            _pointCount = 0;
            _modelRecorded = false;
            _tremorRecorded = false;
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
        /// <param name="origin">
        /// 近い順を決める起点。**バニラの地震があればその震央、無ければ⑤の
        /// 影響範囲の中心**である（火山だけが揺れているときも観測点は要る）。
        /// </param>
        private static void Rescan(Vec2 origin)
        {
            _pointCount = 0;

            try
            {
                var bm = BuildingManager.instance;
                if (bm == null) return;

                var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
                if (buildings == null) return;

                Vec2 epicentre = origin;
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
                    point.DistanceToVolcano = 0f;
                    // ClearAll() で既に空だが、観測点の入れ替えとバッファの中身が
                    // 食い違う経路を将来作らないための保険。
                    point.Buffer.Clear();
                    point.ModelBuffer.Clear();
                    point.TremorBuffer.Clear();
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
