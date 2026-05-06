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
    private RoadNetworkGenerator roadGen;
    private PathPlanner pathPlanner;

    void Start()
    {
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        pathPlanner = FindObjectOfType<PathPlanner>();
        
        // 延迟生成，确保路网与交通灯已经完全构建好
        if (roadGen != null && roadGen.nodes != null && roadGen.nodes.Count > 0)
        {
            Invoke("SpawnNPCs", 0.5f); 
        }
        else
        {
            StartCoroutine(WaitAndSpawn());
        }
    }

    private System.Collections.IEnumerator WaitAndSpawn()
    {
        while (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count == 0)
        {
            roadGen = FindObjectOfType<RoadNetworkGenerator>();
            pathPlanner = FindObjectOfType<PathPlanner>();
            yield return new WaitForSeconds(0.5f);
        }
        yield return new WaitForSeconds(0.5f); // 额外等半秒确保绝对稳妥
        SpawnNPCs();
    }

    public void SpawnNPCs()
    {
        if (npcVehiclePrefab == null) { Debug.LogError("TrafficManager: 缺少 NPC Prefab!"); return; }
        if (roadGen.nodes.Count < 2) { Debug.LogWarning("TrafficManager: 路网节点不足，无法生成 NPC"); return; }
       //等着修复，现在先做地形部分。 if (pathPlanner == null) { Debug.LogError("TrafficManager: 缺少 PathPlanner!"); return; }

        // 随机打乱节点池，从中抽取不重复的出生点
        List<RoadNetworkGenerator.WaypointNode> shuffledNodes = new List<RoadNetworkGenerator.WaypointNode>(roadGen.nodes);
        ShuffleList(shuffledNodes);

        int spawnedCount = 0;
        for (int i = 0; i < shuffledNodes.Count && spawnedCount < npcCount; i++)
        {
            var startNode = shuffledNodes[i];
            
            // 智能寻找一个距离较远的目标节点，防止原地打转
            var targetNode = GetFarNode(startNode);
            if (targetNode == null) continue;

            // 严格按照白皮书获取真实地形高度
            Vector3 spawnPos = startNode.position;
            if (WorldModel.Instance != null)
            {
                spawnPos.y = WorldModel.Instance.GetTerrainHeight(new Vector2(spawnPos.x, spawnPos.z));
            }

            // 生成车辆实体
            GameObject npcObj = Instantiate(npcVehiclePrefab, spawnPos, Quaternion.identity);
            npcObj.name = $"NPC_Vehicle_{spawnedCount}";

            // 【核心指令】强制切断物理，开启纯数学轨道模式
            SimpleCarController controller = npcObj.GetComponent<SimpleAutoDrive>()?.GetComponent<SimpleCarController>();
            if (controller == null) controller = npcObj.GetComponentInChildren<SimpleCarController>();
            
            if (controller != null)
            {
                controller.isNPC = true; 
            }

            // 对接 V2.0 路径管线
            SimpleAutoDrive autoDrive = npcObj.GetComponent<SimpleAutoDrive>();
            if (autoDrive != null)
            {
             /*   CatmullRomSpline spline = pathPlanner.PlanPath(startNode.position, targetNode.position);
                if (spline != null && spline.TotalLength > 0)
                {
                    // 将样条曲线与目标路口语义 ID 喂给底层执行器
                    autoDrive.SetSplinePath(spline, targetNode.id);
                    npcVehicles.Add(autoDrive);
                    spawnedCount++;
                }
                else
                {
                    Destroy(npcObj); // 规划失败直接回炉，不占性能
                }
            }
            else
            {
                Destroy(npcObj);
            }*/
        }}
        Debug.Log($"✅ TrafficManager: 成功生成 {spawnedCount} 辆纯数学轨道 NPC");
    }

    // 简易远点抽取算法
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
