using System.Collections.Generic;
using UnityEngine;
using System.Linq;

// ==========================================
// 【V2.2 核心语义定义】
// ==========================================
public enum NodeType { Endpoint, Straight, Merge, Intersection }
public enum IntersectionState { Uncontrolled, GreenLight, RedLight, YellowLight }

[System.Serializable]
public partial class RoadNode
{
    public int Id;
    public Vector3 WorldPos;      // 绝对坐标真理（包含平滑后的 Y）
    public Vector3 Tangent;       // 前进切线（Mesh 挤出的走向基准）
    public Vector3 Normal;        // 横向法线（Mesh 宽度的伸展基准）
    public NodeType Type;
    public List<int> NeighborIds;
    public IntersectionState State;
}

// 供 a1 数学辅助类使用的样条线数据结构
public struct SplinePoint
{
    public Vector3 Pos;
    public Vector3 Tangent;
    public Vector3 Normal;
}

public class WorldModel : MonoBehaviour
{
    public static WorldModel Instance { get; private set; }

    [Header("系统挂载 (V2.2 核心组件)")]
    public RoadNetworkGenerator roadGenerator;
    public TerrainGridSystem terrainGrid;
    public ProceduralRoadBuilder roadBuilder;
    public TrafficLightManager trafficLightManager;
    public TrafficManager trafficManager;

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
        
        // 【V4.1 生命周期时序锁 - 绝对起点】
        // 必须严格排在 IngestAndPrecomputeGraph 之前！否则高度图未生成，路网全坠入 Y=0
        terrainGrid.Initialize(worldBounds);

        // 3. 建立真理层 (核心：高度平滑 + 切线计算 + 坐标锁死)
        IngestAndPrecomputeGraph(roadGenerator);

        // 4. 通知 a1 视觉层执行 (a1 现在只需“按图填色”)
        roadBuilder.BuildRoads();

        // 5. 通知 a3 交通层执行
        if (trafficLightManager != null) trafficLightManager.PlaceTrafficLights();
        if (trafficManager != null) trafficManager.SpawnNPCs();

        Debug.Log("[WorldModel] ✨ 世界生成完成，真理层已就绪。");
    }

    /// <summary>
    /// 【核心手术】吞入原始图并预计算所有几何向量
    /// </summary>
  private void IngestAndPrecomputeGraph(RoadNetworkGenerator source)
{
    _graph.Clear();

    // 防穿插离地偏移量（节点初始略高于地形，避免 Z-fighting 与踩空感）
    const float NODE_GROUND_OFFSET = 0.2f;

    // --- 第一步：统一高度基准 + 离地偏移 ---
    foreach (var raw in source.nodes)
    {
        // 强制调用唯一真理接口获取地形高度
        float baseY = GetUnifiedHeight(raw.position.x, raw.position.z);
        // 叠加偏移，确保节点视觉与逻辑始终在地表之上
        float finalY = baseY + NODE_GROUND_OFFSET;

        _graph[raw.id] = new RoadNode
        {
            Id = raw.id,
            WorldPos = new Vector3(raw.position.x, finalY, raw.position.z),
            Type = ClassifyNode(raw.neighbors.Count),
            NeighborIds = new List<int>(raw.neighbors),
            State = IntersectionState.Uncontrolled
        };
    }

    // --- 第二步：高程平滑处理 ---
    // 【V4.1 删除】此方法会篡改真理高度，导致节点重新拉偏，已彻底移除！
    // SmoothNodeHeights();

    // --- 第2.5步：计算动态路口半径与路口语义 ---
    foreach (var node in _graph.Values)
    {
        if (node.NeighborIds.Count <= 2) continue; // 非路口节点跳过

        // 计算与所有邻居的最大距离
        float maxDist = 0f;
        foreach (var nbId in node.NeighborIds)
        {
            float d = Vector3.Distance(node.WorldPos, _graph[nbId].WorldPos);
            if (d > maxDist) maxDist = d;
        }

        // 动态半径 = 最大边距的一半 + 固定缓冲
        float rawRadius = (maxDist * 0.5f) + 2f;

        // 从 roadBuilder 获取路宽做下限，防止半径过小导致路口多边形退化
        float minRadius = 6f;
        if (roadBuilder != null) minRadius = roadBuilder.roadWidth * 1.2f;

        node.IntersectionRadius = Mathf.Clamp(rawRadius, minRadius, 25f);

        // 语义分类
        node.Kind = node.NeighborIds.Count switch
        {
            3 => IntersectionKind.T_Junction,
            4 => IntersectionKind.Crossroad,
            _ => IntersectionKind.MultiWay
        };
    }

    // --- 第三步：预计算切线与法线 ---
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

        Vector3 avgDir = Vector3.zero;
        foreach (var nbId in node.NeighborIds)
        {
            avgDir += (_graph[nbId].WorldPos - node.WorldPos).normalized;
        }
        return (avgDir / node.NeighborIds.Count).normalized;
    }

    private void SmoothNodeHeights()
    {
        for (int iteration = 0; iteration < 2; iteration++)
        {
            foreach (var node in _graph.Values)
            {
                if (node.NeighborIds.Count == 0) continue;
                float neighborAvgY = 0;
                foreach (var nid in node.NeighborIds) neighborAvgY += _graph[nid].WorldPos.y;
                neighborAvgY /= node.NeighborIds.Count;

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
        b.Expand(100f);
        return b;
    }

    // --- 供子系统调用的原子接口 (a4 职责) ---

    public float GetTerrainHeight(Vector2 worldXZ)
    {
        if (terrainGrid != null) return terrainGrid.SampleHeight(worldXZ);
        return 0f;
    }

    public RoadNode GetNearestNode(Vector3 pos)
    {
        if (_spatialIndex == null) return null;
        int id = _spatialIndex.QueryNearest(pos);
        return _graph.ContainsKey(id) ? _graph[id] : null;
    }

    public RoadNode GetNode(int id) => _graph.GetValueOrDefault(id);

    public float GetNodeFixedHeight(int id) => _graph.ContainsKey(id) ? _graph[id].WorldPos.y : 0f;

    public void SetIntersectionState(int id, IntersectionState s) { if (_graph.ContainsKey(id)) _graph[id].State = s; }

    public IntersectionState GetIntersectionState(int id) => _graph.GetValueOrDefault(id)?.State ?? IntersectionState.Uncontrolled;

    public float GetEdgeCost(int a, int b) => Vector3.Distance(_graph[a].WorldPos, _graph[b].WorldPos);

    private NodeType ClassifyNode(int count) => count switch { 1 => NodeType.Endpoint, 2 => NodeType.Straight, 3 => NodeType.Merge, _ => NodeType.Intersection };

    public void UpdateNodeVisualPosition(int id, Vector3 newPos)
    {
        if (!_graph.ContainsKey(id)) return;
        float y = GetUnifiedHeight(newPos.x, newPos.z);
        _graph[id].WorldPos = new Vector3(newPos.x, y, newPos.z);
    }

    public void RebuildSpatialIndex() { _spatialIndex = new KDTree(_graph.Values); }
    
    public (Vector3 worldPos, Vector3 tangent) GetNodeData(int nodeId)
    {
        if (_graph.TryGetValue(nodeId, out RoadNode node))
            return (node.WorldPos, node.Tangent);
        Debug.LogError($"[WorldModel] 节点 {nodeId} 不存在");
        return (Vector3.zero, Vector3.forward);
    }
    
    public float GetUnifiedHeight(float x, float z)
    {
        if (terrainGrid != null)
        {
            return terrainGrid.SampleHeight(new Vector2(x, z));
        }
        return 0f;
    }
}