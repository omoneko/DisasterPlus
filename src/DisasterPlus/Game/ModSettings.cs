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

        /// <summary>
        /// ★★ **退役した保存キー（forecastButtonX/Y・earthquakeButtonX/Y・
        ///     typhoonButtonX/Y・volcanoButtonX/Y の 8 本）。**
        ///
        /// ①②④⑤のボタンはバニラの災害パネルの中に置かれるようになり、位置は
        /// パネル自身の autolayout が決める（<c>DisasterPanelBar</c>）。したがって
        /// この 8 本を読む場所はもう 1 つも無い。
        ///
        /// **それでも宣言は残す。** .cgs のキーと値は公開契約であり、
        /// - 宣言を消すとキーだけが .cgs に取り残され、後日この名前が
        ///   *別の意味で* 復活したときに、古い座標が新しい設定として読まれる
        /// - 番号や名前を詰め直さない、という本 MOD の規律と同じ理由
        ///
        /// **この 8 本を別の意味で再利用してはいけない。** 新しい設定には
        /// 新しいキー名を付けること。
        /// </summary>
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

        /// <summary>
        /// **第 2 層。合成記象（P 波・S 波・コーダ）**。既定 OFF。
        ///
        /// ON にすると 2 つのことが同時に起きる:
        ///   1. 波形グラフに <c>[Disaster + model]</c> の線が 1 本増える（バニラの線は残る）
        ///   2. カメラの揺れが、バニラの 2 本の正弦波の代わりに合成記象の形になる
        ///
        /// ★ **既定 OFF を外さないこと。** <c>EarthquakeShakeBoost</c> が既定 ON に
        ///   できるのは、強度 55（バニラ既定）で追加分が**厳密に 0** ＝ 挙動が
        ///   バニラとビット単位で同一になるからで（<c>ShakeWaveform.IntensityFactor</c>）、
        ///   こちらにはその逃げ道が無い —— 合成記象はどの強度でもバニラと違う形である。
        ///   これは<c>EarthquakeLongPeriod</c> と同じ扱いになる。
        /// </summary>
        public static SavedBool EarthquakeSeismogram;
        public static SavedBool TyphoonEnabled;
        public static SavedInt TyphoonButtonX;
        public static SavedInt TyphoonButtonY;
        public static SavedInt TyphoonIntensity;
        public static SavedBool TyphoonWindDamage;
        public static SavedInt TyphoonWindStrength;
        public static SavedBool TyphoonFloodEnabled;
        public static SavedInt TyphoonFloodStrength;
        public static SavedBool TyphoonSouthernHemisphere;
        /// <summary>
        /// **退役キー（随伴竜巻）。読む場所はもう 1 つも無い。**
        ///
        /// バニラの竜巻災害を台風に随伴させる機能は撤去された。持ち主の指示
        /// 「竜巻を発生させずに竜巻の被害だけを複数発生させてください」に対して、
        /// ④は竜巻の実体を作らず <c>TyphoonGust</c> の局所被害域だけを出す。
        ///
        /// **それでも宣言は残す。**<c>ForecastButtonX</c> の doc と同じ理由で、
        /// .cgs のキーと値は公開契約だからである。
        /// **この 2 本を別の意味で再利用してはいけない** —— 新しい設定
        /// （<see cref="TyphoonGustEnabled"/> / <see cref="TyphoonGustStrength"/>）には
        /// 新しいキー名を付けてある。撤去したことは
        /// <c>Strings.TyphoonTornadoRetiredNote</c> が設定画面で名乗る。
        /// </summary>
        public static SavedBool TyphoonTornadoes;
        public static SavedInt TyphoonTornadoCount;

        /// <summary>竜巻並みの局所被害を出すか（**竜巻の実体は作らない**）。</summary>
        public static SavedBool TyphoonGustEnabled;

        /// <summary>その強さ 0〜10。0 で完全に無効。</summary>
        public static SavedInt TyphoonGustStrength;
        public static SavedBool TyphoonCloudEnabled;
        public static SavedBool TyphoonVanillaCloudBoost;

        /// <summary>
        /// 暴風雨の演出（横殴りの飛沫と、市民・車の吹き飛ばし）。
        ///
        /// ★ **建物にも道路にも樹木にも触れない。** 飛沫は main スレッドの描画だけ、
        ///   吹き飛ばしは <c>DisasterHelpers.AddWind</c>（市民と車だけ）である。
        ///   だから風害（<see cref="TyphoonWindDamage"/>）とは別のつまみにしてある ——
        ///   「被害は要らないが嵐は見たい」も、その逆も選べる。
        /// </summary>
        public static SavedBool TyphoonStormFx;

        /// <summary>
        /// 台風の風の音。**⑤の噴火音と同じ経路**（<c>AudioManager.EffectGroup</c>）なので、
        /// プレイヤーの効果音スライダーとミュートはゲームが掛ける。
        /// ④が鳴らすのは**1 本だけ**である（<c>EffectGroup</c> の席を奪わない）。
        /// </summary>
        public static SavedBool TyphoonStormSound;
        public static SavedBool VolcanoEnabled;
        public static SavedInt VolcanoButtonX;
        public static SavedInt VolcanoButtonY;

        /// <summary>
        /// 火山の形態。**.cgs に書かれる公開契約なので番号を詰め直さない**
        /// （<c>DisasterPlus.Core.Volcano.VolcanoForm</c> と同じ値）。
        ///
        /// ★ フィールド名が <c>VolcanoShapeSetting</c> なのは、Core の型名
        ///   <c>VolcanoShape</c>（3 形態のプロファイルという内容そのもの）と
        ///   衝突するからである。**保存キーの文字列 "volcanoShape" は変えない。**
        /// </summary>
        public static SavedInt VolcanoShapeSetting;

        /// <summary>火山の半径（m）。範囲は形態ごとに違うので使う側でクランプする。</summary>
        public static SavedInt VolcanoRadius;

        /// <summary>火山の最終高（m）。同上。</summary>
        public static SavedInt VolcanoHeight;

        /// <summary>
        /// 準備（破壊）の前線が隆起の前線より何メートル先を走るか（m）。
        /// 0 にすると「壊した直後のセルを同じ tick で上げる」ことになり、余裕が無くなる。
        /// </summary>
        public static SavedInt VolcanoClearingLeadMetres;

        /// <summary>
        /// 山肌の凹凸の強さ（%）。**0 で今日どおりの滑らかな円錐**、100 が形態ごとの既定、
        /// 上限は <c>VolcanoRelief.MaxStrengthUnit</c>（150）である。
        ///
        /// これ 1 本だけを出しているのは、起伏の性格（谷の本数・波長・粗さ）が
        /// 形態ごとに <c>Core/Volcano/VolcanoRelief</c> の表で決まっていて、
        /// プレイヤーが決めるのは「どのくらい効かせるか」だけだからである。
        /// **つまみを増やさない。**
        /// </summary>
        public static SavedInt VolcanoReliefStrength;

        /// <summary>
        /// 隆起にかけるゲーム内分。<c>UpliftSchedule.TotalTicksFor</c> が
        /// 「山頂が毎 tick 1/64 m 以上動く」上限で切り詰めるので、長すぎる値を
        /// 入れても無言で止まることは無い。
        /// </summary>
        public static SavedInt VolcanoUpliftMinutes;

        /// <summary>
        /// 噴煙を描くか（T7）。**切っても隆起も溶岩もそのまま動く** ——
        /// 噴火の描画は main スレッドだけの機能で、ゲームの状態を 1 つも変えない。
        /// </summary>
        public static SavedBool VolcanoEruptionFx;

        /// <summary>
        /// 噴火の音を鳴らすか。**切っても隆起も溶岩も噴煙もそのまま動く** ——
        /// 音は main スレッドだけの機能で、ゲームの状態を 1 つも変えない。
        ///
        /// ★ 音量そのものはここでは持たない。**プレイヤーの効果音スライダーと
        ///   ミュートがそのまま効く**（<c>VolcanoEruptionAudio</c> のクラス doc）ので、
        ///   2 本目の音量つまみを作ると、どちらが効いているのか分からなくなる。
        /// </summary>
        public static SavedBool VolcanoEruptionSound;

        /// <summary>
        /// 火口から出す溶岩の本数（T8）。**0 で完全に無効**（溶岩も着火も出ない）。
        /// 上限は <c>VolcanoLava.MaxFlows</c> が使う側でクランプする。
        /// </summary>
        public static SavedInt VolcanoLavaFlows;

        /// <summary>
        /// 溶岩の通り道に火を付けるか（T8）。**切っても溶岩は流れる**（見た目だけになる）。
        /// </summary>
        public static SavedBool VolcanoLavaFire;

        /// <summary>
        /// 溶岩の面を描くか（T9）。**切っても溶岩は流れ、地面を焦がし、建物に火を付ける**
        /// —— 描画は main スレッドだけの機能で、ゲームの状態を 1 つも変えない。
        /// </summary>
        public static SavedBool VolcanoLavaRender;

        /// <summary>
        /// 斜面を下る土煙の帯（「火砕流」の代用）を出すか。
        /// **これは火砕流の再現ではない** —— ゲームに火砕流のエフェクトは 1 つも無く、
        /// 出しているのは建物崩壊の粉塵を溶岩の経路へ流したものである
        /// （<c>VolcanoPyroclasticFx</c> のクラス doc）。
        /// **切っても噴火も溶岩も何も変わらない**（この帯は何も壊さない）。
        /// </summary>
        public static SavedBool VolcanoPyroclasticFx;

        /// <summary>形態の保存値（公開契約）。<c>VolcanoForm</c> と同じ番号。</summary>
        public const int VolcanoShapeShield = 0;
        public const int VolcanoShapeStrato = 1;
        public const int VolcanoShapeDome = 2;

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
            // ★ 退役キー。読む場所はもう無いが、キーは公開契約なので宣言を残す
            //   （ForecastButtonX の doc。別の意味で再利用しないこと）。
            ForecastButtonX  = new SavedInt("forecastButtonX", FileName, -1, true);
            ForecastButtonY  = new SavedInt("forecastButtonY", FileName, -1, true);

            EarthquakeEnabled = new SavedBool("earthquakeEnabled", FileName, true, true);
            // ★ 退役キー。ForecastButtonX/Y と全く同じ扱い（同 doc）。
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

            // ★ 第 2 層その 3。既定 OFF（ModSettings.EarthquakeSeismogram の doc）。
            //    OFF のあいだ、記録も描画もカメラの揺れも**今日と 1 ビットも違わない**。
            EarthquakeSeismogram = new SavedBool("eqSeismogram", FileName, false, true);

            // ④台風。パネルの表示そのものは④が発生させない限り何も起きないので、
            // 有効化は既定 ON でよい（②の EarthquakeEnabled と同じ扱い）。
            // 台風を実際に起こすのはプレイヤーの明示的な操作だけである（T3）。
            TyphoonEnabled = new SavedBool("typhoonEnabled", FileName, true, true);
            // ★ 退役キー。ForecastButtonX/Y と全く同じ扱い（同 doc）。
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

            // ★ 危険半円（進行方向のどちら側を強くするか）。**既定は北半球＝右**。
            //    実在の台風では渦の回転と移動が足し算になる側が強く、北半球では
            //    進行方向の右、南半球では左になる（TrackBias のクラス doc）。
            //    既定を false（＝北半球）にするのは、CS のマップの大半が
            //    北半球の街を想定して作られているからで、物理的な根拠ではない。
            TyphoonSouthernHemisphere =
                new SavedBool("typhoonSouthernHemisphere", FileName, false, true);

            // ★ 既定 ON（風害と同じ理由）。ただしこれは**セーブに焼き付く状態を触る
            //    唯一の機能**なので、復元経路は 3 箇所（終了時・アンロード時・保存時）
            //    から呼ばれる（TyphoonFlood のクラス doc）。
            TyphoonFloodEnabled = new SavedBool("typhoonFlood", FileName, true, true);
            TyphoonFloodStrength = new SavedInt("typhoonFloodStrength", FileName, 3, true);

            // ★★ **退役キー 2 本。** 随伴竜巻は撤去された（上の doc）。
            //    宣言だけ残し、読む場所は 1 つも無い。**別の意味で再利用しないこと。**
            TyphoonTornadoes = new SavedBool("typhoonTornado", FileName, false, true);
            TyphoonTornadoCount = new SavedInt("typhoonTornadoCount", FileName, 1, true);

            // ★ 竜巻並みの局所被害。**新しいキーである**（退役キーを詰め直していない）。
            //    既定 ON —— 風害と同じ理由で、台風はプレイヤーが明示的に起こすので
            //    起きたことの原因が取り違えられない。強さ 0 で完全に無効になる。
            TyphoonGustEnabled = new SavedBool("typhoonGust", FileName, true, true);
            TyphoonGustStrength = new SavedInt("typhoonGustStrength", FileName, 3, true);

            // ★ 雲は既定 ON。**見た目だけの機能で、ゲームの状態を 1 バイトも変えない**
            //    （main スレッドで Graphics.DrawMesh を出すだけ）。切っても他の 5 要素は
            //    そのまま動く（TyphoonCloud のクラス doc の独立性）。
            TyphoonCloudEnabled = new SavedBool("typhoonCloud", FileName, true, true);
            // バニラのスカイドームの雲を濃く・速くする。**存在しない環境がありうる**
            // （DLC・グラフィック設定。IL 事実文書 §C-2、PARTIAL）。無ければ黙って諦める。
            TyphoonVanillaCloudBoost = new SavedBool("typhoonCloudBoost", FileName, true, true);

            // ★ 暴風雨の演出は既定 ON。**ゲームの状態を壊す方向には 1 バイトも動かさない**
            //    （飛沫は描画だけ、吹き飛ばしは市民と車だけ）。
            //    ★★ 保存値のキーは公開契約。既存のキーを詰め直さず、末尾に足す。
            TyphoonStormFx = new SavedBool("typhoonStormFx", FileName, true, true);

            // ★ 風の音は既定 ON。**それまで④は音を 1 つも鳴らしていなかった。**
            //    ★★ 保存値のキーは公開契約。既存のキーを詰め直さず、末尾に足す。
            TyphoonStormSound = new SavedBool("typhoonStormSound", FileName, true, true);

            // ★ ⑤火山は既定 ON。**DLC 非所持を理由に止めない** —— ⑤は Natural
            //    Disasters を要らない（設計書 §1.4）。しかも⑤は自動では 1 度も
            //    発火しない（プレイヤーが地点を指し、不可逆であることを確認して
            //    初めて始まる）ので、既定 ON でも黙って地形が変わることはない。
            VolcanoEnabled = new SavedBool("volcanoEnabled", FileName, true, true);
            // ★ 退役キー。ForecastButtonX/Y と全く同じ扱い（同 doc）。
            VolcanoButtonX = new SavedInt("volcanoButtonX", FileName, -1, true);
            VolcanoButtonY = new SavedInt("volcanoButtonY", FileName, -1, true);

            // ★ 保存値は公開契約。0=盾状 / 1=成層 / 2=溶岩ドーム の番号を詰め直さない。
            //    範囲外の値は VolcanoShape.FormOf が既定（成層）へ落とす。
            VolcanoShapeSetting = new SavedInt("volcanoShape", FileName, VolcanoShapeStrato, true);
            // 単位はメートル。範囲は形態ごとに違うので、スライダーの範囲ではなく
            // VolcanoShape.RadiusFor / HeightFor が使う側でクランプする
            // （.cgs は手で編集されうる）。
            VolcanoRadius = new SavedInt("volcanoRadius", FileName, 1200, true);
            VolcanoHeight = new SavedInt("volcanoHeight", FileName, 600, true);

            // 準備の前線が隆起の前線より何メートル先を走るか。0 にすると
            // 「壊した直後のセルを同じ tick で上げる」ことになり、余裕が無くなる。
            VolcanoClearingLeadMetres = new SavedInt("volcanoClearLead", FileName, 96, true);

            // 山肌の凹凸の強さ（%）。0 で今日どおりの滑らかな円錐。
            // ★ 新しいキーである。既存のキーの名前も既定値も変えていない
            //   （.cgs は公開契約で、番号も文字列も詰め直さない）。
            VolcanoReliefStrength = new SavedInt("volcanoRelief", FileName, 100, true);

            // 隆起にかけるゲーム内分。UpliftSchedule.TotalTicksFor が
            // 「山頂が毎 tick 1/64 m 以上動く」上限で切り詰める。
            VolcanoUpliftMinutes = new SavedInt("volcanoUpliftMinutes", FileName, 30, true);

            // 噴煙を描くか。切っても隆起は止まらない（描画は main スレッドだけの機能）。
            VolcanoEruptionFx = new SavedBool("volcanoEruptionFx", FileName, true, true);

            // 斜面を下る土煙の帯を出すか。既定 ON。
            // ★ **新しいキーである。既存のキーの名前も既定値も 1 つも変えていない**
            //   （.cgs は公開契約で、番号も文字列も詰め直さない）。
            VolcanoPyroclasticFx = new SavedBool("volcanoPyroclasticFx", FileName, true, true);

            // 噴火の音を鳴らすか。既定 ON。
            // ★ **新しいキーである。既存のキーの名前も既定値も 1 つも変えていない**
            //   （.cgs は公開契約で、番号も文字列も詰め直さない）。同梱 wav が
            //   無い環境では ON のままでも黙って無音になるだけなので、既定 ON でよい。
            VolcanoEruptionSound = new SavedBool("volcanoEruptionSound", FileName, true, true);

            // 火口から出す流れの本数。0 で完全に無効（溶岩も着火も出ない）。
            VolcanoLavaFlows = new SavedInt("volcanoLavaFlows", FileName, 4, true);
            // 溶岩の通り道に火を付けるか。切っても溶岩は流れる（見た目だけになる）。
            VolcanoLavaFire = new SavedBool("volcanoLavaFire", FileName, true, true);
            // 溶岩の面を描くか。切っても溶岩は流れる（描画は main スレッドだけの機能）。
            VolcanoLavaRender = new SavedBool("volcanoLavaRender", FileName, true, true);

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
