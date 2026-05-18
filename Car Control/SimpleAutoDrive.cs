using UnityEngine;

[RequireComponent(typeof(SimpleCarController))]
public partial class SimpleAutoDrive : MonoBehaviour
{
    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed = 15f;
    public float safeDistance = 8f;
    public float lookAheadT = 0.02f;
    public bool dynamicLookAhead = true;
    public float lookAheadMin = 0.015f;
    public float lookAheadMax = 0.06f;

    public float rightLaneOffset = 3.5f;

    public enum DriveState { Idle, Following, Avoiding, Stopping, Waiting, RemoteControlled }

    [Header("状态机")]
    public DriveState currentState = DriveState.Idle;

    [Header("调试信息")]
    public float currentT = 0f;
    public bool obstacleDetected = false;
    public int currentLaneId = -1;
    public VehiclePriority vehiclePriority = VehiclePriority.Normal;
    public bool isYielding = false;
    private float yieldRightOffset = 6f;

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
        _uiManager = FindObjectOfType<MasterUIManager>();
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
        if (!carController.autoMode && !carController.wasdOverride && currentState != DriveState.RemoteControlled) return;
        if (avoidCooldown > 0f) avoidCooldown -= Time.deltaTime;

        UpdateSensorData();
        UpdateStuckDetection();
        UpdateTrajectoryLine();
        DrawLidarRays();

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

        RaycastHit hit;
        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 1.5f, transform.forward, out hit, safeDistance))
        {
            var otherCar = hit.collider.GetComponentInParent<SimpleCarController>();
            if (otherCar != null && otherCar != this.carController)
            {
                obstacleDetected = true;
                if (hit.distance < safeDistance * 0.4f && carController.GetSpeed() > 3f)
                {
                    if (_uiManager != null) _uiManager.ShowTORWarning(1.5f);
                    AppendThought("CRITICAL: Obstacle " + hit.distance.ToString("F1") + "m ahead! TOR triggered");
                }
            }
        }

        isYielding = false;
        if (vehiclePriority == VehiclePriority.Normal && currentState == DriveState.Following)
        {
            if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 2f, -transform.forward, out hit, safeDistance * 2f))
            {
                var behindCar = hit.collider.GetComponentInParent<SimpleCarController>();
                if (behindCar != null && behindCar != this.carController && behindCar.vehiclePriority == VehiclePriority.Emergency)
                {
                    isYielding = true;
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
                    float sampleT = (currentT < 0.99f) ? Mathf.Min(currentT + 0.01f, 1f) : currentT;
                    Vector3 pA = (currentT > 0.01f) ? currentSpline.GetPoint(currentT - 0.01f) : currentSpline.GetPoint(0f);
                    Vector3 pB = currentSpline.GetPoint(sampleT);
                    Vector3 tangent = (pB - pA).normalized;
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

    void RequestNewRandomPath()
    {
        if (WorldModel.Instance != null && pathPlanner != null)
        {
            for (int i = 0; i < 10; i++)
            {
                int randTargetId = Random.Range(0, WorldModel.Instance.NodeCount);
                RoadNode targetNode = WorldModel.Instance.GetNode(randTargetId);
                if (targetNode != null && targetNode.NeighborIds != null && targetNode.NeighborIds.Count > 1)
                {
                    if (Vector3.Distance(transform.position, targetNode.WorldPos) < 20f) continue;
                    CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, targetNode.WorldPos);
                    if (newSpline != null && newSpline.TotalLength > 0)
                    {
                        SetSplinePath(newSpline, targetNode.Id);
                        return;
                    }
                }
            }
        }
        currentState = DriveState.Idle;
        currentSpline = null;
        carController.SetAutoControl(0f, 0f);
        Debug.LogWarning($"[AutoDrive] Vehicle {gameObject.name} cannot find valid path, entering idle.");
    }

    public void ResetNavigation()
    {
        RequestNewRandomPath();
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

        if (finalDestination != Vector3.zero)
        {
            CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, finalDestination);
            if (newSpline != null && newSpline.TotalLength > 0)
            {
                currentSpline = newSpline;
                currentT = 0f;
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

        float actualSpeed = Mathf.Abs(carController.GetSpeed());
        float lookDist = Mathf.Clamp(actualSpeed * 0.8f, 5f, 15f);
        Vector3 origin = transform.position + Vector3.up * 0.3f;
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
            Gizmos.color = Color.yellow;
            Vector3 drawPoint = currentSpline.GetPoint(currentT);
            Gizmos.DrawWireSphere(drawPoint, 2f);
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
        if (_uiManager != null) _uiManager.ShowTORWarning(2.5f);
    }
}