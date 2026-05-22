using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 将 CatmullRom 样条曲线烘焙为离散折线。用物理距离查点，避免导数扭曲。
/// 用于 SimpleAutoDrive 的轨迹跟随。
/// </summary>
public class BakedTrajectory
{
    public List<Vector3> Points = new List<Vector3>();
    public float TotalLength;
    private List<float> accumulatedDistances = new List<float>();

    /// <summary>将样条曲线按 stepDistance 步长烘焙为密集折线点。</summary>
    public BakedTrajectory(CatmullRomSpline spline, float stepDistance = 0.5f)
    {
        TotalLength = 0f;
        accumulatedDistances.Add(0f);
        Points.Add(spline.GetPoint(0f));

        int samples = Mathf.Max(10, Mathf.CeilToInt(spline.TotalLength / stepDistance));
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 pt = spline.GetPoint(t);
            float dist = Vector3.Distance(Points[i - 1], pt);
            TotalLength += dist;
            accumulatedDistances.Add(TotalLength);
            Points.Add(pt);
        }
    }

    /// <summary>根据物理前进距离（米），插值获取精确坐标。</summary>
    public Vector3 GetPointAtDistance(float dist)
    {
        if (dist <= 0) return Points[0];
        if (dist >= TotalLength) return Points[Points.Count - 1];

        for (int i = 0; i < accumulatedDistances.Count - 1; i++)
        {
            if (dist >= accumulatedDistances[i] && dist <= accumulatedDistances[i + 1])
            {
                float segmentLen = accumulatedDistances[i + 1] - accumulatedDistances[i];
                if (segmentLen == 0) return Points[i];
                float t = (dist - accumulatedDistances[i]) / segmentLen;
                return Vector3.Lerp(Points[i], Points[i + 1], t);
            }
        }
        return Points[Points.Count - 1];
    }

    /// <summary>根据物理距离获取折线方向（避免样条导数扭曲）。</summary>
    public Vector3 GetTangentAtDistance(float dist)
    {
        if (dist <= 0) return (Points[1] - Points[0]).normalized;
        if (dist >= TotalLength) return (Points[Points.Count - 1] - Points[Points.Count - 2]).normalized;

        for (int i = 0; i < accumulatedDistances.Count - 1; i++)
        {
            if (dist >= accumulatedDistances[i] && dist <= accumulatedDistances[i + 1])
            {
                return (Points[i + 1] - Points[i]).normalized;
            }
        }
        return (Points[Points.Count - 1] - Points[Points.Count - 2]).normalized;
    }
}