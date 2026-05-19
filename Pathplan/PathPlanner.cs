using System.Collections.Generic;
using UnityEngine;

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

    private WorldModel _worldModel => WorldModel.Instance;

    public List<int> FindDiscretePath(Vector3 startPos, Vector3 targetPos)
    {
        RoadNode startNode = _worldModel.GetNearestNode(startPos);
        RoadNode targetNode = _worldModel.GetNearestNode(targetPos);

        if (startNode == null || targetNode == null)
        {
            Debug.LogWarning("[PathPlanner] 无法在路网中找到有效的起点或终点！");
            return null;
        }

        return RunAStar(startNode.Id, targetNode.Id);
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

    public CatmullRomSpline PlanPathSpline(Vector3 startPos, Vector3 targetPos)
    {
        List<int> discretePath = FindDiscretePath(startPos, targetPos);
        if (discretePath == null || discretePath.Count < 2) return null;

        List<Vector3> controlPoints = new List<Vector3>();

        for (int i = 0; i < discretePath.Count - 1; i++)
        {
            int currId = discretePath[i];
            int nextId = discretePath[i + 1];

            string edgeKey = Mathf.Min(currId, nextId) + "_" + Mathf.Max(currId, nextId);

            if (_worldModel.GlobalSplineCache.TryGetValue(edgeKey, out var cachedSpline))
            {
                bool reverse = (currId > nextId);

                for (int j = 0; j < cachedSpline.Count; j++)
                {
                    int idx = reverse ? (cachedSpline.Count - 1 - j) : j;
                    Vector3 pt = cachedSpline[idx].Pos;

                    if (controlPoints.Count > 0 && Vector3.Distance(controlPoints[controlPoints.Count - 1], pt) < 0.1f)
                        continue;

                    controlPoints.Add(pt);
                }
            }
            else
            {
                RoadNode node = _worldModel.GetNode(currId);
                if (node != null && (controlPoints.Count == 0 || Vector3.Distance(controlPoints[controlPoints.Count - 1], node.WorldPos) > 0.1f))
                {
                    controlPoints.Add(node.WorldPos);
                }
            }
        }

        RoadNode lastNode = _worldModel.GetNode(discretePath[discretePath.Count - 1]);
        if (lastNode != null && (controlPoints.Count == 0 || Vector3.Distance(controlPoints[controlPoints.Count - 1], lastNode.WorldPos) > 0.1f))
        {
            controlPoints.Add(lastNode.WorldPos);
        }

        if (controlPoints.Count < 2) return null;

        return new CatmullRomSpline(controlPoints, useCentripetal: false);
    }

    private List<int> RunAStar(int startId, int targetId)
    {
        Dictionary<int, PathNode> openSet = new Dictionary<int, PathNode>();
        Dictionary<int, PathNode> closedSet = new Dictionary<int, PathNode>();

        PathNode startNode = new PathNode(startId);
        startNode.GCost = 0;
        startNode.HCost = HeuristicCost(startId, targetId);
        openSet.Add(startId, startNode);

        PathNode cachedBest = null;
        int cachedBestId = -1;

        while (openSet.Count > 0)
        {
            PathNode currentNode;
            if (cachedBest != null && openSet.ContainsKey(cachedBestId))
            {
                currentNode = cachedBest;
            }
            else
            {
                currentNode = null;
                foreach (var kvp in openSet)
                {
                    if (currentNode == null || kvp.Value.FCost < currentNode.FCost)
                    {
                        currentNode = kvp.Value;
                        cachedBestId = kvp.Key;
                    }
                }
                cachedBest = currentNode;
            }

            openSet.Remove(currentNode.NodeId);
            cachedBest = null;

            if (currentNode.NodeId == targetId)
            {
                List<int> path = new List<int>();
                PathNode node = currentNode;
                while (node.ParentId != -1)
                {
                    path.Add(node.NodeId);
                    if (!closedSet.TryGetValue(node.ParentId, out node) &&
                        !openSet.TryGetValue(node.ParentId, out node))
                    {
                        Debug.LogError($"[PathPlanner] 路径重建断裂！节点 {node.NodeId} 找不到父节点 {node.ParentId}。");
                        // 【修复 Bug 3】不要直接 return null，跳出循环，把已找回的半截合法路径返回
                        break;
                    }
                }
                path.Add(node.NodeId);
                path.Reverse();
                
                if (path.Count < 2)
                {
                    // 【修复】起终点过近（同一节点），兜底：复制一份伪目标保证调用方不死
                    if (path.Count == 1)
                    {
                        Debug.LogWarning($"[PathPlanner] 起终点过近，使用兜底单节点路径");
                        path.Add(path[0]);
                    }
                    else
                    {
                        Debug.LogWarning("[PathPlanner] 路径长度不足，返回 null");
                        return null;
                    }
                }
                
                return path;
            }

            closedSet.Add(currentNode.NodeId, currentNode);

            RoadNode currentRoadNode = _worldModel.GetNode(currentNode.NodeId);
            foreach (int neighborId in currentRoadNode.NeighborIds)
            {
                if (closedSet.ContainsKey(neighborId)) continue;

                float edgeCost = _worldModel.GetEdgeCost(currentNode.NodeId, neighborId);
                if (edgeCost >= float.MaxValue) continue;

                float tentativeG = currentNode.GCost + edgeCost;

                if (!openSet.TryGetValue(neighborId, out PathNode neighborNode))
                {
                    neighborNode = new PathNode(neighborId);
                    neighborNode.HCost = HeuristicCost(neighborId, targetId);
                    openSet.Add(neighborId, neighborNode);
                }

                if (tentativeG < neighborNode.GCost)
                {
                    neighborNode.ParentId = currentNode.NodeId;
                    neighborNode.GCost = tentativeG;
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
}