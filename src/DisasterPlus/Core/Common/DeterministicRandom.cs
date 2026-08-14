namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// (tick, id) から再現可能な乱数を作る。
    /// System.Random を使うと、セーブ・ロード・ユニットテストで同じ結果にならない。
    ///
    /// **Core/Earthquake/VanillaRandomizer とは別物。取り違えないこと。**
    /// こちらは状態を持たないハッシュで、**この MOD が自分で決めること**
    /// （③の延焼選定、②第 2 層の被害選定）に使う。中身はこの MOD の内部仕様なので
    /// 変えてよい。あちらはゲームの ColossalFramework.Math.Randomizer を
    /// ビット単位で写したもので、**バニラが引く値を先読みする**ためだけにあり、
    /// 1 ビットも変えてはいけない。
    ///
    /// 判別の規則:
    ///   その数字が「この MOD が発明した判断」を決めるなら DeterministicRandom。
    ///   「バニラが引く値と一致しなければならない」なら VanillaRandomizer。
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
