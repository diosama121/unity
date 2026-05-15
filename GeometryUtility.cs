using UnityEngine;
using System.Collections.Generic;

public static class GeometryUtility
{
    public static bool LineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        Vector2 r = p2 - p1;
        Vector2 s = q2 - q1;
        float rxs = Cross(r, s);
        float qpxr = Cross(q1 - p1, r);

        if (Mathf.Abs(rxs) < 0.0001f)
            return false;

        float t = Cross(q1 - p1, s) / rxs;
        float u = qpxr / rxs;
        return t > 0.001f && t < 0.999f && u > 0.001f && u < 0.999f;
    }

    public static bool LineSegmentsIntersect(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
    {
        return LineSegmentsIntersect(new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z),
                                     new Vector2(p3.x, p3.z), new Vector2(p4.x, p4.z));
    }

    public static Vector2 LineLineIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float denom = (p4.y - p3.y) * (p2.x - p1.x) - (p4.x - p3.x) * (p2.y - p1.y);
        if (Mathf.Abs(denom) < 0.0001f)
            return Vector2.zero;

        float ua = ((p4.x - p3.x) * (p1.y - p3.y) - (p4.y - p3.y) * (p1.x - p3.x)) / denom;
        return new Vector2(p1.x + ua * (p2.x - p1.x), p1.y + ua * (p2.y - p1.y));
    }

    public static float PointToLineDistanceSqr(Vector2 p, Vector2 a, Vector2 b)
    {
        float abX = b.x - a.x, abY = b.y - a.y;
        float apX = p.x - a.x, apY = p.y - a.y;
        float abLenSq = abX * abX + abY * abY;

        if (abLenSq < 0.0001f)
            return apX * apX + apY * apY;

        float t = Mathf.Clamp01((apX * abX + apY * abY) / abLenSq);
        float projX = a.x + t * abX;
        float projY = a.y + t * abY;
        float dx = p.x - projX;
        float dy = p.y - projY;
        return dx * dx + dy * dy;
    }

    public static float PointToLineDistanceSqr(Vector3 p, Vector3 a, Vector3 b)
    {
        return PointToLineDistanceSqr(new Vector2(p.x, p.z), new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    public static bool PointInPolygonXZ(Vector3 point, Vector3[] polygon)
    {
        int n = polygon.Length;
        bool inside = false;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = polygon[i].x, zi = polygon[i].z;
            float xj = polygon[j].x, zj = polygon[j].z;

            if ((zi > point.z) != (zj > point.z) &&
                point.x < (xj - xi) * (point.z - zi) / (zj - zi) + xi)
                inside = !inside;
        }

        return inside;
    }

    public static bool PointInPolygon(Vector2 point, Vector2[] polygon)
    {
        int n = polygon.Length;
        bool inside = false;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = polygon[i].x, yi = polygon[i].y;
            float xj = polygon[j].x, yj = polygon[j].y;

            if ((yi > point.y) != (yj > point.y) &&
                point.x < (xj - xi) * (point.y - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }

    public static float PolygonAreaXZ(Vector3[] polygon)
    {
        float area = 0;
        int n = polygon.Length;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += polygon[i].x * polygon[j].z;
            area -= polygon[j].x * polygon[i].z;
        }

        return Mathf.Abs(area * 0.5f);
    }

    public static Vector3 ComputePolygonCentroidXZ(Vector3[] polygon)
    {
        float area = 0;
        float cx = 0, cz = 0;
        int n = polygon.Length;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float ai = polygon[i].x * polygon[j].z - polygon[j].x * polygon[i].z;
            cx += (polygon[i].x + polygon[j].x) * ai;
            cz += (polygon[i].z + polygon[j].z) * ai;
            area += ai;
        }

        area *= 0.5f;
        float inv6Area = 1.0f / (6.0f * area);
        return new Vector3(cx * inv6Area, 0, cz * inv6Area);
    }

    public static List<Vector3> SortPointsByPolarAngle(List<Vector3> points, Vector3 center)
    {
        return points.OrderBy(p => Mathf.Atan2(p.z - center.z, p.x - center.x)).ToList();
    }

    public static List<Vector2> SortPointsByPolarAngle(List<Vector2> points, Vector2 center)
    {
        return points.OrderBy(p => Mathf.Atan2(p.y - center.y, p.x - center.x)).ToList();
    }

    public static Vector2 RotatePoint(Vector2 point, float angleRad)
    {
        float c = Mathf.Cos(angleRad);
        float s = Mathf.Sin(angleRad);
        return new Vector2(point.x * c - point.y * s, point.x * s + point.y * c);
    }

    public static Vector3 RotatePoint(Vector3 point, float angleRad)
    {
        float c = Mathf.Cos(angleRad);
        float s = Mathf.Sin(angleRad);
        return new Vector3(point.x * c - point.z * s, point.y, point.x * s + point.z * c);
    }

    public static Vector3 ProjectPointOnPlane(Vector3 point, Vector3 planeNormal, Vector3 planePoint)
    {
        float dist = Vector3.Dot(point - planePoint, planeNormal);
        return point - planeNormal * dist;
    }

    public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return a + ab * t;
    }

    public static bool ArePointsColinear(Vector3 a, Vector3 b, Vector3 c, float tolerance = 0.001f)
    {
        Vector3 cross = Vector3.Cross(b - a, c - a);
        return cross.sqrMagnitude < tolerance * tolerance;
    }

    public static bool ArePointsColinear(Vector2 a, Vector2 b, Vector2 c, float tolerance = 0.001f)
    {
        float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        return Mathf.Abs(cross) < tolerance;
    }

    public static Rect CalculateAABB(List<Vector3> points)
    {
        if (points == null || points.Count == 0)
            return new Rect();

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var p in points)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }

    public static List<Vector3> GenerateCirclePoints(Vector3 center, float radius, int segments)
    {
        List<Vector3> points = new List<Vector3>();
        float angleStep = 2f * Mathf.PI / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep;
            points.Add(new Vector3(center.x + Mathf.Cos(angle) * radius, center.y, center.z + Mathf.Sin(angle) * radius));
        }

        return points;
    }

    public static List<Vector3> GenerateRegularPolygon(Vector3 center, float radius, int sides)
    {
        List<Vector3> points = new List<Vector3>();
        float angleStep = 2f * Mathf.PI / sides;
        float startAngle = -Mathf.PI / 2f;

        for (int i = 0; i < sides; i++)
        {
            float angle = startAngle + i * angleStep;
            points.Add(new Vector3(center.x + Mathf.Cos(angle) * radius, center.y, center.z + Mathf.Sin(angle) * radius));
        }

        return points;
    }

    public static float AngleBetweenVectors(Vector3 a, Vector3 b)
    {
        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f)) * Mathf.Rad2Deg;
    }

    public static float AngleBetweenVectors(Vector2 a, Vector2 b)
    {
        return Mathf.Acos(Mathf.Clamp(Vector2.Dot(a.normalized, b.normalized), -1f, 1f)) * Mathf.Rad2Deg;
    }

    public static Vector3 ComputeTangentFromNeighbors(Vector3 center, List<Vector3> neighbors)
    {
        if (neighbors == null || neighbors.Count == 0)
            return Vector3.forward;

        Vector3 avgDir = Vector3.zero;
        foreach (var nb in neighbors)
        {
            avgDir += (nb - center).normalized;
        }

        avgDir.Normalize();
        return avgDir;
    }

    public static Vector3 ComputeNormalFromTangent(Vector3 tangent)
    {
        return Vector3.Cross(Vector3.up, tangent).normalized;
    }
}