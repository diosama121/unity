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
    /// 将 XZ 平面中心线扩展为带宽度的封闭多边形（Vector3 列表）
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
    /// 合并多个多边形，返回 Vector3 轮廓列表（用于调试或旧版）
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
    /// 合并多边形，直接返回 Clipper2 的 Paths64（推荐给 Triangle.NET 使用）
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

    /// <summary>
    /// 差集：subjects 减去 clips
    /// </summary>
    public static Paths64 Difference(Paths64 subjects, Paths64 clips)
        => Clipper.Difference(subjects, clips, FillRule.NonZero);

    /// <summary>
    /// 交集：subjects 与 clips 的相交部分
    /// </summary>
    public static Paths64 Intersect(Paths64 subjects, Paths64 clips)
        => Clipper.Intersect(subjects, clips, FillRule.NonZero);

    /// <summary>
    /// 生成平滑路口多边形（带数据清洗与兜底）
    /// </summary>
    public static Path64 GenerateSmoothIntersectionPolygon(
        Vector3 nodePos,
        List<(Vector3 nodeA, Vector3 nodeB)> connectedEdges,
        float roadWidth,
        float expansionOffset = 1.5f)
    {
        // ==========================================
        // 【a1 数据清洗防线】拦截导致 Clipper2 崩溃的畸形边
        // ==========================================
        if (connectedEdges != null)
        {
            // 1. 剔除幽灵边（长度极短，会导致数学除零或无穷小）
            connectedEdges.RemoveAll(edge => Vector3.Distance(edge.nodeA, edge.nodeB) < 0.1f);
        }

        // 2. 如果清洗后有效的连接边少于2条，意味着这不是一个正常路口
        // 拒绝走复杂的倒角逻辑，直接回退为简单的圆盘补丁，保命要紧！
        if (connectedEdges == null || connectedEdges.Count < 2)
        {
            return GenerateCirclePolygon(nodePos, roadWidth * 0.75f);
        }

        // 3. 对每条边生成一个矩形补丁（基于道路宽度膨胀线段）
        Paths64 edgePatches = new Paths64();
        float patchLength = roadWidth * 0.8f; // 仅用作旧逻辑保留
        foreach (var edge in connectedEdges)
        {
            Vector3 dir = (edge.nodeB - edge.nodeA).normalized;
            float distance = Vector3.Distance(edge.nodeA, edge.nodeB);
            // 使用从节点中心向邻居方向延伸一小段，避免补丁过短
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

        // 4. 合并所有边的补丁
        Paths64 mergedPatches = Clipper.Union(edgePatches, FillRule.NonZero);
        if (mergedPatches.Count == 0)
            return GenerateCirclePolygon(nodePos, roadWidth * 0.75f);

        // 5. 对合并后的多边形进行倒角（圆滑处理）
        ClipperOffset rounder = new ClipperOffset();
        // 使用 Miter 合并可能导致尖角，可改用 Round
        rounder.AddPaths(mergedPatches, JoinType.Round, EndType.Polygon);
        Paths64 result = new Paths64();
        // 微膨胀以产生圆角，expansionOffset 通常 1.5f
        rounder.Execute(expansionOffset * SCALE, result);
        return result.Count > 0 ? result[0] : GenerateCirclePolygon(nodePos, roadWidth * 0.75f);
    }
}