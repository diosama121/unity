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
    // [已移除] lookAheadMin/Max/safeFollowSpeed/dynamicLookAhead — 旧PID转向参数，现由SnapToCurve接管

    [Header("传感器设置")]
    public float sensorForwardOffset = 4f;
    public LayerMask obstacleLayers;     // Vehicle + Pedestrian
    public LayerMask pedestrianLayer;    // Pedestrian only

    [Header("手动驾驶参数（玩具车模式）")]
    public float manualDriveSpeed = 15f;
    public float manualTurnRate = 120f;  // 度/秒
    public float manualAccel = 25f;      // m/s²

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

    // ★ 辅助类状态暴露（供 SystemDataManager 导出）
    public int  SubsumptionActiveLayer   => _subsumptionEngine?.ActiveLayer ?? 0;
    public bool IsInDilemmaZone          => _dilemmaZoneArbiter?.IsInDilemmaZone ?? false;
    public bool IsDeadlockPerturbating   => _deadlockRecovery?.IsPerturbating ?? false;
    public bool IsYieldingToEmergency    => _emergencyYieldHandler?.IsYielding ?? false;
    public float PullOverOffset           => _pullOverHandler?.CurrentOffset ?? 0f;

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
    public int currentEdgeIndex = 0;   // V4.2: 改为public供数据导出

    // 【轨迹锁死】：一旦 StartPath 被调用，禁止运行时任何重吸附/重寻路
    private bool _isTrajectoryLocked = false;
    // 【目的地锁定】：SetDestination 后禁止 ExtendPathSeamlessly 覆盖
    private bool _hasDestination = false;

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
    public float frontDistance { get; private set; }
    public float frontSpeed { get; private set; }
    public bool  redLightAhead { get; private set; }
    private bool  pedestrianDanger;
    private float stoppedTimer;
    private bool  _isYellowLight;            // 当前是否为黄灯（用于困境区仲裁）

    // ========== 停车标志 ==========
    private float _stopSignTimer = 0f;          // 停车标志已停止计时
    private bool  _stopSignReleased = false;     // ★ 已放行标志，防止停满2秒后立即重新触发
    private const float stopSignDuration = 2f;   // 停车标志需停满2秒

    // ========== 倒车 ==========
    private float reverseTimer = 0f;
    private const float reverseDuration = 2.5f;

    // ========== 僵死恢复（轨迹为空时重试） ==========
    private float _recoveryTimer = 0f;
    private const float recoveryInterval = 2f;

    // ========== 手动模式状态 ==========
    private bool _wasPlayerControlled = false;
    private float _manualSpeed = 0f;

    // ========== 信息面板开关（U键） ==========
    private bool _showInfoPanel = true;

    // ========== NPC全局注册表（假检测用） ==========
    public static List<SimpleAutoDrive> AllCars = new List<SimpleAutoDrive>();

    // ========== 红绿灯/交规 ==========
    private TrafficLightManager _trafficLightManager;
    [Header("红绿灯交规")]
    public float redLightStopDistance = 18f;
    public float stopLinePassedThreshold = -2f;

    // ★ 公开给辅助类读取
    public float distToStopLine { get; private set; }
    public bool  hasStopLineAhead { get; private set; }
    public Vector3 nearestStopLinePos { get; private set; }

    private WorldModel worldModel => WorldModel.Instance;

    // ==========================================
    // 辅助类实例（包容式架构 + 死锁脱困 + 黄灯仲裁 + 紧急让行）
    // ==========================================
    private SubsumptionEngine     _subsumptionEngine;
    private DeadlockRecovery      _deadlockRecovery;
    private DilemmaZoneArbiter    _dilemmaZoneArbiter;
    private EmergencyYieldHandler _emergencyYieldHandler;
    private PullOverHandler       _pullOverHandler;
    private SpecialSituations     _specialSituations; // ★ ROS2 AEB 外部中断

    // ==========================================
    // 初始化
    // ==========================================
    void Start()
    {
        carController = GetComponent<SimpleCarController>();
        if (pathPlanner == null) pathPlanner = FindObjectOfType<PathPlanner>();

        // 剥离一切刚体动力学，剥夺 Unity Solver 的控制权
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

        // 初始化辅助类
        _subsumptionEngine     = new SubsumptionEngine();
        _deadlockRecovery      = new DeadlockRecovery();
        _dilemmaZoneArbiter    = new DilemmaZoneArbiter();
        _emergencyYieldHandler = new EmergencyYieldHandler();
        _pullOverHandler       = new PullOverHandler();
        _specialSituations     = GetComponent<SpecialSituations>(); // ★ ROS2 AEB

        // 防止 TrafficManager 的寻路记忆被清空
        if (pathEdgeIds.Count > 0 && currentTrajectory == null)
            StartPath();
        else if (pathEdgeIds.Count == 0 && currentTrajectory == null)
            RequestNewPath();

        // 注册到全局NPC列表（假检测用）
        AllCars.Add(this);
    }

    void OnDestroy()
    {
        AllCars.Remove(this);
    }

    float SampleFrontDistance(float maxDist, LayerMask mask)
    {
        if (currentTrajectory == null || currentEdgeLength <= 0) return maxDist;
        if (mask.value == 0) return maxDist;

        float step = 0.5f;
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
    // 每帧更新：包容式架构纵向控制 + 辅助类调度
    // ==========================================
    void Update()
    {
        UpdateTrajectoryLine();
        DrawLidarRays();

        // ★ U键：切换信息面板
        if (Input.GetKeyDown(KeyCode.U))
            _showInfoPanel = !_showInfoPanel;

        if (isPlayerControlled)
        {
            // === 进入手动模式 ===
            if (!_wasPlayerControlled)
            {
                _wasPlayerControlled = true;
                _manualSpeed = carController.currentSpeed; // 继承当前速度
                carController.manualControl = true;
                // 重置辅助类状态
                _subsumptionEngine.ResetDebounce();
                _deadlockRecovery.Reset();
                Debug.Log($"[SimpleAutoDrive] {name} → 手动驾驶模式");
            }

            // === 自由移动（玩具车） ===
            float steerInput = Input.GetAxis("Horizontal");
            float throttleInput = Input.GetAxis("Vertical");
            bool braking = Input.GetKey(KeyCode.Space);

            if (braking)
            {
                _manualSpeed = Mathf.MoveTowards(_manualSpeed, 0f, manualAccel * 2f * Time.deltaTime);
            }
            else
            {
                float targetManualSpeed = throttleInput * manualDriveSpeed;
                _manualSpeed = Mathf.MoveTowards(_manualSpeed, targetManualSpeed, manualAccel * Time.deltaTime);
            }

            // 位移
            transform.position += transform.forward * (_manualSpeed * Time.deltaTime);
            // 转向（速度无关）
            float turnAmount = steerInput * manualTurnRate * Time.deltaTime;
            transform.Rotate(0f, turnAmount, 0f);

            // 同步到 SimpleCarController（供外部读取用）
            carController.currentSpeed = _manualSpeed;
            carController.currentSteeringAngle = steerInput * carController.maxSteeringAngle;

            return;
        }
        else if (_wasPlayerControlled)
        {
            // === 手动→自动：回归车流 ===
            _wasPlayerControlled = false;
            _manualSpeed = 0f;
            carController.manualControl = false;
            MergeBackToTraffic();
        }

        // ==========================================
        // ★ 轨迹为空 → 死锁恢复（DeadlockRecovery接管）
        // ==========================================
        if (currentTrajectory == null || currentEdgeLength <= 0)
        {
            _deadlockRecovery.Update(transform.position, Time.deltaTime);

            if (_deadlockRecovery.NeedsForceRepath)
            {
                Debug.LogWarning($"[SimpleAutoDrive] {name} 死锁脱困Stage2：强制重规划");
                _deadlockRecovery.Reset();
                ForceNewDestination();
            }
            else
            {
                _recoveryTimer += Time.deltaTime;
                if (_recoveryTimer > recoveryInterval)
                {
                    _recoveryTimer = 0f;
                    RequestNewPath();
                }
            }
            return;
        }
        _recoveryTimer = 0f;
        _deadlockRecovery.Reset(); // 轨迹正常，重置死锁检测

        // ==========================================
        // 传感器采样
        // ==========================================
        UpdateSensors();

        // ==========================================
        // 紧急车辆让行检测（论文 §5.4 TF-2）
        // ==========================================
        _emergencyYieldHandler.Update(transform.position, false, Time.deltaTime);

        // ==========================================
        // 直路让行靠边（PullOverHandler）
        // 仅在直路且不在路口范围内时允许靠边
        // ==========================================
        if (_emergencyYieldHandler.IsYielding && IsOnStraightRoad())
        {
            _pullOverHandler.Activate();
        }
        else
        {
            _pullOverHandler.Deactivate();
        }
        _pullOverHandler.Update(Time.deltaTime);

        // ==========================================
        // 黄灯困境区仲裁（论文 §4.2.5）
        // ==========================================
        if (_isYellowLight && hasStopLineAhead)
        {
            bool shouldBrake = _dilemmaZoneArbiter.Evaluate(distToStopLine, currentSpeed, maxDeceleration);
            if (!shouldBrake)
            {
                // 困境区仲裁判定可通过 → 覆盖红灯标志，让L1不接管
                redLightAhead = false;
            }
        }

        // ==========================================
        // 包容式架构纵向控制（论文 §4.2.1）
        // ==========================================
        float effectiveCruiseSpeed = targetSpeed;
        if (_emergencyYieldHandler.IsYielding)
            effectiveCruiseSpeed = Mathf.Min(effectiveCruiseSpeed, _emergencyYieldHandler.GetYieldSpeed());

        // ★ 速度限制：读取当前车道的 SpeedLimit (km/h → m/s)
        float laneSpeedLimit = GetLaneSpeedLimit();
        if (laneSpeedLimit > 0f)
            effectiveCruiseSpeed = Mathf.Min(effectiveCruiseSpeed, laneSpeedLimit);

        var sensorInput = new SubsumptionEngine.SensorInput
        {
            currentSpeed       = currentSpeed,
            maxSpeed           = carController.maxSpeed,
            maxAcceleration    = maxAcceleration,
            maxDeceleration    = maxDeceleration,
            frontDistance      = frontDistance,
            safeDistance       = safeDistance,
            emergencyBrakeDist = emergencyBrakeDist,
            frontSpeed         = frontSpeed,
            redLightAhead      = redLightAhead,
            pedestrianDanger   = pedestrianDanger,
            distToStopLine     = distToStopLine,
            targetCruiseSpeed  = effectiveCruiseSpeed
        };

        var layerOutput = _subsumptionEngine.Resolve(sensorInput, Time.deltaTime);
        longState = _subsumptionEngine.ActiveState;

        float targetSpd = layerOutput.targetSpeed;
        bool  brakeHard = layerOutput.isHardBrake;

        // ★ 外部AEB（ROS2）优先级最高，覆盖包容式架构
        var extCmd = _specialSituations?.Handle();
        if (extCmd.HasValue)
        {
            brakeHard = extCmd.Value.isBraking;
            targetSpd = 0f;
        }

        // ==========================================
        // 停止超时 → 倒车脱困（保留在此处管理）
        // ==========================================
        if (longState == LongitudinalState.Stopped)
        {
            stoppedTimer += Time.deltaTime;
            if (stoppedTimer > 10f)
            {
                stoppedTimer = 0f;
                reverseTimer = 0f;
                reverseComplete = false;
                longState = LongitudinalState.Reverse;
                Debug.Log($"[SimpleAutoDrive] {name} 停止超时10s → 倒车脱困");
            }
        }
        else
        {
            stoppedTimer = 0f;
        }

        // ==========================================
        // 倒车逻辑
        // ==========================================
        if (longState == LongitudinalState.Reverse)
        {
            reverseTimer += Time.deltaTime;
            if (reverseTimer > reverseDuration)
            {
                longState = LongitudinalState.Stopped;
                stoppedTimer = 0f;
                targetSpd = 0f;
                brakeHard = false;
            }
            else
            {
                targetSpd = -2f;
                brakeHard = false;
            }
        }

        // ==========================================
        // 速度平滑 + 位移
        // ==========================================
        float accel = brakeHard ? maxDeceleration : (targetSpd < currentSpeed ? normalDeceleration : maxAcceleration);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpd, accel * Time.deltaTime);

        float moveDist = currentSpeed * Time.deltaTime;
        AdvanceOnEdge(moveDist);
        SnapToCurve();

        // ★ 应用横向偏移（直路让行靠边）
        if (_pullOverHandler.IsActive && currentTrajectory != null)
        {
            Vector3 tangent = currentTrajectory.GetTangentAtDistance(currentDistOnEdge);
            Vector3 lateralOffset = _pullOverHandler.GetLateralOffset(tangent);
            transform.position += lateralOffset;
        }

        if (currentTrajectory == null) return;

        // 下发命令（仅用于视觉表现：车轮转动 / 尾灯）
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
    public void SetPath(List<int> edgeIds, float startDistance = 0f)
    {
        pathEdgeIds = edgeIds ?? new List<int>();
        // 路径变更时重置死锁恢复
        _deadlockRecovery?.Reset();
        StartPath(startDistance);
    }

    void StartPath(float startDistance = 0f)
    {
        if (pathEdgeIds == null || pathEdgeIds.Count == 0) return;

        currentEdgeIndex = 0;
        LoadCurrentEdge();
        currentDistOnEdge = Mathf.Clamp(startDistance, 0f, currentEdgeLength);
        currentSpeed = 0f;
        longState = LongitudinalState.FreeDrive;
        stoppedTimer = 0f;
        reverseTimer = 0f;
        _hasDestination = false;

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

        if (id >= 0 && worldModel.GlobalLanes.TryGetValue(id, out Lane lane))
            splineToBake = lane.CenterSpline;
        else if (id < 0 && worldModel.GlobalConnectors.TryGetValue(-id - 1, out LaneConnector conn))
        {
            // 优先使用离散Hermite多段线（绕过CatmullRom二次近似，端点切线绝对精准）
            if (conn.Polyline != null && conn.Polyline.Count >= 2)
            {
                currentTrajectory = new BakedTrajectory(conn.Polyline);
                currentEdgeLength = currentTrajectory.TotalLength;
                return;
            }
            splineToBake = conn.TurnCurve;
        }

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

        if (distance >= 0)
        {
            float remainingDist = distance;
            while (remainingDist > 0 && currentTrajectory != null)
            {
                float distToEdgeEnd = currentEdgeLength - currentDistOnEdge;

                if (remainingDist >= distToEdgeEnd)
                {
                    remainingDist -= distToEdgeEnd;

                    if (currentEdgeIndex < pathEdgeIds.Count - 1)
                    {
                        // 切换到下一个预规划的 edge
                        currentEdgeIndex++;
                        LoadCurrentEdge();

                        // 中间边切换失败(轨迹空)→整条路径已损毁,走全图重寻路
                        if (currentTrajectory == null || currentEdgeLength <= 0f)
                        {
                            Debug.LogWarning($"[SimpleAutoDrive] {name} 边切换失败 idx={currentEdgeIndex},触发重寻路");
                            currentSpeed = 0f;
                            _isTrajectoryLocked = false;
                            currentTrajectory = null;
                            RequestNewPath();
                            break;
                        }

                        currentDistOnEdge = 0f;
                    }
                    else
                    {
                        currentDistOnEdge = currentEdgeLength;
                        // 尝试拓扑顺延。成功则继续推进刚延伸的 edge（不丢距离）
                        if (ExtendPathSeamlessly())
                            continue;
                        // 顺延失败 → 解锁轨迹 + 全图寻路
                        currentSpeed = 0f;
                        _isTrajectoryLocked = false;
                        currentTrajectory = null;
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
        }
        else
        {
            // 倒车：沿折线后退
            float remainingDist = -distance;
            while (remainingDist > 0 && currentTrajectory != null)
            {
                if (remainingDist >= currentDistOnEdge)
                {
                    remainingDist -= currentDistOnEdge;
                    if (currentEdgeIndex > 0)
                    {
                        currentEdgeIndex--;
                        LoadCurrentEdge();
                        currentDistOnEdge = currentEdgeLength;
                    }
                    else
                    {
                        currentDistOnEdge = 0f;
                        currentSpeed = 0f;
                        break;
                    }
                }
                else
                {
                    currentDistOnEdge -= remainingDist;
                    remainingDist = 0f;
                }
            }
        }
        currentDistOnEdge = Mathf.Clamp(currentDistOnEdge, 0f, currentEdgeLength);
        // 裁剪已走过的边，防止路径无限增长(idx=82崩溃)
        TrimPassedEdges();
    }

    void TrimPassedEdges()
    {
        if (currentEdgeIndex > 10 && currentEdgeIndex < pathEdgeIds.Count)
        {
            int removeCount = currentEdgeIndex;
            pathEdgeIds.RemoveRange(0, removeCount);
            currentEdgeIndex = 0;
        }
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

        // 倒车时翻转朝向
        if (longState == LongitudinalState.Reverse)
            tangent = -tangent;

        // 3. 旋转直接对齐切线
        Quaternion targetRot = Quaternion.LookRotation(tangent);
        transform.SetPositionAndRotation(pos, targetRot);
    }

    // ==========================================
    // 直路判定：仅在 Lane（非 Connector）且远离路口时允许靠边
    // ==========================================
    /// <summary>
    /// 判断当前是否在直路上（非路口区域）。
    /// 条件：当前边是 Lane（非 Connector）且距停止线 > 15m。
    /// </summary>
    bool IsOnStraightRoad()
    {
        // 当前边必须是 Lane（正ID），不是 Connector（负ID）
        if (currentEdgeIndex < 0 || currentEdgeIndex >= pathEdgeIds.Count)
            return false;
        int curId = pathEdgeIds[currentEdgeIndex];
        if (curId < 0) return false; // Connector = 路口区域

        // 远离停止线/路口
        if (hasStopLineAhead && distToStopLine < 15f)
            return false;

        return true;
    }

    /// <summary>
    /// 获取当前车道的速度限制（m/s），从 WorldModel 读取。
    /// 返回0表示无限制（或无法获取）。
    /// </summary>
    float GetLaneSpeedLimit()
    {
        if (worldModel == null) return 0f;
        if (currentEdgeIndex < 0 || currentEdgeIndex >= pathEdgeIds.Count) return 0f;
        int curId = pathEdgeIds[currentEdgeIndex];
        if (curId < 0) return 0f; // Connector 不限速

        if (worldModel.GlobalLanes.TryGetValue(curId, out Lane lane))
            return lane.SpeedLimit / 3.6f; // km/h → m/s

        return 0f;
    }

    // ==========================================
    // 传感器采样（分支：NPC假检测 / 主车真雷达）
    // ==========================================
    void UpdateSensors()
    {
        frontSpeed = 0f;
        hasStopLineAhead = false;
        distToStopLine = 999f;
        _isYellowLight = false;

        // ★ 统一红绿灯检测（都用 WorldModel 相位状态 + 停止线）
        redLightAhead = CheckRedLightUnified();

        // ★ 停车标志检测（无交通灯路口 = StopSign）
        if (!redLightAhead && CheckStopSign())
            redLightAhead = true;

        // ★ 环岛入口让行
        if (!redLightAhead && CheckRoundaboutYield())
            redLightAhead = true;

        if (isPlayerControlled)
        {
            // 主车：真实物理雷达（后续对接ROS2）
            frontDistance = SampleFrontDistance(50f, obstacleLayers);
            pedestrianDanger = SampleFrontDistance(5f, pedestrianLayer) < 5f;
        }
        else
        {
            // NPC：真检测行人（用 Physics，性能压力小：行人数量有限）
            pedestrianDanger = CheckPedestrianDanger();
            frontDistance = CheckFrontVehicleFake();
        }

        // ★ 红灯时：用停止线距离钳制frontDistance，让状态机自动减速停车
        if (redLightAhead && hasStopLineAhead && distToStopLine > 0)
            frontDistance = Mathf.Min(frontDistance, distToStopLine);
    }

    /// <summary> 统一红绿灯检测：查WorldModel相位状态 + 停止线距离 </summary>
    bool CheckRedLightUnified()
    {
        if (worldModel == null) return false;
        RoadNode nearestNode = worldModel.GetNearestNode(transform.position);
        if (nearestNode == null) return false;
        if (nearestNode.Type != NodeType.Intersection && nearestNode.Type != NodeType.Merge) return false;

        float distToNode = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(nearestNode.WorldPos.x, 0, nearestNode.WorldPos.z));
        if (distToNode > 30f) return false;

        // 获取该路口的停止线
        StopLine stopLine = worldModel.GetNearestStopLine(nearestNode.Id, transform.position);
        distToStopLine = stopLine != null
            ? Vector3.Distance(transform.position, stopLine.Position)
            : distToNode - 5f;
        nearestStopLinePos = stopLine != null ? stopLine.Position : nearestNode.WorldPos;
        hasStopLineAhead = true;

        // 已越过停止线 → 不再拦截（防路口内锁死）
        if (distToStopLine < stopLinePassedThreshold) return false;

        // 查相位状态
        IntersectionState state = stopLine != null && stopLine.AssociatedPhaseId >= 0
            ? worldModel.GetPhaseState(stopLine.AssociatedPhaseId)
            : worldModel.GetIntersectionState(nearestNode.Id);

        // ★ 标记黄灯状态（供DilemmaZoneArbiter使用）
        if (state == IntersectionState.YellowLight)
            _isYellowLight = true;

        return (state == IntersectionState.RedLight || state == IntersectionState.YellowLight);
    }

    /// <summary>
    /// 停车标志检测：无交通灯路口(Uncontrolled)视为StopSign。
    /// 接近停止线时触发 → 停车 → 停满2秒 → 解除。
    /// </summary>
    bool CheckStopSign()
    {
        if (worldModel == null) return false;
        RoadNode nearestNode = worldModel.GetNearestNode(transform.position);
        if (nearestNode == null) return false;
        if (nearestNode.Type != NodeType.Intersection && nearestNode.Type != NodeType.Merge) return false;

        float distToNode = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(nearestNode.WorldPos.x, 0, nearestNode.WorldPos.z));
        if (distToNode > 25f) { _stopSignTimer = 0f; return false; }

        // 只对 Uncontrolled 路口（无交通灯）实施 StopSign
        IntersectionState state = worldModel.GetIntersectionState(nearestNode.Id);
        if (state != IntersectionState.Uncontrolled) { _stopSignTimer = 0f; return false; }

        // 获取停止线
        StopLine stopLine = worldModel.GetNearestStopLine(nearestNode.Id, transform.position);
        float distToLine = stopLine != null
            ? Vector3.Distance(transform.position, stopLine.Position)
            : distToNode - 5f;

        // 已越过停止线或太远
        if (distToLine < stopLinePassedThreshold || distToLine > 12f)
        {
            _stopSignTimer = 0f;
            _stopSignReleased = false; // ★ 已越过停止线，重置放行状态
            return false;
        }

        // ★ 已放行则不再拦截，直到越过停止线
        if (_stopSignReleased) return false;

        // 接近停止线 且 车速很低 → 累计停止计时
        if (distToLine < 8f && currentSpeed < 0.5f)
        {
            _stopSignTimer += Time.deltaTime;
            if (_stopSignTimer >= stopSignDuration)
            {
                _stopSignTimer = 0f;
                _stopSignReleased = true; // ★ 标记已放行，防止下一帧立即重新触发
                return false; // 停够2秒，放行
            }
        }

        // 设置停止线信息（供 OnGUI 显示）
        if (stopLine != null)
        {
            hasStopLineAhead = true;
            distToStopLine = Mathf.Min(distToStopLine, distToLine);
            nearestStopLinePos = stopLine.Position;
        }

        return true; // 需继续停止
    }

    /// <summary>
    /// 环岛入口让行检测：如果最近路口是环岛，且环岛内有其他车辆，则让行。
    /// </summary>
    bool CheckRoundaboutYield()
    {
        if (worldModel == null) return false;
        RoadNode nearestNode = worldModel.GetNearestNode(transform.position);
        if (nearestNode == null) return false;

        // 只有环岛路口才触发
        if (nearestNode.Kind != IntersectionKind.Roundabout) return false;

        float distToNode = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(nearestNode.WorldPos.x, 0, nearestNode.WorldPos.z));
        if (distToNode > 25f) return false; // 太远不检测

        // 检查环岛内是否有其他车辆（距离路口中心 < 20m 且不是自己）
        AllCars.RemoveAll(c => c == null);
        foreach (var car in AllCars)
        {
            if (car == this || car == null) continue;
            float carDistToNode = Vector3.Distance(
                new Vector3(car.transform.position.x, 0, car.transform.position.z),
                new Vector3(nearestNode.WorldPos.x, 0, nearestNode.WorldPos.z));
            if (carDistToNode < 20f)
                return true; // 环岛内有车，让行
        }

        return false;
    }

    /// <summary> NPC假检测：遍历全局车列表，前向点积判断前方车辆距离 </summary>
    float CheckFrontVehicleFake()
    {
        float minDist = 30f;
        Vector3 myPos = transform.position;
        Vector3 myFwd = transform.forward;
        // 清理null引用
        AllCars.RemoveAll(c => c == null);

        foreach (var car in AllCars)
        {
            if (car == this || car == null) continue;
            Vector3 toOther = car.transform.position - myPos;
            toOther.y = 0;
            float dist = toOther.magnitude;
            if (dist > 30f || dist < 0.1f) continue;

            // 前向判断：在前方锥形内（dot > cos(45°) ≈ 0.7）
            float dot = Vector3.Dot(toOther.normalized, myFwd);
            if (dot < 0.7f) continue;

            // 横向判断：同一道路（侧向偏移 < 4m）
            float lateralDist = Mathf.Abs(Vector3.Cross(myFwd, toOther).y);
            if (lateralDist > 4f) continue;

            if (dist < minDist) minDist = dist;
        }
        return minDist;
    }

    /// <summary> NPC真检测：前方3m内是否有行人（Physics.OverlapSphere） </summary>
    bool CheckPedestrianDanger()
    {
        if (pedestrianLayer.value == 0) return false;
        Vector3 checkPos = transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;
        Collider[] hits = Physics.OverlapSphere(checkPos, 2.5f, pedestrianLayer);
        foreach (var hit in hits)
        {
            if (!hit.transform.IsChildOf(transform))
                return true;
        }
        return false;
    }

    // ==========================================
    // 纯拓扑路径顺延：完全不看空间距离，图论说连着哪就无脑接哪
    // ==========================================
    bool ExtendPathSeamlessly()
    {
        // 有明确目的地时，禁止随机拓扑顺延覆盖
        if (_hasDestination) return false;

        int lastId = pathEdgeIds[pathEdgeIds.Count - 1];
        if (lastId > 0 && worldModel.GlobalLanes.TryGetValue(lastId, out Lane lane))
        {
            if (lane.NextConnectorIds != null && lane.NextConnectorIds.Count > 0)
            {
                int randConn = lane.NextConnectorIds[Random.Range(0, lane.NextConnectorIds.Count)];
                if (worldModel.GlobalConnectors.TryGetValue(randConn, out LaneConnector conn))
                {
                    pathEdgeIds.Add(-conn.ConnectorId - 1);
                    pathEdgeIds.Add(conn.ToLaneId);
                    currentEdgeIndex++;
                    LoadCurrentEdge();

                    // 防止僵尸状态：connector或lane轨迹烧录失败必须回滚
                    if (currentTrajectory == null || currentEdgeLength <= 0f)
                    {
                        pathEdgeIds.RemoveRange(pathEdgeIds.Count - 2, 2);
                        currentEdgeIndex--;
                        LoadCurrentEdge(); // 退回上一段边
                        currentDistOnEdge = currentEdgeLength;
                        Debug.LogWarning($"[SimpleAutoDrive] {name} 延伸失败(轨迹空),已回滚");
                        return false;
                    }

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
        // 轨迹锁死状态下，绝对禁止重新寻路
        if (_isTrajectoryLocked && currentTrajectory != null) return;

        if (worldModel == null || pathPlanner == null) return;

        // 优先用方向过滤找到同向车道
        int startLaneId = worldModel.FindNearestLane(transform.position, transform.forward);
        if (startLaneId < 0)
            startLaneId = worldModel.FindNearestLane(transform.position);

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

        // 兜底：按车辆当前位置重新锚定到最近车道
        {
            int laneId = worldModel.FindNearestLane(transform.position, transform.forward);
            if (laneId < 0)
                laneId = worldModel.FindNearestLane(transform.position);
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

    /// <summary> 死循环突破：强制找一个远离当前位置且有多个邻居的节点作为目的地 </summary>
    void ForceNewDestination()
    {
        if (worldModel == null || pathPlanner == null) return;
        _isTrajectoryLocked = false;
        _hasDestination = false;

        int startLaneId = worldModel.FindNearestLane(transform.position, transform.forward);
        if (startLaneId < 0)
            startLaneId = worldModel.FindNearestLane(transform.position);
        if (startLaneId < 0) return;

        // 找一个远离当前且非死路的节点（>=2个邻居）
        RoadNode bestNode = null;
        float bestDist = 0f;
        for (int i = 0; i < 30; i++)
        {
            int randId = Random.Range(0, worldModel.NodeCount);
            RoadNode node = worldModel.GetNode(randId);
            if (node == null || node.NeighborIds == null || node.NeighborIds.Count <= 1) continue;
            float d = Vector3.Distance(transform.position, node.WorldPos);
            if (d < 50f) continue;
            if (d > bestDist)
            {
                bestDist = d;
                bestNode = node;
            }
        }

        if (bestNode != null)
        {
            int endLaneId = worldModel.FindNearestLane(bestNode.WorldPos);
            if (endLaneId >= 0 && endLaneId != startLaneId)
            {
                List<int> edgePath = pathPlanner.PlanEdgePath(startLaneId, endLaneId);
                if (edgePath != null && edgePath.Count > 0)
                {
                    _hasDestination = true;
                    SetPath(edgePath);
                    Debug.Log($"[SimpleAutoDrive] {name} 强制跳转至 {bestNode.WorldPos} (距离{bestDist:F0}m)");
                    return;
                }
            }
        }

        // 兜底：直接走 RequestNewPath
        RequestNewPath();
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
        {
            _hasDestination = true;
            SetPath(edgePath);
        }
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
        _subsumptionEngine?.ResetDebounce();
        _deadlockRecovery?.Reset();
        _pullOverHandler?.Reset();
        LoadCurrentEdge();
    }

    public LongitudinalState GetCurrentLongState() => longState;
    public DriveState GetCurrentState() => currentState;

    public void ToggleAutoDrive()
    {
        isPlayerControlled = !isPlayerControlled;
    }

    /// <summary>
    /// 手动→自动：解锁轨迹，吸附到最近车道，重新规划路径
    /// </summary>
    public void MergeBackToTraffic()
    {
        if (worldModel == null || pathPlanner == null) return;

        // 1. 找最近车道
        int nearestLaneId = worldModel.FindNearestLane(transform.position, transform.forward);
        if (nearestLaneId < 0)
            nearestLaneId = worldModel.FindNearestLane(transform.position);
        if (nearestLaneId < 0)
        {
            Debug.LogWarning($"[SimpleAutoDrive] {name} 回归失败：找不到最近车道");
            return;
        }

        // 2. 找最近边
        if (!worldModel.GlobalLanes.TryGetValue(nearestLaneId, out Lane lane) || lane.CenterSpline == null)
        {
            Debug.LogWarning($"[SimpleAutoDrive] {name} 回归失败：车道{nearestLaneId}不存在");
            return;
        }

        // 3. 吸附位置到车道上
        Vector3 snappedPos = lane.CenterSpline.GetClosestPoint(transform.position);
        snappedPos.y = worldModel.GetUnifiedHeight(snappedPos.x, snappedPos.z) + 0.15f;
        transform.position = snappedPos;

        // 4. 对齐车道方向 ★ 修复：将距离(米)转为归一化t(0-1)再查切线
        float distOnLane = lane.CenterSpline.GetDistanceAtPoint(snappedPos);
        float normalizedT = lane.CenterSpline.GetTFromLength(distOnLane);
        Vector3 tangent = lane.CenterSpline.GetTangent(normalizedT);
        if (tangent.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(tangent);

        // 5. 解锁轨迹并重新规划
        _isTrajectoryLocked = false;
        _hasDestination = false;
        currentSpeed = 0f;
        currentDistOnEdge = 0f;
        longState = LongitudinalState.FreeDrive;
        _subsumptionEngine?.ResetDebounce();
        _deadlockRecovery?.Reset();
        _pullOverHandler?.Reset();

        // 6. 设定起始车道 + 规划路径
        pathEdgeIds.Clear();
        pathEdgeIds.Add(nearestLaneId);
        currentEdgeIndex = 0;

        // 预先烘焙该车道
        currentTrajectory = new BakedTrajectory(lane.CenterSpline, 0.5f);
        currentEdgeLength = currentTrajectory.TotalLength;

        // 设置当前距离为吸附点距离
        currentDistOnEdge = Mathf.Clamp(distOnLane, 0f, currentEdgeLength);
        _isTrajectoryLocked = true;

        Debug.Log($"[SimpleAutoDrive] {name} 回归车流 → 车道{nearestLaneId} 距离{distOnLane:F1}m");

        // 7. 继续延伸到目的地
        RequestNewPath();
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

    // ==========================================
    // 实时状态面板 (OnGUI) — 主车专用
    // ==========================================
    void OnGUI()
    {
        if (!isPlayerControlled) return;
        if (!_showInfoPanel) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.fontSize = 14;
        boxStyle.normal.textColor = Color.white;

        GUIStyle warnStyle = new GUIStyle(GUI.skin.label);
        warnStyle.fontSize = 14;
        warnStyle.normal.textColor = Color.yellow;

        GUIStyle errStyle = new GUIStyle(GUI.skin.label);
        errStyle.fontSize = 14;
        errStyle.normal.textColor = Color.red;

        GUIStyle okStyle = new GUIStyle(GUI.skin.label);
        okStyle.fontSize = 14;
        okStyle.normal.textColor = Color.green;

        float panelW = 420f;
        float panelH = 420f;
        Rect panelRect = new Rect(10, 10, panelW, panelH);
        GUI.Box(panelRect, $"[{name}] 实时状态", boxStyle);

        float y = 35f;
        float lineH = 22f;
        float x0 = 20f;

        // ---- 轨迹状态 ----
        bool trajOk = (currentTrajectory != null && currentEdgeLength > 0.001f);
        GUI.Label(new Rect(x0, y, 380, lineH),
            $"轨迹: {(trajOk ? "OK" : "NULL/空")} | 边长度={currentEdgeLength:F2}m",
            trajOk ? okStyle : errStyle);
        y += lineH;

        GUI.Label(new Rect(x0, y, 380, lineH),
            $"进度: curDist={currentDistOnEdge:F2} / {currentEdgeLength:F2}  ({currentT:P1})",
            boxStyle);
        y += lineH;

        // ---- 路径信息 ----
        int curId = (currentEdgeIndex >= 0 && currentEdgeIndex < pathEdgeIds.Count) ? pathEdgeIds[currentEdgeIndex] : -999;
        string curSegDesc = curId >= 0 ? $"车道#{curId}" : (curId < 0 ? $"连接器#{(-curId - 1)}" : "无效");
        GUI.Label(new Rect(x0, y, 380, lineH),
            $"路径: idx={currentEdgeIndex}/{pathEdgeIds.Count} | 当前段={curSegDesc}",
            curId == -999 ? errStyle : boxStyle);
        y += lineH;

        // 显示后续3段路径
        GUI.Label(new Rect(x0, y, 380, lineH), "后续3段:", boxStyle);
        y += lineH;
        for (int i = 1; i <= 3; i++)
        {
            int ni = currentEdgeIndex + i;
            if (ni < pathEdgeIds.Count)
            {
                int nid = pathEdgeIds[ni];
                string desc = nid >= 0 ? $"车道#{nid}" : $"连接器#{(-nid - 1)}";
                GUI.Label(new Rect(x0 + 10, y, 370, lineH), $"[{i}] {desc}", okStyle);
            }
            else
            {
                GUI.Label(new Rect(x0 + 10, y, 370, lineH), $"[{i}] ---(路径末端)---", warnStyle);
            }
            y += lineH;
        }
        y += 4f;

        // ---- 当前车道/连接器详情 ----
        if (curId >= 0 && worldModel.GlobalLanes.TryGetValue(curId, out Lane curLane))
        {
            GUI.Label(new Rect(x0, y, 380, lineH),
                $"当前车道: ID={curLane.LaneId} Road={curLane.RoadId} Dir={curLane.Direction}",
                boxStyle);
            y += lineH;
            int connCount = (curLane.NextConnectorIds != null) ? curLane.NextConnectorIds.Count : 0;
            GUI.Label(new Rect(x0, y, 380, lineH),
                $"下一连接器数: {connCount}",
                connCount > 0 ? okStyle : warnStyle);
            y += lineH;
        }
        else if (curId < 0 && worldModel.GlobalConnectors.TryGetValue(-curId - 1, out LaneConnector curConn))
        {
            GUI.Label(new Rect(x0, y, 380, lineH),
                $"当前连接器: ID={curConn.ConnectorId} Type={curConn.TurnType} From={curConn.FromLaneId} To={curConn.ToLaneId}",
                boxStyle);
            y += lineH;
        }
        y += 4f;

        // ---- 速度/状态 ----
        GUI.Label(new Rect(x0, y, 380, lineH),
            $"速度: {currentSpeed:F2}m/s ({currentSpeed * 3.6f:F1}km/h) | 锁: {_isTrajectoryLocked}",
            boxStyle);
        y += lineH;

        string stateStr = $"纵向状态: {longState} | 包容层: L{_subsumptionEngine?.ActiveLayer ?? 0} | 驱动: {currentState}";
        GUI.Label(new Rect(x0, y, 380, lineH), stateStr,
            (longState == LongitudinalState.FreeDrive) ? okStyle :
            (longState == LongitudinalState.Stopped) ? errStyle : warnStyle);
        y += lineH;

        // ★ ROS2 AEB 状态
        bool aebActive = _specialSituations != null && _specialSituations.IsExternalAEBActive();
        GUI.Label(new Rect(x0, y, 380, lineH),
            $"ROS2 AEB: {(aebActive ? "★ 紧急制动中 ★" : "待命")}",
            aebActive ? errStyle : okStyle);
        y += lineH;

        // ---- 红绿灯/停止线 ----
        string lightStr = _isYellowLight ? "黄灯" : (redLightAhead ? "红灯" : "无/绿灯");
        string stopSignStr = _stopSignTimer > 0f ? $" StopSign:{_stopSignTimer:F1}s/{stopSignDuration}s" : "";
        GUI.Label(new Rect(x0, y, 380, lineH),
            $"红绿灯: {lightStr}{stopSignStr} | 停止线距: {(hasStopLineAhead ? $"{distToStopLine:F1}m" : "无")} | 困境区: {(_dilemmaZoneArbiter?.IsInDilemmaZone ?? false)}",
            redLightAhead ? errStyle : okStyle);
        y += lineH;
        GUI.Label(new Rect(x0, y, 380, lineH),
            $"限速: {GetLaneSpeedLimit() * 3.6f:F0}km/h | 前车距离: {frontDistance:F1}m | 行人危险: {pedestrianDanger} | 让行: {_emergencyYieldHandler?.IsYielding ?? false}",
            frontDistance < safeDistance ? warnStyle : okStyle);
        y += lineH;
        GUI.Label(new Rect(x0, y, 380, lineH),
            $"靠边偏移: {_pullOverHandler?.CurrentOffset ?? 0f:F2}m | 靠边中: {_pullOverHandler?.IsActive ?? false} | 直路: {IsOnStraightRoad()}",
            (_pullOverHandler?.IsActive ?? false) ? warnStyle : okStyle);
        y += lineH;

        GUI.Label(new Rect(x0, y, 380, lineH),
            $"恢复计时: {_recoveryTimer:F1}s / {recoveryInterval}s | 位置: ({transform.position.x:F1},{transform.position.z:F1})",
            boxStyle);
        y += lineH;

        GUI.Label(new Rect(x0, y, 380, lineH),
            $"路径总段数: {pathEdgeIds.Count} | hasDest: {_hasDestination}",
            boxStyle);
    }
}