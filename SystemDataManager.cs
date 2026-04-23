using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;

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
        csvPath = Application.dataPath + "/VehicleTelemetry.csv";
        jsonPath = Application.dataPath + "/RoadMapData.json";
        
        // 每次启动重置并写入表头
        csvData.Clear();
        csvData.AppendLine("Timestamp,PosX,PosZ,Speed(km_h),AI_State,Obstacle_Detected");
        
        if (roadGen == null) roadGen = FindObjectOfType<RoadNetworkGenerator>();
    }

    void Update()
    {
        // 【按 F9】 开始/停止 录制
        if (Input.GetKeyDown(KeyCode.F9))
        {
            isRecording = !isRecording;
            
            if (isRecording) 
            {
                // 按下录制时，强制再次寻找场景里的车！
                if (targetCar == null) targetCar = FindObjectOfType<SimpleCarController>();
                if (targetAI == null) targetAI = FindObjectOfType<SimpleAutoDrive>();

                if (targetCar == null)
                {
                    Debug.LogError("❌ 找不到主车！数据录制失败。请确保场景中有挂载 SimpleCarController 的车。");
                    isRecording = false;
                }
                else
                {
                    Debug.Log("🔴 开始录制车辆数据... (每0.2秒记录一次)");
                }
            }
            else 
            {
                File.WriteAllText(csvPath, csvData.ToString());
                Debug.Log($"✅ 车辆遥测数据已导出至: {csvPath}");
            }
        }

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
        
        // 暴力获取物理真实速度，绝对不会报错
        Rigidbody rb = targetCar.GetComponent<Rigidbody>();
        float speedKmh = rb != null ? rb.velocity.magnitude * 3.6f : 0f;
        
        csvData.AppendLine($"{Time.time:F2},{targetCar.transform.position.x:F2},{targetCar.transform.position.z:F2}," +
                           $"{speedKmh:F2},{aiState},{hasObs}");
                           
        // 在控制台打印提示，让你知道数据确实正在被写入
        Debug.Log($"正在记录 -> 车速: {speedKmh:F1} km/h, 状态: {aiState}");
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