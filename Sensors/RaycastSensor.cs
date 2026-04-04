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
    public int rayCount = 5;
    
    [Tooltip("多射线传感器：扫描角度范围")]
    public float scanAngle = 90f;

    [Header("可视化")]
    [Tooltip("是否显示射线（调试用）")]
    public bool showRays = true;

    [Header("检测结果")]
    public float frontObstacleDistance = -1f;  // -1 表示未检测到
    public float leftObstacleDistance = -1f;
    public float rightObstacleDistance = -1f;
    
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
    void DetectFrontObstacle()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;  // 从车辆中心发射
        Vector3 direction = transform.forward;

        RaycastHit hit;
        if (Physics.Raycast(origin, direction, out hit, forwardDetectionRange))
        {
            frontObstacleDistance = hit.distance;
            
            if (showRays)
            {
                Debug.DrawLine(origin, hit.point, Color.red);
            }
        }
        else
        {
            frontObstacleDistance = -1f;
            
            if (showRays)
            {
                Debug.DrawRay(origin, direction * forwardDetectionRange, Color.green);
            }
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

    /// <summary>
    /// 检测红绿灯（通过 Tag 识别）
    /// </summary>
    public bool DetectTrafficLight(out string lightState, float maxDistance = 20f)
    {
        lightState = "None";
        
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        RaycastHit hit;

        if (Physics.Raycast(origin, transform.forward, out hit, maxDistance))
        {
            if (hit.collider.CompareTag("TrafficLight"))
            {
                // 假设红绿灯对象有 TrafficLightController 组件
                var trafficLight = hit.collider.GetComponent<TrafficLightController>();
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
