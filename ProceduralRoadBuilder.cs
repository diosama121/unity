using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 程序化道路生成器
/// 功能：根据RoadNetworkGenerator的路网，自动生成道路Mesh + 路口Prefab
/// 挂载位置：与 RoadNetworkGenerator 同一个 GameObject
/// </summary>
[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    // =============================================
    // Inspector 配置
    // =============================================

    [Header("=== 道路参数 ===")]
    [Tooltip("道路宽度（米）")]
    public float roadWidth = 6f;

    [Tooltip("道路材质（不填则用默认灰色）")]
    public Material roadMaterial;

    [Tooltip("人行道材质")]
    public Material sidewalkMaterial;

    [Tooltip("道路高度偏移（略高于地面防止Z-fighting）")]
    public float roadHeightOffset = 0.5f;

    [Header("=== 路口Prefab ===")]
    [Tooltip("1号：十字路口（4方向）")]
    public GameObject prefab_Intersection4;

    [Tooltip("2号：T字路口（3方向）")]
    public GameObject prefab_Intersection3;

    [Tooltip("3号：直路（2方向对向）")]
    public GameObject prefab_Straight;

    [Tooltip("4号：转角（2方向直角）")]
    public GameObject prefab_Corner;

    [Tooltip("5号：断头路（1方向）")]
    public GameObject prefab_DeadEnd;

    [Tooltip("6号：孤立点/建筑物占位")]
    public GameObject prefab_Isolated;

    [Header("=== 生成控制 ===")]
    [Tooltip("是否生成道路Mesh（两点间的路面）")]
    public bool generateRoadMesh = true;

    [Tooltip("是否在路点处放置路口Prefab")]
    public bool placeIntersectionPrefabs = true;

    [Tooltip("是否生成人行道")]
    public bool generateSidewalk = true;

    [Tooltip("人行道宽度")]
    public float sidewalkWidth = 1.5f;

    [Tooltip("人行道高度")]
    public float sidewalkHeight = 0.2f;

    [Header("=== 调试 ===")]
    public bool showDebugLog = true;

    // =============================================
    // 内部变量
    // =============================================

    private RoadNetworkGenerator roadGen;
    public float intersectionScale = 1f;

    // 生成的所有道路物体（方便清理）
    private List<GameObject> generatedObjects = new List<GameObject>();

    // 道路根节点
    private GameObject roadRoot;
    private GameObject intersectionRoot;

    // =============================================
    // 路口类型枚举
    // =============================================

    public enum IntersectionType
    {
        Isolated = 0,  // 无连接
        DeadEnd = 1,  // 1个连接
        Corner = 2,  // 2个连接，直角
        Straight = 3,  // 2个连接，直线
        TJunction = 4,  // 3个连接
        Cross = 5   // 4个连接
    }

    // =============================================
    // 公共接口
    // =============================================

    /// <summary>
    /// 构建道路（供外部调用，RoadNetworkGenerator.Generate()后调用）
    /// </summary>
    public void BuildRoads()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();

        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count == 0)
        {
            Debug.LogError("ProceduralRoadBuilder: 未找到路网数据，请先生成路网！");
            return;
        }

        ClearRoads();

        roadRoot = new GameObject("Roads_Mesh");
        intersectionRoot = new GameObject("Roads_Intersections");
        roadRoot.transform.SetParent(transform);
        intersectionRoot.transform.SetParent(transform);

        generatedObjects.Add(roadRoot);
        generatedObjects.Add(intersectionRoot);

        EnsureMaterials();

        int roadCount = 0;
        int straightCount = 0;
        int intersectionCount = 0;

        HashSet<string> processedEdges = new HashSet<string>();

        foreach (var edge in roadGen.edges)
        {
            string key = $"{Mathf.Min(edge.Item1, edge.Item2)}_{Mathf.Max(edge.Item1, edge.Item2)}";
            if (processedEdges.Contains(key)) continue;
            processedEdges.Add(key);

            var nodeA = roadGen.nodes[edge.Item1];
            var nodeB = roadGen.nodes[edge.Item2];

            // 1. 生成路段Mesh
            if (generateRoadMesh)
            {
                BuildRoadSegment(nodeA.position, nodeB.position, roadRoot.transform);
                roadCount++;
            }

            // 2. 在路段中点放直线Prefab（独立于Mesh开关）
            if (placeIntersectionPrefabs && prefab_Straight != null)
            {
                Vector3 center = (nodeA.position + nodeB.position) / 2f;
                Vector3 dir = (nodeB.position - nodeA.position);
                dir.y = 0;
                Quaternion rot = Quaternion.LookRotation(dir.normalized);

                GameObject obj = Instantiate(
                    prefab_Straight,
                    center + Vector3.up * roadHeightOffset,
                    rot,
                    roadRoot.transform
                );
                obj.name = $"Straight_{edge.Item1}_{edge.Item2}";
                obj.transform.localScale = Vector3.one * intersectionScale;
                generatedObjects.Add(obj);
                straightCount++;
            }
        }

        // 3. 在路点处放路口Prefab（十字、T字、转角、断头、孤立）
        if (placeIntersectionPrefabs)
        {
            foreach (var node in roadGen.nodes)
            {
                // 直线已经在边上处理了，这里跳过Straight类型
                IntersectionType type = ClassifyNode(node);
                if (type == IntersectionType.Straight) continue;

                PlaceIntersection(node, type, intersectionRoot.transform);
                intersectionCount++;
            }
        }

        if (showDebugLog)
            Debug.Log($"✅ 道路生成完成：{roadCount} 路段Mesh，{straightCount} 直线Prefab，{intersectionCount} 路口Prefab");
    }
    /// 清理所有生成的道路
    /// </summary>
    public void ClearRoads()
    {
        foreach (var obj in generatedObjects)
        {
            if (obj != null)
                DestroyImmediate(obj);
        }
        generatedObjects.Clear();

        // 额外清理可能残留的子物体
        Transform roadRootT = transform.Find("Roads_Mesh");
        Transform intRootT = transform.Find("Roads_Intersections");
        if (roadRootT != null) DestroyImmediate(roadRootT.gameObject);
        if (intRootT != null) DestroyImmediate(intRootT.gameObject);
    }

    // =============================================
    // 路段Mesh生成
    // =============================================

    void BuildRoadSegment(Vector3 posA, Vector3 posB, Transform parent)
    {
        Vector3 center = (posA + posB) / 2f;
        Vector3 direction = (posB - posA);
        float length = direction.magnitude;
        direction.Normalize();

        // ---- 主路面 ----
        GameObject roadObj = new GameObject("RoadSegment");
        roadObj.transform.SetParent(parent);
        roadObj.transform.position = center + Vector3.up * roadHeightOffset;
        roadObj.transform.rotation = Quaternion.LookRotation(direction);

        MeshFilter mf = roadObj.AddComponent<MeshFilter>();
        MeshRenderer mr = roadObj.AddComponent<MeshRenderer>();
        mr.material = roadMaterial;

        mf.mesh = BuildQuadMesh(length, roadWidth);

        // 添加Collider（车辆可以在上面行驶）
        MeshCollider mc = roadObj.AddComponent<MeshCollider>();
        mc.sharedMesh = mf.mesh;

        // ---- 人行道（左右各一条）----
        if (generateSidewalk && sidewalkMaterial != null)
        {
            BuildSidewalkSegment(posA, posB, parent, true);
            BuildSidewalkSegment(posA, posB, parent, false);
        }
    }

    void BuildSidewalkSegment(Vector3 posA, Vector3 posB, Transform parent, bool isLeft)
    {
        Vector3 direction = (posB - posA).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, direction);
        float sideOffset = (roadWidth / 2f + sidewalkWidth / 2f) * (isLeft ? -1f : 1f);

        Vector3 offsetA = posA + right * sideOffset;
        Vector3 offsetB = posB + right * sideOffset;
        Vector3 center = (offsetA + offsetB) / 2f;
        float length = Vector3.Distance(offsetA, offsetB);

        GameObject swObj = new GameObject($"Sidewalk_{(isLeft ? "L" : "R")}");
        swObj.transform.SetParent(parent);
        swObj.transform.position = center + Vector3.up * (roadHeightOffset + sidewalkHeight);
        swObj.transform.rotation = Quaternion.LookRotation(direction);

        MeshFilter mf = swObj.AddComponent<MeshFilter>();
        MeshRenderer mr = swObj.AddComponent<MeshRenderer>();
        mr.material = sidewalkMaterial;
        mf.mesh = BuildQuadMesh(length, sidewalkWidth);
    }

    /// <summary>
    /// 生成一个朝向Z轴的Quad Mesh
    /// </summary>
    Mesh BuildQuadMesh(float length, float width)
    {
        Mesh mesh = new Mesh();

        float halfW = width / 2f;
        float halfL = length / 2f;

        mesh.vertices = new Vector3[]
        {
            new Vector3(-halfW, 0, -halfL),
            new Vector3( halfW, 0, -halfL),
            new Vector3(-halfW, 0,  halfL),
            new Vector3( halfW, 0,  halfL)
        };

        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };

        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, length / width),  // UV沿长度方向平铺
            new Vector2(1, length / width)
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // =============================================
    // 路口分类 + 摆放
    // =============================================

    /// <summary>
    /// 根据节点连接数和方向，判断路口类型
    /// </summary>
    IntersectionType ClassifyNode(RoadNetworkGenerator.WaypointNode node)
    {
        int connectionCount = node.neighbors.Count;

        switch (connectionCount)
        {
            case 0: return IntersectionType.Isolated;
            case 1: return IntersectionType.DeadEnd;
            case 3: return IntersectionType.TJunction;
            case 4: return IntersectionType.Cross;
            case 2:
                bool corner = IsCorner(node);
                Debug.Log($"节点{node.id} 2连接 → {(corner ? "Corner" : "Straight")} | 角度:{Vector3.Angle((roadGen.nodes[node.neighbors[0]].position - node.position).normalized, (roadGen.nodes[node.neighbors[1]].position - node.position).normalized):F1}°");
                return corner ? IntersectionType.Corner : IntersectionType.Straight;
            default:
                // 5个以上连接，当十字路口处理
                return IntersectionType.Cross;
        }
    }

    /// <summary>
    /// 判断2个连接的节点是否是转角（夹角小于150度为转角）
    /// </summary>
    bool IsCorner(RoadNetworkGenerator.WaypointNode node)
    {
        if (node.neighbors.Count != 2) return false;

        Vector3 posA = roadGen.nodes[node.neighbors[0]].position;
        Vector3 posB = roadGen.nodes[node.neighbors[1]].position;

        Vector3 dirA = (posA - node.position).normalized;
        Vector3 dirB = (posB - node.position).normalized;

        float angle = Vector3.Angle(dirA, dirB);
        return angle < 150f; // 夹角小于150度认为是转角
    }

    /// <summary>
    /// 在路点处放置对应路口Prefab或生成占位体
    /// </summary>
    void PlaceIntersection(RoadNetworkGenerator.WaypointNode node,
                         IntersectionType type, Transform parent)
    {
        GameObject prefab = GetPrefabForType(type);
        Vector3 pos = node.position + Vector3.up * roadHeightOffset;
        Quaternion rot = CalculateIntersectionRotation(node, type);

        GameObject obj;

        if (prefab != null)
        {
            obj = Instantiate(prefab, pos, rot, parent);
            Debug.Log($"✅ 放置Prefab: {type} 于 {pos}"); // 加这行
        }
        else
        {
            obj = CreatePlaceholder(type, pos, rot, parent);
            Debug.Log($"⚠️ 无Prefab占位: {type} 于 {pos}"); // 加这行
        }

        obj.name = $"Node_{node.id}_{type}";
        generatedObjects.Add(obj);
    }
    /// <summary>
    /// 根据路口类型获取对应Prefab
    /// </summary>
    GameObject GetPrefabForType(IntersectionType type)
    {
        switch (type)
        {
            case IntersectionType.Cross: return prefab_Intersection4;
            case IntersectionType.TJunction: return prefab_Intersection3;
            case IntersectionType.Straight: return prefab_Straight;
            case IntersectionType.Corner: return prefab_Corner;
            case IntersectionType.DeadEnd: return prefab_DeadEnd;
            case IntersectionType.Isolated: return prefab_Isolated;
            default: return null;
        }
    }

    /// <summary>
    /// 计算路口朝向（对齐到主要道路方向）
    /// </summary>
    Quaternion CalculateIntersectionRotation(RoadNetworkGenerator.WaypointNode node,
                                             IntersectionType type)
    {
        if (node.neighbors.Count == 0)
            return Quaternion.identity;

        if (type == IntersectionType.Straight)
        {
            // 直路：对齐到两个邻居的连线方向
            Vector3 dirA = (roadGen.nodes[node.neighbors[0]].position - node.position);
            dirA.y = 0;
            return Quaternion.LookRotation(dirA.normalized);
        }

        if (type == IntersectionType.Corner)
        {
            Vector3 dirA = (roadGen.nodes[node.neighbors[0]].position - node.position);
            Vector3 dirB = (roadGen.nodes[node.neighbors[1]].position - node.position);
            dirA.y = 0; dirB.y = 0;
            dirA.Normalize(); dirB.Normalize();

            Vector3 bisector = (dirA + dirB).normalized;
            float angle = Mathf.Atan2(bisector.x, bisector.z) * Mathf.Rad2Deg;
            return Quaternion.Euler(0, angle + 45f + 180f, 0); // 加180修正反向
        }

        if (type == IntersectionType.TJunction)
        {
            Vector3 sum = Vector3.zero;
            foreach (int nId in node.neighbors)
            {
                Vector3 d = (roadGen.nodes[nId].position - node.position);
                d.y = 0;
                sum += d.normalized;
            }
            sum.y = 0;
            if (sum.magnitude > 0.01f)
                return Quaternion.LookRotation(sum.normalized) *
                       Quaternion.Euler(0, 270f, 0); // 加90修正偏移
            return Quaternion.identity;
        }
        // 十字路口和其他：取第一个邻居方向
        Vector3 mainDir = (roadGen.nodes[node.neighbors[0]].position - node.position);
        mainDir.y = 0;
        if (mainDir.magnitude > 0.01f)
            return Quaternion.LookRotation(mainDir.normalized);

        return Quaternion.identity;
    }

    /// <summary>
    /// 创建颜色占位体（无Prefab时使用）
    /// </summary>
    GameObject CreatePlaceholder(IntersectionType type, Vector3 pos,
                                 Quaternion rot, Transform parent)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.transform.SetParent(parent);
        obj.transform.position = pos;
        obj.transform.rotation = rot;
        obj.transform.localScale = new Vector3(roadWidth, 0.05f, roadWidth);

        // 颜色区分类型
        Material mat = new Material(Shader.Find("Standard"));
        switch (type)
        {
            case IntersectionType.Cross: mat.color = new Color(0.3f, 0.3f, 0.3f); break; // 深灰：十字
            case IntersectionType.TJunction: mat.color = new Color(0.4f, 0.4f, 0.4f); break; // 中灰：T字
            case IntersectionType.Straight: mat.color = new Color(0.35f, 0.35f, 0.35f); break; // 灰：直路
            case IntersectionType.Corner: mat.color = new Color(0.4f, 0.35f, 0.3f); break; // 暖灰：转角
            case IntersectionType.DeadEnd: mat.color = new Color(0.5f, 0.4f, 0.3f); break; // 棕：断头
            case IntersectionType.Isolated: mat.color = new Color(0.2f, 0.5f, 0.2f); break; // 绿：孤立/建筑
        }

        obj.GetComponent<MeshRenderer>().material = mat;
        return obj;
    }

    // =============================================
    // 材质初始化
    // =============================================

    void EnsureMaterials()
    {
        if (roadMaterial == null)
        {
            roadMaterial = new Material(Shader.Find("Standard"));
            roadMaterial.color = new Color(0.25f, 0.25f, 0.25f); // 深灰色道路
            roadMaterial.name = "RoadMaterial_Auto";
        }

        if (sidewalkMaterial == null && generateSidewalk)
        {
            sidewalkMaterial = new Material(Shader.Find("Standard"));
            sidewalkMaterial.color = new Color(0.7f, 0.7f, 0.65f); // 浅灰色人行道
            sidewalkMaterial.name = "SidewalkMaterial_Auto";
        }
    }

    // =============================================
    // 与RoadNetworkGenerator联动
    // =============================================

    void Start()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();
    }
}
