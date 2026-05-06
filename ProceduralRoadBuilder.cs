using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    // ==================== 保留所有 Inspector 字段 ====================
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

    // ==================== 内部状态 ====================
    private RoadNetworkGenerator roadGen;
    private GameObject meshRoot;
    public RoadNetworkGenerator RoadGen => roadGen;

    private Dictionary<int, List<Vector3>> intersectionExposedVerts = new Dictionary<int, List<Vector3>>();

  public void BuildRoads()
{
    // 不再使用 roadGen，直接从 a4 获取真理节点
    if (WorldModel.Instance == null || WorldModel.Instance.Nodes == null)
    {
        Debug.LogError("[ProceduralRoadBuilder] WorldModel 不可用");
        return;
    }

    ClearRoads();
    meshRoot = new GameObject("Road_Mesh");
    meshRoot.transform.SetParent(transform, false);

    var allEdgeQuads = new List<Vector3[]>();
    HashSet<string> processedEdges = new HashSet<string>();

    foreach (var node in WorldModel.Instance.Nodes)  // 假设 Nodes 返回 IEnumerable<RoadNode>
    {
        if (node.NeighborIds == null) continue;

        foreach (int neighborId in node.NeighborIds)
        {
            // 防止重复生成同一条边
            string edgeKey = Mathf.Min(node.Id, neighborId) + "_" + Mathf.Max(node.Id, neighborId);
            if (processedEdges.Contains(edgeKey)) continue;
            processedEdges.Add(edgeKey);

            // 从数学工具获取样条点（高度、切线、法线均由 a4 真理决定）
            List<SplinePoint> spline = RoadMathUtility.GetRoadSpline(node.Id, neighborId, meshResolution);
            if (spline == null || spline.Count < 2) continue;

            // 扫掠生成四边形带
            List<Vector3[]> quads = RoadMathUtility.SweepSplineToQuads(spline, roadWidth);
            allEdgeQuads.AddRange(quads);
        }
    }

    // 将四边形列表合并成一个道路 Mesh
    Mesh roadMesh = RoadMeshUtility.BuildRoadMesh(allEdgeQuads);
    if (roadMesh == null) return;

    // 装配到场景
    int roadLayer = LayerMask.NameToLayer(roadLayerName);
    if (roadLayer < 0) roadLayer = 0;
    CreateRoadObject("Road_Mesh", roadMesh, BuildMaterialArray(roadMaterial), roadLayer, meshRoot.transform);

    Debug.Log($"[ProceduralRoadBuilder] ✅ 道路生成完成（顶点 {roadMesh.vertexCount}）");
}

    // 退让：简单去掉首尾 roadWidth*0.8f 范围的点（实际可按累计距离裁剪）
    private List<SplinePoint> TruncateForIntersections(List<SplinePoint> spline)
    {
        if (spline.Count <= 4) return spline; // 太短则全保留
        // 去掉两端的一点（最简实现）
        List<SplinePoint> trunk = new List<SplinePoint>(spline);
        // 实际应计算距离，此处仅作示意，保证编译通过
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
        // 临时：取第一个 mesh 放入新物体，后续可合并
        if (meshes.Count == 0) return;
        Mesh final = meshes[0]; // 简单处理，后续可合并
        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        GameObject roadObj = new GameObject("Road_Mesh");
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
    }
private Material[] BuildMaterialArray(Material fallback)
{
    Material[] mats = new Material[6];
    Material defaultMat = new Material(Shader.Find("Standard"));
    defaultMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off); // 双面渲染

    // 确保 fallback 也存在
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

    // 强制所有材质双面渲染（再次确保）
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
    // 增加双面渲染的材质侧保险
    foreach (var m in materials)
        m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
    obj.AddComponent<MeshCollider>().sharedMesh = mesh;
}
    void Start() => roadGen = GetComponent<RoadNetworkGenerator>();
}