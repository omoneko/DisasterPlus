using ColossalFramework;
using DisasterPlus.Core.Forecast;

namespace DisasterPlus.Game
{
    /// <summary>
    /// WeatherManager と DisasterManager から現在の気象と災害確率を読む。
    ///
    /// **sim スレッドから呼ぶこと。** これらのマネージャはシミュレーションが所有する。
    /// main スレッドから直接触ると、スタックトレースの出ない IndexOutOfRangeException
    /// ポップアップが後になってバニラ側から出る（この MOD の try/catch では捕まえられない）。
    ///
    /// current と target の両方を読むのが本機能の要。バニラは今の値しか見せず、
    /// 「今どちらへ向かっているか」を出さない。
    ///
    /// 併せて「測位済みの進行中の嵐」の数もここで数える。理由は
    /// <see cref="WeatherSnapshot.LocatedLightningStorms"/> の doc を参照
    /// （バニラのハザードマップは静的なリスク面ではなく、レーダーで測位された
    /// 進行中の嵐の予測被害範囲であり、該当が無ければ全セル 0 になる）。
    /// </summary>
    public static class WeatherReader
    {
        /// <summary>
        /// Created かつ Deleted でない、を判定するためのマスク。
        ///
        /// IL 実測: DisasterManager.UpdateTexture 自身が m_disasters を走査するときに
        /// <c>ldfld m_flags; ldc.i4.3; and; ldc.i4.1; bne.un</c>
        /// （＝ <c>(m_flags &amp; 3) == 1</c>）で同じ判定をしている。
        /// Flags は Created=1, Deleted=2（リフレクションで実測）。
        /// バニラの走査条件をそのまま写すことで、数えている母集団が
        /// UpdateTexture が実際に描画対象にする母集団と必ず一致する。
        /// </summary>
        private const int CreatedNotDeletedMask = (int)DisasterData.Flags.Created
                                                | (int)DisasterData.Flags.Deleted;

        /// <summary>
        /// UpdateHazardMap のゲートその 1。4096 = DisasterData.Flags.Located。
        /// レーダー等で「測位済み」になっていなければハザードマップに一切描かれない。
        /// </summary>
        private const int LocatedMask = (int)DisasterData.Flags.Located;

        /// <summary>
        /// UpdateHazardMap のゲートその 2。12 = Emerging(4) | Active(8)。
        /// 発生中・進行中でなければ同じく描かれない。
        /// </summary>
        private const int InProgressMask = (int)DisasterData.Flags.Emerging
                                         | (int)DisasterData.Flags.Active;

        /// <summary>
        /// Read() 内の想定外例外を Log.Error で鳴らしたか。
        ///
        /// Read() は sim tick ごと（通常速度でおよそ 50 回/秒）に呼ばれる。
        /// 恒常的に投げる状態（ゲーム更新でフィールドが消えた等）になると、
        /// 無条件の Log.Error は毎秒 50 行を output_log.txt に書き続けて
        /// ログを使い物にならなくする。1 回目だけ確実に目立たせ、以後は
        /// Log.Diag のキー単位スロットル（512 sim フレームに 1 回）へ落とす。
        /// 兄弟クラスの HazardMapReader._sampleErrorLogged と同じ手口
        /// （Task 4 のレビューでそちらに入れた修正を、同じ欠陥のあるこちらへ展開）。
        ///
        /// レベルアンロードでリセットしないのも同じ理由。「投げる」はこの DLL が
        /// 参照しているゲームのビルドに対する事実であり、都市ごとの状態ではない。
        /// </summary>
        private static bool _readErrorLogged;

        public static WeatherSnapshot Read()
        {
            try
            {
                if (!Singleton<WeatherManager>.exists) return WeatherSnapshot.Invalid();
                var w = Singleton<WeatherManager>.instance;

                float band = TrendMath.DefaultDeadband;

                // 気温はスケールが大きいので不感帯を広げる（0.02 度では常に変化扱いになる）。
                // 数値の根拠と、なぜ Core に置いているかは TrendMath.TemperatureDeadband 参照。
                var temperature = new ForecastReading(
                    w.m_currentTemperature, w.m_targetTemperature, TrendMath.TemperatureDeadband);
                var rain = new ForecastReading(w.m_currentRain, w.m_targetRain, band);
                var cloud = new ForecastReading(w.m_currentCloud, w.m_targetCloud, band);
                var fog = new ForecastReading(w.m_currentFog, w.m_targetFog, band);

                // DisasterManager が居なくても気象スナップショットは有効に返す。
                // 災害確率が読めないだけで「読み取れません」に落とすのは過剰。
                // ただし probability=0f のまま Valid=true にすると、呼び出し側からは
                // 「本当に 0% だった」のか「読めなかった」のか区別が付かない捏造ゼロになる
                // （レビュー指摘）。disasterInfoAvailable で明示的に区別する。
                float probability = 0f;
                int cooldown = 0;
                int lightningStorms = 0;
                int tornadoes = 0;
                bool disasterInfoAvailable = false;
                if (Singleton<DisasterManager>.exists)
                {
                    var d = Singleton<DisasterManager>.instance;
                    probability = d.m_randomDisastersProbability;
                    cooldown = d.m_randomDisasterCooldown;
                    CountLocatedStorms(d, out lightningStorms, out tornadoes);
                    disasterInfoAvailable = true;
                }

                return new WeatherSnapshot(
                    temperature, rain, cloud, fog, w.m_windDirection,
                    probability, cooldown, lightningStorms, tornadoes,
                    disasterInfoAvailable, true);
            }
            catch (System.Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("weather read failed", e);
                }
                else
                {
                    Log.Diag("WeatherRead", "weather read failed: " + e.GetType().Name);
                }
                return WeatherSnapshot.Invalid();
            }
        }

        /// <summary>
        /// ハザードマップに実際に描かれる条件を満たした雷雨・竜巻を数える。
        /// **sim スレッド専用**（m_disasters はシミュレーションが所有するバッファ）。
        ///
        /// 走査の形は DisasterManager.UpdateTexture の IL をそのまま写している:
        /// <c>m_disasters.m_buffer</c> を <c>m_disasters.m_size</c> まで回し、
        /// <c>(m_flags &amp; 3) == 1</c> で生きている要素だけを見る。
        /// そこへ UpdateHazardMap 自身の 2 つのゲート（Located / Emerging|Active）を
        /// 足したものが「今ハザードマップに何か描く災害」の正確な定義になる。
        ///
        /// DisasterData.Info は <c>PrefabCollection&lt;DisasterInfo&gt;.GetPrefab(m_infoIndex)</c>
        /// を呼ぶだけで境界検査をしない（IL 実測: get_Info は 4 命令）。壊れた
        /// セーブや MOD 由来の不正な m_infoIndex で投げうるので、要素ごとに
        /// try/catch で囲んで 1 件の失敗が集計全体を落とさないようにする。
        /// </summary>
        private static void CountLocatedStorms(DisasterManager d, out int lightning, out int tornado)
        {
            lightning = 0;
            tornado = 0;

            var list = d.m_disasters;
            if (list == null) return;

            var buffer = list.m_buffer;
            if (buffer == null) return;

            // m_size を信用しきらない。バニラは m_size までしか回さないが、
            // こちらは配列長でも頭を押さえておく（ここで IndexOutOfRange を出すと
            // sim スレッドなのでスタックトレース無しのポップアップになる）。
            int size = list.m_size;
            if (size > buffer.Length) size = buffer.Length;

            for (int i = 0; i < size; i++)
            {
                int flags = (int)buffer[i].m_flags;

                if ((flags & CreatedNotDeletedMask) != (int)DisasterData.Flags.Created) continue;
                if ((flags & LocatedMask) == 0) continue;
                if ((flags & InProgressMask) == 0) continue;

                DisasterAI ai;
                try
                {
                    var info = buffer[i].Info;
                    // UnityEngine.Object の == オーバーロードで破棄済み(fake-null)も弾く。
                    // UpdateTexture も同じ位置で Object::op_Inequality を使っている。
                    if (info == null) continue;
                    ai = info.m_disasterAI;
                }
                catch
                {
                    // 1 件の不正な m_infoIndex で集計全体を捨てない。
                    continue;
                }

                if (ai == null) continue;

                // ThunderStormAI / TornadoAI はいずれも
                // WeatherDisasterAI -> DisasterAI 派生（リフレクションで実測）。
                // ai の静的型は DisasterAI なのでこの is はダウンキャスト検査であり、
                // 「常に false」で CS0184（このプロジェクトではエラー扱い）にはならない。
                if (ai is ThunderStormAI) lightning++;
                else if (ai is TornadoAI) tornado++;
            }
        }
    }
}
