namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// **バニラの延焼確率の割り算**を、④が踏み抜かないための模型。
    /// <b>Core なのでエンジンには一切触らない</b> —— ここにあるのは
    /// 「その割り算がいつ 0 で割るか」という<b>算術の事実</b>だけである。
    ///
    /// ── これが実機で落ちた（DivideByZeroException） ────────────────────
    ///
    /// <c>ThunderStormAI.GetFireSpreadProbability</c> の IL（本タスクで実測）:
    /// <code>
    /// IL_003E  num = (int)(SimulationManager.m_currentFrameIndex - data.m_activationFrame)
    /// IL_0050  ldc.i4 1500
    /// IL_0055  ldc.i4.8
    /// IL_0056  ldloc.0
    /// IL_0057  ldc.i4.s 10
    /// IL_0059  shr            // ★ shr.un ではない＝ num は符号付き
    /// IL_005A  add
    /// IL_005B  div            // ★ 整数除算。0 で割ると例外。
    /// </code>
    /// すなわち <c>1500 / (8 + (num &gt;&gt; 10))</c> である。
    /// <c>num</c> が負のとき算術シフトは**下方向に丸める**ので、
    /// <c>num ∈ [<see cref="UnsafeElapsedFirst"/>, <see cref="UnsafeElapsedLast"/>]</c>
    /// でちょうど <c>8 + (-8) = 0</c> になり、**その 1024 フレームだけ 0 除算する。**
    /// <c>num</c> がそれより小さいと商は負になるだけで例外は出ない ——
    /// **危ないのは窓の中だけ**であり、だから「たまたま動く」ことがある。
    ///
    /// ── なぜバニラは落ちないのに④は落ちたのか ──────────────────────
    ///
    /// <c>num</c> が負になるのは <c>m_activationFrame</c> が未来にあるとき、つまり
    /// 災害が **Emerging（発生予告中）** のあいだである。<c>StartDisaster</c> は
    /// <c>m_activationFrame = m_startFrame + m_emergingDuration</c> と書き
    /// （IL 実測）、Thunderstorm プレハブの <c>m_emergingDuration</c> は
    /// <b>8192</b>（sharedassets55 から実測。<c>m_radius</c> 4000 /
    /// <c>m_activeDuration</c> 8192 と同じ並び）。つまり台風を起こした瞬間の
    /// <c>num</c> はちょうど <c>-8192</c> ＝ **危険な窓の左端**である。
    ///
    /// この割り算は、<b>災害グループに属する建物や木が燃えている</b>ときにだけ
    /// 呼ばれる（<c>CommonBuildingAI.HandleFireSpread</c> /
    /// <c>TreeManager.HandleFireSpread</c>）。バニラの嵐は Active になるまで
    /// 火を出さないので <c>num &gt;= 0</c> でしか呼ばれない。ところが④は
    /// **クリックした瞬間から落雷を撒く**（<c>TyphoonLightning</c>）ので、
    /// Emerging のうちに火が付き、そこで初めてこの割り算が負の <c>num</c> で走る。
    ///
    /// ── ④の対処（<c>TyphoonSlot.Begin</c>）───────────────────────────
    ///
    /// **<c>m_activationFrame</c> を開始フレームまで引き寄せる。** ④の台風は
    /// 「押した瞬間にそこで始まる」ものであって、8192 フレームの発生予告を持たない
    /// （持たせると、持続時間 8192 フレームの台風が<b>一生 Emerging のまま終わる</b>）。
    /// こうすると <c>num</c> は常に 0 以上になり、除数は <see cref="MinSafeDivisor"/>
    /// 以上に固定される —— **窓に入る経路そのものが消える。**
    /// </summary>
    public static class VanillaFireSpread
    {
        /// <summary>割られる数（<c>ldc.i4 1500</c>）。値そのものは④は使わないが、事実として残す。</summary>
        public const int Numerator = 1500;

        /// <summary>除数の定数項（<c>ldc.i4.8</c>）。</summary>
        public const int DivisorBase = 8;

        /// <summary>経過フレームの算術シフト量（<c>ldc.i4.s 10</c>）。</summary>
        public const int ElapsedShift = 10;

        /// <summary><c>num &gt;= 0</c> のときの除数の最小値。<see cref="DivisorBase"/> と同じ。</summary>
        public const int MinSafeDivisor = DivisorBase;

        /// <summary>0 除算になる <c>num</c> の下端（含む）。<c>-8 * 1024</c>。</summary>
        public const int UnsafeElapsedFirst = -(DivisorBase << ElapsedShift);

        /// <summary>0 除算になる <c>num</c> の上端（含む）。<c>-7 * 1024 - 1</c>。</summary>
        public const int UnsafeElapsedLast = -((DivisorBase - 1) << ElapsedShift) - 1;

        /// <summary>
        /// バニラが数える経過フレーム <c>num</c>。<b>符号付きに落として返す</b> ——
        /// バニラ自身が <c>shr</c>（算術シフト）で読んでいるからで、
        /// ここを <c>uint</c> のまま扱うと「未来の活性化」が巨大な正の数に化けて、
        /// この型が守ろうとしている事実そのものが消える。
        /// </summary>
        public static int ElapsedOf(uint currentFrame, uint activationFrame)
        {
            return unchecked((int)(currentFrame - activationFrame));
        }

        /// <summary>
        /// バニラが実際に使う除数 <c>8 + (num &gt;&gt; 10)</c>。
        /// **0 を返しうる**（それがこの型の存在理由である）。
        /// </summary>
        public static int DivisorOf(int elapsed)
        {
            return DivisorBase + (elapsed >> ElapsedShift);
        }

        /// <summary>この <c>num</c> でバニラの割り算が 0 除算するか。</summary>
        public static bool DividesByZero(int elapsed)
        {
            return DivisorOf(elapsed) == 0;
        }

        /// <summary>
        /// この活性化フレームなら、<b>今このフレーム以降ずっと</b> 0 除算しないか。
        ///
        /// <c>activationFrame &lt;= currentFrame</c> であれば <c>num</c> は 0 以上から
        /// 単調に増えるだけなので、除数は <see cref="MinSafeDivisor"/> 以上のままである。
        /// 逆に未来の活性化は、たとえ今が窓の外でも<b>いずれ窓を通過する</b>
        /// （<c>num</c> は毎フレーム 1 ずつ増える）ので、安全とは言えない。
        /// </summary>
        public static bool IsSafeActivation(uint currentFrame, uint activationFrame)
        {
            return activationFrame <= currentFrame;
        }

        /// <summary>
        /// ④が <c>m_activationFrame</c> に書くべき値。<b>「今すぐ活性化」</b>である。
        ///
        /// <paramref name="startFrame"/> は <c>StartDisaster</c> が書いた
        /// <c>m_startFrame</c>（＝台風を起こしたフレーム）。そのまま返すのが原則だが、
        /// **0 だけは 1 に上げる** —— <c>m_activationFrame == 0</c> は
        /// 「活性化の予定が無い」という別の意味を持ち
        /// （<c>ThunderStormAI.IsStillEmerging</c> の <c>IL_0015</c> の <c>brfalse</c> が
        /// 0 を恒久的な Emerging として扱う）、そのまま書くと災害が永久に
        /// Emerging のまま残ってスロットを 1 個食い潰す。
        ///
        /// 1 を返した 1 フレームだけ <c>num</c> は <c>-1</c> になるが、
        /// 除数は <c>8 + (-1 &gt;&gt; 10) = 7</c> であって 0 ではない。
        /// </summary>
        public static uint SafeActivationFrame(uint startFrame)
        {
            return startFrame == 0u ? 1u : startFrame;
        }
    }
}
