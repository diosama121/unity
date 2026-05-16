using UnityEngine;
using System.Collections.Generic;

public static class SplineMath
{
    public static Vector3 EvaluateHermite(float t, Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
    }

    public static Vector3 EvaluateCatmullRom(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    public static Vector3 EvaluateCentripetalCatmullRom(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float alpha = 0.5f;
        float t0 = 0f;
        float t1 = t0 + Mathf.Pow(Vector3.Distance(p0, p1), alpha);
        float t2 = t1 + Mathf.Pow(Vector3.Distance(p1, p2), alpha);
        float t3 = t2 + Mathf.Pow(Vector3.Distance(p2, p3), alpha);

        float u = Mathf.InverseLerp(t1, t2, t1 + t * (t2 - t1));

        Vector3 A1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
        Vector3 A2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
        Vector3 A3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;

        Vector3 B1 = (t2 - u) / (t2 - t0) * A1 + (u - t0) / (t2 - t0) * A2;
        Vector3 B2 = (t3 - u) / (t3 - t1) * A2 + (u - t1) / (t3 - t1) * A3;

        return (t2 - u) / (t2 - t1) * B1 + (u - t1) / (t2 - t1) * B2;
    }

    public static Vector3 GetSplineTangent(float t, Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1)
    {
        float t2 = t * t;
        return (-6 * t2 + 6 * t) * p0 + (-3 * t2 + 4 * t - 1) * m0 + (6 * t2 - 6 * t) * p1 + (3 * t2 - 2 * t) * m1;
    }

    public static List<Vector3> SampleSpline(List<Vector3> controlPoints, int samplesPerSegment, bool useCentripetal = false)
    {
        List<Vector3> points = new List<Vector3>();
        if (controlPoints.Count < 2) return points;

        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            int p0 = Mathf.Max(0, i - 1);
            int p1 = i;
            int p2 = i + 1;
            int p3 = Mathf.Min(controlPoints.Count - 1, i + 2);

            for (int j = 0; j <= samplesPerSegment; j++)
            {
                float t = (float)j / samplesPerSegment;
                Vector3 pos;

                if (useCentripetal)
                    pos = EvaluateCentripetalCatmullRom(t, controlPoints[p0], controlPoints[p1], controlPoints[p2], controlPoints[p3]);
                else
                    pos = EvaluateCatmullRom(t, controlPoints[p0], controlPoints[p1], controlPoints[p2], controlPoints[p3]);

                points.Add(pos);
            }
        }

        return points;
    }

    public static float CalculateSplineLength(List<Vector3> splinePoints)
    {
        float length = 0f;
        for (int i = 0; i < splinePoints.Count - 1; i++)
        {
            length += Vector3.Distance(splinePoints[i], splinePoints[i + 1]);
        }
        return length;
    }

    public static List<float> PrecomputeCumulativeLengths(List<Vector3> splinePoints)
    {
        List<float> lengths = new List<float> { 0 };
        float accumulated = 0f;

        for (int i = 0; i < splinePoints.Count - 1; i++)
        {
            accumulated += Vector3.Distance(splinePoints[i], splinePoints[i + 1]);
            lengths.Add(accumulated);
        }

        return lengths;
    }

    public static float GetTFromLength(List<Vector3> splinePoints, float targetLength)
    {
        List<float> lengths = PrecomputeCumulativeLengths(splinePoints);
        float totalLength = lengths[lengths.Count - 1];

        if (totalLength <= 0 || targetLength <= 0) return 0f;
        if (targetLength >= totalLength) return 1f;

        int index = lengths.BinarySearch(targetLength);
        if (index < 0) index = ~index;

        if (index == 0) return 0f;
        if (index >= lengths.Count) return 1f;

        float prevLength = lengths[index - 1];
        float segmentLength = lengths[index] - prevLength;
        float segmentT = (targetLength - prevLength) / segmentLength;

        float totalSegments = splinePoints.Count - 1;
        return ((index - 1) + segmentT) / totalSegments;
    }

    public static Vector3[] SweepSplineToQuad(SplinePoint a, SplinePoint b, float width)
    {
        float halfWidth = width * 0.5f;

        Vector3 leftA = a.Pos - a.Normal * halfWidth;
        Vector3 rightA = a.Pos + a.Normal * halfWidth;
        Vector3 leftB = b.Pos - b.Normal * halfWidth;
        Vector3 rightB = b.Pos + b.Normal * halfWidth;

        return new Vector3[] { leftA, rightA, rightB, leftB };
    }

    public static List<Vector3[]> SweepSplineToQuads(List<SplinePoint> spline, float width)
    {
        List<Vector3[]> quads = new List<Vector3[]>();
        for (int i = 0; i < spline.Count - 1; i++)
        {
            quads.Add(SweepSplineToQuad(spline[i], spline[i + 1], width));
        }
        return quads;
    }
}

public struct SplinePoint
{
    public Vector3 Pos;
    public Vector3 Tangent;
    public Vector3 Normal;
}