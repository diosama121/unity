using UnityEngine;
using System.Collections.Generic;

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

private bool pendingGenerate = false;
private bool pendingRandomGenerate = false;

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
private bool uiExpanded = true;
private string uiSeed = "42";
private string uiWidth = "5";
private string uiHeight = "5";
private string uiCellSize = "20";
private string uiOffset = "5";

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
pathPlanner = new GameObject("PathPlanner").AddComponent<PathPlanner>();
}
}

if (generateOnStart) Generate();
SyncUIFields();
}

void Update()
{
if (pendingGenerate) { pendingGenerate = false; Generate(); }
if (pendingRandomGenerate) { pendingRandomGenerate = false; Generate(); }
}

// =============================================
// 核心生成：道路优先
// =============================================

public void Generate()
{
Clear();
Random.InitState(seed);

// Step 1: 生成道路网格（先定义所有道路段）
GenerateRoadGrid();

// Step 2: 从道路端点提取节点
ExtractNodesFromRoads();

// Step 3: 按道路连接节点
ConnectNodesByRoads();

// Step 4: 同步到PathPlanner（A*在此图上运行）
if (autoLinkPathPlanner && pathPlanner != null)
SyncToPathPlanner();

// 重置自动驾驶
var autoDrive = FindObjectOfType<SimpleAutoDrive>();
if (autoDrive != null)
{
autoDrive.currentState = SimpleAutoDrive.DriveState.Idle;
var f = typeof(SimpleAutoDrive).GetField("path",
System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (f != null) f.SetValue(autoDrive, null);
}

Debug.Log($"✅ 路网生成完成 | 种子:{seed} | 节点:{nodes.Count} | 道路:{roadSegments.Count} | 连接:{edges.Count}");

var roadBuilder = GetComponent<ProceduralRoadBuilder>();
if (roadBuilder != null) roadBuilder.BuildRoads();

var tlm = GetComponent<TrafficLightManager>();
if (tlm != null) tlm.PlaceTrafficLights();
}

// =============================================
// Step 1: 生成道路网格
// =============================================

void GenerateRoadGrid()
{
roadSegments.Clear();

// 先生成所有网格点位置（带随机偏移）
Vector3[,] gridPositions = new Vector3[gridWidth, gridHeight];
// 按网格生成道路段（横向+纵向）
for (int x = 0; x < gridWidth; x++)
{
for (int z = 0; z < gridHeight; z++)
{
float bx = x * cellSize;
float bz = z * cellSize;

// ========== 修改此处：根据模式决定是否有坡度 ==========
float elevation = 0f;
if (isCountryside)
{
elevation = Mathf.PerlinNoise(bx * 0.05f, bz * 0.05f) * 5f;
}

float ox = 0f, oz = 0f;
bool isCorner = (x == 0 || x == gridWidth - 1) &&
(z == 0 || z == gridHeight - 1);

if (!isCorner && randomOffset > 0f)
{
ox = Random.Range(-randomOffset, randomOffset);
oz = Random.Range(-randomOffset, randomOffset);
}

gridPositions[x, z] = new Vector3(bx + ox, elevation, bz + oz);
}
}

// 按网格生成道路段（横向+纵向）
for (int x = 0; x < gridWidth; x++)
{
for (int z = 0; z < gridHeight; z++)
{
// 横向道路（向右）
if (x + 1 < gridWidth)
{
bool isBoundary = (z == 0 || z == gridHeight - 1);
if (isBoundary || Random.value > connectionRemoveRate)
{
roadSegments.Add(new RoadSegment
{
start = gridPositions[x, z],
end = gridPositions[x + 1, z]
});
}
}

// 纵向道路（向上）
if (z + 1 < gridHeight)
{
bool isBoundary = (x == 0 || x == gridWidth - 1);
if (isBoundary || Random.value > connectionRemoveRate)
{
roadSegments.Add(new RoadSegment
{
start = gridPositions[x, z],
end = gridPositions[x, z + 1]
});
}
}
}
}
}

// =============================================
// Step 2: 从道路端点提取节点（合并重复位置）
// =============================================

void ExtractNodesFromRoads()
{
nodes.Clear();
Dictionary<Vector3, int> posToId = new Dictionary<Vector3, int>();
int nextId = 0;

foreach (var seg in roadSegments)
{
// 起点
if (!posToId.ContainsKey(seg.start))
{
posToId[seg.start] = nextId;
nodes.Add(new WaypointNode { id = nextId, position = seg.start });
nextId++;
}
seg.startNodeId = posToId[seg.start];

// 终点
if (!posToId.ContainsKey(seg.end))
{
posToId[seg.end] = nextId;
nodes.Add(new WaypointNode { id = nextId, position = seg.end });
nextId++;
}
seg.endNodeId = posToId[seg.end];
}

// 重建grid（用于GetFarCornerPosition）
grid = new int[gridWidth, gridHeight];
int idx = 0;
for (int x = 0; x < gridWidth; x++)
for (int z = 0; z < gridHeight; z++)
grid[x, z] = idx++;
}

// =============================================
// Step 3: 按道路连接节点
// =============================================

void ConnectNodesByRoads()
{
edges.Clear();
foreach (var node in nodes) node.neighbors.Clear();

foreach (var seg in roadSegments)
{
int a = seg.startNodeId;
int b = seg.endNodeId;

if (!nodes[a].neighbors.Contains(b))
nodes[a].neighbors.Add(b);

if (!nodes[b].neighbors.Contains(a))
nodes[b].neighbors.Add(a);

edges.Add((a, b));
}
}

// =============================================
// Step 4: 同步到PathPlanner
// =============================================

void SyncToPathPlanner()
{
pathPlanner.ResetNetwork();

// 每条道路段插入中间点（每15米一个节点）
float interpolateStep = 15f;
Dictionary<Vector3, int> posToId = new Dictionary<Vector3, int>();

int GetOrAddNode(Vector3 pos)
{
// 查找是否已有近似位置节点（容差0.1米）
foreach (var kv in posToId)
{
if (Vector3.Distance(kv.Key, pos) < 0.1f)
return kv.Value;
}
int id = pathPlanner.AddWaypoint(pos);
posToId[pos] = id;
return id;
}

foreach (var seg in roadSegments)
{
float segLength = Vector3.Distance(seg.start, seg.end);
int steps = Mathf.Max(1, Mathf.FloorToInt(segLength / interpolateStep));

int prevId = GetOrAddNode(seg.start);

for (int i = 1; i <= steps; i++)
{
float t = (float)i / steps;
Vector3 pos = Vector3.Lerp(seg.start, seg.end, t);
int curId = GetOrAddNode(pos);
pathPlanner.ConnectWaypoints(prevId, curId);
prevId = curId;
}
}

Debug.Log($"✅ 路网已同步到PathPlanner（含插值节点）");
}

// =============================================
// 清空
// =============================================

public void Clear()
{
nodes.Clear();
edges.Clear();
roadSegments.Clear();
grid = null;
if (pathPlanner != null) pathPlanner.ResetNetwork();
}

// =============================================
// 公共接口
// =============================================

public List<Vector3> GetAllNodePositions()
{
var list = new List<Vector3>();
foreach (var n in nodes) list.Add(n.position);
return list;
}

public Bounds GetNetworkBounds()
{
if (nodes.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
Vector3 min = nodes[0].position, max = nodes[0].position;
foreach (var n in nodes) { min = Vector3.Min(min, n.position); max = Vector3.Max(max, n.position); }
Bounds b = new Bounds(); b.SetMinMax(min, max); return b;
}

public Vector3 GetRandomNodePosition()
{
if (nodes.Count == 0) return Vector3.zero;
return nodes[Random.Range(0, nodes.Count)].position;
}

public Vector3 GetFarCornerPosition()
{
if (nodes.Count == 0) return Vector3.zero;
// 找离原点最远的节点
Vector3 farthest = nodes[0].position;
float maxDist = 0f;
foreach (var n in nodes)
{
float d = Vector3.Distance(Vector3.zero, n.position);
if (d > maxDist) { maxDist = d; farthest = n.position; }
}
return farthest;
}

// =============================================
// 运行时UI
// =============================================

void SyncUIFields()
{
uiSeed = seed.ToString();
uiWidth = gridWidth.ToString();
uiHeight = gridHeight.ToString();
uiCellSize = cellSize.ToString();
uiOffset = randomOffset.ToString();
}

void ApplyUISettings()
{
if (int.TryParse(uiSeed, out int s)) seed = s;
if (int.TryParse(uiWidth, out int w)) gridWidth = Mathf.Max(2, w);
if (int.TryParse(uiHeight, out int h)) gridHeight = Mathf.Max(2, h);
if (float.TryParse(uiCellSize, out float c)) cellSize = Mathf.Max(5f, c);
if (float.TryParse(uiOffset, out float o)) randomOffset = Mathf.Max(0f, o);
}

void OnGUI()
{
if (!showRuntimeUI) return;

float pw = 260f, ph = uiExpanded ? 450f : 40f;
float x = Screen.width - pw - 10f, y = 10f;

GUI.Box(new Rect(x, y, pw, ph), "");
GUILayout.BeginArea(new Rect(x + 8, y + 8, pw - 16, ph - 16));

GUILayout.BeginHorizontal();
GUILayout.Label("🗺️ 路网生成器", new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold });
if (GUILayout.Button(uiExpanded ? "▲" : "▼", GUILayout.Width(30))) uiExpanded = !uiExpanded;
GUILayout.EndHorizontal();

if (uiExpanded)
{
GUILayout.Space(5);
GUILayout.BeginHorizontal(); GUILayout.Label("种子 Seed:", GUILayout.Width(90)); uiSeed = GUILayout.TextField(uiSeed, GUILayout.Width(80)); GUILayout.EndHorizontal();
GUILayout.BeginHorizontal(); GUILayout.Label("宽 (列数):", GUILayout.Width(90)); uiWidth = GUILayout.TextField(uiWidth, GUILayout.Width(80)); GUILayout.EndHorizontal();
GUILayout.BeginHorizontal(); GUILayout.Label("高 (行数):", GUILayout.Width(90)); uiHeight = GUILayout.TextField(uiHeight, GUILayout.Width(80)); GUILayout.EndHorizontal();
GUILayout.BeginHorizontal(); GUILayout.Label("格子大小(m):", GUILayout.Width(90)); uiCellSize = GUILayout.TextField(uiCellSize, GUILayout.Width(80)); GUILayout.EndHorizontal();
GUILayout.BeginHorizontal(); GUILayout.Label("随机偏移(m):", GUILayout.Width(90)); uiOffset = GUILayout.TextField(uiOffset, GUILayout.Width(80)); GUILayout.EndHorizontal();

GUILayout.Space(8);
GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
if (GUILayout.Button("▶生成路网", GUILayout.Height(30))) { ApplyUISettings(); pendingGenerate = true; }

GUILayout.Space(3);
GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
if (GUILayout.Button("🎲随机种子并生成", GUILayout.Height(25)))
{
int newSeed = (int)(Time.realtimeSinceStartup * 1000) % 99999;
seed = newSeed;
uiSeed = seed.ToString();
ApplyUISettings();
pendingRandomGenerate = true;
}
GUI.backgroundColor = Color.white;
GUILayout.Space(5);
GUILayout.Label($"节点:{nodes.Count} 道路:{roadSegments.Count} 连接:{edges.Count}", new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.gray } });
GUILayout.Label($"当前种子: {seed}", new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.cyan } });
GUILayout.BeginHorizontal(); GUILayout.Label("环境模式:", GUILayout.Width(90)); isCountryside = GUILayout.Toggle(isCountryside, isCountryside ? "乡村 (起伏地形)" : "城市 (平坦+高楼)"); GUILayout.EndHorizontal();
}

GUILayout.EndArea();
}

// =============================================
// Gizmos
// =============================================

void OnDrawGizmos()
{
if (!showGizmos || nodes == null) return;

Gizmos.color = nodeColor;
foreach (var node in nodes)
{
Gizmos.DrawSphere(node.position + Vector3.up * 0.5f, nodeSphereSize);
#if UNITY_EDITOR
Handles.Label(node.position + Vector3.up * 2f, node.id.ToString(),
new GUIStyle { normal = { textColor = Color.yellow }, fontSize = 10 });
#endif
}

// 绘制道路段（绿色=实际道路）
Gizmos.color = Color.green;
foreach (var seg in roadSegments)
{
Gizmos.DrawLine(seg.start + Vector3.up * 0.5f, seg.end + Vector3.up * 0.5f);
}

if (nodes.Count > 0)
{
Bounds b = GetNetworkBounds();
Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
Gizmos.DrawWireCube(b.center + Vector3.up * 0.5f, b.size + Vector3.up);
}
}
}