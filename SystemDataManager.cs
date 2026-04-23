using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// 仿真系统数据管理器 (中期报告数据采集专用)
/// 功能：导出车辆行驶遥测数据 (CSV) + 导出路网拓扑数据 (JSON)
/// </summary>
public class SystemDataManager : MonoBehaviour
{
    [Header("=== 车辆遥测数据 (生成CSV供Excel画图) ===")]
    public SimpleCarController targetCar;
    public SimpleAutoDrive targetAI;
    public float recordInterval = 0.2f; // 每 0.2 秒记录一次
    private bool isRecording = false;
    private float timer = 0f;
    private StringBuilder csvData = new StringBuilder();

    [Header("=== 路网数据 (生成JSON) ===")]
    public RoadNetworkGenerator roadGen;
    
    private string csvPath;
    private string jsonPath;

    void Start()
    {
        // 数据会保存在你的 Unity 工程的 Assets 同级目录下
        csvPath = Application.dataPath + "/VehicleTelemetry.csv";
        jsonPath = Application.dataPath + "/RoadMapData.json";
        
        // 写入 CSV 表头
        csvData.AppendLine("Timestamp,PosX,PosZ,Speed(km_h),SteeringAngle,AI_State,Obstacle_Detected");

        // 如果没有拖拽赋值，自动寻找场景中的主车和路网
        if (targetCar == null) targetCar = FindObjectOfType<SimpleCarController>();
        if (targetAI == null) targetAI = FindObjectOfType<SimpleAutoDrive>();
        if (roadGen == null) roadGen = FindObjectOfType<RoadNetworkGenerator>();
    }

    void Update()
    {
        // 【按 F9】 开始/停止 录制车辆行驶数据
        if (Input.GetKeyDown(KeyCode.F9))
        {
            isRecording = !isRecording;
            if (!isRecording) 
            {
                File.WriteAllText(csvPath, csvData.ToString());
                Debug.Log($"✅ 车辆遥测数据已导出至: {csvPath}\n快用 Excel 打开生成图表放入中期报告中！");
            }
            else 
            {
                Debug.Log("🔴 开始录制车辆数据...");
            }
        }

        // 【按 F10】 一键导出当前生成的路网数据
        if (Input.GetKeyDown(KeyCode.F10)) 
        {
            ExportRoadMap();
        }

        // 定时记录车辆数据
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
        float speedKmh = targetCar.GetSpeed() * 3.6f;
        
        // 记录格式：时间, X坐标, Z坐标, 车速, 方向盘转角, AI状态, 是否检测到障碍
        csvData.AppendLine($"{Time.time:F2},{targetCar.transform.position.x:F2},{targetCar.transform.position.z:F2}," +
                           $"{speedKmh:F2},{targetCar.currentSteeringAngle:F2},{aiState},{hasObs}");
    }

    void ExportRoadMap()
    {
        if (roadGen == null || roadGen.nodes.Count == 0)
        {
            Debug.LogWarning("⚠️ 尚未生成路网，无法导出。");
            return;
        }
        
        MapData data = new MapData();
        foreach (var node in roadGen.nodes)
        {
            data.nodes.Add(new NodeData { 
                id = node.id, 
                x = node.position.x, 
                y = node.position.y, 
                z = node.position.z 
            });
        }
        
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(jsonPath, json);
        Debug.Log($"✅ 仿真路网拓扑已成功序列化并导出至: {jsonPath}");
    }

    // ================== JSON 序列化数据结构 ==================
    [System.Serializable]
    public class MapData { public List<NodeData> nodes = new List<NodeData>(); }
    
    [System.Serializable]
    public class NodeData { public int id; public float x, y, z; }
}