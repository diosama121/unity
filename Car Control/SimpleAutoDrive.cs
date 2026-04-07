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
    // 先检查状态，不是Following直接重置
    if (currentState != DriveState.Following)
    {
        stuckTimer = 0f;
        stuckCheckTimer = 0f;
        startupDelay = 0f; // 非Following状态清零delay
        lastPosition = transform.position;
        return;
    }

    // Following状态下，delay期间不计卡死
    if (startupDelay > 0f)
    {
        startupDelay -= Time.deltaTime;
        stuckTimer = 0f;
        stuckCheckTimer = 0f;
        lastPosition = transform.position;
        return;
    }

    stuckCheckTimer += Time.deltaTime;
    if (stuckCheckTimer < stuckCheckInterval) return;
    stuckCheckTimer = 0f;

    float moved = Vector3.Distance(transform.position, lastPosition);
    lastPosition = transform.position;

    if (moved < 0.3f)
    {
        stuckTimer += stuckCheckInterval;

        if (stuckTimer > 4f && stuckTimer <= 6f)
        {
            Debug.Log("⚠️ 检测到卡死，尝试倒车");
            carController.SetAutoControl(-0.4f, 0f);
        }
        else if (stuckTimer > 6f)
        {
            stuckTimer = 0f;
            avoidCooldown = 2f;
            RerouteToDestination();
            Debug.Log("⚠️ 卡死超时，重新规划路径");
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

        FollowPath();
    }

    void HandleAvoidingState()
    {
        if (isReversing)
        {
            reverseTimer += Time.deltaTime;
            carController.SetAutoControl(-0.4f, 0f);

            if (reverseTimer >= reverseDuration)
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
            stuckTimer = 0f;
            stuckCheckTimer = 0f;
            lastPosition = transform.position;
            startupDelay = 3f;
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

        float speedFactor;
        if (absAngle > 60f)      speedFactor = 0.2f;
        else if (absAngle > 30f) speedFactor = 0.4f;
        else                     speedFactor = 1f;

        if (currentWaypointIndex == path.Count - 1 && distanceToNextWaypoint < 15f)
            speedFactor *= Mathf.Max(distanceToNextWaypoint / 15f, 0.1f);

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