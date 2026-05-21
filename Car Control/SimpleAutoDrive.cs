using UnityEngine;

[RequireComponent(typeof(SimpleCarController))]
public partial class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed = 12f;
    public float safeDistance = 8f;
    public float lookAheadT = 0.02f;
    public bool dynamicLookAhead = true;
    public float lookAheadMin = 3f;
    public float lookAheadMax = 12f;

    public float rightLaneOffset = 3.5f;

    [Header("传感器设置")]
    [Tooltip("雷达向车头方向偏移的距离，一般轿车中心到车头大概 2 到 2.5 米")]
    public float sensorForwardOffset = 2.5f;

    public enum DriveState { Idle, Following, Avoiding, Stopping, Waiting, RemoteControlled }

    [Header("状态机")]
    public DriveState currentState = DriveState.Idle;

    [Header("调试信息")]
    public float currentT = 0f;
    public bool obstacleDetected = false;
    public int currentLaneId = -1;
    public bool isYielding = false;


    private LineRenderer trajectoryLine;
    private Vector3[] trajectoryPoints = new Vector3[20];
    
    public IntersectionState currentIntersectionState = IntersectionState.Uncontrolled; 
    public int currentDestinationNodeId = -1;
    private int nearestIntersectionNodeId = -1;
    private Vector3 stopTargetPosition = Vector3.zero;
    private bool hasStopTarget = false;

    private int reverseCount = 0;
    private SimpleCarController carController;
    private MasterUIManager _uiManager;
    private RoadNetworkGenerator roadGen;
    
    private CatmullRomSpline currentSpline;
    private Vector3 finalDestination = Vector3.zero;

    private float avoidCooldown = 0f;
    private bool isReversing = false;
    private float reverseTimer = 0f;

    private Vector3 lastPosition = Vector3.zero;
    private float escapeSteering = 0f;

    private float brakeMaxDecel = 8f;
    private float laneSearchTimer = 0f;
    private bool isFetchingNextPath = false;
    private int lastNodeId = -1;
    private float rerouteCooldown = 0f;
    private Vector3 lastLaneCheckPos = Vector3.one * -9999f;

    void Start()
    {
        carController = GetComponent<SimpleCarController>();
        if (pathPlanner == null) pathPlanner = FindObjectOfType<PathPlanner>();
        _uiManager = FindObjectOfType<MasterUIManager>();
        roadGen = FindObjectOfType<RoadNetworkGenerator>();
        carController.autoMode = true;
        lastPosition = transform.position;
        laneSearchTimer = Random.Range(0f, 0.2f);

        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr == null)
            lr = gameObject.AddComponent<LineRenderer>();
        trajectoryLine = lr;
        trajectoryLine.positionCount = trajectoryPoints.Length;
        if (lr.sharedMaterial == null)
        {
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.startColor = Color.green;
            lr.endColor = new Color(0, 1, 0, 0);
        }
        lr.startWidth = 0.1f;
        lr.endWidth = 0.05f;
        trajectoryLine.numCapVertices = 4;
        for (int i = 0; i < trajectoryPoints.Length; i++)
            trajectoryPoints[i] = transform.position;
        trajectoryLine.SetPositions(trajectoryPoints);
    }

    void Update()
    {
        if (avoidCooldown > 0f) avoidCooldown -= Time.deltaTime;

        // 【修复 Bug 9】将传感器和可视化代码提到 return 之前。
        // 确保即使玩家在手动驾驶，依然能看到雷达扫描和预测轨迹！
        UpdateSensorData();
        UpdateTrajectoryLine();
        DrawLidarRays();

        // 只有状态机和防卡死检测会被拦截
        if (!carController.autoMode && !carController.wasdOverride && currentState != DriveState.RemoteControlled) return;

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

        Vector3 frontOrigin = transform.position + transform.forward * sensorForwardOffset + Vector3.up * 0.5f;

        RaycastHit[] hits = Physics.SphereCastAll(frontOrigin, 1.5f, transform.forward, safeDistance);
        foreach (var hit in hits)
        {
            var otherCar = hit.collider.GetComponentInParent<SimpleCarController>();
            if (otherCar != null && otherCar != this.carController)
            {
                obstacleDetected = true;
                if (hit.distance < safeDistance * 0.4f && carController.GetSpeed() > 3f)
                {
                    if (_uiManager != null && !carController.isNPC) _uiManager.ShowTORWarning(1.5f);
                    if (!carController.isNPC) AppendThought("CRITICAL: Obstacle " + hit.distance.ToString("F1") + "m ahead! TOR triggered");
                }
                break;
            }
        }

        isYielding = false;
        if (carController.vehiclePriority == VehiclePriority.Normal && currentState == DriveState.Following)
        {
            Vector3 backOrigin = transform.position - transform.forward * sensorForwardOffset + Vector3.up * 0.5f;

            RaycastHit[] backHits = Physics.SphereCastAll(backOrigin, 2f, -transform.forward, safeDistance * 2f);
            foreach (var backHit in backHits)
            {
                var behindCar = backHit.collider.GetComponentInParent<SimpleCarController>();
                if (behindCar != null && behindCar != this.carController && behindCar.vehiclePriority == VehiclePriority.Emergency)
                {
                    isYielding = true;
                    break;
                }
            }
        }

        if (WorldModel.Instance != null)   
        {
            nearestIntersectionNodeId = -1;
            RoadNode nearestNode = WorldModel.Instance.GetNearestNode(transform.position);
            if (nearestNode != null && (nearestNode.Type == NodeType.Intersection || nearestNode.Type == NodeType.Merge))
            {
                nearestIntersectionNodeId = nearestNode.Id;
                StopLine relevantStopLine = WorldModel.Instance.GetNearestStopLine(nearestNode.Id, transform.position);
                if (relevantStopLine != null && Vector3.Distance(transform.position, relevantStopLine.Position) < 20f)
                {
                    Vector3 dirToStopLine = relevantStopLine.Position - transform.position;
                    if (Vector3.Dot(transform.forward, dirToStopLine) > 0)
                    {
                        currentIntersectionState = WorldModel.Instance.GetPhaseState(relevantStopLine.AssociatedPhaseId);
                    }
                    else currentIntersectionState = IntersectionState.Uncontrolled;
                }
                else
                    currentIntersectionState = IntersectionState.Uncontrolled;
            }
            else currentIntersectionState = IntersectionState.Uncontrolled;
        }
        else
        {
            currentIntersectionState = IntersectionState.Uncontrolled;
        }
    }

    void UpdateStuckDetection()
    {
        return;
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

    void RequestNewRandomPath()
    {
        if (WorldModel.Instance == null || pathPlanner == null)
        {
            isFetchingNextPath = false;
            return;
        }

        for (int i = 0; i < 15; i++)
        {
            int      randId     = Random.Range(0, WorldModel.Instance.NodeCount);
            RoadNode targetNode = WorldModel.Instance.GetNode(randId);

            if (targetNode == null || targetNode.NeighborIds == null
                || targetNode.NeighborIds.Count <= 1) continue;

            if (Vector3.Distance(transform.position, targetNode.WorldPos) < 20f) continue;

            Vector3 dirToTarget = (targetNode.WorldPos - transform.position).normalized;
            if (Vector3.Dot(transform.forward, dirToTarget) < 0.3f) continue;

            CatmullRomSpline newSpline =
                pathPlanner.PlanPathSpline(transform.position, targetNode.WorldPos, transform.forward);
            if (newSpline == null || newSpline.TotalLength <= 0) continue;

            Vector3 newStartTangent = (newSpline.GetPoint(0.05f) - newSpline.GetPoint(0f)).normalized;
            if (Vector3.Dot(transform.forward, newStartTangent) < 0.7f) continue;

            SetSplinePath(newSpline, targetNode.Id);
            isFetchingNextPath = false;
            return;
        }

        RoadNode nearest = WorldModel.Instance.GetNearestNode(transform.position);
        if (nearest?.NeighborIds != null)
        {
            foreach (int nbId in nearest.NeighborIds)
            {
                if (nbId == lastNodeId) continue;

                RoadNode nbNode = WorldModel.Instance.GetNode(nbId);
                Vector3  dir    = (nbNode.WorldPos - transform.position).normalized;
                if (Vector3.Dot(transform.forward, dir) <= 0.1f) continue;

                CatmullRomSpline fallback =
                    pathPlanner.PlanPathSpline(transform.position, nbNode.WorldPos, transform.forward);
                if (fallback != null && fallback.TotalLength > 0)
                {
                    SetSplinePath(fallback, nbNode.Id);
                    isFetchingNextPath = false;
                    return;
                }
            }
        }

        if (currentSpline == null || currentSpline.TotalLength <= 0)
        {
            carController.SetAutoControl(0f, 0f);
            targetSpeed  = 0f;
            currentState = DriveState.Idle;
        }
        else
        {
            targetSpeed = 2f;
        }
        isFetchingNextPath = false;
    }

    public void ResetNavigation()
    {
        RequestNewRandomPath();
    }

    public void SetSplinePath(CatmullRomSpline spline, int destinationNodeId)
    {
        lastNodeId              = currentDestinationNodeId;
        currentDestinationNodeId = destinationNodeId;
        currentSpline           = spline;

        currentT     = spline.TotalLength > 0
            ? currentSpline.GetClosestT(transform.position, 0f)
            : 0f;

        currentState = DriveState.Following;
    }

    void RerouteToDestination()
    {
        if (pathPlanner == null) return;

        if (finalDestination != Vector3.zero)
        {
            CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, finalDestination, transform.forward);
            if (newSpline != null && newSpline.TotalLength > 0)
            {
                currentSpline = newSpline;
                currentT = 0f;
                currentState = DriveState.Following;
                return;
            }
        }

        RequestNewRandomPath();
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

    void UpdateTrajectoryLine()
    {
        if (trajectoryLine == null || trajectoryPoints == null) return;

        Vector3 origin = transform.position + Vector3.up * 0.3f;

        if (isReversing)
        {
            float revLookDist = 6f;
            for (int i = 0; i < trajectoryPoints.Length; i++)
            {
                float t = i / (float)(trajectoryPoints.Length - 1);
                float dist = t * revLookDist;
                trajectoryPoints[i] = origin - transform.forward * dist;
            }
            trajectoryLine.startColor = new Color(1f, 0.15f, 0.05f, 0.85f);
            trajectoryLine.endColor = new Color(1f, 0.15f, 0.05f, 0.05f);
            trajectoryLine.SetPositions(trajectoryPoints);
            return;
        }

        float actualSpeed = Mathf.Abs(carController.GetSpeed());
        float lookDist = Mathf.Clamp(actualSpeed * 0.8f, 5f, 15f);
        float steerAngle = carController.currentSteeringAngle;

        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            float t = i / (float)(trajectoryPoints.Length - 1);
            float dist = t * lookDist;
            float turnRadius = steerAngle != 0 ? (360f / (steerAngle * 2f * Mathf.PI) * lookDist) : float.MaxValue;
            if (Mathf.Abs(steerAngle) < 0.5f || turnRadius > 500f)
            {
                trajectoryPoints[i] = origin + transform.forward * dist;
            }
            else
            {
                float arcAngle = dist / Mathf.Abs(turnRadius);
                Vector3 center = transform.position + transform.right * Mathf.Sign(steerAngle) * turnRadius;
                Vector3 dir = (origin - center).normalized;
                float rotSign = Mathf.Sign(steerAngle);
                trajectoryPoints[i] = center + Quaternion.Euler(0, arcAngle * Mathf.Rad2Deg * rotSign, 0) * dir * Mathf.Abs(turnRadius);
                trajectoryPoints[i].y = origin.y;
            }
        }
        trajectoryLine.SetPositions(trajectoryPoints);

        if (currentState == DriveState.Stopping)
        {
            trajectoryLine.startColor = new Color(1f, 0.3f, 0.1f, 0.7f);
            trajectoryLine.endColor = new Color(1f, 0.3f, 0.1f, 0.1f);
        }
        else if (isYielding)
        {
            trajectoryLine.startColor = new Color(0.2f, 0.6f, 1f, 0.7f);
            trajectoryLine.endColor = new Color(0.2f, 0.6f, 1f, 0.1f);
        }
        else
        {
            trajectoryLine.startColor = new Color(0, 1f, 0.5f, 0.7f);
            trajectoryLine.endColor = new Color(0, 1f, 0.5f, 0.1f);
        }
    }

    void DrawLidarRays()
    {
        int rayCount = 32;
        float maxDist = 30f;
        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (360f / rayCount) * Mathf.Deg2Rad;
            Vector3 dir = transform.TransformDirection(new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)));
            float dist = maxDist;
            Color rayCol = new Color(0, 0.8f, 1f, 0.3f);

            if (Physics.Raycast(transform.position + Vector3.up * 0.4f, dir, out RaycastHit hit, maxDist))
            {
                dist = hit.distance;
                rayCol = new Color(0, 1f, 0.6f, 0.5f);
            }
            Debug.DrawRay(transform.position + Vector3.up * 0.4f, dir * dist, rayCol);
        }
    }

    void OnDrawGizmos()
    {
        if (currentSpline != null && currentT < 1f)
        {
            float lookAheadDist = Mathf.Clamp(Mathf.Abs(carController.GetSpeed()) * 0.5f, 3f, 12f);
            float lookT = Mathf.Clamp01(currentT + lookAheadDist / currentSpline.TotalLength);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(currentSpline.GetPoint(lookT), 2f);
        }
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, safeDistance);

        if (obstacleDetected)
        {
            RaycastHit hitInfo;
            if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 1.5f, transform.forward, out hitInfo, safeDistance))
            {
                Gizmos.color = new Color(0, 1f, 0.4f, 0.6f);
                Gizmos.DrawWireCube(hitInfo.point, new Vector3(2.2f, 1.5f, 4.5f));
            }
        }
    }

    void AppendThought(string line)
    {
        if (_uiManager != null) _uiManager.AppendThoughtLine(line);
    }

    void TriggerTORIfNeeded(float distToStop, float speed)
    {
        if (speed < 2f || distToStop > 10f) return;
        if (carController.isNPC) return;
        if (_uiManager != null) _uiManager.ShowTORWarning(2.5f);
    }

    public void ResetStateMachine()
    {
        isReversing = false;
        reverseTimer = 0f;
        avoidCooldown = 0f;
        hasStopTarget = false;
        reverseCount = 0;
        currentDestinationNodeId = -1;

        if (currentSpline != null && currentSpline.TotalLength > 0)
        {
            currentT = currentSpline.GetClosestT(transform.position, 0f);
            currentT = Mathf.Clamp01(currentT);
            currentState = DriveState.Following;
        }
        else
        {
            currentState = DriveState.Idle;
        }
    }
}