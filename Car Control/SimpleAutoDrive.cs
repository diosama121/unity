using UnityEngine;

[RequireComponent(typeof(SimpleCarController))]
public partial class SimpleAutoDrive : MonoBehaviour
{
    // ==========================================
    // 状态枚举
    // ==========================================
    public enum DriveState { Cruising, Transit, Stopping, Reversing, Crashed }

    [Header("组件引用")]
    public PathPlanner pathPlanner;

    [Header("控制参数")]
    public float targetSpeed      = 12f;
    public float safeDistance     = 8f;
    public bool  dynamicLookAhead = true;
    public float lookAheadMin     = 3f;
    public float lookAheadMax     = 12f;

    [Header("传感器设置")]
    public float sensorForwardOffset = 2.5f;

    [Header("状态机")]
    public DriveState currentState = DriveState.Cruising;
    public bool isPlayerControlled = false;

    [Header("调试信息")]
    public float currentT           = 0f;
    public bool  obstacleDetected   = false;
    public int   currentLaneId      = -1;
    public int   currentConnectorId = -1;
    public bool  isYielding         = false;
    public IntersectionState currentIntersectionState = IntersectionState.Uncontrolled;

    // --- 内部引用 ---
    private SimpleCarController carController;
    private MasterUIManager     _uiManager;
    private LineRenderer        trajectoryLine;
    private Vector3[]           trajectoryPoints = new Vector3[20];

    // --- 路径跟随 ---
    public CatmullRomSpline currentSpline;
    private int   currentDestinationNodeId = -1;
    private int   lastNodeId               = -1;
    private float rerouteCooldown          = 0f;
    private bool  isFetchingNextPath       = false;
    private Vector3 lastLaneCheckPos       = Vector3.one * -9999f;

    // --- 红绿灯停车 ---
    private int     nearestIntersectionNodeId = -1;
    private Vector3 stopTargetPosition        = Vector3.zero;
    private bool    hasStopTarget             = false;

    // --- 倒车脱困 ---
    private float avoidCooldown  = 0f;
    private bool  isReversing    = false;
    private float reverseTimer   = 0f;
    private int   reverseCount   = 0;
    private float escapeSteering = 0f;

    // --- 诊断 ---
    private float _diagTimer = 0f;

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
    }

    // ==========================================
    // 主循环：感知 → 状态机 → 下发指令
    // ==========================================
    void Update()
    {
        // 传感器和可视化始终运行（即使被玩家接管也可见）
        UpdatePerception();
        UpdateTrajectoryLine();
        DrawLidarRays();

        VehicleCommand cmd = default;

        if (isPlayerControlled)
        {
            // 玩家直接输入
            cmd.throttle  = Input.GetAxis("Vertical");
            cmd.steering  = Input.GetAxis("Horizontal");
            cmd.isBraking = Input.GetKey(KeyCode.Space);
        }
        else
        {
            // 1. 感知层：获取前方距离、红绿灯状态
            UpdatePerception();

            // 2. 状态机评估：决定下一个 currentState
            EvaluateState();

            // 3. 执行状态：计算并输出 Command
            switch (currentState)
            {
                case DriveState.Cruising: cmd = HandleCruising();   break;
                case DriveState.Transit:  cmd = HandleTransit();    break;
                case DriveState.Stopping: cmd = HandleStopping();   break;
                case DriveState.Reversing: cmd = HandleReversing(); break;
                case DriveState.Crashed:   cmd = HandleCrashed();   break;
                default:
                    cmd.throttle = 0f; cmd.steering = 0f; break;
            }
        }

        // 4. 统一向下发送指令
        carController.ApplyCommand(cmd);
    }

    // ==========================================
    // 感知层
    // ==========================================
    void UpdatePerception()
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
                }
                break;
            }
        }

        // 让行检测
        isYielding = false;
        if (carController.vehiclePriority == VehiclePriority.Normal && currentState == DriveState.Cruising)
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

        // 红绿灯感知
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
                        currentIntersectionState = WorldModel.Instance.GetPhaseState(relevantStopLine.AssociatedPhaseId);
                    else
                        currentIntersectionState = IntersectionState.Uncontrolled;
                }
                else currentIntersectionState = IntersectionState.Uncontrolled;
            }
            else currentIntersectionState = IntersectionState.Uncontrolled;
        }
        else currentIntersectionState = IntersectionState.Uncontrolled;
    }

    // ==========================================
    // 状态机评估
    // ==========================================
    void EvaluateState()
    {
        // 障碍物倒车脱困
        if (obstacleDetected && avoidCooldown <= 0f && currentState == DriveState.Cruising)
        {
            currentState = DriveState.Reversing;
            isReversing  = true;
            reverseTimer = 0f;
            return;
        }

        // 红绿灯停车
        if (currentIntersectionState == IntersectionState.RedLight || currentIntersectionState == IntersectionState.YellowLight)
        {
            int stopNodeId = (nearestIntersectionNodeId >= 0) ? nearestIntersectionNodeId : currentDestinationNodeId;
            if (stopNodeId >= 0 && WorldModel.Instance != null)
            {
                StopLine relevantStopLine = WorldModel.Instance.GetNearestStopLine(stopNodeId, transform.position);
                if (relevantStopLine != null)
                {
                    stopTargetPosition = relevantStopLine.Position;
                    hasStopTarget      = true;
                }
            }
            currentState = DriveState.Stopping;
            return;
        }

        // 灯变绿，从 Stopping 恢复
        if (currentState == DriveState.Stopping
            && (currentIntersectionState == IntersectionState.GreenLight || currentIntersectionState == IntersectionState.Uncontrolled))
        {
            hasStopTarget = false;
            currentState  = DriveState.Cruising;
            return;
        }

        // 从 Reversing 恢复
        if (currentState == DriveState.Reversing && !isReversing)
        {
            currentState = DriveState.Cruising;
            return;
        }

        // 默认：巡航
        if (currentState != DriveState.Transit && currentState != DriveState.Reversing && currentState != DriveState.Stopping && currentState != DriveState.Crashed)
            currentState = DriveState.Cruising;
    }

    // ==========================================
    // 状态实现：巡航（沿线行驶）
    // ==========================================
    VehicleCommand HandleCruising()
    {
        VehicleCommand cmd = default;

        if (currentSpline == null || currentSpline.TotalLength <= 0)
        {
            return cmd; // 停住
        }

        // --- 冷却 ---
        rerouteCooldown -= Time.deltaTime;

        // --- 诊断 ---
        _diagTimer += Time.deltaTime;
        if (_diagTimer >= 3f)
        {
            _diagTimer = 0f;
            Debug.Log($"[AutoDrive] {name} | Cruising | T={currentT:F3} speed={carController.GetSpeed():F1} tgtSpeed={targetSpeed:F1} splineLen={currentSpline.TotalLength:F1}");
        }

        // --- Pure Pursuit 转向（算 target T → 算 localTarget → 算 steering）---
        float currentSpeed  = carController.GetSpeed();
        float lookAheadDist = dynamicLookAhead
            ? Mathf.Clamp(Mathf.Abs(currentSpeed) * 0.5f, 3f, 12f)
            : 5f;
        float lookT     = Mathf.Clamp01(currentT + lookAheadDist / currentSpline.TotalLength);
        Vector3 targetPt = currentSpline.GetPoint(lookT);
        Vector3 localPt  = transform.InverseTransformPoint(targetPt);
        cmd.steering     = Mathf.Clamp(localPt.x / 4f, -1f, 1f);

        // --- 油门 ---
        cmd.throttle = targetSpeed / carController.maxSpeed;

        // --- T 值推进 ---
        float realT = currentSpline.GetClosestT(transform.position, currentT);
        currentT = Mathf.Clamp01(Mathf.Max(realT - 0.02f, currentT));

        // --- 车道刷新 ---
        if (Vector3.Distance(lastLaneCheckPos, transform.position) > 5f || currentT > 0.8f)
        {
            lastLaneCheckPos = transform.position;
            if (WorldModel.Instance != null)
                currentLaneId = WorldModel.Instance.FindNearestLane(transform.position);
        }

        // --- 驶出车道：尝试切入路口连接器 ---
        if (currentT >= 0.98f && currentLaneId >= 0
            && WorldModel.Instance != null
            && WorldModel.Instance.GlobalLanes.TryGetValue(currentLaneId, out Lane currentLane))
        {
            if (currentLane.NextConnectorIds != null && currentLane.NextConnectorIds.Count > 0)
            {
                int nextConnId = currentLane.NextConnectorIds[Random.Range(0, currentLane.NextConnectorIds.Count)];
                if (WorldModel.Instance.GlobalConnectors.TryGetValue(nextConnId, out LaneConnector conn))
                {
                    currentConnectorId = conn.ConnectorId;
                    currentSpline      = conn.TurnCurve;
                    currentState       = DriveState.Transit;
                    currentT           = 0f;
                    return cmd;
                }
            }
            else
            {
                // 死胡同，停车
                currentState = DriveState.Stopping;
            }
        }

        // --- 接近终点 → 请求新路径 ---
        if (currentT >= 0.95f && !isFetchingNextPath && rerouteCooldown <= 0f)
        {
            rerouteCooldown    = 1f;
            isFetchingNextPath = true;
            RequestNewRandomPath();
        }

        return cmd;
    }

    // ==========================================
    // 状态实现：路口过渡（沿 Connector 行驶）
    // ==========================================
    VehicleCommand HandleTransit()
    {
        VehicleCommand cmd = default;

        if (currentSpline == null || currentSpline.TotalLength <= 0)
        {
            currentState = DriveState.Cruising;
            return cmd;
        }

        // --- Pure Pursuit 转向（与 Cruising 一致）---
        float currentSpeed  = carController.GetSpeed();
        float lookAheadDist = dynamicLookAhead
            ? Mathf.Clamp(Mathf.Abs(currentSpeed) * 0.5f, 3f, 12f)
            : 5f;
        float lookT     = Mathf.Clamp01(currentT + lookAheadDist / currentSpline.TotalLength);
        Vector3 targetPt = currentSpline.GetPoint(lookT);
        Vector3 localPt  = transform.InverseTransformPoint(targetPt);
        cmd.steering     = Mathf.Clamp(localPt.x / 4f, -1f, 1f);

        // --- 油门（路口内限速 60%）---
        cmd.throttle = (targetSpeed / carController.maxSpeed) * 0.6f;

        // --- T 值推进 ---
        float realT = currentSpline.GetClosestT(transform.position, currentT);
        currentT = Mathf.Clamp01(Mathf.Max(realT - 0.02f, currentT));

        // --- 驶出路口：切回目标车道 ---
        if (currentT >= 0.98f && currentConnectorId >= 0
            && WorldModel.Instance != null
            && WorldModel.Instance.GlobalConnectors.TryGetValue(currentConnectorId, out LaneConnector conn))
        {
            if (WorldModel.Instance.GlobalLanes.TryGetValue(conn.ToLaneId, out Lane exitLane))
            {
                currentLaneId      = exitLane.LaneId;
                currentSpline      = exitLane.CenterSpline;
                currentState       = DriveState.Cruising;
                currentT           = 0f;
                currentConnectorId = -1;
            }
        }

        return cmd;
    }

    // ==========================================
    // 状态实现：停车（红绿灯）
    // ==========================================
    VehicleCommand HandleStopping()
    {
        VehicleCommand cmd = new VehicleCommand { throttle = 0f, steering = 0f, isBraking = true };

        // 诊断
        _diagTimer += Time.deltaTime;
        if (_diagTimer >= 3f)
        {
            _diagTimer = 0f;
            Debug.Log($"[AutoDrive] {name} | STOPPING | intx={currentIntersectionState} hasStop={hasStopTarget} nodeId={nearestIntersectionNodeId} dist={Vector3.Distance(transform.position, stopTargetPosition):F2}");
        }

        // 灯已变绿 → 状态机会在下一帧切回 Cruising
        if (currentIntersectionState == IntersectionState.GreenLight || currentIntersectionState == IntersectionState.Uncontrolled)
        {
            hasStopTarget = false;
            return new VehicleCommand(); // 释放刹车
        }

        return cmd;
    }

    // ==========================================
    // 状态实现：倒车脱困
    // ==========================================
    VehicleCommand HandleReversing()
    {
        VehicleCommand cmd = new VehicleCommand();

        if (isReversing)
        {
            reverseTimer += Time.deltaTime;
            cmd.throttle  = -0.5f;
            cmd.steering  = escapeSteering;

            if (reverseTimer >= 1.2f + reverseCount * 0.5f)
            {
                reverseCount++;
                isReversing    = false;
                reverseTimer   = 0f;
                avoidCooldown  = 2.5f;

                if (currentSpline != null && currentSpline.TotalLength > 0)
                {
                    currentT = currentSpline.GetClosestT(transform.position, currentT);
                    currentT = Mathf.Max(0, currentT - 0.12f);
                }

                return new VehicleCommand(); // 停一下再切回
            }
            return cmd;
        }

        // 不再有障碍物 → 恢复
        if (!obstacleDetected)
        {
            isReversing   = false;
            reverseTimer  = 0f;
            avoidCooldown = 1f;
            return new VehicleCommand();
        }

        // 仍有障碍 → 继续倒车
        isReversing  = true;
        reverseTimer = 0f;
        cmd.throttle = -0.5f;
        return cmd;
    }

    // ==========================================
    // 状态实现：碰撞停止
    // ==========================================
    VehicleCommand HandleCrashed()
    {
        return new VehicleCommand { isBraking = true };
    }

    // ==========================================
    // ToggleAutoDrive（兼容 MasterUIManager 旧调用）
    // ==========================================
    public void ToggleAutoDrive()
    {
        isPlayerControlled = !isPlayerControlled;
    }

    // ==========================================
    // 路径管理
    // ==========================================
    public void SetSplinePath(CatmullRomSpline spline, int destinationNodeId)
    {
        lastNodeId               = currentDestinationNodeId;
        currentDestinationNodeId = destinationNodeId;
        currentSpline            = spline;
        currentT                 = spline.TotalLength > 0
            ? currentSpline.GetClosestT(transform.position, 0f)
            : 0f;
        currentState = DriveState.Cruising;
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
            int randId           = Random.Range(0, WorldModel.Instance.NodeCount);
            RoadNode targetNode  = WorldModel.Instance.GetNode(randId);
            if (targetNode == null || targetNode.NeighborIds == null || targetNode.NeighborIds.Count <= 1) continue;
            if (Vector3.Distance(transform.position, targetNode.WorldPos) < 20f) continue;

            Vector3 dirToTarget = (targetNode.WorldPos - transform.position).normalized;
            if (Vector3.Dot(transform.forward, dirToTarget) < 0.3f) continue;

            CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, targetNode.WorldPos, transform.forward);
            if (newSpline == null || newSpline.TotalLength <= 0) continue;

            Vector3 newStartTangent = (newSpline.GetPoint(0.05f) - newSpline.GetPoint(0f)).normalized;
            if (Vector3.Dot(transform.forward, newStartTangent) < 0.7f) continue;

            SetSplinePath(newSpline, targetNode.Id);
            isFetchingNextPath = false;
            return;
        }

        // fallback: 走邻居节点
        RoadNode nearest = WorldModel.Instance.GetNearestNode(transform.position);
        if (nearest?.NeighborIds != null)
        {
            foreach (int nbId in nearest.NeighborIds)
            {
                if (nbId == lastNodeId) continue;
                RoadNode nbNode = WorldModel.Instance.GetNode(nbId);
                Vector3 dir = (nbNode.WorldPos - transform.position).normalized;
                if (Vector3.Dot(transform.forward, dir) <= 0.1f) continue;

                CatmullRomSpline fallback = pathPlanner.PlanPathSpline(transform.position, nbNode.WorldPos, transform.forward);
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
            currentState = DriveState.Cruising; // 停住等待
            targetSpeed  = 0f;
        }
        else targetSpeed = 2f;

        isFetchingNextPath = false;
    }

    public void SetDestination(Vector3 destination)
    {
        if (pathPlanner == null) return;
        CatmullRomSpline newSpline = pathPlanner.PlanPathSpline(transform.position, destination, transform.forward);
        if (newSpline != null && newSpline.TotalLength > 0)
        {
            currentSpline = newSpline;
            currentT      = 0f;
            currentState  = DriveState.Cruising;
        }
    }

    public void ResetNavigation() => RequestNewRandomPath();

    public void ResetStateMachine()
    {
        isReversing    = false;
        reverseTimer   = 0f;
        avoidCooldown  = 0f;
        hasStopTarget  = false;
        reverseCount   = 0;
        currentDestinationNodeId = -1;

        if (currentSpline != null && currentSpline.TotalLength > 0)
        {
            currentT     = currentSpline.GetClosestT(transform.position, 0f);
            currentT     = Mathf.Clamp01(currentT);
            currentState = DriveState.Cruising;
        }
        else currentState = DriveState.Cruising;
    }

    public DriveState GetCurrentState() => currentState;

    // ==========================================
    // 可视化
    // ==========================================
    void UpdateTrajectoryLine()
    {
        if (trajectoryLine == null || trajectoryPoints == null) return;
        Vector3 origin = transform.position + Vector3.up * 0.3f;

        if (isReversing)
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

        float speed     = Mathf.Abs(carController.GetSpeed());
        float lookDist  = Mathf.Clamp(speed * 0.8f, 5f, 15f);
        float steerAng  = carController.currentSteeringAngle;

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

        if (currentState == DriveState.Stopping)
        {
            trajectoryLine.startColor = new Color(1f, 0.3f, 0.1f, 0.7f);
            trajectoryLine.endColor   = new Color(1f, 0.3f, 0.1f, 0.1f);
        }
        else if (isYielding)
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
            if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 1.5f, transform.forward, out RaycastHit hit, safeDistance))
            {
                Gizmos.color = new Color(0, 1f, 0.4f, 0.6f);
                Gizmos.DrawWireCube(hit.point, new Vector3(2.2f, 1.5f, 4.5f));
            }
        }
    }
}