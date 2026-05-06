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

    private WorldModel _worldModel; // 缓存单例引用（避免频繁 Instance 查找）

    void Awake() => _worldModel = WorldModel.Instance;

    /// <summary>
    /// 【离散路径】仅返回拓扑节点ID序列（a4 基础契约）
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
    /// 【平滑路径】返回世界坐标序列（含样条线插值，可直接驱动车辆运动）
    /// </summary>
      public List<Vector3> FindSmoothPath(Vector3 startPos, Vector3 targetPos)
    {
        // 1. 获取离散节点路径
        List<int> discretePath = FindDiscretePath(startPos, targetPos);
        if (discretePath == null || discretePath.Count < 2) 
        {
            Debug.LogWarning("[PathPlanner] 离散路径无效，无法生成平滑轨迹");
            return null;
        }

        // 2. 构建控制点序列（关键修正：保留原始Y坐标）
        List<Vector3> controlPoints = new List<Vector3>();
        controlPoints.Add(startPos); // 起点（保留原始高度）
        
        foreach (int nodeId in discretePath)
        {
            RoadNode node = _worldModel.GetNode(nodeId);
            if (node != null) controlPoints.Add(node.WorldPos); // 保留原始Y值
        }
        
        controlPoints.Add(targetPos); // 终点（保留原始高度）

        // 3. 【核心修正】正确使用 CatmullRomSpline（不再调用不存在的 Generate）
        CatmullRomSpline spline = new CatmullRomSpline(controlPoints, useCentripetal: false);
        
        // 4. 手动采样生成平滑路径（适配现有 API）
        List<Vector3> smoothPath = new List<Vector3>();
        int totalSegments = controlPoints.Count - 1; // 控制点间的段数
        int pointsPerSegment = 10; // 每段生成10个点（可配置）
        
        for (int i = 0; i < totalSegments; i++)
        {
            for (int j = 0; j <= pointsPerSegment; j++)
            {
                // 计算全局归一化参数 t ∈ [0,1]
                float globalT = (i + (float)j / pointsPerSegment) / totalSegments;
                smoothPath.Add(spline.GetPoint(globalT));
            }
        }
        return smoothPath;
    }

    /// <summary>
    /// A* 核心实现（仅操作节点ID，完全依赖 a4 接口）
    /// </summary>
    private List<int> RunAStar(int startId, int targetId)
    {
        Dictionary<int, PathNode> openSet = new Dictionary<int, PathNode>();
        Dictionary<int, PathNode> closedSet = new Dictionary<int, PathNode>();
        
        // 初始化起点
        PathNode startNode = new PathNode(startId);
        startNode.GCost = 0;
        startNode.HCost = HeuristicCost(startId, targetId); // 使用统一启发式
        openSet.Add(startId, startNode);

        while (openSet.Count > 0)
        {
            // 从 openSet 中取出 FCost 最低的节点
            PathNode currentNode = null;
            foreach (var node in openSet.Values)
            {
                if (currentNode == null || node.FCost < currentNode.FCost)
                    currentNode = node;
            }

            if (currentNode.NodeId == targetId)
                return ReconstructPath(currentNode);

            openSet.Remove(currentNode.NodeId);
            closedSet.Add(currentNode.NodeId, currentNode);

            // 【关键修正】使用 NeighborIds（非 ConnectedNodeIds）
            RoadNode currentRoadNode = _worldModel.GetNode(currentNode.NodeId);
            foreach (int neighborId in currentRoadNode.NeighborIds)
            {
                if (closedSet.ContainsKey(neighborId)) continue;

                // 100% 依赖 a4 的边代价接口
                float edgeCost = _worldModel.GetEdgeCost(currentNode.NodeId, neighborId);
                if (edgeCost >= float.MaxValue) continue; // 跳过不可达边

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

    /// <summary>
    /// 统一启发式函数（与 a4 的物理距离计算逻辑对齐）
    /// </summary>
    private float HeuristicCost(int fromId, int toId)
    {
        RoadNode from = _worldModel.GetNode(fromId);
        RoadNode to = _worldModel.GetNode(toId);
        return (from != null && to != null) 
            ? Vector3.Distance(from.WorldPos, to.WorldPos) 
            : float.MaxValue;
    }

    private List<int> ReconstructPath(PathNode endNode)
    {
        List<int> path = new List<int>();
        PathNode currentNode = endNode;
        
        while (currentNode.ParentId != -1)
        {
            path.Add(currentNode.NodeId);
            currentNode = new PathNode(currentNode.ParentId);
        }
        path.Add(currentNode.NodeId);
        path.Reverse();
        return path;
    }
}