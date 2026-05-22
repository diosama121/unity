using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SimpleCarController))]
public class SimpleAutoDrive : MonoBehaviour
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
    public float currentT
    {
        get => (currentEdgeLength > 0f) ? currentDistOnEdge / currentEdgeLength : 0f;
        set => currentDistOnEdge = value * currentEdgeLength;
    }

    // ========== 旧字段兼容（外部读写映射） ==========
    private CatmullRomSpline _backingSpline; // 仅用于兼容旧接口
     public CatmullRomSpline currentSpline
    { 
        get => _backingSpline;
        set { _backingSpline = value; currentTrajectory = (value != null) ? new BakedTrajectory(value) : null; currentEdgeLength = (currentTrajectory != null) ? currentTrajectory.TotalLength : 0f; }
    } 
    public int currentLaneId
    {
        get
        {
            if (_isTrajectoryLocked && currentEdgeIndex < pathEdgeIds.Count)
            {
                int id = pathEdgeIds[currentEdgeIndex];
                return (id > 0) ? id : -1;
            }
            if (currentEdgeIndex < pathEdgeIds.Count)
            {
                int id = pathEdgeIds[currentEdgeIndex];
                return (id > 0) ? id : -1;
            }
            return (worldModel != null) ? worldModel.FindNearestLane(transform.position, transform.forward) : -1;
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
    private LineRenderer        trajectoryLine;
    private Vector3[]           trajectoryPoints = new Vector3[20];

    // ========== 路径（图边序列） ==========
    [HideInInspector] public List<int> pathEdgeIds = new List<int>(); // +ve = LaneId, -ve = -ConnectorId
    private int currentEdgeIndex = 0;

    // 【轨迹锁死】：一旦 StartPath 被调用，禁止运行时任何重吸附/重寻路
    private bool _isTrajectoryLocked = false;

    // ========== 曲线运动 (离散烘焙架构：Polyline 替代连续曲线) ==========
    private BakedTrajectory currentTrajectory;
    public float currentDistOnEdge = 0f;     // 绝对物理距离(米)，替代旧的 currentT
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
        // 【修复3】：剥离一切刚体动力学，剥夺 Unity Solver 的控制权
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

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
        if (pathEdgeIds.Count > 0 && currentTrajectory == null)
            StartPath();
        else if (pathEdgeIds.Count == 0 && currentTrajectory == null)
            RequestNewPath(); 
    }

    float SampleFrontDistance(float maxDist, LayerMask mask)
    {
        if (currentTrajectory == null || currentEdgeLength <= 0) return maxDist;
        if (mask.value == 0) return maxDist;

        float step = 0.5f;
        // 把起始侦测点推到车头之外，防止扫到自己的包围盒导致死锁
        float startD = sensorForwardOffset > 0 ? sensorForwardOffset : 2.5f; 
        
        for (float d = startD; d < maxDist; d += step)
        {
            float distOnEdge = currentDistOnEdge + d;
            if (distOnEdge > currentEdgeLength) break;
            
            Vector3 point = currentTrajectory.GetPointAtDistance(distOnEdge);
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

        if (currentTrajectory == null || currentEdgeLength <= 0) return;

        UpdateSensors();
        (float targetSpd, bool brakeHard) = GetLongitudinalCommand();

        float accel = brakeHard ? maxDeceleration : (targetSpd < currentSpeed ? normalDeceleration : maxAcceleration);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpd, accel * Time.deltaTime);

        float moveDist = currentSpeed * Time.deltaTime;
        AdvanceOnEdge(moveDist);

        // ==== 焊死在离散折线上的核心吸附 ====
        SnapToCurve();

        if (currentTrajectory == null) return; // AdvanceOnEdge/RequestNewPath 可能已将轨迹清空

        // 下发命令仅用于视觉表现（车轮转动/尾灯），物理位移已被 SnapToCurve 彻底接管
        carController.ApplyCommand(new VehicleCommand
        {
            throttle  = currentSpeed / Mathf.Max(carController.maxSpeed, 0.1f),
            steering  = 0f,
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
        if (pathEdgeIds == null || pathEdgeIds.Count == 0) return;

        currentEdgeIndex = 0;
        LoadCurrentEdge();
        currentDistOnEdge = 0f;
        currentSpeed = 0f;
        longState = LongitudinalState.FreeDrive;
        stoppedTimer = 0f;
        reverseTimer = 0f;

        // 锁死！一旦上路，断绝一切外部重定位干扰
        _isTrajectoryLocked = true;
    }

    void LoadCurrentEdge()
    {
        if (currentEdgeIndex >= pathEdgeIds.Count)
        {
            currentTrajectory = null;
            currentEdgeLength = 0;
            return;
        }
        
        int id = pathEdgeIds[currentEdgeIndex];
        CatmullRomSpline splineToBake = null;

        if (id > 0 && worldModel.GlobalLanes.TryGetValue(id, out Lane lane))
            splineToBake = lane.CenterSpline;
        else if (id < 0 && worldModel.GlobalConnectors.TryGetValue(-id - 1, out LaneConnector conn))
            splineToBake = conn.TurnCurve;

        if (splineToBake != null)
        {
            // 瞬间烘焙！每 0.5 米采样一个点，彻底消除参数畸变
            currentTrajectory = new BakedTrajectory(splineToBake, 0.5f);
            currentEdgeLength = currentTrajectory.TotalLength;
        }
        else
        {
            currentTrajectory = null;
            currentEdgeLength = 0;
        }
    }

    // ==========================================
    // 沿边推进与切换
    // ==========================================
   void AdvanceOnEdge(float distance)
    {
        if (currentEdgeLength <= 0) return;
        
        float remainingDist = distance;
        while (remainingDist > 0 && currentTrajectory != null)
        {
            float distToEdgeEnd = currentEdgeLength - currentDistOnEdge;
            
            if (remainingDist >= distToEdgeEnd)
            {
                remainingDist -= distToEdgeEnd;
                
                if (currentEdgeIndex < pathEdgeIds.Count - 1)
                {
                    currentEdgeIndex++;
                    LoadCurrentEdge();
                    currentDistOnEdge = 0f; 
                }
                else
                {
                    currentDistOnEdge = currentEdgeLength;
                    currentSpeed = 0f;

                    // 先尝试纯拓扑顺延（盲接 NextConnectorIds），失败才走全图寻路
                    if (!ExtendPathSeamlessly())
                        RequestNewPath();
                    break;
                }
            }
            else
            {
                currentDistOnEdge += remainingDist;
                remainingDist = 0f;
            }
        }
        currentDistOnEdge = Mathf.Clamp(currentDistOnEdge, 0f, currentEdgeLength);
    }
 void SnapToCurve()
    {
        if (currentTrajectory == null) return;
        
        // 1. 获取基于绝对距离的空间坐标
        Vector3 pos = currentTrajectory.GetPointAtDistance(currentDistOnEdge);
        if (worldModel != null)
            pos.y = worldModel.GetUnifiedHeight(pos.x, pos.z) + 0.15f;

        // 2. 获取基于绝对距离的折线切线 (永远是两点相减，永不翻转)
        Vector3 tangent = currentTrajectory.GetTangentAtDistance(currentDistOnEdge);
        if (tangent.sqrMagnitude < 0.0001f) tangent = transform.forward;

        // 3. 旋转直接对齐切线 —— 折线方向是确定性的，Slerp 滞后反而导致弯道抽搐
        Quaternion targetRot = Quaternion.LookRotation(tangent);

        transform.SetPositionAndRotation(pos, targetRot);
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
        if (currentEdgeLength - currentDistOnEdge > 15f) return false; // 离路口还远

        IntersectionState state = worldModel.GetIntersectionState(nearestNode.Id);
        return (state == IntersectionState.RedLight || state == IntersectionState.YellowLight);
    }

    // ==========================================
    // 纯拓扑路径顺延：完全不看空间距离，图论说连着哪就无脑接哪
    // ==========================================
    bool ExtendPathSeamlessly()
    {
        int lastId = pathEdgeIds[pathEdgeIds.Count - 1];
        if (lastId > 0 && worldModel.GlobalLanes.TryGetValue(lastId, out Lane lane))
        {
            if (lane.NextConnectorIds != null && lane.NextConnectorIds.Count > 0)
            {
                int randConn = lane.NextConnectorIds[Random.Range(0, lane.NextConnectorIds.Count)];
                if (worldModel.GlobalConnectors.TryGetValue(randConn, out LaneConnector conn))
                {
                    pathEdgeIds.Add(-conn.ConnectorId - 1);  // connector 编码为负数
                    pathEdgeIds.Add(conn.ToLaneId);
                    currentEdgeIndex++;
                    LoadCurrentEdge();
                    currentDistOnEdge = 0f;
                    return true;
                }
            }
        }
        return false;
    }

    // ==========================================
    // 自动路径规划（仅在初始或彻底迷路时调用）
    // ==========================================
   void RequestNewPath()
    {
        // 【最强防御】：轨迹锁死状态下，绝对禁止重新寻路！
        if (_isTrajectoryLocked && currentTrajectory != null) return;

        if (worldModel == null || pathPlanner == null) return;

        // 只在刚出生或彻底停下时，带车头朝向来找初始车道
        // 杜绝吸附到脚下反向/垂直的错误车道
        int startLaneId = worldModel.FindNearestLane(transform.position, transform.forward);
        if (startLaneId < 0) return;

        for (int i = 0; i < 15; i++)
        {
            int randId = Random.Range(0, worldModel.NodeCount);
            RoadNode targetNode = worldModel.GetNode(randId);
            if (targetNode == null || targetNode.NeighborIds == null || targetNode.NeighborIds.Count <= 1) continue;
            if (Vector3.Distance(transform.position, targetNode.WorldPos) < 30f) continue;

            int endLaneId = worldModel.FindNearestLane(targetNode.WorldPos);
            if (endLaneId < 0 || startLaneId == endLaneId) continue;

            List<int> edgePath = pathPlanner.PlanEdgePath(startLaneId, endLaneId);
            if (edgePath != null && edgePath.Count > 0)
            {
                SetPath(edgePath);
                return;
            }
        }

        // 兜底：沿当前轨迹所在车道走
        if (currentTrajectory != null && currentEdgeLength > 0)
        {
            int laneId = worldModel.FindNearestLane(transform.position, transform.forward);
            if (laneId >= 0)
            {
                pathEdgeIds = new List<int> { laneId };
                StartPath();
                return;
            }
        }

        // 彻底找不到路，解开锁等待下次唤醒
        currentTrajectory = null;
        currentSpeed = 0f;
        _isTrajectoryLocked = false;
        Debug.LogWarning($"[SimpleAutoDrive] {name} 无法规划路径，原地待命...");
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
        int startLaneId = worldModel.FindNearestLane(transform.position, transform.forward);
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
        currentDistOnEdge = 0f;
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


