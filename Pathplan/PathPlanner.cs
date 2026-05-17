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

    /// <summary>
    /// 离散路径（拓扑节点ID序列）
    /// </summary>
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

    /// <summary>
    /// 平滑路径（含样条插值，可直接驱动车辆）
    /// </summary>
    public List<Vector3> FindSmoothPath(Vector3 startPos, Vector3 targetPos)
    {
        List<int> discretePath = FindDiscretePath(startPos, targetPos);
        if (discretePath == null || discretePath.Count < 2)
        {
            Debug.LogWarning("[PathPlanner] 离散路径无效，无法生成平滑轨迹");
            return null;
        }

        // 构建控制点序列
        List<Vector3> controlPoints = new List<Vector3> { startPos };
        foreach (int nodeId in discretePath)
        {
            RoadNode node = _worldModel.GetNode(nodeId);
            if (node != null) controlPoints.Add(node.WorldPos);
        }
        controlPoints.Add(targetPos);

        CatmullRomSpline spline = new CatmullRomSpline(controlPoints, useCentripetal: false);

        List<Vector3> smoothPath = new List<Vector3>();
        int totalSegments = controlPoints.Count - 1;
        int pointsPerSegment = 10;
        for (int i = 0; i < totalSegments; i++)
        {
            for (int j = 0; j <= pointsPerSegment; j++)
            {
                float globalT = (i + (float)j / pointsPerSegment) / totalSegments;
                smoothPath.Add(spline.GetPoint(globalT));
            }
        }
        return smoothPath;
    }

    /// <summary>
    /// 【新增接口】直接返回样条曲线对象，供自动驾驶系统(A3)追踪使用
    /// </summary>
    public CatmullRomSpline PlanPathSpline(Vector3 startPos, Vector3 targetPos)
    {
        List<int> discretePath = FindDiscretePath(startPos, targetPos);
        if (discretePath == null || discretePath.Count < 2) return null;

        List<Vector3> controlPoints = new List<Vector3> { startPos };
        foreach (int nodeId in discretePath)
        {
            RoadNode node = _worldModel.GetNode(nodeId);
            if (node != null) controlPoints.Add(node.WorldPos);
        }
        controlPoints.Add(targetPos);

        return new CatmullRomSpline(controlPoints, useCentripetal: false);
    }

    /// <summary>
    /// A* 核心实现
    /// </summary>
    private List<int> RunAStar(int startId, int targetId)
    {
        Dictionary<int, PathNode> openSet = new Dictionary<int, PathNode>();
        Dictionary<int, PathNode> closedSet = new Dictionary<int, PathNode>();

        PathNode startNode = new PathNode(startId);
        startNode.GCost = 0;
        startNode.HCost = HeuristicCost(startId, targetId);
        openSet.Add(startId, startNode);

        while (openSet.Count > 0)
        {
            PathNode currentNode = null;
            foreach (var node in openSet.Values)
            {
                if (currentNode == null || node.FCost < currentNode.FCost)
                    currentNode = node;
            }

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
                        Debug.LogError("[PathPlanner] 路径重建失败：找不到父节点");
                        return null;
                    }
                }
                path.Add(node.NodeId);
                path.Reverse();
                return path;
            }

            openSet.Remove(currentNode.NodeId);
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