using System.Collections.Generic;
using UnityEngine;

public class TerrainGridSystem : MonoBehaviour
{
    public static TerrainGridSystem Instance { get; private set; }

    [Header("网格参数")]
    public float cellSize = 2f; // 强制建议为 2f

    [Header("地形噪声")]
    public float noiseFrequency = 0.05f;
    public float heightScale = 10f;
    public bool usePerlinNoise = true;

    [Header("城市平整融合")]
    public float urbanFlatHeight = 0f;          
    public float urbanBlendRadius = 50f;        

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

    private Vector2[] _fastRoadNodesCache;
    private float _cachedRoadWidth = 6f; // 缓存路宽提升性能

    private void Awake()
    {
        if (cellSize > 2.0f) cellSize = 2.0f;
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

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

    // ===================== 天地同步高程 =====================
    public float SampleHeightRaw(float worldX, float worldZ)
    {
        RoadNetworkGenerator roadGen = FindObjectOfType<RoadNetworkGenerator>();
        bool isCountry = (roadGen != null && roadGen.isCountryside);

        float noiseY = 0f;
        if (isCountry)
        {
            float seedOffset = roadGen.seed * 1000f;
            float nx = (worldX + seedOffset) * noiseFrequency;
            float nz = (worldZ + seedOffset) * noiseFrequency;
            noiseY = Mathf.PerlinNoise(nx, nz) * roadGen.countrysideHeightScale;
        }

        float blendedY = noiseY;

        if (_fastRoadNodesCache != null && _fastRoadNodesCache.Length > 0)
        {
            float minSqrDist = float.MaxValue;
            Vector2 closestNode = Vector2.zero;
            foreach (var nodePos in _fastRoadNodesCache)
            {
                float dx = worldX - nodePos.x;
                float dz = worldZ - nodePos.y;
                float sqrD = dx * dx + dz * dz;
                if (sqrD < minSqrDist) 
                {
                    minSqrDist = sqrD;
                    closestNode = nodePos;
                }
            }
            
            float minDist = Mathf.Sqrt(minSqrDist);
            
            if (isCountry)
            {
                // 【核心修复】：乡村模式，动态路宽平台化，防止地形穿透马路！
                float platformRadius = _cachedRoadWidth * 0.7f; 
                float blendRadius = _cachedRoadWidth * 1.5f;    
                
                if (minDist < platformRadius + blendRadius)
                {
                    float seedOffset = roadGen.seed * 1000f;
                    float cx = (closestNode.x + seedOffset) * noiseFrequency;
                    float cz = (closestNode.y + seedOffset) * noiseFrequency;
                    float centerNodeHeight = Mathf.PerlinNoise(cx, cz) * roadGen.countrysideHeightScale;

                    if (minDist <= platformRadius) {
                        blendedY = centerNodeHeight; // 平台核心区绝对平整
                    } else {
                        float mask = Mathf.SmoothStep(0f, 1f, (minDist - platformRadius) / blendRadius);
                        blendedY = Mathf.Lerp(centerNodeHeight, noiseY, mask);
                    }
                }
            }
            else
            {
                if (minDist < urbanBlendRadius * 1.5f)
                {
                    float urbanMask = 1f - Mathf.SmoothStep(0f, urbanBlendRadius * 1.5f, minDist);
                    blendedY = Mathf.Lerp(noiseY, urbanFlatHeight, urbanMask);
                }
            }
        }

        return blendedY + terrainHeightOffset;
    }

    public float SampleHeight(Vector2 worldXZ)
    {
        if (_heightMap == null) return 0f;
        float fx = (worldXZ.x - _minX) / cellSize;
        float fz = (worldXZ.y - _minZ) / cellSize;
        fx = Mathf.Clamp(fx, 0f, _dimX - 1); fz = Mathf.Clamp(fz, 0f, _dimZ - 1);
        int x0 = Mathf.FloorToInt(fx); int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, _dimX - 1); int z1 = Mathf.Min(z0 + 1, _dimZ - 1);
        float tx = fx - x0; float tz = fz - z0;
        float h0 = Mathf.Lerp(_heightMap[x0, z0], _heightMap[x1, z0], tx);
        float h1 = Mathf.Lerp(_heightMap[x0, z1], _heightMap[x1, z1], tx);
        return Mathf.Lerp(h0, h1, tz);
    }

    private void GenerateHeightMap(Bounds bounds)
    {
        _minX = bounds.min.x; _minZ = bounds.min.z;
        _dimX = Mathf.CeilToInt(bounds.size.x / cellSize) + 1;
        _dimZ = Mathf.CeilToInt(bounds.size.z / cellSize) + 1;

        ProceduralRoadBuilder builder = FindObjectOfType<ProceduralRoadBuilder>();
        if (builder != null) _cachedRoadWidth = builder.roadWidth;

        RoadNetworkGenerator roadGen = FindObjectOfType<RoadNetworkGenerator>();
        List<Vector2> validNodes = new List<Vector2>();
        if (roadGen != null && roadGen.nodes != null)
        {
            foreach (var node in roadGen.nodes)
                if (node.neighbors.Count > 0) validNodes.Add(new Vector2(node.position.x, node.position.z));
        }
        _fastRoadNodesCache = validNodes.ToArray();

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
        int cellCountX = _dimX - 1; int cellCountZ = _dimZ - 1;

        for (int i = 0; i < cellCountX; i++)
        {
            for (int j = 0; j < cellCountZ; j++)
            {
                if (_roadMask[i, j]) continue; 
                float x0 = _minX + i * cellSize; float z0 = _minZ + j * cellSize;
                float x1 = _minX + (i + 1) * cellSize; float z1 = _minZ + (j + 1) * cellSize;

                int vi = vertices.Count;
                vertices.Add(new Vector3(x0, _heightMap[i, j], z0));
                vertices.Add(new Vector3(x1, _heightMap[i + 1, j], z0));
                vertices.Add(new Vector3(x0, _heightMap[i, j + 1], z1));
                vertices.Add(new Vector3(x1, _heightMap[i + 1, j + 1], z1));

                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));

                triangles.Add(vi); triangles.Add(vi + 2); triangles.Add(vi + 1);
                triangles.Add(vi + 1); triangles.Add(vi + 2); triangles.Add(vi + 3);
            }
        }

        Mesh mesh = new Mesh();
        if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        if (_meshFilter == null)
        {
            GameObject terrainObj = new GameObject("TerrainGrid");
            terrainObj.transform.SetParent(transform, false);
            terrainObj.layer = LayerMask.NameToLayer(terrainLayerName);
            _meshFilter = terrainObj.AddComponent<MeshFilter>();
            _meshRenderer = terrainObj.AddComponent<MeshRenderer>();
            if (terrainMaterial != null) _meshRenderer.sharedMaterial = terrainMaterial;
            terrainObj.AddComponent<MeshCollider>();
        }
        _meshFilter.sharedMesh = mesh;
        if (_meshFilter.GetComponent<MeshCollider>() != null) _meshFilter.GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    public void BakeRoadMask(List<Vector3[]> roadPolygons)
    {
        GenerateTerrainMesh(); // 废弃挖洞，直接生成
    }
}