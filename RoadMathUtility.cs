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
    /// V4.1 终极规范：道路样条生成（强行剥离旧基准 + 实时上浮 + 立体切线防品红）
    /// </summary>
    public static List<SplinePoint> GetRoadSpline(int nodeIdA, int nodeIdB, float stepDistance = 0.5f)
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

        int steps = Mathf.Max(2, Mathf.CeilToInt(dist / stepDistance));
        float deltaT = 1f / steps;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float t2 = t * t, t3 = t2 * t;
            
            Vector3 pos = (2 * t3 - 3 * t2 + 1) * p0 + 
                          (t3 - 2 * t2 + t) * m0 + 
                          (-2 * t3 + 3 * t2) * p1 + 
                          (t3 - t2) * m1;
            
            // 【修复 1：强行剥离旧基准】彻底无视数学方程算出来的 pos.y
            // 【修复 2：实时采样 + 强制上浮】+ 0.2f 确保道路面片永远在地形网格上方，消灭 Z-Fighting
            pos.y = wm.GetUnifiedHeight(pos.x, pos.z) + 0.2f;

            // 【修复 3：切线高度同步（兜底防品红碎面）】
            // 绝对不能用平面的解析切线！必须利用前后点的真实 Y 坐标重新算差值重构立体切线
            float t_prev = Mathf.Max(0f, t - deltaT);
            float t_next = Mathf.Min(1f, t + deltaT);

            Vector3 pos_prev = EvaluateHermite(t_prev, p0, m0, p1, m1);
            pos_prev.y = wm.GetUnifiedHeight(pos_prev.x, pos_prev.z) + 0.2f;

            Vector3 pos_next = EvaluateHermite(t_next, p0, m0, p1, m1);
            pos_next.y = wm.GetUnifiedHeight(pos_next.x, pos_next.z) + 0.2f;

            Vector3 tangent = (pos_next - pos_prev).normalized;
            if (tangent.sqrMagnitude < 0.001f) 
                tangent = (p1 - p0).normalized;

            Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;

            points.Add(new SplinePoint 
            { 
                Pos = pos, 
                Tangent = tangent, 
                Normal = normal 
            });
        }
        return points;
    }

    /// <summary>
    /// 样条扫掠 → 四边形
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

    private static Vector3 EvaluateHermite(float t, Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + 
               (t3 - 2 * t2 + t) * m0 + 
               (-2 * t3 + 3 * t2) * p1 + 
               (t3 - t2) * m1;
    }
}
