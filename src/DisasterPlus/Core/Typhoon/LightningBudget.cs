namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 落雷キューの取り分の計算。**この型の仕事は「上限 20 発に当てないこと」である。**
    ///
    /// ── 当てると何が起きるか（IL 事実文書 §A-3） ──────────────────────
    ///
    /// <c>WeatherManager.QueueLightningStrike</c> は空きスロットが無く
    /// <c>m_lightningQueue.m_size &gt;= 20</c> のとき **false を返して要求を黙って捨てる**
    /// （本タスクで <c>IL_023B–024D</c> の <c>ldc.i4.s 20</c> を再確認した）。
    /// しかも捨てられるのは④のぶんとは限らない —— キューが埋まっている間は
    /// **宿主のバニラ雷雨が撃つはずだった落雷も、他 MOD の落雷も**同じように消える。
    /// 例外は 1 つも出ない。
    ///
    /// ── キューは読めない。だから「見積もって譲る」 ──────────────────
    ///
    /// <c>m_lightningQueue</c> は private の <c>FastList&lt;LightningStrike&gt;</c> で、
    /// <c>InstanceManager.GetPosition</c> の switch にも Lightning の分岐が無い（§A-2b）。
    /// **④はキューの残量を読めない。** リフレクションで覗く手もあるが、この MOD が
    /// まだ一度もやっていない種類の依存であり、しかもキューは sim スレッドが毎ステップ
    /// 詰め直すので覗いた瞬間の値には意味が薄い。代わりに<b>両側を計算で押さえる</b>:
    ///
    /// 1. **④が撃ったぶん**は④が数える（予定フレームの台帳 ＋ <see cref="HasExpired"/>）。
    ///    これは正確である
    /// 2. **宿主のバニラ雷雨が撃つぶん**は式から**上限**が出る。
    ///    <c>ThunderStormAI.SimulationStep</c>（本タスクで IL_010B–0175 を実測）:
    ///    <code>
    ///    c = min(100, (f - act) >>u 3, (act + dur - f) >>u 3)
    ///    c = (c * intensity + 50) / 100
    ///    n = Randomizer.Int32(max(1, c / 20), max(1, 1 + c / 10))
    ///    </code>
    ///    <b><c>m_randomSeed</c> を読まなくても上限が出せる</b>のが要点である
    ///    （<c>intensity</c> / <c>m_activationFrame</c> / <c>m_activeDuration</c> は
    ///    ④が全部知っている）。**平均ではなく上限を取る** —— 平均で見積もると、
    ///    運の悪い step にちょうど 20 を超える
    /// 3. ④の取り分は <see cref="Allowance"/>
    ///
    /// ── 環境落雷はどちらに倒したか（設計書 §4.2 の「意識的に選ぶ」）──────────
    ///
    /// <c>WeatherManager.SimulationStepImpl</c> 末尾は
    /// <c>m_currentRain &gt; 0.8f &amp;&amp; m_lightningQueue.m_size == 0</c> のとき
    /// 自前の落雷を積む（§A-3）。**条件は「キューが空」なので、④が常に 1 発以上
    /// 積んでいれば環境落雷は完全に止まる。** したがって:
    ///
    ///   - 20 発の予算から<b>環境落雷ぶんを引く必要は無い</b>（T4 からの申し送り）
    ///   - **④は「撒き続ける」に倒す。** 発数を 0 に絞る位相（眼の中など）を作ると、
    ///     そこだけ環境落雷が復活する。<see cref="Allowance"/> が
    ///     <c>inFlight == 0</c> のとき必ず 1 以上を返すことをテストが固定している
    ///
    /// ── <see cref="MinFreeSlots"/> を 0 にしないこと ─────────────────────
    ///
    /// 環境落雷を止めることと**キューを埋め切ること**は違う。埋め切ると、
    /// ゲーム側の <c>QueueLightningStrike</c> が常に false を返す状態になり、
    /// 宿主の嵐も他 MOD も落雷を積めなくなる。常に 2 枠を空けておく。
    ///
    /// **この型は Core にある。** <c>UnityEngine</c> も Cities API も使わない純算術で、
    /// 8 件のテストが上の 4 つの約束を固定している。
    /// </summary>
    public static class LightningBudget
    {
        /// <summary>
        /// 同時に飛べる落雷の数。**ゲームが実際に使っている数字**
        /// （§A-3 <c>IL_0246: ldc.i4.s 20</c>）。増やすと、超過した要求が黙って
        /// 捨てられる状態に自分から突っ込むことになる。
        /// </summary>
        public const int QueueCapacity = 20;

        /// <summary>
        /// 常に空けておく枠。ゲーム自身と他 MOD のために残す（クラス doc）。
        /// **0 にしないこと。**
        /// </summary>
        public const int MinFreeSlots = 2;

        /// <summary>
        /// <c>QueueLightningStrike</c> が要求を切り上げる最短の遅延（§A-3 IL_0000、
        /// <c>startFrame = Max(startFrame, currentFrameIndex + 15)</c>）。
        ///
        /// ④はこれを**自分で足してから**要求する。切り上げに任せると、
        /// ④の台帳に載る予定フレームと実際の発火フレームがずれ、
        /// <see cref="HasExpired"/> の判定が早まって在庫を実際より少なく数える。
        /// </summary>
        public const uint MinDelayFrames = 15u;

        /// <summary>
        /// 予定フレームを過ぎてからスロットが解放されるまで（§A-3、
        /// <c>sf + 45 &lt; m_currentFrameIndex</c> で <c>ReleaseInstance</c>）。
        /// **発火した時点ではなく、ここでキューから消える。**
        /// </summary>
        public const uint ExpiryFrames = 45u;

        /// <summary>
        /// ④が使う遅延の幅。0〜この値のあいだに散らす（実装は <c>TyphoonLightning</c>）。
        /// 長くするほど 1 発あたりのキュー占有時間が伸びるので、実質的に発数が減る。
        /// </summary>
        public const uint MaxDelayFrames = 256u;

        /// <summary>
        /// 宿主のバニラ雷雨がこのフレームに使うランプ値 <c>c</c>（§A-1 IL_010B–0145）。
        ///
        /// バニラの式は Active 分岐でしか走らないので、活性化前・持続時間の後は 0 を返す
        /// （バニラの <c>shr.un</c> は <c>f &lt; act</c> で巨大な値を出すが、そこは
        /// バニラ自身が通らない経路である）。<paramref name="activeDuration"/> が 0 の
        /// ときも 0 —— **プレハブが読めていないのだから、見積もれない。**
        /// </summary>
        public static int VanillaRampCount(uint frame, uint activationFrame,
                                           uint activeDuration, byte intensity)
        {
            if (activeDuration == 0u) return 0;
            if (frame < activationFrame) return 0;

            // uint 同士の加算はオーバーフローしうるので ulong で持つ。
            ulong end = (ulong)activationFrame + activeDuration;
            if (frame > end) return 0;

            ulong since = frame - activationFrame;
            ulong until = end - frame;

            int c = 100;
            int rise = (int)(since >> 3);
            int fall = (int)(until >> 3);
            if (rise < c) c = rise;
            if (fall < c) c = fall;

            return (c * intensity + 50) / 100;
        }

        /// <summary>
        /// <paramref name="rampCount"/> のとき宿主が 1 回の <c>SimulationStep</c> で
        /// 積みうる**最大**発数（§A-1: <c>n = Int32(max(1, c/20), max(1, 1 + c/10))</c>）。
        ///
        /// <c>Randomizer.Int32(min, max)</c> の上限側そのものを返す。**平均を返さないこと**
        /// —— 平均で見積もると、運の悪い step にちょうど 20 を超える。
        /// </summary>
        public static int VanillaMaxStrikes(int rampCount)
        {
            if (rampCount < 0) rampCount = 0;
            int max = 1 + rampCount / 10;
            return max < 1 ? 1 : max;
        }

        /// <summary>
        /// この tick に④が新たに積んでよい発数の上限。
        ///
        /// <c>QueueCapacity - MinFreeSlots - vanillaReserve - inFlight</c>（負なら 0）。
        /// **0 を返すのは正常な状態である** —— 強度が高いほど宿主の取り分が増え、
        /// 強度 170 あたりから④の取り分は 0 になる。そのときキューを非空に保っているのは
        /// 宿主の落雷なので、環境落雷は依然として抑制されている（クラス doc）。
        /// </summary>
        public static int Allowance(int inFlight, int vanillaReserve)
        {
            if (inFlight < 0) inFlight = 0;
            if (vanillaReserve < 0) vanillaReserve = 0;

            int allowance = QueueCapacity - MinFreeSlots - vanillaReserve - inFlight;
            return allowance < 0 ? 0 : allowance;
        }

        /// <summary>
        /// ゲームが要求を切り上げる下限フレーム（§A-3 IL_0000）。
        /// ④はここから <see cref="MaxDelayFrames"/> までのあいだに散らす。
        /// </summary>
        public static uint EarliestFrame(uint currentFrame)
        {
            return currentFrame + MinDelayFrames;
        }

        /// <summary>
        /// 予定フレーム <paramref name="scheduledFrame"/> の落雷が、
        /// <paramref name="currentFrame"/> の時点でキューから消えているか
        /// （§A-3: <c>sf + 45 &lt; m_currentFrameIndex</c>）。
        ///
        /// **発火は <c>sf == currentFrame</c> のときだが、スロットが空くのはここである。**
        /// 発火した瞬間に在庫から落とすと、実際にはまだ埋まっている枠を空きと数えて
        /// 上限に当たりに行くことになる。
        /// </summary>
        public static bool HasExpired(uint scheduledFrame, uint currentFrame)
        {
            // uint の巻き戻りを避けるため ulong で比較する。
            return (ulong)scheduledFrame + ExpiryFrames < currentFrame;
        }
    }
}
