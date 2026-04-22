using UnityEngine;
using System.Collections.Generic;

public class TrafficManager : MonoBehaviour
{
    [Header("NPC 配置")]
    public GameObject npcVehiclePrefab;
    public int npcCount = 3;
    
    private List<SimpleAutoDrive> npcVehicles = new List<SimpleAutoDrive>();
    private RoadNetworkGenerator roadGen;

    void Start()
    {
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        // 延迟生成，确保路网已经完全构建好
        Invoke(nameof(SpawnNPCVehicles), 1f); 
    }

   void SpawnNPCVehicles()
    {
        // 增加安全校验
        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count < 5) 
        {
            Debug.LogWarning("路网未就绪，NPC 推迟生成...");
            Invoke(nameof(SpawnNPCVehicles), 1f); 
            return;
        }

        for (int i = 0; i < npcCount; i++)
        {
            // 获取随机路点
            Vector3 spawnPos = roadGen.GetRandomNodePosition();
            spawnPos.y += 0.5f; // 防止嵌入地面

            GameObject npc = Instantiate(npcVehiclePrefab, spawnPos, Quaternion.identity);
            npc.name = $"NPC_Vehicle_{i}";

            // 【核心修复1：动态赋予刚体】
            // 必须在 SimpleCarController 的 Start 运行前挂载刚体
            if (npc.GetComponent<Rigidbody>() == null)
            {
                npc.AddComponent<Rigidbody>();
            }

            // 【核心修复2：确保三大件脚本齐全】
            SimpleCarController carCtrl = npc.GetComponent<SimpleCarController>();
            if (carCtrl == null) carCtrl = npc.AddComponent<SimpleCarController>();

            SimpleAutoDrive autoDrive = npc.GetComponent<SimpleAutoDrive>();
            if (autoDrive == null) autoDrive = npc.AddComponent<SimpleAutoDrive>();

            RaycastSensor sensor = npc.GetComponent<RaycastSensor>();
            if (sensor == null) sensor = npc.AddComponent<RaycastSensor>();

            // 开启自动驾驶模式
            carCtrl.autoMode = true;
            autoDrive.targetSpeed = 5f; // NPC 慢速更显真实
            npcVehicles.Add(autoDrive);
            
            // 给一个微小的延迟再指派目的地，防止寻路器还没重置
            StartCoroutine(DelayedAssign(autoDrive));
        }
    }
    System.Collections.IEnumerator DelayedAssign(SimpleAutoDrive npc)
    {
        yield return new WaitForSeconds(0.5f);
        AssignRandomDestination(npc);
    }
    void Update()
    {
        // 监控 NPC 状态，到达目的地（Idle）则指派新任务
        foreach (var npc in npcVehicles)
        {
            if (npc != null && npc.currentState == SimpleAutoDrive.DriveState.Idle)
            {
                AssignRandomDestination(npc);
            }
        }
    }

    void AssignRandomDestination(SimpleAutoDrive npc)
    {
        if (roadGen == null) return;
        
        Vector3 randomDest = roadGen.GetRandomNodePosition();
        // 确保目的地不是当前位置
        while (Vector3.Distance(npc.transform.position, randomDest) < 20f)
        {
            randomDest = roadGen.GetRandomNodePosition();
        }
        
        npc.SetDestination(randomDest);
    }
}