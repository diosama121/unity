using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 行人系统 —— 仅在城市模式下随机生成简单漫游行人Cube
/// 挂载位置：WorldModel 或场景主控物体
/// </summary>
public class PedestrianSpawner : MonoBehaviour
{
    [Header("=== 生成配置 ===")]
    [Tooltip("生成间隔（秒）")]
    public float spawnInterval = 3f;

    [Tooltip("每次生成尝试的概率 (0-1)")]
    [Range(0f, 1f)] 
    public float spawnChance = 0.2f;

    [Tooltip("同时存在的最大行人数量")]
    public int maxPedestrians = 15;

    [Tooltip("行人Cube尺寸")]
    public Vector3 pedestrianSize = new Vector3(0.3f, 1.7f, 0.3f);

    [Header("=== 漫游配置 ===")]
    [Tooltip("漫游速度")]
    public float walkSpeed = 1.2f;

    [Tooltip("改变方向间隔（秒）")]
    public float directionChangeInterval = 3f;

    [Tooltip("漫游范围（距生成点最大距离）")]
    public float roamRadius = 15f;

    [Header("=== 行人材质颜色 ===")]
    public Color[] pedestrianColors = new Color[]
    {
        new Color(0.2f, 0.3f, 0.8f),   // 蓝
        new Color(0.8f, 0.2f, 0.2f),   // 红
        new Color(0.2f, 0.7f, 0.3f),   // 绿
        new Color(0.9f, 0.7f, 0.1f),   // 黄
        new Color(0.5f, 0.2f, 0.7f),   // 紫
        new Color(0.1f, 0.5f, 0.5f),   // 青
        new Color(0.95f, 0.5f, 0.2f),  // 橙
        new Color(0.8f, 0.8f, 0.8f),   // 浅灰
    };

    // 行人数据结构
    private class Pedestrian
    {
        public GameObject gameObject;
        public Vector3 spawnOrigin;       // 生成原点
        public Vector3 currentDirection;  // 当前移动方向
        public float directionTimer;      // 方向切换计时器
        public float speed;               // 个体随机速度
    }

    private List<Pedestrian> pedestrians = new List<Pedestrian>();
    private float spawnTimer = 0f;
    private RoadNetworkGenerator roadGen;
    private bool isCityMode = false;

    void Start()
    {
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        if (roadGen == null)
        {
            Debug.LogWarning("[PedestrianSpawner] 未找到RoadNetworkGenerator，行人系统禁用。");
            enabled = false;
            return;
        }

        // 仅在非乡村模式（城市模式）下激活
        isCityMode = !roadGen.isCountryside;
        if (!isCityMode)
        {
            Debug.Log("[PedestrianSpawner] 当前为乡村模式，行人系统不激活。");
            enabled = false;
            return;
        }

        Debug.Log("[PedestrianSpawner] 行人系统已就绪（城市模式），最大" + maxPedestrians + "人。");
    }

    void Update()
    {
        if (!isCityMode) return;

        // 生成计时
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            TrySpawnPedestrian();
        }

        // 更新所有行人漫游
        UpdatePedestrians();
    }

    /// <summary>
    /// 尝试生成一个行人
    /// </summary>
    private void TrySpawnPedestrian()
    {
        // 清理已销毁的行人
        pedestrians.RemoveAll(p => p == null || p.gameObject == null);

        if (pedestrians.Count >= maxPedestrians) return;

        // 按概率决定是否生成
        if (Random.value > spawnChance) return;

        // 获取随机道路节点位置
        Vector3? spawnPos = GetRandomRoadNodePosition();
        if (!spawnPos.HasValue) return;

        Vector3 pos = spawnPos.Value;
        pos.y += pedestrianSize.y * 0.5f; // 抬高到地面上方

        // 创建行人Cube
        GameObject pedObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pedObj.name = "Pedestrian_" + pedestrians.Count;
        pedObj.transform.position = pos;
        pedObj.transform.localScale = pedestrianSize;

        // 随机颜色
        Color pedColor = pedestrianColors[Random.Range(0, pedestrianColors.Length)];
        MeshRenderer mr = pedObj.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.material = new Material(Shader.Find("Standard"));
            mr.material.color = pedColor;
        }

        // 移除碰撞体（不需要物理交互）
        Collider col = pedObj.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        // 创建行人数据
        Pedestrian ped = new Pedestrian
        {
            gameObject = pedObj,
            spawnOrigin = pos,
            currentDirection = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized,
            directionTimer = Random.Range(0f, directionChangeInterval),
            speed = walkSpeed * Random.Range(0.7f, 1.3f)
        };

        pedestrians.Add(ped);
    }

    /// <summary>
    /// 更新所有行人的漫游移动
    /// </summary>
    private void UpdatePedestrians()
    {
        for (int i = pedestrians.Count - 1; i >= 0; i--)
        {
            Pedestrian ped = pedestrians[i];
            if (ped == null || ped.gameObject == null)
            {
                pedestrians.RemoveAt(i);
                continue;
            }

            // 方向切换计时器
            ped.directionTimer -= Time.deltaTime;
            if (ped.directionTimer <= 0f)
            {
                ped.directionTimer = directionChangeInterval * Random.Range(0.7f, 1.5f);
                // 随机新方向，但有偏向回原点的趋势
                Vector3 toOrigin = (ped.spawnOrigin - ped.gameObject.transform.position);
                toOrigin.y = 0f;
                float distToOrigin = toOrigin.magnitude;

                Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;

                // 如果离原点太远，偏向回归方向
                if (distToOrigin > roamRadius * 0.7f)
                {
                    float bias = Mathf.Clamp01(distToOrigin / roamRadius);
                    ped.currentDirection = (toOrigin.normalized * bias + randomDir * (1f - bias)).normalized;
                }
                else
                {
                    ped.currentDirection = randomDir;
                }
            }

            // 移动行人
            Vector3 newPos = ped.gameObject.transform.position + ped.currentDirection * ped.speed * Time.deltaTime;

            // 限制在漫游范围内
            Vector3 toOriginCheck = newPos - ped.spawnOrigin;
            toOriginCheck.y = 0f;
            if (toOriginCheck.magnitude > roamRadius)
            {
                // 超出范围，拉回并反转方向
                newPos = ped.spawnOrigin + toOriginCheck.normalized * roamRadius;
                ped.currentDirection = -toOriginCheck.normalized;
                ped.directionTimer = directionChangeInterval * 0.5f;
            }

            // 保持在地面高度
            if (WorldModel.Instance != null)
            {
                float groundY = WorldModel.Instance.GetUnifiedHeight(newPos.x, newPos.z);
                newPos.y = groundY + pedestrianSize.y * 0.5f;
            }

            ped.gameObject.transform.position = newPos;
        }
    }

    /// <summary>
    /// 获取随机道路节点附近的位置
    /// </summary>
    private Vector3? GetRandomRoadNodePosition()
    {
        if (roadGen == null || roadGen.nodes == null || roadGen.nodes.Count == 0)
            return null;

        var nodes = roadGen.nodes;
        int maxAttempts = 10;
        for (int i = 0; i < maxAttempts; i++)
        {
            int idx = Random.Range(0, nodes.Count);
            var node = nodes[idx];
            if (node == null) continue;

            Vector3 pos = node.position;
            // 在节点附近随机偏移
            pos.x += Random.Range(-4f, 4f);
            pos.z += Random.Range(-4f, 4f);

            if (WorldModel.Instance != null)
            {
                pos.y = WorldModel.Instance.GetUnifiedHeight(pos.x, pos.z);
            }

            return pos;
        }

        return null;
    }

    /// <summary>
    /// 清除所有行人
    /// </summary>
    public void ClearAllPedestrians()
    {
        foreach (var ped in pedestrians)
        {
            if (ped != null && ped.gameObject != null)
            {
                if (Application.isPlaying) Destroy(ped.gameObject);
                else DestroyImmediate(ped.gameObject);
            }
        }
        pedestrians.Clear();
        Debug.Log("[PedestrianSpawner] 所有行人已清除。");
    }

    void OnDestroy()
    {
        ClearAllPedestrians();
    }

    void OnDrawGizmos()
    {
        if (pedestrians == null) return;
        Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.4f);
        foreach (var ped in pedestrians)
        {
            if (ped == null || ped.gameObject == null) continue;
            // 绘制漫游范围
            Gizmos.DrawWireSphere(ped.spawnOrigin, roamRadius);
        }
    }
}