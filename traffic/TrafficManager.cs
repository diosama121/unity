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
     
    }

  //已清空，待重构。
}