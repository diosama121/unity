using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 行人系统 —— 城市模式下在人行道/路边生成漫游行人
/// V2.0：修复行人生成在路中间的问题，改为基于车道中心线外扩至人行道位置。
///   1. 从 WorldModel.GlobalLanes 随机选车道
///   2. 取车道中心线上的随机点
///   3. 沿垂直方向偏移到人行道（优先无相邻车道的一侧）
///   4. 行人漫游时优先沿道路方向移动，增加真实感
/// </summary>
public class PedestrianSpawner : MonoBehaviour
{
    [Header("=== 生成配置 ===")]
    [Tooltip("生成间隔（秒）")]
    public float spawnInterval = 3f;

    [Tooltip("每次生成尝试的概率 (0-1)")]
    [Range(0f, 1f)] 
    public float spawnChance = 0.3f;

    [Tooltip("同时存在的最大行人数量")]
    public int maxPedestrians = 20;

    [Tooltip("行人Cube尺寸")]
    public Vector3 pedestrianSize = new Vector3(0.3f, 1.7f, 0.3f);

    [Tooltip("人行道距车道中心的偏移量（米）")]
    public float sidewalkOffset = 4.5f;

    [Header("=== 漫游配置 ===")]
    [Tooltip("漫游速度")]
    public float walkSpeed = 1.2f;

    [Tooltip("改变方向间隔（秒）")]
    public float directionChangeInterval = 3f;

    [Tooltip("漫游范围（距生成点最大距离）")]
    public float roamRadius = 20f;

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
        public Vector3 roadTangent;       // ★ 所在道路的切线方向（沿路漫游偏好）
    }

    private List<Pedestrian> pedestrians = new List<Pedestrian>();
    private float spawnTimer = 0f;
    private RoadNetworkGenerator roadGen;
    private bool isCityMode = false;

    /// <summary>当前活跃行人数量（供数据导出）</summary>
    public int ActivePedestrianCount => pedestrians.Count;

    /// <summary>获取所有活跃行人GameObject（供ROS2上报）</summary>
    public GameObject[] GetActivePedestrians()
    {
        pedestrians.RemoveAll(p => p == null || p.gameObject == null);
        var result = new GameObject[pedestrians.Count];
        for (int i = 0; i < pedestrians.Count; i++)
            result[i] = pedestrians[i].gameObject;
        return result;
    }

    void Start()
    {
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        if (roadGen == null)
        {
            Debug.LogWarning("[PedestrianSpawner] 未找到RoadNetworkGenerator，行人系统禁用。");
            enabled = false;
            return;
        }

        isCityMode = !roadGen.isCountryside;
        if (!isCityMode)
        {
            Debug.Log("[PedestrianSpawner] 当前为乡村模式，行人系统不激活。");
            enabled = false;
            return;
        }

        Debug.Log("[PedestrianSpawner] V2.0 行人系统已就绪（人行道生成），最大" + maxPedestrians + "人。");
    }

    void Update()
    {
        if (!isCityMode) return;

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            TrySpawnPedestrian();
        }

        UpdatePedestrians();
    }

    /// <summary>
    /// ★ V2.0：从车道中心线偏移到人行道位置生成行人
    /// </summary>
    private void TrySpawnPedestrian()
    {
        pedestrians.RemoveAll(p => p == null || p.gameObject == null);
        if (pedestrians.Count >= maxPedestrians) return;
        if (Random.value > spawnChance) return;

        Vector3? spawnPos = GetSidewalkPosition(out Vector3 roadTangent);
        if (!spawnPos.HasValue) return;

        Vector3 pos = spawnPos.Value;
        pos.y += pedestrianSize.y * 0.5f;

        GameObject pedObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pedObj.name = "Pedestrian_" + pedestrians.Count;
        pedObj.transform.position = pos;
        pedObj.transform.localScale = pedestrianSize;

        Color pedColor = pedestrianColors[Random.Range(0, pedestrianColors.Length)];
        MeshRenderer mr = pedObj.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.material = new Material(Shader.Find("Standard"));
            mr.material.color = pedColor;
        }

        Collider col = pedObj.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        // 初始方向优先沿道路方向
        Vector3 initDir = roadTangent.normalized;
        if (Random.value > 0.7f) initDir = -initDir; // 30%概率反向走

        Pedestrian ped = new Pedestrian
        {
            gameObject = pedObj,
            spawnOrigin = pos,
            currentDirection = initDir,
            directionTimer = Random.Range(0f, directionChangeInterval),
            speed = walkSpeed * Random.Range(0.7f, 1.3f),
            roadTangent = roadTangent
        };

        pedestrians.Add(ped);
    }

    /// <summary>
    /// ★ V2.0：从 WorldModel 车道数据计算人行道位置
    /// 策略：随机选车道 → 取中心线随机点 → 沿垂直方向偏移到人行道
    /// </summary>
    private Vector3? GetSidewalkPosition(out Vector3 roadTangent)
    {
        roadTangent = Vector3.forward; // 默认值
        WorldModel wm = WorldModel.Instance;
        if (wm == null || wm.GlobalLanes == null || wm.GlobalLanes.Count == 0)
            return null;

        // 收集有效车道（长度 > 10m）
        List<Lane> validLanes = new List<Lane>();
        foreach (var kvp in wm.GlobalLanes)
        {
            if (kvp.Value.CenterSpline != null && kvp.Value.CenterSpline.TotalLength > 10f)
                validLanes.Add(kvp.Value);
        }
        if (validLanes.Count == 0) return null;

        // 随机选车道
        Lane lane = validLanes[Random.Range(0, validLanes.Count)];

        // 在车道中心线上取随机位置（避开端点，防路口中央）
        float t = Random.Range(0.15f, 0.85f);
        Vector3 centerPos = lane.CenterSpline.GetPoint(t);

        // 获取车道切线方向
        Vector3 tangent = lane.CenterSpline.GetTangent(t);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.forward;
        roadTangent = tangent.normalized; // ★ 传出给调用方

        // 计算垂直方向（人行道偏移方向）
        Vector3 perpendicular = Vector3.Cross(Vector3.up, tangent).normalized;

        // ★ 判断偏移方向：优先偏移到无相邻车道的一侧
        if (lane.LeftLaneId < 0 && lane.RightLaneId >= 0)
        {
            perpendicular = -perpendicular;
        }
        else if (lane.RightLaneId < 0 && lane.LeftLaneId >= 0)
        {
            // 保持perpendicular
        }
        else if (lane.LeftLaneId >= 0 && lane.RightLaneId >= 0)
        {
            return null;
        }
        else if (Random.value > 0.5f)
        {
            perpendicular = -perpendicular;
        }

        // 偏移到人行道位置
        Vector3 sidewalkPos = centerPos + perpendicular * sidewalkOffset;

        // 贴地
        if (wm != null)
            sidewalkPos.y = wm.GetUnifiedHeight(sidewalkPos.x, sidewalkPos.z);

        return sidewalkPos;
    }

    /// <summary>
    /// 更新所有行人的漫游移动（★ V2.0：优先沿道路方向 + V3.0：车辆避让）
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

            Vector3 pedPos = ped.gameObject.transform.position;

            // ★ V3.0：检测附近车辆，如果有则避让
            Vector3? escapeDir = GetVehicleAvoidanceDirection(pedPos);
            bool isFleeing = escapeDir.HasValue;

            ped.directionTimer -= Time.deltaTime;
            if (ped.directionTimer <= 0f || isFleeing)
            {
                ped.directionTimer = (isFleeing ? 0.3f : directionChangeInterval * Random.Range(0.7f, 1.5f));

                if (isFleeing)
                {
                    // 逃逸模式：远离最近车辆
                    ped.currentDirection = escapeDir.Value;
                }
                else
                {
                    Vector3 toOrigin = (ped.spawnOrigin - pedPos);
                    toOrigin.y = 0f;
                    float distToOrigin = toOrigin.magnitude;

                    Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;

                    if (Random.value < 0.6f && ped.roadTangent.sqrMagnitude > 0.01f)
                    {
                        float sign = Random.value > 0.5f ? 1f : -1f;
                        randomDir = (ped.roadTangent * sign + randomDir * 0.4f).normalized;
                    }

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
            }

            float activeSpeed = isFleeing ? ped.speed * 2.5f : ped.speed;
            Vector3 newPos = pedPos + ped.currentDirection * activeSpeed * Time.deltaTime;

            // 限制在漫游范围内（逃逸时放宽）
            Vector3 toOriginCheck = newPos - ped.spawnOrigin;
            toOriginCheck.y = 0f;
            float maxRange = isFleeing ? roamRadius * 1.5f : roamRadius;
            if (toOriginCheck.magnitude > maxRange)
            {
                newPos = ped.spawnOrigin + toOriginCheck.normalized * maxRange;
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

    /// <summary>
    /// ★ V3.0：检测附近车辆，返回逃逸方向。无车辆返回null。
    /// </summary>
    private Vector3? GetVehicleAvoidanceDirection(Vector3 pedPos)
    {
        float detectionRadius = 5f;
        Vector3 bestEscape = Vector3.zero;
        bool foundVehicle = false;

        foreach (var car in SimpleAutoDrive.AllCars)
        {
            if (car == null) continue;
            Vector3 carPos = car.transform.position;
            carPos.y = pedPos.y;
            float dist = Vector3.Distance(pedPos, carPos);
            if (dist > detectionRadius) continue;

            foundVehicle = true;
            Vector3 away = (pedPos - carPos).normalized;
            away.y = 0f;

            float weight = 1f - (dist / detectionRadius);
            bestEscape += away * weight;
        }

        if (!foundVehicle) return null;
        return bestEscape.normalized;
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
            Gizmos.DrawWireSphere(ped.spawnOrigin, roamRadius);
        }
    }
}