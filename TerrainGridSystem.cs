using System.Collections.Generic;
using UnityEngine;

public class TerrainGridSystem : MonoBehaviour
{
    public static TerrainGridSystem Instance { get; private set; }

    [Header("网格参数")]
    public float cellSize = 1f;

    [Header("地形噪声")]
    public float noiseFrequency = 0.05f;
    public float heightScale = 10f;
    public bool usePerlinNoise = true;

    [Header("城市平整融合")]
    public float urbanFlatHeight = 0f;          // 城市基准平面高度
    public float urbanBlendRadius = 50f;        // 城市平整影响半径

    [Header("地形网格渲染")]
    public Material terrainMaterial;
    public float terrainHeightOffset = 0f;
    public string terrainLayerName = "Default";

    private float[,] _heightMap;
    private bool[,] _roadMask;
    private int _dimX, _dimZ;
    private float _minX, _minZ;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;

    private void Awake()
    {
        if (cellSize > 2.0f)
        {
            Debug.LogWarning($"[TerrainGrid] cellSize {cellSize} 过大，强制设为 2.0m");
            cellSize = 2.0f;
        }
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log("[TerrainGrid] Instance 已成功注册。");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Initialize(Bounds bounds)
    {
        bounds.Expand(100f);
        GenerateHeightMap(bounds);
        GenerateTerrainMesh();
    }

    public void Reinitialize(Bounds bounds)
    {
        bounds.Expand(100f);
        GenerateHeightMap(bounds);
        _roadMask = new bool[_dimX - 1, _dimZ - 1];
        GenerateTerrainMesh();
    }

    // ===================== 核心供血源：SampleHeightRaw =====================
   public float SampleHeightRaw(float worldX, float worldZ)
{
    RoadNetworkGenerator roadGen = FindObjectOfType<RoadNetworkGenerator>();
    
    // 1. 基础噪声高度
    float noiseY = 0f;
    if (roadGen != null && roadGen.isCountryside)   // 只有乡村模式才计算噪声
    {
        float seedOffset = roadGen.seed * 1000f;
        float scale = roadGen.countrysideHeightScale;
        float nx = (worldX + seedOffset) * noiseFrequency;
        float nz = (worldZ + seedOffset) * noiseFrequency;
        noiseY = Mathf.PerlinNoise(nx, nz) * scale;
    }
    
    // 2. 计算城市权重：距离最近路口
    float urbanMask = 0f;
    if (roadGen != null)
    {
        float minDist = float.MaxValue;
        foreach (var node in roadGen.nodes)
        {
            // 只要有 3 条以上邻居就视为路口（含十字、丁字）
            if (node.neighbors.Count >= 3)
            {
                float d = Vector2.Distance(
                    new Vector2(worldX, worldZ),
                    new Vector2(node.position.x, node.position.z));
                if (d < minDist) minDist = d;
            }
        }
        if (minDist < float.MaxValue)
            urbanMask = 1f - Mathf.SmoothStep(0f, urbanBlendRadius, minDist);
    }
    
    // 3. 平滑融合
    float blendedY = Mathf.Lerp(noiseY, urbanFlatHeight, urbanMask);
    return blendedY + terrainHeightOffset;
}

    // ===================== 旧有 SampleHeight 保持双线性插值 =====================
    public float SampleHeight(Vector2 worldXZ)
    {
        if (_heightMap == null) return 0f;
        float fx = (worldXZ.x - _minX) / cellSize;
        float fz = (worldXZ.y - _minZ) / cellSize;
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

    // ===================== 内部实现 =====================
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
                _heightMap[i, j] = SampleHeightRaw(wx, wz);
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

                // ✅ 修正：_heightMap 已包含 terrainHeightOffset，不再重复添加
                Vector3 v00 = new Vector3(x0, _heightMap[i, j], z0);
                Vector3 v10 = new Vector3(x1, _heightMap[i + 1, j], z0);
                Vector3 v01 = new Vector3(x0, _heightMap[i, j + 1], z1);
                Vector3 v11 = new Vector3(x1, _heightMap[i + 1, j + 1], z1);

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
}