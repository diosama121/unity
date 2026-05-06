using System.Collections.Generic;
using UnityEngine;

// 语义定义
public enum NodeType { Endpoint, Straight, Merge, Intersection }
public enum IntersectionState { Uncontrolled, GreenLight, RedLight, YellowLight }

public class RoadNode
{
    public int Id;
    public Vector3 WorldPos;
    public NodeType Type;
    public List<int> NeighborIds;
    public IntersectionState State;
}

public class WorldModel : MonoBehaviour
{
    public static WorldModel Instance { get; private set; }

    [Header("系统挂载 (V2.0 核心组件)")]
    public RoadNetworkGenerator roadGenerator; 
    public TerrainGridSystem terrainGrid;      
    public ProceduralRoadBuilder roadBuilder;
    public TrafficLightManager trafficLightManager;
    private Dictionary<int, RoadNode> _graph = new Dictionary<int, RoadNode>();
    private KDTree _spatialIndex;

    // 【a4 缝合点】：为 a5 观测台开放的数据面板接口
    public int NodeCount => _graph.Count;
    public IEnumerable<RoadNode> Nodes => _graph.Values;

  private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        // 删除了原先在这里直接调用 IngestGraph 等生成的代码
        // 现在系统启动时是一片空白，等待总指挥按下 UI 按钮
    }

    /// <summary>
    /// UI 创世按钮的最终触发接口
    /// </summary>
    public void TriggerWorldGeneration()
    {
        Debug.Log("[WorldModel] 🚀 收到创世指令，系统点火！");
        
        // 1. 触发数学拓扑生成
        FindObjectOfType<RoadNetworkGenerator>().Generate();
        
        // 2. 初始化地形底座 (需根据实际包围盒传入)
        FindObjectOfType<TerrainGridSystem>().Initialize(new Bounds(Vector3.zero, new Vector3(500, 100, 500)));
        
        // 3. 吞入图网络，建立真理层
        IngestGraph(FindObjectOfType<RoadNetworkGenerator>());
        
        // 4. a1 视觉层开始干活
        FindObjectOfType<ProceduralRoadBuilder>().BuildRoads();
        
        // 5. a3 交通调度开始放红绿灯和车
        FindObjectOfType<TrafficLightManager>()?.PlaceTrafficLights();
    }

    public float GetTerrainHeight(Vector2 worldXZ)
    {
        if (terrainGrid != null)
            return terrainGrid.SampleHeight(worldXZ);
        return 0f; 
    }

    // 吞入V1.0的原始图，吐出V2.0的语义图
    private void IngestGraph(RoadNetworkGenerator source)
    {
        foreach (var raw in source.nodes)
        {
            float y = GetTerrainHeight(new Vector2(raw.position.x, raw.position.z));
            
            _graph[raw.id] = new RoadNode
            {
                Id = raw.id,
                WorldPos = new Vector3(raw.position.x, y, raw.position.z),
                Type = ClassifyNode(raw.neighbors.Count),
                NeighborIds = new List<int>(raw.neighbors),
                State = IntersectionState.Uncontrolled
            };
        }
        
        _spatialIndex = new KDTree(_graph.Values);
        Debug.Log($"[WorldModel] 🌍 语义图谱构建完成，节点数: {_graph.Count}");
    }

    // --- 供 a2 / a3 调用的核心查询接口 ---

    public RoadNode GetNearestNode(Vector3 worldPos)
    {
        if (_spatialIndex == null) return null;
        int id = _spatialIndex.QueryNearest(worldPos);
        return _graph.ContainsKey(id) ? _graph[id] : null;
    }

    public RoadNode GetNode(int id)
    {
        return _graph.ContainsKey(id) ? _graph[id] : null;
    }

    public IntersectionState GetIntersectionState(int nodeId)
    {
        return _graph.ContainsKey(nodeId) ? _graph[nodeId].State : IntersectionState.Uncontrolled;
    }

    // 【a4 缝合点】：彻底打通 a3 的红绿灯语义写入闭环
    public void SetIntersectionState(int nodeId, IntersectionState state) 
    { 
        if (_graph.ContainsKey(nodeId))
        {
            _graph[nodeId].State = state;
        }
    }

    private NodeType ClassifyNode(int edgeCount) => edgeCount switch
    {
        1 => NodeType.Endpoint,
        2 => NodeType.Straight,
        3 => NodeType.Merge,
        _ => NodeType.Intersection
    };
    /// <summary>
    /// 接收 a1 传回的视觉多边形中心，修正 V1 原始坐标的偏差
    /// </summary>
    public void UpdateNodeVisualPosition(int nodeId, Vector3 newPos)
    {
        if (_graph.ContainsKey(nodeId))
        {
            // 核心安全锁：绝不盲目信任外部的 Y 轴。
            // 提取 a1 传来的 XZ 坐标，强制重新向地形系统索要绝对高度 Y
            float realY = GetTerrainHeight(new Vector2(newPos.x, newPos.z));
            _graph[nodeId].WorldPos = new Vector3(newPos.x, realY, newPos.z);
        }
        else
        {
            Debug.LogWarning($"[WorldModel] 试图更新不存在的节点视觉中心 ID: {nodeId}");
        }
    }

    /// <summary>
    /// 【极度重要】在 a1 汇报完所有视觉中心后，必须调用此方法重建空间索引！
    /// 否则 a2, a3, a5 的 KDTree 查询将发生严重的“刻舟求剑”错位！
    /// </summary>
    public void RebuildSpatialIndex()
    {
        _spatialIndex = new KDTree(_graph.Values);
        Debug.Log("[WorldModel] 🌳 视觉中心反哺对齐完毕，KDTree 空间索引已重建！");
    }
    
    // ==========================================
    // 【a4 缝合点】：供 a2 (寻路领航员) 调用的寻路代价接口
    // ==========================================

    /// <summary>
    /// 获取拓扑图中两个节点之间的物理通行代价（真实三维距离）
    /// 如果节点不存在，返回 float.MaxValue (不可达)
    /// </summary>
    public float GetEdgeCost(int fromNodeId, int toNodeId)
    {
        if (_graph.ContainsKey(fromNodeId) && _graph.ContainsKey(toNodeId))
        {
            // 以真理层记录的真实世界坐标计算距离
            return Vector3.Distance(_graph[fromNodeId].WorldPos, _graph[toNodeId].WorldPos);
        }
        
        Debug.LogWarning($"[WorldModel] 试图获取无效边的代价: {fromNodeId} -> {toNodeId}");
        return float.MaxValue; 
    }
}