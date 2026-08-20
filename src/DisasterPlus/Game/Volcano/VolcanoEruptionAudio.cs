using System;
using System.IO;
using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 噴火音がこの環境で鳴らせるか。**「解決した」ではなく「使える」を持たせる**
    /// （<see cref="VolcanoVanillaFacts"/> と同じ規律。④のレビューと②の監査が、
    /// 「フィールドが解決したか」を述語にした検査が値の使えない環境で PASS を出す
    /// 欠陥を見つけている）。
    ///
    /// bool と int しか持たないので、キャッシュしても Unity の fake-null 問題を
    /// 持ち込まない。既定値は全て false ＝「まだ／もう読めていない」。
    /// </summary>
    public struct VolcanoAudioFacts
    {
        /// <summary><c>AudioManager</c> が居て <c>EffectGroup</c> に届いたか。</summary>
        public readonly bool EffectGroupResolved;

        /// <summary>
        /// <c>AudioManager.AddEvent(AudioGroup, AudioInfo, Vector3, Vector3, float, float,
        /// float, int)</c> を解決できたか。**⑤が音を出す唯一の経路**である。
        /// </summary>
        public readonly bool AddEventResolved;

        /// <summary>
        /// <c>AudioClip.Create(string,int,int,int,bool)</c> と
        /// <c>AudioClip.SetData(float[],int)</c> を解決できたか。
        /// **同期でクリップを作る唯一の経路**である。
        /// </summary>
        public readonly bool ClipApiResolved;

        /// <summary>同梱 wav が MOD フォルダに在るか。**無くても⑤は今日どおり動く。**</summary>
        public readonly bool FileFound;

        /// <summary>同梱 wav のバイト数（在れば）。</summary>
        public readonly long FileBytes;

        public VolcanoAudioFacts(bool effectGroupResolved, bool addEventResolved,
                                 bool clipApiResolved, bool fileFound, long fileBytes)
        {
            EffectGroupResolved = effectGroupResolved;
            AddEventResolved = addEventResolved;
            ClipApiResolved = clipApiResolved;
            FileFound = fileFound;
            FileBytes = fileBytes;
        }

        /// <summary>
        /// 音を鳴らす経路がこのゲームのビルドで成立するか。
        /// **<see cref="FileFound"/> は含めない** —— ファイルの有無はプレイヤーの都合
        /// （消せる）であって、ゲーム更新の兆候ではない。混ぜると、自分で消した人に
        /// 「前提が壊れた」と名乗ることになる（狼少年にしない）。
        /// </summary>
        public bool Usable
        {
            get { return EffectGroupResolved && AddEventResolved && ClipApiResolved; }
        }
    }

    /// <summary>
    /// 噴火の音。**main スレッド専用**（Unity のオーディオ資産は全て main）。
    ///
    /// ── なぜ「バニラの経路に載せる」のか（IL 実測）────────────────────
    ///
    /// 自前で <c>GameObject</c> ＋ <c>AudioSource</c> を作ると、**プレイヤーの音量
    /// スライダーもミュートも効かない**（生の音量で鳴り続ける）。バニラの災害の音が
    /// どこを通っているかを IL で全数当たると、答えは 1 本道だった ——
    /// **どれも <c>AudioManager.EffectGroup</c> へ流し込んでいる**
    /// （呼び出し元の一覧は⑤ IL 事実文書 §H-23。⑤の doc はそれらの型名を書かない ——
    /// 「⑤は災害まわりの型に触れていない」の担保が grep だからである。
    /// <see cref="VolcanoFeature"/> のクラス doc）:
    ///
    /// <code>
    /// AudioManager.AddEvent(EffectGroup, info, position, velocity,
    ///                       maxDistance, volume, pitch, playerID)
    ///     → m_eventBuffer へ積むだけ（Monitor.TryEnter で守られている）
    ///     → AudioManager.LateUpdate が掃き出して EffectGroup.AddPlayer を呼ぶ
    ///
    /// AudioManager.Awake:
    ///     m_effectGroup = new AudioGroup(3, new SavedFloat(Settings.effectAudioVolume,
    ///                                                      Settings.gameSettingsFile, …))
    /// AudioGroup.UpdatePlayers 末尾:
    ///     m_cachedVolume = (float)m_groupVolume      ← 効果音スライダーそのもの
    ///     m_totalVolume  = m_cachedVolume * master   ← master にミュートが入る
    /// AudioGroup.AddPlayer 冒頭:
    ///     if (m_totalVolume &lt; 0.01) return;         ← ミュート／0 なら 1 音も出さない
    ///     m_targetVolume = info.m_volume * volume * m_cachedVolume
    /// </code>
    ///
    /// つまり <c>EffectGroup</c> へ流し込みさえすれば、**効果音スライダーとミュートは
    /// ゲームが掛けてくれる**。3D の距離減衰（<c>rolloffMode = Linear</c>、
    /// <c>minDistance = 0</c>、<c>maxDistance</c> は渡した値）・ドップラ・優先度・
    /// フェードも全部あちら側にある。
    ///
    /// ★ <b><see cref="AudioSource"/> も <see cref="GameObject"/> も本 MOD は 1 つも作らない。</b>
    ///   <c>AudioManager.ObtainPlayer</c> がプールから出して <c>AudioInfo.ObtainClip()</c>
    ///   （＝ <c>m_clip</c> をそのまま返すだけ）を <c>source.clip</c> に挿す。
    ///   本 MOD が持つ Unity オブジェクトは <see cref="AudioClip"/> 1 個と
    ///   <see cref="AudioInfo"/> 1 個だけで、どちらも <see cref="Destroy"/> で消す。
    ///
    /// ── <c>AddEvent</c> か <c>AddPlayer</c> か ─────────────────────
    ///
    /// <c>AddPlayer</c> を直接呼ぶと、**<c>UpdatePlayers</c> より後に呼んでしまった
    /// フレームでは音が消える**（<c>m_playerDataCount</c> は <c>UpdatePlayers</c> の
    /// 末尾で 0 に戻る）。MOD の更新フックがバニラの <c>LateUpdate</c> の前後どちらで
    /// 走るかは保証が無いので、その賭けはしない。
    /// <c>AddEvent</c> は <c>m_eventBuffer</c> へ積むだけで、<c>AudioManager.LateUpdate</c>
    /// が**必ず <c>UpdatePlayers</c> より前に**掃き出す。しかも
    /// <c>Monitor.TryEnter(m_eventBuffer, SYNCHRONIZE_TIMEOUT)</c> で守られていて、
    /// バニラ自身が sim スレッドから呼んでいる（呼び出し元は⑤ IL 事実文書 §H-23）。
    /// **順序でもスレッドでも賭けが無い側**を採る。
    ///
    /// ── ループの維持と停止 ───────────────────────────────
    ///
    /// <c>UpdatePlayers</c> は <b><c>m_id</c> が一致する <c>PlayerData</c> が今フレーム
    /// 在るかどうか</b>だけを見てループを維持する。したがって
    ///
    /// <code>
    /// 鳴らし続ける = 毎フレーム同じ id で AddEvent する
    /// 止める       = AddEvent をやめる（あとはバニラが m_fadeLength でフェードして解放する）
    /// </code>
    ///
    /// 明示的な <c>Stop()</c> は要らないし、**在ってもいけない**（プールされた
    /// <c>AudioSource</c> は本 MOD のものではない）。
    ///
    /// ★ 1 フレームに 1 回だけ呼ぶこと。2 回呼ぶと <c>PlayerData</c> が 2 本積まれ、
    ///   <c>EffectGroup</c> の枠（<c>m_maxActiveCount = 3</c>）を 1 つの音で潰す。
    ///   だから sim スレッド（速度 3 では 1 フレームに複数 tick 回る）ではなく
    ///   **main スレッドの描画側から**呼ぶ。
    ///
    /// ── 毎フレームの費用 ────────────────────────────────
    ///
    /// <c>AddEvent</c> 1 回（<c>SimulationEvent</c> は struct、<c>FastList.Add</c> は
    /// 償却で確保なし）と <c>Vector3</c> 2 個。**ヒープ確保は 0 バイト、ログは 0 行。**
    ///
    /// ── 1 都市 1 回だけの費用 ──────────────────────────────
    ///
    /// <b>最初の噴火のフレーム</b>で 6.2 MB を読み、サンプルへ変換し（オフライン実測で 6 ms）、
    /// 山の 10 秒を切り出して <c>AudioClip</c> にする。**一瞬引っかかる。**
    /// レベルロードで先読みしないのは、⑤がプレイヤーの操作でしか始まらない機能で、
    /// 火山を置かない都市に毎回この費用を払わせたくないからである。
    /// 失敗しても**二度は試さない**（<see cref="_loadAttempted"/>）。
    /// </summary>
    public static class VolcanoEruptionAudio
    {
        /// <summary>同梱 wav の置き場所（MOD フォルダからの相対）。<c>Locales</c> と同じ扱い。</summary>
        public const string AudioFolderName = "Audio";

        /// <summary>同梱 wav のファイル名。**設定でも翻訳でもない固定の名前**である。</summary>
        public const string FileName = "erupting-volcano.wav";

        /// <summary>
        /// ループに使う窓（秒）。**実測に基づく**（<see cref="LoopSlice"/> のクラス doc）——
        /// 同梱ファイルは 36 秒の噴火 1 回ぶんの録音で、3〜13 秒が山である。
        /// </summary>
        private const float LoopStartSeconds = 3.0f;

        private const float LoopLengthSeconds = 10.0f;

        /// <summary>継ぎ目のクロスフェード（秒）。</summary>
        private const float LoopFadeSeconds = 1.0f;

        /// <summary>
        /// 聞こえる範囲（m）。バニラの竜巻が 5000、雷が 10000 を渡している
        /// （<c>rolloffMode</c> は <c>Linear</c>、<c>minDistance</c> は 0 なので、
        /// 音量はここまで直線的に落ちる）。⑤は竜巻と同じ帯に置く。
        /// </summary>
        private const float MaxDistanceMetres = 5000f;

        /// <summary>
        /// 噴出の強さ 0 のときの音量比。0 にしないのは、⑤の強さが 1 区切りごとに
        /// ゆらぐため —— 0 まで落とすと噴火の途中で音が切れたように聞こえる。
        /// </summary>
        private const float MinVolumeUnit = 0.35f;

        /// <summary>
        /// <c>AudioInfo.m_fadeLength</c>（秒）。<c>PlayerData.m_fadeSpeed = 1 / これ</c>
        /// なので、**0 にしてはいけない**（除算が ∞ になり、フェードが消える）。
        /// 止めたときにこの秒数でフェードアウトして解放される。
        /// </summary>
        private const float FadeSeconds = 2f;

        /// <summary>
        /// <c>AddEvent</c> に渡す固定の player ID。
        ///
        /// ★ バニラの id と衝突しない値を選ぶ。バニラが使うのは
        ///   <c>InstanceID.RawData</c>（型番号 × 2^24 ＋ 添字。実際には小さい正の数）と、
        ///   <c>m_effectPlayerID</c> が 0 から 1 ずつ**減らしていく**負の数である。
        ///   どちらの側からも遠い大きな正の定数を置く。
        ///   **⑤の噴火は同時に 1 つしか無い**（位相機械が 1 本）ので、
        ///   火山ごとに変える必要は無い。
        /// </summary>
        private const int PlayerId = 0x7D15A570;

        // ── main 側の状態（★ 配列にしない。参照 1 個ずつ）──────────────────

        private static AudioClip _clip;
        private static AudioInfo _info;

        /// <summary>この都市で読み込みを既に試したか。**失敗しても二度は試さない。**</summary>
        private static bool _loadAttempted;

        /// <summary>直近の読み込み結果（**英語・診断用**）。</summary>
        private static string _detail = "not loaded yet";

        private static bool _errorLogged;

        /// <summary>
        /// 直近の走査結果。**<see cref="ScanAudioFacts"/> が main スレッドで書き、
        /// 診断（sim スレッド）が読む。** ③④⑤が既に使っている形と同じで、
        /// bool と long しか持たない struct なのでキャッシュしても
        /// Unity の fake-null 問題を持ち込まない。
        /// </summary>
        private static VolcanoAudioFacts _facts;

        private static bool _factsScanned;

        /// <summary>クリップを持っているか（診断用）。</summary>
        public static bool ClipLoaded { get { return _clip != null && _info != null; } }

        /// <summary>
        /// 直近に走査した事実。**まだ 1 度も走査していなければ既定値（全て false）** なので、
        /// 読む側は <see cref="FactsScanned"/> を先に見ること ——
        /// 「走査していない」を「経路が無い」と名乗ると、診断が嘘をつく。
        /// </summary>
        public static VolcanoAudioFacts LastFacts { get { return _facts; } }

        /// <summary><see cref="ScanAudioFacts"/> が 1 度でも走ったか。</summary>
        public static bool FactsScanned { get { return _factsScanned; } }

        /// <summary>
        /// 読み込みの顛末を 1 行で（**英語**）。**将来ファイルが差し替えられたり
        /// 消されたりしたときの唯一の手がかり**なので、何が起きたかを必ず名乗る。
        /// </summary>
        public static string Detail { get { return _detail; } }

        /// <summary>
        /// **main スレッドから呼ぶこと。** 音の経路がこの環境で成立するかを調べるだけの走査で、
        /// Unity オブジェクトを 1 つも作らず、クリップも読まない
        /// （6 MB のファイル I/O をレベルロードに持ち込まない）。
        /// <see cref="Update"/> が門にするのも**この同じ式**である。
        ///
        /// ★★ <b>sim スレッドから呼ばないこと。</b> <c>File.Exists</c> の
        ///   ブロッキング I/O と、<see cref="LocaleLoader.ModDirectoryPath"/> の中の
        ///   <c>PluginManager.GetInstances</c> を sim スレッドへ持ち込むことになる。
        ///   診断（<c>IDisasterFeature.WriteDiagnostics</c>）は**sim スレッドの契約**なので、
        ///   あちらは走査せず <see cref="LastFacts"/> のキャッシュを読む。
        ///   結果をここで <see cref="_facts"/> に置いているのはそのためである。
        ///
        /// <c>Singleton&lt;T&gt;.exists</c> を先に見る（<c>instance</c> は <c>sInstance</c> が
        /// null のとき <c>FindObjectOfType</c> と <c>new GameObject</c> を走らせる）。
        /// </summary>
        public static VolcanoAudioFacts ScanAudioFacts()
        {
            bool effectGroup = false;
            bool addEvent = false;
            bool clipApi = false;
            bool fileFound = false;
            long fileBytes = 0L;

            try
            {
                if (Singleton<AudioManager>.exists)
                {
                    effectGroup = Singleton<AudioManager>.instance.EffectGroup != null;
                }
            }
            catch
            {
                effectGroup = false;
            }

            try
            {
                addEvent = typeof(AudioManager).GetMethod(
                    "AddEvent",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new Type[]
                    {
                        typeof(AudioGroup), typeof(AudioInfo), typeof(Vector3), typeof(Vector3),
                        typeof(float), typeof(float), typeof(float), typeof(int)
                    },
                    null) != null;
            }
            catch
            {
                addEvent = false;
            }

            try
            {
                // ★ 引数の型まで指定する。Create には 6 引数の旧 _3D 版（Obsolete）が
                //   同居しているので、名前だけで引くと別物を掴む。
                bool create = typeof(AudioClip).GetMethod(
                    "Create",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static,
                    null,
                    new Type[]
                    {
                        typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool)
                    },
                    null) != null;

                bool setData = typeof(AudioClip).GetMethod(
                    "SetData",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new Type[] { typeof(float[]), typeof(int) },
                    null) != null;

                clipApi = create && setData;
            }
            catch
            {
                clipApi = false;
            }

            try
            {
                string path = FilePath();
                if (path != null && File.Exists(path))
                {
                    fileFound = true;
                    fileBytes = new FileInfo(path).Length;
                }
            }
            catch
            {
                fileFound = false;
                fileBytes = 0L;
            }

            _facts = new VolcanoAudioFacts(effectGroup, addEvent, clipApi, fileFound, fileBytes);
            _factsScanned = true;
            return _facts;
        }

        /// <summary>
        /// **main スレッド、毎フレーム。** <see cref="VolcanoHub"/> のスナップショットだけを
        /// 読み、ゲームの状態を 1 つも変えない。
        ///
        /// 噴火していないフレームは**何もしない**（＝ <c>AddEvent</c> をやめる）。
        /// それがそのまま「きれいに止まる」の実装である。
        /// </summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            try
            {
                UpdateStep(snapshot);
            }
            catch (Exception e)
            {
                // ★ 1 回だけ鳴らす。ここは毎フレームの経路なので Log.Error を繰り返さない。
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano eruption audio failed", e);
                }
                _detail = "the audio path threw " + e.GetType().Name;

                // ★★ **ここで Destroy() を呼んではいけない。** あちらは
                //    <c>_loadAttempted</c> を false に戻すので、次のフレームが
                //    6 MB の wav を読み直し、また同じ例外で落ちる ——
                //    **毎フレームのファイル I/O**になる。手放すのはオブジェクトだけで、
                //    「もう試した」は立てたままにする（この都市ではもう鳴らさない）。
                ReleaseObjects();
            }
        }

        private static void UpdateStep(VolcanoSnapshot snapshot)
        {
            // 噴火していない ＝ 積むのをやめる。バニラが FadeSeconds かけて畳む。
            if (snapshot == null || !snapshot.Valid || !snapshot.EruptionActive) return;

            EnsureClip();
            // ★ 参照そのものを毎フレーム見る（配列に入れない）。破棄済みなら Unity の
            //   fake-null で null と等価になる。ここでは作り直さない ——
            //   読み込みは 1 都市 1 回で、失敗を毎フレーム再試行しない。
            if (_clip == null || _info == null) return;

            if (!Singleton<AudioManager>.exists) return;
            AudioManager audio = Singleton<AudioManager>.instance;

            AudioGroup group = audio.EffectGroup;
            if (group == null) return;

            // 音量は噴出の強さに従う（竜巻が m_intensity で同じことをしている）。
            // **プレイヤーの効果音スライダーとミュートはこの値に掛からない** ——
            // それは AudioGroup 側が m_cachedVolume / m_totalVolume で掛ける。
            float unit = Clamp01(snapshot.EruptionIntensityUnit);
            float volume = MinVolumeUnit + (1f - MinVolumeUnit) * unit;

            Vec3 summit = snapshot.SummitWorld;
            var position = new Vector3(summit.X, summit.Y, summit.Z);

            // 火口は動かないので velocity は 0（ドップラを掛けない）。
            // pitch は 1 のまま —— 噴出の強さは音量で表す。ループの再生速度を
            // 動かすと、区切りごとのゆらぎがそのまま音程のふらつきになる。
            audio.AddEvent(group, _info, position, Vector3.zero,
                           MaxDistanceMetres, volume, 1f, PlayerId);
        }

        /// <summary>
        /// 同梱 wav を読み、<see cref="AudioClip"/> と <see cref="AudioInfo"/> を 1 個ずつ作る。
        /// **都市ごとに 1 回だけ。失敗しても再試行しない**（毎フレーム 6 MB を読み直さない）。
        ///
        /// ★ <b>ここが「ファイルが無くても今日どおり」の実体である。</b>
        ///   見つからない・壊れている・API が無い —— どれも <see cref="_detail"/> に
        ///   理由を残して黙って戻るだけで、例外は 1 つも外へ出さない。
        /// </summary>
        private static void EnsureClip()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            string path = FilePath();
            if (path == null)
            {
                _detail = "the mod folder could not be resolved, so no eruption sound is loaded";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            if (!File.Exists(path))
            {
                // ★ **Warn ではなく Info。** ファイルを消すのはプレイヤーの自由で、
                //   消した結果は「今日どおりの無音の噴火」である。異常ではない。
                _detail = "no " + FileName + " in the mod's " + AudioFolderName
                          + " folder; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                _detail = FileName + " could not be read (" + e.GetType().Name
                          + "); the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            WavPcm pcm = WavPcm.Parse(bytes);
            if (!pcm.Valid)
            {
                _detail = FileName + " could not be parsed: " + pcm.Error
                          + "; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            // 1 回きりの録音から山の部分だけを切り出す（LoopSlice のクラス doc に実測）。
            // 切れないほど短いファイルに差し替えられていたら、元の波形がそのまま返る。
            float[] samples = LoopSlice.Build(pcm.Samples, pcm.Channels, pcm.SampleRate,
                                              LoopStartSeconds, LoopLengthSeconds,
                                              LoopFadeSeconds);
            int frames = samples.Length / pcm.Channels;
            if (frames <= 0)
            {
                _detail = FileName + " holds no sample frame; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            AudioClip clip = AudioClip.Create("DisasterPlus_VolcanoEruption",
                                              frames, pcm.Channels, pcm.SampleRate, false);
            if (clip == null)
            {
                _detail = "AudioClip.Create returned nothing; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            if (!clip.SetData(samples, 0))
            {
                UnityEngine.Object.Destroy(clip);
                _detail = "AudioClip.SetData refused the samples; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            // ★ AudioInfo は PrefabInfo ではなく素の ScriptableObject で、
            //   ObtainClip() は m_clip をそのまま返すだけである（IL 実測）。
            //   だから実行時に組み立ててよい —— セーブにプレハブ名は焼き付かない。
            AudioInfo info = ScriptableObject.CreateInstance<AudioInfo>();
            if (info == null)
            {
                UnityEngine.Object.Destroy(clip);
                _detail = "AudioInfo could not be created; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            info.name = "DisasterPlus_VolcanoEruption";
            info.m_clip = clip;
            info.m_volume = 1f;      // 実際の音量は AddEvent の引数と効果音スライダーで決まる
            info.m_pitch = 1f;
            info.m_fadeLength = FadeSeconds;
            info.m_loop = true;
            info.m_is3D = true;      // これが 3D 減衰（spatialBlend = 1）の門である
            info.m_randomTime = false;
            info.m_variations = null;

            _clip = clip;
            _info = info;

            _detail = "loaded " + pcm.SampleRate + " Hz, " + pcm.Channels + " ch, "
                      + pcm.BitsPerSample + " bit, "
                      + pcm.LengthSeconds.ToString("F1") + " s source -> "
                      + (frames / (float)pcm.SampleRate).ToString("F1") + " s loop";
            Log.Info("volcano eruption sound: " + _detail);
        }

        /// <summary>同梱 wav の絶対パス。MOD フォルダが引けなければ null。</summary>
        private static string FilePath()
        {
            string dir = LocaleLoader.ModDirectoryPath();
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(Path.Combine(dir, AudioFolderName), FileName);
        }

        /// <summary>
        /// **main スレッド。** 音を畳む。冪等。
        /// **レベルアンロードと、設定で音を切ったときに呼ぶ。**
        ///
        /// ★ <c>AudioSource</c> も <c>GameObject</c> も本 MOD のものではないので触らない
        ///   （<c>Stop()</c> も呼ばない）。持っているのはこの 2 個だけで、どちらも
        ///   <c>Component</c> ではないから <c>GameObject</c> の道連れにならない ——
        ///   **自分で <c>Object.Destroy</c> する。** ここを飛ばすと都市を出入りする
        ///   たびにクリップ 1 個（数 MB）が残る。
        ///
        /// ★ 破棄した瞬間にバニラのプレイヤーがまだこのクリップを指していることは在りうるが、
        ///   <c>UpdatePlayers</c> がクリップを触るのは <c>m_notReady</c> のときだけで、
        ///   <c>m_notReady</c> は <c>loadState != Loaded</c> のときにしか立たない。
        ///   <c>AudioClip.Create</c> ＋ <c>SetData</c> のクリップは最初から
        ///   <c>Loaded</c> なので、その枝には入らない（IL 実測）。
        /// </summary>
        public static void Destroy()
        {
            ReleaseObjects();

            // ここでだけ「もう試した」を戻す。**次の都市（あるいは設定を入れ直したとき）は
            // もう一度読む**のが正しい —— 前の都市で消されていたファイルが戻っていることも、
            // 逆に消されたこともある。
            _loadAttempted = false;
            _detail = "not loaded yet";
        }

        /// <summary>
        /// 持っている Unity オブジェクト 2 個だけを手放す。**「もう試した」は戻さない。**
        /// 例外経路からはこちらを呼ぶ（<see cref="Update"/> の catch の理由を参照）。
        /// 冪等。
        /// </summary>
        private static void ReleaseObjects()
        {
            // info を先に消す。PlayerData / AudioPlayer の照合は Object.op_Equality なので
            // fake-null になった時点で一致しなくなり、そのフレームで解放へ回る。
            if (_info != null) UnityEngine.Object.Destroy(_info);
            _info = null;

            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            _clip = null;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
