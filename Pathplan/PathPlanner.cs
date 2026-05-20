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
}