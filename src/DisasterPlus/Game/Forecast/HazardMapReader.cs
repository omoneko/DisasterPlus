using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地点のハザード値を読む。バニラは色でしか見せないので、
    /// 数値にすること自体が本機能の付加価値になる。
    ///
    /// IL 実測（Task 4 Step 1）でブリーフの想定が崩れた点:
    ///   ブリーフの推測: Byte DisasterManager.SampleDisasterHazardMap(Vector3, SubInfoMode)
    ///   実際の宣言:     Color DisasterManager.SampleDisasterHazardMap(Vector3 pos)
    /// SubInfoMode は引数に無く（呼んでも絞り込みは効かない）、戻り値は byte ではなく
    /// UI 描画用に補間済みの Color。中身は private フィールド m_hazardAmount
    /// （256x256 の byte グリッド、z*256+x でインデックス）をバイリニア補間し、
    /// InfoProperties.m_modeProperties[29]（DisasterHazard の固定インデックス、IL の
    /// ldc.i4.s 29）の neutral/active/target 色へブレンドするだけで、数値そのものは
    /// 呼び出し元に渡ってこない。Color から数値を逆算しようとすると、その 3 色の
    /// 実際の RGBA（データ駆動でシリアライズされており IL からは読めない）を知らないと
    /// 一般には不可能なので、Color 経由での近似は行わない。
    ///
    /// そこで本クラスは m_hazardAmount を直接読む。private フィールドへの反射アクセスは
    /// この MOD で初めてではなく、ToolRegistration.Register&lt;T&gt;() と同じ手口
    /// （フィールドが見つからなければ例外にせず警告してゼロを返す＝ゲーム更新でフィールド名が
    /// 変わっても静かに壊れるだけで落ちない）。
    ///
    /// スレッド安全性: SampleDisasterHazardMap の IL 本体は m_hazardAmount に対して
    /// ldfld と ldelem.u1 のみで stfld が一切無い（読み取り専用）。加えて、同じ配列を
    /// 読む DisasterManager.UpdateTexture（Texture2D 更新＝Unity API 制約で main/描画
    /// スレッド専用）もロック無しで同じフィールドを読んでおり、バニラ自身が
    /// 「sim スレッドが書き込み中でも main スレッドから読んでよい」前提で実装されている。
    /// byte[] の単一要素読み取りはティアしない（.NET の最小アドレス単位）ので、
    /// 最悪でも 1 sim tick 古い値を読むだけで安全側に倒れる。よって本クラスも
    /// main スレッドから直接読んでよい（WeatherReader のような sim スレッド経由・
    /// スナップショット越しの読み取りは不要）。
    /// </summary>
    public static class HazardMapReader
    {
        // DisasterManager.m_hazardAmount: private byte[]、256x256 グリッド（z*256+x でインデックス）。
        // IL 実測: DisasterManager.Awake() で確保、SampleDisasterHazardMap / UpdateTexture が読むだけ。
        private static readonly FieldInfo HazardAmountField =
            typeof(DisasterManager).GetField("m_hazardAmount",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // フィールドが見つからない場合の警告は起動あたり 1 回に抑える
        // （呼び出し頻度が高いので Log.Warn を毎回出すとログが溢れる）。
        //
        // レベルアンロードでリセットしないのは意図的。「見つからない」はこの DLL が
        // 参照しているゲームのビルドに対する事実であり、都市ごとの状態ではない
        // （プロセス寿命＝ゲーム起動から終了まで変わらない）。「セッション状態は
        // アンロードでリセットする」という本プロジェクトの原則をここに機械的に
        // 当てはめて Reset() を足すと、都市を切り替えるたびに同じ 1 行が再び
        // 出るだけの「リセットのためのリセット」になってしまう。
        private static bool _missingFieldWarned;

        // SampleAt() 内の想定外例外（GetValue の型不一致等）も同じ理由で 1 回だけ
        // Log.Error で鳴らし、以後は Log.Diag（キー単位で 512 sim フレームに 1 回）へ
        // 落とす。ここは毎フレーム呼ばれ得るパスなので、Log.Error を無条件で
        // 置いたままだと、1 回きりの取りこぼしではなく恒常的な例外（将来ゲーム更新で
        // m_hazardAmount の型が変わり GetValue が InvalidCastException を出し続ける、等）
        // が起きたときにログが埋まる。1 回目は確実に目立たせつつ、そのあとは
        // 完全に黙らせない程度に絞る。
        private static bool _sampleErrorLogged;

        // グリッドの解像度・セルサイズはバニラの public const（IL 実測で確認済み:
        // HAZARDMAP_RESOLUTION=256 Int32, HAZARDMAP_CELL_SIZE=38.4 Single、両方 public
        // static const）をそのまま使う。ここで自前の定数を持たないのは、将来
        // 解像度が変われば参照先の const を拾って再ビルドすれば自動的に追従し、
        // 手でコピーした数字が黙って古いまま残る事故を避けるため。
        // 原点（グリッド中心）は解像度の半分として導出する（第三の数字を別途持たない）。
        private const int GridSize = DisasterManager.HAZARDMAP_RESOLUTION;
        private const float WorldUnitsPerCell = DisasterManager.HAZARDMAP_CELL_SIZE;
        private const float GridOrigin = DisasterManager.HAZARDMAP_RESOLUTION / 2f;

        /// <summary>
        /// worldPos 地点のハザード強度を 0-255 で返す。
        ///
        /// subMode は現状のバニラ実装（m_hazardAmount は単一グリッド）では絞り込みに
        /// 使われない。Task 5 の呼び出し契約と、将来サブモード別グリッドに変わった場合の
        /// 拡張点として引数だけ残してある。
        ///
        /// 格子の左下（floor）側 1 セルだけを読む点に注意。バニラの
        /// SampleDisasterHazardMap は 4 隅をバイリニア補間した Color を返すが、
        /// こちらは補間せず生の格子値を返す（Color 経由の逆算は不正確になるため、
        /// 上のクラス doc の通り意図して採用していない）。そのため、セルの境界付近では
        /// この戻り値とバニラのヒートマップの見た目が最大 1 セル分ずれ得る。
        /// これはバグではなく「不正確な色からの逆算より、生の格子値の方がマシ」という
        /// トレードオフの結果。
        /// </summary>
        public static byte SampleAt(Vector3 worldPos, InfoManager.SubInfoMode subMode, out bool ok)
        {
            ok = false;
            try
            {
                if (!Singleton<DisasterManager>.exists) return 0;

                if (HazardAmountField == null)
                {
                    if (!_missingFieldWarned)
                    {
                        _missingFieldWarned = true;
                        Log.Error("DisasterManager.m_hazardAmount field not found (game update?)", null);
                    }
                    return 0;
                }

                var map = (byte[])HazardAmountField.GetValue(Singleton<DisasterManager>.instance);
                if (map == null || map.Length == 0) return 0;

                int gx = Mathf.Clamp(Mathf.FloorToInt(worldPos.x / WorldUnitsPerCell + GridOrigin), 0, GridSize - 1);
                int gz = Mathf.Clamp(Mathf.FloorToInt(worldPos.z / WorldUnitsPerCell + GridOrigin), 0, GridSize - 1);
                int index = gz * GridSize + gx;
                if (index < 0 || index >= map.Length) return 0;

                ok = true;
                return map[index];
            }
            catch (System.Exception e)
            {
                // SampleAt はカーソルが地図上にある間ずっと毎フレーム呼ばれ得るパス。
                // 1 回目だけ確実に目立たせ（Log.Error）、以後は Log.Diag のキー単位
                // スロットル（512 sim フレームに 1 回）に落として流量を抑える
                // （完全に黙らせるのではなく、間隔を空けて出し続ける）。
                if (!_sampleErrorLogged)
                {
                    _sampleErrorLogged = true;
                    Log.Error("hazard sample failed", e);
                }
                else
                {
                    Log.Diag("HazardSample", "hazard sample failed: " + e.GetType().Name);
                }
                return 0;
            }
        }
    }
}
