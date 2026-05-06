using System.Collections.Generic;
using UnityEngine;
using System.Linq;

// ==========================================
// 【V2.2 核心语义定义】
// ==========================================
public enum NodeType { Endpoint, Straight, Merge, Intersection }
public enum IntersectionState { Uncontrolled, GreenLight, RedLight, YellowLight }

[System.Serializable]
public class RoadNode
{
    public int Id;
    public Vector3 WorldPos;      // 绝对坐标真理（包含平滑后的 Y）
    public Vector3 Tangent;       // 前进切线（Mesh 挤出的走向基准）
    public Vector3 Normal;        // 横向法线（Mesh 宽度的伸展基准）
    public NodeType Type;
    public List<int> NeighborIds;
    public IntersectionState State;
}

public class WorldModel : MonoBehaviour
{
    public static WorldModel Instance { get; private set; }

    [Header("系统挂载 (V2.2 核心组件)")]
    public RoadNetworkGenerator roadGenerator; 
    public TerrainGridSystem terrainGrid;      
    public ProceduralRoadBuilder roadBuilder;
    public TrafficLightManager trafficLightManager;
    public TrafficManager trafficManager; // 新增：交通流调度

    private Dictionary<int, RoadNode> _graph = new Dictionary<int, RoadNode>();
    private KDTree _spatialIndex;

    // 观测接口
    public int NodeCount => _graph.Count;
    public IEnumerable<RoadNode> Nodes => _graph.Values;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 【总指挥点火接口】V2.2 严密初始化序列
    /// </summary>
    public void TriggerWorldGeneration()
    {
        Debug.Log("[WorldModel] 🚀 创世序列启动...");

        // 1. 触发图拓扑随机生成 (V1.0)
        roadGenerator.Generate();

        // 2. 动态计算地形包围盒 (给 a1 裙边预留 100m 冗余)
        Bounds worldBounds = CalculateWorldBounds();
        terrainGrid.Initialize(worldBounds);

        // 3. 建立真理层 (核心：高度平滑 + 切线计算 + 坐标锁死)
        IngestAndPrecomputeGraph(roadGenerator);

        // 4. 通知 a1 视觉层执行 (a1 现在只需“按图填色”)
        roadBuilder.BuildRoads();

        // 5. 通知 a3 交通层执行
        trafficLightManager?.PlaceTrafficLights();
        trafficManager?.SpawnNPCs();

        Debug.Log("[WorldModel] ✨ 世界生成完成，真理层已就绪。");
    }

    /// <summary>
    /// 【核心手术】吞入原始图并预计算所有几何向量
    /// 解决 a5 提到的高程崩塌与视觉错位问题
    /// </summary>
    private void IngestAndPrecomputeGraph(RoadNetworkGenerator source)
    {
        _graph.Clear();

        // --- 第一步：初步摄入并校准高度 ---
        foreach (var raw in source.nodes)
        {
            float baseY = terrainGrid.SampleHeight(new Vector2(raw.position.x, raw.position.z));
            
            _graph[raw.id] = new RoadNode
            {
                Id = raw.id,
                WorldPos = new Vector3(raw.position.x, baseY, raw.position.z),
                Type = ClassifyNode(raw.neighbors.Count),
                NeighborIds = new List<int>(raw.neighbors),
                State = IntersectionState.Uncontrolled
            };
        }

        // --- 第二步：高程平滑处理 (解决 a5 提到的阶跃与尖刺) ---
        SmoothNodeHeights();

        // --- 第三步：预计算切线与法线 (a4 亲自计算，严禁 a1 乱动) ---
        foreach (var node in _graph.Values)
        {
            node.Tangent = CalculateNodeTangent(node);
            node.Normal = Vector3.Cross(node.Tangent, Vector3.up).normalized;
        }

        _spatialIndex = new KDTree(_graph.Values);
    }

    // --- a4 的几何计算车间 ---

    private Vector3 CalculateNodeTangent(RoadNode node)
    {
        if (node.NeighborIds.Count == 0) return Vector3.forward;
        
        // 取得所有邻居方向的平均向量作为该点的切线走向
        Vector3 avgDir = Vector3.zero;
        foreach (var nbId in node.NeighborIds)
        {
            avgDir += (_graph[nbId].WorldPos - node.WorldPos).normalized;
        }
        return (avgDir / node.NeighborIds.Count).normalized;
    }

    private void SmoothNodeHeights()
    {
        // 简单的拉普拉斯平滑：让节点高度向邻居靠拢，消除断层
        for (int iteration = 0; iteration < 2; iteration++)
        {
            foreach (var node in _graph.Values)
            {
                if (node.NeighborIds.Count == 0) continue;
                float neighborAvgY = 0;
                foreach (var nid in node.NeighborIds) neighborAvgY += _graph[nid].WorldPos.y;
                neighborAvgY /= node.NeighborIds.Count;
                
                // 混合原始高度与邻居平均高度（80% 平滑度）
                node.WorldPos.y = Mathf.Lerp(node.WorldPos.y, neighborAvgY, 0.8f);
            }
        }
    }

    private Bounds CalculateWorldBounds()
    {
        if (roadGenerator.nodes.Count == 0) return new Bounds(Vector3.zero, new Vector3(500, 100, 500));
        Vector3 min = roadGenerator.nodes[0].position;
        Vector3 max = roadGenerator.nodes[0].position;
        foreach (var n in roadGenerator.nodes)
        {
            min = Vector3.Min(min, n.position);
            max = Vector3.Max(max, n.position);
        }
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        b.Expand(100f); // 为 a1 的地表裙边预留空间
        return b;
    }

    // --- 供子系统调用的原子接口 (a4 职责) ---
public float GetTerrainHeight(Vector2 worldXZ)
{
    if (terrainGrid != null)
        return terrainGrid.SampleHeight(worldXZ);
    return 0f; 
}
   public RoadNode GetNearestNode(Vector3 worldPos) 
{
    if (_spatialIndex == null) return null;
    // 将 QueryNearestNode 改为 QueryNearest
    int id = _spatialIndex.QueryNearest(worldPos); 
    return _graph.ContainsKey(id) ? _graph[id] : null;
}
    public RoadNode GetNode(int id) => _graph.GetValueOrDefault(id);
    public float GetNodeFixedHeight(int id) => _graph.ContainsKey(id) ? _graph[id].WorldPos.y : 0f;

    public void SetIntersectionState(int id, IntersectionState s) { if(_graph.ContainsKey(id)) _graph[id].State = s; }
    public IntersectionState GetIntersectionState(int id) => _graph.GetValueOrDefault(id)?.State ?? IntersectionState.Uncontrolled;

    public float GetEdgeCost(int a, int b) => Vector3.Distance(_graph[a].WorldPos, _graph[b].WorldPos);

    private NodeType ClassifyNode(int count) => count switch { 1 => NodeType.Endpoint, 2 => NodeType.Straight, 3 => NodeType.Merge, _ => NodeType.Intersection };

    /// <summary>
    /// 【对齐接口】a1 反馈视觉重心，重塑空间真理
    /// </summary>
    public void UpdateNodeVisualPosition(int id, Vector3 newPos)
    {
        if (!_graph.ContainsKey(id)) return;
        // 仅接受 XZ 修正，Y 轴依然锁定地形真理
        float y = terrainGrid.SampleHeight(new Vector2(newPos.x, newPos.z));
        _graph[id].WorldPos = new Vector3(newPos.x, y, newPos.z);
    }

    public void RebuildSpatialIndex() { _spatialIndex = new KDTree(_graph.Values); }
}