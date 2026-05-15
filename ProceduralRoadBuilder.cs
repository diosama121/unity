using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    [Header("=== 道路核心参数 ===")]
    public float roadWidth = 6f;
    public float meshResolution = 2f;
    public Material roadMaterial;
    public float roadHeightOffset = 0.05f;
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

        var allPolys = new List<Vector3[]>();
        HashSet<string> processedEdges = new HashSet<string>();

        foreach (var node in WorldModel.Instance.Nodes)
        {
            if (node.NeighborIds == null) continue;

            // 路口八角补丁，消除锐角缝隙
            if (node.NeighborIds.Count > 2) 
            {
                float patchRadius = roadWidth * 0.75f; 
                Vector3 center = node.WorldPos;
                float patchYOffset = 0.21f; 
                center.y = WorldModel.Instance.GetUnifiedHeight(center.x, center.z) + patchYOffset; 

                int segments = 16;
                float angleStep = 360f / segments;
                for (int i = 0; i < segments; i++)
                {
                    float a1 = i * angleStep * Mathf.Deg2Rad;
                    float a2 = (i + 1) * angleStep * Mathf.Deg2Rad;
                    Vector3 p1 = center + new Vector3(Mathf.Cos(a1) * patchRadius, 0, Mathf.Sin(a1) * patchRadius);
                    Vector3 p2 = center + new Vector3(Mathf.Cos(a2) * patchRadius, 0, Mathf.Sin(a2) * patchRadius);
                    p1.y = center.y; p2.y = center.y;
                    allPolys.Add(new Vector3[] { center, p2, p1 });
                }
            }

            foreach (int neighborId in node.NeighborIds)
            {
                string edgeKey = Mathf.Min(node.Id, neighborId) + "_" + Mathf.Max(node.Id, neighborId);
                if (processedEdges.Contains(edgeKey)) continue;
                processedEdges.Add(edgeKey);

                List<SplinePoint> spline = RoadMathUtility.GetRoadSpline(node.Id, neighborId, meshResolution, roadWidth);
                if (spline == null || spline.Count < 2) continue;
                allPolys.AddRange(RoadMathUtility.SweepSplineToQuads(spline, roadWidth));
            }
        }

        Mesh roadMesh = RoadMeshUtility.BuildRoadMesh(allPolys);
        if (roadMesh == null) return;

        int roadLayer = LayerMask.NameToLayer(roadLayerName) < 0 ? 0 : LayerMask.NameToLayer(roadLayerName);
        CreateRoadObject("Temp_Road_Mesh", roadMesh, BuildMaterialArray(roadMaterial), roadLayer, meshRoot.transform);
        RoadMeshCombiner.CombineRoadMeshes(meshRoot.transform);
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