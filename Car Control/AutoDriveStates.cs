using UnityEngine;

/// <summary>
/// SimpleAutoDrive 状态机方法（partial class）
/// 包含所有 Handle*State 方法和 FollowPath 路径跟随逻辑
/// </summary>
public partial class SimpleAutoDrive : MonoBehaviour
{
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

        Vector3 dirToStop = (stopTargetPosition - transform.position).normalized;
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
                    float checkT = Mathf.Min(bestLaneT + 0.05f, 1f);
                    Vector3 laneDirection = (lane.CenterSpline.GetPoint(checkT) - lane.CenterSpline.GetPoint(bestLaneT)).normalized;
                    if (Vector3.Dot(transform.forward, laneDirection) > 0f)
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
}