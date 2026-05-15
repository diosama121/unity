using System.Collections.Generic;
using UnityEngine;

// ==========================================
// RoadDataStructures.cs
// 道路网通用数据结构、枚举与 RoadNode 扩展字段
// ==========================================

/// <summary>
/// 路口类型枚举。
/// 用于标识退缩处（Setback）或节点所在交叉口的几何形态。
/// </summary>
public enum IntersectionKind
{
    None,
    T_Junction,
    Crossroad,
    MultiWay,
    Roundabout
}

/// <summary>
/// 道路横断面轮廓参数。
/// 定义一条道路在垂直于切线方向上的几何尺寸。
/// </summary>
[System.Serializable]
public struct RoadProfile
{
    /// <summary>行车道总宽度（米）</summary>
    public float Width;

    /// <summary>相对地形的整体高度偏移（米），用于桥涵/立交场景</summary>
    public float HeightOffset;

    /// <summary>单侧人行道宽度（米）</summary>
    public float SidewalkWidth;

    /// <summary>单侧路肩宽度（米）</summary>
    public float ShoulderWidth;
}

/// <summary>
/// 退缩处（Setback）的几何描述。
/// 当道路在接近路口时以特定半径为退缩圆收缩路面时，记录该退缩区域的关键几何数据。
/// </summary>
[System.Serializable]
public struct SetbackEdgeData
{
    /// <summary>关联的路网节点 ID</summary>
    public int NodeId;

    /// <summary>退缩圆的圆心（世界坐标）</summary>
    public Vector3 Center;

    /// <summary>退缩处所有边缘顶点（世界坐标列表），用于生成退缩多边形/网格</summary>
    public List<Vector3> EdgeVertices;

    /// <summary>退缩圆的半径（米）</summary>
    public float Radius;

    /// <summary>该退缩处所属路口的类型</summary>
    public IntersectionKind Kind;
}

/// <summary>
/// Frenet 坐标系框架。
/// 在道路中心线上的任意一点，定义局部前进方向(T)、横向法线(N)和副法线(B)，
/// 构成一个右手正交坐标系，用于行驶轨迹规划与横向偏移计算。
/// </summary>
[System.Serializable]
public struct FrenetFrame
{
    /// <summary>Frenet 原点（世界坐标下的参考点）</summary>
    public Vector3 Origin;

    /// <summary>切向量 T：沿道路前进方向的单位向量</summary>
    public Vector3 T;

    /// <summary>法向量 N：道路横向（左为正）的单位向量</summary>
    public Vector3 N;

    /// <summary>副法向量 B：世界空间向上的单位向量（T x N）</summary>
    public Vector3 B;
}

// ==========================================
// RoadNode 扩展字段（Partial Class）
// ==========================================

public partial class RoadNode
{
    /// <summary>
    /// 路口退缩半径（米）。
    /// 仅当本节点位于路口（Type == Intersection 或 Merge）时有效；
    /// 0 表示未设置或非路口节点。
    /// </summary>
    public float IntersectionRadius = 0f;

    /// <summary>
    /// 是否为郊区/乡村道路边缘节点。
    /// true 时后续生成逻辑可采用更宽的路肩、无路缘石等乡村道路特征。
    /// </summary>
    public bool IsRuralEdge = false;

    /// <summary>
    /// 本节点所处路口的类型。
    /// 用于指导路口几何生成（T 型、十字、环岛等）。
    /// </summary>
    public IntersectionKind Kind = IntersectionKind.None;
}