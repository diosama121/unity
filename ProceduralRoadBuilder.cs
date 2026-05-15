using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    [Header("=== 道路核心参数 ===")]
    public float roadWidth = 6f;
    public float meshResolution = 2f;
    public Material roadMaterial;
    public float roadHeightOffset = 0.15f;
    public string roadLayerName = "Road";

    [Header("=== 样条切线参数 ===")]
    [Range(0f, 1f)] public float tangentLength = 0.3f;
    public float maxTangentLength = 2.0f;

    [Header("=== UV Scale ===")]
    public float uvScale = 0.1f;

    [Header("=== 直路材质（按方向） ===")]
    public Material horizontalRoadMaterial;
    public Material verticalRoadMaterial;
    public Material diagonalRoadMaterial;

    [Header("=== 路口材质（按形状） ===")]
    public Material tJunctionMaterial;
    public Material crossJunctionMaterial;
    public Material complexJunctionMaterial;

    [Header("=== 乡村统一覆盖 ===")]
    public bool useCountrysideUniformMaterials = true;
    public Material countrysideRoadMaterial;
    public Material countrysideJunctionMaterial;

    [Header("=== 地表生成（供外部读取） ===")]
    public bool generateTerrainBase = true;
    public Material terrainBaseMaterial;

    [Header("=== 城镇模式参数（供外部读取） ===")]
    public bool generateCity = true;
    public float buildingHeight = 10f;
    public Material buildingMaterial;
    public float sidewalkWidth = 2f;
    public Material sidewalkMaterial;
    public float sidewalkHeight = 0.2f;

    [Header("=== 调试可视化 ===")]
    public bool showSplineGizmos = false;

    private RoadNetworkGenerator roadGen;
    public RoadNetworkGenerator RoadGen => roadGen;
    private GameObject meshRoot;

    void Awake() => roadGen = GetComponent<RoadNetworkGenerator>();

    public void BuildRoads()
    {
        if (WorldModel.Instance == null || WorldModel.Instance.Nodes == null) return;

        float stepDist = meshResolution;
        if (roadGen != null && roadGen.isCountryside)
            stepDist = 0.5f;

        foreach (var node in WorldModel.Instance.Nodes)
        {
            Vector3 pos = node.WorldPos; 
            pos.y = WorldModel.Instance.GetUnifiedHeight(pos.x, pos.z);
            node.WorldPos = pos; 
        }

        ClearRoads();
        meshRoot = new GameObject("Road_Mesh_Root");
        meshRoot.layer = LayerMask.NameToLayer(roadLayerName) < 0 ? 0 : LayerMask.NameToLayer(roadLayerName);
        meshRoot.transform.SetParent(transform, false);

        foreach (var node in WorldModel.Instance.Nodes)
        {
            if (node.NeighborIds != null && node.NeighborIds.Count > 2)
                node.IntersectionRadius = CalculateIntersectionRadius(node, roadWidth);
        }

        var allPolys = new List<Vector3[]>();
        HashSet<string> processedEdges = new HashSet<string>();

        Dictionary<int, List<JunctionEdgeEntry>> junctionEntriesByNode = new Dictionary<int, List<JunctionEdgeEntry>>();

        foreach (var node in WorldModel.Instance.Nodes)
        {
            if (node.NeighborIds == null) continue;

            foreach (int neighborId in node.NeighborIds)
            { 
                string edgeKey = Mathf.Min(node.Id, neighborId) + "_" + Mathf.Max(node.Id, neighborId);
                if (processedEdges.Contains(edgeKey)) continue;
                processedEdges.Add(edgeKey);

                List<SplinePoint> spline = RoadMathUtility.GetRoadSpline(node.Id, neighborId, stepDist, roadWidth);
                if (spline == null || spline.Count < 2) continue;

                RoadMathUtility.SweptRoadResult swept = RoadMathUtility.SweepSplineToQuadsWithSetback(spline, node.Id, neighborId, roadWidth);
                allPolys.AddRange(swept.Quads);

                if (swept.StartSetback.EdgeVertices != null && swept.StartSetback.EdgeVertices.Count >= 2)
                {
                    if (!junctionEntriesByNode.ContainsKey(node.Id))
                        junctionEntriesByNode[node.Id] = new List<JunctionEdgeEntry>();

                    Vector3 mid = (swept.StartSetback.EdgeVertices[0] + swept.StartSetback.EdgeVertices[1]) * 0.5f;
                    Vector3 outDir = mid - node.WorldPos;
                    outDir.y = 0f;
                    junctionEntriesByNode[node.Id].Add(new JunctionEdgeEntry
                    {
                        LeftPos = swept.StartSetback.EdgeVertices[0],
                        RightPos = swept.StartSetback.EdgeVertices[1],
                        OutwardDir = outDir.normalized
                    });
                }

                if (swept.EndSetback.EdgeVertices != null && swept.EndSetback.EdgeVertices.Count >= 2)
                {
                    if (!junctionEntriesByNode.ContainsKey(neighborId))
                        junctionEntriesByNode[neighborId] = new List<JunctionEdgeEntry>();

                    RoadNode nbNode = WorldModel.Instance.GetNode(neighborId);
                    Vector3 mid = (swept.EndSetback.EdgeVertices[0] + swept.EndSetback.EdgeVertices[1]) * 0.5f;
                    Vector3 outDir = mid - nbNode.WorldPos;
                    outDir.y = 0f;
                    junctionEntriesByNode[neighborId].Add(new JunctionEdgeEntry
                    {
                        LeftPos = swept.EndSetback.EdgeVertices[0],
                        RightPos = swept.EndSetback.EdgeVertices[1],
                        OutwardDir = outDir.normalized
                    });
                }
            }
        }

        foreach (var kvp in junctionEntriesByNode)
        {
            RoadNode juncNode = WorldModel.Instance.GetNode(kvp.Key);
            if (juncNode == null || juncNode.NeighborIds == null || juncNode.NeighborIds.Count < 2) continue;
            BuildIntersectionPatches(juncNode, kvp.Value, allPolys);
        }

        Mesh roadMesh = RoadMeshUtility.BuildRoadMesh(allPolys);
        if (roadMesh == null) return;

        int roadLayer = LayerMask.NameToLayer(roadLayerName) < 0 ? 0 : LayerMask.NameToLayer(roadLayerName);
        CreateRoadObject("Temp_Road_Mesh", roadMesh, BuildMaterialArray(roadMaterial), roadLayer, meshRoot.transform);
        RoadMeshCombiner.CombineRoadMeshes(meshRoot.transform);
    }

    

    private void BuildIntersectionPatches(RoadNode junctionNode, List<JunctionEdgeEntry> entries, List<Vector3[]> allPolys)
    {
        if (entries.Count < 2) return;

        Vector3 center = junctionNode.WorldPos;
        center.y = WorldModel.Instance.GetUnifiedHeight(center.x, center.z) + roadHeightOffset;

        List<JunctionEdgeEntry> sorted = entries
            .OrderBy(e => Mathf.Atan2(e.OutwardDir.z, e.OutwardDir.x))
            .ToList();

        int count = sorted.Count;
        int cornerSamples = 5;
        int radialSteps = 4;

        for (int i = 0; i < count; i++)
        {
            JunctionEdgeEntry currentRoad = sorted[i];
            JunctionEdgeEntry nextRoad = sorted[(i + 1) % count];

            GenerateRadialPie(center, currentRoad.LeftPos, currentRoad.RightPos, radialSteps, allPolys);

            Vector3 p0 = currentRoad.RightPos;
            Vector3 p2 = nextRoad.LeftPos;
            Vector3 p1 = center;

            Vector3[] curve = new Vector3[cornerSamples];
            for (int s = 0; s < cornerSamples; s++)
            {
                float t = (float)s / (cornerSamples - 1);
                float u = 1f - t;
                curve[s] = u * u * p0 + 2f * u * t * p1 + t * t * p2;
            }

            for (int s = 0; s < cornerSamples - 1; s++)
            {
                GenerateRadialPie(center, curve[s], curve[s + 1], radialSteps, allPolys);
            }
        }
    }

    private void GenerateRadialPie(Vector3 center, Vector3 edgeA, Vector3 edgeB, int radialSteps, List<Vector3[]> allPolys)
    {
        Vector3 prevA = center;
        Vector3 prevB = center;

        for (int r = 1; r <= radialSteps; r++)
        {
            float rt = (float)r / radialSteps;
            Vector3 curA = Vector3.Lerp(center, edgeA, rt);
            Vector3 curB = Vector3.Lerp(center, edgeB, rt);

            curA.y = WorldModel.Instance.GetUnifiedHeight(curA.x, curA.z) + roadHeightOffset;
            curB.y = WorldModel.Instance.GetUnifiedHeight(curB.x, curB.z) + roadHeightOffset;

            if (r == 1)
            {
                allPolys.Add(new Vector3[] { center, curB, curA });
            }
            else
            {
                allPolys.Add(new Vector3[] { prevA, curB, prevB });
                allPolys.Add(new Vector3[] { prevA, curA, curB });
            }
            prevA = curA;
            prevB = curB;
        }
    }

    private float CalculateIntersectionRadius(RoadNode node, float baseRoadWidth)
    {
        if (node.NeighborIds.Count <= 1) return baseRoadWidth * 0.1f;
        if (node.NeighborIds.Count == 2) return baseRoadWidth * 0.5f;
        float expansionBuffer = (node.NeighborIds.Count - 2) * 1.2f;
        return (baseRoadWidth * 0.5f) + expansionBuffer;
    }

    void ClearRoads()
    {
        if (meshRoot != null) DestroyImmediate(meshRoot);
        foreach (Transform child in transform) {
            if (child.name.StartsWith("Combined_Road_")) DestroyImmediate(child.gameObject);
        }
    }

    private Material[] BuildMaterialArray(Material fallback)
    {
        Material[] mats = new Material[6];
        Material defaultMat = new Material(Shader.Find("Standard"));
        Material safeFallback = fallback ? fallback : defaultMat;
        if (useCountrysideUniformMaterials) {
            mats[0] = countrysideRoadMaterial ? countrysideRoadMaterial : defaultMat;
            mats[1] = mats[0]; mats[2] = mats[0];
            mats[3] = countrysideJunctionMaterial ? countrysideJunctionMaterial : defaultMat;
            mats[4] = mats[3]; mats[5] = mats[3];
        } else {
            mats[0] = horizontalRoadMaterial ? horizontalRoadMaterial : safeFallback;
            mats[1] = verticalRoadMaterial ? verticalRoadMaterial : safeFallback;
            mats[2] = diagonalRoadMaterial ? diagonalRoadMaterial : safeFallback;
            mats[3] = tJunctionMaterial ? tJunctionMaterial : safeFallback;
            mats[4] = crossJunctionMaterial ? crossJunctionMaterial : safeFallback;
            mats[5] = complexJunctionMaterial ? complexJunctionMaterial : safeFallback;
        }
        foreach (var mat in mats) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        return mats;
    }

    private void CreateRoadObject(string name, Mesh mesh, Material[] materials, int layer, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.layer = layer;
        obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.sharedMaterials = materials;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}