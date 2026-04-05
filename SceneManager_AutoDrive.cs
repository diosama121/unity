using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 自动驾驶仿真平台 - 场景管理器
/// 功能：统一初始化所有系统，提供Demo路网，管理仿真流程
/// 挂载位置：场景中空的 GameObject，命名为 "SimManager"
/// </summary>
public class SceneManager_AutoDrive : MonoBehaviour
{
    // =============================================
    // Inspector 配置
    // =============================================

    [Header("=== 车辆配置 ===")]
    [Tooltip("将你的车辆 Prefab 拖到这里（需挂载 SimpleCarController + SimpleAutoDrive + RaycastSensor）")]
    public GameObject vehiclePrefab;

    [Tooltip("车辆出生点（可不填，默认用 spawnPosition）")]
    public Transform spawnPoint;

    [Tooltip("车辆出生位置（spawnPoint 为空时使用）")]
    public Vector3 spawnPosition = new Vector3(0, 0.5f, 0);

    [Header("=== 路网配置 ===")]
    [Tooltip("是否在 Start 时自动构建内置 Demo 路网（场景内无路点时使用）")]
    public bool buildDemoRoadNetwork = true;

    [Tooltip("路点间距（米），用于自动生成路网")]
    public float waypointSpacing = 15f;

    [Header("=== 目标点配置 ===")]
    [Tooltip("自动驾驶终点（世界坐标）")]
    public Vector3 destinationPosition = new Vector3(100, 0, 100);

    [Tooltip("是否在 Start 时自动开始导航")]
    public bool autoStartNavigation = true;

    [Header("=== 交通灯配置 ===")]
    [Tooltip("场景中所有交通灯的根物体（可不填，自动查找 Tag=TrafficLight）")]
    public GameObject[] trafficLightObjects;

    [Header("=== 调试 ===")]
    public bool showDebugInfo = true;

    // =============================================
    // 内部引用
    // =============================================

    private GameObject vehicleInstance;
    private SimpleCarController carController;
    private SimpleAutoDrive autoDrive;
    private RaycastSensor sensor;
    private PathPlanner pathPlanner;

    // =============================================
    // 初始化
    // =============================================
    void Start()
    {
        Debug.Log("=== 自动驾驶仿真平台启动 ===");

        InitPathPlanner();

        // 优先用RoadNetworkGenerator
        var generator = FindObjectOfType<RoadNetworkGenerator>();
        if (generator != null)
        {
            // 确保generator也拿到同一个PathPlanner
            generator.pathPlanner = pathPlanner;
            generator.Generate();
            // 目的地改为路网右上角
            destinationPosition = generator.GetFarCornerPosition();
        }
        else if (buildDemoRoadNetwork)
        {
            BuildDemoRoadNetwork();
        }
        else
        {
            pathPlanner.BuildRoadNetworkFromScene();
        }

        SpawnVehicle();

        Debug.Log("=== 场景初始化完成 ===");
    }
    // =============================================
    // 初始化各子系统
    // =============================================

    void InitPathPlanner()
    {
        pathPlanner = FindObjectOfType<PathPlanner>();

        if (pathPlanner == null)
        {
            // 自动创建 PathPlanner GameObject
            GameObject ppGO = new GameObject("PathPlanner");
            pathPlanner = ppGO.AddComponent<PathPlanner>();
            Debug.Log("✅ PathPlanner 自动创建");
        }
        else
        {
            Debug.Log("✅ 找到已有 PathPlanner");
        }
    }

    /// <summary>
    /// 构建内置 Demo 路网（矩形环路 + 中间十字）
    /// 适合快速测试，不需要手动放置路点
    /// </summary>
    void BuildDemoRoadNetwork()
    {
        Debug.Log("🗺️ 构建 Demo 路网...");

        float s = waypointSpacing;

        // ---- 外环（矩形，顺时针）----
        // 下边
        int n0 = pathPlanner.AddWaypoint(new Vector3(0, 0, 0));
        int n1 = pathPlanner.AddWaypoint(new Vector3(s, 0, 0));
        int n2 = pathPlanner.AddWaypoint(new Vector3(s * 2, 0, 0));
        int n3 = pathPlanner.AddWaypoint(new Vector3(s * 3, 0, 0));
        int n4 = pathPlanner.AddWaypoint(new Vector3(s * 4, 0, 0));

        // 右边
        int n5 = pathPlanner.AddWaypoint(new Vector3(s * 4, 0, s));
        int n6 = pathPlanner.AddWaypoint(new Vector3(s * 4, 0, s * 2));
        int n7 = pathPlanner.AddWaypoint(new Vector3(s * 4, 0, s * 3));
        int n8 = pathPlanner.AddWaypoint(new Vector3(s * 4, 0, s * 4));

        // 上边
        int n9 = pathPlanner.AddWaypoint(new Vector3(s * 3, 0, s * 4));
        int n10 = pathPlanner.AddWaypoint(new Vector3(s * 2, 0, s * 4));
        int n11 = pathPlanner.AddWaypoint(new Vector3(s, 0, s * 4));
        int n12 = pathPlanner.AddWaypoint(new Vector3(0, 0, s * 4));

        // 左边
        int n13 = pathPlanner.AddWaypoint(new Vector3(0, 0, s * 3));
        int n14 = pathPlanner.AddWaypoint(new Vector3(0, 0, s * 2));
        int n15 = pathPlanner.AddWaypoint(new Vector3(0, 0, s));

        // ---- 中间十字 ----
        int c0 = pathPlanner.AddWaypoint(new Vector3(s * 2, 0, s));
        int c1 = pathPlanner.AddWaypoint(new Vector3(s * 2, 0, s * 2));  // 中心
        int c2 = pathPlanner.AddWaypoint(new Vector3(s * 2, 0, s * 3));
        int c3 = pathPlanner.AddWaypoint(new Vector3(s, 0, s * 2));
        int c4 = pathPlanner.AddWaypoint(new Vector3(s * 3, 0, s * 2));

        // ---- 连接外环 ----
        pathPlanner.ConnectWaypoints(n0, n1);
        pathPlanner.ConnectWaypoints(n1, n2);
        pathPlanner.ConnectWaypoints(n2, n3);
        pathPlanner.ConnectWaypoints(n3, n4);
        pathPlanner.ConnectWaypoints(n4, n5);
        pathPlanner.ConnectWaypoints(n5, n6);
        pathPlanner.ConnectWaypoints(n6, n7);
        pathPlanner.ConnectWaypoints(n7, n8);
        pathPlanner.ConnectWaypoints(n8, n9);
        pathPlanner.ConnectWaypoints(n9, n10);
        pathPlanner.ConnectWaypoints(n10, n11);
        pathPlanner.ConnectWaypoints(n11, n12);
        pathPlanner.ConnectWaypoints(n12, n13);
        pathPlanner.ConnectWaypoints(n13, n14);
        pathPlanner.ConnectWaypoints(n14, n15);
        pathPlanner.ConnectWaypoints(n15, n0);

        // ---- 连接中间十字 ----
        pathPlanner.ConnectWaypoints(n2, c0);   // 下边 → 十字下
        pathPlanner.ConnectWaypoints(c0, c1);
        pathPlanner.ConnectWaypoints(c1, c2);
        pathPlanner.ConnectWaypoints(c2, n10);  // 十字上 → 上边
        pathPlanner.ConnectWaypoints(n14, c3);  // 左边 → 十字左
        pathPlanner.ConnectWaypoints(c3, c1);
        pathPlanner.ConnectWaypoints(c1, c4);
        pathPlanner.ConnectWaypoints(c4, n6);   // 十字右 → 右边
    }

    /// <summary>
    /// 生成车辆并挂载所有必要组件
    /// </summary>
    void SpawnVehicle()
    {
        Vector3 pos = spawnPoint != null ? spawnPoint.position : spawnPosition;

        if (vehiclePrefab != null)
        {
            vehicleInstance = Instantiate(vehiclePrefab, pos, Quaternion.identity);
            Debug.Log($"✅ 车辆 Prefab 已生成于 {pos}");
        }
        else
        {
            // 没有Prefab时，创建一个临时胶囊体代替
            vehicleInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vehicleInstance.transform.position = pos;
            vehicleInstance.name = "Vehicle_Placeholder";
            Debug.LogWarning("⚠️ 未指定车辆 Prefab，使用占位胶囊体。请在 Inspector 中指定 vehiclePrefab！");
        }

        vehicleInstance.name = "AutoDrive_Vehicle";

        // 确保有 Rigidbody
        if (vehicleInstance.GetComponent<Rigidbody>() == null)
            vehicleInstance.AddComponent<Rigidbody>();

        // 挂载控制组件（如果没有）
        carController = GetOrAdd<SimpleCarController>(vehicleInstance);
        sensor = GetOrAdd<RaycastSensor>(vehicleInstance);
        autoDrive = GetOrAdd<SimpleAutoDrive>(vehicleInstance);

        // 关联 PathPlanner
        autoDrive.pathPlanner = pathPlanner;

        // 启用自动驾驶模式
        carController.autoMode = true;

        Debug.Log("✅ 车辆组件挂载完成");
    }

    void StartNavigation()
    {
        if (autoDrive != null)
        {
            autoDrive.SetDestination(destinationPosition);
            Debug.Log($"🚗 开始导航至 {destinationPosition}");
        }
    }

    // =============================================
    // Update：摄像机跟随
    // =============================================

    void Update()
    {
        HandleHotkeys();
    }

    void HandleHotkeys()
    {
        // M 键：切换手动/自动模式
        if (Input.GetKeyDown(KeyCode.M) && carController != null)
        {
            carController.ToggleMode();
        }

        // R 键：重置车辆位置
        if (Input.GetKeyDown(KeyCode.R) && vehicleInstance != null)
        {
            // 重置位置
            Vector3 pos = spawnPoint != null ? spawnPoint.position : spawnPosition;
            vehicleInstance.transform.position = pos;
            vehicleInstance.transform.rotation = Quaternion.identity;

            Rigidbody rb = vehicleInstance.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // 重置后重新开始导航，不能让车停死
            if (autoDrive != null)
            {
                autoDrive.currentState = SimpleAutoDrive.DriveState.Idle;
                var pathField = typeof(SimpleAutoDrive).GetField("path",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pathField != null) pathField.SetValue(autoDrive, null);

                Invoke(nameof(StartNavigation), 0.3f); // 稍等物理稳定再导航
            }

            Debug.Log("🔄 车辆已重置，重新导航");
        }

        // N 键：重新导航到目标点
        if (Input.GetKeyDown(KeyCode.N) && autoDrive != null)
        {
            autoDrive.SetDestination(destinationPosition);
            Debug.Log("🗺️ 重新开始导航");
        }
        
    }

    // =============================================
    // 工具方法
    // =============================================

    T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        if (comp == null)
        {
            comp = go.AddComponent<T>();
            Debug.Log($"  → 自动添加组件: {typeof(T).Name}");
        }
        return comp;
    }

    // =============================================
    // OnGUI 调试面板
    // =============================================

    void OnGUI()
    {
        if (!showDebugInfo) return;

    GUI.Box(new Rect(10, 10, 280, 450), "");
GUILayout.BeginArea(new Rect(15, 15, 250, 400));

        GUILayout.Label("🚗 自动驾驶仿真平台",
            new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold });
        GUILayout.Space(5);

        if (carController != null)
        {
            GUILayout.Label($"速度: {carController.GetSpeed() * 3.6f:F1} km/h");
            GUILayout.Label($"模式: {(carController.autoMode ? "🤖 自动驾驶" : "🎮 手动控制")}");
        }

        if (autoDrive != null)
        {
            GUILayout.Label($"状态: {autoDrive.currentState}");
            GUILayout.Label($"路点: {autoDrive.currentWaypointIndex} | 距离: {autoDrive.distanceToNextWaypoint:F1}m");
            GUILayout.Label($"障碍物: {(autoDrive.obstacleDetected ? "⚠️ 检测到" : "✅ 无")}");
            GUILayout.Label($"交通灯: {autoDrive.trafficLightState}");
        }

        GUILayout.Space(5);
        GUILayout.Label("─── 按键说明 ───",
            new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.gray } });
        GUILayout.Label("[M] 切换手动/自动模式",
            new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.yellow } });
        GUILayout.Label("[R] 重置车辆位置并重新导航",
            new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.yellow } });
        GUILayout.Label("[N] 重新导航到目标点",
            new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.yellow } });
        GUILayout.Label("[WASD] 手动模式下控制方向",
            new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.yellow } });
        GUILayout.Label("[Space] 手动模式下刹车",
            new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.yellow } });
        GUILayout.Label("[C] 切换摄像机视角",
            new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = Color.yellow } });
        GUILayout.EndArea();
    }
}
