using UnityEngine;
using System.Collections.Generic;

public enum TurnType { Straight, LeftTurn, RightTurn, UTurn }
public enum LaneDirection { Forward, Reverse }
// 纵向状态机（替换旧 DriveState）
public enum LongitudinalState { FreeDrive, FollowCar, Brake, Stopped, Yield, Reverse }

[System.Serializable]
public class Lane
{
    public int LaneId;
    public int RoadId;
    public CatmullRomSpline CenterSpline;
    /// <summary>车道方向</summary>
    public LaneDirection Direction;
    /// <summary>速度限制 (km/h)</summary>
    public float SpeedLimit = 50f;
    /// <summary>左侧车道（逆向车道的左侧即路中央），-1表示无</summary>
    public int LeftLaneId = -1;
    public int RightLaneId = -1;
    public List<int> NextConnectorIds = new List<int>();
}

[System.Serializable]
public class LaneConnector
{
    public int ConnectorId;
    public int JunctionId;
    public int FromLaneId;
    public int ToLaneId;
    public TurnType TurnType;
    public CatmullRomSpline TurnCurve;
    /// <summary>已离散化的Hermite多段线（绕过CatmullRom二次近似的切线误差）。</summary>
    public List<Vector3> Polyline;
}

[System.Serializable]
public class StopLine
{
    public int NodeId;
    public int LaneId;
    public Vector3 Position;
    public Vector3 Normal;
    public int AssociatedPhaseId = -1;
}

/// <summary>KD-Tree 空间索引中的车道采样点条目。</summary>
public class LaneKDEntry
{
    public Vector3 Point;
    public int LaneId;
}