using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 简单自动驾驶控制器
/// 功能：路径跟踪 + 避障 + 红绿灯识别
/// 依赖：SimpleCarController, RaycastSensor, PathPlanner
/// </summary>
[RequireComponent(typeof(SimpleCarController))]
[RequireComponent(typeof(RaycastSensor))]
public class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    [Tooltip("目标速度 (m/s)")]
    public float targetSpeed = 40f;

    [Tooltip("路点到达阈值（米）")]
    public float waypointReachThreshold = 5f;

    [Tooltip("安全距离（米）")]
    public float safeDistance = 10f;

    [Tooltip("红绿灯检测距离（米）")]
    public float trafficLightDistance = 20f;

    // 驾驶状态枚举
    public enum DriveState
    {
        Idle,           // 空闲
        Following,      // 路径跟踪
        Avoiding,       // 避障
        Stopping,       // 停止（红灯/障碍物）
        Waiting         // 等待
    }

    [Header("状态机")]
    public DriveState currentState = DriveState.Idle;

    [Header("调试信息")]
    public int currentWaypointIndex = 0;
    public float distanceToNextWaypoint = 0f;
    public bool obstacleDetected = false;
    public string trafficLightState = "None";

    // 内部组件
    private SimpleCarController carController;
    private RaycastSensor sensor;
    private List<Vector3> path;

    void Start()
    {
        carController = GetComponent<SimpleCarController>();
        sensor = GetComponent<RaycastSensor>();

        if (pathPlanner == null)
        {
            pathPlanner = FindObjectOfType<PathPlanner>();
        }

        // 默认启用自动驾驶模式
        carController.autoMode = true;
    }

    void Update()
    {
        if (!carController.autoMode) return;

        // 更新传感器数据
        UpdateSensorData();

        // 状态机
        switch (currentState)
        {
            case DriveState.Idle:
                HandleIdleState();
                break;

            case DriveState.Following:
                HandleFollowingState();
                break;

            case DriveState.Avoiding:
                HandleAvoidingState();
                break;

            case DriveState.Stopping:
                HandleStoppingState();
                break;

            case DriveState.Waiting:
                HandleWaitingState();
                break;
        }
    }

    // ========== 传感器数据更新 ==========

    void UpdateSensorData()
    {
        // 检测前方障碍物
        obstacleDetected = sensor.HasFrontObstacle(safeDistance);

        // 检测红绿灯
        sensor.DetectTrafficLight(out trafficLightState, trafficLightDistance);

        // 计算到下一个路点的距离
        if (path != null && currentWaypointIndex < path.Count)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatWP = new Vector3(path[currentWaypointIndex].x, 0, path[currentWaypointIndex].z);
            distanceToNextWaypoint = Vector3.Distance(flatPos, flatWP);
        }
    }

    // ========== 状态处理 ==========

    void HandleIdleState()
    {
        carController.SetAutoControl(0f, 0f);

        // 加这个判断，path为null时不重新导航
        if (path != null && path.Count > 0) return;

        if (pathPlanner != null && pathPlanner.GetCurrentPath().Count > 0)
        {
            path = pathPlanner.GetCurrentPath();
            currentWaypointIndex = 0;
            currentState = DriveState.Following;
        }
    }

    void HandleFollowingState()
    {
        // 优先处理红绿灯和障碍物
        if (trafficLightState == "Red")
        {
            currentState = DriveState.Stopping;
            Debug.Log("检测到红灯，停车");
            return;
        }

        if (obstacleDetected)
        {
            currentState = DriveState.Avoiding;
            Debug.Log("检测到障碍物，开始避障");
            return;
        }

        // 检查是否到达路点
        if (distanceToNextWaypoint < waypointReachThreshold)
        {
            currentWaypointIndex++;

            if (currentWaypointIndex >= path.Count)
            {
                currentState = DriveState.Idle;
                path = null;                          // 清空路径
                pathPlanner.currentPath.Clear();      // 清空PathPlanner里的路径
                carController.SetAutoControl(0f, 0f); // 停车
                Debug.Log("✅ 到达目的地，停车");
                return;
            }
        }

        // 路径跟踪控制
        FollowPath();
    }

    void HandleAvoidingState()
    {
        // 简单避障：减速或停止
        if (sensor.GetFrontDistance() < safeDistance / 2f)
        {
            // 距离太近，停车
            carController.SetAutoControl(0f, 0f);
        }
        else
        {
            // 减速通过
            float slowSpeed = targetSpeed * 0.3f;
            carController.SetAutoControl(slowSpeed / carController.maxSpeed, 0f);
        }

        // 障碍物消失后恢复跟踪
        if (!obstacleDetected)
        {
            currentState = DriveState.Following;
            Debug.Log("障碍物已清除，恢复跟踪");
        }
    }

    void HandleStoppingState()
    {
        // 停车
        carController.SetAutoControl(0f, 0f);

        // 绿灯后继续
        if (trafficLightState == "Green" || trafficLightState == "None")
        {
            currentState = DriveState.Following;
            Debug.Log("绿灯，继续行驶");
        }
    }

    void HandleWaitingState()
    {
        // 等待状态（可扩展）
        carController.SetAutoControl(0f, 0f);
    }

    // ========== 路径跟踪算法 ==========

    void FollowPath()
    {
        if (path == null || currentWaypointIndex >= path.Count) return;

        Vector3 targetWaypoint = path[currentWaypointIndex];

        // 计算到目标点的方向
        Vector3 direction = (targetWaypoint - transform.position).normalized;
        direction.y = 0;  // 忽略高度差

        // 计算转向角度
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        float angle = Vector3.SignedAngle(forward, direction, Vector3.up);

        // 归一化转向输入 (-1 到 1)
        float steering = Mathf.Clamp(angle / carController.maxSteeringAngle, -1f, 1f);

        float speedFactor = 1f - Mathf.Abs(steering) * 0.5f;

        // 接近终点时减速
        bool isLastWaypoint = (currentWaypointIndex == path.Count - 1);
        if (isLastWaypoint && distanceToNextWaypoint < 25f)
        {
            // 距终点25米内线性减速
            speedFactor *= distanceToNextWaypoint / 25f;
            speedFactor = Mathf.Max(speedFactor, 0.1f); // 最低保持一点速度防止停死
        }

        float throttle = (targetSpeed * speedFactor) / carController.maxSpeed;
        carController.SetAutoControl(throttle, steering);
    }

    // ========== 公共接口 ==========

    /// <summary>
    /// 设置新的目标位置
    /// </summary>
    public void SetDestination(Vector3 destination)
    {
        if (pathPlanner == null)
        {
            Debug.LogError("未找到 PathPlanner！");
            return;
        }

        // 规划路径
        path = pathPlanner.PlanPath(transform.position, destination);

        if (path.Count > 0)
        {
            currentWaypointIndex = 0;
            currentState = DriveState.Following;
            Debug.Log($"路径规划成功，开始导航到 {destination}");
        }
        else
        {
            Debug.LogError("路径规划失败！");
        }
    }

    /// <summary>
    /// 启动/停止自动驾驶
    /// </summary>
    public void ToggleAutoDrive()
    {
        carController.autoMode = !carController.autoMode;

        if (!carController.autoMode)
        {
            currentState = DriveState.Idle;
        }
    }

    /// <summary>
    /// 获取当前状态
    /// </summary>
    public DriveState GetCurrentState()
    {
        return currentState;
    }

    // ========== 可视化 ==========

    void OnDrawGizmos()
    {
        if (path != null && path.Count > 0)
        {
            // 绘制当前目标路点
            if (currentWaypointIndex < path.Count)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(path[currentWaypointIndex], 3f);
                Gizmos.DrawLine(transform.position, path[currentWaypointIndex]);
            }
        }

        // 显示安全距离范围
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, safeDistance);
    }
}
