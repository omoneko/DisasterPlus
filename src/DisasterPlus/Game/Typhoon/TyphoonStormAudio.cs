using System;
using System.IO;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>嵐の音がどこから来ているか。**診断で名指しするためだけの型。**</summary>
    public enum TyphoonAudioSource
    {
        /// <summary>まだ試していない。</summary>
        None,

        /// <summary>MOD 同梱の <c>Audio/typhoon-wind.wav</c>。**在れば最優先。**</summary>
        BundledFile,

        /// <summary>バニラの竜巻の走行音（<c>TornadoAI.m_vortexSound</c> の <c>m_clip</c>）。</summary>
        VanillaVortex,

        /// <summary>バニラの海のアンビエンス（<c>AudioProperties.m_ambients</c> の中）。</summary>
        VanillaAmbient,

        /// <summary>この環境では鳴らせなかった。**不具合ではない**（台風は今までどおり動く）。</summary>
        Unavailable,
    }

    /// <summary>
    /// 台風の風の音。<b>main スレッド専用</b>（Unity のオーディオ資産は全て main）。
    ///
    /// ── 持ち主の指摘（2026-08-22）────────────────────────────────
    ///
    /// &gt; 風の音が少し物足りないです。
    ///
    /// **④はそれまで音を 1 つも鳴らしていなかった。** 聞こえていたのはバニラの
    /// 雨のアンビエンス（<c>Ambient Rain</c>）と落雷の <c>ThunderClap</c> だけで、
    /// **風の音は 1 つも無かった**（全コードを grep して確認）。「物足りない」は
    /// 「弱い」ではなく「無い」だったので、まず 1 本足す。
    ///
    /// ── ★ 何を鳴らすか（出荷アセットを測って決めた）──────────────────────
    ///
    /// 候補の波形をアセットから取り出して測った（4 秒窓の帯域エネルギー）:
    ///
    /// <code>
    /// クリップ                  RMS    20-80  80-250 250-800 800-2.5k 2.5-8k  定常性
    /// tornado_travelling       0.227   0.3%   6.2%   65.8%   26.6%    1.1%   0.20-0.28
    /// rumbling_disaster_loop   0.315  43.6%  54.9%    1.5%    0.0%    0.0%   0.27-0.34
    /// earthquake_loop          0.355  60.1%  33.5%    4.7%    1.5%    0.1%   0.34-0.37
    /// ambient_sea              0.012   0.0%   0.8%   59.8%   36.9%    2.4%   0.01-0.02
    /// rain                     0.026   0.0%   0.5%    5.0%   33.7%   54.0%   0.02-0.03
    /// </code>
    ///
    /// <b><c>tornado_travelling</c> が「風の唸り」そのものである</b> ——
    /// 中低域（250〜800 Hz）に 66 %、山も谷も無い定常なノイズで、
    /// **頭と尻の RMS が 0.203 対 0.205** なのでループの継ぎ目も鳴らない。
    /// <c>ambient_sea</c> はスペクトルの形は同じだが RMS が **19 分の 1** しかなく、
    /// Unity の <c>AudioSource.volume</c> は 1.0 で頭打ちなので<b>持ち上げられない</b> ——
    /// あれをそのまま鳴らすと、まさに「物足りない」音になる。
    /// <c>rain</c> は 2.5〜8 kHz に 54 % ＝ 細いシャーで、重さを足さない。
    ///
    /// ★★ <b><c>tornado_travelling</c> は Natural Disasters の資産だが、それでよい。</b>
    ///   ④の宿主は <c>ThunderStormAI</c> のプレハブで、あれ自体が
    ///   <c>Expansion3Prefabs</c>（＝ Natural Disasters）にしか無い。
    ///   <b>④の台風を起こせる環境は、必ずこの音も持っている。</b>
    ///   それでも <c>null</c> は必ず検査し、取れなければ海のアンビエンスへ、
    ///   それも無ければ無音へ落ちる（例外は 1 つも出さない）。
    ///
    /// <c>pitch</c> を 1 未満にして<b>重さを足す</b>。Unity の pitch は素直な再生速度
    /// なので、66 % を占める 250〜800 Hz の帯がそのまま下へ移り、唸りが太くなる。
    /// 強い台風ほど低くする（<see cref="PitchAt"/>）。
    ///
    /// ── ★ 借りるのは <c>AudioClip</c> だけ。<c>AudioInfo</c> は自分で作る ────────
    ///
    /// <c>AudioInfo</c> は <b>共有の <c>ScriptableObject</c></b> である。
    /// <c>m_volume</c> や <c>m_loop</c> を書き換えると<b>ゲーム自身の竜巻の音</b>が
    /// 道連れになり、セーブではなくメモリ上に残る（粒子の <c>§D-5</c> と同じ形）。
    /// だから <c>m_clip</c> だけを読み、<c>AudioInfo</c> は
    /// <c>ScriptableObject.CreateInstance</c> で自分の分を作る
    /// （⑤の <c>VolcanoEruptionAudio</c> と同じ。<c>AudioInfo</c> は <c>PrefabInfo</c> では
    /// ないので、セーブにプレハブ名は焼き付かない）。
    ///
    /// ★★ <b>借りたクリップは <see cref="Destroy"/> で破棄しないこと。</b>
    ///   ④のものではない。破棄すると<b>ゲーム自身の竜巻が無音になる</b>。
    ///   <see cref="_clipIsOurs"/> がその区別を持っている。
    ///
    /// ── ★ 2D で鳴らす ──────────────────────────────────────
    ///
    /// <c>m_is3D = false</c>。台風は「向こうにある音源」ではなく
    /// <b>自分がその中に居る現象</b>なので、方向を持たせない。
    /// 代わりに<b>音量をカメラの居る場所の風速相当から決める</b>
    /// （<c>TyphoonProfile.WindAt</c> ＋ <c>SquallLayout.StrengthOf</c>。
    /// 飛沫とまったく同じ式なので、**見えるものと聞こえるものがずれない**）。
    /// 強風域の外では <c>AddEvent</c> を呼ばない ＝ バニラがフェードして畳む。
    ///
    /// ── ★ プレイヤーの音量とミュートは必ず効く ──────────────────────────
    ///
    /// <c>AudioManager.EffectGroup</c> へ流し込むだけで、効果音スライダーとミュートは
    /// ゲームが掛けてくれる（⑤ IL 事実文書 §H-23）。
    /// <b>自前の <c>AudioSource</c> も <c>GameObject</c> も 1 つも作らない。</b>
    ///
    /// <c>AddPlayer</c> ではなく <c>AddEvent</c> を呼ぶこと。前者は
    /// <c>UpdatePlayers</c> より後に呼んだフレームで消える（同 §H-23）。
    ///
    /// ★ <b>1 フレームに 1 回だけ。</b> 同じ id で 2 回積むと <c>PlayerData</c> が
    ///   2 本並び、<c>EffectGroup</c> の枠（<c>m_maxActiveCount = 3</c>）を 1 つの音で潰す。
    ///   <b>だから④は音を 1 本しか鳴らさない</b> —— 雷や崩壊の音の席を奪わないためである。
    ///
    /// ── 同梱ファイルを足したくなったら ────────────────────────────
    ///
    /// <c>Audio/typhoon-wind.wav</c>（44.1 kHz / 16 bit、定常な暴風の録音、10 秒以上）を
    /// MOD フォルダの <c>Audio</c> に置くだけで、次に都市を読み込んだときから
    /// そちらが優先される（⑤の噴火音と同じ経路）。**今は同梱していない。**
    /// </summary>
    public static class TyphoonStormAudio
    {
        /// <summary>同梱 wav の置き場所（MOD フォルダからの相対）。⑤と同じ扱い。</summary>
        public const string AudioFolderName = "Audio";

        /// <summary>同梱 wav のファイル名。**今は同梱していない**（在れば使う）。</summary>
        public const string FileName = "typhoon-wind.wav";

        /// <summary>同梱 wav から切り出すループの窓（秒）。⑤と同じ形。</summary>
        private const float LoopStartSeconds = 1.0f;

        private const float LoopLengthSeconds = 8.0f;

        private const float LoopFadeSeconds = 1.0f;

        /// <summary>
        /// 聞こえる範囲（m）。<c>m_is3D = false</c> なので距離減衰は掛からないが、
        /// <c>AudioGroup.AddPlayer</c> が <c>maxDistance</c> をそのまま
        /// <c>AudioSource</c> に入れるので、値そのものは渡しておく。
        /// バニラの落雷が 10000 を渡している。
        /// </summary>
        private const float MaxDistanceMetres = 10000f;

        /// <summary>吹き付けの強さ 0 のときの音量比。**0 にしない** ——
        /// 強さが揺らぐたびに音が切れたように聞こえる。</summary>
        private const float MinVolumeUnit = 0.30f;

        /// <summary><c>AudioInfo.m_fadeLength</c>（秒）。**0 にしてはいけない**
        /// （<c>m_fadeSpeed = 1 / これ</c> なので除算が ∞ になる）。</summary>
        private const float FadeSeconds = 3f;

        /// <summary>いちばん弱い台風の <c>pitch</c>。</summary>
        private const float PitchWeak = 0.92f;

        /// <summary>いちばん強い台風の <c>pitch</c>。**低いほど重い。**</summary>
        private const float PitchStrong = 0.68f;

        /// <summary>
        /// <c>AddEvent</c> に渡す固定の player ID。バニラが使うのは
        /// <c>InstanceID.RawData</c>（小さい正の数）と <c>m_effectPlayerID</c>
        /// （0 から 1 ずつ減る負の数）なので、どちらからも遠い大きな正の定数を置く。
        /// **⑤の噴火音（0x7D15A570）とも別の値にする**（同時に鳴りうる）。
        /// </summary>
        private const int PlayerId = 0x7D15B004;

        // ── main 側の状態（★ 配列にしない。参照 1 個ずつ）──────────────────

        private static AudioClip _clip;
        private static AudioInfo _info;

        /// <summary>
        /// <see cref="_clip"/> を④が作ったか（＝破棄してよいか）。
        /// **借りたクリップを破棄するとゲーム自身の音が無音になる**（クラス doc）。
        /// </summary>
        private static bool _clipIsOurs;

        private static TyphoonAudioSource _source = TyphoonAudioSource.None;

        /// <summary>この都市で読み込みを既に試したか。**失敗しても二度は試さない。**</summary>
        private static bool _loadAttempted;

        /// <summary>直近の顛末（**英語・診断用**）。</summary>
        private static string _detail = "not loaded yet";

        private static bool _errorLogged;
        private static float _lastVolume;

        public static TyphoonAudioSource Source { get { return _source; } }

        /// <summary>直近に <c>AddEvent</c> へ渡した音量（0 なら鳴らしていない）。</summary>
        public static float LastVolume { get { return _lastVolume; } }

        /// <summary>
        /// 顛末を 1 行で（**英語**）。将来ゲームが更新されて音が消えたときの
        /// 唯一の手がかりなので、何を借りたのかを必ず名乗る。
        /// </summary>
        public static string Detail { get { return _detail; } }

        /// <summary>
        /// **main スレッド、毎フレーム。** <see cref="TyphoonHub"/> のスナップショットだけを
        /// 読み、ゲームの状態を 1 つも変えない。
        ///
        /// 台風が居ないフレーム・強風域の外のフレームは**何もしない**
        /// （＝ <c>AddEvent</c> をやめる）。それがそのまま「きれいに止まる」の実装である。
        /// </summary>
        public static void Update(TyphoonSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon storm sound failed", e);
                }
                _detail = "the audio path threw " + e.GetType().Name;
                _lastVolume = 0f;

                // ★★ **ここで Destroy() を呼んではいけない。** あちらは _loadAttempted を
                //    戻すので、次のフレームがまた読み直して同じ例外で落ちる。
                ReleaseObjects();
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            _lastVolume = 0f;

            if (snapshot == null || !snapshot.Valid || !snapshot.Active) return;

            var camera = VanillaParticles.CameraInfo();
            if (camera == null) return;

            Vector3 eye = camera.m_position;
            Vec3 centre = snapshot.Centre;
            float dx = eye.x - centre.X;
            float dz = eye.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            // ★ 飛沫とまったく同じ式。見えるものと聞こえるものをずらさない。
            float wind = TyphoonProfile.WindAt(distance, snapshot.Intensity,
                                               snapshot.Prefab.StormRadius);
            float strength = SquallLayout.StrengthOf(wind);
            if (!(strength > 0f)) return;

            EnsureClip();
            // ★ 参照そのものを毎フレーム見る。破棄済みなら fake-null で null と
            //   等価になる。ここでは作り直さない（読み込みは 1 都市 1 回）。
            if (_clip == null || _info == null) return;

            if (!Singleton<AudioManager>.exists) return;
            AudioManager audio = Singleton<AudioManager>.instance;

            AudioGroup group = audio.EffectGroup;
            if (group == null) return;

            float volume = MinVolumeUnit + (1f - MinVolumeUnit) * strength;
            float pitch = PitchAt(snapshot.Intensity);

            // **プレイヤーの効果音スライダーとミュートはこの値に掛からない** ——
            // それは AudioGroup 側が m_cachedVolume / m_totalVolume で掛ける。
            audio.AddEvent(group, _info, eye, Vector3.zero,
                           MaxDistanceMetres, volume, pitch, PlayerId);
            _lastVolume = volume;
        }

        /// <summary>
        /// 強度 <paramref name="intensity"/> のときの <c>pitch</c>。
        /// **強い台風ほど低い＝重い。** 範囲は
        /// <see cref="PitchStrong"/>〜<see cref="PitchWeak"/> に必ず収まる。
        /// </summary>
        public static float PitchAt(byte intensity)
        {
            float unit = intensity / 255f;
            return PitchWeak + (PitchStrong - PitchWeak) * unit;
        }

        /// <summary>
        /// クリップと <c>AudioInfo</c> を 1 個ずつ用意する。
        /// **都市ごとに 1 回だけ。失敗しても再試行しない。**
        /// </summary>
        private static void EnsureClip()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            // 1. 同梱ファイル（在れば最優先。**今は同梱していない**）。
            if (LoadBundled()) return;

            // 2. バニラの竜巻の走行音。**④を起こせる環境には必ず在る**（クラス doc）。
            if (BorrowVanilla(VortexClip(), TyphoonAudioSource.VanillaVortex,
                              "the game's tornado wind loop")) return;

            // 3. 海のアンビエンス。**測ると竜巻の 19 分の 1 の音量しかない**ので、
            //    ここへ落ちたら「薄い」と自己申告する。
            if (BorrowVanilla(AmbientClip(), TyphoonAudioSource.VanillaAmbient,
                              "the game's sea ambience (much quieter than the tornado "
                              + "loop, so the storm will sound thin)")) return;

            _source = TyphoonAudioSource.Unavailable;
            _detail = "no usable wind loop in this build; the typhoon is silent "
                      + "(everything else about it is unaffected)";
            Log.Info("typhoon storm sound: " + _detail);
        }

        /// <summary>同梱 wav（在れば）。⑤の噴火音とまったく同じ手順。</summary>
        private static bool LoadBundled()
        {
            string path = FilePath();
            if (path == null || !File.Exists(path)) return false;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                Log.Info("typhoon storm sound: " + FileName + " could not be read ("
                         + e.GetType().Name + "); falling back to a vanilla loop");
                return false;
            }

            WavPcm pcm = WavPcm.Parse(bytes);
            if (!pcm.Valid)
            {
                Log.Info("typhoon storm sound: " + FileName + " could not be parsed ("
                         + pcm.Error + "); falling back to a vanilla loop");
                return false;
            }

            float[] samples = LoopSlice.Build(pcm.Samples, pcm.Channels, pcm.SampleRate,
                                              LoopStartSeconds, LoopLengthSeconds,
                                              LoopFadeSeconds);
            int frames = samples.Length / pcm.Channels;
            if (frames <= 0) return false;

            AudioClip clip = AudioClip.Create("DisasterPlus_TyphoonWind",
                                              frames, pcm.Channels, pcm.SampleRate, false);
            if (clip == null) return false;

            if (!clip.SetData(samples, 0))
            {
                UnityEngine.Object.Destroy(clip);
                return false;
            }

            if (!Wrap(clip, true))
            {
                UnityEngine.Object.Destroy(clip);
                return false;
            }

            _source = TyphoonAudioSource.BundledFile;
            _detail = "loaded " + FileName + " (" + pcm.SampleRate + " Hz, " + pcm.Channels
                      + " ch, " + pcm.LengthSeconds.ToString("F1") + " s source -> "
                      + (frames / (float)pcm.SampleRate).ToString("F1") + " s loop)";
            Log.Info("typhoon storm sound: " + _detail);
            return true;
        }

        /// <summary>
        /// バニラのクリップを 1 つ借りる。**<c>AudioInfo</c> は自分で作る**
        /// （借り元の共有 <c>ScriptableObject</c> は 1 バイトも触らない。クラス doc）。
        /// </summary>
        private static bool BorrowVanilla(AudioClip clip, TyphoonAudioSource source,
                                          string what)
        {
            if (clip == null) return false;
            if (!Wrap(clip, false)) return false;

            _source = source;
            _detail = "borrowed " + what + " (\"" + clip.name + "\", "
                      + clip.length.ToString("F1") + " s, looped, 2D)";
            Log.Info("typhoon storm sound: " + _detail);
            return true;
        }

        /// <summary>
        /// クリップを④の <c>AudioInfo</c> で包む。
        /// <paramref name="ours"/> が true のときだけ <see cref="Destroy"/> が
        /// クリップを破棄する（借り物は触らない）。
        /// </summary>
        private static bool Wrap(AudioClip clip, bool ours)
        {
            AudioInfo info = ScriptableObject.CreateInstance<AudioInfo>();
            if (info == null) return false;

            info.name = "DisasterPlus_TyphoonWind";
            info.m_clip = clip;
            info.m_volume = 1f;      // 実際の音量は AddEvent の引数と効果音スライダーで決まる
            info.m_pitch = 1f;       // pitch も AddEvent の引数で毎フレーム渡す
            info.m_fadeLength = FadeSeconds;
            info.m_loop = true;
            // ★ 2D。台風は「向こうにある音源」ではなく自分がその中に居る現象である。
            info.m_is3D = false;
            info.m_randomTime = false;
            info.m_variations = null;

            _clip = clip;
            _info = info;
            _clipIsOurs = ours;
            return true;
        }

        /// <summary>
        /// バニラの竜巻の走行音（<c>TornadoAI.m_vortexSound.m_clip</c>）。
        /// **プレハブが無ければ null**（Natural Disasters を持っていない環境。
        /// ただしそこでは④の台風自体が起こせない。クラス doc）。
        /// </summary>
        private static AudioClip VortexClip()
        {
            try
            {
                DisasterInfo info = DisasterManager.FindDisasterInfo<TornadoAI>();
                if (info == null) return null;

                var ai = info.m_disasterAI as TornadoAI;
                if (ai == null) return null;

                AudioInfo sound = ai.m_vortexSound;
                return sound != null ? sound.m_clip : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// バニラのアンビエンスの中から海の音を 1 つ。**名前で選ぶ** ——
        /// <c>AudioProperties.m_ambients</c> は名前しか手掛かりが無い。
        /// 取れなければ null（無音へ落ちる）。
        /// </summary>
        private static AudioClip AmbientClip()
        {
            try
            {
                if (!Singleton<AudioManager>.exists) return null;

                var properties = Singleton<AudioManager>.instance.m_properties;
                if (properties == null || properties.m_ambients == null) return null;

                AudioClip best = null;
                for (int i = 0; i < properties.m_ambients.Length; i++)
                {
                    AudioInfo info = properties.m_ambients[i];
                    if (info == null || info.m_clip == null) continue;

                    string clipName = info.m_clip.name;
                    if (clipName == "ambient_sea") return info.m_clip;
                    if (best == null && clipName == "ambient_stream") best = info.m_clip;
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>同梱 wav の絶対パス。MOD フォルダが引けなければ null。</summary>
        private static string FilePath()
        {
            try
            {
                string dir = LocaleLoader.ModDirectoryPath();
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(Path.Combine(dir, AudioFolderName), FileName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// **main スレッド。** 音を畳む。冪等。
        /// **レベルアンロードと、設定で音を切ったときに呼ぶ。**
        ///
        /// ★ <c>AudioSource</c> も <c>GameObject</c> も本 MOD のものではないので触らない
        ///   （<c>Stop()</c> も呼ばない）。<c>AddEvent</c> をやめれば
        ///   バニラが <see cref="FadeSeconds"/> かけてフェードして解放する。
        /// </summary>
        public static void Destroy()
        {
            ReleaseObjects();
            _loadAttempted = false;
            _source = TyphoonAudioSource.None;
            _detail = "not loaded yet";
            _lastVolume = 0f;
        }

        /// <summary>
        /// 持っている Unity オブジェクトを手放す。**「もう試した」は戻さない**
        /// （例外経路からはこちらを呼ぶ）。冪等。
        /// </summary>
        private static void ReleaseObjects()
        {
            // info を先に消す。PlayerData / AudioPlayer の照合は Object.op_Equality なので
            // fake-null になった時点で一致しなくなり、そのフレームで解放へ回る。
            if (_info != null) UnityEngine.Object.Destroy(_info);
            _info = null;

            // ★★ **借りたクリップは破棄しない**（ゲーム自身の竜巻が無音になる）。
            if (_clipIsOurs && _clip != null) UnityEngine.Object.Destroy(_clip);
            _clip = null;
            _clipIsOurs = false;
        }
    }
}
