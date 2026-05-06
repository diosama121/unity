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

      public static List<SplinePoint> GetRoadSpline(int nodeIdA, int nodeIdB, float stepDistance = 2f)
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
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float t2 = t * t, t3 = t2 * t;
            float h00 = 2 * t3 - 3 * t2 + 1;
            float h10 = t3 - 2 * t2 + t;
            float h01 = -2 * t3 + 3 * t2;
            float h11 = t3 - t2;

            Vector3 pos = h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;

            Vector3 tangent = (
                (6 * t2 - 6 * t) * p0 +
                (3 * t2 - 4 * t + 1) * m0 +
                (-6 * t2 + 6 * t) * p1 +
                (3 * t2 - 2 * t) * m1
            ).normalized;

            if (tangent.sqrMagnitude < 0.01f) tangent = (p1 - p0).normalized;
            Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;

            points.Add(new SplinePoint { Pos = pos, Tangent = tangent, Normal = normal });
        }
        return points;
    }

    /// 样条扫掠：将连续两个 SplinePoint 扩展为四边形
    public static List<Vector3[]> SweepSplineToQuads(List<SplinePoint> spline, float roadWidth)
    {
        List<Vector3[]> quads = new List<Vector3[]>();
        float halfW = roadWidth * 0.5f;

        for (int i = 0; i < spline.Count - 1; i++)
        {
            SplinePoint pA = spline[i];
            SplinePoint pB = spline[i + 1];

            Vector3 leftA  = pA.Pos - pA.Normal * halfW;
            Vector3 rightA = pA.Pos + pA.Normal * halfW;
            Vector3 leftB  = pB.Pos - pB.Normal * halfW;
            Vector3 rightB = pB.Pos + pB.Normal * halfW;

            // 强制 Y 轴跟随样条点（绝对高程）
            leftA.y  = pA.Pos.y; rightA.y = pA.Pos.y;
            leftB.y  = pB.Pos.y; rightB.y = pB.Pos.y;

            quads.Add(new Vector3[] { leftA, rightA, rightB, leftB });
        }
        return quads;
    }

}