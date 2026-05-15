using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class RoadMathUtility
{
    public static List<Vector3> SortAroundCenter(Vector3 center, List<Vector3> ring)
    {
        return ring.OrderBy(v => Mathf.Atan2(v.z - center.z, v.x - center.x)).ToList();
    }

    public static List<SplinePoint> GetRoadSpline(int nodeIdA, int nodeIdB, float stepDistance = 0.5f, float roadWidth = 6f)
    {
        List<SplinePoint> points = new List<SplinePoint>();
        WorldModel wm = WorldModel.Instance;
        if (wm == null) return points;

        (Vector3 p0, Vector3 tangentA) = wm.GetNodeData(nodeIdA);
        (Vector3 p1, Vector3 tangentB) = wm.GetNodeData(nodeIdB);

        // 获取两端绝对高度
        p0.y = wm.GetUnifiedHeight(p0.x, p0.z) + 0.2f;
        p1.y = wm.GetUnifiedHeight(p1.x, p1.z) + 0.2f;

        float dist = Vector3.Distance(p0, p1);
        if (dist <= 0.1f) return points;

        // 【核心修复：防打结限幅】
        // 切线的强度绝对不能超过节点间距的 35%，这是防止曲线自交（乱飞的三角）的黄金法则！
        float maxTangentMag = dist * 0.35f; 
        Vector3 m0 = tangentA * Mathf.Min(dist * 0.5f, maxTangentMag);
        Vector3 m1 = tangentB * Mathf.Min(dist * 0.5f, maxTangentMag);

        float actualStep = Mathf.Clamp(stepDistance, 0.1f, 0.5f);
        int steps = Mathf.Max(2, Mathf.CeilToInt(dist / actualStep));
        float deltaT = 1f / steps;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float t2 = t * t, t3 = t2 * t;
            
            // 基础曲线位置（纯数学插值，不产生抖动）
            Vector3 pos = (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
            pos.y = wm.GetUnifiedHeight(pos.x, pos.z) + 0.2f;

            float t_prev = Mathf.Max(0f, t - deltaT);
            float t_next = Mathf.Min(1f, t + deltaT);

            Vector3 pos_prev = EvaluateHermite(t_prev, p0, m0, p1, m1);
            pos_prev.y = wm.GetUnifiedHeight(pos_prev.x, pos_prev.z) + 0.2f;

            Vector3 pos_next = EvaluateHermite(t_next, p0, m0, p1, m1);
            pos_next.y = wm.GetUnifiedHeight(pos_next.x, pos_next.z) + 0.2f;

            Vector3 tangent = (pos_next - pos_prev).normalized;
            if (tangent.sqrMagnitude < 0.001f) tangent = (p1 - p0).normalized;

            Vector3 sweepTangent = new Vector3(tangent.x, 0, tangent.z).normalized;
            Vector3 normal = Vector3.Cross(Vector3.up, sweepTangent).normalized;

            points.Add(new SplinePoint { Pos = pos, Tangent = tangent, Normal = normal });
        }
        return points;
    }

    public static List<Vector3[]> SweepSplineToQuads(List<SplinePoint> spline, float baseRoadWidth)
    {
        List<Vector3[]> quads = new List<Vector3[]>();
        WorldModel wm = WorldModel.Instance;
        
        bool isCountry = (wm != null && wm.roadGenerator != null && wm.roadGenerator.isCountryside);

        for (int i = 0; i < spline.Count - 1; i++)
        {
            SplinePoint pA = spline[i]; 
            SplinePoint pB = spline[i + 1];

            // 【全新功能：动态路宽】
            // 如果是乡村模式，使用低频柏林噪声产生 70% ~ 130% 的路宽波动
            float widthA = baseRoadWidth;
            float widthB = baseRoadWidth;
            if (isCountry)
            {
                widthA = baseRoadWidth * (0.7f + Mathf.PerlinNoise(pA.Pos.x * 0.03f, pA.Pos.z * 0.03f) * 0.6f);
                widthB = baseRoadWidth * (0.7f + Mathf.PerlinNoise(pB.Pos.x * 0.03f, pB.Pos.z * 0.03f) * 0.6f);
            }

            float halfWA = widthA * 0.5f; 
            float halfWB = widthB * 0.5f;

            Vector3 leftA = pA.Pos - pA.Normal * halfWA; 
            Vector3 rightA = pA.Pos + pA.Normal * halfWA;
            Vector3 leftB = pB.Pos - pB.Normal * halfWB; 
            Vector3 rightB = pB.Pos + pB.Normal * halfWB;

            // 打破水平锁死，左右独立贴合地形
            if (wm != null)
            {
                leftA.y = wm.GetUnifiedHeight(leftA.x, leftA.z) + 0.2f;
                rightA.y = wm.GetUnifiedHeight(rightA.x, rightA.z) + 0.2f;
                leftB.y = wm.GetUnifiedHeight(leftB.x, leftB.z) + 0.2f;
                rightB.y = wm.GetUnifiedHeight(rightB.x, rightB.z) + 0.2f;
            }
            else
            {
                leftA.y = pA.Pos.y; rightA.y = pA.Pos.y;
                leftB.y = pB.Pos.y; rightB.y = pB.Pos.y;
            }

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