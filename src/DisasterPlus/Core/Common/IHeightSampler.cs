namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 地形の高さを引く。Game 層が TerrainManager.SampleDetailHeight で実装する。
    /// SampleDetailHeight は読み取り専用でどちらのスレッドからも安全。
    /// </summary>
    public interface IHeightSampler
    {
        float SampleHeight(float x, float z);
    }
}
