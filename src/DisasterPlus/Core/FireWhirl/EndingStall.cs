namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 「終了処理に入ったのに終わらない旋風」の判定。
    ///
    /// なぜ要るか: ③の実装で誤っていた IL 前提のひとつが
    /// 「m_targetPos0 は遠方の目標」で、実際は火災の中心そのものだった。
    /// この取り違えが復活すると、終了処理に入った旋風が
    /// ArriveAtDestination に永遠に到達せず、消えないまま残り続ける。
    /// それでも例外は出ないし、ログも出ない。存在検査（パッチが当たっているか）
    /// では捕まらず、「動いた結果」を見て初めて分かる種類の破れなので、
    /// ここで振る舞いとして検査する。
    ///
    /// エンジン非依存の純粋関数として Core に置き、ユニットテストで固定する。
    /// </summary>
    public static class EndingStall
    {
        /// <summary>
        /// 「最大持続時間の何倍まで待つか」。
        ///
        /// 正常な終了は、渦のスピンダウン（角速度 1.0 から -0.05/step で約 20 step）＋
        /// ArriveAtDestination の待ちカウンタ（5 step）程度、つまりゲーム内 1 分にも
        /// 満たない。最大持続時間そのもの（既定 10 分）でも桁違いに余裕があるが、
        /// 誤検知は「壊れていないのに Degraded」という狼少年になり、この基盤の
        /// 信頼を落とす方が高くつく。倍率 2 なら既定設定で 20 ゲーム内分＝
        /// 正常値の 20 倍以上待つことになり、正常系で踏むことはまず無い。
        /// 一方 m_targetPos0 の取り違えが再発すれば永遠に終わらないので、
        /// 待ち時間をいくら伸ばしても必ず捕まる。
        /// </summary>
        public const float LifetimeMultiplier = 2f;

        /// <param name="endingMinutes">終了処理に入ってからのゲーム内経過（分）。</param>
        /// <param name="maxLifetimeMinutes">設定の最大持続時間（ゲーム内分）。</param>
        public static bool IsStuck(float endingMinutes, int maxLifetimeMinutes)
        {
            // 設定が壊れている（0 以下）ときに全件を stuck と報告しない。
            if (maxLifetimeMinutes <= 0) return false;
            return endingMinutes > maxLifetimeMinutes * LifetimeMultiplier;
        }
    }
}
