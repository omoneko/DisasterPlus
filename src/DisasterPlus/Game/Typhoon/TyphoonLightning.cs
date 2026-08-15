using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風の落雷。<b>sim スレッド専用。</b>
    ///
    /// ── これは演出ではなく実害である ─────────────────────────────
    ///
    /// <c>WeatherManager.QueueLightningStrike</c> が積む落雷は**実体**で、
    /// <c>StrikeNow</c> が <c>BuildingAI.BurnBuilding</c>（建物が燃える）・
    /// <c>TreeManager.BurnTree</c>（木が燃える）・<c>NetAI.CollapseSegment</c>
    /// （送電線が落ちる）を起こす（IL 事実文書 §A-3）。飾りではない。
    ///
    /// 地区政策 <c>CityPlanning.LightningRods (4096)</c> が建物落雷の 70% を消す。
    /// **④は補正しない** —— 避雷針を建てた区域で落雷被害が減るのは正しい挙動である。
    ///
    /// 落雷で燃えた建物は災害グループの <c>m_refCount</c> を上げ、
    /// <c>DisasterAI.IsStillClearing</c>（base）を伸ばす（§A-1 の 5）。
    /// **嵐は火が消えるまで終わらない。** これは正しい挙動で、
    /// <see cref="TyphoonSlot.Deactivate"/> が災害スロットを解放しないことと整合している。
    ///
    /// ── 仕事の本体は「上限 20 発に当てないこと」 ──────────────────────
    ///
    /// 判断は全部 <see cref="LightningBudget"/>（Core、テスト 8 件）にある。ここは
    /// **④が撃ったぶんの台帳**と、実際の撒き方だけを持つ。台帳は
    /// <see cref="LightningBudget.QueueCapacity"/> 要素の固定長配列で、0 が空きスロット。
    /// **<c>List</c> を毎 tick 作らない** —— ここは毎 sim tick 通る経路である。
    ///
    /// 在庫から落とすのは<b>発火した時点ではなく</b>
    /// <see cref="LightningBudget.HasExpired"/>（予定 + 45 フレーム）である。
    /// ゲームがスロットを空けるのがそこだから（§A-3）。
    ///
    /// ── 環境落雷は「撒き続ける」に倒した（設計書 §4.2） ─────────────────
    ///
    /// <c>m_currentRain &gt; 0.8 &amp;&amp; m_lightningQueue.m_size == 0</c> のとき、ゲームは
    /// 自前の落雷を積む（§A-3）。**条件は「キューが空」なので、④が 1 発でも積んで
    /// いれば環境落雷は完全に止まる。** ④は台風の全期間にわたって雨を 0.8 以上に
    /// 保つ（<see cref="TyphoonWeather"/>）ので、撒き続けるほうへ倒す ——
    /// <c>inFlight == 0</c> なら間隔を待たずに必ず 1 発補充する。
    ///
    /// **ただし予算が 0 のときは補充しない。** 強度が高いと宿主の嵐の取り分だけで
    /// 予算を使い切る（強度 170 あたりから <see cref="LightningBudget.Allowance"/> は 0）。
    /// そこで無理に積むと、宿主の嵐自身の落雷が捨てられる —— 環境落雷が 1 発戻ることより
    /// そちらのほうが悪い。**しかもその状態では宿主が大量に積んでいるので、
    /// 実際にはキューが空になっていない。** 診断に予算と在庫の両方を出すのは、
    /// この 2 つの状態を後から見分けるためである。
    ///
    /// ── 落着点は壁雲に偏らせる ───────────────────────────────
    ///
    /// 実際の台風の雷は眼ではなく眼の外側の壁雲で起きる（設計書 §4.2）。
    /// 眼の中を避けると**画面上で眼が読める**という実利もある。
    /// 乱数は <see cref="DeterministicRandom"/> だけを使う ——
    /// ここで決めるのはバニラが引く値ではなく④が発明した判断である
    /// （<c>VanillaRandomizer</c> は使わない）。
    ///
    /// なお環境落雷の落着点は**マップ一様の乱数点**（1 引数版 IL_000F–004D）なので、
    /// 台風から遠く離れて落ちる雷を④のものと数えないこと。
    /// </summary>
    public static class TyphoonLightning
    {
        /// <summary>
        /// 最強のときの発射間隔（フレーム）。1 tick に 1 発までしか積まないので、
        /// これは「連射の上限」である。実際にはたいてい
        /// <see cref="LightningBudget.Allowance"/> のほうが先に効く。
        /// </summary>
        private const uint MinIntervalFrames = 30u;

        /// <summary>いちばん弱いときの発射間隔（フレーム）。</summary>
        private const uint MaxIntervalFrames = 240u;

        /// <summary>強度の最大値（<c>DisasterData.m_intensity</c> は byte）。</summary>
        private const float MaxIntensity = 255f;

        /// <summary>壁雲の外側どこまで散らすか（<c>WallFraction</c> に対する倍率）。</summary>
        private const float WallSpread = 1.3f;

        /// <summary>1 発ぶんに使う乱数の本数。塩をこれだけ進めれば系列が重ならない。</summary>
        private const uint DrawsPerStrike = 5u;

        private const float TwoPi = 6.2831855f;

        /// <summary>
        /// ④が積んだ落雷の予定フレーム。0 は空きスロット。
        /// **固定長**（毎 sim tick 通る経路で <c>List</c> を作らない）。
        /// </summary>
        private static readonly uint[] _scheduled = new uint[LightningBudget.QueueCapacity];

        private static int _inFlight;
        private static int _lastQueued;
        private static int _lastRejected;
        private static int _totalQueued;
        private static int _totalRejected;
        private static int _lastVanillaReserve;
        private static int _lastAllowance;
        private static uint _nextStrikeFrame;
        private static uint _salt;
        private static bool _errorLogged;

        /// <summary>
        /// 「キューが満杯で捨てられた」を 1 回だけ <c>Log.Warn</c> で出したか。
        /// <see cref="_errorLogged"/> と分けてあるのは、片方が立つともう片方の
        /// 1 回目が黙って消えるから。どちらも <see cref="Reset"/> で戻さない
        /// （毎 tick の経路に <c>Log.Warn</c> を置かないため）。
        /// </summary>
        private static bool _rejectionLogged;

        /// <summary>④が今キューに載せている数（予定 + 45 フレームで落ちる）。</summary>
        public static int InFlight { get { return _inFlight; } }

        /// <summary>直近の tick に積んだ数（0 か 1）。</summary>
        public static int LastQueued { get { return _lastQueued; } }

        /// <summary>直近の tick に**ゲームに捨てられた**数。**常に 0 であるべき値。**</summary>
        public static int LastRejected { get { return _lastRejected; } }

        /// <summary>この台風が積んだ累計。</summary>
        public static int TotalQueued { get { return _totalQueued; } }

        /// <summary>
        /// この台風でゲームに捨てられた累計。**0 以外なら上限 20 に当たっている**
        /// ＝宿主の嵐や他 MOD の落雷まで消えている（<see cref="LightningBudget"/> の doc）。
        /// </summary>
        public static int TotalRejected { get { return _totalRejected; } }

        /// <summary>直近に見積もった宿主の嵐の取り分。</summary>
        public static int LastVanillaReserve { get { return _lastVanillaReserve; } }

        /// <summary>直近の④の取り分。0 は異常ではない（クラス doc）。</summary>
        public static int LastAllowance { get { return _lastAllowance; } }

        /// <summary>
        /// sim スレッド。<c>TyphoonFeature.OnSimulationTick</c> のポーズガードより下、
        /// <c>TyphoonWeather.Drive</c> の直後に、台風が動いているときだけ呼ぶ。
        ///
        /// <paramref name="snapshot"/> は使わない。あれは**前 tick の状態**なので
        /// （<see cref="TyphoonSnapshot"/> の T3 節の注記）、位置も強度も
        /// <see cref="TyphoonController"/> の static から同じスレッドで直接読む。
        /// 引数に残してあるのは他の要素と呼び出しの形をそろえるためである
        /// （<c>TyphoonWeather.Drive</c> と同じ扱い）。
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame)
        {
            try
            {
                Step(frame);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon lightning failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyLightning",
                             "typhoon lightning failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(uint frame)
        {
            _lastQueued = 0;
            _lastRejected = 0;

            Expire(frame);

            _lastVanillaReserve = LightningBudget.VanillaMaxStrikes(
                LightningBudget.VanillaRampCount(frame, TyphoonController.ActivationFrame,
                                                 TyphoonController.TotalFrames,
                                                 TyphoonController.Intensity));
            _lastAllowance = LightningBudget.Allowance(_inFlight, _lastVanillaReserve);

            // ★ キューを空にしない（クラス doc）。在庫が 0 なら間隔を待たない。
            bool due = _inFlight == 0 || frame >= _nextStrikeFrame;
            if (due && _lastAllowance > 0) QueueOne(frame);

            WriteDiag(frame);
        }

        /// <summary>
        /// ゲームがスロットを空けたぶんを台帳から落とす（§A-3、予定 + 45 フレーム）。
        /// </summary>
        private static void Expire(uint frame)
        {
            int inFlight = 0;
            for (int i = 0; i < _scheduled.Length; i++)
            {
                if (_scheduled[i] == 0u) continue;
                if (LightningBudget.HasExpired(_scheduled[i], frame)) _scheduled[i] = 0u;
                else inFlight++;
            }
            _inFlight = inFlight;
        }

        private static void QueueOne(uint frame)
        {
            float stormRadius = TyphoonController.StormRadius;
            // プレハブ半径が読めていなければ 0。**推測した半径に落とさない**（設計書 §6）。
            if (!(stormRadius > 0f)) return;

            if (!Singleton<WeatherManager>.exists) return;

            var centre = TyphoonController.Centre;
            if (float.IsNaN(centre.X) || float.IsNaN(centre.Z)) return;

            int slot = FreeSlot();
            if (slot < 0) return;   // 台帳が満杯 ＝ 予算計算と矛盾するので黙って降りる

            uint seed = TyphoonController.DisasterId;
            uint salt = _salt;
            _salt += DrawsPerStrike;

            // 落着点は壁雲に偏らせる（実際の台風の構造。設計書 §4.2）。
            float angle = DeterministicRandom.Unit(seed, salt) * TwoPi;
            float band = TyphoonProfile.EyeFraction
                       + DeterministicRandom.Unit(seed, salt + 1u)
                         * (TyphoonProfile.WallFraction * WallSpread - TyphoonProfile.EyeFraction);
            float d = stormRadius * band;

            var p = new Vector3(centre.X + Mathf.Cos(angle) * d, 0f,
                                centre.Z + Mathf.Sin(angle) * d);
            // §A-1 が ThunderStormAI 自身で使っている高さ取得。sim スレッド。
            if (Singleton<TerrainManager>.exists)
            {
                p.y = Singleton<TerrainManager>.instance
                      .SampleRawHeightSmoothWithWater(p, false, 0f);
            }

            // §A-1 と同じ姿勢の作り方。
            var q = Quaternion.AngleAxis(DeterministicRandom.Unit(seed, salt + 2u) * 360f,
                                         Vector3.up)
                  * Quaternion.AngleAxis(DeterministicRandom.Unit(seed, salt + 3u) * 30f - 15f,
                                         Vector3.right);

            // ★ 遅延は自分で足す。EarliestFrame より手前はゲームが切り上げるので（§A-3）、
            //   足さないと台帳の予定フレームと実際の発火フレームがずれる。
            uint when = LightningBudget.EarliestFrame(frame)
                      + (uint)(DeterministicRandom.Unit(seed, salt + 4u)
                               * LightningBudget.MaxDelayFrames);

            var group = GroupOf(TyphoonController.DisasterId);

            // ★ 戻り値を必ず見る。false は「キューが満杯で捨てられた」の唯一の合図（§A-3）。
            if (Singleton<WeatherManager>.instance.QueueLightningStrike(when, p, q, group))
            {
                _scheduled[slot] = when;
                _inFlight++;
                _lastQueued++;
                _totalQueued++;
                _nextStrikeFrame = frame + IntervalFor(TyphoonController.Intensity);
            }
            else
            {
                _lastRejected++;
                _totalRejected++;
                // **黙って捨てさせない。** 1 回目だけ Warn を出し、以後はスロットル付きの
                // Diag へ落とす（Log.Warn は毎 tick の経路に置けない）。
                if (!_rejectionLogged)
                {
                    _rejectionLogged = true;
                    Log.Warn("typhoon lightning was dropped by the game: the strike queue is "
                             + "full (cap " + LightningBudget.QueueCapacity + "). The host "
                             + "storm's own strikes are being thrown away too.");
                }
                // 上限に当たったなら、次の tick も当たる。間隔を空けて叩き続けない。
                _nextStrikeFrame = frame + MaxIntervalFrames;
            }
        }

        /// <summary>強いほど短く。0〜255 を <see cref="MaxIntervalFrames"/>〜<see cref="MinIntervalFrames"/> に写す。</summary>
        private static uint IntervalFor(byte intensity)
        {
            float t = intensity / MaxIntensity;
            float interval = MaxIntervalFrames - (MaxIntervalFrames - MinIntervalFrames) * t;
            if (interval < MinIntervalFrames) interval = MinIntervalFrames;
            return (uint)interval;
        }

        private static int FreeSlot()
        {
            for (int i = 0; i < _scheduled.Length; i++)
            {
                if (_scheduled[i] == 0u) return i;
            }
            return -1;
        }

        /// <summary>
        /// 落雷を台風の災害グループに束ねる。②の <c>LongPeriodDamage.GroupOf</c> と同じ形。
        /// null でも <c>QueueLightningStrike</c> は通る（グループに入らないだけ）。
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            // Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            // new GameObject を走らせる main スレッド専用 API。ここは sim スレッド。
            if (disasterId == 0 || !Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        /// <summary>
        /// **撃った数が 0 のときも毎回出す**（③の「延焼が動いているか診断から一切
        /// 見えなかった」失敗を繰り返さない）。<c>Log.Diag</c> は同一キーで 512 sim
        /// フレームに 1 回に間引かれるが、**引数の文字列連結は毎 tick 走ってしまう**ので
        /// <c>DiagEnabled</c> で先に落とす（C# は引数を呼び出し前に評価し切る）。
        /// </summary>
        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyLightning",
                "lightning: inFlight=" + _inFlight
                + " queued=" + _lastQueued + " total=" + _totalQueued
                + " rejected=" + _lastRejected + "/" + _totalRejected
                + " vanillaReserve=" + _lastVanillaReserve
                + " allowance=" + _lastAllowance
                + (_inFlight > 0
                    ? "  (queue kept non-empty: environmental lightning suppressed)"
                    : "  (queue may be empty: the game can queue its own strike)"));
        }

        /// <summary>
        /// 台風を手放すとき（<c>TyphoonController.Forget</c>）とレベルアンロードで呼ぶ。
        /// **在庫を都市や次の台風へ持ち越さない。** 持ち越すと、次の台風は
        /// 実際には空いているキューを「埋まっている」と見て撃たなくなる。
        ///
        /// 冪等である（重ねて呼んでよい）。
        /// </summary>
        public static void Reset()
        {
            for (int i = 0; i < _scheduled.Length; i++) _scheduled[i] = 0u;

            _inFlight = 0;
            _lastQueued = 0;
            _lastRejected = 0;
            _totalQueued = 0;
            _totalRejected = 0;
            _lastVanillaReserve = 0;
            _lastAllowance = 0;
            _nextStrikeFrame = 0u;
            _salt = 0u;
            // ★ _errorLogged は戻さない（ゲームのビルドに対する事実であって
            //    都市ごとの状態ではない。TyphoonWeather / TyphoonReader と同じ扱い）。
        }
    }
}
