using System.Collections.Generic;
using UnityEngine;
using Clipper2Lib;
using System.Linq;
using System;

public static class RoadBooleanUtility
{
    private const double SCALE = 1000.0;
    private const double INV_SCALE = 1.0 / SCALE;

    /// <summary>
    /// 将 XZ 平面中心线扩展为带宽度的封闭多边形
    /// </summary>
    public static List<Vector3> ExpandCenterlineToPolygon(List<Vector3> centerLine, float roadWidth)
    {
        if (centerLine == null || centerLine.Count < 2)
            return new List<Vector3>();

        Path64 path = new Path64();
        foreach (var pt in centerLine)
            path.Add(new Point64((long)(pt.x * SCALE), (long)(pt.z * SCALE)));

        ClipperOffset co = new ClipperOffset();
        co.AddPath(path, JoinType.Miter, EndType.Square);
        Paths64 solution = new Paths64();
        co.Execute((roadWidth * 0.5) * SCALE, solution);

        List<Vector3> poly = new List<Vector3>();
        if (solution.Count > 0)
        {
            foreach (var pt in solution[0])
                poly.Add(new Vector3((float)(pt.X * INV_SCALE), 0f, (float)(pt.Y * INV_SCALE)));
        }
        return poly;
    }

    /// <summary>
    /// 合并多个多边形，返回 Vector3 轮廓列表
    /// </summary>
    public static List<List<Vector3>> MergeRoadPolygons(List<Vector3[]> roadPolygons)
    {
        Paths64 merged = MergeRoadPolygonsToPaths64(roadPolygons);
        List<List<Vector3>> contours = new List<List<Vector3>>();
        foreach (var path in merged)
        {
            List<Vector3> contour = new List<Vector3>();
            foreach (var pt in path)
                contour.Add(new Vector3((float)(pt.X * INV_SCALE), 0f, (float)(pt.Y * INV_SCALE)));
            contours.Add(contour);
        }
        return contours;
    }

    /// <summary>
    /// 合并多边形，返回 Clipper2 Paths64
    /// </summary>
    public static Paths64 MergeRoadPolygonsToPaths64(List<Vector3[]> roadPolygons)
    {
        Paths64 subjects = new Paths64();
        foreach (var road in roadPolygons)
        {
            if (road.Length < 3) continue;
            Path64 path = new Path64();
            foreach (var pt in road)
                path.Add(new Point64((long)(pt.x * SCALE), (long)(pt.z * SCALE)));
            subjects.Add(path);
        }
        return Clipper.Union(subjects, FillRule.NonZero);
    }

    public static Path64 GenerateCirclePolygon(Vector3 center, float radius)
    {
        Path64 pointPath = new Path64 { new Point64((long)(center.x * SCALE), (long)(center.z * SCALE)) };
        ClipperOffset co = new ClipperOffset();
        co.AddPath(pointPath, JoinType.Round, EndType.Round);
        Paths64 result = new Paths64();
        co.Execute(radius * SCALE, result);
        return result.Count > 0 ? result[0] : new Path64();
    }

    public static Paths64 Difference(Paths64 subjects, Paths64 clips)
        => Clipper.Difference(subjects, clips, FillRule.NonZero);

    public static Paths64 Intersect(Paths64 subjects, Paths64 clips)
        => Clipper.Intersect(subjects, clips, FillRule.NonZero);

    /// <summary>
    /// 生成平滑路口多边形
    /// </summary>
    public static Path64 GenerateSmoothIntersectionPolygon(
        Vector3 nodePos,
        List<(Vector3 nodeA, Vector3 nodeB)> connectedEdges,
        float roadWidth,
        float expansionOffset = 1.5f)
    {
        if (connectedEdges != null)
        {
            connectedEdges.RemoveAll(edge => Vector3.Distance(edge.nodeA, edge.nodeB) < 0.1f);
        }

        if (connectedEdges == null || connectedEdges.Count < 2)
        {
            return GenerateCirclePolygon(nodePos, roadWidth * 0.75f);
        }

        Paths64 edgePatches = new Paths64();
        float patchLength = roadWidth * 0.8f;
        foreach (var edge in connectedEdges)
        {
            Vector3 dir = (edge.nodeB - edge.nodeA).normalized;
            float distance = Vector3.Distance(edge.nodeA, edge.nodeB);
            Vector3 from = edge.nodeA;
            Vector3 to = edge.nodeA + dir * Mathf.Min(distance * 0.5f, patchLength);
            Path64 line = new Path64
            {
                new Point64((long)(from.x * SCALE), (long)(from.z * SCALE)),
                new Point64((long)(to.x * SCALE), (long)(to.z * SCALE))
            };
            ClipperOffset co = new ClipperOffset();
            co.AddPath(line, JoinType.Miter, EndType.Square);
            Paths64 patch = new Paths64();
            co.Execute((roadWidth * 0.5) * SCALE, patch);
            if (patch.Count > 0)
                edgePatches.Add(patch[0]);
        }

        Paths64 mergedPatches = Clipper.Union(edgePatches, FillRule.NonZero);
        if (mergedPatches.Count == 0)
            return GenerateCirclePolygon(nodePos, roadWidth * 0.75f);

        ClipperOffset rounder = new ClipperOffset();
        rounder.AddPaths(mergedPatches, JoinType.Round, EndType.Polygon);
        Paths64 result = new Paths64();
        rounder.Execute(expansionOffset * SCALE, result);
        return result.Count > 0 ? result[0] : GenerateCirclePolygon(nodePos, roadWidth * 0.75f);
    }

    // 【修复 2 + 3】完全替换的 SanitizePolygons
   public static Paths64 SanitizePolygons(Paths64 rawPaths)
    {
        Paths64 clean = new Paths64();
        foreach (var path in rawPaths)
        {
            // 【终极修复】将阈值从 1.0 降至 0.2 平方米，防止正常的细窄路段被误删断裂
            if (Math.Abs(Clipper.Area(path)) < 0.2 * 1000 * 1000) continue;
            
            // 简化共线点，容差 2.0
            var simplified = Clipper.SimplifyPath(path, 2.0); 

            // 形态学开闭运算：先负膨胀再正膨胀，自动消除微小尖刺、自交点和极小锐角
            Paths64 tempPaths = new Paths64 { simplified };
            tempPaths = Clipper.InflatePaths(tempPaths, -0.5 * 1000.0, JoinType.Round, EndType.Polygon);
            tempPaths = Clipper.InflatePaths(tempPaths, 0.5 * 1000.0, JoinType.Round, EndType.Polygon);

            foreach (var finalPath in tempPaths)
            {
                if (finalPath.Count >= 3)
                {
                    clean.Add(finalPath);
                }
            }
        }
        return clean;
    }
    /// <summary>
    /// 生成道路轮廓向外膨胀的“裙边”外轮廓
    /// </summary>
    public static Path64 GenerateOuterSkirt(Paths64 roadUnion, float offsetMetres)
    {
        ClipperOffset co = new ClipperOffset();
        foreach (var path in roadUnion)
            co.AddPath(path, JoinType.Round, EndType.Polygon);

        Paths64 result = new Paths64();
        co.Execute(offsetMetres * SCALE, result);
        if (result.Count == 0) return new Path64();

        Path64 best = result[0];
        double maxArea = Clipper.Area(best);
        for (int i = 1; i < result.Count; i++)
        {
            double area = Clipper.Area(result[i]);
            if (area > maxArea) { best = result[i]; maxArea = area; }
        }
        return Clipper.SimplifyPath(best, 2.0);
    }

    /// <summary>
    /// 批量点包含检测，内部使用 AABB 加速
    /// </summary>
    public static bool IsAnyPointInsidePolygons(IEnumerable<Point64> pts, List<RoadContourCache> caches)
    {
        foreach (var pt in pts)
        {
            float x = (float)(pt.X * INV_SCALE);
            float y = (float)(pt.Y * INV_SCALE);
            foreach (var cache in caches)
            {
                if (!cache.aabb.Contains(new Vector2(x, y))) continue;
                if (Clipper.PointInPolygon(pt, cache.path) != PointInPolygonResult.IsOutside)
                    return true;
            }
        }
        return false;
    }

    public class RoadContourCache
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