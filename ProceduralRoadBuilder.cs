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

    [Header("=== 地表生成（开关，具体由 EnvironmentMeshBuilder 执行） ===")]
    public bool generateTerrainBase = true;
    public Material terrainBaseMaterial;

    [Header("=== 城镇模式参数（开关，具体由 EnvironmentMeshBuilder 执行） ===")]
    public bool generateCity = true;
    public float buildingHeight = 10f;
    public Material buildingMaterial;
    public float sidewalkWidth = 2f;
    public Material sidewalkMaterial;
    public float sidewalkHeight = 0.2f;

    [Header("=== 调试可视化 ===")]
    public bool showSplineGizmos = false;

    private RoadNetworkGenerator roadGen;
    private GameObject meshRoot;
    public RoadNetworkGenerator RoadGen => roadGen;

    private Dictionary<int, List<Vector3>> intersectionExposedVerts = new Dictionary<int, List<Vector3>>();

    public void BuildRoads()
    {
        if (WorldModel.Instance == null || WorldModel.Instance.Nodes == null)
        {
            Debug.LogError("[ProceduralRoadBuilder] WorldModel 不可用");
            return;
        }

        // 【紧急修复：高程校准】
        // 在算任何曲线之前，先把所有端点从地下拔出来，死死钉在真理地表上！
        foreach (var node in WorldModel.Instance.Nodes)
        {
            Vector3 pos = node.WorldPos; // 若实际属性名为 WorldPos，请替换
            pos.y = WorldModel.Instance.GetUnifiedHeight(pos.x, pos.z);
            node.WorldPos = pos; 
        }

        ClearRoads();
        meshRoot = new GameObject("Road_Mesh_Root");
        meshRoot.layer = LayerMask.NameToLayer(roadLayerName) < 0 ? 0 : LayerMask.NameToLayer(roadLayerName);
        meshRoot.transform.SetParent(transform, false);

        var allEdgeQuads = new List<Vector3[]>();
        HashSet<string> processedEdges = new HashSet<string>();

        foreach (var node in WorldModel.Instance.Nodes)
        {
            if (node.NeighborIds == null) continue;

            foreach (int neighborId in node.NeighborIds)
            {
                string edgeKey = Mathf.Min(node.Id, neighborId) + "_" + Mathf.Max(node.Id, neighborId);
                if (processedEdges.Contains(edgeKey)) continue;
                processedEdges.Add(edgeKey);

                List<SplinePoint> spline = RoadMathUtility.GetRoadSpline(node.Id, neighborId, meshResolution);
                if (spline == null || spline.Count < 2) continue;

                List<Vector3[]> quads = RoadMathUtility.SweepSplineToQuads(spline, roadWidth);
                allEdgeQuads.AddRange(quads);
            }
        }

        Mesh roadMesh = RoadMeshUtility.BuildRoadMesh(allEdgeQuads);
        if (roadMesh == null) return;

        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        if (roadLayer < 0) roadLayer = 0;
        CreateRoadObject("Temp_Road_Mesh", roadMesh, BuildMaterialArray(roadMaterial), roadLayer, meshRoot.transform);

        Debug.Log($"[ProceduralRoadBuilder] 道路初始几何构建完成，准备移交 Combiner 进行终极合批与物理烘焙...");

        RoadMeshCombiner.CombineRoadMeshes(meshRoot.transform);

        Debug.Log($"[ProceduralRoadBuilder] ✅ 道路系统全部构建完毕！");
    }

    private List<SplinePoint> TruncateForIntersections(List<SplinePoint> spline)
    {
        if (spline.Count <= 4) return spline; 
        List<SplinePoint> trunk = new List<SplinePoint>(spline);
        return trunk.GetRange(1, trunk.Count - 2);
    }

    private void ExposeEndpoints(int nodeId, Vector3 left, Vector3 right)
    {
        if (!intersectionExposedVerts.ContainsKey(nodeId))
            intersectionExposedVerts[nodeId] = new List<Vector3>();
        intersectionExposedVerts[nodeId].Add(left);
        intersectionExposedVerts[nodeId].Add(right);
    }

    private void CombineAndAssign(List<Mesh> meshes)
    {
        if (meshes.Count == 0) return;
        Mesh final = meshes[0]; 
        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        GameObject roadObj = new GameObject("Temp_Road_Mesh_Legacy");
        roadObj.layer = roadLayer < 0 ? 0 : roadLayer;
        roadObj.transform.SetParent(meshRoot.transform, false);
        roadObj.AddComponent<MeshFilter>().sharedMesh = final;
        MeshRenderer mr = roadObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = roadMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        roadObj.AddComponent<MeshCollider>().sharedMesh = final;
    }

    void ClearRoads()
    {
        if (meshRoot != null) DestroyImmediate(meshRoot);
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Combined_Road_"))
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private Material[] BuildMaterialArray(Material fallback)
    {
        Material[] mats = new Material[6];
        Material defaultMat = new Material(Shader.Find("Standard"));
        defaultMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        Material safeFallback = fallback ? fallback : defaultMat;

        if (useCountrysideUniformMaterials)
        {
            mats[0] = (countrysideRoadMaterial ? countrysideRoadMaterial : defaultMat);
            mats[1] = mats[0];
            mats[2] = mats[0];
            mats[3] = (countrysideJunctionMaterial ? countrysideJunctionMaterial : defaultMat);
            mats[4] = mats[3];
            mats[5] = mats[3];
        }
        else
        {
            mats[0] = horizontalRoadMaterial ? horizontalRoadMaterial : safeFallback;
            mats[1] = verticalRoadMaterial ? verticalRoadMaterial : safeFallback;
            mats[2] = diagonalRoadMaterial ? diagonalRoadMaterial : safeFallback;
            mats[3] = tJunctionMaterial ? tJunctionMaterial : safeFallback;
            mats[4] = crossJunctionMaterial ? crossJunctionMaterial : safeFallback;
            mats[5] = complexJunctionMaterial ? complexJunctionMaterial : safeFallback;
        }

        foreach (var mat in mats)
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

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
        mr.receiveShadows = true;
        foreach (var m in materials)
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
    }

    void Start() => roadGen = GetComponent<RoadNetworkGenerator>();
}