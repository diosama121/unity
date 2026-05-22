using System.Collections.Generic;
using UnityEngine;
using System.Linq;

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

public class WorldModel : MonoBehaviour
{
    public static WorldModel Instance { get; private set; }

    [Header("System Components")]
    public RoadNetworkGenerator roadGenerator;
    public TerrainGridSystem terrainGrid;
    public ProceduralRoadBuilder roadBuilder;
    public TrafficLightManager trafficLightManager;
    public TrafficManager trafficManager;

    private Dictionary<int, RoadNode> _graph = new Dictionary<int, RoadNode>();
    private KDTree _spatialIndex;

    private List<LaneKDEntry> _laneSamples = new List<LaneKDEntry>();
    private KDTree _laneSpatialIndex;

    public Dictionary<int, Lane> GlobalLanes = new Dictionary<int, Lane>();
    public Dictionary<int, LaneConnector> GlobalConnectors = new Dictionary<int, LaneConnector>();
    public Dictionary<int, List<StopLine>> GlobalStopLines = new Dictionary<int, List<StopLine>>();
    public Dictionary<int, IntersectionState> PhaseStates = new Dictionary<int, IntersectionState>();
    public Dictionary<string, List<SplinePoint>> GlobalSplineCache = new Dictionary<string, List<SplinePoint>>();
    private int _nextLaneId = 0;
    private int _nextConnectorId = 0;

    // 观测接口
    public int NodeCount => _graph.Count;
    public IEnumerable<RoadNode> Nodes => _graph.Values;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void TriggerWorldGeneration()
    {
        Debug.Log("[WorldModel] World generation sequence started...");

        GlobalSplineCache.Clear();
        _laneSamples.Clear();
        _laneSpatialIndex = null;

        roadGenerator.Generate();

        Bounds worldBounds = CalculateWorldBounds();

        IngestAndPrecomputeGraph(roadGenerator);

        terrainGrid.Initialize(worldBounds);

        foreach (var node in _graph.Values)
        {
            node.WorldPos = new Vector3(node.WorldPos.x, GetUnifiedHeight(node.WorldPos.x, node.WorldPos.z) + 0.1f, node.WorldPos.z);
        }

        GenerateAndRegisterLanes();

        GenerateConnectors();

        roadBuilder.BuildRoads();

        if (roadGenerator != null && !roadGenerator.isCountryside)
        {
            GenerateStopLines();
        }

        if (trafficLightManager != null && !roadGenerator.isCountryside)
            trafficLightManager.PlaceTrafficLights();
        if (trafficManager != null)
        {
            foreach (var npc in trafficManager.ActiveNPCs)
            {
                if (npc != null) Destroy(npc.gameObject);
            }
            trafficManager.ResetSpawnState();
        }
        if (trafficManager != null) trafficManager.SpawnNPCsOnLanes();

        Debug.Log("[WorldModel] World generation complete.");
    }

    private void IngestAndPrecomputeGraph(RoadNetworkGenerator source)
    {
        _graph.Clear();

        const float NODE_GROUND_OFFSET = 0.1f;

        foreach (var raw in source.nodes)
        {
            float baseY = GetUnifiedHeight(raw.position.x, raw.position.z);
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

        foreach (var node in _graph.Values)
        {
            if (node.NeighborIds.Count < 3) continue;

            List<int> sorted = node.NeighborIds
                .OrderBy(nbId =>
                {
                    RoadNode nbNode = _graph.GetValueOrDefault(nbId);
                    if (nbNode == null) return 0f;
                    return Mathf.Atan2(
                        nbNode.WorldPos.z - node.WorldPos.z,
                        nbNode.WorldPos.x - node.WorldPos.x);
                })
                .ToList();
            node.PolarSortedNeighbors = sorted;

            node.AngleToNextNeighbor = new Dictionary<int, float>();
            int count = sorted.Count;

            float maxDist = 0f;
            for (int i = 0; i < count; i++)
            {
                int nbA = sorted[i];
                int nbB = sorted[(i + 1) % count];

                RoadNode nodeA = _graph.GetValueOrDefault(nbA);
                RoadNode nodeB = _graph.GetValueOrDefault(nbB);

                if (nodeA == null || nodeB == null) continue;

                float d = Vector3.Distance(node.WorldPos, nodeA.WorldPos);
                if (d > maxDist) maxDist = d;

                Vector3 dirA = new Vector3(
                    nodeA.WorldPos.x - node.WorldPos.x,
                    0f,
                    nodeA.WorldPos.z - node.WorldPos.z).normalized;

                Vector3 dirB = new Vector3(
                    nodeB.WorldPos.x - node.WorldPos.x,
                    0f,
                    nodeB.WorldPos.z - node.WorldPos.z).normalized;

                float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dirA, dirB), -1f, 1f));
                node.AngleToNextNeighbor[nbA] = theta;
            }

            node.IntersectionRadius = Mathf.Clamp(maxDist, 6f, 20f);

            node.Kind = node.NeighborIds.Count switch
            {
                3 => IntersectionKind.T_Junction,
                4 => IntersectionKind.Crossroad,
                _ => IntersectionKind.MultiWay
            };
        }

        foreach (var node in _graph.Values)
        {
            node.Tangent = CalculateNodeTangent(node);
            node.Normal = Vector3.Cross(node.Tangent, Vector3.up).normalized;
        }

        _spatialIndex = new KDTree(_graph.Values);
    }

    private void GenerateAndRegisterLanes()
    {
        GlobalLanes.Clear();
        GlobalConnectors.Clear();
        _nextLaneId = 0;

        // 【核心修复1】：删除 isCountryside 限制，让乡村也能生成数学车道！
        if (roadGenerator == null) return;

        HashSet<string> processedEdges = new HashSet<string>();
        float roadWidth = roadBuilder != null ? roadBuilder.roadWidth : 6f;
        float stepDist = roadBuilder != null ? roadBuilder.meshResolution : 2f;

        foreach (var node in _graph.Values)
        {
            if (node.NeighborIds == null) continue;

            foreach (int neighborId in node.NeighborIds)
            {
                string edgeKey = Mathf.Min(node.Id, neighborId) + "_" + Mathf.Max(node.Id, neighborId);
                if (processedEdges.Contains(edgeKey)) continue;
                processedEdges.Add(edgeKey);

                List<SplinePoint> centerSpline = RoadMathUtility.GetRoadSpline(node.Id, neighborId, stepDist, roadWidth);
                if (centerSpline == null || centerSpline.Count < 2) continue;

                GlobalSplineCache[edgeKey] = centerSpline;

                var (fwdPath, revPath) = RoadMathUtility.GenerateLanePathsFromCenter(centerSpline, roadWidth);
                if (fwdPath.Count < 2 && revPath.Count < 2) continue;

                int roadId = node.Id * 10000 + neighborId;

                Lane fwdLane = null;
                Lane revLane = null;

                if (fwdPath.Count >= 2)
                {
                    fwdLane = new Lane
                    {
                        LaneId = _nextLaneId++,
                        RoadId = roadId,
                        CenterSpline = new CatmullRomSpline(fwdPath),
                        Direction = LaneDirection.Forward,
                        LeftLaneId = -1,
                        RightLaneId = -1,
                        NextConnectorIds = new List<int>()
                    };
                }

                if (revPath.Count >= 2)
                {
                    revLane = new Lane
                    {
                        LaneId = _nextLaneId++,
                        RoadId = roadId,
                        CenterSpline = new CatmullRomSpline(revPath),
                        Direction = LaneDirection.Reverse,
                        LeftLaneId = -1,
                        RightLaneId = -1,
                        NextConnectorIds = new List<int>()
                    };
                }

                if (fwdLane != null && revLane != null)
                {
                    fwdLane.LeftLaneId = revLane.LaneId;
                    revLane.RightLaneId = fwdLane.LaneId;
                }

                if (fwdLane != null) GlobalLanes[fwdLane.LaneId] = fwdLane;
                if (revLane != null) GlobalLanes[revLane.LaneId] = revLane;
            }
        }

        Debug.Log($"[WorldModel] 车道图注册完成: {GlobalLanes.Count}条车道, {GlobalConnectors.Count}个连接器");

        RebuildLaneSpatialIndex();
    }

    private void GenerateStopLines()
    {
        GlobalStopLines.Clear();

        foreach (var node in _graph.Values)
        {
            if (node.NeighborIds == null || node.NeighborIds.Count < 3) continue;

            var stopLines = new List<StopLine>();
            Vector3 junctionPos = node.WorldPos;

            for (int dirIdx = 0; dirIdx < node.NeighborIds.Count; dirIdx++)
            {
                int nbId = node.NeighborIds[dirIdx];
                if (!_graph.TryGetValue(nbId, out RoadNode nbNode)) continue;

                Vector3 approachDir = (junctionPos - nbNode.WorldPos);
                approachDir.y = 0f;
                if (approachDir.sqrMagnitude < 0.001f) continue;

                float safeStopDistance = node.IntersectionRadius + 5.0f;
                Vector3 stopPos = junctionPos - approachDir.normalized * safeStopDistance;
                stopPos.y = GetUnifiedHeight(stopPos.x, stopPos.z) + 0.1f;

                bool isNS = Mathf.Abs(approachDir.z) > Mathf.Abs(approachDir.x);
                int phaseId = node.Id * 10 + (isNS ? 0 : 1);

                stopLines.Add(new StopLine
                {
                    NodeId = node.Id,
                    LaneId = -1,
                    Position = stopPos,
                    Normal = approachDir.normalized,
                    AssociatedPhaseId = phaseId
                });
            }

            if (stopLines.Count > 0)
                GlobalStopLines[node.Id] = stopLines;
        }

        Debug.Log($"[WorldModel] 停止线生成完成: {GlobalStopLines.Count}个路口");
    }

    public StopLine GetNearestStopLine(int junctionId, Vector3 fromPos)
    {
        if (!GlobalStopLines.TryGetValue(junctionId, out var lines) || lines.Count == 0)
            return null;

        StopLine nearest = lines[0];
        float minDist = Vector3.Distance(fromPos, nearest.Position);
        for (int i = 1; i < lines.Count; i++)
        {
            float d = Vector3.Distance(fromPos, lines[i].Position);
            if (d < minDist) { minDist = d; nearest = lines[i]; }
        }
        return nearest;
    }

    private Vector3 CalculateNodeTangent(RoadNode node)
    {
        if (node.NeighborIds.Count == 0) return Vector3.forward;

        if (node.NeighborIds.Count == 2)
        {
            Vector3 p0 = _graph[node.NeighborIds[0]].WorldPos;
            Vector3 p1 = _graph[node.NeighborIds[1]].WorldPos;
            return (p1 - p0).normalized;
        }

        Vector3 avgDir = Vector3.zero;
        foreach (var nbId in node.NeighborIds)
        {
            avgDir += (_graph[nbId].WorldPos - node.WorldPos).normalized;
        }
        return (avgDir / node.NeighborIds.Count).normalized;
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

    public void SetIntersectionState(int id, IntersectionState s)
    {
        if (_graph.ContainsKey(id)) _graph[id].State = s;
        PhaseStates[id] = s;
    }

    public void SetPhaseState(int phaseId, IntersectionState s)
    {
        PhaseStates[phaseId] = s;
    }

    public IntersectionState GetPhaseState(int phaseId)
    {
        return PhaseStates.TryGetValue(phaseId, out var s) ? s : IntersectionState.Uncontrolled;
    }

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

    public int FindNearestLane(Vector3 worldPos)
    {
        if (_laneSpatialIndex != null && _laneSamples.Count > 0)
        {
            int bestIdx = _laneSpatialIndex.QueryNearest(worldPos);
            if (bestIdx >= 0 && bestIdx < _laneSamples.Count)
            {
                return _laneSamples[bestIdx].LaneId;
            }
        }

        return FindNearestLaneBruteForce(worldPos);
    }

    public int FindNearestLaneBruteForce(Vector3 worldPos)
    {
        int bestLaneId = -1;
        float bestDist = 900f;
        Vector2 posXZ = new Vector2(worldPos.x, worldPos.z);

        foreach (var kvp in GlobalLanes)
        {
            Lane lane = kvp.Value;
            if (lane.CenterSpline == null) continue;

            float totalLen = lane.CenterSpline.TotalLength;
            if (totalLen <= 0) continue;

            Vector3 startPt = lane.CenterSpline.GetPoint(0);
            Vector3 endPt = lane.CenterSpline.GetPoint(1);
            float safeExpand = totalLen * 0.5f + 20f;
            float minX = Mathf.Min(startPt.x, endPt.x) - safeExpand;
            float maxX = Mathf.Max(startPt.x, endPt.x) + safeExpand;
            float minZ = Mathf.Min(startPt.z, endPt.z) - safeExpand;
            float maxZ = Mathf.Max(startPt.z, endPt.z) + safeExpand;
            if (posXZ.x < minX || posXZ.x > maxX || posXZ.y < minZ || posXZ.y > maxZ) continue;

            int samples = Mathf.Max(10, Mathf.CeilToInt(totalLen / 1f));
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                Vector3 pt = lane.CenterSpline.GetPoint(t);
                float sqrD = (posXZ.x - pt.x) * (posXZ.x - pt.x) + (posXZ.y - pt.z) * (posXZ.y - pt.z);
                if (sqrD < bestDist)
                {
                    bestDist = sqrD;
                    bestLaneId = lane.LaneId;
                }
            }
        }

        return bestLaneId;
    }

    public void RebuildLaneSpatialIndex()
    {
        _laneSamples.Clear();

        foreach (var kvp in GlobalLanes)
        {
            Lane lane = kvp.Value;
            if (lane.CenterSpline == null || lane.CenterSpline.TotalLength <= 0) continue;

            for (float dist = 0; dist <= lane.CenterSpline.TotalLength; dist += 5f)
            {
                float t = Mathf.Clamp01(dist / lane.CenterSpline.TotalLength);
                Vector3 pt = lane.CenterSpline.GetPoint(t);
                _laneSamples.Add(new LaneKDEntry
                {
                    Point = pt,
                    LaneId = lane.LaneId
                });
            }
        }

        if (_laneSamples.Count > 0)
        {
            var fakeNodes = new List<RoadNode>(_laneSamples.Count);
            for (int i = 0; i < _laneSamples.Count; i++)
            {
                fakeNodes.Add(new RoadNode
                {
                    Id = i,
                    WorldPos = _laneSamples[i].Point
                });
            }

            _laneSpatialIndex = new KDTree(fakeNodes);
        }

        Debug.Log($"[WorldModel] Lane KD-Tree built: {_laneSamples.Count} samples");
    }

   private void GenerateConnectors()
    {
        GlobalConnectors.Clear();
        _nextConnectorId = 0;

        foreach (var kvp in _graph)
        {
            RoadNode node = kvp.Value;
            if (node.NeighborIds == null || node.NeighborIds.Count == 0) continue;

            List<Lane> entryLanes = new List<Lane>();
            List<Lane> exitLanes  = new List<Lane>();

            // 1. 基于绝对图论拓扑，精准收集当前路口的进出车道，彻底抛弃距离检测！
            foreach (int nbId in node.NeighborIds)
            {
                int minId = Mathf.Min(node.Id, nbId);
                int maxId = Mathf.Max(node.Id, nbId);
                int roadId = minId * 10000 + maxId;

                // 找到挂载在这条路上的所有车道
                var lanesOnRoad = GlobalLanes.Values.Where(l => l.RoadId == roadId).ToList();

                foreach (var lane in lanesOnRoad)
                {
                    // 在生成时：Forward 总是从 minId 节点走向 maxId 节点
                    //           Reverse 总是从 maxId 节点走向 minId 节点
                    bool isForward = (lane.Direction == LaneDirection.Forward);
                    
                    if (node.Id == maxId)
                    {
                        // 如果当前路口是 maxId：Forward 从远方开向我(驶入)，Reverse 从我开向远方(驶出)
                        if (isForward) entryLanes.Add(lane);
                        else exitLanes.Add(lane);
                    }
                    else if (node.Id == minId)
                    {
                        // 如果当前路口是 minId：Reverse 从远方开向我(驶入)，Forward 从我开向远方(驶出)
                        if (!isForward) entryLanes.Add(lane);
                        else exitLanes.Add(lane);
                    }
                }
            }

            // 2. 正常路口转向配对
            foreach (Lane entry in entryLanes)
            {
                if (entry.NextConnectorIds == null)
                    entry.NextConnectorIds = new List<int>();

                foreach (Lane exit in exitLanes)
                {
                    // 排除同一条路的掉头（同一条 RoadId 说明是原路返回）
                    // 正常的十字路口不应该直接掉头，除非是死胡同
                    if (entry.RoadId == exit.RoadId) continue;

                    LaneConnector connector = BuildConnector(entry, exit, node);
                    if (connector != null)
                    {
                        GlobalConnectors[connector.ConnectorId] = connector;
                        entry.NextConnectorIds.Add(connector.ConnectorId);
                    }
                }

                // 3. 死路兜底：如果这个车道进入路口后没有任何出口（比如单条边的尽头 Node）
                // 此时强制生成掉头连接器 (U-Turn)，防止车开出地图
                if (entry.NextConnectorIds.Count == 0)
                {
                    LaneConnector uTurn = BuildUTurnConnector(entry, node);
                    if (uTurn != null)
                    {
                        GlobalConnectors[uTurn.ConnectorId] = uTurn;
                        entry.NextConnectorIds.Add(uTurn.ConnectorId);
                    }
                }
            }
        }

        Debug.Log($"[WorldModel] 成功生成 {GlobalConnectors.Count} 条路口连接线 (完全基于图拓扑精确匹配)");
    }

    private LaneConnector BuildConnector(Lane entry, Lane exit, RoadNode node)
    {
        float entryLen = entry.CenterSpline.TotalLength;
        float exitLen = exit.CenterSpline.TotalLength;

        // 只保留路口边界点
        Vector3 p1 = entry.CenterSpline.GetPoint(1f);
        Vector3 p3 = exit.CenterSpline.GetPoint(0f);

        // 【修复切线稳定性】：safeLen 避免微小长度导致切线爆炸
        float entrySafeLen = Mathf.Max(entryLen, 0.1f);
        float exitSafeLen  = Mathf.Max(exitLen, 0.1f);
        Vector3 entryDir = (p1 - entry.CenterSpline.GetPoint(1f - 2f / entrySafeLen)).normalized;
        Vector3 exitDir  = (exit.CenterSpline.GetPoint(2f / exitSafeLen) - p3).normalized;

        float signedAngle = Vector3.SignedAngle(entryDir, exitDir, Vector3.up);

        TurnType tType = TurnType.Straight;
        if (signedAngle < -20f) tType = TurnType.LeftTurn;
        else if (signedAngle > 20f) tType = TurnType.RightTurn;

        float dist = Vector3.Distance(p1, p3);
        float mag = dist * 0.4f;
        if (tType == TurnType.LeftTurn) mag = dist * 0.6f;
        if (tType == TurnType.RightTurn) mag = dist * 0.25f;

        // 埃尔米特插值生成极其平滑的中间控制点
        Vector3 mid1 = SplineMath.EvaluateHermite(0.33f, p1, entryDir * mag, p3, exitDir * mag);
        Vector3 mid2 = SplineMath.EvaluateHermite(0.66f, p1, entryDir * mag, p3, exitDir * mag);

        // 【核心修复】：只用 4 个点构建，彻底消除曲线过冲绕圈现象
        List<Vector3> pts = new List<Vector3>() { p1, mid1, mid2, p3 };
        CatmullRomSpline spline = new CatmullRomSpline(pts, false);

        LaneConnector connector = new LaneConnector();
        connector.ConnectorId = _nextConnectorId++;
        connector.JunctionId = node.Id;
        connector.FromLaneId = entry.LaneId;
        connector.ToLaneId = exit.LaneId;
        connector.TurnCurve = spline;
        connector.TurnType = tType;

        return connector;
    }

    private LaneConnector BuildUTurnConnector(Lane lane, RoadNode node)
    {
        float len = lane.CenterSpline.TotalLength;
        Vector3 p1 = lane.CenterSpline.GetPoint(1f);   // 终点（驶入路口的点）
        Vector3 p2 = lane.CenterSpline.GetPoint(0f);   // 起点（掉头后要回到的点）

        // 取切线：入口方向（指向终点）和出口方向（从起点出发）
        Vector3 entryDir = (p1 - lane.CenterSpline.GetPoint(Mathf.Max(0f, 1f - 2f / len))).normalized;
        Vector3 exitDir = (lane.CenterSpline.GetPoint(Mathf.Min(1f, 2f / len)) - p2).normalized;

        // 掉头需要一个大弧线，控制点向外拉远
        float dist = Vector3.Distance(p1, p2);
        float mag = dist * 0.8f;

        Vector3 mid1 = SplineMath.EvaluateHermite(0.33f, p1, entryDir * mag, p2, exitDir * mag);
        Vector3 mid2 = SplineMath.EvaluateHermite(0.66f, p1, entryDir * mag, p2, exitDir * mag);

        List<Vector3> pts = new List<Vector3> { p1, mid1, mid2, p2 };
        CatmullRomSpline spline = new CatmullRomSpline(pts, false);

        LaneConnector connector = new LaneConnector
        {
            ConnectorId = _nextConnectorId++,
            JunctionId = node.Id,
            FromLaneId = lane.LaneId,
            ToLaneId = lane.LaneId,          // 自己连自己，形成掉头
            TurnCurve = spline,
            TurnType = TurnType.UTurn
        };

        return connector;
    }

    public Lane GetLaneByNodeFlow(int fromNodeId, int toNodeId)
    {
        foreach (var kvp in GlobalLanes)
        {
            Lane lane = kvp.Value;
            if (lane.CenterSpline == null || lane.CenterSpline.TotalLength < 1f) continue;

            Vector3 startPos = lane.CenterSpline.GetPoint(0f);
            Vector3 endPos = lane.CenterSpline.GetPoint(1f);
            RoadNode fromNode = GetNode(fromNodeId);
            RoadNode toNode = GetNode(toNodeId);

            if (fromNode != null && toNode != null)
            {
                if (Vector3.Distance(startPos, fromNode.WorldPos) < 5f &&
                    Vector3.Distance(endPos, toNode.WorldPos) < 5f)
                {
                    return lane;
                }
            }
        }
        return null;
    }

    public LaneConnector GetConnector(int fromLaneId, int toLaneId)
    {
        foreach (var kvp in GlobalConnectors)
        {
            if (kvp.Value.FromLaneId == fromLaneId && kvp.Value.ToLaneId == toLaneId)
                return kvp.Value;
        }
        return null;
    }
}

public class LaneKDEntry
{
    public Vector3 Point;
    public int LaneId;
}
