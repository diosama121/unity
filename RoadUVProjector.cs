using System.Collections.Generic;
using UnityEngine;

public static class RoadUVProjector
{
    public struct TriangleRegionInfo
    {
        public int subMeshIndex;
        public bool isJunction;
        public int edgeIndex;
        public int junctionIndex;
    }

    public class EdgeRoadInfo
    {
        public Vector3[] poly;
        public Vector3 start;
        public Vector3 end;
        public Vector3 forward;
        public Vector3 right;
        public Vector3 origin;
        public int targetSubMesh;
    }

    public class JunctionInfo
    {
        public Vector3[] poly;
        public int degree;
        public Vector3 center;
        public float mainAngle;
    }

    public static Vector2 EdgeLocalUV(Vector3 worldPos, Vector3 origin, Vector3 forward, Vector3 right, float uvScale)
    {
        Vector3 delta = worldPos - origin;
        float u = Vector3.Dot(delta, right) * uvScale;
        float v = Vector3.Dot(delta, forward) * uvScale;
        return new Vector2(u, v);
    }

    public static Vector2 JunctionCenteredUV(Vector3 worldPos, Vector3 center, float mainAngle, float roadWidth, float uvScale)
    {
        Vector3 local = worldPos - center;
        Vector2 flat = new Vector2(local.x, local.z);
        Vector2 rotated = GeometryUtility.RotatePoint(flat, -mainAngle);
        float u = (rotated.x / roadWidth) * uvScale;
        float v = (rotated.y / roadWidth) * uvScale;
        return new Vector2(u, v);
    }

    public static TriangleRegionInfo ClassifyTriangle(Vector3 center,
                                                      List<EdgeRoadInfo> edges,
                                                      List<JunctionInfo> junctions)
    {
        for (int j = 0; j < junctions.Count; j++)
        {
            if (GeometryUtility.PointInPolygonXZ(center, junctions[j].poly))
            {
                int sub = junctions[j].degree switch
                {
                    3 => 3,
                    4 => 4,
                    _ => 5
                };
                return new TriangleRegionInfo
                {
                    subMeshIndex = sub,
                    isJunction = true,
                    junctionIndex = j
                };
            }
        }

        for (int e = 0; e < edges.Count; e++)
        {
            if (GeometryUtility.PointInPolygonXZ(center, edges[e].poly))
            {
                return new TriangleRegionInfo
                {
                    subMeshIndex = edges[e].targetSubMesh,
                    isJunction = false,
                    edgeIndex = e
                };
            }
        }

        float minDist = float.MaxValue;
        int bestEdge = 0;
        bool isJunction = false;
        int bestJunction = -1;

        for (int j = 0; j < junctions.Count; j++)
        {
            float d = Vector3.Distance(center, junctions[j].center);
            if (d < minDist)
            {
                minDist = d;
                bestJunction = j;
                isJunction = true;
            }
        }

        for (int e = 0; e < edges.Count; e++)
        {
            float d = PointToPolygonDistXZ(center, edges[e].poly);
            if (d < minDist)
            {
                minDist = d;
                bestEdge = e;
                isJunction = false;
            }
        }

        if (isJunction)
        {
            int sub = junctions[bestJunction].degree switch
            {
                3 => 3,
                4 => 4,
                _ => 5
            };
            return new TriangleRegionInfo
            {
                subMeshIndex = sub,
                isJunction = true,
                junctionIndex = bestJunction
            };
        }
        else
        {
            return new TriangleRegionInfo
            {
                subMeshIndex = edges[bestEdge].targetSubMesh,
                isJunction = false,
                edgeIndex = bestEdge
            };
        }
    }

    private static float PointToPolygonDistXZ(Vector3 point, Vector3[] polygon)
    {
        float min = float.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % polygon.Length];
            float d = GeometryUtility.PointToLineDistanceSqr(point, a, b);
            if (d < min) min = d;
        }
        return Mathf.Sqrt(min);
    }

    public static float ComputeJunctionMainAngle(Vector3 nodePos, List<Vector3> neighborPositions)
    {
        if (neighborPositions == null || neighborPositions.Count == 0)
            return 0f;

        List<Vector3> sortedNeighbors = GeometryUtility.SortPointsByPolarAngle(neighborPositions, nodePos);

        float sumSin = 0f, sumCos = 0f;
        foreach (var nbPos in sortedNeighbors)
        {
            Vector3 dir = (nbPos - nodePos).normalized;
            float angle = Mathf.Atan2(dir.x, dir.z);
            sumSin += Mathf.Sin(angle);
            sumCos += Mathf.Cos(angle);
        }

        return Mathf.Atan2(sumSin, sumCos);
    }
}