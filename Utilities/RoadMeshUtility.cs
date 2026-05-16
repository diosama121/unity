using UnityEngine;
using System.Collections.Generic;

public static class RoadMeshUtility
{
    // 支持混合传入四边形和三角形
    public static Mesh BuildRoadMesh(List<Vector3[]> allPolys, List<Vector2[]> allUVs = null)
    {
        if (allPolys == null || allPolys.Count == 0) return null;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        for (int p = 0; p < allPolys.Count; p++)
        {
            var poly = allPolys[p];

            if (poly.Length == 3)
            {
                int start = verts.Count;
                verts.AddRange(poly);
                tris.Add(start + 0);
                tris.Add(start + 1);
                tris.Add(start + 2);

                uvs.Add(Vector2.zero);
                uvs.Add(Vector2.zero);
                uvs.Add(Vector2.zero);
            }
            else if (poly.Length == 4)
            {
                int start = verts.Count;
                verts.AddRange(poly);
                tris.Add(start + 0);
                tris.Add(start + 2);
                tris.Add(start + 1);

                tris.Add(start + 0);
                tris.Add(start + 3);
                tris.Add(start + 2);

                if (allUVs != null && p < allUVs.Count && allUVs[p] != null && allUVs[p].Length == 4)
                {
                    uvs.AddRange(allUVs[p]);
                }
                else
                {
                    uvs.Add(Vector2.zero);
                    uvs.Add(Vector2.zero);
                    uvs.Add(Vector2.zero);
                    uvs.Add(Vector2.zero);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
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