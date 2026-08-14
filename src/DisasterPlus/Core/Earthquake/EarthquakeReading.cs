using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 進行中の地震 1 個ぶんの、不変な読み取り結果。
    /// ①の <c>ForecastReading</c> と同じ役割で、sim スレッドが作り main スレッドが読む。
    ///
    /// **ここには「読んだ値」しか入れない。** 推定も予測も持たせない
    /// （それらは表示側と第 2 層の仕事）。唯一の例外が <see cref="Radius"/> で、
    /// これは <see cref="Intensity"/> から一意に決まるバニラの式そのものなので、
    /// 半径を 2 箇所に持たないために計算プロパティにしてある。
    /// </summary>
    public class EarthquakeReading
    {
        /// <summary>災害バッファ上の添字。診断とログの識別子として使う。</summary>
        public readonly ushort DisasterId;

        /// <summary>震央（<c>DisasterData.m_targetPosition</c>）。</summary>
        public readonly Vec3 Epicentre;

        /// <summary>断層の向き（<c>DisasterData.m_angle</c>、ラジアン）。</summary>
        public readonly float AngleRadians;

        /// <summary><c>DisasterData.m_intensity</c>。バニラのランダム発生は 10〜100、本 MOD は 255 まで解放済み。</summary>
        public readonly byte Intensity;

        public readonly EarthquakePhase Phase;

        /// <summary>
        /// 測位済みか。地震にこれを立てるのは震央の <c>EarthquakeCoverage != 0</c>、
        /// すなわち**地震計だけ**（IL 事実文書 §A-2 / §C-2）。
        /// false ならこの地震はハザードマップに一切塗られない（§A-6）。
        /// </summary>
        public readonly bool Located;

        public readonly uint StartFrame;

        /// <summary>
        /// 予定されている（あるいは実測された）発動フレーム。
        /// **0 は「今」ではなく「未定」**。<see cref="ActivationScheduled"/> を必ず先に見ること。
        /// </summary>
        public readonly uint ActivationFrame;

        /// <summary>
        /// <c>m_activationFrame != 0</c>。§A-1 の罠そのもの。
        ///
        /// <c>SelfTrigger(64)</c> が立っていない地震は <c>EarthquakeAI.StartDisaster</c> が
        /// 即 return するため <c>m_activationFrame</c> が 0 のまま残り、
        /// <c>IsStillEmerging</c> が <c>m_activationFrame == 0</c> で常に true を返して
        /// **Emerging のまま永久に固まる**。この 0 をそのまま時刻計算へ流すと
        /// 「あと 4739 年」のような数字になるので、表示側は必ずここで分岐する。
        /// </summary>
        public readonly bool ActivationScheduled;

        /// <summary>
        /// 震央の <c>ImmaterialResourceManager.Resource.EarthquakeCoverage</c> の**生値**。
        /// バニラの警報式は <c>Min(coverage, 100)</c> を使うが、ここではクランプしない
        /// （生値と表示値の両方を診断に出せるようにするため。クランプは
        /// Task 7 の <c>WarningLeadTime</c> が行う）。
        ///
        /// <see cref="CoverageKnown"/> が false のときこの値は無意味。
        /// </summary>
        public readonly int CoverageAtEpicentre;

        /// <summary>
        /// カバレッジを実際に読めたか。
        ///
        /// 計画の型一覧には無いフィールドだが、①の <c>WeatherSnapshot.DisasterInfoAvailable</c> と
        /// 同じ理由で足している。カバレッジ 0 は「地震計が無い」という**意味のある実測値**であり、
        /// 読み取りに失敗したときに同じ 0 を返すと、両者が外見上区別できない捏造ゼロになる。
        /// 「地震計が無いのでハザードマップが空です」は本機能が出す最重要の説明なので、
        /// その根拠が読めなかったことを隠してはいけない。
        /// </summary>
        public readonly bool CoverageKnown;

        /// <summary>
        /// この地震の断層長 <c>L = m_crackLength * (0.5 + intensity*0.005)</c>（§A-3）。
        /// プレハブが解決できていないときは 0（＝不明）。
        /// </summary>
        public readonly float CrackLength;

        /// <summary>同じく断層幅 <c>W = m_crackWidth * (0.5 + intensity*0.005)</c>。不明なら 0。</summary>
        public readonly float CrackWidth;

        public EarthquakeReading(ushort disasterId, Vec3 epicentre, float angleRadians,
                                 byte intensity, EarthquakePhase phase, bool located,
                                 uint startFrame, uint activationFrame, bool activationScheduled,
                                 int coverageAtEpicentre, bool coverageKnown,
                                 float crackLength, float crackWidth)
        {
            DisasterId = disasterId;
            Epicentre = epicentre;
            AngleRadians = angleRadians;
            Intensity = intensity;
            Phase = phase;
            Located = located;
            StartFrame = startFrame;
            ActivationFrame = activationFrame;
            ActivationScheduled = activationScheduled;
            CoverageAtEpicentre = coverageAtEpicentre;
            CoverageKnown = coverageKnown;
            CrackLength = crackLength;
            CrackWidth = crackWidth;
        }

        /// <summary>全体円盤の半径 R。<see cref="SeismicIntensity.RadiusOf"/> をそのまま返す。</summary>
        public float Radius
        {
            get { return SeismicIntensity.RadiusOf(Intensity); }
        }
    }
}
