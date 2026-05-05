using UnityEngine;
using System.Collections.Generic;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoadNetworkGenerator : MonoBehaviour
{
    [Header("=== 路网尺寸 ===")]
    public int gridWidth = 5;
    public int gridHeight = 5;
    public float cellSize = 80f;

    [Header("=== 随机偏移 ===")]
    public float randomOffset = 5f;
    public int seed = 42;
    public float countrysideHeightScale = 5f;

    [Range(0f, 0.4f)]
    public float connectionRemoveRate = 0.1f;

    [Header("=== 生成控制 ===")]
    public bool generateOnStart = true;
    public bool autoLinkPathPlanner = true;
    public bool showRuntimeUI = true;

    [Header("=== 环境模式 ===")]
    [Tooltip("勾选为乡村(起伏地形无高楼)，取消勾选为城市(纯平地形+高楼)")]
    public bool isCountryside = false;

    [Header("=== 可视化 ===")]
    public bool showGizmos = true;
    public Color nodeColor = Color.yellow;
    public Color edgeColor = Color.white;
    public float nodeSphereSize = 1f;

    // =============================================
    // 数据结构：Road优先
    // =============================================

    public class WaypointNode
    {
        public int id;
        public Vector3 position;
        public List<int> neighbors = new List<int>();
        public GameObject gizmoObject;
    }

    // 道路段定义（先有路，再有节点）
    public class RoadSegment
    {
        public Vector3 start;
        public Vector3 end;
        public int startNodeId;
        public int endNodeId;
    }

    [HideInInspector] public List<WaypointNode> nodes = new List<WaypointNode>();
    [HideInInspector] public List<(int, int)> edges = new List<(int, int)>();
    [HideInInspector] public List<RoadSegment> roadSegments = new List<RoadSegment>();

    private int[,] grid;

    // UI


    public PathPlanner pathPlanner;

    // =============================================
    // 生命周期
    // =============================================

    public void Generate()
    {
        // 初始化随机种子，确保可重复生成
        UnityEngine.Random.InitState(seed);

        // 1. 清空现有数据
        nodes.Clear();
        edges.Clear();
        roadSegments.Clear();

        // 2. 生成网格节点
        for (int row = 0; row < gridHeight; row++)
        {
            for (int col = 0; col < gridWidth; col++)
            {
                // 基础坐标：列 -> X轴，行 -> Z轴（即世界坐标 XZ 平面）
                float baseX = col * cellSize;
                float baseZ = row * cellSize;

                // 随机偏移
                float offsetX = UnityEngine.Random.Range(-randomOffset, randomOffset);
                float offsetZ = UnityEngine.Random.Range(-randomOffset, randomOffset);

                Vector3 position = new Vector3(baseX + offsetX, 0f, baseZ + offsetZ);

                WaypointNode node = new WaypointNode
                {
                    id = nodes.Count,
                    position = position,
                    neighbors = new List<int>()
                };
                nodes.Add(node);
            }
        }

        // 3. 建立边（四邻域：右、下）
        for (int row = 0; row < gridHeight; row++)
        {
            for (int col = 0; col < gridWidth; col++)
            {
                int currentId = row * gridWidth + col;

                // 连接右边节点
                if (col < gridWidth - 1)
                {
                    int rightId = row * gridWidth + (col + 1);
                    AddEdge(currentId, rightId);
                }

                // 连接下方节点
                if (row < gridHeight - 1)
                {
                    int downId = (row + 1) * gridWidth + col;
                    AddEdge(currentId, downId);
                }
            }
        }

        // 4. 随机移除部分边（维护连通性不做保证，仅用于视觉多样性）
        if (connectionRemoveRate > 0f && edges.Count > 0)
        {
            int removeCount = Mathf.RoundToInt(edges.Count * connectionRemoveRate);
            if (removeCount > 0)
            {
                // 打乱现有边列表的索引
                List<int> edgeIndices = new List<int>(edges.Count);
                for (int i = 0; i < edges.Count; i++) edgeIndices.Add(i);
                ShuffleList(edgeIndices);

                // 移除前 removeCount 条边
                for (int i = 0; i < removeCount; i++)
                {
                    int idx = edgeIndices[i];
                    var edge = edges[idx];
                    RemoveEdge(edge.Item1, edge.Item2);
                }

                // 清理 edges 中被标记为无效的条目（在 RemoveEdge 中已经移除，但为了安全重新构建 edges 列表）
                edges.RemoveAll(e => !nodes[e.Item1].neighbors.Contains(e.Item2));
            }
        }

        // 5. 构建 RoadSegment 列表（便于后续道路生成）
        foreach (var edge in edges)
        {
            RoadSegment seg = new RoadSegment
            {
                start = nodes[edge.Item1].position,
                end = nodes[edge.Item2].position,
                startNodeId = edge.Item1,
                endNodeId = edge.Item2
            };
            roadSegments.Add(seg);
        }

        // 6. （可选）自动连接 PathPlanner —— 保留原有逻辑但不强制依赖
        if (autoLinkPathPlanner && pathPlanner != null)
        {
          //待实现。
        }

        Debug.Log($"[RoadNetworkGenerator] 路网生成完毕：节点 {nodes.Count}，边 {edges.Count}");
    }

    // =============================================
    // 内部辅助
    // =============================================

    private void AddEdge(int a, int b)
    {
        // 避免重复与自连
        if (a == b) return;
        if (nodes[a].neighbors.Contains(b)) return;

        nodes[a].neighbors.Add(b);
        nodes[b].neighbors.Add(a);

        // 边列表保持规范（a < b）
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        edges.Add((min, max));
    }

    private void RemoveEdge(int a, int b)
    {
        nodes[a].neighbors.Remove(b);
        nodes[b].neighbors.Remove(a);

        // 从 edges 列表中移除对应条目
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        edges.RemoveAll(e => e.Item1 == min && e.Item2 == max);
    }

    // 洗牌算法（Fisher-Yates）
    private void ShuffleList(List<int> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randomIndex = UnityEngine.Random.Range(i, list.Count);
            int temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    // =============================================
    // 其余逻辑（保留原样，未改动）
    // =============================================

    void Start()
    {
        if (generateOnStart) Generate();
    }

    void OnValidate()
    {
        if (autoLinkPathPlanner && pathPlanner == null)
            pathPlanner = GetComponent<PathPlanner>();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmos || nodes == null || nodes.Count == 0) return;

        Gizmos.color = nodeColor;
        foreach (var node in nodes)
        {
            Gizmos.DrawSphere(node.position, nodeSphereSize);
        }

        Gizmos.color = edgeColor;
        foreach (var edge in edges)
        {
            Gizmos.DrawLine(nodes[edge.Item1].position, nodes[edge.Item2].position);
        }
    }
#endif
}