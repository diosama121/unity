using UnityEngine;

/// <summary>
/// 坡度拦截器：静态工具类，用于判断两点之间地形坡度是否可接受。
/// 主要用于道路生成、路径规划等场景中过滤坡度过大的连接。
/// </summary>
public static class SlopeInterceptor
{
    /// <summary>
    /// 默认最大坡度百分比（20%），即高度差 / 水平距离 <= 0.20。
    /// 例如水平100m允许最大20m高差。
    /// </summary>
    private const float DefaultMaxSlopePercent = 0.20f;

    /// <summary>
    /// 水平距离阈值（米）。两点水平距离小于此值时，不做坡度判断，直接返回 true。
    /// 避免极近距离下因微小高度差产生极大坡度比值导致误判。
    /// </summary>
    private const float MinHorizontalDistance = 0.5f;

    /// <summary>
    /// 判断两点之间的地形坡度是否可接受。
    /// 优先使用 TerrainGridSystem.SampleHeight 获取高度，
    /// 若 terrain 为 null 则回退到 WorldModel.Instance.GetUnifiedHeight。
    /// </summary>
    /// <param name="posA">起点世界坐标</param>
    /// <param name="posB">终点世界坐标</param>
    /// <param name="maxSlopePercent">最大允许坡度百分比，默认 0.20</param>
    /// <param name="terrain">地形网格系统实例，可为 null</param>
    /// <returns>坡度可接受返回 true，否则 false</returns>
    public static bool IsSlopeAcceptable(
        Vector3 posA,
        Vector3 posB,
        float maxSlopePercent,
        TerrainGridSystem terrain)
    {
        float heightA, heightB;

        if (terrain != null)
        {
            // 主路径：通过 TerrainGridSystem 采样地形高度
            heightA = terrain.SampleHeight(new Vector2(posA.x, posA.z));
            heightB = terrain.SampleHeight(new Vector2(posB.x, posB.z));
        }
        else
        {
            // 备选路径：通过 WorldModel 统一高度接口获取
            heightA = WorldModel.Instance.GetUnifiedHeight(posA.x, posA.z);
            heightB = WorldModel.Instance.GetUnifiedHeight(posB.x, posB.z);
        }

        return IsSlopeAcceptable(heightA, heightB, posA, posB, maxSlopePercent);
    }

    /// <summary>
    /// 判断两点之间的坡度是否可接受（直接给定两端高度）。
    /// 适用于调用方已经自行获取了高度数据的场景。
    /// </summary>
    /// <param name="heightA">起点高度</param>
    /// <param name="heightB">终点高度</param>
    /// <param name="posA">起点世界坐标（仅用 XZ 计算水平距离）</param>
    /// <param name="posB">终点世界坐标（仅用 XZ 计算水平距离）</param>
    /// <param name="maxSlopePercent">最大允许坡度百分比，默认 0.20</param>
    /// <returns>坡度可接受返回 true，否则 false</returns>
    public static bool IsSlopeAcceptable(
        float heightA,
        float heightB,
        Vector3 posA,
        Vector3 posB,
        float maxSlopePercent = DefaultMaxSlopePercent)
    {
        // 计算 XZ 平面上的水平距离
        float dx = posB.x - posA.x;
        float dz = posB.z - posA.z;
        float horizontalDistance = Mathf.Sqrt(dx * dx + dz * dz);

        // 水平距离过小时不做判断，避免除零或极大比值
        if (horizontalDistance < MinHorizontalDistance)
            return true;

        float heightDiff = Mathf.Abs(heightB - heightA);
        float slopeRatio = heightDiff / horizontalDistance;

        return slopeRatio <= maxSlopePercent;
    }
}