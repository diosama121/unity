using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using TriangleNet.Geometry;
using TriangleNet.Meshing;

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    [Header("=== 道路核心参数 ===")]
    public float roadWidth = 6f;
    public float meshResolution = 2f;
    public Material roadMaterial;
    public float roadHeightOffset = 0.15f;
    public string roadLayerName = "Road";

    [Header("=== 样条切线参数 ===")]
    [Range(0f, 1f)] public float tangentLength = 0.3f;
    public float maxTangentLength = 2.0f;

    [Header("=== UV Scale ===")]
    public float uvScale = 0.1f;

    [Header("=== 直路材质（按方向） ===")]
    public Material horizontalRoadMaterial;
    public Material verticalRoadMaterial;
    public Material diagonalRoadMaterial;

    [Header("=== 路口材质（按形状） ===")]
    public Material tJunctionMaterial;
    public Material crossJunctionMaterial;
    public Material complexJunctionMaterial;

    [Header("=== 乡村统一覆盖 ===")]
    public bool useCountrysideUniformMaterials = true;
    public Material countrysideRoadMaterial;
    public Material countrysideJunctionMaterial;

    [Header("=== 地表生成（供外部读取） ===")]
    public bool generateTerrainBase = true;
    public Material terrainBaseMaterial;

    [Header("=== 城镇模式参数（供外部读取） ===")]
    public bool generateCity = true;
    public float buildingHeight = 10f;
    public Material buildingMaterial;
    public float sidewalkWidth = 2f;
    public Material sidewalkMaterial;
    public float sidewalkHeight = 0.2f;

    [Header("=== 调试可视化 ===")]
    public bool showSplineGizmos = false;

    private RoadNetworkGenerator roadGen;
    public RoadNetworkGenerator RoadGen => roadGen;
    private GameObject meshRoot;

    void Awake() => roadGen = GetComponent<RoadNetworkGenerator>();

    public void BuildRoads()
    {
        if (WorldModel.Instance == null || WorldModel.Instance.Nodes == null) return;

        float stepDist = meshResolution;
        if (roadGen != null && roadGen.isCountryside)
            stepDist = 0.5f;

        foreach (var node in WorldModel.Instance.Nodes)
        {
            Vector3 pos = node.WorldPos; 
            pos.y = WorldModel.Instance.GetUnifiedHeight(pos.x, pos.z);
            node.WorldPos = pos; 
        }

        ClearRoads();
        meshRoot = new GameObject("Road_Mesh_Root");
        meshRoot.layer = LayerMask.NameToLayer(roadLayerName) < 0 ? 0 : LayerMask.NameToLayer(roadLayerName);
        meshRoot.transform.SetParent(transform, false);

        foreach (var node in WorldModel.Instance.Nodes)
        {
            if (node.NeighborIds != null && node.NeighborIds.Count > 2)
                node.IntersectionRadius = CalculateIntersectionRadius(node, roadWidth);
        }

        var allPolys = new List<Vector3[]>();
        var allUVs = new List<Vector2[]>();
        HashSet<string> processedEdges = new HashSet<string>();

        Dictionary<int, List<JunctionEdgeEntry>> junctionEntriesByNode = new Dictionary<int, List<JunctionEdgeEntry>>();

        foreach (var node in WorldModel.Instance.Nodes)
        {
            if (node.NeighborIds == null) continue;

            foreach (int neighborId in node.NeighborIds)
            { 
                string edgeKey = Mathf.Min(node.Id, neighborId) + "_" + Mathf.Max(node.Id, neighborId);
                if (processedEdges.Contains(edgeKey)) continue;
                processedEdges.Add(edgeKey);

                List<SplinePoint> spline;
                if (WorldModel.Instance.GlobalSplineCache.TryGetValue(edgeKey, out var cachedSpline))
                {
                    spline = cachedSpline;
                }
                else
                {
                    spline = RoadMathUtility.GetRoadSpline(node.Id, neighborId, stepDist, roadWidth);
                }
                if (spline == null || spline.Count < 2) continue;

                RoadMathUtility.SweptRoadResult swept = RoadMathUtility.SweepSplineToQuadsWithSetback(spline, node.Id, neighborId, roadWidth, uvScale);
                allPolys.AddRange(swept.Quads);
                allUVs.AddRange(swept.QuadUVs);

                if (swept.StartSetback.EdgeVertices != null && swept.StartSetback.EdgeVertices.Count >= 2)
                {
                    if (!junctionEntriesByNode.ContainsKey(node.Id))
                        junctionEntriesByNode[node.Id] = new List<JunctionEdgeEntry>();

                    Vector3 mid = (swept.StartSetback.EdgeVertices[0] + swept.StartSetback.EdgeVertices[1]) * 0.5f;
                    Vector3 outDir = mid - node.WorldPos;
                    outDir.y = 0f;
                    junctionEntriesByNode[node.Id].Add(new JunctionEdgeEntry
                    {
                        LeftPos = swept.StartSetback.EdgeVertices[0],
                        RightPos = swept.StartSetback.EdgeVertices[1],
                        OutwardDir = outDir.normalized
                    });
                }

                if (swept.EndSetback.EdgeVertices != null && swept.EndSetback.EdgeVertices.Count >= 2)
                {
                    if (!junctionEntriesByNode.ContainsKey(neighborId))
                        junctionEntriesByNode[neighborId] = new List<JunctionEdgeEntry>();

                    RoadNode nbNode = WorldModel.Instance.GetNode(neighborId);
                    Vector3 mid = (swept.EndSetback.EdgeVertices[0] + swept.EndSetback.EdgeVertices[1]) * 0.5f;
                    Vector3 outDir = mid - nbNode.WorldPos;
                    outDir.y = 0f;
                    junctionEntriesByNode[neighborId].Add(new JunctionEdgeEntry
                    {
                        LeftPos = swept.EndSetback.EdgeVertices[0],
                        RightPos = swept.EndSetback.EdgeVertices[1],
                        OutwardDir = outDir.normalized
                    });
                }
            }
        }

        int roadLayer = LayerMask.NameToLayer(roadLayerName) < 0 ? 0 : LayerMask.NameToLayer(roadLayerName);

        foreach (var kvp in junctionEntriesByNode)
        {
            RoadNode juncNode = WorldModel.Instance.GetNode(kvp.Key);
            if (juncNode == null || juncNode.NeighborIds == null || juncNode.NeighborIds.Count < 2) continue;
            IntersectionMeshData juncData = BuildPerfectIntersectionMesh(juncNode, kvp.Value);
            if (juncData.Contour != null && juncData.Contour.Length >= 3) { allPolys.Add(juncData.Contour); }
            if (juncData.RenderMesh != null)
            {
                Material junctionMat = useCountrysideUniformMaterials ? countrysideJunctionMaterial : complexJunctionMaterial;
                if (junctionMat == null) junctionMat = roadMaterial;
                CreateJunctionObject($"Junction_{kvp.Key}", juncData, junctionMat, roadLayer, meshRoot.transform);
            }
        }

        Mesh roadMesh = RoadMeshUtility.BuildRoadMesh(allPolys, allUVs);
        if (roadMesh == null) return;

        CreateRoadObject("Temp_Road_Mesh", roadMesh, BuildMaterialArray(roadMaterial), roadLayer, meshRoot.transform);
        RoadMeshCombiner.CombineRoadMeshes(meshRoot.transform);

        var terrainGrid = TerrainGridSystem.Instance;
        if (terrainGrid != null)
        {
            if (roadGen != null && !roadGen.isCountryside)
            {
                terrainGrid.BakeRoadMask(allPolys);
            }
        }
    }

    public struct IntersectionMeshData
    {
        public Mesh RenderMesh;
        public Mesh ColliderMesh;
        public Vector3[] Contour;
    }

    private struct BoundaryData
    {
        public float Y;
        public Vector3 Normal;
    }

    public IntersectionMeshData BuildPerfectIntersectionMesh(RoadNode junctionNode, List<JunctionEdgeEntry> entries)
    {
        IntersectionMeshData result = new IntersectionMeshData();
        if (entries.Count < 2) return result;

        Vector3 center = junctionNode.WorldPos;
        var sortedEntries = entries.OrderBy(e => -Mathf.Atan2(e.OutwardDir.z, e.OutwardDir.x)).ToList();

        // 预修正循环：确保 LeftPos/RightPos 方向一致，并写回列表
        for (int i = 0; i < sortedEntries.Count; i++)
        {
            var entry = sortedEntries[i];
            Vector3 edgeVec = entry.RightPos - entry.LeftPos;
            if (Vector3.Cross(entry.OutwardDir, edgeVec).y < 0)
            {
                Vector3 temp = entry.LeftPos;
                entry.LeftPos = entry.RightPos;
                entry.RightPos = temp;
                sortedEntries[i] = entry;
            }
        }

        List<Vector3> authoritativeContour = new List<Vector3>();
        Dictionary<Vector2, BoundaryData> exactBoundaryDict = new Dictionary<Vector2, BoundaryData>(new Vector2EqualityComparer());

        for (int i = 0; i < sortedEntries.Count; i++)
        {
            var current = sortedEntries[i];
            var next = sortedEntries[(i + 1) % sortedEntries.Count];

            float boundaryLen = Vector3.Distance(current.LeftPos, current.RightPos);
            int boundarySamples = Mathf.Max(1, Mathf.CeilToInt(boundaryLen / 0.5f));

            for (int s = 0; s <= boundarySamples; s++)
            {
                float t = (float)s / boundarySamples;
                Vector3 edgePt = Vector3.Lerp(current.LeftPos, current.RightPos, t);
                Vector3 edgeNormal = Vector3.up;

                Vector2 key = new Vector2(edgePt.x, edgePt.z);
                if (!exactBoundaryDict.ContainsKey(key))
                {
                    exactBoundaryDict.Add(key, new BoundaryData { Y = edgePt.y, Normal = edgeNormal });
                    authoritativeContour.Add(edgePt);
                }
            }

            float dist = Vector3.Distance(current.RightPos, next.LeftPos);
            float angle = Vector3.Angle(current.OutwardDir, next.OutwardDir);
            float radiusMult = Mathf.Min(0.35f, Mathf.Clamp01(angle / 90f));
            float r = dist * radiusMult;

            Vector3 controlPoint = (current.RightPos - current.OutwardDir * r + next.LeftPos - next.OutwardDir * r) * 0.5f;

            int cornerSamples = 6;
            for (int s = 1; s < cornerSamples; s++)
            {
                float t = (float)s / cornerSamples;
                float u = 1f - t;
                Vector3 curvePt = u * u * current.RightPos + 2f * u * t * controlPoint + t * t * next.LeftPos;
                Vector2 keyCurve = new Vector2(curvePt.x, curvePt.z);
                if (!exactBoundaryDict.ContainsKey(keyCurve))
                {
                    exactBoundaryDict.Add(keyCurve, new BoundaryData { Y = curvePt.y, Normal = Vector3.up });
                    authoritativeContour.Add(curvePt);
                }
            }
        }

        var poly = new Polygon();
        poly.Add(new Contour(authoritativeContour.Select(v => new Vertex(v.x, v.z)).ToList()));

        var renderOptions = new ConstraintOptions { ConformingDelaunay = true };
        var renderQuality = new QualityOptions { MinimumAngle = 20, MaximumArea = 1.5 };
        result.RenderMesh = ProcessMesh(poly.Triangulate(renderOptions, renderQuality), sortedEntries, exactBoundaryDict, true);

        var colliderOptions = new ConstraintOptions { ConformingDelaunay = false };
        var colliderQuality = new QualityOptions { MinimumAngle = 15 };
        result.ColliderMesh = ProcessMesh(poly.Triangulate(colliderOptions, colliderQuality), sortedEntries, exactBoundaryDict, false);

        result.Contour = authoritativeContour.ToArray();
        return result;
    }

    private Mesh ProcessMesh(TriangleNet.Meshing.IMesh triMesh, List<JunctionEdgeEntry> entries, Dictionary<Vector2, BoundaryData> exactBoundaryDict, bool isRenderMesh)
    {
        List<Vector3> finalVertices = new List<Vector3>();
        List<int> finalTriangles = new List<int>();
        Dictionary<int, int> vertexWeldMap = new Dictionary<int, int>();
        List<Vector3> finalNormals = new List<Vector3>();

        // 计算边界顶点平均高度，用于内部顶点的高程
        float boundaryAvgY = 0f;
        int boundaryCount = 0;
        foreach (var kvp in exactBoundaryDict)
        {
            boundaryAvgY += kvp.Value.Y;
            boundaryCount++;
        }
        if (boundaryCount > 0) boundaryAvgY /= boundaryCount;

        foreach (var tri in triMesh.Triangles)
        {
            int[] flipOrder = { 0, 2, 1 };
            for (int i = 0; i < 3; i++)
            {
                var v = tri.GetVertex(flipOrder[i]);
                Vector2 v2D = new Vector2((float)v.X, (float)v.Y);

                if (!vertexWeldMap.TryGetValue(v.ID, out int vertexIndex))
                {
                    float finalY = 0f;
                    Vector3 finalNormal = Vector3.up;
                    bool isBoundary = false;

                    if (exactBoundaryDict.TryGetValue(v2D, out BoundaryData bData))
                    {
                        finalY = bData.Y;
                        finalNormal = bData.Normal;
                        isBoundary = true;
                    }
                    else
                    {
                        foreach (var entry in entries)
                        {
                            Vector2 pA = new Vector2(entry.LeftPos.x, entry.LeftPos.z);
                            Vector2 pB = new Vector2(entry.RightPos.x, entry.RightPos.z);
                            float l2 = Vector2.SqrMagnitude(pA - pB);
                            if (l2 > 0.0001f)
                            {
                                float t = Mathf.Clamp01(Vector2.Dot(v2D - pA, pB - pA) / l2);
                                Vector2 proj = pA + t * (pB - pA);

                                if (Vector2.Distance(v2D, proj) < 0.01f)
                                {
                                    finalY = Mathf.Lerp(entry.LeftPos.y, entry.RightPos.y, t);
                                    isBoundary = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!isBoundary)
                        finalY = boundaryAvgY + roadHeightOffset;

                    finalVertices.Add(new Vector3(v2D.x, finalY, v2D.y));
                    finalNormals.Add(finalNormal);
                    vertexIndex = finalVertices.Count - 1;
                    vertexWeldMap.Add(v.ID, vertexIndex);
                }
                finalTriangles.Add(vertexIndex);
            }
        }

        Mesh mesh = new Mesh { indexFormat = finalVertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(finalVertices);
        mesh.SetTriangles(finalTriangles, 0);
        mesh.normals = finalNormals.ToArray();
        mesh.RecalculateNormals();

        if (isRenderMesh)
        {
            Vector3[] meshNormals = mesh.normals;
            for (int i = 0; i < finalVertices.Count; i++)
            {
                Vector2 key = new Vector2(finalVertices[i].x, finalVertices[i].z);
                if (exactBoundaryDict.ContainsKey(key))
                {
                    meshNormals[i] = finalNormals[i];
                }
            }
            mesh.normals = meshNormals;

            // 生成 UV 坐标
            Vector2[] uv = new Vector2[finalVertices.Count];
            for (int i = 0; i < finalVertices.Count; i++)
            {
                uv[i] = new Vector2(
                    finalVertices[i].x * uvScale,
                    finalVertices[i].z * uvScale
                );
            }
            mesh.uv = uv;
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    private void CreateJunctionObject(string name, IntersectionMeshData data, Material material, int layer, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.layer = layer;
        obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = data.RenderMesh;
        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MeshCollider mc = obj.AddComponent<MeshCollider>();
        mc.sharedMesh = data.ColliderMesh;
    }

    private class Vector2EqualityComparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 v1, Vector2 v2) => Vector2.SqrMagnitude(v1 - v2) < 0.0001f;
        public int GetHashCode(Vector2 v) => (Mathf.Round(v.x * 100f) + Mathf.Round(v.y * 100f)).GetHashCode();
    }

    private float CalculateIntersectionRadius(RoadNode node, float baseRoadWidth)
    {
        if (node.NeighborIds.Count <= 1) return baseRoadWidth * 0.1f;
        if (node.NeighborIds.Count == 2) return baseRoadWidth * 0.5f;
        float expansionBuffer = (node.NeighborIds.Count - 2) * 1.2f;
        return (baseRoadWidth * 0.5f) + expansionBuffer;
    }

    void ClearRoads()
    {
        if (meshRoot != null) { if (Application.isPlaying) Destroy(meshRoot); else DestroyImmediate(meshRoot); }
        foreach (Transform child in transform) {
            if (child.name.StartsWith("Combined_Road_")) { if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject); }
        }
    }

    private Material[] BuildMaterialArray(Material fallback)
    {
        Material[] mats = new Material[6];
        Material defaultMat = new Material(Shader.Find("Standard"));
        Material safeFallback = fallback ? fallback : defaultMat;
        if (useCountrysideUniformMaterials) {
            mats[0] = countrysideRoadMaterial ? countrysideRoadMaterial : defaultMat;
            mats[1] = mats[0]; mats[2] = mats[0];
            mats[3] = countrysideJunctionMaterial ? countrysideJunctionMaterial : defaultMat;
            mats[4] = mats[3]; mats[5] = mats[3];
        } else {
            mats[0] = horizontalRoadMaterial ? horizontalRoadMaterial : safeFallback;
            mats[1] = verticalRoadMaterial ? verticalRoadMaterial : safeFallback;
            mats[2] = diagonalRoadMaterial ? diagonalRoadMaterial : safeFallback;
            mats[3] = tJunctionMaterial ? tJunctionMaterial : safeFallback;
            mats[4] = crossJunctionMaterial ? crossJunctionMaterial : safeFallback;
            mats[5] = complexJunctionMaterial ? complexJunctionMaterial : safeFallback;
        }
        foreach (var mat in mats) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        return mats;
    }

    private void CreateRoadObject(string name, Mesh mesh, Material[] materials, int layer, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.layer = layer;
        obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.sharedMaterials = materials;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}