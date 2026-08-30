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
    /// 見分けは<b>災害 ID を覚えておくこと</b>で行う（<see cref="LastId"/>）。
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

        /// <summary>この災害 ID は海溝型か。<paramref name="id"/> が 0 なら常に false。</summary>
        public static bool IsTrenchQuake(ushort id)
        {
            return id != 0 && id == _id;
        }

        /// <summary>レベルのロード／アンロードで呼ぶ。**都市をまたいで持ち越さない。**</summary>
        public static void Reset()
        {
            _id = 0;
            _epicentre = new Vec3(0f, 0f, 0f);
            _searchDistanceMetres = 0f;
            Detail = null;
        }

        /// <summary>
        /// **sim スレッド。** <paramref name="point"/> にいちばん近い海で地震を起こす。
        /// 起こせなかったら false を返し、理由を <see cref="Detail"/> に残す。
        /// </summary>
        /// <param name="intensity">バニラの強度スライダーの生値（0〜255）。</param>
        public static bool Raise(Vec3 point, byte intensity)
        {
            try
            {
                return RaiseCore(point, intensity);
            }
            catch (System.Exception e)
            {
                Detail = "raising the trench earthquake threw " + e.GetType().Name;
                Log.Error("trench earthquake failed", e);
                return false;
            }
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

            Vec3 sea;
            float distance;
            if (!NearestSea(point, SeaSearch.MaxRing, out sea, out distance))
            {
                // ★★ **内陸マップでは海が無いのが正しい答えである。**
                //    「それらしい地点」を作って起こさない —— 海溝型地震の意味が消える。
                Detail = "no open sea at least "
                         + MinDepthMetres.ToString("F0") + " m deep within "
                         + (SeaSearch.StepMetres * SeaSearch.MaxRing)
                         .ToString("F0") + " m of the point you clicked; a trench "
                         + "earthquake needs open water";
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

            _id = id;
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
                return NearestSea(point, PreviewRings, out sea, out distanceMetres);
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
            sea = new Vec3(0f, 0f, 0f);
            distanceMetres = 0f;

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
            //    最後まで見つからなければ、途中でいちばん深かった所へ落とす
            //    （浅い海しか無いマップでも津波は起こす —— 小さくなるだけ）。
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

                // ★★ **源のまわりが開けた海であること。**（2026-08-30、最終検証）
                //    外力は半径 1280 m の円盤で、その中に陸があると
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
        /// プレビューで見るリング数。<c>SeaSearch.MaxRing</c>(96) は 9,216 m ぶんで
        /// 37,249 点になる。24 リング ＝ 2,304 m ＝ 2,401 点。
        /// </summary>
        private const int PreviewRings = 24;

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
