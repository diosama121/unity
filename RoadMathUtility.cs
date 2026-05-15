using UnityEngine;
using System.Collections.Generic;

public static class RoadMathUtility
{
    public static List<SplinePoint> GetRoadSpline(int nodeIdA, int nodeIdB, float stepDistance = 0.5f, float roadWidth = 6f)
    {
        List<SplinePoint> points = new List<SplinePoint>();
        WorldModel wm = WorldModel.Instance;
        if (wm == null) return points;

        (Vector3 p0, Vector3 _) = wm.GetNodeData(nodeIdA);
        (Vector3 p1, Vector3 _) = wm.GetNodeData(nodeIdB);

        p0.y = wm.GetUnifiedHeight(p0.x, p0.z) + 0.2f;
        p1.y = wm.GetUnifiedHeight(p1.x, p1.z) + 0.2f;

        float dist = Vector3.Distance(p0, p1);
        if (dist <= 0.1f) return points;

        Vector3 edgeDir = (p1 - p0).normalized;

        float maxTangentMag = dist * 0.35f;
        Vector3 m0 = edgeDir * Mathf.Min(dist * 0.5f, maxTangentMag);
        Vector3 m1 = edgeDir * Mathf.Min(dist * 0.5f, maxTangentMag);

        float actualStep = Mathf.Clamp(stepDistance, 0.1f, 0.5f);
        int steps = Mathf.Max(2, Mathf.CeilToInt(dist / actualStep));
        float deltaT = 1f / steps;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            
            Vector3 pos = SplineMath.EvaluateHermite(t, p0, m0, p1, m1);
            pos.y = wm.GetUnifiedHeight(pos.x, pos.z) + 0.2f;

            float t_prev = Mathf.Max(0f, t - deltaT);
            float t_next = Mathf.Min(1f, t + deltaT);

            Vector3 pos_prev = SplineMath.EvaluateHermite(t_prev, p0, m0, p1, m1);
            pos_prev.y = wm.GetUnifiedHeight(pos_prev.x, pos_prev.z) + 0.2f;

            Vector3 pos_next = SplineMath.EvaluateHermite(t_next, p0, m0, p1, m1);
            pos_next.y = wm.GetUnifiedHeight(pos_next.x, pos_next.z) + 0.2f;

            Vector3 tangent = (pos_next - pos_prev).normalized;
            if (tangent.sqrMagnitude < 0.001f) tangent = (p1 - p0).normalized;

            Vector3 sweepTangent = new Vector3(tangent.x, 0, tangent.z).normalized;
            Vector3 normal = GeometryUtility.ComputeNormalFromTangent(sweepTangent);

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

    public static float GetRoadWidthAtPosition(Vector3 worldPos, float baseWidth, bool isCountryside)
    {
        if (!isCountryside)
            return baseWidth;

        return baseWidth * (0.7f + Mathf.PerlinNoise(worldPos.x * 0.03f, worldPos.z * 0.03f) * 0.6f);
    }

    public static float CalculateRoadCurvature(SplinePoint a, SplinePoint b, SplinePoint c)
    {
        Vector3 v1 = b.Pos - a.Pos;
        Vector3 v2 = c.Pos - b.Pos;

        float dot = Vector3.Dot(v1.normalized, v2.normalized);
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));

        float avgLength = (v1.magnitude + v2.magnitude) * 0.5f;
        if (avgLength < 0.001f) return 0f;

        return angle / avgLength;
    }

    public static bool IsSharpTurn(List<SplinePoint> spline, int index, float maxAngleDeg = 60f)
    {
        if (index < 1 || index >= spline.Count - 1)
            return false;

        Vector3 prevDir = (spline[index].Pos - spline[index - 1].Pos).normalized;
        Vector3 nextDir = (spline[index + 1].Pos - spline[index].Pos).normalized;

        float angle = GeometryUtility.AngleBetweenVectors(prevDir, nextDir);
        return angle > maxAngleDeg;
    }

    public static List<SplinePoint> ResampleSplineByLength(List<SplinePoint> original, float targetSegmentLength)
    {
        if (original == null || original.Count < 2)
            return original;

        List<SplinePoint> resampled = new List<SplinePoint>();
        resampled.Add(original[0]);

        float accumulated = 0f;

        for (int i = 0; i < original.Count - 1; i++)
        {
            Vector3 current = original[i].Pos;
            Vector3 next = original[i + 1].Pos;
            float segmentLength = Vector3.Distance(current, next);

            if (accumulated + segmentLength < targetSegmentLength)
            {
                accumulated += segmentLength;
                continue;
            }

            float remaining = targetSegmentLength - accumulated;
            float t = remaining / segmentLength;

            SplinePoint interpolated = new SplinePoint();
            interpolated.Pos = Vector3.Lerp(current, next, t);
            interpolated.Tangent = Vector3.Lerp(original[i].Tangent, original[i + 1].Tangent, t).normalized;
            interpolated.Normal = Vector3.Lerp(original[i].Normal, original[i + 1].Normal, t).normalized;

            resampled.Add(interpolated);
            accumulated = segmentLength - remaining;
        }

        if (!resampled.Contains(original.Last()))
            resampled.Add(original.Last());

        return resampled;
    }
}