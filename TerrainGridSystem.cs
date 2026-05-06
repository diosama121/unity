using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 纯网格地形系统（V2.0 架构）。
/// - 使用 Mathf.PerlinNoise 叠加高度生成顶点 Y 值。
/// - 通过 BakeRoadMask() 剔除道路内部网格单元，道路下方不生成三角形。
/// - 提供双线性插值的高度查询接口 SampleHeight()，供 WorldModel 同步路网节点高度。
/// </summary>
public class TerrainGridSystem : MonoBehaviour
{
    public static TerrainGridSystem Instance { get; private set; }

    [Header("网格参数")]
    [Tooltip("网格单元边长（世界单位）")]
    public float cellSize = 1f;

    [Header("地形噪声")]
    public float noiseFrequency = 0.05f;
    public float heightScale = 10f;
    public bool usePerlinNoise = true;

    [Header("地形网格渲染")]
    public Material terrainMaterial;
    public float terrainHeightOffset = 0f;      // 可整体微调高度
    public string terrainLayerName = "Default";

    // 内部数据
    private float[,] _heightMap;                // 高度场，尺寸 [dimX, dimZ]
    private bool[,] _roadMask;                  // 道路遮罩，尺寸 [dimX-1, dimZ-1]
    private int _dimX, _dimZ;                   // 高度图采样点数
    private float _minX, _minZ;                 // 高度图最小世界坐标
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;

    private void Awake()
    {
        if (cellSize > 2.0f)
        {
            Debug.LogWarning($"[TerrainGrid] cellSize {cellSize} 过大，强制设为 2.0m 以保证道路插值精度。");
            cellSize = 2.0f;
        }
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 根据路网包围盒初始化地形网格（强制扩展安全边距）
    /// </summary>
    public void Initialize(Bounds bounds)
    {
        // ==========================================
        // 【a1 地形真理防线】强制扩大包围盒，防止边缘道路膨胀后越界
        // ==========================================
        bounds.Expand(100f); // 四周各外扩50米的安全冗余
        GenerateHeightMap(bounds);
        GenerateTerrainMesh();
    }

    /// <summary>
    /// 重新初始化（同样应用安全边距）
    /// </summary>
    public void Reinitialize(Bounds bounds)
    {
        bounds.Expand(100f); // 安全冗余
        GenerateHeightMap(bounds);
        _roadMask = new bool[_dimX - 1, _dimZ - 1];   // 清空遮罩
        GenerateTerrainMesh();
    }

    /// <summary>
    /// 传入道路多边形列表（世界坐标下的 Vector3[]，XZ 平面），
    /// 标记每个单元格是否被道路覆盖，并重新生成地形网格。
    /// </summary>
    public void BakeRoadMask(List<Vector3[]> roadPolygons)
    {
        if (_roadMask == null)
        {
            Debug.LogWarning("[TerrainGridSystem] 高度图尚未初始化，无法烘焙道路遮罩。");
            return;
        }

        int cellCountX = _dimX - 1;
        int cellCountZ = _dimZ - 1;

        for (int i = 0; i < cellCountX; i++)
        {
            for (int j = 0; j < cellCountZ; j++)
            {
                float cx = _minX + (i + 0.5f) * cellSize;
                float cz = _minZ + (j + 0.5f) * cellSize;
                Vector2 point = new Vector2(cx, cz);

                bool inside = false;
                foreach (var poly in roadPolygons)
                {
                    if (poly == null || poly.Length < 3) continue;
                    List<Vector2> poly2D = new List<Vector2>(poly.Length);
                    foreach (var v in poly)
                        poly2D.Add(new Vector2(v.x, v.z));
                    if (PointInPolygonXZ(point, poly2D))
                    {
                        inside = true;
                        break;
                    }
                }
                _roadMask[i, j] = inside;
            }
        }

        GenerateTerrainMesh();
    }

    /// <summary>
    /// 高度查询接口（严格遵守 V2.0 规范）：
    /// - 预烘焙 float[,] 二维数组
    /// - 双线性插值
    /// - 边界钳制至 dimension - 1
    /// </summary>
    public float SampleHeight(Vector2 worldXZ)
    {
        if (_heightMap == null) return 0f;
        float fx = (worldXZ.x - _minX) / cellSize;
        float fz = (worldXZ.y - _minZ) / cellSize;

        // 钳制在 [0, dimension - 1] 之间
        fx = Mathf.Clamp(fx, 0f, _dimX - 1);
        fz = Mathf.Clamp(fz, 0f, _dimZ - 1);

        int x0 = Mathf.FloorToInt(fx);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, _dimX - 1);
        int z1 = Mathf.Min(z0 + 1, _dimZ - 1);

        float tx = fx - x0;
        float tz = fz - z0;

        float h00 = _heightMap[x0, z0];
        float h10 = _heightMap[x1, z0];
        float h01 = _heightMap[x0, z1];
        float h11 = _heightMap[x1, z1];

        float h0 = Mathf.Lerp(h00, h10, tx);
        float h1 = Mathf.Lerp(h01, h11, tx);
        return Mathf.Lerp(h0, h1, tz);
    }

    public float GetHeightAt(float worldX, float worldZ)
    {
        return SampleHeight(new Vector2(worldX, worldZ));
    }

    // ------------------------------------------------
    // 内部实现
    // ------------------------------------------------

    private void GenerateHeightMap(Bounds bounds)
    {
        _minX = bounds.min.x;
        _minZ = bounds.min.z;
        float width = bounds.size.x;
        float length = bounds.size.z;

        _dimX = Mathf.CeilToInt(width / cellSize) + 1;
        _dimZ = Mathf.CeilToInt(length / cellSize) + 1;

        _heightMap = new float[_dimX, _dimZ];
        for (int i = 0; i < _dimX; i++)
        {
            for (int j = 0; j < _dimZ; j++)
            {
                float wx = _minX + i * cellSize;
                float wz = _minZ + j * cellSize;
                float y = 0f;
                if (usePerlinNoise)
                    y = Mathf.PerlinNoise(wx * noiseFrequency, wz * noiseFrequency) * heightScale;
                _heightMap[i, j] = y;
            }
        }

        _roadMask = new bool[_dimX - 1, _dimZ - 1];
    }

    private void GenerateTerrainMesh()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        int cellCountX = _dimX - 1;
        int cellCountZ = _dimZ - 1;

        for (int i = 0; i < cellCountX; i++)
        {
            for (int j = 0; j < cellCountZ; j++)
            {
                if (_roadMask[i, j]) continue;

                float x0 = _minX + i * cellSize;
                float z0 = _minZ + j * cellSize;
                float x1 = _minX + (i + 1) * cellSize;
                float z1 = _minZ + (j + 1) * cellSize;

                Vector3 v00 = new Vector3(x0, _heightMap[i, j] + terrainHeightOffset, z0);
                Vector3 v10 = new Vector3(x1, _heightMap[i + 1, j] + terrainHeightOffset, z0);
                Vector3 v01 = new Vector3(x0, _heightMap[i, j + 1] + terrainHeightOffset, z1);
                Vector3 v11 = new Vector3(x1, _heightMap[i + 1, j + 1] + terrainHeightOffset, z1);

                int vi = vertices.Count;
                vertices.Add(v00);
                vertices.Add(v10);
                vertices.Add(v01);
                vertices.Add(v11);

                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(1, 1));

                triangles.Add(vi);
                triangles.Add(vi + 1);
                triangles.Add(vi + 2);
                triangles.Add(vi + 1);
                triangles.Add(vi + 3);
                triangles.Add(vi + 2);
            }
        }

        Mesh mesh = new Mesh();
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (_meshFilter == null)
        {
            GameObject terrainObj = new GameObject("TerrainGrid");
            terrainObj.transform.SetParent(transform, false);
            terrainObj.layer = LayerMask.NameToLayer(terrainLayerName);
            _meshFilter = terrainObj.AddComponent<MeshFilter>();
            _meshRenderer = terrainObj.AddComponent<MeshRenderer>();
            if (terrainMaterial != null)
                _meshRenderer.sharedMaterial = terrainMaterial;
        }
        _meshFilter.sharedMesh = mesh;
    }

    private bool PointInPolygonXZ(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int count = polygon.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                    (polygon[j].y - polygon[i].y) + polygon[i].x))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}