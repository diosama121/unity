using UnityEngine;
using System.Collections.Generic;

public static class RoadMeshUtility
{
    // 支持混合传入四边形和三角形
    public static Mesh BuildRoadMesh(List<Vector3[]> allPolys)
    {
        if (allPolys == null || allPolys.Count == 0) return null;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        foreach (var poly in allPolys)
        {
            if (poly.Length == 3) // 处理圆盘的三角形
            {
                int start = verts.Count;
                verts.AddRange(poly);
                tris.Add(start + 0);
                tris.Add(start + 1);
                tris.Add(start + 2);
            }
            else if (poly.Length == 4) // 处理马路的四边形
            {
                int start = verts.Count;
                verts.AddRange(poly);
                tris.Add(start + 0);
                tris.Add(start + 2);
                tris.Add(start + 1);

                tris.Add(start + 0);
                tris.Add(start + 3);
                tris.Add(start + 2);
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();

        // 法线兜底防翻转
        Vector3[] normals = mesh.normals;
        int downCount = 0;
        for (int i = 0; i < normals.Length; i++) { if (normals[i].y < 0) downCount++; }
        
        if (normals.Length > 0 && downCount > normals.Length / 2)
        {
            int[] currentTris = mesh.triangles;
            for (int i = 0; i < currentTris.Length; i += 3)
            {
                int tmp = currentTris[i + 1];
                currentTris[i + 1] = currentTris[i + 2];
                currentTris[i + 2] = tmp;
            }
            mesh.SetTriangles(currentTris, 0);
            mesh.RecalculateNormals();
        }
        return mesh;
    }
}