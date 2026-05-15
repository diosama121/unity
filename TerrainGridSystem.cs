using System.Collections.Generic;
using UnityEngine;

public class TerrainGridSystem : MonoBehaviour
{
    public static TerrainGridSystem Instance { get; private set; }

    [Header("网格参数")]
    public float cellSize = 2f; 

    [Header("地形噪声")]
    public float noiseFrequency = 0.05f;
    public float heightScale = 30f; 
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

    private float _cachedRoadWidth = 6f;

    // 缓存三维曲线段 (将边细分为多段以贴合 Spline)
    private struct RoadSegment { public Vector3 Start; public Vector3 End; }
    private RoadSegment[] _fastRoadSegmentsCache;

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

    private float DistToSegmentSqr(Vector2 p, Vector2 v, Vector2 w, out float t)
    {
        float l2 = Vector2.SqrMagnitude(v - w);
        if (l2 == 0f) { t = 0f; return Vector2.SqrMagnitude(p - v); }
        t = Mathf.Max(0, Mathf.Min(1, Vector2.Dot(p - v, w - v) / l2));
        Vector2 projection = v + t * (w - v);
        return Vector2.SqrMagnitude(p - projection);
    }

    public float SampleHeightRaw(float worldX, float worldZ)
    {
        RoadNetworkGenerator roadGen = FindObjectOfType<RoadNetworkGenerator>();
        bool isCountry = (roadGen != null && roadGen.isCountryside);

        float noiseY = 0f;
        if (isCountry)
        {
            float seedOffset = roadGen.seed * 1000f;
            noiseY = Mathf.PerlinNoise((worldX + seedOffset) * noiseFrequency, (worldZ + seedOffset) * noiseFrequency) * roadGen.countrysideHeightScale;
        }

        float blendedY = noiseY;

        if (_fastRoadSegmentsCache != null && _fastRoadSegmentsCache.Length > 0)
        {
            float minSqrDist = float.MaxValue;
            float bestTargetY = noiseY; 
            Vector2 p = new Vector2(worldX, worldZ);
            
            foreach (var seg in _fastRoadSegmentsCache)
            {
                float sqrD = DistToSegmentSqr(p, new Vector2(seg.Start.x, seg.Start.z), new Vector2(seg.End.x, seg.End.z), out float t);
                if (sqrD < minSqrDist)
                {
                    minSqrDist = sqrD;
                    bestTargetY = Mathf.Lerp(seg.Start.y, seg.End.y, t); 
                }
            }
            
            float minDist = Mathf.Sqrt(minSqrDist);
            
            if (isCountry)
            {
                // 【核心修复 2】：扩大路肩缓冲带，留足空间给高山缓坡，防止 90 度悬崖
                float platformRadius = _cachedRoadWidth * 0.8f + 2f; 
                float blendRadius = platformRadius + 20f; // 20米的庞大平滑过渡区
                
                if (minDist <= platformRadius) 
                {
                    blendedY = bestTargetY; 
                } 
                else if (minDist < blendRadius) 
                {
                    float mask = Mathf.SmoothStep(0f, 1f, (minDist - platformRadius) / (blendRadius - platformRadius));
                    blendedY = Mathf.Lerp(bestTargetY, noiseY, mask);
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
        float fx = (worldXZ.x - _minX) / cellSize; float fz = (worldXZ.y - _minZ) / cellSize;
        fx = Mathf.Clamp(fx, 0f, _dimX - 1); fz = Mathf.Clamp(fz, 0f, _dimZ - 1);
        int x0 = Mathf.FloorToInt(fx); int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, _dimX - 1); int z1 = Mathf.Min(z0 + 1, _dimZ - 1);
        float tx = fx - x0; float tz = fz - z0;
        return Mathf.Lerp(Mathf.Lerp(_heightMap[x0, z0], _heightMap[x1, z0], tx), Mathf.Lerp(_heightMap[x0, z1], _heightMap[x1, z1], tx), tz);
    }

    private void GenerateHeightMap(Bounds bounds)
    {
        _minX = bounds.min.x; _minZ = bounds.min.z;
        _dimX = Mathf.CeilToInt(bounds.size.x / cellSize) + 1;
        _dimZ = Mathf.CeilToInt(bounds.size.z / cellSize) + 1;

        ProceduralRoadBuilder builder = FindObjectOfType<ProceduralRoadBuilder>();
        if (builder != null) _cachedRoadWidth = builder.roadWidth;

        RoadNetworkGenerator roadGen = FindObjectOfType<RoadNetworkGenerator>();
        List<RoadSegment> segments = new List<RoadSegment>();
        
        // 【核心修复 1】：将边按照 2D 样条线细分成小段，让山谷完全跟随道路拐弯！
        if (roadGen != null && roadGen.edges != null)
        {
            float seedOff = roadGen.seed * 1000f;
            float scale = roadGen.isCountryside ? roadGen.countrysideHeightScale : heightScale;
            float tangLen = builder != null ? builder.tangentLength : 0.3f;

            // 预计算节点切线
            Dictionary<int, Vector3> nodeTangents = new Dictionary<int, Vector3>();
            foreach(var node in roadGen.nodes) {
                Vector3 avgDir = Vector3.zero;
                if (node.neighbors.Count > 0) {
                    foreach(var nbId in node.neighbors) {
                        var nb = roadGen.nodes.Find(n => n.id == nbId);
                        avgDir += (nb.position - node.position).normalized;
                    }
                    nodeTangents[node.id] = (avgDir / node.neighbors.Count).normalized;
                } else nodeTangents[node.id] = Vector3.forward;
            }

            foreach (var edge in roadGen.edges)
            {
                var n1 = roadGen.nodes.Find(n => n.id == edge.Item1);
                var n2 = roadGen.nodes.Find(n => n.id == edge.Item2);
                Vector3 p1 = n1.position; Vector3 p2 = n2.position;
                p1.y = Mathf.PerlinNoise((p1.x + seedOff) * noiseFrequency, (p1.z + seedOff) * noiseFrequency) * scale;
                p2.y = Mathf.PerlinNoise((p2.x + seedOff) * noiseFrequency, (p2.z + seedOff) * noiseFrequency) * scale;

                float dist = Vector3.Distance(p1, p2);
                Vector3 m1 = nodeTangents[n1.id] * dist * tangLen;
                Vector3 m2 = nodeTangents[n2.id] * dist * tangLen;

                int steps = 6; // 将一条边切成 6 段
                Vector3 prevP = p1;
                for(int i = 1; i <= steps; i++) {
                    float t = i / (float)steps;
                    float t2 = t * t, t3 = t2 * t;
                    Vector3 pos = (2*t3 - 3*t2 + 1)*p1 + (t3 - 2*t2 + t)*m1 + (-2*t3 + 3*t2)*p2 + (t3 - t2)*m2;
                    pos.y = Mathf.Lerp(p1.y, p2.y, t);
                    segments.Add(new RoadSegment { Start = prevP, End = pos });
                    prevP = pos;
                }
            }
        }
        _fastRoadSegmentsCache = segments.ToArray();

        _heightMap = new float[_dimX, _dimZ];
        for (int i = 0; i < _dimX; i++)
            for (int j = 0; j < _dimZ; j++)
                _heightMap[i, j] = SampleHeightRaw(_minX + i * cellSize, _minZ + j * cellSize);
        
        SmoothHeightMap(); // 生成后平滑化
        
        _roadMask = new bool[_dimX - 1, _dimZ - 1]; 
    }

    // 【核心修复 3】：3x3 均值滤波。抹除路口不同坡度交汇时产生的断层与尖刺！
    private void SmoothHeightMap()
    {
        float[,] newMap = new float[_dimX, _dimZ];
        for (int x = 0; x < _dimX; x++) {
            for (int z = 0; z < _dimZ; z++) {
                float sum = 0; int count = 0;
                for (int dx = -1; dx <= 1; dx++) {
                    for (int dz = -1; dz <= 1; dz++) {
                        int nx = x + dx; int nz = z + dz;
                        if (nx >= 0 && nx < _dimX && nz >= 0 && nz < _dimZ) {
                            sum += _heightMap[nx, nz]; count++;
                        }
                    }
                }
                newMap[x, z] = sum / count;
            }
        }
        _heightMap = newMap;
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

    public void BakeRoadMask(List<Vector3[]> roadPolygons) { GenerateTerrainMesh(); }
}