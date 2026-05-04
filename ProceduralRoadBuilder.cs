// =============================================================================
// ProceduralRoadBuilder.cs — v3.1 节点-路段分离（水平退让修复）
// 修复：路口截断退让现在使用水平方向，保证截断点高度稳定，不再出现地下网格。
// =============================================================================

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    [Header("=== 道路核心参数 ===")]
    public float roadWidth = 6f;
    public float meshResolution = 2f;
    public Material roadMaterial;
    public float roadHeightOffset = 0.05f;
    public string roadLayerName = "Road";

    [Header("=== 样条切线参数 ===")]
    [Range(0f, 1f)]
    public float tangentLength = 0.33f;
    public float maxTangentLength = 2.0f;

    [Header("=== 路口分离参数 ===")]
    [Tooltip("连接到交叉路口时，道路向后退让的距离，用于给路口多边形腾出空间")]
    public float intersectionSetback = 4.5f;      // roadWidth * 0.75

    [Header("=== 城乡材质切换 ===")]
    public RoadStyle currentStyle = RoadStyle.Auto;
    public Material cityRoadMaterial;
    public Material countryRoadMaterial;

    public enum RoadStyle
    {
        Auto,
        Urban,
        Country
    }

    [Header("=== 调试可视化 ===")]
    public bool showSplineGizmos = false;

    // 节点分类
    public enum NodeType
    {
        DeadEnd,        // neighborCount == 1
        PathNode,       // neighborCount == 2
        Intersection    // neighborCount >= 3
    }

    private RoadNetworkGenerator roadGen;
    private GameObject meshRoot;
    private float[] nodeUnifiedHeight;

    public void BuildRoads()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();
        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count == 0 ||
            roadGen.edges == null || roadGen.edges.Count == 0)
        {
            Debug.LogWarning("[ProceduralRoadBuilder] 路网数据为空，跳过生成。");
            return;
        }

        ClearRoads();
        EnsureMaterials();

        meshRoot = new GameObject("Procedural_Road_System");
        meshRoot.transform.SetParent(transform, false);

        int roadLayer = LayerMask.NameToLayer(roadLayerName);
        if (roadLayer < 0) roadLayer = 0;

        PrecomputeUnifiedHeights();
        Material actualMaterial = GetActiveMaterial();

        // 预计算所有节点的类型
        NodeType[] nodeTypes = new NodeType[roadGen.nodes.Count];
        for (int i = 0; i < roadGen.nodes.Count; i++)
        {
            int neighborCount = roadGen.nodes[i].neighbors != null ? roadGen.nodes[i].neighbors.Count : 0;
            if (neighborCount <= 1)
                nodeTypes[i] = NodeType.DeadEnd;
            else if (neighborCount == 2)
                nodeTypes[i] = NodeType.PathNode;
            else
                nodeTypes[i] = NodeType.Intersection;
        }

        HashSet<string> processedEdges = new HashSet<string>();

        foreach (var edge in roadGen.edges)
        {
            int idA = edge.Item1;
            int idB = edge.Item2;

            string key = idA < idB ? $"{idA}_{idB}" : $"{idB}_{idA}";
            if (processedEdges.Contains(key)) continue;
            processedEdges.Add(key);

            if (idA < 0 || idA >= roadGen.nodes.Count || idB < 0 || idB >= roadGen.nodes.Count) continue;

            Vector3 posA = GetUnifiedNodePosition(idA);
            Vector3 posB = GetUnifiedNodePosition(idB);

            if (Vector3.Distance(posA, posB) < 0.5f) continue;

            Spline spline = BuildEdgeSpline(idA, idB, posA, posB, nodeTypes);
            if (spline == null) continue;

            Mesh roadMesh = ExtrudeSplineToMesh(spline, roadWidth, meshResolution);
            if (roadMesh == null) continue;

            GameObject roadObj = CreateRoadObject($"Road_{idA}_{idB}", roadMesh, actualMaterial, roadLayer, meshRoot.transform);
            roadObj.transform.position = Vector3.zero;
        }

        Debug.Log($"[ProceduralRoadBuilder] ✅ v3.1 路口水平退让完成（Setback:{intersectionSetback}）");
    }

    private Material GetActiveMaterial()
    {
        RoadStyle resolvedStyle = currentStyle;
        if (currentStyle == RoadStyle.Auto && roadGen != null)
            resolvedStyle = roadGen.isCountryside ? RoadStyle.Country : RoadStyle.Urban;

        Material mat = null;
        switch (resolvedStyle)
        {
            case RoadStyle.Urban: mat = cityRoadMaterial; break;
            case RoadStyle.Country: mat = countryRoadMaterial; break;
        }

        return mat != null ? mat : roadMaterial;
    }

    private void PrecomputeUnifiedHeights()
    {
        int nodeCount = roadGen.nodes.Count;
        nodeUnifiedHeight = new float[nodeCount];

        for (int i = 0; i < nodeCount; i++)
        {
            var node = roadGen.nodes[i];
            float sumY = node.position.y;
            int count = 1;

            if (node.neighbors != null)
            {
                foreach (int nbId in node.neighbors)
                {
                    if (nbId >= 0 && nbId < nodeCount)
                    {
                        sumY += roadGen.nodes[nbId].position.y;
                        count++;
                    }
                }
            }

            float averageY = sumY / count;
            nodeUnifiedHeight[i] = Mathf.Lerp(node.position.y, averageY, 0.3f);
        }
    }

    private Vector3 GetUnifiedNodePosition(int nodeId)
    {
        Vector3 original = roadGen.nodes[nodeId].position;
        original.y = nodeUnifiedHeight[nodeId] + roadHeightOffset;
        return original;
    }

    // ------------------------------------------------------------
    // 核心修复：水平方向退让，保持高度 = 路口节点高度
    // ------------------------------------------------------------
    private Spline BuildEdgeSpline(int idA, int idB, Vector3 posA, Vector3 posB, NodeType[] nodeTypes)
    {
        Vector3 dirAB = (posB - posA).normalized;
        if (dirAB.sqrMagnitude < 0.0001f) return null;

        // 只取水平方向用于退让
        Vector3 flatDirAB = new Vector3(dirAB.x, 0f, dirAB.z).normalized;
        if (flatDirAB.sqrMagnitude < 0.0001f) flatDirAB = Vector3.forward;

        Vector3 actualStart = posA;
        Vector3 actualEnd   = posB;

        // 交叉口端点沿水平方向退让，高度保持不变
        if (nodeTypes[idA] == NodeType.Intersection)
            actualStart = posA + flatDirAB * intersectionSetback;

        if (nodeTypes[idB] == NodeType.Intersection)
            actualEnd = posB - flatDirAB * intersectionSetback;

        // 防止退让后变成长度为0或反向的道路
        Vector3 finalDir = (actualEnd - actualStart).normalized;
        if (Vector3.Dot(finalDir, flatDirAB) < 0.001f || Vector3.Distance(actualStart, actualEnd) < 0.1f)
            return null;

        // 保留Z-Fighting微偏移
        float zFightOffset = ((idA * 13 + idB * 29) % 20) * 0.002f;
        actualStart.y += zFightOffset;
        actualEnd.y   += zFightOffset;

        float segLength = Vector3.Distance(actualStart, actualEnd);
        float tLen = segLength * tangentLength;
        tLen = Mathf.Min(tLen, maxTangentLength);

        // 平直切线，沿最终道路方向（可能包含坡度）
        BezierKnot knotA = new BezierKnot(
            (float3)actualStart,
            (float3)(-finalDir * tLen),
            (float3)(finalDir * tLen),
            quaternion.LookRotation(finalDir, math.up())
        );

        BezierKnot knotB = new BezierKnot(
            (float3)actualEnd,
            (float3)(finalDir * tLen),
            (float3)(-finalDir * tLen),
            quaternion.LookRotation(-finalDir, math.up())
        );

        Spline spline = new Spline();
        spline.Add(knotA, TangentMode.Broken);
        spline.Add(knotB, TangentMode.Broken);
        spline.Closed = false;

        return spline;
    }

    // ------------------------------------------------------------
    // 网格挤出（无变化）
    // ------------------------------------------------------------
    private Mesh ExtrudeSplineToMesh(Spline spline, float width, float resolution)
    {
        if (spline == null) return null;

        float splineLength = spline.GetLength();
        if (splineLength < 0.01f) return null;

        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(splineLength / resolution) + 1);
        float halfWidth = width * 0.5f;

        Vector3[] vertices = new Vector3[sampleCount * 2];
        Vector2[] uvs = new Vector2[sampleCount * 2];

        float accumulatedLength = 0f;
        Vector3 prevCenter = Vector3.zero;
        Vector3 prevRight = Vector3.zero;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (sampleCount - 1);
            Vector3 center = (Vector3)spline.EvaluatePosition(t);
            Vector3 tangent = ((Vector3)spline.EvaluateTangent(t)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

            if (right.sqrMagnitude < 0.0001f)
                right = prevRight.sqrMagnitude > 0.0001f ? prevRight : Vector3.right;
            if (prevRight.sqrMagnitude > 0.0001f && Vector3.Dot(right, prevRight) < 0f)
                right = -right;

            prevRight = right;

            vertices[i * 2]     = center - right * halfWidth;
            vertices[i * 2 + 1] = center + right * halfWidth;

            if (i > 0)
                accumulatedLength += Vector3.Distance(center, prevCenter);

            float vCoord = accumulatedLength / width;
            uvs[i * 2]     = new Vector2(0f, vCoord);
            uvs[i * 2 + 1] = new Vector2(1f, vCoord);

            prevCenter = center;
        }

        List<int> triangles = new List<int>();
        for (int i = 0; i < sampleCount - 1; i++)
        {
            int bl = i * 2;
            int br = i * 2 + 1;
            int tl = (i + 1) * 2;
            int tr = (i + 1) * 2 + 1;

            triangles.Add(bl); triangles.Add(tl); triangles.Add(br);
            triangles.Add(br); triangles.Add(tl); triangles.Add(tr);
        }

        Mesh mesh = new Mesh();
        if (vertices.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private GameObject CreateRoadObject(string name, Mesh mesh, Material mat, int layer, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.layer = layer;
        obj.transform.SetParent(parent, false);

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;

        MeshCollider mc = obj.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        return obj;
    }

    private void EnsureMaterials()
    {
        if (roadMaterial == null)
        {
            roadMaterial = new Material(Shader.Find("Standard"));
            roadMaterial.color = new Color(0.18f, 0.18f, 0.18f);
        }
    }

    public void ClearRoads()
    {
        if (meshRoot != null)
        {
            DestroyImmediate(meshRoot);
            meshRoot = null;
        }

        GameObject leftover = GameObject.Find("Procedural_Road_System");
        if (leftover != null) DestroyImmediate(leftover);
    }

    private void Start()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showSplineGizmos || meshRoot == null) return;

        Gizmos.color = new Color(0f, 1f, 0.2f, 0.5f);
        foreach (Transform child in meshRoot.transform)
        {
            MeshFilter mf = child.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                Gizmos.DrawWireMesh(mf.sharedMesh, child.position, child.rotation, child.lossyScale);
        }
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(ProceduralRoadBuilder))]
public class ProceduralRoadBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8);

        ProceduralRoadBuilder builder = (ProceduralRoadBuilder)target;

        GUI.backgroundColor = new Color(0.3f, 0.85f, 0.3f);
        if (GUILayout.Button("▶ Build Roads (Editor)", GUILayout.Height(32)))
        {
            var rng = builder.GetComponent<RoadNetworkGenerator>();
            if (rng != null && (rng.nodes == null || rng.nodes.Count == 0))
                rng.Generate();

            builder.BuildRoads();
        }

        GUI.backgroundColor = new Color(1f, 0.4f, 0.3f);
        if (GUILayout.Button("✕ Clear Roads", GUILayout.Height(24)))
        {
            builder.ClearRoads();
        }

        GUI.backgroundColor = Color.white;
    }
}
#endif