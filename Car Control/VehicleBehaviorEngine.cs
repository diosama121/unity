using System.Reflection;
using UnityEngine;

/// <summary>
/// 车辆行为决策引擎：Subsumption 有限状态机，顶层接管 targetSpeed 与紧急控制。
/// 不修改 A* 寻路与样条生成逻辑。
/// </summary>
public enum DriveState
{
    FatalCrashed,
    EmergencyAvoid,
    Intersection,
    DeadlockEscape,
    PlatoonMerge,
    DynamicCruising
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(SimpleAutoDrive))]
[RequireComponent(typeof(SimpleCarController))]
public class VehicleBehaviorEngine : MonoBehaviour
{
    public DriveState currentState = DriveState.DynamicCruising;
[Header("性能与缓存")]
public float radarScanInterval = 0.15f;
private float radarTimer = 0f;
private bool cachedPedestrianDetected = false;
private bool cachedAmbulanceDetected = false;
private bool cachedTrafficAhead = false;
 private bool isReversingEscape = false;
    [Header("速度基准")]
    public float cruiseSpeedBase = 15f;
    public float minCruiseSpeed = 2f;

    [Header("探测参数")]
    public float pedestrianDetectRange = 18f;
    public float crosswalkDetectRange = 12f;
    public float intersectionApproachRange = 35f;
    public float emergencyVehicleRange = 40f;
    public float frontTrafficRange = 50f;
    public float mergeDetectRange = 25f;
    public float blindSpotRayRange = 12f;
    public float boundaryRayHeight = 0.5f;

    [Header("制动与物理")]
    public float maxBrakeDecel = 10f;
    public float aebBrakeDecel = 12f;
    public float yellowLightReactionTime = 1f;
    public float fatalBoundaryMargin = 0.4f;

    [Header("死锁脱困")]
    public float deadlockSpeedThreshold = 0.8f;
    public float deadlockTimeToTrigger = 3.5f;
    public float overtakeLateralSteer = 0.65f;
    public float reverseThrottle = -0.35f;
    public float reverseDuration = 1.2f;

    [Header("车队与汇入")]
    public float safeTimeHeadway = 1.8f;
    public float minFollowGap = 6f;
    public float stopToGoConfirmTime = 1.5f;
    public float densitySlowFactor = 0.55f;

    [Header("动态巡航")]
    public float curvatureSpeedScale = 8f;
    public float sharpCurveThreshold = 0.12f;
    public float blindSpotSpeedReduction = 0.7f;

    private SimpleAutoDrive autoDrive;
    private SimpleCarController carController;

    private float deadlockTimer;
    private float stopToGoTimer;
    private float reverseEscapeTimer;
    private float overtakeBezierU;

    private Vector3 lastPosition;
    private FieldInfo splineField;
    private float lastZipperYieldTime;
    private static int globalZipperToken;

    private ProceduralRoadBuilder cachedRoadBuilder;
    private float cachedRoadHalfWidth = 3f;

    private LayerMask obstacleMask = ~0;
    private bool pendingBrakeOverride;
    private float pendingBrakeDecel;
    private float pendingSteer;
    private float pendingThrottle;

    void Start()
    {
        autoDrive = GetComponent<SimpleAutoDrive>();
        if (autoDrive == null) autoDrive = GetComponentInChildren<SimpleAutoDrive>();

        carController = GetComponent<SimpleCarController>();
        if (carController == null) carController = GetComponentInChildren<SimpleCarController>();

        splineField = typeof(SimpleAutoDrive).GetField("currentSpline", BindingFlags.Instance | BindingFlags.NonPublic);

        if (autoDrive != null)
            cruiseSpeedBase = autoDrive.targetSpeed;

        lastPosition = transform.position;

        // 缓存道路宽度，避免每帧 FindObjectOfType
        cachedRoadBuilder = FindObjectOfType<ProceduralRoadBuilder>();
        if (cachedRoadBuilder != null) 
            cachedRoadHalfWidth = cachedRoadBuilder.roadWidth * 0.5f;
    }

   void Update()
{
    if (autoDrive == null || carController == null) return;
    if (!carController.autoMode && autoDrive.currentState != SimpleAutoDrive.DriveState.RemoteControlled) return;

    // --- 【补丁：雷达降频】 ---
    radarTimer += Time.deltaTime;
    if (radarTimer >= radarScanInterval)
    {
        radarTimer = 0f;
        // 集中在这一帧做物理相交计算
        cachedPedestrianDetected = DetectPedestrianOrCrosswalk();
        cachedAmbulanceDetected = DetectAmbulance();
        cachedTrafficAhead = DetectTrafficAhead();
    }

    pendingBrakeOverride = false;
    pendingSteer = 0f;
    pendingThrottle = 0f;
    if (currentState == DriveState.FatalCrashed) return;

    if (CheckBoundaryFatal())
    {
        currentState = DriveState.FatalCrashed;
        ExecuteFatalCrash();
        return;
    }

    TickTimers();

    // --- 【补丁：使用缓存数据进行交规判定】 ---
    if (cachedPedestrianDetected)
        ChangeState(DriveState.EmergencyAvoid);
    else if (DetectApproachingIntersection() || cachedAmbulanceDetected) // 接近路口暂不缓存，因为依赖距离
        ChangeState(DriveState.Intersection);
    else if (DetectDeadlockOrStaticObstacle())
        ChangeState(DriveState.DeadlockEscape);
    else if (cachedTrafficAhead || DetectMergeLane())
        ChangeState(DriveState.PlatoonMerge);
    else
        ChangeState(DriveState.DynamicCruising);

    ExecuteState();
}
    void LateUpdate()
    {
        if (autoDrive == null || carController == null) return;
        if (currentState == DriveState.FatalCrashed)
        {
            ExecuteFatalCrash();
            return;
        }

        if (!carController.autoMode && autoDrive.currentState != SimpleAutoDrive.DriveState.RemoteControlled)
            return;

        if (pendingBrakeOverride)
            carController.SetAutoBrake(pendingBrakeDecel);

        if (pendingThrottle != 0f || pendingSteer != 0f)
            carController.SetAutoControl(pendingThrottle, pendingSteer);
    }

    private void ChangeState(DriveState newState)
    {
        if (currentState == newState) return;
        currentState = newState;

        if (newState != DriveState.DeadlockEscape)
        {
            isReversingEscape = false;
            reverseEscapeTimer = 0f;
            overtakeBezierU = 0f;
        }

        if (newState != DriveState.PlatoonMerge)
            stopToGoTimer = 0f;
    }

    private void TickTimers()
    {
        float speed = Mathf.Abs(carController.GetSpeed());
        float moved = Vector3.Distance(transform.position, lastPosition);
        lastPosition = transform.position;

        if (speed < deadlockSpeedThreshold && moved < 0.2f)
            deadlockTimer += Time.deltaTime;
        else
            deadlockTimer = Mathf.Max(0f, deadlockTimer - Time.deltaTime * 0.5f);

        if (speed < 0.5f && !HasBlockingVehicleAhead(frontTrafficRange * 0.5f))
            stopToGoTimer += Time.deltaTime;
        else
            stopToGoTimer = 0f;
    }

    private void ExecuteState()
    {
        switch (currentState)
        {
            case DriveState.EmergencyAvoid:
                ExecuteEmergencyBraking();
                break;
            case DriveState.Intersection:
                ExecuteIntersectionLogic();
                break;
            case DriveState.DeadlockEscape:
                ExecuteDeadlockEscape();
                break;
            case DriveState.PlatoonMerge:
                ExecutePlatoonAndMerge();
                break;
            case DriveState.DynamicCruising:
                ExecuteDynamicCruising();
                break;
        }
    }

    private void ExecuteFatalCrash()
    {
        autoDrive.targetSpeed = 0f;
        pendingBrakeOverride = true;
        pendingBrakeDecel = maxBrakeDecel * 2f;
        pendingThrottle = 0f;
        pendingSteer = 0f;
    }

    private void ExecuteEmergencyBraking()
    {
        float speed = Mathf.Abs(carController.GetSpeed());
        float dist = GetNearestPedestrianDistance();
        if (dist < 0.1f) dist = 0.1f;

        float allowedSpeed = Mathf.Sqrt(2f * maxBrakeDecel * Mathf.Max(0f, dist - 1f));
        autoDrive.targetSpeed = dist < 4f ? 0f : Mathf.Min(cruiseSpeedBase, allowedSpeed);

        if (DetectCrashPropagation())
            autoDrive.targetSpeed = Mathf.Min(autoDrive.targetSpeed, speed * 0.35f);

        float frontDist = GetFrontObstacleDistance(frontTrafficRange);
        float decel = DetectCrashPropagation()
            ? Mathf.Lerp(maxBrakeDecel, aebBrakeDecel, 0.45f)
            : aebBrakeDecel;

        if (frontDist < safeTimeHeadway * Mathf.Max(speed, 1f))
            decel = aebBrakeDecel;

        pendingBrakeOverride = true;
        pendingBrakeDecel = decel;
        pendingThrottle = 0f;
        pendingSteer = 0f;
    }

    private void ExecuteIntersectionLogic()
    {
        float speed = Mathf.Abs(carController.GetSpeed());

        if (autoDrive.isYielding || DetectAmbulance())
        {
            autoDrive.targetSpeed = 0f;
            pendingBrakeOverride = true;
            pendingBrakeDecel = maxBrakeDecel;
            pendingThrottle = 0f;
            pendingSteer = 0f;
            return;
        }

        float distToStop = GetDistanceToStopLine();

        if (ShouldStopForSignal(out float brakeDecel))
        {
            float capSpeed = Mathf.Sqrt(2f * maxBrakeDecel * Mathf.Max(distToStop, 0.5f));
            autoDrive.targetSpeed = Mathf.Min(cruiseSpeedBase, capSpeed);

            pendingBrakeOverride = true;
            pendingBrakeDecel = brakeDecel;
            pendingThrottle = 0f;
            pendingSteer = Mathf.Clamp(
                carController.currentSteeringAngle / Mathf.Max(carController.maxSteeringAngle, 1f),
                -1f, 1f);
            return;
        }

        if (!IsOppositeBoxClear())
        {
            autoDrive.targetSpeed = Mathf.Min(autoDrive.targetSpeed, cruiseSpeedBase * 0.35f);
            return;
        }

        if (autoDrive.currentIntersectionState == IntersectionState.YellowLight)
        {
            float reactionDist = speed * yellowLightReactionTime;
            // 替换 CarControlUtility.SafeDivide 为手动安全除法
            float brakingDist = (maxBrakeDecel == 0f) ? 0f : (speed * speed) / (2f * maxBrakeDecel);
            float safePassMargin = reactionDist + brakingDist;
            if (distToStop > safePassMargin + 2f)
                autoDrive.targetSpeed = Mathf.Min(autoDrive.targetSpeed, cruiseSpeedBase * 0.85f);
        }
    }

    private void ExecuteDeadlockEscape()
    {
        autoDrive.targetSpeed = Mathf.Min(autoDrive.targetSpeed, cruiseSpeedBase * 0.25f);

        if (deadlockTimer < deadlockTimeToTrigger * 0.6f)
            return;

        if (IsLeftLaneClear())
        {
            isReversingEscape = false;
            reverseEscapeTimer = 0f;

            overtakeBezierU = Mathf.Clamp01(overtakeBezierU + Time.deltaTime * 0.35f);
            Vector3 bezierTarget = SampleOvertakeBezier(overtakeBezierU);
            Vector3 local = transform.InverseTransformPoint(bezierTarget);
            float steer = Mathf.Clamp(local.x * 1.5f, -1f, 1f); 

            // 替换 CarControlUtility.SafeDivide 为手动安全除法
            float throttle = (carController.maxSpeed == 0f) ? 0f : (autoDrive.targetSpeed / carController.maxSpeed);
            pendingThrottle = Mathf.Clamp01(throttle);
            pendingSteer = steer;
            pendingBrakeOverride = false;
            return;
        }

        isReversingEscape = true;
        reverseEscapeTimer += Time.deltaTime;
        autoDrive.targetSpeed = cruiseSpeedBase * 0.1f;
        pendingThrottle = reverseThrottle;
        pendingSteer = 0f;
        pendingBrakeOverride = false;

        if (reverseEscapeTimer >= reverseDuration)
        {
            isReversingEscape = false;
            reverseEscapeTimer = 0f;
            overtakeBezierU = 0f;
            deadlockTimer = 0f;
        }
    }

    private void ExecutePlatoonAndMerge()
    {
        float speed = Mathf.Abs(carController.GetSpeed());
        float gap = GetFrontObstacleDistance(frontTrafficRange);
        float density = EstimateTrafficDensity();
        float headwayGap = Mathf.Max(minFollowGap, speed * safeTimeHeadway);

        float accTarget = cruiseSpeedBase;
        if (gap < headwayGap * 2f)
        {
            float rel = Mathf.Max(0.1f, speed);
            accTarget = Mathf.Min(accTarget, rel * (gap / headwayGap));
        }

        accTarget *= Mathf.Lerp(1f, densitySlowFactor, density);

        if (DetectMergeLane())
            accTarget = ApplyZipperMergeCap(accTarget);

        if (speed < 0.5f && gap > headwayGap * 1.2f)
        {
            if (stopToGoTimer >= stopToGoConfirmTime)
                accTarget = cruiseSpeedBase;
            else
                accTarget = 0f;
        }

        autoDrive.targetSpeed = Mathf.Clamp(accTarget, minCruiseSpeed, cruiseSpeedBase);
    }

    private void ExecuteDynamicCruising()
    {
        float curvature = EstimatePathCurvature();
        float curvatureFactor = 1f / (1f + curvature * curvatureSpeedScale);
        float speed = cruiseSpeedBase * curvatureFactor;

        if (curvature > sharpCurveThreshold)
            speed *= blindSpotSpeedReduction;

        if (!IsBlindCornerClear())
            speed *= blindSpotSpeedReduction;

        autoDrive.targetSpeed = Mathf.Clamp(speed, minCruiseSpeed, cruiseSpeedBase);
    }

    // ===================== 探测层 =====================

    private bool CheckBoundaryFatal()
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        Vector3[] dirs = { transform.forward, transform.right, -transform.right };

        foreach (Vector3 dir in dirs)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, 1.2f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<SimpleCarController>() != null) continue;
                if (hit.normal.y > 0.6f) continue;
                if (hit.distance < fatalBoundaryMargin) return true;
            }
        }

        float cte = EstimateCrossTrackError();
        float maxAllowedDeviation = GetRoadHalfWidth() - vehicleWidthMargin() + fatalBoundaryMargin;
        
        if (Mathf.Abs(cte) > maxAllowedDeviation)
            return true;

        return false;
    }

    private bool DetectPedestrianOrCrosswalk()
    {
        return GetNearestPedestrianDistance() < pedestrianDetectRange || DetectCrosswalkAhead();
    }

    private bool DetectCrosswalkAhead()
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        if (Physics.Raycast(origin, transform.forward, out RaycastHit hit, crosswalkDetectRange, obstacleMask, QueryTriggerInteraction.Collide))
        {
            if (IsPedestrianCollider(hit.collider)) return true;
        }

        if (WorldModel.Instance == null) return false;
        RoadNode node = WorldModel.Instance.GetNearestNode(transform.position);
        if (node == null || node.Type != NodeType.Intersection) return false;

        StopLine stop = WorldModel.Instance.GetNearestStopLine(node.Id, transform.position);
        if (stop == null) return false;

        float dist = Vector3.Distance(transform.position, stop.Position);
        return dist < crosswalkDetectRange && Vector3.Dot(transform.forward, stop.Position - transform.position) > 0f;
    }

    private bool DetectApproachingIntersection()
    {
        if (WorldModel.Instance == null) return false;
        RoadNode nearest = WorldModel.Instance.GetNearestNode(transform.position);
        if (nearest == null) return false;
        if (nearest.Type != NodeType.Intersection && nearest.Type != NodeType.Merge) return false;

        float dist = Vector3.Distance(transform.position, nearest.WorldPos);
        if (dist > intersectionApproachRange) return false;

        return Vector3.Dot(transform.forward, (nearest.WorldPos - transform.position).normalized) > 0.2f;
    }

    private bool DetectAmbulance()
    {
        if (autoDrive.isYielding) return true;

        Collider[] hits = Physics.OverlapSphere(transform.position, emergencyVehicleRange, obstacleMask, QueryTriggerInteraction.Ignore);
        foreach (Collider col in hits)
        {
            SimpleCarController other = col.GetComponentInParent<SimpleCarController>();
            if (other != null && other != carController)
            {
                // 通过名字判定是否为救护车/警车
                string objName = other.gameObject.name.ToLower();
                if (objName.Contains("ambulance") || objName.Contains("police") || objName.Contains("emergency"))
                    return true;
            }
        }
        return false;
    }

    private bool DetectDeadlockOrStaticObstacle()
    {
        if (deadlockTimer < deadlockTimeToTrigger) return false;
        if (GetFrontObstacleDistance(8f) < 6f) return true;
        return autoDrive.obstacleDetected && Mathf.Abs(carController.GetSpeed()) < deadlockSpeedThreshold;
    }

    private bool DetectTrafficAhead()
    {
        return autoDrive.obstacleDetected || HasBlockingVehicleAhead(frontTrafficRange * 0.6f);
    }

    private bool DetectMergeLane()
    {
        if (WorldModel.Instance == null) return false;
        RoadNode node = WorldModel.Instance.GetNearestNode(transform.position);
        if (node == null || node.Type != NodeType.Merge) return false;
        return Vector3.Distance(transform.position, node.WorldPos) < mergeDetectRange;
    }

    private bool DetectCrashPropagation()
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        if (!Physics.SphereCast(origin, 1.5f, transform.forward, out RaycastHit first, frontTrafficRange, obstacleMask, QueryTriggerInteraction.Ignore))
            return false;

        SimpleCarController lead = first.collider.GetComponentInParent<SimpleCarController>();
        if (lead == null) return false;

        return Mathf.Abs(lead.currentSpeed) < 1.5f && first.distance < 20f;
    }

    private bool ShouldStopForSignal(out float brakeDecel)
    {
        brakeDecel = maxBrakeDecel;
        float speed = Mathf.Abs(carController.GetSpeed());
        float distToStop = GetDistanceToStopLine();

        if (autoDrive.currentIntersectionState == IntersectionState.RedLight)
            return distToStop > 0.5f;

        if (autoDrive.currentIntersectionState == IntersectionState.YellowLight)
        {
            // 替换 CarControlUtility.SafeDivide 为手动安全除法
            float safeStopDistance = speed * yellowLightReactionTime + ((maxBrakeDecel == 0f) ? 0f : (speed * speed) / (2f * maxBrakeDecel));
            return distToStop <= safeStopDistance + 1f;
        }

        return false;
    }

    private float GetDistanceToStopLine()
    {
        if (WorldModel.Instance == null) return float.MaxValue;

        int junctionId = autoDrive.currentDestinationNodeId;
        RoadNode nearest = WorldModel.Instance.GetNearestNode(transform.position);
        if (nearest != null && (nearest.Type == NodeType.Intersection || nearest.Type == NodeType.Merge))
            junctionId = nearest.Id;

        if (junctionId < 0) return float.MaxValue;

        StopLine stop = WorldModel.Instance.GetNearestStopLine(junctionId, transform.position);
        if (stop == null) return float.MaxValue;

        Vector3 diff = stop.Position - transform.position;
        diff.y = 0f;
        if (Vector3.Dot(transform.forward, diff) < 0f) return float.MaxValue;
        return diff.magnitude;
    }

    private bool IsOppositeBoxClear()
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        if (Physics.Raycast(origin, transform.forward, out RaycastHit hit, 22f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            SimpleCarController other = hit.collider.GetComponentInParent<SimpleCarController>();
            if (other != null && other != carController && hit.distance < 8f)
                return false;
        }
        return true;
    }

    private Vector3 SampleOvertakeBezier(float u)
    {
        Vector3 p0 = transform.position;
        Vector3 p1 = transform.position + transform.forward * 4f - transform.right * 3f;
        Vector3 p2 = transform.position + transform.forward * 10f - transform.right * 1.5f;
        float t = Mathf.Clamp01(u);
        float omt = 1f - t;
        return omt * omt * p0 + 2f * omt * t * p1 + t * t * p2;
    }

    private float GetNearestPedestrianDistance()
    {
        float best = float.MaxValue;
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;

        foreach (Collider col in Physics.OverlapSphere(origin, pedestrianDetectRange, obstacleMask, QueryTriggerInteraction.Collide))
        {
            if (!IsPedestrianCollider(col)) continue;
            best = Mathf.Min(best, Vector3.Distance(transform.position, col.bounds.center));
        }

        if (Physics.SphereCast(origin, 0.8f, transform.forward, out RaycastHit fwd, pedestrianDetectRange, obstacleMask, QueryTriggerInteraction.Collide)
            && IsPedestrianCollider(fwd.collider))
        {
            best = Mathf.Min(best, fwd.distance);
        }

        return best;
    }

    private static bool IsPedestrianCollider(Collider col)
    {
        if (col == null) return false;
        if (col.GetComponentInParent<SimpleCarController>() != null) return false;
        return col.gameObject.name.StartsWith("Pedestrian");
    }

    private float GetFrontObstacleDistance(float maxRange)
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        return Physics.SphereCast(origin, 1.4f, transform.forward, out RaycastHit hit, maxRange, obstacleMask, QueryTriggerInteraction.Ignore)
            ? hit.distance
            : maxRange;
    }

    private bool HasBlockingVehicleAhead(float range)
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        return Physics.SphereCast(origin, 1.5f, transform.forward, out RaycastHit hit, range, obstacleMask, QueryTriggerInteraction.Ignore)
               && hit.collider.GetComponentInParent<SimpleCarController>() != null;
    }

    private bool IsLeftLaneClear()
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        return !Physics.Raycast(origin, -transform.right, out RaycastHit hit, 4.5f, obstacleMask, QueryTriggerInteraction.Ignore)
               || hit.distance > 3.5f;
    }

    private bool IsBlindCornerClear()
    {
        Vector3 origin = transform.position + Vector3.up * boundaryRayHeight;
        Vector3 dir = Quaternion.Euler(0f, 35f, 0f) * transform.forward;
        return !Physics.Raycast(origin, dir, out RaycastHit hit, blindSpotRayRange, obstacleMask, QueryTriggerInteraction.Ignore)
               || hit.distance > blindSpotRayRange * 0.6f;
    }

    private float EstimatePathCurvature()
    {
        CatmullRomSpline spline = GetActiveSpline();
        float t = autoDrive.currentT;

        if (spline != null && spline.TotalLength > 1f)
            return CurvatureFromSamples(spline, t);

        if (WorldModel.Instance != null && autoDrive.currentLaneId >= 0
            && WorldModel.Instance.GlobalLanes.TryGetValue(autoDrive.currentLaneId, out Lane lane)
            && lane.CenterSpline != null && lane.CenterSpline.TotalLength > 1f)
        {
            return CurvatureFromSamples(lane.CenterSpline, Mathf.Clamp01(t));
        }

        return 0f;
    }

    private static float CurvatureFromSamples(CatmullRomSpline spline, float t)
    {
        float dt = 0.02f;
        float t0 = Mathf.Clamp01(t - dt);
        float t1 = Mathf.Clamp01(t + dt);
        Vector3 p0 = spline.GetPoint(t0);
        Vector3 p1 = spline.GetPoint(t1);
        Vector3 tan0 = (p1 - p0).normalized;
        Vector3 p2 = spline.GetPoint(Mathf.Clamp01(t1 + dt));
        Vector3 tan1 = (p2 - p1).normalized;
        float dTheta = Vector3.Angle(tan0, tan1) * Mathf.Deg2Rad;
        float ds = Mathf.Max(Vector3.Distance(p0, p2), 0.5f);
        return dTheta / ds;
    }

    private CatmullRomSpline GetActiveSpline()
    {
        if (splineField == null || autoDrive == null) return null;
        return splineField.GetValue(autoDrive) as CatmullRomSpline;
    }

    private float EstimateCrossTrackError()
    {
        CatmullRomSpline spline = GetActiveSpline();
        if (spline == null || spline.TotalLength < 0.5f) return 0f;

        float actualClosestT = spline.GetClosestT(transform.position, 0.5f);
        Vector3 closestPoint = spline.GetPoint(actualClosestT);

        Vector3 toCar = transform.position - closestPoint;
        toCar.y = 0f;
        return toCar.magnitude;
    }

    private float EstimateTrafficDensity()
    {
        int count = 0;
        foreach (Collider col in Physics.OverlapSphere(transform.position, 25f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (col.GetComponentInParent<SimpleCarController>() != null) count++;
        }
        return Mathf.Clamp01(count / 8f);
    }

    private float ApplyZipperMergeCap(float desiredSpeed)
    {
        int token = Mathf.Abs(GetInstanceID()) % 1000;
        if (Time.time - lastZipperYieldTime > 2.5f && (globalZipperToken % 2) != (token % 2))
        {
            lastZipperYieldTime = Time.time;
            globalZipperToken++;
            return Mathf.Min(desiredSpeed, cruiseSpeedBase * 0.4f);
        }
        return desiredSpeed;
    }

    private float GetRoadHalfWidth()
    {
        return cachedRoadHalfWidth;
    }

    // 直接使用固定车宽估算，不再依赖未定义的属性
    private float vehicleWidthMargin() => 0.5f;

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || currentState != DriveState.DeadlockEscape) return;
        Gizmos.color = Color.magenta;
        for (int i = 0; i <= 10; i++)
            Gizmos.DrawSphere(SampleOvertakeBezier(i / 10f), 0.25f);
    }
} 