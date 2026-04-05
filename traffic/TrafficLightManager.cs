using UnityEngine;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// 交通灯管理器
/// 功能：在路口自动放置交通灯，管理红绿灯状态，与自动驾驶联动
/// 挂载位置：与 RoadNetworkGenerator 同一个 GameObject
/// </summary>
public class TrafficLightManager : MonoBehaviour
{
    // =============================================
    // Inspector 配置
    // =============================================

    [Header("=== 交通灯Prefab ===")]
    [Tooltip("交通灯Prefab（资源包里的，需要挂载TrafficLightController）")]
    public GameObject trafficLightPrefab;

    [Header("=== 放置配置 ===")]
    [Tooltip("在十字路口放置交通灯的概率 0-1")]
    [Range(0f, 1f)]
    public float placementChance = 0.6f;

    [Tooltip("交通灯放置的随机种子（和路网种子一致保证复现）")]
    public int placementSeed = 42;

    [Tooltip("交通灯距路口中心的偏移距离")]
    public float offsetFromCenter = 3f;

    [Tooltip("交通灯高度")]
    public float heightOffset = 0f;

    [Header("=== 灯光配置 ===")]
    [Tooltip("红灯Light颜色")]
    public Color redColor = new Color(1f, 0.1f, 0.1f);

    [Tooltip("黄灯Light颜色")]
    public Color yellowColor = new Color(1f, 0.8f, 0.1f);

    [Tooltip("绿灯Light颜色")]
    public Color greenColor = new Color(0.1f, 1f, 0.1f);

    [Tooltip("灯光强度")]
    public float lightIntensity = 2f;

    [Tooltip("灯光范围")]
    public float lightRange = 15f;

    [Header("=== 时间配置 ===")]
    public float redDuration = 8f;
    public float yellowDuration = 2f;
    public float greenDuration = 8f;

    [Header("=== 调试 ===")]
    public bool showDebugLog = true;

    // =============================================
    // 内部数据
    // =============================================

    // 已放置的交通灯列表
    private List<TrafficLightInstance> trafficLights = new List<TrafficLightInstance>();

    // 交通灯根节点
    private GameObject trafficLightRoot;

    private RoadNetworkGenerator roadGen;
    private ProceduralRoadBuilder roadBuilder;

    // 交通灯实例数据
    public class TrafficLightInstance
    {
        public int nodeId;
        public Vector3 position;
        public GameObject gameObject;
        public TrafficLightController controller;
        public Light lightComponent;
        public string currentState = "Red";
    }

    // =============================================
    // 初始化
    // =============================================

    void Start()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();
        roadBuilder = GetComponent<ProceduralRoadBuilder>();
    }

    // =============================================
    // 公共接口
    // =============================================

    /// <summary>
    /// 放置所有交通灯（由RoadNetworkGenerator.Generate()调用）
    /// </summary>
    public void PlaceTrafficLights()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();

        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count == 0)
        {
            Debug.LogError("TrafficLightManager: 未找到路网数据！");
            return;
        }

        // 清理旧交通灯
        ClearTrafficLights();

        // 创建根节点
        trafficLightRoot = new GameObject("TrafficLights");
        trafficLightRoot.transform.SetParent(transform);

        // 固定种子保证复现
        Random.InitState(placementSeed);

        int placed = 0;

        foreach (var node in roadGen.nodes)
        {
            // 只在十字路口和T字路口放
            int connections = node.neighbors.Count;
            if (connections < 3) continue;

            // 随机决定是否放置
            if (Random.value > placementChance) continue;

            // 在路口放置交通灯
            PlaceTrafficLightAtNode(node);
            placed++;
        }

        if (showDebugLog)
            Debug.Log($"✅ 交通灯放置完成：{placed} 个路口");
    }

    /// <summary>
    /// 清理所有交通灯
    /// </summary>
    public void ClearTrafficLights()
    {
        trafficLights.Clear();

        Transform root = transform.Find("TrafficLights");
        if (root != null) DestroyImmediate(root.gameObject);
    }

    /// <summary>
    /// 获取指定位置最近的交通灯状态
    /// </summary>
    public string GetNearestTrafficLightState(Vector3 position, float maxDistance = 20f)
    {
        float minDist = float.MaxValue;
        string state = "None";

        foreach (var tl in trafficLights)
        {
            float dist = Vector3.Distance(
                new Vector3(position.x, 0, position.z),
                new Vector3(tl.position.x, 0, tl.position.z)
            );

            if (dist < minDist && dist < maxDistance)
            {
                minDist = dist;
                state = tl.currentState;
            }
        }

        return state;
    }

    // =============================================
    // 放置单个交通灯
    // =============================================
 public List<GameObject> GetAllPlacedLights()
{
    return trafficLights.Select(t => t.gameObject).ToList();
}
    void PlaceTrafficLightAtNode(RoadNetworkGenerator.WaypointNode node)
    {
        // 计算放置位置（路口角落，偏移到路边）
        Vector3 basePos = node.position + Vector3.up * heightOffset;

        // 取第一个邻居方向作为朝向参考
        Vector3 facingDir = Vector3.forward;
        if (node.neighbors.Count > 0)
        {
            facingDir = (roadGen.nodes[node.neighbors[0]].position - node.position).normalized;
            facingDir.y = 0;
        }

        // 偏移到路口右侧
        Vector3 rightDir = Vector3.Cross(Vector3.up, facingDir).normalized;
        Vector3 spawnPos = basePos + facingDir * offsetFromCenter + rightDir * offsetFromCenter;

        // 创建交通灯GameObject
        GameObject tlObj;
        if (trafficLightPrefab != null)
        {
            tlObj = Instantiate(trafficLightPrefab, spawnPos,
                Quaternion.LookRotation(-facingDir), trafficLightRoot.transform);
        }
        else
        {
            // 无Prefab时创建占位杆子
            tlObj = CreatePlaceholderLight(spawnPos, facingDir);
        }

        tlObj.name = $"TrafficLight_Node{node.id}";

        // 添加/获取 TrafficLightController
        TrafficLightController controller = tlObj.GetComponent<TrafficLightController>();
        if (controller == null)
            controller = tlObj.AddComponent<TrafficLightController>();

        // 配置时间
        controller.redDuration = redDuration;
        controller.yellowDuration = yellowDuration;
        controller.greenDuration = greenDuration;

        // 错开初始相位，避免所有路口同时变灯
        float phaseOffset = Random.Range(0f, redDuration + yellowDuration + greenDuration);
        controller.SetPhaseOffset(phaseOffset);

        // 添加Light组件（挂在子物体上）
        Light lightComp = AddLightToTrafficLight(tlObj);

        // 添加碰撞体（供RaycastSensor检测）
        if (tlObj.GetComponent<Collider>() == null)
        {
            BoxCollider col = tlObj.AddComponent<BoxCollider>();
            col.size = new Vector3(0.5f, 2f, 0.5f);
            col.center = new Vector3(0, 1f, 0);
        }

        // 记录实例
        var instance = new TrafficLightInstance
        {
            nodeId = node.id,
            position = spawnPos,
            gameObject = tlObj,
            controller = controller,
            lightComponent = lightComp,
            currentState = "Red"
        };

        trafficLights.Add(instance);
    }

    /// <summary>
    /// 在交通灯上添加Light组件
    /// </summary>
    Light AddLightToTrafficLight(GameObject parent)
    {
        GameObject lightObj = new GameObject("TrafficLight_Light");
        lightObj.transform.SetParent(parent.transform);
        lightObj.transform.localPosition = new Vector3(0, 3f, 0);

        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.intensity = lightIntensity;
        light.range = lightRange;
        light.color = redColor; // 初始红色

        return light;
    }

    /// <summary>
    /// 无Prefab时创建简单占位交通灯（杆子+灯头）
    /// </summary>
    GameObject CreatePlaceholderLight(Vector3 pos, Vector3 facing)
    {
        GameObject root = new GameObject("TrafficLight_Placeholder");
        root.transform.position = pos;
        root.transform.rotation = Quaternion.LookRotation(-facing);

        // 杆子
        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.transform.SetParent(root.transform);
        pole.transform.localPosition = new Vector3(0, 1.5f, 0);
        pole.transform.localScale = new Vector3(0.1f, 1.5f, 0.1f);
        pole.GetComponent<MeshRenderer>().material.color = Color.gray;
        DestroyImmediate(pole.GetComponent<Collider>());

        // 灯头
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.transform.SetParent(root.transform);
        head.transform.localPosition = new Vector3(0, 3.2f, 0);
        head.transform.localScale = new Vector3(0.3f, 0.8f, 0.3f);
        head.GetComponent<MeshRenderer>().material.color = Color.black;
        DestroyImmediate(head.GetComponent<Collider>());

        return root;
    }

    // =============================================
    // Update：同步灯光颜色
    // =============================================

    void Update()
    {
        foreach (var tl in trafficLights)
        {
            if (tl.controller == null || tl.lightComponent == null) continue;

            string state = tl.controller.GetCurrentState();
            tl.currentState = state;

            // 根据状态更新Light颜色
            switch (state)
            {
                case "Red":
                    tl.lightComponent.color = redColor;
                    break;
                case "Yellow":
                    tl.lightComponent.color = yellowColor;
                    break;
                case "Green":
                    tl.lightComponent.color = greenColor;
                    break;
            }
        }
    }

    // =============================================
    // Gizmos
    // =============================================

    void OnDrawGizmos()
    {
        foreach (var tl in trafficLights)
        {
            if (tl == null) continue;

            Color c = tl.currentState == "Red" ? Color.red :
                      tl.currentState == "Yellow" ? Color.yellow :
                      tl.currentState == "Green" ? Color.green : Color.white;

            Gizmos.color = c;
            Gizmos.DrawWireSphere(tl.position + Vector3.up * 3f, 1f);
        }
    }
}
