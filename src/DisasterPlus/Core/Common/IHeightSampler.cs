namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// Looks up the terrain height. The Game layer implements it with
    /// TerrainManager.SampleDetailHeight. SampleDetailHeight is read-only and safe from
    /// either thread.
    /// </summary>
    public interface IHeightSampler
    {
        float SampleHeight(float x, float z);
    }
}
