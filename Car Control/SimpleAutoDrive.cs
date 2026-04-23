using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SimpleCarController))]
[RequireComponent(typeof(RaycastSensor))]
public class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed = 15f;
    public float waypointReachThreshold = 5f;
    public float safeDistance = 8f;
    public int lookAheadStep = 1;
    public float trafficLightDistance = 15f;
    [Header("=== 交通规则注入 ===")]
    [Tooltip("靠右行驶的偏移距离")]
    public float rightLaneOffset = 3.5f;
    public float lookAheadDistance = 12f;
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
    private float stuckTimer = 0.3f;
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
            case DriveState.Idle: HandleIdleState(); break;
            case DriveState.Following: HandleFollowingState(); break;
            case DriveState.Avoiding: HandleAvoidingState(); break;
            case DriveState.Stopping: HandleStoppingState(); break;
            case DriveState.Waiting: HandleWaitingState(); break;
        }
    }

    void UpdateSensorData()
    {
        obstacleDetected = sensor.HasFrontObstacle(safeDistance);
        sensor.DetectTrafficLight(out trafficLightState, trafficLightDistance);

        if (path != null && currentWaypointIndex < path.Count)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatWP = new Vector3(path[currentWaypointIndex].x, 0, path[currentWaypointIndex].z);
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
        if (path != null && path.Count > 0)
        {
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

        // 常规到达判定
        if (distanceToNextWaypoint < waypointReachThreshold)
        {
            currentWaypointIndex++;
            stuckTimer = 0f;

            if (currentWaypointIndex >= path.Count)
            {
                currentState = DriveState.Idle;
                path = null;
                if (pathPlanner != null && pathPlanner.currentPath != null) pathPlanner.currentPath.Clear();
                carController.SetAutoControl(0f, 0f);
                Debug.Log("✅ 到达目的地");
                return;
            }
        }
        else if (path != null && currentWaypointIndex < path.Count)
        {
            // 【核心修复】防原地画圈 / 切弯死角
            // 计算车头朝向与目标点的点积，如果 < 0.1f 说明点已经在侧方或后方
            Vector3 toWaypoint = path[currentWaypointIndex] - transform.position;
            toWaypoint.y = 0;

            // 且距离不大于 15m (防止把很远的大弯也误判跳过)
            if (Vector3.Dot(transform.forward, toWaypoint.normalized) < 0.1f && distanceToNextWaypoint < 15f)
            {
                currentWaypointIndex++;
                stuckTimer = 0f;
                Debug.Log("⏩ 节点已被甩在身后，自动跳过防止原地画圈");

                // 边界安全检查
                if (currentWaypointIndex >= path.Count)
                {
                    currentState = DriveState.Idle;
                    path = null;
                    carController.SetAutoControl(0f, 0f);
                    return;
                }
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
            carController.SetAutoControl(-0.4f, escapeSteering);

            // 【延长倒车时间从 1.5 秒改为 2.0 秒，拉开安全距离】
            if (reverseTimer >= 1.2f)
            {
                isReversing = false;
                reverseTimer = 0f;
                avoidCooldown = 1.5f;
                startupDelay = 0.8f;
                carController.SetAutoControl(0f, 0f);
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

    // 1. 确定当前路段的参考方向 (极其重要：用于计算稳定的右向量)
    Vector3 segmentDir;
    if (currentWaypointIndex + 1 < path.Count)
        segmentDir = (path[currentWaypointIndex + 1] - path[currentWaypointIndex]).normalized;
    else
        segmentDir = (path[currentWaypointIndex] - transform.position).normalized;

    // 2. 计算稳定的右侧偏移向量
    Vector3 rightVector = Vector3.Cross(Vector3.up, segmentDir).normalized;
    
    // 3. 获取带偏移的目标点
    Vector3 targetPos = path[currentWaypointIndex] + rightVector * rightLaneOffset;

    // 4. 连续弯道稳定性优化：多点预瞄插值
    // 如果距离当前点很近，提前向下一个偏移点过渡，防止弯道突变
    if (Vector3.Distance(transform.position, targetPos) < lookAheadDistance && currentWaypointIndex + 1 < path.Count)
    {
        Vector3 nextBase = path[currentWaypointIndex + 1];
        Vector3 nextDir = (currentWaypointIndex + 2 < path.Count) ? 
            (path[currentWaypointIndex + 2] - nextBase).normalized : segmentDir;
        Vector3 nextRight = Vector3.Cross(Vector3.up, nextDir).normalized;
        Vector3 nextTarget = nextBase + nextRight * rightLaneOffset;

        // 根据距离平滑切换目标
        float t = 1f - (Vector3.Distance(transform.position, targetPos) / lookAheadDistance);
        targetPos = Vector3.Lerp(targetPos, nextTarget, t);
    }

    // 5. 核心：增加“车道保持”偏航修正
    // 如果车辆因为惯性已经偏离中心线太远，除了追逐目标点，再额外增加一个回归力
    targetPos.y = transform.position.y;
    Vector3 localTarget = transform.InverseTransformPoint(targetPos);
    
    // 6. 计算转向控制 (增加 damping 比例，数值越大转向越柔和)
    float angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
    float steering = Mathf.Clamp(angle / 45f, -1f, 1f); 

    // 7. 速度与障碍物逻辑
    float speedFactor = 1f;
    // 弯道大幅减速：偏移越大，离心力影响越大，必须降速压住车头
    if (Mathf.Abs(angle) > 25f) speedFactor = 0.4f; 
    
    if (trafficLightState == "Red" || obstacleDetected) speedFactor = 0f;

    // 8. 提交控制
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