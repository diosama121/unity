using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 中央交通调度器
/// 功能：负责在路网上批量生成纯数学 NPC，并下发 CatmullRom 轨道飞行任务
/// </summary>
public class TrafficManager : MonoBehaviour
{
    [Header("NPC 配置")]
    public GameObject[] normalNpcPrefabs;
    public GameObject[] emergencyNpcPrefabs; // 优先组（救护车/警车）
    [Range(0f, 1f)]
    public float emergencySpawnRate = 0.1f; // 10%概率
    public int npcCount = 20;
    public LayerMask npcSpawnBlockMask = 0; // 防撞检测层（默认关闭；上线后可设为 Vehicle 层）
    public float spawnHeightOffset = 0.3f;  // 出生点 Y 轴抬高（避免 CheckSphere 碰地形）

    [Header("自适应调度")]
    public bool dynamicScheduling = true;
    public float schedulingCheckInterval = 2f;
    private float schedTimer = 0f;
    private float lastFps = 60f;

    private List<SimpleAutoDrive> npcVehicles = new List<SimpleAutoDrive>();
    /// <summary>当前活跃的紧急车辆（救护车），供 EmergencyYieldHandler 使用</summary>
    [HideInInspector] public SimpleAutoDrive activeEmergencyVehicle;
    public IReadOnlyList<SimpleAutoDrive> ActiveNPCs => npcVehicles;
    private RoadNetworkGenerator roadGen;
    private PathPlanner pathPlanner;

    private bool _hasSpawned = false;

    public void ResetSpawnState() { _hasSpawned = false; }

   public void ClearAllNPCs()
    {
        foreach (var npc in npcVehicles)
        {
            if (npc != null && npc.gameObject != null)
            {
                Destroy(npc.gameObject);
            }
        }
        npcVehicles.Clear();
        _hasSpawned = false;
    }

    void Update()
    {
        if (!dynamicScheduling) return;
        schedTimer += Time.unscaledDeltaTime;
        if (schedTimer >= schedulingCheckInterval)
        {
            schedTimer = 0f;
            lastFps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            if (lastFps < 40f)
            {
                int activeCount = npcVehicles.FindAll(n => n != null).Count;
                if (activeCount > 2)
                {
                    // 删除离主相机最远的NPC，避免删掉玩家视野内的车
                    Camera mainCam = Camera.main;
                    Vector3 camPos = mainCam != null ? mainCam.transform.position : Vector3.zero;
                    SimpleAutoDrive farthest = null;
                    float maxDist = -1f;
                    foreach (var npc in npcVehicles)
                    {
                        if (npc == null) continue;
                        float d = Vector3.Distance(npc.transform.position, camPos);
                        if (d > maxDist) { maxDist = d; farthest = npc; }
                    }
                    if (farthest != null)
                    {
                        npcVehicles.Remove(farthest);
                        Destroy(farthest.gameObject);
                        Debug.Log("[TrafficManager] FPS=" + Mathf.RoundToInt(lastFps) + " < 40, despawning farthest NPC (" + maxDist.ToString("F0") + "m away). Remaining: " + npcVehicles.Count);
                    }
                }
            }

            // NPC生命周期管理：检查终点到达，回收并重生
            RecycleFinishedNPCs();
        }
    }

    private void RecycleFinishedNPCs()
    {
        if (WorldModel.Instance == null || WorldModel.Instance.GlobalLanes == null || WorldModel.Instance.GlobalLanes.Count == 0)
            return;

        for (int i = npcVehicles.Count - 1; i >= 0; i--)
        {
            SimpleAutoDrive npc = npcVehicles[i];
            if (npc == null || npc.gameObject == null)
            {
                npcVehicles.RemoveAt(i);
                continue;
            }

            // ★ 终极修复：绝对不要因为车速为0就把车重生了！
            // 停车和死锁交给 SimpleAutoDrive 自己的大脑去处理。
            // 这里的 TrafficManager 只负责给"掉出地图"的灵异车辆收尸。
            bool reachedEnd = false;
            
            // 如果车子掉入虚空（比如 Y 轴小于 -20），才判定为需要回收
            if (npc.transform.position.y < -20f)
            {
                reachedEnd = true;
            }

            if (reachedEnd)
            {
                // 回收并重生到新车道
                GameObject npcObj = npc.gameObject;
                SimpleCarController controller = npcObj.GetComponent<SimpleCarController>();
                if (controller == null) controller = npcObj.GetComponentInChildren<SimpleCarController>();

                if (RespawnNPCOnNewLane(npc, npcObj, controller)) {
                    // 成功重生
                } else {
                    npcVehicles.RemoveAt(i);
                    Destroy(npcObj);
                }
            }
        }
    }

    private bool RespawnNPCOnNewLane(SimpleAutoDrive npc, GameObject npcObj, SimpleCarController controller)
    {
        if (WorldModel.Instance == null) return false;

        // 收集可用车道
        List<Lane> availableLanes = new List<Lane>();
        foreach (var kvp in WorldModel.Instance.GlobalLanes)
        {
            if (kvp.Value.CenterSpline != null && kvp.Value.CenterSpline.TotalLength > 10f)
                availableLanes.Add(kvp.Value);
        }
        if (availableLanes.Count == 0) return false;

        // 随机选一条新车道，远离当前位置
        Lane chosenLane = availableLanes[Random.Range(0, availableLanes.Count)];
        float startT = Random.Range(0.1f, 0.9f);
        Vector3 rawPos = chosenLane.CenterSpline.GetPoint(startT);
        Vector3 spawnPos = rawPos + Vector3.up * spawnHeightOffset;

        if (Physics.CheckSphere(spawnPos, 4.0f, npcSpawnBlockMask))
            return false;

        Vector3 forwardPt = chosenLane.CenterSpline.GetPoint(Mathf.Clamp01(startT + 0.02f));
        Vector3 forwardDir = (forwardPt - rawPos).normalized;
        if (forwardDir.sqrMagnitude < 0.001f) forwardDir = Vector3.forward;

        npcObj.transform.position = rawPos;
        npcObj.transform.rotation = Quaternion.LookRotation(forwardDir);
        if (controller != null) controller.currentSpeed = 0f;

        float startDist = startT * chosenLane.CenterSpline.TotalLength;
        npc.SetPath(new List<int> { chosenLane.LaneId }, startDist);
        return true;
    }

  public void SpawnNPCs()
    {
        if (_hasSpawned) { Debug.Log("[TrafficManager] NPC 已生成，跳过"); return; }

        // 城市和乡村一视同仁，统一走贴线生成
        SpawnNPCsOnLanes();
    }

    public void SpawnNPCsFromNodes()
    {
        if (_hasSpawned) return;

        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        pathPlanner = FindObjectOfType<PathPlanner>();

        if (normalNpcPrefabs == null || normalNpcPrefabs.Length == 0)
        {
            Debug.LogError("[TrafficManager] normalNpcPrefabs 数组为空！");
            return;
        }
        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count < 2)
        {
            Debug.LogWarning("[TrafficManager] 路网数据不可用，无法通过节点生成 NPC。");
            return;
        }
        if (pathPlanner == null)
        {
            Debug.LogWarning("[TrafficManager] PathPlanner 未找到，无法通过节点生成 NPC。");
            return;
        }

        ClearAllNPCs();

        List<RoadNetworkGenerator.WaypointNode> shuffledNodes = new List<RoadNetworkGenerator.WaypointNode>(roadGen.nodes);
        ShuffleList(shuffledNodes);

        int spawnedCount = 0;
        int attempts = 0;
        int maxAttempts = npcCount * 5;
        HashSet<int> usedStartIds = new HashSet<int>();

        while (spawnedCount < npcCount && attempts < maxAttempts)
        {
            attempts++;
            var startNode = shuffledNodes[Random.Range(0, shuffledNodes.Count)];
            if (usedStartIds.Contains(startNode.id)) continue;
            usedStartIds.Add(startNode.id);

            var targetNode = GetFarNode(startNode);
            if (targetNode == null) continue;

            GameObject chosenPrefab = normalNpcPrefabs[Random.Range(0, normalNpcPrefabs.Length)];
            GameObject npcObj = Instantiate(chosenPrefab, startNode.position, Quaternion.identity);
            npcObj.name = $"NPC_Vehicle_{spawnedCount}";

            SimpleCarController controller = npcObj.GetComponent<SimpleCarController>();
            if (controller == null) controller = npcObj.GetComponentInChildren<SimpleCarController>();
            if (controller != null) controller.isNPC = true;

            SimpleAutoDrive autoDrive = npcObj.GetComponent<SimpleAutoDrive>();
            if (autoDrive == null) autoDrive = npcObj.GetComponentInChildren<SimpleAutoDrive>();
            if (autoDrive != null)
            {
                CatmullRomSpline spline = pathPlanner.PlanPathSpline(startNode.position, targetNode.position);
                if (spline != null && spline.TotalLength > 0)
                {
                    Vector3 exactStartPos = spline.GetPoint(0f);
                    if (WorldModel.Instance != null)
                        exactStartPos.y = WorldModel.Instance.GetUnifiedHeight(exactStartPos.x, exactStartPos.z);
                    npcObj.transform.position = exactStartPos;

                    Vector3 startTangent = (spline.GetPoint(0.01f) - spline.GetPoint(0f)).normalized;
                    if (startTangent != Vector3.zero)
                        npcObj.transform.rotation = Quaternion.LookRotation(startTangent);

                    autoDrive.SetSplinePath(spline, targetNode.id);
                    npcVehicles.Add(autoDrive);
                    spawnedCount++;
                }
                else
                {
                    Destroy(npcObj);
                }
            }
            else
            {
                Destroy(npcObj);
            }
        }

        _hasSpawned = spawnedCount > 0;
        Debug.Log($"[TrafficManager] 节点生成完成。计划: {npcCount}，实际生成: {spawnedCount} 辆。");
    }

  public void SpawnNPCsOnLanes()
    {
        if (_hasSpawned) { Debug.Log("[TrafficManager] NPC 已生成，跳过 SpawnNPCsOnLanes"); return; }

        if (WorldModel.Instance == null || WorldModel.Instance.GlobalLanes == null || WorldModel.Instance.GlobalLanes.Count == 0)
        {
            Debug.LogWarning("[TrafficManager] 未获取到全局车道数据，无法生成 NPC。");
            return;
        }

        if (normalNpcPrefabs == null || normalNpcPrefabs.Length == 0)
        {
            Debug.LogError("[TrafficManager] normalNpcPrefabs 数组为空！请在 Inspector 拖入 NPC 车辆预制体。");
            return;
        }

        // 收集所有有效车道（长度 > 10m）
        List<Lane> availableLanes = new List<Lane>();
        foreach (var kvp in WorldModel.Instance.GlobalLanes)
        {
            if (kvp.Value.CenterSpline != null && kvp.Value.CenterSpline.TotalLength > 10f)
            {
                availableLanes.Add(kvp.Value);
            }
        }

        if (availableLanes.Count == 0)
        {
            Debug.LogWarning("[TrafficManager] 没有足够长的车道可供生成 NPC。");
            return;
        }

        ClearAllNPCs();

        int spawnedCount = 0;
        int attempts = 0;
        int maxAttempts = npcCount * 4;

        // 诊断计数器
        int failCheckSphere = 0, failAutoDriveNull = 0;

        while (spawnedCount < npcCount && attempts < maxAttempts)
        {
            attempts++;

            // 1. 随机选一条车道
            Lane randomLane = availableLanes[Random.Range(0, availableLanes.Count)];

            // 2. 在车道上随机取进度 T（避开首尾，防止出生在路口中间）
            float startT = Random.Range(0.1f, 0.9f);

            // 3. 计算出生坐标（抬高一点，避免 CheckSphere 碰地形/路面 collider）
            Vector3 rawPos = randomLane.CenterSpline.GetPoint(startT);
            Vector3 spawnPos = rawPos + Vector3.up * spawnHeightOffset;

            // 4. 防重叠检测：使用配置的 LayerMask
            if (Physics.CheckSphere(spawnPos, 4.0f, npcSpawnBlockMask))
            {
                failCheckSphere++;
                continue;
            }

            // 5. 计算朝向（取前方微小偏移点的切线）
            Vector3 forwardPt = randomLane.CenterSpline.GetPoint(Mathf.Clamp01(startT + 0.02f));
            Vector3 forwardDir = (forwardPt - rawPos).normalized;
            if (forwardDir.sqrMagnitude < 0.001f) forwardDir = Vector3.forward;
            Quaternion spawnRot = Quaternion.LookRotation(forwardDir);

            // 6. 实例化（用原始位置，物理学由车自身处理）
            GameObject chosenPrefab = normalNpcPrefabs[Random.Range(0, normalNpcPrefabs.Length)];
            GameObject npc = Instantiate(chosenPrefab, rawPos, spawnRot);
            npc.name = $"NPC_Car_{spawnedCount}";

            // 7. 注入 NPC 身份
            SimpleCarController carController = npc.GetComponent<SimpleCarController>();
            if (carController == null) carController = npc.GetComponentInChildren<SimpleCarController>();
            if (carController != null)
            {
                carController.isNPC = true;
            }

            // 8. 给大脑注入初始记忆
            SimpleAutoDrive autoDrive = npc.GetComponent<SimpleAutoDrive>();
            if (autoDrive == null) autoDrive = npc.GetComponentInChildren<SimpleAutoDrive>();
            if (autoDrive != null)
            {
                autoDrive.isPlayerControlled = false;
                // 统一走 SetPath，避免半初始化导致 StartPath/_isTrajectoryLocked 未生效
                float startDist = startT * randomLane.CenterSpline.TotalLength;
                autoDrive.SetPath(new List<int>{randomLane.LaneId}, startDist);
                npcVehicles.Add(autoDrive);
                spawnedCount++;
            }
            else
            {
                failAutoDriveNull++;
                Destroy(npc);
            }
        }

        _hasSpawned = spawnedCount > 0;
        Debug.Log($"[TrafficManager] NPC 投放完成。计划: {npcCount}，实际生成贴线车辆: {spawnedCount} 辆。| 总尝试={attempts} CheckSphere拦截={failCheckSphere} 缺AutoDrive={failAutoDriveNull} 可用车道={availableLanes.Count}");
    }

    public GameObject SpawnEmergencyVehicle(Vector3 nearPosition)
    {
        GameObject prefab = null;
        if (emergencyNpcPrefabs != null && emergencyNpcPrefabs.Length > 0)
        {
            prefab = emergencyNpcPrefabs[Random.Range(0, emergencyNpcPrefabs.Length)];
        }
        if (prefab == null && normalNpcPrefabs != null && normalNpcPrefabs.Length > 0)
        {
            prefab = normalNpcPrefabs[0];
        }
        if (prefab == null)
        {
            Debug.LogError("TrafficManager: 没有可用预制体生成紧急车辆！");
            return null;
        }

        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        pathPlanner = FindObjectOfType<PathPlanner>();

        RoadNode nearestNode = null;
        if (WorldModel.Instance != null)
        {
            nearestNode = WorldModel.Instance.GetNearestNode(nearPosition);
        }

        Vector3 spawnPos = nearPosition;
        if (nearestNode != null)
        {
            spawnPos = nearestNode.WorldPos;
        }

        if (WorldModel.Instance != null)
        {
            spawnPos.y = WorldModel.Instance.GetUnifiedHeight(spawnPos.x, spawnPos.z);
        }

        Vector3 forward = (nearestNode != null) ? nearestNode.Tangent : Vector3.forward;
        GameObject evObj = Instantiate(prefab, spawnPos, Quaternion.LookRotation(forward));
        evObj.name = "Emergency_Vehicle";

      

        SimpleCarController controller = evObj.GetComponent<SimpleCarController>();
        if (controller == null) controller = evObj.GetComponentInChildren<SimpleCarController>();
        if (controller != null)
        {
            controller.isNPC = false;
            controller.vehiclePriority = VehiclePriority.Emergency;
        }

        SimpleAutoDrive autoDrive = evObj.GetComponent<SimpleAutoDrive>();
        if (autoDrive == null) autoDrive = evObj.GetComponentInChildren<SimpleAutoDrive>();
        if (autoDrive != null)
        {
            autoDrive.isEmergencyVehicle = true; // ★ 接线：标识为救护车，避让系统才能正确识别
            if (pathPlanner != null && WorldModel.Instance != null)
            {
                RoadNode targetNode = GetFarNodeFromWorld(nearestNode);
                if (targetNode != null)
                {
                    CatmullRomSpline spline = pathPlanner.PlanPathSpline(spawnPos, targetNode.WorldPos);
                    if (spline != null && spline.TotalLength > 0)
                    {
                        autoDrive.SetSplinePath(spline, targetNode.Id);
                    }
                }
                else
                {
                    autoDrive.ResetNavigation();
                }
            }
        }

        npcVehicles.Add(autoDrive);
        activeEmergencyVehicle = autoDrive; // 注册为活跃紧急车辆，供让行系统使用
        Debug.Log("TrafficManager: 紧急车辆已生成");
        return evObj;
    }

    private RoadNode GetFarNodeFromWorld(RoadNode startNode)
    {
        if (WorldModel.Instance == null) return null;
        float maxDist = 0;
        RoadNode farNode = null;

        int nodeCount = WorldModel.Instance.NodeCount;
        for (int i = 0; i < 5; i++)
        {
            int randIdx = Random.Range(0, nodeCount);
            RoadNode candidate = WorldModel.Instance.GetNode(randIdx);
            if (candidate == null) continue;
            if (startNode != null && candidate.Id == startNode.Id) continue;
            float dist = Vector3.Distance(startNode != null ? startNode.WorldPos : Vector3.zero, candidate.WorldPos);
            if (dist > maxDist && dist > 20f)
            {
                maxDist = dist;
                farNode = candidate;
            }
        }

        if (farNode == null && nodeCount > 1)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                RoadNode candidate = WorldModel.Instance.GetNode(i);
                if (candidate != null && (startNode == null || candidate.Id != startNode.Id))
                {
                    farNode = candidate;
                    break;
                }
            }
        }
        return farNode;
    }

    public void DeleteNearestNPC(Vector3 position)
    {
        npcVehicles.RemoveAll(npc => npc == null);
        if (npcVehicles.Count == 0) return;

        SimpleAutoDrive nearest = null;
        float minDist = float.MaxValue;
        foreach (var npc in npcVehicles)
        {
            float d = Vector3.Distance(npc.transform.position, position);
            if (d < minDist) { minDist = d; nearest = npc; }
        }

        if (nearest != null)
        {
            Debug.Log($"TrafficManager: 删除最近NPC [{nearest.name}] 距离={minDist:F1}m");
            npcVehicles.Remove(nearest);
            Destroy(nearest.gameObject);
        }
    }

    private RoadNetworkGenerator.WaypointNode GetFarNode(RoadNetworkGenerator.WaypointNode startNode)
    {
        float maxDist = 0;
        RoadNetworkGenerator.WaypointNode farNode = null;

        for (int i = 0; i < 5; i++)
        {
            int randIdx = Random.Range(0, roadGen.nodes.Count);
            var candidate = roadGen.nodes[randIdx];
            float dist = Vector3.Distance(startNode.position, candidate.position);
            if (dist > maxDist && dist > 20f)
            {
                maxDist = dist;
                farNode = candidate;
            }
        }

        if (farNode == null)
        {
            int randIdx = Random.Range(0, roadGen.nodes.Count);
            if (roadGen.nodes[randIdx] != startNode) farNode = roadGen.nodes[randIdx];
        }
        return farNode;
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            T temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }
    
}