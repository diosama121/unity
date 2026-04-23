using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 射线传感器
/// 功能：前方障碍物检测、多方向扫描
/// 用途：为自动驾驶提供环境感知数据
/// </summary>
public class RaycastSensor : MonoBehaviour
{
    [Header("传感器配置")]
    [Tooltip("前方检测距离 (米)")]
    public float forwardDetectionRange = 30f;
    
    [Tooltip("侧向检测距离 (米)")]
    public float sideDetectionRange = 10f;
    
    [Tooltip("多射线传感器：射线数量")]
    public int rayCount = 7;
    
    [Tooltip("多射线传感器：扫描角度范围")]
    public float scanAngle = 120f;

    [Header("可视化")]
    [Tooltip("是否显示射线（调试用）")]
    public bool showRays = true;

    [Header("检测结果")]
    public float frontObstacleDistance = -1f;  // -1 表示未检测到
    public float leftObstacleDistance = -1f;
    public float rightObstacleDistance = -1f;
    public LayerMask detectionMask;
    // 多射线检测结果
    public List<RayHitInfo> rayHits = new List<RayHitInfo>();

    [System.Serializable]
    public class RayHitInfo
    {
        public float distance;
        public bool hit;
        public Vector3 hitPoint;
        public string hitObjectName;
    }

    void Update()
    {
        // 执行所有传感器检测
        DetectFrontObstacle();
        DetectSideObstacles();
        PerformMultiRayScan();
    }

    /// <summary>
    /// 检测前方障碍物
    /// </summary>
  /// <summary>
    /// 检测前方障碍物
    /// </summary>
    void DetectFrontObstacle()
    {
        // 【修复1：抬高雷达】将起点从 0.6f 抬高到 1.0f，大致在引擎盖的高度
        Vector3 origin = transform.position + transform.forward * 2.2f + Vector3.up * 1.0f;  
        Vector3 direction = transform.forward;

        RaycastHit hit;
        
        // 【修复2：缩小球体 & 启用层级过滤】
        // 1. 半径从 1.0f 改为 0.4f（直径0.8米，完全足够覆盖车头，且不会扫到地）
        // 2. 加上 detectionMask，如果是纯物理测试，默认传 ~0 (检测所有层)
        int layerMask = detectionMask.value != 0 ? detectionMask.value : ~0;

        if (Physics.SphereCast(origin, 0.4f, direction, out hit, forwardDetectionRange, layerMask))
        {
            // 【修复3】排除自己！确保射线碰到的不是自己的车壳或车轮
            if (hit.collider.transform.root != transform.root)
            {
                // 【终极防误触】：哪怕真的擦到了一点点地面，只要碰撞点的法线是朝上的(平地)，就忽略它！
                if (Vector3.Dot(hit.normal, Vector3.up) > 0.8f) 
                {
                    frontObstacleDistance = -1f; // 忽略平坦的地面
                }
                else
                {
                    frontObstacleDistance = hit.distance;
                    if (showRays) Debug.DrawLine(origin, hit.point, Color.red);
                    return; // 成功检测到真正的垂直障碍物，直接结束
                }
            }
        }
        
        // 没有碰到障碍物，或者只碰到了自己/地面
        frontObstacleDistance = -1f;
        
        if (showRays)
        {
            // 在 Scene 窗口画出安全的绿色探测线
            Debug.DrawRay(origin, direction * forwardDetectionRange, Color.green);
        }
    }
    /// <summary>
    /// 检测侧向障碍物
    /// </summary>
    void DetectSideObstacles()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        
        // 左侧
        Vector3 leftDirection = -transform.right;
        RaycastHit leftHit;
        if (Physics.Raycast(origin, leftDirection, out leftHit, sideDetectionRange))
        {
            leftObstacleDistance = leftHit.distance;
            if (showRays) Debug.DrawLine(origin, leftHit.point, Color.yellow);
        }
        else
        {
            leftObstacleDistance = -1f;
            if (showRays) Debug.DrawRay(origin, leftDirection * sideDetectionRange, Color.cyan);
        }

        // 右侧
        Vector3 rightDirection = transform.right;
        RaycastHit rightHit;
        if (Physics.Raycast(origin, rightDirection, out rightHit, sideDetectionRange))
        {
            rightObstacleDistance = rightHit.distance;
            if (showRays) Debug.DrawLine(origin, rightHit.point, Color.yellow);
        }
        else
        {
            rightObstacleDistance = -1f;
            if (showRays) Debug.DrawRay(origin, rightDirection * sideDetectionRange, Color.cyan);
        }
    }

    /// <summary>
    /// 多射线扫描（类似激光雷达）
    /// </summary>
    void PerformMultiRayScan()
    {
        rayHits.Clear();
        
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        float angleStep = scanAngle / (rayCount - 1);
        float startAngle = -scanAngle / 2f;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = startAngle + (angleStep * i);
            Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward;

            RaycastHit hit;
            RayHitInfo hitInfo = new RayHitInfo();

            if (Physics.Raycast(origin, direction, out hit, forwardDetectionRange))
            {
                if (hit.collider.transform.root == transform.root) continue;
                hitInfo.hit = true;
                hitInfo.distance = hit.distance;
                hitInfo.hitPoint = hit.point;
                hitInfo.hitObjectName = hit.collider.gameObject.name;

                if (showRays)
                {
                    Debug.DrawLine(origin, hit.point, Color.magenta);
                }
            }
            else
            {
                hitInfo.hit = false;
                hitInfo.distance = -1f;

                if (showRays)
                {
                    Debug.DrawRay(origin, direction * forwardDetectionRange, Color.blue);
                }
            }

            rayHits.Add(hitInfo);
        }
    }

    // ========== 公共接口 ==========

    /// <summary>
    /// 是否检测到前方障碍物
    /// </summary>
    public bool HasFrontObstacle(float threshold = 10f)
    {
        return frontObstacleDistance > 0 && frontObstacleDistance < threshold;
    }

    /// <summary>
    /// 获取前方最近障碍物距离
    /// </summary>
    public float GetFrontDistance()
    {
        return frontObstacleDistance;
    }

    /// <summary>
    /// 获取所有射线检测结果
    /// </summary>
    public List<RayHitInfo> GetRayHits()
    {
        return rayHits;
    }


   public bool DetectTrafficLight(out string lightState, float maxDistance = 20f)
{
    lightState = "None";
    Vector3 origin = transform.position + Vector3.up * 1f;

    // 扇形5条射线，覆盖前方左右各30度
    float[] angles = { -30f, -15f, 0f, 15f, 30f };

    foreach (float angle in angles)
    {
        Vector3 dir = Quaternion.Euler(0, angle, 0) * transform.forward;
        RaycastHit hit;

        if (showRays)
            Debug.DrawRay(origin, dir * maxDistance, Color.magenta);

        if (Physics.Raycast(origin, dir, out hit, maxDistance))
        {
            // 先检查自身及父物体
            var trafficLight = hit.collider.GetComponent<TrafficLightController>();
            if (trafficLight == null)
                trafficLight = hit.collider.GetComponentInParent<TrafficLightController>();

            if (trafficLight != null)
            {
                lightState = trafficLight.GetCurrentState();
                return true;
            }
        }
    }

    return false;
}
}
