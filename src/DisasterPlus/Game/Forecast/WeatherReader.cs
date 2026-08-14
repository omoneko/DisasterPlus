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
    /// current と target の両方を読むのが本機能の要。バニラは hazard（静的な危険度）
    /// しか見せず、「今どちらへ向かっているか」を出さない。
    /// </summary>
    public static class WeatherReader
    {
        public static WeatherSnapshot Read()
        {
            try
            {
                if (!Singleton<WeatherManager>.exists) return WeatherSnapshot.Invalid();
                var w = Singleton<WeatherManager>.instance;

                float band = TrendMath.DefaultDeadband;

                // 気温はスケールが大きいので不感帯を広げる（0.02 度では常に変化扱いになる）。
                var temperature = new ForecastReading(
                    w.m_currentTemperature, w.m_targetTemperature, 0.5f);
                var rain = new ForecastReading(w.m_currentRain, w.m_targetRain, band);
                var cloud = new ForecastReading(w.m_currentCloud, w.m_targetCloud, band);
                var fog = new ForecastReading(w.m_currentFog, w.m_targetFog, band);

                // DisasterManager が居なくても気象スナップショットは有効に返す。
                // 災害確率が読めないだけで「読み取れません」に落とすのは過剰。
                float probability = 0f;
                int cooldown = 0;
                if (Singleton<DisasterManager>.exists)
                {
                    var d = Singleton<DisasterManager>.instance;
                    probability = d.m_randomDisastersProbability;
                    cooldown = d.m_randomDisasterCooldown;
                }

                return new WeatherSnapshot(
                    temperature, rain, cloud, fog,
                    w.m_windDirection, w.m_groundWetness, w.m_lastLightningIntensity,
                    probability, cooldown, true);
            }
            catch (System.Exception e)
            {
                Log.Error("weather read failed", e);
                return WeatherSnapshot.Invalid();
            }
        }
    }
}
