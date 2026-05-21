using UnityEngine;

/// <summary>
/// SimpleAutoDrive 状态机方法（partial class）
/// 包含所有 Handle*State 方法和 FollowPath 路径跟随逻辑
/// </summary>
public partial class SimpleAutoDrive : MonoBehaviour
{

    void HandleFollowingState()
    {
        if (currentSpline == null || currentSpline.TotalLength <= 0) return;

        // === 保留：障碍物检测 ===
        if (obstacleDetected && avoidCooldown <= 0f)
        {
            currentState = DriveState.Avoiding;
            return;
        }

        // === 保留：红绿灯停车 ===
        if (currentIntersectionState == IntersectionState.RedLight || currentIntersectionState == IntersectionState.YellowLight)
        {
            int stopNodeId = (nearestIntersectionNodeId >= 0) ? nearestIntersectionNodeId : currentDestinationNodeId;
            if (stopNodeId >= 0 && WorldModel.Instance != null)
            {
                StopLine relevantStopLine = WorldModel.Instance.GetNearestStopLine(stopNodeId, transform.position);
                if (relevantStopLine != null)
                {
                    stopTargetPosition = relevantStopLine.Position;
                    hasStopTarget = true;
                }
            }
            currentState = DriveState.Stopping;
            return;
        }

        // --- 冷却计时 ---
        rerouteCooldown -= Time.deltaTime;

        // --- 1. 投影速度计算 ---
        float sampleNextT   = Mathf.Clamp01(currentT + 0.05f);
        Vector3 splineTangent = (currentSpline.GetPoint(sampleNextT) -
                                 currentSpline.GetPoint(currentT)).normalized;
        float currentSpeed  = carController.GetSpeed();
        float forwardSpeed  = currentSpeed * Vector3.Dot(transform.forward, splineTangent);
        float advanceSpeed  = forwardSpeed;
        float throttle      = 0f;
        float steer         = 0f;

        // --- 2. 倒车 / 前进分支 ---
        if (targetSpeed < -0.1f)
        {
            throttle     = -0.5f;
            steer        = 0f;
            advanceSpeed = 0f;
        }
        else
        {
            if (targetSpeed > 0.1f)
                throttle = Mathf.Clamp((targetSpeed - currentSpeed) / 5f, 0f, 1f);
            if (Mathf.Abs(advanceSpeed) < 0.5f)
                advanceSpeed = Mathf.Sign(advanceSpeed) * 0.5f;

            float lookAheadDist = dynamicLookAhead
                ? Mathf.Clamp(Mathf.Abs(currentSpeed) * 0.5f, 3f, 12f)
                : 5f;
            float lookT     = Mathf.Clamp01(currentT + lookAheadDist / currentSpline.TotalLength);
            Vector3 targetPt = currentSpline.GetPoint(lookT);
            Vector3 localPt  = transform.InverseTransformPoint(targetPt);
            steer = Mathf.Clamp(localPt.x / 4f, -1f, 1f);
        }

        // --- 3. T 值推进 ---
        currentT += (advanceSpeed / currentSpline.TotalLength) * Time.deltaTime;
        currentT  = Mathf.Clamp01(currentT);

        // --- 4. 橡皮筋校准 ---
        float realT = currentSpline.GetClosestT(transform.position, currentT);
        currentT    = Mathf.Lerp(currentT, realT, Time.deltaTime * 2f);
        currentT    = Mathf.Clamp01(currentT);

        // --- 5. 底盘控制 ---
        carController.SetAutoControl(throttle, steer);

        // --- 6. 车道 ID 刷新（距离门槛） ---
        if (Vector3.Distance(lastLaneCheckPos, transform.position) > 5f || currentT > 0.8f)
        {
            lastLaneCheckPos = transform.position;
            currentLaneId    = WorldModel.Instance.FindNearestLane(transform.position);
        }

        // --- 7. T>=0.95 提前接力 ---
        if (currentT >= 0.95f && !isFetchingNextPath && rerouteCooldown <= 0f)
        {
            rerouteCooldown  = 1f;
            isFetchingNextPath = true;
            RequestNewRandomPath();
        }
    }

    void HandleAvoidingState()
    {
        if (isReversing)
        {
            reverseTimer += Time.deltaTime;

            float revSteering = escapeSteering;
            if (currentLaneId >= 0 && WorldModel.Instance != null && WorldModel.Instance.GlobalLanes.TryGetValue(currentLaneId, out Lane revLane))
            {
                float laneT = revLane.CenterSpline.GetClosestT(transform.position, 0.5f);
                float reverseLookDist = 4.0f + reverseTimer * 2.0f;
                float currentLen = revLane.CenterSpline.GetLengthAtT(laneT);
                float reverseLen = Mathf.Max(0f, currentLen - reverseLookDist);
                float reverseLookT = revLane.CenterSpline.GetTFromLength(reverseLen);
                Vector3 reverseLookPoint = revLane.CenterSpline.GetPoint(reverseLookT);
                reverseLookPoint.y = transform.position.y;
                Vector3 localTarget = transform.InverseTransformPoint(reverseLookPoint);
                float angle = Mathf.Atan2(localTarget.x, -localTarget.z) * Mathf.Rad2Deg;
                revSteering = Mathf.Clamp(angle / 45f, -1f, 1f);
            }

            carController.SetAutoControl(-0.4f, revSteering);
            if (reverseTimer >= 1.2f + reverseCount * 0.5f)
            {
                reverseCount++; isReversing = false; reverseTimer = 0f;
                avoidCooldown = 2.5f;
                carController.SetAutoControl(0f, 0f);

                if (currentSpline != null && currentSpline.TotalLength > 0)
                {
                    currentT = currentSpline.GetClosestT(transform.position, currentT);
                    currentT = Mathf.Max(0, currentT - 0.12f);
                }

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
            lastPosition = transform.position;
            carController.SetAutoControl(0f, 0f); // 重置制动
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

        // 运动学制动公式 v²/2d
        float brakingDecel = distToStop > 0.01f ? (speed * speed) / (2f * distToStop) : brakeMaxDecel;
        brakingDecel = Mathf.Clamp(brakingDecel, 0.5f, brakeMaxDecel);

        if (distToStop < 0.5f)
        {
            carController.SetAutoControl(0f, 0f);
            // 确保完全刹停
            carController.SetAutoBrake(brakeMaxDecel);
            return;
        }

        Vector3 diffToStop = stopTargetPosition - transform.position;
        // 替换 CarControlUtility.SafeNormalize 为原生安全归一化
        Vector3 dirToStop = diffToStop.sqrMagnitude > 0.0001f ? diffToStop.normalized : transform.forward;
        Vector3 localDir = transform.InverseTransformDirection(dirToStop);
        float steering = Mathf.Clamp(localDir.x * 2f, -1f, 1f);

        carController.SetAutoControl(0f, steering); // 不踩油门
        carController.SetAutoBrake(brakingDecel);   // 直接注入制动减速度
    }

    void HandleWaitingState() => carController.SetAutoControl(0f, 0f);

    void HandleRemoteControlledState()
    {
        // 保持语义感知活跃但不输出控制
        // 继续更新 LaneId 和 StopLine 距离供 ROS2 遥测
        if (WorldModel.Instance != null)
        {
            laneSearchTimer += Time.deltaTime;
            if (laneSearchTimer > 0.2f)
            {
                if (nearestIntersectionNodeId < 0)
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
        if (float.IsNaN(currentT)) currentT = 0f;

        float actualSpeed = Mathf.Abs(carController.GetSpeed());

        if (currentSpline.TotalLength > 0)
        {
            currentT = currentSpline.GetClosestT(transform.position, currentT);
            if (float.IsNaN(currentT)) currentT = 0f;
            currentT = Mathf.Clamp01(currentT);
        }

        float lookAheadMeters = 6.0f + (actualSpeed * 0.2f);
        Vector3 lookAheadPos = Vector3.zero;

        bool usedLane = false;
        if (WorldModel.Instance != null)
        {
            currentLaneId = WorldModel.Instance.FindNearestLane(transform.position);
            if (currentLaneId >= 0 && WorldModel.Instance.GlobalLanes.TryGetValue(currentLaneId, out Lane lane)
                && lane.CenterSpline != null && lane.CenterSpline.TotalLength > 1f)
            {
                float laneT = lane.CenterSpline.GetClosestT(transform.position, 0.5f);
                Vector3 lanePt = lane.CenterSpline.GetPoint(laneT);
                if ((lanePt - transform.position).sqrMagnitude < 225f)
                {
                    float laneLen = lane.CenterSpline.GetLengthAtT(laneT);
                    float lookLen = Mathf.Min(laneLen + lookAheadMeters, lane.CenterSpline.TotalLength);
                    lookAheadPos = lane.CenterSpline.GetPoint(lane.CenterSpline.GetTFromLength(lookLen));
                    usedLane = true;
                }
            }
        }

        if (!usedLane)
        {
            float curLen = currentSpline.GetLengthAtT(currentT);
            float targLen = Mathf.Min(curLen + lookAheadMeters, currentSpline.TotalLength);
            lookAheadPos = currentSpline.GetPoint(currentSpline.GetTFromLength(targLen));
        }

        float distToSpline = (currentSpline.GetPoint(currentT) - transform.position).magnitude;
        if (distToSpline > 30f && WorldModel.Instance != null)
        {
            int rescueId = WorldModel.Instance.FindNearestLane(transform.position);
            if (rescueId >= 0 && WorldModel.Instance.GlobalLanes.TryGetValue(rescueId, out Lane rl)
                && rl.CenterSpline != null && rl.CenterSpline.TotalLength > 1f)
            {
                float rt = rl.CenterSpline.GetClosestT(transform.position, 0.5f);
                Vector3 rp = rl.CenterSpline.GetPoint(rt);
                if ((rp - transform.position).magnitude < 50f)
                {
                    float rLookLen = Mathf.Min(rl.CenterSpline.GetLengthAtT(rt) + 8f, rl.CenterSpline.TotalLength);
                    lookAheadPos = rl.CenterSpline.GetPoint(rl.CenterSpline.GetTFromLength(rLookLen));
                }
            }
        }

        lookAheadPos.y = transform.position.y;
        Vector3 localTarget = transform.InverseTransformPoint(lookAheadPos);

        float angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
        if (float.IsNaN(angle)) angle = 0f;
        float steering = Mathf.Clamp(angle / 45f, -1f, 1f);

        float speedFactor = 1f;
        if (Mathf.Abs(angle) > 25f) speedFactor = 0.4f;
        if (currentIntersectionState == IntersectionState.RedLight) speedFactor = 0f;

        float throttle = carController.maxSpeed > 0.001f ? (targetSpeed * speedFactor) / carController.maxSpeed : 0f;
        carController.SetAutoControl(throttle, steering);
        carController.SetAutoBrake(0f);
    }
}