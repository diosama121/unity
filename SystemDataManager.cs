using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// V2.0 数据管理：基于 a2 PathPlanner 真实字段重构
/// 真实字段：Id / WorldPos / NeighborIds
/// </summary>
public class SystemDataManager : MonoBehaviour
{
    [Header("=== 车辆遥测数据 (生成CSV供Excel画图) ===")]
    public SimpleCarController targetCar;
    public SimpleAutoDrive targetAI;
    public float recordInterval = 0.2f; 
    private bool isRecording = false;
    private float timer = 0f;
    private StringBuilder csvData = new StringBuilder();

    [Header("=== 路网数据 (生成JSON) ===")]
    public RoadNetworkGenerator roadGen;
    
    private string csvPath;
    private string jsonPath;

    void Start()
    {
        // ✅ 修复：使用 Application.persistentDataPath 替代 Application.dataPath
        // 避免权限问题，支持全平台
        csvPath = System.IO.Path.Combine(Application.persistentDataPath, 
            "VehicleTelemetry_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        jsonPath = System.IO.Path.Combine(Application.persistentDataPath, 
            "RoadMapData_V2.0.json");
        
        csvData.Clear();
        csvData.AppendLine("Timestamp,PosX,PosZ,Speed(km/h),State,NodeID,NodeType");
        
        if (roadGen == null) roadGen = FindObjectOfType<RoadNetworkGenerator>();
    }

    void Update()
    {
        // F9: 开始录制（主车）
        if (Input.GetKeyDown(KeyCode.F9))
        {
            if (!isRecording)
            {
                // 自动查找主车(isPlayerControlled在SimpleAutoDrive上)
                var allDrives = FindObjectsOfType<SimpleAutoDrive>();
                SimpleAutoDrive mainDrive = null;
                foreach (var d in allDrives)
                {
                    if (d.isPlayerControlled) { mainDrive = d; break; }
                }
                if (mainDrive != null)
                {
                    targetCar = mainDrive.GetComponent<SimpleCarController>();
                    targetAI = mainDrive;
                }

                if (targetCar == null)
                {
                    Debug.LogError("[SysData] F9 录制失败：找不到主车(isPlayerControlled=true)");
                }
                else
                {
                    isRecording = true;
                    timer = 0f;
                    Debug.Log($"[SysData] F9 开始录制主车 {targetCar.name} | 文件: {csvPath}");
                }
            }
        }

        // F10: 停止录制 + 导出CSV
        if (Input.GetKeyDown(KeyCode.F10))
        {
            if (isRecording)
            {
                isRecording = false;
                File.WriteAllText(csvPath, csvData.ToString());
                Debug.Log($"[SysData] F10 停止录制,已导出 {csvData.Length} 字节 → {csvPath}");
            }
        }

        if (isRecording && targetCar != null)
        {
            timer += Time.deltaTime;
            if (timer >= recordInterval)
            {
                RecordVehicleData();
                timer = 0f;
            }
        }
    }

    void RecordVehicleData()
    {
        string aiState = targetAI != null ? targetAI.currentState.ToString() : "Manual";
        bool hasObs = targetAI != null ? targetAI.obstacleDetected : false;
        
        Rigidbody rb = targetCar.GetComponent<Rigidbody>();
        float speedKmh = rb != null ? rb.velocity.magnitude * 3.6f : 0f;

        // ======================
        // V2.0 语义数据（基于 a2 真实字段）
        // ======================
        Vector3 carPos = targetCar.transform.position;
        RoadNode currentNode = WorldModel.Instance.GetNearestNode(carPos);
        
        int nodeID = -1;
        string nodeType = "未知";
        if (currentNode != null)
        {
            // 真实字段：Id (大写I)
            nodeID = currentNode.Id;
            
            // 真实字段：NeighborIds (大写N/I，List<int>)
            if (currentNode.NeighborIds != null)
            {
                if (currentNode.NeighborIds.Count >= 3)
                {
                    nodeType = "路口";
                }
                else if (currentNode.NeighborIds.Count == 2)
                {
                    nodeType = "路段";
                }
                else
                {
                    nodeType = "端点";
                }
            }
        }
        
        csvData.AppendLine($"{Time.time:F2},{targetCar.transform.position.x:F2},{targetCar.transform.position.z:F2}," +
                           $"{speedKmh:F2},{aiState},{nodeID},{nodeType}");
    }

    void ExportRoadMap()
    {
        if (roadGen == null || roadGen.nodes.Count == 0) return;
        MapData data = new MapData();
        foreach (var node in roadGen.nodes)
        {
            data.nodes.Add(new NodeData { id = node.id, x = node.position.x, y = node.position.y, z = node.position.z });
        }
        File.WriteAllText(jsonPath, JsonUtility.ToJson(data, true));
        Debug.Log($"✅ 路网数据已导出至: {jsonPath}");
    }

    [System.Serializable]
    public class MapData { public List<NodeData> nodes = new List<NodeData>(); }
    [System.Serializable]
    public class NodeData { public int id; public float x, y, z; }
}