using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SimpleCarController))]
public class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed = 15f;
    public float safeDistance = 8f;
    public float lookAheadT = 0.02f;
    
    [Header("=== 交通规则注入 ===")]
    public float rightLaneOffset = 3.5f;

    public enum DriveState { Idle, Following, Avoiding, Stopping, Waiting, RemoteControlled }

    [Header("状态机")]
    public DriveState currentState = DriveState.Idle;

    [Header("调试信息")]
    public float currentT = 0f;
    public bool obstacleDetected = false;
    public int currentLaneId = -1;
    
    public IntersectionState currentIntersectionState = IntersectionState.Uncontrolled; 
    public int currentDestinationNodeId = -1;
    private Vector3 stopTargetPosition = Vector3.zero;
    private bool hasStopTarget = false;

    private int reverseCount = 0;
    private SimpleCarController carController;
    private TrafficManager trafficManager;
    
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

    private float brakeMaxDecel = 8f;
    private float laneSearchTimer = 0f;

    void Start()
    {
        carController = GetComponent<SimpleCarController>();
        if (pathPlanner == null) pathPlanner = FindObjectOfType<PathPlanner>();
        trafficManager = FindObjectOfType<TrafficManager>();
        carController.autoMode = true;
        lastPosition = transform.position;
        laneSearchTimer = Random.Range(0f, 0.2f);
    }

    void Update()
    {
        if (!carController.autoMode && currentState != DriveState.RemoteControlled) return;
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
            case DriveState.RemoteControlled: HandleRemoteControlledState(); break;
        }
    }

    void UpdateSensorData()
    {
        obstacleDetected = false;

        if (trafficManager != null)
        {
            var npcs = trafficManager.ActiveNPCs;
            if (npcs != null)
            {
                Vector3 myPos = transform.position;
                Vector3 myForward = transform.forward;

                for (int i = 0; i < npcs.Count; i++)
                {
                    SimpleAutoDrive other = npcs[i];
                    if (other == this || other == null) continue;

                    Vector3 otherPos = other.transform.position;
                    Vector3 dirToOther = otherPos - myPos;
                    float dist = dirToOther.magnitude;

                    if (dist < safeDistance)
                    {
                        Vector3 dirNorm = dirToOther / dist;
                        float dot = Vector3.Dot(myForward, dirNorm);
                        if (dot > 0.8f)
                        {
                            obstacleDetected = true;
                            break;
                        }
                    }
                }
            }
        }

        if (currentDestinationNodeId >= 0 && WorldModel.Instance != null)
        {
            StopLine relevantStopLine = WorldModel.Instance.GetNearestStopLine(currentDestinationNodeId, transform.position);
            if (relevantStopLine != null)
            {
                currentIntersectionState = WorldModel.Instance.GetPhaseState(relevantStopLine.AssociatedPhaseId);
            }
            else
            {
                currentIntersectionState = WorldModel.Instance.GetIntersectionState(currentDestinationNodeId);
            }
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
            if (currentDestinationNodeId >= 0 && WorldModel.Instance != null)
            {
                StopLine relevantStopLine = WorldModel.Instance.GetNearestStopLine(currentDestinationNodeId, transform.position);
                if (relevantStopLine != null)
                {
                    stopTargetPosition = relevantStopLine.Position;
                    hasStopTarget = true;
                }
            }
            currentState = DriveState.Stopping;
            return;
        }

        if (currentT >= 1.0f)
        {
            RequestNewRandomPath();
            return;
        }

        FollowPath();
    }

    void RequestNewRandomPath()
    {
        if (WorldModel.Instance != null && pathPlanner != null)
        {
            int randTargetId = Random.Range(0, WorldModel.Instance.NodeCount);
            RoadNode targetNode = WorldModel.Instance.GetNode(randTargetId);
            
            if (targetNode != null)
            {
                CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, targetNode.WorldPos);
                if (newSpline != null && newSpline.TotalLength > 0)
                {
                    SetSplinePath(newSpline, targetNode.Id);
                    return;
                }
            }
        }
        currentState = DriveState.Idle;
        currentSpline = null;
        carController.SetAutoControl(0f, 0f);
    }

    public void ResetNavigation()
    {
        RequestNewRandomPath();
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
        if (currentIntersectionState == IntersectionState.GreenLight || currentIntersectionState == IntersectionState.Uncontrolled)
        {
            hasStopTarget = false;
            stuckTimer = 0f; stuckCheckTimer = 0f; lastPosition = transform.position; startupDelay = 2f;
            carController.SetAutoControl(0f, 0f); // 【新增】重置制动
            carController.SetAutoBrake(0f);
            currentState = DriveState.Following;
            return;
        }

        if (!hasStopTarget)
        {
            carController.SetAutoControl(0f, 0f);
            return;
        }

        float distToStop = Vector3.Distance(transform.position, stopTargetPosition);
        float speed = Mathf.Abs(carController.GetSpeed());

        // 【Phase 2核心】：运动学制动公式 v²/2d
        float brakingDecel = distToStop > 0.01f ? (speed * speed) / (2f * distToStop) : brakeMaxDecel;
        brakingDecel = Mathf.Clamp(brakingDecel, 0.5f, brakeMaxDecel);

        if (distToStop < 0.5f)
        {
            carController.SetAutoControl(0f, 0f);
            // 【新增】确保完全刹停
            carController.SetAutoBrake(brakeMaxDecel);
            return;
        }

        Vector3 dirToStop = (stopTargetPosition - transform.position).normalized;
        Vector3 localDir = transform.InverseTransformDirection(dirToStop);
        float steering = Mathf.Clamp(localDir.x * 2f, -1f, 1f);

        carController.SetAutoControl(0f, steering); // 不踩油门
        carController.SetAutoBrake(brakingDecel);   // 直接注入制动减速度
    }

    void HandleWaitingState() => carController.SetAutoControl(0f, 0f);

    void HandleRemoteControlledState()
    {
        // 【Bug2修复】保持语义感知活跃但不输出控制
        // 继续更新 LaneId 和 StopLine 距离供 ROS2 遥测
        if (WorldModel.Instance != null)
        {
            laneSearchTimer += Time.deltaTime;
            if (laneSearchTimer > 0.2f)
            {
                currentLaneId = WorldModel.Instance.FindNearestLane(transform.position);
                laneSearchTimer = 0f;
            }
        }
        if (currentDestinationNodeId >= 0 && WorldModel.Instance != null)
        {
            currentIntersectionState = WorldModel.Instance.GetIntersectionState(currentDestinationNodeId);
        }
        // 不调用 carController.SetAutoControl() — 控制权归 ROS2
    }

    void FollowPath()
    {
        if (currentSpline == null || currentSpline.TotalLength < 0.1f) return;

        float actualSpeed = Mathf.Abs(carController.GetSpeed());
        if (actualSpeed > 0.1f && currentSpline.TotalLength > 0)
        {
            currentT += (actualSpeed * Time.deltaTime) / currentSpline.TotalLength;
            currentT = Mathf.Clamp01(currentT);
        }

        Vector3 posOnSpline = currentSpline.GetPoint(currentT);
        Vector3 lateralTarget = posOnSpline;
        bool hasLaneAnchor = false;

        float targetLookAheadT = Mathf.Min(currentT + lookAheadT, 1f);
        Vector3 lookAheadPos = currentSpline.GetPoint(targetLookAheadT);

        if (WorldModel.Instance != null)
        {
            laneSearchTimer += Time.deltaTime;
            if (laneSearchTimer > 0.2f)
            {
                currentLaneId = WorldModel.Instance.FindNearestLane(transform.position);
                laneSearchTimer = 0f;
            }

            if (currentLaneId >= 0 && WorldModel.Instance.GlobalLanes.TryGetValue(currentLaneId, out Lane lane))
            {
                float bestLaneT = 0f;
                float minDistSqr = float.MaxValue;
                int searchSteps = 20;

                for (int i = 0; i <= searchSteps; i++)
                {
                    float t = i / (float)searchSteps;
                    Vector3 pt = lane.CenterSpline.GetPoint(t);
                    float sqrD = (pt.x - posOnSpline.x) * (pt.x - posOnSpline.x) + (pt.z - posOnSpline.z) * (pt.z - posOnSpline.z);
                    if (sqrD < minDistSqr)
                    {
                        minDistSqr = sqrD;
                        bestLaneT = t;
                    }
                }

                if (minDistSqr < 36f)
                {
                    Vector3 lanePoint = lane.CenterSpline.GetPoint(bestLaneT);
                    lateralTarget.x = lanePoint.x;
                    lateralTarget.z = lanePoint.z;

                    float laneLookT = Mathf.Clamp01(bestLaneT + (lookAheadT * 2f));
                    Vector3 laneLook = lane.CenterSpline.GetPoint(laneLookT);
                    lookAheadPos.x = laneLook.x;
                    lookAheadPos.z = laneLook.z;

                    hasLaneAnchor = true;
                }
            }
        }

        if (!hasLaneAnchor)
        {
            float nextT = Mathf.Min(currentT + 0.001f, 1f);
            Vector3 tangent = (currentSpline.GetPoint(nextT) - posOnSpline).normalized;
            Vector3 rightVector = Vector3.Cross(Vector3.up, tangent).normalized;

            lateralTarget = posOnSpline + rightVector * rightLaneOffset;
            lookAheadPos = lookAheadPos + rightVector * rightLaneOffset;
        }

        lookAheadPos.y = transform.position.y;
        Vector3 localTarget = transform.InverseTransformPoint(lookAheadPos);

        float angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
        float steering = Mathf.Clamp(angle / 45f, -1f, 1f);

        float speedFactor = 1f;
        if (Mathf.Abs(angle) > 25f) speedFactor = 0.4f;

        if (currentIntersectionState == IntersectionState.RedLight) speedFactor = 0f;

        float throttle = (targetSpeed * speedFactor) / carController.maxSpeed;
        carController.SetAutoControl(throttle, steering);
        carController.SetAutoBrake(0f);
    }

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
        
        CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, target);
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