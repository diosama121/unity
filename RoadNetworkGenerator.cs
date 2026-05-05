using UnityEngine;
using System.Collections.Generic;
using System;



#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoadNetworkGenerator : MonoBehaviour
{
[Header("=== 路网尺寸 ===")]
public int gridWidth = 5;
public int gridHeight = 5;
public float cellSize = 80f;

[Header("=== 随机偏移 ===")]
public float randomOffset = 5f;
public int seed = 42;
public float countrysideHeightScale = 5f;  

[Range(0f, 0.4f)]
public float connectionRemoveRate = 0.1f;

[Header("=== 生成控制 ===")]
public bool generateOnStart = true;
public bool autoLinkPathPlanner = true;
public bool showRuntimeUI = true;
[Header("=== 环境模式 ===")]
[Tooltip("勾选为乡村(起伏地形无高楼)，取消勾选为城市(纯平地形+高楼)")]
public bool isCountryside = false;
[Header("=== 可视化 ===")]
public bool showGizmos = true;
public Color nodeColor = Color.yellow;
public Color edgeColor = Color.white;
public float nodeSphereSize = 1f;

private bool pendingGenerate = false;
private bool pendingRandomGenerate = false;

// =============================================
// 数据结构：Road优先
// =============================================

public class WaypointNode
{
public int id;
public Vector3 position;
public List<int> neighbors = new List<int>();
public GameObject gizmoObject;
}

// 道路段定义（先有路，再有节点）
public class RoadSegment
{
public Vector3 start;
public Vector3 end;
public int startNodeId;
public int endNodeId;
}

[HideInInspector] public List<WaypointNode> nodes = new List<WaypointNode>();
[HideInInspector] public List<(int, int)> edges = new List<(int, int)>();
[HideInInspector] public List<RoadSegment> roadSegments = new List<RoadSegment>();

private int[,] grid;

// UI
private bool uiExpanded = true;
private string uiSeed = "42";
private string uiWidth = "5";
private string uiHeight = "5";
private string uiCellSize = "20";
private string uiOffset = "5";

public PathPlanner pathPlanner;

// =============================================
// 生命周期
// =============================================
  public void Generate()
    {
         // delegate to existing internal method
    }
//待重构
}