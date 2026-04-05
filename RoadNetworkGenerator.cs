using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 随机路网生成器
/// 功能：网格为基础 + 随机偏移，支持种子复现，Inspector + 运行时UI双模式输入
/// 挂载位置：场景中空 GameObject，命名 "RoadNetworkGenerator"
/// </summary>
public class RoadNetworkGenerator : MonoBehaviour
{
    // =============================================
    // Inspector 配置
    // =============================================

    [Header("=== 路网尺寸 ===")]
    [Tooltip("横向格子数量")]
    public int gridWidth = 5;

    [Tooltip("纵向格子数量")]
    public int gridHeight = 5;

    [Tooltip("每个格子的基础间距（米）")]
    public float cellSize = 20f;

    [Header("=== 随机偏移 ===")]
    [Tooltip("路点随机偏移最大值（米），0 = 纯网格")]
    public float randomOffset = 5f;

    [Tooltip("随机种子（相同种子生成相同路网）")]
    public int seed = 42;

    [Tooltip("随机删除部分连接，模拟真实路网（0=不删，1=全删）")]
    [Range(0f, 0.4f)]
    public float connectionRemoveRate = 0.1f;

    [Header("=== 生成控制 ===")]
    [Tooltip("启动时自动生成路网")]
    public bool generateOnStart = true;

    [Tooltip("生成路网后自动关联到 PathPlanner")]
    public bool autoLinkPathPlanner = true;

    [Tooltip("是否显示运行时UI面板")]
    public bool showRuntimeUI = true;

    [Header("=== 可视化 ===")]
    public bool showGizmos = true;
    public Color nodeColor = Color.yellow;
    public Color edgeColor = Color.white;
    public float nodeSphereSize = 1f;
    // 在类的字段区域加
    private bool pendingGenerate = false;
    private bool pendingRandomGenerate = false;
    // =============================================
    // 内部数据
    // =============================================

    // 路点数据结构
    public class WaypointNode
    {
        public int id;
        public Vector3 position;
        public List<int> neighbors = new List<int>();
        public GameObject gizmoObject; // 可选：场景中的标记物体
    }

    // 生成的路网数据
    [HideInInspector]
    public List<WaypointNode> nodes = new List<WaypointNode>();

    [HideInInspector]
    public List<(int, int)> edges = new List<(int, int)>();

    // 二维索引，方便查找 grid[x][z] → nodeId
    private int[,] grid;

    // 运行时UI状态
    private bool uiExpanded = true;
    private string uiSeed = "42";
    private string uiWidth = "5";
    private string uiHeight = "5";
    private string uiCellSize = "20";
    private string uiOffset = "5";

    // PathPlanner引用
    public PathPlanner pathPlanner;

    // =============================================
    // 生命周期
    // =============================================

    void Start()
    {
        if (autoLinkPathPlanner)
        {
            pathPlanner = FindObjectOfType<PathPlanner>();
            if (pathPlanner == null)
            {
                GameObject ppGO = new GameObject("PathPlanner");
                pathPlanner = ppGO.AddComponent<PathPlanner>();
            }
        }

        if (generateOnStart)
        {
            Generate();
        }

        // 同步UI字段
        SyncUIFields();
    }

    // =============================================
    // 核心生成逻辑
    // =============================================

    /// <summary>
    /// 生成路网（主入口）
    /// </summary>
    void Update()
    {
        if (pendingGenerate)
        {
            pendingGenerate = false;
            Generate();
        }

        if (pendingRandomGenerate)
        {
            pendingRandomGenerate = false;
            Generate();
        }
    }
    public void Generate()
    {
        // 清空旧数据
        Clear();

        // 初始化随机数（固定种子）
        Random.InitState(seed);

        // 生成节点
        GenerateNodes();

        // 生成连接
        GenerateEdges();

        // 同步到 PathPlanner
        if (autoLinkPathPlanner && pathPlanner != null)
        {
            SyncToPathPlanner();
        }
        SimpleAutoDrive autoDrive = FindObjectOfType<SimpleAutoDrive>();
        if (autoDrive != null)
        {
            autoDrive.currentState = SimpleAutoDrive.DriveState.Idle;
            // 用反射清空私有path字段
            var pathField = typeof(SimpleAutoDrive).GetField("path",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (pathField != null)
                pathField.SetValue(autoDrive, null);

            Debug.Log("⚠️ 路网已重建，自动驾驶已重置为 Idle");
        }

        Debug.Log($"✅ 路网生成完成 | 种子:{seed} | 尺寸:{gridWidth}x{gridHeight} | " +
                  $"节点:{nodes.Count} | 连接:{edges.Count}");
        var roadBuilder = GetComponent<ProceduralRoadBuilder>();
        if (roadBuilder != null)
            roadBuilder.BuildRoads();
        var trafficLightManager = GetComponent<TrafficLightManager>();
        if (trafficLightManager != null)
            trafficLightManager.PlaceTrafficLights();
    }

    /// <summary>
    /// 生成所有路点节点
    /// </summary>
    void GenerateNodes()
    {
        grid = new int[gridWidth, gridHeight];
        int id = 0;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                // 基础网格位置
                float baseX = x * cellSize;
                float baseZ = z * cellSize;

                // 随机偏移（四角节点不偏移，保持路网边界整齐）
                float offsetX = 0f;
                float offsetZ = 0f;

                bool isCorner = (x == 0 || x == gridWidth - 1) &&
                                (z == 0 || z == gridHeight - 1);

                if (!isCorner && randomOffset > 0f)
                {
                    offsetX = Random.Range(-randomOffset, randomOffset);
                    offsetZ = Random.Range(-randomOffset, randomOffset);
                }

                Vector3 pos = new Vector3(baseX + offsetX, 0f, baseZ + offsetZ);

                WaypointNode node = new WaypointNode
                {
                    id = id,
                    position = pos
                };

                nodes.Add(node);
                grid[x, z] = id;
                id++;
            }
        }
    }

    /// <summary>
    /// 生成节点之间的连接
    /// </summary>
    void GenerateEdges()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                int currentId = grid[x, z];

                // 向右连接
                if (x + 1 < gridWidth)
                {
                    TryAddEdge(currentId, grid[x + 1, z]);
                }

                // 向上连接
                if (z + 1 < gridHeight)
                {
                    TryAddEdge(currentId, grid[x, z + 1]);
                }
            }
        }
    }

    /// <summary>
    /// 尝试添加一条边（有概率随机删除）
    /// </summary>
    void TryAddEdge(int nodeA, int nodeB)
    {
        // 边界边不删除，保证路网连通性
        bool isBoundaryEdge = IsBoundaryEdge(nodeA, nodeB);

        if (!isBoundaryEdge && Random.value < connectionRemoveRate)
        {
            return; // 随机删除这条边
        }

        edges.Add((nodeA, nodeB));
        nodes[nodeA].neighbors.Add(nodeB);
        nodes[nodeB].neighbors.Add(nodeA);
    }

    /// <summary>
    /// 判断是否是边界边（边界边不随机删除）
    /// </summary>
    bool IsBoundaryEdge(int nodeA, int nodeB)
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (grid[x, z] == nodeA || grid[x, z] == nodeB)
                {
                    if (x == 0 || x == gridWidth - 1 || z == 0 || z == gridHeight - 1)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 清空路网数据
    /// </summary>
    public void Clear()
    {
        nodes.Clear();
        edges.Clear();
        grid = null;

        if (pathPlanner != null)
        {
            // 重置PathPlanner（通过重建路网）
            pathPlanner.GetRoadNetwork().Clear();
        }
    }

    // =============================================
    // 同步到 PathPlanner
    // =============================================
    void SyncToPathPlanner()
    {
        // 用新方法完全重置，而不是只Clear字典
        pathPlanner.ResetNetwork();

        // 重新添加所有节点
        foreach (var node in nodes)
        {
            pathPlanner.AddWaypoint(node.position);
        }

        // 重新添加所有连接
        foreach (var edge in edges)
        {
            pathPlanner.ConnectWaypoints(edge.Item1, edge.Item2);
        }

        Debug.Log($"✅ 路网已同步到 PathPlanner：{nodes.Count} 节点，{edges.Count} 连接");
    }
    // =============================================
    // 公共接口
    // =============================================

    /// <summary>
    /// 获取所有节点位置（供外部使用）
    /// </summary>
    public List<Vector3> GetAllNodePositions()
    {
        List<Vector3> positions = new List<Vector3>();
        foreach (var node in nodes)
            positions.Add(node.position);
        return positions;
    }

    /// <summary>
    /// 获取路网边界（用于摄像机定位）
    /// </summary>
    public Bounds GetNetworkBounds()
    {
        if (nodes.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);

        Vector3 min = nodes[0].position;
        Vector3 max = nodes[0].position;

        foreach (var node in nodes)
        {
            min = Vector3.Min(min, node.position);
            max = Vector3.Max(max, node.position);
        }

        Bounds bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    /// <summary>
    /// 获取随机一个节点位置（用于随机目的地）
    /// </summary>
    public Vector3 GetRandomNodePosition()
    {
        if (nodes.Count == 0) return Vector3.zero;
        int idx = Random.Range(0, nodes.Count);
        return nodes[idx].position;
    }

    /// <summary>
    /// 获取路网右上角节点位置（常用作默认目的地）
    /// </summary>
    public Vector3 GetFarCornerPosition()
    {
        if (grid == null || nodes.Count == 0) return Vector3.zero;
        return nodes[grid[gridWidth - 1, gridHeight - 1]].position;
    }

    // =============================================
    // 运行时 UI
    // =============================================

    void SyncUIFields()
    {
        uiSeed = seed.ToString();
        uiWidth = gridWidth.ToString();
        uiHeight = gridHeight.ToString();
        uiCellSize = cellSize.ToString();
        uiOffset = randomOffset.ToString();
    }

    void OnGUI()
    {
        if (!showRuntimeUI) return;

        float panelWidth = 260f;
        float panelHeight = uiExpanded ? 300f : 40f;
        float x = Screen.width - panelWidth - 10f;
        float y = 10f;

        GUI.Box(new Rect(x, y, panelWidth, panelHeight), "");
        GUILayout.BeginArea(new Rect(x + 8, y + 8, panelWidth - 16, panelHeight - 16));

        // 标题栏
        GUILayout.BeginHorizontal();
        GUILayout.Label("🗺️ 路网生成器",
            new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold });
        if (GUILayout.Button(uiExpanded ? "▲" : "▼", GUILayout.Width(30)))
            uiExpanded = !uiExpanded;
        GUILayout.EndHorizontal();

        if (uiExpanded)
        {
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("种子 Seed:", GUILayout.Width(90));
            uiSeed = GUILayout.TextField(uiSeed, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("宽 (列数):", GUILayout.Width(90));
            uiWidth = GUILayout.TextField(uiWidth, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("高 (行数):", GUILayout.Width(90));
            uiHeight = GUILayout.TextField(uiHeight, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("格子大小(m):", GUILayout.Width(90));
            uiCellSize = GUILayout.TextField(uiCellSize, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("随机偏移(m):", GUILayout.Width(90));
            uiOffset = GUILayout.TextField(uiOffset, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            if (GUILayout.Button("▶  生成路网", GUILayout.Height(30)))
            {
                ApplyUISettings();
                pendingGenerate = true;
            }

            GUILayout.Space(3);

            GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
            if (GUILayout.Button("🎲  随机种子并生成", GUILayout.Height(25)))
            {
                seed = Random.Range(0, 99999);
                uiSeed = seed.ToString();
                ApplyUISettings();
                pendingRandomGenerate = true;
            }

            GUI.backgroundColor = Color.white;

            GUILayout.Space(5);

            GUILayout.Label($"节点: {nodes.Count}  连接: {edges.Count}",
                new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.gray } });
            GUILayout.Label($"当前种子: {seed}",
                new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.cyan } });
        }

        GUILayout.EndArea();
    }
    /// <summary>
    /// 把UI输入的字符串应用到实际参数
    /// </summary>
    void ApplyUISettings()
    {
        if (int.TryParse(uiSeed, out int s)) seed = s;
        if (int.TryParse(uiWidth, out int w)) gridWidth = Mathf.Max(2, w);
        if (int.TryParse(uiHeight, out int h)) gridHeight = Mathf.Max(2, h);
        if (float.TryParse(uiCellSize, out float c)) cellSize = Mathf.Max(5f, c);
        if (float.TryParse(uiOffset, out float o)) randomOffset = Mathf.Max(0f, o);
    }

    // =============================================
    // Gizmos 可视化
    // =============================================

    void OnDrawGizmos()
    {
        if (!showGizmos || nodes == null) return;

        // 绘制节点
        Gizmos.color = nodeColor;
        foreach (var node in nodes)
        {
            Gizmos.DrawSphere(node.position + Vector3.up * 0.5f, nodeSphereSize);

#if UNITY_EDITOR
            // 显示节点ID
            Handles.Label(node.position + Vector3.up * 2f,
                node.id.ToString(),
                new GUIStyle { normal = { textColor = Color.yellow }, fontSize = 10 });
#endif
        }

        // 绘制连接
        Gizmos.color = edgeColor;
        foreach (var edge in edges)
        {
            if (edge.Item1 < nodes.Count && edge.Item2 < nodes.Count)
            {
                Gizmos.DrawLine(
                    nodes[edge.Item1].position + Vector3.up * 0.5f,
                    nodes[edge.Item2].position + Vector3.up * 0.5f
                );
            }
        }

        // 绘制路网边界框
        if (nodes.Count > 0)
        {
            Bounds b = GetNetworkBounds();
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Gizmos.DrawWireCube(b.center + Vector3.up * 0.5f, b.size + Vector3.up);
        }
    }
}
