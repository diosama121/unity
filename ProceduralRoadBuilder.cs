using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Clipper2Lib;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
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

    [Header("=== 乡村地形起伏 ===")]
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

    [Header("=== 地表生成 ===")]
    public bool generateTerrainBase = true;
    public Material terrainBaseMaterial;
    public float terrainBaseHeightOffset = -0.1f;

    [Header("=== 调试可视化 ===")]
    public bool showSplineGizmos = false;

    private RoadNetworkGenerator roadGen;
    private GameObject meshRoot;
    private GameObject terrainRoot;
    private float[] nodeUnifiedHeight;

    private enum RoadDirection { Horizontal, Vertical, Diagonal }

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
        Material fallbackMat = roadMaterial;

        // ── 1. 构建 EdgeRoadInfo 列表（保留原始多边形） ──
        HashSet<string> processedEdges = new HashSet<string>();
        List<RoadUVProjector.EdgeRoadInfo> edgeInfos = new List<RoadUVProjector.EdgeRoadInfo>();
        List<Vector3[]> edgePolysForUnion = new List<Vector3[]>();

        foreach (var edge in roadGen.edges)
        {
            int idA = edge.Item1;
            int idB = edge.Item2;
            string key = idA < idB ? $"{idA}_{idB}" : $"{idB}_{idA}";
            if (processedEdges.Contains(key)) continue;
            processedEdges.Add(key);

            if (idA < 0 || idA >= roadGen.nodes.Count ||
                idB < 0 || idB >= roadGen.nodes.Count) continue;

            Vector3 posA = GetUnifiedNodePosition(idA);
            Vector3 posB = GetUnifiedNodePosition(idB);
            if (Vector3.Distance(posA, posB) < 0.5f) continue;

            // 方向规范化：确保 (posA, posB) 的 UV 方向稳定（避免镜像）
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

            edgeInfos.Add(new RoadUVProjector.EdgeRoadInfo
            {
                poly = polyArray,
                start = posA,
                end = posB,
                forward = dir,
                right = right,
                origin = posA,
                targetSubMesh = sub
            });
            edgePolysForUnion.Add(polyArray);
        }

        if (edgeInfos.Count == 0)
        {
            Debug.LogWarning("[ProceduralRoadBuilder] 没有生成任何道路多边形。");
            return;
        }

        // ── 2. 构建 JunctionInfo 列表（保留原始多边形） ──
        List<RoadUVProjector.JunctionInfo> junctionInfos = new List<RoadUVProjector.JunctionInfo>();
        List<Vector3[]> junctionPolysForUnion = new List<Vector3[]>();

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

            Path64 smoothPoly = RoadBooleanUtility.GenerateSmoothIntersectionPolygon(nodePos, connectedEdges, roadWidth);
            var verts = smoothPoly.Select(pt => new Vector3((float)(pt.X / 1000.0), 0f, (float)(pt.Y / 1000.0))).ToArray();
            if (verts.Length < 3) continue;

            Vector3 center = Vector3.zero;
            foreach (var v in verts) center += v;
            center /= verts.Length;

            // 主方向计算前先对邻居按极角排序，消除顺序依赖
            float mainAngle = RoadUVProjector.ComputeJunctionMainAngle(nodePos, neighborPositions);

            junctionInfos.Add(new RoadUVProjector.JunctionInfo
            {
                poly = verts,
                degree = node.neighbors.Count,
                center = center,
                mainAngle = mainAngle
            });
            junctionPolysForUnion.Add(verts);
        }

        // ── 3. 合并所有多边形为道路总轮廓 ──
        List<Vector3[]> allPolys = new List<Vector3[]>();
        allPolys.AddRange(edgePolysForUnion);
        allPolys.AddRange(junctionPolysForUnion);
        Paths64 finalRoadUnion = RoadBooleanUtility.MergeRoadPolygonsToPaths64(allPolys);
        if (finalRoadUnion.Count == 0)
        {
            Debug.LogWarning("[ProceduralRoadBuilder] 合并后道路轮廓为空。");
            return;
        }

        // ── 4. 一体化网格生成（含精确分类） ──
        Mesh roadMesh = BuildUnifiedMesh(finalRoadUnion, edgeInfos, junctionInfos);
        if (roadMesh == null) return;

        Material[] materials = BuildMaterialArray(fallbackMat);
        CreateRoadObject("Road_Union_Mesh", roadMesh, materials, roadLayer, meshRoot.transform);
        Debug.Log($"[ProceduralRoadBuilder] ✅ 道路网格生成完成（顶点 {roadMesh.vertexCount}，三角形 {roadMesh.triangles.Length / 3}）");

        if (generateTerrainBase)
        {
            // 打印 finalRoadUnion 层级信息
            for (int i = 0; i < finalRoadUnion.Count; i++)
            {
                double area = Clipper.Area(finalRoadUnion[i]);
                Debug.Log($"[RoadHole] 路径{i} 顶点数:{finalRoadUnion[i].Count} 面积:{area / 1e6:F2}m² 方向:{(area > 0 ? "正(外轮廓)" : "负(洞/岛)")}");
            }
            GenerateTerrainBase(finalRoadUnion);
        }
    }
    /// <summary>
    /// 统一道路方向：确保 UV 起始方向一致，消除镜像。
    /// 规则：比较起点和终点的 X 坐标，若 start.x > end.x 则交换。
    /// </summary>
    private (Vector3 start, Vector3 end) NormalizeEdgeDirection(Vector3 posA, Vector3 posB)
    {
        if (posA.x > posB.x + 0.001f)   // 统一按 X 轴排序（可扩展为字典序）
            return (posB, posA);
        return (posA, posB);
    }

    private Mesh BuildUnifiedMesh(Paths64 roadUnion,
                                  List<RoadUVProjector.EdgeRoadInfo> edgeInfos,
                                  List<RoadUVProjector.JunctionInfo> junctionInfos)
    {
        // 三角剖分（同前）
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

        // 预先为每个 junction 和 edge 构建 2D 线段列表用于包含性判断
        var junctionPolygons2D = junctionInfos.Select(j => j.poly.Select(v => new Vector2(v.x, v.z)).ToList()).ToList();
        var edgePolygons2D = edgeInfos.Select(e => e.poly.Select(v => new Vector2(v.x, v.z)).ToList()).ToList();

        foreach (var tri in mesh.Triangles)
        {
            Vector3 a_raw = new Vector3((float)tri.GetVertex(0).X, 0f, (float)tri.GetVertex(0).Y);
            Vector3 b_raw = new Vector3((float)tri.GetVertex(1).X, 0f, (float)tri.GetVertex(1).Y);
            Vector3 c_raw = new Vector3((float)tri.GetVertex(2).X, 0f, (float)tri.GetVertex(2).Y);
            Vector3 center = (a_raw + b_raw + c_raw) / 3f;

            // 使用包含判定优先分类（Junction > Edge）
            RoadUVProjector.TriangleRegionInfo info = RoadUVProjector.ClassifyTriangle(
      center, edgeInfos, junctionInfos);

            Vector3 a = new Vector3(a_raw.x, SampleTerrainHeight(a_raw.x, a_raw.z) + roadHeightOffset, a_raw.z);
            Vector3 b = new Vector3(b_raw.x, SampleTerrainHeight(b_raw.x, b_raw.z) + roadHeightOffset, b_raw.z);
            Vector3 c = new Vector3(c_raw.x, SampleTerrainHeight(c_raw.x, c_raw.z) + roadHeightOffset, c_raw.z);

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

            // 材质分配：junction 使用 3~5，edge 使用 0~2，确保 junction 优先
            int subIdx = info.subMeshIndex;   // 已经是 0~5 的最终子网格索引
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

    // ================================================================
    // 地表生成 —— 第二阶段重构：主地块 + 道路洞剖分法
    // ================================================================
  private void GenerateTerrainBase(Paths64 roadUnion)
{
    // 1. 净化道路轮廓
    Paths64 cleanUnion = CleanRoadUnion(roadUnion);

    // 2. 建立主地块矩形
    Bounds bounds = GetNetworkBounds();
    float margin = 20f;
    float minX = bounds.min.x - margin;
    float minZ = bounds.min.z - margin;
    float maxX = bounds.max.x + margin;
    float maxZ = bounds.max.z + margin;

    Path64 groundRect = new Path64
    {
        new Point64((long)(minX * 1000.0), (long)(minZ * 1000.0)),
        new Point64((long)(maxX * 1000.0), (long)(minZ * 1000.0)),
        new Point64((long)(maxX * 1000.0), (long)(maxZ * 1000.0)),
        new Point64((long)(minX * 1000.0), (long)(maxZ * 1000.0))
    };

    // 3. 从 roadUnion 中识别层级：面积最大正路径 = 一级道路洞，负路径 = 二级陆地岛
    int roadHoleIndex = -1;
    double maxPositiveArea = 0;
    List<int> islandIndices = new List<int>();

    for (int i = 0; i < cleanUnion.Count; i++)
    {
        double area = Clipper.Area(cleanUnion[i]);
        if (area > 0 && area > maxPositiveArea)
        {
            maxPositiveArea = area;
            roadHoleIndex = i;
        }
    }

    // 收集所有负面积路径作为内部保留岛
    for (int i = 0; i < cleanUnion.Count; i++)
    {
        if (i == roadHoleIndex) continue;
        double area = Clipper.Area(cleanUnion[i]);
        if (area < 0)
            islandIndices.Add(i);
    }

    Debug.Log($"[Terrain] 一级道路洞索引:{roadHoleIndex} 面积:{maxPositiveArea / 1e6:F2}m² 二级岛数量:{islandIndices.Count}");

    // 4. 构建 Triangle.NET Polygon（保留原始层级）
    Polygon poly = new Polygon();

    // 主外轮廓：groundRect
    var groundVerts = groundRect.Select(pt =>
        new Vertex((double)pt.X / 1000.0, (double)pt.Y / 1000.0)).ToList();
    poly.Add(new Contour(groundVerts));

    // 一级洞：道路整体占地区
    // 一级洞：道路整体占地区
if (roadHoleIndex >= 0)
{
    // 洞需要逆时针（Clipper正面积为顺时针，反转后为逆时针）
    Path64 holePath = new Path64(cleanUnion[roadHoleIndex]);
    holePath.Reverse();
    var holeVerts = holePath.Select(pt =>
        new Vertex((double)pt.X / 1000.0, (double)pt.Y / 1000.0)).ToList();

    var holeContour = new Contour(holeVerts);
    poly.Add(holeContour, true);   // 作为洞添加

    // 为洞添加内部种子点（Triangle.NET需要以此识别洞区域）
    if (holeVerts.Count >= 3)
    {
        // 取前N个顶点的平均位置作为种子点
        double sx = 0, sy = 0;
        int seedCount = Mathf.Min(3, holeVerts.Count);
        for (int s = 0; s < seedCount; s++)
        {
            sx += holeVerts[s].X;
            sy += holeVerts[s].Y;
        }
        poly.Holes.Add(new Point(sx / seedCount, sy / seedCount));
    }

    // 二级岛：道路内部的保留地块
    foreach (int islandIdx in islandIndices)
    {
        var islandPath = cleanUnion[islandIdx];
        // 负面积路径逆时针，岛需顺时针，反转
        Path64 islandClockwise = new Path64(islandPath);
        islandClockwise.Reverse();
        var islandVerts = islandClockwise.Select(pt =>
            new Vertex((double)pt.X / 1000.0, (double)pt.Y / 1000.0)).ToList();
        poly.Add(new Contour(islandVerts));   // 岛，不是洞
    }
}

    // 5. 三角剖分
    GenericMesher m = new GenericMesher();
    var tMesh = m.Triangulate(poly);

    List<Vector3> vertsList = new List<Vector3>();
    List<int> triList = new List<int>();
    Dictionary<int, int> map = new Dictionary<int, int>();
    int idx = 0;
    foreach (var v in tMesh.Vertices)
    {
        float x = (float)v.X, z = (float)v.Y;
        float y = SampleTerrainHeight(x, z) + terrainBaseHeightOffset;
        vertsList.Add(new Vector3(x, y, z));
        map[v.ID] = idx++;
    }
    foreach (var tri in tMesh.Triangles)
    {
     triList.Add(map[tri.GetVertex(0).ID]);
        triList.Add(map[tri.GetVertex(2).ID]); 
        triList.Add(map[tri.GetVertex(1).ID]);
    }

    Mesh terrainMesh = new Mesh();
    terrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
    terrainMesh.SetVertices(vertsList);
    terrainMesh.SetTriangles(triList, 0);
    terrainMesh.RecalculateNormals();

    if (terrainBaseMaterial != null || roadMaterial != null)
    {
        Material mat = terrainBaseMaterial ? terrainBaseMaterial : roadMaterial;
        mat.renderQueue = 1999;
        GameObject go = new GameObject("Terrain_Base");
        go.transform.SetParent(terrainRoot.transform, false);
        go.layer = LayerMask.NameToLayer("Default");
        go.AddComponent<MeshFilter>().sharedMesh = terrainMesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        Debug.Log($"[Terrain] 地表生成完成，顶点 {vertsList.Count}");
    }
}

    // 辅助：净化道路轮廓（保留自旧版）
    private Paths64 CleanRoadUnion(Paths64 roadUnion)
    {
        Paths64 cleanUnion = new Paths64();
        foreach (var p in roadUnion)
            cleanUnion.Add(new Path64(p));

        cleanUnion = Clipper.SimplifyPaths(cleanUnion, 2.0);
        Paths64 trimmedUnion = new Paths64();
        foreach (var path in cleanUnion)
            trimmedUnion.Add(Clipper.TrimCollinear(path, false));
        cleanUnion = trimmedUnion;

        cleanUnion = Clipper.InflatePaths(cleanUnion, 1.0, JoinType.Round, EndType.Polygon);
        cleanUnion = Clipper.InflatePaths(cleanUnion, -1.0, JoinType.Round, EndType.Polygon);
        return cleanUnion;
    }

    // 删除的旧方法：FilterTerrainPaths / NormalizePathDirection / Difference 流程
    // 不再需要。

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

    private float SampleTerrainHeight(float worldX, float worldZ)
    {
        if (roadGen != null && roadGen.isCountryside)
            return Mathf.PerlinNoise(worldX * terrainNoiseFrequency, worldZ * terrainNoiseFrequency)
                   * roadGen.countrysideHeightScale;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            return terrain.SampleHeight(new Vector3(worldX, 0f, worldZ));
        return 0f;
    }

    private Bounds GetNetworkBounds()
    {
        if (roadGen.nodes == null || roadGen.nodes.Count == 0)
            return new Bounds(Vector3.zero, Vector3.one * 100);
        Vector3 min = roadGen.nodes[0].position;
        Vector3 max = roadGen.nodes[0].position;
        foreach (var n in roadGen.nodes)
        {
            min = Vector3.Min(min, n.position);
            max = Vector3.Max(max, n.position);
        }
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
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
            roadMaterial.color = new Color(0.18f, 0.18f, 0.18f);
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