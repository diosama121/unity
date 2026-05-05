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

    [Header("=== 城镇模式参数 ===")]
    public bool generateCity = true;                // 是否生成城市建筑与人行道
    public float buildingHeight = 10f;             // 建筑拉伸高度
    public Material buildingMaterial;              // 建筑材质
    public float sidewalkWidth = 2f;               // 人行道宽度
    public Material sidewalkMaterial;              // 人行道材质
    public float sidewalkHeight = 0.2f;            // 人行道台阶高度

    [Header("=== 调试可视化 ===")]
    public bool showSplineGizmos = false;

    private RoadNetworkGenerator roadGen;
    private GameObject meshRoot;
    private GameObject terrainRoot;
    private float[] nodeUnifiedHeight;

    private enum RoadDirection { Horizontal, Vertical, Diagonal }

    // ================================================================
    // 统一高度获取（V2.0 架构：优先 TerrainGridSystem）
    // ================================================================
    private float GetHeightAt(float worldX, float worldZ)
    {
        if (TerrainGridSystem.Instance != null)
            return TerrainGridSystem.Instance.GetHeightAt(worldX, worldZ);
        // 降级到旧逻辑（兼容无 TerrainGridSystem 的情况）
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            return terrain.SampleHeight(new Vector3(worldX, 0f, worldZ));
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

        // ── 2. 构建 JunctionInfo 列表 ──
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

        // ── 4. 一体化道路网格生成（含精确分类） ──
        Mesh roadMesh = BuildUnifiedMesh(finalRoadUnion, edgeInfos, junctionInfos);
        if (roadMesh == null) return;

        Material[] materials = BuildMaterialArray(fallbackMat);
        CreateRoadObject("Road_Union_Mesh", roadMesh, materials, roadLayer, meshRoot.transform);
        Debug.Log($"[ProceduralRoadBuilder] ✅ 道路网格生成完成（顶点 {roadMesh.vertexCount}）");

        // ── 5. 生成地表（带道路洞） ──
        if (generateTerrainBase)
            GenerateTerrainBase(finalRoadUnion);

        // ── 6. 城镇模式：生成建筑与人行道 ──
        if (generateCity && !roadGen.isCountryside)
            GenerateCityFromRoads(finalRoadUnion);
    }

    // ================================================================
    // 道路网格构建（应用地形高度）
    // ================================================================
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

            // 使用新的高度查询
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

    // ================================================================
    // 地表生成（法线已修正）
    // ================================================================
    private void GenerateTerrainBase(Paths64 roadUnion)
    {
        Paths64 cleanUnion = CleanRoadUnion(roadUnion);
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

        // 寻找最大正面积为洞，其余为岛
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
        for (int i = 0; i < cleanUnion.Count; i++)
        {
            if (i == roadHoleIndex) continue;
            double area = Clipper.Area(cleanUnion[i]);
            if (area < 0) islandIndices.Add(i);
        }

        Polygon poly = new Polygon();
        var groundVerts = groundRect.Select(pt => new Vertex((double)pt.X / 1000.0, (double)pt.Y / 1000.0)).ToList();
        poly.Add(new Contour(groundVerts));

        if (roadHoleIndex >= 0)
        {
            Path64 holePath = new Path64(cleanUnion[roadHoleIndex]);
            holePath.Reverse();
            var holeVerts = holePath.Select(pt => new Vertex((double)pt.X / 1000.0, (double)pt.Y / 1000.0)).ToList();
            poly.Add(new Contour(holeVerts), true);
            // 添加种子点
            if (holeVerts.Count >= 3)
            {
                double sx = 0, sy = 0;
                int seedCount = Mathf.Min(3, holeVerts.Count);
                for (int s = 0; s < seedCount; s++)
                { sx += holeVerts[s].X; sy += holeVerts[s].Y; }
                poly.Holes.Add(new Point(sx / seedCount, sy / seedCount));
            }
        }

        foreach (int islandIdx in islandIndices)
        {
            Path64 islandPath = new Path64(cleanUnion[islandIdx]);
            islandPath.Reverse();
            var islandVerts = islandPath.Select(pt => new Vertex((double)pt.X / 1000.0, (double)pt.Y / 1000.0)).ToList();
            poly.Add(new Contour(islandVerts));
        }

        GenericMesher m = new GenericMesher();
        var tMesh = m.Triangulate(poly);

        List<Vector3> vertsList = new List<Vector3>();
        List<int> triList = new List<int>();
        Dictionary<int, int> map = new Dictionary<int, int>();
        int idx = 0;
        foreach (var v in tMesh.Vertices)
        {
            float x = (float)v.X, z = (float)v.Y;
            float y = GetHeightAt(x, z) + terrainBaseHeightOffset;
            vertsList.Add(new Vector3(x, y, z));
            map[v.ID] = idx++;
        }

        // ★ 法线修正：三角形顺序改为 0,2,1
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
        terrainMesh.RecalculateBounds();

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

    // ================================================================
    // 城镇模式：建筑与人行道（V2.0 数学拉伸）
    // ================================================================
    private void GenerateCityFromRoads(Paths64 roadUnion)
    {
        ExtrudeBuildingsFromIslands(roadUnion);
        GenerateSidewalks(roadUnion);
        Debug.Log("[City] 城市建筑与人行道生成完成。");
    }

    private void ExtrudeBuildingsFromIslands(Paths64 roadUnion)
    {
        if (buildingMaterial == null) return;

        GameObject buildingRoot = new GameObject("City_Buildings");
        buildingRoot.transform.SetParent(transform, false);

        foreach (var path in roadUnion)
        {
            double area = Clipper.Area(path);
            if (area >= 0) continue;   // 只取负面积岛屿（内部地块）

            Path64 island = new Path64(path);
            island.Reverse();          // 变为顺时针（正面积）
            if (Clipper.Area(island) <= 0) continue;

            List<Vector3> baseVerts = island.Select(pt =>
                new Vector3((float)pt.X / 1000.0, 0, (float)pt.Y / 1000.0)).ToList();

            // 应用地形高度
            for (int i = 0; i < baseVerts.Count; i++)
            {
                var v = baseVerts[i];
                v.y = GetHeightAt(v.x, v.z);
                baseVerts[i] = v;
            }

            Mesh buildingMesh = ExtrudePolygon(baseVerts, buildingHeight);
            if (buildingMesh == null) continue;

            GameObject buildingObj = new GameObject("Building");
            buildingObj.transform.SetParent(buildingRoot.transform, false);
            buildingObj.AddComponent<MeshFilter>().sharedMesh = buildingMesh;
            buildingObj.AddComponent<MeshRenderer>().sharedMaterial = buildingMaterial;
            // 简单碰撞体（可选）
            // buildingObj.AddComponent<MeshCollider>().sharedMesh = buildingMesh;
        }
    }

    private Mesh ExtrudePolygon(List<Vector3> baseVerts, float height)
    {
        if (baseVerts.Count < 3) return null;

        int n = baseVerts.Count;
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        // 底面（忽略，只做顶面和侧面）
        // 顶面顶点
        List<Vector3> topVerts = new List<Vector3>();
        foreach (var v in baseVerts)
            topVerts.Add(v + Vector3.up * height);

        // 侧面：每一边两个三角形
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 aRect = baseVerts[i];
            Vector3 bRect = baseVerts[j];
            Vector3 cRect = topVerts[i];
            Vector3 dRect = topVerts[j];

            int vi = verts.Count;
            verts.Add(aRect);
            verts.Add(bRect);
            verts.Add(cRect);
            verts.Add(dRect);

            tris.Add(vi);
            tris.Add(vi + 2);
            tris.Add(vi + 1);
            tris.Add(vi + 2);
            tris.Add(vi + 3);
            tris.Add(vi + 1);
        }

        // 顶面：使用多边形三角剖分（简单凸多边形风扇法）
        // 将 topVerts 加入，并生成三角形
        int topStart = verts.Count;
        verts.AddRange(topVerts);
        for (int i = 1; i < n - 1; i++)
        {
            tris.Add(topStart);
            tris.Add(topStart + i);
            tris.Add(topStart + i + 1);
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

   private void GenerateSidewalks(Paths64 roadUnion)
{
    if (sidewalkMaterial == null) return;

    ClipperOffset co = new ClipperOffset();
    foreach (var path in roadUnion)
    {
        if (Clipper.Area(path) > 0)
            co.AddPath(path, JoinType.Round, EndType.Joined);
    }

    long expandDelta = (long)(sidewalkWidth * 1000.0);

    // ★ 修复：Execute 无返回值，需传入已有容器
    Paths64 expanded = new Paths64();
    co.Execute(expanded, expandDelta);

    Paths64 sidewalkPaths = Clipper.Difference(expanded, roadUnion, FillRule.NonZero);
    if (sidewalkPaths.Count == 0) return;

    GameObject sidewalkRoot = new GameObject("Sidewalks");
    sidewalkRoot.transform.SetParent(transform, false);

    foreach (var sp in sidewalkPaths)
    {
        if (sp.Count < 3) continue;

        // ★ 修复：显式转为 float
        var verts = sp.Select(pt => new Vector3((float)(pt.X / 1000.0), 0, (float)(pt.Y / 1000.0))).ToList();

        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            v.y = GetHeightAt(v.x, v.z);
            verts[i] = v;
        }

        Mesh sidewalkMesh = ExtrudePolygon(verts, sidewalkHeight);
        if (sidewalkMesh == null) continue;

        GameObject sidewalkObj = new GameObject("Sidewalk");
        sidewalkObj.transform.SetParent(sidewalkRoot.transform, false);
        sidewalkObj.AddComponent<MeshFilter>().sharedMesh = sidewalkMesh;
        sidewalkObj.AddComponent<MeshRenderer>().sharedMaterial = sidewalkMaterial;
    }
}

    // ================================================================
    // 辅助方法（多数保持原样）
    // ================================================================
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

    private Paths64 CleanRoadUnion(Paths64 roadUnion)
    {
        Paths64 cleanUnion = new Paths64();
        foreach (var p in roadUnion) cleanUnion.Add(new Path64(p));
        cleanUnion = Clipper.SimplifyPaths(cleanUnion, 2.0);
        Paths64 trimmedUnion = new Paths64();
        foreach (var path in cleanUnion)
            trimmedUnion.Add(Clipper.TrimCollinear(path, false));
        cleanUnion = trimmedUnion;
        cleanUnion = Clipper.InflatePaths(cleanUnion, 1.0, JoinType.Round, EndType.Polygon);
        cleanUnion = Clipper.InflatePaths(cleanUnion, -1.0, JoinType.Round, EndType.Polygon);
        return cleanUnion;
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