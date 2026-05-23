using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using IOPath = System.IO.Path;
/// <summary>
/// V3.0 数据管理中心 — 一键导出 + 遥测录制
/// 导出三部分：
///   1. 世界实体快照（节点/车道/车辆/NPC等数量）
///   2. 主车属性快照（HUD左侧面板 + U键信息）
///   3. 世界构建参数（种子/网格/模式等，无主车时作为核心输出）
/// </summary>
public class SystemDataManager : MonoBehaviour
{
    public static SystemDataManager Instance { get; private set; }

    [Header("=== 车辆遥测数据（F9录制 / F10停止）===")]
    public SimpleCarController targetCar;
    public SimpleAutoDrive targetAI;
    public float recordInterval = 0.2f;
    private bool _isRecording = false;
    public bool IsRecording => _isRecording;
    private float _recordTimer = 0f;
    private StringBuilder _csvData;
    private string _csvPath;

    [Header("=== 路网数据 ===")]
    public RoadNetworkGenerator roadGen;

    // 内部组件缓存
    private TrafficManager _trafficManager;
    private MasterUIManager _uiManager;
    private ROS2BridgeV2 _ros2Bridge;
    private CameraController _cameraController;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        _csvData = new StringBuilder();
        _csvData.AppendLine("Timestamp,PosX,PosZ,Speed(km/h),State,NodeID,NodeType");

        if (roadGen == null) roadGen = FindObjectOfType<RoadNetworkGenerator>();
        CacheComponents();
    }

    void CacheComponents()
    {
        _trafficManager = FindObjectOfType<TrafficManager>();
        _uiManager = FindObjectOfType<MasterUIManager>();
        _ros2Bridge = FindObjectOfType<ROS2BridgeV2>();
        _cameraController = FindObjectOfType<CameraController>();
    }

    void Update()
    {
        // F9: 开始遥测录制
        if (Input.GetKeyDown(KeyCode.F9))
        {
            if (!_isRecording) StartRecording();
        }

        // F10: 停止录制并导出CSV
        if (Input.GetKeyDown(KeyCode.F10))
        {
            if (_isRecording) StopRecording();
        }

        // F11: 一键导出完整报告
        if (Input.GetKeyDown(KeyCode.F11))
        {
            ExportFullReport();
        }

        if (_isRecording && targetCar != null)
        {
            _recordTimer += Time.deltaTime;
            if (_recordTimer >= recordInterval)
            {
                RecordVehicleData();
                _recordTimer = 0f;
            }
        }
    }

    // ============================================================
    // 公共 API（供 DebugPanel / UI 按钮调用）
    // ============================================================

    /// <summary>开始遥测录制，自动查找主车</summary>
    public void StartRecording()
    {
        var allDrives = FindObjectsOfType<SimpleAutoDrive>();
        SimpleAutoDrive mainDrive = null;
        foreach (var d in allDrives)
        {
            if (d.isPlayerControlled) { mainDrive = d; break; }
        }
        if (mainDrive == null)
        {
            Debug.LogError("[SysData] 开始录制失败：找不到主车(isPlayerControlled=true)");
            return;
        }

        targetCar = mainDrive.GetComponent<SimpleCarController>();
        targetAI = mainDrive;
        _isRecording = true;
        _recordTimer = 0f;

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _csvPath = IOPath.Combine(Application.persistentDataPath, $"VehicleTelemetry_{timestamp}.csv");
        _csvData.Clear();
        _csvData.AppendLine("Timestamp,PosX,PosZ,Speed(km/h),State,NodeID,NodeType");

        Debug.Log($"[SysData] 开始录制主车 {targetCar.name} → {_csvPath}");
    }

    /// <summary>停止遥测录制，写入CSV文件</summary>
    public void StopRecording()
    {
        _isRecording = false;
        if (string.IsNullOrEmpty(_csvPath))
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _csvPath =  System.IO.Path.Combine(Application.persistentDataPath, $"VehicleTelemetry_{timestamp}.csv");
        }
        File.WriteAllText(_csvPath, _csvData.ToString());
        Debug.Log($"[SysData] 停止录制, {_csvData.Length} bytes → {_csvPath}");
    }
 
    /// <summary>切换录制状态（旧DebugPanel兼容）</summary>
    public void ToggleRecording()
    {
        if (_isRecording) StopRecording();
        else StartRecording();
    }

    // ============================================================
    // 一键导出完整报告（JSON）
    // ============================================================

    /// <summary>
    /// 一键导出完整JSON报告到 persistentDataPath。
    /// 文件命名：FullReport_yyyyMMdd_HHmmss.json
    /// </summary>
    public string ExportFullReport()
    {
        RefreshCache();
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string exportPath =  System.IO.Path.Combine(Application.persistentDataPath, $"FullReport_{timestamp}.json");

        var world = WorldModel.Instance;
        if (world == null)
        {
            Debug.LogError("[SysData] 导出失败：WorldModel.Instance 为空");
            return null;
        }

        // ===== Part 1: 世界实体快照 =====
        var worldSnapshot = new WorldSnapshot
        {
            nodeCount = world.NodeCount,
            laneCount = world.GlobalLanes?.Count ?? 0,
            connectorCount = world.GlobalConnectors?.Count ?? 0,
            stopLineJunctionCount = world.GlobalStopLines?.Count ?? 0,
            npcVehicleCount = _trafficManager?.ActiveNPCs?.Count ?? FindObjectsOfType<SimpleAutoDrive>().Length - 1,
            totalSimpleAutoDrives = FindObjectsOfType<SimpleAutoDrive>().Length,
            timeScale = Time.timeScale,
            fps = Mathf.RoundToInt(1f / Time.unscaledDeltaTime),
        };

        // ===== Part 2: 主车属性快照 =====
        SimpleAutoDrive mainDrive = FindMainCar();
        CarSnapshot carSnapshot = null;

        if (mainDrive != null)
        {
            SimpleCarController ctrl = mainDrive.GetComponent<SimpleCarController>();
            carSnapshot = new CarSnapshot
            {
                name = mainDrive.name,
                isPlayerControlled = mainDrive.isPlayerControlled,
                // HUD 车道/状态
                speed = ctrl != null ? ctrl.currentSpeed : mainDrive.currentSpeed,
                steeringAngle = ctrl != null ? ctrl.currentSteeringAngle : 0f,
                autoMode = mainDrive.isPlayerControlled ? "手动" : "自动",
                aiState = mainDrive.currentState.ToString(),
                longState = mainDrive.longState.ToString(),
                laneId = mainDrive.currentLaneId,
                isYielding = mainDrive.isYielding,
                // 传感器
                frontDistance = mainDrive.frontDistance,
                frontSpeed = mainDrive.frontSpeed,
                redLightAhead = mainDrive.redLightAhead,
                // 停止线
                distToStopLine = mainDrive.distToStopLine,
                hasStopLineAhead = mainDrive.hasStopLineAhead,
                nearestStopLinePos = mainDrive.nearestStopLinePos,
                // 位置
                position = mainDrive.transform.position,
                // ROS2
                ros2Connected = _ros2Bridge != null && _ros2Bridge.isConnected,
                ros2SendRate = _ros2Bridge != null ? _ros2Bridge.sendRate : 0f,
                // 目标/路径
                targetSpeed = mainDrive.targetSpeed,
                currentEdgeIndex = mainDrive.currentEdgeIndex,
                pathEdgeCount = mainDrive.pathEdgeIds?.Count ?? 0,
                // 高程
                groundHeight = world.GetUnifiedHeight(mainDrive.transform.position.x, mainDrive.transform.position.z),
            };
        }

        // ===== Part 3: 世界构建参数 =====
        WorldBuildParams buildParams = new WorldBuildParams
        {
            seed = roadGen != null ? roadGen.seed : -1,
            gridWidth = roadGen != null ? roadGen.gridWidth : 0,
            gridHeight = roadGen != null ? roadGen.gridHeight : 0,
            cellSize = roadGen != null ? roadGen.cellSize : 0f,
            isCountryside = roadGen != null && roadGen.isCountryside,
            randomOffset = roadGen != null ? roadGen.randomOffset : 0f,
            connectionRemoveRate = roadGen != null ? roadGen.connectionRemoveRate : 0f,
            countrysideHeightScale = roadGen != null ? roadGen.countrysideHeightScale : 0f,
            npcSpawnCount = _trafficManager != null ? _trafficManager.npcCount : 0,
            actualNPCCount = worldSnapshot.npcVehicleCount,
            nodeCount = worldSnapshot.nodeCount,
            laneCount = worldSnapshot.laneCount,
            connectorCount = worldSnapshot.connectorCount,
            edgeCount = roadGen?.edges?.Count ?? 0,
        };

        // ===== 组装完整报告 =====
        var report = new FullReportData
        {
            exportTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            exportVersion = "V3.0",
            worldSnapshot = worldSnapshot,
            carSnapshot = carSnapshot,
            worldBuildParams = buildParams,
            hasMainCar = carSnapshot != null,
        };

        string json = JsonUtility.ToJson(report, true);
        File.WriteAllText(exportPath, json);
        Debug.Log($"[SysData] 完整报告已导出 ({json.Length} chars) → {exportPath}");

        // 视觉反馈：弹一个浮动提示
        if (_uiManager != null)
        {
            _uiManager.AppendThoughtLine($"报告已导出: FullReport_{timestamp}.json");
        }

        return exportPath;
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    void RefreshCache()
    {
        if (_trafficManager == null) _trafficManager = FindObjectOfType<TrafficManager>();
        if (_uiManager == null) _uiManager = FindObjectOfType<MasterUIManager>();
        if (_ros2Bridge == null) _ros2Bridge = FindObjectOfType<ROS2BridgeV2>();
        if (_cameraController == null) _cameraController = FindObjectOfType<CameraController>();
    }

    SimpleAutoDrive FindMainCar()
    {
        var allDrives = FindObjectsOfType<SimpleAutoDrive>();
        foreach (var d in allDrives)
        {
            if (d.isPlayerControlled) return d;
        }
        // 没主车时返回相机跟随的车
        if (_cameraController != null && _cameraController.target != null)
        {
            return _cameraController.target.GetComponent<SimpleAutoDrive>();
        }
        return null;
    }

    void RecordVehicleData()
    {
        string aiState = targetAI != null ? targetAI.currentState.ToString() : "Manual";
        Rigidbody rb = targetCar != null ? targetCar.GetComponent<Rigidbody>() : null;
        float speedKmh = rb != null ? rb.velocity.magnitude * 3.6f : 0f;

        Vector3 carPos = targetCar != null ? targetCar.transform.position : Vector3.zero;
        RoadNode currentNode = WorldModel.Instance != null ? WorldModel.Instance.GetNearestNode(carPos) : null;

        int nodeID = -1;
        string nodeType = "未知";
        if (currentNode != null)
        {
            nodeID = currentNode.Id;
            if (currentNode.NeighborIds != null)
            {
                nodeType = currentNode.NeighborIds.Count >= 3 ? "路口"
                         : currentNode.NeighborIds.Count == 2 ? "路段" : "端点";
            }
        }

        _csvData.AppendLine($"{Time.time:F2},{carPos.x:F2},{carPos.z:F2},{speedKmh:F2},{aiState},{nodeID},{nodeType}");
    }

    [System.Obsolete("请使用 ExportFullReport() 一键导出")]
    void ExportRoadMap()
    {
        if (roadGen == null || roadGen.nodes.Count == 0) return;
        var data = new MapData();
        foreach (var node in roadGen.nodes)
            data.nodes.Add(new NodeData { id = node.id, x = node.position.x, y = node.position.y, z = node.position.z });
        string path =  System.IO.Path.Combine(Application.persistentDataPath, "RoadMapData_V2.0.json");
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        Debug.Log($"路网数据已导出至: {path}");
    }

    // ============================================================
    // JSON 数据结构
    // ============================================================

    [System.Serializable]
    public class FullReportData
    {
        public string exportTime;
        public string exportVersion;
        public WorldSnapshot worldSnapshot;
        public CarSnapshot carSnapshot;
        public WorldBuildParams worldBuildParams;
        public bool hasMainCar;
    }

    [System.Serializable]
    public class WorldSnapshot
    {
        public int nodeCount;
        public int laneCount;
        public int connectorCount;
        public int stopLineJunctionCount;
        public int npcVehicleCount;
        public int totalSimpleAutoDrives;
        public float timeScale;
        public int fps;
    }

    [System.Serializable]
    public class CarSnapshot
    {
        public string name;
        public bool isPlayerControlled;
        // 动态属性（HUD左侧显示）
        public float speed;
        public float steeringAngle;
        public string autoMode;
        public string aiState;
        public string longState;
        public int laneId;
        public bool isYielding;
        // 传感器
        public float frontDistance;
        public float frontSpeed;
        public bool redLightAhead;
        // 停止线
        public float distToStopLine;
        public bool hasStopLineAhead;
        public Vector3 nearestStopLinePos;
        // 位置
        public Vector3 position;
        // ROS2
        public bool ros2Connected;
        public float ros2SendRate;
        // 路径/目标
        public float targetSpeed;
        public int currentEdgeIndex;
        public int pathEdgeCount;
        // 高程
        public float groundHeight;
    }

    [System.Serializable]
    public class WorldBuildParams
    {
        public int seed;
        public int gridWidth;
        public int gridHeight;
        public float cellSize;
        public bool isCountryside;
        public float randomOffset;
        public float connectionRemoveRate;
        public float countrysideHeightScale;
        public int npcSpawnCount;
        public int actualNPCCount;
        public int nodeCount;
        public int laneCount;
        public int connectorCount;
        public int edgeCount;
    }

    [System.Serializable] public class MapData { public List<NodeData> nodes = new List<NodeData>(); }
    [System.Serializable] public class NodeData { public int id; public float x, y, z; }
}