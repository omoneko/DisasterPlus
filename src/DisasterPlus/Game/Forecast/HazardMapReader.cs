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
        private static bool _missingFieldWarned;

        private const int GridSize = 256;
        private const float WorldUnitsPerCell = 38.4f; // IL 実測: SampleDisasterHazardMap の座標変換（除算）
        private const float GridOrigin = 128f;          // IL 実測: 同上のオフセット

        /// <summary>
        /// worldPos 地点のハザード強度を 0-255 で返す。
        ///
        /// subMode は現状のバニラ実装（m_hazardAmount は単一グリッド）では絞り込みに
        /// 使われない。Task 5 の呼び出し契約と、将来サブモード別グリッドに変わった場合の
        /// 拡張点として引数だけ残してある。
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
                Log.Error("hazard sample failed", e);
                return 0;
            }
        }
    }
}
