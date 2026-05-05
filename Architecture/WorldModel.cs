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

    private Dictionary<int, RoadNode> _graph = new Dictionary<int, RoadNode>();
    private KDTree _spatialIndex;

    // 【a4 缝合点】：为 a5 观测台开放的数据面板接口
    public int NodeCount => _graph.Count;
    public IEnumerable<RoadNode> Nodes => _graph.Values;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 初始化时序必须严格遵守！
        // 1. 原始拓扑生成
        if (roadGenerator != null)
        {
            roadGenerator.Generate();
            IngestGraph(roadGenerator);
        }

        // 2. 地形烘焙 (a1 交付完毕，解开封印)
        if (terrainGrid != null && roadGenerator != null)
        {
            // 注意：真实环境中需要传入路网多边形用于剔除道路下方网格
            // 这里假定外部 ProceduralRoadBuilder 生成时会处理，先初始化基础网格
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(roadGenerator.gridWidth * roadGenerator.cellSize, 100, roadGenerator.gridHeight * roadGenerator.cellSize));
            terrainGrid.Initialize(bounds);
        }
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
}