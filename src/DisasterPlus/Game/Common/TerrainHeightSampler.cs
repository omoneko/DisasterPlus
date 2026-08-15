using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Core の IHeightSampler を CS の地形で実装する。
    /// SampleDetailHeight は読み取り専用で、sim / main どちらのスレッドからも安全。
    ///
    /// ★★ <c>Singleton&lt;T&gt;.exists</c> を**必ず先に見る**（全体レビュー M14）。
    /// <c>Singleton&lt;T&gt;.instance</c> は <c>sInstance</c> が null のとき
    /// <c>FindObjectOfType</c> と <c>new GameObject</c> を走らせる **main スレッド専用
    /// API** で、sim スレッドから踏むと落ちる（<c>VolcanoReader</c> がこの MOD の
    /// 規則としてそう書いている）。この型は⑤の調査（sim スレッド。そのあと不可逆に
    /// 都市を壊す経路）からも呼ばれる。
    ///
    /// 読めないときは <b>NaN</b> を返す。0 を返すと「海面の高さだった」と区別が付かず、
    /// 呼び出し側は読めなかったことに気づけない。
    /// </summary>
    public class TerrainHeightSampler : IHeightSampler
    {
        public static readonly TerrainHeightSampler Instance = new TerrainHeightSampler();

        private TerrainHeightSampler() { }

        public float SampleHeight(float x, float z)
        {
            if (!Singleton<TerrainManager>.exists) return float.NaN;

            var tm = Singleton<TerrainManager>.instance;
            if (tm == null) return float.NaN;

            return tm.SampleDetailHeight(new Vector3(x, 0f, z));
        }
    }
}
