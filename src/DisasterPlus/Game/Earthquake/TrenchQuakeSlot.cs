using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **海溝型地震。** プレイヤーが指した地点に<b>いちばん近い海</b>で地震を起こし、
    /// <b>その地震だけ</b>が津波を連れてくる。**sim スレッド専用。**
    ///
    /// ── 所有者の指示（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 地震について、バニラの断層地震だけでなく海溝型地震を追加したいです。
    /// &gt; 現在断層型地震でも津波が発生していますが、バニラの地震では津波は
    /// &gt; 発生させず、新たに新設する海溝型地震（アイコンも新規で）でのみ発生する
    /// &gt; ようにしてください。発生は、アイコンクリック→左クリックした場所に
    /// &gt; 一番近い海で発生にしてください。
    ///
    /// ── ★★ 何が「海溝型」なのか（実装上の定義）────────────────────────
    ///
    /// ゲームの災害には型が 1 つしか無い（<c>EarthquakeAI</c>）。**新しい災害種別を
    /// 足すことはできない。** そこで「海溝型」は
    /// <b>この MOD が海の上に自分で起こした地震</b>と定義する。
    /// 見分けは<b>災害 ID と乱数種の組</b>を覚えておくことで行う
    /// （<see cref="LastId"/> と <see cref="IsTrenchQuake"/>）。
    ///
    /// ★★ **ID だけでは足りない。**（2026-08-30、最終検証で再現された）
    ///   <c>DisasterManager.CreateDisaster</c> は index 1 から
    ///   <c>m_flags == None</c> の枠を<b>先頭一致で使い回し</b>、
    ///   <c>ReleaseDisaster</c> は枠を <c>default(DisasterData)</c> で潰す。
    ///   つまり海溝型が終わると<b>次の災害がたいてい同じ番号を取る</b>。
    ///   ID だけで見ていたので、その災害まで海溝型と誤認していた ——
    ///   <b>バニラの地震の断層が抑止され、海の上なら津波まで付いた。</b>
    ///   これは所有者の指示「バニラの地震では津波は発生させず」に真っ向から反する。
    ///
    ///   <c>CreateDisaster</c> は枠ごとに <c>m_randomSeed</c> を
    ///   sim の乱数から引き直す（IL 実測）。**種が変わっていたら別人**である。
    ///
    /// ★ <b>震源の位置では見分けない。</b> プレイヤーがバニラの災害パネルから
    ///   海の上に地震を置くこともできるが、それは断層型のつもりで置いたものである。
    ///   位置で判定すると、その地震にも津波が付いてしまい、
    ///   「バニラの地震では津波は発生させず」という指示に反する。
    ///
    /// ── 罠（③④⑤で踏んだものと同じ。IL 実測済み）──────────────────────
    ///
    /// <list type="number">
    /// <item><c>CreateDisaster</c> の<b>戻り値を必ず見る</b>。false のとき出力は 0 で、
    ///   そのまま書くと<b>他人の災害スロット（0 番）を書き潰す</b></item>
    /// <item><c>m_flags |= SelfTrigger (64)</c> が要る。<c>EarthquakeAI.StartDisaster</c> は
    ///   このビットを見て、立っていなければ<b>永久に Emerging のまま固まる</b></item>
    /// <item>プレハブが引けない環境（ND DLC 非所持）では<b>断って理由を残す</b></item>
    /// </list>
    /// </summary>
    public static class TrenchQuakeSlot
    {
        /// <summary>
        /// <c>DisasterData.Flags.SelfTrigger</c>。**立てないと Emerging で固まる。**
        /// </summary>
        private const ushort SelfTrigger = 64;

        private static ushort _id;

        /// <summary>
        /// 起こしたときの <c>DisasterData.m_randomSeed</c>。
        /// **番号が使い回されたことを見抜く唯一の手掛かり**（クラス doc の ★★）。
        /// </summary>
        private static ulong _seed;
        private static Vec3 _epicentre;
        private static float _searchDistanceMetres;

        /// <summary>
        /// 直近に起こした海溝型地震の災害 ID。0 なら「まだ 1 度も起こしていない」。
        /// <c>TsunamiChain</c> はこれと一致する地震にしか津波を付けない。
        /// </summary>
        public static ushort LastId { get { return _id; } }

        /// <summary>実際に震源になった海の座標。</summary>
        public static Vec3 Epicentre { get { return _epicentre; } }

        /// <summary>指した地点から震源までの距離（m）。**思ったより遠いときに名乗る。**</summary>
        public static float SearchDistanceMetres { get { return _searchDistanceMetres; } }

        /// <summary>直近の顛末（**英語・診断用**）。断ったときは必ず入る。</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// <see cref="Raise"/> を試みた回数。**ツールはこれが増えるのを待つ。**
        ///
        /// ★★ 地震を起こすのは sim スレッドなので、ツール（main）は
        ///   <b>成否をその場では知れない</b>。以前はクリックした瞬間に
        ///   ツールを閉じていたので、sim が断ったときは
        ///   <b>印は出る・ツールは閉じる・何も起きない</b>——
        ///   画面上は<b>死んだボタン</b>と見分けが付かなかった
        ///   （2026-08-30、第 4 回検証）。
        /// </summary>
        public static int AttemptSerial { get; private set; }

        /// <summary>直近の <see cref="Raise"/> が成功したか。</summary>
        public static bool LastAttemptOk { get; private set; }

        /// <summary>
        /// この災害 ID は海溝型か。<paramref name="id"/> が 0 なら常に false。
        ///
        /// ★★ **番号だけで判定しない。**<c>m_randomSeed</c> も照合する ——
        ///   番号は使い回されるので、これが無いと<b>次のバニラの地震を
        ///   海溝型と誤認する</b>（クラス doc の ★★）。
        ///   照合できない状況（バッファが読めない）では<b>false</b>を返す。
        ///   誤って津波を付けるより、付けないほうが指示に近い。
        /// </summary>
        public static bool IsTrenchQuake(ushort id)
        {
            if (id == 0 || id != _id) return false;

            try
            {
                DisasterManager manager = Singleton<DisasterManager>.instance;
                if (manager == null || manager.m_disasters == null) return false;

                DisasterData[] buffer = manager.m_disasters.m_buffer;
                if (buffer == null || id >= buffer.Length) return false;

                // ★ 枠が空いた ＝ もう自分の災害ではない。忘れる。
                if (buffer[id].m_flags == DisasterData.Flags.None)
                {
                    _id = 0;
                    _seed = 0UL;
                    return false;
                }

                if (buffer[id].m_randomSeed == _seed) return true;

                // ★★ **種が違う ＝ 番号が使い回された。** 忘れる。
                //    忘れないと、以後この番号を見るたびに同じ照合を繰り返し、
                //    <c>LastId</c> はセッションのあいだ 0 に戻らない
                //    （2026-08-30、第 5 回検証）。
                _id = 0;
                _seed = 0UL;
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>レベルのロード／アンロードで呼ぶ。**都市をまたいで持ち越さない。**</summary>
        public static void Reset()
        {
            _id = 0;
            _seed = 0UL;
            _epicentre = new Vec3(0f, 0f, 0f);
            _searchDistanceMetres = 0f;
            AttemptSerial = 0;
            LastAttemptOk = false;
            Detail = null;
        }

        /// <summary>
        /// **sim スレッド。** <paramref name="point"/> にいちばん近い海で地震を起こす。
        /// 起こせなかったら false を返し、理由を <see cref="Detail"/> に残す。
        /// </summary>
        /// <param name="intensity">バニラの強度スライダーの生値（0〜255）。</param>
        public static bool Raise(Vec3 point, byte intensity)
        {
            bool ok;

            try
            {
                ok = RaiseCore(point, intensity);
            }
            catch (System.Exception e)
            {
                Detail = "raising the trench earthquake threw " + e.GetType().Name;
                Log.Error("trench earthquake failed", e);
                ok = false;
            }

            // ★★ **必ず名乗る。** ツールはこの 2 つを見て、閉じるか開いたままかを決める。
            LastAttemptOk = ok;
            AttemptSerial++;

            if (!ok)
            {
                Log.Info("trench earthquake NOT raised: "
                         + (Detail ?? "no reason was recorded")
                         + ". The tool stays armed so it can be tried again");
            }

            return ok;
        }

        private static bool RaiseCore(Vec3 point, byte intensity)
        {
            DisasterInfo info = DisasterManager.FindDisasterInfo<EarthquakeAI>();
            if (info == null)
            {
                Detail = "no EarthquakeAI prefab is loaded; the Natural Disasters DLC is "
                         + "required for the trench earthquake";
                return false;
            }

            // ★★ **断るのは「津波がまだ途中」のあいだだけ。**（第 4 回検証）
            //
            //    前の版は <c>IsTrenchQuake(_id)</c>（＝災害スロットがまだ生きているか）
            //    で断っていた。ところが <c>EarthquakeAI</c> の Clearing は
            //    <c>(|x| + 4800) &gt; elapsed*0.125 - 1000 - L</c> で終わるので、
            //    スロットは<b>クリックから 17〜35 実分</b>も押さえられる。
            //    津波のほうは 7 分 40 秒で終わっているのに、そのあと
            //    <b>30 分ちかく「まだ走っている」と断り続けていた。</b>
            //
            //    断る理由は<b>津波を 1 本しか追えないこと</b>であって、
            //    地震が長生きすることではない。だから津波の状態で見る。
            //    （そのころには最初の地震は Active を過ぎていて地面も割らないので、
            //     海溝型の印を 2 つ目へ移して構わない。）
            if (IsTrenchQuake(_id) && TsunamiChain.StillOwes(_id))
            {
                Detail = "the tsunami from the previous trench earthquake (#" + _id
                         + ") has not finished yet; only one is tracked at a time. "
                         + "Wait for it - the wave is still on its way";
                return false;
            }

            Vec3 sea;
            float distance;
            bool sawDeepWater;
            if (!NearestSea(point, SearchRings, out sea, out distance, out sawDeepWater))
            {
                // ★★ **内陸マップでは海が無いのが正しい答えである。**
                //    「それらしい地点」を作って起こさない —— 海溝型地震の意味が消える。
                //
                // ★★ **理由を取り違えない。** 深い水はあったが狭かった場合と、
                //    そもそも深い水が無かった場合は、プレイヤーの次の一手が違う
                //    （前者は沖へ寄る、後者は諦める）。
                float reach = SeaSearch.StepMetres * SearchRings;

                Detail = sawDeepWater
                    ? ("the water within " + reach.ToString("F0")
                       + " m of the point you clicked is deep enough but too narrow: a "
                       + "trench earthquake needs open sea for "
                       + (DisasterPlus.Core.Earthquake.TsunamiSource.RadiusMetres
                          * OpenSeaFraction).ToString("F0")
                       + " m in every direction, or the source resonates instead of "
                       + "radiating. Click further out to sea")
                    : ("no sea at least " + MinDepthMetres.ToString("F0")
                       + " m deep within " + reach.ToString("F0")
                       + " m of the point you clicked; a trench earthquake needs "
                       + "deep open water");
                return false;
            }

            ushort id;
            if (!Singleton<DisasterManager>.instance.CreateDisaster(out id, info))
            {
                // ★★ **戻り値を見ないと 0 番のスロットを書き潰す。**
                Detail = "CreateDisaster refused (the disaster buffer is full?)";
                return false;
            }

            DisasterData[] buffer = Singleton<DisasterManager>.instance.m_disasters.m_buffer;

            buffer[id].m_targetPosition = new Vector3(sea.X, sea.Y, sea.Z);
            buffer[id].m_intensity = intensity;

            // ★★ これが無いと StartDisaster が即 return し、**Emerging のまま固まる。**
            buffer[id].m_flags |= (DisasterData.Flags)SelfTrigger;

            // ★ <c>DisasterAI.StartDisaster</c> は protected（IL 確認済み）なので
            //   直接は呼べない。公開ラッパーの <c>StartNow</c> を使う ——
            //   あれは <c>m_flags &amp; 0x3C</c>（Emerging|Active|Clearing|Finished）が
            //   立っていなければ <c>StartDisaster</c> を呼ぶだけで、
            //   <c>CreateDisaster</c> 直後は <c>Created(0x01)</c> だけなので必ず通る。
            //   <b>SelfTrigger(64) は 0x3C に含まれない</b>ので、先に立てても判定は
            //   変わらない（③の <c>FireWhirlSpawner</c> と同じ道）。
            info.m_disasterAI.StartNow(id, ref buffer[id]);

            // ★★ **追う相手を乗り換える。**（Codex P1）前の地震が Clearing で
            //    まだ生きていると、連鎖は古い相手を追い続けて<b>この地震の
            //    Emerging→Active を見逃す</b>。前の津波は出し終えている
            //    （上の StillOwes が保証）ので、ここで捨ててよい。
            TsunamiChain.Retarget();

            _id = id;
            _seed = buffer[id].m_randomSeed;
            _epicentre = sea;
            _searchDistanceMetres = distance;
            Detail = null;

            Log.Info("trench earthquake " + id + " raised at (" + sea.X.ToString("F0") + ","
                     + sea.Z.ToString("F0") + "), " + distance.ToString("F0")
                     + " m from the point that was clicked, intensity " + intensity
                     + ". This is the ONLY kind of earthquake that brings a tsunami");
            return true;
        }

        /// <summary>
        /// <paramref name="point"/> にいちばん近い海。
        /// 順序は <see cref="SeaSearch"/>（Core・テスト済み）が決め、
        /// ここは<b>そこに水があるかを聞くだけ</b>である。
        ///
        /// ★ <b>川や湖ではなく海を採る。</b><c>HasWater</c> は流れる水にも true を
        ///   返すので、**水面が海面の高さにあること**も見る
        ///   （<c>WaterSimulation.m_currentSeaLevel</c>）。市街地の川で
        ///   海溝型地震が起きると、津波の説明が成立しない。
        /// </summary>
        internal static bool TryFindNearestSea(Vec3 point, out Vec3 sea,
                                               out float distanceMetres)
        {
            try
            {
                // ★★ **プレビューはリング数を絞る。**（2026-08-30、最終検証）
                //    全走査は 37,249 点で、1 点ごとに HasWater と WaterLevel が
                //    水シミュの読み取りロックを取り直す。RenderOverlay は
                //    6 フレームごとに呼ぶので、毎秒 70 万回の錠のやり取りになっていた。
                return NearestSea(point, SearchRings, out sea, out distanceMetres);
            }
            catch
            {
                sea = new Vec3(0f, 0f, 0f);
                distanceMetres = 0f;
                return false;
            }
        }

        /// <summary>
        /// 震源に使える海を探す。<paramref name="maxRings"/> でリング数を絞れる
        /// （プレビュー用）。<b>見つからなければ断る</b> ——
        /// 浅い海に落とすくらいなら、起こさないほうがよい（下の ★★）。
        /// </summary>
        private static bool NearestSea(Vec3 point, int maxRings,
                                       out Vec3 sea, out float distanceMetres)
        {
            bool ignored;
            return NearestSea(point, maxRings, out sea, out distanceMetres, out ignored);
        }

        /// <summary>
        /// 同上。<paramref name="sawDeepWater"/> に「十分に深い海はあったが
        /// <see cref="IsOpenSea"/> で落ちた」かどうかを返す。
        ///
        /// ★★ **断る理由を取り違えない。**（2026-08-30、最終検証）
        ///   30 m のフィヨルドや広い川は深さの条件を通り、開けていないことで落ちる。
        ///   それを「そんなに深い海は無い」と言うと<b>嘘になる</b> ——
        ///   プレイヤーがもらえる説明はこの 1 文だけなのだから、外してはいけない。
        /// </summary>
        private static bool NearestSea(Vec3 point, int maxRings,
                                       out Vec3 sea, out float distanceMetres,
                                       out bool sawDeepWater)
        {
            sea = new Vec3(0f, 0f, 0f);
            distanceMetres = 0f;
            sawDeepWater = false;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null) return false;

            float seaLevel = terrain.WaterSimulation != null
                ? terrain.WaterSimulation.m_currentSeaLevel
                : DefaultSeaLevelMetres;

            // ★★ **いちばん近い海ではなく、いちばん近い「深い」海を採る。**
            //    （2026-08-30、オフライン再現で分かった）
            //
            //    ゲームの浅水ソルバは流量を <c>v = min(v, m_height)</c> で
            //    <b>水深に頭打ちする</b>。だから浅い海はどんなに強く押しても
            //    大きな波を運べない。実測（tools/WaterSolverSim、格子 1081）:
            //
            //        水深 40 m: 隆起 17.8 m、環は 5.3 km 先でも 2.1 m
            //        水深 10 m: 隆起  4.2 m、環は 2.4 km でほぼ消える
            //
            //    「海溝型」はそもそも<b>沖の深い海</b>で起きるものなので、
            //    深いほうを採るのは物理的にも正しい。
            //
            //    近い順に走査し、**十分に深い海が見つかった時点で確定**する。
            //    ★ **見つからなければ断る（false）。**「いちばん深かった所へ落とす」
            //      という逃げ道は置いていない —— 浅い海はこのソルバでは波を運べず、
            //      震源に穴が開くだけになるからである（TsunamiSource のクラス doc）。
            int count = SeaSearch.CountUpTo(maxRings < 0 ? 0 : (maxRings > SeaSearch.MaxRing ? SeaSearch.MaxRing : maxRings));
            for (int i = 0; i < count; i++)
            {
                float dx, dz;
                if (!SeaSearch.At(i, out dx, out dz)) break;

                float x = point.X + dx;
                float z = point.Z + dz;
                if (x < -MapHalfExtent || x > MapHalfExtent) continue;
                if (z < -MapHalfExtent || z > MapHalfExtent) continue;

                var xz = new Vector2(x, z);
                if (!terrain.HasWater(xz)) continue;

                float level = terrain.WaterLevel(xz);

                // ★ 海面より目に見えて高い水は川・湖である。
                if (level > seaLevel + RiverToleranceMetres) continue;

                // ★ 水深も見る。**波打ち際は「海」ではない** ——
                //   そこで起こすと震源が陸に見える。
                // ★★ **水深はソルバと同じ量で測る**（TsunamiWave.DepthAt の doc）。
                //    SampleRawHeightSmooth は m_rawHeights2 を読むが、
                //    水シミュが使うのは m_blockHeights である。
                if (TsunamiWave.DepthAt(terrain, x, z) < MinDepthMetres) continue;

                // ★ ここまで来た ＝ 十分に深い海はあった。断る理由が変わる。
                sawDeepWater = true;

                // ★★ **源のまわりが開けた海であること。**（2026-08-30、最終検証）
                //    外力の円盤は半径 1280 m で、確かめるのはその 0.85 倍
                //    （1,088 m）の 8 方位である。その中に陸があると
                //    <b>源そのものが桶になって共振する</b>（掃引で +111 m まで跳ねた）。
                //    オフライン再現で確かめたのは<b>開けた海</b>だけなので、
                //    保証できない地形では起こさない。
                if (!IsOpenSea(terrain, x, z)) continue;

                sea = new Vec3(x, level, z);
                distanceMetres = SeaSearch.DistanceMetres(dx, dz);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 海を探すリング数。**プレビューと発生で同じ値を使うこと。**
        ///
        /// ★★ 別々にしていたのが 2026-08-30 の指摘だった。プレビューは 24 リング
        ///   （2,304 m）、発生は 96 リング（9,216 m）だったので、
        ///   <b>印が出ないのにクリックすると 9 km 先で地震が起きた</b>。
        ///   画面外で起きるので、プレイヤーには「押しても何も起きない」に見える ——
        ///   まさに実機テストで判定を壊す壊れ方である。
        ///
        /// ★ 揃える先は<b>狭いほう</b>。<c>SeaSearch.MaxRing</c>(96) は 37,249 点で、
        ///   1 点ごとに <c>HasWater</c> と <c>WaterLevel</c> が
        ///   <c>WaterSimulation.BeginRead</c>（<c>Monitor.TryEnter</c> のスピン）を
        ///   取り直す。プレビューは毎フレームに近い頻度で呼ばれるので、
        ///   広いほうへ揃えると<b>水スレッドと錠を奪い合う。</b>
        ///   24 リング ＝ 2,304 m ＝ 2,401 点。
        /// </summary>
        private const int SearchRings = 24;

        /// <summary>源のまわりを確かめる方位の数（8 方位）。</summary>
        private const int OpenSeaProbes = 8;

        /// <summary>
        /// 源のまわりを確かめる距離（外力の半径に対する比）。
        /// 縁ぎりぎりまで見ると入り江が全部落ちるので、少し内側で見る。
        /// </summary>
        private const float OpenSeaFraction = 0.85f;

        /// <summary>
        /// 外力の円盤のまわりが<b>開けた海</b>かどうか。
        ///
        /// ★★ これが無いと、オフライン再現で確かめていない地形
        ///   （入り江・浅瀬・マップ端）で起こしてしまう。
        ///   再現ツールの海は<b>陸が 1 セルも無い平らな海</b>なので、
        ///   そこで取った保証は「開けた海」にしか及ばない。
        /// </summary>
        private static bool IsOpenSea(TerrainManager terrain, float x, float z)
        {
            float r = DisasterPlus.Core.Earthquake.TsunamiSource.RadiusMetres
                      * OpenSeaFraction;

            for (int i = 0; i < OpenSeaProbes; i++)
            {
                float a = 6.2831853f * i / OpenSeaProbes;
                float px = x + Mathf.Cos(a) * r;
                float pz = z + Mathf.Sin(a) * r;

                if (px < -MapHalfExtent || px > MapHalfExtent) return false;
                if (pz < -MapHalfExtent || pz > MapHalfExtent) return false;

                if (!terrain.HasWater(new Vector2(px, pz))) return false;
                if (TsunamiWave.DepthAt(terrain, px, pz) < MinDepthMetres * 0.5f) return false;
            }

            return true;
        }

        /// <summary>
        /// マップ半辺（m）。<c>TsunamiAI.FindSea</c> の IL 実測にある 8640 と同じ。
        /// </summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// 海面が読めないときの既定（m）。<c>WaterSimulation.DEFAULT_SEA_LEVEL</c> の
        /// 実測値である（リフレクションで確認）。
        /// </summary>
        private const float DefaultSeaLevelMetres = 40f;

        /// <summary>これより高い水面は川・湖とみなす（m）。</summary>
        private const float RiverToleranceMetres = 6f;

        /// <summary>これより浅い水は「海」に数えない（m）。</summary>
        private const float MinDepthMetres =
            DisasterPlus.Core.Earthquake.TsunamiSource.ReferenceDepthMetres * 0.6f;
    }
}
