using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 中央交通调度器
/// 功能：负责在路网上批量生成纯数学 NPC，并下发 CatmullRom 轨道飞行任务
/// </summary>
public class TrafficManager : MonoBehaviour
{
    [Header("NPC 配置")]
    public GameObject npcVehiclePrefab;
    public int npcCount = 3;
    
    private List<SimpleAutoDrive> npcVehicles = new List<SimpleAutoDrive>();
    public IReadOnlyList<SimpleAutoDrive> ActiveNPCs => npcVehicles;
    private RoadNetworkGenerator roadGen;
    private PathPlanner pathPlanner;

    private bool _hasSpawned = false;

    public void ResetSpawnState() { _hasSpawned = false; }

    public void SpawnNPCs()
    {
        if (_hasSpawned) { Debug.Log("TrafficManager: NPC已生成，跳过重复调用"); return; }

        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        pathPlanner = FindObjectOfType<PathPlanner>();

        if (npcVehiclePrefab == null) { Debug.LogError("TrafficManager: 缺少 NPC Prefab!"); return; }
        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count < 2) { Debug.LogWarning("TrafficManager: 路网节点不足，无法生成 NPC"); return; }
        
        // 【修复 1：解除封印】恢复对 PathPlanner 的检查
        if (pathPlanner == null) { Debug.LogError("TrafficManager: 缺少 PathPlanner!"); return; }

        List<RoadNetworkGenerator.WaypointNode> shuffledNodes = new List<RoadNetworkGenerator.WaypointNode>(roadGen.nodes);
        ShuffleList(shuffledNodes);

        int spawnedCount = 0;
        for (int i = 0; i < shuffledNodes.Count && spawnedCount < npcCount; i++)
        {
            var startNode = shuffledNodes[i];
            var targetNode = GetFarNode(startNode);
            if (targetNode == null) continue;

            Vector3 spawnPos = startNode.position;
            if (WorldModel.Instance != null)
            {
                spawnPos.y = WorldModel.Instance.GetUnifiedHeight(spawnPos.x, spawnPos.z);
            }

            GameObject npcObj = Instantiate(npcVehiclePrefab, spawnPos, Quaternion.identity);
            npcObj.name = $"NPC_Vehicle_{spawnedCount}";

            SimpleCarController controller = npcObj.GetComponent<SimpleCarController>();
            if (controller == null) controller = npcObj.GetComponentInChildren<SimpleCarController>();
            if (controller != null) controller.isNPC = true; 

            SimpleAutoDrive autoDrive = npcObj.GetComponent<SimpleAutoDrive>();
            if (autoDrive == null) autoDrive = npcObj.GetComponentInChildren<SimpleAutoDrive>();
            if (autoDrive != null)
            {
                // 【修复 2：解除样条规划封印，调用正确的接口】
                CatmullRomSpline spline = pathPlanner.PlanPathSpline(startNode.position, targetNode.position);
                if (spline != null && spline.TotalLength > 0)
                {
                    autoDrive.SetSplinePath(spline, targetNode.id);
                    npcVehicles.Add(autoDrive);
                    spawnedCount++;
                }
                else
                {
                    Debug.LogWarning($"TrafficManager: NPC {spawnedCount} 样条路径规划失败，已销毁实例");
                    Destroy(npcObj);
                }
            }
            else
            {
                Debug.LogWarning($"TrafficManager: NPC预制体缺少SimpleAutoDrive组件（已检查自身及子物体），实例已销毁");
                Destroy(npcObj);
            }
        }

        // 仅在完成全部生成循环后才锁定状态（避免中途失败锁死后续重试）
        _hasSpawned = true;
        Debug.Log($"✅ TrafficManager: 成功生成 {spawnedCount} 辆纯数学轨道 NPC");
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