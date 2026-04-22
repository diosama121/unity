using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SimpleCarController))]
[RequireComponent(typeof(RaycastSensor))]
public class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed = 8f;
    public float waypointReachThreshold = 5f;
    public float safeDistance = 8f;
    public int lookAheadStep = 1;
    public float trafficLightDistance = 15f;

    public enum DriveState
    {
        Idle, Following, Avoiding, Stopping, Waiting
    }

    [Header("状态机")]
    public DriveState currentState = DriveState.Idle;

    [Header("调试信息")]
    public int currentWaypointIndex = 0;
    public float distanceToNextWaypoint = 0f;
    public bool obstacleDetected = false;
    public string trafficLightState = "None";

    private SimpleCarController carController;
    private RaycastSensor sensor;
    private List<Vector3> path;
    private Vector3 finalDestination = Vector3.zero;

    // 避障
    private float avoidCooldown = 0f;
    private float startupDelay = 0f;
    private bool isReversing = false;
    private float reverseTimer = 0f;
    private float reverseDuration = 1.5f;

    // 防卡死
    private float stuckTimer = 0f;
    private Vector3 lastPosition = Vector3.zero;
    private float stuckCheckInterval = 0.5f;
    private float stuckCheckTimer = 0f;
// 避障脱困时的转向记录
    private float escapeSteering = 0f;
    void Start()
    {
        carController = GetComponent<SimpleCarController>();
        sensor = GetComponent<RaycastSensor>();
        if (pathPlanner == null)
            pathPlanner = FindObjectOfType<PathPlanner>();
        carController.autoMode = true;
        lastPosition = transform.position;
    }

    void Update()
    {
        if (!carController.autoMode) return;

        if (avoidCooldown > 0f) avoidCooldown -= Time.deltaTime;

        UpdateSensorData();
        UpdateStuckDetection();

        switch (currentState)
        {
            case DriveState.Idle:      HandleIdleState();      break;
            case DriveState.Following: HandleFollowingState(); break;
            case DriveState.Avoiding:  HandleAvoidingState();  break;
            case DriveState.Stopping:  HandleStoppingState();  break;
            case DriveState.Waiting:   HandleWaitingState();   break;
        }
    }

    void UpdateSensorData()
    {
        obstacleDetected = sensor.HasFrontObstacle(safeDistance);
        sensor.DetectTrafficLight(out trafficLightState, trafficLightDistance);

        if (path != null && currentWaypointIndex < path.Count)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatWP  = new Vector3(path[currentWaypointIndex].x, 0, path[currentWaypointIndex].z);
            distanceToNextWaypoint = Vector3.Distance(flatPos, flatWP);
        }
    }

    void UpdateStuckDetection()
    {
        // 先检查状态，非Following状态直接重置
        if (currentState != DriveState.Following)
        {
            stuckTimer = 0f;
            stuckCheckTimer = 0f;
            startupDelay = 0f; 
            lastPosition = transform.position;
            return;
        }

        // Following状态下，delay期间不计卡死（比如刚看完红绿灯起步时）
        if (startupDelay > 0f)
        {
            startupDelay -= Time.deltaTime;
            stuckTimer = 0f;
            stuckCheckTimer = 0f;
            lastPosition = transform.position; // 保证起步瞬间不计算位移差
            return;
        }

        stuckCheckTimer += Time.deltaTime;
        if (stuckCheckTimer < stuckCheckInterval) return;
        stuckCheckTimer = 0f;

        float moved = Vector3.Distance(transform.position, lastPosition);
        lastPosition = transform.position;

        // 如果移动距离过小，判定为卡死
        if (moved < 0.3f)
        {
            stuckTimer += stuckCheckInterval;

            // 【核心修复】将控制权交接给避障状态机，不再在当前状态硬写控制指令
          if (stuckTimer > 4f)
            {
                Debug.Log("⚠️ 检测到物理卡死，进入避障模式执行倒车脱困");
                stuckTimer = 0f;          
                isReversing = true;       
                reverseTimer = 0f;        
                
                // 【核心新增：智能反向打方向盘】
                // 如果我们要去的路点在右侧（往往是右转切弯撞了右侧柱子）
                // 此时倒车向右打方向盘（1f），车屁股向右走，车头就会向左甩开！
                if (path != null && currentWaypointIndex < path.Count) 
                {
                    Vector3 toWaypoint = transform.InverseTransformPoint(path[currentWaypointIndex]);
                    escapeSteering = toWaypoint.x > 0 ? 1f : -1f;
                } 
                else 
                {
                    escapeSteering = 0f;
                }

                currentState = DriveState.Avoiding; 
            }
        }
        else
        {
            stuckTimer = 0f;
        }
    }

    void HandleIdleState()
    {
        carController.SetAutoControl(0f, 0f);
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
        if (trafficLightState == "Red")
        {
            currentState = DriveState.Stopping;
            Debug.Log("检测到红灯，停车");
            return;
        }

        if (obstacleDetected && avoidCooldown <= 0f)
        {
            isReversing = false;
            reverseTimer = 0f;
            currentState = DriveState.Avoiding;
            Debug.Log("检测到障碍物，开始避障");
            return;
        }

        // --- 到达判定 与 防死角逻辑 ---
        if (distanceToNextWaypoint < waypointReachThreshold)
        {
            currentWaypointIndex++;
            stuckTimer = 0f;

            if (currentWaypointIndex >= path.Count)
            {
                currentState = DriveState.Idle;
                path = null;
                pathPlanner.currentPath.Clear();
                carController.SetAutoControl(0f, 0f);
                Debug.Log("✅ 到达目的地");
                return;
            }
        }
        else if (path != null && currentWaypointIndex < path.Count - 1)
        {
            // 【核心修复：防回头切弯】
            // 如果目标点在车身侧面或偏后（比如倒车脱困后），硬转弯极易撞墙。
            // 计算车头朝向与目标点方向的点积，小于 0.2f 说明点在侧面/后面，直接跳过！
            Vector3 toWaypoint = path[currentWaypointIndex] - transform.position;
            toWaypoint.y = 0;
            if (Vector3.Dot(transform.forward, toWaypoint.normalized) < 0.2f) 
            {
                currentWaypointIndex++;
                stuckTimer = 0f;
                Debug.Log("⏩ 目标点在侧/后方，为防止切弯撞墙，已自动跳过");
            }
        }

        FollowPath();
    }

    void HandleAvoidingState()
    {
       if (isReversing)
        {
            reverseTimer += Time.deltaTime;
            
            // 【应用转向，倒车力度加大，使车头甩开角度】
            carController.SetAutoControl(-0.6f, escapeSteering);

            // 【延长倒车时间从 1.5 秒改为 2.0 秒，拉开安全距离】
            if (reverseTimer >= 2.0f)
            {
                isReversing = false;
                reverseTimer = 0f;
                avoidCooldown = 2f;
                startupDelay = 1f;
                RerouteToDestination();
                currentState = DriveState.Following;
                Debug.Log("倒车完成，重新规划路径");
            }
            return;
        }
        if (obstacleDetected)
        {
            isReversing = true;
            reverseTimer = 0f;
            Debug.Log("开始倒车避障");
            return;
        }

        isReversing = false;
        reverseTimer = 0f;
        avoidCooldown = 1f;
        currentState = DriveState.Following;
        Debug.Log("障碍物消失，恢复跟踪");
    }

    void HandleStoppingState()
    {
        carController.SetAutoControl(0f, 0f);
        if (trafficLightState == "Green" || trafficLightState == "None")
        {
            // 【修复】状态切换瞬间强制重置
            stuckTimer = 0f;
            stuckCheckTimer = 0f;
            lastPosition = transform.position; 
            startupDelay = 2f; // 给一个起步缓冲
            currentState = DriveState.Following;
            Debug.Log("绿灯，继续行驶");
        }
    }

    void HandleWaitingState()
    {
        carController.SetAutoControl(0f, 0f);
    }

  void FollowPath()
    {
        if (path == null || currentWaypointIndex >= path.Count) return;

        Vector3 targetWaypoint = path[currentWaypointIndex];
        targetWaypoint.y = transform.position.y;

        Vector3 localTarget = transform.InverseTransformPoint(targetWaypoint);
        float angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
        float absAngle = Mathf.Abs(angle);
        float steering = Mathf.Clamp(angle / 45f, -1f, 1f);

        // 【优化】向心力补偿限速逻辑
        float lookAheadAngle = absAngle;
        
        // 只有当距离下一个路点小于 8 米时，才开始为下个弯道减速
        if (distanceToNextWaypoint < 8f && currentWaypointIndex + lookAheadStep < path.Count)
        {
            Vector3 currentDir = (targetWaypoint - transform.position).normalized;
            Vector3 nextDir = (path[currentWaypointIndex + lookAheadStep] - targetWaypoint).normalized;
            currentDir.y = 0; nextDir.y = 0;
            
            float curveAngle = Vector3.Angle(currentDir, nextDir);
            
            // 距离越近，弯道曲率的权重越大，实现平滑减速过渡
            float weight = 1f - (distanceToNextWaypoint / 8f);
            lookAheadAngle = Mathf.Max(absAngle, curveAngle * weight); 
        }

        // 放宽角度限制，防止在微小弯道（路点随机偏移）频繁急刹车
        float speedFactor = 1f;
        if (lookAheadAngle > 75f)      speedFactor = 0.3f;   // 急弯
        else if (lookAheadAngle > 45f) speedFactor = 0.6f;   // 中弯
        else if (lookAheadAngle > 20f) speedFactor = 0.9f;   // 微弯/修正

        // 终点平滑刹车
        if (currentWaypointIndex == path.Count - 1 && distanceToNextWaypoint < 15f)
            speedFactor *= Mathf.Max(distanceToNextWaypoint / 15f, 0.15f);

        // 输出油门和转向
        float throttle = (targetSpeed * speedFactor) / carController.maxSpeed;
        carController.SetAutoControl(throttle, steering);
    }
        void RerouteToDestination()
    {
        if (pathPlanner == null) return;

        Vector3 target = finalDestination != Vector3.zero ? finalDestination
                       : (path != null && path.Count > 0 ? path[path.Count - 1] : Vector3.zero);

        if (target == Vector3.zero) return;

        List<Vector3> newPath = pathPlanner.PlanPath(transform.position, target);
        if (newPath.Count > 1)
        {
            path = newPath;
            currentWaypointIndex = 0;
            Debug.Log($"重新规划路径成功，{path.Count}个路点");
        }
        else
        {
            Vector3 escape = GetEscapeNode();
            path = pathPlanner.PlanPath(transform.position, escape);
            currentWaypointIndex = 0;
            Debug.Log("规划失败，导航到逃逸点");
        }
    }

    Vector3 GetEscapeNode()
    {
        var roadGen = FindObjectOfType<RoadNetworkGenerator>();
        if (roadGen == null || roadGen.nodes.Count == 0)
            return transform.position - transform.forward * 15f;

        var sorted = new List<RoadNetworkGenerator.WaypointNode>(roadGen.nodes);
        sorted.Sort((a, b) =>
            Vector3.Distance(transform.position, a.position)
            .CompareTo(Vector3.Distance(transform.position, b.position)));

        for (int i = 2; i < Mathf.Min(10, sorted.Count); i++)
        {
            Vector3 dir = (sorted[i].position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, dir);
            if (dot < 0.3f) return sorted[i].position;
        }

        return sorted[Mathf.Min(3, sorted.Count - 1)].position;
    }

    public void SetDestination(Vector3 destination)
    {
        if (pathPlanner == null) { Debug.LogError("未找到PathPlanner！"); return; }
        finalDestination = destination;
        path = pathPlanner.PlanPath(transform.position, destination);
        if (path.Count > 0)
        {
            currentWaypointIndex = 0;
            currentState = DriveState.Following;
            Debug.Log($"路径规划成功，开始导航到 {destination}");
        }
        else Debug.LogError("路径规划失败！");
    }

    public void ToggleAutoDrive()
    {
        carController.autoMode = !carController.autoMode;
        if (!carController.autoMode) currentState = DriveState.Idle;
    }

    public DriveState GetCurrentState() => currentState;

    void OnDrawGizmos()
    {
        if (path != null && path.Count > 0 && currentWaypointIndex < path.Count)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(path[currentWaypointIndex], 2f);
            Gizmos.DrawLine(transform.position, path[currentWaypointIndex]);
        }
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, safeDistance);
    }
}