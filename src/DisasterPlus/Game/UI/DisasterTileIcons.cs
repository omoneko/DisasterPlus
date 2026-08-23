using System;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 災害パネルのタイルに載せる**自前の絵**（<c>Texture2D</c> 2 枚）。main スレッド専用。
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; タブアイコンの火山と台風をイラストにしてほしいです。
    ///
    /// ── ★★ バニラのスプライト名は 1 つも書かない ────────────────────
    ///
    /// <see cref="DisasterPanelBar"/> のクラス doc の約束である。スプライト名は
    /// アトラスのデータであってアセンブリからは読めないので、名前を当てにいくと
    /// **見えないタイル**になり得る。しかも④と⑤は<b>バニラに存在しない災害</b>で、
    /// 当てにいく絵がそもそも無い。
    ///
    /// 絵は <see cref="DisasterIconArt"/>（Core、画素の式）が持っており、ここは
    /// それを <c>Texture2D</c> に焼くだけである。**形の議論は Core 側にある。**
    /// サイレン MOD の <c>WarningIcon</c> と同じ組み立てで、
    /// <c>UITextureSprite</c> に挿せばそのまま出る。
    ///
    /// ── 焼くのは 1 都市に 1 回 ───────────────────────────────
    ///
    /// 128×128 が 2 枚（128 KB）。<see cref="Destroy"/> をレベルアンロードで必ず
    /// 呼ぶこと —— <c>Texture2D</c> は <c>Component</c> ではないので、
    /// <c>GameObject</c> を消しても道連れにならない。
    ///
    /// ★ **参照 1 個ずつで持つ。** 配列に入れると、破棄済み（fake-null）を抱えたまま
    ///   非 null になり、2 つ目の都市で無言で絵が出なくなる（⑤で実際に踏んだ形）。
    /// </summary>
    public static class DisasterTileIcons
    {
        /// <summary>一辺（px）。タイルは 109×100 なので、これで足りる。</summary>
        private const int Size = 128;

        private static Texture2D _volcano;
        private static Texture2D _typhoon;
        private static bool _failed;

        /// <summary>火山の絵。**引けなければ null**（呼び出し側は文字のままにする）。</summary>
        public static Texture2D Volcano
        {
            get
            {
                if (_volcano != null) return _volcano;
                if (_failed) return null;

                _volcano = Build(true, "DisasterPlus_IconVolcano");
                return _volcano;
            }
        }

        /// <summary>台風の絵。同上。</summary>
        public static Texture2D Typhoon
        {
            get
            {
                if (_typhoon != null) return _typhoon;
                if (_failed) return null;

                _typhoon = Build(false, "DisasterPlus_IconTyphoon");
                return _typhoon;
            }
        }

        /// <summary>**レベルアンロードで必ず呼ぶ。** 冪等。</summary>
        public static void Destroy()
        {
            if (_volcano != null) UnityEngine.Object.Destroy(_volcano);
            if (_typhoon != null) UnityEngine.Object.Destroy(_typhoon);

            _volcano = null;
            _typhoon = null;
            // _failed は戻さない（ゲームのビルドに対する事実である）。
        }

        private static Texture2D Build(bool volcano, string name)
        {
            try
            {
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.name = name;
                tex.wrapMode = TextureWrapMode.Clamp;

                var pixels = new Color32[Size * Size];

                for (int y = 0; y < Size; y++)
                {
                    // ★ v は **0 が下**（<see cref="DisasterIconArt"/> の約束）。
                    //   Unity のテクスチャも 0 行目が下なので、そのまま入れてよい。
                    float v = (y + 0.5f) / Size;

                    for (int x = 0; x < Size; x++)
                    {
                        float u = (x + 0.5f) / Size;

                        IconPixel p = volcano
                            ? DisasterIconArt.Volcano(u, v)
                            : DisasterIconArt.Typhoon(u, v);

                        pixels[y * Size + x] = new Color32(p.R, p.G, p.B, p.A);
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(false);
                return tex;
            }
            catch (Exception e)
            {
                _failed = true;
                Log.Warn("disaster tile icons: the texture could not be built ("
                         + e.GetType().Name + "); the tiles keep their text label");
                return null;
            }
        }
    }
}
