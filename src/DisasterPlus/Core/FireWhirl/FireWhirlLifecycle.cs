namespace DisasterPlus.Core.FireWhirl
{
    public enum FireWhirlVerdict
    {
        Continue,
        Dissipate,
    }

    /// <summary>
    /// 火災旋風 1 基の寿命。3 段構えで消える。
    ///   1. 発生元の火災が続いている限り存続
    ///   2. 発生条件を割り込んだ状態が猶予を超えて続いたら消滅
    ///   3. 絶対上限に達したら、火災が続いていても必ず打ち切る
    ///
    /// 3 は必須。延焼拡大は「延焼が増える → 条件を満たし続ける → 旋風が延命する」という
    /// 自己強化ループを作るので、上限が唯一の安全弁になる。
    /// </summary>
    public struct FireWhirlLifecycle
    {
        public readonly float ElapsedMinutes;
        public readonly float ConditionBrokenMinutes;

        private FireWhirlLifecycle(float elapsed, float broken)
        {
            ElapsedMinutes = elapsed;
            ConditionBrokenMinutes = broken;
        }

        public static FireWhirlLifecycle Start()
        {
            return new FireWhirlLifecycle(0f, 0f);
        }

        /// <param name="conditionMet">この時点でまだ発生条件（R 内に N 棟）を満たしているか。</param>
        public FireWhirlLifecycle Advance(float deltaMinutes, bool conditionMet)
        {
            if (deltaMinutes <= 0f) return this;

            // 条件が戻ったら猶予カウンタをリセットする。火勢のちらつきで消えないように。
            float broken = conditionMet ? 0f : ConditionBrokenMinutes + deltaMinutes;
            return new FireWhirlLifecycle(ElapsedMinutes + deltaMinutes, broken);
        }

        public FireWhirlVerdict Evaluate(FireWhirlConfig config)
        {
            if (ElapsedMinutes >= config.MaxLifetimeMinutes) return FireWhirlVerdict.Dissipate;
            if (ConditionBrokenMinutes >= config.ConditionGraceMinutes) return FireWhirlVerdict.Dissipate;
            return FireWhirlVerdict.Continue;
        }
    }
}
