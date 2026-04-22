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
        if (roadGen == null || roadGen.nodes.Count < npcCount * 2) return;

        for (int i = 0; i < npcCount; i++)
        {
            Vector3 spawnPos = roadGen.GetRandomNodePosition();
            GameObject npc = Instantiate(npcVehiclePrefab, spawnPos, Quaternion.identity);
            npc.name = $"NPC_Vehicle_{i}";

            SimpleAutoDrive autoDrive = npc.GetComponent<SimpleAutoDrive>();
            SimpleCarController carCtrl = npc.GetComponent<SimpleCarController>();

            if (autoDrive != null && carCtrl != null)
            {
                carCtrl.autoMode = true;
                // NPC 可以开得稍微慢一点
                autoDrive.targetSpeed = 6f; 
                npcVehicles.Add(autoDrive);
                
                AssignRandomDestination(autoDrive);
            }
        }
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