using System.Collections.Generic;
using UnityEngine;

public class CatmullRomSpline
{
    public List<Vector3> ControlPoints { get; private set; }
    public float TotalLength { get; private set; }
    
    private List<float> _cumulativeLengths;
    private const int SAMPLES_PER_SEGMENT = 15;
    private bool _useCentripetal;

    public CatmullRomSpline(List<Vector3> controlPoints, bool useCentripetal = false)
    {
        ControlPoints = new List<Vector3>();
        foreach (var p in controlPoints)
        {
            ControlPoints.Add(new Vector3(p.x, 0, p.z));
        }
        _useCentripetal = useCentripetal;
        BakeCurve();
    }

    private void BakeCurve()
    {
        if (ControlPoints.Count < 2)
        {
            TotalLength = 0;
            return;
        }

        _cumulativeLengths = new List<float> { 0 };
        TotalLength = 0;

        for (int i = 0; i < ControlPoints.Count - 1; i++)
        {
            Vector3 prev = GetPointOnSegment(i, 0);
            for (int j = 1; j <= SAMPLES_PER_SEGMENT; j++)
            {
                float t = j / (float)SAMPLES_PER_SEGMENT;
                Vector3 current = GetPointOnSegment(i, t);
                float segmentLength = Vector3.Distance(prev, current);
                TotalLength += segmentLength;
                _cumulativeLengths.Add(TotalLength);
                prev = current;
            }
        }
    }

    public Vector3 GetPoint(float t)
    {
        int numPoints = ControlPoints.Count;
        t = Mathf.Clamp01(t);
        
        float scaledT = t * (numPoints - 1);
        int segmentIndex = Mathf.FloorToInt(scaledT);
        
        if (segmentIndex >= numPoints - 1) 
            return ControlPoints[numPoints - 1];
        
        float localT = scaledT - segmentIndex;

        int p0 = Mathf.Max(0, segmentIndex - 1);
        int p1 = segmentIndex;
        int p2 = segmentIndex + 1;
        int p3 = Mathf.Min(numPoints - 1, segmentIndex + 2);

        if (_useCentripetal)
            return CalculateCentripetalCatmullRom(
                ControlPoints[p0], 
                ControlPoints[p1], 
                ControlPoints[p2], 
                ControlPoints[p3], 
                localT
            );
        else
            return CalculateUniformCatmullRom(
                ControlPoints[p0], 
                ControlPoints[p1], 
                ControlPoints[p2], 
                ControlPoints[p3], 
                localT
            );
    }

    private Vector3 GetPointOnSegment(int segmentIndex, float t)
    {
        int p1 = segmentIndex;
        int p2 = segmentIndex + 1;
        int p0 = Mathf.Max(0, p1 - 1);
        int p3 = Mathf.Min(ControlPoints.Count - 1, p2 + 1);

        if (_useCentripetal)
            return CalculateCentripetalCatmullRom(
                ControlPoints[p0], 
                ControlPoints[p1], 
                ControlPoints[p2], 
                ControlPoints[p3], 
                t
            );
        else
            return CalculateUniformCatmullRom(
                ControlPoints[p0], 
                ControlPoints[p1], 
                ControlPoints[p2], 
                ControlPoints[p3], 
                t
            );
    }

    private Vector3 CalculateUniformCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private Vector3 CalculateCentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
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
    
    public float GetTFromLength(float length)
    {
        if (TotalLength <= 0 || length <= 0) return 0;
        if (length >= TotalLength) return 1;
        
        float target = Mathf.Clamp(length, 0, TotalLength);
        int index = _cumulativeLengths.BinarySearch(target);
        if (index < 0) index = ~index;
        
        if (index == 0) return 0;
        if (index >= _cumulativeLengths.Count) return 1;
        
        float prevLength = _cumulativeLengths[index - 1];
        float segmentLength = _cumulativeLengths[index] - prevLength;
        float segmentT = (target - prevLength) / segmentLength;
        
        int numSegments = ControlPoints.Count - 1;
        float totalSegments = numSegments * SAMPLES_PER_SEGMENT;
        float globalT = (index - 1 + segmentT) / totalSegments;
        
        return globalT * (ControlPoints.Count - 1);
    }
}