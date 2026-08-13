namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// ログチャンネルのビットフラグ。
    ///
    /// このビット位置は .cgs に保存される公開契約であり、凍結扱いとする。
    /// 値を詰め直したり並べ替えたりしてはいけない。チャンネルを廃止するときも
    /// ビットを残し、UI から外すだけにする。
    /// </summary>
    public static class LogChannel
    {
        public const int General    = 1;
        public const int FireWhirl  = 2;
        public const int Forecast   = 4;
        public const int Earthquake = 8;
        public const int Typhoon    = 16;
        public const int Volcano    = 32;
        public const int Diagnostics = 64;

        /// <summary>
        /// 既定マスク。General だけを有効にする。
        ///
        /// 0 にしてはいけない。チャンネル指定のない既存の Log.Diag(key, msg) は
        /// General 扱いになるため、0 にすると現在出ている診断ログが黙って消え、
        /// docs/playtest-checklist.md が参照している実機確認の手順が壊れる。
        /// </summary>
        public const int DefaultMask = General;

        /// <summary>channel のビットが全て mask に立っていれば true。</summary>
        public static bool IsEnabled(int channel, int mask)
        {
            if (channel == 0) return false;
            return (mask & channel) == channel;
        }
    }
}
