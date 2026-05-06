using System.Collections.Generic;
using UnityEngine;
using Clipper2Lib;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using System.Linq;
using System;

/// <summary>
/// V2.0 纯净外围环境生成器 (a1 分裂模块)
/// 专门负责根据最终的道路轮廓，生成地表、建筑、人行道
/// </summary>
public class EnvironmentMeshBuilder : MonoBehaviour
{
    private ProceduralRoadBuilder paramsSource;
    private class RoadContourCache
    {
        public Path64 path;
        public Rect aabb;

        public RoadContourCache(Path64 p)
        {
            path = p;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var pt in p)
            {
                float x = (float)(pt.X / 1000.0);
                float y = (float)(pt.Y / 1000.0);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            aabb = Rect.MinMaxRect(minX - 0.1f, minY - 0.1f, maxX + 0.1f, maxY + 0.1f);
        }
    }
  private bool IsIllegalTriangleEdgeCheck(TriangleNet.Topology.Triangle tri, List<RoadSegmentCache> roadSegs)
{
    Vector2 a = new Vector2((float)tri.GetVertex(0).X, (float)tri.GetVertex(0).Y);
    Vector2 b = new Vector2((float)tri.GetVertex(1).X, (float)tri.GetVertex(1).Y);
    Vector2 c = new Vector2((float)tri.GetVertex(2).X, (float)tri.GetVertex(2).Y);

    (Vector2, Vector2)[] triEdges = { (a, b), (b, c), (c, a) };

    foreach (var road in roadSegs)
    {
        foreach (var roadSeg in road.segments)
        {
            foreach (var triEdge in triEdges)
            {
                if (LineSegmentsIntersect(triEdge.Item1, triEdge.Item2, roadSeg.a, roadSeg.b))
                    return true;
            }
        }
    }
    return false;
}

// 线段相交检测（不含端点重叠，可调整容差）
private bool LineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
{
    float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    Vector2 r = p2 - p1;
    Vector2 s = q2 - q1;
    float rxs = Cross(r, s);
    float qpxr = Cross(q1 - p1, r);

    if (Mathf.Abs(rxs) < 0.0001f)
    {
        // 共线或平行，视为不相交（因为我们关心的是真正的跨越）
        return false;
    }

    float t = Cross(q1 - p1, s) / rxs;
    float u = qpxr / rxs;

    // 允许微小容差，仅当严格在(0,1)区间内才认为相交（排除端点重合）
    return (t > 0.001f && t < 0.999f && u > 0.001f && u < 0.999f);
}
    public void GenerateEnvironment(Paths64 finalRoadUnion, ProceduralRoadBuilder paramsSource)
    {
        this.paramsSource = paramsSource;

        if (paramsSource.generateTerrainBase)
        {
            GenerateTerrainBase(finalRoadUnion);
        }

        if (paramsSource.generateCity && !paramsSource.RoadGen.isCountryside)
        {
            ExtrudeBuildingsFromIslands(finalRoadUnion);
            GenerateSidewalks(finalRoadUnion);
            Debug.Log("[Environment] 🏙️ 城市建筑与人行道生成完成。");
        }
    }

    private float GetHeightAt(float x, float z)
    {
        if (TerrainGridSystem.Instance != null)
            return TerrainGridSystem.Instance.GetHeightAt(x, z);
        return 0f;
    }

    // ==========================================
    // 地表生成（强制 Margin = 50f）
    // ==========================================
    private void GenerateTerrainBase(Paths64 rawRoadUnion)
{
    if (rawRoadUnion == null || rawRoadUnion.Count == 0) return;

    // --- 防御层1：拓扑净化 ---
    Paths64 cleanRoads = new Paths64();
    foreach (var path in rawRoadUnion)
    {
        if (Math.Abs(Clipper.Area(path)) < 1.0 * 1000 * 1000) continue;
        cleanRoads.Add(path);
    }

    // 构建空间缓存
    List<RoadContourCache> roadCaches = new List<RoadContourCache>();
    List<RoadSegmentCache> roadSegmentCaches = new List<RoadSegmentCache>();
    foreach (var path in cleanRoads)
    {
        roadCaches.Add(new RoadContourCache(path));
        roadSegmentCaches.Add(new RoadSegmentCache(path));
    }

    // --- 生成外裙边（道路向外膨胀 18 米） ---
    ClipperOffset skirtOffset = new ClipperOffset();
    foreach (var path in cleanRoads)
        skirtOffset.AddPath(path, JoinType.Round, EndType.Polygon);
    Paths64 outerSkirt = new Paths64();
    skirtOffset.Execute(18.0 * 1000.0, outerSkirt);
    // 简化裙边以减少顶点数（可选）
    outerSkirt = Clipper.SimplifyPaths(outerSkirt, 2.0);

    // 使用裙边的第一个轮廓作为外边界，若膨胀失败则回退为动态包围盒
    Path64 outerBoundary;
    if (outerSkirt.Count > 0)
    {
        outerBoundary = outerSkirt[0];
        // 如果膨胀结果有多个轮廓，取面积最大的
        double maxArea = Clipper.Area(outerBoundary);
        for (int i = 1; i < outerSkirt.Count; i++)
        {
            double area = Clipper.Area(outerSkirt[i]);
            if (area > maxArea) { outerBoundary = outerSkirt[i]; maxArea = area; }
        }
    }
    else
    {
        // 回退：基于节点包围盒扩展 50m
        Bounds netBounds = GetNetworkBounds();
        outerBoundary = new Path64
        {
            new Point64((netBounds.min.x - 50) * 1000, (netBounds.min.z - 50) * 1000),
            new Point64((netBounds.max.x + 50) * 1000, (netBounds.min.z - 50) * 1000),
            new Point64((netBounds.max.x + 50) * 1000, (netBounds.max.z + 50) * 1000),
            new Point64((netBounds.min.x - 50) * 1000, (netBounds.max.z + 50) * 1000)
        };
    }

    // --- 构建 Triangle.NET 约束多边形 ---
    Polygon poly = new Polygon();

    // 外边界（裙边）
    var outerVerts = outerBoundary.Select(pt => new Vertex(pt.X / 1000.0, pt.Y / 1000.0)).ToList();
    poly.Add(new Contour(outerVerts));

    // 道路空洞
    foreach (var path in cleanRoads)
    {
        var holeVerts = path.Select(pt => new Vertex(pt.X / 1000.0, pt.Y / 1000.0)).ToList();
        poly.Add(new Contour(holeVerts));
    }

    // --- CDT 三角剖分（彻底关闭质量优化，零度角！）---
    var meshParams = new ConstraintOptions { ConformingDelaunay = true };
    // 关键：不传入 QualityOptions，或使用默认构造（相当于 MinAngle=0）
    var triangleMesh = poly.Triangulate(meshParams); // 不传 QualityOptions

    // --- 提取网格（边相交检测剔除越界三角形）---
    List<Vector3> finalVertices = new List<Vector3>();
    List<int> finalTriangles = new List<int>();
    Dictionary<int, int> vertexIndexMap = new Dictionary<int, int>();

    foreach (var tri in triangleMesh.Triangles)
    {
        // 防御层3终极版：边相交检测
        if (IsIllegalTriangleEdgeCheck(tri, roadSegmentCaches)) continue;

        for (int i = 2; i >= 0; i--)
        {
            var v = tri.GetVertex(i);
            if (!vertexIndexMap.TryGetValue(v.ID, out int newIndex))
            {
                newIndex = finalVertices.Count;
                float realY = GetHeightAt((float)v.X, (float)v.Y);
                finalVertices.Add(new Vector3((float)v.X, realY, (float)v.Y));
            }
            finalTriangles.Add(newIndex);
        }
    }

    // 构建 Mesh 并附到场景（与原代码一致）
    Mesh terrainMesh = new Mesh();
    terrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
    terrainMesh.SetVertices(finalVertices);
    terrainMesh.SetTriangles(finalTriangles, 0);
    terrainMesh.RecalculateNormals();
    terrainMesh.RecalculateBounds();

    Material mat = paramsSource.terrainBaseMaterial ? paramsSource.terrainBaseMaterial : paramsSource.roadMaterial;
    if (mat == null) mat = new Material(Shader.Find("Standard"));
    mat.renderQueue = 1999;

    GameObject terrainRoot = GameObject.Find("Procedural_Terrain");
    if (terrainRoot == null)
    {
        terrainRoot = new GameObject("Procedural_Terrain");
        terrainRoot.transform.SetParent(transform, false);
    }
    GameObject go = new GameObject("Terrain_Base");
    go.transform.SetParent(terrainRoot.transform, false);
    go.layer = LayerMask.NameToLayer("Default");
    go.AddComponent<MeshFilter>().sharedMesh = terrainMesh;
    MeshRenderer mr = go.AddComponent<MeshRenderer>();
    mr.sharedMaterial = mat;
    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    mr.receiveShadows = true;

    Debug.Log($"[Terrain V3.1] 裙边地表生成完成，顶点 {finalVertices.Count}");
}

    // ==========================================
    // 建筑生成（强制向内收缩 1.5m）
    // ==========================================
    private void ExtrudeBuildingsFromIslands(Paths64 roadUnion)
    {
        if (paramsSource.buildingMaterial == null) return;

        GameObject buildingRoot = new GameObject("City_Buildings");
        buildingRoot.transform.SetParent(transform, false);

        // 提取所有负面积岛屿，并整体收缩
        ClipperOffset co = new ClipperOffset();
        foreach (var path in roadUnion)
        {
            double area = Clipper.Area(path);
            if (area >= 0) continue;
            Path64 island = new Path64(path);
            island.Reverse();
            co.AddPath(island, JoinType.Miter, EndType.Polygon);
        }

        Paths64 shrunkIslands = new Paths64();
        // 强制向内收缩 1.5 米
        co.Execute(-1.5 * 1000.0, shrunkIslands);

        foreach (var safeIsland in shrunkIslands)
        {
            if (safeIsland.Count < 3) continue;
            List<Vector3> baseVerts = safeIsland.Select(pt =>
                new Vector3((float)(pt.X / 1000.0), 0f, (float)(pt.Y / 1000.0))).ToList();

            // 高度真理约束
            for (int i = 0; i < baseVerts.Count; i++)
            {
                var v = baseVerts[i];
                v.y = GetHeightAt(v.x, v.z);
                baseVerts[i] = v;
            }

            Mesh buildingMesh = ExtrudePolygon(baseVerts, paramsSource.buildingHeight);
            if (buildingMesh == null) continue;

            GameObject buildingObj = new GameObject("Building");
            buildingObj.transform.SetParent(buildingRoot.transform, false);
            buildingObj.AddComponent<MeshFilter>().sharedMesh = buildingMesh;
            buildingObj.AddComponent<MeshRenderer>().sharedMaterial = paramsSource.buildingMaterial;
        }
    }

    // ==========================================
    // 人行道生成
    // ==========================================
    private void GenerateSidewalks(Paths64 roadUnion)
    {
        if (paramsSource.sidewalkMaterial == null) return;
        ClipperOffset co = new ClipperOffset();
        foreach (var path in roadUnion)
            if (Clipper.Area(path) > 0)
                co.AddPath(path, JoinType.Round, EndType.Joined);

        long expandDelta = (long)(paramsSource.sidewalkWidth * 1000.0);
        Paths64 expanded = new Paths64();
        co.Execute(expandDelta, expanded);

        Paths64 sidewalkPaths = Clipper.Difference(expanded, roadUnion, FillRule.NonZero);
        if (sidewalkPaths.Count == 0) return;

        GameObject sidewalkRoot = new GameObject("Sidewalks");
        sidewalkRoot.transform.SetParent(transform, false);

        foreach (var sp in sidewalkPaths)
        {
            if (sp.Count < 3) continue;
            var verts = sp.Select(pt => new Vector3((float)(pt.X / 1000.0), 0, (float)(pt.Y / 1000.0))).ToList();
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                v.y = GetHeightAt(v.x, v.z);
                verts[i] = v;
            }
            Mesh sidewalkMesh = ExtrudePolygon(verts, paramsSource.sidewalkHeight);
            if (sidewalkMesh == null) continue;
            GameObject sidewalkObj = new GameObject("Sidewalk");
            sidewalkObj.transform.SetParent(sidewalkRoot.transform, false);
            sidewalkObj.AddComponent<MeshFilter>().sharedMesh = sidewalkMesh;
            sidewalkObj.AddComponent<MeshRenderer>().sharedMaterial = paramsSource.sidewalkMaterial;
        }
    }

    // ==========================================
    // 多边形拉伸为带顶底的实体网格
    // ==========================================
    private Mesh ExtrudePolygon(List<Vector3> baseVerts, float height)
    {
        if (baseVerts.Count < 3) return null;
        // 确保顶点顺序为顺时针（俯视），构建时法线朝外
        List<Vector3> bottom = new List<Vector3>(baseVerts);
        List<Vector3> top = new List<Vector3>();
        foreach (var v in bottom)
            top.Add(v + Vector3.up * height);

        Mesh mesh = new Mesh();
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        // 底面（反三角）
        int baseIdx = verts.Count;
        for (int i = 0; i < bottom.Count; i++) verts.Add(bottom[i]);
        for (int i = 2; i < bottom.Count; i++)
        {
            tris.Add(baseIdx + 0);
            tris.Add(baseIdx + i);
            tris.Add(baseIdx + i - 1);
        }

        // 顶面
        int topIdx = verts.Count;
        for (int i = 0; i < top.Count; i++) verts.Add(top[i]);
        for (int i = 2; i < top.Count; i++)
        {
            tris.Add(topIdx + 0);
            tris.Add(topIdx + i - 1);
            tris.Add(topIdx + i);
        }

        // 侧面
        int sideStart = verts.Count;
        for (int i = 0; i < bottom.Count; i++)
        {
            int next = (i + 1) % bottom.Count;
            verts.Add(bottom[i]);
            verts.Add(bottom[next]);
            verts.Add(top[i]);
            verts.Add(top[next]);

            int a = sideStart + i * 4;
            tris.Add(a + 0);
            tris.Add(a + 1);
            tris.Add(a + 2);
            tris.Add(a + 2);
            tris.Add(a + 1);
            tris.Add(a + 3);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ==========================================
    // 辅助：获取路网包围盒
    // ==========================================
    private Bounds GetNetworkBounds()
    {
        if (paramsSource.RoadGen.nodes == null || paramsSource.RoadGen.nodes.Count == 0)
            return new Bounds(Vector3.zero, Vector3.one * 100);
        Vector3 min = paramsSource.RoadGen.nodes[0].position;
        Vector3 max = paramsSource.RoadGen.nodes[0].position;
        foreach (var n in paramsSource.RoadGen.nodes)
        {
            min = Vector3.Min(min, n.position);
            max = Vector3.Max(max, n.position);
        }
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    // 简化道路轮廓（同原 ProceduralRoadBuilder 中的 CleanRoadUnion）
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
    private class RoadSegmentCache
    {
        public List<(Vector2 a, Vector2 b)> segments = new List<(Vector2, Vector2)>();

        public RoadSegmentCache(Path64 path)
        {
            var pts = path.Select(pt => new Vector2((float)(pt.X / 1000.0), (float)(pt.Y / 1000.0))).ToList();
            for (int i = 0; i < pts.Count; i++)
            {
                int j = (i + 1) % pts.Count;
                segments.Add((pts[i], pts[j]));
            }
        }
    }
}