using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **地震計が記録するぶんの火山性地震。sim スレッド専用。**
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// > 火山性地震は震度計に記録されていないのも修正してください。
    ///
    /// ②の <c>SeismographRecorder</c> は<b>バニラの地震が 1 つも無ければ観測点を
    /// まるごと捨てる</b>作りだったので、火山だけが揺れているあいだ地震計は
    /// 空欄のままだった。**揺れているのに記録が空**は、この MOD がいちばん
    /// 避けたい形（「読めなかった」と「値が 0」を混ぜる）そのものである。
    ///
    /// ── ★★ カメラの揺れとは<b>別の時計</b>で評価している ────────────────
    ///
    /// <see cref="VolcanoTremorShake"/>（main）は<b>実時間</b>（<c>EffectTimeDelta</c>）で
    /// 時計を進める —— カメラの揺れは目で見るものなので、そうでないと速度を
    /// 上げたときに震えが速くなりすぎる。
    /// こちらは記象なので<b>sim フレーム</b>を横軸に取る（②のプロットが全部そうである。
    /// <c>WaveformPlot</c> のクラス doc）。両者は<b>同じ閉じた式を別の時計で評価している</b>
    /// のであって、片方がもう片方のコピーではない。
    ///
    /// したがって<b>この線は「カメラが実際に足した変位」ではない</b>。
    /// 名乗りも <c>[Disaster + volcanic tremor]</c> ——
    /// ②の第 2 層（合成記象）と同じ「この MOD のモデル」の側であり、
    /// 第 1 層（バニラの式）を名乗らせないこと。
    ///
    /// ── 何を持っているか ────────────────────────────────
    ///
    /// 状態は「いつ揺れ始めたか」1 つだけである（<see cref="_startFrame"/>）。
    /// 活動度も減衰も <see cref="VolcanicTremor"/> の閉じた式で、フレームを
    /// 飛ばしても同じ値になる。
    /// </summary>
    public static class VolcanoTremorTrace
    {
        /// <summary>
        /// sim フレーム → 秒。**速度 1 のとき 1 秒 60 フレーム**である。
        /// 速度を上げるとフレーム番号のほうが速く進むので、記象は時間軸が
        /// 縮んで見える —— ②のプロットは全部その約束で描かれている
        /// （横軸はフレームであって実時間ではない）。
        /// </summary>
        private const float FramesPerSecond = 60f;

        private static bool _active;
        private static uint _startFrame;
        private static float _activityUnit;
        private static Vec3 _centre;
        private static float _reachMetres;
        private static uint _seed;

        /// <summary>今、火山が揺れているか。</summary>
        public static bool Active { get { return _active; } }

        /// <summary>影響範囲の中心（火口ではない。<see cref="VolcanoTremorShake"/> と同じ理由）。</summary>
        public static Vec3 Centre { get { return _centre; } }

        /// <summary>今の活動度 <c>[0,1]</c>。診断とパネル向け。</summary>
        public static float ActivityUnit { get { return _activityUnit; } }

        /// <summary>レベルアンロード・火山の中止で呼ぶ。</summary>
        public static void Reset()
        {
            _active = false;
            _startFrame = 0u;
            _activityUnit = 0f;
            _centre = new Vec3(0f, 0f, 0f);
            _reachMetres = 0f;
            _seed = 0u;
        }

        /// <summary>
        /// **sim スレッド、ポーズガードより下**（揺れは状態の前進である）。
        /// ⑤の位相から今の活動度を求め、揺れ始めのフレームを憶える。
        /// </summary>
        public static void Update(uint frame)
        {
            if (!ModSettings.VolcanoEnabled.value || !ModSettings.VolcanoQuake.value)
            {
                if (_active) Reset();
                return;
            }

            VolcanoFootprint footprint = VolcanoState.Footprint;
            if (!footprint.Valid)
            {
                if (_active) Reset();
                return;
            }

            float activity = VolcanoTremorActivity.For(VolcanoState.Phase,
                                                       VolcanoState.ProgressUnit,
                                                       VolcanoEruption.IntensityUnit,
                                                       VolcanoLava.CoolUnit);
            if (!(activity > 0f))
            {
                if (_active) Reset();
                return;
            }

            if (!_active)
            {
                _active = true;
                _startFrame = frame;
                _centre = footprint.Centre;
                _reachMetres = footprint.RadiusMetres * VolcanoTremorActivity.ReachRadiusFactor;
                // ★ 種は⑤の中心から作る（<see cref="VolcanoTremorShake"/> と同じ式）。
                //   同じ火山なら記象もカメラも同じ揺れになる。
                _seed = DeterministicRandom.Hash(
                    unchecked((uint)Round(footprint.Centre.X)),
                    unchecked((uint)Round(footprint.Centre.Z)));
            }

            _activityUnit = activity;
        }

        /// <summary>
        /// 観測点 <paramref name="distanceMetres"/> における、フレーム
        /// <paramref name="frame"/> の地動 <c>[-1,1]</c>。
        /// 揺れていなければ、あるいは届く範囲の外なら <b>0</b>。
        ///
        /// ★ ここの 0 は**「揺れていない」という値**であって「読めなかった」ではない
        ///   （記録するかどうかは <see cref="Active"/> が決める）。
        /// </summary>
        public static float DisplacementAt(float distanceMetres, uint frame)
        {
            if (!_active) return 0f;
            if (frame < _startFrame) return 0f;

            float attenuation = VolcanicTremor.AttenuationAt(distanceMetres, _reachMetres);
            if (!(attenuation > 0f)) return 0f;

            float seconds = (frame - _startFrame) / FramesPerSecond;

            // ★★ **②と同じ変位の単位へ直す**（<c>VolcanoTremorActivity.DisplacementGain</c>）。
            //    記象の縦の尺度は 3 本で共通なので、生の <c>[-1,1]</c> のまま入れると
            //    **火山性微動だけがバニラの本震より 1.7 倍大きい絵**になる。
            //    カメラの側と同じ倍率を使うのがその担保である。
            return VolcanicTremor.DisplacementAt(_seed, seconds, _activityUnit)
                   * attenuation * VolcanoTremorActivity.DisplacementGain;
        }

        /// <summary>観測点から⑤の中心までの水平距離（m）。</summary>
        public static float DistanceFromCentre(Vec3 position)
        {
            float dx = position.X - _centre.X;
            float dz = position.Z - _centre.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        private static int Round(float v)
        {
            return (int)System.Math.Floor(v + 0.5f);
        }
    }
}
