using System.Collections.Generic;
using UnityEngine;

public enum IntersectionKind
{
    None,
    T_Junction,
    Crossroad,
    MultiWay,
    Roundabout
}

[System.Serializable]
public struct RoadProfile
{
    public float Width;
    public float HeightOffset;
    public float SidewalkWidth;
    public float ShoulderWidth;
}

[System.Serializable]
public struct SetbackEdgeData
{
    public int NodeId;
    public Vector3 Center;
    public List<Vector3> EdgeVertices;
    public float Radius;
    public IntersectionKind Kind;
}

[System.Serializable]
public struct FrenetFrame
{
    public Vector3 Origin;
    public Vector3 T;
    public Vector3 N;
    public Vector3 B;
}

// --- RoadNode extension fields ---

public partial class RoadNode
{
    public float IntersectionRadius = 0f;
    public bool IsRuralEdge = false;
    public IntersectionKind Kind = IntersectionKind.None;
    public List<int> PolarSortedNeighbors;
    public Dictionary<int, float> AngleToNextNeighbor;
}

public struct JunctionEdgeEntry
{
    public Vector3 LeftPos;
    public Vector3 RightPos;
    public Vector3 OutwardDir;
}