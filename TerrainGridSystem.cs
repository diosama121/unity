// TerrainGridSystem.cs
// ------------------------------------------------------------
// 高稳定版本地表系统（Grid + Road Mask）
// 不依赖 Triangle.NET
// 用于替代“全局剖分地表”方案
// ------------------------------------------------------------
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainGridSystem : MonoBehaviour
{
    [Header("地表尺寸")]
    public int width = 200;
    public int height = 200;
    public float cellSize = 1f;

    [Header("道路掩码（world space polygon）")]
    public List<Vector3> roadPolygon = new List<Vector3>();

    [Header("高度控制")]
    public float heightScale = 2f;
    public float noiseScale = 0.05f;
    public bool useNoise = true;

    [Header("挖空模式")]
    public bool removeRoadArea = true;

    Mesh mesh;
    List<Vector3> vertices;
    List<int> triangles;

    void Start()
    {
        Generate();
    }

    [ContextMenu("Generate Terrain")]
    public void Generate()
    {
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;

        BuildGrid();
        ApplyMesh();
    }

    void BuildGrid()
    {
        vertices = new List<Vector3>();
        triangles = new List<int>();

        for (int z = 0; z <= height; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                float worldX = x * cellSize;
                float worldZ = z * cellSize;

                float y = 0;

                if (useNoise)
                {
                    y = Mathf.PerlinNoise(worldX * noiseScale, worldZ * noiseScale) * heightScale;
                }

                vertices.Add(new Vector3(worldX, y, worldZ));
            }
        }

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * (width + 1) + x;

                Vector3 v0 = vertices[i];
                Vector3 v1 = vertices[i + 1];
                Vector3 v2 = vertices[i + width + 1];
                Vector3 v3 = vertices[i + width + 2];

                Vector3 center = (v0 + v1 + v2 + v3) * 0.25f;

                if (removeRoadArea && IsInsideRoad(center))
                    continue;

                triangles.Add(i);
                triangles.Add(i + width + 1);
                triangles.Add(i + 1);
}}}}