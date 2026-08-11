using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Core の IHeightSampler を CS の地形で実装する。
    /// SampleDetailHeight は読み取り専用で、sim / main どちらのスレッドからも安全。
    /// </summary>
    public class TerrainHeightSampler : IHeightSampler
    {
        public static readonly TerrainHeightSampler Instance = new TerrainHeightSampler();

        private TerrainHeightSampler() { }

        public float SampleHeight(float x, float z)
        {
            return TerrainManager.instance.SampleDetailHeight(new Vector3(x, 0f, z));
        }
    }
}
