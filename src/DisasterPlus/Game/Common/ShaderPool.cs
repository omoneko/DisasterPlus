using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>どちらの合成を狙うか。取れなければ互いに落ちる。</summary>
    public enum ShaderPreference
    {
        /// <summary>加算（炎・噴煙・溶岩）。</summary>
        Additive = 0,

        /// <summary>アルファブレンド（雲）。</summary>
        AlphaBlended = 1,
    }

    /// <summary>
    /// <see cref="ShaderPool"/> が返した 1 件。**<c>Shader</c> が null なら「描かない」。**
    /// </summary>
    public struct ShaderPick
    {
        /// <summary>使うシェーダ。**null なら何も描かない。**</summary>
        public readonly UnityEngine.Shader Shader;

        /// <summary>そのシェーダの名前（診断用）。取れなければ null。</summary>
        public readonly string Name;

        /// <summary>
        /// 粒子系（加算 / アルファブレンド / パーティクル）で解決したか。
        /// **<c>Standard</c> はここに数えない** —— 数えた瞬間にこの旗は
        /// 構造上 false になれなくなる（⑤ <c>VolcanoLavaFx</c> のクラス doc）。
        /// </summary>
        public readonly bool Particle;

        /// <summary>
        /// <c>Shader.Find</c> ではなく**読み込み済み <c>Material</c> から借りた**か。
        /// 借りたのは<b>シェーダだけ</b>で、<c>Material</c> そのものではない
        /// （<see cref="ShaderPool"/> のクラス doc の区別）。
        /// </summary>
        public readonly bool Borrowed;

        /// <summary>
        /// <c>Standard</c> まで落ちたか。**このときだけ**呼び出し側が
        /// <see cref="ShaderPool.MakeStandardTransparent"/> を掛ける
        /// （既定の <c>Standard</c> は不透明なので、掛けないと不透明な板になる）。
        /// </summary>
        public readonly bool StandardFallback;

        public ShaderPick(UnityEngine.Shader shader, bool particle, bool borrowed,
                          bool standardFallback)
        {
            Shader = shader;
            Name = shader != null ? shader.name : null;
            Particle = particle;
            Borrowed = borrowed;
            StandardFallback = standardFallback;
        }

        /// <summary>マテリアルを作れるか。**破棄済み（fake-null）もここで弾く。**</summary>
        public bool Usable { get { return Shader != null; } }

        /// <summary>診断に出す 1 行ぶんの説明（**英語**）。</summary>
        public string Describe()
        {
            if (Shader == null) return "NONE (no shader resolved)";

            string s = Name;
            if (Borrowed) s += "  (shader borrowed from a loaded material, not Shader.Find)";
            if (StandardFallback)
            {
                s += "  (fallback: no particle shader in this build; forced to transparent "
                     + "so it does not draw opaque quads)";
            }
            else if (!Particle)
            {
                s += "  (fallback: not a particle shader, so it blends but does not glow)";
            }
            return s;
        }
    }

    /// <summary>
    /// 自前の <c>Material</c> に載せるシェーダを 1 つ返す。**main スレッド専用。**
    ///
    /// ── なぜ <c>Shader.Find</c> だけでは足りないのか（13 回目の誤った「確認済み」）───
    ///
    /// 実機テストで <c>Shader.Find</c> が**組み込みの <c>"Standard"</c> を含めて
    /// 全ての名前に null を返した**。
    /// 事前調査では出荷アセットをバイト走査して <c>globalgamemanagers</c> の中に
    /// <c>Particles/Additive</c> と <c>Particles/Alpha Blended</c> の文字列を見つけていたが、
    /// <b>アセットに名前が在ることと <c>Shader.Find</c> が解決することは別である</b> ——
    /// Unity はビルドに含めなかったシェーダを剥がすし、<c>Shader.Find</c> は
    /// **実際にロードされているシェーダしか返さない**。
    ///
    /// ── 借りるのは「シェーダ」であって「マテリアル」ではない ─────────────
    ///
    /// この 2 つは紛らわしいが、まったく別の話である:
    ///
    /// <code>
    /// ✗ CS の Material を借りて自前の MeshRenderer / DrawMesh に載せる
    ///     → 何も描画されないか真っ黒になる（③ 付録 A、火災旋風 §4.9）。
    ///       CS のマテリアルはエンジンが供給する per-instance データ
    ///       （VehicleManager.m_materialBlock の ID_TyreMatrix 等）を前提にしている。
    ///
    /// ✓ CS の Material から Shader だけを取り、new Material(thatShader) を自分で作る
    ///     → 出荷済み Unity ゲームで名前が引けないときの定石。
    ///       per-instance データを前提にした値は 1 つも引き継がない。
    /// </code>
    ///
    /// **この型は借りた <c>Material</c> インスタンスを 1 度も返さない。**
    /// 返すのは <c>Shader</c> だけで、<c>Material</c> は呼び出し側が自分で作り、
    /// 自分で <c>Object.Destroy</c> する。
    ///
    /// ── 費用（<c>Resources.FindObjectsOfTypeAll</c> は安くない）──────────────
    ///
    /// 走査は配列の確保と全オブジェクトの走査を伴うので、**1 セッションに 1 回**しか
    /// 走らせない。解決できたら結果を持ち回し、以後は走査しない。
    /// 1 つも解決できなかったときだけ <see cref="RetryFrames"/> 空けて再走査する
    /// （将来のビルドでロードのタイミングが変わった場合の保険。
    /// ④の <c>ApplyVanillaBoost</c> と同じ間引きで、こちらは走査が重いぶん長い）。
    /// **毎フレームの経路から呼んでも走査は走らない。**
    ///
    /// ── 都市ごとの状態を持たない ────────────────────────────
    ///
    /// ここが抱えるのは<b>ゲームのビルドに対する事実</b>だけで、都市の状態も
    /// <c>Material</c> も持たない。だからレベルアンロードで消す物が無い。
    /// 万一シェーダが破棄されても <see cref="ShaderPick.Usable"/> が
    /// <c>UnityEngine.Object</c> の <c>==</c> 多重定義で fake-null を弾き、
    /// 次の呼び出しで解決し直す（**配列に入れないのはこの自己修復のためである**。
    /// 火災旋風 §4.8）。
    ///
    /// ── 在庫をログに出す ──────────────────────────────────
    ///
    /// この環境に実際どんなシェーダが在るのかは**まだ誰も知らない**。
    /// 次の実機テストが推測ではなく答えを持ち帰れるよう、走査した時点で
    /// 相異なるシェーダ名を <c>Log.Info</c> に 1 度だけ出す（件数の上限つき）。
    /// </summary>
    public static class ShaderPool
    {
        /// <summary>1 つも解決しなかったときに再走査するまで空けるフレーム数。</summary>
        private const int RetryFrames = 1800;

        /// <summary>ログに名前を出す上限（診断であってダンプではない）。</summary>
        private const int InventoryLogMax = 60;

        /// <summary>1 行に並べる名前の数。</summary>
        private const int NamesPerLine = 6;

        /// <summary>どの段でも借りないことを表す点数（低いほど良い）。</summary>
        private const int NoScore = int.MaxValue;

        /// <summary>加算を狙うときに <c>Shader.Find</c> へ渡す名前。**順序が優先順位である。**</summary>
        private static readonly string[] AdditiveNames =
        {
            "Particles/Additive",
            "Legacy Shaders/Particles/Additive",
            "Particles/Alpha Blended",
            "Legacy Shaders/Particles/Alpha Blended",
        };

        /// <summary>アルファブレンドを狙うときの名前。**順序が優先順位である。**</summary>
        private static readonly string[] AlphaBlendedNames =
        {
            "Particles/Alpha Blended",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Additive",
            "Legacy Shaders/Particles/Additive",
        };

        // ★ 配列にしない。preference ごとに参照 1 個（struct のフィールド）で持ち、
        //   毎回その参照そのものを != null で見る（クラス doc の自己修復）。
        private static ShaderPick _additive;
        private static ShaderPick _alphaBlended;

        private static bool _scanned;
        private static int _nextScanFrame;
        private static bool _errorLogged;

        private static int _distinctShaderCount = -1;

        /// <summary>走査で見つけた相異なるシェーダ名の数（**まだ走査していなければ -1**）。</summary>
        public static int DistinctShaderCount { get { return _distinctShaderCount; } }

        /// <summary>
        /// 使えるシェーダを 1 つ返す。**main スレッド専用**（<c>Shader</c> /
        /// <c>Resources</c> に触る）。**毎フレーム呼んでよい** —— 解決済みなら
        /// キャッシュを返すだけで、走査には間引きが掛かっている。
        ///
        /// <b>返り値の <see cref="ShaderPick.Usable"/> を必ず見ること。</b>
        /// false は「この環境では描けない」であって、例外ではない。
        /// </summary>
        public static ShaderPick Resolve(ShaderPreference preference)
        {
            try
            {
                EnsureResolved();
            }
            catch (Exception e)
            {
                // ★ ここで throw させない。呼び出し元はどれも毎フレームの描画経路で、
                //   例外を上げるとフレームごとにログが埋まる（③が 6,938 行で踏んだ形）。
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("shader resolution failed; nothing is drawn", e);
                }
                return new ShaderPick(null, false, false, false);
            }

            return preference == ShaderPreference.AlphaBlended ? _alphaBlended : _additive;
        }

        /// <summary>
        /// <c>Standard</c> を透過モードにする。**<see cref="ShaderPick.StandardFallback"/> が
        /// 立っているときだけ**呼ぶこと。Unity 5.6 の StandardShaderGUI が Transparent
        /// モードで入れるのと同じ設定である。
        ///
        /// 借りてきた別のシェーダに掛けてはいけない —— <c>_Mode</c> も <c>_SrcBlend</c> も
        /// <c>Standard</c> の契約であって、他のシェーダでは無意味か有害になる。
        /// </summary>
        public static void MakeStandardTransparent(Material m)
        {
            if (m == null) return;

            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        // ── 解決 ───────────────────────────────────────────

        /// <summary>
        /// 両方の preference を**まとめて**解決する。走査が 1 回で済むからである。
        /// 既に両方取れていれば即座に戻る（＝毎フレームの呼び出しはここで終わる）。
        /// </summary>
        private static void EnsureResolved()
        {
            if (_additive.Usable && _alphaBlended.Usable) return;

            // ★ 走査の間引き。1 度走査したあとは RetryFrames 空けるまで走らせない。
            if (_scanned && Time.frameCount < _nextScanFrame) return;
            _nextScanFrame = Time.frameCount + RetryFrames;

            Catalog catalog = ScanCatalog();
            _distinctShaderCount = catalog.DistinctCount;

            // ★ 在庫のログはセッションに 1 回だけ（次の実機テストへの答え）。
            if (!_scanned)
            {
                _scanned = true;
                LogInventory(catalog);
            }

            if (!_additive.Usable) _additive = Choose(ShaderPreference.Additive, catalog);
            if (!_alphaBlended.Usable) _alphaBlended = Choose(ShaderPreference.AlphaBlended, catalog);
        }

        /// <summary>
        /// 1 件を決める。**この順序が仕様である**:
        /// <code>
        /// ① Shader.Find(粒子系の名前)      安い。名前が通る環境ならここで終わる
        /// ② 読み込み済み Material から借用  出荷済みゲームで名前が引けないときの定石
        /// ③ Shader.Find("Standard")        不透明。呼び出し側が透過へ落とす
        /// ④ 在庫の中の "Standard"          Shader.Find 自体が壊れている環境の保険
        /// ⑤ null                           **描かない**
        /// </code>
        /// </summary>
        private static ShaderPick Choose(ShaderPreference preference, Catalog catalog)
        {
            string[] names = preference == ShaderPreference.AlphaBlended
                ? AlphaBlendedNames : AdditiveNames;

            // ① 判定は必ず != null（?? は fake-null を素通しする）。
            for (int i = 0; i < names.Length; i++)
            {
                UnityEngine.Shader s = UnityEngine.Shader.Find(names[i]);
                if (s != null) return new ShaderPick(s, true, false, false);
            }

            // ② シェーダ**だけ**を借りる（Material は借りない。クラス doc）。
            UnityEngine.Shader borrowed = preference == ShaderPreference.AlphaBlended
                ? catalog.AlphaBlended : catalog.Additive;
            if (borrowed != null)
            {
                bool particle = preference == ShaderPreference.AlphaBlended
                    ? catalog.AlphaBlendedParticle : catalog.AdditiveParticle;
                return new ShaderPick(borrowed, particle, true, false);
            }

            // ③ 粒子系が 1 つも無い環境の受け皿。
            UnityEngine.Shader standard = UnityEngine.Shader.Find("Standard");
            if (standard != null) return new ShaderPick(standard, false, false, true);

            // ④ Shader.Find が組み込みにさえ null を返す環境（実機で観測済み）。
            if (catalog.Standard != null) return new ShaderPick(catalog.Standard, false, true, true);

            // ⑤ 何も無い。**描かない**（借り物の Material で誤魔化さない）。
            return new ShaderPick(null, false, false, false);
        }

        // ── 在庫の走査 ─────────────────────────────────────

        /// <summary>1 回の走査で分かったこと。**静的には持たない**（その場で使い切る）。</summary>
        private struct Catalog
        {
            public UnityEngine.Shader Additive;
            public int AdditiveScore;
            public bool AdditiveParticle;

            public UnityEngine.Shader AlphaBlended;
            public int AlphaBlendedScore;
            public bool AlphaBlendedParticle;

            public UnityEngine.Shader Standard;

            public List<string> Names;
            public int MaterialCount;
            public int DistinctCount;
        }

        /// <summary>
        /// 読み込み済みの <c>Material</c> を 1 度だけ全走査し、そこに載っている
        /// <c>Shader</c> を集める。**確保を伴うので <see cref="EnsureResolved"/> 以外から
        /// 呼ばないこと。**
        /// </summary>
        private static Catalog ScanCatalog()
        {
            var catalog = new Catalog();
            catalog.AdditiveScore = NoScore;
            catalog.AlphaBlendedScore = NoScore;
            catalog.Names = new List<string>();

            var seen = new HashSet<string>();

            // Resources.FindObjectsOfTypeAll は非アクティブもアセットも返す
            // （SceneObjects のクラス doc で確認済みの挙動）。シェーダが欲しいので
            // ここでは絞り込まない。
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            if (materials == null) return catalog;

            catalog.MaterialCount = materials.Length;

            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null) continue;   // fake-null（破棄済み）

                UnityEngine.Shader s = m.shader;
                if (s == null) continue;

                string name = s.name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!seen.Add(name)) continue;   // 同じシェーダは 1 回だけ見る

                catalog.Names.Add(name);

                if (name == "Standard" && catalog.Standard == null) catalog.Standard = s;

                Consider(ref catalog, s, name);
            }

            catalog.DistinctCount = catalog.Names.Count;
            return catalog;
        }

        /// <summary>1 つのシェーダを両方の preference の候補として採点する。</summary>
        private static void Consider(ref Catalog catalog, UnityEngine.Shader s, string name)
        {
            string lower = name.ToLowerInvariant();

            bool particle;

            int additive = ScoreFor(lower, ShaderPreference.Additive, out particle);
            if (Better(additive, catalog.AdditiveScore, name, catalog.Additive))
            {
                catalog.Additive = s;
                catalog.AdditiveScore = additive;
                catalog.AdditiveParticle = particle;
            }

            int alpha = ScoreFor(lower, ShaderPreference.AlphaBlended, out particle);
            if (Better(alpha, catalog.AlphaBlendedScore, name, catalog.AlphaBlended))
            {
                catalog.AlphaBlended = s;
                catalog.AlphaBlendedScore = alpha;
                catalog.AlphaBlendedParticle = particle;
            }
        }

        /// <summary>
        /// 新しい候補が今の最良より良いか。同点なら名前の序数比較で決める ——
        /// <c>Resources.FindObjectsOfTypeAll</c> の順序は保証されないので、
        /// **同点を先着で決めると起動ごとに違うシェーダが当たる**（＝再現しない見た目）。
        /// </summary>
        private static bool Better(int score, int bestScore, string name,
                                   UnityEngine.Shader best)
        {
            if (score == NoScore) return false;
            if (score < bestScore) return true;
            if (score > bestScore) return false;
            if (best == null) return true;
            return string.CompareOrdinal(name, best.name) < 0;
        }

        /// <summary>
        /// 借りる価値の点数（**低いほど良い**）。<see cref="NoScore"/> は「借りない」。
        ///
        /// <paramref name="particle"/> は粒子系として数えてよいかで、
        /// 「半透明だが粒子系ではない」段では false になる。
        ///
        /// ★ 同じ段では <c>Custom/</c> で始まる名前を後ろへ回す。CS 自身のシェーダは
        ///   エンジンが供給する per-instance データを前提にしていることがあり
        ///   （クラス doc の区別）、素の <c>Material</c> に載せると期待どおりに
        ///   ならない可能性がそのぶん高い。
        /// </summary>
        private static int ScoreFor(string lower, ShaderPreference preference, out bool particle)
        {
            bool additive = lower.Contains("additive");
            bool alphaBlended = lower.Contains("alpha blended") || lower.Contains("alphablended");
            bool isParticle = lower.Contains("particle");
            bool transparent = lower.Contains("transparent") || lower.Contains("unlit");

            bool wantAdditive = preference == ShaderPreference.Additive;

            int tier;
            if (isParticle && additive) tier = wantAdditive ? 0 : 1;
            else if (isParticle && alphaBlended) tier = wantAdditive ? 1 : 0;
            else if (additive || alphaBlended) tier = 2;
            else if (isParticle) tier = 3;
            else if (transparent) tier = 4;
            else
            {
                // 不透明なシェーダは借りない。載せると板の群れになるだけで、
                // Standard を透過モードへ落とした方がまだ見られる。
                particle = false;
                return NoScore;
            }

            particle = tier <= 3;
            return tier * 2 + (lower.StartsWith("custom/", StringComparison.Ordinal) ? 1 : 0);
        }

        // ── 在庫のログ ─────────────────────────────────────

        /// <summary>
        /// この環境に何が在るのかを 1 度だけ名乗る。**件数に上限を付ける**
        /// （診断であってダンプではない。最大 12 行）。
        /// 粒子系・半透明らしい名前を先に並べるので、頭の数行を読めば答えが分かる。
        /// </summary>
        private static void LogInventory(Catalog catalog)
        {
            bool standardFindable = UnityEngine.Shader.Find("Standard") != null;

            Log.Info("shader inventory: " + catalog.DistinctCount
                     + " distinct shader(s) on " + catalog.MaterialCount
                     + " loaded material(s); Shader.Find(\"Standard\") = "
                     + (standardFindable ? "ok" : "NULL")
                     + " (a name present in the shipped assets does not mean Shader.Find "
                     + "resolves it - Unity strips shaders that are not in the build)");

            if (catalog.Names == null || catalog.Names.Count == 0) return;

            catalog.Names.Sort(CompareInventory);

            int shown = catalog.Names.Count < InventoryLogMax
                ? catalog.Names.Count : InventoryLogMax;

            var line = new StringBuilder();
            for (int i = 0; i < shown; i++)
            {
                if (line.Length > 0) line.Append(" | ");
                line.Append(catalog.Names[i]);

                if ((i + 1) % NamesPerLine == 0 || i == shown - 1)
                {
                    Log.Info("shader inventory: " + line);
                    line.Length = 0;   // .NET 3.5 に StringBuilder.Clear は無い
                }
            }

            if (catalog.Names.Count > shown)
            {
                Log.Info("shader inventory: ... and " + (catalog.Names.Count - shown)
                         + " more (particle / transparent names are listed first)");
            }
        }

        /// <summary>粒子系・半透明らしい名前を先に、そのあとは序数順に並べる。</summary>
        private static int CompareInventory(string a, string b)
        {
            int ra = InterestRank(a);
            int rb = InterestRank(b);
            if (ra != rb) return ra - rb;
            return string.CompareOrdinal(a, b);
        }

        private static int InterestRank(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("particle") || lower.Contains("additive")
                || lower.Contains("alpha") || lower.Contains("transparent")
                || lower.Contains("unlit") || lower.Contains("standard"))
            {
                return 0;
            }
            return 1;
        }
    }
}
