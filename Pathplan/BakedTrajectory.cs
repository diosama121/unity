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
        for (int i = 1; i <=