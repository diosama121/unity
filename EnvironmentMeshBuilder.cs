using System.Collections.Generic;
using UnityEngine;
using Clipper2Lib;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using System;

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
                    if (GeometryUtility.LineSegmentsIntersect(triEdge.Item1, triEdge.Item2, roadSeg.a, roadSeg.b))
                        return true;
                }
            }
        }
        return false;
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

    private void GenerateTerrainBase(Paths64 rawRoadUnion)
    {
        Paths64 cleanRoads = RoadBooleanUtility.SanitizePolygons(rawRoadUnion);
        if (cleanRoads.Count == 0) return;

        Path64 outerSkirt = RoadBooleanUtility.GenerateOuterSkirt(cleanRoads, 18f);
        if (outerSkirt.Count < 3) return;

        TriangulationUtility.RawMeshData meshData = TriangulationUtility.GenerateCDTMeshData(outerSkirt, cleanRoads);

        // === a1 修复：翻转三角形顺序，确保法线向上 ===
        List<int> flippedTris = new List<int>();
        for (int i = 0; i < meshData.triangles.Count; i += 3)
        {
            int a = meshData.triangles[i];
            int b = meshData.triangles[i + 1];
            int c = meshData.triangles[i + 2];
            flippedTris.Add(a);
            flippedTris.Add(c);
            flippedTris.Add(b);
        }



        List<Vector3> verts3D = new List<Vector3>();
        foreach (var v2 in meshData.vertices)
        {
            float y = WorldModel.Instance != null
                ? WorldModel.Instance.GetUnifiedHeight(v2.x, v2.y)
                : 0f;
            verts3D.Add(new Vector3(v2.x, y, v2.y));
        }

        Mesh terrainMesh = new Mesh();
        terrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        terrainMesh.SetVertices(verts3D);
        terrainMesh.SetTriangles(flippedTris, 0);
        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();
        terrainMesh.RecalculateNormals();
        Vector3[] normals = terrainMesh.normals;
        if (normals.Length > 0 && normals[0].y < 0)
        {
            // 翻转所有三角形顺序
            int[] currentTris = terrainMesh.triangles;
            for (int i = 0; i < currentTris.Length; i += 3)
            {
                int tmp = currentTris[i + 1];
                currentTris[i + 1] = currentTris[i + 2];
                currentTris[i + 2] = tmp;
            }
            terrainMesh.SetTriangles(currentTris, 0);
            terrainMesh.RecalculateNormals();
            terrainMesh.RecalculateBounds();
        }
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
    }

    private void ExtrudeBuildingsFromIslands(Paths64 roadUnion)
    {
        if (paramsSource.buildingMaterial == null) return;

        GameObject buildingRoot = new GameObject("City_Buildings");
        buildingRoot.transform.SetParent(transform, false);

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
        co.Execute(-1.5 * 1000.0, shrunkIslands);

        foreach (var safeIsland in shrunkIslands)
        {
            if (safeIsland.Count < 3) continue;
            List<Vector3> baseVerts = safeIsland.Select(pt =>
                new Vector3((float)(pt.X / 1000.0), 0f, (float)(pt.Y / 1000.0))).ToList();

            for (int i = 0; i < baseVerts.Count; i++)
            {
                var v = baseVerts[i];
                v.y = WorldModel.Instance != null
                    ? WorldModel.Instance.GetUnifiedHeight(v.x, v.z)
                    : 0f;
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
                v.y = WorldModel.Instance != null
                    ? WorldModel.Instance.GetUnifiedHeight(v.x, v.z)
                    : 0f;
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

    private Mesh ExtrudePolygon(List<Vector3> baseVerts, float height)
    {
        if (baseVerts.Count < 3) return null;
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