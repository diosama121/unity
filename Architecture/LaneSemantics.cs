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
    public LaneDirection Direction;
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