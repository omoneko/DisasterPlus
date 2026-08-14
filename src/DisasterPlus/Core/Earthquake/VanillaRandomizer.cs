namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// ゲームの <c>ColossalFramework.Math.Randomizer</c> を**ビット単位で写したもの**
    /// （ColossalManaged.dll。Assembly-CSharp ではない）。設計書 付録 A-1 が唯一の典拠で、
    /// そこに IL がそのまま載っている。**推測で書き換えないこと。**
    ///
    /// なぜ写す必要があるか: DisasterHelpers.DestroyBuildings は建物ごとに
    /// <c>new Randomizer(buildingID | (disasterID &lt;&lt; 16))</c> を作り、そこから
    /// 倒壊しきい値と出火しきい値を 1 個ずつ引く（IL 事実文書 §A-3）。この種は
    /// フレームにもステップにも依存しないので、**同じ種を再構成すれば、バニラが
    /// これから引く値をこちらで先に知ることができる**。②の第 1 層の中核
    /// （「この建物は震央から X m 以内なら倒れる」を予言ではなく事実として言う）は
    /// 全てこの再現の上に乗っている。1 ビットずれると、もっともらしい数字が
    /// 出続けたまま全部が嘘になる。
    ///
    /// **Core/Common/DeterministicRandom とは別物。片方を消さないこと。**
    ///   - その数字が「この MOD が発明した判断」を決めるなら DeterministicRandom。
    ///   - 「バニラが引く値と一致しなければならない」なら VanillaRandomizer。
    ///
    /// 実装上の注意:
    ///   - 乗算は必ず 64 bit で溢れる。<c>unchecked</c> を明示する（C# の既定と同じだが、
    ///     将来 CheckForOverflowUnderflow を有効化しても壊れないため）。
    ///   - 戻り値は**種を進める前**に作られる。順序を入れ替えると全部ずれる。
    ///   - ctor の乗算は long（conv.i8 の符号拡張が効く）。long と ulong の乗算・加算は
    ///     2 の補数で同一ビットになるので、ここでは ulong に寄せている。負の入力での
    ///     一致はテストで固定してある。
    ///   - これは **struct** で、引くたびに自分の種を書き換える。値渡しでコピーすると
    ///     そこから列が分岐する。呼び出し側はローカル変数に置いて使い切ること。
    ///
    /// net35 のビルドと net8.0 のテストビルドで同じ結果になることは、整数演算だけで
    /// 構成されていることから保証される。**ゲーム本体との一致**は実行時に
    /// Assumptions が本物の Randomizer と突き合わせて確認する（Task 5）。
    /// </summary>
    public struct VanillaRandomizer
    {
        /// <summary>Knuth MMIX の乗数。</summary>
        public const ulong Multiplier = 6364136223846793005UL;

        /// <summary>Knuth MMIX の増分。</summary>
        public const ulong Increment = 1442695040888963407UL;

        private ulong _seed;

        /// <summary>IL: <c>seed = 6364136223846793005 * (long)v + 1442695040888963407</c></summary>
        public VanillaRandomizer(int value)
        {
            unchecked
            {
                // (ulong)(long)value で conv.i8 の符号拡張をそのまま再現する。
                _seed = Multiplier * (ulong)(long)value + Increment;
            }
        }

        /// <summary>テストと前提検証のためだけに公開する。通常の利用では読まない。</summary>
        public ulong Seed { get { return _seed; } }

        /// <summary>
        /// IL: <c>戻り値 = (int)(((seed &gt;&gt; 32) * (ulong)max) &gt;&gt; 32)</c> のあとに
        /// <c>seed = M * seed + I</c>。DestroyBuildings が呼ぶのはこの UInt32 版で、
        /// <c>Int32(int)</c> というオーバーロードは**存在しない**。
        /// </summary>
        public int Int32(uint max)
        {
            unchecked
            {
                // ★ 戻り値を先に作る。ここを下に動かすと全ての引きが 1 個ずれる。
                //    (seed >> 32) も max も 32 bit なので、この乗算は溢れない。
                int result = (int)(((_seed >> 32) * (ulong)max) >> 32);
                _seed = Multiplier * _seed + Increment;
                return result;
            }
        }
    }
}
