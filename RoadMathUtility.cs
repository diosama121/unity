using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class RoadMathUtility
{
    /// <summary>
    /// 极角排序
    /// </summary>
    public static List<Vector3> SortAroundCenter(Vector3 center, List<Vector3> ring)
    {
        return ring.OrderBy(v => Mathf.Atan2(v.z - center.z, v.x - center.x)).ToList();
    }

    /// <summary>
    /// V4.1 规范：道路样条生成（高程偏移修正 + 高密采样 + 3D立体切线重构）
    /// </summary>
    public static List<SplinePoint> GetRoadSpline(int nodeIdA, int nodeIdB, float roadHeightOffset = 0f, float stepDistance = 0.5f)
    {
        List<SplinePoint> points = new List<SplinePoint>();
        WorldModel wm = WorldModel.Instance;
        if (wm == null) return points;

        (Vector3 p0, Vector3 tangentA) = wm.GetNodeData(nodeIdA);
        (Vector3 p1, Vector3 tangentB) = wm.GetNodeData(nodeIdB);

        float dist = Vector3.Distance(p0, p1);
        if (dist <= 0.1f) return points;

        Vector3 m0 = tangentA * dist * 0.5f;
        Vector3 m1 = tangentB * dist * 0.5f;

        // 【修复 2：局部精度不足】强制将默认步进降至 0.5f，让道路完美贴合高频起伏
        int steps = Mathf.Max(2, Mathf.CeilToInt(dist / stepDistance));

        // ================= 第一遍循环：锁定绝对 3D 真理坐标 =================
        List<Vector3> path3D = new List<Vector3>();
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float t2 = t * t, t3 = t2 * t;
            
            Vector3 pos = (2 * t3 - 3 * t2 + 1) * p0 + 
                          (t3 - 2 * t2 + t) * m0 + 
                          (-2 * t3 + 3 * t2) * p1 + 
                          (t3 - t2) * m1;
            
            // 【修复 1：解决总高度不对】强制高程吸附，并叠加上升基准偏移量
            pos.y = wm.GetUnifiedHeight(pos.x, pos.z) + roadHeightOffset;
            path3D.Add(pos);
        }

        // ================= 第二遍循环：重构立体切线与法线 =================
        for (int i = 0; i < path3D.Count; i++)
        {
            Vector3 tangent;
            
            // 【修复 3：解决法线与品红崩溃（最核心）】
            // 绝对不能用平面的切线去配立体的点！必须利用前后点真实 3D 坐标的差值，重构包含 Y 轴起伏的真正切线
            if (path3D.Count < 3)
                tangent = (path3D[path3D.Count - 1] - path3D[0]).normalized;
            else if (i == 0)
                tangent = (path3D[1] - path3D[0]).normalized;
            else if (i == path3D.Count - 1)
                tangent = (path3D[i] - path3D[i - 1]).normalized;
            else
                tangent = (path3D[i + 1] - path3D[i - 1]).normalized;

            // 物理防崩溃：拦截极小向量
            if (tangent.sqrMagnitude < 0.001f) 
                tangent = (p1 - p0).normalized;

            // 基于真正的 3D 切线计算横向法线，彻底解决城镇突变导致的品红面翻转
            Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;

            points.Add(new SplinePoint 
            { 
                Pos = path3D[i], 
                Tangent = tangent, 
                Normal = normal 
            });
        }
        return points;
    }

    /// <summary>
    /// 样条扫掠 → 四边形（字段规范修正版）
    /// </summary>
    public static List<Vector3[]> SweepSplineToQuads(List<SplinePoint> spline, float roadWidth)
    {
        List<Vector3[]> quads = new List<Vector3[]>();
        float halfW = roadWidth * 0.5f;

        for (int i = 0; i < spline.Count - 1; i++)
        {
            SplinePoint pA = spline[i];
            SplinePoint pB = spline[i + 1];

            Vector3 leftA = pA.Pos - pA.Normal * halfW;
            Vector3 rightA = pA.Pos + pA.Normal * halfW;
            Vector3 leftB = pB.Pos - pB.Normal * halfW;
            Vector3 rightB = pB.Pos + pB.Normal * halfW;

            leftA.y = pA.Pos.y;
            rightA.y = pA.Pos.y;
            leftB.y = pB.Pos.y;
            rightB.y = pB.Pos.y;

            quads.Add(new Vector3[] { leftA, rightA, rightB, leftB });
        }
        return quads;
    }
}
