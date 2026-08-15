using ColossalFramework;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

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
        public static SavedBool OverlayEnabled;
        public static SavedInt OverlayHotkey;
        public static SavedInt LogChannelMask;
        public static SavedBool ForecastEnabled;
        public static SavedInt ForecastButtonX;
        public static SavedInt ForecastButtonY;
        public static SavedBool EarthquakeEnabled;
        public static SavedInt EarthquakeButtonX;
        public static SavedInt EarthquakeButtonY;
        public static SavedBool EarthquakeShakeBoost;
        public static SavedBool EarthquakeTsunamiChain;
        public static SavedInt EarthquakeTsunamiDelayMinutes;
        public static SavedBool EarthquakeLongPeriod;
        public static SavedInt EarthquakeLongPeriodStrength;
        public static SavedBool TyphoonEnabled;
        public static SavedInt TyphoonButtonX;
        public static SavedInt TyphoonButtonY;
        public static SavedInt TyphoonIntensity;
        public static SavedBool TyphoonWindDamage;
        public static SavedInt TyphoonWindStrength;
        public static SavedBool TyphoonFloodEnabled;
        public static SavedInt TyphoonFloodStrength;
        public static SavedBool TyphoonTornadoes;
        public static SavedInt TyphoonTornadoCount;
        public static SavedBool TyphoonCloudEnabled;
        public static SavedBool TyphoonVanillaCloudBoost;

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
            // 競合MOD（NDR）が居るときは既定 OFF。あちらが同じ解放をするので二重にやらない
            // （仕様 3.2 / 3.3(a)）。手動で ON にはできる。
            //
            // SavedBool の既定値はキーがまだ .cgs に無いときだけ効く。よって既に選択した
            // プレイヤーの値は保たれ、移行処理も要らない。新設キーを .exists で判定する
            // 方式（全員 false になる）の罠にも掛からない。
            IntensityUnlock       = new SavedBool("intensityUnlock", FileName, !ModCompat.NdrPresent, true);
            EarthquakeDamageOwner = new SavedInt("eqDamageOwner", FileName, EarthquakeOwnerOther, true);
            // 診断オーバーレイ。既定は OFF（開発者向け機能なので一般プレイヤーには出さない）。
            OverlayEnabled        = new SavedBool("diagOverlayEnabled", FileName, false, true);
            OverlayHotkey         = new SavedInt("diagOverlayHotkey", FileName, (int)KeyCode.F11, true);
            LogChannelMask        = new SavedInt("diagLogChannels", FileName,
                                                  DisasterPlus.Core.Diagnostics.LogChannel.DefaultMask, true);

            ForecastEnabled  = new SavedBool("forecastEnabled", FileName, true, true);
            // -1 = 未決定。ForecastPanelButton が初回インストール時に FreeSlotFinder で
            // 空き位置を探し、決まった座標をここへ書き戻す。以後はその座標を再利用する。
            ForecastButtonX  = new SavedInt("forecastButtonX", FileName, -1, true);
            ForecastButtonY  = new SavedInt("forecastButtonY", FileName, -1, true);

            EarthquakeEnabled = new SavedBool("earthquakeEnabled", FileName, true, true);
            // -1 = 未決定。ForecastButtonX/Y と全く同じ扱い
            // （EarthquakePanelButton が空き位置を決めて書き戻す。Task 4）。
            EarthquakeButtonX = new SavedInt("earthquakeButtonX", FileName, -1, true);
            EarthquakeButtonY = new SavedInt("earthquakeButtonY", FileName, -1, true);
            // 既定 ON にできるのは、強度 55（バニラ既定）で追加分が厳密に 0 になり、
            // そのとき CameraShakeBooster が m_cameraShake に一切書き込まないから
            // ——つまり既定の地震では挙動がバニラとビット単位で同一になる
            // （ShakeWaveform.IntensityFactor とそのユニットテストが固定している）。
            EarthquakeShakeBoost = new SavedBool("eqShakeBoost", FileName, true, true);

            // ★ 第 2 層は必ず既定 OFF にする（計画「第 2 層 — 足す」の共通規則）。
            //    バニラに存在しない挙動を既定で入れると、プレイヤーは
            //    「地震のあと勝手に津波が来る」原因が MOD だと気付く手段を持たない。
            //    上の EarthquakeShakeBoost が既定 ON にできるのは、既定の強度で
            //    追加分が厳密に 0 ＝ バニラとビット単位で同一になるからで、
            //    こちらにはその逃げ道が無い。
            EarthquakeTsunamiChain = new SavedBool("eqTsunamiChain", FileName, false, true);
            // ゲーム内分。範囲 5〜120 はスライダー側で縛る（.cgs の値は公開契約なので
            // 範囲外の値が入っていても読み捨てず、そのまま使う——遅延が長いだけで
            // 壊れる値ではない）。
            EarthquakeTsunamiDelayMinutes = new SavedInt("eqTsunamiDelay", FileName, 30, true);

            // ★ 第 2 層。こちらも必ず既定 OFF（上の EarthquakeTsunamiChain と同じ理由）。
            //    しかも津波より重い —— これは**バニラなら倒れなかった建物を倒す**。
            //    既定で入れると、プレイヤーは高層ビルが崩れた原因が MOD だと
            //    気付く手段を持たない（LongPeriodDamage のクラス doc）。
            EarthquakeLongPeriod = new SavedBool("eqLongPeriod", FileName, false, true);
            // 0〜10。0 で完全に無効（LongPeriodResponse.ExtraCollapseChance が
            // 厳密に 0 を返す）。範囲はスライダー側で縛るが、.cgs の値は公開契約なので
            // 範囲外が入っていても読み捨てず、使う側でクランプする。
            EarthquakeLongPeriodStrength = new SavedInt("eqLongPeriodStrength", FileName, 3, true);

            // ④台風。パネルの表示そのものは④が発生させない限り何も起きないので、
            // 有効化は既定 ON でよい（②の EarthquakeEnabled と同じ扱い）。
            // 台風を実際に起こすのはプレイヤーの明示的な操作だけである（T3）。
            TyphoonEnabled = new SavedBool("typhoonEnabled", FileName, true, true);
            // -1 = 未決定。ForecastButtonX/Y・EarthquakeButtonX/Y と全く同じ扱い
            // （TyphoonPanelButton が空き位置を決めて書き戻す。T5）。
            TyphoonButtonX = new SavedInt("typhoonButtonX", FileName, -1, true);
            TyphoonButtonY = new SavedInt("typhoonButtonY", FileName, -1, true);
            // 台風の強度。①が 255 まで解放済み（IntensityUnlock）。範囲 10〜255 は
            // スライダー側で縛るが、.cgs の値は公開契約なので範囲外が入っていても
            // 読み捨てず、使う側（TyphoonController.ClampIntensity）でクランプする。
            // ゲーム自身の嵐は 55。既定 120 はそれよりはっきり強いが、
            // 上限 255 ほど極端でもない値として選んだ。
            TyphoonIntensity = new SavedInt("typhoonIntensity", FileName, 120, true);

            // ★ 風害は既定 ON。②の第 2 層（津波連鎖・長周期）と判断が違う理由は
            //    TyphoonWind のクラス doc —— 台風はプレイヤーが明示的に起こすので、
            //    起きたことの原因が取り違えられない。設計書 §4.3 も既定 ON を指定。
            TyphoonWindDamage = new SavedBool("typhoonWind", FileName, true, true);
            // 0〜10。0 で完全に無効（WindDamageModel.CollapseChance が厳密に 0 を返す）。
            // 範囲はスライダーが縛るが、.cgs の値は公開契約なので使う側でクランプする。
            TyphoonWindStrength = new SavedInt("typhoonWindStrength", FileName, 3, true);

            // ★ 既定 ON（風害と同じ理由）。ただしこれは**セーブに焼き付く状態を触る
            //    唯一の機能**なので、復元経路は 3 箇所（終了時・アンロード時・保存時）
            //    から呼ばれる（TyphoonFlood のクラス doc）。
            TyphoonFloodEnabled = new SavedBool("typhoonFlood", FileName, true, true);
            TyphoonFloodStrength = new SavedInt("typhoonFloodStrength", FileName, 3, true);

            // ★ 随伴竜巻は**既定 OFF**（設計書 §2 が明示）。風害・氾濫と判断が違うのは、
            //    これが唯一「④の外の MOD に破壊を渡す」要素だからである ——
            //    バニラ竜巻の破壊は DisasterHelpers.DestroyStuff を通るので、
            //    Natural Disasters Renewal がいる環境ではあちらの竜巻設定に従う
            //    （IL 事実文書 §F-1、TyphoonTornado のクラス doc）。
            //    見た目が無料でバニラ品質という利点と引き換えなので、
            //    プレイヤーに明示的に選ばせる。
            TyphoonTornadoes = new SavedBool("typhoonTornado", FileName, false, true);
            // 0〜3。範囲はスライダーが縛るが、.cgs の値は公開契約なので範囲外が
            // 入っていても読み捨てず、使う側（TyphoonTornado.Step）でクランプする。
            TyphoonTornadoCount = new SavedInt("typhoonTornadoCount", FileName, 1, true);

            // ★ 雲は既定 ON。**見た目だけの機能で、ゲームの状態を 1 バイトも変えない**
            //    （main スレッドで Graphics.DrawMesh を出すだけ）。切っても他の 5 要素は
            //    そのまま動く（TyphoonCloud のクラス doc の独立性）。
            TyphoonCloudEnabled = new SavedBool("typhoonCloud", FileName, true, true);
            // バニラのスカイドームの雲を濃く・速くする。**存在しない環境がありうる**
            // （DLC・グラフィック設定。IL 事実文書 §C-2、PARTIAL）。無ければ黙って諦める。
            TyphoonVanillaCloudBoost = new SavedBool("typhoonCloudBoost", FileName, true, true);

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
