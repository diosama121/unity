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

    [Header("=== 节点过滤 ===")]
    [Tooltip("跳过间距小于此值的节点对，防止过密导致网格爆炸")]
    public float minNodeDistance = 15f;

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

    public int nodeCount { get; internal set; }

    // =============================================
    // 生命周期
    // =============================================

    public void Generate()
    {
        // 1. 初始化与清空脏数据
        nodes.Clear();
        edges.Clear();
        roadSegments.Clear();
        
        // 【a4 核心修复】：消除命名空间歧义，绝对激活 V1.0 的种子系统！
        UnityEngine.Random.InitState(seed);

        // 2. 生成纯数学节点 (Nodes)
        for (int z = 0; z < gridHeight; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                float offsetX = UnityEngine.Random.Range(-randomOffset, randomOffset);
                float offsetZ = UnityEngine.Random.Range(-randomOffset, randomOffset);
                
                // 边缘节点不加偏移，保证路网外围是个整齐的矩形
                if (x == 0 || x == gridWidth - 1) offsetX = 0;
                if (z == 0 || z == gridHeight - 1) offsetZ = 0;

                Vector3 pos = new Vector3(x * cellSize + offsetX, 0, z * cellSize + offsetZ);
                
                WaypointNode node = new WaypointNode();
                node.id = nodes.Count; // 严格按顺序分配 ID
                node.position = pos;
                node.neighbors = new List<int>();
                nodes.Add(node);
            }
        }

        // 3. 生成拓扑连线 (Edges) 与邻居关系 (Neighbors)
        for (int z = 0; z < gridHeight; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int currentIndex = z * gridWidth + x;

                // 向右侧连线 (防越界：x < gridWidth - 1)
                if (x < gridWidth - 1)
                {
                    int rightIndex = z * gridWidth + (x + 1);
                    float dist = Vector3.Distance(nodes[currentIndex].position, nodes[rightIndex].position);
                    if (dist < minNodeDistance) continue;
                    if (UnityEngine.Random.value > connectionRemoveRate) // 随机挖空机制
                    {
                        nodes[currentIndex].neighbors.Add(rightIndex);
                        nodes[rightIndex].neighbors.Add(currentIndex);
                        edges.Add((currentIndex, rightIndex));
                    }
                }

                // 向上方连线 (防越界：z < gridHeight - 1)
                if (z < gridHeight - 1)
                {
                    int topIndex = (z + 1) * gridWidth + x;
                    float dist = Vector3.Distance(nodes[currentIndex].position, nodes[topIndex].position);
                    if (dist < minNodeDistance) continue;
                    if (UnityEngine.Random.value > connectionRemoveRate)
                    {
                        nodes[currentIndex].neighbors.Add(topIndex);
                        nodes[topIndex].neighbors.Add(currentIndex);
                        edges.Add((currentIndex, topIndex));
                    }
                }
            }
        }

        Debug.Log($"[RoadNetworkGenerator] 🟢 拓扑生成完毕! 节点数: {nodes.Count}, 边数: {edges.Count}");
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

    // 优先从真理层 WorldModel 获取坐标，实现高度同步
    bool useTruthPositions = WorldModel.Instance != null && WorldModel.Instance.NodeCount > 0;

    Gizmos.color = nodeColor;
    foreach (var node in nodes)
    {
        Vector3 pos = useTruthPositions ? WorldModel.Instance.GetNode(node.id).WorldPos : node.position;
        Gizmos.DrawSphere(pos, nodeSphereSize);
    }

    Gizmos.color = edgeColor;
    foreach (var edge in edges)
    {
        Vector3 p1 = useTruthPositions ? WorldModel.Instance.GetNode(edge.Item1).WorldPos : nodes[edge.Item1].position;
        Vector3 p2 = useTruthPositions ? WorldModel.Instance.GetNode(edge.Item2).WorldPos : nodes[edge.Item2].position;
        Gizmos.DrawLine(p1, p2);
    }
}
#endif
}