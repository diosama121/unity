using System.Collections.Generic;
using UnityEngine;

public class SemanticPathPlanner : MonoBehaviour
{
    [Header("A* 动态权重")]
    public float headingWeight = 2.5f;
    public float dynamicWeightMin = 1.2f;
    public float dynamicWeightMax = 3.0f;

    [Header("交规惩罚")]
    public float wrongWayPenalty = 500f;
    public float laneCrossingPenalty = 200f;

    private WorldModel _wm => WorldModel.Instance;
    private RoadNetworkGenerator _rng;
    private ProceduralRoadBuilder _roadBuilder;

    void Start()
    {
        _rng = FindObjectOfType<RoadNetworkGenerator>();
        _roadBuilder = FindObjectOfType<ProceduralRoadBuilder>();
    }

    private class AStarNode
    {
        public int NodeId;
        public int ParentId;
        public float GCost;
        public float HCost;
        public float FCost => GCost + HCost;
        public AStarNode(int id) { NodeId = id; ParentId = -1; GCost = float.MaxValue; HCost = 0f; }
    }

    public List<int> FindHeuristicPath(Vector3 startPos, Vector3 targetPos, Vector3 carForward)
    {
        if (_wm == null) return null;
        RoadNode startNode = _wm.GetNearestNode(startPos);
        RoadNode targetNode = _wm.GetNearestNode(targetPos);
        if (startNode == null || targetNode == null) return null;
        return RunHeuristicAStar(startNode, targetNode, carForward);
    }

    private List<int> RunHeuristicAStar(RoadNode start, RoadNode target, Vector3 carForward)
    {
        var openSet = new Dictionary<int, AStarNode>();
        var closedSet = new Dictionary<int, AStarNode>();

        float startToTargetDist = Vector3.Distance(start.WorldPos, target.WorldPos);

        AStarNode startNode = new AStarNode(start.Id);
        startNode.GCost = 0f;
        startNode.HCost = ComputeHeuristic(start, target, carForward, startToTargetDist, startToTargetDist);
        openSet.Add(start.Id, startNode);

        int cachedBestId = -1;
        while (openSet.Count > 0)
        {
            AStarNode current = null;
            int currentId = -1;
            foreach (var kvp in openSet)
            {
                if (current == null || kvp.Value.FCost < current.FCost)
                {
                    current = kvp.Value;
                    currentId = kvp.Key;
                }
            }
            openSet.Remove(currentId);

            if (current.NodeId == target.Id)
            {
                return ReconstructPath(current, closedSet, openSet);
            }

            closedSet.Add(current.NodeId, current);
            RoadNode currentRoadNode = _wm.GetNode(current.NodeId);
            if (currentRoadNode == null) continue;

            float currentToTarget = Vector3.Distance(currentRoadNode.WorldPos, target.WorldPos);

            foreach (int neighborId in currentRoadNode.NeighborIds)
            {
                if (closedSet.ContainsKey(neighborId)) continue;

                float edgeDist = _wm.GetEdgeCost(current.NodeId, neighborId);
                if (edgeDist >= float.MaxValue) continue;

                float rulePenalty = ComputeRulePenalty(currentRoadNode, neighborId, carForward);
                float tentativeG = current.GCost + edgeDist + rulePenalty;

                if (!openSet.TryGetValue(neighborId, out AStarNode neighborNode))
                {
                    neighborNode = new AStarNode(neighborId);
                    neighborNode.HCost = ComputeHeuristic(
                        _wm.GetNode(neighborId), target, carForward, startToTargetDist, currentToTarget);
                    openSet.Add(neighborId, neighborNode);
                }

                if (tentativeG < neighborNode.GCost)
                {
                    neighborNode.ParentId = current.NodeId;
                    neighborNode.GCost = tentativeG;
                }
            }
        }

        return null;
    }

    private float ComputeHeuristic(RoadNode from, RoadNode to, Vector3 startForward,
        float startToTargetDist, float currentToTargetDist)
    {
        if (from == null || to == null) return float.MaxValue;

        float distHeuristic = Vector3.Distance(from.WorldPos, to.WorldPos);

        Vector3 toDest = (to.WorldPos - from.WorldPos);
        toDest.y = 0;
        float headingHeuristic = 0f;
        if (toDest.magnitude > 0.01f)
        {
            float angle = Vector3.Angle(startForward, toDest.normalized);
            headingHeuristic = angle * headingWeight;
        }

        float progressRatio = startToTargetDist > 0.01f
            ? 1f - Mathf.Clamp01(currentToTargetDist / startToTargetDist)
            : 0f;
        float dynamicWeight = Mathf.Lerp(dynamicWeightMin, dynamicWeightMax, progressRatio);

        return (distHeuristic + headingHeuristic) * dynamicWeight;
    }

    private float ComputeRulePenalty(RoadNode from, int toId, Vector3 carForward)
    {
        float penalty = 0f;
        RoadNode to = _wm.GetNode(toId);
        if (to == null) return penalty;

        Vector3 edgeDir = (to.WorldPos - from.WorldPos);
        edgeDir.y = 0;

        if (edgeDir.magnitude > 0.01f && from.Type == NodeType.Intersection)
        {
            float dot = Vector3.Dot(carForward.normalized, edgeDir.normalized);
            if (dot < -0.3f) penalty += wrongWayPenalty;
        }

        return penalty;
    }

    private List<int> ReconstructPath(AStarNode target, Dictionary<int, AStarNode> closed,
        Dictionary<int, AStarNode> open)
    {
        var path = new List<int>();
        AStarNode node = target;
        while (node.ParentId != -1)
        {
            path.Add(node.NodeId);
            if (!closed.TryGetValue(node.ParentId, out node) && !open.TryGetValue(node.ParentId, out node))
                return null;
        }
        path.Add(node.NodeId);
        path.Reverse();
        return path;
    }

    public CatmullRomSpline GenerateAdaptiveSpline(List<int> pathNodeIds)
    {
        if (pathNodeIds == null || pathNodeIds.Count < 2) return null;

        if (_rng == null) _rng = FindObjectOfType<RoadNetworkGenerator>();
        if (_roadBuilder == null) _roadBuilder = FindObjectOfType<ProceduralRoadBuilder>();

        bool isRural = _rng != null && _rng.isCountryside;
        float roadW = _roadBuilder != null ? _roadBuilder.roadWidth : 6f;

        float offsetDistance = isRural ? 0f : -(roadW / 4f);

        List<Vector3> adjustedPoints = new List<Vector3>();
        for (int i = 0; i < pathNodeIds.Count; i++)
        {
            RoadNode node = _wm.GetNode(pathNodeIds[i]);
            if (node == null) continue;

            Vector3 forwardDir = GetPathDirection(pathNodeIds, i);
            if (forwardDir.magnitude < 0.001f) forwardDir = Vector3.forward;

            Vector3 leftNormal = Vector3.Cross(Vector3.up, forwardDir).normalized;
            Vector3 lanePos = node.WorldPos + leftNormal * offsetDistance;
            adjustedPoints.Add(lanePos);
        }

        if (adjustedPoints.Count < 2) return null;

        return new CatmullRomSpline(adjustedPoints, useCentripetal: false);
    }

    private Vector3 GetPathDirection(List<int> pathNodeIds, int index)
    {
        if (index < pathNodeIds.Count - 1)
        {
            RoadNode a = _wm.GetNode(pathNodeIds[index]);
            RoadNode b = _wm.GetNode(pathNodeIds[index + 1]);
            if (a != null && b != null)
            {
                Vector3 dir = b.WorldPos - a.WorldPos;
                dir.y = 0;
                return dir.normalized;
            }
        }
        if (index > 0)
        {
            RoadNode a = _wm.GetNode(pathNodeIds[index - 1]);
            RoadNode b = _wm.GetNode(pathNodeIds[index]);
            if (a != null && b != null)
            {
                Vector3 dir = b.WorldPos - a.WorldPos;
                dir.y = 0;
                return dir.normalized;
            }
        }
        return Vector3.forward;
    }
}