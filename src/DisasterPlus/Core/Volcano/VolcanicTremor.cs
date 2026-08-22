using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **火山性地震。** 火口の下でマグマが動くあいだ地面が揺れ続ける、あの揺れの合成である。
    /// エンジン非依存の純関数だけで、Unity の型もゲームの型も
    /// <c>System.Random</c> も出てこない。
    ///
    /// ── なぜ②の地震をそのまま起こさないのか（2026-08-22、所有者の依頼）──────────
    ///
    /// > 噴火と同時に火山性の地震の発生もお願いします。
    ///
    /// **バニラの <c>EarthquakeAI</c> を起こすのは間違いである。** あれは地図に
    /// 断層の亀裂を刻む —— ⑤が同じ地形セルへ山を書いているところへ、別の書き手が
    /// 割り込むことになる（設計書 §1.2 の平らな溝と同じ壊れ方をする）。
    /// 火山の地震はそもそも断層地震ではない。
    ///
    /// **②の合成記象（<c>SeismogramModel</c>）もそのままは使わない。** あれは
    /// **1 回の断層破壊**のモデルで、P 波が着き、S 波が着き、コーダが減衰して終わる。
    /// 火山性地震はそうではない:
    ///
    ///   1. **群発** —— 小さい地震が数十〜数百回。1 回 1 回は短く、大きいものは稀
    ///   2. **火山性微動（harmonic tremor）** —— マグマが動いているあいだ
    ///      <b>切れ目なく</b>続く、周期のそろった低い揺れ
    ///   3. **噴火の前から始まり、噴火中に最大になり、あとを引いて収まる**
    ///
    /// この 3 つだけを作る。**②の設定（<c>eqSeismogram</c> / <c>eqShakeBoost</c>）は
    /// 1 つも見ない** —— あちらが既定 OFF なのはバニラの地震のカメラ揺れを
    /// 差し替えるからで、⑤の火山は<b>プレイヤーが自分で起こした、⑤自身の現象</b>である。
    /// ②を全部切っていても火山は揺れる（<c>Game/Volcano/VolcanoTremorShake</c>）。
    ///
    /// ── 群発の作り方（★ 状態を持たない）───────────────────────────
    ///
    /// 時間を <see cref="SlotSeconds"/> の枠に切り、**枠ごとに必ず 1 回**地震を置く。
    /// 「起きるか起きないか」を活動度で決めない ——
    /// 決めると<b>活動度が変わった瞬間に、もう鳴っている過去の地震が消える</b>
    /// （<see cref="DisplacementAt"/> は t の閉じた式で、過去の枠も毎回引き直すため）。
    /// 代わりに<b>大きさ</b>を活動度に比例させる。静かなときは「ほとんど感じない
    /// 地震が絶え間なく起きている」で、それは火山性群発の姿そのものである。
    ///
    /// 大きさの分布は <c>u⁴</c>（<see cref="MagnitudeCurve"/>）—— **小さいものが圧倒的に
    /// 多く、大きいものが稀**。実際の地震の規模別頻度（Gutenberg–Richter）と同じ向きで、
    /// **ここで作っているのは分布の形だけである**（マグニチュードではない）。
    /// 実測（テストが固定）: 6 割が「ほとんど感じない」側、1 割強だけが大きい側。
    ///
    /// ── 折り返さないこと ───────────────────────────────────
    ///
    /// カメラの揺れは**描画フレームごと**に評価する（60 fps ⇒ ナイキスト 30 Hz）。
    /// いちばん速い成分は地震の搬送波 <see cref="EventFastHz"/> = 3.2 Hz で、
    /// 1 周期あたり 18 点とれる。**これ以上速い成分を足さないこと。**
    ///
    /// ── 同じ火山は同じ揺れ ───────────────────────────────
    ///
    /// <see cref="DeterministicRandom"/> だけを使い、**フレーム番号を種に混ぜない**。
    /// <see cref="DisplacementAt"/> は t の閉じた式なので、飛んだフレームがあっても
    /// そのフレームで評価したのと同じ値になる。
    /// </summary>
    public static class VolcanicTremor
    {
        /// <summary>群発の枠（秒）。**1 枠にちょうど 1 回**地震が入る。</summary>
        public const float SlotSeconds = 1.6f;

        /// <summary>どこまで前の枠の地震が今の揺れに効くか。</summary>
        public const int TailSlots = 3;

        /// <summary>地震の立ち上がり（秒）。**0 にしない**（不連続は目に出る）。</summary>
        public const float EventRiseSeconds = 0.10f;

        /// <summary>地震の減衰の時定数（秒）。</summary>
        public const float EventDecaySeconds = 0.55f;

        /// <summary>地震の搬送波（Hz）。**ここがいちばん速い成分である。**</summary>
        public const float EventFastHz = 3.2f;

        /// <summary>地震の低いほうの搬送波（Hz）。</summary>
        public const float EventSlowHz = 1.1f;

        /// <summary>火山性微動の搬送波（Hz）。切れ目なく続く低い揺れ。</summary>
        public const float TremorHz = 1.8f;

        /// <summary>微動の振幅がうねる周期（Hz）。**マグマの流れのゆらぎ。**</summary>
        public const float TremorSwellHz = 0.11f;

        /// <summary>同上、もう 1 本（通約でない値にして反復に聞こえないようにする）。</summary>
        public const float TremorSwell2Hz = 0.037f;

        /// <summary>
        /// 微動の振幅（活動度 1 のとき）。**群発の山より小さくする** ——
        /// 大きいと記象が正弦波 1 本に埋もれて、地震が起きているように見えない
        /// （<c>docs/images/volcano/tremor-waveform.png</c> で 0.34 → 0.26 に下げた）。
        /// </summary>
        public const float TremorAmplitude = 0.26f;

        /// <summary>微動の振幅の下限比（うねっても完全には途切れない）。</summary>
        public const float TremorFloor = 0.45f;

        /// <summary>いちばん小さい地震の大きさ（活動度 1 のとき）。</summary>
        public const float MinEventMagnitude = 0.06f;

        /// <summary>いちばん大きい地震の大きさ（活動度 1 のとき）。</summary>
        public const float MaxEventMagnitude = 1.0f;

        /// <summary>隆起中（マグマ上昇）の活動度の下限。**噴火の前から揺れている。**</summary>
        public const float BuildUpFloor = 0.18f;

        /// <summary>隆起が終わった時点の活動度。</summary>
        public const float BuildUpCeiling = 0.62f;

        /// <summary>噴火中の活動度の下限（噴出の強さ 0 のとき）。</summary>
        public const float EruptionFloor = 0.55f;

        /// <summary>噴火のあと、冷えきるまでに残る活動度（余韻）。</summary>
        public const float AfterglowUnit = 0.42f;

        /// <summary>塩（枠の位相をずらす）。</summary>
        private const uint OffsetSalt = 0x0F5E7u;

        /// <summary>塩（枠の大きさ）。</summary>
        private const uint MagnitudeSalt = 0x4D41475u;

        /// <summary>塩（微動の位相）。</summary>
        private const uint TremorSalt = 0x54524Du;

        /// <summary>
        /// 今の活動度 <c>[0,1]</c>。**⑤が決めた量**であって、実在の観測量ではない。
        ///
        /// <paramref name="upliftProgressUnit"/> は隆起の進捗（マグマの上昇に対応）、
        /// <paramref name="eruptionUnit"/> は噴出の強さ、
        /// <paramref name="coolUnit"/> は溶岩の冷え具合（1 で冷えきった）。
        ///
        /// <paramref name="erupting"/> が false で <paramref name="afterEruption"/> も
        /// false なら「まだ噴いていない」＝ 隆起の側の式を使う。
        /// </summary>
        public static float ActivityUnit(float upliftProgressUnit, bool erupting,
                                         float eruptionUnit, bool afterEruption, float coolUnit)
        {
            if (erupting)
            {
                return Clamp01(EruptionFloor
                               + (1f - EruptionFloor) * Clamp01(eruptionUnit));
            }

            if (afterEruption)
            {
                // 噴火が終わってから冷えきるまで、余韻が引いていく。
                return Clamp01(AfterglowUnit * (1f - Clamp01(coolUnit)));
            }

            // 隆起（マグマ上昇）。**噴火の前から揺れている**のが火山性群発である。
            return Clamp01(BuildUpFloor
                           + (BuildUpCeiling - BuildUpFloor) * Clamp01(upliftProgressUnit));
        }

        /// <summary>時刻 <paramref name="clockSeconds"/> が入る枠の番号。</summary>
        public static int SlotAt(float clockSeconds)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0;
            return (int)(clockSeconds / SlotSeconds);
        }

        /// <summary>
        /// 枠 <paramref name="slot"/> の地震の大きさ <c>[0,1]</c>。
        /// **活動度に比例する**（枠は必ず 1 回起きる。クラス doc）。
        /// </summary>
        public static float MagnitudeUnit(uint seed, int slot, float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (slot < 0) return 0f;

            float u = DeterministicRandom.Unit(seed, unchecked((uint)slot ^ MagnitudeSalt));
            float shaped = MagnitudeCurve(u);
            return a * (MinEventMagnitude
                        + (MaxEventMagnitude - MinEventMagnitude) * shaped);
        }

        /// <summary>
        /// 規模別頻度の形（<c>u⁴</c>）。**小さいものが圧倒的に多く、大きいものが稀。**
        /// マグニチュードそのものではない（クラス doc）。
        /// </summary>
        public static float MagnitudeCurve(float u)
        {
            float x = Clamp01(u);
            float x2 = x * x;
            return x2 * x2;
        }

        /// <summary>枠の中での発生時刻（秒、<c>[0, SlotSeconds)</c>）。</summary>
        public static float OffsetInSlot(uint seed, int slot)
        {
            if (slot < 0) return 0f;
            return SlotSeconds * DeterministicRandom.Unit(seed,
                                                          unchecked((uint)slot ^ OffsetSalt));
        }

        /// <summary>
        /// 火山性微動（連続）の変位 <c>[-1,1]</c>。**切れ目が無い**のがこれの正体で、
        /// 群発の 1 回 1 回とは別に鳴り続ける。
        /// </summary>
        public static float TremorAt(uint seed, float clockSeconds, float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;

            float phase = 6.2831853f * DeterministicRandom.Unit(seed, TremorSalt);

            float swell = 0.5f * (1f + (float)Math.Sin(6.2831853 * TremorSwellHz * clockSeconds))
                        * 0.6f
                        + 0.5f * (1f + (float)Math.Sin(6.2831853 * TremorSwell2Hz * clockSeconds
                                                        + 1.7)) * 0.4f;
            float envelope = TremorFloor + (1f - TremorFloor) * swell;

            float carrier = (float)Math.Sin(6.2831853 * TremorHz * clockSeconds + phase);
            return TremorAmplitude * a * envelope * carrier;
        }

        /// <summary>
        /// 地震 1 回の波形（<c>[-1,1]</c> に <paramref name="magnitude"/> を掛けたもの）。
        /// 立ち上がって指数的に減衰する。<paramref name="ageSeconds"/> が負なら 0。
        /// </summary>
        public static float EventAt(float ageSeconds, float magnitude)
        {
            if (IsBad(ageSeconds) || ageSeconds < 0f) return 0f;
            if (IsBad(magnitude) || magnitude <= 0f) return 0f;

            float rise = ageSeconds < EventRiseSeconds
                ? ageSeconds / EventRiseSeconds
                : 1f;
            float decay = (float)Math.Exp(-(ageSeconds - EventRiseSeconds < 0f
                                            ? 0f : ageSeconds - EventRiseSeconds)
                                          / EventDecaySeconds);

            float carrier = 0.72f * (float)Math.Sin(6.2831853 * EventFastHz * ageSeconds)
                          + 0.28f * (float)Math.Sin(6.2831853 * EventSlowHz * ageSeconds);

            return magnitude * rise * decay * carrier;
        }

        /// <summary>
        /// 火口の真下の地動 <c>[-1,1]</c>（微動 ＋ 直近 <see cref="TailSlots"/> 枠の群発）。
        /// **t の閉じた式で、状態を 1 つも持たない。**
        /// </summary>
        public static float DisplacementAt(uint seed, float clockSeconds, float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;

            float v = TremorAt(seed, clockSeconds, a);

            int slot = SlotAt(clockSeconds);
            for (int k = 0; k <= TailSlots; k++)
            {
                int s = slot - k;
                if (s < 0) break;

                float start = s * SlotSeconds + OffsetInSlot(seed, s);
                float age = clockSeconds - start;
                if (age < 0f) continue;

                v += EventAt(age, MagnitudeUnit(seed, s, a));
            }

            if (v > 1f) return 1f;
            if (v < -1f) return -1f;
            return v;
        }

        /// <summary>
        /// 距離による減衰 <c>[0,1]</c>。<paramref name="distanceMetres"/> は火口からの距離、
        /// <paramref name="reachMetres"/> は「ここまでは感じる」距離である。
        /// <paramref name="reachMetres"/> の外はきっかり 0 —— **街の反対側は揺れない。**
        /// </summary>
        public static float AttenuationAt(float distanceMetres, float reachMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(reachMetres) || reachMetres <= 0f) return 0f;
            if (distanceMetres >= reachMetres) return 0f;

            // 1 / (1 + (d/ref)^2) を、reach でちょうど 0 になるように窓で切る。
            float x = distanceMetres / reachMetres;
            float near = 1f / (1f + 9f * x * x);
            float window = 1f - x * x;
            float v = near * window;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
