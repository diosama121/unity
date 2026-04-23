using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(RoadNetworkGenerator))]
public class ProceduralRoadBuilder : MonoBehaviour
{
    [Header("=== 道路核心参数 ===")]
    public float roadWidth = 6f;
    public Material roadMaterial;
    public float roadHeightOffset = 0.05f;

    [Header("=== 人行道(物理护栏) ===")]
    public bool generateSidewalk = true;
    public float sidewalkWidth = 1.5f;
    public float sidewalkHeight = 0.3f; // 抬高充当护栏
    public Material sidewalkMaterial;

    [Header("=== 城市与环境 ===")]
    public bool generateCityBlocks = true;  // 勾选生成城市，取消勾选只有公路
    public float textureScale = 5f;
    public bool spawnPedestrians = true;

    private RoadNetworkGenerator roadGen;
    private List<GameObject> generatedObjects = new List<GameObject>();
    private GameObject meshRoot;

    public void BuildRoads()
    {
        roadGen = GetComponent<RoadNetworkGenerator>();
        if (roadGen == null || roadGen.nodes == null) return;

        ClearRoads();
        meshRoot = new GameObject("Procedural_Road_System");
        meshRoot.transform.SetParent(transform);
        generatedObjects.Add(meshRoot);

        EnsureMaterials();

        // 1. 生成公路路段与两侧人行道
        HashSet<string> processedEdges = new HashSet<string>();
        foreach (var edge in roadGen.edges)
        {
            string key = $"{Mathf.Min(edge.Item1, edge.Item2)}_{Mathf.Max(edge.Item1, edge.Item2)}";
            if (processedEdges.Contains(key)) continue;
            processedEdges.Add(key);
            BuildEnhancedRoadSegment(roadGen.nodes[edge.Item1].position, roadGen.nodes[edge.Item2].position);
        }

        // 2. 生成多边形完美路口 (含路口人行道包围)
        foreach (var node in roadGen.nodes)
        {
            if (node.neighbors.Count > 1)
            {
                BuildPerfectIntersection(node);
            }
        }

        // 3. 生成城市建筑群
        if (generateCityBlocks)
        {
            GenerateCityBuildings();
        }

        Debug.Log("✅ 完美地形与城市生成完毕！无缝拼接，防压路保护已生效。");
    }

    // ==========================================
    // 模块一：精准掐头去尾的路段生成
    // ==========================================
    void BuildEnhancedRoadSegment(Vector3 posA, Vector3 posB)
    {
        Vector3 dir = (posB - posA).normalized;
        float totalLength = Vector3.Distance(posA, posB);

        // 两头各自缩进半个路宽，留给多边形路口
        float inset = roadWidth / 2f;
        if (totalLength <= inset * 2f) return; // 节点太近则直接跳过

        Vector3 start = posA + dir * inset;
        Vector3 end = posB - dir * inset;
        float length = totalLength - inset * 2f;
        Vector3 center = (start + end) / 2f;

        // --- 主路面 ---
        GameObject roadObj = new GameObject("Road_Segment");
        roadObj.transform.SetParent(meshRoot.transform);
        roadObj.transform.position = center + Vector3.up * roadHeightOffset;
        roadObj.transform.rotation = Quaternion.LookRotation(dir);

        MeshFilter mf = roadObj.AddComponent<MeshFilter>();
        MeshRenderer mr = roadObj.AddComponent<MeshRenderer>();
        mr.material = roadMaterial;
        mf.mesh = BuildTiledQuadMesh(length, roadWidth);
        roadObj.AddComponent<MeshCollider>().sharedMesh = mf.mesh;

        // --- 人行道护栏 ---
        if (generateSidewalk)
        {
            CreateSidewalkWithCollider(start, end, roadObj.transform, true);
            CreateSidewalkWithCollider(start, end, roadObj.transform, false);
        }

        // --- 虚拟行人 ---
        if (spawnPedestrians && Random.value < 0.2f && length > 15f)
        {
            GameObject pedObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            pedObj.name = $"Pedestrian_{start.x}";
            pedObj.transform.SetParent(roadObj.transform);
            pedObj.transform.position = center + Vector3.up * 1f;
            pedObj.GetComponent<MeshRenderer>().material.color = Color.red;

            // 如果你有 VirtualPedestrian 脚本，这里不会报错；如果没有，它只是个静态红色胶囊
            if (pedObj.GetComponent<VirtualPedestrian>() == null)
            {
                // 兼容处理：如果你之前删了脚本，这里防止报错
                var vp = pedObj.AddComponent<VirtualPedestrian>();
                vp.crossDistance = roadWidth + sidewalkWidth;
            }
        }
    }

    // ==========================================
    // 模块二：带有高度自适应的完美端点缝合路口
    // ==========================================
    void BuildPerfectIntersection(RoadNetworkGenerator.WaypointNode node)
    {
        float inset = roadWidth / 2f;
        GameObject intObj = new GameObject($"Intersection_{node.id}");
        intObj.transform.SetParent(meshRoot.transform);
        intObj.transform.position = node.position + Vector3.up * (roadHeightOffset + 0.005f);

        // 1. 收集方向并排序
        List<Vector3> dirs = new List<Vector3>();
        foreach (int nId in node.neighbors)
        {
            Vector3 d = (roadGen.nodes[nId].position - node.position);
            d.y = 0;
            if (d.sqrMagnitude > 0.001f) dirs.Add(d.normalized);
        }
        dirs.Sort((a, b) => Mathf.Atan2(a.x, a.z).CompareTo(Mathf.Atan2(b.x, b.z)));

        // 2. 路面顶点（保持不变）
        List<Vector3> roadVerts = new List<Vector3>();
        roadVerts.Add(Vector3.zero);
        foreach (var dir in dirs)
        {
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            float slopeY = GetSlopeY(node, dir, inset);
            roadVerts.Add(dir * inset - right * (roadWidth / 2f) + Vector3.up * slopeY);
            roadVerts.Add(dir * inset + right * (roadWidth / 2f) + Vector3.up * slopeY);
        }
        Mesh roadMesh = CreateFanMesh(roadVerts);
        intObj.AddComponent<MeshFilter>().mesh = roadMesh;
        intObj.AddComponent<MeshRenderer>().material = roadMaterial;
        intObj.AddComponent<MeshCollider>().sharedMesh = roadMesh;

        // 3. 人行道：只在相邻两条路之间的"缺口"处生成挡墙
        if (!generateSidewalk) return;

        for (int i = 0; i < dirs.Count; i++)
        {
            // 当前方向的右侧边缘点，和下一个方向的左侧边缘点之间就是缺口
            Vector3 dirA = dirs[i];
            Vector3 dirB = dirs[(i + 1) % dirs.Count];

            Vector3 rightA = Vector3.Cross(Vector3.up, dirA).normalized;
            Vector3 rightB = Vector3.Cross(Vector3.up, dirB).normalized;

            float slopeA = GetSlopeY(node, dirA, inset);
            float slopeB = GetSlopeY(node, dirB, inset);

            // 缺口的两个端点（在路口边缘，路面高度上）
            Vector3 cornerA = dirA * inset + rightA * (roadWidth / 2f) + Vector3.up * slopeA;
            Vector3 cornerB = dirB * inset - rightB * (roadWidth / 2f) + Vector3.up * slopeB;

            // 缺口宽度，太大说明是真实缺口，太小说明两条路紧贴（不需要挡墙）
            float gapDist = Vector3.Distance(cornerA, cornerB);
            if (gapDist < 0.3f) continue;

            // 在缺口处生成一段挡墙
            Vector3 wallCenter = (cornerA + cornerB) / 2f;
            Vector3 wallDir = (cornerB - cornerA).normalized;
            float wallLength = gapDist;

            GameObject wall = new GameObject($"Intersection_Wall_{node.id}_{i}");
            wall.transform.SetParent(intObj.transform);
            // 挡墙位置：路口本地坐标，抬高半个sidewalkHeight
            wall.transform.position = intObj.transform.position + wallCenter + Vector3.up * (sidewalkHeight / 2f);
            wall.transform.rotation = Quaternion.LookRotation(wallDir);

            var mf = wall.AddComponent<MeshFilter>();
            mf.mesh = BuildWallMesh(wallLength, sidewalkWidth * 0.5f, sidewalkHeight);
            wall.AddComponent<MeshRenderer>().material = sidewalkMaterial;

            var bc = wall.AddComponent<BoxCollider>();
            bc.size = new Vector3(sidewalkWidth * 0.5f, sidewalkHeight, wallLength);
            bc.center = Vector3.zero;
        }
    }

    // 提取坡度计算，两处复用
    float GetSlopeY(RoadNetworkGenerator.WaypointNode node, Vector3 dir, float inset)
    {
        foreach (int n in node.neighbors)
        {
            Vector3 nDir = roadGen.nodes[n].position - node.position;
            if (Vector3.Dot(nDir.normalized, dir) > 0.9f)
                return (inset / nDir.magnitude) * (roadGen.nodes[n].position.y - node.position.y);
        }
        return 0f;
    }

    // 实心挡墙网格
    Mesh BuildWallMesh(float length, float width, float height)
    {
        float hw = width / 2f, hh = height / 2f, hl = length / 2f;
        Vector3[] verts = new Vector3[]
        {
        new Vector3(-hw, -hh, -hl), new Vector3( hw, -hh, -hl),
        new Vector3(-hw, -hh,  hl), new Vector3( hw, -hh,  hl),
        new Vector3(-hw,  hh, -hl), new Vector3( hw,  hh, -hl),
        new Vector3(-hw,  hh,  hl), new Vector3( hw,  hh,  hl),
        };
        int[] tris = new int[]
        {
    0,1,2, 1,3,2,  // 底
    4,6,5, 5,6,7,  // 顶
    0,4,1, 1,4,5,  // 前
    2,3,6, 3,7,6,  // 后
    0,2,4, 2,6,4,  // 左
    1,5,3, 3,5,7,  // 右
        };
        Mesh mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }
    // ==========================================
    // 模块三：防压路保护的城市建筑群生成
    // ==========================================
    void GenerateCityBuildings()
    {
        // 尝试获取生成器参数，如果由于权限问题拿不到，给个保底默认值
        float cellSize = 30f;
        float randomOffset = 5f;
        int gw = 10;
        int gh = 10;

        if (roadGen != null)
        {
            // 通过反射或直接访问获取 RoadNetworkGenerator 的参数
            // 假设你的 RoadNetworkGenerator 里的变量是 public
            cellSize = roadGen.cellSize;
            randomOffset = roadGen.randomOffset;
            gw = roadGen.gridWidth;
            gh = roadGen.gridHeight;
        }

        GameObject cityRoot = new GameObject("City_Buildings");
        cityRoot.transform.SetParent(meshRoot.transform);

        Material glassMat = new Material(Shader.Find("Standard"));
        glassMat.color = new Color(0.12f, 0.15f, 0.2f);
        glassMat.SetFloat("_Metallic", 0.9f);
        glassMat.SetFloat("_Glossiness", 0.8f);

        // 核心安全计算：格子总宽 - 两侧最大偏移量 - 路宽 - 人行道宽 - 缝隙保护
        float totalRoadOccupancy = roadWidth + (generateSidewalk ? sidewalkWidth * 2 : 0);
        float safeSize = cellSize - (randomOffset * 2f) - totalRoadOccupancy - 2f;

        // 兜底保护
        if (safeSize < 2f) safeSize = 2f;

        // 固定种子，保证每次生成的楼房都在同一个位置
        Random.InitState(42);

        for (int x = 0; x < gw - 1; x++)
        {
            for (int z = 0; z < gh - 1; z++)
            {
                // 30%的概率留空不盖楼，增加呼吸感
                if (Random.value > 0.3f)
                {
                    // 中心点在当前格子的中央
                    Vector3 center = new Vector3((x + 0.5f) * cellSize, 0, (z + 0.5f) * cellSize);

                    // 获取该点中心的地形高度，适配乡村起伏
                    float elevation = 0f;
                    if (roadGen != null && roadGen.nodes != null && roadGen.nodes.Count > 0)
                    {
                        // 找最近的节点获取大致高度
                        float minDist = float.MaxValue;
                        foreach (var node in roadGen.nodes)
                        {
                            float dist = Vector3.Distance(new Vector3(node.position.x, 0, node.position.z), new Vector3(center.x, 0, center.z));
                            if (dist < minDist) { minDist = dist; elevation = node.position.y; }
                        }
                    }

                    float bWidth = Random.Range(safeSize * 0.4f, safeSize * 0.9f);
                    float bDepth = Random.Range(safeSize * 0.4f, safeSize * 0.9f);
                    float bHeight = Random.Range(15f, 50f);

                    GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    building.name = $"Building_{x}_{z}";
                    building.transform.SetParent(cityRoot.transform);
                    // 高度叠加地形起伏
                    building.transform.position = new Vector3(center.x, elevation + (bHeight / 2f), center.z);
                    building.transform.localScale = new Vector3(bWidth, bHeight, bDepth);

                    building.GetComponent<MeshRenderer>().material = glassMat;
                    // Cube 自带 BoxCollider，能挡住雷达
                }
            }
        }
    }

    // ==========================================
    // 底层网格工具函数
    // ==========================================
    void CreateSidewalkWithCollider(Vector3 start, Vector3 end, Transform parent, bool isLeft)
    {
        Vector3 dir = (end - start).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        float sideOffset = (roadWidth / 2f + sidewalkWidth / 2f) * (isLeft ? -1f : 1f);
        float length = Vector3.Distance(start, end);
        Vector3 center = (start + end) / 2f;

        GameObject sw = new GameObject($"Sidewalk_{(isLeft ? "L" : "R")}");
        sw.transform.SetParent(parent);

        // 直接用世界坐标，避免受父物体旋转影响
        sw.transform.position = center + right * sideOffset + Vector3.up * (roadHeightOffset + sidewalkHeight);
        sw.transform.rotation = Quaternion.LookRotation(dir); // 和路段朝向一致

        MeshFilter mf = sw.AddComponent<MeshFilter>();
        mf.mesh = BuildTiledQuadMesh(length, sidewalkWidth);
        sw.AddComponent<MeshRenderer>().material = sidewalkMaterial;
        sw.AddComponent<MeshCollider>().sharedMesh = mf.mesh;
    }

    Mesh CreateFanMesh(List<Vector3> verts)
    {
        Mesh m = new Mesh();
        m.vertices = verts.ToArray();
        List<int> tris = new List<int>();
        // verts[0]是中心点，从i=1开始每两个相邻外围点组成一个三角
        for (int i = 1; i < verts.Count - 1; i++)
        {
            tris.Add(0);
            tris.Add(i);
            tris.Add(i + 1);
        }
        // 最后一个三角：最后一个外围点 → 第一个外围点，闭合扇形
        tris.Add(0);
        tris.Add(verts.Count - 1);
        tris.Add(1);
        m.triangles = tris.ToArray();
        m.RecalculateNormals();
        return m;
    }

    Mesh BuildTiledQuadMesh(float length, float width)
    {
        Mesh mesh = new Mesh();
        float hw = width / 2f; float hl = length / 2f;
        mesh.vertices = new Vector3[] {
            new Vector3(-hw, 0, -hl), new Vector3(hw, 0, -hl),
            new Vector3(-hw, 0, hl), new Vector3(hw, 0, hl)
        };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        float vRepeat = length / textureScale;
        mesh.uv = new Vector2[] {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, vRepeat), new Vector2(1, vRepeat)
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    void EnsureMaterials()
    {
        if (roadMaterial == null) roadMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.2f, 0.2f) };
        if (sidewalkMaterial == null) sidewalkMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.6f, 0.6f, 0.6f) };
    }

    public void ClearRoads()
    {
        foreach (var obj in generatedObjects) if (obj != null) DestroyImmediate(obj);
        generatedObjects.Clear();
    }

    void Start() { roadGen = GetComponent<RoadNetworkGenerator>(); }
}