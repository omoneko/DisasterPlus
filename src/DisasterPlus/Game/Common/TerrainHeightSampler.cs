using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Implements Core's IHeightSampler on top of CS's terrain.
    /// SampleDetailHeight is read-only and safe from either the sim or the main thread.
    ///
    /// ★★ **Always look at <c>Singleton&lt;T&gt;.exists</c> first** (overall review M14).
    /// When <c>sInstance</c> is null, <c>Singleton&lt;T&gt;.instance</c> runs
    /// <c>FindObjectOfType</c> and <c>new GameObject</c> — **main-thread-only APIs** that
    /// crash if stepped on from the sim thread (<c>VolcanoReader</c> writes this down as a
    /// rule of this mod). This type is also called from ⑤'s survey (sim thread; the path
    /// that then goes on to wreck the city irreversibly).
    ///
    /// Returns <b>NaN</b> when it cannot read. Return 0 and it is indistinguishable from
    /// "the height was sea level", so the caller never notices the read failed.
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
