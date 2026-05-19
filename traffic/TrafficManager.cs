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
    public int npcCount = 3;

    [Header("自适应调度")]
    public bool dynamicScheduling = true;
    public float schedulingCheckInterval = 2f;
    private float schedTimer = 0f;
    private float lastFps = 60f;

    private List<SimpleAutoDrive> npcVehicles = new List<SimpleAutoDrive>();
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
                    var weakest = npcVehicles.FindLast(n => n != null);
                    if (weakest != null)
                    {
                        npcVehicles.Remove(weakest);
                        Destroy(weakest.gameObject);
                        Debug.Log("[TrafficManager] FPS=" + Mathf.RoundToInt(lastFps) + " < 40, despawning 1 NPC. Remaining: " + npcVehicles.Count);
                    }
                }
            }
        }
    }

  public void SpawnNPCs()
    {
        if (_hasSpawned) { Debug.Log("TrafficManager: NPC已生成，跳过重复调用"); return; }

        ClearAllNPCs();

        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        pathPlanner = FindObjectOfType<PathPlanner>();

        if (normalNpcPrefabs == null || normalNpcPrefabs.Length == 0) { Debug.LogError("[TrafficManager] normalNpcPrefabs 数组为空！请在 Inspector 拖入 NPC 车辆预制体。"); return; }
        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count < 2) { Debug.LogWarning("[TrafficManager] 路网数据不可用，无法生成 NPC。"); return; }
        if (pathPlanner == null) { Debug.LogWarning("[TrafficManager] PathPlanner 未找到，无法生成 NPC。"); return; }

        List<RoadNetworkGenerator.WaypointNode> shuffledNodes = new List<RoadNetworkGenerator.WaypointNode>(roadGen.nodes);
        ShuffleList(shuffledNodes);

        int spawnedCount = 0;
        for (int i = 0; i < shuffledNodes.Count && spawnedCount < npcCount; i++)
        {
            var startNode = shuffledNodes[i];
            var targetNode = GetFarNode(startNode);
            if (targetNode == null) continue;

            // 先随便在这个节点实例化
            GameObject chosenPrefab = normalNpcPrefabs[Random.Range(0, normalNpcPrefabs.Length)];
            GameObject npcObj = Instantiate(chosenPrefab, startNode.position, Quaternion.identity);
            npcObj.name = $"NPC_Vehicle_{spawnedCount}";

            SimpleCarController controller = npcObj.GetComponent<SimpleCarController>();
            if (controller == null) controller = npcObj.GetComponentInChildren<SimpleCarController>();
            // 【修复点】：确保强制设置为NPC
            if (controller != null) controller.ChangeRole(true); 

            SimpleAutoDrive autoDrive = npcObj.GetComponent<SimpleAutoDrive>();
            if (autoDrive == null) autoDrive = npcObj.GetComponentInChildren<SimpleAutoDrive>();
            if (autoDrive != null)
            {
                CatmullRomSpline spline = pathPlanner.PlanPathSpline(startNode.position, targetNode.position);
                if (spline != null && spline.TotalLength > 0)
                {
                    // 【修复点核心】：将出生位置强行纠正到样条曲线的绝对起点（路线上）
                    Vector3 exactStartPos = spline.GetPoint(0f);
                    if (WorldModel.Instance != null)
                    {
                        exactStartPos.y = WorldModel.Instance.GetUnifiedHeight(exactStartPos.x, exactStartPos.z);
                    }
                    npcObj.transform.position = exactStartPos; // 精确吸附

                    Vector3 startTangent = (spline.GetPoint(0.01f) - spline.GetPoint(0f)).normalized;
                    if (startTangent != Vector3.zero)
                    {
                        npcObj.transform.rotation = Quaternion.LookRotation(startTangent);
                    }

                    autoDrive.SetSplinePath(spline, targetNode.id);
                    npcVehicles.Add(autoDrive);
                    spawnedCount++;
                }
                else
                {
                    Debug.LogWarning($"[TrafficManager] NPC {spawnedCount} 路径规划失败，节点 {startNode.id} -> {targetNode.id}，已销毁实例。");
                    Destroy(npcObj);
                }
            }
        }
        _hasSpawned = spawnedCount > 0;
        if (!_hasSpawned) Debug.LogWarning("[TrafficManager] 未成功生成任何 NPC。");
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

        // 已移除幻觉代码：AIStateBubble 和 DangerZoneVisualizer

        SimpleCarController controller = evObj.GetComponent<SimpleCarController>();
        if (controller == null) controller = evObj.GetComponentInChildren<SimpleCarController>();
        if (controller != null)
        {
            controller.isNPC = false;
            controller.autoMode = true;
            controller.vehiclePriority = VehiclePriority.Emergency;
        }

        SimpleAutoDrive autoDrive = evObj.GetComponent<SimpleAutoDrive>();
        if (autoDrive == null) autoDrive = evObj.GetComponentInChildren<SimpleAutoDrive>();
        if (autoDrive != null)
        {
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