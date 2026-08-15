namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤が地形を書けるかどうか。**Task 2 の主目的そのもの**である。
    ///
    /// ①はバニラのハザードマップを、②はバニラの決定論的な被害モデルを見せた。
    /// ④は嵐プレハブの実数値を実機で測った。**⑤が実機でしか測れないのはこれ** ——
    /// <c>RawHeights</c> の実寸と、地形を書き換える 4 つの到達経路である。
    /// ここが揃わなければ⑤は 1 メートルも山を上げない（<see cref="Usable"/>）。
    ///
    /// struct にしているのは④の <see cref="TyphoonPrefabFacts"/> と同じ理由
    /// （bool と int しか持たないので、キャッシュしても Unity の fake-null
    /// 自己修復問題を持ち込まない）。既定値は全て false ＝「まだ／もう読めていない」。
    ///
    /// **フラグは 1 本にまとめない。** 山（<see cref="HeightsResolved"/> /
    /// <see cref="UpdateAreaResolved"/>）・火口と焦げ（<see cref="CraterResolved"/> /
    /// <see cref="BurnGroundResolved"/>）・溶岩の流路（<see cref="SlopeSampleResolved"/>）は
    /// 独立に壊れうる。まとめると「溶岩が流れないだけ」の環境で山まで止まる。
    /// </summary>
    public struct VolcanoTerrainFacts
    {
        /// <summary>マップ全体の raw セル数（1081²）。§A-1 の <c>TerrainManager.Awake</c> 実測。</summary>
        public const int ExpectedRawArrayLength = 1168561;

        /// <summary><c>TerrainManager.RawHeights</c>（<c>ushort[]</c>）を取れたか。</summary>
        public readonly bool HeightsResolved;

        /// <summary>
        /// <c>RawHeights.Length</c>。**1081² = 1168561 でなければ以後のセル計算が全部ずれる**
        /// （<c>index = z*1081 + x</c>）。読めなかったときは 0。
        /// </summary>
        public readonly int RawArrayLength;

        /// <summary>
        /// <c>TerrainModify.UpdateArea(int,int,int,int,bool,bool,bool)</c> を解決できたか。
        /// **書いた高さをゲームへ反映する唯一の経路**（§A-1）。
        /// </summary>
        public readonly bool UpdateAreaResolved;

        /// <summary><c>DisasterHelpers.MakeCrater(Vector2,float,float,bool)</c>（§C-8）。</summary>
        public readonly bool CraterResolved;

        /// <summary><c>DisasterHelpers.BurnGround(Vector2,float,float)</c>（§B-7b。**DLC 不要**）。</summary>
        public readonly bool BurnGroundResolved;

        /// <summary>
        /// <c>TerrainManager.SampleDetailHeight(Vector3, out float, out float)</c>（§B-6）。
        /// **溶岩が下り方向を見つける唯一の経路。** 山と準備と噴火はこれに依存しない。
        /// </summary>
        public readonly bool SlopeSampleResolved;

        /// <summary>
        /// Natural Disasters DLC を持っているか。**⑤は DLC を要らない**（設計書 §1.4）。
        /// 分岐するのは樹木の着火だけ（<c>TreeManager.BurnTree</c> は DLC ゲート、§B-7c）なので、
        /// **false でも FAIL 扱いにしない。**
        /// </summary>
        public readonly bool NaturalDisastersOwned;

        public VolcanoTerrainFacts(bool heightsResolved, int rawArrayLength,
                                   bool updateAreaResolved, bool craterResolved,
                                   bool burnGroundResolved, bool slopeSampleResolved,
                                   bool naturalDisastersOwned)
        {
            HeightsResolved = heightsResolved;
            RawArrayLength = rawArrayLength;
            UpdateAreaResolved = updateAreaResolved;
            CraterResolved = craterResolved;
            BurnGroundResolved = burnGroundResolved;
            SlopeSampleResolved = slopeSampleResolved;
            NaturalDisastersOwned = naturalDisastersOwned;
        }

        /// <summary>
        /// ⑤が山を 1 つでも作ってよいか。**これが⑤全体の門である。**
        ///
        /// 配列そのものと、書いた値をゲームへ反映する経路の 2 つが要る。
        /// 長さが 1081² でないときに「とりあえず書く」をやると、
        /// <c>z*1081 + x</c> の添字が別のセルを指し、**マップの無関係な場所が隆起する**。
        /// 推測で代替しない（設計書 §6）。
        ///
        /// **前提検証（<c>Assumptions.Volcano</c>）はこの式そのものを述語にすること。**
        /// 「フィールドが解決した」を述語にすると、値が使えない場合に PASS が出る
        /// （④のレビューと②の監査が同じ欠陥を見つけている）。
        /// </summary>
        public bool Usable
        {
            get
            {
                return HeightsResolved
                       && RawArrayLength == ExpectedRawArrayLength
                       && UpdateAreaResolved;
            }
        }
    }

    /// <summary>
    /// sim スレッドで作り main スレッドで読む不変スナップショット。
    /// ①の <c>WeatherSnapshot</c>・②の <c>EarthquakeSnapshot</c>・
    /// ④の <see cref="TyphoonSnapshot"/> と同じ規律で、**一度作ったら書き換えない**。
    ///
    /// **T3 以降がフィールドを足していく。追加は必ず ctor の末尾に付けること**
    /// （既存の呼び出し側を全部直させないため）。
    ///
    /// ⑤の表示規約: ここに載る値のうちバニラの実測なのは
    /// <see cref="Terrain"/>（配列の実寸と到達経路）と <see cref="GameMode"/> だけで、
    /// それ以外は全て本 MOD が決めた量である（設計書 §7.4）。
    /// </summary>
    public class VolcanoSnapshot
    {
        /// <summary>読み取りに成功したか。false なら表示側は「読み取れません」と出す。</summary>
        public readonly bool Valid;

        /// <summary>地形 API の実測。**⑤が動けるかどうかはここだけで決まる。**</summary>
        public readonly VolcanoTerrainFacts Terrain;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>。</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// ゲームモードか（マップエディタなら false）。
        /// <c>m_blockHeights</c> の追随速度がゲームで上へ 2 m、エディタで 8 m に変わる（§A-2）。
        /// **読めなければゲームモードとみなす**（遅いほうを名乗る）。
        /// </summary>
        public readonly bool GameMode;

        public VolcanoSnapshot(bool valid, VolcanoTerrainFacts terrain,
                               uint currentFrame, bool gameMode)
        {
            Valid = valid;
            Terrain = terrain;
            CurrentFrame = currentFrame;
            GameMode = gameMode;
        }

        /// <summary>読み取りに失敗したときの 1 個。**0 を並べた「それらしい」値を作らない。**</summary>
        public static VolcanoSnapshot Invalid()
        {
            return new VolcanoSnapshot(false, new VolcanoTerrainFacts(), 0u, true);
        }
    }
}
