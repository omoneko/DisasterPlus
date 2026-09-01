using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>雲の粒（パフ）で組んだ渦が今どうなっているか。</summary>
    public enum TyphoonCloudFxState
    {
        /// <summary>まだ 1 度も試していない（あるいは都市に入っていない）。</summary>
        Off,

        /// <summary>借りられる粒子エフェクトがこの環境に 1 つも無い。**メッシュへ退避する。**</summary>
        NoEffect,

        /// <summary>台風が居ないので出していない。**不具合ではない。**</summary>
        Idle,

        /// <summary>毎フレーム粒を出している（**借り物の粒子**）。</summary>
        Emitting,

        /// <summary>
        /// **自前の白い雲の粒**で描いている（<see cref="TyphoonVortexPuffFx"/>）。
        /// 2026-08-22 以降はこれが既定で、<see cref="Emitting"/> は退避の側である。
        /// </summary>
        OwnPuffs,

        /// <summary>例外で落ちた。**メッシュへ退避する。**</summary>
        Failed,
    }

    /// <summary>
    /// 台風の渦を**バニラの粒子エフェクトで組んだ入道雲**として描く。<b>main スレッド専用。</b>
    ///
    /// ── 持ち主の指摘（2026-08-22）と、その原因 ────────────────────────
    ///
    /// &gt; 雲のエフェクトが煙になっているので見た目がとても変です。
    /// &gt; 渦を巻く入道雲をイメージして作り直してください。
    ///
    /// <b>原因は素材と形の 2 つで、素材のほうが決定的だった。</b>
    ///
    /// 旧実装は候補の先頭に <c>Factory Smoke</c> を置いていた。出荷アセット
    /// （<c>sharedassets11.assets</c>）から粒子マテリアルのテクスチャを取り出して測ると:
    ///
    /// <code>
    /// マテリアル     テクスチャ         平均 RGB        見た目
    /// Smoke         smoke            ( 75,  78,  80)  暗い煤色の丸い塊（火の粉の点入り）
    /// Steam         steam            (168, 184, 189)  淡い青白の綿。**雲そのもの**
    /// Water         water            (201, 222, 254)  白青の飛沫
    /// Placement Dust placement-dust  (198, 165, 131)  砂色の土煙
    /// IndustryDust  IndustryDust     ( 99,  94,  79)  茶灰の粉塵
    /// </code>
    ///
    /// **<c>Smoke</c> は「灰色に塗られた雲」ではなく、煤と火の粉の絵である。**
    /// <c>startColor</c> は乗算で掛かるので、白を掛けても暗いままで、
    /// どんな形に並べても煙にしか見えない。**指摘は素材の話として正しい。**
    ///
    /// ── だから名前ではなく<b>マテリアル</b>で選ぶ ─────────────────────────
    ///
    /// エフェクト実測文書の在庫は <b>PARTIAL</b>（アセットに在ることと実行時 API が
    /// 返すことは別）である。名前を書き並べて先頭から試す旧実装は、その名前が
    /// このビルドに在るかどうかに賭けていた。いまは
    ///
    /// 1. <c>EffectsWrapper.m_BuiltinEffects</c> と <c>EffectCollection.Effects</c> を
    ///    <b>実際に列挙し</b>、
    /// 2. 各 <c>ParticleEffect</c> の <c>ParticleSystemRenderer.sharedMaterial.name</c> を読み、
    /// 3. <b>マテリアル名で採点して</b>いちばん雲らしいものを採る
    ///    （<c>Steam</c> ≫ <c>Water</c> &gt; 粉塵 &gt; <c>Smoke</c>、
    ///     火・爆発の加算合成マテリアルは**除外**）。
    ///
    /// 名前の一覧は**同点の並べ替えにしか使わない**。ゲームが更新されて名前が変わっても、
    /// マテリアルが同じなら同じ絵が出る。<c>sharedMaterial</c> を読むこと ——
    /// <c>material</c> はレンダラのマテリアルを**複製して差し替える**（共有状態を壊す）。
    ///
    /// ── 形は <see cref="VortexPuffLayout"/> と <see cref="VortexCloudProfile"/> が持つ ────
    ///
    /// <b>雲底（暗く平ら）・塔（明るくもこもこ、2 段）・かなとこ（いちばん白く広い）</b>の
    /// 3 層で、層ごとに**別の複製**を作る。色と粒径は <c>ParticleSystem</c> 側の共有状態で
    /// <c>RenderEffect</c> の呼び出しごとには変えられないので（§B-4）、
    /// **明暗を変えたい単位がそのまま複製の数**になる。旧実装は複製 1 つ・均一な灰色だった。
    ///
    /// 撒き方は <c>RenderEffect(..., timeOffset: -1f, ...)</c> ＝ **継続モード**
    /// （§B-3。バニラの <c>SinkholeAI.RenderInstance</c> と同じ形）で、
    /// <c>SpawnArea(位置, 上, 円盤半径, 帯の高さ)</c> の**円柱**を 1 段ぶんずつ湧かす。
    ///
    /// ── 眼は穴のまま（約束の持ち主が変わった）────────────────────────
    ///
    /// 旧 doc は「Game 側がこの約束を守る義務がある。破ると眼が埋まる ——
    /// 例外は出ないしテストでも捕まらない」と書いていた。**いまは Core が守る。**
    /// 円盤半径も粒径も <see cref="VortexPuffLayout"/> が宣言していて、
    /// <c>EyeClearanceOf</c> をテストが全粒について固定している。
    ///
    /// ここに残る唯一の逃げ道は粒径の下限クランプ（<see cref="MinSizeMetres"/>）である。
    /// **効くのはいちばん小さい渦だけで、そこでも眼は埋まらない**（検算しておく）:
    /// 渦の下限 <see cref="MinVortexRadiusMetres"/> ＝ 900 m のとき塔の粒径は
    /// 0.040 × 900 ＝ 36 m で、40 m へ持ち上がる ——
    /// 粒の届く先が 2 m 伸びる。塔の余白は (0.26 − 0.16 − 0.062 − 0.020) × 900 ＝
    /// 16.2 m なので、14.2 m 残る。**下限を上げるならここを計算し直すこと。**
    ///
    /// ── 毎フレームの仕事量の上限（3 本で決まる）──────────────────────
    ///
    /// 1. <c>RenderEffect</c> は <b><see cref="VortexPuffLayout.PuffCount"/> 回</b>ちょうど
    ///    （88 回。旧実装は 30 回）。1 回あたりの費用は <c>SpawnArea</c> の値渡しと
    ///    距離カリング 2 本で、**確保は 0 バイト**（§E）。撃つ粒子の総数は下の 2. が
    ///    決めていて呼び出し回数には依らないので、増えたのは呼び出しの定数費用だけである。
    /// 2. 新しく湧く粒子は <b><see cref="ParticlesPerSecond"/> 個／秒</b>を全粒で分け合う
    ///    （<see cref="VortexPuffLayout.MagnitudeFor"/> が §B-4 の式を逆に解く。
    ///    フレームレートにもゲーム速度にも依らない）。
    /// 3. 生きている粒子の総数は<b>複製ごとの <c>maxParticles</c></b> で頭打ち
    ///    （2600 + 3600 + 1800 = 8000。旧実装は 1 系で 7000）。
    ///    バニラ自身が <c>pps ×= (1 - fill²)</c> で絞り込む（§B-4）。
    ///
    /// ── 取れなければ静かに諦める ──────────────────────────────
    ///
    /// 列挙も <c>FindEffect</c> も**空を返しうる**（未登録・ゲーム更新・別 MOD）。
    /// そのときは <see cref="TyphoonCloudFxState.NoEffect"/> にして**ログ 1 行**を出し、
    /// 例外は投げない。呼び出し側（<see cref="TyphoonCloud"/>）は旧メッシュ経路へ退避する。
    /// 台風の他の要素は 1 つも止まらない。
    /// </summary>
    public static partial class TyphoonCloudFx
    {
        /// <summary>渦全体で 1 秒あたりに湧かす粒子の数。**④が選んだ演出値。**</summary>
        private const float ParticlesPerSecond = 700f;

        /// <summary>
        /// 渦の外周半径 ÷ 暴風域半径。
        ///
        /// ★★ <b>旧実装は強風域半径（暴風域の 2.2 倍）をそのまま使っていた。</b>
        ///   強度 128 でそれは 10.6 km ＝ <b>直径 21 km</b> で、
        ///   <b>マップの一辺（17.3 km）より大きい。</b> 同じ粒の数を 4.8 倍の面積へ
        ///   撒くことになるので、粒はどこまでも離れて並び、渦にも雲にも見えず
        ///   **ぽつぽつと湧く煙の柱**として読める。持ち主の「煙になっている」という
        ///   指摘には、素材（<c>Smoke</c>）だけでなくこの大きさも効いている。
        ///
        ///   いまは暴風域半径（バニラの落雷散布円・ハザード円盤と同じ式）を基準にし、
        ///   <see cref="MaxVortexRadiusMetres"/> で頭打ちにする。**マップに収まって
        ///   はじめて渦は渦に見える。**
        /// </summary>
        /// <remarks>
        /// ★★ 1.35 -> 2.70（2026-08-29、所有者の指示「台風の雲の大きさを
        ///   2x2 の 4 倍サイズにしてください」）。
        ///   <b>面積 4 倍 ＝ 半径 2 倍</b>である（2x2 は縦横それぞれ 2 倍という意味）。
        ///
        /// ★ 上限（<see cref="MaxVortexRadiusMetres"/> ＝ マップ半辺）はそのまま。
        ///   そこへ当たった時点で「マップに収まる」という別の制約が勝つ。
        /// </remarks>
        /// <remarks>
        /// ★★ 2.70 -> 4.05（2026-09-02、所有者「デフォルトの大きさをもっと大きく」）。
        ///   <b>半径 1.5 倍 ＝ 面積 2.25 倍</b>。上限（マップ半辺）はそのままなので、
        ///   強い台風では今までどおりそこで頭打ちになり、
        ///   <b>効くのは中くらいまでの台風＝「既定の大きさ」</b>である。
        /// </remarks>
        private const float VortexRadiusFactor = 4.05f;

        /// <summary>渦の外周半径の上限（m）。マップ半辺は 8640 m なので、
        /// 直径 12 km ＝ マップの 7 割に収まる。</summary>
        private const float MaxVortexRadiusMetres = 8640f;

        /// <summary>同下限（m）。これより小さいと眼が粒 1 個で埋まる。</summary>
        /// <remarks>
        /// ★ 下限も同じく 2 倍（900 -> 1800）。弱い台風でも 4 倍にする。
        /// ★★ さらに 1800 -> 2700（2026-09-02、半径を 1.5 倍にしたのに合わせる）。
        ///   クラス doc の検算（粒径 0.040 × 半径）はこの下限を前提にしているので、
        ///   **下げるときはあちらを計算し直すこと。**
        /// </remarks>
        private const float MinVortexRadiusMetres = 2700f;

        /// <summary>粒径の下限（m）。小さすぎると点にしか見えない。
        /// **いちばん小さい渦でだけ効き、そこでも眼は埋まらない**（クラス doc の検算）。</summary>
        private const float MinSizeMetres = 40f;

        /// <summary>粒径の上限（m）。大きすぎると板に見えるし、透過の重なりが重い。</summary>
        private const float MaxSizeMetres = 900f;

        /// <summary>雲底の基準高度（m）。**旧実装（900 m）より低い** ——
        /// 入道雲は下面が低く、そこから上へ伸びる。</summary>
        /// <summary>
        /// 雲底の下限（m）。
        ///
        /// ★★ 2026-08-22 に 560 → 1200 へ上げた（実機報告「雲の発生位置が雷よりも
        ///   低いので違和感があります。もっと高くできませんか？」）。
        ///   バニラの落雷は空から地面へ走るので、雲がその下に在ると
        ///   <b>雷が雲を突き抜けて上から降ってくる</b>ように見える。
        /// </summary>
        private const float BaseAltitudeMetres = 1200f;

        /// <summary>
        /// <b>雷の起点（バニラの稲妻メッシュの天辺、m）。</b>まだ測っていなければ 0。
        ///
        /// ★★ **定数で当てずっぽうに上げない。**（2026-09-02、実機報告
        ///   「雷の起点より台風雲のほうが半分ほどの高度」）
        ///
        ///   バニラの稲妻は <c>WeatherProperties.m_lightningMesh</c> を
        ///   <b>着地点に、回転だけ掛けて等倍で</b> 置く（<c>WeatherManager</c> の
        ///   IL_0231-0256）。つまり<b>起点の高さはメッシュの寸法そのもの</b>で、
        ///   コードのどこにも数字が無い。前回 560 → 1200 m と上げたのは
        ///   「雷より低い」を直すためだったが、<b>雷が何 m なのかを測らずに</b>
        ///   決めたので半端なところで止まっていた。
        ///
        ///   だから<b>実行時に読む</b>。<c>m_lightningMesh</c> は public で、
        ///   <c>bounds</c> はメッシュのローカル境界なので、そのまま起点の高さになる。
        ///
        /// ★ 都市ごとに読み直す（<see cref="Reset"/>）。Unity のオブジェクトは
        ///   都市をまたぐと fake-null になるので、static に持ちっぱなしにしない
        ///   （このプロジェクトの「静的キャッシュの罠」）。
        /// </summary>
        private static float _boltTopMetres;

        /// <summary>雷の高さを測れなかったときに使う値（m）。従来の雲頂と同じ。</summary>
        private const float BoltTopFallbackMetres = 3400f;

        /// <summary>
        /// 雷の起点の高さ（m）。**main スレッド。** 1 度測ったら覚える。
        /// </summary>
        private static float BoltTopMetres()
        {
            if (_boltTopMetres > 0f) return _boltTopMetres;

            try
            {
                if (Singleton<WeatherManager>.exists)
                {
                    WeatherProperties props = Singleton<WeatherManager>.instance.m_properties;
                    Mesh mesh = props != null ? props.m_lightningMesh : null;

                    if (mesh != null)
                    {
                        float top = mesh.bounds.max.y;
                        if (top > 1f)
                        {
                            _boltTopMetres = top;
                            Log.Info("typhoon: the vanilla lightning bolt is "
                                     + top.ToString("F0") + " m tall (mesh bounds "
                                     + mesh.bounds.min.y.ToString("F0") + " .. "
                                     + mesh.bounds.max.y.ToString("F0")
                                     + "). The storm cloud is placed so its base sits at "
                                     + "that height - a bolt has to come out of the cloud, "
                                     + "not fall through it.");
                            return _boltTopMetres;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("typhoon: could not measure the lightning bolt ("
                         + e.GetType().Name + "); using "
                         + BoltTopFallbackMetres.ToString("F0") + " m");
            }

            _boltTopMetres = BoltTopFallbackMetres;
            return _boltTopMetres;
        }

        /// <summary>
        /// 雲底の高さ（m）。**雷の天辺と地形の両方より上に置く。**
        /// </summary>
        private static float CloudBaseMetres(float centreY)
        {
            float altitude = centreY + MinClearanceMetres;

            if (altitude < BaseAltitudeMetres) altitude = BaseAltitudeMetres;

            // ★★ 雷の天辺より下に雲があると、**雷が雲を突き抜けて上から降る**。
            float bolt = BoltTopMetres();
            if (altitude < bolt) altitude = bolt;

            return altitude;
        }

        /// <summary>山岳マップで山に埋まらないための、中心の地形高からの最低クリアランス（m）。</summary>
        /// <summary>
        /// 地面（台風の中心の地形高さ）からの最低の浮き（m）。同上で 300 → 900。
        /// </summary>
        private const float MinClearanceMetres = 900f;

        /// <summary>雲の厚み（m）。<c>HeightFraction</c> がこの中のどこに置くかを決める。
        /// **旧実装の 320 m から 2200 m へ上げた** —— 積乱雲の鉛直の伸びが指摘の中心である。
        /// かなとこの帯はここから更に少し上へ出るので、雲頂はおよそ 3000 m になる。
        /// バニラの竜巻メッシュが高さ 2000 m なので、ゲームの尺度から外れてはいない。</summary>
        private const float ThicknessMetres = 2200f;

        /// <summary>雲底が渦に沿って流れる速さ（m/秒）。上層ほど遅い（<c>SwirlFraction</c>）。</summary>
        private const float SwirlMetresPerSecond = 34f;

        /// <summary>半径方向の速さ（m/秒）。下層は吸い込み、かなとこは吹き出す。</summary>
        private const float RadialMetresPerSecond = 20f;

        /// <summary>上昇の速さ（m/秒）。塔の中がいちばん強い。</summary>
        private const float RiseMetresPerSecond = 16f;

        /// <summary>遠景から見えること（借り元は 500〜1000 m しかない）。</summary>
        private const float VisibilityMetres = 12000f;

        /// <summary>エフェクトを探し直すまでに空けるフレーム数。
        /// 列挙は <c>Dictionary</c> 1 周ぶんの費用があるので毎フレームは走らせない。</summary>
        private const int LookupRetryFrames = 600;

        private static int _lookupMissCount;
        private static TyphoonCloudFxState _state = TyphoonCloudFxState.Off;
        private static int _lastRenderCalls;

        private static bool _errorLogged;

        public static TyphoonCloudFxState State { get { return _state; } }

        /// <summary>直近のフレームで出した <c>RenderEffect</c> の回数。</summary>
        public static int LastRenderCalls { get { return _lastRenderCalls; } }

        /// <summary>診断に出す 1 行（**英語**）。</summary>
        public static string EffectDetail
        {
            get
            {
                if (!AnyClone())
                {
                    return "NONE (no vanilla particle effect could be borrowed)";
                }
                return "cloned \"" + (SourceName ?? "?") + "\" [material \""
                       + (SourceMaterial ?? "?") + "\"] into "
                       + CloneCount() + " layer(s): "
                       + VortexPuffLayout.PuffCount + " puffs/frame, "
                       + (int)ParticlesPerSecond + " particles/s, cap " + TotalParticleCap();
            }
        }

        /// <summary>
        /// 渦の外周半径（m）。**退避経路のメッシュもこの 1 本を使う** ——
        /// 2 か所で決めると、粒とメッシュで大きさが食い違う。
        /// 読めていなければ 0（＝描かない。推測した半径で空を埋めない）。
        /// </summary>
        public static float VortexRadiusMetres(TyphoonSnapshot snapshot)
        {
            if (snapshot == null) return 0f;

            float storm = snapshot.StormRadius;
            if (!(storm > 0f)) return 0f;

            float radius = storm * VortexRadiusFactor;
            if (float.IsNaN(radius)) return 0f;
            if (radius > MaxVortexRadiusMetres) radius = MaxVortexRadiusMetres;
            if (radius < MinVortexRadiusMetres) radius = MinVortexRadiusMetres;
            return radius;
        }

        /// <summary>
        /// **借りられるかだけを見る、副作用の無い問い合わせ。**
        /// <c>Assumptions</c> がここを呼ぶ —— 検証の述語は
        /// <b>この機能が実際に門にしている式でなければならない</b>ので、
        /// あちらに候補の並びを書き写さず、<see cref="Lookup"/> そのものを共有する。
        /// クローンも作らないし、間引きのカウンタにも触らない。
        /// main スレッド専用（<c>Assumptions.Run</c> 自体が main スレッド専用）。
        /// </summary>
        public static bool CanBorrow(out string name)
        {
            try
            {
                string material;
                return Lookup(out name, out material) != null;
            }
            catch
            {
                name = null;
                return false;
            }
        }

        /// <summary>
        /// **main スレッド、毎フレーム。** 描けたら true。
        /// false のとき呼び出し側は旧メッシュ経路へ退避する。
        ///
        /// <paramref name="spinDegrees"/> は <see cref="TyphoonCloud"/> が持っている
        /// 回転角（ポーズ中は進まない）。ここで別に数えると、退避経路と本経路で
        /// 渦の向きが食い違う。
        /// </summary>
        public static bool Update(TyphoonSnapshot snapshot, float spinDegrees)
        {
            try
            {
                return Step(snapshot, spinDegrees);
            }
            catch (System.Exception e)
            {
                _state = TyphoonCloudFxState.Failed;
                _lastRenderCalls = 0;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon cloud puffs failed", e);
                }

                // 壊れたクローンを抱えたまま毎フレーム投げ続けない。
                DestroyClones();
                TyphoonVortexPuffFx.Destroy();
                return false;
            }
        }

        private static bool Step(TyphoonSnapshot snapshot, float spinDegrees)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active)
            {
                // ★ 台風が居ないのに雲だけ空に残らないよう、自前の粒は畳む。
                //   （借り物のクローンは湧かせるのをやめれば自然に消える。）
                TyphoonVortexPuffFx.Destroy();

                if (AnyClone()) _state = TyphoonCloudFxState.Idle;
                _lastRenderCalls = 0;
                return false;
            }

            float radius = VortexRadiusMetres(snapshot);
            if (!(radius > 0f))
            {
                _lastRenderCalls = 0;
                return false;
            }

            // ★★ **まず自前の白い雲で描く**（2026-08-22、所有者の指摘
            //    「まだ煙のようなものが見えるんですが」）。
            //
            //    借り物の粒子は素材の選択自体は成功していた（実機ログの
            //    <c>resolved: Large Pool Steam</c>）が、**湯気の絵は薄すぎて
            //    雲にならない** —— 不透明度 0.34-0.41 で背景が透ける
            //    （<see cref="CloudParticleAssets"/> のクラス doc に計測）。
            //    しかも撒いた粒はバニラのシミュレーションのもので漂うため、
            //    渦の形を持ち続けられない。
            //
            //    <see cref="TyphoonVortexPuffFx"/> は同じ <see cref="VortexPuffLayout"/> の
            //    表を使って、**芯が不透明な白い雲の粒を毎フレーム置き直す**。
            float altitudeMetres = CloudBaseMetres(snapshot.Centre.Y);

            // ★★ **雲の中の稲妻。** 雲と<b>同じ半径・高さ・厚み</b>を渡すこと ——
            //    ずれると雷が雲の外で光る（それが直そうとしている症状そのものである）。
            //    雲が描けなくても雷は描く（借り物の粒へ退避した絵でも空は光ってよい）。
            TyphoonBoltFx.Update(snapshot, VanillaParticles.CameraInfo(),
                                 radius, altitudeMetres, ThicknessMetres);

            if (TyphoonVortexPuffFx.Update(snapshot, radius, spinDegrees,
                                           altitudeMetres, ThicknessMetres))
            {
                _state = TyphoonCloudFxState.OwnPuffs;
                _lastRenderCalls = 0;

                // ★ 借り物を抱えたままにしない。両方出すと**二重の雲**になる。
                if (AnyClone()) DestroyClones();
                return true;
            }

            // ★ ここから下は退避である。アルファブレンドのシェーダが引けない環境で
            //   しか通らない（実機では <c>Custom/Particles/Alpha Blended</c> が
            //   読み込み済みマテリアルから引けている）。

            // ★ 参照そのものを毎フレーム見る。破棄済みなら fake-null で null と
            //   等価になり、ここで作り直される（2 つ目の都市の自己修復）。
            if (!AnyClone() && !Acquire()) return false;

            var camera = VanillaParticles.CameraInfo();
            if (camera == null)
            {
                // ★ null を渡すと ParticleEffect.RenderEffect の先頭で NRE になる
                //   （CheckRenderDistance / Intersect）。**描かずに黙って待つ。**
                _lastRenderCalls = 0;
                return true;   // エフェクトは持っている。メッシュへ退避させない。
            }

            float timeDelta = VanillaParticles.TimeDelta();
            if (!(timeDelta > 0f))
            {
                // ポーズ中・速度 0。粒は湧かないが渦は残る（既存の粒子が漂う）。
                _lastRenderCalls = 0;
                _state = TyphoonCloudFxState.Emitting;
                return true;
            }

            Emit(snapshot, radius, spinDegrees, timeDelta, camera);
            return true;
        }

        private static void Emit(TyphoonSnapshot snapshot, float radius, float spinDegrees,
                                 float timeDelta, RenderManager.CameraInfo camera)
        {
            Vec3 centre = snapshot.Centre;
            float altitude = CloudBaseMetres(centre.Y);

            // 粒径は渦の大きさに合わせる。**共有状態ではなくクローン側**なので
            // 毎フレーム書いてよい（MainModule は struct、確保は 0 バイト）。
            ApplySizes(radius);

            // ★ default(InstanceID) の RawData は 0 で、Randomizer の種が 0 に固定される
            //   （§B-6）。ParticleEffect 直呼びなら probability = 100 固定なので実害は
            //   無いが、非 0 を入れておくのが作法である。
            InstanceID id = InstanceID.Empty;
            id.Disaster = snapshot.TyphoonId != 0 ? snapshot.TyphoonId : (ushort)1;

            float spin = spinDegrees * 0.0174532925f;
            int calls = 0;

            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                VortexPuff puff = VortexPuffLayout.PuffAt(i);

                ParticleEffect effect = CloneFor(puff.Layer);
                if (effect == null) continue;

                float a = puff.AngleRadians + spin;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                float r = puff.RadiusFraction * radius;

                var position = new Vector3(centre.X + cos * r,
                                           altitude + puff.HeightFraction * ThicknessMetres,
                                           centre.Z + sin * r);

                // 二次循環。接線（渦の回る向き）＋ 半径方向（下層は内へ、上層は外へ）
                // ＋ 上昇。**渦の回り方と同じ向き**（spin と符号を合わせる）。
                float swirl = SwirlMetresPerSecond * puff.SwirlFraction;
                float radial = RadialMetresPerSecond * puff.RadialFraction;
                var velocity = new Vector3(-sin * swirl + cos * radial,
                                           RiseMetresPerSecond * puff.RiseFraction,
                                           cos * swirl + sin * radial);

                float discRadius = puff.DiscFraction * radius;
                if (!(discRadius > 0f)) continue;

                float band = puff.BandFraction * ThicknessMetres;

                // ★ magnitude は円盤ごとに解き直す。§B-4 の式は面積で効くので、
                //   円盤を広げたぶんだけ密度を下げないと外側だけ濃くなる。
                float magnitude = VortexPuffLayout.MagnitudeFor(discRadius, RateOverTime,
                                                                ParticlesPerSecond,
                                                                VortexPuffLayout.PuffCount);
                if (!(magnitude > 0f)) continue;

                // SpawnArea(pos, dir, radius, halfHeight) は必ず「点/円盤」経路に落ちる
                // （§B-2）。halfHeight は **上へだけ** [0, band) で散らす（§B-4）ので、
                // HeightFraction を段の下端にしてあることと噛み合う。
                var area = new EffectInfo.SpawnArea(position, Vector3.up, discRadius, band);

                // timeOffset = -1f ＝ **継続モード**（§B-3）。
                effect.RenderEffect(id, area, velocity, 0f,
                                    magnitude * puff.DensityFraction,
                                    -1f, timeDelta, camera);
                calls++;
            }

            _lastRenderCalls = calls;
            _state = calls > 0 ? TyphoonCloudFxState.Emitting : TyphoonCloudFxState.NoEffect;
        }

        /// <summary>
        /// **レベルアンロードと、設定で雲を切ったときに呼ぶ。** main スレッド専用。冪等。
        /// </summary>
        public static void Destroy()
        {
            DestroyClones();
            // ★ 自前の白い雲の GameObject と、その素材（Material / Texture2D）も
            //   自分で消す。どれも Component では無いので、飛ばすと
            //   都市を出入りするたびに 1 組ずつ残る。
            TyphoonVortexPuffFx.Destroy();
            // ★ 稲妻のメッシュ・マテリアル・テクスチャも自前なので、自分で消す。
            TyphoonBoltFx.Destroy();
            CloudParticleAssets.Destroy();
            _lookupMissCount = 0;
            _lastRenderCalls = 0;
            _state = TyphoonCloudFxState.Off;

            // ★★ 雷の高さも忘れる。<c>Mesh</c> は Unity のオブジェクトなので、
            //    都市をまたぐと fake-null になる——持ちっぱなしにしない
            //    （このプロジェクトの「静的キャッシュの罠」）。
            _boltTopMetres = 0f;
            // ★ _unavailableLogged / _errorLogged は戻さない（クラス doc）。
        }
    }
}
