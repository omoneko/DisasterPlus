namespace DisasterPlus.Core.Typhoon
{
    using DisasterPlus.Core.Common;

    /// <summary>
    /// <b>台風の経路を、これから先まで引くのに必要な値ひとそろい。</b>**エンジン非依存。**
    ///
    /// ── なぜ要るのか（2026-09-02、所有者の依頼）────────────────────────
    ///
    /// &gt; 台風の進路、暴風域、風の分布を都市マップ上に示すボタンを配置して、
    /// &gt; 機能するようにしてください
    ///
    /// 「今どこにいるか」はスナップショットの <c>Centre</c> で足りるが、
    /// <b>「これからどこへ行くか」は経路そのものを引き直さないと出ない</b>。
    /// <see cref="TyphoonTrack"/> はそれを
    /// <c>(origin, seed, speed, approachFrames)</c> の 4 つから決めているので、
    /// その 4 つを描画側へ運ぶ箱がこれである。
    ///
    /// ★★ **描画は main スレッド、経路は sim スレッドが決める。**
    ///   だから <c>TyphoonController</c> の static を描画から直接読んではいけない
    ///   （このプロジェクトのスレッド境界の規律）。**スナップショットに載せて運ぶ。**
    ///   一度作ったら書き換えない値の組なので、struct で足りる。
    ///
    /// ★ 予測できるのは<b>経路だけ</b>である。強度は上陸減衰
    ///   （<see cref="TyphoonTrack.DecayAfter"/>）を含み、それは
    ///   「これから陸の上を通るか」に依存するので、**先の強度は名乗らない**。
    ///   暴風域の円は「今の強度での今の半径」だけを描く。
    /// </summary>
    public struct TyphoonTrackPlan
    {
        /// <summary>プレイヤーがクリックした地点（＝経路の基準点）。</summary>
        public readonly Vec2 Origin;

        /// <summary>経路の乱数種。方位と曲率がここから決まる。</summary>
        public readonly uint Seed;

        /// <summary>進行速度（m/frame）。**0 は「持続時間が読めなかった」の意味。**</summary>
        public readonly float Speed;

        /// <summary>クリック地点に着くまでのフレーム数（マップ外から来るぶん）。</summary>
        public readonly uint ApproachFrames;

        /// <summary>寿命（フレーム）。経路をどこまで引くかの終端。</summary>
        public readonly uint TotalFrames;

        public TyphoonTrackPlan(Vec2 origin, uint seed, float speed,
                                uint approachFrames, uint totalFrames)
        {
            Origin = origin;
            Seed = seed;
            Speed = speed;
            ApproachFrames = approachFrames;
            TotalFrames = totalFrames;
        }

        /// <summary>
        /// 経路を引けるか。**false のときは 1 本も線を描かないこと** ——
        /// 速度 0 は「プレハブの持続時間が読めなかった」であって「止まっている」
        /// ではない（<see cref="TyphoonTrack.SpeedFor"/> の doc）。
        /// 読めていない値から線を引くと、それは推測を地図に描くことになる。
        /// </summary>
        public bool Usable
        {
            get { return Speed > 0f && TotalFrames > 0u; }
        }

        /// <summary>台風が生まれてから <paramref name="elapsedFrames"/> 後の中心。</summary>
        public Vec2 CentreAt(uint elapsedFrames)
        {
            return TyphoonTrack.CentreAt(Origin, Seed, elapsedFrames, Speed, ApproachFrames);
        }

        /// <summary>同じ時刻の進行方位（rad）。</summary>
        public float HeadingAt(uint elapsedFrames)
        {
            return TyphoonTrack.HeadingAt(Origin, Seed, elapsedFrames, Speed, ApproachFrames);
        }

        /// <summary>
        /// 経路上の点を <paramref name="into"/> へ書く。**確保しない**
        /// （描画は毎フレームなので、呼び出し側が配列を使い回す）。
        ///
        /// <paramref name="fromFrames"/> から <paramref name="toFrames"/> までを
        /// <paramref name="count"/> 等分して書き、実際に書いた数を返す。
        /// <see cref="Usable"/> が false なら 0。
        /// </summary>
        public int Sample(Vec2[] into, int count, uint fromFrames, uint toFrames)
        {
            if (into == null || count <= 0) return 0;
            if (!Usable) return 0;
            if (count > into.Length) count = into.Length;
            if (toFrames < fromFrames) return 0;

            if (count == 1)
            {
                into[0] = CentreAt(fromFrames);
                return 1;
            }

            uint span = toFrames - fromFrames;
            for (int i = 0; i < count; i++)
            {
                // ★ long で割ってから戻す。span は寿命ぶん（数万）まで行くので、
                //   span * i を uint のまま計算すると大きな count で溢れる。
                uint at = fromFrames + (uint)((long)span * i / (count - 1));
                into[i] = CentreAt(at);
            }

            return count;
        }

        /// <summary>読めなかったときの値。<see cref="Usable"/> は false になる。</summary>
        public static TyphoonTrackPlan None
        {
            get { return new TyphoonTrackPlan(new Vec2(0f, 0f), 0u, 0f, 0u, 0u); }
        }
    }
}
