namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// (tick, id) から再現可能な乱数を作る。
    /// System.Random を使うと、セーブ・ロード・ユニットテストで同じ結果にならない。
    /// </summary>
    public static class DeterministicRandom
    {
        /// <summary>32bit の混合関数（MurmurHash3 の finalizer を 2 入力に拡張したもの）。</summary>
        public static uint Hash(uint a, uint b)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u;
                h ^= b + 0x85EBCA6Bu + (h << 6) + (h >> 2);
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>[0, 1) の一様乱数。</summary>
        public static float Unit(uint a, uint b)
        {
            // 上位 24bit を使う。float の仮数は 24bit なので、これ以上使っても精度が出ない。
            return (Hash(a, b) >> 8) * (1.0f / 16777216.0f);
        }
    }
}
