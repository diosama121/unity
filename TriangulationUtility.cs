// ========== 新建文件 TriangulationUtility.cs ==========
// 彻底封装 Triangle.NET，输出纯 2D 网格数据

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Clipper2Lib;
using TriangleNet.Geometry;
using TriangleNet.Meshing;
using TriangleNet.Topology;

public static class TriangulationUtility
{
    public struct RawMeshData
    {
        public List<Vector2> vertices;      // XZ 平面坐标
        public List<int> triangles;
    }

    /// <summary>
    /// 执行约束 Delaunay 三角剖分，并剔除越界三角形（四点采样法）。
    /// </summary>
    /// <param name="outerBoundary">外轮廓（裙边），世界单位</param>
    /// <param name="holePaths">内部道路空洞轮廓集合，世界单位</param>
    /// <returns>二维网格数据（仅 XZ，不含 Y）</returns>
    public static RawMeshData GenerateCDTMeshData(Path64 outerBoundary, Paths64 holePaths)
    {
        // 构建约束多边形
        var poly = new Polygon();

        // 外部边界
        var outerVerts = outerBoundary.Select(pt => new Vertex(pt.X / 1000.0, pt.Y / 1000.0)).ToList();
        poly.Add(new Contour(outerVerts));

        // 内部空洞
        foreach (var hole in holePaths)
        {
            if (hole.Count < 3) continue;
            var holeVerts = hole.Select(pt => new Vertex(pt.X / 1000.0, pt.Y / 1000.0)).ToList();
            poly.Add(new Contour(holeVerts));
        }

        // CDT 三角化（关闭质量优化，避免崩溃）
        var meshParams = new ConstraintOptions { ConformingDelaunay = true };
        var mesh = poly.Triangulate(meshParams);

        // 准备空间索引用于过滤
        var caches = holePaths.Select(p => new RoadContourCache(p)).ToList();

        // 收集合法三角形
        var vertices = new List<Vector2>();
        var triangles = new List<int>();
        var indexMap = new Dictionary<int, int>();

        foreach (var tri in mesh.Triangles)
        {
            // 四点采样过滤
            if (IsTriangleInsideRoad(tri, caches)) continue;

            for (int i = 2; i >= 0; i--) // 统一 winding
            {
                var v = tri.GetVertex(i);
                if (!indexMap.TryGetValue(v.ID, out int newIdx))
                {
                    newIdx = vertices.Count;
                    indexMap[v.ID] = newIdx;
                    vertices.Add(new Vector2((float)v.X, (float)v.Y));
                }
                triangles.Add(newIdx);
            }
        }

        return new RawMeshData { vertices = vertices, triangles = triangles };
    }

    // 四点采样判断三角形是否侵入道路空洞
    private static bool IsTriangleInsideRoad(Triangle tri, List<RoadContourCache> caches)
    {
        var v0 = tri.GetVertex(0);
        var v1 = tri.GetVertex(1);
        var v2 = tri.GetVertex(2);

        Point64[] samples = new Point64[4];
        samples[0] = new Point64((v0.X + v1.X + v2.X) / 3.0 * 1000.0, (v0.Y + v1.Y + v2.Y) / 3.0 * 1000.0);
        samples[1] = new Point64((v0.X + v1.X) / 2.0 * 1000.0, (v0.Y + v1.Y) / 2.0 * 1000.0);
        samples[2] = new Point64((v1.X + v2.X) / 2.0 * 1000.0, (v1.Y + v2.Y) / 2.0 * 1000.0);
        samples[3] = new Point64((v2.X + v0.X) / 2.0 * 1000.0, (v2.Y + v0.Y) / 2.0 * 1000.0);

        foreach (var cache in caches)
        {
            int insideCount = 0;
            foreach (var pt in samples)
            {
                float x = (float)(pt.X / 1000.0);
                float y = (float)(pt.Y / 1000.0);
                if (!cache.aabb.Contains(new Vector2(x, y))) continue;
                if (Clipper.PointInPolygon(pt, cache.path) != PointInPolygonResult.IsOutside)
                    insideCount++;
            }
            if (insideCount >= 2) return true;
        }
        return false;
    }

    // 复用 RoadBooleanUtility.RoadContourCache，也可在此定义
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
}