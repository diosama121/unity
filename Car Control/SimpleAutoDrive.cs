using UnityEngine;
using System.Collections.Generic;
// using AutonomousSim.Navigation; // 如果 a2 的真实类在这个命名空间下，请取消此行注释

[RequireComponent(typeof(SimpleCarController))]
[RequireComponent(typeof(RaycastSensor))] 
public class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed = 15f;
    public float safeDistance = 8f;
    [Tooltip("基于样条总长度的预瞄比例 (0.0 ~ 1.0)，数值越小反应越慢但越平滑")]
    public float lookAheadT = 0.02f;
    
    [Header("=== 交通规则注入 ===")]
    public float rightLaneOffset = 3.5f;

    public enum DriveState { Idle, Following, Avoiding, Stopping, Waiting }

    [Header("状态机")]
    public DriveState currentState = DriveState.Idle;

    [Header("调试信息")]
    public float currentT = 0f;
    public bool obstacleDetected = false;
    
    // 严格对齐白皮书
    public IntersectionState currentIntersectionState = IntersectionState.Uncontrolled; 
    private int currentDestinationNodeId = -1;

    private int reverseCount = 0;
    private SimpleCarController carController;
    private RaycastSensor sensor;
    
    // 【V2.0 核心】直接持有 a2 下发的样条对象
    private CatmullRomSpline currentSpline;
    
    private Vector3 finalDestination = Vector3.zero;

    private float avoidCooldown = 0f;
    private float startupDelay = 0f;
    private bool isReversing = false;
    private float reverseTimer = 0f;

    private float stuckTimer = 0.3f;
    private Vector3 lastPosition = Vector3.zero;
    private float stuckCheckInterval = 0.5f;
    private float stuckCheckTimer = 0f;
    private float escapeSteering = 0f;

    void Start()
    {
        carController = GetComponent<SimpleCarController>();
        sensor = GetComponent<RaycastSensor>();
        if (pathPlanner == null) pathPlanner = FindObjectOfType<PathPlanner>();
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

        // 白皮书接口查询红绿灯 (直接命中 a1 的真身)
        if (currentDestinationNodeId >= 0 && WorldModel.Instance != null)
        {
            currentIntersectionState = WorldModel.Instance.GetIntersectionState(currentDestinationNodeId);
        }
        else
        {
            currentIntersectionState = IntersectionState.Uncontrolled;
        }
    }

    void UpdateStuckDetection()
    {
        if (currentState != DriveState.Following)
        {
            stuckTimer = 0f; stuckCheckTimer = 0f; startupDelay = 0f; lastPosition = transform.position; return;
        }
        if (startupDelay > 0f)
        {
            startupDelay -= Time.deltaTime;
            stuckTimer = 0f; stuckCheckTimer = 0f; lastPosition = transform.position; return;
        }

        stuckCheckTimer += Time.deltaTime;
        if (stuckCheckTimer < stuckCheckInterval) return;
        stuckCheckTimer = 0f;

        float moved = Vector3.Distance(transform.position, lastPosition);
        lastPosition = transform.position;

        if (moved < 0.3f)
        {
            stuckTimer += stuckCheckInterval;
            if (stuckTimer > 4f)
            {
                stuckTimer = 0f; isReversing = true; reverseTimer = 0f;
                
                if (currentSpline != null)
                {
                    Vector3 tangent = currentSpline.GetPoint(Mathf.Min(currentT + 0.01f, 1f)) - currentSpline.GetPoint(currentT);
                    Vector3 localTangent = transform.InverseTransformDirection(tangent);
                    escapeSteering = localTangent.x > 0 ? 1f : -1f;
                }
                else escapeSteering = 0f;

                currentState = DriveState.Avoiding;
            }
        }
        else stuckTimer = 0f;
    }

    void HandleIdleState()
    {
        carController.SetAutoControl(0f, 0f);
        if (currentSpline != null)
        {
            currentT = 0f;
            currentState = DriveState.Following;
        }
    }

    void HandleFollowingState()
    {
        if (currentIntersectionState == IntersectionState.RedLight)
        {
            currentState = DriveState.Stopping;
            return;
        }

        float frontDist = sensor.GetFrontDistance();
        if (frontDist > 0 && frontDist < safeDistance * 0.5f && avoidCooldown <= 0f)
        {
            isReversing = false; reverseTimer = 0f;
            currentState = DriveState.Avoiding;
            return;
        }

        if (currentT >= 1.0f)
        {
            currentState = DriveState.Idle;
            currentSpline = null;
            carController.SetAutoControl(0f, 0f);
            return;
        }

        FollowPath();
    }

    void HandleAvoidingState()
    {
        if (isReversing)
        {
            reverseTimer += Time.deltaTime;
            carController.SetAutoControl(-0.4f, escapeSteering);
            if (reverseTimer >= 1.2f + reverseCount * 0.5f)
            {
                reverseCount++; isReversing = false; reverseTimer = 0f;
                avoidCooldown = 1.5f; startupDelay = 0.8f;
                carController.SetAutoControl(0f, 0f);
                
                currentT = Mathf.Max(0, currentT - 0.05f); 
                
                if (reverseCount >= 3)
                {
                    reverseCount = 0;
                    currentT = 1f; 
                }
                RerouteToDestination();
                currentState = DriveState.Following;
            }
            return;
        }
        if (obstacleDetected) { isReversing = true; reverseTimer = 0f; return; }

        isReversing = false; reverseTimer = 0f; avoidCooldown = 1f;
        currentState = DriveState.Following;
    }

    void HandleStoppingState()
    {
        carController.SetAutoControl(0f, 0f);
        if (currentIntersectionState == IntersectionState.GreenLight || currentIntersectionState == IntersectionState.Uncontrolled)
        {
            stuckTimer = 0f; stuckCheckTimer = 0f; lastPosition = transform.position; startupDelay = 2f;
            currentState = DriveState.Following;
        }
    }

    void HandleWaitingState() => carController.SetAutoControl(0f, 0f);

    void FollowPath()
    {
        if (currentSpline == null || currentSpline.TotalLength < 0.1f) return;

        // 1. 基于车辆实际速度，按样条总长度推进连续 t 值
        float actualSpeed = Mathf.Abs(carController.GetSpeed());
        if (actualSpeed > 0.1f && currentSpline.TotalLength > 0)
        {
            currentT += (actualSpeed * Time.deltaTime) / currentSpline.TotalLength;
            currentT = Mathf.Clamp01(currentT);
        }

        // 2. 获取当前点与切线方向
        Vector3 posOnSpline = currentSpline.GetPoint(currentT);
        float nextT = Mathf.Min(currentT + 0.001f, 1f);
        Vector3 tangent = (currentSpline.GetPoint(nextT) - posOnSpline).normalized;
        
        Vector3 rightVector = Vector3.Cross(Vector3.up, tangent).normalized;
        Vector3 offsetTargetPos = posOnSpline + rightVector * rightLaneOffset;

        // 3. 严格调用白皮书获取真实高程
        if (WorldModel.Instance != null)
        {
            offsetTargetPos.y = WorldModel.Instance.GetTerrainHeight(new Vector2(offsetTargetPos.x, offsetTargetPos.z));
        }

        // 4. 获取前方预瞄点
        float targetLookAheadT = Mathf.Min(currentT + lookAheadT, 1f);
        Vector3 lookAheadPos = currentSpline.GetPoint(targetLookAheadT) + rightVector * rightLaneOffset;
        
        if (WorldModel.Instance != null)
        {
            lookAheadPos.y = WorldModel.Instance.GetTerrainHeight(new Vector2(lookAheadPos.x, lookAheadPos.z));
        }

        // 5. 将预瞄目标作为追踪点计算偏航角
        lookAheadPos.y = transform.position.y; 
        Vector3 localTarget = transform.InverseTransformPoint(lookAheadPos);

        float angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
        float steering = Mathf.Clamp(angle / 45f, -1f, 1f);

        // 6. 速度与红绿灯逻辑
        float speedFactor = 1f;
        if (Mathf.Abs(angle) > 25f) speedFactor = 0.4f;

        if (currentIntersectionState == IntersectionState.RedLight) speedFactor = 0f;
        else
        {
            float frontDist = sensor.GetFrontDistance();
            if (frontDist > 0 && frontDist < safeDistance) speedFactor *= Mathf.Clamp01((frontDist - 2f) / safeDistance);
        }

        float throttle = (targetSpeed * speedFactor) / carController.maxSpeed;
        carController.SetAutoControl(throttle, steering);
    }

    // ======================================================================
    // 【V2.0 标准入口】直接消费 a2 下发的 CatmullRomSpline
    // ======================================================================
    public void SetSplinePath(CatmullRomSpline spline, int destinationNodeId)
    {
        this.currentSpline = spline;
        this.currentDestinationNodeId = destinationNodeId; 
        this.currentT = 0f;
        this.currentState = DriveState.Following;
    }

    void RerouteToDestination()
    {
        if (pathPlanner == null) return;
        Vector3 target = finalDestination != Vector3.zero ? finalDestination : transform.position + transform.forward * 20f;
        
        // 直接接收 a2 真实的样条对象
        CatmullRomSpline newSpline = pathPlanner.PlanPath(transform.position, target);
        if (newSpline != null && newSpline.TotalLength > 0)
        {
            currentSpline = newSpline;
            currentT = 0f;
        }
    }

    public void SetDestination(Vector3 destination)
    {
        if (pathPlanner == null) { Debug.LogError("未找到PathPlanner！"); return; }
        finalDestination = destination;
        RerouteToDestination();
    }

    public void ToggleAutoDrive()
    {
        carController.autoMode = !carController.autoMode;
        if (!carController.autoMode) currentState = DriveState.Idle;
    }

    public DriveState GetCurrentState() => currentState;

    void OnDrawGizmos()
    {
        if (currentSpline != null && currentT < 1f)
        {
            Gizmos.color = Color.yellow;
            Vector3 drawPoint = currentSpline.GetPoint(currentT);
            Gizmos.DrawWireSphere(drawPoint, 2f);
        }
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, safeDistance);
    }
}
