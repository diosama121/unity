using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Clipper2Lib;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using System.Linq;
using System;

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    [Header("=== 道路核心参数 ===")]
    public float roadWidth = 6f;
    public float meshResolution = 2f;
    public Material roadMaterial;
    public float roadHeightOffset = 0.05f;
    public string roadLayerName = "Road";

    [Header("=== 乡村地形起伏（已废弃直接计算，保留字段用于UI） ===")]
    public float terrainNoiseFrequency = 0.05f;

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

    [Header("=== 地表生成（已迁移至 EnvironmentMeshBuilder，此处保留开关） ===")]
    public bool generateTerrainBase = true;
    public Material terrainBaseMaterial;

    [Header("=== 城镇模式参数（已迁移，保留开关） ===")]
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
    private GameObject terrainRoot;
    private float[] nodeUnifiedHeight;
public RoadNetworkGenerator RoadGen => roadGen;
    // 存储最终道路多边形，供环境构造器使用
    private Paths64 finalRoadUnion;

    private enum RoadDirection { Horizontal, Vertical, Diagonal }

    // ==========================================
    // 统一高度查询（V2.0 真理层）
    // ==========================================
    private float GetHeightAt(float worldX, float worldZ)
    {
        if (WorldModel.Instance != null)
            return WorldModel.Instance.GetTerrainHeight(new Vector2(worldX, worldZ));
        if (TerrainGridSystem.Instance != null)
            return TerrainGridSystem.Instance.GetHeightAt(worldX, worldZ);
        return 0f;
    }

    public void BuildRoads()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();
        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count == 0 ||
            roadGen.edges == null || roadGen.edges.Count == 0)
        {
            Debug.LogWarning("[ProceduralRoadBuilder] 路网数据为空，跳过生成。");
            return;
        }

        ClearRoads();
        EnsureMaterials();

        meshRoot = new GameObject("Procedural_Road_System");
        meshRoot.transform.SetParent(transform, false);
        terrainRoot = new GameObject("Procedural_Terrain");
        terrainRoot.transform.SetParent(transform, false);

        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        if (roadLayer < 0) roadLayer = 0;

        PrecomputeUnifiedHeights();

        // ── 1. 收集所有道路多边形（仅XZ投影），计算精确总包围盒 ──
        List<Vector3[]> allPolys = new List<Vector3[]>();
        HashSet<string> processedEdges = new HashSet<string>();
        foreach (var edge in roadGen.edges)
        {
            int idA = edge.Item1, idB = edge.Item2;
            string key = idA < idB ? $"{idA}_{idB}" : $"{idB}_{idA}";
            if (processedEdges.Contains(key)) continue;
            processedEdges.Add(key);
            if (idA < 0 || idA >= roadGen.nodes.Count ||
                idB < 0 || idB >= roadGen.nodes.Count) continue;

            Vector3 posA = GetUnifiedNodePosition(idA);
            Vector3 posB = GetUnifiedNodePosition(idB);
            if (Vector3.Distance(posA, posB) < 0.5f) continue;

            (posA, posB) = NormalizeEdgeDirection(posA, posB);
            Spline spline = BuildSimpleSpline(posA, posB);
            if (spline == null) continue;
            List<Vector3> centerLine = SampleSpline(spline, meshResolution);
            if (centerLine.Count < 2) continue;
            List<Vector3> polygon = RoadBooleanUtility.ExpandCenterlineToPolygon(centerLine, roadWidth);
            if (polygon.Count < 3) continue;
            allPolys.Add(polygon.ToArray());
        }

        // 路口多边形（带预清洗与兜底，不在此处熔断，仅收集用于包围盒）
        for (int i = 0; i < roadGen.nodes.Count; i++)
        {
            var node = roadGen.nodes[i];
            if (node.neighbors == null || node.neighbors.Count < 3) continue;
            Vector3 nodePos = GetUnifiedNodePosition(i);
            var connectedEdges = new List<(Vector3, Vector3)>();
            foreach (int nbId in node.neighbors)
            {
                Vector3 nbPos = GetUnifiedNodePosition(nbId);
                connectedEdges.Add((nodePos, nbPos));
            }
            try
            {
                Path64 smoothPoly = RoadBooleanUtility.GenerateSmoothIntersectionPolygon(nodePos, connectedEdges, roadWidth);
                var verts = smoothPoly.Select(pt => new Vector3((float)(pt.X / 1000.0), 0f, (float)(pt.Y / 1000.0))).ToArray();
                if (verts.Length >= 3)
                    allPolys.Add(verts);
            }
            catch { } // 包围盒收集阶段允许静默失败
        }

        // 计算精确总包围盒
        Bounds preciseBounds = new Bounds();
        bool firstBounds = true;
        foreach (var poly in allPolys)
        {
            foreach (var v in poly)
            {
                if (firstBounds) { preciseBounds = new Bounds(v, Vector3.zero); firstBounds = false; }
                else preciseBounds.Encapsulate(v);
            }
        }
        preciseBounds.Expand(roadWidth * 2f);

        // 通知地形系统重新初始化
        if (TerrainGridSystem.Instance != null)
            TerrainGridSystem.Instance.Reinitialize(preciseBounds);

        // ── 2. 正式生成道路网格（带熔断）──
        ProcessEdgesAndJunctions(roadLayer);

        // ── 3. 反哺视觉中心点给真理层 ──
        ReportVisualCentersToWorldModel();

        // ── 4. 将最终道路多边形交接给环境建造施工队 ──
        if (finalRoadUnion != null && finalRoadUnion.Count > 0)
        {
            EnvironmentMeshBuilder envBuilder = GetComponent<EnvironmentMeshBuilder>();
            if (envBuilder == null)
                envBuilder = gameObject.AddComponent<EnvironmentMeshBuilder>();
            envBuilder.GenerateEnvironment(finalRoadUnion, this);
        }
    }

    // 存储第二阶段用到的信息
    private List<RoadUVProjector.EdgeRoadInfo> edgeInfosForRoad = new List<RoadUVProjector.EdgeRoadInfo>();
    private List<RoadUVProjector.JunctionInfo> junctionInfosForRoad = new List<RoadUVProjector.JunctionInfo>();

    // 用于反哺：nodeId -> 视觉中心
    private Dictionary<int, Vector3> nodeVisualCenters = new Dictionary<int, Vector3>();

    private void ProcessEdgesAndJunctions(int roadLayer)
    {
        edgeInfosForRoad.Clear();
        junctionInfosForRoad.Clear();
        nodeVisualCenters.Clear();

        List<Vector3[]> edgePolys = new List<Vector3[]>();
        List<Vector3[]> junctionPolys = new List<Vector3[]>();

        Material fallbackMat = roadMaterial;

        // 边多边形
        HashSet<string> procEdges = new HashSet<string>();
        foreach (var edge in roadGen.edges)
        {
            int idA = edge.Item1, idB = edge.Item2;
            string key = idA < idB ? $"{idA}_{idB}" : $"{idB}_{idA}";
            if (procEdges.Contains(key)) continue;
            procEdges.Add(key);
            if (idA < 0 || idA >= roadGen.nodes.Count || idB < 0 || idB >= roadGen.nodes.Count) continue;

            Vector3 posA = GetUnifiedNodePosition(idA);
            Vector3 posB = GetUnifiedNodePosition(idB);
            if (Vector3.Distance(posA, posB) < 0.5f) continue;
            (posA, posB) = NormalizeEdgeDirection(posA, posB);

            Spline spline = BuildSimpleSpline(posA, posB);
            if (spline == null) continue;
            List<Vector3> centerLine = SampleSpline(spline, meshResolution);
            if (centerLine.Count < 2) continue;
            List<Vector3> polygon = RoadBooleanUtility.ExpandCenterlineToPolygon(centerLine, roadWidth);
            if (polygon.Count < 3) continue;

            Vector3[] polyArray = polygon.ToArray();
            Vector3 dir = (posB - posA).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            RoadDirection roadDir = ClassifyDirection(posA, posB);
            int sub = roadDir switch { RoadDirection.Horizontal => 0, RoadDirection.Vertical => 1, _ => 2 };

            edgeInfosForRoad.Add(new RoadUVProjector.EdgeRoadInfo
            {
                poly = polyArray,
                start = posA,
                end = posB,
                forward = dir,
                right = right,
                origin = posA,
                targetSubMesh = sub
            });
            edgePolys.Add(polyArray);
        }

        // 路口多边形（带熔断）
        for (int i = 0; i < roadGen.nodes.Count; i++)
        {
            var node = roadGen.nodes[i];
            if (node.neighbors == null || node.neighbors.Count < 3) continue;
            Vector3 nodePos = GetUnifiedNodePosition(i);
            var connectedEdges = new List<(Vector3, Vector3)>();
            List<Vector3> neighborPositions = new List<Vector3>();
            foreach (int nbId in node.neighbors)
            {
                Vector3 nbPos = GetUnifiedNodePosition(nbId);
                connectedEdges.Add((nodePos, nbPos));
                neighborPositions.Add(nbPos);
            }

            // ==========================================
            // 【a1 崩溃熔断机制】绝对不能让一个畸形路口毁了整个世界
            // ==========================================
            try
            {
                Path64 smoothPoly = RoadBooleanUtility.GenerateSmoothIntersectionPolygon(nodePos, connectedEdges, roadWidth);
                var verts = smoothPoly.Select(pt => new Vector3((float)(pt.X / 1000.0), 0f, (float)(pt.Y / 1000.0))).ToArray();
                if (verts.Length < 3) continue;

                Vector3 center = Vector3.zero;
                foreach (var v in verts) center += v;
                center /= verts.Length;

                float mainAngle = RoadUVProjector.ComputeJunctionMainAngle(nodePos, neighborPositions);
                junctionInfosForRoad.Add(new RoadUVProjector.JunctionInfo
                {
                    poly = verts,
                    degree = node.neighbors.Count,
                    center = center,
                    mainAngle = mainAngle
                });
                junctionPolys.Add(verts);
                nodeVisualCenters[i] = center; // 记录视觉中心
            }
            catch (System.Exception ex)
            {
                // 捕获到异常，向 a5 观测官发送警报，并强行跳过该路口
                Debug.LogError($"[ProceduralRoadBuilder] 🚨 节点 {node.id} 几何生成崩溃，已熔断跳过！原因: {ex.Message}");
                // 生成红色报错方块
                CreateErrorBox(nodePos);
                continue;
            }
        }

        List<Vector3[]> allPolys = new List<Vector3[]>(edgePolys);
        allPolys.AddRange(junctionPolys);
        finalRoadUnion = RoadBooleanUtility.MergeRoadPolygonsToPaths64(allPolys);
        if (finalRoadUnion.Count == 0)
        {
            Debug.LogWarning("[ProceduralRoadBuilder] 合并后道路轮廓为空。");
            return;
        }

        // 创建道路网格
        Mesh roadMesh = BuildUnifiedMesh(finalRoadUnion, edgeInfosForRoad, junctionInfosForRoad);
        if (roadMesh == null) return;

        Material[] materials = BuildMaterialArray(fallbackMat);
        CreateRoadObject("Road_Union_Mesh", roadMesh, materials, roadLayer, meshRoot.transform);
        Debug.Log($"[ProceduralRoadBuilder] ✅ 道路网格生成完成（顶点 {roadMesh.vertexCount}）");
    }

    // ==========================================
    // 真理反哺：将路口视觉重心写回 WorldModel
    // ==========================================
    private void ReportVisualCentersToWorldModel()
    {    
        if (WorldModel.Instance == null) return;    
        
        foreach (var kv in nodeVisualCenters)    
        {    
            int nodeId = kv.Key;    
            Vector3 center = kv.Value;    
            WorldModel.Instance.UpdateNodeVisualPosition(nodeId, center);    
        }
        
        // 【必须添加】通知 a4 重建 KDTree，使新坐标生效
        WorldModel.Instance.RebuildSpatialIndex();
    }

    // 创建红色错误方块
    private void CreateErrorBox(Vector3 position)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "CRASH_NODE";
        box.transform.position = position + Vector3.up * 2f;
        box.transform.localScale = Vector3.one * 3f;
        box.GetComponent<Renderer>().material.color = Color.red;
        // 保留它以便调试，也可以添加 Destroy(box, 60f);
    }

    // ==========================================
    // 网格构建（应用高度）
    // ==========================================
    private Mesh BuildUnifiedMesh(Paths64 roadUnion,
                                  List<RoadUVProjector.EdgeRoadInfo> edgeInfos,
                                  List<RoadUVProjector.JunctionInfo> junctionInfos)
    {
        Polygon polygon = new Polygon();
        foreach (var path in roadUnion)
        {
            var verts = path.Select(pt => new Vertex((double)pt.X / 1000.0, (double)pt.Y / 1000.0)).ToList();
            polygon.Add(new Contour(verts));
        }

        GenericMesher mesher = new GenericMesher();
        var mesh = mesher.Triangulate(polygon);

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvList = new List<Vector2>();
        List<int>[] subTris = new List<int>[6];
        for (int i = 0; i < 6; i++) subTris[i] = new List<int>();

        foreach (var tri in mesh.Triangles)
        {
            Vector3 a_raw = new Vector3((float)tri.GetVertex(0).X, 0f, (float)tri.GetVertex(0).Y);
            Vector3 b_raw = new Vector3((float)tri.GetVertex(1).X, 0f, (float)tri.GetVertex(1).Y);
            Vector3 c_raw = new Vector3((float)tri.GetVertex(2).X, 0f, (float)tri.GetVertex(2).Y);
            Vector3 center = (a_raw + b_raw + c_raw) / 3f;

            RoadUVProjector.TriangleRegionInfo info = RoadUVProjector.ClassifyTriangle(center, edgeInfos, junctionInfos);

            Vector3 a = new Vector3(a_raw.x, GetHeightAt(a_raw.x, a_raw.z) + roadHeightOffset, a_raw.z);
            Vector3 b = new Vector3(b_raw.x, GetHeightAt(b_raw.x, b_raw.z) + roadHeightOffset, b_raw.z);
            Vector3 c = new Vector3(c_raw.x, GetHeightAt(c_raw.x, c_raw.z) + roadHeightOffset, c_raw.z);

            int idx0 = vertices.Count; vertices.Add(a);
            int idx1 = vertices.Count; vertices.Add(b);
            int idx2 = vertices.Count; vertices.Add(c);

            Vector2 uvA, uvB, uvC;
            if (info.isJunction)
            {
                var junc = junctionInfos[info.junctionIndex];
                uvA = RoadUVProjector.JunctionCenteredUV(a, junc.center, junc.mainAngle, roadWidth, uvScale);
                uvB = RoadUVProjector.JunctionCenteredUV(b, junc.center, junc.mainAngle, roadWidth, uvScale);
                uvC = RoadUVProjector.JunctionCenteredUV(c, junc.center, junc.mainAngle, roadWidth, uvScale);
            }
            else
            {
                var edge = edgeInfos[info.edgeIndex];
                uvA = RoadUVProjector.EdgeLocalUV(a, edge.origin, edge.forward, edge.right, uvScale);
                uvB = RoadUVProjector.EdgeLocalUV(b, edge.origin, edge.forward, edge.right, uvScale);
                uvC = RoadUVProjector.EdgeLocalUV(c, edge.origin, edge.forward, edge.right, uvScale);
            }

            uvList.Add(uvA); uvList.Add(uvB); uvList.Add(uvC);

            int subIdx = info.subMeshIndex;
            subTris[subIdx].Add(idx2);
            subTris[subIdx].Add(idx1);
            subTris[subIdx].Add(idx0);
        }

        Mesh roadMesh = new Mesh();
        if (vertices.Count > 65535)
            roadMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        roadMesh.subMeshCount = 6;
        roadMesh.SetVertices(vertices);
        roadMesh.uv = uvList.ToArray();
        for (int i = 0; i < 6; i++)
            roadMesh.SetTriangles(subTris[i], i);
        roadMesh.RecalculateNormals();
        roadMesh.RecalculateBounds();
        return roadMesh;
    }

    // ==========================================
    // 工具方法（保留）
    // ==========================================
    private float SampleTerrainHeight(float worldX, float worldZ) => GetHeightAt(worldX, worldZ);

    private (Vector3 start, Vector3 end) NormalizeEdgeDirection(Vector3 posA, Vector3 posB)
    {
        if (posA.x > posB.x + 0.001f) return (posB, posA);
        return (posA, posB);
    }

    private RoadDirection ClassifyDirection(Vector3 posA, Vector3 posB)
    {
        Vector3 dir = (posB - posA).normalized;
        float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360;
        if ((angle >= 0 && angle < 30) || (angle >= 150 && angle < 210) || (angle >= 330 && angle <= 360))
            return RoadDirection.Horizontal;
        if ((angle >= 60 && angle < 120) || (angle >= 240 && angle < 300))
            return RoadDirection.Vertical;
        return RoadDirection.Diagonal;
    }

    private void PrecomputeUnifiedHeights()
    {
        int nodeCount = roadGen.nodes.Count;
        nodeUnifiedHeight = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            var node = roadGen.nodes[i];
            float sumY = node.position.y;
            int count = 1;
            if (node.neighbors != null)
                foreach (int nbId in node.neighbors)
                    if (nbId >= 0 && nbId < nodeCount) { sumY += roadGen.nodes[nbId].position.y; count++; }
            nodeUnifiedHeight[i] = Mathf.Lerp(node.position.y, sumY / count, 0.3f);
        }
    }

    private Vector3 GetUnifiedNodePosition(int nodeId)
    {
        Vector3 pos = roadGen.nodes[nodeId].position;
        pos.y = nodeUnifiedHeight[nodeId] + roadHeightOffset;
        return pos;
    }

    private Spline BuildSimpleSpline(Vector3 posA, Vector3 posB)
    {
        Vector3 dir = (posB - posA).normalized;
        if (dir.sqrMagnitude < 0.0001f) return null;
        float dist = Vector3.Distance(posA, posB);
        float tLen = Mathf.Min(dist * tangentLength, maxTangentLength);
        BezierKnot knotA = new BezierKnot(posA, -dir * tLen, dir * tLen, Quaternion.LookRotation(dir));
        BezierKnot knotB = new BezierKnot(posB, dir * tLen, -dir * tLen, Quaternion.LookRotation(-dir));
        Spline spline = new Spline();
        spline.Add(knotA, TangentMode.Broken);
        spline.Add(knotB, TangentMode.Broken);
        spline.Closed = false;
        return spline;
    }

    private List<Vector3> SampleSpline(Spline spline, float step)
    {
        List<Vector3> points = new List<Vector3>();
        float length = spline.GetLength();
        if (length < 0.01f) return points;
        int count = Mathf.Max(2, Mathf.CeilToInt(length / step) + 1);
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            points.Add((Vector3)spline.EvaluatePosition(t));
        }
        return points;
    }

    private Material[] BuildMaterialArray(Material fallback)
    {
        Material[] mats = new Material[6];
        if (useCountrysideUniformMaterials)
        {
            for (int i = 0; i < 3; i++) mats[i] = countrysideRoadMaterial ? countrysideRoadMaterial : fallback;
            for (int i = 3; i < 6; i++) mats[i] = countrysideJunctionMaterial ? countrysideJunctionMaterial : fallback;
        }
        else
        {
            mats[0] = horizontalRoadMaterial ? horizontalRoadMaterial : fallback;
            mats[1] = verticalRoadMaterial ? verticalRoadMaterial : fallback;
            mats[2] = diagonalRoadMaterial ? diagonalRoadMaterial : fallback;
            mats[3] = tJunctionMaterial ? tJunctionMaterial : fallback;
            mats[4] = crossJunctionMaterial ? crossJunctionMaterial : fallback;
            mats[5] = complexJunctionMaterial ? complexJunctionMaterial : fallback;
        }
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
        obj.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private void EnsureMaterials()
    {
        if (roadMaterial == null)
        {
            roadMaterial = new Material(Shader.Find("Standard"));
            roadMaterial.color = new Color(0.2f, 0.2f, 0.2f);
        }
        if (terrainBaseMaterial == null)
        {
            terrainBaseMaterial = new Material(Shader.Find("Standard"));
            terrainBaseMaterial.color = new Color(0.3f, 0.5f, 0.3f);
            terrainBaseMaterial.renderQueue = 1999;
        }
    }

    public void ClearRoads()
    {
        if (meshRoot != null) { DestroyImmediate(meshRoot); meshRoot = null; }
        if (terrainRoot != null) { DestroyImmediate(terrainRoot); terrainRoot = null; }
        GameObject leftover = GameObject.Find("Procedural_Road_System");
        if (leftover != null) DestroyImmediate(leftover);
    }

    private void Start() => roadGen = GetComponent<RoadNetworkGenerator>();
}