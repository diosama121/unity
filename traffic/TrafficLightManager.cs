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
        public int directionIndex;
        public int phaseId; // 【V2.3】世界坐标方位ID，与WorldModel.StopLine.AssociatedPhaseId绝对对齐
        public Vector3 position;
        public GameObject gameObject;
        public TrafficLightController controller;
        public Light lightComponent;
        public string currentState = "Red";
        
        public IntersectionState lastSyncedState = IntersectionState.Uncontrolled;
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
            int connections = node.neighbors.Count;
            if (connections < 3) continue;
            if (Random.value > placementChance) continue;

            // 【Phase 3 真相位】为每个入口方向创建独立交通灯
            for (int dirIdx = 0; dirIdx < node.neighbors.Count; dirIdx++)
            {
                PlaceTrafficLightAtNode(node, dirIdx);
                placed++;
            }
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
        if (root != null) { if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject); }
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
    void PlaceTrafficLightAtNode(RoadNetworkGenerator.WaypointNode node, int directionIndex)
    {
        int neighborId = node.neighbors[directionIndex];
        Vector3 neighborPos = roadGen.nodes[neighborId].position;
        
        Vector3 facingDir = (neighborPos - node.position);
        facingDir.y = 0;
        if (facingDir == Vector3.zero) facingDir = Vector3.forward;
        facingDir.Normalize();

        float groundY = WorldModel.Instance != null ? WorldModel.Instance.GetUnifiedHeight(node.position.x, node.position.z) : 0f;
        Vector3 basePos = new Vector3(node.position.x, groundY, node.position.z) + Vector3.up * heightOffset;
        Vector3 rightDir = Vector3.Cross(Vector3.up, facingDir).normalized;
        
        float safeOffset = offsetFromCenter + 1.5f;
        Vector3 spawnPos = basePos + facingDir * safeOffset + rightDir * safeOffset;
        
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

        tlObj.name = $"TrafficLight_Node{node.id}_Dir{directionIndex}";

        // 添加/获取 TrafficLightController
        TrafficLightController controller = tlObj.GetComponent<TrafficLightController>();
        if (controller == null)
            controller = tlObj.AddComponent<TrafficLightController>();

        // 配置时间
        controller.redDuration = redDuration;
        controller.yellowDuration = yellowDuration;
        controller.greenDuration = greenDuration;

        // 【V2.3 真相位核心】基于世界坐标方位，绝对锁定 NS/EW
        Vector3 approachDir = (node.position - neighborPos);
        approachDir.y = 0;
        bool isNS = Mathf.Abs(approachDir.z) > Mathf.Abs(approachDir.x);
        int phaseId = node.id * 10 + (isNS ? 0 : 1);
        float phaseOffset = isNS ? 0f : (greenDuration + yellowDuration);
        controller.SetPhaseOffset(phaseOffset);

        // 添加Light组件（挂在子物体上）
        Light lightComp = AddLightToTrafficLight(tlObj);

        // 把Light传给Controller，让Controller直接控制颜色
        controller.managedLight = lightComp;
        controller.redColor = redColor;
        controller.yellowColor = yellowColor;
        controller.greenColor = greenColor;

        // 添加碰撞体（供RaycastSensor检测）
        if (tlObj.GetComponent<Collider>() == null)
        {
            BoxCollider col = tlObj.AddComponent<BoxCollider>();
            // 【修复】将灯柱碰撞体改为 0.5x0.5，防止侵占转弯车道
            col.size = new Vector3(0.5f, 4f, 0.5f); 
            col.center = new Vector3(0, 2f, 0);
        }

        // 记录实例
        var instance = new TrafficLightInstance
        {
            nodeId = node.id,
            directionIndex = directionIndex,
            phaseId = phaseId,
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
        // 如果白皮书未就绪，不执行握手
        if (WorldModel.Instance == null) return;

        foreach (var tl in trafficLights)
        {
            if (tl.controller == null || tl.lightComponent == null) continue;

            string state = tl.controller.GetCurrentState();
            tl.currentState = state;

            IntersectionState newState = state switch
            {
                "Red" => IntersectionState.RedLight,
                "Yellow" => IntersectionState.YellowLight,
                "Green" => IntersectionState.GreenLight,
                _ => IntersectionState.Uncontrolled
            };

            if (tl.lastSyncedState != newState)
            {
                // 已注释：多灯共享同一nodeId互相覆写，改为只使用SetPhaseState
                // WorldModel.Instance.SetIntersectionState(tl.nodeId, newState);
                WorldModel.Instance.SetPhaseState(tl.phaseId, newState);
                tl.lastSyncedState = newState;
            }
            
            // 同步灯光
            switch (state)
            {
                case "Red": tl.lightComponent.color = redColor; break;
                case "Yellow": tl.lightComponent.color = yellowColor; break;
                case "Green": tl.lightComponent.color = greenColor; break;
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
