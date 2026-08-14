using DisasterPlus.Core.Forecast;

namespace DisasterPlus.Game
{
    /// <summary>
    /// sim スレッドで作り main スレッドで読む不変スナップショット。
    /// 一度作ったら書き換えない。
    /// </summary>
    public class WeatherSnapshot
    {
        public readonly ForecastReading Temperature;
        public readonly ForecastReading Rain;
        public readonly ForecastReading Cloud;
        public readonly ForecastReading Fog;
        public readonly float WindDegrees;
        public readonly float DisasterProbability;
        public readonly int DisasterCooldown;

        /// <summary>
        /// 現在「測位済み（Located）かつ進行中（Emerging|Active）」の雷雨の数。
        ///
        /// なぜこれを運ぶか（全体レビューの最重要指摘、IL 実測で確定）:
        /// バニラのハザードマップは**静的なリスク面ではない**。
        /// <c>ThunderStormAI.UpdateHazardMap</c> / <c>TornadoAI.UpdateHazardMap</c> は
        /// どちらも先頭 2 命令ブロックが
        ///   <c>(m_flags &amp; 4096) == 0 -&gt; return</c>（4096 = DisasterData.Flags.Located）
        ///   <c>(m_flags &amp; 12)   == 0 -&gt; return</c>（12 = Emerging|Active）
        /// というゲートで、どちらも地形・建物を一切参照しない（IL 全走査で確認）。
        /// 通過した場合だけ m_targetPosition の周りに半径と強度で決まる円盤を塗る。
        /// さらに <c>DisasterManager.UpdateTexture</c> は毎回 256x256 セルを全てゼロで
        /// 埋めてから、このゲートを通った災害だけを書き込む。
        ///
        /// つまりグリッドの中身は「レーダーで測位済みの進行中の嵐の予測被害範囲」であり、
        /// 該当する嵐が 1 つも無ければ**全セルが 0** になる。
        ///
        /// この数を運ばないと、DLC はあるが気象レーダーを建てていないプレイヤーが
        /// 「マップに表示」を押して都市のどこにカーソルを置いても
        /// 「落雷: 0」と読むことになる。<c>SampleAt</c> はサブモードが一致していて
        /// グリッドも実在するので ok=true を返す——ただ中身が全部ゼロなだけである。
        /// プレイヤーは「どこにも落雷リスクが無い」と結論するが、真実は
        /// 「今どの嵐も検知されていない」。本機能がまさに防ぐために書かれた
        /// 「確信を持って誤った数値」そのものになる。
        ///
        /// <see cref="DisasterInfoAvailable"/> が false のときこの値は無意味
        /// （読めなかったので 0 のまま）。呼び出し側は必ずそちらを先に見ること。
        /// </summary>
        public readonly int LocatedLightningStorms;

        /// <summary>
        /// 現在「測位済みかつ進行中」の竜巻の数。
        /// 意味と注意点は <see cref="LocatedLightningStorms"/> と同じ。
        ///
        /// 補足（IL 実測）: <c>TornadoAI.SimulationStep</c> は自分で
        /// <c>DisasterManager.DetectDisaster(disasterID, located: false)</c> を呼ぶ
        /// （IL_0038 が <c>ldc.i4.0</c>）。<c>ThunderStormAI</c> は
        /// <c>DetectDisaster</c> を一切呼ばない。<c>located: true</c> を渡す呼び出し元は
        /// <c>WeatherRadarAI</c> / <c>SpaceRadarAI</c> / <c>TsunamiBuoyAI</c> の
        /// <c>ProduceGoods</c>、<c>FirewatchTowerAI.NearObjectInFire</c>、
        /// <c>SinkholeAI.SimulationStep</c>、および <c>DisasterWrapper.DetectDisaster</c>
        /// （MOD/シナリオ用スクリプト API）だけで、雷雨・竜巻に Located を立てられるのは
        /// このうち**気象レーダー（WeatherRadarAI）だけ**である。
        /// </summary>
        public readonly int LocatedTornadoes;

        /// <summary>
        /// DisasterProbability / DisasterCooldown / LocatedLightningStorms /
        /// LocatedTornadoes が実際に DisasterManager から読めたか。
        ///
        /// レビュー指摘（コーディネーターからのフィードバック）: DisasterManager が
        /// 居ない場合、以前は DisasterProbability を黙って 0f のまま返していた。
        /// これは「確率 0%」という実際の読み取り結果と外見上区別が付かない
        /// **捏造されたゼロ**であり、ハザード数値を表示中でない種別のラベルで出す
        /// のと同種の「確信を持って誤った数値」になる。このフラグで
        /// 「読めなかった（不明）」と「読んだ結果 0 だった」を呼び出し側が
        /// 区別できるようにする。false のときパネルは確率の行そのものを出さない
        /// （0.0% と表示しない）。
        ///
        /// 測位済み嵐の数にも同じ理屈が効く。false のときに「嵐は検知されていません」
        /// と言い切ると、それ自体が読めていない事実を隠した断定になるので、
        /// パネルは汎用の「不明」に落とすこと。
        /// </summary>
        public readonly bool DisasterInfoAvailable;

        /// <summary>読み取りに成功したか。false ならパネルは「読み取れません」と出す。</summary>
        public readonly bool Valid;

        public WeatherSnapshot(ForecastReading temperature, ForecastReading rain,
                               ForecastReading cloud, ForecastReading fog,
                               float windDegrees,
                               float disasterProbability, int disasterCooldown,
                               int locatedLightningStorms, int locatedTornadoes,
                               bool disasterInfoAvailable, bool valid)
        {
            Temperature = temperature;
            Rain = rain;
            Cloud = cloud;
            Fog = fog;
            WindDegrees = windDegrees;
            DisasterProbability = disasterProbability;
            DisasterCooldown = disasterCooldown;
            LocatedLightningStorms = locatedLightningStorms;
            LocatedTornadoes = locatedTornadoes;
            DisasterInfoAvailable = disasterInfoAvailable;
            Valid = valid;
        }

        public static WeatherSnapshot Invalid()
        {
            var zero = new ForecastReading(0f, 0f, TrendMath.DefaultDeadband);
            return new WeatherSnapshot(zero, zero, zero, zero, 0f, 0f, 0, 0, 0, false, false);
        }
    }
}
