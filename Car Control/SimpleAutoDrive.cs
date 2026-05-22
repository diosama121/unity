using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SimpleCarController))]
public partial class SimpleAutoDrive : MonoBehaviour
{
    // ==========================================
    // 旧枚举保留（外部代码兼容）
    // ==========================================
    public enum DriveState { Cruising, Transit, Stopping, Reversing, Crashed }

    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed        = 8f;
    public float safeDistance       = 4f;
    public float emergencyBrakeDist = 3.5f;
    public float safeFollowSpeed    = 10f;
    public bool  dynamicLookAhead   = true;
    public float lookAheadMin       = 3f;
    public float lookAheadMax       = 12f;

    [Header("传感器设置")]
    public float sensorForwardOffset = 4f;
    public LayerMask obstacleLayers;     // Vehicle + Pedestrian
    public LayerMask pedestrianLayer;    // Pedestrian only

    [Header("状态机")]
    public bool isPlayerControlled = false; 
    public LongitudinalState longState = LongitudinalState.FreeDrive;
 public bool reverseComplete = false;
    [Header("调试信息")]
    public float currentT = 0f;

    // ========== 旧字段兼容（外部读写映射） ==========
    public CatmullRomSpline currentSpline
    {
        get => currentCurve;
        set { currentCurve = value; currentEdgeLength = (value != null) ? value.TotalLength : 0f; }
    }
    public int currentLaneId
    {
        get
        {
            if (currentEdgeIndex < pathEdgeIds.Count)
            {
                int id = pathEdgeIds[currentEdgeIndex];
                return (id > 0) ? id : -1;
            }
            return (worldModel != null) ? worldModel.FindNearestLane(transform.position) : -1;
        }
        set
        {
            if (value > 0 && pathEdgeIds.Count == 0)
                pathEdgeIds.Add(value);
        }
    }
    public bool isYielding       => longState == LongitudinalState.Yield;
    public bool obstacleDetected => frontDistance < safeDistance;

    public DriveState currentState
    {
        get => longState switch
        {
            LongitudinalState.FreeDrive  => DriveState.Cruising,
            LongitudinalState.FollowCar  => DriveState.Cruising,
            LongitudinalState.Yield      => DriveState.Cruising,
            LongitudinalState.Brake      => DriveState.Stopping,
            LongitudinalState.Stopped    => DriveState.Stopping,
            LongitudinalState.Reverse    => DriveState.Reversing,
            _ => DriveState.Cruising
        };
        set
        {
            longState = value switch
            {
                DriveState.Cruising  => LongitudinalState.FreeDrive,
                DriveState.Transit   => LongitudinalState.FreeDrive,
                DriveState.Stopping  => LongitudinalState.Stopped,
                DriveState.Reversing => LongitudinalState.Reverse,
                DriveState.Crashed   => LongitudinalState.Stopped,
                _ => LongitudinalState.FreeDrive
            };
        }
    }

    // --- 内部引用 ---
    private SimpleCarController carController;
    private MasterUIManager     _uiManager;
    private LineRenderer        trajectoryLine;
    private Vector3[]           trajectoryPoints = new Vector3[20];

    // ========== 路径（图边序列） ==========
    [HideInInspector] public List<int> pathEdgeIds = new List<int>(); // +ve = LaneId, -ve = -ConnectorId
    private int currentEdgeIndex = 0;

    // ========== 曲线运动 ==========
    private CatmullRomSpline currentCurve;
    private float currentEdgeLength;
    public float currentSpeed;               // m/s, 正=前进, 负=倒车

    // ========== 加速度参数 ==========
    public float maxAcceleration    = 10f;
    public float maxDeceleration    = 15f;    // 紧急制动
    public float normalDeceleration = 5f;     // 普通减速

    // ========== 传感器 ==========
    private float frontDistance;
    private float frontSpeed;
    private bool  redLightAhead;
    private bool  pedestrianDanger;
    private float stoppedTimer;

    // ========== 倒车 ==========
    private float reverseTimer = 0f;
    private const float reverseDuration = 2.5f;

    private WorldModel worldModel => WorldModel.Instance;

    // ==========================================
    // 初始化
    // ==========================================
 void Start()
    {
        carController = GetComponent<SimpleCarController>();
        if (pathPlanner == null) pathPlanner = FindObjectOfType<PathPlanner>();
        _uiManager = FindObjectOfType<MasterUIManager>();

        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr == null) lr = gameObject.AddComponent<LineRenderer>();
        trajectoryLine = lr;
        trajectoryLine.positionCount = trajectoryPoints.Length;
        if (lr.sharedMaterial == null)
        {
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.startColor = Color.green;
            lr.endColor   = new Color(0, 1, 0, 0);
        }
        lr.startWidth = 0.1f;
        lr.endWidth   = 0.05f;
        trajectoryLine.numCapVertices = 4;
        for (int i = 0; i < trajectoryPoints.Length; i++)
            trajectoryPoints[i] = transform.position;
        trajectoryLine.SetPositions(trajectoryPoints);

        // 防止 TrafficManager 的寻路记忆被清空
        if (pathEdgeIds.Count > 0 && currentCurve == null)
            StartPath();
        else if (pathEdgeIds.Count == 0 && currentCurve == null)
            RequestNewPath(); 
    }

    float SampleFrontDistance(float maxDist, LayerMask mask)
    {
        if (currentCurve == null || currentEdgeLength <= 0) return maxDist;
        if (mask.value == 0) return maxDist;

        float step = 0.5f;
        // 把起始侦测点推到车头之外，防止扫到自己的包围盒导致死锁
        float startD = sensorForwardOffset > 0 ? sensorForwardOffset : 2.5f; 
        
        for (float d = startD; d < maxDist; d += step)
        {
            float t = currentT + d / currentEdgeLength;
            if (t > 1.0f) break;
            
            Vector3 point = currentCurve.GetPoint(t);
            Collider[] hits = Physics.OverlapSphere(point, 1.0f, mask);
            
            foreach (var hit in hits)
            {
                if (!hit.transform.IsChildOf(this.transform))
                    return d;
            }
        }
        return maxDist;
    }

    // ==========================================
    // 每帧更新：纯曲线滑动 + 纵向状态机
    // ==========================================
   void Update()
    {
        UpdateTrajectoryLine();
        DrawLidarRays();

        if (isPlayerControlled)
        {
            // 玩家控制时交还物理权
            VehicleCommand cmd = new VehicleCommand
            {
                throttle  = Input.GetAxis("Vertical"),
                steering  = Input.GetAxis("Horizontal"),
                isBraking = Input.GetKey(KeyCode.Space)
            };
            carController.ApplyCommand(cmd);
            return;
        }

        if (currentCurve == null || currentEdgeLength <= 0) return;

        UpdateSensors();
        (float targetSpd, bool brakeHard) = GetLongitudinalCommand();

        float accel = brakeHard ? maxDeceleration : (targetSpd < currentSpeed ? normalDeceleration : maxAcceleration);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpd, accel * Time.deltaTime);

        float moveDist = currentSpeed * Time.deltaTime;
        AdvanceOnEdge(moveDist);

        // ==== 保留你的路线吸附核心 ====
        SnapToCurve();

        // 仅为了让车轮能够有转向动画，随便算个切线
        Vector3 targetPos = currentCurve.GetPoint(Mathf.Clamp01(currentT + 0.05f));
        Vector3 localTarget = transform.InverseTransformPoint(targetPos);
        float steering = Mathf.Clamp(localTarget.x / 3f, -1f, 1f);
        
        // 下发命令仅仅是为了视觉表现（车轮转动/尾灯），物理位移已被 SnapToCurve 彻底接管
        carController.ApplyCommand(new VehicleCommand
        {
            throttle  = currentSpeed / Mathf.Max(carController.maxSpeed, 0.1f),
            steering  = steering,
            isBraking = brakeHard
        });
    }

    // ==========================================
    // 路径管理
    // ==========================================
    public void SetPath(List<int> edgeIds)
    {
        pathEdgeIds = edgeIds ?? new List<int>();
        StartPath();
    }

    void StartPath()
    {
        currentEdgeIndex = 0;
        currentT = 0f;
        currentSpeed = 0f;
        longState = LongitudinalState.FreeDrive;
        stoppedTimer = 0f;
        reverseTimer = 0f;
        LoadCurrentEdge();
    }

    void LoadCurrentEdge()
    {
        if (currentEdgeIndex >= pathEdgeIds.Count)
        {
            currentCurve = null;
            currentEdgeLength = 0;
            return;
        }
        int id = pathEdgeIds[currentEdgeIndex];

        if (id > 0) // 车道
        {
            if (worldModel != null && worldModel.GlobalLanes.TryGetValue(id, out Lane lane))
                currentCurve = lane.CenterSpline;
        }
        else // 连接器（id为负值，-(ConnectorId+1) 编码以避免 -0 歧义）
        {
            int connId = -id - 1;
            if (worldModel != null && worldModel.GlobalConnectors.TryGetValue(connId, out LaneConnector conn))
                currentCurve = conn.TurnCurve;
        }
        currentEdgeLength = (currentCurve != null) ? currentCurve.TotalLength : 0f;
    }

    // ==========================================
    // 沿边推进与切换
    // ==========================================
   void AdvanceOnEdge(float distance)
    {
        if (currentEdgeLength <= 0) return;
        
        float remainingDist = distance;
        while (remainingDist > 0 && currentCurve != null)
        {
            float distToEdgeEnd = (1.0f - currentT) * currentEdgeLength;
            
            if (remainingDist >= distToEdgeEnd)
            {
                remainingDist -= distToEdgeEnd;
                
                if (currentEdgeIndex < pathEdgeIds.Count - 1)
                {
                    currentEdgeIndex++;
                    LoadCurrentEdge();
                    currentT = 0f; 
                }
                else
                {
                    currentT = 1.0f;
                    currentSpeed = 0f;
                    RequestNewPath();
                    break;
                }
            }
            else
            {
                currentT += remainingDist / currentEdgeLength;
                remainingDist = 0f;
            }
        }
        currentT = Mathf.Clamp01(currentT);
    }
 void SnapToCurve()
    {
        if (currentCurve == null) return;
        Vector3 pos = currentCurve.GetPoint(currentT);
        if (worldModel != null)
            pos.y = worldModel.GetUnifiedHeight(pos.x, pos.z) + 0.15f;

        // 【解决转圈核心】：强制钳制 nextT 不超过 1.0，防止到了路口尽头切线倒转
        float nextT = Mathf.Min(1.0f, currentT + 0.02f);
        Vector3 nextPos = currentCurve.GetPoint(nextT);
        Vector3 tangent = (nextPos - pos).normalized;
        
        if (tangent == Vector3.zero)
        {
            float prevT = Mathf.Max(0.0f, currentT - 0.02f);
            tangent = (pos - currentCurve.GetPoint(prevT)).normalized;
        }
        if (tangent == Vector3.zero) tangent = transform.forward;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            // 给刚体直接赋速度，完美覆盖 SimpleCarController 的内部物理摩擦力干扰
            rb.velocity = tangent * currentSpeed; 
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(pos);
            rb.MoveRotation(Quaternion.LookRotation(tangent));
        }
        else
        {
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent));
        }
    }

    // ==========================================
    // 纵向状态机
    // ==========================================
    (float targetSpeed, bool brakeHard) GetLongitudinalCommand()
    {
        switch (longState)
        {
            case LongitudinalState.FreeDrive:
                if (frontDistance < emergencyBrakeDist || pedestrianDanger)
                    longState = LongitudinalState.Brake;
                else if (frontDistance < safeDistance)
                    longState = LongitudinalState.FollowCar;
                return (targetSpeed, false);

            case LongitudinalState.FollowCar:
                if (frontDistance < emergencyBrakeDist)
                    longState = LongitudinalState.Brake;
                else if (frontDistance > safeDistance + 5f)
                    longState = LongitudinalState.FreeDrive;
                return (Mathf.Min(frontSpeed, safeFollowSpeed), false);

            case LongitudinalState.Brake:
                if (Mathf.Abs(currentSpeed) < 0.1f && frontDistance < 0.5f)
                    longState = LongitudinalState.Stopped;
                else if (!pedestrianDanger && frontDistance > safeDistance + 2f)
                    longState = LongitudinalState.FreeDrive;
                return (0f, true);

            case LongitudinalState.Stopped:
                stoppedTimer += Time.deltaTime;
                if (!redLightAhead && frontDistance > safeDistance + 2f)
                {
                    stoppedTimer = 0f;
                    longState = LongitudinalState.FreeDrive;
                }
                else if (stoppedTimer > 10f) // 堵塞超时 → 倒车脱困
                {
                    stoppedTimer = 0f;
                    reverseTimer = 0f;
                    reverseComplete = false;
                    longState = LongitudinalState.Reverse;
                }
                return (0f, true);

            case LongitudinalState.Reverse:
                reverseTimer += Time.deltaTime;
                if (reverseTimer > reverseDuration)
                {
                    longState = LongitudinalState.Stopped;
                    stoppedTimer = 8f; // 倒车后短暂等待再尝试前进
                    return (0f, false);
                }
                return (-2f, false);

            case LongitudinalState.Yield:
                if (frontDistance > safeDistance + 3f)
                    longState = LongitudinalState.FreeDrive;
                return (Mathf.Min(currentSpeed, safeFollowSpeed * 0.5f), false);

            default:
                return (0f, false);
        }
    }

    // ==========================================
    // 沿曲线传感器采样
    // ==========================================
    void UpdateSensors()
    {
        frontDistance    = SampleFrontDistance(30f, obstacleLayers);
        frontSpeed       = 0f; // TODO: 通过前车引用获取真实速度
        pedestrianDanger = SampleFrontDistance(3f, pedestrianLayer) < 3f;
        redLightAhead    = CheckRedLight();
    }

   

    bool CheckRedLight()
    {
        if (worldModel == null) return false;
        RoadNode nearestNode = worldModel.GetNearestNode(transform.position);
        if (nearestNode == null) return false;
        if (nearestNode.Type != NodeType.Intersection && nearestNode.Type != NodeType.Merge) return false;
        if (currentEdgeLength * (1f - currentT) > 15f) return false; // 离路口还远

        IntersectionState state = worldModel.GetIntersectionState(nearestNode.Id);
        return (state == IntersectionState.RedLight || state == IntersectionState.YellowLight);
    }

    // ==========================================
    // 自动路径规划（随机远端节点 → A* 边序列）
    // ==========================================
   void RequestNewPath()
    {
        if (worldModel == null || pathPlanner == null) return;

        // 保持原有的 A* 循环寻路逻辑
        for (int i = 0; i < 15; i++)
        {
            int randId = Random.Range(0, worldModel.NodeCount);
            RoadNode targetNode = worldModel.GetNode(randId);
            if (targetNode == null || targetNode.NeighborIds == null || targetNode.NeighborIds.Count <= 1) continue;
            if (Vector3.Distance(transform.position, targetNode.WorldPos) < 30f) continue;

            int startLaneId = worldModel.FindNearestLane(transform.position);
            int endLaneId   = worldModel.FindNearestLane(targetNode.WorldPos);
            if (startLaneId < 0 || endLaneId < 0 || startLaneId == endLaneId) continue;

            List<int> edgePath = pathPlanner.PlanEdgePath(startLaneId, endLaneId);
            if (edgePath != null && edgePath.Count > 0)
            {
                SetPath(edgePath);
                return;
            }
        }

        if (currentCurve != null && currentEdgeLength > 0)
        {
            int laneId = worldModel.FindNearestLane(transform.position);
            if (laneId >= 0)
            {
                pathEdgeIds = new List<int> { laneId };
                StartPath();
                return;
            }
        }

        // 不再暴力清零 targetSpeed 导致引擎半身不遂，只清空当前曲线自然停车
        currentCurve = null;
        currentSpeed = 0f; 
        Debug.LogWarning($"[SimpleAutoDrive] {name} 无法规划任何路径，等待中...");
    }
    // ==========================================
    // 公共接口（兼容旧调用）
    // ==========================================
    public void SetSplinePath(CatmullRomSpline spline, int destinationNodeId)
    {
        // 旧接口兼容：退化为基于边序列的路径规划
        RequestNewPath();
    }

    public void SetDestination(Vector3 destination)
    {
        if (worldModel == null || pathPlanner == null) return;
        int startLaneId = worldModel.FindNearestLane(transform.position);
        int endLaneId   = worldModel.FindNearestLane(destination);
        if (startLaneId < 0 || endLaneId < 0) return;

        List<int> edgePath = pathPlanner.PlanEdgePath(startLaneId, endLaneId);
        if (edgePath != null && edgePath.Count > 0)
            SetPath(edgePath);
    }

    public void ResetNavigation() => RequestNewPath();

    public void ResetStateMachine()
    {
        longState = LongitudinalState.FreeDrive;
        stoppedTimer = 0f;
        reverseTimer = 0f;
        currentSpeed = 0f;
        currentT = 0f;
        currentEdgeIndex = 0;
        LoadCurrentEdge();
    }

    public LongitudinalState GetCurrentLongState() => longState;
    public DriveState GetCurrentState() => currentState;

    public void ToggleAutoDrive()
    {
        isPlayerControlled = !isPlayerControlled;
    }

    // ==========================================
    // 可视化：轨迹线
    // ==========================================
    void UpdateTrajectoryLine()
    {
        if (trajectoryLine == null || trajectoryPoints == null) return;
        Vector3 origin = transform.position + Vector3.up * 0.3f;

        bool isRev = (longState == LongitudinalState.Reverse);

        if (isRev)
        {
            for (int i = 0; i < trajectoryPoints.Length; i++)
            {
                float t = i / (float)(trajectoryPoints.Length - 1);
                trajectoryPoints[i] = origin - transform.forward * (t * 6f);
            }
            trajectoryLine.startColor = new Color(1f, 0.15f, 0.05f, 0.85f);
            trajectoryLine.endColor   = new Color(1f, 0.15f, 0.05f, 0.05f);
            trajectoryLine.SetPositions(trajectoryPoints);
            return;
        }

        float speed    = Mathf.Abs(currentSpeed);
        float lookDist = Mathf.Clamp(speed * 0.8f, 5f, 15f);
        float steerAng = carController.currentSteeringAngle;  
        ///重

        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            float t = i / (float)(trajectoryPoints.Length - 1);
            float dist = t * lookDist;
            float turnRadius = steerAng != 0 ? (360f / (steerAng * 2f * Mathf.PI) * lookDist) : float.MaxValue;
            if (Mathf.Abs(steerAng) < 0.5f || turnRadius > 500f)
            {
                trajectoryPoints[i] = origin + transform.forward * dist;
            }
            else
            {
                float arcAngle = dist / Mathf.Abs(turnRadius);
                Vector3 center = transform.position + transform.right * Mathf.Sign(steerAng) * turnRadius;
                Vector3 dir    = (origin - center).normalized;
                trajectoryPoints[i] = center + Quaternion.Euler(0, arcAngle * Mathf.Rad2Deg * Mathf.Sign(steerAng), 0) * dir * Mathf.Abs(turnRadius);
                trajectoryPoints[i].y = origin.y;
            }
        }
        trajectoryLine.SetPositions(trajectoryPoints);

        if (longState == LongitudinalState.Brake || longState == LongitudinalState.Stopped)
        {
            trajectoryLine.startColor = new Color(1f, 0.3f, 0.1f, 0.7f);
            trajectoryLine.endColor   = new Color(1f, 0.3f, 0.1f, 0.1f);
        }
        else if (longState == LongitudinalState.Yield)
        {
            trajectoryLine.startColor = new Color(0.2f, 0.6f, 1f, 0.7f);
            trajectoryLine.endColor   = new Color(0.2f, 0.6f, 1f, 0.1f);
        }
        else
        {
            trajectoryLine.startColor = new Color(0, 1f, 0.5f, 0.7f);
            trajectoryLine.endColor   = new Color(0, 1f, 0.5f, 0.1f);
        }
    }

    void DrawLidarRays()
    {
        int   rayCount = 32;
        float maxDist  = 30f;
        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (360f / rayCount) * Mathf.Deg2Rad;
            Vector3 dir = transform.TransformDirection(new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)));
            float dist   = maxDist;
            Color rayCol = new Color(0, 0.8f, 1f, 0.3f);

            if (Physics.Raycast(transform.position + Vector3.up * 0.4f, dir, out RaycastHit hit, maxDist))
            {
                dist   = hit.distance;
                rayCol = new Color(0, 1f, 0.6f, 0.5f);
            }
            Debug.DrawRay(transform.position + Vector3.up * 0.4f, dir * dist, rayCol);
        }
    }

    void OnDrawGizmos()
    {
        if (currentCurve != null && currentT < 1f)
        {
            float lookAheadDist = Mathf.Clamp(Mathf.Abs(currentSpeed) * 0.5f, 3f, 12f);
            float lookT = Mathf.Clamp01(currentT + lookAheadDist / currentEdgeLength);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(currentCurve.GetPoint(lookT), 2f);
        }
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, safeDistance);

        if (frontDistance < safeDistance)
        {
            if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 1.5f, transform.forward, out RaycastHit hit, safeDistance))
            {
                Gizmos.color = new Color(0, 1f, 0.4f, 0.6f);
                Gizmos.DrawWireCube(hit.point, new Vector3(2.2f, 1.5f, 4.5f));
            }
        }
    }
}