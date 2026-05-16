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
    private struct RoadSegment { public Vector3 Start; public Vector3 End; public float Width; }
    private RoadSegment[] _fastRoadSegmentsCache;

    private struct IntersectionData { public Vector3 Center; public float Radius; public float TargetY; }
    private IntersectionData[] _fastIntersectionsCache;

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
        Vector2 p = new Vector2(worldX, worldZ);

        if (_fastRoadSegmentsCache != null && _fastRoadSegmentsCache.Length > 0)
        {
            float minSqrDist = float.MaxValue;
            float bestTargetY = noiseY;
            float bestWidth = _cachedRoadWidth;

            foreach (var seg in _fastRoadSegmentsCache)
            {
                float sqrD = DistToSegmentSqr(p, new Vector2(seg.Start.x, seg.Start.z), new Vector2(seg.End.x, seg.End.z), out float segT);
                if (sqrD < minSqrDist)
                {
                    minSqrDist = sqrD;
                    bestTargetY = Mathf.Lerp(seg.Start.y, seg.End.y, segT);
                    bestWidth = seg.Width;
                }
            }

            float d = Mathf.Sqrt(minSqrDist);
            float r = bestWidth * 2.5f;

            float t = Mathf.Exp(-(d * d) / (r * r));
            blendedY = Mathf.Lerp(noiseY, bestTargetY, t);
        }

        if (_fastIntersectionsCache != null && _fastIntersectionsCache.Length > 0)
        {
            float finalY = blendedY;
            float maxWeight = 0f;
            foreach (var inter in _fastIntersectionsCache)
            {
                float sqrD = Vector2.SqrMagnitude(p - new Vector2(inter.Center.x, inter.Center.z));
                float influenceRadius = inter.Radius * 3.0f;

                if (sqrD < influenceRadius * influenceRadius)
                {
                    float d = Mathf.Sqrt(sqrD);
                    float t = Mathf.Exp(-(d * d) / (inter.Radius * inter.Radius));
                    if (t > maxWeight)
                    {
                        maxWeight = t;
                        finalY = Mathf.Lerp(blendedY, inter.TargetY, t);
                    }
                }
            }
            blendedY = finalY;
        }

        return blendedY + terrainHeightOffset;
    }

    public float SampleHeightRawOnly(float worldX, float worldZ)
    {
        RoadNetworkGenerator roadGen = FindObjectOfType<RoadNetworkGenerator>();
        bool isCountry = (roadGen != null && roadGen.isCountryside);

        float noiseY = 0f;
        if (isCountry)
        {
            float seedOffset = roadGen.seed * 1000f;
            noiseY = Mathf.PerlinNoise((worldX + seedOffset) * noiseFrequency, (worldZ + seedOffset) * noiseFrequency) * roadGen.countrysideHeightScale;
        }
        return noiseY + terrainHeightOffset;
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
        WorldModel wm = WorldModel.Instance;
        List<RoadSegment> segments = new List<RoadSegment>();
        
        // 【核心修复 1】：将边按照 2D 样条线细分成小段，让山谷完全跟随道路拐弯！
        if (roadGen != null && roadGen.edges != null)
        {
            float seedOff = roadGen.seed * 1000f;
            float scale = roadGen.isCountryside ? roadGen.countrysideHeightScale : 0f;
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
                if (dist <= 0.1f) continue;
                Vector3 dir = (p2 - p1) / dist;

                float startRadius = 0f, endRadius = 0f;
                if (wm != null)
                {
                    RoadNode rnA = wm.GetNode(edge.Item1);
                    RoadNode rnB = wm.GetNode(edge.Item2);
                    if (rnA != null && rnA.NeighborIds != null && rnA.NeighborIds.Count > 2)
                        startRadius = rnA.IntersectionRadius;
                    if (rnB != null && rnB.NeighborIds != null && rnB.NeighborIds.Count > 2)
                        endRadius = rnB.IntersectionRadius;
                }

                float effectiveDist = dist - startRadius - endRadius;
                if (effectiveDist <= 0.2f) continue;

                Vector3 tp1 = p1 + dir * startRadius;
                Vector3 tp2 = p2 - dir * endRadius;
                tp1.y = Mathf.Lerp(p1.y, p2.y, startRadius / dist);
                tp2.y = Mathf.Lerp(p1.y, p2.y, (dist - endRadius) / dist);

                float tDist = Vector3.Distance(tp1, tp2);
                Vector3 m1 = nodeTangents[n1.id] * tDist * tangLen;
                Vector3 m2 = nodeTangents[n2.id] * tDist * tangLen;

                int steps = 6;
                Vector3 prevP = tp1;
                for(int i = 1; i <= steps; i++) {
                    float t = i / (float)steps;
                    float t2 = t * t, t3 = t2 * t;
                    Vector3 pos = (2*t3 - 3*t2 + 1)*tp1 + (t3 - 2*t2 + t)*m1 + (-2*t3 + 3*t2)*tp2 + (t3 - t2)*m2;
                    pos.y = Mathf.Lerp(tp1.y, tp2.y, t);
                    float segWidth = _cachedRoadWidth;
                    if (roadGen != null && roadGen.isCountryside)
                    {
                        Vector3 midPt = (prevP + pos) * 0.5f;
                        segWidth = RoadMathUtility.GetRoadWidthAtPosition(midPt, _cachedRoadWidth, true);
                    }
                    segments.Add(new RoadSegment { Start = prevP, End = pos, Width = segWidth });
                    prevP = pos;
                }
            }
        }
        _fastRoadSegmentsCache = segments.ToArray();

        List<IntersectionData> intersections = new List<IntersectionData>();
        if (wm != null && wm.Nodes != null)
        {
            float seedOff = roadGen != null ? roadGen.seed * 1000f : 0f;
            float iScale = (roadGen != null && roadGen.isCountryside) ? roadGen.countrysideHeightScale : 0f;

            foreach (var node in wm.Nodes)
            {
                if (node.NeighborIds != null && node.NeighborIds.Count > 2)
                {
                    float nodeY = Mathf.PerlinNoise((node.WorldPos.x + seedOff) * noiseFrequency, (node.WorldPos.z + seedOff) * noiseFrequency) * iScale;
                    float radius = node.IntersectionRadius > 0 ? node.IntersectionRadius : _cachedRoadWidth * 1.2f;

                    intersections.Add(new IntersectionData
                    {
                        Center = node.WorldPos,
                        Radius = radius,
                        TargetY = nodeY
                    });
                }
            }
        }
        _fastIntersectionsCache = intersections.ToArray();

        _heightMap = new float[_dimX, _dimZ];
        for (int i = 0; i < _dimX; i++)
            for (int j = 0; j < _dimZ; j++)
                _heightMap[i, j] = SampleHeightRaw(_minX + i * cellSize, _minZ + j * cellSize);
        
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
        if (roadPolygons == null || roadPolygons.Count == 0 || _roadMask == null || _heightMap == null) return;

        int cellCountX = _dimX - 1;
        int cellCountZ = _dimZ - 1;

        for (int i = 0; i < cellCountX; i++)
        {
            for (int j = 0; j < cellCountZ; j++)
            {
                if (_roadMask[i, j]) continue;

                float cx = _minX + (i + 0.5f) * cellSize;
                float cz = _minZ + (j + 0.5f) * cellSize;
                Vector2 cellCenter = new Vector2(cx, cz);

                foreach (var poly in roadPolygons)
                {
                    if (poly.Length < 3) continue;
                    if (PointInPolygonXZ(cellCenter, poly))
                    {
                        _roadMask[i, j] = true;
                        break;
                    }
                }
            }
        }

        GenerateTerrainMesh();
        Debug.Log($"[TerrainGrid] BakeRoadMask完成: 移除 {CountMaskedCells()} 个道路覆盖地形单元");
    }

    private bool PointInPolygonXZ(Vector2 point, Vector3[] poly)
    {
        bool inside = false;
        int n = poly.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = new Vector2(poly[i].x, poly[i].z);
            Vector2 pj = new Vector2(poly[j].x, poly[j].z);
            if ((pi.y > point.y) != (pj.y > point.y) &&
                point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private int CountMaskedCells()
    {
        int count = 0;
        int cellCountX = _dimX - 1;
        int cellCountZ = _dimZ - 1;
        for (int i = 0; i < cellCountX; i++)
            for (int j = 0; j < cellCountZ; j++)
                if (_roadMask[i, j]) count++;
        return count;
    }
}