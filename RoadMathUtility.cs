using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class RoadMathUtility
{
    public static List<Vector3> SortAroundCenter(Vector3 center, List<Vector3> ring)
    {
        return ring.OrderBy(v => Mathf.Atan2(v.z - center.z, v.x - center.x)).ToList();
    }

    // 【核心修复】：加上默认参数 roadWidth = 6f，彻底解决报错！
    public static List<SplinePoint> GetRoadSpline(int nodeIdA, int nodeIdB, float stepDistance = 0.5f, float roadWidth = 6f)
    {
        List<SplinePoint> points = new List<SplinePoint>();
        WorldModel wm = WorldModel.Instance;
        if (wm == null) return points;

        (Vector3 p0, Vector3 tangentA) = wm.GetNodeData(nodeIdA);
        (Vector3 p1, Vector3 tangentB) = wm.GetNodeData(nodeIdB);

        p0.y = wm.GetUnifiedHeight(p0.x, p0.z) + 0.2f;
        p1.y = wm.GetUnifiedHeight(p1.x, p1.z) + 0.2f;

        float dist = Vector3.Distance(p0, p1);
        if (dist <= 0.1f) return points;

        Vector3 m0 = tangentA * dist * 0.5f;
        Vector3 m1 = tangentB * dist * 0.5f;

        float actualStep = Mathf.Clamp(stepDistance, 0.1f, 0.5f);
        int steps = Mathf.Max(2, Mathf.CeilToInt(dist / actualStep));
        float deltaT = 1f / steps;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float t2 = t * t, t3 = t2 * t;
            
            Vector3 pos = (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
            
            // 动态路宽平台化
            pos.y = GetPlatformHeight(wm, pos, p0, p1, roadWidth);

            float t_prev = Mathf.Max(0f, t - deltaT);
            float t_next = Mathf.Min(1f, t + deltaT);

            Vector3 pos_prev = EvaluateHermite(t_prev, p0, m0, p1, m1);
            pos_prev.y = GetPlatformHeight(wm, pos_prev, p0, p1, roadWidth);

            Vector3 pos_next = EvaluateHermite(t_next, p0, m0, p1, m1);
            pos_next.y = GetPlatformHeight(wm, pos_next, p0, p1, roadWidth);

            Vector3 tangent = (pos_next - pos_prev).normalized;
            if (tangent.sqrMagnitude < 0.001f) tangent = (p1 - p0).normalized;

            Vector3 sweepTangent = new Vector3(tangent.x, 0, tangent.z).normalized;
            Vector3 normal = Vector3.Cross(Vector3.up, sweepTangent).normalized;

            points.Add(new SplinePoint { Pos = pos, Tangent = tangent, Normal = normal });
        }
        return points;
    }

    // 根据真实路宽计算平台大小
    private static float GetPlatformHeight(WorldModel wm, Vector3 pos, Vector3 p0, Vector3 p1, float roadWidth)
    {
        float trueY = wm.GetUnifiedHeight(pos.x, pos.z) + 0.2f;
        float d0 = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(p0.x, p0.z));
        float d1 = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(p1.x, p1.z));

        float flatRadius = roadWidth * 0.7f; 
        float blendZone = roadWidth * 1.2f;  

        if (d0 < flatRadius) return p0.y;
        if (d0 < flatRadius + blendZone) return Mathf.Lerp(p0.y, trueY, Mathf.SmoothStep(0, 1, (d0 - flatRadius) / blendZone));

        if (d1 < flatRadius) return p1.y;
        if (d1 < flatRadius + blendZone) return Mathf.Lerp(p1.y, trueY, Mathf.SmoothStep(0, 1, (d1 - flatRadius) / blendZone));

        return trueY;
    }

    public static List<Vector3[]> SweepSplineToQuads(List<SplinePoint> spline, float roadWidth)
    {
        List<Vector3[]> quads = new List<Vector3[]>();
        float halfW = roadWidth * 0.5f;

        for (int i = 0; i < spline.Count - 1; i++)
        {
            SplinePoint pA = spline[i]; SplinePoint pB = spline[i + 1];
            Vector3 leftA = pA.Pos - pA.Normal * halfW; Vector3 rightA = pA.Pos + pA.Normal * halfW;
            Vector3 leftB = pB.Pos - pB.Normal * halfW; Vector3 rightB = pB.Pos + pB.Normal * halfW;

            leftA.y = pA.Pos.y; rightA.y = pA.Pos.y;
            leftB.y = pB.Pos.y; rightB.y = pB.Pos.y;

            quads.Add(new Vector3[] { leftA, rightA, rightB, leftB });
        }
        return quads;
    }

    private static Vector3 EvaluateHermite(float t, Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
    }
}