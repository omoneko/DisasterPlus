using ColossalFramework;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 永続設定。
    ///
    /// ファイル名を MOD 名・アセンブリ名と同じ "DisasterPlus" にしてはいけない。
    /// 毎起動で "An element with the same key already exists ... Deleting" が出て
    /// 設定が消え、MOD がエラー扱いになり Workshop 公開まで壊れる。
    ///
    /// 保存されるキーと値は公開契約として扱う。列挙由来の整数値は意味を固定し、
    /// 項目を廃止するときも番号を詰めない（既存プレイヤーの .cgs の値が別物になるため）。
    /// </summary>
    public static class ModSettings
    {
        public const string FileName = "DisasterPlusSettings";

        /// <summary>地震の被害計算の担当。0 = 競合MODに任せる、1 = Disaster + が担当。
        /// この数値は .cgs に書かれる公開契約。値の意味を変えたり詰めたりしないこと。</summary>
        public const int EarthquakeOwnerOther = 0;
        public const int EarthquakeOwnerSelf = 1;

        private static bool _ready;

        public static SavedBool FireWhirlEnabled;
        public static SavedInt DetectRadius;
        public static SavedInt DetectCount;
        public static SavedInt MaxLifetimeMinutes;
        public static SavedInt SpreadStrength;
        public static SavedInt MinSeparation;
        public static SavedBool IntensityUnlock;
        public static SavedInt EarthquakeDamageOwner;

        public static void Ensure()
        {
            if (_ready) return;

            if (GameSettings.FindSettingsFileByName(FileName) == null)
            {
                GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });
            }

            FireWhirlEnabled      = new SavedBool("fireWhirlEnabled", FileName, true, true);
            DetectRadius          = new SavedInt("fwDetectRadius", FileName, 150, true);
            DetectCount           = new SavedInt("fwDetectCount", FileName, 12, true);
            MaxLifetimeMinutes    = new SavedInt("fwMaxLifetime", FileName, 10, true);
            SpreadStrength        = new SavedInt("fwSpreadStrength", FileName, 3, true);
            MinSeparation         = new SavedInt("fwMinSeparation", FileName, 300, true);
            IntensityUnlock       = new SavedBool("intensityUnlock", FileName, true, true);
            EarthquakeDamageOwner = new SavedInt("eqDamageOwner", FileName, EarthquakeOwnerOther, true);

            _ready = true;
        }

        /// <summary>設定値を Core の設定オブジェクトへ詰め替える。Core は SavedInt を知らない。</summary>
        public static FireWhirlConfig ToFireWhirlConfig()
        {
            Ensure();
            var c = FireWhirlConfig.Defaults();
            c.DetectRadius = DetectRadius.value;
            c.DetectCount = DetectCount.value;
            c.MaxLifetimeMinutes = MaxLifetimeMinutes.value;
            c.MinSeparation = MinSeparation.value;
            c.SpreadStrength = SpreadStrength.value;
            return c;
        }
    }
}
