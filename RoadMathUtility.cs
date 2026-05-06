using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class RoadMathUtility
{
    /// <summary>
    /// 极角排序：以 center 为原点，按 Atan2(dz, dx) 顺时针排序
    /// </summary>
    public static List<Vector3> SortAroundCenter(Vector3 center, List<Vector3> ring)
    {
        return ring.OrderBy(v => Mathf.Atan2(v.z - center.z, v.x - center.x)).ToList();
    }

    // 后续可扩展法线推挤、退让计算等，但当前调度器直接使用 spline 中的 Normal
}