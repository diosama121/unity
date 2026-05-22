using System.Collections.Generic;
using UnityEngine;

/// <summary>基于 A* 的路网路径规划器，在 Lane 级别拓扑图上搜索边序列路径。</summary>
public class PathPlanner : MonoBehaviour
{
    private class PathNode
    {
        public int NodeId;
        public int ParentId;
        public float GCost;
        public float HCost;
        public float FCost => GCost + HCost;

        public PathNode(int id)
        {
            NodeId = id;
            ParentId = -1;
            GCost = float.MaxValue;
            HCost = 0;
        }
    }

    private class PriorityQueue<T>
    {
        private List<(T item, float priority)> elements = new List<(T, float)>();
        public int Count => elements.Count;

        public void Enqueue(T item, float priority)
        {
            elements.Add((item, priority));
            int ci = elements.Count - 1;
            while (ci > 0)
            {
                int pi = (ci - 1) / 2;
                if (elements[ci].priority >= elements[pi].priority) break;
                var tmp = elements[ci]; elements[ci] = elements[pi]; elements[pi] = tmp;
                ci = pi;
            }
        }

        public T Dequeue()
        {
            int li = elements.Count - 1;
            T frontItem = elements[0].item;
            elements[0] = elements[li];
            elements.RemoveAt(li);
            --li;
            int pi = 0;
            while (true)
            {
                int ci = pi * 2 + 1;
                if (ci > li) break;
                int rc = ci + 1;
                if (rc <= li && elements[rc].priority < elements[ci].priority) ci = rc;
                if (elements[pi].priority <= elements[ci].priority) break;
                var tmp = elements[pi]; elements[pi] = elements[ci]; elements[ci] = tmp;
                pi = ci;
            }
            return frontItem;
        }
    }

    private WorldModel _worldModel => WorldModel.Instance;

    public List<int> FindDiscretePath(Vector3 startPos, Vector3 targetPos, Vector3? startForward = null)
    {
        RoadNode startNode = GetLogicalStartNode(startPos, startForward);
        RoadNode targetNode = _worldModel.GetNearestNode(targetPos);

        if (startNode == null || targetNode == null)
        {
            Debug.LogWarning("[PathPlanner] 无法在路网中找到有效的起点或终点！");
            return null;
        }

        return RunAStar(startNode.Id, targetNode.Id);
    }

    private RoadNode GetLogicalStartNode(Vector3 startPos, Vector3? forward)
    {
        if (forward.HasValue)
        {
            int laneId = _worldModel.FindNearestLane(startPos);
            if (laneId >= 0 && _worldModel.GlobalLanes.TryGetValue(laneId, out Lane lane))
            {
                int nodeAId = lane.RoadId / 10000;
                int nodeBId = lane.RoadId % 10000;
                RoadNode nodeA = _worldModel.GetNode(nodeAId);
                RoadNode nodeB = _worldModel.GetNode(nodeBId);

                if (nodeA != null && nodeB != null)
                {
                    Vector3 dirAToB = (nodeB.WorldPos - nodeA.WorldPos).normalized;
                    if (Vector3.Dot(dirAToB, forward.Value) >= 0)
                        return nodeB;
                    else
                        return nodeA;
                }
            }
        }
        return _worldModel.GetNearestNode(startPos);
    }

    public List<Vector3> FindSmoothPath(Vector3 startPos, Vector3 targetPos)
    {
        List<int> discretePath = FindDiscretePath(startPos, targetPos);
        if (discretePath == null || discretePath.Count < 2)
        {
            Debug.LogWarning("[PathPlanner] 离散路径无效，无法生成平滑轨迹");
            return null;
        }

        List<Vector3> controlPoints = new List<Vector3> { startPos };
        foreach (int nodeId in discretePath)
        {
            RoadNode node = _worldModel.GetNode(nodeId);
            if (node != null) controlPoints.Add(node.WorldPos);
        }
        controlPoints.Add(targetPos);

        CatmullRomSpline spline = new CatmullRomSpline(controlPoints, useCentripetal: false);

        List<Vector3> smoothPath = new List<Vector3> { spline.GetPoint(0f) };

        int totalSegments = controlPoints.Count - 1;
        int pointsPerSegment = 10;
        for (int i = 0; i < totalSegments; i++)
        {
            for (int j = 1; j <= pointsPerSegment; j++)
            {
                float globalT = (i + (float)j / pointsPerSegment) / totalSegments;
                smoothPath.Add(spline.GetPoint(globalT));
            }
        }
        return smoothPath;
    }

    public CatmullRomSpline PlanPathSpline(Vector3 startPos, Vector3 targetPos, Vector3? startForward = null)
    {
        List<int> discretePath = FindDiscretePath(startPos, targetPos, startForward);
        if (discretePath == null || discretePath.Count < 2) return null;

        List<Vector3> controlPoints = new List<Vector3>();

        // 起点对齐
        controlPoints.Add(startPos);
        if (startForward.HasValue)
            controlPoints.Add(startPos + startForward.Value * 3f);

        for (int i = 0; i < discretePath.Count - 1; i++)
        {
            int currId = discretePath[i];
            int nextId = discretePath[i + 1];

            // 获取正确的偏置车道
            Lane lane = _worldModel.GetLaneByNodeFlow(currId, nextId);
            if (lane != null && lane.CenterSpline != null)
            {
                // 加入车道的控制点（剔除重合点防止打结）
                foreach (var pt in lane.CenterSpline.ControlPoints)
                {
                    if (controlPoints.Count == 0 || Vector3.Distance(controlPoints[controlPoints.Count - 1], pt) > 0.1f)
                        controlPoints.Add(pt);
                }

                // 如果没到终点，插入路口转向器 (Connector) 的控制点
                if (i < discretePath.Count - 2)
                {
                    int nextNextId = discretePath[i + 2];
                    Lane nextLane = _worldModel.GetLaneByNodeFlow(nextId, nextNextId);
                    if (nextLane != null)
                    {
                        LaneConnector connector = _worldModel.GetConnector(lane.LaneId, nextLane.LaneId);
                        if (connector != null && connector.TurnCurve != null)
                        {
                            foreach (var pt in connector.TurnCurve.ControlPoints)
                            {
                                if (Vector3.Distance(controlPoints[controlPoints.Count - 1], pt) > 0.1f)
                                    controlPoints.Add(pt);
                            }
                        }
                    }
                }
            }
            else
            {
                // 异常回退兜底
                var fallbackPts = GetCachedPoints(currId, nextId);
                foreach (var pt in fallbackPts)
                {
                    if (controlPoints.Count == 0 || Vector3.Distance(controlPoints[controlPoints.Count - 1], pt) > 0.1f)
                        controlPoints.Add(pt);
                }
            }
        }

        return new CatmullRomSpline(controlPoints, useCentripetal: false);
    }

    private List<Vector3> GetCachedPoints(int currId, int nextId)
    {
        List<Vector3> result = new List<Vector3>();

        string edgeKey = Mathf.Min(currId, nextId) + "_" + Mathf.Max(currId, nextId);

        if (_worldModel.GlobalSplineCache.TryGetValue(edgeKey, out var cachedSpline))
        {
            bool reverse = (currId > nextId);
            for (int j = 0; j < cachedSpline.Count; j++)
            {
                int idx = reverse ? (cachedSpline.Count - 1 - j) : j;
                result.Add(cachedSpline[idx].Pos);
            }
        }
        else
        {
            RoadNode node = _worldModel.GetNode(nextId);
            if (node != null)
                result.Add(node.WorldPos);
        }

        return result;
    }

    private List<int> RunAStar(int startId, int targetId)
    {
        var openQueue = new PriorityQueue<int>();
        var closedSet = new HashSet<int>();
        var nodeRecords = new Dictionary<int, PathNode>();

        PathNode startNode = new PathNode(startId) { GCost = 0, HCost = HeuristicCost(startId, targetId) };
        nodeRecords[startId] = startNode;
        openQueue.Enqueue(startId, startNode.FCost);

        while (openQueue.Count > 0)
        {
            int currentId = openQueue.Dequeue();

            if (closedSet.Contains(currentId)) continue;

            if (currentId == targetId)
            {
                List<int> path = new List<int>();
                int curr = currentId;
                while (curr != -1)
                {
                    path.Add(curr);
                    if (!nodeRecords.TryGetValue(curr, out PathNode record))
                    {
                        Debug.LogError($"[PathPlanner] 路径回溯中断！丢失节点: {curr}");
                        break;
                    }
                    curr = record.ParentId;
                }
                path.Reverse();

                if (path.Count == 1)
                {
                    Debug.LogWarning("[PathPlanner] 起终点过近，使用兜底单节点路径");
                    path.Add(path[0]);
                }
                return path;
            }

            closedSet.Add(currentId);
            PathNode currentNode = nodeRecords[currentId];

            RoadNode currentRoadNode = _worldModel.GetNode(currentId);
            if (currentRoadNode == null || currentRoadNode.NeighborIds == null) continue;

            foreach (int neighborId in currentRoadNode.NeighborIds)
            {
                if (closedSet.Contains(neighborId)) continue;

                float edgeCost = _worldModel.GetEdgeCost(currentId, neighborId);
                if (edgeCost >= float.MaxValue) continue;

                float tentativeG = currentNode.GCost + edgeCost;

                if (!nodeRecords.TryGetValue(neighborId, out PathNode neighborNode))
                {
                    neighborNode = new PathNode(neighborId);
                    neighborNode.HCost = HeuristicCost(neighborId, targetId);
                    nodeRecords[neighborId] = neighborNode;
                }

                if (tentativeG < neighborNode.GCost)
                {
                    neighborNode.ParentId = currentId;
                    neighborNode.GCost = tentativeG;
                    openQueue.Enqueue(neighborId, neighborNode.FCost);
                }
            }
        }

        Debug.LogWarning("[PathPlanner] 无法找到有效路径！");
        return null;
    }

    private float HeuristicCost(int fromId, int toId)
    {
        RoadNode from = _worldModel.GetNode(fromId);
        RoadNode to = _worldModel.GetNode(toId);
        return (from != null && to != null)
            ? Vector3.Distance(from.WorldPos, to.WorldPos)
            : float.MaxValue;
    }

    // ==========================================
    // A* 边序列路径规划（车道图）
    // 节点 = LaneId，边 = Connector 连接
    // 返回: +ve = LaneId, -ve = -ConnectorId 交替序列
    // ==========================================
    private class EdgePathNode
    {
        public int LaneId;
        public int ParentLaneId;
        public int ViaConnectorId;   // 从 ParentLaneId 到达本 LaneId 所经过的 ConnectorId
        public float GCost;
        public float HCost;
        public float FCost => GCost + HCost;

        public EdgePathNode(int laneId)
        {
            LaneId = laneId;
            ParentLaneId = -1;
            ViaConnectorId = -1;
            GCost = float.MaxValue;
            HCost = 0;
        }
    }

    public List<int> PlanEdgePath(int startLaneId, int endLaneId)
    {
        if (_worldModel == null)
        {
            Debug.LogWarning("[PathPlanner] WorldModel 不可用");
            return null;
        }

        if (!_worldModel.GlobalLanes.ContainsKey(startLaneId) || !_worldModel.GlobalLanes.ContainsKey(endLaneId))
        {
            Debug.LogWarning($"[PathPlanner] 车道ID无效: start={startLaneId}, end={endLaneId}");
            return null;
        }

        var openQueue = new PriorityQueue<int>();
        var closedSet = new HashSet<int>();
        var nodeRecords = new Dictionary<int, EdgePathNode>();

        EdgePathNode startNode = new EdgePathNode(startLaneId) { GCost = 0, HCost = LaneHeuristic(startLaneId, endLaneId) };
        nodeRecords[startLaneId] = startNode;
        openQueue.Enqueue(startLaneId, startNode.FCost);

        while (openQueue.Count > 0)
        {
            int currentId = openQueue.Dequeue();
            if (closedSet.Contains(currentId)) continue;

            if (currentId == endLaneId)
            {
                // 回溯构建路径: LaneId → -ConnectorId → LaneId → ...
                var result = new List<int>();
                int curr = currentId;
                while (curr != -1)
                {
                    if (!nodeRecords.TryGetValue(curr, out EdgePathNode record)) break;
                    result.Add(curr); // LaneId (正)
                    if (record.ViaConnectorId >= 0)
                        result.Add(-(record.ViaConnectorId + 1)); // ConnectorId 编码为负 (偏移1以区分 -0)
                    curr = record.ParentLaneId;
                }
                result.Reverse();
                return result;
            }

            closedSet.Add(currentId);
            EdgePathNode currentNode = nodeRecords[currentId];

            if (!_worldModel.GlobalLanes.TryGetValue(currentId, out Lane currentLane)) continue;
            if (currentLane.NextConnectorIds == null || currentLane.NextConnectorIds.Count == 0) continue;

            float currentLaneLen = (currentLane.CenterSpline != null) ? currentLane.CenterSpline.TotalLength : 0f;

            foreach (int connId in currentLane.NextConnectorIds)
            {
                if (!_worldModel.GlobalConnectors.TryGetValue(connId, out LaneConnector connector)) continue;
                int nextLaneId = connector.ToLaneId;
                if (closedSet.Contains(nextLaneId)) continue;
                if (!_worldModel.GlobalLanes.ContainsKey(nextLaneId)) continue;

                float connLen = (connector.TurnCurve != null) ? connector.TurnCurve.TotalLength : 0f;
                float edgeCost = currentLaneLen + connLen;
                if (edgeCost >= float.MaxValue) continue;

                float tentativeG = currentNode.GCost + edgeCost;

                if (!nodeRecords.TryGetValue(nextLaneId, out EdgePathNode neighborNode))
                {
                    neighborNode = new EdgePathNode(nextLaneId);
                    neighborNode.HCost = LaneHeuristic(nextLaneId, endLaneId);
                    nodeRecords[nextLaneId] = neighborNode;
                }

                if (tentativeG < neighborNode.GCost)
                {
                    neighborNode.ParentLaneId = currentId;
                    neighborNode.ViaConnectorId = connId;
                    neighborNode.GCost = tentativeG;
                    openQueue.Enqueue(nextLaneId, neighborNode.FCost);
                }
            }
        }

        // 路径不存在，静默返回 null（由调用方统一处理无路径情况）
        return null;
    }

    private float LaneHeuristic(int laneIdA, int laneIdB)
    {
        Lane laneA = _worldModel.GlobalLanes.GetValueOrDefault(laneIdA);
        Lane laneB = _worldModel.GlobalLanes.GetValueOrDefault(laneIdB);
        if (laneA?.CenterSpline == null || laneB?.CenterSpline == null) return float.MaxValue;

        Vector3 midA = laneA.CenterSpline.GetPoint(0.5f);
        Vector3 midB = laneB.CenterSpline.GetPoint(0.5f);
        return Vector3.Distance(midA, midB);
    }
}