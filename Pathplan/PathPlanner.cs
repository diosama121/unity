using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Splines; // 引入 Splines 命名空间

/// <summary>
/// 路径规划器 - A* 算法实现 (支持样条线后处理)
/// </summary>
public class PathPlanner : MonoBehaviour
{
    [Header("路网配置")]
    public GameObject waypointPrefab;
    public bool showRoadNetwork = true;

    [Header("路径平滑配置")]
    [Tooltip("路径插值采样密度：数值越大，路径点越密集，贴合度越高")]
    public int splineSampleCount = 10; 

    [Header("路径规划结果")]
    public List<Vector3> currentPath = new List<Vector3>();
    public float totalPathLength = 0f;

    private Dictionary<int, RoadNode> roadNetwork = new Dictionary<int, RoadNode>();
    private int nextNodeId = 0;

    [System.Serializable]
    public class RoadNode
    {
        public int id;
        public Vector3 position;
        public List<int> neighbors = new List<int>(); 
        public Dictionary<int, float> edgeCosts = new Dictionary<int, float>(); 
        
        // 【新增】存储到邻居节点的 SplineContainer 引用
        public Dictionary<int, SplineContainer> edgeSplines = new Dictionary<int, SplineContainer>(); 

        public float gCost = float.MaxValue; 
        public float hCost = 0f;
        public float fCost => gCost + hCost; 
        public int parentId = -1;

        public RoadNode(int id, Vector3 position)
        {
            this.id = id;
            this.position = position;
        }
    }

    // ========== 路网构建 ==========

    public int AddWaypoint(Vector3 position)
    {
        int id = nextNodeId++;
        RoadNode node = new RoadNode(id, position);
        roadNetwork[id] = node;
        return id;
    }

    /// <summary>
    /// 连接两个路点，并保存它们之间的 Spline 信息
    /// </summary>
    public void ConnectWaypoints(int nodeA, int nodeB, float cost = -1f, SplineContainer spline = null)
    {
        if (!roadNetwork.ContainsKey(nodeA) || !roadNetwork.ContainsKey(nodeB))
        {
            Debug.LogError("节点不存在！");
            return;
        }

        if (cost < 0)
        {
            cost = Vector3.Distance(roadNetwork[nodeA].position, roadNetwork[nodeB].position);
        }

        // 双向连接
        if (!roadNetwork[nodeA].neighbors.Contains(nodeB))
        {
            roadNetwork[nodeA].neighbors.Add(nodeB);
            roadNetwork[nodeA].edgeCosts[nodeB] = cost;
            roadNetwork[nodeA].edgeSplines[nodeB] = spline; // 保存 A->B 的样条线
        }

        if (!roadNetwork[nodeB].neighbors.Contains(nodeA))
        {
            roadNetwork[nodeB].neighbors.Add(nodeA);
            roadNetwork[nodeB].edgeCosts[nodeA] = cost;
            roadNetwork[nodeB].edgeSplines[nodeA] = spline; // 保存 B->A 的样条线
        }
    }

    // (BuildRoadNetworkFromScene 方法保持原有逻辑，但在自动连接时建议传入对应的 SplineContainer)

    // ========== A* 路径规划 ==========

    public List<Vector3> PlanPath(Vector3 startPos, Vector3 goalPos)
    {
        int startNodeId = FindNearestNode(startPos);
        int goalNodeId = FindNearestNode(goalPos);

        if (startNodeId == -1 || goalNodeId == -1)
        {
            Debug.LogError("无法找到起点或终点节点！");
            return new List<Vector3>();
        }

        return PlanPath(startNodeId, goalNodeId);
    }

    public List<Vector3> PlanPath(int startNodeId, int goalNodeId)
    {
        if (!roadNetwork.ContainsKey(startNodeId) || !roadNetwork.ContainsKey(goalNodeId))
        {
            Debug.LogError("起点或终点节点不存在！");
            return new List<Vector3>();
        }

        // 重置所有节点状态
        foreach (var node in roadNetwork.Values)
        {
            node.gCost = float.MaxValue;
            node.hCost = 0f;
            node.parentId = -1;
        }

        List<int> openList = new List<int>();
        HashSet<int> closedList = new HashSet<int>();

        RoadNode startNode = roadNetwork[startNodeId];
        startNode.gCost = 0f;
        startNode.hCost = Vector3.Distance(startNode.position, roadNetwork[goalNodeId].position);
        openList.Add(startNodeId);

        while (openList.Count > 0)
        {
            int currentId = openList.OrderBy(id => roadNetwork[id].fCost).First();
            RoadNode currentNode = roadNetwork[currentId];

            if (currentId == goalNodeId)
            {
                // 【核心修改】使用带样条线插值的重构方法
                return ReconstructPathWithSpline(goalNodeId);
            }

            openList.Remove(currentId);
            closedList.Add(currentId);

            foreach (int neighborId in currentNode.neighbors)
            {
                if (closedList.Contains(neighborId)) continue;

                RoadNode neighbor = roadNetwork[neighborId];
                float tentativeGCost = currentNode.gCost + currentNode.edgeCosts[neighborId];

                if (tentativeGCost < neighbor.gCost)
                {
                    neighbor.parentId = currentId;
                    neighbor.gCost = tentativeGCost;
                    neighbor.hCost = Vector3.Distance(neighbor.position, roadNetwork[goalNodeId].position);

                    if (!openList.Contains(neighborId))
                    {
                        openList.Add(neighborId);
                    }
                }
            }
        }

        Debug.LogWarning("未找到路径！");
        return new List<Vector3>();
    }

    /// <summary>
    /// 【新增】重构路径并进行样条线密集采样
    /// </summary>
    private List<Vector3> ReconstructPathWithSpline(int goalNodeId)
    {
        List<Vector3> smoothPath = new List<Vector3>();
        List<int> nodeIdPath = new List<int>();
        
        // 1. 先回溯得到节点ID序列
        int currentId = goalNodeId;
        while (currentId != -1)
        {
            nodeIdPath.Add(currentId);
            currentId = roadNetwork[currentId].parentId;
        }
        nodeIdPath.Reverse(); // 此时为：起点 -> ... -> 终点

        if (nodeIdPath.Count < 2) return smoothPath;

        float totalLength = 0f;

        // 2. 遍历每一段路段，进行样条线采样
        for (int i = 0; i < nodeIdPath.Count - 1; i++)
        {
            int fromId = nodeIdPath[i];
            int toId = nodeIdPath[i + 1];
            
            RoadNode fromNode = roadNetwork[fromId];
            RoadNode toNode = roadNetwork[toId];

            // 获取这段路对应的 SplineContainer
            SplineContainer container = fromNode.edgeSplines[toId];

            if (container != null && container.Spline != null && container.Spline.Count > 0)
            {
                Spline spline = container.Spline;
                
                // 获取该路段在 Spline 中的索引 (假设是双向连接的第一个有效路段)
                // 实际项目中，建议在 ConnectWaypoints 时直接记录 splineIndex
                int splineIndex = 0; 
                
                // 计算方向一致性：检查 Spline 的起点是否更接近 fromNode
                Vector3 splineStart = spline[0].Position;
                Vector3 splineEnd = spline[spline.Count - 1].Position;
                
                bool isForward = Vector3.Distance(splineStart, fromNode.position) < Vector3.Distance(splineEnd, fromNode.position);

                // 密集采样
                for (int s = 0; s < splineSampleCount; s++)
                {
                    float t = (float)s / (splineSampleCount - 1);
                    
                    // 根据方向调整 t 值
                    float evalT = isForward ? t : (1 - t);
                    
                    Vector3 sampledPos = container.EvaluatePosition(splineIndex, evalT);
                    
                    // 避免重复点（上一段的终点是下一段的起点）
                    if (smoothPath.Count > 0 && Vector3.Distance(smoothPath[smoothPath.Count - 1], sampledPos) < 0.01f)
                        continue;
                        
                    smoothPath.Add(sampledPos);
                }
                
                // 累加真实曲线长度（近似）
                totalLength += Vector3.Distance(fromNode.position, toNode.position); 
            }
            else
            {
                // 如果没有样条线，退化为直线连接
                smoothPath.Add(fromNode.position);
                totalLength += Vector3.Distance(fromNode.position, toNode.position);
            }
        }

        // 确保添加终点
        smoothPath.Add(roadNetwork[nodeIdPath[nodeIdPath.Count - 1]].position);

        currentPath = smoothPath;
        totalPathLength = totalLength;
        
        Debug.Log($"路径规划成功（样条线版）：{smoothPath.Count} 个密集采样点，总长度 {totalLength:F2} 米");
        return smoothPath;
    }

    // (FindNearestNode, OnDrawGizmos, GetRoadNetwork 等其他方法保持不变)
    private int FindNearestNode(Vector3 position)
    {
        if (roadNetwork.Count == 0) return -1;
        int nearestId = -1;
        float minDistance = float.MaxValue;
        foreach (var node in roadNetwork.Values)
        {
            float distance = Vector3.Distance(position, node.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestId = node.id;
            }
        }
        return nearestId;
    }

    void OnDrawGizmos()
    {
        if (!showRoadNetwork) return;
        Gizmos.color = Color.blue;
        foreach (var node in roadNetwork.Values)
        {
            Gizmos.DrawSphere(node.position, 1f);
        }
        Gizmos.color = Color.cyan;
        foreach (var node in roadNetwork.Values)
        {
            foreach (int neighborId in node.neighbors)
            {
                if (roadNetwork.ContainsKey(neighborId))
                {
                    Gizmos.DrawLine(node.position, roadNetwork[neighborId].position);
                }
            }
        }
        if (currentPath.Count > 1)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < currentPath.Count - 1; i++)
            {
                Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
            }
        }
    }

    public Dictionary<int, RoadNode> GetRoadNetwork() { return roadNetwork; }
    public List<Vector3> GetCurrentPath() { return currentPath; }
    public void ResetNetwork()
    {
        roadNetwork.Clear();
        currentPath.Clear();
        nextNodeId = 0;
        totalPathLength = 0f;
    }
}