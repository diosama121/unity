using UnityEngine;
using System.Collections.Generic;

public static class RoadMeshUtility
{
    /// <summary>
    /// 直路四边形带
    /// </summary>
    public static Mesh BuildQuadStrip(List<Vector3> lefts, List<Vector3> rights)
    {
        if (lefts == null || rights == null || lefts.Count != rights.Count || lefts.Count < 2)
            return null;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        for (int i = 0; i < lefts.Count; i++)
        {
            verts.Add(lefts[i]);
            verts.Add(rights[i]);
        }

        for (int i = 0; i < lefts.Count - 1; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = i0 + 2;
            int i3 = i1 + 2;

            tris.Add(i0); tris.Add(i1); tris.Add(i2);
            tris.Add(i1); tris.Add(i3); tris.Add(i2);
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// 路口扇形
    /// </summary>
    public static Mesh BuildPieSlices(Vector3 center, List<Vector3> ring)
    {
        if (ring == null || ring.Count < 3) return null;

        List<Vector3> verts = new List<Vector3> { center };
        verts.AddRange(ring);

        List<int> tris = new List<int>();
        for (int i = 0; i < ring.Count; i++)
        {
            int next = (i + 1) % ring.Count;
            tris.Add(0);
            tris.Add(i + 1);
            tris.Add(next + 1);
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();

        // 如果法线朝下则翻转
        Vector3[] normals = mesh.normals;
        if (normals.Length > 0 && Vector3.Dot(normals[0], Vector3.up) < 0)
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                int temp = tris[i + 1];
                tris[i + 1] = tris[i + 2];
                tris[i + 2] = temp;
            }
            mesh.SetTriangles(tris, 0);
        }
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// 合并四边形组为单一 Mesh（含法线翻转防御）
    /// </summary>
    public static Mesh BuildRoadMesh(List<Vector3[]> allQuads)
    {
        if (allQuads == null || allQuads.Count == 0) return null;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        foreach (var quad in allQuads)
        {
            if (quad.Length != 4) continue;
            int startIdx = verts.Count;
            verts.AddRange(quad);

            // 两个三角形：左前-右前-右后, 左前-右后-左后
            tris.Add(startIdx + 0);
            tris.Add(startIdx + 2);
            tris.Add(startIdx + 1);

            tris.Add(startIdx + 0);
            tris.Add(startIdx + 3);
            tris.Add(startIdx + 2);
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();

        // 【修复 4：全局法线兜底】检查所有法线，如果大部分法线朝下，强制翻转整个网格
        Vector3[] normals = mesh.normals;
        int flippedCount = 0;
        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i].y < 0) flippedCount++;
        }

        if (normals.Length > 0 && flippedCount > normals.Length / 2) // 超过一半朝下
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