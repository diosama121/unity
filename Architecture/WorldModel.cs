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
public RoadNetworkGenerator roadGenerator; // 槽位 1：路网生成器
public TerrainGridSystem terrainGrid;      // 槽位 2：地形网格系统 (手动补上这一行)


    private Dictionary<int, RoadNode> _graph = new Dictionary<int, RoadNode>();
    private KDTree _spatialIndex;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 初始化时序必须严格遵守！
        // 1. 地形烘焙 (a1 交付后解注释)
        // terrainGrid.BakeRoadMask();

        // 2. 原始拓扑生成
        if (roadGenerator != null)
        {
            roadGenerator.Generate();
            IngestGraph(roadGenerator);
        }
    }
    public float GetTerrainHeight(Vector2 worldXZ)
{
    if (terrainGrid != null)
        return terrainGrid.SampleHeight(worldXZ);
    return 0f; // 如果地形还没加载好，返回0作为兜底
}
    // 吞入V1.0的原始图，吐出V2.0的语义图
    private void IngestGraph(RoadNetworkGenerator source)
    {
        foreach (var raw in source.nodes)
        {
            // float y = terrainGrid.SampleHeight(new Vector2(raw.position.x, raw.position.z));
            float y = 0f; // 目前先用0，等 a1 交付接口
            
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
        return _graph[id];
    }

    public RoadNode GetNode(int id)
    {
        return _graph.ContainsKey(id) ? _graph[id] : null;
    }

    public IntersectionState GetIntersectionState(int nodeId)
    {
        return _graph.ContainsKey(nodeId) ? _graph[nodeId].State : IntersectionState.Uncontrolled;
    }

    private NodeType ClassifyNode(int edgeCount) => edgeCount switch
    {
        1 => NodeType.Endpoint,
        2 => NodeType.Straight,
        3 => NodeType.Merge,
        _ => NodeType.Intersection
    };
    public virtual void SetIntersectionState(int nodeId, IntersectionState state) 
{ 
    // a1 的真实写入逻辑（比如更新字典等）
}
}