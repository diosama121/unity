using UnityEngine;
using System.Collections.Generic;

public static class RoadMeshUtility
{
    /// <summary>
    /// 直路四边形带：lefts 和 rights 按顺序一一对应，生成三角形带
    /// 确保法线朝上（右手定则）
    /// </summary>
    public static Mesh BuildQuadStrip(List<Vector3> lefts, List<Vector3> rights)
    {
        if (lefts == null || rights == null || lefts.Count != rights.Count || lefts.Count < 2)
            return null;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        for (int i = 0; i < lefts.Count; i++)
        {
            verts.Add(lefts[i]);   // 偶数索引：Left
            verts.Add(rights[i]);  // 奇数索引：Right
        }

        for (int i = 0; i < lefts.Count - 1; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = i0 + 2;
            int i3 = i1 + 2;

            // 三角形1：左[i] - 右[i] - 左[i+1]
            tris.Add(i0); tris.Add(i1); tris.Add(i2);
            // 三角形2：右[i] - 右[i+1] - 左[i+1]
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
    /// 路口扇形：center 为圆心，ring 为已排序的轮廓顶点（顺时针或逆时针均可）
    /// 生成的三角形法线朝上
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
            // 三角形：center (0) -> ring[i] -> ring[next]
            // 若 ring 为顺时针，法线会朝上，若为逆时针则需调整，这里假设顺时针
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
            // 翻转三角形
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
    public static Mesh BuildRoadMesh(List<Vector3[]> allQuads)
{
    if (allQuads == null || allQuads.Count == 0) return null;

    List<Vector3> verts = new List<Vector3>();
    List<int> tris = new List<int>();

    foreach (var quad in allQuads)
    {
        // quad 顺序：左前, 右前, 右后, 左后
        if (quad.Length != 4) continue;
        int startIdx = verts.Count;
        verts.AddRange(quad);

        // 两个三角形：0-1-2, 0-2-3
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
    return mesh;
}
}