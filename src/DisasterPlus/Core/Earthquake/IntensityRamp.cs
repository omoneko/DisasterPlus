namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 全体円盤のランプ <c>s = 1 - d/R</c> を、**同心の塗り重ね**として画面に出すための段取り。
    ///
    /// ── なぜ「塗り重ね」なのか ──────────────────────────────
    ///
    /// CS1 のオーバーレイ API が描けるのは<b>塗り潰された</b>円・四角・帯だけである
    /// （<c>RenderManager.OverlayEffect</c>。円は <c>ID_CenterPos</c> に
    /// <c>(x, z, -r, +r)</c> を渡す 1 パス、四角は 4 隅を渡す 1 パスで、
    /// どちらも太さの引数を持たない＝輪郭線ではない）。連続階調のランプを 1 回の
    /// 描画で出す手段は無いので、**同心円を大きい順に半透明で塗り重ねて**
    /// 階段状のランプを作る。
    ///
    /// 段数は <see cref="SeismicScale.Steps"/> と**同じ 10**にしてある。これは
    /// 見た目の都合ではなく誠実さの都合で、パネルのカーソル行が出している
    /// 10 段のバーと、地図に出る 10 段の濃さが**同じ量子化**になる。
    /// 片方が 10 段でもう片方が 8 段だと、同じ s に対して 2 つの違う「段」が
    /// 画面に同時に出ることになる。
    ///
    /// ── 濃さは s に比例させる（曲げない）─────────────────────────
    ///
    /// 円盤 <c>k</c>（0 がいちばん外、<c>Steps-1</c> がいちばん内）は
    /// <c>s &gt; k/Steps</c> の範囲を覆う。したがって 0..k を塗り終えた時点で
    /// 実際に見えている輪帯は <c>k/Steps &lt; s ≤ (k+1)/Steps</c> で、その代表値は
    /// 中点 <c>(k+0.5)/Steps</c> である。**そこでの累積不透明度が
    /// <c>MaxOpacity × s</c> ちょうどになるように**、各円盤単体のアルファを逆算する
    /// （<see cref="DrawAlpha"/>）。単に同じアルファを重ねると
    /// <c>1-(1-a)^(k+1)</c> という**飽和した曲線**になり、弱い側を強く・強い側を弱く
    /// 見せる。それは「示していると称する量とは違う減衰を描く」ことに他ならない。
    ///
    /// ── 前提: <c>alphaBlend: true</c> が本当にアルファ合成であること ───────────
    ///
    /// <c>OverlayEffect</c> は <c>m_shapeShader</c> と <c>m_shapeShaderBlend</c> の
    /// **2 本のシェーダ**を持ち、<c>alphaBlend</c> 引数だけがその選択を決める
    /// （IL 実測: <c>DrawCircle</c> IL_0127–013F / <c>DrawQuad</c> IL_014C–0164）。
    /// 2 本ある理由は「片方が合成しないから」以外に無く、バニラ自身も
    /// <c>DisasterTool.RenderOverlay</c> が <c>alphaBlend: true</c> で
    /// 地区の色の上に円を重ねている。シェーダのブレンド式そのものは DLL に
    /// 無い（アセット側）ので、ここは IL からは詰め切れない最後の一歩である。
    ///
    /// **外した場合の壊れ方までは押さえてある。** 仮に合成ではなく上書きだったとしても、
    /// <see cref="DrawAlpha"/> は k について単調増加なので、**濃さが震央へ向かって
    /// 増える向きは絶対に反転しない**（曲線が凸に歪むだけ）。実機チェックリストの
    /// 項目 53 が「10 段の濃淡が見えるか、それとも一様な 1 枚の円盤に見えるか」で
    /// この一歩を確定させる。
    /// </summary>
    public static class IntensityRamp
    {
        /// <summary>
        /// 段数。**<see cref="SeismicScale.Steps"/> と同じでなければならない**
        /// （クラス doc の「同じ量子化」）。ユニットテストがこの一致を固定している。
        /// </summary>
        public const int Steps = SeismicScale.Steps;

        /// <summary>
        /// 震央（s = 1）での累積不透明度の上限。
        ///
        /// 1.0 にしない。オーバーレイの下には地形・道路・建物があり、それを
        /// 完全に隠すと「地図に重ねた分布」ではなく「地図の代わり」になる。
        /// </summary>
        public const float MaxOpacity = 0.55f;

        /// <summary>
        /// 円盤 <paramref name="index"/> の半径。0 が全体円盤そのもの（R）、
        /// <c>Steps-1</c> がいちばん内側（R/Steps）。**大きい順に描くこと。**
        /// </summary>
        public static float RadiusOf(int index, float radius)
        {
            if (index < 0 || index >= Steps) return 0f;
            if (float.IsNaN(radius) || radius <= 0f) return 0f;
            return radius * (Steps - index) / Steps;
        }

        /// <summary>
        /// 円盤 0..<paramref name="index"/> を塗った時点で見えている輪帯の代表 s（中点）。
        /// </summary>
        public static float RepresentativeS(int index)
        {
            if (index < 0) return 0f;
            if (index >= Steps) return 1f;
            return (index + 0.5f) / Steps;
        }

        /// <summary>
        /// その輪帯で**見えていてほしい**累積不透明度。s に厳密に比例する。
        /// </summary>
        public static float TargetOpacity(int index)
        {
            if (index < 0) return 0f;
            return MaxOpacity * RepresentativeS(index);
        }

        /// <summary>
        /// 円盤 <paramref name="index"/> **単体**に与えるアルファ。
        ///
        /// 大きい順にアルファ合成したとき、輪帯 k の累積が
        /// <see cref="TargetOpacity"/>(k) になるようにする:
        /// <code>
        /// 1 - A_k = (1 - a_0)(1 - a_1)...(1 - a_k)
        /// a_k     = (A_k - A_{k-1}) / (1 - A_{k-1})
        /// </code>
        /// 分子は <c>MaxOpacity / Steps</c> の定数、分母は 1 未満なので
        /// a_k は k について単調増加になる。
        /// </summary>
        public static float DrawAlpha(int index)
        {
            if (index < 0 || index >= Steps) return 0f;

            float previous = index == 0 ? 0f : TargetOpacity(index - 1);
            float rest = 1f - previous;
            if (rest <= 0f) return 0f;

            float a = (TargetOpacity(index) - previous) / rest;
            if (a < 0f) return 0f;
            if (a > 1f) return 1f;
            return a;
        }

        /// <summary>
        /// 大きい順に <see cref="DrawAlpha"/> をアルファ合成した結果。
        /// **テスト専用の逆算**で、描画側はこれを使わない。
        /// </summary>
        public static float AccumulatedOpacity(int index)
        {
            float remaining = 1f;
            for (int k = 0; k <= index && k < Steps; k++)
            {
                remaining *= 1f - DrawAlpha(k);
            }
            return 1f - remaining;
        }
    }
}
