using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④が握っているバニラの**災害スロット 1 個**そのもの。<b>sim スレッド専用。</b>
    ///
    /// ── なぜ <see cref="TyphoonController"/> から切り出したか ─────────────
    ///
    /// ④には性質のまったく違う 2 つの仕事がある。ひとつは**④自身のモデル**
    /// （経路・強度の包絡線・上陸減衰・位相・上陸予測）で、これは
    /// <c>Core/Typhoon</c> の純関数の上に乗った算術であり、間違えても
    /// 「台風が変な動きをする」で済む。もうひとつが**バニラの災害バッファへの
    /// 書き込み**で、こちらは間違えると<b>他人の災害スロットを書き潰す</b>・
    /// <b>スロットを 1 個永久に食い潰す</b>・<b>無関係な災害を引きずり回す</b>という、
    /// 例外の出ない壊れ方をする（罠 1 と罠 2、そして ID 再利用）。
    ///
    /// この型は後者だけを持つ。**<c>DisasterManager</c> / <c>DisasterData</c> /
    /// <c>DisasterAI</c> / <c>DisasterInfo</c> に触るコードは、④ではここにしか無い。**
    /// T7〜T10 が④に新しい要素を足しても、この境界は動かないこと。
    ///
    /// ── 罠 1: <c>SelfTrigger</c>（③が実際に出荷した） ───────────────────
    ///
    /// <c>ThunderStormAI.StartDisaster</c> は <c>IL_000E</c> で <c>m_flags &amp; 64</c> を見て、
    /// 立っていなければ**即 return する**（IL 事実文書 §A-1）。そのとき
    /// <c>m_activationFrame</c> は 0 のまま・<c>Significant(256)</c> も付かないので、
    /// <c>IsStillEmerging</c> が永久 true になり、周囲の建物は <c>DetectDisaster</c> を
    /// 呼ばず、**ハザードマップにも通知にも一切出ない**。例外は 1 つも出ない。
    /// ③がこれを出荷し、②のレビューで初めて見つかった。
    ///
    /// **だからフラグを立てるだけでは足りない。** <see cref="Begin"/> は
    /// <c>StartNow</c> の直後に <c>m_activationFrame != 0</c> を観測する。将来
    /// <c>m_flags</c> の代入がリファクタで消えても、実行時に必ず気付く。
    ///
    /// ── 罠 2: <c>CreateDisaster</c> の戻り値 ─────────────────────────
    ///
    /// 災害は上限 256。<c>CreateDisaster</c> は失敗時に **false を返し
    /// <c>disasterIndex = 0</c> を出す**（例外は出ない。地震 §E-1）。見ないで書くと
    /// **他人の災害スロットを書き潰す**。<see cref="Create"/> は必ず戻り値を見る。
    ///
    /// ── ID は再利用される ───────────────────────────────────
    ///
    /// 災害 ID は解放後に再利用される。別の災害に化けたまま <c>m_targetPosition</c> を
    /// 書き続けると**無関係な災害を④が引きずり回す**（③の <c>FireWhirlPinner</c> が
    /// 距離で偽陽性を弾いているのと同じ事故）。<see cref="TryGetBuffer"/> が
    /// 毎 tick 4 つの条件で持ち主かどうかを確かめる。
    ///
    /// ── <c>Singleton&lt;T&gt;.exists</c> を先に見る ─────────────────────
    ///
    /// <c>Singleton&lt;T&gt;.instance</c> は <c>sInstance</c> が null のとき
    /// <c>FindObjectOfType</c> と <c>new GameObject</c> を走らせる **main スレッド専用
    /// API** で、sim スレッドから踏むと落ちる（<see cref="TsunamiChain"/> の同じ注記）。
    /// </summary>
    internal static class TyphoonSlot
    {
        private static ushort _id;
        private static uint _activationFrame;

        /// <summary>例外を 1 回だけ <c>Log.Error</c> で出したか。<see cref="Forget"/> で戻さない
        /// （ゲームのビルドに対する事実であって都市ごとの状態ではない）。</summary>
        private static bool _errorLogged;

        /// <summary>掴んでいる災害スロットの添字。0 は「持っていない」。</summary>
        internal static ushort Id { get { return _id; } }

        /// <summary>
        /// <c>StartDisaster</c> が書いた <c>m_startFrame + m_emergingDuration</c>。
        /// スロットが再利用されたことを見分ける最も安いキーであり、
        /// **落雷の予算がバニラの取り分を見積もるための起点**でもある
        /// （<c>LightningBudget.VanillaRampCount</c>）。
        /// </summary>
        internal static uint ActivationFrame { get { return _activationFrame; } }

        /// <summary>
        /// 災害スロットを 1 個取る。**まだ開始しない。**
        ///
        /// 2 段に分かれているのは、④の経路の種が<b>災害 ID そのもの</b>だからである
        /// （設計書 §4.1「同じセーブで再現できること」）。ID が決まらないと中心が
        /// 決まらず、中心が決まらないと <see cref="Begin"/> に渡す座標が作れない。
        /// </summary>
        internal static bool Create(out string refusal)
        {
            refusal = null;

            var info = FindStormInfo();
            if (info == null)
            {
                refusal = "no ThunderStormAI prefab (Natural Disasters DLC?)";
                return false;
            }

            if (!Singleton<DisasterManager>.exists)
            {
                refusal = "DisasterManager is not available";
                return false;
            }

            ushort id;
            // ★ 罠 2: 戻り値を必ず見る。false のとき id = 0 になり、そのまま書き込むと
            //    **他人の災害スロットを書き潰す**（上限 256）。
            if (!Singleton<DisasterManager>.instance.CreateDisaster(out id, info))
            {
                refusal = "CreateDisaster returned false (disaster buffer full?)";
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFull", refusal);
                return false;
            }

            _id = id;
            _activationFrame = 0u;
            return true;
        }

        /// <summary>
        /// 取ったスロットに初期状態を書き、<c>StartNow</c> で開始し、
        /// **<c>SelfTrigger</c> が本当に効いたかをその場で確かめる**（罠 1）。
        ///
        /// <paramref name="pos"/> は入力がクランプ前の中心（y は無視）で、
        /// 返るときには <c>ClampDisasterTarget</c> と地形高を通した実際の書き込み値に
        /// なっている。呼び出し側はその y を「中心の高さ」として使う。
        ///
        /// 失敗したらスロットを解放して <see cref="Forget"/> するので、
        /// 呼び出し側は後始末を書かなくてよい。
        /// </summary>
        internal static bool Begin(ref Vector3 pos, float angle, byte intensity, out string refusal)
        {
            refusal = null;

            if (_id == 0 || !Singleton<DisasterManager>.exists)
            {
                refusal = "DisasterManager is not available";
                Forget();
                return false;
            }

            var manager = Singleton<DisasterManager>.instance;
            var buffer = manager.m_disasters.m_buffer;
            if (buffer == null || _id >= buffer.Length)
            {
                refusal = "the disaster index is out of range";
                Forget();
                return false;
            }

            var info = buffer[_id].Info;
            if (info == null || info.m_disasterAI == null)
            {
                refusal = "the new disaster slot has no ThunderStormAI";
                Abandon(manager, _id);
                Forget();
                return false;
            }

            var ai = info.m_disasterAI;
            ai.ClampDisasterTarget(ref pos);                        // public
            pos.y = SampleHeight(pos);                              // StartDisaster と同じ扱い（§A-1）

            buffer[_id].m_targetPosition = pos;
            buffer[_id].m_angle = angle;
            buffer[_id].m_intensity = intensity;
            // ★ 罠 1: これが無いと StartDisaster は IL_000E で即 return する（クラス doc）。
            buffer[_id].m_flags |= DisasterData.Flags.SelfTrigger;

            // StartDisaster は protected。CreateDisaster 直後の m_flags は Created(1) だけ
            // なので StartNow の「& 60 が 0 なら」の門は必ず通る（§E-3）。
            ai.StartNow(_id, ref buffer[_id]);

            // ★ SelfTrigger が本当に効いたかを、その場で確かめる。StartDisaster が
            //    通っていれば m_activationFrame = m_startFrame + m_emergingDuration が
            //    入っている（§A-1 IL_003F）。
            if (buffer[_id].m_activationFrame == 0u)
            {
                refusal = "StartDisaster did not schedule an activation frame; "
                        + "the SelfTrigger flag did not take effect";
                Log.Error("typhoon: " + refusal, null);
                Abandon(manager, _id);
                Forget();
                return false;
            }

            _activationFrame = buffer[_id].m_activationFrame;
            return true;
        }

        /// <summary>
        /// 掴んでいるスロットがまだ④のものか。**災害 ID は解放後に再利用される**
        /// （クラス doc）。1 つでも外れたら false を返し、理由を
        /// <paramref name="lostReason"/> に入れる —— 呼び出し側は<b>書き込みをやめて
        /// 手放す</b>こと。バニラの終了経路を呼んではいけない（もう④のものではない）。
        /// </summary>
        internal static bool TryGetBuffer(out DisasterData[] buffer, out string lostReason)
        {
            buffer = null;
            lostReason = null;

            if (!Singleton<DisasterManager>.exists)
            {
                lostReason = "DisasterManager is not available";
                return false;
            }

            var candidate = Singleton<DisasterManager>.instance.m_disasters.m_buffer;
            if (candidate == null || _id == 0 || _id >= candidate.Length)
            {
                lostReason = "the disaster index is out of range";
                return false;
            }

            if ((candidate[_id].m_flags & DisasterData.Flags.Created) == 0)
            {
                lostReason = "the disaster slot was released";
                return false;
            }

            // 再利用されたスロットを見分ける最も安いキー。開始時に控えた値と一致するか。
            if (candidate[_id].m_activationFrame != _activationFrame)
            {
                lostReason = "the disaster slot was reused by something else";
                return false;
            }

            // get_Info は境界検査をしない 4 命令なので、要素ごとに try/catch する。
            try
            {
                var info = candidate[_id].Info;
                if (info == null || !(info.m_disasterAI is ThunderStormAI))
                {
                    lostReason = "the disaster slot is no longer a thunderstorm";
                    return false;
                }
            }
            catch
            {
                lostReason = "the disaster slot could not be identified";
                return false;
            }

            buffer = candidate;
            return true;
        }

        /// <summary>
        /// 毎 tick の書き込み（IL 事実文書 §E-1）。<paramref name="pos"/> は入力が
        /// クランプ前の中心で、返るときには実際に書いた座標（クランプ＋地形高）になる。
        ///
        /// <c>ClampDisasterTarget</c> は本タスクで IL を読んだところ**マップ矩形ではなく
        /// 「解放済みタイル」の内側**へ丸める（<c>GameAreaManager.IsUnlocked</c> /
        /// <c>GetAreaBounds</c> を 8 方向ぶん見る）。だから④は「本当の中心」を自分で持ち、
        /// ここにはその**クランプした写し**を書く。終了判定をクランプ後の値で行うと、
        /// 台風は購入済みエリアの縁に貼り付いたまま永久に終わらなくなる。
        /// </summary>
        internal static void WriteTarget(DisasterData[] buffer, ref Vector3 pos,
                                         float angle, byte intensity)
        {
            var ai = buffer[_id].Info.m_disasterAI;
            ai.ClampDisasterTarget(ref pos);
            pos.y = SampleHeight(pos);

            buffer[_id].m_targetPosition = pos;
            buffer[_id].m_angle = angle;
            buffer[_id].m_intensity = intensity;
        }

        /// <summary>
        /// バニラの終了経路に乗せる。<c>DeactivateNow</c> は public で、本タスクで IL を
        /// 読んだところ **<c>m_flags</c> に <c>Active(8)</c> が立っていなければ何もしない**。
        /// 立っていれば <c>ThunderStormAI.DeactivateDisaster</c> が走り、
        /// <c>SelfTrigger</c> 付きなので <c>m_targetRain = 0</c> / <c>m_targetCloud = 0</c> が
        /// 書かれる（§A-1）。
        ///
        /// ★ **Emerging 中に止めた場合はここが空振りする。** だから④が触った天候は
        ///   ④自身が戻さなければならない（<c>TyphoonWeather.Release</c>）。
        ///
        /// **災害スロットは解放しない。** <c>DisasterAI.IsStillClearing</c>（base）は
        /// 災害グループの <c>m_refCount &gt; 1</c>、すなわち④の落雷で燃えた建物が残っている
        /// 間 Clearing を続ける（§A-1）。**嵐は火が消えるまで終わらない**のが正しい挙動で、
        /// その後 <c>DisasterManager.SimulationStepImpl</c> が <c>ReleaseDisaster</c> を呼ぶ。
        /// ②の <see cref="TsunamiChain"/> が <c>ReleaseDisaster</c> を「使えるが使わない」と
        /// 判断したのと同じ理由（<c>OnDisasterStarted</c> を受け取った他 MOD から見て、
        /// 終了通知の無い災害を作らない）。
        /// </summary>
        internal static void Deactivate()
        {
            try
            {
                if (_id != 0 && Singleton<DisasterManager>.exists)
                {
                    var buffer = Singleton<DisasterManager>.instance.m_disasters.m_buffer;
                    if (buffer != null && _id < buffer.Length)
                    {
                        var info = buffer[_id].Info;
                        if (info != null && info.m_disasterAI is ThunderStormAI)
                        {
                            info.m_disasterAI.DeactivateNow(_id, ref buffer[_id]);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon deactivation failed", e);
                }
            }
        }

        /// <summary>参照を捨てるだけ。**災害スロットには触らない。**</summary>
        internal static void Forget()
        {
            _id = 0;
            _activationFrame = 0u;
        }

        /// <summary>
        /// <c>SelfTrigger</c> の見張りが鳴ったときだけ通る後始末。**通常は到達しない。**
        ///
        /// <c>DeactivateNow</c> では畳めない。<c>DisasterAI.DeactivateNow</c> は
        /// <c>m_flags &amp; Active(8)</c> が無ければ何もせず、この時点の旗は
        /// <c>Created|Emerging</c> だからである。しかも
        /// <c>ThunderStormAI.IsStillEmerging</c> は <c>m_activationFrame == 0</c> のとき
        /// **恒久的に true を返す**（IL_0015 の <c>brfalse</c>）ので、この災害は
        /// Emerging のまま**永久に Finished にならず、スロットも解放されない**。
        ///
        /// ②の <see cref="TsunamiChain"/> は <c>ReleaseDisaster</c> を「使えるが使わない」と
        /// 判断した。あちらは位相が <c>m_startFrame</c> 基準で自然に進み、最悪でも
        /// 39 ゲーム内時間で自己解放されると IL で確認できていたからである。
        /// **ここはその条件が成り立たない**（進まないことが IL で確定している）ので、
        /// 判断を分ける。放置すると災害スロット 256 を 1 個、都市の寿命ぶん食い潰し、
        /// 災害一覧にも永久に残る。
        /// </summary>
        private static void Abandon(DisasterManager manager, ushort id)
        {
            try
            {
                manager.ReleaseDisaster(id);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon could not release the stuck disaster slot", e);
                }
            }
        }

        private static float SampleHeight(Vector3 pos)
        {
            if (!Singleton<TerrainManager>.exists) return 0f;
            return Singleton<TerrainManager>.instance.SampleDetailHeight(pos);
        }

        /// <summary>
        /// <c>ThunderStormAI</c> を持つ災害プレハブ。**キャッシュしない**
        /// （<c>TsunamiChain.FindTsunamiInfo</c> と同じ判断。走査は
        /// 台風を起こす瞬間にしか走らない）。
        /// </summary>
        private static DisasterInfo FindStormInfo()
        {
            try
            {
                return DisasterManager.FindDisasterInfo<ThunderStormAI>();
            }
            catch
            {
                return null;
            }
        }
    }
}
