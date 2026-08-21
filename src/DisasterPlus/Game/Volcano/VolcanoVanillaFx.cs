using System;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤が借りるバニラの粒子エフェクトが、この環境で実際に引けたか。
    ///
    /// ★ <b>「引けた」＝ 描ける。</b> <see cref="VolcanoVanillaFx"/> はクローンに
    /// 失敗しても<b>元のプレハブをそのまま描く</b>ので、
    /// 各項目の <c>bool</c> が**そのまま描画側の門**になっている
    /// （クローンは色と寿命の改善であって、必要条件ではない）。
    /// <c>Assumptions</c> の述語もこの同じ値を見る。
    /// </summary>
    public struct VolcanoVanillaFacts
    {
        /// <summary>噴煙に使う灰色の煙が引けたか。</summary>
        public readonly bool AshResolved;

        /// <summary>火口の炎（<c>Fire Particles</c>）が引けたか。</summary>
        public readonly bool FlameResolved;

        /// <summary>噴石（<c>Medium Explosion Particles</c>）が引けたか。</summary>
        public readonly bool EjectaResolved;

        /// <summary>火砕流もどきの土煙（<c>Collapse Particles</c>）が引けたか。</summary>
        public readonly bool DustResolved;

        /// <summary>
        /// <c>RenderManager.instance.CurrentCameraInfo</c> が今 null でないか。
        /// <c>ParticleEffect.RenderEffect</c> は先頭で <c>CheckRenderDistance</c> /
        /// <c>Intersect</c> を呼ぶので、**null を渡すと NRE になる**（IL 実測）。
        /// </summary>
        public readonly bool CameraInfoResolved;

        public VolcanoVanillaFacts(bool ashResolved, bool flameResolved, bool ejectaResolved,
                                   bool dustResolved, bool cameraInfoResolved)
        {
            AshResolved = ashResolved;
            FlameResolved = flameResolved;
            EjectaResolved = ejectaResolved;
            DustResolved = dustResolved;
            CameraInfoResolved = cameraInfoResolved;
        }

        /// <summary>
        /// 噴火の 3 つ（噴煙・炎・噴石）が全部出せるか。
        /// **<see cref="VolcanoEruptionFx"/> が門にしている式そのもの**である。
        /// </summary>
        public bool EruptionUsable
        {
            get { return CameraInfoResolved && AshResolved && FlameResolved && EjectaResolved; }
        }

        /// <summary>
        /// 火砕流もどきを出せるか。**<see cref="VolcanoPyroclasticFx"/> の門そのもの。**
        /// 噴火の 3 つとは独立に成否が決まるので、検査も別にする。
        /// </summary>
        public bool PyroclasticUsable
        {
            get { return CameraInfoResolved && DustResolved; }
        }
    }

    /// <summary>
    /// バニラの粒子エフェクトを名前で引き、必要なら複製して抱える置き場。
    /// **main スレッド専用**（<c>Object.Instantiate</c> と <c>ParticleSystem</c> を触る）。
    ///
    /// ── なぜ自前メッシュをやめたのか ──────────────────────────────
    ///
    /// 実機テストで <c>Shader.Find</c> が**組み込みの <c>"Standard"</c> を含めて
    /// 全ての名前に null を返した**。自前マテリアルの噴煙は 1 粒も描かれていなかった。
    /// 一方バニラの粒子エフェクトは<b>既に読み込まれ、既に動くマテリアルを持っている</b>。
    /// 借りるべきものが最初から在った、というのが結論である。
    ///
    /// ★ 「CS のマテリアルを借りると見えない」（③が確定させた罠）は
    ///   <b>自前 <c>MeshRenderer</c> / <c>Graphics.DrawMesh</c> の話</b>である。
    ///   粒子のマテリアルは <c>ParticleSystemRenderer</c> に付いていて per-instance の
    ///   <c>MaterialPropertyBlock</c> を要求しない（バニラ自身が
    ///   <c>EffectsWrapper.CreateParticleEffect</c> で借りたマテリアルを差している）。
    ///   **したがってここには当たらない。** 溶岩の面（<see cref="VolcanoLavaFx"/>）は
    ///   粒子ではないので、あちらは <see cref="ShaderPool"/> のままで正しい。
    ///
    /// ── 引き方（2 通りある。どちらも public）─────────────────────────
    ///
    /// <code>
    /// EffectManager.instance.m_EffectsWrapper.GetBuiltinEffect(name)  読み込み済み全部
    /// EffectCollection.FindEffect(name)                               登録済み 186 個だけ
    /// </code>
    ///
    /// ⑤が使う 4 つのうち <c>Factory Smoke</c> は <c>EffectCollection</c> に**登録されて
    /// いない**（`FindEffect` は null を返す）。だから 1 本目が <c>GetBuiltinEffect</c> で、
    /// <c>FindEffect</c> は保険である。
    /// <c>GetBuiltinEffect</c> の戻り値は <c>System.Object</c> なので <c>as</c> で受ける
    /// （IL 実測。型を間違えても例外にならず null になる）。
    ///
    /// ── ★ 引けなかったときに何が起きるか ────────────────────────────
    ///
    /// **1 行ログを出して、その 1 つを出さないだけ。** 例外は投げないし、噴火は続く。
    /// 探索は <see cref="RetryFrames"/> フレームに 1 回までへ間引く
    /// （<c>FindEffect</c> は見つからないとゲーム側が警告を吐くので、
    /// 毎フレーム呼ぶと output_log が埋まる）。
    ///
    /// ── ★ クローンは「改善」であって必要条件ではない ─────────────────────
    ///
    /// 複製に失敗したら**元のプレハブをそのまま描く**。色も寿命も可視距離もバニラのまま
    /// になるが、何も描かれないよりはるかに良い。この設計のおかげで、
    /// <see cref="ScanFacts"/> の <c>bool</c> が**そのまま描画側の門**になる
    /// （「検査は通ったのに機能が動かない」という、このプロジェクトで 2 度出た形を作らない）。
    ///
    /// ── ★ 元のプレハブを書き換えないこと（§D-5）───────────────────────
    ///
    /// <c>ParticleSystem.main.startColor</c> / <c>startSize</c> や
    /// <c>ParticleEffect.m_minLifeTime</c> は**そのプレハブの共有状態**である。
    /// <c>Fire Particles</c> を直に書き換えると**街じゅうの建物火災の色が変わり**、
    /// しかもセーブではなくメモリ上に残る。値を変えたいときは必ず複製する。
    /// 炎（<see cref="Flames"/>）は複製しない ——**そのままの見た目が欲しい**からで、
    /// だから 1 バイトも書き換えない。
    ///
    /// ── ★ 静的キャッシュを配列にしない（③が出荷した不具合）─────────────────
    ///
    /// <c>UnityEngine.Object</c> の <c>==</c> は破棄済みを null と等価にするが、
    /// <c>static GameObject[]</c> に入れるとその自己修復が効かず、
    /// **2 つ目の都市で無言のまま見えなくなる**。ここは参照 1 個ずつで持ち、
    /// 毎回その参照そのものを <c>== null</c> で見る。
    /// </summary>
    internal static partial class VolcanoVanillaFx
    {
        /// <summary>噴煙の元。細い上昇ジェットで、柱そのもの。**基本ゲーム。**</summary>
        internal const string AshName = "Factory Smoke";

        /// <summary><see cref="AshName"/> が引けないときの代え（より白く太い）。</summary>
        internal const string AshAltName = "Factory Steam";

        /// <summary>炎。建物火災とまったく同じもの。**基本ゲーム。**</summary>
        internal const string FlameName = "Fire Particles";

        /// <summary>噴石。初速 100–150 m/s、放出角 0–80°。**基本ゲーム。**</summary>
        internal const string EjectaName = "Medium Explosion Particles";

        /// <summary>火砕流もどきの土煙。建物崩壊の粉塵。**基本ゲーム。**</summary>
        internal const string DustName = "Collapse Particles";

        /// <summary>探し直すまでに空けるフレーム数（<see cref="ShaderPool"/> と同じ間引き）。</summary>
        private const int RetryFrames = 300;

        // ── ★ 配列にしない。参照 1 個ずつ ─────────────────────────

        private static GameObject _ashObject;
        private static ParticleEffect _ashClone;

        /// <summary>
        /// 噴煙柱の**傘**に使う 2 個目の複製（淡い灰・粒が大きい・寿命が長い）。
        /// 引けなくても柱の複製で代用できるので、こちらは<b>門にしない</b>。
        /// </summary>
        private static GameObject _umbrellaObject;
        private static ParticleEffect _umbrellaClone;
        private static GameObject _ejectaObject;
        private static ParticleEffect _ejectaClone;
        private static GameObject _dustObject;
        private static ParticleEffect _dustClone;

        /// <summary>
        /// 炎（<c>Fire Particles</c>）。**複製ではなくゲーム自身のプレハブそのもの。**
        /// ★ 配列にしない。参照 1 個で持ち、毎回 <c>== null</c> で見る。
        /// </summary>
        private static ParticleEffect _flame;

        private static int _ashMiss;
        private static int _ashAltMiss;

        // ★ 傘は**自分の**間引きカウンタを持つ。噴煙柱と共有すると、同じフレームで
        //   2 回減るので RetryFrames が実質半分になる（引けない環境で探索が倍になる）。
        private static int _umbrellaMiss;
        private static int _umbrellaAltMiss;
        private static int _flameMiss;
        private static int _ejectaMiss;
        private static int _dustMiss;

        /// <summary>複製に失敗したので、以後は元のプレハブをそのまま使う。</summary>
        private static bool _ashCloneRefused;
        private static bool _umbrellaCloneRefused;
        private static bool _ejectaCloneRefused;
        private static bool _dustCloneRefused;

        /// <summary>「引けなかった」を名前ごとに 1 度だけ名乗るための旗。</summary>
        private static bool _ashMissLogged;
        private static bool _umbrellaRefusedLogged;
        private static bool _flameMissLogged;
        private static bool _ejectaMissLogged;
        private static bool _dustMissLogged;

        private static bool _inventoryLogged;

        // ── 診断が読むキャッシュ（**bool と int と string だけ**）─────────────
        //
        // ★★ IDisasterFeature.WriteDiagnostics は **sim スレッド専用**である。
        //    そこから Unity のオブジェクトに触ってはいけない —— 参照の == null さえ、
        //    ネイティブへ降りる比較なので main スレッドの契約の外にある。
        //    だから診断へ出すのは、main スレッドが解決したときに書いておいた
        //    この平の値だけにする（Detail / *ResolvedCached が読むのはここ）。

        private static bool _ashOk;
        private static bool _flameOk;
        private static bool _ejectaOk;
        private static bool _dustOk;
        private static bool _ashCloned;
        private static bool _umbrellaCloned;
        private static bool _ejectaCloned;
        private static bool _dustCloned;

        /// <summary>直近に測った在庫（診断の 1 行に出す）。</summary>
        private static int _effectCount;
        private static int _particleMaterialCount;

        /// <summary>
        /// <c>ParticleEffect.m_particleSystem</c>（private, <c>[NonSerialized]</c>）。
        /// <c>InitializeEffect()</c> が実体を作れたかを**複製のときに 1 度だけ**確かめる。
        /// <c>EmitParticles</c> はこのフィールドを null 検査なしで参照するので、
        /// 作れていない複製を描画へ渡すと**バニラの中で NRE になる**。
        /// </summary>
        private static readonly System.Reflection.FieldInfo ParticleSystemField =
            typeof(ParticleEffect).GetField("m_particleSystem",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>噴煙に使うエフェクト。引けなければ null（**呼び出し側が黙って飛ばす**）。</summary>
        internal static ParticleEffect AshPlume()
        {
            // ★ 解決の結果をここで控える。診断（sim スレッド）は
            //   Unity のオブジェクトに触れないので、この平の bool を読む。
            ParticleEffect resolved = ResolveAsh();
            _ashOk = resolved != null;
            _ashCloned = _ashClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveAsh()
        {
            if (_ashClone != null) return _ashClone;

            // ★ 間引きカウンタは名前ごとに別にする。1 本にすると、1 つ目が外れた
            //   フレームで 2 つ目が必ず「まだ探さない」になり、代えが永久に試されない。
            ParticleEffect source = Source(AshName, ref _ashMiss, ref _ashMissLogged);
            if (source == null) source = Source(AshAltName, ref _ashAltMiss, ref _ashMissLogged);
            if (source == null) return null;

            if (_ashCloneRefused) return Ready(source) ? source : null;

            // ここへ来たのは複製の参照が死んでいるとき（別の都市・破棄済み）。
            // 抜け殻を残したまま作り直さない。
            ReleaseAndDestroy(ref _ashObject, ref _ashClone);

            _ashObject = CloneAsh(source);
            _ashClone = _ashObject == null ? null : _ashObject.GetComponent<ParticleEffect>();
            if (_ashClone == null)
            {
                ReleaseAndDestroy(ref _ashObject, ref _ashClone);
                _ashCloneRefused = true;
                // ★ 初期化されていないプレハブを描画へ渡さない（Ready の doc）。
                return Ready(source) ? source : null;
            }

            return _ashClone;
        }

        /// <summary>
        /// 噴煙柱の**傘**に使う複製。引けない・複製できないときは <c>null</c> を返す ——
        /// **元のプレハブへは落とさない**。呼び出し側（<see cref="VolcanoEruptionFx"/>）は
        /// そのとき柱の複製で傘を描くので、素の <c>Factory Smoke</c>（可視 1 km・粒 7）で
        /// 代用するより見た目が良い。
        /// </summary>
        internal static ParticleEffect AshUmbrella()
        {
            ParticleEffect resolved = ResolveUmbrella();
            _umbrellaCloned = _umbrellaClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveUmbrella()
        {
            if (_umbrellaClone != null) return _umbrellaClone;
            if (_umbrellaCloneRefused) return null;

            // ★ 元プレハブは噴煙柱と同じものだが、**間引きカウンタは別**にする
            //   （共有すると同じフレームで 2 回減って RetryFrames が半分になる）。
            //   「引けなかった」の 1 行だけは名前ごとなので共有でよい。
            ParticleEffect source = Source(AshName, ref _umbrellaMiss, ref _ashMissLogged);
            if (source == null)
            {
                source = Source(AshAltName, ref _umbrellaAltMiss, ref _ashMissLogged);
            }
            if (source == null) return null;

            ReleaseAndDestroy(ref _umbrellaObject, ref _umbrellaClone);

            _umbrellaObject = CloneAshUmbrella(source);
            _umbrellaClone = _umbrellaObject == null
                ? null : _umbrellaObject.GetComponent<ParticleEffect>();
            if (_umbrellaClone == null)
            {
                ReleaseAndDestroy(ref _umbrellaObject, ref _umbrellaClone);
                _umbrellaCloneRefused = true;

                // ★ **黙って諦めない。** 1 行だけ名乗る（毎フレームの経路なので
                //   Warn は使わない。噴火はそのまま続き、傘は柱の複製で描かれる）。
                if (!_umbrellaRefusedLogged)
                {
                    _umbrellaRefusedLogged = true;
                    Log.Info("volcano effects: the ash umbrella could not be cloned in this "
                             + "environment; Disaster + draws the umbrella with the column's "
                             + "own clone instead (it looks denser) and the eruption carries on");
                }
                return null;
            }

            return _umbrellaClone;
        }

        /// <summary>
        /// 火口の炎。**複製しない** —— バニラの建物火災とまったく同じ見た目が欲しく、
        /// 1 バイトも書き換えないから共有して安全である（クラス doc の §D-5）。
        ///
        /// ★★ **1 本目は <c>BuildingProperties.m_fireEffect.m_particleEffect</c> から取る。**
        ///   <c>BuildingManager.InitializeProperties</c> が
        ///   <c>m_fireEffect.InitializeEffect()</c> を呼び、<c>FireEffect.CreateEffect</c> が
        ///   その中で <c>m_particleEffect.InitializeEffect()</c> を呼ぶ（IL 実測）ので、
        ///   **この経路で届く 1 個は必ず初期化済み**である。名前引きは同じ物に当たる
        ///   はずだが、当たる保証はどこにも無い。
        ///
        /// ★ 初期化されていないプレハブを描画へ渡さないこと。<c>EmitParticles</c> は
        ///   <c>m_particleSystem</c> を null 検査なしで参照するので（IL 実測）、
        ///   **バニラの中で NRE になる**。
        /// </summary>
        internal static ParticleEffect Flames()
        {
            ParticleEffect resolved = ResolveFlames();
            _flameOk = resolved != null;
            return resolved;
        }

        private static ParticleEffect ResolveFlames()
        {
            if (_flame != null) return _flame;

            if (_flameMiss > 0)
            {
                _flameMiss--;
                return null;
            }

            ParticleEffect found = BuildingFireParticles();
            if (found == null) found = Lookup(FlameName);

            if (found == null || !Ready(found))
            {
                _flameMiss = RetryFrames;
                if (!_flameMissLogged)
                {
                    _flameMissLogged = true;
                    Log.Info("volcano effects: the game's own fire particles could not be "
                             + "reached in this environment; the crater has no flames and the "
                             + "eruption carries on");
                }
                return null;
            }

            _flame = found;
            return _flame;
        }

        /// <summary>
        /// <c>BuildingProperties.m_fireEffect</c>（<c>FireEffect</c>）の粒子。
        /// **初期化済みが保証されている唯一の経路**（<see cref="Flames"/> の doc）。
        /// </summary>
        private static ParticleEffect BuildingFireParticles()
        {
            try
            {
                if (!Singleton<BuildingManager>.exists) return null;

                var properties = Singleton<BuildingManager>.instance.m_properties;
                if (properties == null) return null;

                // m_fireEffect の宣言型は EffectInfo なので as で降ろす（IL 実測）。
                var fire = properties.m_fireEffect as FireEffect;
                return fire == null ? null : fire.m_particleEffect;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>噴石。**重力を下向きに直した複製**を返す（元は上向き＝爆炎用）。</summary>
        internal static ParticleEffect Ejecta()
        {
            // ★ 解決の結果をここで控える。診断（sim スレッド）は
            //   Unity のオブジェクトに触れないので、この平の bool を読む。
            ParticleEffect resolved = ResolveEjecta();
            _ejectaOk = resolved != null;
            _ejectaCloned = _ejectaClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveEjecta()
        {
            if (_ejectaClone != null) return _ejectaClone;

            ParticleEffect source = Source(EjectaName, ref _ejectaMiss, ref _ejectaMissLogged);
            if (source == null) return null;

            if (_ejectaCloneRefused) return Ready(source) ? source : null;

            ReleaseAndDestroy(ref _ejectaObject, ref _ejectaClone);

            _ejectaObject = CloneEjecta(source);
            _ejectaClone = _ejectaObject == null
                ? null : _ejectaObject.GetComponent<ParticleEffect>();
            if (_ejectaClone == null)
            {
                ReleaseAndDestroy(ref _ejectaObject, ref _ejectaClone);
                _ejectaCloneRefused = true;
                // ★ 初期化されていないプレハブを描画へ渡さない（Ready の doc）。
                return Ready(source) ? source : null;
            }

            return _ejectaClone;
        }

        /// <summary>火砕流もどきの土煙。**ほぼ水平に広がる複製**を返す。</summary>
        internal static ParticleEffect PyroclasticDust()
        {
            // ★ 解決の結果をここで控える。診断（sim スレッド）は
            //   Unity のオブジェクトに触れないので、この平の bool を読む。
            ParticleEffect resolved = ResolveDust();
            _dustOk = resolved != null;
            _dustCloned = _dustClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveDust()
        {
            if (_dustClone != null) return _dustClone;

            ParticleEffect source = Source(DustName, ref _dustMiss, ref _dustMissLogged);
            if (source == null) return null;

            if (_dustCloneRefused) return Ready(source) ? source : null;

            ReleaseAndDestroy(ref _dustObject, ref _dustClone);

            _dustObject = CloneDust(source);
            _dustClone = _dustObject == null ? null : _dustObject.GetComponent<ParticleEffect>();
            if (_dustClone == null)
            {
                ReleaseAndDestroy(ref _dustObject, ref _dustClone);
                _dustCloneRefused = true;
                // ★ 初期化されていないプレハブを描画へ渡さない（Ready の doc）。
                return Ready(source) ? source : null;
            }

            return _dustClone;
        }

        /// <summary>
        /// 今この環境で描けるカメラ情報。**null を <c>RenderEffect</c> へ渡さないこと。**
        /// </summary>
        internal static RenderManager.CameraInfo CameraInfo()
        {
            try
            {
                return Singleton<RenderManager>.exists
                    ? Singleton<RenderManager>.instance.CurrentCameraInfo : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// バニラが今フレームぶんとして使っている時間差。
        /// <b><c>Time.deltaTime</c> ではない。</b> <c>EffectManager.EndRenderingImpl</c> は
        /// <c>SimulationManager.m_simulationTimeDelta</c> を渡していて（IL 実測）、
        /// こちらを使うと**一時停止と速度変更にそのまま追随する**。
        /// 読めなければ 0 を返す ——**0 は「今フレームは 1 粒も出さない」であって、
        /// 例外でも作り話でもない。**
        /// </summary>
        internal static float EffectTimeDelta()
        {
            try
            {
                if (!Singleton<SimulationManager>.exists) return 0f;
                float dt = Singleton<SimulationManager>.instance.m_simulationTimeDelta;
                if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) return 0f;
                return dt;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// **main スレッド専用。** 何が引けるかを調べる。
        /// <c>Assumptions</c> と描画側の両方がこれを使い、
        /// **描画側が門にしているのはこの struct の <c>bool</c> そのもの**である。
        /// </summary>
        internal static VolcanoVanillaFacts ScanFacts()
        {
            LogInventoryOnce();

            return new VolcanoVanillaFacts(
                AshPlume() != null,
                Flames() != null,
                Ejecta() != null,
                PyroclasticDust() != null,
                CameraInfo() != null);
        }

        /// <summary>
        /// 診断に出す 1 行（**英語**）。将来のゲーム更新で黙って何も出なくなったときの
        /// 唯一の手がかりなので、**何が引けて何が複製できたか**を必ず名乗る。
        ///
        /// ★★ **ここから解決を走らせないこと。** 診断は sim スレッドから組み立てられる
        ///   （<c>FeatureHost.BuildReport</c>）ので、<c>Object.Instantiate</c> はもちろん
        ///   Unity の参照比較すら踏んではいけない。読むのは main スレッドが
        ///   書いておいた平の値だけである。
        /// </summary>
        internal static string Detail
        {
            get
            {
                return "builtin effects=" + (_effectCount > 0 ? _effectCount.ToString() : "?")
                       + ", particle materials="
                       + (_particleMaterialCount > 0 ? _particleMaterialCount.ToString() : "?")
                       + "; ash=" + State(_ashOk, _ashCloned, _ashCloneRefused)
                       + ", umbrella=" + (_umbrellaCloned ? "cloned"
                            : (_umbrellaCloneRefused ? "shares the column clone" : "not yet"))
                       + ", flames=" + (_flameOk ? "shared" : "MISSING")
                       + ", ejecta=" + State(_ejectaOk, _ejectaCloned, _ejectaCloneRefused)
                       + ", dust=" + State(_dustOk, _dustCloned, _dustCloneRefused);
            }
        }

        /// <summary>
        /// 土煙を直近に引けていたか。**診断専用の平の読み取り**で、
        /// 解決は 1 度も走らせない（<see cref="Detail"/> と同じ理由）。
        /// </summary>
        internal static bool DustResolvedCached { get { return _dustOk; } }

        /// <summary>
        /// **実機で 1 度だけ在庫を数える**（事実文書の在庫が PARTIAL のままなので）。
        /// <c>GetEffectList()</c> は <c>Keys.ToArray()</c> で**配列を確保する**ので、
        /// ここでしか呼ばない。数えられるまでは <see cref="_inventoryLogged"/> を立てない
        /// （読み込みの途中では辞書がまだ空でありうる）。
        /// </summary>
        internal static void LogInventoryOnce()
        {
            if (_inventoryLogged) return;

            try
            {
                if (!Singleton<EffectManager>.exists) return;
                var wrapper = Singleton<EffectManager>.instance.m_EffectsWrapper;
                if (wrapper == null || wrapper.m_BuiltinEffects == null) return;

                int count = wrapper.m_BuiltinEffects.Count;
                if (count <= 0) return;

                _effectCount = count;
                _particleMaterialCount = wrapper.m_BuiltinParticleMaterials != null
                    ? wrapper.m_BuiltinParticleMaterials.Count : 0;
                _inventoryLogged = true;

                Log.Info("volcano effects: " + count + " builtin effect(s), "
                         + _particleMaterialCount + " particle material(s); "
                         // ★ この 1 行が MISSING なら、⑤は借り物を 1 つも使わない。
                         //   引けているかどうかを確かめる手段が無くなるためで、
                         //   確かめずに RenderEffect へ渡すとバニラの中で NRE になる。
                         + "ParticleEffect.m_particleSystem="
                         + (ParticleSystemField != null ? "ok" : "MISSING") + "; "
                         + AshName + "=" + Probe(wrapper, AshName)
                         + ", " + FlameName + "=" + Probe(wrapper, FlameName)
                         + ", " + EjectaName + "=" + Probe(wrapper, EjectaName)
                         + ", " + DustName + "=" + Probe(wrapper, DustName));
            }
            catch (Exception e)
            {
                // 数えられなくても噴火は出る。**1 度だけ名乗って以後は黙る。**
                _inventoryLogged = true;
                Log.Info("volcano effects: the builtin effect inventory could not be read ("
                         + e.GetType().Name + ")");
            }
        }

        /// <summary>
        /// **main スレッド。レベルアンロードで呼ぶ。** 冪等。
        ///
        /// ★ <c>ReleaseEffect()</c> を先に呼ぶこと。<c>ParticleEffect.CreateEffect</c> が
        ///   作る内側の複製は <b>"Particle Effects" ルート（<c>DontDestroyOnLoad</c>）</b>
        ///   の下にぶら下がっていて、こちらの <c>GameObject</c> を消しても道連れにならない。
        ///   飛ばすと**都市を出入りするたびに粒子系が 1 組ずつ残る。**
        /// </summary>
        internal static void Destroy()
        {
            ReleaseAndDestroy(ref _ashObject, ref _ashClone);
            ReleaseAndDestroy(ref _umbrellaObject, ref _umbrellaClone);
            ReleaseAndDestroy(ref _ejectaObject, ref _ejectaClone);
            ReleaseAndDestroy(ref _dustObject, ref _dustClone);

            // ★ 炎は**借りているだけ**なので Release も Destroy もしない
            //   （こちらが初期化していないものを解放すると、ゲームの建物火災を止める）。
            //   参照を手放すだけ。
            _flame = null;

            _ashCloneRefused = false;
            _umbrellaCloneRefused = false;
            _ejectaCloneRefused = false;
            _dustCloneRefused = false;
            _ashMiss = 0;
            _ashAltMiss = 0;
            _umbrellaMiss = 0;
            _umbrellaAltMiss = 0;
            _flameMiss = 0;
            _ejectaMiss = 0;
            _dustMiss = 0;
            _inventoryLogged = false;
            _effectCount = 0;
            _particleMaterialCount = 0;
            _ashOk = false;
            _flameOk = false;
            _ejectaOk = false;
            _dustOk = false;
            _ashCloned = false;
            _umbrellaCloned = false;
            _ejectaCloned = false;
            _dustCloned = false;
            // ★ _ashMissLogged などは戻さない（ゲームのビルドに対する事実であって
            //   都市ごとの状態ではない。④⑤の他の型と同じ判断）。
        }

        private static void ReleaseAndDestroy(ref GameObject go, ref ParticleEffect clone)
        {
            try
            {
                if (clone != null) clone.ReleaseEffect();
            }
            catch
            {
                // 解放できなくても続ける。GameObject は下で必ず消す。
            }

            if (go != null) UnityEngine.Object.Destroy(go);
            go = null;
            clone = null;
        }

        /// <summary>
        /// 名前でバニラのプレハブを引く。**引けなければ null**（例外は出さない）。
        /// 見つからないあいだは <see cref="RetryFrames"/> フレームに 1 回まで。
        /// </summary>
        private static ParticleEffect Source(string name, ref int missCount, ref bool logged)
        {
            if (missCount > 0)
            {
                missCount--;
                return null;
            }

            ParticleEffect found = Lookup(name);

            if (found == null)
            {
                missCount = RetryFrames;
                if (!logged)
                {
                    logged = true;
                    // ★ Log.Warn はスロットルされない。ここは毎フレームの経路なので
                    //   Info を 1 回だけ。**欠けても噴火は続く。**
                    Log.Info("volcano effects: the game's particle effect \"" + name
                             + "\" could not be looked up in this environment; Disaster + "
                             + "leaves that piece of the eruption out and carries on");
                }
            }

            return found;
        }

        /// <summary>
        /// 名前でバニラのプレハブを引くだけ。**間引きもログも持たない純粋な走査。**
        /// </summary>
        private static ParticleEffect Lookup(string name)
        {
            try
            {
                if (Singleton<EffectManager>.exists)
                {
                    var wrapper = Singleton<EffectManager>.instance.m_EffectsWrapper;
                    if (wrapper != null)
                    {
                        // ★ 戻り値は System.Object（IL 実測）。as で受けて型違いも null にする。
                        var byName = wrapper.GetBuiltinEffect(name) as ParticleEffect;
                        if (byName != null) return byName;
                    }
                }

                // ★ 保険。Factory Smoke は EffectCollection に**登録されていない**ので
                //   こちらでは引けないが、Fire Particles などは引ける。
                return EffectCollection.FindEffect(name) as ParticleEffect;
            }
            catch
            {
                return null;
            }
        }


        private static string Probe(EffectsWrapper wrapper, string name)
        {
            try
            {
                return wrapper.GetBuiltinEffect(name) != null ? "ok" : "MISSING";
            }
            catch
            {
                return "MISSING";
            }
        }

        private static string State(bool resolved, bool cloned, bool refused)
        {
            if (!resolved) return "MISSING";
            if (cloned) return "cloned";
            return refused ? "shared (clone refused)" : "shared";
        }
    }
}
