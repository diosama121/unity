using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 道路 UV 投影工具类（三角形级局部投影）
/// 负责三角形归属分类、局部坐标系 UV 计算
/// </summary>
public static class RoadUVProjector
{
    // ── 数据结构 ──
    public struct TriangleRegionInfo
    {
        public int subMeshIndex;      // 0~5
        public bool isJunction;
        public int edgeIndex;
        public int junctionIndex;
    }

    public class EdgeRoadInfo
    {
        public Vector3[] poly;
        public Vector3 start;
        public Vector3 end;
        public Vector3 forward;       // (end - start).normalized
        public Vector3 right;         // Cross(Vector3.up, forward).normalized
        public Vector3 origin;        // start
        public int targetSubMesh;     // 0/1/2
    }

    public class JunctionInfo
    {
        public Vector3[] poly;
        public int degree;
        public Vector3 center;
        public float mainAngle;       // 弧度，贴图旋转角
    }

    // ── UV 投影函数 ──
    /// <summary>直路边局部 UV（沿道路方向铺设）</summary>
    public static Vector2 EdgeLocalUV(Vector3 worldPos, Vector3 origin, Vector3 forward, Vector3 right, float uvScale)
    {
        Vector3 delta = worldPos - origin;
        float u = Vector3.Dot(delta, right) * uvScale;
        float v = Vector3.Dot(delta, forward) * uvScale;
        return new Vector2(u, v);
    }

    /// <summary>路口居中 UV（以路口中心为基准旋转）</summary>
    public static Vector2 JunctionCenteredUV(Vector3 worldPos, Vector3 center, float mainAngle, float roadWidth, float uvScale)
    {
        Vector3 local = worldPos - center;
        Vector2 flat = new Vector2(local.x, local.z);
        Vector2 rotated = Rotate2D(flat, -mainAngle);
        float u = (rotated.x / roadWidth) * uvScale;
        float v = (rotated.y / roadWidth) * uvScale;
        return new Vector2(u, v);
    }

    private static Vector2 Rotate2D(Vector2 v, float rad)
    {
        float c = Mathf.Cos(rad);
        float s = Mathf.Sin(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // ── 三角形分类 ──
    /// <summary>
    /// 根据三角形中心点、所有直路边与路口信息，返回完整归属
    /// </summary>
    public static TriangleRegionInfo ClassifyTriangle(Vector3 center,
                                                      List<EdgeRoadInfo> edges,
                                                      List<JunctionInfo> junctions)
    {
        // 先检测路口多边形
        for (int j = 0; j < junctions.Count; j++)
        {
            if (PointInPolygonXZ(center, junctions[j].poly))
            {
                int sub = junctions[j].degree switch
                {
                    3 => 3,
                    4 => 4,
                    _ => 5
                };
                return new TriangleRegionInfo
                {
                    subMeshIndex = sub,
                    isJunction = true,
                    junctionIndex = j
                };
            }
        }

        // 直路边：选择距离最近的边多边形
        float minDist = float.MaxValue;
        int bestEdge = 0;
        for (int e = 0; e < edges.Count; e++)
        {
            float d = PointToPolygonDistXZ(center, edges[e].poly);
            if (d < minDist)
            {
                minDist = d;
                bestEdge = e;
            }
        }

        return new TriangleRegionInfo
        {
            subMeshIndex = edges[bestEdge].targetSubMesh,
            isJunction = false,
            edgeIndex = bestEdge
        };
    }

    // ── 几何辅助 ──
    public static bool PointInPolygonXZ(Vector3 point, Vector3[] polygon)
    {
        int n = polygon.Length;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = polygon[i].x, zi = polygon[i].z;
            float xj = polygon[j].x, zj = polygon[j].z;
            if ((zi > point.z) != (zj > point.z) &&
                point.x < (xj - xi) * (point.z - zi) / (zj - zi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static float PointToPolygonDistXZ(Vector3 point, Vector3[] polygon)
    {
        float min = float.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % polygon.Length];
            float d = DistPointSegmentXZ(point, a, b);
            if (d < min) min = d;
        }
        return min;
    }

    private static float DistPointSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        float abX = b.x - a.x, abZ = b.z - a.z;
        float apX = p.x - a.x, apZ = p.z - a.z;
        float bpX = p.x - b.x, bpZ = p.z - b.z;
        float abLenSq = abX * abX + abZ * abZ;
        if (abLenSq < 0.0001f)
            return Mathf.Sqrt(apX * apX + apZ * apZ);
        float t = Mathf.Clamp01((apX * abX + apZ * abZ) / abLenSq);
        float projX = a.x + t * abX;
        float projZ = a.z + t * abZ;
        float dx = p.x - projX;
        float dz = p.z - projZ;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ── 路口主方向角计算（改进的平均角度法） ──
    /// <summary>
    /// 计算路口纹理主方向角（弧度），基于所有相邻道路方向的角度平均值。
    /// 避免了向量平均在对称方向上抵消为零的问题。
    /// </summary>
    public static float ComputeJunctionMainAngle(Vector3 nodePos, List<Vector3> neighborPositions)
    {
        if (neighborPositions == null || neighborPositions.Count == 0)
            return 0f;

        float sumSin = 0f, sumCos = 0f;
        foreach (var nbPos in neighborPositions)
        {
            Vector3 dir = (nbPos - nodePos).normalized;
            float angle = Mathf.Atan2(dir.x, dir.z);
            sumSin += Mathf.Sin(angle);
            sumCos += Mathf.Cos(angle);
        }

        return Mathf.Atan2(sumSin, sumCos); // 已自动归一化
    }
}