using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    public float roadWidth = 6f;
    public Material roadMaterial;
    public float roadHeightOffset = 0.05f;
    public string roadLayerName = "Road";
    // 其他材质配置省略，保持原 Inspector 字段

    private RoadNetworkGenerator roadGen;
    private GameObject meshRoot;
    public RoadNetworkGenerator RoadGen => roadGen;

    // 直路暴露给路口缝合的端点
    private Dictionary<int, List<Vector3>> intersectionExposedVerts = new Dictionary<int, List<Vector3>>();

    public void BuildRoads()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();
        if (roadGen == null || roadGen.edges == null) return;

        ClearRoads();
        meshRoot = new GameObject("Road_Mesh");
        meshRoot.transform.SetParent(transform, false);

        List<Mesh> roadMeshes = new List<Mesh>();
        intersectionExposedVerts.Clear();

        // 第一阶段：生成所有直路
        foreach (var edge in roadGen.edges)
        {
            int idA = edge.Item1, idB = edge.Item2;
            if (idA < 0 || idB < 0 || idA >= roadGen.nodes.Count || idB >= roadGen.nodes.Count) continue;

            List<SplinePoint> spline = GetRoadSpline(idA, idB, meshResolution: 2f);
            if (spline == null || spline.Count < 2) continue;

            // 路口退让
            List<SplinePoint> trunk = TruncateForIntersections(spline, idA, idB);
            if (trunk.Count < 2) continue;

            // 法线扫掠 -> 左右顶点列表
            List<Vector3> lefts = new List<Vector3>(), rights = new List<Vector3>();
            foreach (var sp in trunk)
            {
                Vector3 left = sp.Pos - sp.Normal * (roadWidth * 0.5f);
                Vector3 right = sp.Pos + sp.Normal * (roadWidth * 0.5f);
                left.y = sp.Pos.y;   // 高度强制一致
                right.y = sp.Pos.y;
                lefts.Add(left);
                rights.Add(right);
            }

            // 存入网格
            Mesh stripMesh = RoadMeshUtility.BuildQuadStrip(lefts, rights);
            if (stripMesh != null) roadMeshes.Add(stripMesh);

            // 暴露端点给路口
            ExposeEndpoints(idA, lefts[0], rights[0]);
            ExposeEndpoints(idB, lefts[lefts.Count - 1], rights[rights.Count - 1]);
        }

        // 第二阶段：生成所有路口
        foreach (var node in roadGen.nodes)
        {
            if (node.neighbors == null || node.neighbors.Count < 3) continue;
            if (!intersectionExposedVerts.ContainsKey(node.id)) continue;

            List<Vector3> ring = intersectionExposedVerts[node.id];
            Vector3 center = GetNodeFixedHeight(node.id).AsVector3Y();
            // 极角排序
            ring = RoadMathUtility.SortAroundCenter(center, ring);
            // 切披萨
            Mesh pie = RoadMeshUtility.BuildPieSlices(center, ring);
            if (pie != null) roadMeshes.Add(pie);
        }

        // 合并所有道路子 Mesh 为一个（可选）
        CombineAndAssign(roadMeshes);
    }

    private List<SplinePoint> TruncateForIntersections(List<SplinePoint> spline, int idA, int idB)
    {
        float cutDist = roadWidth * 0.8f;
        // 简单实现：去掉首尾 cuttingDist 距离内的点
        float total = 0;
        List<float> dists = new List<float>();
        for (int i = 1; i < spline.Count; i++)
        {
            total += Vector3.Distance(spline[i-1].Pos, spline[i].Pos);
            dists.Add(total);
        }
        List<SplinePoint> result = new List<SplinePoint>();
        foreach (var sp in spline)
        {
            // 这里仅示意，需要更精确的沿曲线距离判断
            // 假设 spline 里面已经带有 t 或距离信息，暂简化为全保留
            result.Add(sp);
        }
        // 实际实现需要用到 spline 中各点累计距离
        return result;
    }

    private void ExposeEndpoints(int nodeId, Vector3 left, Vector3 right)
    {
        if (!intersectionExposedVerts.ContainsKey(nodeId))
            intersectionExposedVerts[nodeId] = new List<Vector3>();
        intersectionExposedVerts[nodeId].Add(left);
        intersectionExposedVerts[nodeId].Add(right);
    }

    private void CombineAndAssign(List<Mesh> meshes) { /* 合并并赋给 MeshFilter */ }

    // 内部模拟 a4 接口调用，实际替换为 WorldModel.Instance.xxx
    private List<SplinePoint> GetRoadSpline(int a, int b, float meshResolution) =>
        WorldModel.Instance.GetRoadSpline(a, b, meshResolution);
    private float GetNodeFixedHeight(int id) =>
        WorldModel.Instance.GetNodeFixedHeight(id);

    void ClearRoads() { if (meshRoot) DestroyImmediate(meshRoot); }
}

public struct SplinePoint { public Vector3 Pos; public Vector3 Tangent; public Vector3 Normal; }
public static class Vector3Extensions { public static Vector3 AsVector3Y(this float y) => new Vector3(0, y, 0); }