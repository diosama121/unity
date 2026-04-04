using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 路径规划器 - A* 算法实现
/// 功能：将场景道路抽象为图结构，使用 A* 算法计算最优路径
/// </summary>
public class PathPlanner : MonoBehaviour
{
    [Header("路网配置")]
    [Tooltip("路点预制体（空物体，用于标记路网节点）")]
    public GameObject waypointPrefab;
    
    [Tooltip("是否显示路网（调试用）")]
    public bool showRoadNetwork = true;

    [Header("路径规划结果")]
    public List<Vector3> currentPath = new List<Vector3>();
    public float totalPathLength = 0f;

    // 路网图结构
    private Dictionary<int, RoadNode> roadNetwork = new Dictionary<int, RoadNode>();
    private int nextNodeId = 0;

    [System.Serializable]
    public class RoadNode
    {
        public int id;
        public Vector3 position;
        public List<int> neighbors = new List<int>();  // 相邻节点ID
        public Dictionary<int, float> edgeCosts = new Dictionary<int, float>();  // 到邻居的代价

        // A* 算法用
        public float gCost = float.MaxValue;  // 从起点到当前节点的实际代价
        public float hCost = 0f;              // 当前节点到目标的启发式代价
        public float fCost => gCost + hCost;  // 总代价
        public int parentId = -1;

        public RoadNode(int id, Vector3 position)
        {
            this.id = id;
            this.position = position;
        }
    }

    void Start()
    {
        // 初始化时可以自动构建路网
        // BuildRoadNetworkFromScene();
    }

    // ========== 路网构建 ==========

    /// <summary>
    /// 手动添加路点
    /// </summary>
    public int AddWaypoint(Vector3 position)
    {
        int id = nextNodeId++;
        RoadNode node = new RoadNode(id, position);
        roadNetwork[id] = node;
        
        Debug.Log($"添加路点 {id} 于位置 {position}");
        return id;
    }

    /// <summary>
    /// 连接两个路点（双向）
    /// </summary>
    public void ConnectWaypoints(int nodeA, int nodeB, float cost = -1f)
    {
        if (!roadNetwork.ContainsKey(nodeA) || !roadNetwork.ContainsKey(nodeB))
        {
            Debug.LogError("节点不存在！");
            return;
        }

        // 如果未指定代价，使用欧几里得距离
        if (cost < 0)
        {
            cost = Vector3.Distance(roadNetwork[nodeA].position, roadNetwork[nodeB].position);
        }

        // 双向连接。
        if (!roadNetwork[nodeA].neighbors.Contains(nodeB))
        {
            roadNetwork[nodeA].neighbors.Add(nodeB);
            roadNetwork[nodeA].edgeCosts[nodeB] = cost;
        }

        if (!roadNetwork[nodeB].neighbors.Contains(nodeA))
        {
            roadNetwork[nodeB].neighbors.Add(nodeA);
            roadNetwork[nodeB].edgeCosts[nodeA] = cost;
        }

        Debug.Log($"连接节点 {nodeA} 和 {nodeB}，代价 {cost:F2}");
    }

    /// <summary>
    /// 从场景中自动检测路点（通过 Tag="Waypoint"）
    /// </summary>
    public void BuildRoadNetworkFromScene()
    {
        roadNetwork.Clear();
        nextNodeId = 0;

        GameObject[] waypoints = GameObject.FindGameObjectsWithTag("Waypoint");
        
        // 添加所有路点
        foreach (GameObject wp in waypoints)
        {
            AddWaypoint(wp.transform.position);
        }

        // 自动连接相邻路点（基于距离阈值）
        float connectionThreshold = 50f;  // 50米内自动连接
        
        foreach (var nodeA in roadNetwork.Values)
        {
            foreach (var nodeB in roadNetwork.Values)
            {
                if (nodeA.id >= nodeB.id) continue;  // 避免重复连接

                float distance = Vector3.Distance(nodeA.position, nodeB.position);
                if (distance < connectionThreshold)
                {
                    ConnectWaypoints(nodeA.id, nodeB.id, distance);
                }
            }
        }

        Debug.Log($"路网构建完成：{roadNetwork.Count} 个节点");
    }

    // ========== A* 路径规划 ==========

    /// <summary>
    /// A* 算法计算从起点到终点的最优路径
    /// </summary>
    public List<Vector3> PlanPath(Vector3 startPos, Vector3 goalPos)
    {
        // 找到最近的起点和终点节点
        int startNodeId = FindNearestNode(startPos);
        int goalNodeId = FindNearestNode(goalPos);

        if (startNodeId == -1 || goalNodeId == -1)
        {
            Debug.LogError("无法找到起点或终点节点！");
            return new List<Vector3>();
        }

        return PlanPath(startNodeId, goalNodeId);
    }

    /// <summary>
    /// A* 算法（节点ID版本）
    /// </summary>
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

        // 开放列表和关闭列表
        List<int> openList = new List<int>();
        HashSet<int> closedList = new HashSet<int>();

        // 初始化起点
        RoadNode startNode = roadNetwork[startNodeId];
        startNode.gCost = 0f;
        startNode.hCost = Vector3.Distance(startNode.position, roadNetwork[goalNodeId].position);
        openList.Add(startNodeId);

        while (openList.Count > 0)
        {
            // 选择 fCost 最小的节点
            int currentId = openList.OrderBy(id => roadNetwork[id].fCost).First();
            RoadNode currentNode = roadNetwork[currentId];

            // 到达目标
            if (currentId == goalNodeId)
            {
                return ReconstructPath(goalNodeId);
            }

            openList.Remove(currentId);
            closedList.Add(currentId);

            // 遍历邻居
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
    /// 重构路径（从终点回溯到起点）
    /// </summary>
    private List<Vector3> ReconstructPath(int goalNodeId)
    {
        List<Vector3> path = new List<Vector3>();
        int currentId = goalNodeId;
        float length = 0f;

        while (currentId != -1)
        {
            path.Add(roadNetwork[currentId].position);
            
            int parentId = roadNetwork[currentId].parentId;
            if (parentId != -1)
            {
                length += Vector3.Distance(roadNetwork[currentId].position, roadNetwork[parentId].position);
            }
            
            currentId = parentId;
        }

        path.Reverse();  // 反转，使其从起点到终点
        
        currentPath = path;
        totalPathLength = length;
        
        Debug.Log($"路径规划成功：{path.Count} 个路点，总长度 {length:F2} 米");
        return path;
    }

    /// <summary>
    /// 找到离指定位置最近的节点
    /// </summary>
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

    // ========== 可视化 ==========

    void OnDrawGizmos()
    {
        if (!showRoadNetwork) return;

        // 绘制所有节点
        Gizmos.color = Color.blue;
        foreach (var node in roadNetwork.Values)
        {
            Gizmos.DrawSphere(node.position, 1f);
        }

        // 绘制所有边
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

        // 绘制当前规划的路径
        if (currentPath.Count > 1)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < currentPath.Count - 1; i++)
            {
                Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
                Gizmos.DrawSphere(currentPath[i], 1.5f);
            }
            Gizmos.DrawSphere(currentPath[currentPath.Count - 1], 1.5f);
        }
    }

    // ========== 公共接口 ==========

    public Dictionary<int, RoadNode> GetRoadNetwork()
    {
        return roadNetwork;
    }

    public List<Vector3> GetCurrentPath()
    {
        return currentPath;
    }
    public void ResetNetwork()
{
    roadNetwork.Clear();
    currentPath.Clear();
    nextNodeId = 0;
    totalPathLength = 0f;
}
}
